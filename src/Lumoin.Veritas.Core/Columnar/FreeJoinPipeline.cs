using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Runs a basic graph pattern through the Free Join generic join: it builds a
/// <see cref="GeneralizedHashTrie"/> per pattern from the columnar index, on a
/// single global variable order, and drives <see cref="FreeJoinExecutor"/> over
/// them. This is the columnar-index counterpart of the inline GHT construction
/// the Free Join tests do — the reusable builder a rendezvous route consumes to
/// reach the executor.
/// </summary>
/// <remarks>
/// <para>
/// This first cut builds every relation at <b>full depth</b> — every scan column
/// is a trie level, no leaf vectors — so the generic join reproduces the
/// worst-case-optimal join. The per-relation depth that makes Free Join interpolate
/// toward the binary hash join (leaf vectors on the acyclic cut) is a later
/// increment, chosen by the deferred selector; here the depth is fixed.
/// </para>
/// <para>
/// The variable order is the one the worst-case-optimal join descends
/// (<see cref="ColumnarRotationPlanner"/>); a rotation-incompatible query has no
/// such order and yields <see langword="null"/>, the same boundary the other
/// columnar engines draw. The result is the <see cref="SolutionBatch"/> stream,
/// answer-identical to leapfrog and the batched pipeline, so the conformance
/// corpus is the oracle once it is routed.
/// </para>
/// </remarks>
public static class FreeJoinPipeline
{
    /// <summary>
    /// Runs the query through the Free Join generic join over full-depth GHTs
    /// built from the index, or returns <see langword="null"/> when no global
    /// variable order exists for the index's order set (rotation-incompatible).
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The result batches over the global variable order, or <see langword="null"/> when the shape has no global order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<SolutionBatch>? Run(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index.OrderSetMode, query);
        if(variableOrder is null)
        {
            return null;
        }

        Dictionary<Variable, int> orderIndex = new(variableOrder.Count);
        for(int k = 0; k < variableOrder.Count; k++)
        {
            orderIndex[variableOrder[k]] = k;
        }

        List<GeneralizedHashTrie> relations = new(query.Patterns.Count);
        foreach(TriplePattern pattern in query.Patterns)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, pattern);
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, pattern), FullDepthTrieColumns(scanSchema, orderIndex), leafColumns: []));
        }

        return FreeJoinExecutor.Execute(relations, variableOrder);
    }

    /// <summary>
    /// The scan-schema column indices ordered by their variable's position in the
    /// global order — a full-depth trie (every column a level), so the deepest
    /// descent follows the global elimination order the executor requires.
    /// </summary>
    /// <param name="scanSchema">The pattern's scan schema, positional against its columns.</param>
    /// <param name="orderIndex">Each variable's position in the global order.</param>
    /// <returns>The column indices in global order.</returns>
    private static int[] FullDepthTrieColumns(IReadOnlyList<Variable> scanSchema, Dictionary<Variable, int> orderIndex)
    {
        int[] columns = new int[scanSchema.Count];
        for(int column = 0; column < columns.Length; column++)
        {
            columns[column] = column;
        }

        Array.Sort(columns, (left, right) => orderIndex[scanSchema[left]].CompareTo(orderIndex[scanSchema[right]]));

        return columns;
    }
}
