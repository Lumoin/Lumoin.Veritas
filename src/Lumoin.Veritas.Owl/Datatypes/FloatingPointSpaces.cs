using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A contiguous run of the ranks one IEEE-754 value space assigns its values —
/// the lower and upper bounds a conjunction of ordered facets folds into. The
/// run is empty exactly when its lower bound exceeds its upper bound, and an
/// empty run is a proof that no value of the space satisfies the facets.
/// </summary>
/// <param name="LowerRank">The lowest rank the run admits.</param>
/// <param name="UpperRank">The highest rank the run admits.</param>
internal readonly record struct FloatingRankInterval(long LowerRank, long UpperRank)
{
    /// <summary>Whether the run admits no rank at all, so the folded facets have no witness in the space.</summary>
    public bool IsEmpty => LowerRank > UpperRank;
}

/// <summary>
/// The discrete order algebra of the <c>xsd:float</c> and <c>xsd:double</c>
/// value spaces: a monotone rank map from values to integers, and the folding
/// of ordered facets into a rank run whose emptiness decides the conjunction.
/// </summary>
/// <remarks>
/// <para>
/// The rank of a value is its sign-magnitude IEEE-754 bit pattern with the
/// magnitude negated for a negative sign, which is monotone in the XSD order
/// and gives both zeros ONE shared rank: <c>+0</c> and <c>-0</c> are order-equal
/// to every facet while staying identity-distinct values, and the two notions
/// never meet — nothing here answers value identity. The infinities hold the
/// extreme ranks. <c>NaN</c> has no rank at all: it is order-incomparable, so
/// every ordered facet excludes it and a <c>NaN</c>-valued bound is refused.
/// </para>
/// <para>
/// Because the magnitude of a non-<c>NaN</c> pattern never exceeds the
/// infinity pattern (<c>0x7F800000</c> for the single space, <c>0x7FF0…0</c>
/// for the double space), a <see cref="long"/> rank keeps a margin of more than
/// four thousand million million above the widest extreme — so the one-rank
/// step an exclusive bound takes at an infinity can never wrap into a wrong
/// verdict, on either space.
/// </para>
/// <para>
/// Emptiness rides the rank bijection alone: the decision is integer and bit
/// arithmetic with no floating-point operation in it, and bound lexical forms
/// map through the shared <see cref="OwlNumericLexicals"/> parse so a bound is
/// never re-parsed or re-rounded here.
/// </para>
/// </remarks>
internal static class FloatingPointSpaces
{
    /// <summary>The rank of <c>+INF</c> in the <c>xsd:float</c> space — the IEEE-754 single infinity bit pattern.</summary>
    private const long FloatInfinityRank = 0x7F800000L;

    /// <summary>The rank of <c>+INF</c> in the <c>xsd:double</c> space — the IEEE-754 double infinity bit pattern.</summary>
    private const long DoubleInfinityRank = 0x7FF0000000000000L;

    /// <summary>Whether a numeric space is one of the two IEEE-754 spaces this algebra models.</summary>
    /// <param name="space">The numeric space.</param>
    /// <returns><see langword="true"/> for the float and double spaces.</returns>
    public static bool IsModelled(OwlNumericSpace space)
    {
        return space is OwlNumericSpace.Float or OwlNumericSpace.Double;
    }

    /// <summary>The rank run covering a whole IEEE-754 space, from <c>-INF</c> to <c>+INF</c> — the run an unfaceted base type constrains to.</summary>
    /// <param name="space">The numeric space, one <see cref="IsModelled"/> admits.</param>
    /// <returns>The whole-space run.</returns>
    public static FloatingRankInterval Whole(OwlNumericSpace space)
    {
        long extreme = InfinityRank(space);

        return new FloatingRankInterval(-extreme, extreme);
    }

