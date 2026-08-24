using System.Buffers;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The boolean overlay surface: the area/area canon across all four operations,
/// lower dimensions and mixed pairs, the pinned empty algebra with typed-empty
/// result kinds and the relate-admissibility composition in both directions,
/// collection admission with the stratified union fold, deterministic canonical
/// emission with bitwise commutativity, heterogeneous whole-point-set results, and
/// the noding-honesty pins.
/// </summary>
[TestClass]
internal sealed class GeometryOverlayTests
{
    /// <summary>The area canon reproduces across all four operations.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="intersectionText">The expected intersection emission.</param>
    /// <param name="unionText">The expected union emission.</param>
    /// <param name="differenceText">The expected difference emission.</param>
    /// <param name="symDifferenceText">The expected symmetric-difference emission.</param>
    [TestMethod]
    [DataRow(
        "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))",
        "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
        "POLYGON ((0 0, 4 0, 4 2, 6 2, 6 6, 2 6, 2 4, 0 4, 0 0))",
        "POLYGON ((0 0, 4 0, 4 2, 2 2, 2 4, 0 4, 0 0))",
        "MULTIPOLYGON (((0 0, 4 0, 4 2, 2 2, 2 4, 0 4, 0 0)), ((2 4, 4 4, 4 2, 6 2, 6 6, 2 6, 2 4)))",
        DisplayName = "overlapping squares")]
    [DataRow(
        "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", "POLYGON ((3 3, 4 3, 4 4, 3 4, 3 3))",
        "POLYGON EMPTY",
        "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 1, 0 0)), ((3 3, 4 3, 4 4, 3 4, 3 3)))",
        "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))",
        "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 1, 0 0)), ((3 3, 4 3, 4 4, 3 4, 3 3)))",
        DisplayName = "disjoint squares")]
    [DataRow(
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
        "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 4, 4 4, 4 2, 2 2))",
        "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 4, 4 4, 4 2, 2 2))",
        DisplayName = "contained square punches a hole")]
    [DataRow(
        "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))", "POLYGON ((2 0, 4 0, 4 2, 2 2, 2 0))",
        "LINESTRING (2 0, 2 2)",
        "POLYGON ((0 0, 2 0, 4 0, 4 2, 2 2, 0 2, 0 0))",
        "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))",
        "POLYGON ((0 0, 2 0, 4 0, 4 2, 2 2, 0 2, 0 0))",
        DisplayName = "shared edge is the whole point set")]
    [DataRow(
        "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))", "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))",
        "POINT (2 2)",
        "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((2 2, 4 2, 4 4, 2 4, 2 2)))",
        "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))",
        "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((2 2, 4 2, 4 4, 2 4, 2 2)))",
        DisplayName = "single-point touch decomposes minimally")]
    [DataRow(
        "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
        "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
        "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
        "POLYGON EMPTY",
        "POLYGON EMPTY",
        DisplayName = "equal operands")]
    public void AreaCanonReproducesAcrossAllFourOperations(
        string firstText, string secondText,
        string intersectionText, string unionText, string differenceText, string symDifferenceText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual(intersectionText, WktGeometryWriter.WriteString(in intersection), $"intersection('{firstText}', '{secondText}').");

        Assert.IsTrue(GeometryOverlay.TryUnion(in first, in second, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(unionText, WktGeometryWriter.WriteString(in union), $"union('{firstText}', '{secondText}').");

        Assert.IsTrue(GeometryOverlay.TryDifference(in first, in second, out FlatGeometry difference), "Difference applies.");
        Assert.AreEqual(differenceText, WktGeometryWriter.WriteString(in difference), $"difference('{firstText}', '{secondText}').");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in first, in second, out FlatGeometry symDifference), "SymDifference applies.");
        Assert.AreEqual(symDifferenceText, WktGeometryWriter.WriteString(in symDifference), $"symDifference('{firstText}', '{secondText}').");
    }

