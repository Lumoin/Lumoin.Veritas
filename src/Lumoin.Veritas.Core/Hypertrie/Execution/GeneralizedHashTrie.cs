using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// A Generalized Hash Trie (GHT) over one relation — the unifying data
/// structure of Free Join. The first <see cref="TrieColumns"/> of the
/// relation's schema become hashed trie levels (each internal node an
/// <see cref="OpenAddressedTable{TValue}"/> mapping a column value to its
/// child), and the remaining <see cref="LeafColumns"/> sit as flat tuple
/// vectors at the leaves. The trie <em>depth</em> is what makes one structure
/// span both join paradigms: depth-1 (one hash level, then leaf vectors) is a
/// binary hash join's build side; full depth (every column hashed) is a
/// worst-case-optimal join's trie.
/// </summary>
/// <remarks>
/// <para>
/// Built from a relation's <see cref="SolutionBatch"/> stream in one of two
/// <see cref="FreeJoinTrieBuild"/> modes behind the same navigation contract.
/// An <see cref="FreeJoinTrieBuild.Eager"/> trie hashes every internal map at
/// build time, copies leaf tuples into packed vectors, and is read-only
/// thereafter. A <see cref="FreeJoinTrieBuild.Lazy"/> trie — the
/// column-oriented lazy trie — stores the relation's columns and the root row
/// set without hashing; each internal map materialises on its first
/// navigation touch by grouping its row subset on its level's column, and
/// leaves are row-id subsets read through the column store. Lazy navigation
/// therefore mutates the trie: a lazy instance serves one query's descent on
/// one thread, the same single-consumer discipline as a query's arena.
/// </para>
/// <para>
/// <see cref="Flatten"/> reconstructs the relation's rows — the differential
/// oracle that proves the trie lossless under either mode (in the lazy mode
/// it forces the whole trie). The hot path stays concrete: the internal maps
/// are value-type-specialised <see cref="OpenAddressedTable{TValue}"/> over a
/// node id, not a virtual node hierarchy, and the build mode is a private
/// discriminator, never a dispatch surface.
/// </para>
/// </remarks>
[DebuggerDisplay("GeneralizedHashTrie Schema={Schema.Count} TrieDepth={TrieColumns.Length} Nodes={NodeCount} Leaves={LeafCount}")]
public sealed class GeneralizedHashTrie
{
    /// <summary>The relation's full schema, positional against the source batch columns and the flattened output.</summary>
    public IReadOnlyList<Variable> Schema { get; }

    /// <summary>The schema-column indices that form the trie levels, in descent order.</summary>
    public int[] TrieColumns { get; }

    /// <summary>The schema-column indices stored as leaf tuple values, below the deepest trie level.</summary>
    public int[] LeafColumns { get; }

    /// <summary>The internal maps; <c>nodes[0]</c> is the root. A map at a non-deepest trie level maps a value to a child node id, the deepest level's map maps a value to a leaf id. Eager entries are always built; a lazy entry is <see langword="null"/> until its first touch forces it.</summary>
    private readonly List<OpenAddressedTable<int>?> nodes;

    /// <summary>The eager leaf tuple vectors, packed row-major with <see cref="LeafColumns"/> length as stride; indexed by leaf id. Unused in the lazy mode, whose leaves are <see cref="leafRows"/>.</summary>
    private readonly List<List<uint>> leaves;

    /// <summary>The lazy column store: one value vector per schema column in scan order, read through by leaf accesses and level groupings. <see langword="null"/> in the eager mode — the build-mode discriminator.</summary>
    private readonly List<uint>[]? lazyColumns;

    /// <summary>The lazy nodes' unforced row subsets, parallel to <see cref="nodes"/>; an entry is released to <see langword="null"/> once its node is forced. <see langword="null"/> in the eager mode.</summary>
    private readonly List<List<int>?>? nodeRows;

