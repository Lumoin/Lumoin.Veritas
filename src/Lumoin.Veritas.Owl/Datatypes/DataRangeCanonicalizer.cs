using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The semantic normal form of an <see cref="OwlDataRange"/> tree: a pure,
/// bottom-up rewrite that sorts a datatype restriction's facets into a canonical
/// order, collapses a degenerate exact-real interval to a point enumeration, and
/// collapses an empty exact-real interval to the shared canonical bottom. Two
/// ranges that denote the same value space through a facet reordering or a
/// degenerate-interval spelling reach one canonical instance, so the demand mint
/// interns them to one marker.
/// </summary>
/// <remarks>
/// The INSTANCE-PRESERVATION invariant: when the input is already canonical the
/// canonicalizer returns the SAME instance, and any subtree that does not change
/// is never rebuilt. So a range that arrives canonical (a context-arm
/// reconstructed demand) round-trips through the sidecar's entry canonicalization
/// unchanged, keeping the reconstructed-concept-to-marker map keyed on the same
/// instances.
/// </remarks>
internal static class DataRangeCanonicalizer
{
    /// <summary>The bound above which <see cref="TryEnumerate"/> declines to materialise a bounded-integer footprint.</summary>
    public const int MaxEnumerationCandidates = 64;

    /// <summary>
    /// Canonicalizes a data range bottom-up over an explicit post-order worklist,
    /// preserving the instance of any node whose canonical form is itself. The walk
    /// is total: an <see cref="OwlDataRange"/> is a finite immutable tree whose
    /// nesting depth is bounded by the parser's uniform nesting cap, so there is no
    /// depth at which the walk cannot answer.
    /// </summary>
    /// <param name="range">The data range.</param>
    /// <returns>The canonical range — the same instance when the input is already canonical.</returns>
    public static OwlDataRange Canonicalize(OwlDataRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        Stack<(OwlDataRange Node, bool Expanded)> work = new();
        Stack<OwlDataRange> results = new();
        work.Push((range, false));
        while(work.Count > 0)
        {
            (OwlDataRange node, bool expanded) = work.Pop();
            IReadOnlyList<OwlDataRange> children = ChildrenOf(node);
            if(children.Count == 0)
            {
                results.Push(CanonicalizeLeaf(node));

                continue;
            }

            if(!expanded)
            {
                work.Push((node, true));
                foreach(OwlDataRange child in children)
                {
                    work.Push((child, false));
                }

                continue;
            }

            List<OwlDataRange> canonicalChildren = new(children.Count);
            bool changed = false;
            for(int index = 0; index < children.Count; index++)
            {
                OwlDataRange canonicalChild = results.Pop();
                canonicalChildren.Add(canonicalChild);
                if(!ReferenceEquals(canonicalChild, children[index]))
                {
                    changed = true;
                }
            }

            results.Push(changed ? RebuildConstructor(node, canonicalChildren) : node);
        }

        return results.Pop();
    }

    /// <summary>
    /// Enumerates the integer values of a bounded exact-real integer restriction,
    /// on demand and within a budget, appending each as an <c>xsd:integer</c>
    /// literal. Reports failure for a non-integer, unbounded, or non-exact-real
    /// range, or a footprint above the budget. The canonicalizer never calls this
    /// itself; it is the explicit-values helper the checker-internal finite checks
    /// and the cross-pair subtraction paths reach for.
    /// </summary>
    /// <param name="range">The candidate range.</param>
    /// <param name="budget">The maximum footprint to materialise.</param>
    /// <param name="candidatesToAppendTo">The candidate literals, appended to.</param>
    /// <returns><see langword="true"/> when the range's footprint materialised within the budget.</returns>
    public static bool TryEnumerate(OwlDataRange range, int budget, List<Literal> candidatesToAppendTo)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(candidatesToAppendTo);

        if(range is not OwlDatatypeRestriction restriction
            || OwlDatatypeFamilies.NumericSpaceOf(restriction.Datatype.Iri) != OwlNumericSpace.ExactReal
            || !ExactIntervals.TryBuildInterval(restriction.Datatype, restriction, out ExactInterval interval, out _)
            || !interval.IntegersOnly
            || !interval.TryIntegerFootprint(out BigInteger? low, out BigInteger? high)
            || low is not BigInteger lowValue
            || high is not BigInteger highValue)
        {
            return false;
        }

        BigInteger count = highValue - lowValue + BigInteger.One;
        if(count <= BigInteger.Zero || count > budget)
        {
            return false;
        }

