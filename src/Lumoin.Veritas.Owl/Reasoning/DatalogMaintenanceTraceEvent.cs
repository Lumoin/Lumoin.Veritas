using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The path one maintained commit's underlying <c>Apply</c> took — the public
/// mirror of the RL maintained closure's internal maintenance mode, surfaced on
/// the trace bus and the per-commit result so a consumer reads the cost class
/// without reaching into the closure internals.
/// </summary>
public enum ReasoningMaintenanceMode
{
    /// <summary>The incremental overdelete / rederive / insert pipeline ran; the commit's served delta is bounded by the facts it touched.</summary>
    Incremental = 0,

    /// <summary>A from-scratch rebuild ran because the closure's prior state was inconsistent; a remat-class commit.</summary>
    RebuildInconsistent = 1,

    /// <summary>A from-scratch rebuild ran because the closure's prior <c>Apply</c> left it poisoned; unreachable on the production commit path, where a poisoned instance is discarded before any further commit.</summary>
    RebuildPoisoned = 2,

    /// <summary>The maintenance wiring built a fresh closure from the caller's committed base rather than feeding the closure a delta — the discard-recovery (invalidated instance) and wholesale-replace lanes; a remat-class commit.</summary>
    RebuildRequested = 3,
}

/// <summary>
/// The deterministic counters of one maintained commit's <c>Apply</c> — the RL
/// maintained closure's internal statistics lifted to the public surface, so the
/// per-commit result and the trace event carry the same nine values (the eight
/// counters and the <see cref="Mode"/>) without exposing the closure's internal
/// type.
/// </summary>
/// <param name="OverdeleteMarked">The number of facts marked after the overdelete fixpoint.</param>
/// <param name="DeletionRounds">The number of overdelete rounds run.</param>
/// <param name="DirectlyRederived">The number of deleted facts the head-bound matcher restored directly.</param>
/// <param name="RestoredTotal">The number of marked facts present again in the closure at completion.</param>
/// <param name="InsertRounds">The number of semi-naive insert rounds run.</param>
/// <param name="ChoiceOwnerReFires">The number of choice/list owner construct re-fires.</param>
/// <param name="BaseDemotions">The number of base additions that demoted an existing derived fact.</param>
/// <param name="BasePromotions">The number of seeded removals promoted to derived.</param>
/// <param name="Mode">The path the commit's <c>Apply</c> (or the wiring rebuild) took.</param>
public readonly record struct ReasoningMaintenanceStatistics(
    int OverdeleteMarked,
    int DeletionRounds,
    int DirectlyRederived,
    int RestoredTotal,
    int InsertRounds,
    int ChoiceOwnerReFires,
    int BaseDemotions,
    int BasePromotions,
    ReasoningMaintenanceMode Mode);

/// <summary>
/// One maintained-commit announcement on the shared trace bus: the base delta
/// the commit consumed, the served (base ∪ derived) delta it produced, the
/// maintenance counters and mode, whether the overlay stayed on, whether the
/// commit was rebuild-class, and the maintenance cost. Emitted once per
/// maintained commit, and only after the commit LANDS — a non-landing commit's
/// pre-append maintenance is discarded with its invalidated instance, so
/// observability never reports a phantom commit. The per-commit companion to the
/// per-materialization <see cref="ReasoningTraceEvent"/> and the per-decision
/// <see cref="ReasoningDecisionTraceEvent"/>, joined to the surrounding operation
/// by <see cref="CorrelationId"/>. A <c>readonly record struct</c> so emitting is
/// allocation-free with the <c>in</c>-parameter handler.
/// </summary>
/// <param name="SequenceNumber">The event's monotonic sequence number within the trace stream.</param>
/// <param name="TimestampTicks">The emission time in UTC ticks.</param>
/// <param name="CorrelationId">The correlation id linking this event to the maintained engine's stream.</param>
/// <param name="BaseAddedCount">The number of triples the commit added to the asserted base.</param>
/// <param name="BaseRetractedCount">The number of triples the commit retracted from the asserted base.</param>
/// <param name="ServedAddedCount">The number of triples the commit added to the served store (base ∪ derived).</param>
/// <param name="ServedRemovedCount">The number of triples the commit removed from the served store.</param>
/// <param name="Statistics">The maintenance counters and mode of the commit's <c>Apply</c>.</param>
/// <param name="OverlayOn">Whether the commit left the derived overlay on (the closure stayed consistent) rather than withdrawn (served asserted-only).</param>
/// <param name="RebuildClass">Whether the commit ran a from-scratch build rather than the incremental pipeline — the inconsistency, discard-recovery, and wholesale-replace lanes.</param>
/// <param name="ElapsedMilliseconds">The maintenance cost in fractional milliseconds, measured on a monotonic clock so a sub-millisecond commit is not lost to zero.</param>
public readonly record struct DatalogMaintenanceTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    int BaseAddedCount,
    int BaseRetractedCount,
    int ServedAddedCount,
    int ServedRemovedCount,
    ReasoningMaintenanceStatistics Statistics,
    bool OverlayOn,
    bool RebuildClass,
    double ElapsedMilliseconds): ITraceEvent
{
    /// <summary>Builds the event from a maintained commit's base delta, served delta, statistics, overlay and rebuild flags, and measured cost.</summary>
    /// <param name="sequenceNumber">The event's sequence number.</param>
    /// <param name="timestampTicks">The emission time in UTC ticks.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="baseAddedCount">The number of triples added to the asserted base.</param>
    /// <param name="baseRetractedCount">The number of triples retracted from the asserted base.</param>
    /// <param name="servedAddedCount">The number of triples added to the served store.</param>
    /// <param name="servedRemovedCount">The number of triples removed from the served store.</param>
    /// <param name="statistics">The commit's maintenance counters and mode.</param>
    /// <param name="overlayOn">Whether the overlay stayed on.</param>
    /// <param name="rebuildClass">Whether the commit was rebuild-class.</param>
    /// <param name="elapsedMilliseconds">The maintenance cost.</param>
    /// <returns>The trace event.</returns>
    public static DatalogMaintenanceTraceEvent From(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        int baseAddedCount,
        int baseRetractedCount,
        int servedAddedCount,
        int servedRemovedCount,
        in ReasoningMaintenanceStatistics statistics,
        bool overlayOn,
        bool rebuildClass,
        double elapsedMilliseconds)
    {
        return new DatalogMaintenanceTraceEvent(
            sequenceNumber,
            timestampTicks,
            correlationId,
            baseAddedCount,
            baseRetractedCount,
            servedAddedCount,
            servedRemovedCount,
            statistics,
            overlayOn,
            rebuildClass,
            elapsedMilliseconds);
    }
}
