using System.Threading;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Chooses which view-borne join route serves one query — the Free Join generic join, the batched
/// scan-and-hash pipeline, or the columnar leapfrog driver — from the shape and the view the engine
/// hands it. The engine consults it once per qualifying query, after the columnar view is resolved and
/// before any route is entered; <see cref="QueryEnginePolicy.JoinRouteSelector"/> is where a deployment
/// supplies its own, and <see cref="JoinStrategySelectors"/> holds the ones the library ships.
/// </summary>
/// <remarks>
/// <para>
/// <b>Synchronous by design.</b> The selector is a pure function from <see cref="JoinSelectionContext"/>
/// to <see cref="JoinSelectionDecision"/> on the per-query hot path; it consults no external system and
/// reads no clock. Asynchronous concerns belong at the boundaries, not here — the same rule
/// <see cref="Lumoin.Veritas.Core.Hypertrie.Planning.Planner"/> follows.
/// </para>
/// <para>
/// <b>A decision is never a correctness statement.</b> Every route this delegate can name answers the
/// query identically; a decision the engine cannot serve — a route that declines the shape, or a route
/// this seam does not own — costs a fall-through to the sound default and never an answer.
/// </para>
/// <para>
/// <b>Cancellation.</b> The token is the query's own. A selector doing real work honours it on entry;
/// the built-ins are a handful of integer comparisons and ignore it.
/// </para>
/// </remarks>
/// <param name="context">The query, the view, and the shape features the engine measured.</param>
/// <param name="cancellationToken">The query's cancellation token.</param>
/// <returns>The route to run, stamped with the deciding selector's identity.</returns>
public delegate JoinSelectionDecision JoinStrategySelectorDelegate(
    in JoinSelectionContext context,
    CancellationToken cancellationToken);
