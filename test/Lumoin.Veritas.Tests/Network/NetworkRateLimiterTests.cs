using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Tests.Network;

/// <summary>
/// The per-peer rate limiter: a burst up to the bucket capacity is permitted, the next call past an empty bucket
/// with no queue is denied (never permitted), and each peer has its own bucket. Replenishment is disabled and the
/// queue is empty so the burst boundary is deterministic without depending on wall-clock timing.
/// </summary>
[TestClass]
internal sealed class NetworkRateLimiterTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Consults the limiter for a replication-fetch call naming <paramref name="peer"/>.</summary>
    /// <param name="limiter">The limiter to consult.</param>
    /// <param name="peer">The peer key the call names.</param>
    /// <returns>The verdict.</returns>
    private ValueTask<NetworkGovernanceDecision> DecideAsync(NetworkRateLimiter limiter, NetworkPeerKey peer)
    {
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundReplicationFetch, null, peer, OperationSizeHint: 0, PartitionCoordinate: -1);

        return limiter.Decide(request, TestContext.CancellationToken);
    }

    /// <summary>A burst up to the bucket capacity is permitted, the next call past an empty bucket with no queue is denied (the cap, not a proceed-after-delay), and a different peer draws on its own bucket.</summary>
    [TestMethod]
    public async Task BurstOverTheBucketIsDeniedPerPeer()
    {
        using NetworkRateLimiter limiter = new(tokenLimit: 2, tokensPerPeriod: 1, replenishmentPeriod: TimeSpan.FromMinutes(1), queueLimit: 0, autoReplenishment: false);
        using VeritasMemoryPool<byte> pool = new();
        using NetworkPeerKey peerA = NetworkPeerKey.RentReplicaId(pool, [1]);
        using NetworkPeerKey peerB = NetworkPeerKey.RentReplicaId(pool, [2]);

        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(limiter, peerA).ConfigureAwait(false)).Kind, "The first token is permitted.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(limiter, peerA).ConfigureAwait(false)).Kind, "The second token is permitted.");
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(limiter, peerA).ConfigureAwait(false)).Kind, "The third call past the empty bucket with no queue is denied — the cap is enforced, not deferred.");

        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(limiter, peerB).ConfigureAwait(false)).Kind, "A different peer draws on its own bucket.");
    }
}
