using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The value-layer definitions of <c>geo:gmlLiteral</c>, <c>geo:geoJSONLiteral</c>,
/// <c>geo:kmlLiteral</c>, <c>geo:dggsLiteral</c>, and the house <c>a5Literal</c> subclass: each markup
/// definition carries its vocabulary IRI, declares the lexical-validity facet only, maps its
/// recognizer's verdict to valid, invalid or the abstention, answers the empty lexical form valid as
/// the empty geometry, and abstains on value identity. The DGGS definition's whitespace-only form is
/// invalid rather than empty — its grammar requires the angle-bracket IRI prefix on every non-empty
/// form — and it certifies house-flavour bodies while abstaining on foreign grids; the
/// <c>a5Literal</c> subclass certifies its whole grammar and decides value identity by canonical
/// cell-set equality.
/// </summary>
[TestClass]
internal sealed class GeoSerializationDatatypeTests
{
    /// <summary>A house-flavour cell-set literal over one cell, shared by the flavour rows.</summary>
    private const string HouseCells = "<https://lumoin.com/veritas/dggs/a5> CELLS (4f05dccc726e0000)";
    /// <summary>The GML definition carries the vocabulary IRI and declares the lexical-validity facet only.</summary>
    [TestMethod]
    public void GmlDeclaresIriAndLexicalValidityFacet()
    {
        GmlLiteralValueDatatype definition = GmlLiteralValueDatatype.Instance;

        Assert.IsTrue(definition.DatatypeIri.Span.SequenceEqual(GeoVocabulary.Geo.GmlLiteral.Span));
        Assert.AreEqual(ValueDatatypeFacets.LexicalValidity, definition.Facets);
    }

