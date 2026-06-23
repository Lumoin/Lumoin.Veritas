using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Network;

/// <summary>
/// A per-peer rate-limiting <see cref="NetworkGovernanceDelegate"/>: a token bucket for each peer, over the BCL
/// <see cref="PartitionedRateLimiter{TResource}"/>. It is the rate/concurrency facet of network governance, the
/// sibling of <see cref="NetworkFirewall"/>. Acquiring a token permits the call; when the bucket is empty the
/// acquisition waits in a bounded queue for one to replenish and then permits, and only a full queue denies. The
/// cap is enforced by the bucket itself — the limiter never returns a <see cref="NetworkGovernanceKind.Delay"/>
/// verdict, because the gate honors a delay as "proceed after backing off" without re-acquiring a token, which
/// would admit every concurrent caller that found the same empty bucket and so breach the rate under load.
/// Acquiring then releasing a single token fits the gate's consult-then-proceed contract: the token bucket
/// replenishes over time, so the permit is not held across the call (concurrency limiting, which holds a lease for
/// the call's duration, is a later facet that needs the lease threaded through the decorator, not the gate).
/// </summary>
/// <remarks>
/// The rate is fixed at construction: the BCL limiter captures a partition's options when the partition is first
/// seen, so live rate tuning means recreating the limiter and swapping it behind the seam (the coarse-grained
/// live knob), whereas <see cref="NetworkFirewall"/> reconfigures in place (the fine-grained live knob).
/// </remarks>
public sealed class NetworkRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<NetworkGovernanceRequest> limiter;

    /// <summary>Creates a per-peer token-bucket limiter.</summary>
    /// <param name="tokenLimit">The bucket capacity per peer — the maximum burst; positive.</param>
    /// <param name="tokensPerPeriod">The tokens replenished each period; positive.</param>
    /// <param name="replenishmentPeriod">The replenishment period; positive.</param>
    /// <param name="queueLimit">How many over-budget calls per peer may wait for a token rather than be denied; 0 denies immediately once the bucket is empty. Not negative.</param>
    /// <param name="autoReplenishment">Whether the limiter replenishes on its own timer; <see langword="false"/> requires manual replenishment (used to make tests deterministic).</param>
    /// <exception cref="ArgumentOutOfRangeException">A positive-required count is not positive, the queue limit is negative, or the period is not positive.</exception>
    public NetworkRateLimiter(int tokenLimit, int tokensPerPeriod, TimeSpan replenishmentPeriod, int queueLimit = 0, bool autoReplenishment = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokensPerPeriod);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(replenishmentPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(queueLimit);

        TokenLimit = tokenLimit;
        TokensPerPeriod = tokensPerPeriod;
        ReplenishmentPeriod = replenishmentPeriod;
        QueueCapacity = queueLimit;
        AutoReplenishment = autoReplenishment;
        OptionsFactory = CreateOptions;
        limiter = PartitionedRateLimiter.Create<NetworkGovernanceRequest, string>(Partition);
        Decide = Evaluate;
    }

    /// <summary>The governance delegate the gate consults; permits while the peer's bucket has tokens. Bind this at the transport decorator.</summary>
    public NetworkGovernanceDelegate Decide { get; }

    /// <summary>The bucket capacity per peer.</summary>
    private int TokenLimit { get; }

    /// <summary>The tokens replenished each period.</summary>
    private int TokensPerPeriod { get; }

    /// <summary>The replenishment period.</summary>
    private TimeSpan ReplenishmentPeriod { get; }

    /// <summary>How many over-budget calls per peer may wait for a token rather than be denied.</summary>
    private int QueueCapacity { get; }

    /// <summary>Whether the limiter replenishes on its own timer.</summary>
    private bool AutoReplenishment { get; }

    /// <summary>The cached options factory, so resolving a partition allocates no per-call delegate.</summary>
    private Func<string, TokenBucketRateLimiterOptions> OptionsFactory { get; }

    /// <summary>Disposes the underlying partitioned limiter and all its per-peer buckets.</summary>
    public void Dispose()
    {
        limiter.Dispose();
    }

    /// <summary>Acquires one token for the call's peer: a token (now, or after a bounded wait in the queue) permits it; a full queue denies it. The token is consumed when acquired, so the cap is enforced by the bucket — the limiter never returns a delay.</summary>
    /// <param name="request">The call being governed.</param>
    /// <param name="cancellationToken">The token that cancels the acquisition or its queued wait.</param>
    /// <returns>A permit when a token was acquired, otherwise a deny.</returns>
    private async ValueTask<NetworkGovernanceDecision> Evaluate(NetworkGovernanceRequest request, CancellationToken cancellationToken)
    {
        using RateLimitLease lease = await limiter.AcquireAsync(request, permitCount: 1, cancellationToken).ConfigureAwait(false);

        return lease.IsAcquired
            ? NetworkGovernanceDecision.Permit
            : NetworkGovernanceDecision.Deny;
    }

    /// <summary>Resolves the per-peer token-bucket partition for a call, keyed by the peer's content.</summary>
    /// <param name="request">The call being governed.</param>
    /// <returns>The peer's rate-limit partition.</returns>
    private RateLimitPartition<string> Partition(NetworkGovernanceRequest request)
    {
        return RateLimitPartition.GetTokenBucketLimiter(PartitionKey(request.PeerKey), OptionsFactory);
    }

    /// <summary>Builds the token-bucket options from this limiter's fixed configuration; the partition key is ignored, every peer's bucket shares the configuration.</summary>
    /// <param name="partitionKey">The partition key; unused.</param>
    /// <returns>The token-bucket options.</returns>
    private TokenBucketRateLimiterOptions CreateOptions(string partitionKey)
    {
        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = TokenLimit,
            TokensPerPeriod = TokensPerPeriod,
            ReplenishmentPeriod = ReplenishmentPeriod,
            AutoReplenishment = AutoReplenishment,
            QueueLimit = QueueCapacity,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }

    /// <summary>The per-peer partition key: the peer's kind and byte content, or a single shared key for unidentified peers.</summary>
    /// <param name="peer">The peer key.</param>
    /// <returns>The partition key.</returns>
    private static string PartitionKey(NetworkPeerKey peer)
    {
        return peer.IsUnidentified
            ? string.Empty
            : $"{(int)peer.Tag.Get<NetworkPeerKeyKind>()}:{Convert.ToHexString(peer.Bytes.Span)}";
    }
}
