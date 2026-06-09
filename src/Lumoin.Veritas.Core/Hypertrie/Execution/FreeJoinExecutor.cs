using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The Free Join generic join over <see cref="GeneralizedHashTrie"/> relations:
/// a variable-at-a-time multiway intersection driven by one global variable
/// order. A relation contributes a hashed trie node at each of its trie levels
/// and, once its trie is fully descended, a leaf tuple vector for its remaining
/// columns. With every relation full-depth this is the worst-case-optimal join
/// (the hash-trie analogue of leapfrog); with shallow tries over the join keys
/// and leaves over the private output columns it is the binary hash join. One
/// loop spans both.
/// </summary>
/// <remarks>
/// <para>
/// <b>Leaf columns.</b> A relation below full depth carries its trailing
/// variables as a leaf tuple vector, iterated as a cross-product factor when
/// those variables are reached. This increment requires every leaf column to be
/// <em>private</em> — a variable no other relation binds; a shared (join)
/// variable must therefore be a trie level in every relation that has it, so a
/// global level is either a pure trie intersection or a pure private-leaf
/// iteration, never a mix. The leaf-intersection case — a join variable living
/// in one relation's leaf — is a later increment and is rejected here.
/// </para>
/// <para>
/// <b>Precondition.</b> Each relation's trie levels follow the global variable
/// order, and its leaf columns are private and ordered after every trie level;
/// the executor verifies this and throws otherwise, since the lockstep descent
/// depends on it.
/// </para>
/// </remarks>
public static class FreeJoinExecutor
{
    /// <summary>
    /// Runs the generic join over the relations on the variable order, yielding
    /// the result as <see cref="SolutionBatch"/>es over that order.
    /// </summary>
    /// <param name="relations">The relations, each a <see cref="GeneralizedHashTrie"/> at some depth.</param>
    /// <param name="variableOrder">The global descent order; covers every relation's variables.</param>
    /// <returns>The result batches over <paramref name="variableOrder"/>; full except the last.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A relation's trie order does not follow the variable order, or a leaf column is a shared variable or precedes a trie level.</exception>
    public static IEnumerable<SolutionBatch> Execute(IReadOnlyList<GeneralizedHashTrie> relations, IReadOnlyList<Variable> variableOrder)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(variableOrder);

        Dictionary<Variable, int> globalIndex = new(variableOrder.Count);
        for(int k = 0; k < variableOrder.Count; k++)
        {
            globalIndex[variableOrder[k]] = k;
        }

        //How many relations bind each variable: a leaf column must be bound by
        //exactly one (private), so the join variables stay on the trie levels.
        Dictionary<Variable, int> bindingCount = [];
        foreach(GeneralizedHashTrie trie in relations)
        {
            foreach(Variable variable in trie.Schema)
            {
                bindingCount[variable] = bindingCount.TryGetValue(variable, out int existing) ? existing + 1 : 1;
            }
        }

        //Per global level: the relations descending a trie node there, and (for
        //a private leaf level) the single owning relation with the leaf column
        //it binds. Built once: a global-order-consistent build keeps a relation
        //at the trie level for variable k whenever k is reached, and a private
        //leaf level has no trie participant, so the two never share a level.
        List<TrieParticipant>[] trieAt = new List<TrieParticipant>[variableOrder.Count];
        LeafParticipant[] leafAt = new LeafParticipant[variableOrder.Count];
        for(int k = 0; k < variableOrder.Count; k++)
        {
            trieAt[k] = [];
            leafAt[k] = LeafParticipant.None;
        }

        for(int relation = 0; relation < relations.Count; relation++)
        {
            GeneralizedHashTrie trie = relations[relation];

            int previousGlobal = -1;
            int deepestTrieGlobal = -1;
            for(int level = 0; level < trie.TrieColumns.Length; level++)
            {
                Variable variable = trie.Schema[trie.TrieColumns[level]];
                int global = globalIndex[variable];
                if(global <= previousGlobal)
                {
                    throw new ArgumentException("A relation's trie levels must follow the global variable order.", nameof(relations));
                }

                previousGlobal = global;
                deepestTrieGlobal = global;
                trieAt[global].Add(new TrieParticipant(relation, level == trie.TrieColumns.Length - 1));
            }

            for(int leafColumn = 0; leafColumn < trie.LeafColumns.Length; leafColumn++)
            {
                Variable variable = trie.Schema[trie.LeafColumns[leafColumn]];
                if(bindingCount[variable] != 1)
                {
                    throw new ArgumentException("A leaf column must be a private variable; a shared join variable must be a trie level (the leaf-intersection case is a later increment).", nameof(relations));
                }

                int global = globalIndex[variable];
                if(global <= deepestTrieGlobal)
                {
                    throw new ArgumentException("A relation's leaf columns must follow its trie levels in the global variable order.", nameof(relations));
                }

                leafAt[global] = new LeafParticipant(relation, leafColumn);
            }
        }

