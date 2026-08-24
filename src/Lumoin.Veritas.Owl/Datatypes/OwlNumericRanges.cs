using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// One numeric value space of the OWL 2 datatype map as an interval:
/// optional integer bounds (a <c>null</c> bound is unbounded) and whether
/// the space contains integers only or the whole rational/real continuum
/// between the bounds.
/// </summary>
/// <param name="Min">The inclusive lower bound, or <c>null</c> for −∞.</param>
/// <param name="Max">The inclusive upper bound, or <c>null</c> for +∞.</param>
/// <param name="IntegersOnly">Whether the space contains only integers; the map's bounded spaces are all integer spaces, so integer bounds lose nothing.</param>
[DebuggerDisplay("[{Min}, {Max}] IntegersOnly={IntegersOnly}")]
public readonly record struct OwlNumericRange(BigInteger? Min, BigInteger? Max, bool IntegersOnly)
{
    /// <summary>
    /// Whether this space contains every value of <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The candidate subspace.</param>
    /// <returns><see langword="true"/> when <paramref name="inner"/> is contained.</returns>
    public bool Contains(OwlNumericRange inner)
    {
        if(IntegersOnly && !inner.IntegersOnly)
        {
            return false;
        }

        bool minHolds = Min is not BigInteger min || (inner.Min is BigInteger innerMin && innerMin >= min);
        bool maxHolds = Max is not BigInteger max || (inner.Max is BigInteger innerMax && innerMax <= max);

        return minHolds && maxHolds;
    }

    /// <summary>
    /// Whether this space contains the parsed numeric value. Float- and
    /// double-kind values answer <see langword="false"/> — their value
    /// spaces are kept out of the interval algebra, so membership of such
    /// a value stays unknown to the caller.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is in the space.</returns>
    public bool ContainsValue(NumericValue value)
    {
        if(value.Kind == NumericKind.Integer)
        {
            BigInteger integer = value.AsInteger();

            return (Min is not BigInteger min || integer >= min) && (Max is not BigInteger max || integer <= max);
        }

        if(value.Kind == NumericKind.Decimal)
        {
            decimal number = value.AsDecimal();
            if(decimal.Truncate(number) == number)
            {
                BigInteger integer = new(number);

                return (Min is not BigInteger min || integer >= min) && (Max is not BigInteger max || integer <= max);
            }

            if(IntegersOnly)
            {
                return false;
            }

            //A non-integral decimal against integer bounds: inside when
            //strictly between them (the bounds themselves are integers,
            //so equality cannot occur).
            return (Min is not BigInteger lower || number > (decimal)lower) && (Max is not BigInteger upper || number < (decimal)upper);
        }

        return false;
    }
}

/// <summary>
/// The numeric value spaces of the OWL 2 datatype map and their interval
/// algebra: lookups by datatype IRI, intersection, and the map datatypes
/// containing a given space. <c>xsd:float</c> and <c>xsd:double</c> stay
/// outside the algebra — their value spaces carry rounding, infinities,
/// and <c>NaN</c>, so questions about them answer unknown rather than
/// approximately.
/// </summary>
public static class OwlNumericRanges
{
    /// <summary>The map's spaces keyed by datatype IRI, in the order <see cref="SupersetsOf"/> reports them.</summary>
    private static List<KeyValuePair<Utf8String, OwlNumericRange>> Spaces { get; } = BuildSpaces();

    private static List<KeyValuePair<Utf8String, OwlNumericRange>> BuildSpaces()
    {
        OwlNumericRange integers = new(null, null, IntegersOnly: true);
        OwlNumericRange continuum = new(null, null, IntegersOnly: false);

        return
        [
            new(Vocabulary.Xsd.UnsignedByte, new OwlNumericRange(0, 255, IntegersOnly: true)),
            new(Vocabulary.Xsd.ByteValue, new OwlNumericRange(-128, 127, IntegersOnly: true)),
            new(Vocabulary.Xsd.UnsignedShort, new OwlNumericRange(0, 65535, IntegersOnly: true)),
            new(Vocabulary.Xsd.Short, new OwlNumericRange(-32768, 32767, IntegersOnly: true)),
            new(Vocabulary.Xsd.UnsignedInt, new OwlNumericRange(0, 4294967295, IntegersOnly: true)),
            new(Vocabulary.Xsd.Int, new OwlNumericRange(-2147483648, 2147483647, IntegersOnly: true)),
            new(Vocabulary.Xsd.UnsignedLong, new OwlNumericRange(0, ulong.MaxValue, IntegersOnly: true)),
            new(Vocabulary.Xsd.Long, new OwlNumericRange(long.MinValue, long.MaxValue, IntegersOnly: true)),
            new(Vocabulary.Xsd.PositiveInteger, new OwlNumericRange(1, null, IntegersOnly: true)),
            new(Vocabulary.Xsd.NonNegativeInteger, new OwlNumericRange(0, null, IntegersOnly: true)),
            new(Vocabulary.Xsd.NegativeInteger, new OwlNumericRange(null, -1, IntegersOnly: true)),
            new(Vocabulary.Xsd.NonPositiveInteger, new OwlNumericRange(null, 0, IntegersOnly: true)),
            new(Vocabulary.Xsd.Integer, integers),
            new(Vocabulary.Xsd.Decimal, continuum),
            new(OwlVocabulary.Rational, continuum),
            new(OwlVocabulary.Real, continuum),
        ];
    }

    /// <summary>
    /// Looks up the value space of a map datatype.
    /// </summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="range">The space, when the datatype is in the algebra.</param>
    /// <returns><see langword="true"/> when the datatype has a space here.</returns>
    public static bool TryGetRange(Utf8String datatypeIri, out OwlNumericRange range)
    {
        foreach(KeyValuePair<Utf8String, OwlNumericRange> space in Spaces)
        {
            if(space.Key.Equals(datatypeIri))
            {
                range = space.Value;

                return true;
            }
        }

        range = default;

        return false;
    }

    /// <summary>
    /// Intersects two spaces.
    /// </summary>
    /// <param name="first">The first space.</param>
    /// <param name="second">The second space.</param>
    /// <returns>The intersection, or <c>null</c> when it is empty.</returns>
    public static OwlNumericRange? Intersect(OwlNumericRange first, OwlNumericRange second)
    {
        BigInteger? min = (first.Min, second.Min) switch
        {
            (null, null) => null,
            (BigInteger only, null) => only,
            (null, BigInteger single) => single,
            (BigInteger left, BigInteger right) => BigInteger.Max(left, right)
        };

        BigInteger? max = (first.Max, second.Max) switch
        {
            (null, null) => null,
            (BigInteger only, null) => only,
            (null, BigInteger single) => single,
            (BigInteger left, BigInteger right) => BigInteger.Min(left, right)
        };

        if(min is BigInteger lower && max is BigInteger upper && lower > upper)
        {
            return null;
        }

        return new OwlNumericRange(min, max, first.IntegersOnly || second.IntegersOnly);
    }

    /// <summary>
    /// The map datatypes whose value space contains <paramref name="range"/>.
    /// </summary>
    /// <param name="range">The space to cover.</param>
    /// <returns>The containing datatypes' IRIs, narrowest family members first.</returns>
    public static List<Utf8String> SupersetsOf(OwlNumericRange range)
    {
        List<Utf8String> supersets = [];
        foreach(KeyValuePair<Utf8String, OwlNumericRange> space in Spaces)
        {
            if(space.Value.Contains(range))
            {
                supersets.Add(space.Key);
            }
        }

        return supersets;
    }
}
