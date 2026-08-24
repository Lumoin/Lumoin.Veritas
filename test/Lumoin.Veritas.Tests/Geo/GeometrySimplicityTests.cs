using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The isSimple family: per-kind simplicity over simple and non-simple
/// fixtures, the per-element boundary rule for multi-curves, the collection
/// conjunction with its deliberately unexamined member arrangement, and the
/// Z-blindness discrimination.
/// </summary>
[TestClass]
internal sealed class GeometrySimplicityTests
{
    /// <summary>Simplicity answers per kind over the fixture table.</summary>
    /// <param name="text">The WKT operand.</param>
    /// <param name="expected">The expected simplicity verdict.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", true, DisplayName = "Empty point is vacuously simple")]
    [DataRow("MULTIPOLYGON EMPTY", true, DisplayName = "Empty multi is vacuously simple")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", true, DisplayName = "Empty collection is vacuously simple")]
    [DataRow("POINT (1 1)", true, DisplayName = "A point is always simple")]
    [DataRow("MULTIPOINT ((0 0), (1 1))", true, DisplayName = "Distinct points are simple")]
    [DataRow("MULTIPOINT ((1 1), (1 1))", false, DisplayName = "A duplicate position is not simple")]
    [DataRow("MULTIPOINT ((-0 0), (0 0))", false, DisplayName = "Signed zeros coincide")]
    [DataRow("MULTIPOINT Z ((1 2 3), (1 2 9))", false, DisplayName = "Members distinct only in Z coincide")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", true, DisplayName = "An open chain is simple")]
    [DataRow("LINESTRING (0 0, 2 0, 2 2, 0 2, 0 0)", true, DisplayName = "A closed ring is simple")]
    [DataRow("LINESTRING (0 0, 2 2, 2 0, 0 2)", false, DisplayName = "A self-crossing chain is not simple")]
    [DataRow("LINESTRING (0 0, 2 0, 1 0)", false, DisplayName = "A backtracking chain overlaps itself")]
    [DataRow("LINESTRING (0 0, 2 0, 2 2, 1 0)", false, DisplayName = "An endpoint landing on the interior is not simple")]
    [DataRow("LINESTRING (0 0, 1 1, 1 1, 2 2)", true, DisplayName = "A repeated vertex alone is not a self-intersection")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", true, DisplayName = "A convex ring is simple")]
    [DataRow("POLYGON ((0 0, 4 0, 0 4, 4 4, 0 0))", false, DisplayName = "A figure-eight ring is not simple")]
    [DataRow("POLYGON ((0 0, 8 0, 8 8, 0 8, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))", true, DisplayName = "A holed polygon with clean rings is simple")]
    [DataRow("MULTILINESTRING ((0 0, 1 1), (1 1, 2 0))", true, DisplayName = "Members meeting at shared endpoints are simple")]
    [DataRow("MULTILINESTRING ((0 0, 2 2), (0 2, 2 0))", false, DisplayName = "Members crossing mid-curve are not simple")]
    [DataRow("MULTILINESTRING ((0 0, 2 2), (1 1, 3 1))", false, DisplayName = "A member endpoint on another member's interior is not simple")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 1), LINESTRING (0 0, 2 2))", true, DisplayName = "A collection of simple members is simple")]
    [DataRow("GEOMETRYCOLLECTION (LINESTRING (0 0, 2 0, 1 0))", false, DisplayName = "A collection with a non-simple member is not simple")]
    public void SimplicityAnswersPerKind(string text, bool expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");

        Assert.AreEqual(expected, GeometrySimplicity.IsSimple(geometry), $"isSimple('{text}').");
    }

    /// <summary>A closed member's boundary is empty per element, so any contact with it breaks simplicity.</summary>
    [TestMethod]
    public void TwoClosedRingsTouchingAtAPointAreNotSimple()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTILINESTRING ((0 0, 2 0, 2 2, 0 2, 0 0), (2 2, 4 2, 4 4, 2 4, 2 2))", out FlatGeometry rings, out _),
            "The touching rings must parse.");

        Assert.IsFalse(
            GeometrySimplicity.IsSimple(rings),
            "A closed member's boundary is empty per element, so any contact with it breaks simplicity.");
    }

    /// <summary>The collection conjunction leaves the member-pairwise arrangement unexamined.</summary>
    [TestMethod]
    public void OverlappingSimpleCollectionMembersStayCollectivelySimple()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("GEOMETRYCOLLECTION (LINESTRING (0 0, 2 2), LINESTRING (0 2, 2 0))", out FlatGeometry collection, out _),
            "The collection must parse.");

        Assert.IsTrue(
            GeometrySimplicity.IsSimple(collection),
            "The member-pairwise arrangement is deliberately unexamined — two crossing simple members answer true.");
    }

    /// <summary>The uninitialized carrier is the empty collection and vacuously simple.</summary>
    [TestMethod]
    public void DefaultGeometryIsSimple()
    {
        Assert.IsTrue(GeometrySimplicity.IsSimple(default), "default(FlatGeometry) is the empty collection, vacuously simple.");
    }

    /// <summary>Structural equality sees the Z column while topological equality is planar.</summary>
    [TestMethod]
    public void StructuralEqualityAndTopologicalEqualsAnswerDifferentQuestions()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT Z (1 2 3)", out FlatGeometry withHeight, out _), "The Z point must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 2)", out FlatGeometry planar, out _), "The planar point must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(withHeight, planar, TopologicalPredicate.SfEquals, out bool topological), "The pair must evaluate.");

        Assert.IsFalse(withHeight.Equals(planar), "Structural equality sees the Z column.");
        Assert.IsTrue(topological, "Topological equality is planar by definition.");
    }
}