        return Drive(relations, variableOrder, trieAt, leafAt);
    }

    /// <summary>The iterator behind <see cref="Execute"/>: an explicit-stack depth-first walk, one frame per bound variable, over shared per-relation state restored on backtrack.</summary>
    /// <param name="relations">The relations.</param>
    /// <param name="variableOrder">The global descent order.</param>
    /// <param name="trieAt">The trie participants at each global level.</param>
    /// <param name="leafAt">The private-leaf owner at each global level, if any.</param>
    /// <returns>The result batches.</returns>
    private static IEnumerable<SolutionBatch> Drive(
        IReadOnlyList<GeneralizedHashTrie> relations,
        IReadOnlyList<Variable> variableOrder,
        List<TrieParticipant>[] trieAt,
        LeafParticipant[] leafAt)
    {
        int lastLevel = variableOrder.Count - 1;

        //Per-relation state: a current trie node while descending, then -1 once
        //the trie is fully descended, when the relation is at the leaf id with a
        //narrowed set of leaf rows (null for a full-depth terminal).
        int[] node = new int[relations.Count];
        int[] leafId = new int[relations.Count];
        List<int>?[] leafRows = new List<int>?[relations.Count];
        uint[] bindings = new uint[variableOrder.Count];

        SolutionBatch output = new(variableOrder);
        int rows = 0;

        List<Frame> stack = [MakeFrame(0, relations, trieAt, leafAt, node, leafId, leafRows)];

        while(stack.Count > 0)
        {
            Frame frame = stack[^1];

            //Returning into a frame whose current value was applied: undo it
            //before advancing to the next value.
            if(frame.Descended)
            {
                Restore(frame, node, leafId, leafRows);
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

            Save(frame, node, leafId, leafRows);
            Apply(frame, value, relations, node, leafId, leafRows);
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
                stack.Add(MakeFrame(frame.Level + 1, relations, trieAt, leafAt, node, leafId, leafRows));
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }

    /// <summary>Builds the frame for a variable: a private-leaf level iterates the owner's distinct leaf values; a trie level is the multiway intersection of its participants' current nodes.</summary>
    /// <param name="level">The global variable level.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="trieAt">The trie participants at each level.</param>
    /// <param name="leafAt">The private-leaf owner at each level, if any.</param>
    /// <param name="node">The per-relation current trie node ids.</param>
    /// <param name="leafId">The per-relation leaf ids, valid once at the leaf.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows, valid once at the leaf.</param>
    /// <returns>The frame.</returns>
    private static Frame MakeFrame(
        int level,
        IReadOnlyList<GeneralizedHashTrie> relations,
        List<TrieParticipant>[] trieAt,
        LeafParticipant[] leafAt,
        int[] node,
        int[] leafId,
        List<int>?[] leafRows)
    {
        if(leafAt[level].IsSet)
        {
            LeafParticipant owner = leafAt[level];
            List<uint> keys = DistinctLeafValues(relations[owner.Relation], leafId[owner.Relation], owner.LeafColumn, leafRows[owner.Relation]!);

            return new Frame(level, isLeaf: true, [owner.Relation], [false], owner.LeafColumn, keys);
        }

        List<TrieParticipant> participants = trieAt[level];
        int[] relationIndices = new int[participants.Count];
        bool[] isDeepest = new bool[participants.Count];
        for(int i = 0; i < participants.Count; i++)
        {
            relationIndices[i] = participants[i].Relation;
            isDeepest[i] = participants[i].IsDeepest;
        }

        return new Frame(level, isLeaf: false, relationIndices, isDeepest, leafColumn: -1, IntersectTrieNodes(relations, relationIndices, node));
    }

    /// <summary>The multiway intersection of the trie participants' current nodes: the keys of the smallest node every other participant's node also holds.</summary>
    /// <param name="relations">The relations.</param>
    /// <param name="participants">The trie participants' relation indices.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <returns>The intersection candidate values.</returns>
    private static List<uint> IntersectTrieNodes(IReadOnlyList<GeneralizedHashTrie> relations, int[] participants, int[] node)
    {
        int smallest = participants[0];
        for(int i = 1; i < participants.Length; i++)
        {
            int relation = participants[i];
            if(relations[relation].NodeAt(node[relation]).Count < relations[smallest].NodeAt(node[smallest]).Count)
            {
                smallest = relation;
            }
        }

        List<uint> keys = [];
        OpenAddressedTable<int>.Enumerator scan = relations[smallest].NodeAt(node[smallest]).GetEnumerator();
        while(scan.MoveNext())
        {
            uint key = (uint)scan.Current.Key;

            bool inAll = true;
            for(int i = 0; i < participants.Length; i++)
            {
                int relation = participants[i];
                if(relation != smallest && !relations[relation].NodeAt(node[relation]).TryGetValue(key, out int _))
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

        return keys;
    }

    /// <summary>The distinct values of one leaf column across a relation's narrowed leaf rows — the candidate bindings a private-leaf level offers.</summary>
    /// <param name="relation">The owning relation.</param>
    /// <param name="leafId">The relation's current leaf id.</param>
    /// <param name="leafColumn">The leaf column index.</param>
    /// <param name="rows">The relation's narrowed leaf rows.</param>
    /// <returns>The distinct values, in first-seen order.</returns>
    private static List<uint> DistinctLeafValues(GeneralizedHashTrie relation, int leafId, int leafColumn, List<int> rows)
    {
        HashSet<uint> seen = [];
        List<uint> values = [];
        foreach(int row in rows)
        {
            uint value = relation.LeafValue(leafId, row, leafColumn);
            if(seen.Add(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>Saves each participant's state before the current value is applied, so the frame can undo it on backtrack.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows.</param>
    private static void Save(Frame frame, int[] node, int[] leafId, List<int>?[] leafRows)
    {
        for(int i = 0; i < frame.Participants.Length; i++)
        {
            int relation = frame.Participants[i];
            frame.SavedNode[i] = node[relation];
            frame.SavedLeafId[i] = leafId[relation];
            frame.SavedLeafRows[i] = leafRows[relation];
        }
    }

    /// <summary>Restores each participant's saved state, undoing the current value's application.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows.</param>
    private static void Restore(Frame frame, int[] node, int[] leafId, List<int>?[] leafRows)
    {
        for(int i = 0; i < frame.Participants.Length; i++)
        {
            int relation = frame.Participants[i];
            node[relation] = frame.SavedNode[i];
            leafId[relation] = frame.SavedLeafId[i];
            leafRows[relation] = frame.SavedLeafRows[i];
        }
    }

    /// <summary>Applies the chosen value: a leaf level narrows the owner's rows to those carrying it; a trie level descends every participant, the deepest descent moving a relation onto its leaf (a full-depth relation becomes terminal).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="value">The chosen value.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows.</param>
    private static void Apply(Frame frame, uint value, IReadOnlyList<GeneralizedHashTrie> relations, int[] node, int[] leafId, List<int>?[] leafRows)
    {
        if(frame.IsLeaf)
        {
            int relation = frame.Participants[0];
            List<int> current = leafRows[relation]!;
            List<int> narrowed = [];
            foreach(int row in current)
            {
                if(relations[relation].LeafValue(leafId[relation], row, frame.LeafColumn) == value)
                {
                    narrowed.Add(row);
                }
            }

            leafRows[relation] = narrowed;

            return;
        }

        for(int i = 0; i < frame.Participants.Length; i++)
        {
            int relation = frame.Participants[i];
            relations[relation].NodeAt(node[relation]).TryGetValue(value, out int child);

            if(frame.IsDeepest[i])
            {
                //The deepest trie node maps the value to a leaf id. A relation
                //with leaf columns opens its full tuple set for the narrowing to
                //come; a full-depth relation has none and is now terminal.
                node[relation] = -1;
                leafId[relation] = child;
                leafRows[relation] = relations[relation].LeafColumns.Length > 0 ? AllRows(relations[relation], child) : null;
            }
            else
            {
                node[relation] = child;
            }
        }
    }

    /// <summary>Every row of a freshly reached leaf — the unnarrowed tuple set a relation opens with on arriving at its leaf.</summary>
    /// <param name="relation">The relation.</param>
    /// <param name="leafId">The reached leaf id.</param>
    /// <returns>The leaf's row indices.</returns>
    private static List<int> AllRows(GeneralizedHashTrie relation, int leafId)
    {
        int count = relation.LeafTupleCount(leafId);
        List<int> rows = new(count);
        for(int row = 0; row < count; row++)
        {
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>A trie participant at a level: the relation, and whether this is its deepest trie level (the descent that moves it onto its leaf).</summary>
    /// <param name="Relation">The relation index.</param>
    /// <param name="IsDeepest">Whether this is the relation's deepest trie level.</param>
    private readonly record struct TrieParticipant(int Relation, bool IsDeepest);

    /// <summary>The private-leaf owner at a level: the relation and the leaf column it binds there. <see cref="None"/> marks a level with no leaf owner.</summary>
    /// <param name="Relation">The owning relation index, or −1 for none.</param>
    /// <param name="LeafColumn">The leaf column index, or −1 for none.</param>
    private readonly record struct LeafParticipant(int Relation, int LeafColumn)
    {
        /// <summary>The absent owner — a level that is not a private-leaf level.</summary>
        public static LeafParticipant None { get; } = new(-1, -1);

        /// <summary>Whether this level has a private-leaf owner.</summary>
        public bool IsSet => Relation >= 0;
    }

    /// <summary>
    /// One bound-variable frame of the depth-first walk: its candidate values,
    /// the cursor into them, the relations participating at this variable, and
    /// their state saved before the current value's application so it can be
    /// undone on backtrack.
    /// </summary>
    private sealed class Frame
    {
        /// <summary>The global variable level this frame binds.</summary>
        public int Level { get; }

        /// <summary>Whether this is a private-leaf iteration (one owner) rather than a trie intersection.</summary>
        public bool IsLeaf { get; }

        /// <summary>The relations participating at this variable.</summary>
        public int[] Participants { get; }

        /// <summary>Whether each trie participant is at its deepest level, parallel to <see cref="Participants"/>; all <see langword="false"/> for a leaf frame.</summary>
        public bool[] IsDeepest { get; }

        /// <summary>The leaf column iterated by a leaf frame, or −1 for a trie frame.</summary>
        public int LeafColumn { get; }

        /// <summary>The candidate values for this variable — the trie intersection or the owner's distinct leaf values.</summary>
        public List<uint> Keys { get; }

        /// <summary>The next candidate index to try.</summary>
        public int Index { get; set; }

        /// <summary>Whether the current value has been applied and awaits undo on the next visit.</summary>
        public bool Descended { get; set; }

        /// <summary>Each participant's node id saved before the current application, parallel to <see cref="Participants"/>.</summary>
        public int[] SavedNode { get; }

        /// <summary>Each participant's leaf id saved before the current application.</summary>
        public int[] SavedLeafId { get; }

        /// <summary>Each participant's narrowed leaf rows saved before the current application.</summary>
        public List<int>?[] SavedLeafRows { get; }

        /// <summary>Constructs a frame over its participants and candidate values.</summary>
        /// <param name="level">The global variable level.</param>
        /// <param name="isLeaf">Whether this is a private-leaf iteration.</param>
        /// <param name="participants">The participating relations.</param>
        /// <param name="isDeepest">Whether each trie participant is at its deepest level.</param>
        /// <param name="leafColumn">The leaf column for a leaf frame, or −1.</param>
        /// <param name="keys">The candidate values.</param>
        public Frame(int level, bool isLeaf, int[] participants, bool[] isDeepest, int leafColumn, List<uint> keys)
        {
            Level = level;
            IsLeaf = isLeaf;
            Participants = participants;
            IsDeepest = isDeepest;
            LeafColumn = leafColumn;
            Keys = keys;
            SavedNode = new int[participants.Length];
            SavedLeafId = new int[participants.Length];
            SavedLeafRows = new List<int>?[participants.Length];
        }
    }
}
