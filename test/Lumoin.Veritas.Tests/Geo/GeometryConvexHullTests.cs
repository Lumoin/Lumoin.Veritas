using System.Buffers;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The convex hull surface: monotone-chain golden fixtures, kind-blind
/// collection operands, collinear mid-edge exclusion, value-equality dedup with the
/// signed-zero pin, the envelope-family degenerate collapses, canonical
/// counter-clockwise emission, totality, and the planar-XY carriage drop.
/// </summary>
[TestClass]
internal sealed class GeometryConvexHullTests
{
    /// <summary>The hull reproduces the derived canon per operand.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedText">The expected canonical hull emission.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", "POINT (1 2)", DisplayName = "single point")]
    [DataRow("MULTIPOINT ((0 0), (4 0), (4 4), (0 4), (2 2))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", DisplayName = "interior point drops")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "POLYGON ((0 0, 2 0, 1 1, 0 0))", DisplayName = "bent line closes")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 2 1, 0 4, 0 0))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", DisplayName = "concavity fills")]
    [DataRow("GEOMETRYCOLLECTION (POINT (0 0), LINESTRING (4 0, 4 4), POINT (0 4))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", DisplayName = "collection is kind-blind")]
    [DataRow("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (0 0), POINT (4 0)), POINT (2 5))", "POLYGON ((0 0, 4 0, 2 5, 0 0))", DisplayName = "nested collection is kind-blind")]
    [DataRow("MULTIPOINT ((0 0), (2 0), (4 0), (4 4))", "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "collinear mid-edge excluded")]
    [DataRow("POLYGON ((0 0, 4 0, 0 4, 0 0))", "POLYGON ((0 0, 4 0, 0 4, 0 0))", DisplayName = "convex operand reproduces")]
    [DataRow("MULTILINESTRING ((0 0, 1 1), (4 0, 4 4))", "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "multiline flattens")]
    [DataRow("MULTIPOLYGON (((0 0, 1 0, 0 1, 0 0)), ((4 0, 5 0, 4 5, 4 0)))", "POLYGON ((0 0, 5 0, 4 5, 0 1, 0 0))", DisplayName = "multipolygon flattens")]
    [DataRow("GEOMETRYCOLLECTION (POINT EMPTY, LINESTRING (0 0, 3 0), POINT (1 4))", "POLYGON ((0 0, 3 0, 1 4, 0 0))", DisplayName = "empty members contribute nothing")]
    public void HullReproducesTheDerivedCanon(string inputText, string expectedText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        FlatGeometry hull = GeometryConvexHull.Compute(in operand);

        Assert.AreEqual(expectedText, WktGeometryWriter.WriteString(in hull), $"convexHull('{inputText}').");
    }

    /// <summary>Degenerate operands collapse per the envelope family's convention.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedText">The expected collapsed emission.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", "POINT EMPTY", DisplayName = "typed empty collapses to the empty point")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT EMPTY", DisplayName = "empty collection collapses to the empty point")]
    [DataRow("MULTIPOINT ((1 1), (1 1))", "POINT (1 1)", DisplayName = "coincident positions collapse to one point")]
    [DataRow("LINESTRING (0 0, 1 1, 3 3)", "LINESTRING (0 0, 3 3)", DisplayName = "collinear set collapses to the extreme pair")]
    [DataRow("MULTIPOINT ((2 2), (0 0))", "LINESTRING (0 0, 2 2)", DisplayName = "two positions collapse to a linestring")]
    public void DegenerateOperandsCollapsePerTheEnvelopeFamily(string inputText, string expectedText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        FlatGeometry hull = GeometryConvexHull.Compute(in operand);

        Assert.AreEqual(expectedText, WktGeometryWriter.WriteString(in hull), $"convexHull('{inputText}').");
    }

    /// <summary>The uninitialized carrier has a defined hull — the function is total.</summary>
    [TestMethod]
    public void DefaultOperandAnswersTheEmptyPoint()
    {
        FlatGeometry hull = GeometryConvexHull.Compute(default);

        Assert.AreEqual("POINT EMPTY", WktGeometryWriter.WriteString(in hull), "convexHull(default) is total.");
    }

    /// <summary>Value-equal signed-zero positions dedup to one canonical vertex.</summary>
    [TestMethod]
    public void SignedZeroPositionsCoincideAndCanonicalize()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((-0 -0), (0 0), (3 0), (0 3))", out FlatGeometry operand, out _),
            "The signed-zero multipoint must parse.");

        FlatGeometry hull = GeometryConvexHull.Compute(in operand);

        Assert.AreEqual(
            "POLYGON ((0 0, 3 0, 0 3, 0 0))",
            WktGeometryWriter.WriteString(in hull),
            "Value-equal signed-zero positions dedup to one canonical vertex.");
    }

    /// <summary>Two identical hull calls answer bitwise-identical results.</summary>
    [TestMethod]
    public void RepeatedCallsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (4 4), (1 3), (0 4))", out FlatGeometry operand, out _),
            "The corpus multipoint must parse.");

        FlatGeometry first = GeometryConvexHull.Compute(in operand);
        FlatGeometry second = GeometryConvexHull.Compute(in operand);

        Assert.IsTrue(first.Equals(second), "Two identical hull calls answer bitwise-identical results.");
    }

    /// <summary>Hull results are planar: Z and M never ride along.</summary>
    [TestMethod]
    public void CarriageNeverRidesTheHull()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON Z ((0 0 5, 4 0 6, 4 4 7, 0 4 8, 0 0 5))", out FlatGeometry operand, out _),
            "The Z polygon must parse.");
        Assert.IsTrue(operand.Is3D, "The operand carries Z.");

        FlatGeometry hull = GeometryConvexHull.Compute(in operand);

        Assert.IsFalse(hull.Is3D, "Hull results carry no Z.");
        Assert.IsFalse(hull.IsMeasured, "Hull results carry no M.");
        Assert.HasCount(0, hull.ZOrdinates, "No Z column is allocated on a hull result.");
        Assert.AreEqual("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", WktGeometryWriter.WriteString(in hull), "The hull is the planar answer.");
    }

    /// <summary>The hull owns fresh columns and survives operand disposal.</summary>
    [TestMethod]
    public void HullNeverAliasesTheOperandColumns()
    {
        var allocator = new CountingAllocator();
        var allocators = new FlatGeometryAllocators(allocator.RentVertices, allocator.RentOrdinates);

        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (2 5))", allocators, out FlatGeometry operand, out _),
            "The counted multipoint must parse.");
        int liveAfterParse = allocator.Live;

        FlatGeometry hull = GeometryConvexHull.Compute(in operand);

        Assert.AreEqual(liveAfterParse, allocator.Live, "The hull rents nothing from the operand's allocator.");

        hull.Dispose();

        Assert.AreEqual(liveAfterParse, allocator.Live, "Disposing the hull returns no operand rental.");

        operand.Dispose();

        Assert.AreEqual(
            "POLYGON ((0 0, 4 0, 2 5, 0 0))",
            WktGeometryWriter.WriteString(in hull),
            "The hull survives operand disposal — no aliased columns.");
    }

    /// <summary>A pooling stand-in counting live rentals; methods bind as the seam's named delegates.</summary>
    private sealed class CountingAllocator
    {
        /// <summary>The rentals not yet returned.</summary>
        public int Live { get; set; }

        /// <summary>Rents a counted vertex column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
        public CountingOwner<Point2d> RentVertices(int length)
        {
            Live++;

            return new CountingOwner<Point2d>(this, new Point2d[length]);
        }

        /// <summary>Rents a counted ordinate column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
        public CountingOwner<double> RentOrdinates(int length)
        {
            Live++;

            return new CountingOwner<double>(this, new double[length]);
        }
    }

    /// <summary>A rental that reports its return to the counting allocator.</summary>
    private sealed class CountingOwner<T>(CountingAllocator allocator, T[] array): IMemoryOwner<T>
    {
        /// <summary>The allocator the return reports to.</summary>
        private CountingAllocator Allocator { get; } = allocator;

        /// <summary>The rented storage.</summary>
        private T[] Backing { get; } = array;

        /// <inheritdoc/>
        public Memory<T> Memory => Backing;

        /// <inheritdoc/>
        public void Dispose()
        {
            Allocator.Live--;
        }
    }
}
