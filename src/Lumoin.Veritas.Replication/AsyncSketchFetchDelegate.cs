using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Asynchronously fetches a peer replica's persisted integrity-sketch image, encoded at
/// <paramref name="symbolBudget"/> symbols — the awaited, transport-facing sibling of
/// <see cref="SketchFetchDelegate"/>. A real fetch crosses a wire (a Verisync message channel over a socket or an
/// in-memory pipe), so it is asynchronous; <see cref="AntiEntropySession.ReconcileAsync"/> awaits it and keeps
/// every await in the session, never in the core's synchronous repair path. The image is the same
/// self-describing, block-checksummed sketch format the local side persists, so the session's verifying load
/// refuses corrupt bytes before any combine; a peer that cannot be reached returns
/// <see cref="SketchFetchResult.Unavailable"/>, which the session reads as an unavailable peer and declines.
/// </summary>
/// <param name="symbolBudget">The number of coded symbols the peer's sketch image must carry — the session's budget, derived from the local item count under the active <see cref="ReplicationPolicy"/>.</param>
/// <param name="cancellationToken">The token that cancels the fetch.</param>
/// <returns>The peer's persisted sketch image as a pool-owning <see cref="SketchFetchResult"/> the caller OWNS and must dispose exactly once, or <see cref="SketchFetchResult.Unavailable"/> when no peer is reachable.</returns>
public delegate ValueTask<SketchFetchResult> AsyncSketchFetchDelegate(int symbolBudget, CancellationToken cancellationToken);
