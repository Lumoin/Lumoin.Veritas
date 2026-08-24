using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The datatype-registry arc stage-B canonicalization layer: structural value
/// equality, the semantic normal form (facet sort, degenerate interval to a point
/// enumeration, empty interval to the shared canonical bottom), the on-demand
/// bounded-integer enumeration, and the writer's hard rejection of the canonical
/// bottom. Each row carries its certified battery id.
/// </summary>
[TestClass]
internal sealed class DataRangeCanonicalizationTests
{
    /// <summary>CAN-01: canonicalization is idempotent and preserves the instance of an already-canonical range and its unchanged subtrees.</summary>
    [TestMethod]
    public void CAN01IdempotentAndInstancePreserving()
    {
        OwlDataRange alreadyCanonical = new OwlDataOneOf([Lit("5", Vocabulary.Xsd.Integer)]);
        Assert.AreSame(alreadyCanonical, DataRangeCanonicalizer.Canonicalize(alreadyCanonical), "An already-canonical enumeration returns the same instance.");

        OwlDataRange once = DataRangeCanonicalizer.Canonicalize(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)));
        Assert.AreSame(once, DataRangeCanonicalizer.Canonicalize(once), "Re-canonicalizing a canonical form returns the same instance.");

        OwlDataRange intersection = new OwlDataIntersectionOf([DatatypeRange(Vocabulary.Xsd.Integer), DatatypeRange(Vocabulary.Xsd.Decimal)]);
        Assert.AreSame(intersection, DataRangeCanonicalizer.Canonicalize(intersection), "A constructor over already-canonical children is not rebuilt.");
    }

    /// <summary>CAN-02: xsd:integer with minInclusive 5 and maxInclusive 5 canonicalizes to the point enumeration of exactly the integer 5.</summary>
    [TestMethod]
    public void CAN02DegenerateInclusiveIntegerToPoint()
    {
        OwlDataOneOf point = AssertPoint(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)), "5", Vocabulary.Xsd.Integer);

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([point], 1, DatatypeRegistry.Empty), "The point holds one value.");
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([point], 2, DatatypeRegistry.Empty), "The point holds exactly one value, so a demand for two clashes.");
    }

    /// <summary>CAN-03: a degenerate owl:real[5,5] mints the point with the representable IRI xsd:integer, never the base owl:real (which has no lexical space).</summary>
    [TestMethod]
    public void CAN03DegenerateRealMintsRepresentableInteger()
    {
        AssertPoint(new OwlDatatypeRestriction(new NamedNode(OwlVocabulary.Real), [Facet(Vocabulary.XsdFacets.MinInclusive, Lit("5", Vocabulary.Xsd.Integer)), Facet(Vocabulary.XsdFacets.MaxInclusive, Lit("5", Vocabulary.Xsd.Integer))]), "5", Vocabulary.Xsd.Integer);
    }

    /// <summary>CAN-04: a degenerate owl:rational[5.5,5.5] mints the point with the representable IRI xsd:decimal, never the base owl:rational.</summary>
    [TestMethod]
    public void CAN04DegenerateRationalMintsRepresentableDecimal()
    {
        AssertPoint(new OwlDatatypeRestriction(new NamedNode(OwlVocabulary.Rational), [Facet(Vocabulary.XsdFacets.MinInclusive, Lit("5.5", Vocabulary.Xsd.Decimal)), Facet(Vocabulary.XsdFacets.MaxInclusive, Lit("5.5", Vocabulary.Xsd.Decimal))]), "5.5", Vocabulary.Xsd.Decimal);
    }

    /// <summary>CAN-05: xsd:integer with minExclusive 4 and maxExclusive 6 has an integer footprint of one (5), canonicalizing to the point enumeration.</summary>
    [TestMethod]
    public void CAN05ExclusiveBoundsFootprintOfOneToPoint()
    {
        AssertPoint(IntegerRestriction((Vocabulary.XsdFacets.MinExclusive, 4), (Vocabulary.XsdFacets.MaxExclusive, 6)), "5", Vocabulary.Xsd.Integer);
    }

    /// <summary>CAN-06: xsd:integer with minInclusive 6 and maxInclusive 5 is empty, canonicalizing to the shared canonical bottom by reference.</summary>
    [TestMethod]
    public void CAN06EmptyIntervalToCanonicalBottom()
    {
        Assert.AreSame(CanonicalForms.EmptyRange, DataRangeCanonicalizer.Canonicalize(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 6), (Vocabulary.XsdFacets.MaxInclusive, 5))), "An empty interval canonicalizes to the shared canonical bottom.");
    }

    /// <summary>CAN-07: the canonical bottom is reference-identifiable, is unsatisfiable and holds zero distinct values through every checker consumer, and is hard-rejected by the RDF writer.</summary>
    [TestMethod]
    public void CAN07CanonicalBottomConsumerBehaviorsAndWriterRejection()
    {
        Assert.AreSame(CanonicalForms.EmptyRange, DataRangeCanonicalizer.Canonicalize(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 6), (Vocabulary.XsdFacets.MaxInclusive, 5))));
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(CanonicalForms.EmptyRange, DatatypeRegistry.Empty), "The canonical bottom is unsatisfiable.");
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([CanonicalForms.EmptyRange], 1, DatatypeRegistry.Empty), "The canonical bottom holds zero distinct values.");

        OwlOntologyDocument document = Document(new OwlDataPropertyRangeAxiom(new NamedNode(Iri("p")), CanonicalForms.EmptyRange) { Origin = Origin() });
        Assert.ThrowsExactly<InvalidOperationException>(() => OwlStructuralToRdf.ToQuads(document));
    }

    /// <summary>CAN-08: two restrictions with the same facet set in different order canonicalize to structurally equal forms (the facet sort recovers sharing).</summary>
    [TestMethod]
    public void CAN08FacetSortMakesReorderedRestrictionsEqual()
    {
        OwlDataRange forward = DataRangeCanonicalizer.Canonicalize(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)));
        OwlDataRange reversed = DataRangeCanonicalizer.Canonicalize(IntegerRestriction((Vocabulary.XsdFacets.MaxInclusive, 10), (Vocabulary.XsdFacets.MinInclusive, 1)));

        Assert.IsTrue(DataRangeEquality.StructuralEquals(forward, reversed), "A facet reordering canonicalizes to one structural form.");
        Assert.AreEqual(DataRangeEquality.StructuralHash(forward), DataRangeEquality.StructuralHash(reversed), "The structural hash agrees with the structural equality.");
    }

    /// <summary>CAN-09: facet order does not change the denoted value space — the checker decides both orderings identically.</summary>
    [TestMethod]
    public void CAN09FacetOrderDoesNotChangeDenotedSpace()
    {
        OwlDataRange forward = IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10));
        OwlDataRange reversed = IntegerRestriction((Vocabulary.XsdFacets.MaxInclusive, 10), (Vocabulary.XsdFacets.MinInclusive, 1));

        Assert.AreEqual(DatatypeSatisfiabilityChecker.DecideRange(forward, DatatypeRegistry.Empty), DatatypeSatisfiabilityChecker.DecideRange(reversed, DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeSatisfiabilityChecker.DecideMinCardinality([forward], 5, DatatypeRegistry.Empty), DatatypeSatisfiabilityChecker.DecideMinCardinality([reversed], 5, DatatypeRegistry.Empty));
    }

    /// <summary>CAN-10: a pattern rider blocks the degenerate rewrite — the restriction is facet-sorted but not collapsed to a point.</summary>
    [TestMethod]
    public void CAN10PatternRiderBlocksDegenerateRewrite()
    {
        OwlDataRange range = new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer),
        [
            Facet(Vocabulary.XsdFacets.MinInclusive, Lit("5", Vocabulary.Xsd.Integer)),
            Facet(Vocabulary.XsdFacets.MaxInclusive, Lit("5", Vocabulary.Xsd.Integer)),
            Facet(Vocabulary.XsdFacets.Pattern, Lit("5", Vocabulary.Xsd.String)),
        ]);

        OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(range);
        Assert.IsInstanceOfType<OwlDatatypeRestriction>(canonical, "A pattern rider keeps the restriction a restriction.");
        Assert.IsNotInstanceOfType<OwlDataOneOf>(canonical, "The pattern rider blocks the degenerate-to-point rewrite.");
    }

    /// <summary>CAN-11: a degenerate xsd:float[5,5] is never rewritten — the gate is the exact-real numeric SPACE, and float is a disjoint space.</summary>
    [TestMethod]
    public void CAN11FloatDegenerateNotRewritten()
    {
        OwlDataRange range = new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Float), [Facet(Vocabulary.XsdFacets.MinInclusive, Lit("5", Vocabulary.Xsd.Float)), Facet(Vocabulary.XsdFacets.MaxInclusive, Lit("5", Vocabulary.Xsd.Float))]);

        Assert.IsInstanceOfType<OwlDatatypeRestriction>(DataRangeCanonicalizer.Canonicalize(range), "A float restriction is not on the exact-real line and is never rewritten to a point.");
    }

    /// <summary>CAN-12: structural equality is order-sensitive over operand lists, and the structural hash agrees with equal trees.</summary>
    [TestMethod]
    public void CAN12StructuralEqualityOrderSensitive()
    {
        OwlDataRange forward = new OwlDataOneOf([Lit("1", Vocabulary.Xsd.Integer), Lit("2", Vocabulary.Xsd.Integer)]);
        OwlDataRange forwardCopy = new OwlDataOneOf([Lit("1", Vocabulary.Xsd.Integer), Lit("2", Vocabulary.Xsd.Integer)]);
        OwlDataRange reversed = new OwlDataOneOf([Lit("2", Vocabulary.Xsd.Integer), Lit("1", Vocabulary.Xsd.Integer)]);

        Assert.IsTrue(DataRangeEquality.StructuralEquals(forward, forwardCopy), "Identical trees are structurally equal.");
        Assert.AreEqual(DataRangeEquality.StructuralHash(forward), DataRangeEquality.StructuralHash(forwardCopy), "The hash agrees for equal trees.");
        Assert.IsFalse(DataRangeEquality.StructuralEquals(forward, reversed), "A reordered enumeration is not structurally equal.");

        Assert.IsTrue(DataRangeStructuralComparer.Instance.Equals(forward, forwardCopy));
        Assert.AreEqual(DataRangeStructuralComparer.Instance.GetHashCode(forward), DataRangeStructuralComparer.Instance.GetHashCode(forwardCopy));
        Assert.IsFalse(DataRangeStructuralComparer.Instance.Equals(forward, reversed));
    }

    /// <summary>TryEnumerate materialises a bounded-integer footprint within the budget, and declines an over-budget or non-bounded-integer range.</summary>
    [TestMethod]
    public void TryEnumerateBoundedIntegerFootprintWithinBudget()
    {
        List<Literal> values = [];
        Assert.IsTrue(DataRangeCanonicalizer.TryEnumerate(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 10), (Vocabulary.XsdFacets.MaxInclusive, 12)), DataRangeCanonicalizer.MaxEnumerationCandidates, values));
        Assert.HasCount(3, values);
        Assert.AreEqual("10", values[0].Value.ToString());
        Assert.AreEqual("12", values[2].Value.ToString());

        List<Literal> overBudget = [];
        Assert.IsFalse(DataRangeCanonicalizer.TryEnumerate(IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 100)), DataRangeCanonicalizer.MaxEnumerationCandidates, overBudget), "A footprint above the budget is declined.");
        Assert.IsEmpty(overBudget);

        Assert.IsFalse(DataRangeCanonicalizer.TryEnumerate(DatatypeRange(Vocabulary.Xsd.Integer), DataRangeCanonicalizer.MaxEnumerationCandidates, []), "An unbounded range is declined.");
    }

    /// <summary>Asserts a range canonicalizes to a single-literal enumeration with the given lexical form and datatype IRI, returning the enumeration.</summary>
    /// <param name="range">The range to canonicalize.</param>
    /// <param name="lexical">The expected point lexical form.</param>
    /// <param name="datatypeIri">The expected point datatype IRI.</param>
    /// <returns>The point enumeration.</returns>
    private static OwlDataOneOf AssertPoint(OwlDataRange range, string lexical, Utf8String datatypeIri)
    {
        OwlDataRange canonical = DataRangeCanonicalizer.Canonicalize(range);
        Assert.IsInstanceOfType<OwlDataOneOf>(canonical, "A degenerate exact-real interval canonicalizes to a point enumeration.");
        OwlDataOneOf point = (OwlDataOneOf)canonical;
        Assert.HasCount(1, point.Literals, "A degenerate interval canonicalizes to exactly one point.");
        Assert.AreEqual(lexical, point.Literals[0].Value.ToString(), "The point carries the canonical lexical form.");
        Assert.IsTrue(point.Literals[0].Datatype.Iri.Equals(datatypeIri), "The point carries the representable datatype IRI.");

        return point;
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

    /// <summary>A named-datatype data range.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeReference DatatypeRange(Utf8String datatypeIri)
    {
        return new OwlDatatypeReference(new NamedNode(datatypeIri));
    }

    /// <summary>An integer datatype restriction over the given integer facet bounds.</summary>
    /// <param name="bounds">The facet–bound pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerRestriction(params (Utf8String Facet, int Bound)[] bounds)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((Utf8String facet, int bound) in bounds)
        {
            facets.Add(new OwlFacetRestriction(new NamedNode(facet), Lit(bound.ToString(System.Globalization.CultureInfo.InvariantCulture), Vocabulary.Xsd.Integer)));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }

    /// <summary>A structural document over the given axioms.</summary>
    /// <param name="axioms">The axioms.</param>
    /// <returns>The document.</returns>
    private static OwlOntologyDocument Document(params OwlAxiom[] axioms)
    {
        return new OwlOntologyDocument([.. axioms], ontologyIri: null, new DiagnosticBag(), new HashSet<Utf8String>(), new HashSet<Utf8String>(), new HashSet<Utf8String>(), new HashSet<Utf8String>(), new HashSet<Utf8String>());
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
