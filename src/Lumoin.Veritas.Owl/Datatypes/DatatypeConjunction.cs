using System.Collections.Generic;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// One data node's constraint on a registered datatype: the positive facet
/// restrictions the value must satisfy, the negated data ranges the value must
/// avoid, and an optional minimum-cardinality threshold the counting question is
/// asked against. The checker builds this at a consult site from the data atom in
/// scope; every slot is empty (or zero) in the unconstrained base case.
/// </summary>
/// <param name="PositiveFacets">The facet restrictions the value must satisfy — pattern, length, and ordering facets over the registered base.</param>
/// <param name="NegatedAtoms">The negated data ranges whose values are removed from the registered value space.</param>
/// <param name="Threshold">The minimum number of distinct values the counting question demands, or zero when no threshold is asked.</param>
public readonly record struct DatatypeConjunction(
    IReadOnlyList<OwlFacetRestriction> PositiveFacets,
    IReadOnlyList<OwlDataRange> NegatedAtoms,
    int Threshold)
{
    /// <summary>The empty conjunction — no facets, no negated atoms, no threshold.</summary>
    public static DatatypeConjunction Empty { get; } = new([], [], 0);

    /// <summary>A conjunction of positive facets alone.</summary>
    /// <param name="facets">The positive facet restrictions.</param>
    /// <returns>The conjunction.</returns>
    public static DatatypeConjunction OfFacets(IReadOnlyList<OwlFacetRestriction> facets)
    {
        return new DatatypeConjunction(facets, [], 0);
    }
}
