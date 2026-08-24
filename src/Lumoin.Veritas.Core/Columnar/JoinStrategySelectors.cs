using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The join-route selectors the library ships. Each is a static method group, so it captures nothing and
/// allocates nothing per query — the discipline <see cref="Lumoin.Veritas.Core.Network.NetworkGovernance"/>
/// and <see cref="Lumoin.Veritas.Core.Hypertrie.Planning.Planners"/> follow.
/// </summary>
public static class JoinStrategySelectors
{
    /// <summary>The estimated compression — flat rows over stored tuples — a star must clear before the factorising route is engaged; the measured time crossover, set by the join-selector soak.</summary>
    internal const double StarEngageRatio = 4.0;

    /// <summary>The estimated compression a chain must clear before the join-then-nest route is engaged; the measured time crossover, set by the join-selector soak.</summary>
    internal const double ChainEngageRatio = 4.0;

    /// <summary>
    /// The default rule, and the one an unconfigured policy uses: a cyclic core or a disconnected
    /// (cartesian) shape on a six-order view takes the Free Join generic join — the two shapes the
    /// batched pipeline declines and the leapfrog driver serves worst; everything else keeps the batched
    /// scan-and-hash route where policy allows it, and the leapfrog driver where it does not. It states no
    /// depth, build, or factorisation of its own, so the engine's standing behaviour holds on those axes.
    /// </summary>
    public static JoinStrategySelectorDelegate Structural { get; } = SelectStructural;

    /// <summary>
    /// The flags-verbatim rule: the batched scan-and-hash route wherever policy enables it, the leapfrog
    /// driver otherwise, with no shape engagement — the routing a policy carrying no selector had before
    /// the structural rule, kept as a named instance for a deployment that wants exactly it.
    /// </summary>
    public static JoinStrategySelectorDelegate Manual { get; } = SelectManual;

    /// <summary>
    /// The calibrated rule: the structural rule's route, plus the factorising engagements the measured
    /// per-key statistics of the actual view justify. Depth stays the engine's own per-relation decision —
    /// a query-global depth would be a different and coarser semantics — and the trie build mode stays the
    /// policy's, since no reproducible crossover between the two build modes is in the measured record.
    /// With no statistic to read it is the structural rule exactly: it decides nothing the data did not
    /// justify.
    /// </summary>
    public static JoinStrategySelectorDelegate Calibrated { get; } = SelectCalibrated;

    /// <summary>The structural rule's body: integer and boolean comparisons over the features, and nothing else.</summary>
    /// <param name="context">The consultation context.</param>
    /// <param name="cancellationToken">The query's token; the rule is a handful of comparisons and does not consult it.</param>
    /// <returns>The decision.</returns>
    private static JoinSelectionDecision SelectStructural(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        (QueryEngineKind route, JoinSelectionReason reason) = StructuralRouteOf(context.Features);

        return JoinSelectionDecision.Structural(route, reason);
    }

    /// <summary>The flags-verbatim rule's body: the shape engagements of the structural rule, absent.</summary>
    /// <param name="context">The consultation context.</param>
    /// <param name="cancellationToken">The query's token; the rule is a single comparison and does not consult it.</param>
    /// <returns>The decision.</returns>
    private static JoinSelectionDecision SelectManual(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        return context.Features switch
        {
            { BatchedRouteEligible: true } => JoinSelectionDecision.Manual(QueryEngineKind.ColumnarBatched, JoinSelectionReason.AcyclicBatched),
            _ => JoinSelectionDecision.Manual(QueryEngineKind.Columnar, JoinSelectionReason.SoundDefault),
        };
    }

    /// <summary>
    /// The calibrated rule's body: the structural route rule, read through the same function the structural
    /// rule reads, beside the factorisation the view's own per-key statistics justify.
    /// </summary>
    /// <param name="context">The consultation context.</param>
    /// <param name="cancellationToken">The query's token; the rule is comparisons over statistics already read and does not consult it.</param>
    /// <returns>The decision.</returns>
    private static JoinSelectionDecision SelectCalibrated(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        (QueryEngineKind route, JoinSelectionReason reason) = StructuralRouteOf(context.Features);

        return JoinSelectionDecision.Calibrated(route, reason, FactorizationOf(context.View, context.Query));
    }

