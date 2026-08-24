using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The GML span recognizer's battery: the standard's example literal, well-formed bodies across the
/// certified GML 3.2 roster, the empty-geometry forms, the prolog forms, provably malformed bodies, the
/// abstentions the profile leaves unmodeled, and element nesting at the geometry bound, at the hard cap and
/// beyond it.
/// </summary>
[TestClass]
internal sealed class GmlLexicalTests
{
    /// <summary>The code point of the byte-order mark, which UTF-8 encodes ahead of everything else.</summary>
    private const int ByteOrderMarkCodePoint = 0xFEFF;

    /// <summary>The namespace declaration a certified fragment carries on its root element.</summary>
    private const string GmlDeclaration = " xmlns:gml=\"http://www.opengis.net/gml/3.2\"";

    /// <summary>The XML declaration, which may open the fragment.</summary>
    private const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    /// <summary>The example literal of the standard: a point carrying an explicit spatial reference system.</summary>
    private const string ExampleLiteral = "<gml:Point srsName=\"http://www.opengis.net/def/crs/OGC/1.3/CRS84\"" + GmlDeclaration + "><gml:pos>-83.38 33.95</gml:pos></gml:Point>";

    /// <summary>The shortest certified fragment, carried by the rows whose subject is the prolog rather than the body.</summary>
    private const string PointBody = "<gml:Point" + GmlDeclaration + "><gml:pos>1 2</gml:pos></gml:Point>";

    /// <summary>The root of a nesting chain: the aggregate that carries the namespace declaration and its member wrapper.</summary>
    private const string NestingRootOpen = "<gml:MultiGeometry" + GmlDeclaration + "><gml:geometryMembers>";

    /// <summary>One nesting level of a chain: an aggregate and its plural member wrapper, two open elements.</summary>
    private const string NestingLevelOpen = "<gml:MultiGeometry><gml:geometryMembers>";

    /// <summary>The end tags of one nesting level.</summary>
    private const string NestingLevelClose = "</gml:geometryMembers></gml:MultiGeometry>";

    /// <summary>The innermost geometry of a nesting chain, itself two open elements deep.</summary>
    private const string NestingLeaf = "<gml:Point><gml:pos>1 2</gml:pos></gml:Point>";

