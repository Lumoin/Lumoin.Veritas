namespace Lumoin.Veritas.Core.Hypertrie.Tracing;

/// <summary>
/// Discriminator for the union cases of <see cref="QueryTraceEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Struct types cannot participate in inheritance-based closed
/// unions, so a subsystem's trace events use a single
/// <c>readonly record struct</c> carrying a discriminator plus
/// all union-specific payload fields. Consumers switch on this
/// enum to interpret the event.
/// </para>
/// <para>
/// <b>Privacy note.</b> Access-denial events are present in this
/// union; access-not-found events are not. The latter would
/// undermine the privacy guarantee that "denied" and "not found"
/// are observationally indistinguishable in the audit channel.
/// Operators who want audit visibility into denied accesses
/// configure their access-control policy to return Deny rather
/// than NotFound.
/// </para>
/// </remarks>
public enum QueryTraceEventKind
{
    /// <summary>A query has begun executing. Carries the query identifier and the number of patterns.</summary>
    QueryStarted = 0,

    /// <summary>A query has completed execution. Carries the query identifier and the number of solutions yielded.</summary>
    QueryCompleted = 1,

    /// <summary>A triejoin iterator has been opened on a pattern. Carries the pattern index and the iterator's variable order.</summary>
    IteratorOpened = 2,

    /// <summary>A triejoin iterator has advanced to the next key at the current level via MoveNext.</summary>
    IteratorAdvanced = 3,

    /// <summary>A triejoin iterator has reached the end of the current level (no further keys at this depth).</summary>
    IteratorReachedEnd = 4,

    /// <summary>A leapfrog intersection step occurred — one cursor was advanced past another.</summary>
    LeapfrogStep = 5,

    /// <summary>A complete solution has been yielded — every variable is bound.</summary>
    SolutionYielded = 6,

    /// <summary>The planner returned a decision at this step — descend, skip, yield, or stop.</summary>
    PlannerDecision = 7,

    /// <summary>An access-control policy returned Deny for a candidate triple. The audit channel.</summary>
    AccessDenied = 8,

    /// <summary>The query rendezvous chose an execution engine. Carries the engine, the selection reason, the engine's triple count, and the lazy-build cost when a view was materialised — joinable with <see cref="QueryCompleted"/> by correlation id, which is the feedback signal adaptive planners consume.</summary>
    EngineSelected = 9,
}
