using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Plans and runs the batched scan-and-hash pipeline for ACYCLIC
/// basic graph patterns — the binary-join half of the join hybrid.
/// <see cref="TryPlan"/> accepts a query only when the GYO
/// reduction proves its hypergraph acyclic AND a connected
/// left-deep order exists whose every join step shares one or two
/// variables; everything else stays on leapfrog, whose worst-case
/// optimality is exactly what cyclic shapes need.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why acyclicity gates the route.</b> On acyclic shapes,
/// scan-plus-hash beat the leapfrog descent in every probe this
/// project ran — sequential block-decoded scans and batch-granular
/// work replace per-tuple descents. On cyclic shapes binary joins
/// lose their worst-case bound (intermediate results can
/// quadratically exceed the output), so the router never sends
/// them here.
/// </para>
/// <para>
/// <b>Plan shape.</b> Fully-bound patterns become membership
/// constraints checked up front. The remaining patterns order
/// greedily: the most-bound pattern first, then any pattern
/// sharing 1–2 variables with the accumulated schema
/// (<see cref="SolutionBatchJoin.CanJoin"/>); a query that strands
/// a pattern — disconnected, or sharing all three variables — is
/// not pipelinable and reports as such.
/// </para>
/// </remarks>
public static class ColumnarBatchPipeline
{
    /// <summary>
    /// Plans the pipeline for <paramref name="query"/> on
    /// <paramref name="index"/>, or returns <see langword="null"/>
    /// when the query's shape belongs to leapfrog: cyclic, a
    /// per-pattern self-join, a disconnected (cartesian) component,
    /// or a join step outside the hash join's key width.
    /// </summary>
    /// <param name="index">The index the scans will run on; its materialised orders bound scanability.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="useSemijoinReduction">Whether to attach a join tree so <see cref="Run"/> reduces dangling tuples by semijoin before the join. Engaged only for three or more scan patterns, where an intermediate can exceed the output; two-pattern joins, whose single intermediate is the output, gain nothing and stay on the unreduced stream.</param>
    /// <param name="useFactorizedStar">Whether to route a star shape — three or more patterns all joining on one shared key — through the factorising join, keeping the intermediates product-of-unions until the final flatten instead of materialising each cross product. Takes precedence over semijoin reduction when the shape qualifies; non-star shapes are unaffected.</param>
    /// <param name="useFactorizedChain">Whether to route a three-pattern chain — the third pattern joining on a branch variable of the first join — through the join then the nesting step, keeping the chain factorised (a second level) across the branch-variable join. Takes precedence over semijoin reduction when the shape qualifies; non-chain shapes are unaffected.</param>
    /// <returns>The plan, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ColumnarBatchPlan? TryPlan(ColumnarTripleIndex index, BasicGraphPattern query, bool useSemijoinReduction = false, bool useFactorizedStar = false, bool useFactorizedChain = false)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);

        List<TriplePattern> constraints = [];
        List<TriplePattern> scannable = [];
        List<HashSet<Variable>> edges = [];

        foreach(TriplePattern pattern in query.Patterns)
        {
            if(pattern.HasSelfJoin())
            {
                return null;
            }

            HashSet<Variable> variables = [.. pattern.Variables()];

            if(variables.Count == 0)
            {
                constraints.Add(pattern);

                continue;
            }

            scannable.Add(pattern);
            edges.Add(variables);
        }

        if(scannable.Count == 0)
        {
            return new ColumnarBatchPlan(constraints, [], []);
        }

        if(!IsAcyclic(edges))
        {
            return null;
        }

        //Greedy connected left-deep order: most bound positions
        //first (the selectivity heuristic), then any pattern whose
        //shared-variable count with the accumulated schema fits the
        //hash join's key.
        bool[] placed = new bool[scannable.Count];
        List<TriplePattern> order = [];
        List<Variable> schema = [];

        int start = 0;
        for(int i = 1; i < scannable.Count; i++)
        {
            if(BoundCountOf(scannable[i]) > BoundCountOf(scannable[start]))
            {
                start = i;
            }
        }

        //Schema accumulation follows the SCAN schemas (permutation
        //tail order) so the plan's schema is byte-identical to the
        //column order the executed pipeline produces — a HashSet's
        //iteration order would not be.
        placed[start] = true;
        order.Add(scannable[start]);
        AppendNew(schema, ColumnarBatchScan.ScanSchemaOf(index, scannable[start]));

