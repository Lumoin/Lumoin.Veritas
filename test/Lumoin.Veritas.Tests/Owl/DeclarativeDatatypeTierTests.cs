using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The datatype-registry arc stage-C declarative tier: the pattern, bounded, enumerated, derived,
/// intersection, and complement datatypes answering the four registered-datatype operations. Each row
/// carries its certified battery id.
/// </summary>
[TestClass]
internal sealed class DeclarativeDatatypeTierTests
{
    /// <summary>NFA-PATDT-MINUS: the pattern a[bc] minus the enumerations "ab" and "ac" is unsatisfiable.</summary>
    [TestMethod]
    public void NFAPATDTMINUSPatternMinusEnumerationsIsUnsatisfiable()
    {
        PatternDatatype pattern = new(Iri("Abc"), Utf8Strings.From("a[bc]"));
        DatatypeConjunction conjunction = new([], [OneOf(StrLit("ab")), OneOf(StrLit("ac"))], 0);

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, pattern.DecideConjunction(in conjunction));
    }

    /// <summary>DEC-BND-MEM: a bounded integer datatype [0,100] contains 50 and excludes 150.</summary>
    [TestMethod]
    public void DECBNDMEMBoundedIntegerMembership()
    {
        BoundedDatatype bounded = new(Iri("Percent"), Vocabulary.Xsd.Integer, [Facet(Vocabulary.XsdFacets.MinInclusive, IntLit(0)), Facet(Vocabulary.XsdFacets.MaxInclusive, IntLit(100))]);
        Assert.AreEqual(DatatypeMembership.In, bounded.Contains(IntLit(50)));
        Assert.AreEqual(DatatypeMembership.Out, bounded.Contains(IntLit(150)));
    }

    /// <summary>DEC-BND-EMPTY: a bounded integer datatype [0,100] conjoined with minInclusive 200 is empty.</summary>
    [TestMethod]
    public void DECBNDEMPTYBoundedIntegerConjunctionEmpty()
    {
        BoundedDatatype bounded = new(Iri("Percent"), Vocabulary.Xsd.Integer, [Facet(Vocabulary.XsdFacets.MinInclusive, IntLit(0)), Facet(Vocabulary.XsdFacets.MaxInclusive, IntLit(100))]);
        DatatypeConjunction conjunction = DatatypeConjunction.OfFacets([Facet(Vocabulary.XsdFacets.MinInclusive, IntLit(200))]);

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, bounded.DecideConjunction(in conjunction));
    }

    /// <summary>DEC-ENUM-MEM: an enumeration {"red","green","blue"} contains "red" and excludes "yellow".</summary>
    [TestMethod]
    public void DECENUMMEMEnumerationMembership()
    {
        EnumeratedDatatype colours = new(Iri("Colour"), [StrLit("red"), StrLit("green"), StrLit("blue")]);
        Assert.AreEqual(DatatypeMembership.In, colours.Contains(StrLit("red")));
        Assert.AreEqual(DatatypeMembership.Out, colours.Contains(StrLit("yellow")));
    }

    /// <summary>DEC-ENUM-SAME: preserve-space string identity is Same on equal lexical forms and Distinct otherwise.</summary>
    [TestMethod]
    public void DECENUMSAMEEnumerationValueIdentity()
    {
        EnumeratedDatatype colours = new(Iri("Colour"), [StrLit("red"), StrLit("green")]);
        Assert.AreEqual(DatatypeValueIdentity.Same, colours.SameValue(StrLit("red"), StrLit("red")));
        Assert.AreEqual(DatatypeValueIdentity.Distinct, colours.SameValue(StrLit("red"), StrLit("green")));
    }

    /// <summary>DEC-DERIVED: a string derived by maxLength 3 contains "ab" and excludes "abcd".</summary>
    [TestMethod]
    public void DECDERIVEDStringMaxLengthMembership()
    {
        PatternDatatype anyString = new(Iri("AnyString"), Utf8Strings.From(".*"));
        DerivedDatatype shortString = new(Iri("Short"), anyString, [Facet(Vocabulary.XsdFacets.MaxLength, IntLit(3))]);
        Assert.AreEqual(DatatypeMembership.In, shortString.Contains(StrLit("ab")));
        Assert.AreEqual(DatatypeMembership.Out, shortString.Contains(StrLit("abcd")));
    }

    /// <summary>DEC-INTERSECT: the intersection of {"a","b"} and {"c"} is empty.</summary>
    [TestMethod]
    public void DECINTERSECTDisjointEnumerationsAreEmpty()
    {
        IntersectionDatatype intersection = new(Iri("Both"), [new EnumeratedDatatype(Iri("Ab"), [StrLit("a"), StrLit("b")]), new EnumeratedDatatype(Iri("C"), [StrLit("c")])]);

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, intersection.DecideConjunction(DatatypeConjunction.Empty));
    }

    /// <summary>DEC-COMPLEMENT: the complement of {"red"} in the string space excludes "red" and contains "green".</summary>
    [TestMethod]
    public void DECCOMPLEMENTComplementOfEnumeration()
    {
        ComplementDatatype notRed = new(Iri("NotRed"), new EnumeratedDatatype(Iri("Red"), [StrLit("red")]));
        Assert.AreEqual(DatatypeMembership.Out, notRed.Contains(StrLit("red")));
        Assert.AreEqual(DatatypeMembership.In, notRed.Contains(StrLit("green")));
    }

    /// <summary>CNT-AA: the language of (a|a) has exactly one distinct string (the determinize-first discipline).</summary>
    [TestMethod]
    public void CNTAAAmbiguousUnionCountsOnce()
    {
        PatternDatatype pattern = new(Iri("Aa"), Utf8Strings.From("(a|a)"));
        DatatypeCountBound count = pattern.DistinctValues(DatatypeConjunction.Empty);
        Assert.AreEqual(DatatypeCountKind.Finite, count.Kind);
        Assert.AreEqual(1, count.Value);
    }

    /// <summary>CNT-FINITE: the language of (a|b)(c|d) has exactly four distinct strings.</summary>
    [TestMethod]
    public void CNTFINITEFourDistinctStrings()
    {
        PatternDatatype pattern = new(Iri("Grid"), Utf8Strings.From("(a|b)(c|d)"));
        DatatypeCountBound count = pattern.DistinctValues(DatatypeConjunction.Empty);
        Assert.AreEqual(DatatypeCountKind.Finite, count.Kind);
        Assert.AreEqual(4, count.Value);
    }

    /// <summary>CNT-INF: the language of a+ is infinite.</summary>
    [TestMethod]
    public void CNTINFUnboundedRepetitionIsInfinite()
    {
        PatternDatatype pattern = new(Iri("Aplus"), Utf8Strings.From("a+"));

        Assert.AreEqual(DatatypeCountKind.Infinite, pattern.DistinctValues(DatatypeConjunction.Empty).Kind);
    }

    /// <summary>An enumeration of one literal.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns>The enumeration.</returns>
    private static OwlDataOneOf OneOf(Literal literal)
    {
        return new OwlDataOneOf([literal]);
    }

    /// <summary>A string literal.</summary>
    /// <param name="value">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StrLit(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>An integer literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntLit(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>A facet–value pair.</summary>
    /// <param name="facetIri">The facet IRI.</param>
    /// <param name="value">The facet value.</param>
    /// <returns>The facet restriction.</returns>
    private static OwlFacetRestriction Facet(Utf8String facetIri, Literal value)
    {
        return new OwlFacetRestriction(new NamedNode(facetIri), value);
    }

    /// <summary>An example-namespace datatype IRI.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string localName)
    {
        return Utf8Strings.From("http://example.org/" + localName);
    }
}
