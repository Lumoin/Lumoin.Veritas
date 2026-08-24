namespace Lumoin.Veritas.Core.Indexing;

/// <summary>The probe forms a <see cref="ValueProbeRequest"/> can carry in the temporal profile.</summary>
public enum ValueProbeKind
{
    /// <summary>An axis window: entries whose value lies between the bounds, each bound optional and open or closed.</summary>
    Range,

    /// <summary>An as-of seek: the entries in effect at the probe instant (the nearest-predecessor primitive, plus the interval-cover check on an interval pair).</summary>
    AsOf,
}

/// <summary>
/// A value-index probe: the axis constraint an access method answers with locators.
/// </summary>
/// <remarks>
/// <para>
/// Version 1 carries the TEMPORAL profile only: axis bounds (lower/upper, each optional and open or
/// closed) for <see cref="ValueProbeKind.Range"/>, and the instant form for
/// <see cref="ValueProbeKind.AsOf"/>. Bounds are literals of the axis datatype; the access method
/// canonicalizes them onto its normalized axis. Further profiles type their own request members when
/// their first access method is built.
/// </para>
/// </remarks>
/// <param name="Kind">The probe form.</param>
/// <param name="LowerBound">The lower axis bound, or <see langword="null"/> for an unbounded lower end.</param>
/// <param name="LowerInclusive">Whether the lower bound itself is included.</param>
/// <param name="UpperBound">The upper axis bound, or <see langword="null"/> for an unbounded upper end.</param>
/// <param name="UpperInclusive">Whether the upper bound itself is included.</param>
/// <param name="AsOf">The as-of instant for <see cref="ValueProbeKind.AsOf"/>, else <see langword="null"/>.</param>
public readonly record struct ValueProbeRequest(
    ValueProbeKind Kind,
    Literal? LowerBound,
    bool LowerInclusive,
    Literal? UpperBound,
    bool UpperInclusive,
    Literal? AsOf)
{
    /// <summary>Builds a range probe.</summary>
    /// <param name="lowerBound">The lower bound, or <see langword="null"/> for none.</param>
    /// <param name="lowerInclusive">Whether the lower bound is included.</param>
    /// <param name="upperBound">The upper bound, or <see langword="null"/> for none.</param>
    /// <param name="upperInclusive">Whether the upper bound is included.</param>
    /// <returns>The request.</returns>
    public static ValueProbeRequest Range(Literal? lowerBound, bool lowerInclusive, Literal? upperBound, bool upperInclusive)
    {
        return new ValueProbeRequest(ValueProbeKind.Range, lowerBound, lowerInclusive, upperBound, upperInclusive, AsOf: null);
    }

    /// <summary>Builds an as-of probe.</summary>
    /// <param name="asOf">The as-of instant.</param>
    /// <returns>The request.</returns>
    public static ValueProbeRequest AtInstant(Literal asOf)
    {
        return new ValueProbeRequest(ValueProbeKind.AsOf, LowerBound: null, LowerInclusive: false, UpperBound: null, UpperInclusive: false, asOf);
    }
}
