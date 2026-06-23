using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Tests.Network;

/// <summary>
/// The governance control surface drives firewall and throttle live behind one bound delegate: blocking and
/// unblocking a peer take effect on the next call; a firewall-denied peer short-circuits before the throttle, so it
/// consumes no token; and attaching, removing, or replacing the throttle is observed on the next call.
/// </summary>
[TestClass]
internal sealed class NetworkGovernanceControllerTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Consults the controller for a replication-fetch call naming <paramref name="peer"/>.</summary>
    /// <param name="controller">The controller to consult.</param>
    /// <param name="peer">The peer key the call names.</param>
    /// <returns>The verdict.</returns>
    private ValueTask<NetworkGovernanceDecision> DecideAsync(NetworkGovernanceController controller, NetworkPeerKey peer)
    {
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundReplicationFetch, null, peer, OperationSizeHint: 0, PartitionCoordinate: -1);

        return controller.Decide(request, TestContext.CancellationToken);
    }

    /// <summary>Blocking a peer denies it from the next call, and unblocking permits it again.</summary>
    [TestMethod]
    public async Task FirewallControlTakesEffectLive()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkGovernanceController controller = new();
        byte[] peerId = [1];
        using NetworkPeerKey peer = NetworkPeerKey.RentReplicaId(pool, peerId);

        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Permitted before any block.");

        controller.Deny(NetworkPeerKeyKind.ReplicaId, peerId);
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Blocked on the next call.");

        controller.Allow(NetworkPeerKeyKind.ReplicaId, peerId);
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Unblocked on the next call.");
    }

    /// <summary>A firewall-denied peer short-circuits before the throttle and consumes no token, so its budget is intact once unblocked.</summary>
    [TestMethod]
    public async Task DeniedPeerShortCircuitsAndKeepsItsTokens()
    {
        using VeritasMemoryPool<byte> pool = new();
        using NetworkRateLimiter throttle = new(tokenLimit: 1, tokensPerPeriod: 1, replenishmentPeriod: TimeSpan.FromMinutes(1), queueLimit: 0, autoReplenishment: false);
        NetworkGovernanceController controller = new(throttle);
        byte[] peerId = [2];
        using NetworkPeerKey peer = NetworkPeerKey.RentReplicaId(pool, peerId);

        controller.Deny(NetworkPeerKeyKind.ReplicaId, peerId);
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "The firewall denies.");
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Still denied — the throttle was never reached.");

        controller.Allow(NetworkPeerKeyKind.ReplicaId, peerId);
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "The single token is intact: the denied calls consumed none.");
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Now the throttle denies — the bucket is spent.");
    }

    /// <summary>Attaching a throttle, then removing it, is observed on the next call.</summary>
    [TestMethod]
    public async Task ThrottleAttachAndRemoveAreLive()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkGovernanceController controller = new();
        byte[] peerId = [3];
        using NetworkPeerKey peer = NetworkPeerKey.RentReplicaId(pool, peerId);

        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "No throttle: permitted.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "No throttle: still permitted.");

        using NetworkRateLimiter throttle = new(tokenLimit: 1, tokensPerPeriod: 1, replenishmentPeriod: TimeSpan.FromMinutes(1), queueLimit: 0, autoReplenishment: false);
        controller.UseThrottle(throttle);
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Throttle attached: the first token permits.");
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Throttle attached: the bucket is spent.");

        controller.RemoveThrottle();
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(controller, peer).ConfigureAwait(false)).Kind, "Throttle removed: permitted again.");
    }
}
