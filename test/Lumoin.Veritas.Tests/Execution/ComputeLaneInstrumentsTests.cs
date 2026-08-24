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
/// The <c>veritas.compute_lane.*</c> observable instruments report the
/// lane's live state through an OpenTelemetry meter: worker count, total
/// turns, total sheds, and per-class queue depth. Pulled deterministically
/// through a <see cref="MeterListener"/> after the lane has reached a known
/// state.
/// </summary>
[TestClass]
internal sealed class ComputeLaneInstrumentsTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Builds a non-browser environment snapshot.</summary>
    /// <param name="processorCount">The logical processor count.</param>
    /// <returns>The environment snapshot.</returns>
    private static ExecutionEnvironment Env(int processorCount)
    {
        return new ExecutionEnvironment(processorCount, null, false, null);
    }

    [TestMethod]
    public async Task InstrumentsReportLiveLaneState()
    {
        FakeTimeProvider time = new();
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 2, ComputeQueueCapacity = 16 };
        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        await using var laneScope = lane.ConfigureAwait(false);

        for(int i = 0; i < 3; i++)
        {
            lane.Admit(ComputeWorkClass.ViewBuild, _ => ValueTask.CompletedTask);
        }

        //Reach a known state before pulling the instruments.
        await WaitForTurnsCompletedAsync(lane, 3, TestContext.CancellationToken).ConfigureAwait(false);

        using Meter meter = new(VeritasMetrics.MeterName, "1.0.0");
        ComputeLaneInstruments.Register(meter, lane);

        int workers = -1;
        long turns = -1;
        long shed = -1;
        Dictionary<string, int> queueDepthByClass = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if(instrument.Meter == meter)
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };

        //Worker count and per-class queue depth are int instruments; turns and sheds are long counters.
        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            if(instrument.Name == VeritasMetrics.ComputeLaneWorkers)
            {
                workers = measurement;

                return;
            }

            if(instrument.Name == VeritasMetrics.ComputeLaneQueueDepth)
            {
                foreach(KeyValuePair<string, object?> tag in tags)
                {
                    if(tag.Key == "class" && tag.Value is string className)
                    {
                        queueDepthByClass[className] = measurement;
                    }
                }
            }
        });
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            switch(instrument.Name)
            {
                case VeritasMetrics.ComputeLaneTurnsTotal:
                    turns = measurement;
                    break;

                case VeritasMetrics.ComputeLaneShedTotal:
                    shed = measurement;
                    break;

                default:
                    break;
            }
        });
        listener.Start();
        listener.RecordObservableInstruments();

        //Worker count is the resolved width.
        Assert.AreEqual(2, workers);
        Assert.AreEqual(3L, turns);
        Assert.AreEqual(0L, shed);

        //At least the six built-in classes are reported (a consumer-created class adds more), and
        //the drained queue is empty.
        Assert.IsGreaterThanOrEqualTo(6, queueDepthByClass.Count);
        Assert.AreEqual(0, queueDepthByClass["view_build"]);
        Assert.AreEqual(0, queueDepthByClass["scrub"]);
    }

    /// <summary>
    /// Awaits until the lane has completed at least <paramref name="expected"/>
    /// turns, driven by the lane's state-transition signal and bounded by the
    /// test token — no wall-clock spin, so it is robust under thread-pool load.
    /// </summary>
    /// <param name="lane">The lane under test.</param>
    /// <param name="expected">The completed-turn count to await.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task that completes once the lane has run the expected turns.</returns>
    private static async Task WaitForTurnsCompletedAsync(ThreadedComputeLane lane, long expected, CancellationToken cancellationToken)
    {
        while(true)
        {
            Task advanced = lane.ObserveStateTransition();
            if(lane.TurnsCompleted >= expected)
            {
                return;
            }

            await advanced.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
