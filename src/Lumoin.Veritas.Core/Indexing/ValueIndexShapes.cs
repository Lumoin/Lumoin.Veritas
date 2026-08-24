using System;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// The value-constraint query shapes a <see cref="ValueAccessMethod"/> declares it can answer.
/// </summary>
/// <remarks>
/// <para>
/// The flags name the canonical shape families of an ordered value axis. <see cref="NearestPredecessor"/> —
/// the seek to the greatest indexed value at or below a probe point — is the seam's MANDATORY primitive:
/// every access method must declare it, because the as-of and last-shape families reduce to it.
/// <see cref="IntervalOverlap"/> is opt-in: only a method registered over an interval pair carries the
/// endpoint structure to answer it.
/// </para>
/// <para>
/// A declaration is a capability statement consumed by registration sanity checks and by the probe
/// recognizer's routing; it never changes an answer — a shape the method does not declare falls through to
/// the scan.
/// </para>
/// </remarks>
[Flags]
public enum ValueIndexShapes
{
    /// <summary>No shapes declared — never valid for a registered method.</summary>
    None = 0,

    /// <summary>The as-of point snapshot: the value in effect at a probe instant.</summary>
    AsOfPoint = 1,

    /// <summary>The interval-overlap family: entries whose interval intersects a probe window.</summary>
    IntervalOverlap = 2,

    /// <summary>The range window: entries whose axis value lies within a probe window.</summary>
    RangeWindow = 4,

    /// <summary>The nearest-predecessor seek: the greatest indexed value at or below a probe point — the mandatory primitive.</summary>
    NearestPredecessor = 8,

    /// <summary>The last-per-series shape: the newest entry per series key (the predecessor seek at the axis tail).</summary>
    LastPerSeries = 16,

    /// <summary>The bitemporal as-of shape: the valid-time axis probe under a transaction-time bound (transaction time rides commit order).</summary>
    BitemporalAsOf = 32,
}
