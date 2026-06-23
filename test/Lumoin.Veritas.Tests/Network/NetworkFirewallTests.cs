using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Tests.Network;

/// <summary>
/// The denylist firewall: it permits every peer until one is denied, denies exactly the denylisted peer while
/// permitting others, reinstates an allowed peer, clears, and observes a live denial on the very next call — the
/// real-time control-in an editor/MCP/CLI command drives.
/// </summary>
[TestClass]
internal sealed class NetworkFirewallTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Consults the firewall for a replication-fetch call naming <paramref name="peer"/>.</summary>
    /// <param name="firewall">The firewall to consult.</param>
    /// <param name="peer">The peer key the call names.</param>
    /// <returns>The verdict.</returns>
    private ValueTask<NetworkGovernanceDecision> DecideAsync(NetworkFirewall firewall, NetworkPeerKey peer)
    {
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundReplicationFetch, null, peer, OperationSizeHint: 0, PartitionCoordinate: -1);

        return firewall.Decide(request, TestContext.CancellationToken);
    }

    /// <summary>A fresh firewall permits every peer, identified or not.</summary>
    [TestMethod]
    public async Task DefaultPermitsEveryPeer()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        using NetworkPeerKey key = NetworkPeerKey.RentReplicaId(pool, [1, 2, 3]);

        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, key).ConfigureAwait(false)).Kind, "An un-denied peer is permitted.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, NetworkPeerKey.None).ConfigureAwait(false)).Kind, "An unidentified peer is permitted by a denylist firewall.");
    }

    /// <summary>The denylisted peer is denied; another peer is permitted.</summary>
    [TestMethod]
    public async Task DeniedPeerIsDeniedAndOthersPermitted()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        byte[] peerA = [1, 2, 3];
        byte[] peerB = [4, 5, 6];
        firewall.Deny(NetworkPeerKeyKind.ReplicaId, peerA);

        using NetworkPeerKey keyA = NetworkPeerKey.RentReplicaId(pool, peerA);
        using NetworkPeerKey keyB = NetworkPeerKey.RentReplicaId(pool, peerB);
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(firewall, keyA).ConfigureAwait(false)).Kind, "The denied peer is denied.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, keyB).ConfigureAwait(false)).Kind, "Another peer is permitted.");
    }

    /// <summary>Denying then allowing a peer reinstates it; clearing reinstates everyone.</summary>
    [TestMethod]
    public async Task AllowAndClearReinstatePeers()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        byte[] peerA = [1, 2, 3];
        byte[] peerB = [4, 5, 6];
        using NetworkPeerKey keyA = NetworkPeerKey.RentReplicaId(pool, peerA);
        using NetworkPeerKey keyB = NetworkPeerKey.RentReplicaId(pool, peerB);

        firewall.Deny(NetworkPeerKeyKind.ReplicaId, peerA);
        firewall.Allow(NetworkPeerKeyKind.ReplicaId, peerA);
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, keyA).ConfigureAwait(false)).Kind, "An allowed peer is reinstated.");

        firewall.Deny(NetworkPeerKeyKind.ReplicaId, peerA);
        firewall.Deny(NetworkPeerKeyKind.ReplicaId, peerB);
        firewall.Clear();
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, keyA).ConfigureAwait(false)).Kind, "Clear reinstates the first peer.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, keyB).ConfigureAwait(false)).Kind, "Clear reinstates the second peer.");
    }

    /// <summary>A denial added at runtime is observed on the next call to the same firewall — the live control-in.</summary>
    [TestMethod]
    public async Task LiveDenyIsObservedOnTheNextCall()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        byte[] peer = [7, 8, 9];
        using NetworkPeerKey key = NetworkPeerKey.RentReplicaId(pool, peer);

        Assert.AreEqual(NetworkGovernanceKind.Permit, (await DecideAsync(firewall, key).ConfigureAwait(false)).Kind, "Permitted before the denial.");
        firewall.Deny(NetworkPeerKeyKind.ReplicaId, peer);
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await DecideAsync(firewall, key).ConfigureAwait(false)).Kind, "Denied on the next call after a live denial.");
    }
}