        for(BigInteger value = lowValue; value <= highValue; value++)
        {
            candidatesToAppendTo.Add(new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer)));
        }

        return true;
    }

    /// <summary>The child data ranges of a constructor node, or an empty list for a leaf.</summary>
    /// <param name="node">The data range.</param>
    /// <returns>The children.</returns>
    private static IReadOnlyList<OwlDataRange> ChildrenOf(OwlDataRange node)
    {
        return node switch
        {
            OwlDataIntersectionOf intersection => intersection.Ranges,
            OwlDataUnionOf union => union.Ranges,
            OwlDataComplementOf complement => [complement.Range],
            _ => []
        };
    }

    /// <summary>Rebuilds a constructor node over its canonicalized children.</summary>
    /// <param name="node">The original constructor node.</param>
    /// <param name="canonicalChildren">The canonicalized children, in declaration order.</param>
    /// <returns>The rebuilt node.</returns>
    private static OwlDataRange RebuildConstructor(OwlDataRange node, List<OwlDataRange> canonicalChildren)
    {
        return node switch
        {
            OwlDataIntersectionOf => new OwlDataIntersectionOf(canonicalChildren),
            OwlDataUnionOf => new OwlDataUnionOf(canonicalChildren),
            OwlDataComplementOf => new OwlDataComplementOf(canonicalChildren[0]),
            _ => node
        };
    }

    /// <summary>Canonicalizes a leaf data range: a bare datatype and an enumeration are already canonical; a restriction is rewritten or facet-sorted.</summary>
    /// <param name="node">The leaf data range.</param>
    /// <returns>The canonical leaf.</returns>
    private static OwlDataRange CanonicalizeLeaf(OwlDataRange node)
    {
        return node is OwlDatatypeRestriction restriction ? CanonicalizeRestriction(restriction) : node;
    }

    /// <summary>
    /// Canonicalizes a datatype restriction: an exact-real restriction carrying
    /// only the four ordering facets rewrites to the canonical bottom when its
    /// interval is empty, or to a point enumeration when its interval is a single
    /// value; otherwise the facets are sorted into canonical order.
    /// </summary>
    /// <param name="restriction">The datatype restriction.</param>
    /// <returns>The canonical form.</returns>
    private static OwlDataRange CanonicalizeRestriction(OwlDatatypeRestriction restriction)
    {
        if(OwlDatatypeFamilies.NumericSpaceOf(restriction.Datatype.Iri) == OwlNumericSpace.ExactReal
            && ExactIntervals.TryBuildInterval(restriction.Datatype, restriction, out ExactInterval interval, out _))
        {
            if(interval.IsEmpty())
            {
                return CanonicalForms.EmptyRange;
            }

            if(TryPointLiteral(interval, out Literal point))
            {
                return new OwlDataOneOf([point]);
            }
        }

        return SortFacets(restriction);
    }

    /// <summary>
    /// The single point literal an exact-real interval denotes, minted with the
    /// value's representable datatype (<c>xsd:integer</c> when integral, else
    /// <c>xsd:decimal</c>) — never the restriction's base, which may lack a lexical
    /// space. Recognises a both-inclusive degenerate point and an integers-only
    /// footprint of exactly one integer (e.g. an exclusive-bounds interval).
    /// </summary>
    /// <param name="interval">The exact-real interval.</param>
    /// <param name="point">The point literal, when the interval is a single value.</param>
    /// <returns><see langword="true"/> when the interval is a single value.</returns>
    private static bool TryPointLiteral(ExactInterval interval, out Literal point)
    {
        if(interval.TryDegeneratePoint(out NumericValue value))
        {
            point = PointLiteralOf(value);

            return true;
        }

        if(interval.IntegersOnly
            && interval.TryIntegerFootprint(out BigInteger? low, out BigInteger? high)
            && low is BigInteger lowValue
            && high is BigInteger highValue
            && lowValue == highValue)
        {
            point = PointLiteralOf(new NumericValue(lowValue));

            return true;
        }

        point = default!;

        return false;
    }

    /// <summary>Mints the canonical point literal of an exact-real value with its representable datatype IRI and canonical lexical form.</summary>
    /// <param name="value">The exact-real value.</param>
    /// <returns>The point literal.</returns>
    private static Literal PointLiteralOf(NumericValue value)
    {
        return new Literal(Utf8Strings.From(value.ToCanonicalLexical()), new NamedNode(value.DatatypeIri));
    }

    /// <summary>
    /// Sorts a restriction's facets into canonical order — by facet IRI, then the
    /// bound literal's lexical form, then its datatype IRI — preserving the
    /// restriction's instance when the facets are already in that order. The sort
    /// is semantics-preserving: the facet algebra dispatches purely by facet IRI,
    /// so ordering is denotationally inert but part of the canonical form.
    /// </summary>
    /// <param name="restriction">The datatype restriction.</param>
    /// <returns>The facet-sorted restriction — the same instance when already sorted.</returns>
    private static OwlDatatypeRestriction SortFacets(OwlDatatypeRestriction restriction)
    {
        IReadOnlyList<OwlFacetRestriction> facets = restriction.Restrictions;
        if(facets.Count < 2)
        {
            return restriction;
        }

        List<OwlFacetRestriction> sorted = [.. facets];
        sorted.Sort(CompareFacets);

        for(int index = 0; index < facets.Count; index++)
        {
            if(!facets[index].Equals(sorted[index]))
            {
                return new OwlDatatypeRestriction(restriction.Datatype, sorted);
            }
        }

        return restriction;
    }

    /// <summary>The canonical total order over facet restrictions: facet IRI, then bound lexical form, then bound datatype IRI.</summary>
    /// <param name="first">The first facet.</param>
    /// <param name="second">The second facet.</param>
    /// <returns>The sign of the comparison.</returns>
    private static int CompareFacets(OwlFacetRestriction first, OwlFacetRestriction second)
    {
        int byFacet = first.Facet.Iri.Span.SequenceCompareTo(second.Facet.Iri.Span);
        if(byFacet != 0)
        {
            return byFacet;
        }

        int byValue = first.Value.Value.Span.SequenceCompareTo(second.Value.Value.Span);
        if(byValue != 0)
        {
            return byValue;
        }

        return first.Value.Datatype.Iri.Span.SequenceCompareTo(second.Value.Datatype.Iri.Span);
    }
}
