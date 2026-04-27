using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Memory;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lumoin.Veritas.Tests.MemoryPool;

[TestClass]
internal sealed class VeritasMemoryPoolTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RentReturnsExactBufferSize()
    {
        using VeritasMemoryPool<byte> pool = new();

        int[] testSizes = [1, 16, 32, 64, 128, 256, 512, 1024];

        foreach(int size in testSizes)
        {
            using IMemoryOwner<byte> buffer = pool.Rent(size);
            Assert.AreEqual(size, buffer.Memory.Length, $"Buffer size should be exactly {size} bytes.");
        }
    }

    [TestMethod]
    public void RentReusesSlabsForSameSize()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int bufferSize = 64;
        const int rentCount = 10;

        List<IMemoryOwner<byte>> buffers = [];

        try
        {
            for(int i = 0; i < rentCount; i++)
            {
                buffers.Add(pool.Rent(bufferSize));
            }

            foreach(IMemoryOwner<byte> buffer in buffers)
            {
                Assert.AreEqual(bufferSize, buffer.Memory.Length);
            }
        }
        finally
        {
            foreach(IMemoryOwner<byte> buffer in buffers)
            {
                buffer.Dispose();
            }
        }
    }

    [TestMethod]
    public void DisposePreventsAccess()
    {
        using VeritasMemoryPool<byte> pool = new();
        IMemoryOwner<byte> buffer = pool.Rent(32);

        buffer.Memory.Span.Fill(0xFF);
        buffer.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = buffer.Memory);
    }

    [TestMethod]
    public void DoubleDisposeIsIdempotent()
    {
        using VeritasMemoryPool<byte> pool = new();
        IMemoryOwner<byte> buffer = pool.Rent(32);

        buffer.Memory.Span.Fill(0xFF);
        buffer.Dispose();

        //Second dispose should not throw.
        buffer.Dispose();
    }

    [TestMethod]
    public void RentHandlesEdgeCases()
    {
        using VeritasMemoryPool<byte> pool = new();

        using(IMemoryOwner<byte> buffer = pool.Rent(1))
        {
            Assert.AreEqual(1, buffer.Memory.Length);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pool.Rent(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => pool.Rent(-1));
    }

    [TestMethod]
    public void RentThrowsWhenPoolDisposed()
    {
        VeritasMemoryPool<byte> pool = new();
        pool.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => pool.Rent(32));
    }

    [TestMethod]
    public void DisposingRentalAfterPoolDisposedDoesNotThrow()
    {
        VeritasMemoryPool<byte> pool = new();
        IMemoryOwner<byte> owner = pool.Rent(32);
        owner.Memory.Span.Fill(0xCC);

        //Disposing the pool clears all slabs while a rental is still active.
        pool.Dispose();

        //The rental's Dispose calls Pool.Return on an already-disposed slab.
        //This must not throw.
        owner.Dispose();
    }

    [TestMethod]
    public void TrimExcessThrowsWhenPoolDisposed()
    {
        VeritasMemoryPool<byte> pool = new();
        pool.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => pool.TrimExcess());
    }

    [TestMethod]
    public void SharedReturnsSingletonInstance()
    {
        VeritasMemoryPool<byte> first = VeritasMemoryPool<byte>.Shared;
        VeritasMemoryPool<byte> second = VeritasMemoryPool<byte>.Shared;

        Assert.AreSame(first, second, "Shared should return the same instance.");

        using IMemoryOwner<byte> buffer = first.Rent(64);
        Assert.AreEqual(64, buffer.Memory.Length);
    }

    [TestMethod]
    public void DefaultCapacityStrategyReturnsMoreSegmentsForSmallerSizes()
    {
        int smallCapacity = VeritasMemoryPool<byte>.DefaultCapacityStrategy(32);
        int mediumCapacity = VeritasMemoryPool<byte>.DefaultCapacityStrategy(128);
        int largeCapacity = VeritasMemoryPool<byte>.DefaultCapacityStrategy(8192);

        Assert.IsGreaterThan(mediumCapacity, smallCapacity,
            "Small buffers should get more segments per slab than medium buffers.");
        Assert.IsGreaterThan(largeCapacity, mediumCapacity,
            "Medium buffers should get more segments per slab than large buffers.");
    }

    [TestMethod]
    public void CustomCapacityStrategyIsUsed()
    {
        int strategyCallCount = 0;

        int CustomStrategy(int segmentSize)
        {
            Interlocked.Increment(ref strategyCallCount);
            return 2;
        }

        using Meter meter = new("Test", "1.0.0");
        using VeritasMemoryPool<byte> pool = new(
            meter,
            capacityStrategy: CustomStrategy);

        //Rent three buffers of the same size to force slab creation and overflow.
        using IMemoryOwner<byte> b1 = pool.Rent(32);
        using IMemoryOwner<byte> b2 = pool.Rent(32);
        using IMemoryOwner<byte> b3 = pool.Rent(32);

        //The strategy should have been invoked at least twice.
        Assert.IsGreaterThanOrEqualTo(2, strategyCallCount,
            $"Custom strategy should have been called at least twice, was called {strategyCallCount} times.");
    }

    [TestMethod]
    public void TrimExcessReclaimsUnusedSlabs()
    {
        using Meter meter = new("Test", "1.0.0");
        using VeritasMemoryPool<byte> pool = new(
            meter,
            capacityStrategy: _ => 2);

        //Hold three buffers simultaneously to force creation of a second slab.
        IMemoryOwner<byte> b1 = pool.Rent(32);
        IMemoryOwner<byte> b2 = pool.Rent(32);
        IMemoryOwner<byte> b3 = pool.Rent(32);

        //Return all buffers so both slabs become fully available.
        b1.Dispose();
        b2.Dispose();
        b3.Dispose();

        int reclaimed = pool.TrimExcess();
        Assert.IsGreaterThan(0, reclaimed, "TrimExcess should reclaim at least one unused slab.");
    }

    [TestMethod]
    public void TrimExcessDoesNotReclaimSlabsWithActiveRentals()
    {
        using Meter meter = new("Test", "1.0.0");
        using VeritasMemoryPool<byte> pool = new(
            meter,
            capacityStrategy: _ => 2);

        //Keep a rental alive so the slab cannot be reclaimed.
        using IMemoryOwner<byte> active = pool.Rent(32);

        int reclaimed = pool.TrimExcess();
        Assert.AreEqual(0, reclaimed, "TrimExcess should not reclaim slabs with active rentals.");
    }

    [TestMethod]
    public void RentWorksAfterTrimExcess()
    {
        using Meter meter = new("Test", "1.0.0");
        using VeritasMemoryPool<byte> pool = new(
            meter,
            capacityStrategy: _ => 2);

        //Create slabs, return everything, then trim.
        IMemoryOwner<byte> b1 = pool.Rent(64);
        IMemoryOwner<byte> b2 = pool.Rent(64);
        b1.Dispose();
        b2.Dispose();
        pool.TrimExcess();

        //Pool should create fresh slabs on demand after trimming.
        using IMemoryOwner<byte> afterTrim = pool.Rent(64);
        Assert.AreEqual(64, afterTrim.Memory.Length, "Rent should work after TrimExcess reclaims slabs.");
    }

    [TestMethod]
    public void TracingCanBeDisabled()
    {
        ConcurrentBag<Activity> activities = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "VeritasMemoryPool",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => activities.Add(activity)
        };

        ActivitySource.AddActivityListener(listener);

        using Meter meter = new("Test", "1.0.0");
        using VeritasMemoryPool<byte> pool = new(
            meter,
            tracingEnabled: false);

        using(pool.Rent(32)) { }

        Assert.IsEmpty(activities,
            "No activities should be created when tracing is disabled.");
    }

    [TestMethod]
    public async Task MetricsAreReportedCorrectly()
    {
        using Meter meter = new(VeritasMetrics.MeterName, "1.0.0");
        ConcurrentDictionary<string, long> reportedMetrics = new();

        using MeterListener listener = new();

        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if(instrument.Meter == meter)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            reportedMetrics.AddOrUpdate(instrument.Name, measurement, (_, _) => measurement);
        });

        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            reportedMetrics.AddOrUpdate(instrument.Name, measurement, (_, _) => measurement);
        });

        listener.Start();

        using VeritasMemoryPool<byte> pool = new(meter);

        using(pool.Rent(100))
        {
            using(pool.Rent(200))
            {
                listener.RecordObservableInstruments();
                await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.CancellationToken).ConfigureAwait(false);

                bool foundSlabs = reportedMetrics.TryGetValue(VeritasMetrics.MemoryPoolTotalSlabs, out long totalSlabs);
                Assert.IsTrue(foundSlabs, "TotalSlabs metric should be reported.");
                Assert.AreEqual(2, totalSlabs, "Should have created two slabs for different buffer sizes.");

                bool foundMemory = reportedMetrics.TryGetValue(VeritasMetrics.MemoryPoolTotalMemoryAllocated, out long totalMemory);
                Assert.IsTrue(foundMemory, "TotalMemoryAllocated metric should be reported.");

                int expectedCapacity100 = VeritasMemoryPool<byte>.DefaultCapacityStrategy(100);
                int expectedCapacity200 = VeritasMemoryPool<byte>.DefaultCapacityStrategy(200);
                long expectedMemory = (100 * expectedCapacity100) + (200 * expectedCapacity200);
                Assert.AreEqual(expectedMemory, totalMemory, "Total memory should match expected allocation.");
            }
        }
    }

    [TestMethod]
    public async Task ConcurrentRentAndDisposeAcrossThreads()
    {
        using VeritasMemoryPool<byte> pool = new();

        Task<int>[] tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            IMemoryOwner<byte> owner = pool.Rent((i % 8 + 1) * 16);
            owner.Memory.Span.Fill((byte)(i % 256));

            //Yield to force potential thread switches.
            await Task.Yield();

            int length = owner.Memory.Length;
            owner.Dispose();

            return length;
        }).ToArray();

        int[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        Assert.HasCount(50, results, "All concurrent rent-dispose cycles should complete.");
    }
}
