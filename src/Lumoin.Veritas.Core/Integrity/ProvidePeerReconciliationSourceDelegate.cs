using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Supplies the single-block peer-reconciliation restoring source for one repair pass, or
/// <see langword="null"/> when no peer is available — the pass then runs exactly as a local-only repair.
/// Invoked INSIDE the repair pass, after its own manifest recovery and only when the system-of-record is
/// damaged, so the host fetches against the damaged generation's recovered facts. Fetching a peer's
/// verified sketch is transport work, so the seam is awaitable. A provider that throws is treated as no
/// source: the fault is named on the trace
/// (<see cref="Lumoin.Veritas.Core.Diagnostics.StorageTraceEventKind.PeerSourceUnavailable"/>) and the
/// round continues local-only — a transport fault never aborts a viable local repair; cancellation
/// propagates.
/// </summary>
/// <param name="commitGeneration">The damaged generation under repair, from the pass's own recovered manifest.</param>
/// <param name="dictionaryEpoch">The term-dictionary epoch that generation's manifest records; the supplied source must be keyed to it.</param>
/// <param name="cancellationToken">Cancels the fetch.</param>
/// <returns>The restoring source, or <see langword="null"/> to leave the rung unsourced.</returns>
public delegate ValueTask<PeerReconciliationSource?> ProvidePeerReconciliationSourceDelegate(
    long commitGeneration,
    long dictionaryEpoch,
    CancellationToken cancellationToken);
