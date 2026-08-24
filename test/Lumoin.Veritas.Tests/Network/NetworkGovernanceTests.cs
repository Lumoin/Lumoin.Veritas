using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Tests.Network;

/// <summary>
/// The network-governance seam contracts: the always-permit default permits every boundary (asynchronously, so a
/// host can later return a verdict that round-trips remote hardware), the decision factories carry the right kind
/// and back-off, and the governance trace event satisfies the diagnostics-bus contract.
/// </summary>
[TestClass]
internal sealed class NetworkGovernanceTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The unconfigured default permits a request at every boundary.</summary>
    [TestMethod]
    public async Task AlwaysPermitPermitsEveryBoundary()
    {
        foreach(NetworkBoundary boundary in Enum.GetValues<NetworkBoundary>())
        {
            NetworkGovernanceRequest request = new(boundary, null, NetworkPeerKey.None, OperationSizeHint: 0, PartitionCoordinate: -1);
            NetworkGovernanceDecision decision = await NetworkGovernance.AlwaysPermit(request, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(NetworkGovernanceKind.Permit, decision.Kind, $"The default must permit at the {boundary} boundary.");
            Assert.AreEqual(TimeSpan.Zero, decision.RetryAfter, "A permit carries no back-off.");
        }
    }

    /// <summary>The Permit and Deny defaults and the Delay factory carry the matching kind and back-off.</summary>
    [TestMethod]
    public void DecisionFactoriesCarryTheirKindAndBackoff()
    {
        Assert.AreEqual(NetworkGovernanceKind.Permit, NetworkGovernanceDecision.Permit.Kind);
        Assert.AreEqual(TimeSpan.Zero, NetworkGovernanceDecision.Permit.RetryAfter);
        Assert.AreEqual(NetworkGovernanceKind.Deny, NetworkGovernanceDecision.Deny.Kind);

        NetworkGovernanceDecision delay = NetworkGovernanceDecision.Delay(TimeSpan.FromMilliseconds(250));
        Assert.AreEqual(NetworkGovernanceKind.Delay, delay.Kind);
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), delay.RetryAfter);
    }

    /// <summary>A negative back-off is a contract violation.</summary>
    [TestMethod]
    public void DelayRejectsANegativeBackoff()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkGovernanceDecision.Delay(TimeSpan.FromSeconds(-1)));
    }

    /// <summary>The pooled peer-key factories own a copy of their bytes, tag them with the right kind, the unidentified default reads as such and carries no kind, and an empty payload is refused — the self-describing handle never holds a kind without its bytes.</summary>
    [TestMethod]
    public void PeerKeyFactoriesTagTheirBytesAndRejectEmpty()
    {
        using VeritasMemoryPool<byte> pool = new();
        byte[] bytes = [1, 2, 3, 4];

        using(NetworkPeerKey replica = NetworkPeerKey.RentReplicaId(pool, bytes))
        {
            Assert.AreEqual(NetworkPeerKeyKind.ReplicaId, replica.Tag.Get<NetworkPeerKeyKind>(), "The tag must carry the replica-id kind.");
            Assert.IsTrue(replica.Bytes.Span.SequenceEqual(bytes), "The key must own a copy of its bytes.");
            Assert.IsFalse(replica.IsUnidentified, "A rented key identifies a peer.");
        }

        using(NetworkPeerKey endpoint = NetworkPeerKey.RentEndpointIri(pool, bytes))
        {
            Assert.AreEqual(NetworkPeerKeyKind.EndpointIri, endpoint.Tag.Get<NetworkPeerKeyKind>());
        }

        using(NetworkPeerKey socket = NetworkPeerKey.RentSocketAddress(pool, bytes))
        {
            Assert.AreEqual(NetworkPeerKeyKind.SocketAddress, socket.Tag.Get<NetworkPeerKeyKind>());
        }

        Assert.IsTrue(NetworkPeerKey.None.IsUnidentified, "None is the unidentified peer.");
        Assert.IsFalse(NetworkPeerKey.None.Tag.TryGet<NetworkPeerKeyKind>(out _), "None carries no kind.");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkPeerKey.RentReplicaId(pool, ReadOnlySpan<byte>.Empty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkPeerKey.RentEndpointIri(pool, ReadOnlySpan<byte>.Empty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkPeerKey.RentSocketAddress(pool, ReadOnlySpan<byte>.Empty));
    }

    /// <summary>The governance trace event implements the diagnostics-bus contract and carries its boundary and verdict.</summary>
    [TestMethod]
    public void TraceEventCarriesBoundaryAndOutcome()
    {
        NetworkGovernanceTraceEvent evt = new(SequenceNumber: 7, TimestampTicks: 123, CorrelationId: Guid.Empty, NetworkBoundary.OutboundServiceQuery, NetworkGovernanceKind.Deny, PeerKeyHash: 0, RetryAfterTicks: 0);

        Assert.AreEqual(7L, ((ITraceEvent)evt).SequenceNumber, "The event must surface its sequence number through the bus contract.");
        Assert.AreEqual(NetworkBoundary.OutboundServiceQuery, evt.Boundary);
        Assert.AreEqual(NetworkGovernanceKind.Deny, evt.Outcome);
    }
}
