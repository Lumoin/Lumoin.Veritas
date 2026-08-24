using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Governs a replication peer fetch: a closure-free decorator that runs the network-governance gate before an
/// inner <see cref="AsyncSketchFetchDelegate"/> and, on a permit, fetches; on a deny, returns
/// <see cref="SketchFetchResult.Unavailable"/>, which <see cref="AntiEntropySession.ReconcileAsync"/> reads as an
/// unavailable peer — so governance declines a fetch by value, exactly as the session declines every other
/// unreachable-peer case, never by throwing. A deny never invokes the inner fetch, so there is nothing to dispose.
/// Its <see cref="FetchAsync"/> is itself an <see cref="AsyncSketchFetchDelegate"/>, so it composes in front of any
/// fetch (the in-memory test fetch or the <see cref="SketchChannelClient"/> transport).
/// </summary>
/// <remarks>
/// The peer is per-connection, so the peer key and access context are construction state, not per-call arguments —
/// the replication fetch seam keeps its budget-only signature. This decorator owns the peer key and disposes it
/// with itself: the peer key's pooled bytes are read inside the asynchronous governance decision, so making a
/// single owner whose lifetime is the connection's keeps them valid for the decision's whole duration. Disposing
/// this decorator while a fetch is in flight is the ordinary use-after-dispose misuse, not a separate-owner race
/// that could silently return the buffer to the pool mid-decision. As an explicit binding frame it captures
/// nothing, so it holds no lexical closure.
/// </remarks>
public sealed class GovernedSketchFetch : IDisposable
{
    private readonly AsyncSketchFetchDelegate inner;
    private readonly NetworkGovernanceDelegate governance;
    private readonly NetworkPeerKey peer;
    private readonly AccessContext? context;
    private readonly TimeProvider timeProvider;
    private readonly TraceHandler<NetworkGovernanceTraceEvent>? trace;
    private readonly Guid correlationId;

    //A naked field: the trace sequence is advanced with Interlocked, which needs a by-ref target.
    private long sequence;

    /// <summary>Creates a governed fetch over an inner fetch for one peer connection.</summary>
    /// <param name="inner">The fetch this governs — the transport (or in-memory) fetch invoked on a permit.</param>
    /// <param name="governance">The policy consulted before each fetch.</param>
    /// <param name="peer">The peer the connection reaches; <see cref="NetworkPeerKey.None"/> when unidentified. Ownership transfers to this decorator, which disposes it; the caller must not dispose it or use it elsewhere.</param>
    /// <param name="context">The opaque access context identifying the local node to the policy, or <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock a delayed fetch backs off against and the event is timestamped with.</param>
    /// <param name="trace">The diagnostics sink each governance verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted events carry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/>, <paramref name="governance"/>, <paramref name="peer"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public GovernedSketchFetch(
        AsyncSketchFetchDelegate inner,
        NetworkGovernanceDelegate governance,
        NetworkPeerKey peer,
        AccessContext? context,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace = null,
        Guid correlationId = default)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.inner = inner;
        this.governance = governance;
        this.peer = peer;
        this.context = context;
        this.timeProvider = timeProvider;
        this.trace = trace;
        this.correlationId = correlationId;
    }

    /// <summary>Disposes the peer key this fetch owns. Do not dispose while a <see cref="FetchAsync"/> is in flight.</summary>
    public void Dispose()
    {
        peer.Dispose();
    }

    /// <summary>Governs then fetches: consults the policy for an outbound replication fetch and, on a permit, invokes the inner fetch (whose owned result flows to the caller); on a deny, returns <see cref="SketchFetchResult.Unavailable"/> without fetching, which the session reads as an unavailable peer. An <see cref="AsyncSketchFetchDelegate"/> — pass it to <see cref="AntiEntropySession.ReconcileAsync"/>.</summary>
    /// <param name="symbolBudget">The fetch's symbol budget; also the governance size hint.</param>
    /// <param name="cancellationToken">The token that cancels the governance decision or the fetch.</param>
    /// <returns>The peer's owned sketch image on a permit, or <see cref="SketchFetchResult.Unavailable"/> on a deny.</returns>
    public async ValueTask<SketchFetchResult> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
    {
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundReplicationFetch, context, peer, symbolBudget, PartitionCoordinate: -1);
        bool permitted = await NetworkGovernanceGate.TryEnterAsync(governance, request, timeProvider, trace, correlationId, Interlocked.Increment(ref sequence), cancellationToken).ConfigureAwait(false);
        if(!permitted)
        {
            return SketchFetchResult.Unavailable;
        }

        return await inner(symbolBudget, cancellationToken).ConfigureAwait(false);
    }
}
