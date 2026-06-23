using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Tests.Network;

/// <summary>
/// The live governance swap-holder folds an ordered policy chain: an empty chain permits, the first non-permit
/// verdict wins and the rest of the chain is not consulted, a later policy's deny is reached only when the earlier
/// ones permit, and a hot-swapped chain governs the next call.
/// </summary>
[TestClass]
internal sealed class LiveNetworkGovernanceTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A policy that always returns the same verdict.</summary>
    /// <param name="decision">The verdict to return.</param>
    /// <returns>A delegate returning <paramref name="decision"/> immediately.</returns>
    private static NetworkGovernanceDelegate Always(NetworkGovernanceDecision decision)
    {
        return (request, cancellationToken) => new ValueTask<NetworkGovernanceDecision>(decision);
    }

    /// <summary>A governance request for an unidentified peer at the replication boundary.</summary>
    /// <returns>The request.</returns>
    private static NetworkGovernanceRequest Request()
    {
        return new NetworkGovernanceRequest(NetworkBoundary.OutboundReplicationFetch, null, NetworkPeerKey.None, OperationSizeHint: 0, PartitionCoordinate: -1);
    }

    /// <summary>An empty chain permits every call.</summary>
    [TestMethod]
    public async Task EmptyChainPermits()
    {
        LiveNetworkGovernance live = new();

        NetworkGovernanceDecision decision = await live.Decide(Request(), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(NetworkGovernanceKind.Permit, decision.Kind, "An empty chain permits.");
    }

    /// <summary>The first non-permit verdict wins and the rest of the chain is not consulted.</summary>
    [TestMethod]
    public async Task DenyFirstShortCircuitsAndSkipsTheRest()
    {
        int laterCalls = 0;
        NetworkGovernanceDelegate later = (request, cancellationToken) =>
        {
            laterCalls++;

            return new ValueTask<NetworkGovernanceDecision>(NetworkGovernanceDecision.Permit);
        };
        LiveNetworkGovernance live = new(Always(NetworkGovernanceDecision.Deny), later);

        NetworkGovernanceDecision decision = await live.Decide(Request(), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(NetworkGovernanceKind.Deny, decision.Kind, "The first non-permit wins.");
        Assert.AreEqual(0, laterCalls, "A deny short-circuits, so the rest of the chain is not consulted.");
    }

    /// <summary>A later policy's deny is reached when the earlier ones permit, and every policy permitting yields a permit.</summary>
    [TestMethod]
    public async Task LaterDenyReachedWhenEarlierPermitAndAllPermitYieldsPermit()
    {
        LiveNetworkGovernance laterDeny = new(Always(NetworkGovernanceDecision.Permit), Always(NetworkGovernanceDecision.Deny));
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await laterDeny.Decide(Request(), TestContext.CancellationToken).ConfigureAwait(false)).Kind, "A later deny is reached once earlier policies permit.");

        LiveNetworkGovernance allPermit = new(Always(NetworkGovernanceDecision.Permit), Always(NetworkGovernanceDecision.Permit));
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await allPermit.Decide(Request(), TestContext.CancellationToken).ConfigureAwait(false)).Kind, "Every policy permitting yields a permit.");
    }

    /// <summary>A hot-swapped chain governs the next call.</summary>
    [TestMethod]
    public async Task HotSwapIsObservedOnTheNextCall()
    {
        LiveNetworkGovernance live = new(Always(NetworkGovernanceDecision.Permit));
        Assert.AreEqual(NetworkGovernanceKind.Permit, (await live.Decide(Request(), TestContext.CancellationToken).ConfigureAwait(false)).Kind, "Permits before the swap.");

        live.SetPolicies(Always(NetworkGovernanceDecision.Deny));
        Assert.AreEqual(NetworkGovernanceKind.Deny, (await live.Decide(Request(), TestContext.CancellationToken).ConfigureAwait(false)).Kind, "The swapped-in chain governs the next call.");
    }
}
