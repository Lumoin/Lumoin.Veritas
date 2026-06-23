using System;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Fetches a peer replica's persisted integrity-sketch image, encoded at <paramref name="symbolBudget"/>
/// symbols, so an <see cref="AntiEntropySession"/> can load it as a verified sketch and combine it with the
/// local one. The image is the same self-describing, block-checksummed sketch format the local side persists,
/// so the session's verifying load refuses corrupt bytes before any combine. A peer that cannot be reached
/// returns an empty image, which the session reads as an unavailable peer and declines.
/// </summary>
/// <remarks>
/// This slice of the arc is synchronous: the in-process binding persists the peer's index on the calling
/// thread, so there is no transport to await. The cross-process transport — an awaited fetch over the
/// replication message channel — is a later rung; keeping the seam synchronous here means the core's
/// synchronous repair path can drive it without an async hop, and the asynchronous variant is added beside
/// it rather than reshaping this one.
/// </remarks>
/// <param name="symbolBudget">The number of coded symbols the peer's sketch image must carry — the session's budget, derived from the local item count under the active <see cref="ReplicationPolicy"/>.</param>
/// <returns>The peer's persisted sketch image, or an empty image when no peer is reachable.</returns>
public delegate ReadOnlyMemory<byte> SketchFetchDelegate(int symbolBudget);
