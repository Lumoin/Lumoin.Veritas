using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Drives anti-entropy reconciliation across retry rounds until a replica converges or a round bound is reached.
/// Each round runs <see cref="AntiEntropySession.ReconcileAsync"/> against the peer fetch and carries its result
/// index into the next round, so a converging round's union is what the next round reconciles from, and a declining
/// round — an unavailable peer, a rejected or incomplete peer sketch — is simply retried. This is the continuous
/// catch-up core of an active-active node: a permitted-and-clean round converges, every transient adversity is a
/// retried round (exactly the behavior certified under drop / corruption / partition), and the fetch it drives is
/// any <see cref="AsyncSketchFetchDelegate"/> — typically a governed (and optionally fault-injected) one.
/// </summary>
/// <remarks>
/// The bound is a round count, and declining rounds are retried back-to-back: this is the bounded catch-up a node
/// runs to converge now, not the paced background daemon (a long-lived node schedules repeated calls to this on an
/// interval). The reconcile path is value-based, so the loop branches on the outcome and never catches an
/// exception for an expected decline.
/// </remarks>
public static class ReplicaReconcileLoop
{
    /// <summary>Reconciles round after round until the replica converges (or is already consistent), or <paramref name="maxRounds"/> rounds have run.</summary>
    /// <param name="local">The local replica to converge.</param>
    /// <param name="dictionaryEpoch">This endpoint's dictionary epoch, threaded to each round's session; a structural peer stamped with a different epoch is refused per round.</param>
    /// <param name="fetch">The peer fetch each round drives — typically a governed and/or fault-injected fetch.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under, and the false-decode ceiling a complete peel is gated on.</param>
    /// <param name="pool">The pool the session's transient buffers are rented from.</param>
    /// <param name="timeProvider">The clock the session measures elapsed time and timestamps events against.</param>
    /// <param name="maxRounds">The maximum number of rounds to run; positive.</param>
    /// <param name="trace">The diagnostics sink each round emits its outcome to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted events carry.</param>
    /// <param name="cancellationToken">The token that cancels a round.</param>
    /// <returns>The loop result: the final index, the last round's outcome, the rounds run, and whether it converged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="local"/>, <paramref name="fetch"/>, <paramref name="pool"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRounds"/> is not positive.</exception>
    public static async ValueTask<ReplicaReconcileResult> RunUntilConvergedAsync(
        ColumnarTripleIndex local,
        ulong dictionaryEpoch,
        AsyncSketchFetchDelegate fetch,
        ReplicationPolicy policy,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        int maxRounds,
        TraceHandler<ReplicationTraceEvent>? trace = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRounds);

        ColumnarTripleIndex current = local;
        AntiEntropyOutcome outcome = AntiEntropyOutcome.PeerUnavailable;
        int round = 0;
        while(round < maxRounds)
        {
            round++;
            AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(current, dictionaryEpoch, fetch, policy, pool, timeProvider, trace, correlationId, cancellationToken).ConfigureAwait(false);
            current = result.ConvergedIndex;
            outcome = result.Outcome;
            if(outcome is AntiEntropyOutcome.Converged or AntiEntropyOutcome.AlreadyConsistent)
            {
                return new ReplicaReconcileResult(current, outcome, round, Converged: true, result.RecoveredAdditions);
            }
        }

        return new ReplicaReconcileResult(current, outcome, round, Converged: false, RecoveredAdditions: default);
    }
}