    /// <summary>Lower-dimension pairs answer their whole point sets.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="intersectionText">The expected intersection emission.</param>
    /// <param name="unionText">The expected union emission.</param>
    [TestMethod]
    [DataRow(
        "LINESTRING (0 0, 2 2)", "LINESTRING (0 2, 2 0)",
        "POINT (1 1)",
        "MULTILINESTRING ((0 0, 1 1), (0 2, 1 1), (1 1, 2 0), (1 1, 2 2))",
        DisplayName = "crossing lines")]
    [DataRow(
        "LINESTRING (0 1, 4 1)", "POLYGON ((1 0, 3 0, 3 3, 1 3, 1 0))",
        "LINESTRING (1 1, 3 1)",
        "GEOMETRYCOLLECTION (LINESTRING (0 1, 1 1), LINESTRING (3 1, 4 1), POLYGON ((1 0, 3 0, 3 1, 3 3, 1 3, 1 1, 1 0)))",
        DisplayName = "line clipped by a polygon")]
    public void LowerDimensionPairsAnswerTheirPointSets(string firstText, string secondText, string intersectionText, string unionText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual(intersectionText, WktGeometryWriter.WriteString(in intersection), $"intersection('{firstText}', '{secondText}').");

        Assert.IsTrue(GeometryOverlay.TryUnion(in first, in second, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(unionText, WktGeometryWriter.WriteString(in union), $"union('{firstText}', '{secondText}').");
    }

    /// <summary>Collinear and touching line arrangements answer their point sets.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="intersectionText">The expected intersection emission.</param>
    /// <param name="unionText">The expected union emission.</param>
    [TestMethod]
    [DataRow(
        "LINESTRING (0 0, 4 0)", "LINESTRING (2 0, 6 0)",
        "LINESTRING (2 0, 4 0)",
        "LINESTRING (0 0, 2 0, 4 0, 6 0)",
        DisplayName = "collinear overlap")]
    [DataRow(
        "LINESTRING (0 0, 4 0)", "LINESTRING (2 0, 2 3)",
        "POINT (2 0)",
        "MULTILINESTRING ((0 0, 2 0), (2 0, 2 3), (2 0, 4 0))",
        DisplayName = "T junction")]
    public void LineArrangementsAnswerTheirPointSets(string firstText, string secondText, string intersectionText, string unionText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual(intersectionText, WktGeometryWriter.WriteString(in intersection), $"intersection('{firstText}', '{secondText}').");

        Assert.IsTrue(GeometryOverlay.TryUnion(in first, in second, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(unionText, WktGeometryWriter.WriteString(in union), $"union('{firstText}', '{secondText}').");
    }

    /// <summary>Puntal pairs follow the plain set algebra.</summary>
    [TestMethod]
    public void PuntalPairsFollowTheSetAlgebra()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT ((0 0), (1 1), (2 2))", out FlatGeometry first, out _), "The first multipoint must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT ((1 1), (3 3))", out FlatGeometry second, out _), "The second multipoint must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual("POINT (1 1)", WktGeometryWriter.WriteString(in intersection), "The shared position survives.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in first, in second, out FlatGeometry union), "Union applies.");
        Assert.AreEqual("MULTIPOINT ((0 0), (1 1), (2 2), (3 3))", WktGeometryWriter.WriteString(in union), "The merged set is distinct and sorted.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in first, in second, out FlatGeometry difference), "Difference applies.");
        Assert.AreEqual("MULTIPOINT ((0 0), (2 2))", WktGeometryWriter.WriteString(in difference), "The shared position drops.");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in first, in second, out FlatGeometry symmetric), "SymDifference applies.");
        Assert.AreEqual("MULTIPOINT ((0 0), (2 2), (3 3))", WktGeometryWriter.WriteString(in symmetric), "Exactly-one-side positions survive.");
    }

    /// <summary>Point-against-area operations route through the exact locator.</summary>
    [TestMethod]
    public void PointAgainstAreaRoutesThroughTheLocator()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT ((2 2), (9 9), (0 0))", out FlatGeometry points, out _), "The multipoint must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in points, in square, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual(
            "MULTIPOINT ((0 0), (2 2))",
            WktGeometryWriter.WriteString(in intersection),
            "Interior and boundary positions survive; the exterior one drops.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in points, in square, out FlatGeometry difference), "Difference applies.");
        Assert.AreEqual("POINT (9 9)", WktGeometryWriter.WriteString(in difference), "Only the exterior position survives.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in points, in square, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(
            "GEOMETRYCOLLECTION (POINT (9 9), POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)))",
            WktGeometryWriter.WriteString(in union),
            "Covered positions vanish into the area; the leftover joins it.");
    }

    /// <summary>Removing a measure-zero set keeps the closed area point set.</summary>
    [TestMethod]
    public void AreaMinusLineKeepsTheClosedPointSet()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (2 -1, 2 5)", out FlatGeometry line, out _), "The crossing line must parse.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in square, in line, out FlatGeometry difference), "Difference applies.");
        Assert.AreEqual(
            "POLYGON ((0 0, 2 0, 4 0, 4 4, 2 4, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in difference),
            "Removing a measure-zero set keeps the closed area, noded where the line met the boundary.");
    }

