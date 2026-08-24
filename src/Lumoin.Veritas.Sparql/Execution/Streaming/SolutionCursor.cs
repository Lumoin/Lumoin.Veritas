using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// One streaming operator instance in a compiled pipeline: pull the next solution. Cursors are manually
/// driven — nothing auto-disposes on a throwing <see cref="MoveNextAsync"/> or a mid-pull cancellation — so
/// every consumer wraps its pulls in <c>try</c>/<c>finally</c> that disposes the owning
/// <see cref="StreamingPipeline"/>; teardown walks the pipeline's flat cursor list, never the tree.
/// </summary>
internal abstract class SolutionCursor : IAsyncDisposable
{
    /// <summary>Advances to the next solution; <see langword="false"/> means exhausted. Never throws for
    /// expected emptiness (value-based); completes synchronously on every pull that has buffered input, and
    /// asynchronously only where the source is genuinely async (a per-row backend, a deferred residency
    /// build, a lazy materialise-boundary first pull).</summary>
    /// <param name="cancellationToken">A token that aborts the pull.</param>
    /// <returns><see langword="true"/> when <see cref="Current"/> holds the next solution.</returns>
    public abstract ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken);

    /// <summary>The current solution after a <see langword="true"/> <see cref="MoveNextAsync"/>.</summary>
    public abstract SparqlSolution Current { get; }

    /// <summary>Rows this cursor has produced so far — the trace and bounded-work-pin observable, counted
    /// regardless of which source path produced them.</summary>
    public int RowsProduced { get; protected set; }

    /// <summary>Whether this cursor emits rows in its (left/input) child's order. The windowed-slice gate
    /// reads this: a position-based window engages the streaming path only over an order-preserving chain,
    /// keeping strict on/off multiset identity certifiable; joins are the reordering exception.</summary>
    public abstract bool IsOrderPreserving { get; }

    /// <summary>
    /// Re-arms the cursor chain for the next <c>EXISTS</c> pre-binding without reallocation: DISPOSES the
    /// current per-binding source (the leaf's live backend/batch enumerator and any arena it owns) BEFORE
    /// re-arming on the new pre-binding, then resets <see cref="RowsProduced"/> and per-pull state (seen-sets
    /// cleared). Binding-independent state — a drained, MATERIALISED build side, never a live enumerator —
    /// is retained. Reset is therefore the deterministic disposal owner of every intermediate binding's
    /// source; the pipeline's final <see cref="DisposeAsync"/> owns only the LAST binding's source.
    /// </summary>
    /// <param name="preBinding">The next pre-binding; the seeded leaf configuration consumes it, an unseeded
    /// cursor re-arms from scratch and the compatibility filter above applies it.</param>
    /// <returns>A task completing when the prior source is disposed and the cursor is re-armed.</returns>
    public abstract ValueTask ResetAsync(SparqlSolution preBinding);

    /// <summary>Releases the cursor's sources exactly once; safe on a partially-advanced or
    /// partially-compiled chain (a cursor whose sources were never opened disposes trivially).</summary>
    /// <returns>A task completing when the cursor's sources are released.</returns>
    public abstract ValueTask DisposeAsync();
}
