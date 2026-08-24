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
            Assert.HasCount(size, buffer.Memory, $"Buffer size should be exactly {size} bytes.");
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
                Assert.HasCount(bufferSize, buffer.Memory);
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
            Assert.HasCount(1, buffer.Memory);
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
        Assert.HasCount(64, buffer.Memory);
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
        Assert.HasCount(64, afterTrim.Memory, "Rent should work after TrimExcess reclaims slabs.");
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

        //The pool parents a rental's activity to Activity.Current, so a marker
        //around this test's rental scopes the assertion to activities THIS
        //rental would create: concurrent tests legitimately produce pool
        //activities on the shared source, but only one inside the marker's
        //trace would betray the disabled flag.
        using Activity marker = new("TracingCanBeDisabledScope");
        marker.Start();

        using(pool.Rent(32)) { }

        marker.Stop();

        foreach(Activity activity in activities)
        {
            Assert.AreNotEqual(marker.TraceId, activity.TraceId,
                "No activity should originate from this test's rental when tracing is disabled.");
        }
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
                //RecordObservableInstruments invokes every observable callback synchronously on this
                //thread, so the recorded values are readable the moment it returns.
                listener.RecordObservableInstruments();

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

    /// <summary>Every observable pool instrument reports measurements tagged with its own pool's process-unique instance identity, so a metrics consumer can attribute a measurement among many pools sharing the instrument names — and the efficiency observable reports the exact rented-segment percentage.</summary>
    [TestMethod]
    public void ObservableMeasurementsCarryThePoolInstanceTag()
    {
        using Meter meterA = new(VeritasMetrics.MeterName, "1.0.0");
        using Meter meterB = new(VeritasMetrics.MeterName, "1.0.0");
        using VeritasMemoryPool<byte> poolA = new(meterA);
        using VeritasMemoryPool<byte> poolB = new(meterB);

        Assert.AreNotEqual(poolA.InstanceId, poolB.InstanceId, "Two pool instances mint distinct process-unique identities.");

        using TaggedObservableProbe probe = new(meterA, meterB);
        using IMemoryOwner<byte> rental = poolA.Rent(100);
        probe.Snapshot();

        string[] observables =
        [
            VeritasMetrics.MemoryPoolTotalSlabs,
            VeritasMetrics.MemoryPoolTotalMemoryAllocated,
            VeritasMetrics.MemoryPoolActiveRentals,
            VeritasMetrics.MemoryPoolAllocationEfficiency
        ];
        foreach(string name in observables)
        {
            Assert.AreEqual(poolA.InstanceId, probe.InstanceTag(meterA, name), $"{name} on the first pool carries its own instance tag.");
            Assert.AreEqual(poolB.InstanceId, probe.InstanceTag(meterB, name), $"{name} on the second pool carries its own instance tag.");
        }

        //One rented segment of the DefaultCapacityStrategy(100) segments the slab allocated,
        //computed with the same operations the observable performs, so equality is exact.
        Assert.AreEqual(1.0, probe.Value(meterA, VeritasMetrics.MemoryPoolActiveRentals), "The renting pool reports its one active rental.");
        Assert.AreEqual((double)1 / VeritasMemoryPool<byte>.DefaultCapacityStrategy(100) * 100.0, probe.Value(meterA, VeritasMetrics.MemoryPoolAllocationEfficiency), "The renting pool reports the exact rented-segment percentage.");
        Assert.AreEqual(0.0, probe.Value(meterB, VeritasMetrics.MemoryPoolActiveRentals), "The idle pool reports no rentals.");
        Assert.AreEqual(0.0, probe.Value(meterB, VeritasMetrics.MemoryPoolAllocationEfficiency), "The idle pool reports zero efficiency.");
    }

    /// <summary>Collects the latest observable measurement per instrument, with its pool-instance tag, for exactly two meters under test.</summary>
    private sealed class TaggedObservableProbe : IDisposable
    {
        /// <summary>The listener the observables report through.</summary>
        private readonly MeterListener listener;

        /// <summary>The first observed meter.</summary>
        private readonly Meter first;

        /// <summary>The second observed meter.</summary>
        private readonly Meter second;

        /// <summary>The latest value and instance tag per reporting instrument.</summary>
        private readonly Dictionary<Instrument, (double Value, int? InstanceTag)> observed = [];

        /// <summary>Starts the listener over both meters' instruments.</summary>
        /// <param name="first">The first observed meter.</param>
        /// <param name="second">The second observed meter.</param>
        public TaggedObservableProbe(Meter first, Meter second)
        {
            this.first = first;
            this.second = second;
            listener = new MeterListener();
            listener.InstrumentPublished = OnInstrumentPublished;
            listener.SetMeasurementEventCallback<int>(OnIntMeasurement);
            listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
            listener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
            listener.Start();
        }

        /// <summary>Enables measurement events for both observed meters' instruments.</summary>
        /// <param name="instrument">The published instrument.</param>
        /// <param name="meterListener">The listener to enable on.</param>
        private void OnInstrumentPublished(Instrument instrument, MeterListener meterListener)
        {
            if(instrument.Meter == first || instrument.Meter == second)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        }

        /// <summary>Records one observed measurement.</summary>
        /// <param name="instrument">The reporting instrument.</param>
        /// <param name="measurement">The observed value.</param>
        /// <param name="tags">The measurement tags.</param>
        /// <param name="state">The enablement state (unused).</param>
        private void OnIntMeasurement(Instrument instrument, int measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            Record(instrument, measurement, tags);
        }

        /// <summary>Records one observed measurement.</summary>
        /// <param name="instrument">The reporting instrument.</param>
        /// <param name="measurement">The observed value.</param>
        /// <param name="tags">The measurement tags.</param>
        /// <param name="state">The enablement state (unused).</param>
        private void OnLongMeasurement(Instrument instrument, long measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            Record(instrument, measurement, tags);
        }

        /// <summary>Records one observed measurement.</summary>
        /// <param name="instrument">The reporting instrument.</param>
        /// <param name="measurement">The observed value.</param>
        /// <param name="tags">The measurement tags.</param>
        /// <param name="state">The enablement state (unused).</param>
        private void OnDoubleMeasurement(Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            Record(instrument, measurement, tags);
        }

        /// <summary>Stores the measurement and its instance tag, replacing any earlier reading of the instrument.</summary>
        /// <param name="instrument">The reporting instrument.</param>
        /// <param name="value">The observed value.</param>
        /// <param name="tags">The measurement tags.</param>
        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            int? instanceTag = null;
            foreach(KeyValuePair<string, object?> tag in tags)
            {
                if(string.Equals(tag.Key, VeritasMetrics.MemoryPoolInstanceTag, StringComparison.Ordinal) && tag.Value is int id)
                {
                    instanceTag = id;
                }
            }

            observed[instrument] = (value, instanceTag);
        }

        /// <summary>Invokes every observable callback synchronously so the recorded values are current.</summary>
        public void Snapshot()
        {
            listener.RecordObservableInstruments();
        }

        /// <summary>The instance tag the named instrument's latest measurement carried on the given meter.</summary>
        /// <param name="meter">The meter the instrument belongs to.</param>
        /// <param name="instrumentName">The instrument name.</param>
        /// <returns>The instance tag.</returns>
        /// <exception cref="InvalidOperationException">No measurement was recorded for the instrument, or it carried no instance tag.</exception>
        public int InstanceTag(Meter meter, string instrumentName)
        {
            (_, int? instanceTag) = Find(meter, instrumentName);

            return instanceTag ?? throw new InvalidOperationException($"The instrument '{instrumentName}' reported no instance tag.");
        }

        /// <summary>The named instrument's latest measured value on the given meter.</summary>
        /// <param name="meter">The meter the instrument belongs to.</param>
        /// <param name="instrumentName">The instrument name.</param>
        /// <returns>The value.</returns>
        public double Value(Meter meter, string instrumentName)
        {
            (double value, _) = Find(meter, instrumentName);

            return value;
        }

        /// <summary>Finds the latest reading of the named instrument on the given meter.</summary>
        /// <param name="meter">The meter the instrument belongs to.</param>
        /// <param name="instrumentName">The instrument name.</param>
        /// <returns>The reading.</returns>
        /// <exception cref="InvalidOperationException">No measurement was recorded for the instrument.</exception>
        private (double Value, int? InstanceTag) Find(Meter meter, string instrumentName)
        {
            foreach(KeyValuePair<Instrument, (double Value, int? InstanceTag)> entry in observed)
            {
                if(entry.Key.Meter == meter && string.Equals(entry.Key.Name, instrumentName, StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }

            throw new InvalidOperationException($"The instrument '{instrumentName}' reported no measurement.");
        }

        /// <summary>Stops the listener.</summary>
        public void Dispose()
        {
            listener.Dispose();
        }
    }

    [TestMethod]
    public async Task ConcurrentRentAndDisposeAcrossThreads()
    {
        using VeritasMemoryPool<byte> pool = new();

        Task<int>[] tasks = new Task<int>[50];
        for(int i = 0; i < tasks.Length; i++)
        {
            int captured = i;
            tasks[i] = Task.Run(async () =>
            {
                IMemoryOwner<byte> owner = pool.Rent((captured % 8 + 1) * 16);
                owner.Memory.Span.Fill((byte)(captured % 256));

                //Yield to force potential thread switches.
                await Task.Yield();

                int length = owner.Memory.Length;
                owner.Dispose();

                return length;
            });
        }

        int[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        Assert.HasCount(50, results, "All concurrent rent-dispose cycles should complete.");
    }
}
