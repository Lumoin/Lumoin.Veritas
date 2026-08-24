using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The centroid surface: effective-dimension stratification with the measure
/// thresholds shared with area/length, role-signed anchored moments with the
/// explicit de-anchoring identity, the closure-is-structure vertex mean, emptiness
/// refusals, the two-tier geometry collapse, planar carriage, and bitwise
/// determinism.
/// </summary>
[TestClass]
internal sealed class GeometryCentroidTests
{
    /// <summary>The absolute bound the derived-canon comparisons hold to.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>The looser absolute bound for far-from-origin rows, whose answers live at 1e8.</summary>
    private const double FarTolerance = 1e-6;

    /// <summary>The centroid reproduces the derived canon per operand.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedX">The expected centroid X.</param>
    /// <param name="expectedY">The expected centroid Y.</param>
    [TestMethod]
    [DataRow("POINT (3 4)", 3.0, 4.0, DisplayName = "single point is itself")]
    [DataRow("MULTIPOINT ((0 0), (0 0), (3 0))", 1.0, 0.0, DisplayName = "duplicate positions are mass")]
    [DataRow("LINESTRING (0 0, 2 0, 2 1)", 4.0 / 3.0, 1.0 / 6.0, DisplayName = "line weights by length, not vertices")]
    [DataRow("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", 0.5, 0.5, DisplayName = "unit square")]
    [DataRow("POLYGON ((0 0, 2 0, 2 1, 1 1, 1 2, 0 2, 0 0))", 5.0 / 6.0, 5.0 / 6.0, DisplayName = "L-shape diverges from its vertex mean")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 2, 1 1))", 61.0 / 30.0, 61.0 / 30.0, DisplayName = "hole subtracts its moment")]
    [DataRow("MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((10 0, 11 0, 11 1, 10 1, 10 0)))", 2.9, 0.9, DisplayName = "members blend area-weighted, never equal-weight")]
    [DataRow("MULTILINESTRING ((0 0, 4 0), (0 10, 2 10))", 5.0 / 3.0, 10.0 / 3.0, DisplayName = "multiline blends by length")]
    public void CentroidReproducesTheDerivedCanon(string inputText, double expectedX, double expectedY)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d centroid), $"centroid('{inputText}') answers.");
        Assert.AreEqual(expectedX, centroid.X, Tolerance, $"centroid('{inputText}').X.");
        Assert.AreEqual(expectedY, centroid.Y, Tolerance, $"centroid('{inputText}').Y.");
    }

    /// <summary>Only the highest stratum carrying nonzero measure contributes.</summary>
    [TestMethod]
    public void HighestNonzeroMeasureStratumIgnoresLowerStrata()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0)), LINESTRING (10 10, 20 10), POINT (-50 0))",
                out FlatGeometry mixed, out _),
            "The mixed collection must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in mixed, out Point2d centroid), "The mixed collection answers.");
        Assert.AreEqual(1.0, centroid.X, Tolerance, "Only the areal stratum weighs in.");
        Assert.AreEqual(1.0, centroid.Y, Tolerance, "Only the areal stratum weighs in.");
    }

    /// <summary>A far point member is ignored outright by an areal operand, bit for bit.</summary>
    [TestMethod]
    public void FarPointMemberNeverMovesAnArealCentroid()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0)), POINT (1000000 1000000))",
                out FlatGeometry collection, out _),
            "The polygon-plus-far-point collection must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", out FlatGeometry solo, out _),
            "The solo polygon must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in collection, out Point2d fromCollection), "The collection answers.");
        Assert.IsTrue(GeometryCentroid.TryCompute(in solo, out Point2d fromSolo), "The solo polygon answers.");
        Assert.AreEqual(fromSolo, fromCollection, "The far point is ignored outright, bit-for-bit.");
    }

    /// <summary>A zero-area polygon cascades to the length rule over its own rings.</summary>
    [TestMethod]
    public void ZeroAreaPolygonCascadesToItsOwnRingLength()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 0 0, 0 0))", out FlatGeometry degenerate, out _),
            "The zero-area polygon must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead("LINESTRING (0 0, 4 0, 0 0, 0 0)", out FlatGeometry asLine, out _),
            "The same run as a linestring must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in degenerate, out Point2d fromPolygon), "The degenerate polygon answers.");
        Assert.IsTrue(GeometryCentroid.TryCompute(in asLine, out Point2d fromLine), "The linestring answers.");
        Assert.AreEqual(fromLine, fromPolygon, "The cascade is the length rule over the same vertex run.");
        Assert.AreEqual(2.0, fromPolygon.X, Tolerance, "The length-weighted answer.");
        Assert.AreEqual(0.0, fromPolygon.Y, Tolerance, "The length-weighted answer.");
    }

    /// <summary>A zero-length line cascades to the vertex mean of its coincident run.</summary>
    [TestMethod]
    public void ZeroLengthLineCascadesToItsCoincidentVertex()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (5 5, 5 5)", out FlatGeometry operand, out _), "The zero-length line must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d centroid), "The zero-length line answers.");
        Assert.AreEqual(new Point2d(5, 5), centroid, "The vertex mean of a coincident run is the position itself.");
    }

    /// <summary>Ring edges and a real line blend inside one length stratum.</summary>
    [TestMethod]
    public void DegeneratePolygonBesideARealLineBlendsAtDimensionOne()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (POLYGON ((0 0, 2 0, 0 0, 0 0)), LINESTRING (0 10, 2 10))",
                out FlatGeometry operand, out _),
            "The degenerate-polygon-plus-line collection must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d centroid), "The collection answers.");
        Assert.AreEqual(1.0, centroid.X, Tolerance, "Ring edges and the line blend in one length stratum.");
        Assert.AreEqual(10.0 / 3.0, centroid.Y, Tolerance, "Ring edges and the line blend in one length stratum.");
    }

    /// <summary>A ring's closing duplicate is storage, never mass.</summary>
    [TestMethod]
    public void RingClosureIsStructureNeverMass()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (POINT (0 0), POLYGON ((10 10, 10 10, 10 10, 10 10)))",
                out FlatGeometry operand, out _),
            "The closure collection must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d centroid), "The closure collection answers.");
        Assert.AreEqual(7.5, centroid.X, Tolerance, "Three stored ring copies are mass, the closing duplicate is not.");
        Assert.AreEqual(7.5, centroid.Y, Tolerance, "Three stored ring copies are mass, the closing duplicate is not.");
    }

    /// <summary>Empty operands refuse on the Try tier and collapse on the geometry tier.</summary>
    /// <param name="inputText">The WKT operand.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", DisplayName = "empty point")]
    [DataRow("LINESTRING EMPTY", DisplayName = "empty linestring")]
    [DataRow("POLYGON EMPTY", DisplayName = "empty polygon")]
    [DataRow("MULTIPOINT EMPTY", DisplayName = "empty multipoint")]
    [DataRow("MULTILINESTRING EMPTY", DisplayName = "empty multilinestring")]
    [DataRow("MULTIPOLYGON EMPTY", DisplayName = "empty multipolygon")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", DisplayName = "empty collection")]
    public void EmptyOperandsRefuseOnTheTryTierAndCollapseOnTheGeometryTier(string inputText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsFalse(GeometryCentroid.TryCompute(in operand, out _), $"centroid('{inputText}') refuses by emptiness.");

        FlatGeometry collapsed = GeometryCentroid.ComputeCentroidGeometry(in operand);

        Assert.AreEqual("POINT EMPTY", WktGeometryWriter.WriteString(in collapsed), "The geometry tier stays total.");
    }

    /// <summary>The uninitialized carrier refuses on the Try tier and is total on the geometry tier.</summary>
    [TestMethod]
    public void DefaultOperandRefusesWithoutThrowing()
    {
        Assert.IsFalse(GeometryCentroid.TryCompute(default, out _), "centroid(default) refuses by emptiness.");

        FlatGeometry collapsed = GeometryCentroid.ComputeCentroidGeometry(default);

        Assert.AreEqual("POINT EMPTY", WktGeometryWriter.WriteString(in collapsed), "The geometry tier is total on default.");
    }

    /// <summary>The geometry tier is the scalar answer wrapped as a point.</summary>
    [TestMethod]
    public void GeometryTierWrapsTheScalarAnswer()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", out FlatGeometry operand, out _),
            "The unit square must parse.");

        FlatGeometry point = GeometryCentroid.ComputeCentroidGeometry(in operand);

        Assert.AreEqual("POINT (0.5 0.5)", WktGeometryWriter.WriteString(in point), "The geometry tier is the scalar answer as a point.");
    }

    /// <summary>Every stratum's anchoring holds far from the origin.</summary>
    [TestMethod]
    public void AnchoringHoldsEveryStratumFarFromTheOrigin()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "POLYGON ((100000000 100000000, 100000001 100000000, 100000001 100000001, 100000000 100000001, 100000000 100000000))",
                out FlatGeometry square, out _),
            "The offset square must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "LINESTRING (100000000 100000000, 100000002 100000000, 100000002 100000001)",
                out FlatGeometry polyline, out _),
            "The offset polyline must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "MULTIPOINT ((100000000 100000000), (100000000 100000000), (100000003 100000000))",
                out FlatGeometry points, out _),
            "The offset multipoint must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in square, out Point2d squareCentroid), "The offset square answers.");
        Assert.AreEqual(100000000.5, squareCentroid.X, Tolerance, "The areal anchor holds at 1e8.");
        Assert.AreEqual(100000000.5, squareCentroid.Y, Tolerance, "The areal anchor holds at 1e8.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in polyline, out Point2d lineCentroid), "The offset polyline answers.");
        Assert.AreEqual(100000000.0 + (4.0 / 3.0), lineCentroid.X, FarTolerance, "The lineal anchor holds at 1e8.");
        Assert.AreEqual(100000000.0 + (1.0 / 6.0), lineCentroid.Y, FarTolerance, "The lineal anchor holds at 1e8.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in points, out Point2d pointsCentroid), "The offset multipoint answers.");
        Assert.AreEqual(100000001.0, pointsCentroid.X, Tolerance, "The puntal anchor holds at 1e8.");
        Assert.AreEqual(100000000.0, pointsCentroid.Y, Tolerance, "The puntal anchor holds at 1e8.");
    }

    /// <summary>Far-apart members blend through the per-ring de-anchoring identity.</summary>
    [TestMethod]
    public void FarApartMembersBlendThroughTheDeAnchoringIdentity()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "MULTIPOLYGON (((100000000 0, 100000001 0, 100000001 1, 100000000 1, 100000000 0)), ((0 100000000, 1 100000000, 1 100000001, 0 100000001, 0 100000000)))",
                out FlatGeometry operand, out _),
            "The far-apart multipolygon must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d centroid), "The far-apart multipolygon answers.");
        Assert.AreEqual(50000000.5, centroid.X, FarTolerance, "The per-ring de-anchoring identity carries the blend.");
        Assert.AreEqual(50000000.5, centroid.Y, FarTolerance, "The per-ring de-anchoring identity carries the blend.");
    }

    /// <summary>Centroid results are planar: Z and M never ride along.</summary>
    [TestMethod]
    public void CarriageNeverRidesTheCentroid()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON Z ((0 0 5, 4 0 6, 4 4 7, 0 4 8, 0 0 5))", out FlatGeometry operand, out _),
            "The Z polygon must parse.");
        Assert.IsTrue(operand.Is3D, "The operand carries Z.");

        FlatGeometry point = GeometryCentroid.ComputeCentroidGeometry(in operand);

        Assert.IsFalse(point.Is3D, "Centroid results carry no Z.");
        Assert.IsFalse(point.IsMeasured, "Centroid results carry no M.");
        Assert.HasCount(0, point.ZOrdinates, "No Z column is allocated on a centroid result.");
        Assert.AreEqual("POINT (2 2)", WktGeometryWriter.WriteString(in point), "The centroid is the planar answer.");
    }

    /// <summary>Two identical centroid calls answer bitwise-identical results.</summary>
    [TestMethod]
    public void RepeatedCallsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 2 0, 2 1, 1 1, 1 2, 0 2, 0 0))", out FlatGeometry operand, out _),
            "The L-shape must parse.");

        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d first), "The first call answers.");
        Assert.IsTrue(GeometryCentroid.TryCompute(in operand, out Point2d second), "The second call answers.");
        Assert.AreEqual(first, second, "Two identical centroid calls answer bitwise-identical results.");

        FlatGeometry once = GeometryCentroid.ComputeCentroidGeometry(in operand);
        FlatGeometry twice = GeometryCentroid.ComputeCentroidGeometry(in operand);

        Assert.IsTrue(once.Equals(twice), "Two identical geometry-tier calls answer bitwise-identical results.");
    }
}
