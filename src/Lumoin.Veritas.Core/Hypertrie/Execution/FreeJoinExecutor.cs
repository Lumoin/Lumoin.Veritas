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
/// and leaves over the trailing columns it is the binary hash join. One loop
/// spans both.
/// </summary>
/// <remarks>
/// <para>
/// <b>The intersection.</b> At each variable the participants are the relations
/// descending a trie node there and the at-leaf relations whose leaf carries
/// that variable; the candidate values are the intersection of the trie nodes'
/// keys and the leaf participants' distinct values, the smallest set driving
/// the scan. A chosen value descends the trie participants (the deepest descent
/// moving a relation onto its leaf) and narrows the leaf participants' rows. A
/// leaf carrying a join variable therefore joins like any other relation — the
/// binary join's build leaf and the worst-case-optimal trie meet in one rule.
/// </para>
/// <para>
/// <b>Precondition.</b> Each relation's trie levels follow the global variable
/// order, and its leaf columns are ordered after every trie level (a relation
/// reaches its leaf at its deepest trie level, before binding any leaf
/// variable); the executor verifies this and throws otherwise, since the
/// lockstep descent depends on it.
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
    /// <exception cref="ArgumentException">A relation's trie order does not follow the variable order, or a leaf column precedes a trie level.</exception>
    public static IEnumerable<SolutionBatch> Execute(IReadOnlyList<GeneralizedHashTrie> relations, IReadOnlyList<Variable> variableOrder)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(variableOrder);

        Dictionary<Variable, int> globalIndex = new(variableOrder.Count);
        for(int k = 0; k < variableOrder.Count; k++)
        {
            globalIndex[variableOrder[k]] = k;
        }

        //Per global level: the relations descending a trie node there, and the
        //at-leaf relations whose leaf binds that variable. Built once: a
        //global-order-consistent build keeps a relation at the trie level for
        //variable k whenever k is reached, and a relation reaches its leaf at
        //its deepest trie level (before any of its leaf variables).
        List<TrieParticipant>[] trieAt = new List<TrieParticipant>[variableOrder.Count];
        List<LeafParticipant>[] leafAt = new List<LeafParticipant>[variableOrder.Count];
        for(int k = 0; k < variableOrder.Count; k++)
        {
            trieAt[k] = [];
            leafAt[k] = [];
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
                int global = globalIndex[variable];
                if(global <= deepestTrieGlobal)
                {
                    throw new ArgumentException("A relation's leaf columns must follow its trie levels in the global variable order.", nameof(relations));
                }

                leafAt[global].Add(new LeafParticipant(relation, leafColumn));
            }
        }

        return Drive(relations, variableOrder, trieAt, leafAt);
    }

    /// <summary>The iterator behind <see cref="Execute"/>: an explicit-stack depth-first walk, one frame per bound variable, over shared per-relation state restored on backtrack.</summary>
    /// <param name="relations">The relations.</param>
    /// <param name="variableOrder">The global descent order.</param>
    /// <param name="trieAt">The trie participants at each global level.</param>
    /// <param name="leafAt">The leaf participants at each global level.</param>
    /// <returns>The result batches.</returns>
    private static IEnumerable<SolutionBatch> Drive(
        IReadOnlyList<GeneralizedHashTrie> relations,
        IReadOnlyList<Variable> variableOrder,
        List<TrieParticipant>[] trieAt,
        List<LeafParticipant>[] leafAt)
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

    /// <summary>Builds the frame for a variable: the participating trie and leaf relations and their intersection candidate values.</summary>
    /// <param name="level">The global variable level.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="trieAt">The trie participants at each level.</param>
    /// <param name="leafAt">The leaf participants at each level.</param>
    /// <param name="node">The per-relation current trie node ids.</param>
    /// <param name="leafId">The per-relation leaf ids, valid once at the leaf.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows, valid once at the leaf.</param>
    /// <returns>The frame.</returns>
    private static Frame MakeFrame(
        int level,
        IReadOnlyList<GeneralizedHashTrie> relations,
        List<TrieParticipant>[] trieAt,
        List<LeafParticipant>[] leafAt,
        int[] node,
        int[] leafId,
        List<int>?[] leafRows)
    {
        List<TrieParticipant> trie = trieAt[level];
        List<LeafParticipant> leaf = leafAt[level];

        int[] trieRelations = new int[trie.Count];
        bool[] trieIsDeepest = new bool[trie.Count];
        for(int i = 0; i < trie.Count; i++)
        {
            trieRelations[i] = trie[i].Relation;
            trieIsDeepest[i] = trie[i].IsDeepest;
        }

        int[] leafRelations = new int[leaf.Count];
        int[] leafColumns = new int[leaf.Count];
        for(int i = 0; i < leaf.Count; i++)
        {
            leafRelations[i] = leaf[i].Relation;
            leafColumns[i] = leaf[i].LeafColumn;
        }

        List<uint> keys = Intersect(relations, trieRelations, leafRelations, leafColumns, node, leafId, leafRows);

        return new Frame(level, trieRelations, trieIsDeepest, leafRelations, leafColumns, keys);
    }

    /// <summary>
    /// The multiway intersection at a variable: the values held by every trie
    /// participant's current node and present in every leaf participant's
    /// narrowed rows. The smallest participant set drives the scan; the others
    /// are probed — a trie node by lookup, a leaf by a precomputed value set.
    /// </summary>
    /// <param name="relations">The relations.</param>
    /// <param name="trieRelations">The trie participants' relation indices.</param>
    /// <param name="leafRelations">The leaf participants' relation indices.</param>
    /// <param name="leafColumns">The leaf participants' leaf columns, parallel to <paramref name="leafRelations"/>.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows.</param>
    /// <returns>The intersection candidate values.</returns>
    private static List<uint> Intersect(
        IReadOnlyList<GeneralizedHashTrie> relations,
        int[] trieRelations,
        int[] leafRelations,
        int[] leafColumns,
        int[] node,
        int[] leafId,
        List<int>?[] leafRows)
    {
        //Each leaf participant's distinct values, for both probing and (when it
        //is the smallest) driving.
        HashSet<uint>[] leafSets = new HashSet<uint>[leafRelations.Length];
        for(int i = 0; i < leafRelations.Length; i++)
        {
            int relation = leafRelations[i];
            HashSet<uint> values = [];
            foreach(int row in leafRows[relation]!)
            {
                values.Add(relations[relation].LeafValue(leafId[relation], row, leafColumns[i]));
            }

            leafSets[i] = values;
        }

        //The smallest participant set drives. Kind 0 is a trie node, kind 1 a
        //leaf value set.
        long smallestSize = long.MaxValue;
        int driverKind = -1;
        int driverIndex = -1;
        for(int i = 0; i < trieRelations.Length; i++)
        {
            int size = relations[trieRelations[i]].NodeAt(node[trieRelations[i]]).Count;
            if(size < smallestSize)
            {
                smallestSize = size;
                driverKind = 0;
                driverIndex = i;
            }
        }

        for(int i = 0; i < leafRelations.Length; i++)
        {
            if(leafSets[i].Count < smallestSize)
            {
                smallestSize = leafSets[i].Count;
                driverKind = 1;
                driverIndex = i;
            }
        }

        bool InAllOthers(uint value)
        {
            for(int i = 0; i < trieRelations.Length; i++)
            {
                if((driverKind != 0 || i != driverIndex) && !relations[trieRelations[i]].NodeAt(node[trieRelations[i]]).TryGetValue(value, out int _))
                {
                    return false;
                }
            }

            for(int i = 0; i < leafRelations.Length; i++)
            {
                if((driverKind != 1 || i != driverIndex) && !leafSets[i].Contains(value))
                {
                    return false;
                }
            }

            return true;
        }

        List<uint> keys = [];
        if(driverKind == 0)
        {
            int relation = trieRelations[driverIndex];
            OpenAddressedTable<int>.Enumerator scan = relations[relation].NodeAt(node[relation]).GetEnumerator();
            while(scan.MoveNext())
            {
                uint value = (uint)scan.Current.Key;
                if(InAllOthers(value))
                {
                    keys.Add(value);
                }
            }
        }
        else
        {
            foreach(uint value in leafSets[driverIndex])
            {
                if(InAllOthers(value))
                {
                    keys.Add(value);
                }
            }
        }

        return keys;
    }

    /// <summary>Saves each participant's state before the current value is applied, so the frame can undo it on backtrack.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows.</param>
    private static void Save(Frame frame, int[] node, int[] leafId, List<int>?[] leafRows)
    {
        for(int i = 0; i < frame.AllParticipants.Length; i++)
        {
            int relation = frame.AllParticipants[i];
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
        for(int i = 0; i < frame.AllParticipants.Length; i++)
        {
            int relation = frame.AllParticipants[i];
            node[relation] = frame.SavedNode[i];
            leafId[relation] = frame.SavedLeafId[i];
            leafRows[relation] = frame.SavedLeafRows[i];
        }
    }

    /// <summary>Applies the chosen value: each trie participant descends (the deepest descent moving it onto its leaf, or to a full-depth terminal), and each leaf participant narrows its rows to those carrying the value.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="value">The chosen value.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="leafRows">The per-relation narrowed leaf rows.</param>
    private static void Apply(Frame frame, uint value, IReadOnlyList<GeneralizedHashTrie> relations, int[] node, int[] leafId, List<int>?[] leafRows)
    {
        for(int i = 0; i < frame.TrieRelations.Length; i++)
        {
            int relation = frame.TrieRelations[i];
            relations[relation].NodeAt(node[relation]).TryGetValue(value, out int child);

            if(frame.TrieIsDeepest[i])
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

        for(int i = 0; i < frame.LeafRelations.Length; i++)
        {
            int relation = frame.LeafRelations[i];
            int leafColumn = frame.LeafColumns[i];
            List<int> current = leafRows[relation]!;
            List<int> narrowed = [];
            foreach(int row in current)
            {
                if(relations[relation].LeafValue(leafId[relation], row, leafColumn) == value)
                {
                    narrowed.Add(row);
                }
            }

            leafRows[relation] = narrowed;
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

    /// <summary>A leaf participant at a level: an at-leaf relation and the leaf column it binds there.</summary>
    /// <param name="Relation">The relation index.</param>
    /// <param name="LeafColumn">The leaf column index.</param>
    private readonly record struct LeafParticipant(int Relation, int LeafColumn);

    /// <summary>
    /// One bound-variable frame of the depth-first walk: its candidate values,
    /// the cursor into them, the trie and leaf relations participating at this
    /// variable, and their state saved before the current value's application so
    /// it can be undone on backtrack.
    /// </summary>
    private sealed class Frame
    {
        /// <summary>The global variable level this frame binds.</summary>
        public int Level { get; }

        /// <summary>The relations descending a trie node at this variable.</summary>
        public int[] TrieRelations { get; }

        /// <summary>Whether each trie participant is at its deepest level, parallel to <see cref="TrieRelations"/>.</summary>
        public bool[] TrieIsDeepest { get; }

        /// <summary>The at-leaf relations binding this variable through their leaf.</summary>
        public int[] LeafRelations { get; }

        /// <summary>The leaf column each leaf participant binds, parallel to <see cref="LeafRelations"/>.</summary>
        public int[] LeafColumns { get; }

        /// <summary>Every participating relation — trie then leaf — over which state is saved and restored.</summary>
        public int[] AllParticipants { get; }

        /// <summary>The candidate values for this variable: the trie-and-leaf intersection.</summary>
        public List<uint> Keys { get; }

        /// <summary>The next candidate index to try.</summary>
        public int Index { get; set; }

        /// <summary>Whether the current value has been applied and awaits undo on the next visit.</summary>
        public bool Descended { get; set; }

        /// <summary>Each participant's node id saved before the current application, parallel to <see cref="AllParticipants"/>.</summary>
        public int[] SavedNode { get; }

        /// <summary>Each participant's leaf id saved before the current application.</summary>
        public int[] SavedLeafId { get; }

        /// <summary>Each participant's narrowed leaf rows saved before the current application.</summary>
        public List<int>?[] SavedLeafRows { get; }

        /// <summary>Constructs a frame over its trie and leaf participants and candidate values.</summary>
        /// <param name="level">The global variable level.</param>
        /// <param name="trieRelations">The trie participants.</param>
        /// <param name="trieIsDeepest">Whether each trie participant is at its deepest level.</param>
        /// <param name="leafRelations">The leaf participants.</param>
        /// <param name="leafColumns">The leaf column each leaf participant binds.</param>
        /// <param name="keys">The candidate values.</param>
        public Frame(int level, int[] trieRelations, bool[] trieIsDeepest, int[] leafRelations, int[] leafColumns, List<uint> keys)
        {
            Level = level;
            TrieRelations = trieRelations;
            TrieIsDeepest = trieIsDeepest;
            LeafRelations = leafRelations;
            LeafColumns = leafColumns;
            AllParticipants = [.. trieRelations, .. leafRelations];
            Keys = keys;
            Index = 0;
            Descended = false;
            SavedNode = new int[AllParticipants.Length];
            SavedLeafId = new int[AllParticipants.Length];
            SavedLeafRows = new List<int>?[AllParticipants.Length];
        }
    }
}