    /// <summary>
    /// The structural route rule: the one function both the structural and the calibrated rule read, so the
    /// calibrated route axis cannot drift from the rule it is the degenerate case of.
    /// </summary>
    /// <param name="features">The shape features the route is decided on.</param>
    /// <returns>The route and the rationale.</returns>
    private static (QueryEngineKind Route, JoinSelectionReason Reason) StructuralRouteOf(in JoinSelectionFeatures features)
    {
        //The shape engagements are scoped to a six-order view. GYO-cyclicity and rotation-incompatibility
        //are different properties — the rendezvous asks ColumnarRotationPlanner for a precedence-order
        //plan, not for the GYO reduction — so no claim is made that a cyclic shape cannot reach the seam
        //under a reduced order set. The guard is what makes that not matter: any shape arriving here under
        //a reduced order set takes neither engagement and keeps the route it would have had.
        return features switch
        {
            { OrderSetMode: ColumnarOrderSetMode.AllSixOrders, ComponentCount: >= 2 } => (QueryEngineKind.FreeJoin, JoinSelectionReason.DisconnectedComponents),
            { OrderSetMode: ColumnarOrderSetMode.AllSixOrders, Acyclic: false } => (QueryEngineKind.FreeJoin, JoinSelectionReason.CyclicCore),
            { BatchedRouteEligible: true } => (QueryEngineKind.ColumnarBatched, JoinSelectionReason.AcyclicBatched),
            _ => (QueryEngineKind.Columnar, JoinSelectionReason.SoundDefault),
        };
    }

    /// <summary>
    /// Which factorising route the view's per-key fan-out estimates justify: the star where its estimated
    /// compression clears <see cref="StarEngageRatio"/>, otherwise the chain where its own clears
    /// <see cref="ChainEngageRatio"/>, and nothing stated at all where neither does. Estimation is
    /// conservative — a shape or a statistic the estimator cannot read states nothing, so the policy's own
    /// flags stand — and the pipeline's shape detection revalidates whatever is engaged, so a wrong
    /// estimate can cost time but never answers.
    /// </summary>
    /// <remarks>
    /// The thresholds gate on the factorisation's estimated compression — flat rows over stored tuples
    /// (<c>∏fan / Σfan</c> for a star over its arms, <c>fanA·S / (fanA + S)</c> with <c>S = fanB·fanC</c>
    /// for a chain). Storage break-even sits at ratio 1, but the measured TIME break-even sits higher
    /// (grouping overhead is paid per key). Estimates read the base columns; an uncompacted delta skews
    /// them slightly, which only moves decisions near a threshold.
    /// </remarks>
    /// <param name="view">The columnar view the query will run on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The engagement, or <see cref="FactorizationEngagement.Unspecified"/> when the statistics justify none.</returns>
    private static FactorizationEngagement FactorizationOf(ColumnarTripleIndex view, BasicGraphPattern query)
    {
        if(ShouldFactorizeStar(view, query))
        {
            return FactorizationEngagement.Star;
        }

        if(ShouldFactorizeChain(view, query))
        {
            return FactorizationEngagement.Chain;
        }

        return FactorizationEngagement.Unspecified;
    }

    /// <summary>
    /// Whether the query is a single-key star whose estimated compression clears
    /// <see cref="StarEngageRatio"/>: one variable common to every pattern, each arm's per-key fan-out
    /// estimable, and the product of the fan-outs exceeding their sum by the threshold.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns><see langword="true"/> when the star route should be engaged.</returns>
    private static bool ShouldFactorizeStar(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        if(query.Patterns.Count < 3)
        {
            return false;
        }

        Variable? key = TrySharedKey(query);
        if(key is null)
        {
            return false;
        }

        double sum = 0;
        double product = 1;
        foreach(TriplePattern pattern in query.Patterns)
        {
            if(!ColumnarKeyStatistics.TryEstimateKeyFanOut(index, pattern, key.Value, out double fanOut))
            {
                return false;
            }

            sum += fanOut;
            product *= fanOut;
        }

        return product >= sum * StarEngageRatio;
    }

