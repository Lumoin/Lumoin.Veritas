using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A structured trace event carrying one order's <see cref="ColumnarStatistics"/>
/// scalar summary on the diagnostics <see cref="TraceHandler{TEvent}"/> channel.
/// Scalar-only, so emitting it is allocation-free; the full per-level fan-out
/// distribution stays in <see cref="ColumnarStatistics"/>.
/// </summary>
public readonly record struct ColumnarStatisticsTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The order's permutation index.</summary>
    public int Permutation { get; init; }

    /// <summary>The triple count.</summary>
    public long TripleCount { get; init; }

    /// <summary>Distinct level-0 values.</summary>
    public int Level0Count { get; init; }

    /// <summary>Distinct level-0/level-1 pairs.</summary>
    public int Level1Count { get; init; }

    /// <summary>The leaf value count.</summary>
    public int Level2Count { get; init; }

    /// <summary>Mean level-0 → level-1 fan-out.</summary>
    public double Level0FanOutMean { get; init; }

    /// <summary>Max level-0 → level-1 fan-out.</summary>
    public int Level0FanOutMax { get; init; }

    /// <summary>Mean level-1 → level-2 fan-out.</summary>
    public double Level1FanOutMean { get; init; }

    /// <summary>Max level-1 → level-2 fan-out.</summary>
    public int Level1FanOutMax { get; init; }

    /// <summary>The order's total packed footprint in bits per triple.</summary>
    public double BitsPerTriple { get; init; }

    /// <summary>Builds an event from one order's statistics.</summary>
    /// <param name="sequenceNumber">The monotonic stream sequence number.</param>
    /// <param name="timestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units.</param>
    /// <param name="correlationId">The logical-operation correlation id.</param>
    /// <param name="statistics">The order statistics to carry.</param>
    /// <returns>The trace event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="statistics"/> is <see langword="null"/>.</exception>
    public static ColumnarStatisticsTraceEvent ForOrder(long sequenceNumber, long timestampTicks, Guid correlationId, ColumnarStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        return new ColumnarStatisticsTraceEvent
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Permutation = statistics.Permutation,
            TripleCount = statistics.TripleCount,
            Level0Count = statistics.Level0Count,
            Level1Count = statistics.Level1Count,
            Level2Count = statistics.Level2Count,
            Level0FanOutMean = statistics.Level0FanOut.Mean,
            Level0FanOutMax = statistics.Level0FanOut.Max,
            Level1FanOutMean = statistics.Level1FanOut.Mean,
            Level1FanOutMax = statistics.Level1FanOut.Max,
            BitsPerTriple = statistics.BitsPerTriple,
        };
    }
}