        for(int step = 1; step < scannable.Count; step++)
        {
            int next = -1;
            for(int i = 0; i < scannable.Count; i++)
            {
                if(placed[i])
                {
                    continue;
                }

                int shared = CountShared(schema, edges[i]);

                if(shared is >= 1 and <= SolutionBatchJoin.MaximumJoinVariables)
                {
                    next = i;

                    break;
                }
            }

            if(next < 0)
            {
                //Disconnected component, or every remaining pattern
                //shares more variables than the key packs.
                return null;
            }

            placed[next] = true;
            order.Add(scannable[next]);
            AppendNew(schema, ColumnarBatchScan.ScanSchemaOf(index, scannable[next]));
        }

        //A star — three or more patterns all joining on one shared key —
        //factorises across the whole chain: the key stays the group key and
        //each pattern attaches as a branch, so no cross product materialises
        //between joins. It takes precedence over semijoin reduction, which
        //bounds intermediates but still flattens them.
        IReadOnlyList<Variable>? starKey = useFactorizedStar ? TryStarKey(index, order) : null;

        //A three-pattern chain whose third pattern joins on a branch variable
        //of the first join factorises one level deeper: the join, then the
        //nesting step. Mutually exclusive with the star (the third pattern
        //shares the key there, a branch here), so it is checked only when the
        //shape is not a star.
        Variable? chainNestVariable = starKey is null && useFactorizedChain ? TryChainNestVariable(index, order) : null;

        //Yannakakis pays only when an intermediate can outgrow the
        //output — three or more relations. The tree is built over the
        //execution order so its indices address the materialised
        //relations directly; if it fails to build the run stays on the
        //unreduced stream, whose answers are identical.
        GyoJoinTree? joinTree = starKey is null && chainNestVariable is null && useSemijoinReduction && order.Count >= 3
            ? GyoJoinTree.TryBuild(EdgesOf(order))
            : null;

