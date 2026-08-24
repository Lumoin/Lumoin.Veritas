using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The plan-time shape facts a join-route decision is taken on: the edge-set build over the query's
/// variable-bearing patterns, the GYO acyclicity reduction, and the connected-component count. The edge
/// sets and the reduction each have exactly one definition here, read by both the batched pipeline's own
/// gate and the join-route selector seam, so the two cannot drift apart on what an edge is or what
/// "acyclic" means. What is NOT shared is stated plainly: the per-pattern self-join exclusion is the
/// batched route's own capability rule (and the columnar path's upstream gate), not a shape fact — a
/// self-joining pattern never reaches the seam at all (<see cref="QueryEngineRendezvous.IsColumnarCapable"/>).
/// </summary>
internal static class JoinShapeAnalysis
{
    /// <summary>The features for one query on one view under one policy.</summary>
    /// <param name="view">The columnar view the chosen route would run on.</param>
    /// <param name="query">The basic graph pattern under evaluation.</param>
    /// <param name="policy">The policy governing which routes exist for this query.</param>
    /// <returns>The shape features.</returns>
    internal static JoinSelectionFeatures Describe(ColumnarTripleIndex view, BasicGraphPattern query, in QueryEnginePolicy policy)
    {
        JoinShapeScan scan = BuildEdgeSets(query);
        ReadKeyFanOuts(view, query, out int maximumKeyFanOut, out double degreeWeightedMeanFanOut);

        return new JoinSelectionFeatures(
            PatternCount: query.Patterns.Count,
            ViewTripleCount: view.TripleCount,
            Acyclic: IsAcyclic(scan.Edges),
            ComponentCount: ComponentCount(scan.Edges),
            OrderSetMode: view.OrderSetMode,
            BatchedRouteEligible: policy.PreferBatchedForAcyclic,
            MaximumKeyFanOut: maximumKeyFanOut,
            TailBearingRelationCount: TailBearingRelationCountOf(view, query),
            DegreeWeightedMeanFanOut: degreeWeightedMeanFanOut);
    }

    /// <summary>
    /// The two per-key group statistics one join-route decision is taken on, read in one pass over the
    /// query's patterns: the largest matches one join-key value carries, and the heaviest degree-weighted
    /// mean, each aggregated as the maximum over every pattern and join variable whose statistic the view
    /// exposes, and each at its own unreadable value when the view exposes none. One loop over one readable
    /// set, so the two features cannot be measured over different patterns. Join variables are the ones
    /// <see cref="FreeJoinPipeline.JoinVariablesOf"/> names, so the features and the depth rule read one
    /// definition of what a join key is.
    /// </summary>
    /// <param name="view">The columnar view the statistics are read off.</param>
    /// <param name="query">The basic graph pattern under evaluation.</param>
    /// <param name="maximumKeyFanOut">The heaviest readable join-key fan-out, or the unreadable value.</param>
    /// <param name="degreeWeightedMeanFanOut">The heaviest readable join-key degree-weighted mean, or the unreadable value.</param>
    private static void ReadKeyFanOuts(ColumnarTripleIndex view, BasicGraphPattern query, out int maximumKeyFanOut, out double degreeWeightedMeanFanOut)
    {
        HashSet<Variable> joinVariables = FreeJoinPipeline.JoinVariablesOf(view, query);
        maximumKeyFanOut = JoinSelectionFeatures.UnreadableKeyFanOut;
        degreeWeightedMeanFanOut = JoinSelectionFeatures.UnreadableWeightedFanOut;

        foreach(TriplePattern pattern in query.Patterns)
        {
            foreach(Variable variable in ColumnarBatchScan.ScanSchemaOf(view, pattern))
            {
                if(!joinVariables.Contains(variable)
                    || !ColumnarKeyStatistics.TryReadKeyGroupFanOut(view, pattern, variable, out int fanOut, out double weightedMean))
                {
                    continue;
                }

                if(fanOut > maximumKeyFanOut)
                {
                    maximumKeyFanOut = fanOut;
                }

                if(weightedMean > degreeWeightedMeanFanOut)
                {
                    degreeWeightedMeanFanOut = weightedMean;
                }
            }
        }
    }

    /// <summary>
    /// How many relations a join-cover build leaves a private tail on, over the global descent order the Free
    /// Join route would use: the join-cover plan's own count, read through
    /// <see cref="FreeJoinPipeline.TailBearingRelationCount"/> so the feature and the depth rule's guard
    /// cannot disagree. <see cref="JoinSelectionFeatures.UnplannedTailBearingRelationCount"/> when the view
    /// materialises no global order for the shape.
    /// </summary>
    /// <param name="view">The columnar view the route would descend.</param>
    /// <param name="query">The basic graph pattern under evaluation.</param>
    /// <returns>The join-cover plan's tail-bearing relation count, or the unplanned value.</returns>
    private static int TailBearingRelationCountOf(ColumnarTripleIndex view, BasicGraphPattern query)
    {
        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(view.OrderSetMode, query);
        if(variableOrder is null)
        {
            return JoinSelectionFeatures.UnplannedTailBearingRelationCount;
        }

        FreeJoinRelationPlan[] plans = FreeJoinPipeline.PlanRelations(view, query, variableOrder, FreeJoinPipeline.JoinVariablesOf(view, query), FreeJoinDepthRule.JoinCover);

        return FreeJoinPipeline.TailBearingRelationCount(plans);
    }

