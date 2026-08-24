using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The distance family of the geometry substrate: the containment pre-pass zeroes
/// interior overlaps, the facet phase minimizes over runs and points, distance is
/// invariant under multi/collection wrapping, symmetric, and undefined on empty
/// operands.
/// </summary>
[TestClass]
internal sealed class GeometryDistanceTests
{
    /// <summary>The comparison tolerance for distance values.</summary>
    private const double Tolerance = 1e-12;

    /// <summary>The distance matches the known answer and is symmetric.</summary>
    /// <param name="first">The first operand's WKT text.</param>
    /// <param name="second">The second operand's WKT text.</param>
    /// <param name="expected">The expected distance.</param>
    [TestMethod]
    [DataRow("POINT(0 0)", "POINT(3 4)", 5.0)]
    [DataRow("POINT(0 0)", "POINT(0 0)", 0.0)]
    [DataRow("POINT(5 5)", "LINESTRING(0 10, 10 10)", 5.0)]
    [DataRow("POINT(-3 -4)", "LINESTRING(0 0, 10 0)", 5.0)]
    [DataRow("POINT(15 3)", "LINESTRING(0 0, 10 0)", 5.830951894845301)]
    [DataRow("LINESTRING(0 0, 10 0)", "LINESTRING(0 5, 10 5)", 5.0)]
    [DataRow("LINESTRING(0 0, 10 0)", "LINESTRING(20 0, 30 0)", 10.0)]
    [DataRow("LINESTRING(0 0, 10 0)", "LINESTRING(5 -5, 5 5)", 0.0)]
    [DataRow("POINT(5 5)", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 0.0)]
    [DataRow("POINT(5 15)", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 5.0)]
    [DataRow("POINT(10 5)", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 0.0)]
    [DataRow("LINESTRING(4 4, 6 6)", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 0.0)]
    [DataRow("POLYGON((4 4, 6 4, 6 6, 4 6, 4 4))", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 0.0)]
    [DataRow("POLYGON((5 5, 15 5, 15 15, 5 15, 5 5))", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 0.0)]
    [DataRow("POINT(5 5)", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0), (3 3, 7 3, 7 7, 3 7, 3 3))", 2.0)]
    [DataRow("MULTIPOINT((100 0), (5 5))", "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", 0.0)]
    public void DistanceMatchesTheKnownAnswer(string first, string second, double expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(first, out FlatGeometry a, out _), $"'{first}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(second, out FlatGeometry b, out _), $"'{second}' must parse.");

        Assert.IsTrue(GeometryDistance.TryCompute(in a, in b, out double forward), "Distance is defined for non-empty operands.");
        Assert.IsTrue(GeometryDistance.TryCompute(in b, in a, out double backward), "Distance is defined both ways.");

        Assert.AreEqual(expected, forward, Tolerance, $"d('{first}', '{second}').");
        Assert.AreEqual(forward, backward, Tolerance, "Distance is symmetric.");
    }

    /// <summary>Distance is invariant under wrapping a leaf in a multi or collection.</summary>
    [TestMethod]
    public void DistanceIsInvariantUnderCollectionWrapping()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT(20 0)", out FlatGeometry bare, out _));
        Assert.IsTrue(WktGeometryReader.TryRead("GEOMETRYCOLLECTION(POINT(20 0))", out FlatGeometry wrapped, out _));
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))", out FlatGeometry polygon, out _));

        Assert.IsTrue(GeometryDistance.TryCompute(in bare, in polygon, out double bareDistance));
        Assert.IsTrue(GeometryDistance.TryCompute(in wrapped, in polygon, out double wrappedDistance));

        Assert.AreEqual(bareDistance, wrappedDistance, Tolerance,
            "Multis and collections contribute no distance cases of their own.");
    }

    /// <summary>A geometry inside a hole is outside the polygon and keeps its hole-ring distance.</summary>
    [TestMethod]
    public void PolygonInsideAnotherPolygonsHoleKeepsItsDistanceToTheHoleRing()
    {
        Assert.IsTrue(WktGeometryReader.TryRead(
            "POLYGON((4 4, 6 4, 6 6, 4 6, 4 4))", out FlatGeometry island, out _));
        Assert.IsTrue(WktGeometryReader.TryRead(
            "POLYGON((0 0, 10 0, 10 10, 0 10, 0 0), (3 3, 7 3, 7 7, 3 7, 3 3))", out FlatGeometry ring, out _));

        Assert.IsTrue(GeometryDistance.TryCompute(in island, in ring, out double distance));

        Assert.AreEqual(1.0, distance, Tolerance,
            "A geometry inside a hole is outside the polygon; the facet phase finds the hole-ring distance.");
    }

    /// <summary>A zero-length segment behaves as its point.</summary>
    [TestMethod]
    public void ZeroLengthDegenerateSegmentsReduceToPointCases()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING(5 5, 5 5)", out FlatGeometry degenerate, out _));
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING(0 0, 10 0)", out FlatGeometry line, out _));

        Assert.IsTrue(GeometryDistance.TryCompute(in degenerate, in line, out double distance));

        Assert.AreEqual(5.0, distance, Tolerance, "A zero-length segment behaves as its point.");
    }

    /// <summary>Distance is undefined when either operand is empty.</summary>
    /// <param name="first">The first operand's WKT text.</param>
    /// <param name="second">The second operand's WKT text.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", "POINT(1 2)")]
    [DataRow("POINT(1 2)", "GEOMETRYCOLLECTION EMPTY")]
    [DataRow("MULTIPOLYGON EMPTY", "LINESTRING EMPTY")]
    public void DistanceIsUndefinedOnEmptyOperands(string first, string second)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(first, out FlatGeometry a, out _), $"'{first}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(second, out FlatGeometry b, out _), $"'{second}' must parse.");

        Assert.IsFalse(GeometryDistance.TryCompute(in a, in b, out _),
            "Distance to the empty point set is undefined — never a fabricated zero.");
    }
}
