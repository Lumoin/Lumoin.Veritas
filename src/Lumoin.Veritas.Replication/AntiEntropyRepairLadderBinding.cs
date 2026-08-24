using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Binds an <see cref="AntiEntropySession"/> to the core's public repair-source ladder
/// (<see cref="RepairSourceLadder.DescendAsync(RepairAttemptDelegate, CancellationToken)"/>) as its
/// peer-reconciliation rung, without touching the core. The ladder owns the descent order and first-success-wins semantics; this frame supplies
/// the per-rung restore action: the local rungs (<see cref="RepairRung.RederiveLocally"/>,
/// <see cref="RepairRung.LocalParity"/>) decline — this source only recovers from a peer — and
/// <see cref="RepairRung.PeerReconciliation"/> runs the session, restoring when the peel was complete. The
/// session's outcome is held in <see cref="Result"/> so the caller reads the converged index after the descent.
/// </summary>
/// <remarks>
/// The ladder takes a <see cref="RepairAttemptDelegate"/>; this binding supplies it as a closure-free explicit
/// frame — the session inputs are held as fields and <see cref="Attempt"/> is passed as a bound method group, so
/// no captured state escapes the descent. One instance drives one descent; reuse re-runs the session and
/// overwrites <see cref="Result"/>.
/// </remarks>
public sealed class AntiEntropyRepairLadderBinding
{
    private readonly ColumnarTripleIndex local;
    private readonly SketchFetchDelegate fetch;
    private readonly ReplicationPolicy policy;
    private readonly MemoryPool<byte> pool;
    private readonly TimeProvider timeProvider;
    private readonly TraceHandler<ReplicationTraceEvent>? trace;
    private readonly Guid correlationId;

    /// <summary>Creates a binding over the inputs one anti-entropy reconciliation needs.</summary>
    /// <param name="local">The local replica to reconcile and, on a complete peel, converge.</param>
    /// <param name="fetch">The seam that returns the peer's persisted sketch image at a requested symbol budget.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under.</param>
    /// <param name="pool">The pool the session's transient buffers are rented from.</param>
    /// <param name="timeProvider">The clock the session's elapsed time is measured against.</param>
    /// <param name="trace">The diagnostics sink the session's outcome is emitted to when the rung runs; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted event carries, linking the reconcile to the repair that drove the descent.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    public AntiEntropyRepairLadderBinding(
        ColumnarTripleIndex local,
        SketchFetchDelegate fetch,
        ReplicationPolicy policy,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        TraceHandler<ReplicationTraceEvent>? trace = null,
        Guid correlationId = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.local = local;
        this.fetch = fetch;
        this.policy = policy;
        this.pool = pool;
        this.timeProvider = timeProvider;
        this.trace = trace;
        this.correlationId = correlationId;
    }

    /// <summary>The outcome of the peer-reconciliation session, or <see langword="null"/> until the ladder reaches the peer-reconciliation rung. On a complete peel its <see cref="AntiEntropySessionResult.ConvergedIndex"/> is the converged replica.</summary>
    public AntiEntropySessionResult? Result { get; private set; }

    /// <summary>The per-rung restore action the ladder invokes: declines the local rungs and runs the anti-entropy session at the peer-reconciliation rung, reporting whether it restored — a complete peel. The session itself is synchronous, so every answer is a synchronously-completed value task.</summary>
    /// <param name="rung">The rung the ladder is attempting.</param>
    /// <param name="cancellationToken">The descent's token; the synchronous session completes without observing it.</param>
    /// <returns><see langword="true"/> when the peer-reconciliation rung completed the peel; <see langword="false"/> to let the ladder descend.</returns>
    public ValueTask<bool> Attempt(RepairRung rung, CancellationToken cancellationToken)
    {
        return rung switch
        {
            RepairRung.PeerReconciliation => new ValueTask<bool>(ReconcileWithPeer()),
            _ => new ValueTask<bool>(false),
        };
    }

    /// <summary>Runs the anti-entropy session, records its outcome in <see cref="Result"/>, and reports whether the peel was complete.</summary>
    /// <returns><see langword="true"/> when the session completed the peel; otherwise <see langword="false"/>.</returns>
    private bool ReconcileWithPeer()
    {
        AntiEntropySessionResult result = AntiEntropySession.Reconcile(local, fetch, policy, pool, timeProvider, trace, correlationId);
        Result = result;

        return result.IsComplete;
    }
}
