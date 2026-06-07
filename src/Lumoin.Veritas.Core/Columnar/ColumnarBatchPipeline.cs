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
    /// <returns>The plan, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ColumnarBatchPlan? TryPlan(ColumnarTripleIndex index, BasicGraphPattern query)
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

        return new ColumnarBatchPlan(constraints, order, schema);
    }

    /// <summary>
    /// Runs a planned pipeline: constraints first (any miss yields
    /// nothing), then the scans and left-deep hash joins. Batches
    /// stream over <see cref="ColumnarBatchPlan.Schema"/>.
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
