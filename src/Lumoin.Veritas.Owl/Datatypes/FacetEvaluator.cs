using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// Evaluates one XSD facet restriction against a candidate value to a three-valued membership, for the
/// value-level checks the declarative tier's <c>Contains</c> makes: length facets by rune count, ordering
/// facets over the exact-real numeric line, and pattern facets by an automaton walk. A facet the evaluator
/// does not model answers <see cref="DatatypeMembership.Indeterminate"/> rather than guessing.
/// </summary>
internal static class FacetEvaluator
{
    /// <summary>Evaluates a facet restriction against a value.</summary>
    /// <param name="facet">The facet restriction.</param>
    /// <param name="value">The candidate value.</param>
    /// <param name="budgets">The automaton budgets a pattern facet compiles under.</param>
    /// <returns>The membership verdict over the single facet.</returns>
    public static DatatypeMembership Evaluate(OwlFacetRestriction facet, Literal value, AutomatonBudgets budgets)
    {
        Utf8String facetIri = facet.Facet.Iri;
        if(facetIri.Equals(Vocabulary.XsdFacets.Length) || facetIri.Equals(Vocabulary.XsdFacets.MinLength) || facetIri.Equals(Vocabulary.XsdFacets.MaxLength))
        {
            return LengthFacet(facetIri, facet.Value, value);
        }

        if(facetIri.Equals(Vocabulary.XsdFacets.Pattern))
        {
            return PatternFacet(facet.Value, value, budgets);
        }

        bool isLower = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MinExclusive);
        bool isUpper = facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxExclusive);
        if(isLower || isUpper)
        {
            bool inclusive = facetIri.Equals(Vocabulary.XsdFacets.MinInclusive) || facetIri.Equals(Vocabulary.XsdFacets.MaxInclusive);

            return OrderingFacet(value, facet.Value, isLower, inclusive);
        }

        return DatatypeMembership.Indeterminate;
    }

    /// <summary>Evaluates a length facet by rune count.</summary>
    /// <param name="facetIri">The length facet IRI.</param>
    /// <param name="bound">The length bound literal.</param>
    /// <param name="value">The candidate value.</param>
    /// <returns>The membership verdict.</returns>
    private static DatatypeMembership LengthFacet(Utf8String facetIri, Literal bound, Literal value)
    {
        if(!int.TryParse(bound.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit))
        {
            return DatatypeMembership.Indeterminate;
        }

        int length = DatatypeLexical.RuneCount(value.Value);
        bool satisfied = facetIri switch
        {
            _ when facetIri.Equals(Vocabulary.XsdFacets.Length) => length == limit,
            _ when facetIri.Equals(Vocabulary.XsdFacets.MinLength) => length >= limit,
            _ => length <= limit
        };

        return satisfied ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <summary>Evaluates a pattern facet by compiling it and walking the value's code points.</summary>
    /// <param name="pattern">The pattern literal.</param>
    /// <param name="value">The candidate value.</param>
    /// <param name="budgets">The automaton budgets.</param>
    /// <returns>The membership verdict.</returns>
    private static DatatypeMembership PatternFacet(Literal pattern, Literal value, AutomatonBudgets budgets)
    {
        PatternCompileResult compiled = XsdPatternCompiler.Compile(pattern.Value.Span, budgets);
        if(compiled.Status != PatternCompileStatus.Compiled)
        {
            return DatatypeMembership.Indeterminate;
        }

        return compiled.Automaton!.Accepts(DatatypeLexical.CodePoints(value.Value)) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <summary>Evaluates an ordering facet over the exact-real numeric line.</summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="bound">The bound literal.</param>
    /// <param name="isLower">Whether the facet is a lower bound.</param>
    /// <param name="inclusive">Whether the bound is inclusive.</param>
    /// <returns>The membership verdict.</returns>
    private static DatatypeMembership OrderingFacet(Literal value, Literal bound, bool isLower, bool inclusive)
    {
        if(OwlDatatypeFamilies.NumericSpaceOf(value.Datatype.Iri) != OwlNumericSpace.ExactReal
            || OwlDatatypeFamilies.NumericSpaceOf(bound.Datatype.Iri) != OwlNumericSpace.ExactReal
            || !OwlNumericLexicals.TryGetValue(value.Value.ToString(), value.Datatype.Iri, out NumericValue candidate)
            || !OwlNumericLexicals.TryGetValue(bound.Value.ToString(), bound.Datatype.Iri, out NumericValue boundValue))
        {
            return DatatypeMembership.Indeterminate;
        }

        ComparisonResult comparison = NumericValue.Compare(candidate, boundValue);
        if(comparison == ComparisonResult.Incomparable)
        {
            return DatatypeMembership.Indeterminate;
        }

        bool satisfied = (isLower, inclusive) switch
        {
            (true, true) => comparison is ComparisonResult.Greater or ComparisonResult.Equal,
            (true, false) => comparison == ComparisonResult.Greater,
            (false, true) => comparison is ComparisonResult.Less or ComparisonResult.Equal,
            (false, false) => comparison == ComparisonResult.Less
        };

        return satisfied ? DatatypeMembership.In : DatatypeMembership.Out;
    }
}
