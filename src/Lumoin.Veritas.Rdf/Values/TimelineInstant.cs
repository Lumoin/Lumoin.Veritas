using System;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// A normalized position on the proleptic timeline: the UTC day since 1970-01-01 and the nanosecond
/// within it, produced by <see cref="DateTimeValue.ToInstant"/> after the explicit-or-implicit
/// timezone adjustment. The one comparison axis the SPARQL evaluator and every temporal value index
/// share, so a probe and a scan can never disagree on order.
/// </summary>
/// <param name="Day">The UTC day since 1970-01-01.</param>
/// <param name="NanosecondOfDay">The nanosecond within the day, in [0, 86 400 000 000 000).</param>
public readonly record struct TimelineInstant(long Day, long NanosecondOfDay): IComparable<TimelineInstant>
{
    /// <summary>Orders instants chronologically: by day, then by nanosecond within the day.</summary>
    /// <param name="other">The other instant.</param>
    /// <returns>A negative, zero, or positive value as this instant is earlier, simultaneous, or later.</returns>
    public int CompareTo(TimelineInstant other)
    {
        int byDay = Day.CompareTo(other.Day);

        return byDay != 0 ? byDay : NanosecondOfDay.CompareTo(other.NanosecondOfDay);
    }

    /// <summary>Whether one instant is earlier than another.</summary>
    /// <param name="left">The first instant.</param>
    /// <param name="right">The second instant.</param>
    /// <returns><see langword="true"/> when earlier.</returns>
    public static bool operator <(TimelineInstant left, TimelineInstant right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>Whether one instant is later than another.</summary>
    /// <param name="left">The first instant.</param>
    /// <param name="right">The second instant.</param>
    /// <returns><see langword="true"/> when later.</returns>
    public static bool operator >(TimelineInstant left, TimelineInstant right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>Whether one instant is earlier than or simultaneous with another.</summary>
    /// <param name="left">The first instant.</param>
    /// <param name="right">The second instant.</param>
    /// <returns><see langword="true"/> when not later.</returns>
    public static bool operator <=(TimelineInstant left, TimelineInstant right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>Whether one instant is later than or simultaneous with another.</summary>
    /// <param name="left">The first instant.</param>
    /// <param name="right">The second instant.</param>
    /// <returns><see langword="true"/> when not earlier.</returns>
    public static bool operator >=(TimelineInstant left, TimelineInstant right)
    {
        return left.CompareTo(right) >= 0;
    }
}
