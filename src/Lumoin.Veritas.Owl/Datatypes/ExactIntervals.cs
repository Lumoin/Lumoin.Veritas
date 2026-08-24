using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The one exact-real interval builder shared by the satisfiability checker and
/// the data-range canonicalizer: a datatype/restriction over the
/// <c>owl:real</c>/<c>rational</c>/<c>decimal</c>/<c>integer</c> tower folds into
/// an <see cref="ExactInterval"/> over the exact-real line, together with the
/// narrowest exact-real value-space level of the constraint. A float, double,
/// temporal, or otherwise unmodelled constraint reports failure rather than an
/// approximate interval.
/// </summary>
internal static class ExactIntervals
{
    /// <summary>
    /// Builds the exact-real interval and value-space level of a base datatype
    /// optionally narrowed by a datatype restriction, or reports that the
    /// constraint cannot be modelled exactly (a non-exact-real base, or a facet
    /// the ordering algebra does not model).
    /// </summary>
    /// <param name="datatype">The base datatype node.</param>
    /// <param name="restriction">The datatype restriction narrowing the base, or <see langword="null"/> for the bare base.</param>
    /// <param name="interval">The interval, when modelled.</param>
    /// <param name="level">The constraint's exact-real value-space level, when modelled.</param>
    /// <returns><see langword="true"/> when the constraint is a fully modelled exact-real range.</returns>
    public static bool TryBuildInterval(NamedNode datatype, OwlDatatypeRestriction? restriction, out ExactInterval interval, out RealLevel level)
    {
        interval = ExactInterval.Unbounded;
        level = RealLevel.Real;

        if(OwlDatatypeFamilies.NumericSpaceOf(datatype.Iri) != OwlNumericSpace.ExactReal)
        {
            return false;
        }

        if(OwlNumericRanges.TryGetRange(datatype.Iri, out OwlNumericRange baseRange))
        {
            interval = ExactInterval.FromRange(baseRange);
        }

        level = LevelOf(datatype.Iri);
        if(restriction is not OwlDatatypeRestriction present)
        {
            return true;
        }

        foreach(OwlFacetRestriction facet in present.Restrictions)
        {
            if(!TryApplyFacet(facet, ref interval))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies a numeric bound facet to an interval; reports failure for any facet
    /// the exact-real procedure does not model (length, pattern, digit counts) or
    /// a bound value that is not an exact-real number.
    /// </summary>
    /// <param name="facet">The facet restriction.</param>
    /// <param name="interval">The interval to tighten in place.</param>
    /// <returns><see langword="true"/> when the facet was applied.</returns>
    public static bool TryApplyFacet(OwlFacetRestriction facet, ref ExactInterval interval)
    {
        Utf8String facetIri = facet.Facet.Iri;
        bool isLower = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MinExclusive);
        bool isUpper = facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxExclusive);
        if(!isLower && !isUpper)
        {
            return false;
        }

        if(OwlDatatypeFamilies.NumericSpaceOf(facet.Value.Datatype.Iri) != OwlNumericSpace.ExactReal
            || !OwlNumericLexicals.TryGetValue(facet.Value.Value.ToString(), facet.Value.Datatype.Iri, out NumericValue bound)
            || bound.Kind is not (NumericKind.Integer or NumericKind.Decimal))
        {
            return false;
        }

        bool inclusive = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive);
        Endpoint endpoint = new(bound, inclusive);
        interval = isLower
            ? ExactInterval.Intersect(interval, new ExactInterval(endpoint, null, false))
            : ExactInterval.Intersect(interval, new ExactInterval(null, endpoint, false));

        return true;
    }

    /// <summary>The narrowest-to-broadest exact-real value-space ordering, for the bare-complement emptiness rule.</summary>
    /// <param name="datatypeIri">The exact-real datatype IRI.</param>
    /// <returns>The level.</returns>
    public static RealLevel LevelOf(Utf8String datatypeIri)
    {
        if(datatypeIri.Equals(OwlVocabulary.Real))
        {
            return RealLevel.Real;
        }

        if(datatypeIri.Equals(OwlVocabulary.Rational))
        {
            return RealLevel.Rational;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Decimal))
        {
            return RealLevel.Decimal;
        }

        //The integer tower (xsd:integer and the bounded derived types).
        return RealLevel.Integer;
    }

    /// <summary>Whether a value is integral, reporting the integer.</summary>
    /// <param name="value">The value.</param>
    /// <param name="integer">The integer, when the value is integral.</param>
    /// <returns><see langword="true"/> when the value is an exact integer.</returns>
    public static bool IsIntegral(NumericValue value, out BigInteger integer)
    {
        if(value.Kind == NumericKind.Integer)
        {
            integer = value.AsInteger();

            return true;
        }

        if(value.Kind == NumericKind.Decimal)
        {
            decimal number = value.AsDecimal();
            if(decimal.Truncate(number) == number)
            {
                integer = new BigInteger(number);

                return true;
            }
        }

        integer = BigInteger.Zero;

        return false;
    }
}

