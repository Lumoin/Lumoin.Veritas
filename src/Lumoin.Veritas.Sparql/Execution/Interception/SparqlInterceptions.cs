using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Algebra;

namespace Lumoin.Veritas.Sparql.Execution.Interception;

/// <summary>
/// The shipped interception entries, each a thin adapter binding an expand-phase trigger shape to the
/// engine's fast-path machinery — the guards and the answering logic live unchanged in the engine methods
/// the adapters call, so re-housing the dispatch here changed no behavior. Entries are static methods bound
/// as method groups by <see cref="SparqlInterceptionRegistry.Default"/>.
/// </summary>
internal static class SparqlInterceptions
{
    /// <summary>The count-star entry's name: a bare <c>COUNT(*)</c> over a BGP answers from the factorised build's cardinality.</summary>
    public const string CountStarName = "count-star";

    /// <summary>The distinct-star-keys entry's name: a <c>DISTINCT</c> of star-key variables over a BGP answers one row per group.</summary>
    public const string DistinctStarKeysName = "distinct-star-keys";

    /// <summary>The limit-leaf-cap entry's name: a <c>LIMIT</c> over a count-preserving chain caps the BGP leaf's drain.</summary>
    public const string LimitLeafCapName = "limit-leaf-cap";

    /// <summary>The slice-window-drain entry's name: on-mode, a window the cap cannot reach drains through a transient cursor pipeline.</summary>
    public const string SliceWindowDrainName = "slice-window-drain";

    /// <summary>The ASK first-solution short-circuit's name — an entry-strategy interception living at <see cref="SparqlQueryEngine.EvaluateAskAsync(Algebra.AlgebraOperator, System.Threading.CancellationToken)"/>, not in the registry; it shares the trace vocabulary and the policy switch.</summary>
    public const string AskFirstSolutionName = "ask-first-solution";

    /// <summary>The value-index probe entry's name: a <c>FILTER</c> whose comparison matches a registered value index's declared axis answers from the index's locators.</summary>
    public const string ValueIndexProbeName = "value-index-probe";

    /// <summary>A bare <c>COUNT(*)</c> over a BGP: answers the aggregate subtree from the factorised build's cardinality without materialising the BGP.</summary>
    /// <param name="node">The expand-phase operator.</param>
    /// <param name="site">The evaluation state.</param>
    /// <param name="cancellationToken">Unused; the count answers synchronously.</param>
    /// <returns>The answered count table, or declined.</returns>
    public static ValueTask<SparqlInterceptionOutcome> CountStar(AlgebraOperator node, SparqlInterceptionSite site, CancellationToken cancellationToken)
    {
        return node is AggregateJoin aggregateJoin && site.Engine.TryEvaluateCountOnly(aggregateJoin, site.Graph) is SolutionTable counted
            ? new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Answered(counted))
            : new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Declined);
    }

    /// <summary>A <c>DISTINCT</c> of star-key variables over a BGP: answers one row per group from the factorised build's keys.</summary>
    /// <param name="node">The expand-phase operator.</param>
    /// <param name="site">The evaluation state.</param>
    /// <param name="cancellationToken">Unused; the keys answer synchronously.</param>
    /// <returns>The answered key table, or declined.</returns>
    public static ValueTask<SparqlInterceptionOutcome> DistinctStarKeys(AlgebraOperator node, SparqlInterceptionSite site, CancellationToken cancellationToken)
    {
        return node is Distinct distinct && site.Engine.TryEvaluateDistinctKeys(distinct, site.Graph) is SolutionTable distinctKeys
            ? new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Answered(distinctKeys))
            : new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Declined);
    }

    /// <summary>
    /// A <c>LIMIT</c> whose chain down to a BGP preserves row counts: annotates the leaf with an
    /// offset+limit drain cap. The slice itself still expands and evaluates normally — the cap only bounds
    /// the future leaf visit, so answers are unchanged and only the surplus is never drained. Where this cap
    /// applies it is strictly preferred over the streaming window (no cursor overhead), which is why this
    /// entry precedes <see cref="SliceWindowDrain"/> in the registry.
    /// </summary>
    /// <param name="node">The expand-phase operator.</param>
    /// <param name="site">The evaluation state.</param>
    /// <param name="cancellationToken">Unused; the walk is synchronous.</param>
    /// <returns>The leaf-cap annotation, or declined.</returns>
    public static ValueTask<SparqlInterceptionOutcome> LimitLeafCap(AlgebraOperator node, SparqlInterceptionSite site, CancellationToken cancellationToken)
    {
        return node is Slice { Limit: int limit } slice && limit >= 0 && SparqlQueryEngine.TryFindCappableBgp(slice.Input) is Bgp cappable
            ? new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.LeafCap(cappable, (int)Math.Min((long)slice.Offset + limit, int.MaxValue)))
            : new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Declined);
    }

    /// <summary>
    /// The on-mode streaming window: a <c>LIMIT</c> the leaf cap cannot reach (its chain is not
    /// count-preserving) drains through a transient cursor pipeline that early-terminates upstream
    /// production once the window fills — gated on the enclosing budget (charges refunded after the drain),
    /// the order-preservation rollback inside the compile, and the breaker decline (a window directly over
    /// OrderBy/Group/AggregateJoin cannot early-exit, and off-mode's columnar slice is strictly better there).
    /// </summary>
    /// <param name="node">The expand-phase operator.</param>
    /// <param name="site">The evaluation state.</param>
    /// <param name="cancellationToken">A token that aborts the drain.</param>
    /// <returns>The drained window table, or declined.</returns>
    public static async ValueTask<SparqlInterceptionOutcome> SliceWindowDrain(AlgebraOperator node, SparqlInterceptionSite site, CancellationToken cancellationToken)
    {
        if(node is not Slice { Limit: int limit } slice
            || limit < 0
            || !site.Engine.EnginePolicy.PreferStreamingOperators
            || SparqlQueryEngine.SliceWindowBottomsOnBreaker(slice.Input))
        {
            return SparqlInterceptionOutcome.Declined;
        }

        SolutionTable? window = await site.Engine.TryDrainSliceWindowAsync(slice, site.Graph, site.CursorBudget, site.ExistsDepth, site.Rewrites, site.Trace, cancellationToken).ConfigureAwait(false);

        return window is SolutionTable table ? SparqlInterceptionOutcome.Answered(table) : SparqlInterceptionOutcome.Declined;
    }

    /// <summary>
    /// The value-index probe: a <c>FILTER</c> over a BGP whose shape the recognizer matches against a
    /// registered axis answers from the index's locators — gated on the default-OFF
    /// <see cref="SparqlEnginePolicy.PreferValueIndexes"/>, and declining (to the unchanged scan) on every
    /// unmatched shape, undeclared predicate, cross-family constant, non-default graph, or unbuildable
    /// generation, so the route never changes an answer.
    /// </summary>
    /// <param name="node">The expand-phase operator.</param>
    /// <param name="site">The evaluation state.</param>
    /// <param name="cancellationToken">Unused; the probe answers synchronously.</param>
    /// <returns>The probe-answered table, or declined.</returns>
    public static ValueTask<SparqlInterceptionOutcome> ValueIndexProbe(AlgebraOperator node, SparqlInterceptionSite site, CancellationToken cancellationToken)
    {
        return node is Filter { Input: Bgp } filter
            && site.Engine.EnginePolicy.PreferValueIndexes
            && site.Engine.TryEvaluateValueIndexProbe(filter, site.Graph) is SolutionTable probed
            ? new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Answered(probed))
            : new ValueTask<SparqlInterceptionOutcome>(SparqlInterceptionOutcome.Declined);
    }
}
