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
    /// Runs the generic join as a <see cref="FactorizedBatch"/> for a star with
    /// optional chain extensions: every <em>centre</em> relation is a two-column
    /// trie sharing the order's leading variable as its key and binding one
    /// further branch variable (pairwise distinct), and every other relation is
    /// an <em>extension</em> — a two-column trie over one centre branch variable
    /// then a fresh variable, the chain step the §4.2 <c>NestBranch</c> takes.
    /// The two columns may be split either way: both as trie levels, or one
    /// trie level over a leaf vector carrying the second column. A key's
    /// level-1 <em>extent</em> is a hash node in the first form and a leaf
    /// tuple vector in the second, and the branch values emitted from it are
    /// that extent's DISTINCT values under either — a node's key set already is
    /// a set, a leaf vector is a bag this emission deduplicates. After the key
    /// each centre's matches vary
    /// independently, so the join emits one group per key value with each
    /// centre's matches as a branch; an extended branch is regrouped into a
    /// nested single-level sub-batch keyed on the branch variable, carrying each
    /// extension's matches per value (the depth-2 form, with branch values
    /// drawing no extension match dropped — the semijoin the chain entails).
    /// Returns <see langword="null"/> for any other shape; the differential
    /// oracle is the flattened batch against <see cref="Execute"/>.
    /// </summary>
    /// <param name="relations">The relations, each a two-column trie however the two columns split between trie levels and a leaf vector: a centre over the key then its branch, or an extension over a centre branch then a fresh variable.</param>
    /// <param name="variableOrder">The global descent order; its first variable is the shared key, then the branch variables, then the extension variables.</param>
    /// <param name="arena">The arena the groups' key tuples, metadata, and branch values are allocated from; the result is valid until it is disposed.</param>
    /// <returns>The factorised result over <paramref name="variableOrder"/>, or <see langword="null"/> when the relations are not a star with chain extensions.</returns>
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
        int relationCount = relations.Count;

        //Every relation must carry exactly two columns, however they split
        //between trie levels and a leaf vector. A build always makes at least
        //one trie level, so the admissible splits are two trie levels and one
        //trie level over a one-column leaf; the leaf split is the discriminator
        //the emission reads a level-1 extent through.
        bool[] leafSourced = new bool[relationCount];
        for(int relation = 0; relation < relationCount; relation++)
        {
            GeneralizedHashTrie shape = relations[relation];
            if(shape.TrieColumns.Length + shape.LeafColumns.Length != 2)
            {
                return null;
            }

            leafSourced[relation] = shape.LeafColumns.Length == 1;
        }

        //Centres first: a relation whose level 0 is the key, binding a branch
        //variable at level 1 — distinct across centres and never the key.
        Dictionary<Variable, int> centreOfBranch = [];
        List<int> centreRelations = [];
        for(int relation = 0; relation < relationCount; relation++)
        {
            GeneralizedHashTrie trie = relations[relation];
            Variable level0 = trie.Schema[trie.TrieColumns[0]];
            if(level0 != key)
            {
                continue;
            }

            Variable level1 = LevelOneVariableOf(trie);
            if(level1 == key || !centreOfBranch.TryAdd(level1, relation))
            {
                return null;
            }

            centreRelations.Add(relation);
        }

        if(centreRelations.Count < 2)
        {
            return null;
        }

        //Extensions: every remaining relation must descend one centre branch
        //variable at level 0 and bind a fresh variable at level 1 — not the
        //key, not a branch, not another extension's variable (depth two only).
        List<int>?[] extensionsOf = new List<int>?[relationCount];
        HashSet<Variable> extensionVariables = [];
        for(int relation = 0; relation < relationCount; relation++)
        {
            GeneralizedHashTrie trie = relations[relation];
            Variable level0 = trie.Schema[trie.TrieColumns[0]];
            if(level0 == key)
            {
                continue;
            }

            if(!centreOfBranch.TryGetValue(level0, out int centre))
            {
                return null;
            }

            Variable level1 = LevelOneVariableOf(trie);
            if(level1 == key || centreOfBranch.ContainsKey(level1) || !extensionVariables.Add(level1))
            {
                return null;
            }

            extensionsOf[centre] ??= [];
            extensionsOf[centre]!.Add(relation);
        }

        //The order's non-key variables are exactly the branches plus the
        //extension variables: nothing else is bound.
        if(centreOfBranch.Count + extensionVariables.Count != variableOrder.Count - 1)
        {
            return null;
        }

        //Per branch (centre order): its output columns, and for an extended
        //branch the nested sub-batch's schema and inner branch columns — the
        //branch variable leads the nested schema, each extension one column.
        int branchCount = centreRelations.Count;
        int[][] branchColumns = new int[branchCount][];
        int[] branchStrides = new int[branchCount];
        Variable[]?[] nestedSchemas = new Variable[]?[branchCount];
        int[][]?[] nestedBranchColumns = new int[][]?[branchCount];
        int maximumExtensions = 0;
        for(int branch = 0; branch < branchCount; branch++)
        {
            GeneralizedHashTrie trie = relations[centreRelations[branch]];
            Variable branchVariable = LevelOneVariableOf(trie);
            List<int>? extensions = extensionsOf[centreRelations[branch]];

            if(extensions is null)
            {
                branchColumns[branch] = [orderIndex[branchVariable]];
                branchStrides[branch] = 1;

                continue;
            }

            //An extended branch carries no flat values in the parent; its
            //columns are the branch variable then each extension's variable,
            //positional against the nested sub-batch's schema.
            branchStrides[branch] = 0;
            int[] columns = new int[1 + extensions.Count];
            Variable[] nestedSchema = new Variable[1 + extensions.Count];
            int[][] innerColumns = new int[extensions.Count][];
            columns[0] = orderIndex[branchVariable];
            nestedSchema[0] = branchVariable;
            for(int extension = 0; extension < extensions.Count; extension++)
            {
                GeneralizedHashTrie extensionTrie = relations[extensions[extension]];
                Variable extensionVariable = LevelOneVariableOf(extensionTrie);
                columns[1 + extension] = orderIndex[extensionVariable];
                nestedSchema[1 + extension] = extensionVariable;
                innerColumns[extension] = [1 + extension];
            }

            branchColumns[branch] = columns;
            nestedSchemas[branch] = nestedSchema;
            nestedBranchColumns[branch] = innerColumns;
            maximumExtensions = Math.Max(maximumExtensions, extensions.Count);
        }

        bool hasExtensions = maximumExtensions > 0;

        //The smallest centre root drives the key intersection; a key absent
        //from any centre's root binds no row and drops the group.
        int driver = centreRelations[0];
        for(int branch = 1; branch < branchCount; branch++)
        {
            if(relations[centreRelations[branch]].NodeAt(0).Count < relations[driver].NodeAt(0).Count)
            {
                driver = centreRelations[branch];
            }
        }

        List<FactorizedGroup> groups = [];

        //Scratch reused across keys: the level-1 entry per centre — a node id
        //when both columns are trie levels, a leaf id when the second sits in
        //a leaf vector — the flat branch row counts, the per-extension entries
        //and counts under a branch value, the distinct-value buffers a
        //leaf-sourced extent is read through (materialised on first such use,
        //so a wholly full-depth call allocates none), and the staged
        //single-value tuple for arena copies.
        int[] centreEntry = new int[branchCount];
        int[] branchRowCounts = new int[branchCount];
        int[] extensionEntry = new int[Math.Max(maximumExtensions, 1)];
        int[] extensionCounts = new int[Math.Max(maximumExtensions, 1)];
        int[] extensionStrides = new int[Math.Max(maximumExtensions, 1)];
        Array.Fill(extensionStrides, 1);
        BranchValueBuffer?[] branchBuffers = new BranchValueBuffer?[branchCount];
        BranchValueBuffer?[] extensionBuffers = new BranchValueBuffer?[Math.Max(maximumExtensions, 1)];
        Span<uint> keyScratch = stackalloc uint[1];

        OpenAddressedTable<int>.Enumerator keyScan = relations[driver].NodeAt(0).GetEnumerator();
        while(keyScan.MoveNext())
        {
            uint keyValue = (uint)keyScan.Current.Key;

            bool present = true;
            for(int branch = 0; branch < branchCount; branch++)
            {
                if(!relations[centreRelations[branch]].NodeAt(0).TryGetValue(keyValue, out centreEntry[branch]))
                {
                    present = false;

                    break;
                }
            }

            if(!present)
            {
                continue;
            }

            //Extended branches first: regroup each branch value into a nested
            //group carrying every extension's matches, dropping values absent
            //from any extension; a branch left with no value kills the group
            //(its product is empty) before the flat branches are written.
            FactorizedBatch?[]? nestedBranches = hasExtensions ? new FactorizedBatch?[branchCount] : null;
            bool dead = false;
            for(int branch = 0; branch < branchCount && !dead; branch++)
            {
                List<int>? extensions = extensionsOf[centreRelations[branch]];
                if(extensions is null)
                {
                    continue;
                }

                //The branch values this centre holds under the key, read
                //through the depth discriminator: a level-1 node's key set, or
                //the leaf extent's values deduplicated into the branch's
                //buffer. Each value is nested by the same body either way.
                int centre = centreRelations[branch];
                List<FactorizedGroup> nestedGroups;
                if(leafSourced[centre])
                {
                    BranchValueBuffer buffer = branchBuffers[branch] ??= new BranchValueBuffer();
                    buffer.FillFromLeaf(relations[centre], centreEntry[branch]);
                    nestedGroups = new(buffer.Count);
                    uint[] branchValues = buffer.Values;
                    for(int value = 0; value < buffer.Count; value++)
                    {
                        NestBranchValue(relations, extensions, leafSourced, extensionBuffers, extensionEntry, extensionCounts, extensionStrides, branchValues[value], arena, nestedGroups);
                    }
                }
                else
                {
                    OpenAddressedTable<int> values = relations[centre].NodeAt(centreEntry[branch]);
                    nestedGroups = new(values.Count);
                    OpenAddressedTable<int>.Enumerator valueScan = values.GetEnumerator();
                    while(valueScan.MoveNext())
                    {
                        NestBranchValue(relations, extensions, leafSourced, extensionBuffers, extensionEntry, extensionCounts, extensionStrides, (uint)valueScan.Current.Key, arena, nestedGroups);
                    }
                }

                if(nestedGroups.Count == 0)
                {
                    dead = true;

                    continue;
                }

                nestedBranches![branch] = new FactorizedBatch(nestedSchemas[branch]!, [0], nestedBranchColumns[branch]!, nestedGroups);
            }

            if(dead)
            {
                continue;
            }

            //The flat branches: each centre's branch-variable values under this
            //key — the distinct values of its level-1 extent — counted before
            //the arena run is sized, then filled in place. A node-sourced
            //extent enumerates straight into the span with no transient array
            //between it and the arena; a leaf-sourced one is deduplicated into
            //the branch's buffer first, since a leaf carries no distinct count.
            //An extended branch carries zero rows here; its data is the
            //sub-batch.
            for(int branch = 0; branch < branchCount; branch++)
            {
                if(branchStrides[branch] == 0)
                {
                    branchRowCounts[branch] = 0;

                    continue;
                }

                int centre = centreRelations[branch];
                if(leafSourced[centre])
                {
                    BranchValueBuffer buffer = branchBuffers[branch] ??= new BranchValueBuffer();
                    buffer.FillFromLeaf(relations[centre], centreEntry[branch]);
                    branchRowCounts[branch] = buffer.Count;

                    continue;
                }

                branchRowCounts[branch] = relations[centre].NodeAt(centreEntry[branch]).Count;
            }

            FactorizedBranches branches = FactorizedBranches.Allocate(branchRowCounts, branchStrides, arena);
            for(int branch = 0; branch < branchCount; branch++)
            {
                if(branchStrides[branch] == 0)
                {
                    continue;
                }

                Span<uint> destination = branches.BranchSpan(branch);
                if(leafSourced[centreRelations[branch]])
                {
                    branchBuffers[branch]!.Values.AsSpan(0, branchRowCounts[branch]).CopyTo(destination);

                    continue;
                }

                int next = 0;
                OpenAddressedTable<int>.Enumerator branchScan = relations[centreRelations[branch]].NodeAt(centreEntry[branch]).GetEnumerator();
                while(branchScan.MoveNext())
                {
                    destination[next] = (uint)branchScan.Current.Key;
                    next++;
                }
            }

            keyScratch[0] = keyValue;
            groups.Add(new FactorizedGroup(arena.AllocateFrom(keyScratch), branches, nestedBranches));
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

    /// <summary>
    /// A two-column relation's level-1 variable, read through the depth
    /// discriminator: the second trie level's variable when both columns are
    /// trie levels, the single leaf column's variable when the second column
    /// sits in a leaf vector. Level 0 needs no such reading — a build always
    /// makes at least one trie level, so <see cref="GeneralizedHashTrie.TrieColumns"/>
    /// position zero names a trie level at either depth.
    /// </summary>
    /// <param name="relation">The two-column relation.</param>
    /// <returns>The variable the relation binds at level one.</returns>
    private static Variable LevelOneVariableOf(GeneralizedHashTrie relation)
    {
        return relation.LeafColumns.Length switch
        {
            1 => relation.Schema[relation.LeafColumns[0]],
            _ => relation.Schema[relation.TrieColumns[1]]
        };
    }

    /// <summary>
    /// Nests one branch value under its centre's extensions: every extension
    /// must hold the value — the semijoin the chain entails, so a value no
    /// extension matches is dropped — and a held value becomes a nested group
    /// whose branches are each extension's distinct matches. Those matches are
    /// a level-1 node's key set when the extension carries both columns as
    /// trie levels, and the leaf extent's deduplicated values when its second
    /// column sits in a leaf vector.
    /// </summary>
    /// <param name="relations">The relations.</param>
    /// <param name="extensions">The centre's extension relation indices.</param>
    /// <param name="leafSourced">Whether each relation's level-1 extent is a leaf vector, indexed by relation.</param>
    /// <param name="extensionBuffers">The per-extension-slot distinct-value buffers, materialised on first leaf-sourced use.</param>
    /// <param name="extensionEntry">The per-extension level-1 entry under this value: a node id at full depth, a leaf id at cover depth.</param>
    /// <param name="extensionCounts">The per-extension distinct match counts, sized before the arena run.</param>
    /// <param name="extensionStrides">The per-extension branch strides, each one.</param>
    /// <param name="branchValue">The branch value to nest.</param>
    /// <param name="arena">The arena the nested group's runs are allocated from.</param>
    /// <param name="nestedGroupsToAppendTo">The nested groups a held value is appended to.</param>
    private static void NestBranchValue(
        IReadOnlyList<GeneralizedHashTrie> relations,
        List<int> extensions,
        bool[] leafSourced,
        BranchValueBuffer?[] extensionBuffers,
        int[] extensionEntry,
        int[] extensionCounts,
        int[] extensionStrides,
        uint branchValue,
        FactorizedArena arena,
        List<FactorizedGroup> nestedGroupsToAppendTo)
    {
        for(int extension = 0; extension < extensions.Count; extension++)
        {
            if(!relations[extensions[extension]].NodeAt(0).TryGetValue(branchValue, out extensionEntry[extension]))
            {
                return;
            }
        }

        for(int extension = 0; extension < extensions.Count; extension++)
        {
            int relation = extensions[extension];
            if(leafSourced[relation])
            {
                BranchValueBuffer buffer = extensionBuffers[extension] ??= new BranchValueBuffer();
                buffer.FillFromLeaf(relations[relation], extensionEntry[extension]);
                extensionCounts[extension] = buffer.Count;

                continue;
            }

            extensionCounts[extension] = relations[relation].NodeAt(extensionEntry[extension]).Count;
        }

        FactorizedBranches nestedStore = FactorizedBranches.Allocate(
            extensionCounts.AsSpan(0, extensions.Count), extensionStrides.AsSpan(0, extensions.Count), arena);
        for(int extension = 0; extension < extensions.Count; extension++)
        {
            Span<uint> destination = nestedStore.BranchSpan(extension);
            if(leafSourced[extensions[extension]])
            {
                extensionBuffers[extension]!.Values.AsSpan(0, extensionCounts[extension]).CopyTo(destination);

                continue;
            }

            int next = 0;
            OpenAddressedTable<int>.Enumerator extensionScan = relations[extensions[extension]].NodeAt(extensionEntry[extension]).GetEnumerator();
            while(extensionScan.MoveNext())
            {
                destination[next] = (uint)extensionScan.Current.Key;
                next++;
            }
        }

        Span<uint> valueScratch = stackalloc uint[1];
        valueScratch[0] = branchValue;
        nestedGroupsToAppendTo.Add(new FactorizedGroup(arena.AllocateFrom(valueScratch), nestedStore));
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

    /// <summary>
    /// One level-1 extent's distinct values, read out of a leaf tuple vector.
    /// A hash node's key set already is a set; a leaf vector is a BAG — the
    /// eager build appends one tuple per source row and the lazy leaf is a
    /// duplicate-preserving row subset — so this buffer re-establishes the set
    /// the factorised emission requires, and reports the count before any value
    /// is copied, which the arena's write-in-place branch allocation needs.
    /// Cleared and refilled per use, its values array grown geometrically and
    /// never returned: the shape <see cref="LevelFrame"/>'s buffers already
    /// take.
    /// </summary>
    private sealed class BranchValueBuffer
    {
        /// <summary>The values appended so far, so a repeated leaf tuple is skipped.</summary>
        private HashSet<uint> Seen { get; } = [];

        /// <summary>The distinct values in first-occurrence order, the first <see cref="Count"/> valid; replaced by a larger array when an extent outgrows it.</summary>
        public uint[] Values { get; private set; } = [];

        /// <summary>The number of valid values in <see cref="Values"/> for the current fill.</summary>
        public int Count { get; private set; }

        /// <summary>Refills the buffer with a leaf extent's distinct values, walking the leaf's tuples once and appending each value on first occurrence.</summary>
        /// <param name="relation">The relation the leaf belongs to.</param>
        /// <param name="leafId">The leaf id the level-0 lookup returned.</param>
        public void FillFromLeaf(GeneralizedHashTrie relation, int leafId)
        {
            Seen.Clear();
            Count = 0;

            int tuples = relation.LeafTupleCount(leafId);
            for(int row = 0; row < tuples; row++)
            {
                uint value = relation.LeafValue(leafId, row, 0);
                if(!Seen.Add(value))
                {
                    continue;
                }

                if(Count == Values.Length)
                {
                    uint[] grown = new uint[Math.Max(4, Values.Length * 2)];
                    Array.Copy(Values, grown, Count);
                    Values = grown;
                }

                Values[Count] = value;
                Count++;
            }
        }
    }
}
