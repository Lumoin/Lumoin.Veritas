using System;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Tracing;

/// <summary>
/// Trace events emitted by the hypertrie query subsystem.
/// </summary>
/// <remarks>
/// <para>
/// This struct models the closed union of query trace events as a
/// tagged struct: <see cref="Kind"/> discriminates which variant
/// the instance represents, and the per-variant payload fields
/// are either populated or defaulted depending on
/// <see cref="Kind"/>. The factory methods
/// (<see cref="QueryStarted"/>, <see cref="IteratorOpened"/>, and
/// so on) enforce correct population; direct construction via the
/// <c>new</c> keyword is technically possible but discouraged.
/// </para>
/// <para>
/// The struct is <c>readonly record struct</c> so emitting an
/// event allocates nothing: construction happens on the stack,
/// and the <c>in</c>-parameter delegate signature of
/// <see cref="TraceHandler{TEvent}"/> passes by reference.
/// </para>
/// <para>
/// <b>Audit channel.</b> The <see cref="QueryTraceEventKind.AccessDenied"/>
/// kind is the audit channel for access-control denials. Consumers
/// implementing the audit pipeline filter the trace stream for
/// this kind and persist the events. The audit channel is
/// deliberately the same trace stream as everything else — there
/// is no separate audit pipe.
/// </para>
/// <para>
/// <b>Privacy note.</b> No <c>AccessNotFound</c> kind exists. A
/// <c>NotFound</c> access decision is observationally
/// indistinguishable from a real miss; emitting a trace event
/// would defeat that privacy guarantee. Operators wanting
/// audit visibility into denied accesses configure their policy
/// to return <c>Deny</c> instead of <c>NotFound</c>.
/// </para>
/// </remarks>
public readonly record struct QueryTraceEvent: ITraceEvent
{
    /// <inheritdoc/>
    public long SequenceNumber { get; init; }

    /// <inheritdoc/>
    public long TimestampTicks { get; init; }

    /// <inheritdoc/>
    public Guid CorrelationId { get; init; }

    /// <summary>The event-kind discriminator.</summary>
    public QueryTraceEventKind Kind { get; init; }

    /// <summary>
    /// Index of the triple pattern this event relates to within
    /// the parent <see cref="BasicGraphPattern"/>, or <c>-1</c>
    /// when not pattern-specific (for example, query-level
    /// events).
    /// </summary>
    public int PatternIndex { get; init; }

    /// <summary>
    /// The variable bound, advanced, or skipped by the event, or
    /// <c>default</c> when the event is not variable-specific.
    /// </summary>
    public Variable Variable { get; init; }

    /// <summary>
    /// The encoded term value at the position the event refers to
    /// (for example, the key the iterator advanced past, the
    /// subject of a denied triple). <c>0</c> when not applicable.
    /// </summary>
    public long Value { get; init; }

    /// <summary>
    /// The denied triple's positions, populated only for
    /// <see cref="QueryTraceEventKind.AccessDenied"/>. <c>0</c>
    /// otherwise.
    /// </summary>
    public long DeniedSubject { get; init; }

    /// <summary>The denied triple's predicate; see <see cref="DeniedSubject"/>.</summary>
    public long DeniedPredicate { get; init; }

    /// <summary>The denied triple's object; see <see cref="DeniedSubject"/>.</summary>
    public long DeniedObject { get; init; }

    /// <summary>
    /// A scalar count carried by the event. For
    /// <see cref="QueryTraceEventKind.QueryCompleted"/> this is
    /// the number of solutions yielded; for
    /// <see cref="QueryTraceEventKind.QueryStarted"/> the number
    /// of patterns; for
    /// <see cref="QueryTraceEventKind.EngineSelected"/> the
    /// selected engine's triple count; <c>0</c> otherwise.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// The execution engine selected, populated only for
    /// <see cref="QueryTraceEventKind.EngineSelected"/>;
    /// <c>default</c> otherwise.
    /// </summary>
    public QueryEngineKind Engine { get; init; }

    /// <summary>
    /// Why the engine was selected, populated only for
    /// <see cref="QueryTraceEventKind.EngineSelected"/>;
    /// <c>default</c> otherwise.
    /// </summary>
    public EngineSelectionReason SelectionReason { get; init; }

    /// <summary>
    /// The shape features the join-route decision was taken on, populated only for
    /// <see cref="QueryTraceEventKind.EngineSelected"/> events that passed through the join-route selector
    /// seam; <c>default</c> otherwise.
    /// </summary>
    public JoinSelectionFeatures SelectionFeatures { get; init; }

    /// <summary>
    /// The join-route decision that was taken, populated only for
    /// <see cref="QueryTraceEventKind.EngineSelected"/> events that passed through the join-route selector
    /// seam. Its <see cref="JoinSelectionDecision.SelectorKind"/> is
    /// <see cref="JoinStrategySelectorKind.None"/> when no selector was consulted, and its
    /// <see cref="JoinSelectionDecision.Route"/> may differ from <see cref="Engine"/> when the chosen route
    /// declined the shape and the query fell through to the sound default.
    /// </summary>
    public JoinSelectionDecision SelectionDecision { get; init; }

    /// <summary>
    /// How many of the run's relations the applied depths build through their last column, populated only
    /// for <see cref="QueryTraceEventKind.FreeJoinPlanApplied"/>; <c>0</c> otherwise.
    /// </summary>
    public int FullDepthRelationCount { get; init; }

    /// <summary>
    /// How many of the run's relations a join-cover build leaves a private tail on — the cover baseline,
    /// which an extension never moves — populated only for
    /// <see cref="QueryTraceEventKind.FreeJoinPlanApplied"/>; <c>0</c> otherwise.
    /// </summary>
    public int PlannedTailBearingRelationCount { get; init; }

    /// <summary>
    /// One bit per relation, set where the relation builds at full depth, indexed by plan position and
    /// saturating past position sixty-three. Populated only for
    /// <see cref="QueryTraceEventKind.FreeJoinPlanApplied"/>; <c>0</c> otherwise.
    /// </summary>
    public long FullDepthRelationMask { get; init; }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.QueryStarted"/>
    /// event.
    /// </summary>
    public static QueryTraceEvent QueryStarted(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int patternCount)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.QueryStarted,
            PatternIndex = -1,
            Variable = default,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = patternCount,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.QueryCompleted"/>
    /// event.
    /// </summary>
    public static QueryTraceEvent QueryCompleted(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int solutionCount)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.QueryCompleted,
            PatternIndex = -1,
            Variable = default,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = solutionCount,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.IteratorOpened"/>
    /// event.
    /// </summary>
    public static QueryTraceEvent IteratorOpened(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int patternIndex)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.IteratorOpened,
            PatternIndex = patternIndex,
            Variable = default,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = 0,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.IteratorAdvanced"/>
    /// event recording the variable being bound and the value the
    /// iterator advanced to.
    /// </summary>
    public static QueryTraceEvent IteratorAdvanced(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int patternIndex,
        Variable variable,
        long value)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.IteratorAdvanced,
            PatternIndex = patternIndex,
            Variable = variable,
            Value = value,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = 0,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.IteratorReachedEnd"/>
    /// event recording that the iterator has no further keys at
    /// the current variable level.
    /// </summary>
    public static QueryTraceEvent IteratorReachedEnd(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int patternIndex,
        Variable variable)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.IteratorReachedEnd,
            PatternIndex = patternIndex,
            Variable = variable,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = 0,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.LeapfrogStep"/>
    /// event recording one round of laggard advancement during
    /// leapfrog intersection.
    /// </summary>
    public static QueryTraceEvent LeapfrogStep(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        Variable variable,
        long value)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.LeapfrogStep,
            PatternIndex = -1,
            Variable = variable,
            Value = value,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = 0,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.SolutionYielded"/>
    /// event.
    /// </summary>
    public static QueryTraceEvent SolutionYielded(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.SolutionYielded,
            PatternIndex = -1,
            Variable = default,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = 0,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.PlannerDecision"/>
    /// event recording a planner choice. The <see cref="Variable"/>
    /// field carries the variable the planner chose to descend by,
    /// or <c>default</c> for non-descent decisions.
    /// </summary>
    public static QueryTraceEvent PlannerDecision(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        Variable variable)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.PlannerDecision,
            PatternIndex = -1,
            Variable = variable,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = 0,
        };
    }

    /// <summary>
    /// Constructs an <see cref="QueryTraceEventKind.EngineSelected"/>
    /// event recording a rendezvous decision. <see cref="Count"/>
    /// carries the selected engine's triple count and
    /// <see cref="Value"/> the lazy-build cost in milliseconds —
    /// <c>0</c> when no view was materialised. Joining this event
    /// with <see cref="QueryTraceEventKind.QueryCompleted"/> by
    /// correlation id attributes observed query cost to the
    /// selection decision. A decision taken through the join-route
    /// selector seam carries the features it was taken on and the
    /// decision itself, so the join yields (features, decision) to
    /// observed cost; every other decision leaves both at
    /// <c>default</c>, where
    /// <see cref="JoinStrategySelectorKind.None"/> says no selector
    /// was consulted.
    /// </summary>
    public static QueryTraceEvent EngineSelected(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        QueryEngineKind engine,
        EngineSelectionReason reason,
        int tripleCount,
        long buildMilliseconds,
        JoinSelectionFeatures selectionFeatures = default,
        JoinSelectionDecision selectionDecision = default)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.EngineSelected,
            PatternIndex = -1,
            Variable = default,
            Value = buildMilliseconds,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = tripleCount,
            Engine = engine,
            SelectionReason = reason,
            SelectionFeatures = selectionFeatures,
            SelectionDecision = selectionDecision,
        };
    }

    /// <summary>
    /// Constructs a <see cref="QueryTraceEventKind.FreeJoinPlanApplied"/>
    /// event recording the depths one Free Join run's plan applied.
    /// <see cref="Count"/> carries the run's relation count; the three
    /// plan members carry the applied full-depth count, the cover
    /// baseline's tail-bearing count, and the full-depth bitmask.
    /// Joining this event with
    /// <see cref="QueryTraceEventKind.QueryCompleted"/> by correlation
    /// id attributes observed query cost to the depths that ran.
    /// </summary>
    public static QueryTraceEvent FreeJoinPlanApplied(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int relationCount,
        int fullDepthRelationCount,
        int plannedTailBearingRelationCount,
        long fullDepthRelationMask)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.FreeJoinPlanApplied,
            PatternIndex = -1,
            Variable = default,
            Value = 0,
            DeniedSubject = 0,
            DeniedPredicate = 0,
            DeniedObject = 0,
            Count = relationCount,
            FullDepthRelationCount = fullDepthRelationCount,
            PlannedTailBearingRelationCount = plannedTailBearingRelationCount,
            FullDepthRelationMask = fullDepthRelationMask,
        };
    }

    /// <summary>
    /// Constructs an <see cref="QueryTraceEventKind.AccessDenied"/>
    /// event — the audit channel for access-control denials.
    /// </summary>
    public static QueryTraceEvent AccessDenied(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        long subject,
        long predicate,
        long @object)
    {
        return new()
        {
            SequenceNumber = sequenceNumber,
            TimestampTicks = timestampTicks,
            CorrelationId = correlationId,
            Kind = QueryTraceEventKind.AccessDenied,
            PatternIndex = -1,
            Variable = default,
            Value = 0,
            DeniedSubject = subject,
            DeniedPredicate = predicate,
            DeniedObject = @object,
            Count = 0,
        };
    }
}
