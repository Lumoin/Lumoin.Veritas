using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Replication;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The replication fault injector applies its deterministic plan per call: drop returns an empty image without
/// fetching, corrupt mutates the fetched bytes, fail throws, and pass returns the image unchanged; a delayed fault
/// waits on the injected clock. These are the adversities a distributed-repair certification drives.
/// </summary>
[TestClass]
internal sealed class FaultInjectingSketchFetchTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The plan selects drop, corrupt, fail, then pass on successive calls, and each is applied as specified.</summary>
    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public async Task PlanAppliesDropCorruptFailAndPassPerCall()
    {
        using VeritasMemoryPool<byte> pool = new();
        byte[] payload = [1, 2, 3];
        int calls = 0;
        AsyncSketchFetchDelegate inner = (symbolBudget, token) =>
        {
            calls++;

            return new ValueTask<SketchFetchResult>(SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, 0, payload, pool));
        };
        SketchFetchFaultPlan plan = callIndex => callIndex switch
        {
            1 => SketchFetchFault.Drop,
            2 => SketchFetchFault.Corrupt,
            3 => SketchFetchFault.Fail,
            _ => SketchFetchFault.Pass,
        };
        FaultInjectingSketchFetch injector = new(inner, plan, pool, TimeProvider.System);

        using(SketchFetchResult dropped = await injector.FetchAsync(64, TestContext.CancellationToken).ConfigureAwait(false))
        {
            Assert.IsTrue(dropped.IsUnavailable, "Drop returns an unavailable result.");
            Assert.AreEqual(0, calls, "Drop skips the inner fetch.");
        }

        using(SketchFetchResult corrupted = await injector.FetchAsync(64, TestContext.CancellationToken).ConfigureAwait(false))
        {
            Assert.AreEqual(1, calls, "Corrupt runs the inner fetch.");
            Assert.AreEqual(payload.Length, corrupted.Image.Length, "Corrupt preserves the length, so it is distinct from a drop.");
            Assert.IsFalse(corrupted.Image.Span.SequenceEqual(payload), "Corrupt mutates the fetched bytes.");
        }

        await Assert.ThrowsExactlyAsync<InjectedNetworkFaultException>(() => injector.FetchAsync(64, TestContext.CancellationToken).AsTask()).ConfigureAwait(false);

        using SketchFetchResult passed = await injector.FetchAsync(64, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(passed.Image.Span.SequenceEqual(payload), "Pass returns the inner image unchanged.");
    }

    /// <summary>A delayed fault waits on the injected clock before applying, so a scenario is deterministic without a wall-clock wait.</summary>
    [TestMethod]
    public async Task DelayedFaultWaitsOnTheInjectedClock()
    {
        using VeritasMemoryPool<byte> pool = new();
        FakeTimeProvider clock = new();
        byte[] payload = [9];
        AsyncSketchFetchDelegate inner = (symbolBudget, token) => new ValueTask<SketchFetchResult>(SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, 0, payload, pool));
        TimeSpan latency = TimeSpan.FromMilliseconds(500);
        SketchFetchFaultPlan plan = callIndex => SketchFetchFault.After(latency, SketchFetchFaultKind.Pass);
        FaultInjectingSketchFetch injector = new(inner, plan, pool, clock);

        Task<SketchFetchResult> pending = injector.FetchAsync(64, TestContext.CancellationToken).AsTask();
        Assert.IsFalse(pending.IsCompleted, "The fetch waits on the injected latency.");

        clock.Advance(latency);
        using SketchFetchResult result = await pending.ConfigureAwait(false);
        Assert.IsTrue(result.Image.Span.SequenceEqual(payload), "After the latency the fetch passes through.");
    }
}