/// <summary>An interval endpoint over the exact-real line.</summary>
/// <param name="Value">The endpoint value (an exact integer or decimal).</param>
/// <param name="Inclusive">Whether the endpoint is included.</param>
internal readonly record struct Endpoint(NumericValue Value, bool Inclusive);

/// <summary>
/// An interval over the exact-real line: an optional lower and upper endpoint
/// (<c>null</c> is unbounded) and whether the space is restricted to integers.
/// </summary>
/// <param name="Lower">The lower endpoint, or <c>null</c> for −∞.</param>
/// <param name="Upper">The upper endpoint, or <c>null</c> for +∞.</param>
/// <param name="IntegersOnly">Whether the interval contains only integers.</param>
internal readonly record struct ExactInterval(Endpoint? Lower, Endpoint? Upper, bool IntegersOnly)
{
    /// <summary>The unbounded continuum interval — the whole exact-real line.</summary>
    public static ExactInterval Unbounded { get; } = new(null, null, false);

    /// <summary>Builds an interval from a numeric range of the datatype map.</summary>
    /// <param name="range">The numeric range.</param>
    /// <returns>The interval.</returns>
    public static ExactInterval FromRange(OwlNumericRange range)
    {
        Endpoint? lower = range.Min is BigInteger min ? new Endpoint(new NumericValue(min), true) : null;
        Endpoint? upper = range.Max is BigInteger max ? new Endpoint(new NumericValue(max), true) : null;

        return new ExactInterval(lower, upper, range.IntegersOnly);
    }

    /// <summary>Intersects two intervals.</summary>
    /// <param name="first">The first interval.</param>
    /// <param name="second">The second interval.</param>
    /// <returns>The intersection.</returns>
    public static ExactInterval Intersect(ExactInterval first, ExactInterval second)
    {
        return new ExactInterval(
            MaxLower(first.Lower, second.Lower),
            MinUpper(first.Upper, second.Upper),
            first.IntegersOnly || second.IntegersOnly);
    }

    /// <summary>Whether this interval is empty over its value space.</summary>
    /// <returns><see langword="true"/> when no value of the required kind lies in the interval.</returns>
    public bool IsEmpty()
    {
        if(IntegersOnly)
        {
            return !TryIntegerFootprint(out _, out _);
        }

        if(Lower is Endpoint lower && Upper is Endpoint upper)
        {
            ComparisonResult comparison = NumericValue.Compare(lower.Value, upper.Value);

            return comparison switch
            {
                ComparisonResult.Greater => true,
                ComparisonResult.Equal => !(lower.Inclusive && upper.Inclusive),
                _ => false
            };
        }

        return false;
    }

    /// <summary>Whether this interval is a single included point, reporting the point value.</summary>
    /// <param name="point">The point value, when degenerate.</param>
    /// <returns><see langword="true"/> when the interval is a single included point.</returns>
    public bool TryDegeneratePoint(out NumericValue point)
    {
        if(Lower is Endpoint lower && Upper is Endpoint upper
            && lower.Inclusive && upper.Inclusive
            && NumericValue.Compare(lower.Value, upper.Value) == ComparisonResult.Equal)
        {
            point = lower.Value;

            return true;
        }

        point = default;

        return false;
    }

    /// <summary>The integer footprint of this interval — the integers it covers.</summary>
    /// <param name="low">The lowest covered integer, or <c>null</c> for −∞.</param>
    /// <param name="high">The highest covered integer, or <c>null</c> for +∞.</param>
    /// <returns><see langword="true"/> when the footprint is non-empty.</returns>
    public bool TryIntegerFootprint(out BigInteger? low, out BigInteger? high)
    {
        low = Lower is Endpoint lower ? CeilingInteger(lower) : null;
        high = Upper is Endpoint upper ? FloorInteger(upper) : null;

        return !(low is BigInteger lowValue && high is BigInteger highValue && lowValue > highValue);
    }

    /// <summary>Whether a value lies within this interval, honouring the endpoint inclusivity.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the value is inside.</returns>
    public bool Contains(NumericValue value)
    {
        if(IntegersOnly && !ExactIntervals.IsIntegral(value, out _))
        {
            return false;
        }

        if(Lower is Endpoint lower)
        {
            ComparisonResult comparison = NumericValue.Compare(value, lower.Value);
            bool ok = lower.Inclusive ? comparison is ComparisonResult.Greater or ComparisonResult.Equal : comparison == ComparisonResult.Greater;
            if(!ok)
            {
                return false;
            }
        }

        if(Upper is Endpoint upper)
        {
            ComparisonResult comparison = NumericValue.Compare(value, upper.Value);
            bool ok = upper.Inclusive ? comparison is ComparisonResult.Less or ComparisonResult.Equal : comparison == ComparisonResult.Less;
            if(!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The smallest integer at or above a lower endpoint.</summary>
    /// <param name="endpoint">The lower endpoint.</param>
    /// <returns>The smallest covered integer.</returns>
    private static BigInteger CeilingInteger(Endpoint endpoint)
    {
        BigInteger ceiling = Ceiling(endpoint.Value);
        bool integral = ExactIntervals.IsIntegral(endpoint.Value, out _);

        return endpoint.Inclusive || !integral ? ceiling : ceiling + BigInteger.One;
    }

    /// <summary>The largest integer at or below an upper endpoint.</summary>
    /// <param name="endpoint">The upper endpoint.</param>
    /// <returns>The largest covered integer.</returns>
    private static BigInteger FloorInteger(Endpoint endpoint)
    {
        BigInteger floor = Floor(endpoint.Value);
        bool integral = ExactIntervals.IsIntegral(endpoint.Value, out _);

        return endpoint.Inclusive || !integral ? floor : floor - BigInteger.One;
    }

    /// <summary>The ceiling of an exact-real value as an integer.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The ceiling.</returns>
    private static BigInteger Ceiling(NumericValue value)
    {
        return value.Kind == NumericKind.Integer ? value.AsInteger() : new BigInteger(decimal.Ceiling(value.AsDecimal()));
    }

    /// <summary>The floor of an exact-real value as an integer.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The floor.</returns>
    private static BigInteger Floor(NumericValue value)
    {
        return value.Kind == NumericKind.Integer ? value.AsInteger() : new BigInteger(decimal.Floor(value.AsDecimal()));
    }

    /// <summary>The larger of two lower endpoints; at equal values the exclusive one wins.</summary>
    /// <param name="first">The first lower endpoint.</param>
    /// <param name="second">The second lower endpoint.</param>
    /// <returns>The tighter lower endpoint.</returns>
    private static Endpoint? MaxLower(Endpoint? first, Endpoint? second)
    {
        if(first is not Endpoint left)
        {
            return second;
        }

        if(second is not Endpoint right)
        {
            return first;
        }

        return NumericValue.Compare(left.Value, right.Value) switch
        {
            ComparisonResult.Greater => left,
            ComparisonResult.Less => right,
            _ => new Endpoint(left.Value, left.Inclusive && right.Inclusive)
        };
    }

    /// <summary>The smaller of two upper endpoints; at equal values the exclusive one wins.</summary>
    /// <param name="first">The first upper endpoint.</param>
    /// <param name="second">The second upper endpoint.</param>
    /// <returns>The tighter upper endpoint.</returns>
    private static Endpoint? MinUpper(Endpoint? first, Endpoint? second)
    {
        if(first is not Endpoint left)
        {
            return second;
        }

        if(second is not Endpoint right)
        {
            return first;
        }

        return NumericValue.Compare(left.Value, right.Value) switch
        {
            ComparisonResult.Less => left,
            ComparisonResult.Greater => right,
            _ => new Endpoint(left.Value, left.Inclusive && right.Inclusive)
        };
    }
}

/// <summary>The exact-real value spaces ordered narrowest to broadest by containment.</summary>
internal enum RealLevel
{
    /// <summary>The integer tower.</summary>
    Integer = 0,

    /// <summary>The <c>xsd:decimal</c> space.</summary>
    Decimal = 1,

    /// <summary>The <c>owl:rational</c> space.</summary>
    Rational = 2,

    /// <summary>The <c>owl:real</c> space.</summary>
    Real = 3,
}
