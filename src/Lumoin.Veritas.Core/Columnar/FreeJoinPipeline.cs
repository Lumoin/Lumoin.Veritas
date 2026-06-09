using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        return FreeJoinExecutor.Execute(BuildRelations(index, query, variableOrder), variableOrder);
    }

    /// <summary>
    /// Runs the query through the Free Join generic join as a single-level
    /// <see cref="FactorizedBatch"/> when the shape is a star — every pattern
    /// sharing one variable and binding one further, distinct variable — or
    /// returns <see langword="null"/> when it is not that shape. The star key is
    /// bound first, so each relation is a trie over the key then a branch whose
    /// matches under a key form an independent branch; the result keeps each
    /// key's branches apart instead of materialising their flat product, and
    /// flattens to the same rows the flat <see cref="Run"/> yields.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The factorised star result, or <see langword="null"/> when the query is not a single-key star.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static FactorizedBatch? RunFactorized(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        if(query.Patterns.Count < 2 || !TryPlanStarOrder(index, query, out IReadOnlyList<Variable>? starOrder))
        {
            return null;
        }

        return FreeJoinExecutor.ExecuteFactorized(BuildRelations(index, query, starOrder), starOrder);
    }

    /// <summary>
    /// Detects a single-key star over the patterns' scan schemas and yields the
    /// variable order that binds its shared key first: the key is the one
    /// variable present in every pattern, and each pattern must bind the key and
    /// exactly one further variable, all distinct across patterns. Binding the
    /// key first makes each relation a trie over the key then a branch that the
    /// factorising executor consumes; a non-star shape has no such key and is
    /// declined.
    /// </summary>
    /// <param name="index">The columnar index, for each pattern's scan schema.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="starOrder">The key-first variable order on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the query is a single-key star.</returns>
    private static bool TryPlanStarOrder(ColumnarTripleIndex index, BasicGraphPattern query, [NotNullWhen(true)] out IReadOnlyList<Variable>? starOrder)
    {
        starOrder = null;

        List<Variable>[] schemas = new List<Variable>[query.Patterns.Count];
        for(int pattern = 0; pattern < query.Patterns.Count; pattern++)
        {
            schemas[pattern] = [.. ColumnarBatchScan.ScanSchemaOf(index, query.Patterns[pattern])];
        }

        //The star key is the one variable shared by every pattern; a count other
        //than one is not a single-key star.
        Variable key = default;
        int shared = 0;
        foreach(Variable candidate in schemas[0])
        {
            bool inAll = true;
            for(int pattern = 1; pattern < schemas.Length; pattern++)
            {
                if(!schemas[pattern].Contains(candidate))
                {
                    inAll = false;

                    break;
                }
            }

            if(inAll)
            {
                key = candidate;
                shared++;
            }
        }

        if(shared != 1)
        {
            return false;
        }

        //Each pattern binds the key and exactly one other variable, all distinct
        //across patterns: the star's independent branches.
        List<Variable> order = [key];
        HashSet<Variable> branches = [];
        foreach(List<Variable> schema in schemas)
        {
            if(schema.Count != 2)
            {
                return false;
            }

            Variable branch = schema[0] == key ? schema[1] : schema[0];
            if(branch == key || !branches.Add(branch))
            {
                return false;
            }

            order.Add(branch);
        }

        starOrder = order;

        return true;
    }

    /// <summary>
    /// Builds one full-depth <see cref="GeneralizedHashTrie"/> per pattern from
    /// the index: every scan column a trie level ordered by its variable's place
    /// in the global order, so the deepest descent follows the elimination order
    /// the executor requires.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="variableOrder">The global descent order.</param>
    /// <returns>The relations, parallel to the query's patterns.</returns>
    private static List<GeneralizedHashTrie> BuildRelations(ColumnarTripleIndex index, BasicGraphPattern query, IReadOnlyList<Variable> variableOrder)
    {
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

        return relations;
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
