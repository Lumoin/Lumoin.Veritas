using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.Extensions.Time.Testing;

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

        IAsyncDisposable trigger = SweepTriggers.Watermark(
            store,
            threshold,
            pollInterval,
            time,
            TestContext.CancellationToken);
        await using(trigger.ConfigureAwait(false))
        {
            //Arm the round seam, deliver exactly one poll tick on the fake clock, and
            //await the loop finishing that round — deterministic, no polling, no wall clock.
            Task round = ((ISweepRoundObservation)trigger).ObserveRound();
            time.Advance(pollInterval);
            await round.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(1, store.SweepCount);
            Assert.IsNotNull(graphStore.Snapshot);
        }

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

        IAsyncDisposable trigger = SweepTriggers.Watermark(
            store,
            threshold,
            pollInterval,
            time,
            TestContext.CancellationToken);
        await using(trigger.ConfigureAwait(false))
        {
            //Five delivered-and-processed rounds, none crossing the threshold: awaiting
            //each round makes "the loop looked and declined" a deterministic fact.
            ISweepRoundObservation rounds = (ISweepRoundObservation)trigger;
            for(int tick = 0; tick < 5; tick++)
            {
                Task round = rounds.ObserveRound();
                time.Advance(pollInterval);
                await round.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }

            Assert.AreEqual(0, store.SweepCount);
            Assert.IsNotNull(graphStore.Snapshot);
        }

    }

    [TestMethod]
    public async Task ScheduledTriggerFiresSweepEachInterval()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        FakeTimeProvider time = new();
        TimeSpan interval = TimeSpan.FromMilliseconds(100);

        IAsyncDisposable trigger = SweepTriggers.Scheduled(
            store,
            interval,
            time,
            TestContext.CancellationToken);
        await using(trigger.ConfigureAwait(false))
        {
            ISweepRoundObservation rounds = (ISweepRoundObservation)trigger;

            Task firstRound = rounds.ObserveRound();
            time.Advance(interval);
            await firstRound.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(1, store.SweepCount);

            Task secondRound = rounds.ObserveRound();
            time.Advance(interval);
            await secondRound.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(2, store.SweepCount);

            Assert.IsNotNull(graphStore.Snapshot);
        }

    }

    [TestMethod]
    public async Task DisposedTriggerStopsFiring()
    {
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(SampleTriples, store, TestContext.CancellationToken).ConfigureAwait(false);

        FakeTimeProvider time = new();
        TimeSpan interval = TimeSpan.FromMilliseconds(100);

        IAsyncDisposable trigger = SweepTriggers.Scheduled(
            store,
            interval,
            time,
            TestContext.CancellationToken);

        Task round = ((ISweepRoundObservation)trigger).ObserveRound();
        time.Advance(interval);
        await round.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, store.SweepCount);

        //Disposal awaits the trigger's loop draining, so after this returns no
        //further sweeps can be scheduled by it: the disposed timer leaves later
        //clock advances inert and the count is immediately final.
        await trigger.DisposeAsync().ConfigureAwait(false);
        int countAfterDispose = store.SweepCount;

        for(int tick = 0; tick < 5; tick++)
        {
            time.Advance(interval);
        }

        Assert.AreEqual(countAfterDispose, store.SweepCount);
        Assert.IsNotNull(graphStore.Snapshot);
    }

    [TestMethod]
    public async Task DisposingTriggerIsIdempotent()
    {
        using NodeStore store = new(VeritasHashing.Default);

        FakeTimeProvider time = new();
        IAsyncDisposable trigger = SweepTriggers.Scheduled(store, TimeSpan.FromSeconds(1), time, TestContext.CancellationToken);

        await trigger.DisposeAsync().ConfigureAwait(false);
        await trigger.DisposeAsync().ConfigureAwait(false);
        await trigger.DisposeAsync().ConfigureAwait(false);
    }
}
