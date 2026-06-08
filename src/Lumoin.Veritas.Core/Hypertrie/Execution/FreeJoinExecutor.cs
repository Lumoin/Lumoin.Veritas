using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The Free Join generic join over <see cref="GeneralizedHashTrie"/> relations:
/// a variable-at-a-time multiway hash intersection driven by one global
/// variable order. At each variable the relations binding it intersect their
/// current trie nodes — iterate the keys of the smallest node and probe the
/// others — then descend together on each surviving value; when the order is
/// exhausted a full binding is emitted. This is the unified join: with every
/// relation a full-depth trie it is a worst-case-optimal join (the hash-trie
/// analogue of leapfrog); the same loop with shallow tries and leaf vectors is
/// the binary hash join.
/// </summary>
/// <remarks>
/// <para>
/// <b>This increment handles the full-depth case</b> (every relation's columns
/// are trie levels, no leaf vectors) — the WCOJ form, differentially checkable
/// against leapfrog and, on acyclic shapes, the batched pipeline. Leaf vectors
/// (the binary-join interpolation) are a later increment.
/// </para>
/// <para>
/// <b>Precondition.</b> Each relation's trie levels must follow the global
/// variable order (the build orders them so); the executor verifies this and
/// throws otherwise, since the lockstep descent depends on it.
/// </para>
/// </remarks>
public static class FreeJoinExecutor
{
    /// <summary>
    /// Runs the generic join over the relations on the variable order, yielding
    /// the result as <see cref="SolutionBatch"/>es over that order.
    /// </summary>
    /// <param name="relations">The relations, each a full-depth <see cref="GeneralizedHashTrie"/>.</param>
    /// <param name="variableOrder">The global descent order; covers every relation's variables.</param>
    /// <returns>The result batches over <paramref name="variableOrder"/>; full except the last.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A relation carries leaf columns (not yet supported) or its trie order does not follow the variable order.</exception>
    public static IEnumerable<SolutionBatch> Execute(IReadOnlyList<GeneralizedHashTrie> relations, IReadOnlyList<Variable> variableOrder)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(variableOrder);

        Dictionary<Variable, int> globalIndex = new(variableOrder.Count);
        for(int k = 0; k < variableOrder.Count; k++)
        {
            globalIndex[variableOrder[k]] = k;
        }

        //The relations binding each global variable, in relation order. Static
        //for the whole run: a global-order-consistent build means a relation
        //binding variable k is always at the trie level for k when k is reached.
        List<int>[] active = new List<int>[variableOrder.Count];
        for(int k = 0; k < active.Length; k++)
        {
            active[k] = [];
        }

        for(int relation = 0; relation < relations.Count; relation++)
        {
            GeneralizedHashTrie trie = relations[relation];
            if(trie.LeafColumns.Length != 0)
            {
                throw new ArgumentException("The Free Join executor handles full-depth tries only in this increment; a relation carries leaf columns.", nameof(relations));
            }

            int previousGlobal = -1;
            for(int level = 0; level < trie.TrieColumns.Length; level++)
            {
                Variable variable = trie.Schema[trie.TrieColumns[level]];
                int global = globalIndex[variable];
                if(global <= previousGlobal)
                {
                    throw new ArgumentException("A relation's trie levels must follow the global variable order.", nameof(relations));
                }

                previousGlobal = global;
                active[global].Add(relation);
            }
        }