    /// <summary>The lazy nodes' trie levels, parallel to <see cref="nodes"/> — which trie column a force groups by. <see langword="null"/> in the eager mode.</summary>
    private readonly List<int>? nodeLevels;

    /// <summary>The lazy leaves: row-id subsets into the column store, duplicates preserved; indexed by leaf id. <see langword="null"/> in the eager mode.</summary>
    private readonly List<List<int>>? leafRows;

    /// <summary>Constructs a trie over its node and leaf pools; the lazy pools are <see langword="null"/> for an eager trie.</summary>
    /// <param name="schema">The relation's full schema.</param>
    /// <param name="trieColumns">The trie-level column indices.</param>
    /// <param name="leafColumns">The leaf column indices.</param>
    /// <param name="nodes">The internal map pool, root first.</param>
    /// <param name="leaves">The eager leaf tuple-vector pool.</param>
    /// <param name="lazyColumns">The lazy column store, or <see langword="null"/> for an eager trie.</param>
    /// <param name="nodeRows">The lazy nodes' unforced row subsets, or <see langword="null"/> for an eager trie.</param>
    /// <param name="nodeLevels">The lazy nodes' trie levels, or <see langword="null"/> for an eager trie.</param>
    /// <param name="leafRows">The lazy leaf row subsets, or <see langword="null"/> for an eager trie.</param>
    private GeneralizedHashTrie(
        IReadOnlyList<Variable> schema,
        int[] trieColumns,
        int[] leafColumns,
        List<OpenAddressedTable<int>?> nodes,
        List<List<uint>> leaves,
        List<uint>[]? lazyColumns,
        List<List<int>?>? nodeRows,
        List<int>? nodeLevels,
        List<List<int>>? leafRows)
    {
        Schema = schema;
        TrieColumns = trieColumns;
        LeafColumns = leafColumns;
        this.nodes = nodes;
        this.leaves = leaves;
        this.lazyColumns = lazyColumns;
        this.nodeRows = nodeRows;
        this.nodeLevels = nodeLevels;
        this.leafRows = leafRows;
    }

    /// <summary>The internal map at a node id, for navigation by the generic join: its keys are the descent values at this level, mapping to a child node id (non-deepest level) or a leaf id (deepest level). The root is node id 0. A lazy trie forces the node here on its first touch, so navigation is single-consumer in that mode.</summary>
    /// <param name="nodeId">The node id.</param>
    /// <returns>The node's map.</returns>
    internal OpenAddressedTable<int> NodeAt(int nodeId)
    {
        return nodes[nodeId] ?? Force(nodeId);
    }

    /// <summary>The number of tuples in a leaf vector, for the generic join's leaf iteration: the eager leaf's packed length over the leaf stride, or the lazy leaf's row-subset count. Zero when the trie is full-depth (no leaf columns).</summary>
    /// <param name="leafId">The leaf id, as mapped by the deepest trie node.</param>
    /// <returns>The leaf's tuple count.</returns>
    internal int LeafTupleCount(int leafId)
    {
        if(LeafColumns.Length == 0)
        {
            return 0;
        }

        return lazyColumns is null ? leaves[leafId].Count / LeafColumns.Length : leafRows![leafId].Count;
    }

    /// <summary>One leaf tuple's value in a given leaf column, for the generic join's leaf narrowing: read from the eager packed vector, or through the lazy leaf's row id into the column store.</summary>
    /// <param name="leafId">The leaf id.</param>
    /// <param name="tupleRow">The tuple's row within the leaf.</param>
    /// <param name="leafColumnIndex">The index into <see cref="LeafColumns"/>.</param>
    /// <returns>The value.</returns>
    internal uint LeafValue(int leafId, int tupleRow, int leafColumnIndex)
    {
        return lazyColumns is null
            ? leaves[leafId][(tupleRow * LeafColumns.Length) + leafColumnIndex]
            : lazyColumns[LeafColumns[leafColumnIndex]][leafRows![leafId][tupleRow]];
    }

