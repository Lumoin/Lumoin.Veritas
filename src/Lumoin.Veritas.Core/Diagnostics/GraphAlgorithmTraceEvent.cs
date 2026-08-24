using System;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>The kind discriminator for a <see cref="GraphAlgorithmTraceEvent"/>.</summary>
public enum GraphAlgorithmTraceEventKind
{
    /// <summary>A graph-analytics algorithm began running.</summary>
    Started,

    /// <summary>A graph-analytics algorithm finished, carrying the result row count.</summary>
    Completed,
}

/// <summary>
/// A trace event emitted around a graph-analytics algorithm run, on the same <see cref="TraceHandler{TEvent}"/>
/// bus as the query and inference events and joined to a surrounding operation by <see cref="CorrelationId"/>.
/// Consumers (the observability surface) filter the stream for these to attribute analytics cost and surface
/// progress. A <c>readonly record struct</c> so emitting is allocation-free with the <c>in</c>-parameter handler.
/// </summary>
public readonly record struct GraphAlgorithmTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The event-kind discriminator.</summary>
    public GraphAlgorithmTraceEventKind Kind { get; init; }

    /// <summary>The algorithm's catalog name (for example <c>pagerank</c>, <c>cliques</c>).</summary>
    public string Algorithm { get; init; }

    /// <summary>The number of result rows the run produced, populated for <see cref="GraphAlgorithmTraceEventKind.Completed"/>; <c>0</c> otherwise.</summary>
    public long ResultCount { get; init; }

    /// <summary>The run's wall-clock duration in <see cref="TimeSpan.Ticks"/>, populated for <see cref="GraphAlgorithmTraceEventKind.Completed"/>; <c>0</c> otherwise — the cost signal a consumer attributes to the algorithm.</summary>
    public long DurationTicks { get; init; }

    /// <summary>Constructs a <see cref="GraphAlgorithmTraceEventKind.Started"/> event.</summary>
    /// <param name="sequenceNumber">The monotonic sequence number within the trace stream.</param>
    /// <param name="timestampTicks">The UTC timestamp in <see cref="DateTime.Ticks"/>.</param>
    /// <param name="correlationId">The logical-operation correlation id.</param>
    /// <param name="algorithm">The algorithm's catalog name.</param>
    /// <returns>The started event.</returns>
    public static GraphAlgorithmTraceEvent Started(long sequenceNumber, long timestampTicks, Guid correlationId, string algorithm)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = GraphAlgorithmTraceEventKind.Started,
            Algorithm = algorithm,
            ResultCount = 0,
            DurationTicks = 0,
        };
    }

    /// <summary>Constructs a <see cref="GraphAlgorithmTraceEventKind.Completed"/> event carrying the result row count and run duration.</summary>
    /// <param name="sequenceNumber">The monotonic sequence number within the trace stream.</param>
    /// <param name="timestampTicks">The UTC timestamp in <see cref="DateTime.Ticks"/>.</param>
    /// <param name="correlationId">The logical-operation correlation id.</param>
    /// <param name="algorithm">The algorithm's catalog name.</param>
    /// <param name="resultCount">The number of result rows produced.</param>
    /// <param name="durationTicks">The run's wall-clock duration in <see cref="TimeSpan.Ticks"/>.</param>
    /// <returns>The completed event.</returns>
    public static GraphAlgorithmTraceEvent Completed(long sequenceNumber, long timestampTicks, Guid correlationId, string algorithm, long resultCount, long durationTicks)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = GraphAlgorithmTraceEventKind.Completed,
            Algorithm = algorithm,
            ResultCount = resultCount,
            DurationTicks = durationTicks,
        };
    }
}
