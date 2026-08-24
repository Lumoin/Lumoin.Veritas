using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The measures family of the geometry substrate: planar shoelace area with the
/// anchor translation, shell-minus-holes, perimeter including holes, member sums, and
/// the zero families.
/// </summary>
[TestClass]
internal sealed class GeometryMeasuresTests
{
    /// <summary>The comparison tolerance for measure values.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>Area follows shell-minus-holes with member sums and the zero families.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The expected planar area.</param>
    [TestMethod]
    [DataRow("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 100.0)]
    [DataRow("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))", 96.0)]
    [DataRow("MULTIPOLYGON(((0 0, 2 0, 2 2, 0 2, 0 0)), ((10 10, 13 10, 13 13, 10 13, 10 10)))", 13.0)]
    [DataRow("GEOMETRYCOLLECTION(POLYGON((0 0, 2 0, 2 2, 0 2, 0 0)), POINT(1 1))", 4.0)]
    [DataRow("POLYGON((0 0, 5 0, 10 0, 0 0))", 0.0)]
    [DataRow("LINESTRING(0 0, 10 0)", 0.0)]
    [DataRow("POINT(1 2)", 0.0)]
    [DataRow("POLYGON EMPTY", 0.0)]
    [DataRow("GEOMETRYCOLLECTION EMPTY", 0.0)]
    public void AreaFollowsShellMinusHolesWithMemberSums(string text, double expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.AreEqual(expected, GeometryMeasures.Area(in geometry), Tolerance, $"Area of '{text}'.");
    }

    /// <summary>Length sums segments, with polygons contributing their perimeter.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The expected planar length.</param>
    [TestMethod]
    [DataRow("LINESTRING(0 0, 3 4)", 5.0)]
    [DataRow("LINESTRING(0 0, 10 0, 10 10)", 20.0)]
    [DataRow("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 40.0)]
    [DataRow("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))", 48.0)]
    [DataRow("MULTILINESTRING((0 0, 1 0), (0 0, 0 2))", 3.0)]
    [DataRow("GEOMETRYCOLLECTION(LINESTRING(0 0, 1 0), POINT(5 5))", 1.0)]
    [DataRow("MULTIPOINT((1 2), (3 4))", 0.0)]
    [DataRow("LINESTRING EMPTY", 0.0)]
    public void LengthSumsSegmentsWithPolygonPerimeters(string text, double expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.AreEqual(expected, GeometryMeasures.Length(in geometry), Tolerance, $"Length of '{text}'.");
    }

    /// <summary>The anchored shoelace keeps far-from-origin areas stable.</summary>
    [TestMethod]
    public void AnchorTranslationKeepsFarFromOriginAreasStable()
    {
        //The same unit square, offset nine orders of magnitude: the anchored shoelace
        //keeps its terms small, so the area survives with tight error.
        Assert.IsTrue(WktGeometryReader.TryRead(
            "POLYGON((100000000 100000000, 100000001 100000000, 100000001 100000001, 100000000 100000001, 100000000 100000000))",
            out FlatGeometry geometry, out _));

        Assert.AreEqual(1.0, GeometryMeasures.Area(in geometry), 1e-6, "The anchored shoelace stays stable far from the origin.");
    }

    /// <summary>The internal signed-area convention answers negative for a counter-clockwise ring.</summary>
    [TestMethod]
    public void StoredRingOrientationConventionIsClockwisePositive()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON((0 0, 1 0, 1 1, 0 1, 0 0))", out FlatGeometry geometry, out _));

        double signed = GeometryMeasures.SignedRingArea(geometry.Vertices, geometry.Parts[0]);

        Assert.AreEqual(-1.0, signed, Tolerance,
            "A counter-clockwise ring has negative signed area under the stored convention — the sign the relate engine will read.");
    }
}