    /// <summary>The number of internal trie node entries — a build-size diagnostic. An eager trie counts every internal map; a lazy trie counts the entries discovered so far (the root plus every child a forced ancestor's grouping created, each still unforced until its own touch).</summary>
    public int NodeCount => nodes.Count;

    /// <summary>The number of leaf vectors — a build-size diagnostic. An eager trie counts every leaf; a lazy trie counts the leaves its deepest-level forces have created so far.</summary>
    public int LeafCount => lazyColumns is null ? leaves.Count : leafRows!.Count;

    /// <summary>
    /// The number of 32-bit values the trie currently holds — the footprint
    /// diagnostic beside <see cref="NodeCount"/> and <see cref="LeafCount"/>.
    /// An eager trie holds its packed leaf tuple values; a lazy trie holds the
    /// whole column store plus the live row-id subsets (unforced nodes and
    /// created leaves), so the two modes' retention is comparable on the
    /// benchmark stand. This counts the VALUE side only: a full-depth trie has
    /// no leaf columns, so its zero here is the absence of leaf values and not
    /// the absence of footprint, and depths are comparable only through the
    /// node-side companion <see cref="RetainedNodeEntryCount"/>.
    /// </summary>
    public long RetainedValueCount
    {
        get
        {
            long retained = 0;

            if(lazyColumns is null)
            {
                foreach(List<uint> leaf in leaves)
                {
                    retained += leaf.Count;
                }

                return retained;
            }

            foreach(List<uint> column in lazyColumns)
            {
                retained += column.Count;
            }

            foreach(List<int>? rows in nodeRows!)
            {
                if(rows is not null)
                {
                    retained += rows.Count;
                }
            }

            foreach(List<int> rows in leafRows!)
            {
                retained += rows.Count;
            }

            return retained;
        }
    }

    /// <summary>
    /// The number of key-to-child entries materialised across the trie's
    /// internal maps — the node-side footprint beside
    /// <see cref="RetainedValueCount"/>'s value side, so tries built at
    /// different depths are comparable. An eager trie counts every map's
    /// entries; a lazy trie counts only the maps its navigation has forced, and
    /// reading this never forces one.
    /// </summary>
    internal long RetainedNodeEntryCount
    {
        get
        {
            long entries = 0;

            //The maps are read straight off the pool rather than through NodeAt,
            //which forces a lazy node on touch: measuring must not materialise
            //what it measures.
            for(int nodeId = 0; nodeId < nodes.Count; nodeId++)
            {
                OpenAddressedTable<int>? table = nodes[nodeId];
                if(table is not null)
                {
                    entries += table.Count;
                }
            }

            return entries;
        }
    }

    /// <summary>
    /// Builds the trie from <paramref name="relation"/> in the given mode. The
    /// eager mode descends every row through the trie columns, creating
    /// internal maps as needed, and appends its leaf columns to the reached
    /// leaf vector. The lazy mode stores the relation's columns and the root
    /// row set without hashing; internal maps materialise per node on first
    /// navigation touch.
    /// </summary>
    /// <param name="schema">The relation's full schema, positional against the batch columns.</param>
    /// <param name="relation">The relation's batch stream; consumed eagerly.</param>
    /// <param name="trieColumns">The schema-column indices forming the trie levels, in descent order; at least one.</param>
    /// <param name="leafColumns">The schema-column indices stored at the leaves; the trie and leaf columns together cover the schema.</param>
    /// <param name="trieBuild">The build mode; eager by default.</param>
    /// <returns>The built trie.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="trieColumns"/> is empty.</exception>
    public static GeneralizedHashTrie Build(
        IReadOnlyList<Variable> schema,
        IEnumerable<SolutionBatch> relation,
        int[] trieColumns,
        int[] leafColumns,
        FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(relation);
        ArgumentNullException.ThrowIfNull(trieColumns);
        ArgumentNullException.ThrowIfNull(leafColumns);

        if(trieColumns.Length == 0)
        {
            throw new ArgumentException("A generalized hash trie needs at least one trie level.", nameof(trieColumns));
        }

        return trieBuild switch
        {
            FreeJoinTrieBuild.Lazy => BuildLazy(schema, relation, trieColumns, leafColumns),
            _ => BuildEager(schema, relation, trieColumns, leafColumns)
        };
    }

