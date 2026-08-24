using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The reader acceptance, rejection, and normalization matrices of the geometry
/// substrate: <see cref="WktLexical"/>'s well-formed rows parse, everything the
/// datatype layer rejects rejects here too, the surface tags normalize, and the
/// structural layer is deliberately stricter than the lexical one.
/// </summary>
[TestClass]
internal sealed class WktGeometryReaderTests
{
    /// <summary>A well-formed text parses and carries the expected root kind.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The expected root kind.</param>
    [TestMethod]
    [DataRow("POINT(1 2)", GeometryKind.Point)]
    [DataRow("Point(-83.4 34.4)", GeometryKind.Point)]
    [DataRow("POINT ZM (1 2 3 4)", GeometryKind.Point)]
    [DataRow("POINT EMPTY", GeometryKind.Point)]
    [DataRow("POINT Z EMPTY", GeometryKind.Point)]
    [DataRow("LINESTRING(0 0, 1 1, 2 2)", GeometryKind.LineString)]
    [DataRow("POLYGON((0 0, 0 10, 10 10, 0 0), (1 1, 2 2, 1 2, 1 1))", GeometryKind.Polygon)]
    [DataRow("MULTIPOINT(1 2, 3 4)", GeometryKind.MultiPoint)]
    [DataRow("MULTIPOINT((1 2), (3 4))", GeometryKind.MultiPoint)]
    [DataRow("MULTILINESTRING((0 0, 1 1), (2 2, 3 3))", GeometryKind.MultiLineString)]
    [DataRow("MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", GeometryKind.MultiPolygon)]
    [DataRow("GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(0 0, 1 1))", GeometryKind.GeometryCollection)]
    [DataRow("GEOMETRYCOLLECTION(POINT EMPTY)", GeometryKind.GeometryCollection)]
    [DataRow("GEOMETRYCOLLECTION EMPTY", GeometryKind.GeometryCollection)]
    [DataRow("POINT(1.5e10 -2.5E-3)", GeometryKind.Point)]
    [DataRow("POINT(.5 +5)", GeometryKind.Point)]
    [DataRow("POINT\t(1\n2)", GeometryKind.Point)]
    [DataRow("POINT(1 2 3)", GeometryKind.Point)]
    [DataRow("POINT(1 2 3 4)", GeometryKind.Point)]
    [DataRow("POLYGON((0 0, 5 0, 10 0, 0 0))", GeometryKind.Polygon)]
    public void WellFormedTextParsesToTheExpectedKind(string text, GeometryKind expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.AreEqual(expected, geometry.Kind, $"'{text}' must carry the expected root kind.");
    }

    /// <summary>A lexically or structurally malformed text rejects.</summary>
    /// <param name="text">The WKT text under test.</param>
    [TestMethod]
    [DataRow("POINTZM(1 2 3 4)")]
    [DataRow("POINTZ(1 2 3)")]
    [DataRow("POINT Z (1 2)")]
    [DataRow("POINT Z (1 2 3 4)")]
    [DataRow("LINESTRING(0 0, 1 1 1, 2 2)")]
    [DataRow("POINT(NaN 1)")]
    [DataRow("POINT(Infinity 2)")]
    [DataRow("POINT(-Inf 2)")]
    [DataRow("LINESTRING(0 0)")]
    [DataRow("POLYGON((0 0, 1 0, 1 1, 0 1))")]
    [DataRow("POLYGON((0 0, 1 0, 0 0))")]
    [DataRow("MULTIPOINT((1 2), EMPTY)")]
    [DataRow("MULTIPOINT(EMPTY)")]
    [DataRow("CIRCULARSTRING(0 0, 1 1, 2 0)")]
    [DataRow("MULTISURFACE EMPTY")]
    [DataRow("SRID=4326;POINT(1 2)")]
    [DataRow("<http://example.org/crs> POINT(1 2)")]
    [DataRow("POINT(1 2) #comment")]
    [DataRow("POINT(1 2) extra")]
    [DataRow("POINT((1 2)")]
    [DataRow("POINT(1)")]
    [DataRow("POINT()")]
    [DataRow("POINT")]
    [DataRow("")]
    [DataRow("   ")]
    public void MalformedTextRejects(string text)
    {
        Assert.IsFalse(WktGeometryReader.TryRead(text, out _, out _), $"'{text}' must reject.");
    }

