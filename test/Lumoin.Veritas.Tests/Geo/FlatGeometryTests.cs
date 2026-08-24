using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The dimension family and carrier semantics of the geometry substrate: topological
/// dimension is kind-intrinsic with the recursive −1 rule for collections, the
/// coordinate/spatial answers are the any-fold house convention, the Z column carries
/// <see cref="double.NaN"/> in non-carrying members' slots, and an uninitialized
/// carrier degrades to the empty collection.
/// </summary>
[TestClass]
internal sealed class FlatGeometryTests
{
    /// <summary>Topological dimension is kind-intrinsic with the recursive −1 collection rule.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The expected topological dimension.</param>
    [TestMethod]
    [DataRow("POINT(1 2)", 0)]
    [DataRow("POINT EMPTY", 0)]
    [DataRow("MULTIPOINT EMPTY", 0)]
    [DataRow("LINESTRING(0 0, 1 1)", 1)]
    [DataRow("LINESTRING EMPTY", 1)]
    [DataRow("POLYGON((0 0, 1 0, 1 1, 0 0))", 2)]
    [DataRow("MULTIPOLYGON EMPTY", 2)]
    [DataRow("GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(0 0, 1 1))", 1)]
    [DataRow("GEOMETRYCOLLECTION(POINT EMPTY, LINESTRING EMPTY)", 1)]
    [DataRow("GEOMETRYCOLLECTION EMPTY", -1)]
    [DataRow("GEOMETRYCOLLECTION(GEOMETRYCOLLECTION EMPTY)", -1)]
    [DataRow("GEOMETRYCOLLECTION(GEOMETRYCOLLECTION EMPTY, POINT EMPTY)", 0)]
    public void TopologicalDimensionFollowsTheRecursiveKindRule(string text, int expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.AreEqual(expected, geometry.TopologicalDimension, $"'{text}' dimension.");
    }

    /// <summary>The coordinate family answers the any-fold convention over the nodes.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="coordinateDimension">The expected coordinate dimension.</param>
    /// <param name="spatialDimension">The expected spatial dimension.</param>
    /// <param name="is3D">Whether any node carries Z.</param>
    /// <param name="isMeasured">Whether any node carries M.</param>
    [TestMethod]
    [DataRow("POINT(1 2)", 2, 2, false, false)]
    [DataRow("POINT Z (1 2 3)", 3, 3, true, false)]
    [DataRow("POINT M (1 2 3)", 3, 2, false, true)]
    [DataRow("POINT ZM (1 2 3 4)", 4, 3, true, true)]
    [DataRow("GEOMETRYCOLLECTION(POINT Z (1 2 3), POINT M (1 2 4))", 4, 3, true, true)]
    [DataRow("GEOMETRYCOLLECTION(POINT Z (1 2 3), POINT(4 5))", 3, 3, true, false)]
    public void CoordinateFamilyAnswersTheAnyFoldConvention(
        string text, int coordinateDimension, int spatialDimension, bool is3D, bool isMeasured)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");

        Assert.AreEqual(coordinateDimension, geometry.CoordinateDimension, "Coordinate dimension.");
        Assert.AreEqual(spatialDimension, geometry.SpatialDimension, "Spatial dimension.");
        Assert.AreEqual(is3D, geometry.Is3D, "is-3D.");
        Assert.AreEqual(isMeasured, geometry.IsMeasured, "is-measured.");
    }

    /// <summary>A non-carrying member's slot in a present Z column holds NaN.</summary>
    [TestMethod]
    public void NonCarryingMembersHoldNaNInThePresentZColumn()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("GEOMETRYCOLLECTION(POINT Z (1 2 3), POINT(4 5))", out FlatGeometry geometry, out _));

        Assert.HasCount(2, geometry.ZOrdinates.ToArray(), "The Z column exists because one member carries Z.");
        Assert.AreEqual(3.0, geometry.ZOrdinates[0], "The carrying member's Z slot holds its value.");
        Assert.IsTrue(double.IsNaN(geometry.ZOrdinates[1]),
            "The non-carrying member's Z slot holds NaN — the stated column invariant.");
    }

    /// <summary>An uninitialized carrier answers as the empty geometry collection.</summary>
    [TestMethod]
    public void DefaultInstanceDegradesToTheEmptyCollection()
    {
        FlatGeometry uninitialized = default;

        Assert.AreEqual(GeometryKind.GeometryCollection, uninitialized.Kind, "Kind of default.");
        Assert.IsTrue(uninitialized.IsEmpty, "Default is empty.");
        Assert.AreEqual(-1, uninitialized.TopologicalDimension, "Dimension of default.");
        Assert.AreEqual(2, uninitialized.CoordinateDimension, "Coordinate dimension of default.");
        Assert.AreEqual("GEOMETRYCOLLECTION EMPTY", WktGeometryWriter.WriteString(in uninitialized),
            "Default writes as the empty collection.");
    }

    /// <summary>Every typed empty keeps its kind and answers empty.</summary>
    [TestMethod]
    public void TypedEmptiesKeepTheirKind()
    {
        foreach(GeometryKind kind in Enum.GetValues<GeometryKind>())
        {
            FlatGeometry empty = FlatGeometry.Empty(kind);

            Assert.AreEqual(kind, empty.Kind, $"Empty({kind}) keeps its kind.");
            Assert.IsTrue(empty.IsEmpty, $"Empty({kind}) is empty.");
        }
    }

    /// <summary>Structural equality compares coordinates bitwise, so negative zero differs.</summary>
    [TestMethod]
    public void StructuralEqualityComparesCoordinatesBitwise()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT(0 1)", out FlatGeometry positiveZero, out _));
        Assert.IsTrue(WktGeometryReader.TryRead("POINT(-0 1)", out FlatGeometry negativeZero, out _));
        Assert.IsTrue(WktGeometryReader.TryRead("POINT(0 1)", out FlatGeometry same, out _));

        Assert.AreEqual(positiveZero, same, "Identical text parses to equal values.");
        Assert.AreNotEqual(positiveZero, negativeZero,
            "Negative zero differs bitwise — value equality would mask a sign-normalizing writer.");
    }
}
