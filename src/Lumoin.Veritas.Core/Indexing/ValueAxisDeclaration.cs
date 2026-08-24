using System;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// The axis form a value-index registration declares: a point axis over one predicate, or an interval
/// pair whose start and end predicates are joined on the occurrence subject at build time.
/// </summary>
/// <remarks>
/// <para>
/// The declaration routes the build and the probe recognizer. A POINT axis indexes the values of one
/// declared predicate and serves the point-shape families (range window, as-of, nearest predecessor,
/// last). An INTERVAL PAIR names a start predicate and an end predicate; the build joins the two on the
/// occurrence subject into (occurrence, start, end) rows — an INNER join, so a half-assembled occurrence
/// (one endpoint committed, the other absent) is invisible to the index, exactly matching the two-pattern
/// scan baseline — and serves the overlap-shape families.
/// </para>
/// <para>
/// Predicates are declared by IRI, not by encoded id: registration happens at composition time, before
/// any store or dictionary exists.
/// </para>
/// </remarks>
public readonly struct ValueAxisDeclaration: IEquatable<ValueAxisDeclaration>
{
    /// <summary>Constructs a declaration from its fields.</summary>
    /// <param name="startPredicateIri">The point predicate, or the interval start predicate.</param>
    /// <param name="endPredicateIri">The interval end predicate, or <see langword="null"/> for a point axis.</param>
    private ValueAxisDeclaration(Utf8String startPredicateIri, Utf8String? endPredicateIri)
    {
        StartPredicateIri = startPredicateIri;
        EndPredicateIri = endPredicateIri;
    }

    /// <summary>The point predicate of a point axis, or the interval start predicate of an interval pair.</summary>
    public Utf8String StartPredicateIri { get; }

    /// <summary>The interval end predicate, or <see langword="null"/> for a point axis.</summary>
    public Utf8String? EndPredicateIri { get; }

    /// <summary>Whether this declaration is an interval pair rather than a point axis.</summary>
    public bool IsIntervalPair => EndPredicateIri is not null;

    /// <summary>Declares a point axis over one predicate.</summary>
    /// <param name="predicateIri">The predicate whose object values form the axis.</param>
    /// <returns>The declaration.</returns>
    public static ValueAxisDeclaration PointAxis(Utf8String predicateIri)
    {
        return new ValueAxisDeclaration(predicateIri, endPredicateIri: null);
    }

    /// <summary>Declares an interval pair joined on the occurrence subject at build time.</summary>
    /// <param name="startPredicateIri">The interval start predicate.</param>
    /// <param name="endPredicateIri">The interval end predicate.</param>
    /// <returns>The declaration.</returns>
    public static ValueAxisDeclaration IntervalPair(Utf8String startPredicateIri, Utf8String endPredicateIri)
    {
        return new ValueAxisDeclaration(startPredicateIri, endPredicateIri);
    }

    /// <summary>Whether this declaration equals another field for field.</summary>
    /// <param name="other">The other declaration.</param>
    /// <returns><see langword="true"/> when the predicates match.</returns>
    public bool Equals(ValueAxisDeclaration other)
    {
        return StartPredicateIri.Equals(other.StartPredicateIri) && Nullable.Equals(EndPredicateIri, other.EndPredicateIri);
    }

    /// <summary>Whether this declaration equals a boxed candidate.</summary>
    /// <param name="obj">The boxed candidate.</param>
    /// <returns><see langword="true"/> when the candidate is an equal declaration.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ValueAxisDeclaration other && Equals(other);
    }

    /// <summary>The hash over the declared predicates.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(StartPredicateIri, EndPredicateIri);
    }

    /// <summary>Whether two declarations are equal.</summary>
    /// <param name="left">The first declaration.</param>
    /// <param name="right">The second declaration.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(ValueAxisDeclaration left, ValueAxisDeclaration right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two declarations differ.</summary>
    /// <param name="left">The first declaration.</param>
    /// <param name="right">The second declaration.</param>
    /// <returns><see langword="true"/> when different.</returns>
    public static bool operator !=(ValueAxisDeclaration left, ValueAxisDeclaration right)
    {
        return !left.Equals(right);
    }
}
