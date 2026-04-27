using System.Threading;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// Picks the next action for the query driver based on the
/// current state of the descent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where consulted.</b> The driver consults the planner at
/// every variable boundary — when one variable level finishes
/// and another is to begin. The default static planner returns
/// the same <see cref="PlannerDecisionKind.DescendVariable"/>
/// sequence regardless of execution state; an adaptive
/// planner inspects <see cref="PlannerContext"/> and may
/// choose differently from one consultation to the next based
/// on observed selectivity, recent access denials, or
/// custom heuristics.
/// </para>
/// <para>
/// <b>Synchronous by design.</b> The planner is a pure
/// function from <see cref="PlannerContext"/> to
/// <see cref="PlannerDecision"/>; it does not consult external
/// systems. Asynchronous concerns (capability servers,
/// statistics services) live in the access-control delegate
/// or in pre-query setup, not in the per-step planner. This
/// keeps the planner a CPU-only, allocation-light hot-path.
/// </para>
/// <para>
/// <b>Cancellation.</b> The
/// <paramref name="cancellationToken"/> is the same token
/// threaded through the rest of query execution. The planner
/// is expected to honour it on entry — typically as a single
/// <see cref="CancellationToken.ThrowIfCancellationRequested"/>
/// call before any work — though it does not have to.
/// </para>
/// </remarks>
/// <param name="context">The current state of the descent.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>The planner's chosen action.</returns>
public delegate PlannerDecision Planner(
    PlannerContext context,
    CancellationToken cancellationToken);
