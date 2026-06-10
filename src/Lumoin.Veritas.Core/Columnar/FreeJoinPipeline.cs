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
    /// Runs the query through the Free Join generic join as a
    /// <see cref="FactorizedBatch"/> when the shape is a star with optional
    /// chain extensions — centre patterns all sharing one key variable and each
    /// binding one further, distinct branch variable, plus extension patterns
    /// each binding one centre branch variable and one fresh variable (the
    /// chain step, nested under that branch) — or returns <see langword="null"/>
    /// when it is not that shape. The key is bound first, so each centre
    /// relation is a trie over the key then its branch and each extension a
    /// trie over its branch variable then its fresh variable; the result keeps
    /// each key's branches apart (an extended branch as a nested sub-batch)
    /// instead of materialising their flat product, and flattens to the same
    /// rows the flat <see cref="Run"/> yields.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="arena">The arena the result's key tuples, metadata, and branch values are allocated from; the caller owns its lifetime, and the result is valid until it is disposed.</param>
    /// <returns>The factorised result, or <see langword="null"/> when the query is not a star with chain extensions.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static FactorizedBatch? RunFactorized(ColumnarTripleIndex index, BasicGraphPattern query, FactorizedArena arena)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(arena);

        if(query.Patterns.Count < 2 || !TryPlanFactorizedOrder(index, query, out IReadOnlyList<Variable>? factorizedOrder))
        {
            return null;
        }

        return FreeJoinExecutor.ExecuteFactorized(BuildRelations(index, query, factorizedOrder), factorizedOrder, arena);
    }

    /// <summary>
    /// Detects a star with optional chain extensions over the patterns' scan
    /// schemas and yields the variable order that binds the centre key first,
    /// then the branch variables, then the extension variables. Candidate keys
    /// are tried in first-appearance order; for a key to hold, at least two
    /// patterns must bind it (the centres, each adding one distinct branch
    /// variable) and every remaining pattern must bind exactly one branch
    /// variable plus one fresh variable (an extension — fresh meaning not the
    /// key, not a branch, and not another extension's variable, the depth-two
    /// bound). A shape with no such key — a triangle, a join between two
    /// branches, a chain deeper than one extension hop from every candidate
    /// centre — is declined.
    /// </summary>
    /// <param name="index">The columnar index, for each pattern's scan schema.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="factorizedOrder">The key-first variable order on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the query is a star with chain extensions.</returns>
    private static bool TryPlanFactorizedOrder(ColumnarTripleIndex index, BasicGraphPattern query, [NotNullWhen(true)] out IReadOnlyList<Variable>? factorizedOrder)
    {
        factorizedOrder = null;

        List<Variable>[] schemas = new List<Variable>[query.Patterns.Count];
        for(int pattern = 0; pattern < query.Patterns.Count; pattern++)
        {
            schemas[pattern] = [.. ColumnarBatchScan.ScanSchemaOf(index, query.Patterns[pattern])];

            if(schemas[pattern].Count != 2)
            {
                return false;
            }
        }

        //Candidate keys in first-appearance order, so the chosen factorisation
        //is deterministic; any accepted key yields a correct grouping.
        List<Variable> candidates = [];
        HashSet<Variable> seen = [];
        foreach(List<Variable> schema in schemas)
        {
            foreach(Variable variable in schema)
            {
                if(seen.Add(variable))
                {
                    candidates.Add(variable);
                }
            }
        }

        foreach(Variable candidate in candidates)
        {
            if(TryOrderForKey(schemas, candidate, out factorizedOrder))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the schemas form a star with chain extensions centred on the
    /// given key, yielding the key-first variable order when they do: the
    /// centre patterns' branch variables in pattern order, then the extension
    /// patterns' fresh variables in pattern order.
    /// </summary>
    /// <param name="schemas">The patterns' two-variable scan schemas.</param>
    /// <param name="key">The candidate centre key.</param>
    /// <param name="factorizedOrder">The key-first variable order on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the shape holds for this key.</returns>
    private static bool TryOrderForKey(List<Variable>[] schemas, Variable key, [NotNullWhen(true)] out IReadOnlyList<Variable>? factorizedOrder)
    {
        factorizedOrder = null;

        //Centres: every schema binding the key contributes one distinct branch.
        List<Variable> branches = [];
        HashSet<Variable> branchSet = [];
        foreach(List<Variable> schema in schemas)
        {
            if(!schema.Contains(key))
            {
                continue;
            }

            Variable branch = schema[0] == key ? schema[1] : schema[0];
            if(branch == key || !branchSet.Add(branch))
            {
                return false;
            }

            branches.Add(branch);
        }

        if(branches.Count < 2)
        {
            return false;
        }

        //Extensions: every remaining schema binds exactly one branch variable
        //and one fresh variable — not the key, not a branch, not another
        //extension's variable.
        List<Variable> extensions = [];
        HashSet<Variable> extensionSet = [];
        foreach(List<Variable> schema in schemas)
        {
            if(schema.Contains(key))
            {
                continue;
            }

            bool firstIsBranch = branchSet.Contains(schema[0]);
            bool secondIsBranch = branchSet.Contains(schema[1]);
            if(firstIsBranch == secondIsBranch)
            {
                return false;
            }

            Variable fresh = firstIsBranch ? schema[1] : schema[0];
            if(fresh == key || !extensionSet.Add(fresh))
            {
                return false;
            }

            extensions.Add(fresh);
        }

        factorizedOrder = [key, .. branches, .. extensions];

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
