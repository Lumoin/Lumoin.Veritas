using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The datatype-registry arc stage-B cross-pair subtraction over a disjoint
/// data-property pair: a non-point demand under one property, minus the forced
/// points under a disjoint property, decides the pair. Each row carries its
/// certified battery id; the stage-B holding pin documents why the retained W3C
/// row-3 pin is safe through this stage.
/// </summary>
[TestClass]
internal sealed class DataRestrictionCrossPairTests
{
    /// <summary>An unmapped datatype IRI the checker does not model.</summary>
    private static Utf8String OpaqueDatatype { get; } = Utf8Strings.From("http://example.org/Opaque");

    /// <summary>XP-SUB-UNSAT: q ranges over integer[10,11]; p is forced to both 10 and 11; disjoint. The q-witness must avoid both, leaving nothing — a clash.</summary>
    [TestMethod]
    public void XPSUBUNSATNonPointMinusOppositePointsEmptyClashes()
    {
        DataPropertyBox box = Box(Disjoint("p", "q"));
        List<AlcConcept> demands = [Some("q", IntegerBetween(10, 11)), Some("p", IntegerPoint(10)), Some("p", IntegerPoint(11))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>XP-SUB-SAT: q ranges over integer[10,12]; p is forced to 10 and 11; disjoint. The q-witness can still take 12 — consistent.</summary>
    [TestMethod]
    public void XPSUBSATNonPointMinusOppositePointsNonEmptyConsistent()
    {
        DataPropertyBox box = Box(Disjoint("p", "q"));
        List<AlcConcept> demands = [Some("q", IntegerBetween(10, 12)), Some("p", IntegerPoint(10)), Some("p", IntegerPoint(11))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>XP-SUB-UNK: q ranges over an unmapped datatype restriction; p is forced to 10; disjoint. The subtraction is undecidable, so the sidecar soundly abstains.</summary>
    [TestMethod]
    public void XPSUBUNKUnmappedNonPointAbstains()
    {
        DataPropertyBox box = Box(Disjoint("p", "q"));
        List<AlcConcept> demands = [Some("q", OpaqueAtLeast(10)), Some("p", IntegerPoint(10))];

        Assert.AreEqual(DataConsistencyStatus.Undecided, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>XP-MINCARD: q demands two distinct values in integer[10,11]; p is forced to 10; disjoint. Subtracting 10 leaves only 11, one value, below the threshold — a clash the threshold-preserving subtraction catches.</summary>
    [TestMethod]
    public void XPMINCARDThresholdPreservingSubtractionClashes()
    {
        DataPropertyBox box = Box(Disjoint("p", "q"));
        List<AlcConcept> demands = [MinCard(2, "q", IntegerBetween(10, 11)), Some("p", IntegerPoint(10))];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>XP-SAMESIDE: q carries a same-side point 10 and a min-cardinality of two over integer[10,11]; p is forced to 99; disjoint. The same-side point is never subtracted, so q keeps {10,11} — consistent.</summary>
    [TestMethod]
    public void XPSAMESIDESameSidePointNotSubtracted()
    {
        DataPropertyBox box = Box(Disjoint("p", "q"));
        List<AlcConcept> demands = [Some("q", IntegerPoint(10)), MinCard(2, "q", IntegerBetween(10, 11)), Some("p", IntegerPoint(99))];

        Assert.AreEqual(DataConsistencyStatus.Consistent, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out _));
    }

    /// <summary>HOLD-B / NFA-BUILTIN-MINUS: the built-in xsd:string automaton route decides the conjunction [string+pattern a[bc], not-OneOf(ab), not-OneOf(ac)] Unsatisfiable — the pattern language {ab, ac} minus both excluded strings is empty. No registration is needed; the route is a built-in over the string value space.</summary>
    [TestMethod]
    public void HOLDBStringPatternMinusEnumerationsUnsatisfiable()
    {
        OwlDataRange pattern = new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.String), [Facet(Vocabulary.XsdFacets.Pattern, Lit("a[bc]", Vocabulary.Xsd.String))]);
        List<OwlDataRange> conjunction =
        [
            pattern,
            new OwlDataComplementOf(new OwlDataOneOf([Lit("ab", Vocabulary.Xsd.String)])),
            new OwlDataComplementOf(new OwlDataOneOf([Lit("ac", Vocabulary.Xsd.String)])),
        ];

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(conjunction, DatatypeRegistry.Empty));
    }

    /// <summary>MRK-04: a canonical point demand round-trips through the sidecar's entry canonicalization as an identity no-op, so the reported conflict carries the caller's SAME concept instances (the instance-preservation invariant the context-arm marker map relies on).</summary>
    [TestMethod]
    public void MRK04CanonicalInputConflictCarriesSameInstances()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"));
        AlcDataSome demandA = Some("a", new OwlDataOneOf([Lit("7", Vocabulary.Xsd.Integer)]));
        AlcDataSome demandB = Some("b", new OwlDataOneOf([Lit("7", Vocabulary.Xsd.Integer)]));
        List<AlcConcept> demands = [demandA, demandB];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out IReadOnlyList<AlcConcept> conflict));
        Assert.IsTrue(ContainsSame(conflict, demandA), "The conflict carries the caller's original demand-A instance.");
        Assert.IsTrue(ContainsSame(conflict, demandB), "The conflict carries the caller's original demand-B instance.");
    }

