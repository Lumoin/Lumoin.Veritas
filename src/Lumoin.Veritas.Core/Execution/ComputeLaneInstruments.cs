using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Registers the <c>veritas.compute_lane.*</c> observable instruments on
/// an OpenTelemetry <see cref="Meter"/>, reading a lane's live state so a
/// dashboard answers "is the lane the bottleneck" without a hunt. The
/// registration is decoupled from the lane — the lane only exposes its
/// observable properties, and the host wires them to its meter — so any
/// <see cref="IComputeLane"/> implementation is observable the same way.
/// </summary>
/// <remarks>
/// <para>
/// Worker count and queue depth are gauges (up-down counters); turns and
/// sheds are monotonic counters. Queue depth is reported per priority
/// class via a tagged measurement set. The turn-duration histogram
/// (<see cref="VeritasMetrics.ComputeLaneTurnDuration"/>) is push-based
/// and recorded at the turn-execution site, not here.
/// </para>
/// </remarks>
public static class ComputeLaneInstruments
{
    /// <summary>
    /// Registers the compute-lane observable instruments on
    /// <paramref name="meter"/>, observing <paramref name="lane"/>. The
    /// instruments live with the meter; disposing the meter unregisters
    /// them.
    /// </summary>
    /// <param name="meter">The meter the instruments are created on.</param>
    /// <param name="lane">The lane the instruments observe.</param>
    /// <exception cref="ArgumentNullException"><paramref name="meter"/> or <paramref name="lane"/> is <c>null</c>.</exception>
    public static void Register(Meter meter, IComputeLane lane)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(lane);

        meter.CreateObservableUpDownCounter(
            VeritasMetrics.ComputeLaneWorkers,
            () => lane.WorkerCount,
            "workers",
            "Current compute-lane worker count.");

        meter.CreateObservableUpDownCounter(
            VeritasMetrics.ComputeLaneQueueDepth,
            () => EnumerateQueueDepths(lane),
            "items",
            "Queued compute-lane work depth per priority class.");

        meter.CreateObservableCounter(
            VeritasMetrics.ComputeLaneTurnsTotal,
            () => lane.TurnsCompleted,
            "turns",
            "Total compute-lane turns completed.");

        meter.CreateObservableCounter(
            VeritasMetrics.ComputeLaneShedTotal,
            () => lane.ShedCount,
            "admissions",
            "Total compute-lane admissions shed.");
    }

    /// <summary>Yields the current queued depth of each defined priority class as a class-tagged measurement.</summary>
    /// <param name="lane">The lane to read.</param>
    /// <returns>One measurement per defined priority class.</returns>
    private static IEnumerable<Measurement<int>> EnumerateQueueDepths(IComputeLane lane)
    {
        foreach(ComputeWorkClass workClass in ComputeWorkClass.All)
        {
            yield return new Measurement<int>(
                lane.QueueDepthOf(workClass),
                new KeyValuePair<string, object?>("class", ComputeWorkClassNames.GetName(workClass)));
        }
    }
}