    /// <summary>
    /// Folds one ordered facet into a rank run, reporting failure for anything
    /// outside the modelled shape: a facet that is not one of the four ordered
    /// bounds, a bound literal of a different value space, a bound whose lexical
    /// form does not parse, and a <c>NaN</c>-valued bound.
    /// </summary>
    /// <param name="space">The numeric space the conjunction ranges over.</param>
    /// <param name="facetIri">The facet IRI.</param>
    /// <param name="bound">The facet's bound literal.</param>
    /// <param name="interval">The run folded so far.</param>
    /// <param name="tightened">The run with the facet folded in, on success.</param>
    /// <returns><see langword="true"/> when the facet was folded; <see langword="false"/> when it lies outside the modelled shape.</returns>
    public static bool TryApplyFacet(OwlNumericSpace space, Utf8String facetIri, Literal bound, FloatingRankInterval interval, out FloatingRankInterval tightened)
    {
        tightened = interval;

        bool isLower = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MinExclusive);
        bool isUpper = facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxExclusive);
        if(!isLower && !isUpper)
        {
            return false;
        }

        if(OwlDatatypeFamilies.NumericSpaceOf(bound.Datatype.Iri) != space
            || !OwlNumericLexicals.TryGetValue(bound.Value.ToString(), bound.Datatype.Iri, out NumericValue value)
            || !TryRank(space, value, out long rank))
        {
            return false;
        }

        bool exclusive = facetIri.Equals(Vocabulary.XsdFacets.MinExclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxExclusive);
        if(isLower)
        {
            long lower = exclusive ? rank + 1 : rank;
            tightened = new FloatingRankInterval(lower > interval.LowerRank ? lower : interval.LowerRank, interval.UpperRank);

            return true;
        }

        long upper = exclusive ? rank - 1 : rank;
        tightened = new FloatingRankInterval(interval.LowerRank, upper < interval.UpperRank ? upper : interval.UpperRank);

        return true;
    }

    /// <summary>The rank of the <c>+INF</c> of a space; zero for a space this algebra does not model.</summary>
    /// <param name="space">The numeric space.</param>
    /// <returns>The extreme rank.</returns>
    private static long InfinityRank(OwlNumericSpace space)
    {
        return space switch
        {
            OwlNumericSpace.Float => FloatInfinityRank,
            OwlNumericSpace.Double => DoubleInfinityRank,
            _ => 0
        };
    }

    /// <summary>The rank of a value in a space, reporting failure for a value of a different kind and for <c>NaN</c>, which has no place in the order.</summary>
    /// <param name="space">The numeric space.</param>
    /// <param name="value">The parsed bound value.</param>
    /// <param name="rank">The rank, on success.</param>
    /// <returns><see langword="true"/> when the value has a rank in the space.</returns>
    private static bool TryRank(OwlNumericSpace space, NumericValue value, out long rank)
    {
        rank = 0;

        return (space, value.Kind) switch
        {
            (OwlNumericSpace.Float, NumericKind.Float) => TryFloatRank(value.AsFloat(), out rank),
            (OwlNumericSpace.Double, NumericKind.Double) => TryDoubleRank(value.AsDouble(), out rank),
            _ => false
        };
    }

    /// <summary>The rank of a single-precision value: its magnitude bits, negated for a negative sign, so both zeros share rank zero.</summary>
    /// <param name="value">The single-precision value.</param>
    /// <param name="rank">The rank, on success.</param>
    /// <returns><see langword="true"/> for every value but <c>NaN</c>.</returns>
    private static bool TryFloatRank(float value, out long rank)
    {
        rank = 0;
        if(float.IsNaN(value))
        {
            return false;
        }

        int bits = BitConverter.SingleToInt32Bits(value);
        long magnitude = bits & int.MaxValue;
        rank = bits < 0 ? -magnitude : magnitude;

        return true;
    }

    /// <summary>The rank of a double-precision value: its magnitude bits, negated for a negative sign, so both zeros share rank zero.</summary>
    /// <param name="value">The double-precision value.</param>
    /// <param name="rank">The rank, on success.</param>
    /// <returns><see langword="true"/> for every value but <c>NaN</c>.</returns>
    private static bool TryDoubleRank(double value, out long rank)
    {
        rank = 0;
        if(double.IsNaN(value))
        {
            return false;
        }

        long bits = BitConverter.DoubleToInt64Bits(value);
        long magnitude = bits & long.MaxValue;
        rank = bits < 0 ? -magnitude : magnitude;

        return true;
    }
}
