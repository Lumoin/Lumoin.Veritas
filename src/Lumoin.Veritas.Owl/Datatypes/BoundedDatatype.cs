using System;
using System.Collections.Generic;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes.Automata;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// A registered datatype whose value space is an exact-real base narrowed by ordering facets, held as an
/// interval over the shared exact-real interval algebra. Membership, emptiness, and the distinct-value
/// count come from closed-form interval arithmetic — the integer footprint of a bounded integer interval,
/// or the continuum verdict of a real interval — never by enumeration.
/// </summary>
public sealed class BoundedDatatype : RegisteredDatatype
{
    /// <summary>The datatype IRI this definition owns.</summary>
    private Utf8String Iri { get; }

    /// <summary>The base exact-real interval, before any conjunction facet.</summary>
    private ExactInterval BaseInterval { get; }

    /// <summary>Whether the base and facets built a modelled exact-real interval.</summary>
    private bool Valid { get; }

    /// <summary>Creates a bounded datatype over an exact-real base and ordering facets.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <param name="baseIri">The exact-real base datatype IRI.</param>
    /// <param name="facets">The ordering facets narrowing the base.</param>
    public BoundedDatatype(Utf8String datatypeIri, Utf8String baseIri, IReadOnlyList<OwlFacetRestriction> facets)
    {
        ArgumentNullException.ThrowIfNull(facets);
        Iri = datatypeIri;
        Valid = ExactIntervals.TryBuildInterval(new NamedNode(baseIri), new OwlDatatypeRestriction(new NamedNode(baseIri), facets), out ExactInterval interval, out _);
        BaseInterval = Valid ? interval : ExactInterval.Unbounded;
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => Iri;

    /// <inheritdoc/>
    public override DatatypeMembership Contains(Literal value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!Valid || OwlDatatypeFamilies.NumericSpaceOf(value.Datatype.Iri) != OwlNumericSpace.ExactReal)
        {
            return Valid ? DatatypeMembership.Out : DatatypeMembership.Indeterminate;
        }

        if(!OwlNumericLexicals.TryGetValue(value.Value.ToString(), value.Datatype.Iri, out NumericValue numeric))
        {
            return DatatypeMembership.Indeterminate;
        }

        return BaseInterval.Contains(numeric) ? DatatypeMembership.In : DatatypeMembership.Out;
    }

    /// <inheritdoc/>
    public override DatatypeValueIdentity SameValue(Literal first, Literal second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return DatatypeLexical.Identity(first, second);
    }

    /// <inheritdoc/>
    public override DatatypeSatisfiability DecideConjunction(in DatatypeConjunction question)
    {
        if(!Valid)
        {
            return DatatypeSatisfiability.Unknown;
        }

        ExactInterval interval = ApplyFacets(question.PositiveFacets, out bool unmodelled);
        if(interval.IsEmpty())
        {
            return DatatypeSatisfiability.Unsatisfiable;
        }

        return unmodelled || question.NegatedAtoms.Count > 0 ? DatatypeSatisfiability.Unknown : DatatypeSatisfiability.Satisfiable;
    }

    /// <inheritdoc/>
    public override DatatypeCountBound DistinctValues(in DatatypeConjunction question)
    {
        if(!Valid)
        {
            return DatatypeCountBound.Unknown;
        }

        ExactInterval interval = ApplyFacets(question.PositiveFacets, out bool unmodelled);
        if(unmodelled || question.NegatedAtoms.Count > 0)
        {
            return DatatypeCountBound.Unknown;
        }

        if(interval.IsEmpty())
        {
            return DatatypeCountBound.Of(0);
        }

        if(!interval.IntegersOnly)
        {
            return interval.TryDegeneratePoint(out _) ? DatatypeCountBound.Of(1) : DatatypeCountBound.Infinite;
        }

        if(!interval.TryIntegerFootprint(out BigInteger? low, out BigInteger? high) || low is not BigInteger lowValue || high is not BigInteger highValue)
        {
            return DatatypeCountBound.Infinite;
        }

        BigInteger footprint = highValue - lowValue + BigInteger.One;

        return footprint > long.MaxValue ? DatatypeCountBound.Infinite : DatatypeCountBound.Of((long)footprint);
    }

    /// <inheritdoc/>
    internal override AdmissibilityResult CheckAdmissibility(AutomatonBudgets budgets)
    {
        return Valid ? AdmissibilityResult.Accepted : AdmissibilityResult.Rejected();
    }

    /// <summary>Applies a conjunction's positive facets to the base interval, flagging any facet the interval algebra cannot apply.</summary>
    /// <param name="facets">The positive facets.</param>
    /// <param name="unmodelled">Whether a facet could not be applied.</param>
    /// <returns>The narrowed interval.</returns>
    private ExactInterval ApplyFacets(IReadOnlyList<OwlFacetRestriction> facets, out bool unmodelled)
    {
        unmodelled = false;
        ExactInterval interval = BaseInterval;
        foreach(OwlFacetRestriction facet in facets)
        {
            if(!ExactIntervals.TryApplyFacet(facet, ref interval))
            {
                unmodelled = true;
            }
        }

        return interval;
    }
}