    /// <summary>The standard's own example literal is well-formed.</summary>
    [TestMethod]
    public void ExampleLiteralWellFormed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(ExampleLiteral).Span));
    }

    /// <summary>A well-formed body across the certified roster, its content models, and the empty-content position form.</summary>
    /// <param name="body">The GML body under test.</param>
    [TestMethod]
    [DataRow("<gml:LineString" + GmlDeclaration + "><gml:posList>0 0 1 1 2 2</gml:posList></gml:LineString>")]
    [DataRow("<gml:Polygon" + GmlDeclaration + "><gml:exterior><gml:LinearRing><gml:posList>0 0 0 10 10 10 0 0</gml:posList></gml:LinearRing></gml:exterior><gml:interior><gml:LinearRing><gml:posList>1 1 2 2 1 2 1 1</gml:posList></gml:LinearRing></gml:interior></gml:Polygon>")]
    [DataRow("<gml:MultiPoint" + GmlDeclaration + "><gml:pointMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:pointMember></gml:MultiPoint>")]
    [DataRow("<gml:MultiPoint" + GmlDeclaration + "><gml:pointMembers><gml:Point><gml:pos>1 2</gml:pos></gml:Point><gml:Point><gml:pos>3 4</gml:pos></gml:Point></gml:pointMembers></gml:MultiPoint>")]
    [DataRow("<gml:MultiCurve" + GmlDeclaration + "><gml:curveMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:curveMember></gml:MultiCurve>")]
    [DataRow("<gml:MultiSurface" + GmlDeclaration + "><gml:surfaceMembers><gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>0 0 1 0 1 1 0 0</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></gml:surfaceMembers></gml:MultiSurface>")]
    [DataRow("<gml:MultiGeometry" + GmlDeclaration + "><gml:geometryMembers><gml:Point><gml:pos>1 2</gml:pos></gml:Point><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:geometryMembers></gml:MultiGeometry>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos/></gml:Point>")]
    public void WellFormedBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>An empty or all-whitespace body is well-formed — the empty geometry.</summary>
    /// <param name="body">The GML body under test.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void EmptyBodyWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Position tokens are double lexical forms, so the three special values stand there, spelled exactly.</summary>
    /// <param name="body">The GML body under test.</param>
    [TestMethod]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>NaN NaN</gml:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>INF INF</gml:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>-INF -INF</gml:pos></gml:Point>")]
    [DataRow("<gml:LineString" + GmlDeclaration + "><gml:posList>NaN INF -INF 1.5</gml:posList></gml:LineString>")]
    public void DoubleSpecialValuesWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>An XML declaration ahead of the root element leaves the body well-formed.</summary>
    [TestMethod]
    public void XmlDeclarationAcceptedWellFormed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(XmlDeclaration + PointBody).Span));
    }

    /// <summary>A byte-order mark ahead of everything else, with or without an XML declaration behind it, is accepted.</summary>
    [TestMethod]
    public void ByteOrderMarkAcceptedWellFormed()
    {
        string mark = char.ConvertFromUtf32(ByteOrderMarkCodePoint);

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(mark + PointBody).Span));
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(mark + XmlDeclaration + PointBody).Span));
    }

    /// <summary>
    /// A provably malformed body: a root outside the GML schema's fixed target namespace, broken XML
    /// structure, a namespace-ill-formed name, a violated wrapper multiplicity, and a position token that is
    /// no double lexical form.
    /// </summary>
    /// <param name="body">The GML body under test.</param>
    [TestMethod]
    [DataRow("<Point><pos>1 2</pos></Point>")]
    [DataRow("<x:Point xmlns:x=\"http://example.org/other\"><x:pos>1 2</x:pos></x:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>1 2</gml:pos>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>1 2</gml:posList></gml:Point>")]
    [DataRow("<gml:Point srsName=\"a\" srsName=\"b\"" + GmlDeclaration + "><gml:pos>1 2</gml:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><foo:pos>1 2</foo:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + ">&nbsp;</gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><?xml version=\"1.0\"?></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + ">]]></gml:Point>")]
    [DataRow("<:Point" + GmlDeclaration + "/>")]
    [DataRow("<gml:Polygon" + GmlDeclaration + "><gml:exterior><gml:LinearRing><gml:posList>0 0 1 0 1 1 0 0</gml:posList></gml:LinearRing><gml:LinearRing><gml:posList>2 2 3 2 3 3 2 2</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>a b</gml:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>nan 1</gml:pos></gml:Point>")]
    public void MalformedBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// A body the profile leaves unmodeled abstains: a roster member whose content grammar is not encoded
    /// here, a ring standing as a root, the sibling profile's namespace, a document type declaration, the
    /// deprecated coordinates element, a child re-declaring the prefix to a foreign URI, markup splitting
    /// token content, and an unmodeled child of a certified element.
    /// </summary>
    /// <param name="body">The GML body under test.</param>
    [TestMethod]
    [DataRow("<gml:Curve" + GmlDeclaration + "><gml:segments/></gml:Curve>")]
    [DataRow("<gml:LinearRing" + GmlDeclaration + "><gml:posList>0 0 1 0 1 1 0 0</gml:posList></gml:LinearRing>")]
    [DataRow("<gml:Point xmlns:gml=\"http://www.opengis.net/gml\"><gml:pos>1 2</gml:pos></gml:Point>")]
    [DataRow("<!DOCTYPE gml:Point>" + PointBody)]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:coordinates>1,2</gml:coordinates></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos xmlns:gml=\"http://example.org/foreign\">1 2</gml:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pos>1 <!-- note --> 2</gml:pos></gml:Point>")]
    [DataRow("<gml:Point" + GmlDeclaration + "><gml:pointProperty/></gml:Point>")]
    public void AbstainingBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// A fragment nested to the geometry bound the readers of this format certify — the wrapping aggregates
    /// that bound allows around one leaf geometry, each costing an aggregate and its member wrapper — scans
    /// without ever reaching the element cap, so the lexical layer never answers on depth where the readers
    /// accept.
    /// </summary>
    [TestMethod]
    public void GeometryBoundNestingWellFormed()
    {
        int levels = GeometryCodecText.MaximumNestingDepth - 1;
        string body = NestingRootOpen
            + string.Concat(Enumerable.Repeat(NestingLevelOpen, levels - 1))
            + NestingLeaf
            + string.Concat(Enumerable.Repeat(NestingLevelClose, levels));

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Nesting that reaches exactly the cap of open elements is still recognized well-formed.</summary>
    [TestMethod]
    public void NestingAtCapWellFormed()
    {
        int levels = (GmlLexical.MaximumNestingDepth - 2) / 2;
        string body = NestingRootOpen
            + string.Concat(Enumerable.Repeat(NestingLevelOpen, levels - 1))
            + NestingLeaf
            + string.Concat(Enumerable.Repeat(NestingLevelClose, levels));

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Nesting that needs one open element beyond the cap answers the depth outcome, not a grammar verdict.</summary>
    [TestMethod]
    public void NestingBeyondCapDepthExceeded()
    {
        int levels = GmlLexical.MaximumNestingDepth / 2;
        string body = NestingRootOpen
            + string.Concat(Enumerable.Repeat(NestingLevelOpen, levels - 1))
            + NestingLeaf
            + string.Concat(Enumerable.Repeat(NestingLevelClose, levels));

        Assert.AreEqual(GeometryLexicalRecognition.DepthExceeded, GmlLexical.Recognize(Utf8Strings.From(body).Span));
    }
}
