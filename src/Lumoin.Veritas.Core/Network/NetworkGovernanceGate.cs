using System;
using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// The reusable consult-and-honor step a transport decorator runs before an outbound call (or an inbound serve):
/// it asks the <see cref="NetworkGovernanceDelegate"/> for a verdict, emits a <see cref="NetworkGovernanceTraceEvent"/>,
/// and honors the verdict — permit proceeds, delay backs off against the injected clock then proceeds, deny
/// declines. Every transport boundary funnels through this one gate, so rate, firewall, and topology policy apply
/// uniformly and identically whether the call is a replication fetch or a federation query.
/// </summary>
public static class NetworkGovernanceGate
{
    /// <summary>Consults the policy for <paramref name="request"/> and honors the verdict.</summary>
    /// <param name="governance">The policy to consult.</param>
    /// <param name="request">The call being governed.</param>
    /// <param name="timeProvider">The clock a delay backs off against and the event is timestamped with.</param>
    /// <param name="trace">The diagnostics sink the verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted event carries.</param>
    /// <param name="sequenceNumber">The sequence number the emitted event carries.</param>
    /// <param name="cancellationToken">The token that cancels the consultation or the back-off.</param>
    /// <returns><see langword="true"/> when the call may proceed (a permit, or a delay after its back-off); <see langword="false"/> when it is denied and the caller must decline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="governance"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public static async ValueTask<bool> TryEnterAsync(
        NetworkGovernanceDelegate governance,
        NetworkGovernanceRequest request,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace,
        Guid correlationId,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(timeProvider);

        NetworkGovernanceDecision decision = await governance(request, cancellationToken).ConfigureAwait(false);
        Emit(trace, request, decision, timeProvider, correlationId, sequenceNumber);

        return decision.Kind switch
        {
            NetworkGovernanceKind.Permit => true,
            NetworkGovernanceKind.Delay => await DelayThenProceed(decision.RetryAfter, timeProvider, cancellationToken).ConfigureAwait(false),
            NetworkGovernanceKind.Deny => false,
            _ => false,
        };
    }

    /// <summary>Consults the policy and honors the verdict for a seam that signals a decline by exception: a permit (or a delay, after its back-off) returns, a deny throws <see cref="NetworkGovernanceDeniedException"/>. For the SERVICE and graph-resolve transports, whose existing failure channel is a throw the engine's silent handling catches.</summary>
    /// <param name="governance">The policy to consult.</param>
    /// <param name="request">The call being governed.</param>
    /// <param name="timeProvider">The clock a delay backs off against and the event is timestamped with.</param>
    /// <param name="trace">The diagnostics sink the verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted event carries.</param>
    /// <param name="sequenceNumber">The sequence number the emitted event carries.</param>
    /// <param name="cancellationToken">The token that cancels the consultation or the back-off.</param>
    /// <returns>A task that completes when the call may proceed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="governance"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="NetworkGovernanceDeniedException">The policy denied the call.</exception>
    public static async ValueTask EnterOrThrowAsync(
        NetworkGovernanceDelegate governance,
        NetworkGovernanceRequest request,
        TimeProvider timeProvider,
        TraceHandler<NetworkGovernanceTraceEvent>? trace,
        Guid correlationId,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        bool permitted = await TryEnterAsync(governance, request, timeProvider, trace, correlationId, sequenceNumber, cancellationToken).ConfigureAwait(false);
        if(!permitted)
        {
            throw new NetworkGovernanceDeniedException(request.Boundary);
        }
    }

    /// <summary>Backs off for <paramref name="retryAfter"/> against the injected clock, then reports that the call may proceed.</summary>
    /// <param name="retryAfter">The back-off; a non-positive value proceeds immediately.</param>
    /// <param name="timeProvider">The clock the delay runs against.</param>
    /// <param name="cancellationToken">The token that cancels the back-off.</param>
    /// <returns><see langword="true"/> once the back-off elapses.</returns>
    private static async ValueTask<bool> DelayThenProceed(TimeSpan retryAfter, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if(retryAfter > TimeSpan.Zero)
        {
            await Task.Delay(retryAfter, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Emits the governance verdict on the diagnostics channel when a handler is attached.</summary>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="request">The governed call.</param>
    /// <param name="decision">The verdict.</param>
    /// <param name="timeProvider">The clock the event is timestamped with.</param>
    /// <param name="correlationId">The correlation id the event carries.</param>
    /// <param name="sequenceNumber">The sequence number the event carries.</param>
    private static void Emit(
        TraceHandler<NetworkGovernanceTraceEvent>? trace,
        NetworkGovernanceRequest request,
        NetworkGovernanceDecision decision,
        TimeProvider timeProvider,
        Guid correlationId,
        long sequenceNumber)
    {
        if(trace is null)
        {
            return;
        }

        NetworkGovernanceTraceEvent evt = new(
            sequenceNumber,
            timeProvider.GetUtcNow().UtcTicks,
            correlationId,
            request.Boundary,
            decision.Kind,
            ComputePeerKeyHash(request.PeerKey),
            decision.RetryAfter.Ticks);
        trace(in evt);
    }

    /// <summary>A stable, deterministic hash of the peer key for trace joins: the kind seeds the hash so the same bytes under two kinds hash distinctly; an unidentified key hashes to 0. An identified key can also hash to 0 (about 2^-64), so 0 is not a strict unidentified marker — the hash only joins diagnostics, it never feeds a decision.</summary>
    /// <param name="peerKey">The peer key.</param>
    /// <returns>The peer-key hash, or 0 when unidentified.</returns>
    private static long ComputePeerKeyHash(NetworkPeerKey peerKey)
    {
        if(peerKey.IsUnidentified)
        {
            return 0;
        }

        long seed = peerKey.Tag.TryGet<NetworkPeerKeyKind>(out NetworkPeerKeyKind kind) ? (long)(int)kind : 0;

        return unchecked((long)XxHash3.HashToUInt64(peerKey.Bytes.Span, seed));
    }
}
