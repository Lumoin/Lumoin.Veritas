using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class SweepTriggersTests
{
    public TestContext TestContext { get; set; } = null!;

    private static EncodedTriple[] SampleTriples { get; } =
    [
        EncodedTriple.FromEncoded(1, 10, 100),
        EncodedTriple.FromEncoded(1, 11, 200),
        EncodedTriple.FromEncoded(2, 10, 100),
        EncodedTriple.FromEncoded(2, 12, 300),
    ];

    [TestMethod]
    public void WatermarkRejectsNullStore()
    {
        Assert.Throws<ArgumentNullException>(
            () => SweepTriggers.Watermark(null!, nodeCountThreshold: 1, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void WatermarkRejectsNonPositiveThreshold()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweepTriggers.Watermark(store, nodeCountThreshold: 0, cancellationToken: TestContext.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweepTriggers.Watermark(store, nodeCountThreshold: -1, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void WatermarkRejectsNonPositivePollInterval()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweepTriggers.Watermark(store, nodeCountThreshold: 1, pollInterval: TimeSpan.Zero, cancellationToken: TestContext.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweepTriggers.Watermark(store, nodeCountThreshold: 1, pollInterval: TimeSpan.FromMilliseconds(-1), cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void ScheduledRejectsNullStore()
    {
        Assert.Throws<ArgumentNullException>(
            () => SweepTriggers.Scheduled(null!, TimeSpan.FromSeconds(1), cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public void ScheduledRejectsNonPositiveInterval()
    {
        using NodeStore store = new(VeritasHashing.Default);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweepTriggers.Scheduled(store, TimeSpan.Zero, cancellationToken: TestContext.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SweepTriggers.Scheduled(store, TimeSpan.FromMilliseconds(-1), cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task WatermarkTriggerFiresSweepWhenCountReachesThreshold()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        int threshold = store.Count;
        Assert.IsGreaterThan(0, threshold);

        FakeTimeProvider time = new();
        TimeSpan pollInterval = TimeSpan.FromMilliseconds(100);

        using IDisposable trigger = SweepTriggers.Watermark(
            store,
            threshold,
            pollInterval,
            time,
            TestContext.CancellationToken);

        time.Advance(pollInterval);

        bool fired = await WaitForConditionAsync(
            () => store.SweepCount >= 1,
            TimeSpan.FromSeconds(2),
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(fired, "Expected the watermark trigger to fire a sweep at least once.");
        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public async Task WatermarkTriggerDoesNotFireWhenCountIsBelowThreshold()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        //Threshold sits well above current count; no sweep should fire.
        int threshold = store.Count + 1000;

        FakeTimeProvider time = new();
        TimeSpan pollInterval = TimeSpan.FromMilliseconds(100);

        using IDisposable trigger = SweepTriggers.Watermark(
            store,
            threshold,
            pollInterval,
            time,
            TestContext.CancellationToken);

        for(int tick = 0; tick < 5; tick++)
        {
            time.Advance(pollInterval);
        }

        //Allow any racing continuations to run.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, store.SweepCount);
        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public async Task ScheduledTriggerFiresSweepEachInterval()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        FakeTimeProvider time = new();
        TimeSpan interval = TimeSpan.FromMilliseconds(100);

        using IDisposable trigger = SweepTriggers.Scheduled(
            store,
            interval,
            time,
            TestContext.CancellationToken);

        time.Advance(interval);
        bool firedOnce = await WaitForConditionAsync(
            () => store.SweepCount >= 1,
            TimeSpan.FromSeconds(2),
            TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(firedOnce, "Expected the scheduled trigger to fire after the first interval.");

        time.Advance(interval);
        bool firedTwice = await WaitForConditionAsync(
            () => store.SweepCount >= 2,
            TimeSpan.FromSeconds(2),
            TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(firedTwice, "Expected the scheduled trigger to fire after the second interval.");

        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public async Task DisposedTriggerStopsFiring()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        FakeTimeProvider time = new();
        TimeSpan interval = TimeSpan.FromMilliseconds(100);

        IDisposable trigger = SweepTriggers.Scheduled(
            store,
            interval,
            time,
            TestContext.CancellationToken);

        time.Advance(interval);
        bool fired = await WaitForConditionAsync(
            () => store.SweepCount >= 1,
            TimeSpan.FromSeconds(2),
            TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(fired);

        //Dispose blocks on the trigger's loop draining, so after
        //this returns no further sweeps can be scheduled by it.
        trigger.Dispose();
        int countAfterDispose = store.SweepCount;

        for(int tick = 0; tick < 5; tick++)
        {
            time.Advance(interval);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(countAfterDispose, store.SweepCount);
        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public void DisposingTriggerIsIdempotent()
    {
        using NodeStore store = new(VeritasHashing.Default);

        FakeTimeProvider time = new();
        IDisposable trigger = SweepTriggers.Scheduled(store, TimeSpan.FromSeconds(1), time, TestContext.CancellationToken);

        trigger.Dispose();
        trigger.Dispose();
        trigger.Dispose();
    }

    //Polls the given condition with a small real-time delay between
    //attempts; returns whether the condition was met before the
    //timeout. Uses real wall-clock for the polling delay because the
    //thing under test uses a fake clock — sleeping on the fake clock
    //here would deadlock the test.
    private static async Task<bool> WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        while(sw.Elapsed < timeout)
        {
            if(condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }

        return condition();
    }
}
