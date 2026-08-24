using System;
using System.Buffers;
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

        JoinShapeAnalysis.JoinShapeScan scan = JoinShapeAnalysis.BuildEdgeSets(query);

        //A pattern binding one variable at two of its own positions needs the
        //hypertrie driver's synthetic-key descent; the columnar path refuses it
        //upstream, and a direct caller is declined here.
        if(scan.HasSelfJoin)
        {
            return null;
        }

        List<TriplePattern> constraints = scan.Constraints;
        List<TriplePattern> scannable = scan.Scannable;
        List<HashSet<Variable>> edges = scan.Edges;

        if(scannable.Count == 0)
        {
            return new ColumnarBatchPlan(constraints, [], []);
        }

        if(!JoinShapeAnalysis.IsAcyclic(edges))
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
    /// <param name="arenaPool">The pool the factorised routes rent their query-scoped arena slabs from; the caller resolves it so every batched entry attributes rentals to one known pool instance.</param>
    /// <returns>The result batches.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<SolutionBatch> Run(ColumnarTripleIndex index, ColumnarBatchPlan plan, MemoryPool<uint> arenaPool)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(arenaPool);

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
            return RunFactorizedStar(index, plan, arenaPool);
        }

        if(plan.ChainNestVariable is not null)
        {
            return RunFactorizedChain(index, plan, plan.ChainNestVariable.Value, arenaPool);
        }

        return plan.JoinTree is null
            ? RunStreaming(index, plan)
            : RunReduced(index, plan, plan.JoinTree);
    }

    /// <summary>
    /// Counts the query's solutions WITHOUT flattening when the shape
    /// factorises — the first consumer that keeps the compressed form: the
    /// factorised build's flat-row count is the sum over groups of each
    /// group's product of branch sizes, read in O(stored tuples), where
    /// draining the stream pays O(result rows). Returns <see langword="null"/>
    /// when the shape does not factorise (or a build step refuses); the caller
    /// counts by draining instead. The count equals the drained row count
    /// exactly — the flatten the factorised routes end with is skipped, not
    /// approximated.
    /// </summary>
    /// <param name="index">The index to count on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="arenaPool">The pool the factorised build rents its query-scoped arena slabs from.</param>
    /// <returns>The solution count, or <see langword="null"/> when the shape does not factorise.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static long? TryCount(ColumnarTripleIndex index, BasicGraphPattern query, MemoryPool<uint> arenaPool)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(arenaPool);

        ColumnarBatchPlan? plan = TryPlan(index, query, useSemijoinReduction: false, useFactorizedStar: true, useFactorizedChain: true);
        if(plan is null || (plan.StarKey is null && plan.ChainNestVariable is null))
        {
            return null;
        }

        foreach(TriplePattern constraint in plan.Constraints)
        {
            if(!index.Contains(constraint.Subject.BoundTerm, constraint.Predicate.BoundTerm, constraint.Object.BoundTerm))
            {
                return 0;
            }
        }

        using FactorizedArena arena = new(arenaPool);

        FactorizedBatch? factorized = plan.StarKey is not null
            ? TryBuildFactorizedStar(index, plan, arena)
            : TryBuildFactorizedChain(index, plan, plan.ChainNestVariable!.Value, arena);

        return factorized?.FlatRowCount;
    }

    /// <summary>
    /// The distinct key projections of a factorisable star WITHOUT flattening —
    /// the late-materialisation consumer: a star's groups are keyed uniquely
    /// and every group stands for at least one solution, so projecting onto key
    /// variables needs one row per group (deduplicated only when the projection
    /// is a proper key subset), never the branch product. Returns
    /// <see langword="null"/> when the shape is not a factorisable star, a
    /// build step refuses, or the projection reaches outside the star key; the
    /// caller evaluates, projects, and deduplicates normally. The rows equal
    /// the drained-projected-deduplicated rows exactly, in group order.
    /// </summary>
    /// <param name="index">The index to answer on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="projection">The projected variables, distinct, all star-key variables.</param>
    /// <param name="arenaPool">The pool the factorised build rents its query-scoped arena slabs from.</param>
    /// <returns>The distinct projected rows as batches over <paramref name="projection"/>, or <see langword="null"/> when the fast path does not apply.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static List<SolutionBatch>? TryDistinctKeys(ColumnarTripleIndex index, BasicGraphPattern query, IReadOnlyList<Variable> projection, MemoryPool<uint> arenaPool)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(arenaPool);

        if(projection.Count == 0)
        {
            return null;
        }

        ColumnarBatchPlan? plan = TryPlan(index, query, useSemijoinReduction: false, useFactorizedStar: true, useFactorizedChain: true);
        if(plan is null || plan.StarKey is null)
        {
            return null;
        }

        foreach(TriplePattern constraint in plan.Constraints)
        {
            if(!index.Contains(constraint.Subject.BoundTerm, constraint.Predicate.BoundTerm, constraint.Object.BoundTerm))
            {
                return [];
            }
        }

        using FactorizedArena arena = new(arenaPool);

        FactorizedBatch? star = TryBuildFactorizedStar(index, plan, arena);
        if(star is null)
        {
            return null;
        }

        //Each projected variable must be a key variable; its key position maps
        //the group's key tuple onto the output columns. A duplicate or non-key
        //projection declines.
        int[] keyPosition = new int[projection.Count];
        for(int p = 0; p < projection.Count; p++)
        {
            keyPosition[p] = -1;
            for(int k = 0; k < star.KeyColumns.Length; k++)
            {
                if(star.Schema[star.KeyColumns[k]] == projection[p])
                {
                    keyPosition[p] = k;

                    break;
                }
            }

            if(keyPosition[p] < 0)
            {
                return null;
            }

            for(int earlier = 0; earlier < p; earlier++)
            {
                if(projection[earlier] == projection[p])
                {
                    return null;
                }
            }
        }

        //Group keys are unique by construction; a proper key subset dedups by
        //its packed projection.
        HashSet<JoinKey>? seen = projection.Count < star.KeyColumns.Length ? [] : null;

        List<SolutionBatch> output = [];
        SolutionBatch current = new(projection);
        int rows = 0;
        foreach(FactorizedGroup group in star.Groups)
        {
            if(seen is not null
                && !seen.Add(JoinKey.Pack(group.KeyValues[keyPosition[0]], projection.Count > 1 ? group.KeyValues[keyPosition[1]] : 0)))
            {
                continue;
            }

            for(int p = 0; p < projection.Count; p++)
            {
                current.ColumnSpan(p)[rows] = group.KeyValues[keyPosition[p]];
            }

            rows++;

            if(rows == SolutionBatch.BatchLength)
            {
                current.SetCount(rows);
                output.Add(current);
                current = new SolutionBatch(projection);
                rows = 0;
            }
        }

        if(rows > 0)
        {
            current.SetCount(rows);
            output.Add(current);
        }

        return output;
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
    /// a planned star. The factorised buffers live in one query-scoped arena
    /// that this iterator disposes when the consumer drains or abandons the
    /// stream — the single explicit lifetime of the route.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan, whose <see cref="ColumnarBatchPlan.StarKey"/> is set.</param>
    /// <param name="arenaPool">The pool the query-scoped arena rents its slabs from.</param>
    /// <returns>The result batches over <see cref="ColumnarBatchPlan.Schema"/>.</returns>
    private static IEnumerable<SolutionBatch> RunFactorizedStar(ColumnarTripleIndex index, ColumnarBatchPlan plan, MemoryPool<uint> arenaPool)
    {
        using FactorizedArena arena = new(arenaPool);

        FactorizedBatch? factorized = TryBuildFactorizedStar(index, plan, arena);
        foreach(SolutionBatch batch in factorized is null ? RunStreaming(index, plan) : factorized.Flatten())
        {
            yield return batch;
        }
    }

    /// <summary>Builds the star's factorised form — the first two patterns joined on the shared key, each further pattern attached as a branch — or <see langword="null"/> when a step refuses (the correctness backstop the plan's star detection makes unreachable on a planned star).</summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan, whose <see cref="ColumnarBatchPlan.StarKey"/> is set.</param>
    /// <param name="arena">The arena the factorised buffers are allocated from.</param>
    /// <returns>The factorised star, valid until <paramref name="arena"/> is disposed, or <see langword="null"/>.</returns>
    private static FactorizedBatch? TryBuildFactorizedStar(ColumnarTripleIndex index, ColumnarBatchPlan plan, FactorizedArena arena)
    {
        IReadOnlyList<Variable> schema0 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[0]);
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[1]);
        FactorizedBatch? factorized = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, plan.Order[0]), schema0,
            ColumnarBatchScan.Scan(index, plan.Order[1]), schema1,
            arena);

        for(int step = 2; step < plan.Order.Count && factorized is not null; step++)
        {
            IReadOnlyList<Variable> stepSchema = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[step]);
            factorized = FactorizedBatchJoin.AddBranch(
                factorized, ColumnarBatchScan.Scan(index, plan.Order[step]), stepSchema, arena);
        }

        return factorized;
    }

    /// <summary>
    /// The factorised chain: join the first two patterns into a product-of-
    /// unions, then nest the branch the third pattern joins on so the chain
    /// stays factorised across that branch-variable join. The final flatten
    /// expands the result. Answer-identical to the streamed left-deep join;
    /// the fall-back to the stream is a correctness backstop the plan's chain
    /// detection makes unreachable on a planned chain. The factorised buffers
    /// live in one query-scoped arena that this iterator disposes when the
    /// consumer drains or abandons the stream — the single explicit lifetime
    /// of the route.
    /// </summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan, whose <see cref="ColumnarBatchPlan.ChainNestVariable"/> is set.</param>
    /// <param name="nestVariable">The branch variable the third pattern joins on.</param>
    /// <param name="arenaPool">The pool the query-scoped arena rents its slabs from.</param>
    /// <returns>The result batches over <see cref="ColumnarBatchPlan.Schema"/>.</returns>
    private static IEnumerable<SolutionBatch> RunFactorizedChain(ColumnarTripleIndex index, ColumnarBatchPlan plan, Variable nestVariable, MemoryPool<uint> arenaPool)
    {
        using FactorizedArena arena = new(arenaPool);

        FactorizedBatch? nested = TryBuildFactorizedChain(index, plan, nestVariable, arena);
        foreach(SolutionBatch batch in nested is null ? RunStreaming(index, plan) : nested.Flatten())
        {
            yield return batch;
        }
    }

    /// <summary>Builds the chain's depth-2 factorised form — the first two patterns joined, the third nested under its branch variable — or <see langword="null"/> when the nesting refuses (the correctness backstop the plan's chain detection makes unreachable on a planned chain).</summary>
    /// <param name="index">The index the plan was made for.</param>
    /// <param name="plan">The plan, whose <see cref="ColumnarBatchPlan.ChainNestVariable"/> is set.</param>
    /// <param name="nestVariable">The branch variable the third pattern joins on.</param>
    /// <param name="arena">The arena the factorised buffers are allocated from.</param>
    /// <returns>The factorised chain, valid until <paramref name="arena"/> is disposed, or <see langword="null"/>.</returns>
    private static FactorizedBatch? TryBuildFactorizedChain(ColumnarTripleIndex index, ColumnarBatchPlan plan, Variable nestVariable, FactorizedArena arena)
    {
        IReadOnlyList<Variable> schema0 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[0]);
        IReadOnlyList<Variable> schema1 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[1]);
        FactorizedBatch firstTwo = FactorizedBatchJoin.Join(
            ColumnarBatchScan.Scan(index, plan.Order[0]), schema0,
            ColumnarBatchScan.Scan(index, plan.Order[1]), schema1,
            arena);

        IReadOnlyList<Variable> schema2 = ColumnarBatchScan.ScanSchemaOf(index, plan.Order[2]);

        return FactorizedBatchJoin.NestBranch(firstTwo, nestVariable, ColumnarBatchScan.Scan(index, plan.Order[2]), schema2, arena);
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
