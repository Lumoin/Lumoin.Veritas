using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The body of one compute-lane turn: a unit of work run as a turn by a
/// single worker, observing <paramref name="cancellationToken"/>
/// cooperatively. A turn is awaited to completion before the worker
/// takes its next turn. CPU-bound turns simply return a completed
/// <see cref="ValueTask"/>; turns that genuinely await let the
/// single-cooperative-thread (web) lane yield between awaits.
/// </summary>
/// <remarks>
/// <para>
/// Consumed by method-group conversion from a job object that holds the
/// turn's state, not by capturing a lambda — the project's no-closure
/// convention. State and any completion signal travel on that object.
/// </para>
/// </remarks>
/// <param name="cancellationToken">Signals that the turn should abandon its work cooperatively — checked at the work's own safe points.</param>
/// <returns>A task that completes when the turn is done.</returns>
public delegate ValueTask ComputeWorkDelegate(CancellationToken cancellationToken);
