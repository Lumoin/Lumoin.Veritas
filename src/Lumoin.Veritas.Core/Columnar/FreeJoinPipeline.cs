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
/// A relation's <b>join-cover depth</b> extends its trie levels through its last
/// join variable (one another pattern also binds) in the global order, and the
/// private tail columns sit as leaf vectors. A cyclic core therefore descends every
/// level — the worst-case-optimal join — while a star's satellite relations carry
/// one trie level and a leaf vector, the binary-hash-join shape; the generic join
/// interpolates between the two on mixed shapes. The flat route builds each
/// relation at the depth its own key fan-out justifies: the join-cover depth by
/// default, extended through the private tail where one key value concentrates
/// enough matches to pay for hashing it, so one run may carry a mix of depths. The
/// factorised route keeps join-cover depths unconditionally. Trie levels are always
/// a global-order prefix of the
/// relation's columns, the contract <see cref="FreeJoinExecutor"/> descends by.
/// Sub-cover depths (join variables met in leaf vectors) are supported by the
/// executor on both its flat and its factorised path but chosen by no route
/// here; engaging them per shape is the deferred cost-based selector's
/// territory.
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
    /// Runs the query through the Free Join generic join over GHTs built from
    /// the index at the depths this route plans — each relation's join-cover
    /// depth, extended through its private tail where its own key fan-out
    /// justifies it — or returns <see langword="null"/> when no global variable
    /// order exists for the index's order set (rotation-incompatible).
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager is the measured default.</param>
    /// <returns>The result batches over the global variable order, or <see langword="null"/> when the shape has no global order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<SolutionBatch>? Run(ColumnarTripleIndex index, BasicGraphPattern query, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        FreeJoinPlan? plan = TryPlan(index, query, FreeJoinDepthPolicy.Unspecified);

        return plan is null ? null : Run(index, plan, trieBuild);
    }

    /// <summary>
    /// Plans one flat Free Join run: the global descent order, each relation's scan schema, columns, and
    /// trie depth, and the summary values a plan-applied trace event reads. Returns
    /// <see langword="null"/> when no global variable order exists for the index's order set
    /// (rotation-incompatible) — the same boundary the other columnar engines draw, taken by value.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="depthOverride">The depth every relation builds at: <see cref="FreeJoinDepthPolicy.Unspecified"/> leaves the engine's per-relation rule to decide, <see cref="FreeJoinDepthPolicy.Cover"/> holds every relation at its join-cover depth, and <see cref="FreeJoinDepthPolicy.Full"/> extends every relation through its private tail.</param>
    /// <returns>The plan, or <see langword="null"/> when the shape has no global order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    internal static FreeJoinPlan? TryPlan(ColumnarTripleIndex index, BasicGraphPattern query, FreeJoinDepthPolicy depthOverride)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index.OrderSetMode, query);
        if(variableOrder is null)
        {
            return null;
        }

        FreeJoinRelationPlan[] relations = PlanCoverRelations(index, query, variableOrder, JoinVariablesOf(index, query));

        //The tail-bearing count is the cover baseline's own reading, taken before any extension moves a
        //depth, so the summary and the depth rule's guard read one definition at one moment.
        int plannedTailBearing = TailBearingRelationCount(relations);

        if(depthOverride == FreeJoinDepthPolicy.Unspecified)
        {
            ExtendEngagedRelations(index, query, relations);
        }
        else if(depthOverride == FreeJoinDepthPolicy.Full)
        {
            ExtendEveryRelation(relations);
        }

        int fullDepthCount = 0;
        long fullDepthMask = 0;
        for(int relation = 0; relation < relations.Length; relation++)
        {
            if(relations[relation].Depth != relations[relation].Columns.Length)
            {
                continue;
            }

            fullDepthCount++;

            //A pattern count has no cap, so the mask saturates while the counts stay exact.
            if(relation < 64)
            {
                fullDepthMask |= 1L << relation;
            }
        }

        return new FreeJoinPlan(query.Patterns, variableOrder, relations, relations.Length, fullDepthCount, plannedTailBearing, fullDepthMask);
    }

    /// <summary>
    /// Drives the generic join over the relations a plan names, built from the index at the plan's depths.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="plan">The plan <see cref="TryPlan"/> produced for this index.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; the build mode never changes the plan.</param>
    /// <returns>The result batches over the plan's global variable order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    internal static IEnumerable<SolutionBatch> Run(ColumnarTripleIndex index, FreeJoinPlan plan, FreeJoinTrieBuild trieBuild)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(plan);

        return FreeJoinExecutor.Execute(BuildRelations(index, plan.Patterns, plan.Relations, trieBuild), plan.Order);
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
    /// rows the flat <see cref="Run"/> yields. This route builds its relations
    /// at their join-cover depths: every variable the
    /// grouping nests by is a join variable and so stays a trie level, which
    /// makes a centre full-depth exactly when it is extended and leaves only
    /// the terminal emitted values — a non-extended centre's branch and an
    /// extension's fresh variable — in leaf vectors.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="arena">The arena the result's key tuples, metadata, and branch values are allocated from; the caller owns its lifetime, and the result is valid until it is disposed.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager is the measured default.</param>
    /// <returns>The factorised result, or <see langword="null"/> when the query is not a star with chain extensions.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static FactorizedBatch? RunFactorized(ColumnarTripleIndex index, BasicGraphPattern query, FactorizedArena arena, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(arena);

        if(query.Patterns.Count < 2 || !TryPlanFactorizedOrder(index, query, out IReadOnlyList<Variable>? factorizedOrder))
        {
            return null;
        }

        FreeJoinRelationPlan[] plans = PlanRelations(index, query, factorizedOrder, JoinVariablesOf(index, query), FreeJoinDepthRule.JoinCover);

        return FreeJoinExecutor.ExecuteFactorized(BuildRelations(index, query.Patterns, plans, trieBuild), factorizedOrder, arena);
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
    internal static bool TryPlanFactorizedOrder(ColumnarTripleIndex index, BasicGraphPattern query, [NotNullWhen(true)] out IReadOnlyList<Variable>? factorizedOrder)
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
    /// What a connected run's heaviest cover-key value, multiplied by the run's tail-bearing relation
    /// count, must reach before a relation's private tail is hashed into trie levels instead of read as
    /// leaf columns. Inclusive: a product exactly at this value engages. The measured crossover, read off
    /// the join-route census's depth ladder on the eager build; below it the join-cover depth is the
    /// measured winner, and the parity band sits just under the boundary. The product, rather than the fan
    /// alone, is what the measurement separates: the per-visit narrowing a leaf level pays is multiplied by
    /// every leaf level the descent binds before it, so one relation's skew and the number of relations
    /// that multiply it are one signal.
    /// </summary>
    internal static int FullDepthEngageFanTailProduct => 12;

    /// <summary>
    /// What a CONNECTED run's single tail-bearing relation's degree-weighted mean key fan must reach before
    /// its private tail is hashed. A lone tail multiplies nothing, so the crossover arrives far later than
    /// the product boundary and the signal is the weighted mean rather than the maximum: a hub beside a
    /// long flat tail concentrates no work, and the measured record keeps such shapes at cover.
    /// </summary>
    internal static double SingleTailEngageWeightedFan => 64;

    /// <summary>
    /// What a DISCONNECTED run's tail-bearing relation's degree-weighted mean key fan must reach before its
    /// private tail is hashed. An order of magnitude below the connected single-tail bar, because the
    /// cartesian drive re-enumerates a component once per partner-component row: the per-key retrieval cost
    /// is amplified by the partner's answer size, so the crossover arrives at far lower skew.
    /// </summary>
    internal static double DisconnectedEngageWeightedFan => 8;

    /// <summary>
    /// Plans each relation's trie depth for one run over one global order: every relation's scan schema and
    /// its columns in that order, its join-cover depth, and — under
    /// <see cref="FreeJoinDepthRule.FanOutEngaged"/> — the extension of a tail-bearing relation to full
    /// depth where the fitted rule engages it. A statistic the view does not expose keeps the join-cover
    /// depth, and a relation whose cover already spans every column is untouched by the arithmetic. Depth
    /// choices never affect answers, only shape.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="variableOrder">The global descent order.</param>
    /// <param name="joinVariables">The variables bound by more than one pattern.</param>
    /// <param name="depthRule">The depth rule this run plans under.</param>
    /// <returns>The plans, parallel to the query's patterns.</returns>
    internal static FreeJoinRelationPlan[] PlanRelations(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        IReadOnlyList<Variable> variableOrder,
        HashSet<Variable> joinVariables,
        FreeJoinDepthRule depthRule)
    {
        FreeJoinRelationPlan[] plans = PlanCoverRelations(index, query, variableOrder, joinVariables);

        if(depthRule == FreeJoinDepthRule.FanOutEngaged)
        {
            ExtendEngagedRelations(index, query, plans);
        }

        return plans;
    }

    /// <summary>
    /// Plans every relation at its join-cover depth over one global order: the scan schema, the schema's
    /// column indices in that order, and the depth the cover reaches. The baseline every depth rule and
    /// every override starts from.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="variableOrder">The global descent order.</param>
    /// <param name="joinVariables">The variables bound by more than one pattern.</param>
    /// <returns>The cover-depth plans, parallel to the query's patterns.</returns>
    private static FreeJoinRelationPlan[] PlanCoverRelations(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        IReadOnlyList<Variable> variableOrder,
        HashSet<Variable> joinVariables)
    {
        Dictionary<Variable, int> orderIndex = new(variableOrder.Count);
        for(int k = 0; k < variableOrder.Count; k++)
        {
            orderIndex[variableOrder[k]] = k;
        }

        FreeJoinRelationPlan[] plans = new FreeJoinRelationPlan[query.Patterns.Count];
        for(int pattern = 0; pattern < query.Patterns.Count; pattern++)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, query.Patterns[pattern]);
            int[] columns = OrderedColumns(scanSchema, orderIndex);
            int depth = JoinCoverDepth(scanSchema, columns, joinVariables);
            plans[pattern] = new FreeJoinRelationPlan(scanSchema, columns, depth);
        }

        return plans;
    }

    /// <summary>
    /// Extends through their private tails the cover-depth relations the fitted rule engages, leaving every
    /// other relation at the depth it has. The run's shape is read once — its tail-bearing relation count
    /// through the one counter the shape features also read, over the cover plan before any extension, and
    /// its connected-component count through the one shape analysis the route seam reads — and each
    /// tail-bearing relation is then decided on its own statistics.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="plans">The cover-depth plans, extended in place.</param>
    private static void ExtendEngagedRelations(ColumnarTripleIndex index, BasicGraphPattern query, FreeJoinRelationPlan[] plans)
    {
        int tailBearingRelationCount = TailBearingRelationCount(plans);

        //A run whose every relation's cover already spans its columns has no tail to hash; a cyclic core is
        //the shape that lands here, and it never reads a statistic.
        if(tailBearingRelationCount == 0)
        {
            return;
        }

        int componentCount = JoinShapeAnalysis.ComponentCount(JoinShapeAnalysis.BuildEdgeSets(query).Edges);

        for(int pattern = 0; pattern < plans.Length; pattern++)
        {
            FreeJoinRelationPlan plan = plans[pattern];

            //A relation whose cover already spans every column has no private tail.
            if(plan.Depth == plan.Columns.Length)
            {
                continue;
            }

            //The deepest trie level's variable is the key the executor narrows the leaf by.
            Variable key = plan.ScanSchema[plan.Columns[plan.Depth - 1]];
            if(ShouldExtendThroughTail(index, query.Patterns[pattern], key, componentCount, tailBearingRelationCount))
            {
                plans[pattern] = plan with { Depth = plan.Columns.Length };
            }
        }
    }

    /// <summary>
    /// Extends every relation through its private tail, whatever its statistics say — the explicit
    /// full-depth override, which decides no shape and reads no statistic.
    /// </summary>
    /// <param name="plans">The plans, extended in place.</param>
    private static void ExtendEveryRelation(FreeJoinRelationPlan[] plans)
    {
        for(int pattern = 0; pattern < plans.Length; pattern++)
        {
            plans[pattern] = plans[pattern] with { Depth = plans[pattern].Columns.Length };
        }
    }

    /// <summary>
    /// Whether one tail-bearing relation's own statistics justify hashing its private tail, on the branch
    /// its run's shape selects: a disconnected run reads the degree-weighted mean against
    /// <see cref="DisconnectedEngageWeightedFan"/>; a connected run of two or more tail-bearing relations
    /// reads the heaviest key value against <see cref="FullDepthEngageFanTailProduct"/> divided across the
    /// tail count; a connected run of one reads the degree-weighted mean against
    /// <see cref="SingleTailEngageWeightedFan"/>. A statistic the view does not expose keeps the cover depth.
    /// </summary>
    /// <param name="index">The columnar index the relation scans.</param>
    /// <param name="pattern">The relation's pattern.</param>
    /// <param name="key">The relation's cover key — the deepest trie level's variable.</param>
    /// <param name="componentCount">The run's connected-component count over shared variables.</param>
    /// <param name="tailBearingRelationCount">The run's tail-bearing relation count over the cover plan.</param>
    /// <returns><see langword="true"/> when the relation extends through its private tail.</returns>
    private static bool ShouldExtendThroughTail(ColumnarTripleIndex index, TriplePattern pattern, Variable key, int componentCount, int tailBearingRelationCount)
    {
        if(componentCount >= 2)
        {
            return ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, pattern, key, out double disconnectedFan)
                && disconnectedFan >= DisconnectedEngageWeightedFan;
        }

        if(tailBearingRelationCount >= 2)
        {
            return ColumnarKeyStatistics.TryEstimateMaximumKeyFanOut(index, pattern, key, out int maximum)
                && maximum * tailBearingRelationCount >= FullDepthEngageFanTailProduct;
        }

        return ColumnarKeyStatistics.TryEstimateWeightedMeanKeyFanOut(index, pattern, key, out double singleTailFan)
            && singleTailFan >= SingleTailEngageWeightedFan;
    }

    /// <summary>
    /// How many of the planned relations carry a private tail — trie levels short of the relation's column
    /// count, so the columns past the cover sit as leaf vectors. The one definition of what a tail-bearing
    /// relation is: the depth rule's guard, the plan summary, and the shape features all read this, over a
    /// join-cover plan, so they cannot disagree. A plan built under
    /// <see cref="FreeJoinDepthRule.FanOutEngaged"/> counts the relations that did not engage, which is why
    /// every caller counts the join-cover plan.
    /// </summary>
    /// <param name="plans">The per-relation plans, parallel to a query's patterns.</param>
    /// <returns>The number of plans whose trie depth is short of their column count.</returns>
    internal static int TailBearingRelationCount(FreeJoinRelationPlan[] plans)
    {
        int tailBearing = 0;
        for(int pattern = 0; pattern < plans.Length; pattern++)
        {
            if(plans[pattern].Depth < plans[pattern].Columns.Length)
            {
                tailBearing++;
            }
        }

        return tailBearing;
    }

    /// <summary>
    /// Builds one <see cref="GeneralizedHashTrie"/> per pattern from the index at the depths
    /// <see cref="PlanRelations"/> planned: trie levels over the plan's leading columns, the rest as leaf
    /// columns.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="patterns">The patterns the relations scan, parallel to <paramref name="plans"/>.</param>
    /// <param name="plans">The per-relation plans, parallel to <paramref name="patterns"/>.</param>
    /// <param name="trieBuild">How each trie materialises its maps.</param>
    /// <returns>The relations, parallel to the patterns.</returns>
    private static List<GeneralizedHashTrie> BuildRelations(ColumnarTripleIndex index, IReadOnlyList<TriplePattern> patterns, FreeJoinRelationPlan[] plans, FreeJoinTrieBuild trieBuild)
    {
        List<GeneralizedHashTrie> relations = new(plans.Length);
        for(int pattern = 0; pattern < plans.Length; pattern++)
        {
            FreeJoinRelationPlan plan = plans[pattern];
            relations.Add(GeneralizedHashTrie.Build(plan.ScanSchema, ColumnarBatchScan.Scan(index, patterns[pattern]), plan.Columns[..plan.Depth], plan.Columns[plan.Depth..], trieBuild));
        }

        return relations;
    }

    /// <summary>
    /// The query's join variables — those bound by more than one pattern — read
    /// off the patterns' scan schemas, the same surfaces the relations build
    /// from. A variable a single pattern binds twice also lands here, which
    /// keeps its levels deep; depth choices never affect answers, only shape.
    /// </summary>
    /// <param name="index">The columnar index, for each pattern's scan schema.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The join-variable set.</returns>
    internal static HashSet<Variable> JoinVariablesOf(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        HashSet<Variable> seen = [];
        HashSet<Variable> joins = [];
        foreach(TriplePattern pattern in query.Patterns)
        {
            foreach(Variable variable in ColumnarBatchScan.ScanSchemaOf(index, pattern))
            {
                if(!seen.Add(variable))
                {
                    joins.Add(variable);
                }
            }
        }

        return joins;
    }

    /// <summary>
    /// The join-cover depth of one relation: trie levels extend through the last
    /// of its global-order columns whose variable is a join variable, and at
    /// least one level always remains, so a relation descends before its leaves
    /// are read. The columns past the cover sit as leaf vectors, and no join
    /// variable ever lands in one — trie levels stay a global-order prefix, the
    /// contract <see cref="FreeJoinExecutor"/> descends by.
    /// </summary>
    /// <param name="scanSchema">The pattern's scan schema, positional against its columns.</param>
    /// <param name="orderedColumns">The schema's column indices in global order.</param>
    /// <param name="joinVariables">The variables bound by more than one pattern.</param>
    /// <returns>The trie depth: zero for an empty schema, otherwise between one and the column count.</returns>
    internal static int JoinCoverDepth(IReadOnlyList<Variable> scanSchema, int[] orderedColumns, HashSet<Variable> joinVariables)
    {
        if(orderedColumns.Length == 0)
        {
            return 0;
        }

        int depth = 1;
        for(int position = 0; position < orderedColumns.Length; position++)
        {
            if(joinVariables.Contains(scanSchema[orderedColumns[position]]))
            {
                depth = position + 1;
            }
        }

        return depth;
    }

    /// <summary>
    /// The scan-schema column indices ordered by their variable's position in
    /// the global order, so a trie built over any prefix follows the global
    /// elimination order the executor requires.
    /// </summary>
    /// <param name="scanSchema">The pattern's scan schema, positional against its columns.</param>
    /// <param name="orderIndex">Each variable's position in the global order.</param>
    /// <returns>The column indices in global order.</returns>
    internal static int[] OrderedColumns(IReadOnlyList<Variable> scanSchema, Dictionary<Variable, int> orderIndex)
    {
        int[] columns = new int[scanSchema.Count];
        for(int column = 0; column < columns.Length; column++)
        {
            columns[column] = column;
        }

        Array.Sort(columns, new GlobalOrderColumnComparer(scanSchema, orderIndex));

        return columns;
    }

    /// <summary>
    /// Orders scan-schema column indices by their variable's position in the global order,
    /// carrying the schema and order map as explicit state so the comparison closes over no
    /// enclosing local.
    /// </summary>
    /// <param name="scanSchema">The pattern's scan schema, positional against its columns.</param>
    /// <param name="orderIndex">Each variable's position in the global order.</param>
    private sealed class GlobalOrderColumnComparer(IReadOnlyList<Variable> scanSchema, Dictionary<Variable, int> orderIndex) : IComparer<int>
    {
        /// <summary>The pattern's scan schema, positional against its columns.</summary>
        private IReadOnlyList<Variable> ScanSchema { get; } = scanSchema;

        /// <summary>Each variable's position in the global order.</summary>
        private Dictionary<Variable, int> OrderIndex { get; } = orderIndex;

        /// <summary>Compares two column indices by their variable's global-order position.</summary>
        /// <param name="left">The first column index.</param>
        /// <param name="right">The second column index.</param>
        /// <returns>The sign of the two columns' global-order positions compared.</returns>
        public int Compare(int left, int right)
        {
            return OrderIndex[ScanSchema[left]].CompareTo(OrderIndex[ScanSchema[right]]);
        }
    }
}
