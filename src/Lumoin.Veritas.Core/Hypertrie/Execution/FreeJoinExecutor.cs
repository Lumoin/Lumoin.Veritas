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

    /// <summary>
    /// Runs the generic join as a single-level <see cref="FactorizedBatch"/> for
    /// a star — every relation a full-depth two-column trie sharing the order's
    /// leading variable as its key and binding one further, the branch variables
    /// pairwise distinct — or returns <see langword="null"/> when the relations
    /// are not that shape. After the shared key each relation's matches vary
    /// independently of the others, so the join emits one group per key value
    /// carrying each relation's matches as a branch, never their flat product;
    /// the differential oracle is the flattened batch against <see cref="Execute"/>.
    /// </summary>
    /// <param name="relations">The relations; for the star each is a full-depth two-column trie over the key then a branch variable.</param>
    /// <param name="variableOrder">The global descent order; its first variable is the shared key, the rest the branch variables.</param>
    /// <param name="arena">The arena the groups' branch values are allocated from; the result is valid until it is disposed.</param>
    /// <returns>The factorised result over <paramref name="variableOrder"/>, or <see langword="null"/> when the relations are not a single-key star.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static FactorizedBatch? ExecuteFactorized(IReadOnlyList<GeneralizedHashTrie> relations, IReadOnlyList<Variable> variableOrder, FactorizedArena arena)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(variableOrder);
        ArgumentNullException.ThrowIfNull(arena);

        if(relations.Count < 2 || variableOrder.Count < 2)
        {
            return null;
        }

        Dictionary<Variable, int> orderIndex = new(variableOrder.Count);
        for(int k = 0; k < variableOrder.Count; k++)
        {
            orderIndex[variableOrder[k]] = k;
        }

        Variable key = variableOrder[0];

        //Each relation must be a full-depth two-column trie over the key then a
        //branch: two trie levels, no leaf vectors, the key at level 0, and a
        //branch variable at level 1 distinct across relations and never the key.
        //Any other shape is not the single-key star this step factorises.
        int[] branchColumn = new int[relations.Count];
        HashSet<Variable> branchVariables = [];
        for(int relation = 0; relation < relations.Count; relation++)
        {
            GeneralizedHashTrie trie = relations[relation];
            if(trie.TrieColumns.Length != 2 || trie.LeafColumns.Length != 0)
            {
                return null;
            }

            Variable level0 = trie.Schema[trie.TrieColumns[0]];
            Variable level1 = trie.Schema[trie.TrieColumns[1]];
            if(level0 != key || level1 == key || !branchVariables.Add(level1))
            {
                return null;
            }

            branchColumn[relation] = orderIndex[level1];
        }

        //The order's non-key variables are exactly the branch variables: the
        //star binds the key then one variable per relation, nothing more.
        if(branchVariables.Count != variableOrder.Count - 1)
        {
            return null;
        }

        //The smallest root drives the key intersection; a key absent from any
        //relation's root binds no star row and drops the group.
        int driver = 0;
        for(int relation = 1; relation < relations.Count; relation++)
        {
            if(relations[relation].NodeAt(0).Count < relations[driver].NodeAt(0).Count)
            {
                driver = relation;
            }
        }

        List<FactorizedGroup> groups = [];

        //Scratch reused across keys: the child node id per relation under the
        //current key, refilled before each use.
        int[] childNode = new int[relations.Count];

        //Every star branch is a single residual variable, so each branch's
        //stride is one; this array is constant across groups.
        int[] branchStride = new int[relations.Count];
        Array.Fill(branchStride, 1);

        OpenAddressedTable<int>.Enumerator keyScan = relations[driver].NodeAt(0).GetEnumerator();
        while(keyScan.MoveNext())
        {
            uint keyValue = (uint)keyScan.Current.Key;

            bool present = true;
            for(int relation = 0; relation < relations.Count; relation++)
            {
                if(!relations[relation].NodeAt(0).TryGetValue(keyValue, out childNode[relation]))
                {
                    present = false;

                    break;
                }
            }

            if(!present)
            {
                continue;
            }

            //Each relation's branch is its branch-variable values under this key
            //— the keys of its level-1 node. They vary independently, so they
            //become this group's branches in relation order.
            uint[][] branchValues = new uint[relations.Count][];
            int[] branchRowCounts = new int[relations.Count];
            for(int relation = 0; relation < relations.Count; relation++)
            {
                OpenAddressedTable<int> node = relations[relation].NodeAt(childNode[relation]);
                uint[] values = new uint[node.Count];
                int next = 0;
                OpenAddressedTable<int>.Enumerator branchScan = node.GetEnumerator();
                while(branchScan.MoveNext())
                {
                    values[next] = (uint)branchScan.Current.Key;
                    next++;
                }

                branchValues[relation] = values;
                branchRowCounts[relation] = values.Length;
            }

            groups.Add(new FactorizedGroup([keyValue], FactorizedBranches.Of(branchValues, branchRowCounts, branchStride, arena)));
        }

        int[][] branchColumns = new int[relations.Count][];
        for(int relation = 0; relation < relations.Count; relation++)
        {
            branchColumns[relation] = [branchColumn[relation]];
        }

        return new FactorizedBatch(variableOrder, [0], branchColumns, groups);
    }

    /// <summary>
    /// The iterator behind <see cref="Execute"/>: an explicit-stack depth-first
    /// walk over shared per-relation state restored on backtrack. The stack is
    /// always the contiguous level prefix <c>0..top</c> — one live frame per
    /// global level — so each level owns a single reusable <see cref="LevelFrame"/>
    /// allocated here once; its candidate keys, saved state, leaf value sets,
    /// and row buffers are flat arrays with counts, grown on demand and reused
    /// across every visit. Steady state, the walk allocates nothing per bound
    /// value beyond the output batches.
    /// </summary>
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
        int levelCount = variableOrder.Count;
        int lastLevel = levelCount - 1;

        //Per-relation state: a current trie node while descending, then -1 once
        //the trie is fully descended, when the relation is at the leaf id with a
        //narrowed row buffer and count (a null buffer for a full-depth terminal).
        int[] node = new int[relations.Count];
        int[] leafId = new int[relations.Count];
        int[]?[] rowsBuffer = new int[]?[relations.Count];
        int[] rowsCount = new int[relations.Count];
        uint[] bindings = new uint[levelCount];

        LevelFrame[] frames = new LevelFrame[levelCount];
        for(int level = 0; level < levelCount; level++)
        {
            frames[level] = new LevelFrame(trieAt[level], leafAt[level]);
        }

        SolutionBatch output = new(variableOrder);
        int rows = 0;

        int top = 0;
        EnterLevel(frames[0], relations, node, leafId, rowsBuffer, rowsCount);

        while(top >= 0)
        {
            LevelFrame frame = frames[top];

            //Returning into a frame whose current value was applied: undo it
            //before advancing to the next value.
            if(frame.Descended)
            {
                Restore(frame, node, leafId, rowsBuffer, rowsCount);
                frame.Descended = false;
            }

            if(frame.Cursor >= frame.KeyCount)
            {
                top--;

                continue;
            }

            uint value = frame.Keys[frame.Cursor];
            frame.Cursor++;
            bindings[top] = value;

            Save(frame, node, leafId, rowsBuffer, rowsCount);
            Apply(frame, value, relations, node, leafId, rowsBuffer, rowsCount);
            frame.Descended = true;

            if(top == lastLevel)
            {
                for(int column = 0; column < levelCount; column++)
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
                top++;
                EnterLevel(frames[top], relations, node, leafId, rowsBuffer, rowsCount);
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }

    /// <summary>Opens a level's frame for a fresh visit: resets the cursor and computes the candidate intersection into the frame's reusable keys buffer.</summary>
    /// <param name="frame">The level's frame.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="node">The per-relation current trie node ids.</param>
    /// <param name="leafId">The per-relation leaf ids, valid once at the leaf.</param>
    /// <param name="rowsBuffer">The per-relation narrowed leaf row buffers, valid once at the leaf.</param>
    /// <param name="rowsCount">The per-relation narrowed leaf row counts, parallel to <paramref name="rowsBuffer"/>.</param>
    private static void EnterLevel(
        LevelFrame frame,
        IReadOnlyList<GeneralizedHashTrie> relations,
        int[] node,
        int[] leafId,
        int[]?[] rowsBuffer,
        int[] rowsCount)
    {
        frame.Cursor = 0;
        frame.Descended = false;
        IntersectInto(frame, relations, node, leafId, rowsBuffer, rowsCount);
    }

    /// <summary>
    /// The multiway intersection at a variable, written into the frame's keys
    /// buffer: the values held by every trie participant's current node and
    /// present in every leaf participant's narrowed rows. The smallest
    /// participant set drives the scan; the others are probed — a trie node by
    /// lookup, a leaf by its value set, rebuilt into the frame's reusable sets.
    /// </summary>
    /// <param name="frame">The level's frame.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="rowsBuffer">The per-relation narrowed leaf row buffers.</param>
    /// <param name="rowsCount">The per-relation narrowed leaf row counts.</param>
    private static void IntersectInto(
        LevelFrame frame,
        IReadOnlyList<GeneralizedHashTrie> relations,
        int[] node,
        int[] leafId,
        int[]?[] rowsBuffer,
        int[] rowsCount)
    {
        int[] trieRelations = frame.TrieRelations;
        int[] leafRelations = frame.LeafRelations;

        //Each leaf participant's distinct values, for both probing and (when it
        //is the smallest) driving; the sets are cleared and refilled in place.
        for(int i = 0; i < leafRelations.Length; i++)
        {
            int relation = leafRelations[i];
            HashSet<uint> values = frame.LeafSets[i];
            values.Clear();

            int[] current = rowsBuffer[relation]!;
            for(int r = 0; r < rowsCount[relation]; r++)
            {
                values.Add(relations[relation].LeafValue(leafId[relation], current[r], frame.LeafColumns[i]));
            }
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
            if(frame.LeafSets[i].Count < smallestSize)
            {
                smallestSize = frame.LeafSets[i].Count;
                driverKind = 1;
                driverIndex = i;
            }
        }

        frame.KeyCount = 0;
        if(driverKind == 0)
        {
            int relation = trieRelations[driverIndex];
            OpenAddressedTable<int>.Enumerator scan = relations[relation].NodeAt(node[relation]).GetEnumerator();
            while(scan.MoveNext())
            {
                uint value = (uint)scan.Current.Key;
                if(HeldByAllOthers(frame, relations, node, driverKind, driverIndex, value))
                {
                    frame.AppendKey(value);
                }
            }
        }
        else
        {
            foreach(uint value in frame.LeafSets[driverIndex])
            {
                if(HeldByAllOthers(frame, relations, node, driverKind, driverIndex, value))
                {
                    frame.AppendKey(value);
                }
            }
        }
    }

    /// <summary>Whether every non-driver participant holds the value: each trie participant's current node by lookup, each leaf participant by its value set.</summary>
    /// <param name="frame">The level's frame.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="driverKind">The driving participant's kind: 0 a trie node, 1 a leaf value set.</param>
    /// <param name="driverIndex">The driving participant's index within its kind.</param>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when every non-driver participant holds the value.</returns>
    private static bool HeldByAllOthers(
        LevelFrame frame,
        IReadOnlyList<GeneralizedHashTrie> relations,
        int[] node,
        int driverKind,
        int driverIndex,
        uint value)
    {
        for(int i = 0; i < frame.TrieRelations.Length; i++)
        {
            if((driverKind != 0 || i != driverIndex) && !relations[frame.TrieRelations[i]].NodeAt(node[frame.TrieRelations[i]]).TryGetValue(value, out int _))
            {
                return false;
            }
        }

        for(int i = 0; i < frame.LeafRelations.Length; i++)
        {
            if((driverKind != 1 || i != driverIndex) && !frame.LeafSets[i].Contains(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Saves each participant's state before the current value is applied, so the frame can undo it on backtrack.</summary>
    /// <param name="frame">The level's frame.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="rowsBuffer">The per-relation narrowed leaf row buffers.</param>
    /// <param name="rowsCount">The per-relation narrowed leaf row counts.</param>
    private static void Save(LevelFrame frame, int[] node, int[] leafId, int[]?[] rowsBuffer, int[] rowsCount)
    {
        for(int i = 0; i < frame.AllParticipants.Length; i++)
        {
            int relation = frame.AllParticipants[i];
            frame.SavedNode[i] = node[relation];
            frame.SavedLeafId[i] = leafId[relation];
            frame.SavedRowsBuffer[i] = rowsBuffer[relation];
            frame.SavedRowsCount[i] = rowsCount[relation];
        }
    }

    /// <summary>Restores each participant's saved state, undoing the current value's application.</summary>
    /// <param name="frame">The level's frame.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="rowsBuffer">The per-relation narrowed leaf row buffers.</param>
    /// <param name="rowsCount">The per-relation narrowed leaf row counts.</param>
    private static void Restore(LevelFrame frame, int[] node, int[] leafId, int[]?[] rowsBuffer, int[] rowsCount)
    {
        for(int i = 0; i < frame.AllParticipants.Length; i++)
        {
            int relation = frame.AllParticipants[i];
            node[relation] = frame.SavedNode[i];
            leafId[relation] = frame.SavedLeafId[i];
            rowsBuffer[relation] = frame.SavedRowsBuffer[i];
            rowsCount[relation] = frame.SavedRowsCount[i];
        }
    }

    /// <summary>
    /// Applies the chosen value: each trie participant descends (the deepest
    /// descent moving it onto its leaf, or to a full-depth terminal), and each
    /// leaf participant narrows its rows to those carrying the value. Arrival
    /// and narrowed row sets are written into the frame's per-participant
    /// reusable buffers; the inputs always live in a shallower level's buffers
    /// (this frame's state was restored before the visit), so the write never
    /// aliases its read.
    /// </summary>
    /// <param name="frame">The level's frame.</param>
    /// <param name="value">The chosen value.</param>
    /// <param name="relations">The relations.</param>
    /// <param name="node">The per-relation current node ids.</param>
    /// <param name="leafId">The per-relation leaf ids.</param>
    /// <param name="rowsBuffer">The per-relation narrowed leaf row buffers.</param>
    /// <param name="rowsCount">The per-relation narrowed leaf row counts.</param>
    private static void Apply(
        LevelFrame frame,
        uint value,
        IReadOnlyList<GeneralizedHashTrie> relations,
        int[] node,
        int[] leafId,
        int[]?[] rowsBuffer,
        int[] rowsCount)
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

                if(relations[relation].LeafColumns.Length > 0)
                {
                    int count = relations[relation].LeafTupleCount(child);
                    int[] arrival = frame.ArrivalBuffer(i, count);
                    for(int row = 0; row < count; row++)
                    {
                        arrival[row] = row;
                    }

                    rowsBuffer[relation] = arrival;
                    rowsCount[relation] = count;
                }
                else
                {
                    rowsBuffer[relation] = null;
                    rowsCount[relation] = 0;
                }
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
            int[] current = rowsBuffer[relation]!;
            int currentCount = rowsCount[relation];
            int[] narrowed = frame.NarrowBuffer(i, currentCount);
            int narrowedCount = 0;
            for(int r = 0; r < currentCount; r++)
            {
                if(relations[relation].LeafValue(leafId[relation], current[r], leafColumn) == value)
                {
                    narrowed[narrowedCount] = current[r];
                    narrowedCount++;
                }
            }

            rowsBuffer[relation] = narrowed;
            rowsCount[relation] = narrowedCount;
        }
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
    /// One global level's frame of the depth-first walk: the static trie and
    /// leaf participants at that variable, and the level's reusable walk state —
    /// the candidate keys buffer with its cursor, the participants' state saved
    /// before the current value's application, the leaf participants' value
    /// sets, and the row buffers <see cref="Apply"/> writes arrivals and
    /// narrowings into. One frame per level is live at a time (the stack is the
    /// contiguous level prefix), so a single instance per level serves every
    /// visit; the variable-size buffers grow on demand and are never returned
    /// per value.
    /// </summary>
    private sealed class LevelFrame
    {
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

        /// <summary>The candidate values for this variable, the first <see cref="KeyCount"/> valid; replaced by a larger buffer when an intersection outgrows it.</summary>
        public uint[] Keys { get; set; }

        /// <summary>The number of valid candidate values in <see cref="Keys"/> for the current visit.</summary>
        public int KeyCount { get; set; }

        /// <summary>The next candidate index to try.</summary>
        public int Cursor { get; set; }

        /// <summary>Whether the current value has been applied and awaits undo on the next visit.</summary>
        public bool Descended { get; set; }

        /// <summary>Each participant's node id saved before the current application, parallel to <see cref="AllParticipants"/>.</summary>
        public int[] SavedNode { get; }

        /// <summary>Each participant's leaf id saved before the current application.</summary>
        public int[] SavedLeafId { get; }

        /// <summary>Each participant's narrowed leaf row buffer saved before the current application.</summary>
        public int[]?[] SavedRowsBuffer { get; }

        /// <summary>Each participant's narrowed leaf row count saved before the current application.</summary>
        public int[] SavedRowsCount { get; }

        /// <summary>Each leaf participant's distinct-value set, cleared and refilled per visit, parallel to <see cref="LeafRelations"/>.</summary>
        public HashSet<uint>[] LeafSets { get; }

        /// <summary>Each leaf participant's reusable narrowed-rows buffer, grown on demand, parallel to <see cref="LeafRelations"/>.</summary>
        private int[][] NarrowBuffers { get; }

        /// <summary>Each trie participant's reusable arrival-rows buffer for its deepest descent, grown on demand, parallel to <see cref="TrieRelations"/>.</summary>
        private int[][] ArrivalBuffers { get; }

        /// <summary>Constructs the level's frame over its trie and leaf participants, with empty buffers that grow on first use.</summary>
        /// <param name="trie">The trie participants at this level.</param>
        /// <param name="leaf">The leaf participants at this level.</param>
        public LevelFrame(List<TrieParticipant> trie, List<LeafParticipant> leaf)
        {
            TrieRelations = new int[trie.Count];
            TrieIsDeepest = new bool[trie.Count];
            ArrivalBuffers = new int[trie.Count][];
            for(int i = 0; i < trie.Count; i++)
            {
                TrieRelations[i] = trie[i].Relation;
                TrieIsDeepest[i] = trie[i].IsDeepest;
                ArrivalBuffers[i] = [];
            }

            LeafRelations = new int[leaf.Count];
            LeafColumns = new int[leaf.Count];
            LeafSets = new HashSet<uint>[leaf.Count];
            NarrowBuffers = new int[leaf.Count][];
            for(int i = 0; i < leaf.Count; i++)
            {
                LeafRelations[i] = leaf[i].Relation;
                LeafColumns[i] = leaf[i].LeafColumn;
                LeafSets[i] = [];
                NarrowBuffers[i] = [];
            }

            AllParticipants = [.. TrieRelations, .. LeafRelations];
            Keys = [];
            SavedNode = new int[AllParticipants.Length];
            SavedLeafId = new int[AllParticipants.Length];
            SavedRowsBuffer = new int[]?[AllParticipants.Length];
            SavedRowsCount = new int[AllParticipants.Length];
        }

        /// <summary>Appends a candidate value to the keys buffer, growing it geometrically when full.</summary>
        /// <param name="value">The candidate value.</param>
        public void AppendKey(uint value)
        {
            if(KeyCount == Keys.Length)
            {
                uint[] grown = new uint[Math.Max(4, Keys.Length * 2)];
                Array.Copy(Keys, grown, KeyCount);
                Keys = grown;
            }

            Keys[KeyCount] = value;
            KeyCount++;
        }

        /// <summary>A trie participant's arrival-rows buffer with at least the requested capacity; superseded references are held only by frames already popped.</summary>
        /// <param name="participant">The trie participant index.</param>
        /// <param name="capacity">The required element count.</param>
        /// <returns>The buffer.</returns>
        public int[] ArrivalBuffer(int participant, int capacity)
        {
            int[] buffer = ArrivalBuffers[participant];
            if(buffer.Length < capacity)
            {
                buffer = new int[Math.Max(capacity, buffer.Length * 2)];
                ArrivalBuffers[participant] = buffer;
            }

            return buffer;
        }

        /// <summary>A leaf participant's narrowed-rows buffer with at least the requested capacity; superseded references are held only by frames already popped.</summary>
        /// <param name="participant">The leaf participant index.</param>
        /// <param name="capacity">The required element count.</param>
        /// <returns>The buffer.</returns>
        public int[] NarrowBuffer(int participant, int capacity)
        {
            int[] buffer = NarrowBuffers[participant];
            if(buffer.Length < capacity)
            {
                buffer = new int[Math.Max(capacity, buffer.Length * 2)];
                NarrowBuffers[participant] = buffer;
            }

            return buffer;
        }
    }
}
