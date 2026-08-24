using System;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Diagnostics;

/// <summary>
/// Trace events emitted by the SHACL validator.
/// </summary>
/// <remarks>
/// <para>
/// This struct models the closed union of SHACL trace events as a tagged
/// struct: <see cref="Kind"/> discriminates which variant the instance
/// represents, and the per-variant payload fields are either populated
/// or defaulted depending on <see cref="Kind"/>. The factory methods
/// (<see cref="FocusNodeSelected"/>, <see cref="ConstraintStarted"/>,
/// etc.) enforce correct population; direct construction via the
/// <c>new</c> keyword is technically possible but discouraged.
/// </para>
/// <para>
/// The struct is <c>readonly record struct</c> so emitting an event
/// allocates nothing: construction happens on the stack, and the
/// <c>in</c>-parameter delegate signature of
/// <see cref="TraceHandler{TEvent}"/> passes by reference.
/// </para>
/// <para>
/// <b>Consumer pattern.</b> A handler pattern-matches on
/// <see cref="Kind"/>:
/// </para>
/// <code>
/// TraceHandler&lt;ShaclTraceEvent&gt; handler = (in ShaclTraceEvent evt) =&gt;
/// {
///     switch (evt.Kind)
///     {
///         case ShaclTraceEventKind.FocusNodeSelected:
///             HighlightFocusNode(evt.FocusNodeId, evt.ShapeId);
///             break;
///         case ShaclTraceEventKind.ValidationResultProduced:
///             UpdateResultsPanel(evt.FocusNodeId, evt.ConstraintIri!, evt.Severity);
///             break;
///     }
/// };
/// </code>
/// </remarks>
public readonly record struct ShaclTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The event-kind discriminator.</summary>
    public ShaclTraceEventKind Kind { get; init; }

    /// <summary>
    /// The focus node under evaluation. Populated for all event kinds
    /// except where the event is non-focus-specific. A focus node may
    /// be an IRI, blank node, or literal — the wrapper is
    /// <see cref="TermId"/>, the general handle without a kind
    /// constraint.
    /// </summary>
    public TermId FocusNodeId { get; init; }

    /// <summary>
    /// The shape currently being applied. Populated for all event
    /// kinds. Most SHACL shapes are IRIs, but the spec permits
    /// blank-node (anonymous / inline) shapes too — the wrapper is
    /// <see cref="TermId"/>, matching <see cref="Shape.Id"/>'s
    /// permissiveness on this point.
    /// </summary>
    public TermId ShapeId { get; init; }

    /// <summary>
    /// The value node associated with the event, for variants that have
    /// one (e.g. <see cref="ShaclTraceEventKind.ValidationResultProduced"/>).
    /// <see cref="TermId.None"/> when the variant does not carry a
    /// value node. Value nodes may be IRIs, blank nodes, or literals
    /// — the wrapper is <see cref="TermId"/>.
    /// </summary>
    public TermId ValueNodeId { get; init; }

    /// <summary>
    /// The constraint-component IRI when the event relates to a specific
    /// constraint (started / completed / result / not-implemented).
    /// <c>null</c> when the event does not relate to a constraint (focus
    /// node selection).
    /// </summary>
    public string? ConstraintIri { get; init; }

    /// <summary>
    /// The status at completion. Meaningful only when
    /// <see cref="Kind"/> is <see cref="ShaclTraceEventKind.ConstraintEvaluationCompleted"/>.
    /// </summary>
    public ConstraintEvaluationStatus Status { get; init; }

    /// <summary>
    /// The severity of the produced validation result. Meaningful only
    /// when <see cref="Kind"/> is
    /// <see cref="ShaclTraceEventKind.ValidationResultProduced"/>.
    /// </summary>
    public Severity Severity { get; init; }

    /// <summary>
    /// Creates a <see cref="ShaclTraceEventKind.FocusNodeSelected"/> event.
    /// </summary>
    /// <param name="sequenceNumber">Monotonically increasing event sequence number.</param>
    /// <param name="timestampTicks">UTC ticks at event creation.</param>
    /// <param name="correlationId">Correlation id linking events from the same validation run.</param>
    /// <param name="focusNodeId">The focus node handle under evaluation.</param>
    /// <param name="shapeId">The shape handle being applied to <paramref name="focusNodeId"/>.</param>
    public static ShaclTraceEvent FocusNodeSelected(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        TermId focusNodeId,
        TermId shapeId)
        => new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = ShaclTraceEventKind.FocusNodeSelected,
            FocusNodeId = focusNodeId,
            ShapeId = shapeId
        };

    /// <summary>
    /// Creates a <see cref="ShaclTraceEventKind.ConstraintEvaluationStarted"/> event.
    /// </summary>
    /// <param name="sequenceNumber">Monotonically increasing event sequence number.</param>
    /// <param name="timestampTicks">UTC ticks at event creation.</param>
    /// <param name="correlationId">Correlation id linking events from the same validation run.</param>
    /// <param name="focusNodeId">The focus node handle under evaluation.</param>
    /// <param name="shapeId">The shape handle being applied to <paramref name="focusNodeId"/>.</param>
    /// <param name="constraintIri">The constraint-component IRI string.</param>
    public static ShaclTraceEvent ConstraintStarted(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        TermId focusNodeId,
        TermId shapeId,
        string constraintIri)
        => new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = ShaclTraceEventKind.ConstraintEvaluationStarted,
            FocusNodeId = focusNodeId,
            ShapeId = shapeId,
            ConstraintIri = constraintIri
        };

    /// <summary>
    /// Creates a <see cref="ShaclTraceEventKind.ConstraintEvaluationCompleted"/> event.
    /// </summary>
    /// <param name="sequenceNumber">Monotonically increasing event sequence number.</param>
    /// <param name="timestampTicks">UTC ticks at event creation.</param>
    /// <param name="correlationId">Correlation id linking events from the same validation run.</param>
    /// <param name="focusNodeId">The focus node handle under evaluation.</param>
    /// <param name="shapeId">The shape handle being applied to <paramref name="focusNodeId"/>.</param>
    /// <param name="constraintIri">The constraint-component IRI string.</param>
    /// <param name="status">Constraint pass/fail/skip outcome.</param>
    public static ShaclTraceEvent ConstraintCompleted(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        TermId focusNodeId,
        TermId shapeId,
        string constraintIri,
        ConstraintEvaluationStatus status)
        => new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = ShaclTraceEventKind.ConstraintEvaluationCompleted,
            FocusNodeId = focusNodeId,
            ShapeId = shapeId,
            ConstraintIri = constraintIri,
            Status = status
        };

    /// <summary>
    /// Creates a <see cref="ShaclTraceEventKind.ValidationResultProduced"/> event.
    /// </summary>
    /// <param name="sequenceNumber">Monotonically increasing event sequence number.</param>
    /// <param name="timestampTicks">UTC ticks at event creation.</param>
    /// <param name="correlationId">Correlation id linking events from the same validation run.</param>
    /// <param name="focusNodeId">The focus node handle under evaluation.</param>
    /// <param name="shapeId">The shape handle being applied to <paramref name="focusNodeId"/>.</param>
    /// <param name="constraintIri">The constraint-component IRI string.</param>
    /// <param name="valueNodeId">The value node handle the result refers to, or <see cref="TermId.None"/> when none.</param>
    /// <param name="severity">Severity of the produced validation result.</param>
    public static ShaclTraceEvent ResultProduced(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        TermId focusNodeId,
        TermId shapeId,
        string constraintIri,
        TermId valueNodeId,
        Severity severity)
        => new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = ShaclTraceEventKind.ValidationResultProduced,
            FocusNodeId = focusNodeId,
            ShapeId = shapeId,
            ConstraintIri = constraintIri,
            ValueNodeId = valueNodeId,
            Severity = severity
        };

    /// <summary>
    /// Creates a <see cref="ShaclTraceEventKind.ConstraintNotImplemented"/> event.
    /// </summary>
    /// <param name="sequenceNumber">Monotonically increasing event sequence number.</param>
    /// <param name="timestampTicks">UTC ticks at event creation.</param>
    /// <param name="correlationId">Correlation id linking events from the same validation run.</param>
    /// <param name="focusNodeId">The focus node handle under evaluation.</param>
    /// <param name="shapeId">The shape handle being applied to <paramref name="focusNodeId"/>.</param>
    /// <param name="constraintIri">The constraint-component IRI string the validator does not implement.</param>
    public static ShaclTraceEvent ConstraintNotImplemented(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        TermId focusNodeId,
        TermId shapeId,
        string constraintIri)
        => new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = ShaclTraceEventKind.ConstraintNotImplemented,
            FocusNodeId = focusNodeId,
            ShapeId = shapeId,
            ConstraintIri = constraintIri
        };
}
