using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The buffer surface: point circles under the arc contract, line capsules,
/// polygon dilation and erosion through the boundary-tube set arithmetic — both
/// erosion polarities, the inradius tie, hole heal and survival — the sign and
/// degeneracy conventions, collections, argument validation, and the carriage
/// drop. The validation-gate seam is shared with overlay by construction and
/// pinned in the overlay tests. Numeric asserts follow the distance-file
/// tolerance pattern; the arc areas bound by the inscribed-polygon contract
/// (under two percent at the default tessellation).
/// </summary>
[TestClass]
internal sealed class GeometryBufferTests
{
    /// <summary>The absolute tolerance for exact-expectation numeric asserts.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>A point buffer is the tessellated circle containing its center.</summary>
    [TestMethod]
    public void PointBufferIsTheTessellatedCircle()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (0 0)", out FlatGeometry point, out _), "The point must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in point, 1.0, out FlatGeometry buffered), "The point buffer applies.");
        Assert.AreEqual(GeometryKind.Polygon, buffered.Kind, "A point buffer is a polygon.");
        Assert.HasCount(33, buffered.Vertices, "Eight quadrant segments tessellate the full circle into thirty-two vertices plus closure.");

        foreach(Point2d vertex in buffered.Vertices)
        {
            double radius = Math.Sqrt((vertex.X * vertex.X) + (vertex.Y * vertex.Y));
            Assert.AreEqual(1.0, radius, Tolerance, "Every circle vertex sits at the buffer distance.");
        }

        Assert.IsTrue(GeometryRelate.TryEvaluate(in buffered, in point, TopologicalPredicate.SfContains, out bool contains), "The relate oracle applies.");
        Assert.IsTrue(contains, "The circle contains its center.");
    }

    /// <summary>The quadrant-segments argument controls the tessellation density.</summary>
    [TestMethod]
    public void QuadrantSegmentsControlTheTessellation()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (5 5)", out FlatGeometry point, out _), "The point must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in point, 2.0, 2, out FlatGeometry buffered), "The explicit tessellation applies.");
        Assert.HasCount(9, buffered.Vertices, "Two quadrant segments tessellate into eight vertices plus closure.");
    }

    /// <summary>A line buffer is the capsule containing its line.</summary>
    [TestMethod]
    public void LineBufferIsTheCapsule()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 10 0)", out FlatGeometry line, out _), "The line must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in line, 1.0, out FlatGeometry buffered), "The line buffer applies.");
        Assert.AreEqual(GeometryKind.Polygon, buffered.Kind, "A line buffer is a polygon.");

        double area = GeometryMeasures.Area(in buffered);
        Assert.IsTrue(area > 23.0 && area < 23.15, $"The capsule area {area} is the strip plus the inscribed disc.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(in buffered, in line, TopologicalPredicate.SfContains, out bool contains), "The relate oracle applies.");
        Assert.IsTrue(contains, "The capsule contains its line.");
    }

    /// <summary>Polygon dilation rounds the corners and contains its operand.</summary>
    [TestMethod]
    public void PolygonDilationRoundsTheCorners()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in square, 1.0, out FlatGeometry buffered), "The dilation applies.");

        double area = GeometryMeasures.Area(in buffered);
        Assert.IsTrue(area > 35.0 && area < 35.2, $"The dilated area {area} is the square, four strips, and the inscribed corner disc.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(in buffered, in square, TopologicalPredicate.SfContains, out bool contains), "The relate oracle applies.");
        Assert.IsTrue(contains, "The dilation contains its operand.");
    }

    /// <summary>Eroding a lattice square by one is the exact inner square.</summary>
    [TestMethod]
    public void SquareErosionIsExact()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in square, -1.0, out FlatGeometry eroded), "The erosion applies.");
        Assert.AreEqual(
            "POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))",
            WktGeometryWriter.WriteString(in eroded),
            "Eroding a lattice square by one is the exact inner square — set arithmetic, not approximation.");
    }

    /// <summary>The inradius tie counts as eroded; erosion past the reach empties.</summary>
    [TestMethod]
    public void ErosionAtTheInradiusTieEmpties()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in square, -2.0, out FlatGeometry eroded), "The tie erosion applies.");
        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in eroded), "The inradius tie counts as eroded.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in square, -3.0, out FlatGeometry vanished), "The past-inradius erosion applies.");
        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in vanished), "Full erosion answers the declared empty kind.");
    }

    /// <summary>A hole narrower than the dilation distance heals shut.</summary>
    [TestMethod]
    public void PositiveDistanceHealsANarrowHole()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 4 6, 6 6, 6 4, 4 4))", out FlatGeometry annulus, out _),
            "The annulus must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in annulus, 1.0, out FlatGeometry healed), "The dilation applies.");
        Assert.AreEqual(GeometryKind.Polygon, healed.Kind, "The healed result is one polygon.");
        Assert.HasCount(1, healed.Parts, "The hole narrower than the distance heals — dilation's erosion polarity.");

        double area = GeometryMeasures.Area(in healed);
        Assert.IsTrue(area > 143.0 && area < 143.2, $"The healed area {area} is the dilated outer square.");
    }

    /// <summary>A hole wider than the dilation distance survives, shrunken.</summary>
    [TestMethod]
    public void PositiveDistanceKeepsAWideHole()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 4 6, 6 6, 6 4, 4 4))", out FlatGeometry annulus, out _),
            "The annulus must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in annulus, 0.5, out FlatGeometry buffered), "The dilation applies.");
        Assert.HasCount(2, buffered.Parts, "The hole wider than the distance survives, shrunken.");

        double area = GeometryMeasures.Area(in buffered);
        Assert.IsTrue(area > 119.7 && area < 120.85, $"The area {area} is the dilated square minus the shrunken hole.");
    }

    /// <summary>The sign and empty conventions answer the declared empty polygon.</summary>
    /// <param name="operandText">The WKT operand.</param>
    /// <param name="distance">The buffer distance.</param>
    [TestMethod]
    [DataRow("POINT (1 1)", -1.0, DisplayName = "negative distance on a point")]
    [DataRow("POINT (1 1)", 0.0, DisplayName = "zero distance on a point")]
    [DataRow("LINESTRING (0 0, 5 5)", -0.5, DisplayName = "negative distance on a line")]
    [DataRow("LINESTRING (0 0, 5 5)", 0.0, DisplayName = "zero distance on a line")]
    [DataRow("POLYGON EMPTY", 3.0, DisplayName = "empty areal operand")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", 3.0, DisplayName = "empty collection operand")]
    public void SignAndEmptyConventionsAnswerTheEmptyPolygon(string operandText, double distance)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(operandText, out FlatGeometry operand, out _), $"'{operandText}' must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in operand, distance, out FlatGeometry buffered), "Buffer applies.");
        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in buffered), $"buffer('{operandText}', {distance}) is the declared empty kind.");
    }

    /// <summary>The uninitialized carrier buffers to the declared empty kind.</summary>
    [TestMethod]
    public void DefaultOperandAnswersTheEmptyPolygon()
    {
        Assert.IsTrue(GeometryBuffer.TryCompute(default, 5.0, out FlatGeometry buffered), "Buffer accepts the default operand.");
        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in buffered), "The default operand buffers to the declared empty kind.");
    }

    /// <summary>A valid areal operand's zero buffer reproduces its point set.</summary>
    [TestMethod]
    public void ZeroDistanceRegularizesAnArealOperand()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in square, 0.0, out FlatGeometry buffered), "The zero buffer applies.");
        Assert.AreEqual(
            "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in buffered),
            "A valid areal operand's zero buffer reproduces its point set.");
    }

    /// <summary>The zero buffer of an admitted-invalid operand is best-effort, never a repair promise.</summary>
    [TestMethod]
    public void ZeroDistanceBowTieIsBestEffortNotRepair()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 4, 4 0, 0 4, 0 0))", out FlatGeometry bowTie, out _), "The admitted bow-tie must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in bowTie, 0.0, out FlatGeometry buffered), "The zero buffer terminates deterministically.");
        Assert.AreEqual(
            "POLYGON ((0 0, 4 4, 4 0, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in buffered),
            "The bow-tie answers structurally well-formed with no repair promise.");
    }

    /// <summary>A non-finite distance is a malformed argument and refuses.</summary>
    [TestMethod]
    public void NonFiniteDistanceRefuses()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (0 0)", out FlatGeometry point, out _), "The point must parse.");

        Assert.IsFalse(GeometryBuffer.TryCompute(in point, double.NaN, out _), "A NaN distance is a malformed argument.");
        Assert.IsFalse(GeometryBuffer.TryCompute(in point, double.PositiveInfinity, out _), "An infinite distance is a malformed argument.");
    }

    /// <summary>A quadrant-segments argument below one is a caller contract violation and throws.</summary>
    [TestMethod]
    public void QuadrantSegmentsBelowOneThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GeometryBuffer.TryCompute(FlatGeometry.Empty(GeometryKind.Point), 1.0, 0, out _),
            "A quadrant-segments argument below one is a caller contract violation, never a domain outcome.");
    }

    /// <summary>Collections buffer per member and merge through union.</summary>
    [TestMethod]
    public void CollectionsBufferPerMemberAndMerge()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("GEOMETRYCOLLECTION (POINT (0 0), POINT (20 0))", out FlatGeometry collection, out _),
            "The collection must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in collection, 1.0, out FlatGeometry buffered), "The collection buffer applies.");
        Assert.AreEqual(GeometryKind.MultiPolygon, buffered.Kind, "Disjoint member buffers merge into a multipolygon.");

        Assert.IsTrue(
            WktGeometryReader.TryRead("GEOMETRYCOLLECTION (POINT (0 0), POINT (1 0))", out FlatGeometry overlapping, out _),
            "The overlapping collection must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in overlapping, 1.0, out FlatGeometry merged), "The overlapping collection buffer applies.");
        Assert.AreEqual(GeometryKind.Polygon, merged.Kind, "Overlapping member buffers merge into one polygon.");
    }

    /// <summary>A closed line buffers to the annulus tube.</summary>
    [TestMethod]
    public void ClosedLineBufferIsTheTube()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("LINESTRING (0 0, 10 0, 10 10, 0 10, 0 0)", out FlatGeometry ring, out _),
            "The closed line must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in ring, 1.0, out FlatGeometry buffered), "The closed-run buffer applies.");
        Assert.AreEqual(GeometryKind.Polygon, buffered.Kind, "The tube of a closed line is one polygon.");
        Assert.HasCount(2, buffered.Parts, "The tube is an annulus: outer loop and inner loop.");

        double area = GeometryMeasures.Area(in buffered);
        Assert.IsTrue(area > 79.0 && area < 79.2, $"The tube area {area} is the dilated square minus the eroded one.");
    }

    /// <summary>Buffer results are planar: Z and M never ride along.</summary>
    [TestMethod]
    public void CarriageNeverRidesBufferResults()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT Z (0 0 7)", out FlatGeometry point, out _), "The Z point must parse.");
        Assert.IsTrue(point.Is3D, "The operand carries Z.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in point, 1.0, out FlatGeometry buffered), "The buffer applies.");
        Assert.IsFalse(buffered.Is3D, "Buffer results carry no Z.");
        Assert.IsFalse(buffered.IsMeasured, "Buffer results carry no M.");
    }

    /// <summary>Two identical buffer calls answer bitwise-identical results.</summary>
    [TestMethod]
    public void RepeatedCallsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 3 4, 8 4)", out FlatGeometry line, out _), "The bent line must parse.");

        Assert.IsTrue(GeometryBuffer.TryCompute(in line, 0.75, out FlatGeometry once), "The first call applies.");
        Assert.IsTrue(GeometryBuffer.TryCompute(in line, 0.75, out FlatGeometry twice), "The second call applies.");
        Assert.IsTrue(once.Equals(twice), "Two identical buffer calls answer bitwise-identical results.");
    }
}
