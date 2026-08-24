namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// When a <see cref="PackedBoxIndex"/> materializes its embedded dominance
/// structure — the carriage of the containing mode's sub-linear route. Both
/// carriages run the identical pass over the identical committed columns and
/// produce the identical structure; the only degree of freedom is WHEN the
/// pass runs, so query answers, enumeration order, and steady-state cost are
/// carriage-invariant by construction. The zero member names the measured
/// default: deferral keeps the build in the plain packed-tree cost class and
/// recovers warm rebuilds, at the price of the first containing use of each
/// built epoch carrying the one-time pass.
/// </summary>
public enum DominanceMaterializationMode
{
    /// <summary>
    /// The dominance pass runs at the containing route's (or a forcing
    /// diagnostic accessor's) first use of each built epoch, exactly once,
    /// internally synchronized. Builds that never ask a containing question
    /// never pay the dominance cost, and under a rebuild-per-epoch cadence
    /// the rebuild itself pays only the packed-tree build (an epoch that
    /// then asks a containing question still pays the one-time pass at its
    /// first use) — the measured default.
    /// </summary>
    DeferredToFirstUse = 0,

    /// <summary>
    /// The dominance pass runs at the tail of every successful non-empty
    /// build, on the building thread, before the build returns. The first
    /// containing query of an epoch then carries no materialization — the
    /// carriage for consumers whose first-query latency outranks build cost,
    /// which pays the full dominance delta on every build and rebuild.
    /// </summary>
    EagerAtBuild = 1
}
