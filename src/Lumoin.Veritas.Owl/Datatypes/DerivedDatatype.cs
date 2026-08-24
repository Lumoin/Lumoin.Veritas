using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype derived from a base registered datatype by adding facets. Membership tests the
/// base then the added facets; the emptiness and counting questions fold the added facets into the
/// conjunction and delegate to the base, so the derivation is a closed form over the base's value space.
/// Admissibility and the self-test re-run the base's — a derivation over a pattern base inherits its
/// white-space gate.
/// </summary>
public sealed class DerivedDatatype : RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The base datatype the derivation narrows.</summary>
    private RegisteredDatatype Base { get; }

    /// <summary>The facets the derivation adds to the base.</summary>
    private IReadOnlyList<OwlFacetRestriction> ExtraFacets { get; }

    /// <summary>The automaton budgets the added facet checks run under.</summary>
    private AutomatonBudgets Budgets { get; }

    /// <summary>Creates a derived datatype.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="baseDatatype">The base datatype.</param>
    /// <param name="extraFacets">The added facets.</param>
    /// <param name="budgets">The automaton budgets, or <see langword="null"/> for the shared defaults.</param>
    public DerivedDatatype(Utf8String datatypeIri, RegisteredDatatype baseDatatype, IReadOnlyList<OwlFacetRestriction> extraFacets, AutomatonBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(baseDatatype);
        ArgumentNullException.ThrowIfNull(extraFacets);
        Iri = datatypeIri;
        Base = baseDatatype;
        ExtraFacets = extraFacets;
        Budgets = budgets ?? AutomatonBudgets.Default;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override bool SelfCertified => Base.SelfCertified;

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);
        DatatypeMembership baseMembership = Base.Contains(value);
        if(baseMembership != DatatypeMembership.In)
        {
            return baseMembership;
        }

        bool indeterminate = false;
        foreach(OwlFacetRestriction facet in ExtraFacets)
        {
            DatatypeMembership facetMembership = FacetEvaluator.Evaluate(facet, value, Budgets);
            if(facetMembership == DatatypeMembership.Out)
            {
                return DatatypeMembership.Out;
            }

            indeterminate |= facetMembership == DatatypeMembership.Indeterminate;
        }

        return indeterminate ? DatatypeMembership.Indeterminate : DatatypeMembership.In;
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return Base.SameValue(first, second);
    }

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        DatatypeConjunction merged = Merge(question);

        return Base.DecideConjunction(in merged);
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        DatatypeConjunction merged = Merge(question);

        return Base.DistinctValues(in merged);
    }

    /// <inheritdoc/>
    internal override AdmissibilityResult CheckAdmissibility(AutomatonBudgets budgets)
    {
        return Base.CheckAdmissibility(budgets);
    }

    /// <inheritdoc/>
    internal override bool RunSelfTest(AutomatonBudgets budgets)
    {
        return Base.RunSelfTest(budgets);
    }

    /// <summary>Merges the added facets into a conjunction's positive facets.</summary>
    /// <param name="question">The conjunction.</param>
    /// <returns>The merged conjunction.</returns>
    private DatatypeConjunction Merge(in DatatypeConjunction question)
    {
        List<OwlFacetRestriction> facets = [.. question.PositiveFacets, .. ExtraFacets];

        return new DatatypeConjunction(facets, question.NegatedAtoms, question.Threshold);
    }
}