    /// <summary>The eager build: each row descends the trie columns, creating internal maps as needed, and appends its leaf columns to the reached leaf vector.</summary>
    /// <param name="schema">The relation's full schema.</param>
    /// <param name="relation">The relation's batch stream.</param>
    /// <param name="trieColumns">The trie-level column indices.</param>
    /// <param name="leafColumns">The leaf column indices.</param>
    /// <returns>The built trie.</returns>
    private static GeneralizedHashTrie BuildEager(
        IReadOnlyList<Variable> schema,
        IEnumerable<SolutionBatch> relation,
        int[] trieColumns,
        int[] leafColumns)
    {
        int trieDepth = trieColumns.Length;
        int leafStride = leafColumns.Length;
        int lastLevel = trieDepth - 1;

        List<OpenAddressedTable<int>?> nodes = [new OpenAddressedTable<int>()];
        List<List<uint>> leaves = [];

        foreach(SolutionBatch batch in relation)
        {
            int count = batch.Count;
            for(int row = 0; row < count; row++)
            {
                int node = 0;
                for(int level = 0; level < trieDepth; level++)
                {
                    uint key = batch.ColumnOf(trieColumns[level])[row];
                    OpenAddressedTable<int> table = nodes[node]!;

                    if(level < lastLevel)
                    {
                        if(!table.TryGetValue(key, out int child))
                        {
                            child = nodes.Count;
                            nodes.Add(new OpenAddressedTable<int>());
                            table.Exchange(key, child, out _);
                        }

                        node = child;
                    }
                    else
                    {
                        if(!table.TryGetValue(key, out int leafId))
                        {
                            leafId = leaves.Count;
                            leaves.Add([]);
                            table.Exchange(key, leafId, out _);
                        }

                        List<uint> leaf = leaves[leafId];
                        for(int column = 0; column < leafStride; column++)
                        {
                            leaf.Add(batch.ColumnOf(leafColumns[column])[row]);
                        }
                    }
                }
            }
        }

        return new GeneralizedHashTrie(schema, trieColumns, leafColumns, nodes, leaves, lazyColumns: null, nodeRows: null, nodeLevels: null, leafRows: null);
    }

    /// <summary>The lazy build: the relation's columns are copied into the column store in scan order and the root entry holds every row unforced; no map is hashed here.</summary>
    /// <param name="schema">The relation's full schema.</param>
    /// <param name="relation">The relation's batch stream.</param>
    /// <param name="trieColumns">The trie-level column indices.</param>
    /// <param name="leafColumns">The leaf column indices.</param>
    /// <returns>The built trie.</returns>
    private static GeneralizedHashTrie BuildLazy(
        IReadOnlyList<Variable> schema,
        IEnumerable<SolutionBatch> relation,
        int[] trieColumns,
        int[] leafColumns)
    {
        List<uint>[] columns = new List<uint>[schema.Count];
        for(int column = 0; column < columns.Length; column++)
        {
            columns[column] = [];
        }

        int rowCount = 0;
        foreach(SolutionBatch batch in relation)
        {
            for(int column = 0; column < columns.Length; column++)
            {
                columns[column].AddRange(batch.ColumnOf(column));
            }

            rowCount += batch.Count;
        }

        List<int> rootRows = new(rowCount);
        for(int row = 0; row < rowCount; row++)
        {
            rootRows.Add(row);
        }

        return new GeneralizedHashTrie(
            schema, trieColumns, leafColumns,
            nodes: [null], leaves: [],
            columns, nodeRows: [rootRows], nodeLevels: [0], leafRows: []);
    }

