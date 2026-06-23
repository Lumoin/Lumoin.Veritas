using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
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

    /// <summary>The tagged peer-key factories carry their kind and bytes verbatim, the unidentified default reads as such, and an empty payload is refused — the self-describing handle never silently holds a kind without its bytes.</summary>
    [TestMethod]
    public void PeerKeyFactoriesTagTheirBytesAndRejectEmpty()
    {
        byte[] bytes = [1, 2, 3, 4];
        Assert.AreEqual(NetworkPeerKeyKind.ReplicaId, NetworkPeerKey.ReplicaId(bytes).Kind);
        Assert.AreEqual(NetworkPeerKeyKind.EndpointIri, NetworkPeerKey.EndpointIri(bytes).Kind);
        Assert.AreEqual(NetworkPeerKeyKind.SocketAddress, NetworkPeerKey.SocketAddress(bytes).Kind);
        Assert.IsTrue(NetworkPeerKey.ReplicaId(bytes).Bytes.Span.SequenceEqual(bytes), "The key must carry its bytes verbatim.");

        Assert.IsTrue(NetworkPeerKey.None.IsUnidentified, "None is the unidentified peer.");
        Assert.AreEqual(NetworkPeerKeyKind.Unidentified, NetworkPeerKey.None.Kind);
        Assert.IsFalse(NetworkPeerKey.ReplicaId(bytes).IsUnidentified, "An identified key is not unidentified.");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkPeerKey.ReplicaId(ReadOnlyMemory<byte>.Empty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkPeerKey.EndpointIri(ReadOnlyMemory<byte>.Empty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NetworkPeerKey.SocketAddress(ReadOnlyMemory<byte>.Empty));
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
