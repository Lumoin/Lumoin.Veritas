using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Core.Statistics;

/// <summary>
/// A structured trace event carrying a <see cref="GraphStatistics"/> summary on
/// the diagnostics <see cref="TraceHandler{TEvent}"/> channel — the store-agnostic
/// data-shape counterpart to a physical index summary. Scalar-only, so emitting
/// it is allocation-free.
/// </summary>
public readonly record struct GraphStatisticsTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The triple count.</summary>
    public long TripleCount { get; init; }

    /// <summary>The number of distinct subject terms.</summary>
    public int DistinctSubjects { get; init; }

    /// <summary>The number of distinct predicate terms.</summary>
    public int DistinctPredicates { get; init; }

    /// <summary>The number of distinct object terms.</summary>
    public int DistinctObjects { get; init; }

    /// <summary>The fewest triples sharing one subject.</summary>
    public int MinSubjectOutDegree { get; init; }

    /// <summary>The most triples sharing one subject.</summary>
    public int MaxSubjectOutDegree { get; init; }

    /// <summary>The mean triples per distinct subject.</summary>
    public double MeanSubjectOutDegree { get; init; }

    /// <summary>The triple count of the most frequent predicate.</summary>
    public int MaxPredicateFrequency { get; init; }

    /// <summary>Builds an event from a graph statistics summary.</summary>
    /// <param name="sequenceNumber">The monotonic stream sequence number.</param>
    /// <param name="timestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units.</param>
    /// <param name="correlationId">The logical-operation correlation id.</param>
    /// <param name="statistics">The statistics to carry.</param>
    /// <returns>The trace event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="statistics"/> is <see langword="null"/>.</exception>
    public static GraphStatisticsTraceEvent From(long sequenceNumber, long timestampTicks, Guid correlationId, GraphStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        return new GraphStatisticsTraceEvent
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            TripleCount = statistics.TripleCount,
            DistinctSubjects = statistics.DistinctSubjects,
            DistinctPredicates = statistics.DistinctPredicates,
            DistinctObjects = statistics.DistinctObjects,
            MinSubjectOutDegree = statistics.MinSubjectOutDegree,
            MaxSubjectOutDegree = statistics.MaxSubjectOutDegree,
            MeanSubjectOutDegree = statistics.MeanSubjectOutDegree,
            MaxPredicateFrequency = statistics.MaxPredicateFrequency,
        };
    }
}
