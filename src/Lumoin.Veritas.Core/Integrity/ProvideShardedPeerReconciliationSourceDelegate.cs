using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Supplies the sharded multi-block peer-reconciliation restoring source for one repair pass, or
/// <see langword="null"/> when no peer transport is bound — the pass then runs exactly as a local-only
/// repair. Invoked INSIDE the repair pass, after its own manifest recovery and only when the
/// system-of-record is damaged, so the host binds the transport against the damaged generation's
/// recovered facts. The seam is awaitable because binding may involve transport negotiation. A provider
/// that throws is treated as no source: the fault is named
/// on the trace (<see cref="Lumoin.Veritas.Core.Diagnostics.StorageTraceEventKind.PeerSourceUnavailable"/>)
/// and the round continues local-only; cancellation propagates.
/// </summary>
/// <param name="commitGeneration">The damaged generation under repair, from the pass's own recovered manifest.</param>
/// <param name="dictionaryEpoch">The term-dictionary epoch that generation's manifest records; the supplied source must be keyed to it.</param>
/// <param name="cancellationToken">Cancels the binding.</param>
/// <returns>The restoring source, or <see langword="null"/> to leave the sharded path unsourced.</returns>
public delegate ValueTask<ShardedPeerReconciliationSource?> ProvideShardedPeerReconciliationSourceDelegate(
    long commitGeneration,
    long dictionaryEpoch,
    CancellationToken cancellationToken);