    /// <summary>
    /// One scan of the query's patterns producing the shape inputs both readers consume: the edge sets
    /// (one set of variables per variable-bearing pattern; a fully bound pattern contributes no edge),
    /// the scannable/constraint split, and the per-pattern self-join verdict. The pipeline's planner
    /// consumes all of it; <see cref="Describe"/> consumes the edge sets.
    /// </summary>
    /// <param name="query">The basic graph pattern to scan.</param>
    /// <returns>The scan.</returns>
    internal static JoinShapeScan BuildEdgeSets(BasicGraphPattern query)
    {
        List<TriplePattern> constraints = [];
        List<TriplePattern> scannable = [];
        List<HashSet<Variable>> edges = [];
        bool hasSelfJoin = false;

        foreach(TriplePattern pattern in query.Patterns)
        {
            if(pattern.HasSelfJoin())
            {
                hasSelfJoin = true;
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

        return new JoinShapeScan(constraints, scannable, edges, hasSelfJoin);
    }

    /// <summary>
    /// The GYO reduction: repeatedly drop variables occurring in exactly one edge and edges contained in
    /// another edge; the hypergraph is acyclic exactly when at most one edge survives. The caller's sets
    /// are not modified — the reduction runs over copies.
    /// </summary>
    /// <param name="edgeSets">The patterns' variable sets.</param>
    /// <returns><see langword="true"/> when acyclic.</returns>
    internal static bool IsAcyclic(List<HashSet<Variable>> edgeSets)
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

            SingletonVariablePredicate isSingleton = new(occurrences);
            foreach(HashSet<Variable> edge in edges)
            {
                changed |= edge.RemoveWhere(isSingleton.IsSingleton) > 0;
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

    /// <summary>
    /// The number of connected components the edges form over shared variables, by union-find over an
    /// explicit worklist. Zero edges is zero components.
    /// </summary>
    /// <param name="edgeSets">The patterns' variable sets.</param>
    /// <returns>The component count.</returns>
    internal static int ComponentCount(List<HashSet<Variable>> edgeSets)
    {
        int[] parents = new int[edgeSets.Count];
        for(int i = 0; i < parents.Length; i++)
        {
            parents[i] = i;
        }

        for(int i = 0; i < edgeSets.Count; i++)
        {
            for(int j = i + 1; j < edgeSets.Count; j++)
            {
                if(edgeSets[i].Overlaps(edgeSets[j]))
                {
                    Merge(parents, i, j);
                }
            }
        }

        int components = 0;
        for(int i = 0; i < parents.Length; i++)
        {
            if(RootOf(parents, i) == i)
            {
                components++;
            }
        }

        return components;
    }

    /// <summary>The representative of the set holding an edge, by iterative path halving — no recursion.</summary>
    /// <param name="parents">The union-find parent array.</param>
    /// <param name="edge">The edge index.</param>
    /// <returns>The representative's index.</returns>
    private static int RootOf(int[] parents, int edge)
    {
        int current = edge;
        while(parents[current] != current)
        {
            parents[current] = parents[parents[current]];
            current = parents[current];
        }

        return current;
    }

    /// <summary>Merges the sets holding two edges.</summary>
    /// <param name="parents">The union-find parent array.</param>
    /// <param name="left">One edge index.</param>
    /// <param name="right">The other edge index.</param>
    private static void Merge(int[] parents, int left, int right)
    {
        int leftRoot = RootOf(parents, left);
        int rightRoot = RootOf(parents, right);

        if(leftRoot != rightRoot)
        {
            parents[rightRoot] = leftRoot;
        }
    }

    /// <summary>
    /// One pass over a query's patterns: the edge sets both the batched planner and the selector seam
    /// read, the scannable/constraint split the planner orders over, and the self-join verdict the
    /// planner bails on.
    /// </summary>
    /// <param name="Constraints">The fully bound patterns — membership constraints, binding no variable.</param>
    /// <param name="Scannable">The variable-bearing patterns, in query order, one per edge set.</param>
    /// <param name="Edges">One variable set per scannable pattern, in the same order.</param>
    /// <param name="HasSelfJoin">Whether any pattern binds one variable at two of its own positions.</param>
    internal readonly record struct JoinShapeScan(
        List<TriplePattern> Constraints,
        List<TriplePattern> Scannable,
        List<HashSet<Variable>> Edges,
        bool HasSelfJoin);

    /// <summary>Whether a variable occurs in exactly one edge; carries the occurrence map as explicit state so the predicate closes over no enclosing local.</summary>
    /// <param name="occurrences">Each variable's occurrence count across the surviving edges.</param>
    private sealed class SingletonVariablePredicate(Dictionary<Variable, int> occurrences)
    {
        /// <summary>Each variable's occurrence count across the surviving edges.</summary>
        private Dictionary<Variable, int> Occurrences { get; } = occurrences;

        /// <summary>Tests whether a variable occurs in exactly one surviving edge.</summary>
        /// <param name="variable">The variable to test.</param>
        /// <returns><see langword="true"/> when the variable occurs exactly once.</returns>
        public bool IsSingleton(Variable variable)
        {
            return Occurrences[variable] == 1;
        }
    }
}