    /// <summary>A non-canonical (degenerate-interval) point demand is rebuilt internally, yet the reported conflict is mapped back to the caller's ORIGINAL concept instances, so a clause-learning caller keys the conflict onto the concept it handed in.</summary>
    [TestMethod]
    public void NonCanonicalInputConflictMapsBackToOriginals()
    {
        DataPropertyBox box = Box(Disjoint("a", "b"));
        AlcDataSome demandA = Some("a", IntegerPoint(7));
        AlcDataSome demandB = Some("b", IntegerPoint(7));
        List<AlcConcept> demands = [demandA, demandB];

        Assert.AreEqual(DataConsistencyStatus.Clash, DataRestrictionConsistency.Decide(demands, box, DatatypeRegistry.Empty, out IReadOnlyList<AlcConcept> conflict));
        Assert.IsTrue(ContainsSame(conflict, demandA), "The conflict is mapped back to the caller's original demand-A instance.");
        Assert.IsTrue(ContainsSame(conflict, demandB), "The conflict is mapped back to the caller's original demand-B instance.");
    }

    /// <summary>Whether a conflict core carries a concept by reference identity.</summary>
    /// <param name="conflict">The conflict core.</param>
    /// <param name="concept">The concept to look for.</param>
    /// <returns><see langword="true"/> when the same instance is present.</returns>
    private static bool ContainsSame(IReadOnlyList<AlcConcept> conflict, AlcConcept concept)
    {
        foreach(AlcConcept present in conflict)
        {
            if(ReferenceEquals(present, concept))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An existential data demand on a property.</summary>
    /// <param name="property">The property local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The concept.</returns>
    private static AlcDataSome Some(string property, OwlDataRange range)
    {
        return new AlcDataSome(Iri(property), range);
    }

    /// <summary>A minimum-cardinality data demand on a property.</summary>
    /// <param name="count">The minimum count.</param>
    /// <param name="property">The property local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The concept.</returns>
    private static AlcDataMinCard MinCard(int count, string property, OwlDataRange range)
    {
        return new AlcDataMinCard(count, Iri(property), range);
    }

    /// <summary>A typed literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal Lit(string lexical, Utf8String datatypeIri)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(datatypeIri));
    }

    /// <summary>A facet–value pair.</summary>
    /// <param name="facetIri">The facet IRI.</param>
    /// <param name="value">The facet value.</param>
    /// <returns>The facet restriction.</returns>
    private static OwlFacetRestriction Facet(Utf8String facetIri, Literal value)
    {
        return new OwlFacetRestriction(new NamedNode(facetIri), value);
    }

    /// <summary>An inclusive integer interval restriction.</summary>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBetween(int low, int high)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, low), (Vocabulary.XsdFacets.MaxInclusive, high));
    }

    /// <summary>A degenerate single-point integer interval — a both-inclusive bound at one value.</summary>
    /// <param name="value">The single admitted value.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerPoint(int value)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, value), (Vocabulary.XsdFacets.MaxInclusive, value));
    }

    /// <summary>A restriction over an unmapped datatype the checker does not model, bounded below.</summary>
    /// <param name="bound">The inclusive lower bound value.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction OpaqueAtLeast(int bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(OpaqueDatatype), [Facet(Vocabulary.XsdFacets.MinInclusive, Lit(bound.ToString(System.Globalization.CultureInfo.InvariantCulture), Vocabulary.Xsd.Integer))]);
    }

    /// <summary>An integer datatype restriction over the given integer facet bounds.</summary>
    /// <param name="bounds">The facet–bound pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerRestriction(params (Utf8String Facet, int Bound)[] bounds)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((Utf8String facet, int bound) in bounds)
        {
            facets.Add(Facet(facet, Lit(bound.ToString(System.Globalization.CultureInfo.InvariantCulture), Vocabulary.Xsd.Integer)));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }

    /// <summary>Builds a data-property box from the given axioms.</summary>
    /// <param name="axioms">The data-property axioms.</param>
    /// <returns>The box.</returns>
    private static DataPropertyBox Box(params OwlAxiom[] axioms)
    {
        return DataPropertyBox.Build(axioms);
    }

    /// <summary>A <c>DisjointDataProperties</c> axiom.</summary>
    /// <param name="properties">The mutually disjoint property local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointDataPropertiesAxiom Disjoint(params string[] properties)
    {
        List<NamedNode> operands = [];
        foreach(string property in properties)
        {
            operands.Add(new NamedNode(Iri(property)));
        }

        return new OwlDisjointDataPropertiesAxiom(operands) { Origin = Origin() };
    }

    /// <summary>A placeholder origin quad for a constructed axiom.</summary>
    /// <returns>The origin quad.</returns>
    private static Quad Origin()
    {
        return new Quad(new NamedNode(Iri("s")), new NamedNode(Iri("p")), new NamedNode(Iri("o")), Graph: null);
    }

    /// <summary>An example-namespace IRI for a local name.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string localName)
    {
        return Utf8Strings.From("http://example.org/" + localName);
    }
}
