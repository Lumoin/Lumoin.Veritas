using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The <c>geo:wktLiteral</c> value-layer definition: it declares lexical validity only, composes
/// the CRS-prefix parse with the WKT recognizer,
/// abstains where recognition abstains, abstains unconditionally on value identity, and registers as an
/// unreserved <c>geo:</c> IRI — while nothing registers it by default.
/// </summary>
[TestClass]
internal sealed class WktLiteralValueDatatypeTests
{
    /// <summary>The definition declares the lexical-validity facet only and is recognizer-backed, not self-certified.</summary>
    [TestMethod]
    public void DeclaresLexicalValidityOnly()
    {
        WktLiteralValueDatatype definition = WktLiteralValueDatatype.Instance;

        Assert.AreEqual(ValueDatatypeFacets.LexicalValidity, definition.Facets);
        Assert.IsFalse(definition.SelfCertified);
        Assert.IsEmpty(definition.Probes);
        Assert.IsTrue(definition.DatatypeIri.Span.SequenceEqual("http://www.opengis.net/ont/geosparql#wktLiteral"u8));
    }

    /// <summary>A well-formed lexical form is valid, with and without an explicit CRS prefix, including the empty geometry.</summary>
    /// <param name="form">The lexical form under test.</param>
    [TestMethod]
    [DataRow("POINT(1 2)")]
    [DataRow("<http://www.opengis.net/def/crs/EPSG/0/4326> POINT(1 2)")]
    [DataRow("")]
    [DataRow("POLYGON((0 0, 1 0, 1 1, 0 0))")]
    public void WellFormedFormsValid(string form)
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(form)));
    }

    /// <summary>A provably broken form is invalid, whether the body or the prefix structure is at fault.</summary>
    /// <param name="form">The lexical form under test.</param>
    [TestMethod]
    [DataRow("not a geometry")]
    [DataRow("POINT(1")]
    [DataRow("<> POINT(1 2)")]
    [DataRow("<http://example.org/crs>POINT(1 2)")]
    public void MalformedFormsInvalid(string form)
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(form)));
    }

    /// <summary>An uncertified curve body abstains, leaving the engine's built-in acceptance standing.</summary>
    [TestMethod]
    public void CurveBodyIndeterminate()
    {
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("CIRCULARSTRING(0 0, 1 1, 2 0)")));
    }

    /// <summary>A body beyond the nesting cap abstains — the cap is a resource bound, never an invalidity claim.</summary>
    [TestMethod]
    public void NestingBeyondCapIndeterminate()
    {
        string form = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", WktLexical.MaximumNestingDepth)) + "POINT(1 2)" + new string(')', WktLexical.MaximumNestingDepth);

        Assert.AreEqual(ValueLexicalValidity.Indeterminate, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(form)));
    }

    /// <summary>Value identity abstains for identical and differing forms alike — geometric identity needs parsed geometry.</summary>
    [TestMethod]
    public void SameValueAbstains()
    {
        WktLiteralValueDatatype definition = WktLiteralValueDatatype.Instance;
        Utf8String first = Utf8Strings.From("POINT(1 2)");
        Utf8String second = Utf8Strings.From("POINT(1.0 2.0)");

        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, first));
        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, second));
    }

    /// <summary>The definition registers as an unreserved <c>geo:</c> IRI and resolves back to the shared instance.</summary>
    [TestMethod]
    public void RegistersAsUnreservedGeoIri()
    {
        ValueDatatypeRegistryBuilder builder = new();
        ValueDatatypeRegistration outcome = builder.Add(WktLiteralValueDatatype.Instance);

        Assert.AreEqual(ValueDatatypeRegistrationKind.Accepted, outcome.Kind);
        ValueDatatypeRegistry registry = builder.Build();
        Assert.IsTrue(registry.TryGet(GeoVocabulary.Geo.WktLiteral, out ValueDatatype? found));
        Assert.AreSame(WktLiteralValueDatatype.Instance, found);
    }
}