    /// <summary>Nesting at exactly depth 32 parses.</summary>
    [TestMethod]
    public void NestingAtDepthThirtyTwoParses()
    {
        string text = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", 31)) + "POINT(1 2)"
            + string.Concat(Enumerable.Repeat(")", 31));

        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _),
            "Depth 32 is inside WktLexical's certification bound and must parse.");
        Assert.HasCount(32, geometry.Nodes.ToArray(), "One node per nesting level plus the point.");
    }

    /// <summary>Nesting at depth 33 rejects.</summary>
    [TestMethod]
    public void NestingBeyondDepthThirtyTwoRejects()
    {
        string text = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", 32)) + "POINT(1 2)"
            + string.Concat(Enumerable.Repeat(")", 32));

        Assert.IsFalse(WktGeometryReader.TryRead(text, out _, out _),
            "Depth 33 exceeds WktLexical's bound and must reject.");
    }

    /// <summary>A non-surface tag normalizes to its polygon-shaped kind on read.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The normalized kind.</param>
    [TestMethod]
    [DataRow("TRIANGLE((0 0, 1 0, 0 1, 0 0))", GeometryKind.Polygon)]
    [DataRow("TIN(((0 0, 1 0, 0 1, 0 0)))", GeometryKind.MultiPolygon)]
    [DataRow("POLYHEDRALSURFACE(((0 0, 1 0, 0 1, 0 0)))", GeometryKind.MultiPolygon)]
    public void SurfaceTagsNormalizeOnRead(string text, GeometryKind expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.AreEqual(expected, geometry.Kind, "The surface tag must normalize to its polygon-shaped kind.");
    }

    /// <summary>Both multipoint member spellings parse to one structural value.</summary>
    [TestMethod]
    public void BothMultiPointSpellingsParseToTheSameStructure()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT(1 2, 3 4)", out FlatGeometry bare, out _));
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT((1 2), (3 4))", out FlatGeometry parenthesized, out _));

        Assert.AreEqual(bare, parenthesized, "The two member spellings are one value.");
        Assert.HasCount(2, bare.Parts.ToArray(), "One point part per member.");
    }

    /// <summary>A closed, count-valid ring parses regardless of self-touching or zero area.</summary>
    [TestMethod]
    public void ZeroAreaSelfTouchingRingParses()
    {
        //The ring gate checks closure and counts only; validity beyond that is
        //deliberately not the parser's business.
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON((0 0, 5 0, 10 0, 0 0))", out FlatGeometry geometry, out _),
            "A closed, count-valid, zero-area ring must parse.");
        Assert.AreEqual(0.0, GeometryMeasures.Area(in geometry), "The degenerate ring has a defined zero area.");
    }

    /// <summary>A bare third ordinate infers Z, never M.</summary>
    [TestMethod]
    public void MarkerlessThirdOrdinateReadsAsZ()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT(1 2 3)", out FlatGeometry geometry, out _));

        Assert.IsTrue(geometry.Is3D, "A bare third ordinate infers Z.");
        Assert.IsFalse(geometry.IsMeasured, "No measure without a marker or fourth ordinate.");
        Assert.AreEqual(3.0, geometry.ZOrdinates[0], "The third number lands in the Z column.");
    }

    /// <summary>An M marker routes the third ordinate to the measure column.</summary>
    [TestMethod]
    public void MeasureMarkerRoutesTheThirdOrdinateToM()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT M (1 2 5)", out FlatGeometry geometry, out _));

        Assert.IsFalse(geometry.Is3D, "An M marker carries no Z.");
        Assert.IsTrue(geometry.IsMeasured, "The M marker marks the measure.");
        Assert.AreEqual(5.0, geometry.MOrdinates[0], "The third number lands in the M column, not Z.");
    }
}