    /// <summary>Mixed-dimension pairs with an empty operand follow the dimension formula.</summary>
    [TestMethod]
    public void MixedDimensionEmptyFormulaPins()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 1 1)", out FlatGeometry line, out _), "The line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT EMPTY", out FlatGeometry emptyPoint, out _), "The empty point must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in line, in emptyPoint, out FlatGeometry intersection), "The mixed intersection applies.");
        Assert.AreEqual("POINT EMPTY", WktGeometryWriter.WriteString(in intersection), "Intersection types by the minimum dimension.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in line, in emptyPoint, out FlatGeometry union), "The mixed union applies.");
        Assert.AreEqual("LINESTRING (0 0, 1 1)", WktGeometryWriter.WriteString(in union), "Union answers the non-empty operand.");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in emptyPoint, in line, out FlatGeometry symmetric), "The mixed symDifference applies.");
        Assert.AreEqual("LINESTRING (0 0, 1 1)", WktGeometryWriter.WriteString(in symmetric), "SymDifference answers the non-empty operand.");
    }

    /// <summary>A line minus a covering area splits around it.</summary>
    [TestMethod]
    public void LineDifferenceSplitsAroundTheArea()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 1, 4 1)", out FlatGeometry line, out _), "The line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((1 0, 3 0, 3 3, 1 3, 1 0))", out FlatGeometry polygon, out _), "The polygon must parse.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in line, in polygon, out FlatGeometry difference), "Difference applies.");
        Assert.AreEqual(
            "MULTILINESTRING ((0 1, 1 1), (3 1, 4 1))",
            WktGeometryWriter.WriteString(in difference),
            "The outside pieces survive; the covered piece vanishes.");
    }

    /// <summary>The empty difference of a covered line carries the first operand's dimension.</summary>
    [TestMethod]
    public void CoveredLineDifferenceAnswersTheTypedEmpty()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (2 2, 3 3)", out FlatGeometry line, out _), "The line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", out FlatGeometry polygon, out _), "The polygon must parse.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in line, in polygon, out FlatGeometry difference), "Difference applies.");
        Assert.AreEqual(
            "LINESTRING EMPTY",
            WktGeometryWriter.WriteString(in difference),
            "The empty difference carries the first operand's dimension.");
    }

    /// <summary>The pinned empty-operand identities hold across all four operations.</summary>
    [TestMethod]
    public void EmptyAlgebraIdentitiesHold()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON EMPTY", out FlatGeometry emptyPolygon, out _), "The typed empty must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in square, in emptyPolygon, out FlatGeometry intersection), "a ∩ ∅ applies.");
        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in intersection), "a ∩ ∅ = ∅ typed by the minimum dimension.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in square, in emptyPolygon, out FlatGeometry union), "a ∪ ∅ applies.");
        Assert.AreEqual("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", WktGeometryWriter.WriteString(in union), "a ∪ ∅ = a as a canonical rebuild.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in square, in emptyPolygon, out FlatGeometry difference), "a − ∅ applies.");
        Assert.AreEqual("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", WktGeometryWriter.WriteString(in difference), "a − ∅ = a.");

        Assert.IsTrue(GeometryOverlay.TryDifference(in emptyPolygon, in square, out FlatGeometry reverse), "∅ − a applies.");
        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in reverse), "∅ − a = ∅ typed by the first operand.");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in square, in emptyPolygon, out FlatGeometry symmetric), "symDifference(a, ∅) applies.");
        Assert.AreEqual("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", WktGeometryWriter.WriteString(in symmetric), "symDifference(a, ∅) = a.");
    }

    /// <summary>Typed-empty overlay results stay relate-admissible; the one unresolvable case is refused both ways.</summary>
    [TestMethod]
    public void EmptyOverlayResultsStayRelateAdmissible()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", out FlatGeometry first, out _), "The first square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((3 3, 4 3, 4 4, 3 4, 3 3))", out FlatGeometry second, out _), "The second square must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry emptyResult), "The disjoint intersection applies.");
        Assert.IsTrue(
            GeometryRelate.TryRelate(in emptyResult, in first, out IntersectionMatrix matrix),
            "The typed empty result is admissible to relate — the deciding argument for typed empties.");
        Assert.AreEqual("FFFFFF212", matrix.ToString(), "The empty operand computes its derived form.");

        Assert.IsTrue(GeometryOverlay.TryUnion(FlatGeometry.Empty(GeometryKind.GeometryCollection), FlatGeometry.Empty(GeometryKind.GeometryCollection), out FlatGeometry unresolved), "The dimension−1 union applies.");
        Assert.AreEqual("GEOMETRYCOLLECTION EMPTY", WktGeometryWriter.WriteString(in unresolved), "The unresolvable dimension answers the empty collection.");
        Assert.IsFalse(
            GeometryRelate.TryRelate(in unresolved, in first, out _),
            "The one unresolvable case is refused by relate exactly as its operands would be — the falsifying direction.");
    }

    /// <summary>Collection operands refuse for every operation except union.</summary>
    [TestMethod]
    public void CollectionOperandsAreRefusedExceptForUnion()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("GEOMETRYCOLLECTION (POINT (1 1))", out FlatGeometry collection, out _), "The collection must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsFalse(GeometryOverlay.TryIntersection(in collection, in square, out _), "Intersection refuses a collection first operand.");
        Assert.IsFalse(GeometryOverlay.TryIntersection(in square, in collection, out _), "Intersection refuses a collection second operand.");
        Assert.IsFalse(GeometryOverlay.TryDifference(in collection, in square, out _), "Difference refuses a collection operand.");
        Assert.IsFalse(GeometryOverlay.TrySymDifference(in square, in collection, out _), "SymDifference refuses a collection operand.");
        Assert.IsFalse(GeometryOverlay.TryIntersection(FlatGeometry.Empty(GeometryKind.GeometryCollection), in square, out _), "The empty collection is refused by root kind.");
        Assert.IsFalse(GeometryOverlay.TryDifference(default, in square, out _), "The default operand is refused as the empty collection.");
    }

    /// <summary>The union fold resolves collection members through the binary engine.</summary>
    [TestMethod]
    public void UnionFoldResolvesCollectionsThroughTheBinaryEngine()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("GEOMETRYCOLLECTION (POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0)))", out FlatGeometry collection, out _),
            "The collection must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))", out FlatGeometry small, out _), "The small square must parse.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in collection, in small, out FlatGeometry union), "Union accepts the collection.");
        Assert.AreEqual(
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))",
            WktGeometryWriter.WriteString(in union),
            "The contained member vanishes into the containing one — the locator-honesty pin.");
    }

    /// <summary>The union fold merges mutually overlapping collection members.</summary>
    [TestMethod]
    public void UnionFoldMergesOverlappingMembers()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)), POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2)))",
                out FlatGeometry collection, out _),
            "The overlapping-members collection must parse.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in collection, FlatGeometry.Empty(GeometryKind.Polygon), out FlatGeometry union), "Union accepts the collection.");
        Assert.AreEqual(
            "POLYGON ((0 0, 4 0, 4 2, 6 2, 6 6, 2 6, 2 4, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in union),
            "Mutually overlapping members resolve to one merged arrangement.");
    }

    /// <summary>The union fold merges mixed dimensions under the coverage rule.</summary>
    [TestMethod]
    public void UnionFoldMergesMixedDimensionsUnderCoverage()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (POINT (1 1), POINT (20 20), LINESTRING (0 0, 3 3), LINESTRING (15 0, 16 0), POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)))",
                out FlatGeometry collection, out _),
            "The mixed collection must parse.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in collection, FlatGeometry.Empty(GeometryKind.GeometryCollection), out FlatGeometry union), "Union accepts the mixed collection.");
        Assert.AreEqual(
            "GEOMETRYCOLLECTION (POINT (20 20), LINESTRING (15 0, 16 0), POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)))",
            WktGeometryWriter.WriteString(in union),
            "Covered pieces vanish into higher strata; uncovered ones survive.");
    }

    /// <summary>Each hole assigns to its innermost enclosing shell.</summary>
    [TestMethod]
    public void NestedShellHoleAssignmentIsInnermost()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2))",
                out FlatGeometry outer, out _),
            "The outer annulus must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "POLYGON ((3 3, 7 3, 7 7, 3 7, 3 3), (4 4, 4 6, 6 6, 6 4, 4 4))",
                out FlatGeometry inner, out _),
            "The inner annulus must parse.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in outer, in inner, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(
            "MULTIPOLYGON (((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 8, 8 8, 8 2, 2 2)), ((3 3, 7 3, 7 7, 3 7, 3 3), (4 4, 4 6, 6 6, 6 4, 4 4)))",
            WktGeometryWriter.WriteString(in union),
            "Each hole assigns to its innermost enclosing shell.");
    }

    /// <summary>Commutative operations answer bitwise identically under operand swap.</summary>
    [TestMethod]
    public void CommutativeOperationsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 1 3)", out FlatGeometry first, out _), "The first diagonal must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 1, 3 0)", out FlatGeometry second, out _), "The second diagonal must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry forwardIntersection), "Forward intersection applies.");
        Assert.IsTrue(GeometryOverlay.TryIntersection(in second, in first, out FlatGeometry backwardIntersection), "Backward intersection applies.");
        Assert.IsTrue(
            forwardIntersection.Equals(backwardIntersection),
            "Intersection commutes bitwise on a diagonal crossing — the pair-ordering pin.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in first, in second, out FlatGeometry forwardUnion), "Forward union applies.");
        Assert.IsTrue(GeometryOverlay.TryUnion(in second, in first, out FlatGeometry backwardUnion), "Backward union applies.");
        Assert.IsTrue(forwardUnion.Equals(backwardUnion), "Union commutes bitwise.");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in first, in second, out FlatGeometry forwardSymmetric), "Forward symDifference applies.");
        Assert.IsTrue(GeometryOverlay.TrySymDifference(in second, in first, out FlatGeometry backwardSymmetric), "Backward symDifference applies.");
        Assert.IsTrue(forwardSymmetric.Equals(backwardSymmetric), "SymDifference commutes bitwise.");
    }

    /// <summary>Two identical overlay calls answer bitwise-identical results.</summary>
    [TestMethod]
    public void RepeatedCallsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry first, out _), "The first square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))", out FlatGeometry second, out _), "The second square must parse.");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in first, in second, out FlatGeometry once), "The first call applies.");
        Assert.IsTrue(GeometryOverlay.TrySymDifference(in first, in second, out FlatGeometry twice), "The second call applies.");
        Assert.IsTrue(once.Equals(twice), "Two identical calls answer bitwise-identical results.");
    }

    /// <summary>Value-equal signed-zero vertices node together and emit canonically.</summary>
    [TestMethod]
    public void SignedZeroVerticesNodeTogether()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 2 2)", out FlatGeometry first, out _), "The positive-zero line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (-0 -0, 2 -2)", out FlatGeometry second, out _), "The negative-zero line must parse.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in first, in second, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(
            "LINESTRING (2 -2, 0 0, 2 2)",
            WktGeometryWriter.WriteString(in union),
            "Value-equal signed-zero vertices are one node, emitted canonically.");
    }

    /// <summary>A collapsed slit edge labels from its ring role and survives as the honest line piece.</summary>
    [TestMethod]
    public void SlitRingCollapsedEdgeKeepsThePointSetHonestly()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 2 4, 2 6, 2 4, 0 4, 0 0))", out FlatGeometry slit, out _),
            "The parser-admitted slit ring must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((10 10, 11 10, 11 11, 10 11, 10 10))", out FlatGeometry island, out _), "The island must parse.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in slit, in island, out FlatGeometry union), "Union terminates deterministically on the admitted-invalid operand.");
        Assert.AreEqual(
            "GEOMETRYCOLLECTION (LINESTRING (2 4, 2 6), POLYGON ((0 0, 4 0, 4 4, 2 4, 0 4, 0 0)), POLYGON ((10 10, 11 10, 11 11, 10 11, 10 10)))",
            WktGeometryWriter.WriteString(in union),
            "The collapsed slit edge labels from its ring role and survives as the honest line piece.");
    }

    /// <summary>Bit-identical crossings group to one node.</summary>
    [TestMethod]
    public void ConcurrentSegmentsGroupAtTheLatticeNode()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTILINESTRING ((0 0, 2 2), (0 2, 2 0))", out FlatGeometry cross, out _),
            "The self-crossing multiline must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 1, 2 1)", out FlatGeometry horizontal, out _), "The horizontal line must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in cross, in horizontal, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual(
            "POINT (1 1)",
            WktGeometryWriter.WriteString(in intersection),
            "Bit-identical crossings group to one node — the lattice concurrency canon.");
    }

    /// <summary>The noded-arrangement validation scan refuses a residual crossing.</summary>
    [TestMethod]
    public void ValidationScanRefusesAResidualCrossing()
    {
        var edges = new List<(int Segment, Point2d Start, Point2d End)>
        {
            (0, new Point2d(0, 0), new Point2d(4, 4)),
            (1, new Point2d(0, 4), new Point2d(4, 0)),
        };

        Assert.IsFalse(
            OverlayNoding.ValidateNoded(edges),
            "A residual proper crossing between split edges is not a planar subdivision and refuses.");

        var noded = new List<(int Segment, Point2d Start, Point2d End)>
        {
            (0, new Point2d(0, 0), new Point2d(2, 2)),
            (0, new Point2d(2, 2), new Point2d(4, 4)),
            (1, new Point2d(0, 4), new Point2d(2, 2)),
            (1, new Point2d(2, 2), new Point2d(4, 0)),
        };

        Assert.IsTrue(OverlayNoding.ValidateNoded(noded), "The properly split arrangement passes the scan.");
    }

    /// <summary>Overlay results are planar: Z and M never ride along.</summary>
    [TestMethod]
    public void CarriageNeverRidesOverlayResults()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON Z ((0 0 5, 4 0 5, 4 4 5, 0 4 5, 0 0 5))", out FlatGeometry first, out _), "The Z square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON Z ((2 2 9, 6 2 9, 6 6 9, 2 6 9, 2 2 9))", out FlatGeometry second, out _), "The second Z square must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry intersection), "Intersection applies.");
        Assert.IsFalse(intersection.Is3D, "Overlay results carry no Z.");
        Assert.IsFalse(intersection.IsMeasured, "Overlay results carry no M.");
        Assert.AreEqual("POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))", WktGeometryWriter.WriteString(in intersection), "The planar answer stands.");
    }

    /// <summary>Results own fresh columns and survive operand disposal.</summary>
    [TestMethod]
    public void ResultsNeverAliasOperandColumns()
    {
        var allocator = new CountingAllocator();
        var allocators = new FlatGeometryAllocators(allocator.RentVertices, allocator.RentOrdinates);

        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", allocators, out FlatGeometry operand, out _),
            "The counted square must parse.");
        int liveAfterParse = allocator.Live;

        Assert.IsTrue(GeometryOverlay.TryUnion(in operand, FlatGeometry.Empty(GeometryKind.Polygon), out FlatGeometry union), "The identity union applies.");

        Assert.AreEqual(liveAfterParse, allocator.Live, "The result rents nothing from the operand's allocator.");

        union.Dispose();

        Assert.AreEqual(liveAfterParse, allocator.Live, "Disposing the result returns no operand rental.");

        operand.Dispose();

        Assert.AreEqual(
            "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in union),
            "The identity result survives operand disposal — a rebuild, never an alias.");
    }

    /// <summary>A degenerate operand gates by kind but contributes its point set.</summary>
    [TestMethod]
    public void DegenerateOperandContributesItsPointSet()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (1 1, 1 1)", out FlatGeometry degenerate, out _), "The zero-length line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry square, out _), "The square must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in degenerate, in square, out FlatGeometry intersection), "Intersection applies.");
        Assert.AreEqual("POINT (1 1)", WktGeometryWriter.WriteString(in intersection), "The degenerate line contributes its point set.");

        Assert.IsTrue(GeometryOverlay.TryUnion(in degenerate, in square, out FlatGeometry union), "Union applies.");
        Assert.AreEqual(
            "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
            WktGeometryWriter.WriteString(in union),
            "The covered degenerate operand vanishes into the area.");
    }

    /// <summary>The symmetric difference equals the union of both one-way differences.</summary>
    [TestMethod]
    public void SymDifferenceAgreesWithItsUnionOfDifferences()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry first, out _), "The first square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))", out FlatGeometry second, out _), "The second square must parse.");

        Assert.IsTrue(GeometryOverlay.TrySymDifference(in first, in second, out FlatGeometry symmetric), "SymDifference applies.");
        Assert.IsTrue(GeometryOverlay.TryDifference(in first, in second, out FlatGeometry forward), "Forward difference applies.");
        Assert.IsTrue(GeometryOverlay.TryDifference(in second, in first, out FlatGeometry backward), "Backward difference applies.");
        Assert.IsTrue(GeometryOverlay.TryUnion(in forward, in backward, out FlatGeometry composed), "The union of differences applies.");

        Assert.IsTrue(symmetric.Equals(composed), "symDifference(a, b) equals union(difference(a, b), difference(b, a)) bitwise.");
    }

    /// <summary>The relate predicates certify overlay results — the substrate as its own oracle.</summary>
    [TestMethod]
    public void RelatePredicatesAgreeWithOverlayResults()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out FlatGeometry first, out _), "The first square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))", out FlatGeometry second, out _), "The second square must parse.");

        Assert.IsTrue(GeometryOverlay.TryIntersection(in first, in second, out FlatGeometry intersection), "Intersection applies.");
        Assert.IsTrue(
            GeometryRelate.TryEvaluate(in intersection, in first, TopologicalPredicate.SfWithin, out bool within),
            "The relate oracle applies to the overlay result.");
        Assert.IsTrue(within, "intersection(a, b) is within a — the substrate as its own oracle.");
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