        return new ColumnarBatchPlan(constraints, order, schema, joinTree, starKey, chainNestVariable);
    }

    /// <summary>
    /// Runs a planned pipeline: constraints first (any miss yields
    /// nothing), then — when the plan carries a join tree — Yannakakis'
    /// semijoin reduction before the left-deep join, otherwise the
    /// unreduced left-deep stream. Batches flow over
    /// <see cref="ColumnarBatchPlan.Schema"/> either way; the results
    /// are identical, the reduced path only bounds the intermediates.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan from <see cref="TryPlan"/>.</param>
    /// <returns>The result batches.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<SolutionBatch> Run(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(plan);

        foreach(TriplePattern constraint in plan.Constraints)
        {
            if(!index.Contains(constraint.Subject.BoundTerm, constraint.Predicate.BoundTerm, constraint.Object.BoundTerm))
            {
                return [];
            }
        }

        if(plan.Order.Count == 0)
        {
            //Constraints only: the single empty solution.
            SolutionBatch empty = new(plan.Schema);
            empty.SetCount(1);

            return [empty];
        }

        if(plan.StarKey is not null)
        {
            return RunFactorizedStar(index, plan);
        }

        if(plan.ChainNestVariable is not null)
        {
            return RunFactorizedChain(index, plan, plan.ChainNestVariable.Value);
        }

        return plan.JoinTree is null
            ? RunStreaming(index, plan)
            : RunReduced(index, plan, plan.JoinTree);
    }

    /// <summary>
    /// The unreduced pipeline: scan the first pattern, then left-deep
    /// hash-join each subsequent scan. Intermediates stream but are not
    /// bounded — the path acyclic two-pattern joins and any shape
    /// without an attached join tree take.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The result batches.</returns>
    private static IEnumerable<SolutionBatch> RunStreaming(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        IEnumerable<SolutionBatch> stream = ColumnarBatchScan.Scan(index, plan.Order[0]);
        List<Variable> accumulated = [.. ColumnarBatchScan.ScanSchemaOf(index, plan.Order[0])];

        for(int step = 1; step < plan.Order.Count; step++)
        {
            IReadOnlyList<Variable> probeSchema = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[step]);
            stream = SolutionBatchJoin.HashJoin(stream, [.. accumulated], ColumnarBatchScan.Scan(index, plan.Order[step]), probeSchema);

            foreach(Variable variable in probeSchema)
            {
                if(!accumulated.Contains(variable))
                {
                    accumulated.Add(variable);
                }
            }
        }

        return stream;
    }

    /// <summary>
    /// Yannakakis on an acyclic shape: materialise every relation, run
    /// the two semijoin passes over the join tree, then left-deep join
    /// the reduced relations. After full reduction no relation holds a
    /// dangling tuple, so every intermediate of the connected join order
    /// extends to a full answer — bounding intermediates by the input
    /// and output sizes, the guarantee the streaming path lacks.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="tree">The join tree over <see cref="ColumnarBatchPlan.Order"/>.</param>
    /// <returns>The result batches.</returns>
    private static IEnumerable<SolutionBatch> RunReduced(ColumnarTripleIndex index, ColumnarBatchPlan plan, GyoJoinTree tree)
    {
        int count = plan.Order.Count;
        IReadOnlyList<Variable>[] scanSchemas = new IReadOnlyList<Variable>[count];
        IReadOnlyList<SolutionBatch>[] relations = new IReadOnlyList<SolutionBatch>[count];
        for(int i = 0; i < count; i++)
        {
            scanSchemas[i] = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[i]);
            relations[i] = [.. ColumnarBatchScan.Scan(index, plan.Order[i])];
        }

        //Upward pass: every child reduces its parent, children first, so a
        //parent is shrunk by all its subtrees before reducing its own parent.
        foreach(int node in tree.PostOrder)
        {
            int parent = tree.Parent[node];
            if(parent >= 0)
            {
                relations[parent] = SolutionBatchSemijoin.Reduce(relations[parent], scanSchemas[parent], relations[node], scanSchemas[node]);
            }
        }

        //Downward pass: every child is reduced by its now fully reduced
        //parent, parents first. After both passes the database is globally
        //consistent for the acyclic schema.
        for(int k = tree.PostOrder.Count - 1; k >= 0; k--)
        {
            int node = tree.PostOrder[k];
            int parent = tree.Parent[node];
            if(parent >= 0)
            {
                relations[node] = SolutionBatchSemijoin.Reduce(relations[node], scanSchemas[node], relations[parent], scanSchemas[parent]);
            }
        }

        IEnumerable<SolutionBatch> stream = relations[0];
        List<Variable> accumulated = [.. scanSchemas[0]];

        for(int step = 1; step < count; step++)
        {
            IReadOnlyList<Variable> probeSchema = scanSchemas[step];
            stream = SolutionBatchJoin.HashJoin(stream, [.. accumulated], relations[step], probeSchema);

            foreach(Variable variable in probeSchema)
            {
                if(!accumulated.Contains(variable))
                {
                    accumulated.Add(variable);
                }
            }
        }

        return stream;
    }

    /// <summary>
    /// The factorised star: join the first two patterns into a product-of-
    /// unions keyed on the shared variables, then attach each further pattern
    /// as a branch on that same key. No cross product materialises between
    /// joins; the final flatten expands the result the consumer drains. The
    /// answers are identical to the streamed left-deep join — the plan's star
    /// detection guarantees every step shares exactly the key, so the
    /// fall-back to the stream is only a correctness backstop, never taken on
    /// a planned star.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan, whose <see cref="ColumnarBatchPlan.StarKey"/> is set.</param>
    /// <returns>The result batches over <see cref="ColumnarBatchPlan.Schema"/>.</returns>
    private static IEnumerable<SolutionBatch> RunFactorizedStar(ColumnarTripleIndex index, ColumnarBatchPlan plan)
    {
        IReadOnlyList<Variable> schema0 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[0]);
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[1]);
        FactorizedBatch factorized = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, plan.Order[0]), schema0,
            ColumnarBatchScan.Scan(index, plan.Order[1]), schema1);

        for(int step = 2; step < plan.Order.Count; step++)
        {
            IReadOnlyList<Variable> stepSchema = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[step]);
            FactorizedBatch? extended = FactorizedBatchJoin.AddBranch(
                factorized, ColumnarBatchScan.Scan(index, plan.Order[step]), stepSchema);

            if(extended is null)
            {
                return RunStreaming(index, plan);
            }

            factorized = extended;
        }

        return factorized.Flatten();
    }

    /// <summary>
    /// The factorised chain: join the first two patterns into a product-of-
    /// unions, then nest the branch the third pattern joins on so the chain
    /// stays factorised across that branch-variable join. The final flatten
    /// expands the result. Answer-identical to the streamed left-deep join;
    /// the fall-back to the stream is a correctness backstop the plan's chain
    /// detection makes unreachable on a planned chain.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan, whose <see cref="ColumnarBatchPlan.ChainNestVariable"/> is set.</param>
    /// <param name="nestVariable">The branch variable the third pattern joins on.</param>
    /// <returns>The result batches over <see cref="ColumnarBatchPlan.Schema"/>.</returns>
    private static IEnumerable<SolutionBatch> RunFactorizedChain(ColumnarTripleIndex index, ColumnarBatchPlan plan, Variable nestVariable)
    {
        IReadOnlyList<Variable> schema0 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[0]);
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[1]);
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, plan.Order[0]), schema0,
            ColumnarBatchScan.Scan(index, plan.Order[1]), schema1);

        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[2]);
        FactorizedBatch? nested = FactorizedBatchJoin.NestBranch(firstTwo, nestVariable, ColumnarBatchScan.Scan(index, plan.Order[2]), schema2);

        return nested is null ? RunStreaming(index, plan) : nested.Flatten();
    }

    /// <summary>
    /// The branch variable to nest on when <paramref name="order"/> is a
    /// three-pattern factorisable chain, or <see langword="null"/>. A chain is
    /// exactly three patterns where the first two join on one or two variables
    /// and the third shares exactly one variable with their combined schema —
    /// a branch variable, not the join key — which is the nesting target. A
    /// third pattern sharing the key (a star), more than one variable, or none
    /// disqualifies the shape, which then takes the ordinary stream. Three
    /// patterns is the depth-2 ceiling: a fourth chain link would nest inside
    /// the already-nested branch, which <c>NestBranch</c> does not build.
    /// </summary>
    /// <param name="index">The index the scan schemas are read from.</param>
    /// <param name="order">The execution order from the greedy planner.</param>
    /// <returns>The nesting variable, or <see langword="null"/>.</returns>
    private static Variable? TryChainNestVariable(ColumnarTripleIndex index, List<TriplePattern> order)
    {
        if(order.Count != 3)
        {
            return null;
        }

        IReadOnlyList<Variable> schema0 = ColumnarBatchScan.ScanSchemaOf(index, order[0]);
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, order[1]);
        List<Variable> key = Intersection(schema0, schema1);

        if(key.Count is < 1 or > SolutionBatchJoin.MaximumJoinVariables)
        {
            return null;
        }

        List<Variable> joinSchema = [.. schema0];
        AppendNew(joinSchema, schema1);

        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, order[2]);
        List<Variable> shared = Intersection(schema2, joinSchema);

        //The third pattern must hang off exactly one variable, and that
        //variable must be a branch of the first join (not its key) so the
        //nesting has a single-column branch to target.
        if(shared.Count != 1 || ContainsVariable(key, shared[0]))
        {
            return null;
        }

        return shared[0];
    }

    /// <summary>
    /// The shared join key when <paramref name="order"/> is a factorisable
    /// star, or <see langword="null"/>. A star is three or more patterns
    /// where the first two share one or two variables and every later pattern
    /// shares exactly those same variables with the accumulated schema — so
    /// the key never moves into a branch and each pattern only adds new
    /// columns. Any pattern sharing a non-key (branch) variable, fewer than
    /// the full key, or a wider key disqualifies the shape, which then takes
    /// the ordinary stream.
    /// </summary>
    /// <param name="index">The index the scan schemas are read from.</param>
    /// <param name="order">The execution order from the greedy planner.</param>
    /// <returns>The star key, or <see langword="null"/>.</returns>
    private static List<Variable>? TryStarKey(ColumnarTripleIndex index, List<TriplePattern> order)
    {
        if(order.Count < 3)
        {
            return null;
        }

        IReadOnlyList<Variable> schema0 = ColumnarBatchScan.ScanSchemaOf(index, order[0]);
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, order[1]);
        List<Variable> key = Intersection(schema0, schema1);

        if(key.Count is < 1 or > SolutionBatchJoin.MaximumJoinVariables)
        {
            return null;
        }

        List<Variable> accumulated = [.. schema0];
        AppendNew(accumulated, schema1);

        for(int step = 2; step < order.Count; step++)
        {
            IReadOnlyList<Variable> stepSchema = ColumnarBatchScan.ScanSchemaOf(index, order[step]);
            List<Variable> shared = Intersection(stepSchema, accumulated);

            if(!SameSet(shared, key))
            {
                return null;
            }

            AppendNew(accumulated, stepSchema);
        }

        return key;
    }

    /// <summary>The variables of <paramref name="candidates"/> that also appear in <paramref name="filter"/>, in candidate order.</summary>
    /// <param name="candidates">The schema whose order is preserved.</param>
    /// <param name="filter">The schema membership is tested against.</param>
    /// <returns>The shared variables.</returns>
    private static List<Variable> Intersection(IReadOnlyList<Variable> candidates, IReadOnlyList<Variable> filter)
    {
        List<Variable> shared = [];
        foreach(Variable variable in candidates)
        {
            if(ContainsVariable(filter, variable))
            {
                shared.Add(variable);
            }
        }

        return shared;
    }

    /// <summary>Whether the schema holds the variable.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="variable">The variable.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsVariable(IReadOnlyList<Variable> schema, Variable variable)
    {
        for(int i = 0; i < schema.Count; i++)
        {
            if(schema[i] == variable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether two variable lists hold the same set — equal length and every member of one present in the other.</summary>
    /// <param name="left">The first list.</param>
    /// <param name="right">The second list.</param>
    /// <returns><see langword="true"/> when the sets are equal.</returns>
    private static bool SameSet(List<Variable> left, List<Variable> right)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        foreach(Variable variable in left)
        {
            if(!ContainsVariable(right, variable))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The GYO reduction: repeatedly drop variables occurring in
    /// exactly one edge and edges contained in another edge; the
    /// hypergraph is acyclic exactly when at most one edge
    /// survives.
    /// </summary>
    /// <param name="edgeSets">The patterns' variable sets; consumed (mutated) by the reduction.</param>
    /// <returns><see langword="true"/> when acyclic.</returns>
    private static bool IsAcyclic(List<HashSet<Variable>> edgeSets)
    {
        List<HashSet<Variable>> edges = [];
        foreach(HashSet<Variable> edge in edgeSets)
        {
            edges.Add([.. edge]);
        }

        bool changed = true;
        while(changed)
        {
            changed = false;

            //Variables in exactly one edge contribute nothing to
            //connectivity; drop them.
            Dictionary<Variable, int> occurrences = [];
            foreach(HashSet<Variable> edge in edges)
            {
                foreach(Variable variable in edge)
                {
                    occurrences[variable] = occurrences.TryGetValue(variable, out int count) ? count + 1 : 1;
                }
            }

            foreach(HashSet<Variable> edge in edges)
            {
                changed |= edge.RemoveWhere(variable => occurrences[variable] == 1) > 0;
            }

            //An edge contained in another is an ear; drop it.
            for(int i = edges.Count - 1; i >= 0; i--)
            {
                for(int j = 0; j < edges.Count; j++)
                {
                    if(i != j && edges[i].IsSubsetOf(edges[j]))
                    {
                        edges.RemoveAt(i);
                        changed = true;

                        break;
                    }
                }
            }
        }

        return edges.Count <= 1;
    }

    /// <summary>The patterns' variable sets, in the given order — the join-tree builder's input.</summary>
    /// <param name="patterns">The ordered patterns.</param>
    /// <returns>One variable set per pattern.</returns>
    private static List<IReadOnlyCollection<Variable>> EdgesOf(List<TriplePattern> patterns)
    {
        List<IReadOnlyCollection<Variable>> edges = new(patterns.Count);
        foreach(TriplePattern pattern in patterns)
        {
            edges.Add(new HashSet<Variable>(pattern.Variables()));
        }

        return edges;
    }

    /// <summary>The number of bound positions in the pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The bound count.</returns>
    private static int BoundCountOf(TriplePattern pattern)
    {
        int bound = 0;
        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            if(pattern.At(rdfPosition).IsBound)
            {
                bound++;
            }
        }

        return bound;
    }

    /// <summary>The number of the edge's variables already in the schema.</summary>
    /// <param name="schema">The accumulated schema.</param>
    /// <param name="edge">The pattern's variables.</param>
    /// <returns>The shared count.</returns>
    private static int CountShared(List<Variable> schema, HashSet<Variable> edge)
    {
        int shared = 0;
        foreach(Variable variable in schema)
        {
            if(edge.Contains(variable))
            {
                shared++;
            }
        }

        return shared;
    }

    /// <summary>Appends the scan schema's variables not yet in the accumulated schema, preserving both orders — exactly the hash join's output-schema construction.</summary>
    /// <param name="schema">The accumulated schema.</param>
    /// <param name="scanSchema">The pattern's scan schema.</param>
    private static void AppendNew(List<Variable> schema, IReadOnlyList<Variable> scanSchema)
    {
        foreach(Variable variable in scanSchema)
        {
            if(!schema.Contains(variable))
            {
                schema.Add(variable);
            }
        }
    }
}
