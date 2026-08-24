using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The governed replication fetch: a permitted fetch runs the inner fetch and returns its image, while a denied
/// fetch returns an empty image without running the inner fetch — the value-based decline the session reads as an
/// unavailable peer — and each verdict is emitted on the diagnostics channel against the replication boundary.
/// </summary>
[TestClass]
internal sealed class GovernedSketchFetchTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Captures network-governance trace events through a method group.</summary>
    private sealed class GovernanceTraceCapture
    {
        /// <summary>The captured events, in emission order.</summary>
        public List<NetworkGovernanceTraceEvent> Events { get; } = [];

        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in NetworkGovernanceTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>A permitted fetch returns the inner image and a denial added at runtime returns empty without fetching; the verdicts are emitted against the replication boundary.</summary>
    [TestMethod]
    public async Task PermitFetchesAndDenyReturnsEmptyWithoutFetching()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        GovernanceTraceCapture trace = new();
        byte[] replica = [1, 2, 3];
        byte[] payload = [10, 20, 30];
        NetworkPeerKey peer = NetworkPeerKey.RentReplicaId(pool, replica);

        int calls = 0;
        AsyncSketchFetchDelegate inner = (symbolBudget, token) =>
        {
            calls++;

            return new ValueTask<SketchFetchResult>(SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, 0, payload, pool));
        };
        using GovernedSketchFetch governed = new(inner, firewall.Decide, peer, null, TimeProvider.System, trace.Capture);

        using(SketchFetchResult permitted = await governed.FetchAsync(128, TestContext.CancellationToken).ConfigureAwait(false))
        {
            Assert.IsTrue(permitted.Image.Span.SequenceEqual(payload), "A permitted fetch returns the inner image.");
        }

        Assert.AreEqual(1, calls, "The inner fetch ran once on a permit.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, trace.Events[^1].Outcome, "The permit was emitted.");
        Assert.AreEqual(NetworkBoundary.OutboundReplicationFetch, trace.Events[^1].Boundary, "The verdict names the replication boundary.");

        firewall.Deny(NetworkPeerKeyKind.ReplicaId, replica);

        using SketchFetchResult denied = await governed.FetchAsync(128, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(denied.IsUnavailable, "A denied fetch returns an unavailable result the session reads as an unavailable peer.");
        Assert.AreEqual(1, calls, "The inner fetch did not run on a deny.");
        Assert.AreEqual(NetworkGovernanceKind.Deny, trace.Events[^1].Outcome, "The deny was emitted.");
    }
}
