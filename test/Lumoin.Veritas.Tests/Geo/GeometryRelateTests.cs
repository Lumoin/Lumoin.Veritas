using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The matrix golden-fixture, empty-semantics, collection-refusal,
/// boundary-rule, and degeneracy families of the relate engine: whole-matrix
/// asserts over the mechanically derived canon — the sole guard on these
/// strings, since predicate-vs-pattern consistency reads the same computed
/// matrix.
/// </summary>
[TestClass]
internal sealed class GeometryRelateTests
{
    /// <summary>Whole matrices reproduce the derived canon per operand pair.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="expected">The expected nine-cell serialization.</param>
    [TestMethod]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "2FFF1FFF2", DisplayName = "Two equal polygons")]
    [DataRow("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))", "212FF1FF2", DisplayName = "Containment without boundary contact")]
    [DataRow("POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))", "2FF1FF212", DisplayName = "The containment transpose")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "POLYGON ((0 0, 8 0, 8 8, 0 8, 0 0))", "2FF11F212", DisplayName = "Tangential proper part")]
    [DataRow("LINESTRING (0 0, 2 2)", "LINESTRING (0 2, 2 0)", "0F1FF0102", DisplayName = "Two lines crossing")]
    [DataRow("LINESTRING (1 0, 3 0)", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "F1FF0F212", DisplayName = "Line along a polygon ring")]
    [DataRow("POINT (1 1)", "LINESTRING (0 0, 2 2)", "0FFFFF102", DisplayName = "Point on a line interior")]
    [DataRow("POINT (0 0)", "LINESTRING (0 0, 2 2)", "F0FFFF102", DisplayName = "Point at a line endpoint")]
    [DataRow("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))", "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))", "FF2F01212", DisplayName = "Two polygons touching at one point")]
    [DataRow("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 1, 0 0)), ((3 0, 4 0, 4 1, 3 1, 3 0)))", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", "2F2F11FF2", DisplayName = "MultiPolygon member equal, member disjoint")]
    [DataRow("LINESTRING (5 5, 4 5)", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (4 4, 7 4, 7 7, 4 7, 4 4))", "FF1F00212", DisplayName = "Line in a hole touching the hole ring")]
    [DataRow("MULTIPOINT ((0 0), (1 1))", "MULTIPOINT ((1 1), (2 2))", "0F0FFF0F2", DisplayName = "Overlapping point sets")]
    [DataRow("LINESTRING (0 0, 1 1)", "LINESTRING (5 5, 6 6)", "FF1FF0102", DisplayName = "Envelope-disjoint lines")]
    [DataRow("LINESTRING (0 0, 0 2, 2 2)", "LINESTRING (1 0, 2 0, 2 1)", "FF1FF0102", DisplayName = "Interlocking disjoint lines agree with the envelope-disjoint form")]
    [DataRow("MULTILINESTRING ((0 0, 2 2), (2 0, 0 2))", "LINESTRING (0 1, 2 1)", "0F1FF0102", DisplayName = "Three segments concurrent at the lattice point")]
    [DataRow("LINESTRING (2 2, 6 2)", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "1010F0212", DisplayName = "Line crossing out of a polygon")]
    public void GoldenMatricesReproduceTheDerivedCanon(string firstText, string secondText, string expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(first, second, out IntersectionMatrix matrix), "Non-collection operands always relate.");

        Assert.AreEqual(expected, matrix.ToString(), $"relate('{firstText}', '{secondText}').");
    }

    /// <summary>Empty operands compute their derived matrices, never refuse.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="expected">The expected nine-cell serialization.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", "POINT EMPTY", "FFFFFFFF2", DisplayName = "Empty against empty")]
    [DataRow("POINT EMPTY", "POINT (1 1)", "FFFFFF0F2", DisplayName = "Empty against a point")]
    [DataRow("LINESTRING EMPTY", "LINESTRING (0 0, 1 1)", "FFFFFF102", DisplayName = "Empty against a line")]
    [DataRow("MULTIPOLYGON EMPTY", "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "FFFFFF212", DisplayName = "Empty against a polygon")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", "POINT EMPTY", "FF2FF1FF2", DisplayName = "A polygon against empty")]
    [DataRow("POINT EMPTY", "LINESTRING EMPTY", "FFFFFFFF2", DisplayName = "Two empties of different kinds")]
    public void EmptyOperandsComputeTheirDerivedForms(string firstText, string secondText, string expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(first, second, out IntersectionMatrix matrix), "Empty operands compute, never refuse.");

        Assert.AreEqual(expected, matrix.ToString(), $"relate('{firstText}', '{secondText}').");
    }

    /// <summary>Collection operands refuse by root kind, in either position, empties included.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    [TestMethod]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 1))", "POINT (1 1)")]
    [DataRow("POINT (1 1)", "GEOMETRYCOLLECTION (POINT (1 1))")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT (1 1)")]
    [DataRow("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (1 1)))", "POINT (1 1)")]
    public void CollectionOperandsAreRefused(string firstText, string secondText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");

        Assert.IsFalse(GeometryRelate.TryRelate(first, second, out _), "A collection operand refuses by root kind, the empty collection included.");
        Assert.IsFalse(GeometryRelate.TryEvaluate(first, second, TopologicalPredicate.SfDisjoint, out _), "Predicates refuse collection operands the same way.");
    }

    /// <summary>The uninitialized carrier answers as the empty collection and is refused.</summary>
    [TestMethod]
    public void DefaultGeometryIsRefusedAsTheEmptyCollection()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");

        Assert.IsFalse(GeometryRelate.TryRelate(default, point, out _), "default(FlatGeometry) answers as the empty collection and is refused.");
        Assert.IsFalse(GeometryRelate.TryRelate(point, default, out _), "The refusal holds in either operand position.");
    }

    /// <summary>A closed curve's start vertex has even valence and is interior under the Mod-2 rule.</summary>
    [TestMethod]
    public void ClosedRingStartVertexIsInteriorNotBoundary()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 2 0, 2 2, 0 2, 0 0)", out FlatGeometry ring, out _), "The closed line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (0 0)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(point, ring, out IntersectionMatrix matrix), "The pair must relate.");

        Assert.AreEqual("0FFFFF1F2", matrix.ToString(), "A closed curve's start vertex has even valence and is interior under the Mod-2 rule.");
    }

    /// <summary>A valence-three junction is boundary under the parity rule.</summary>
    [TestMethod]
    public void OddValenceJunctionIsBoundaryUnderTheParityRule()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTILINESTRING ((0 0, 1 1), (1 1, 2 0), (1 1, 1 3))", out FlatGeometry branches, out _),
            "The three-branch junction must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(point, branches, out IntersectionMatrix matrix), "The pair must relate.");

        Assert.AreEqual("F0FFFF102", matrix.ToString(), "A valence-three junction is boundary under the Mod-2 rule.");
    }

    /// <summary>A T-junction is boundary contact: it touches and never crosses.</summary>
    [TestMethod]
    public void LineEndingOnAnotherLineInteriorTouches()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (1 1, 1 3)", out FlatGeometry stem, out _), "The stem must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 1, 2 1)", out FlatGeometry bar, out _), "The bar must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(stem, bar, TopologicalPredicate.SfTouches, out bool touches), "The pair must evaluate.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(stem, bar, TopologicalPredicate.SfCrosses, out bool crosses), "The pair must evaluate.");

        Assert.IsTrue(touches, "A T-junction is boundary contact, so it touches.");
        Assert.IsFalse(crosses, "A T-junction never crosses — the interiors do not meet.");
    }

    /// <summary>Negative and positive zero tally as one point in the valence table.</summary>
    [TestMethod]
    public void SignedZeroCoordinatesTallyAsOnePoint()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (-0 0, 2 0, 2 2, 0 0)", out FlatGeometry ring, out _), "The closed line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (0 0)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(point, ring, out IntersectionMatrix matrix), "The pair must relate.");

        Assert.AreEqual("0FFFFF1F2", matrix.ToString(), "Negative and positive zero are one point, so the curve closes with even valence.");
    }

    /// <summary>A zero-length line's point set computes as the point fast path would.</summary>
    [TestMethod]
    public void ZeroLengthLineBehavesAsAPointSetInTheMatrix()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (1 1, 1 1)", out FlatGeometry degenerate, out _), "The degenerate line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(degenerate, point, out IntersectionMatrix throughGeneral), "The general path must relate.");
        Assert.IsTrue(GeometryRelate.TryRelate(point, point, out IntersectionMatrix throughFastPath), "The fast path must relate.");

        Assert.AreEqual(throughFastPath.ToString(), throughGeneral.ToString(), "The general path agrees with the point fast path on the same point sets.");
    }

    /// <summary>The kind-intrinsic gate lets a zero-length line cross where collapse-gating would not.</summary>
    [TestMethod]
    public void ZeroLengthLineCrossesUnderTheKindIntrinsicGate()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (1 1, 1 1)", out FlatGeometry degenerate, out _), "The degenerate line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 2 2)", out FlatGeometry line, out _), "The line must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(degenerate, line, TopologicalPredicate.SfCrosses, out bool crosses), "The pair must evaluate.");

        Assert.IsTrue(crosses, "The kind-intrinsic gate selects the line/line branch, whose exact-zero cell holds — a recorded divergence from collapse-gating, outside the validity contract.");
    }

    /// <summary>A point-collapsed ring terminates and answers through its point set.</summary>
    [TestMethod]
    public void PointCollapsedRingTerminatesAndAnswers()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((1 1, 1 1, 1 1, 1 1))", out FlatGeometry collapsed, out _), "The collapsed ring must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(collapsed, point, out IntersectionMatrix matrix), "The degenerate operand must terminate.");

        Assert.AreEqual('0', matrix.ToString()[3], "The collapsed ring's point set meets the point at its boundary cell.");
    }

    /// <summary>A repeated vertex is skipped as a zero-length segment, leaving a clean crossing.</summary>
    [TestMethod]
    public void RepeatedVerticesCauseNoSpuriousTouch()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 1 1, 1 1, 2 2)", out FlatGeometry stuttered, out _), "The stuttered line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 2, 2 0)", out FlatGeometry crossing, out _), "The crossing line must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(stuttered, crossing, out IntersectionMatrix matrix), "The pair must relate.");

        Assert.AreEqual("0F1FF0102", matrix.ToString(), "A repeated vertex is skipped as a zero-length segment, leaving a clean crossing.");
    }

    /// <summary>An out-of-contract self-intersecting operand terminates and repeats identically.</summary>
    [TestMethod]
    public void SelfIntersectingRingAnswersDeterministically()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("POLYGON ((0 0, 4 0, 0 4, 4 4, 0 0))", out FlatGeometry bowtie, out _),
            "The self-intersecting ring must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (2 1)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(bowtie, point, out IntersectionMatrix once), "The out-of-contract operand must terminate.");
        Assert.IsTrue(GeometryRelate.TryRelate(bowtie, point, out IntersectionMatrix again), "The second call must terminate identically.");

        Assert.AreEqual(once.ToString(), again.ToString(), "Identical calls answer identically — repeat-stability pinned.");
    }

    /// <summary>Relate is transpose-symmetric across the corpus product.</summary>
    [TestMethod]
    public void RelateIsTransposeSymmetricAcrossTheCanon()
    {
        string[] corpus =
        [
            "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))",
            "POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))",
            "LINESTRING (1 1, 5 5)",
            "POINT (2 2)",
            "MULTIPOINT ((0 0), (3 3))",
        ];

        foreach(string firstText in corpus)
        {
            foreach(string secondText in corpus)
            {
                Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
                Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
                Assert.IsTrue(GeometryRelate.TryRelate(first, second, out IntersectionMatrix forward), "The forward pair must relate.");
                Assert.IsTrue(GeometryRelate.TryRelate(second, first, out IntersectionMatrix backward), "The backward pair must relate.");

                Assert.AreEqual(
                    forward.Transpose().ToString(),
                    backward.ToString(),
                    $"relate('{secondText}', '{firstText}') is the transpose of the forward matrix.");
            }
        }
    }
}
