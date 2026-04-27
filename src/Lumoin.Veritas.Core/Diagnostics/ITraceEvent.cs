using System;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// Marker interface implemented by all structured trace events emitted
/// through a <see cref="TraceHandler{TEvent}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Events carry a monotonic <see cref="SequenceNumber"/> within a single
/// trace stream and a <see cref="TimestampTicks"/> in
/// <see cref="DateTime.Ticks"/> units for ordering, replay, and
/// correlation with external telemetry. Per-subsystem concrete event
/// types add their own payload fields.
/// </para>
/// <para>
/// The <see cref="CorrelationId"/> links events belonging to the same
/// logical operation — a single validation run, a single query execution.
/// Consumers merging multiple trace streams (SHACL + RDFS + SPARQL) use
/// it to reassemble per-operation views.
/// </para>
/// <para>
/// Implementations should be <c>readonly record struct</c> so that
/// emitting an event is allocation-free when paired with the <c>in</c>
/// parameter on <see cref="TraceHandler{TEvent}"/>.
/// </para>
/// </remarks>
public interface ITraceEvent
{
    /// <summary>
    /// Monotonically increasing sequence number within a single trace
    /// stream. The emitter assigns this before passing the event to the
    /// handler. Consumers use it for deterministic replay and ordering
    /// independent of timestamp resolution.
    /// </summary>
    long SequenceNumber { get; }

    /// <summary>
    /// UTC timestamp of the event in <see cref="DateTime.Ticks"/>.
    /// Stored as <see cref="long"/> to avoid defensive copies when
    /// the event is passed by <c>in</c>.
    /// </summary>
    long TimestampTicks { get; }

    /// <summary>
    /// Correlation identifier for the logical operation producing this
    /// event. Events from the same validation run or query execution
    /// share the same value.
    /// </summary>
    Guid CorrelationId { get; }
}