        return Drive(relations, variableOrder, active);
    }

    /// <summary>The iterator behind <see cref="Execute"/>: an explicit-stack depth-first walk of the join, one frame per bound variable.</summary>
    /// <param name="relations">The relations.</param>
    /// <param name="variableOrder">The global descent order.</param>
    /// <param name="active">The relations binding each global variable.</param>
    /// <returns>The result batches.</returns>
    private static IEnumerable<SolutionBatch> Drive(IReadOnlyList<GeneralizedHashTrie> relations, IReadOnlyList<Variable> variableOrder, List<int>[] active)
    {
        int lastLevel = variableOrder.Count - 1;

        //Per-relation current trie node; descends in lockstep with the order.
        int[] cursors = new int[relations.Count];
        uint[] bindings = new uint[variableOrder.Count];

        SolutionBatch output = new(variableOrder);
        int rows = 0;

        List<Frame> stack = [MakeFrame(0, relations, active[0], cursors)];

        while(stack.Count > 0)
        {
            Frame frame = stack[^1];

            //Returning into a frame whose current value was descended: undo the
            //descent before advancing to the next value.
            if(frame.Descended)
            {
                for(int j = 0; j < frame.Active.Length; j++)
                {
                    cursors[frame.Active[j]] = frame.Saved[j];
                }

                frame.Descended = false;
            }

            if(frame.Index >= frame.Keys.Count)
            {
                stack.RemoveAt(stack.Count - 1);

                continue;
            }

            uint value = frame.Keys[frame.Index];
            frame.Index++;
            bindings[frame.Level] = value;

            //Descend every active relation onto the chosen value.
            for(int j = 0; j < frame.Active.Length; j++)
            {
                int relation = frame.Active[j];
                frame.Saved[j] = cursors[relation];
                relations[relation].NodeAt(cursors[relation]).TryGetValue(value, out int child);
                cursors[relation] = child;
            }

            frame.Descended = true;

            if(frame.Level == lastLevel)
            {
                for(int column = 0; column < variableOrder.Count; column++)
                {
                    output.ColumnSpan(column)[rows] = bindings[column];
                }

                rows++;

                if(rows == SolutionBatch.BatchLength)
                {
                    output.SetCount(rows);

                    yield return output;

                    output = new SolutionBatch(variableOrder);
                    rows = 0;
                }
            }
            else
            {
                stack.Add(MakeFrame(frame.Level + 1, relations, active[frame.Level + 1], cursors));
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }

    /// <summary>Builds a frame for a variable: the multiway intersection of the active relations' current nodes — the keys of the smallest node that every other active node also holds.</summary>
    /// <param name="level">The global variable level.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="activeAtLevel">The relations binding this level.</param>
    /// <param name="cursors">The per-relation current node ids.</param>
    /// <returns>The frame.</returns>
    private static Frame MakeFrame(int level, IReadOnlyList<GeneralizedHashTrie> relations, List<int> activeAtLevel, int[] cursors)
    {
        int[] activeRelations = [.. activeAtLevel];

        //The smallest current node drives the intersection scan.
        int smallest = activeRelations[0];
        for(int j = 1; j < activeRelations.Length; j++)
        {
            int relation = activeRelations[j];
            if(relations[relation].NodeAt(cursors[relation]).Count < relations[smallest].NodeAt(cursors[smallest]).Count)
            {
                smallest = relation;
            }
        }

        List<uint> keys = [];
        OpenAddressedTable<int>.Enumerator scan = relations[smallest].NodeAt(cursors[smallest]).GetEnumerator();
        while(scan.MoveNext())
        {
            uint key = (uint)scan.Current.Key;

            bool inAll = true;
            for(int j = 0; j < activeRelations.Length; j++)
            {
                int relation = activeRelations[j];
                if(relation != smallest && !relations[relation].NodeAt(cursors[relation]).TryGetValue(key, out int _))
                {
                    inAll = false;

                    break;
                }
            }

            if(inAll)
            {
                keys.Add(key);
            }
        }

        return new Frame(level, activeRelations, keys);
    }

    /// <summary>
    /// One bound-variable frame of the depth-first walk: the variable's
    /// intersection candidates, the cursor into them, the relations descending
    /// on this variable, and the node ids saved before the current descent so
    /// it can be undone on backtrack.
    /// </summary>
    private sealed class Frame
    {
        /// <summary>The global variable level this frame binds.</summary>
        public int Level { get; }

        /// <summary>The relations binding this variable, descending together on each value.</summary>
        public int[] Active { get; }

        /// <summary>The intersection candidate values for this variable.</summary>
        public List<uint> Keys { get; }

        /// <summary>The next candidate index to try.</summary>
        public int Index { get; set; }

        /// <summary>The active relations' node ids saved before the current value's descent, parallel to <see cref="Active"/>.</summary>
        public int[] Saved { get; }

        /// <summary>Whether the current value has been descended and awaits undo on the next visit.</summary>
        public bool Descended { get; set; }

        /// <summary>Constructs a frame over a level's active relations and intersection keys.</summary>
        /// <param name="level">The global variable level.</param>
        /// <param name="active">The relations binding the level.</param>
        /// <param name="keys">The intersection candidate values.</param>
        public Frame(int level, int[] active, List<uint> keys)
        {
            Level = level;
            Active = active;
            Keys = keys;
            Saved = new int[active.Length];
        }
    }
}
