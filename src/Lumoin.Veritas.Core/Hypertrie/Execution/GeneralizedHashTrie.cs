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
/// child), and the remaining <see cref="LeafColumns"/> sit as packed tuple
/// vectors at the leaves. The trie <em>depth</em> is what makes one structure
/// span both join paradigms: depth-1 (one hash level, then leaf vectors) is a
/// binary hash join's build side; full depth (every column hashed) is a
/// worst-case-optimal join's trie.
/// </summary>
/// <remarks>
/// Built once from a relation's <see cref="SolutionBatch"/> stream, then
/// read-only. <see cref="Flatten"/> reconstructs the relation's rows — the
/// differential oracle that proves the trie lossless before the generic join
/// is built on it. The hot path stays concrete: the internal maps are
/// value-type-specialised <see cref="OpenAddressedTable{TValue}"/> over a node
/// id, not a virtual node hierarchy.
/// </remarks>
[DebuggerDisplay("GeneralizedHashTrie Schema={Schema.Count} TrieDepth={TrieColumns.Length} Nodes={nodes.Count} Leaves={leaves.Count}")]
public sealed class GeneralizedHashTrie
{
    /// <summary>The relation's full schema, positional against the source batch columns and the flattened output.</summary>
    public IReadOnlyList<Variable> Schema { get; }

    /// <summary>The schema-column indices that form the trie levels, in descent order.</summary>
    public int[] TrieColumns { get; }

    /// <summary>The schema-column indices stored as leaf tuple values, below the deepest trie level.</summary>
    public int[] LeafColumns { get; }

    /// <summary>The internal maps; <c>nodes[0]</c> is the root. A map at a non-deepest trie level maps a value to a child node id; the deepest level's map maps a value to a leaf id.</summary>
    private readonly List<OpenAddressedTable<int>> nodes;

    /// <summary>The leaf tuple vectors, packed row-major with <see cref="LeafColumns"/> length as stride; indexed by leaf id.</summary>
    private readonly List<List<uint>> leaves;

    /// <summary>Constructs a trie from its built node and leaf pools.</summary>
    /// <param name="schema">The relation's full schema.</param>
    /// <param name="trieColumns">The trie-level column indices.</param>
    /// <param name="leafColumns">The leaf column indices.</param>
    /// <param name="nodes">The internal map pool, root first.</param>
    /// <param name="leaves">The leaf tuple-vector pool.</param>
    private GeneralizedHashTrie(IReadOnlyList<Variable> schema, int[] trieColumns, int[] leafColumns, List<OpenAddressedTable<int>> nodes, List<List<uint>> leaves)
    {
        Schema = schema;
        TrieColumns = trieColumns;
        LeafColumns = leafColumns;
        this.nodes = nodes;
        this.leaves = leaves;
    }

    /// <summary>The number of internal trie nodes — a build-size diagnostic.</summary>
    public int NodeCount => nodes.Count;

    /// <summary>The number of leaf tuple vectors — a build-size diagnostic.</summary>
    public int LeafCount => leaves.Count;

    /// <summary>
    /// Builds the trie from <paramref name="relation"/>: each row descends the
    /// trie columns, creating internal maps as needed, and appends its leaf
    /// columns to the reached leaf vector.
    /// </summary>
    /// <param name="schema">The relation's full schema, positional against the batch columns.</param>
    /// <param name="relation">The relation's batch stream; consumed eagerly.</param>
    /// <param name="trieColumns">The schema-column indices forming the trie levels, in descent order; at least one.</param>
    /// <param name="leafColumns">The schema-column indices stored at the leaves; the trie and leaf columns together cover the schema.</param>
    /// <returns>The built trie.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="trieColumns"/> is empty.</exception>
    public static GeneralizedHashTrie Build(
        IReadOnlyList<Variable> schema,
        IEnumerable<SolutionBatch> relation,
        int[] trieColumns,
        int[] leafColumns)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(relation);
        ArgumentNullException.ThrowIfNull(trieColumns);
        ArgumentNullException.ThrowIfNull(leafColumns);

        if(trieColumns.Length == 0)
        {
            throw new ArgumentException("A generalized hash trie needs at least one trie level.", nameof(trieColumns));
        }

        int trieDepth = trieColumns.Length;
        int leafStride = leafColumns.Length;
        int lastLevel = trieDepth - 1;

        List<OpenAddressedTable<int>> nodes = [new OpenAddressedTable<int>()];
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
                    OpenAddressedTable<int> table = nodes[node];

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

        return new GeneralizedHashTrie(schema, trieColumns, leafColumns, nodes, leaves);
    }

    /// <summary>
    /// Reconstructs the relation's rows as a <see cref="SolutionBatch"/> stream
    /// over <see cref="Schema"/> — the lossless inverse of <see cref="Build"/>.
    /// A recursion-free descent walks the trie depth-first through one
    /// enumerator per level, placing the trie path values and each leaf tuple
    /// into their schema columns.
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
        levels[0] = nodes[0].GetEnumerator();
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
                levels[level + 1] = nodes[child].GetEnumerator();
                level++;

                continue;
            }

            //Deepest level reached: emit one row per leaf tuple (or one row when
            //the leaf carries no columns — a full-depth trie's terminal).
            List<uint> leaf = leaves[child];
            int leafRows = leafStride == 0 ? 1 : leaf.Count / leafStride;

            for(int leafRow = 0; leafRow < leafRows; leafRow++)
            {
                for(int i = 0; i < trieDepth; i++)
                {
                    output.ColumnSpan(TrieColumns[i])[rows] = path[i];
                }

                for(int column = 0; column < leafStride; column++)
                {
                    output.ColumnSpan(LeafColumns[column])[rows] = leaf[(leafRow * leafStride) + column];
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