    /// <summary>
    /// Forces one lazy node: its row subset groups on its level's column into
    /// a fresh map — child entries born unforced with their row subsets at a
    /// non-deepest level, leaf row subsets at the deepest — and the node's own
    /// row subset is released. Rows group in subset order (scan order
    /// restricted), so the map's insertion sequence matches what the eager
    /// build would have produced for the same node.
    /// </summary>
    /// <param name="nodeId">The unforced node's id.</param>
    /// <returns>The node's built map.</returns>
    private OpenAddressedTable<int> Force(int nodeId)
    {
        List<int> rows = nodeRows![nodeId]!;
        int level = nodeLevels![nodeId];
        List<uint> column = lazyColumns![TrieColumns[level]];
        bool deepest = level == TrieColumns.Length - 1;

        OpenAddressedTable<int> table = new();
        for(int i = 0; i < rows.Count; i++)
        {
            int row = rows[i];
            uint key = column[row];

            if(deepest)
            {
                if(!table.TryGetValue(key, out int leafId))
                {
                    leafId = leafRows!.Count;
                    leafRows.Add([]);
                    table.Exchange(key, leafId, out _);
                }

                leafRows![leafId].Add(row);
            }
            else
            {
                if(!table.TryGetValue(key, out int child))
                {
                    child = nodes.Count;
                    nodes.Add(null);
                    nodeRows.Add([]);
                    nodeLevels.Add(level + 1);
                    table.Exchange(key, child, out _);
                }

                nodeRows[child]!.Add(row);
            }
        }

        nodes[nodeId] = table;
        nodeRows[nodeId] = null;

        return table;
    }

    /// <summary>
    /// Reconstructs the relation's rows as a <see cref="SolutionBatch"/> stream
    /// over <see cref="Schema"/> — the lossless inverse of the build in either
    /// mode (a lazy trie is forced whole by the walk). A recursion-free descent
    /// walks the trie depth-first through one enumerator per level, placing the
    /// trie path values and each leaf tuple into their schema columns.
    /// </summary>
    /// <returns>The flattened batch stream; batches are full except the last.</returns>
    public IEnumerable<SolutionBatch> Flatten()
    {
        int trieDepth = TrieColumns.Length;
        int leafStride = LeafColumns.Length;
        int lastLevel = trieDepth - 1;

        SolutionBatch output = new(Schema);
        int rows = 0;

        uint[] path = new uint[trieDepth];
        OpenAddressedTable<int>.Enumerator[] levels = new OpenAddressedTable<int>.Enumerator[trieDepth];
        levels[0] = NodeAt(0).GetEnumerator();
        int level = 0;

        while(level >= 0)
        {
            if(!levels[level].MoveNext())
            {
                level--;

                continue;
            }

            (ulong key, int child) = levels[level].Current;
            path[level] = (uint)key;

            if(level < lastLevel)
            {
                levels[level + 1] = NodeAt(child).GetEnumerator();
                level++;

                continue;
            }

            //Deepest level reached: emit one row per leaf tuple, or exactly one
            //row when the leaf carries no columns — a full-depth trie's
            //terminal, which LeafTupleCount (zero at stride zero, the generic
            //join's guarded contract) deliberately does not count.
            int leafTuples = leafStride == 0 ? 1 : LeafTupleCount(child);

            for(int leafRow = 0; leafRow < leafTuples; leafRow++)
            {
                for(int i = 0; i < trieDepth; i++)
                {
                    output.ColumnSpan(TrieColumns[i])[rows] = path[i];
                }

                for(int column = 0; column < leafStride; column++)
                {
                    output.ColumnSpan(LeafColumns[column])[rows] = LeafValue(child, leafRow, column);
                }

                rows++;

                if(rows == SolutionBatch.BatchLength)
                {
                    output.SetCount(rows);

                    yield return output;

                    output = new SolutionBatch(Schema);
                    rows = 0;
                }
            }
        }

        if(rows > 0)
        {
            output.SetCount(rows);

            yield return output;
        }
    }
}
