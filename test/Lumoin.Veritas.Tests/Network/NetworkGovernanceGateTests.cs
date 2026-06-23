using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Network;

/// <summary>
/// The governance gate: it consults the policy, emits the verdict on the diagnostics channel, and honors it — a
/// permit proceeds, a deny declines, a delay backs off against the injected clock then proceeds — and the emitted
/// event carries the boundary, the verdict, the back-off, and a stable peer-key hash.
/// </summary>
[TestClass]
internal sealed class NetworkGovernanceGateTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Captures network-governance trace events through a method group, so a test body holds no closure over the list.</summary>
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

    /// <summary>A policy that always returns the same verdict, bound as a network-governance delegate.</summary>
    /// <param name="decision">The verdict to return.</param>
    /// <returns>A delegate returning <paramref name="decision"/> immediately.</returns>
    private static NetworkGovernanceDelegate Always(NetworkGovernanceDecision decision)
    {
        return (request, cancellationToken) => new ValueTask<NetworkGovernanceDecision>(decision);
    }

    /// <summary>A permit lets the call proceed and is emitted.</summary>
    [TestMethod]
    public async Task PermitProceedsAndEmits()
    {
        GovernanceTraceCapture trace = new();
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundServiceQuery, null, NetworkPeerKey.None, OperationSizeHint: 0, PartitionCoordinate: -1);

        bool entered = await NetworkGovernanceGate.TryEnterAsync(Always(NetworkGovernanceDecision.Permit), request, TimeProvider.System, trace.Capture, Guid.Empty, 0, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(entered, "A permit lets the call proceed.");
        Assert.AreEqual(NetworkGovernanceKind.Permit, trace.Events.Single().Outcome);
    }

    /// <summary>A deny declines the call and is emitted.</summary>
    [TestMethod]
    public async Task DenyDeclinesAndEmits()
    {
        GovernanceTraceCapture trace = new();
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundServiceQuery, null, NetworkPeerKey.None, OperationSizeHint: 0, PartitionCoordinate: -1);

        bool entered = await NetworkGovernanceGate.TryEnterAsync(Always(NetworkGovernanceDecision.Deny), request, TimeProvider.System, trace.Capture, Guid.Empty, 0, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(entered, "A deny makes the caller decline.");
        Assert.AreEqual(NetworkGovernanceKind.Deny, trace.Events.Single().Outcome);
    }

    /// <summary>A delay backs off against the injected clock, then proceeds; the emitted event carries the back-off.</summary>
    [TestMethod]
    public async Task DelayBacksOffAgainstTheClockThenProceeds()
    {
        FakeTimeProvider clock = new();
        GovernanceTraceCapture trace = new();
        TimeSpan backoff = TimeSpan.FromMilliseconds(250);
        NetworkGovernanceRequest request = new(NetworkBoundary.OutboundReplicationFetch, null, NetworkPeerKey.None, OperationSizeHint: 0, PartitionCoordinate: -1);

        //The verdict is emitted synchronously before the back-off, so the event is captured before the clock advances.
        Task<bool> entering = NetworkGovernanceGate.TryEnterAsync(Always(NetworkGovernanceDecision.Delay(backoff)), request, clock, trace.Capture, Guid.Empty, 0, TestContext.CancellationToken).AsTask();
        clock.Advance(backoff);
        bool entered = await entering.ConfigureAwait(false);

        Assert.IsTrue(entered, "A delay proceeds after backing off.");
        NetworkGovernanceTraceEvent emitted = trace.Events.Single();
        Assert.AreEqual(NetworkGovernanceKind.Delay, emitted.Outcome);
        Assert.AreEqual(backoff.Ticks, emitted.RetryAfterTicks, "The event carries the back-off.");
    }

    /// <summary>The emitted peer-key hash is 0 for an unidentified key and non-zero for an identified one.</summary>
    [TestMethod]
    public async Task PeerKeyHashIsZeroForNoneAndNonZeroForAnIdentifiedKey()
    {
        using VeritasMemoryPool<byte> pool = new();

        GovernanceTraceCapture noneTrace = new();
        NetworkGovernanceRequest noneRequest = new(NetworkBoundary.InboundServe, null, NetworkPeerKey.None, OperationSizeHint: 0, PartitionCoordinate: -1);
        await NetworkGovernanceGate.TryEnterAsync(Always(NetworkGovernanceDecision.Permit), noneRequest, TimeProvider.System, noneTrace.Capture, Guid.Empty, 0, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0L, noneTrace.Events.Single().PeerKeyHash, "An unidentified key hashes to 0.");

        GovernanceTraceCapture keyTrace = new();
        using NetworkPeerKey key = NetworkPeerKey.RentReplicaId(pool, [9, 9, 9]);
        NetworkGovernanceRequest keyRequest = new(NetworkBoundary.OutboundReplicationFetch, null, key, OperationSizeHint: 0, PartitionCoordinate: -1);
        await NetworkGovernanceGate.TryEnterAsync(Always(NetworkGovernanceDecision.Permit), keyRequest, TimeProvider.System, keyTrace.Capture, Guid.Empty, 1, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreNotEqual(0L, keyTrace.Events.Single().PeerKeyHash, "An identified key hashes to a non-zero value.");
    }
}
