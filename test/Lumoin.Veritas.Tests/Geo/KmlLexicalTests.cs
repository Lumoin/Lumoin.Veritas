using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The KML span recognizer's battery: the standard's example literal, well-formed bodies across the
/// certified KML 2.2 roster and its simple content, the empty-geometry forms, the whitespace a coordinate
/// tuple admits around its commas and the separators it does not, provably malformed bodies, the
/// abstentions the content model leaves unmodeled, and element nesting at the geometry bound, at the hard
/// cap and beyond it.
/// </summary>
[TestClass]
internal sealed class KmlLexicalTests
{
    /// <summary>The default namespace declaration a certified fragment carries on its root element.</summary>
    private const string KmlDeclaration = " xmlns=\"http://www.opengis.net/kml/2.2\"";

    /// <summary>The example literal of the standard: a point in the default KML namespace.</summary>
    private const string ExampleLiteral = "<Point" + KmlDeclaration + "><coordinates>-83.38,33.95</coordinates></Point>";

    /// <summary>The root of a nesting chain: the aggregate that carries the namespace declaration.</summary>
    private const string NestingRootOpen = "<MultiGeometry" + KmlDeclaration + ">";

    /// <summary>One nesting level of a chain: an aggregate wrapping certified members, one open element.</summary>
    private const string NestingLevelOpen = "<MultiGeometry>";

    /// <summary>The end tag of one nesting level.</summary>
    private const string NestingLevelClose = "</MultiGeometry>";

    /// <summary>The innermost geometry of a nesting chain, itself two open elements deep.</summary>
    private const string NestingLeaf = "<Point><coordinates>1,2</coordinates></Point>";

    /// <summary>The standard's own example literal is well-formed.</summary>
    [TestMethod]
    public void ExampleLiteralWellFormed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(ExampleLiteral).Span));
    }

    /// <summary>A well-formed body across the certified roster, its boundary wrappers, and the empty tuple list.</summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("<LineString" + KmlDeclaration + "><coordinates>-83.38,33.95,0 -83.37,33.96,10</coordinates></LineString>")]
    [DataRow("<LinearRing" + KmlDeclaration + "><coordinates>0,0 1,0 1,1 0,0</coordinates></LinearRing>")]
    [DataRow("<Polygon" + KmlDeclaration + "><outerBoundaryIs><LinearRing><coordinates>0,0 10,0 10,10 0,0</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>1,1 2,1 2,2 1,1</coordinates></LinearRing></innerBoundaryIs></Polygon>")]
    [DataRow("<MultiGeometry" + KmlDeclaration + "><Point><coordinates>1,2</coordinates></Point><LineString><coordinates>0,0 1,1</coordinates></LineString></MultiGeometry>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates></coordinates></Point>")]
    public void WellFormedBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>An empty or all-whitespace body is well-formed — the empty geometry.</summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void EmptyBodyWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>The boolean flags carry all four values, and a curve admits the tessellation flag.</summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("<Point" + KmlDeclaration + "><extrude>0</extrude><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><extrude>1</extrude><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><extrude>true</extrude><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><extrude>false</extrude><coordinates>1,2</coordinates></Point>")]
    [DataRow("<LineString" + KmlDeclaration + "><tessellate>1</tessellate><coordinates>0,0 1,1</coordinates></LineString>")]
    public void BooleanFlagValuesWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>The altitude interpretation carries each of its three values.</summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("<Point" + KmlDeclaration + "><altitudeMode>clampToGround</altitudeMode><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><altitudeMode>relativeToGround</altitudeMode><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><altitudeMode>absolute</altitudeMode><coordinates>1,2</coordinates></Point>")]
    public void AltitudeModeValuesWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// Whitespace adjacent to a comma inside a tuple binds to that comma and the tuple continues past it,
    /// on either side of the comma and at every component boundary, while whitespace ending at anything
    /// else still separates one tuple from the next.
    /// </summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1 ,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1, 2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1 , 2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1 , 2 , 3</coordinates></Point>")]
    [DataRow("<LineString" + KmlDeclaration + "><coordinates>0, 0 1 ,1</coordinates></LineString>")]
    [DataRow("<LineString" + KmlDeclaration + "><coordinates>\n  0,0\n  1,1\n</coordinates></LineString>")]
    public void TupleCommaWhitespaceWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// A provably malformed body: a coordinate tuple outside the two-or-three-component grammar, a comma
    /// that never gains its component, a simple element carrying a value outside its enumeration, a root
    /// bound to a namespace outside the KML family, and broken XML.
    /// </summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1,2,3,4</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1,2,</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1,2 , </coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1, ,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>,1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><extrude>yes</extrude><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><altitudeMode>underground</altitudeMode><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point xmlns=\"http://example.org/other\"><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1,2</coordinates>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates>1,2</coordinate></Point>")]
    [DataRow("<Point xmlns=http://www.opengis.net/kml/2.2><coordinates>1,2</coordinates></Point>")]
    public void MalformedBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// A body the content model leaves unmodeled abstains: the roster member whose content grammar is not
    /// encoded here, a local name outside the certified roster, a root that lost its default binding, a flag
    /// the parent geometry does not admit, a child declaring a namespace of its own, and a document type
    /// declaration.
    /// </summary>
    /// <param name="body">The KML body under test.</param>
    [TestMethod]
    [DataRow("<Model" + KmlDeclaration + "><altitudeMode>absolute</altitudeMode></Model>")]
    [DataRow("<Placemark" + KmlDeclaration + "><Point><coordinates>1,2</coordinates></Point></Placemark>")]
    [DataRow("<Point><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><tessellate>1</tessellate><coordinates>1,2</coordinates></Point>")]
    [DataRow("<Point" + KmlDeclaration + "><coordinates" + KmlDeclaration + ">1,2</coordinates></Point>")]
    [DataRow("<!DOCTYPE Point>" + ExampleLiteral)]
    public void AbstainingBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// A fragment nested to the geometry bound the readers of this format certify — the wrapping aggregates
    /// that bound allows around one leaf geometry — scans without ever reaching the element cap, so the
    /// lexical layer never answers on depth where the readers accept.
    /// </summary>
    [TestMethod]
    public void GeometryBoundNestingWellFormed()
    {
        int levels = GeometryCodecText.MaximumNestingDepth - 1;
        string body = NestingRootOpen
            + string.Concat(Enumerable.Repeat(NestingLevelOpen, levels - 1))
            + NestingLeaf
            + string.Concat(Enumerable.Repeat(NestingLevelClose, levels));

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Nesting that reaches exactly the cap of open elements is still recognized well-formed.</summary>
    [TestMethod]
    public void NestingAtCapWellFormed()
    {
        int levels = KmlLexical.MaximumNestingDepth - 2;
        string body = NestingRootOpen
            + string.Concat(Enumerable.Repeat(NestingLevelOpen, levels - 1))
            + NestingLeaf
            + string.Concat(Enumerable.Repeat(NestingLevelClose, levels));

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Nesting that needs one open element beyond the cap answers the depth outcome, not a grammar verdict.</summary>
    [TestMethod]
    public void NestingBeyondCapDepthExceeded()
    {
        int levels = KmlLexical.MaximumNestingDepth - 1;
        string body = NestingRootOpen
            + string.Concat(Enumerable.Repeat(NestingLevelOpen, levels - 1))
            + NestingLeaf
            + string.Concat(Enumerable.Repeat(NestingLevelClose, levels));

        Assert.AreEqual(GeometryLexicalRecognition.DepthExceeded, KmlLexical.Recognize(Utf8Strings.From(body).Span));
    }
}
