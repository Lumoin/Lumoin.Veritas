using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The compute lane records each completed turn's wall-clock duration through
/// the injected <see cref="RecordTurnDurationDelegate"/>, tagged by work
/// class, and <see cref="ComputeLaneInstruments.CreateTurnDurationRecorder"/>
/// turns that into the <c>veritas.compute_lane.turn_duration</c> histogram.
/// The duration is fractional milliseconds, so a sub-millisecond turn is not
/// lost to zero.
/// </summary>
[TestClass]
internal sealed class ComputeLaneTurnDurationTests
{
    /// <summary>The safety bound for waiting on the lane to reach a known state; a timeout means a real defect.</summary>
    private static readonly TimeSpan ConditionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Builds a non-browser environment snapshot.</summary>
    /// <param name="processorCount">The logical processor count.</param>
    /// <returns>The environment snapshot.</returns>
    private static ExecutionEnvironment Env(int processorCount)
    {
        return new ExecutionEnvironment(processorCount, null, false, null);
    }

    [TestMethod]
    public async Task LaneRecordsEachTurnDurationTaggedByClass()
    {
        FakeTimeProvider time = new();
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 2, ComputeQueueCapacity = 16 };

        List<(ComputeWorkClass WorkClass, double Milliseconds)> recorded = [];
        object recordLock = new();
        RecordTurnDurationDelegate recorder = (workClass, milliseconds) =>
        {
            lock(recordLock)
            {
                recorded.Add((workClass, milliseconds));
            }
        };

        ThreadedComputeLane lane = new(policy, () => Env(8), time, recorder);
        await using var laneScope = lane.ConfigureAwait(false);

        for(int i = 0; i < 3; i++)
        {
            lane.Admit(ComputeWorkClass.ViewBuild, _ => ValueTask.CompletedTask);
        }

        //The recorder runs before the turn count is bumped, so three completed turns means three recordings are in.
        Assert.IsTrue(SpinWait.SpinUntil(() => lane.TurnsCompleted == 3, ConditionTimeout), $"Expected three completed turns, saw {lane.TurnsCompleted}.");

        lock(recordLock)
        {
            Assert.HasCount(3, recorded);
            foreach((ComputeWorkClass workClass, double milliseconds) in recorded)
            {
                Assert.AreEqual(ComputeWorkClass.ViewBuild, workClass);
                Assert.IsGreaterThanOrEqualTo(0.0, milliseconds);
            }
        }
    }

    [TestMethod]
    public void RecorderRecordsFractionalDurationsToTheHistogramTaggedByClass()
    {
        using Meter meter = new(VeritasMetrics.MeterName, "1.0.0");
        RecordTurnDurationDelegate recorder = ComputeLaneInstruments.CreateTurnDurationRecorder(meter);

        List<(string Class, double Value)> measurements = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if(instrument.Meter == meter)
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if(instrument.Name != VeritasMetrics.ComputeLaneTurnDuration)
            {
                return;
            }

            string className = string.Empty;
            foreach(KeyValuePair<string, object?> tag in tags)
            {
                if(tag.Key == "class" && tag.Value is string name)
                {
                    className = name;
                }
            }

            measurements.Add((className, measurement));
        });
        listener.Start();

        //A multi-millisecond turn and a sub-millisecond one — the fractional value of the latter survives.
        recorder(ComputeWorkClass.ViewBuild, 3.5);
        recorder(ComputeWorkClass.Reasoning, 0.25);

        Assert.HasCount(2, measurements);
        Assert.Contains(("view_build", 3.5), measurements);
        Assert.Contains(("reasoning", 0.25), measurements);
    }
}