    /// <summary>A GML fragment of the certified profile is a valid lexical form.</summary>
    [TestMethod]
    public void GmlWellFormedFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, GmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<gml:Point srsName=\"http://www.opengis.net/def/crs/EPSG/0/4326\" xmlns:gml=\"http://www.opengis.net/gml/3.2\"><gml:pos>-83.4 34.4</gml:pos></gml:Point>")));
    }

    /// <summary>A root outside the GML namespace is provably not an element of the GML schema, so the form is invalid.</summary>
    [TestMethod]
    public void GmlMalformedFormInvalid()
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, GmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<Point xmlns=\"http://example.org/geometry\"><pos>1 2</pos></Point>")));
    }

    /// <summary>A document type declaration leaves the fragment uncertified, so validity abstains.</summary>
    [TestMethod]
    public void GmlAbstainingFormIndeterminate()
    {
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, GmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<!DOCTYPE Point><gml:Point xmlns:gml=\"http://www.opengis.net/gml/3.2\"><gml:pos>1 2</gml:pos></gml:Point>")));
    }

    /// <summary>The empty lexical form is the empty geometry and is valid, a verdict the recognizer answers itself.</summary>
    [TestMethod]
    public void GmlEmptyFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, GmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("")));
    }

    /// <summary>Value identity abstains for identical and differing GML forms alike — geometric identity needs parsed geometry.</summary>
    [TestMethod]
    public void GmlSameValueAbstains()
    {
        GmlLiteralValueDatatype definition = GmlLiteralValueDatatype.Instance;
        Utf8String first = Utf8Strings.From("<gml:Point xmlns:gml=\"http://www.opengis.net/gml/3.2\"><gml:pos>1 2</gml:pos></gml:Point>");
        Utf8String second = Utf8Strings.From("<gml:Point xmlns:gml=\"http://www.opengis.net/gml/3.2\"><gml:pos>1.0 2.0</gml:pos></gml:Point>");

        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, first));
        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, second));
    }

    /// <summary>The GeoJSON definition carries the vocabulary IRI and declares the lexical-validity facet only.</summary>
    [TestMethod]
    public void GeoJsonDeclaresIriAndLexicalValidityFacet()
    {
        GeoJsonLiteralValueDatatype definition = GeoJsonLiteralValueDatatype.Instance;

        Assert.IsTrue(definition.DatatypeIri.Span.SequenceEqual(GeoVocabulary.Geo.GeoJsonLiteral.Span));
        Assert.AreEqual(ValueDatatypeFacets.LexicalValidity, definition.Facets);
    }

    /// <summary>A GeoJSON geometry object is a valid lexical form.</summary>
    [TestMethod]
    public void GeoJsonWellFormedFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, GeoJsonLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("{\"type\": \"Point\", \"coordinates\": [-83.38,33.95]}")));
    }

    /// <summary>A feature type is provably not a geometry object, so the form is invalid.</summary>
    [TestMethod]
    public void GeoJsonMalformedFormInvalid()
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, GeoJsonLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("{\"type\": \"Feature\", \"geometry\": null}")));
    }

    /// <summary>A type value written with a backslash escape leaves the object uncertified, so validity abstains.</summary>
    [TestMethod]
    public void GeoJsonAbstainingFormIndeterminate()
    {
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, GeoJsonLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("{\"type\":\"Poi\\u006et\",\"coordinates\":[1,2]}")));
    }

    /// <summary>The empty lexical form is the empty geometry and is valid, a verdict the recognizer answers itself.</summary>
    [TestMethod]
    public void GeoJsonEmptyFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, GeoJsonLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("")));
    }

    /// <summary>Value identity abstains for identical and differing GeoJSON forms alike — geometric identity needs parsed geometry.</summary>
    [TestMethod]
    public void GeoJsonSameValueAbstains()
    {
        GeoJsonLiteralValueDatatype definition = GeoJsonLiteralValueDatatype.Instance;
        Utf8String first = Utf8Strings.From("{\"type\":\"Point\",\"coordinates\":[1,2]}");
        Utf8String second = Utf8Strings.From("{\"type\":\"Point\",\"coordinates\":[1.0,2.0]}");

        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, first));
        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, second));
    }

    /// <summary>The KML definition carries the vocabulary IRI and declares the lexical-validity facet only.</summary>
    [TestMethod]
    public void KmlDeclaresIriAndLexicalValidityFacet()
    {
        KmlLiteralValueDatatype definition = KmlLiteralValueDatatype.Instance;

        Assert.IsTrue(definition.DatatypeIri.Span.SequenceEqual(GeoVocabulary.Geo.KmlLiteral.Span));
        Assert.AreEqual(ValueDatatypeFacets.LexicalValidity, definition.Facets);
    }

    /// <summary>A KML geometry element of the certified roster is a valid lexical form.</summary>
    [TestMethod]
    public void KmlWellFormedFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, KmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<Point xmlns=\"http://www.opengis.net/kml/2.2\"><coordinates>-83.38,33.95</coordinates></Point>")));
    }

    /// <summary>A root bound outside the KML family is provably not an element of the KML schema, so the form is invalid.</summary>
    [TestMethod]
    public void KmlMalformedFormInvalid()
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, KmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<Point xmlns=\"http://example.org/geometry\"><coordinates>1,2</coordinates></Point>")));
    }

    /// <summary>A document type declaration leaves the fragment uncertified, so validity abstains.</summary>
    [TestMethod]
    public void KmlAbstainingFormIndeterminate()
    {
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, KmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<!DOCTYPE Point><Point xmlns=\"http://www.opengis.net/kml/2.2\"><coordinates>1,2</coordinates></Point>")));
    }

    /// <summary>The empty lexical form is the empty geometry and is valid, a verdict the recognizer answers itself.</summary>
    [TestMethod]
    public void KmlEmptyFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, KmlLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("")));
    }

    /// <summary>Value identity abstains for identical and differing KML forms alike — geometric identity needs parsed geometry.</summary>
    [TestMethod]
    public void KmlSameValueAbstains()
    {
        KmlLiteralValueDatatype definition = KmlLiteralValueDatatype.Instance;
        Utf8String first = Utf8Strings.From("<Point xmlns=\"http://www.opengis.net/kml/2.2\"><coordinates>1,2</coordinates></Point>");
        Utf8String second = Utf8Strings.From("<Point xmlns=\"http://www.opengis.net/kml/2.2\"><coordinates>1.0,2.0</coordinates></Point>");

        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, first));
        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, second));
    }

    /// <summary>The DGGS definition carries the vocabulary IRI and declares the lexical-validity facet only.</summary>
    [TestMethod]
    public void DggsDeclaresIriAndLexicalValidityFacet()
    {
        DggsLiteralValueDatatype definition = DggsLiteralValueDatatype.Instance;

        Assert.IsTrue(definition.DatatypeIri.Span.SequenceEqual(GeoVocabulary.Geo.DggsLiteral.Span));
        Assert.AreEqual(ValueDatatypeFacets.LexicalValidity, definition.Facets);
    }

    /// <summary>The empty lexical form is the empty geometry and is valid, a verdict the recognizer answers itself.</summary>
    [TestMethod]
    public void DggsEmptyFormValid()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("")));
    }

    /// <summary>A whitespace-only form is not the empty form and carries no angle-bracket prefix, so it is invalid.</summary>
    [TestMethod]
    public void DggsWhitespaceOnlyFormInvalid()
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("   ")));
    }

    /// <summary>A non-empty form without the angle-bracket IRI prefix is provably outside the literal grammar, so it is invalid.</summary>
    [TestMethod]
    public void DggsMissingPrefixFormInvalid()
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("CELL (R3234)")));
    }

    /// <summary>A valid prefix followed by geometry data leaves the data uncertified — its formulation belongs to the identified DGGS — so validity abstains.</summary>
    [TestMethod]
    public void DggsAbstainingFormIndeterminate()
    {
        Assert.AreEqual(ValueLexicalValidity.Indeterminate, DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3234)")));
    }

    /// <summary>Value identity abstains for identical and differing DGGS forms alike — geometric identity needs decoded cells.</summary>
    [TestMethod]
    public void DggsSameValueAbstains()
    {
        DggsLiteralValueDatatype definition = DggsLiteralValueDatatype.Instance;
        Utf8String first = Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3234)");
        Utf8String second = Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3235)");

        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, first));
        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(first, second));
    }

    /// <summary>The generic DGGS definition certifies house-flavour bodies through the shared recognizer: a conformant cell set is valid and a violating one is invalid, not indeterminate.</summary>
    [TestMethod]
    public void DggsCertifiesTheHouseFlavourBody()
    {
        Assert.AreEqual(ValueLexicalValidity.Valid, DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(HouseCells)));
        Assert.AreEqual(ValueLexicalValidity.Invalid, DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS ()")));
    }

    /// <summary>The house definition carries the house datatype IRI and declares the lexical-validity and value-equality facets.</summary>
    [TestMethod]
    public void A5DeclaresIriAndBothFacets()
    {
        A5DggsLiteralValueDatatype definition = A5DggsLiteralValueDatatype.Instance;

        Assert.IsTrue(definition.DatatypeIri.Span.SequenceEqual(A5DggsVocabulary.DatatypeIri.Span));
        Assert.AreEqual(ValueDatatypeFacets.LexicalValidity | ValueDatatypeFacets.ValueEquality, definition.Facets);
    }

    /// <summary>The house definition certifies its whole grammar: a conformant form is valid, the empty form is valid, and a whitespace-only form is invalid.</summary>
    [TestMethod]
    public void A5CertifiesItsWholeGrammar()
    {
        A5DggsLiteralValueDatatype definition = A5DggsLiteralValueDatatype.Instance;

        Assert.AreEqual(ValueLexicalValidity.Valid, definition.ValidateLexicalForm(Utf8Strings.From(HouseCells)));
        Assert.AreEqual(ValueLexicalValidity.Valid, definition.ValidateLexicalForm(Utf8Strings.From("")));
        Assert.AreEqual(ValueLexicalValidity.Invalid, definition.ValidateLexicalForm(Utf8Strings.From("   ")));
    }

    /// <summary>A foreign grid IRI under the implementation-naming subclass is itself the violation, so the form is invalid rather than an abstention.</summary>
    [TestMethod]
    public void A5ForeignGridInvalid()
    {
        Assert.AreEqual(ValueLexicalValidity.Invalid, A5DggsLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3234)")));
    }

    /// <summary>Value identity decides by canonical cell-set equality: token order, duplication, case, and leading zeros carry no meaning, and the empty forms are the same value.</summary>
    [TestMethod]
    public void A5SameValueDecidesSetEquality()
    {
        A5DggsLiteralValueDatatype definition = A5DggsLiteralValueDatatype.Instance;
        Utf8String pair = Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000 a00000000000000)");
        Utf8String reversed = Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS (a00000000000000 600000000000000)");
        Utf8String duplicated = Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000 600000000000000 a00000000000000)");
        Utf8String respelled = Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> cells (0600000000000000 A00000000000000)");
        Utf8String single = Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000)");
        Utf8String empty = Utf8Strings.From("");

        Assert.AreEqual(ValueIdentity.Same, definition.SameValue(pair, reversed));
        Assert.AreEqual(ValueIdentity.Same, definition.SameValue(pair, duplicated));
        Assert.AreEqual(ValueIdentity.Same, definition.SameValue(pair, respelled));
        Assert.AreEqual(ValueIdentity.Distinct, definition.SameValue(pair, single));
        Assert.AreEqual(ValueIdentity.Same, definition.SameValue(empty, empty));
        Assert.AreEqual(ValueIdentity.Distinct, definition.SameValue(empty, single));
        Assert.AreEqual(ValueIdentity.Indeterminate, definition.SameValue(pair, Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3234)")));
    }

    /// <summary>A parent cell and its complete child set are DISTINCT values: child pentagons only approximately tile their parent, so the two denote different regions and no hierarchy collapse is ever applied.</summary>
    [TestMethod]
    public void A5SameValueDistinguishesParentFromItsChildren()
    {
        A5CellId parent = A5CellId.Parse("4f05dccc726e0000");
        A5CellId[] children = A5.CellToChildren(parent);
        string childTokens = string.Join(' ', Array.ConvertAll(children, static child => child.Value.ToString("x", System.Globalization.CultureInfo.InvariantCulture)));
        Utf8String parentLiteral = Utf8Strings.From(HouseCells);
        Utf8String childrenLiteral = Utf8Strings.From($"<https://lumoin.com/veritas/dggs/a5> CELLS ({childTokens})");

        Assert.AreEqual(ValueIdentity.Distinct, A5DggsLiteralValueDatatype.Instance.SameValue(parentLiteral, childrenLiteral));
    }
}