    /// <summary>
    /// Whether the query is a three-pattern chain whose estimated compression clears
    /// <see cref="ChainEngageRatio"/>: a middle pattern sharing one variable with each arm (the arms
    /// sharing nothing), the independent arm's fan-out against the sub-tree product.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns><see langword="true"/> when the chain route should be engaged.</returns>
    private static bool ShouldFactorizeChain(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        if(query.Patterns.Count != 3)
        {
            return false;
        }

        for(int middle = 0; middle < 3; middle++)
        {
            TriplePattern middlePattern = query.Patterns[middle];
            TriplePattern first = query.Patterns[(middle + 1) % 3];
            TriplePattern second = query.Patterns[(middle + 2) % 3];

            Variable? hub = TrySingleSharedVariable(middlePattern, first);
            Variable? nest = TrySingleSharedVariable(middlePattern, second);
            if(hub is null || nest is null || hub.Value == nest.Value || TrySingleSharedVariable(first, second) is not null)
            {
                continue;
            }

            //fanA: the independent arm's matches per hub value; fanB: the
            //middle's nest values per hub; fanC: the leaf arm's matches per
            //nest value. Compression is (fanA·S)/(fanA+S) with S = fanB·fanC.
            if(!ColumnarKeyStatistics.TryEstimateKeyFanOut(index, first, hub.Value, out double fanA)
                || !ColumnarKeyStatistics.TryEstimateKeyFanOut(index, middlePattern, hub.Value, out double fanB)
                || !ColumnarKeyStatistics.TryEstimateKeyFanOut(index, second, nest.Value, out double fanC))
            {
                return false;
            }

            double subTree = fanB * fanC;
            double denominator = fanA + subTree;

            return denominator > 0 && fanA * subTree >= denominator * ChainEngageRatio;
        }

        return false;
    }

    /// <summary>The one variable present in every pattern, or <see langword="null"/> when no or more than one variable is.</summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The shared key, or <see langword="null"/>.</returns>
    private static Variable? TrySharedKey(BasicGraphPattern query)
    {
        Variable? key = null;
        int shared = 0;
        foreach(Variable candidate in VariablesOf(query.Patterns[0]))
        {
            bool inAll = true;
            for(int pattern = 1; pattern < query.Patterns.Count && inAll; pattern++)
            {
                inAll = Binds(query.Patterns[pattern], candidate);
            }

            if(inAll)
            {
                key = candidate;
                shared++;
            }
        }

        return shared == 1 ? key : null;
    }

    /// <summary>The single variable two patterns share, or <see langword="null"/> when they share none or more than one.</summary>
    /// <param name="first">The first pattern.</param>
    /// <param name="second">The second pattern.</param>
    /// <returns>The shared variable, or <see langword="null"/>.</returns>
    private static Variable? TrySingleSharedVariable(TriplePattern first, TriplePattern second)
    {
        Variable? shared = null;
        int count = 0;
        foreach(Variable candidate in VariablesOf(first))
        {
            if(Binds(second, candidate))
            {
                shared = candidate;
                count++;
            }
        }

        return count == 1 ? shared : null;
    }

    /// <summary>The pattern's distinct variables in position order.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The variables.</returns>
    private static List<Variable> VariablesOf(TriplePattern pattern)
    {
        List<Variable> variables = [];
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsVariable && !variables.Contains(pattern.At(position).Variable))
            {
                variables.Add(pattern.At(position).Variable);
            }
        }

        return variables;
    }

    /// <summary>Whether the pattern binds the variable at any position.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="variable">The variable.</param>
    /// <returns><see langword="true"/> when bound.</returns>
    private static bool Binds(TriplePattern pattern, Variable variable)
    {
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsVariable && pattern.At(position).Variable == variable)
            {
                return true;
            }
        }

        return false;
    }
}
