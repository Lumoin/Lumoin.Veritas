using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The concave hull families: the counts overload's Euler and covering identities,
/// the hand-derived erosion canon, the loose-extreme delegation table, the
/// degenerate ladder, the malformed bucket, the result oracles, and determinism.
/// Every pinned literal's derivation is recorded on its row.
/// </summary>
[TestClass]
internal sealed class GeometryConcaveHullTests
{
    /// <summary>The absolute bound the area comparisons hold to.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>The erosion canon reproduces the hand-derived rows with their mesh census.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="edgeLengthRatio">The concaveness ratio.</param>
    /// <param name="expectedTriangles">The expected real triangle count at build time.</param>
    /// <param name="expectedGhosts">The expected ghost count at build time.</param>
    /// <param name="expectedEroded">The expected number of border deletions.</param>
    /// <param name="expectedTriangulatedArea">The expected build-time absolute area sum.</param>
    /// <param name="expectedText">The expected boundary trace.</param>
    [TestMethod]
    [DataRow(
        "MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))", 0.5, 4, 4, 1, 24.0,
        "POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))",
        DisplayName = "The five-point rectangle erodes its top triangle at ratio one half")]
    [DataRow(
        "MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))", 0.0, 4, 4, 1, 24.0,
        "POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))",
        DisplayName = "The five-point rectangle at ratio zero reaches the same fixed point")]
    [DataRow(
        "MULTIPOINT ((0 0), (0 2), (1 1), (2 0), (2 2))", 0.5, 4, 4, 1, 4.0,
        "POLYGON ((0 0, 2 0, 2 2, 0 2, 1 1, 0 0))",
        DisplayName = "The square fan settles its four-way tie on the index-triple key")]
    [DataRow(
        "MULTIPOINT ((0 0), (1 0), (2 0), (0 1), (1 1), (2 1), (0 2), (1 2), (2 2))", 0.5, 8, 8, 0, 4.0,
        "POLYGON ((0 0, 1 0, 2 0, 2 1, 2 2, 1 2, 0 2, 0 1, 0 0))",
        DisplayName = "The three-by-three grid erodes nothing at ratio one half")]
    [DataRow(
        "MULTIPOINT ((0 0), (1 0), (2 0), (2 1), (3 0))", 0.0, 3, 5, 0, 1.5,
        "POLYGON ((0 0, 1 0, 2 0, 3 0, 2 1, 0 0))",
        DisplayName = "The collinear-run fixture retains its collinear boundary points")]
    [DataRow(
        "MULTIPOINT ((0 0), (1 0), (2 0), (3 0), (3 1), (3 2), (3 3), (2 3), (1 3), (0 3), (0 2), (0 1))", 0.0, 10, 12, 0, 9.0,
        "POLYGON ((0 0, 1 0, 2 0, 3 0, 3 1, 3 2, 3 3, 2 3, 1 3, 0 3, 0 2, 0 1, 0 0))",
        DisplayName = "The hollow ring never hollows out - every apex is a boundary vertex")]
    [DataRow(
        "MULTIPOINT ((0 1), (1 5), (2 3), (3 2), (5 1))", 0.0, 5, 3, 2, 10.0,
        "POLYGON ((0 1, 5 1, 3 2, 1 5, 2 3, 0 1))",
        DisplayName = "The strict-cavity discriminator keeps the sorted-order diagonals")]
    //Derivations. Rectangle: census shortest √10 /
    //longest 6, target(0.5) ≈ 4.581; the two size-36 border triangles tie and
    //the top one wins on area (18 > 6 twice-area), its removal retracts both
    //side triangles at their second boundary edge and flags apex (3 1), so
    //the bottom triangle is refused — one removal at either ratio. Square
    //fan: unique four-fan around (1 1), four-way tie at size 4 / twice-area
    //2, the index triple (0, 1, 2) selects the left triangle, after which
    //(1 1) is a boundary vertex and everything else is refused. Grid:
    //target(0.5) ≈ 1.207 exceeds every unit boundary edge regardless of the
    //cocircular cells' diagonals. Collinear run: candidates sort with prefix
    //(0 0),(1 0),(2 0) and apex (2 1); inserting (3 0) gates zero on both
    //prefix ghosts and strictly on the apex-side seed — the seed rule
    //finds the cavity. Hollow ring and discriminator: every ring apex is a
    //boundary vertex so nothing erodes; the discriminator's five points sort
    //to prefix (0 1),(1 5) with apex (2 3), the strict rule keeps the
    //sorted-insertion diagonals ((2 3)–(3 2) and (2 3)–(5 1)), both hull-edge
    //triangles erode at target zero, and a Sign >= 0 variant of the cavity
    //gate flips the diagonals and answers a different ring.
    public void ErosionCanonAnswersTheDerivedRows(
        string inputText,
        double edgeLengthRatio,
        int expectedTriangles,
        int expectedGhosts,
        int expectedEroded,
        double expectedTriangulatedArea,
        string expectedText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(
            GeometryConcaveHull.TryCompute(
                in operand, edgeLengthRatio, out FlatGeometry hull,
                out int triangleCount, out int erodedTriangleCount, out int ghostCount, out double triangulatedArea),
            $"concaveHull('{inputText}', {edgeLengthRatio}) computes.");

        Assert.AreEqual(expectedTriangles, triangleCount, "The Euler pin holds pre-erosion.");
        Assert.AreEqual(expectedGhosts, ghostCount, "The ghost count is the boundary-cycle point count.");
        Assert.AreEqual(expectedEroded, erodedTriangleCount, "The derived erosion count holds.");
        Assert.AreEqual(expectedTriangulatedArea, triangulatedArea, Tolerance, "The covering identity holds.");
        Assert.AreEqual(expectedText, WktGeometryWriter.WriteString(in hull), "The derived boundary trace holds.");
    }

    /// <summary>Ratio one short-circuits to the convex hull before any triangulation work.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedText">The expected convex-hull emission.</param>
    [TestMethod]
    [DataRow("MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))", "POLYGON ((0 0, 6 0, 6 4, 0 4, 0 0))", DisplayName = "A triangulable multipoint")]
    [DataRow("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))", "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))", DisplayName = "A polygon operand")]
    [DataRow("POINT (3 7)", "POINT (3 7)", DisplayName = "A single point")]
    [DataRow("LINESTRING (0 0, 5 0)", "LINESTRING (0 0, 5 0)", DisplayName = "A two-point line")]
    [DataRow("MULTIPOINT ((0 0), (1 0), (5 0))", "LINESTRING (0 0, 5 0)", DisplayName = "A collinear multipoint")]
    [DataRow("POINT EMPTY", "POINT EMPTY", DisplayName = "The empty point")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "POINT EMPTY", DisplayName = "The empty collection")]
    public void LooseExtremeDelegatesToTheConvexHull(string inputText, string expectedText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(
            GeometryConcaveHull.TryCompute(
                in operand, 1.0, out FlatGeometry hull,
                out int triangleCount, out int erodedTriangleCount, out int ghostCount, out double triangulatedArea),
            "Ratio one computes for every operand.");

        //Literal pins, not a differential table — comparing every row against
        //GeometryConvexHull.Compute would certify delegation against itself;
        //the one differential check rides the triangulable row's WKT.
        Assert.AreEqual(expectedText, WktGeometryWriter.WriteString(in hull), "The loose extreme is the convex hull, by delegation.");
        Assert.AreEqual(0, triangleCount, "No triangulation work on the short-circuit path.");
        Assert.AreEqual(0, erodedTriangleCount, "No erosion on the short-circuit path.");
        Assert.AreEqual(0, ghostCount, "No ghosts on the short-circuit path.");
        Assert.AreEqual(0.0, triangulatedArea, "No covering sum on the short-circuit path.");
    }

    /// <summary>Below three hull vertices the answer is parameter-independent.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="edgeLengthRatio">The concaveness ratio.</param>
    /// <param name="expectedText">The expected emission.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", 0.0, "POINT EMPTY", DisplayName = "Empty answers the empty point at ratio zero")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", 0.5, "POINT EMPTY", DisplayName = "The empty collection answers the empty point mid-ratio")]
    [DataRow("POINT (3 7)", 0.0, "POINT (3 7)", DisplayName = "One position answers its point at ratio zero")]
    [DataRow("MULTIPOINT ((2 1), (2 1))", 0.5, "POINT (2 1)", DisplayName = "Coincident positions answer one point mid-ratio")]
    [DataRow("LINESTRING (0 0, 5 0)", 0.0, "LINESTRING (0 0, 5 0)", DisplayName = "Two positions answer their line at ratio zero")]
    [DataRow("MULTIPOINT ((0 0), (1 0), (5 0))", 0.5, "LINESTRING (0 0, 5 0)", DisplayName = "Collinear-many answers the two-extreme line mid-ratio")]
    public void DegenerateLadderIsParameterIndependent(string inputText, double edgeLengthRatio, string expectedText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(
            GeometryConcaveHull.TryCompute(
                in operand, edgeLengthRatio, out FlatGeometry hull,
                out int triangleCount, out _, out int ghostCount, out _),
            "The ladder computes at every valid ratio.");

        Assert.AreEqual(expectedText, WktGeometryWriter.WriteString(in hull), "The ladder is parameter-independent below three hull vertices.");
        Assert.AreEqual(0, triangleCount, "No mesh on the delegated ladder.");
        Assert.AreEqual(0, ghostCount, "No ghosts on the delegated ladder.");
    }

    /// <summary>The answer-independent oracles hold: containment, area monotonicity, simplicity, and vertex provenance.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="edgeLengthRatio">The concaveness ratio.</param>
    [TestMethod]
    [DataRow("MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))", 0.5, DisplayName = "The eroded rectangle")]
    [DataRow("MULTIPOINT ((0 0), (0 2), (1 1), (2 0), (2 2))", 0.0, DisplayName = "The eroded square fan")]
    [DataRow("MULTIPOINT ((0 1), (1 5), (2 3), (3 2), (5 1))", 0.0, DisplayName = "The twice-eroded discriminator")]
    [DataRow("MULTIPOINT ((0 0), (1 0), (2 0), (2 1), (3 0))", 0.0, DisplayName = "The collinear-run boundary")]
    public void ResultOraclesHold(string inputText, double edgeLengthRatio)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");
        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, edgeLengthRatio, out FlatGeometry hull), "The oracle operand computes.");

        //Containment via the intersects predicate — the only shipped predicate
        //that is true for a vertex on the result's own boundary, the majority
        //case — with the evaluation's return asserted alongside.
        foreach(Point2d vertex in GeometryConvexHull.CollectDistinctSorted(operand.Vertices))
        {
            FlatGeometry point = FlatGeometryFactory.CreatePoint(vertex);

            Assert.IsTrue(GeometryRelate.TryEvaluate(point, hull, TopologicalPredicate.SfIntersects, out bool intersects), "The containment pair evaluates.");
            Assert.IsTrue(intersects, $"Every distinct operand vertex is inside or on the result — ({vertex.X}, {vertex.Y}).");
        }

        //Area monotonicity, one-directional; simplicity; and vertex
        //provenance — every result vertex bit-identical to a canonicalized
        //operand vertex.
        FlatGeometry convex = GeometryConvexHull.Compute(in operand);

        Assert.IsLessThanOrEqualTo(GeometryMeasures.Area(in convex) + Tolerance, GeometryMeasures.Area(in hull), "The result never exceeds the hull's area.");
        Assert.IsTrue(GeometrySimplicity.IsSimple(in hull), "Every polygonal result is simple.");

        List<Point2d> canonical = GeometryConvexHull.CollectDistinctSorted(operand.Vertices);

        foreach(Point2d vertex in hull.Vertices)
        {
            bool matched = false;

            foreach(Point2d candidate in canonical)
            {
                if(BitConverter.DoubleToInt64Bits(candidate.X) == BitConverter.DoubleToInt64Bits(vertex.X)
                    && BitConverter.DoubleToInt64Bits(candidate.Y) == BitConverter.DoubleToInt64Bits(vertex.Y))
                {
                    matched = true;

                    break;
                }
            }

            Assert.IsTrue(matched, $"Every result vertex is a canonicalized operand vertex, bit for bit — ({vertex.X}, {vertex.Y}).");
        }
    }

    /// <summary>A negative-zero operand vertex emits canonicalized to positive zero.</summary>
    [TestMethod]
    public void SignedZeroOperandVerticesEmitCanonicalized()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((-0 -0), (4 0), (4 4), (0 4), (1 1))", out FlatGeometry operand, out _),
            "The signed-zero operand must parse.");
        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, 0.5, out FlatGeometry hull), "The signed-zero operand computes.");

        string text = WktGeometryWriter.WriteString(in hull);

        Assert.StartsWith("POLYGON ((0 0", text, StringComparison.Ordinal, $"The -0.0 operand vertex emits canonicalized to +0.0: {text}");
    }

    /// <summary>Repeated calls answer bitwise-identically.</summary>
    [TestMethod]
    public void RepeatedCallsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))", out FlatGeometry operand, out _),
            "The operand must parse.");
        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, 0.5, out FlatGeometry first), "The first call computes.");
        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, 0.5, out FlatGeometry second), "The second call computes.");

        Assert.IsTrue(first.Equals(second), "Repeated calls answer bitwise-identically.");
    }

    /// <summary>The answer is a function of the point set alone, never of spelling or kind.</summary>
    /// <param name="inputText">The WKT operand, a respelling of the rectangle fixture.</param>
    [TestMethod]
    [DataRow("MULTIPOINT ((3 1), (6 4), (0 0), (0 4), (6 0))", DisplayName = "A shuffled spelling of the rectangle fixture")]
    [DataRow("GEOMETRYCOLLECTION (POINT (6 4), MULTIPOINT ((3 1), (0 0)), LINESTRING (0 4, 6 0))", DisplayName = "The same point set across kinds and depth")]
    public void SpellingAndKindNeverChangeTheAnswer(string inputText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(
            GeometryConcaveHull.TryCompute(
                in operand, 0.5, out FlatGeometry hull,
                out int triangleCount, out int erodedTriangleCount, out int ghostCount, out _),
            "The respelled operand computes.");

        Assert.AreEqual("POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))", WktGeometryWriter.WriteString(in hull), "The answer is a function of the point set alone.");
        Assert.AreEqual(4, triangleCount, "The canonical pipeline triangulates identically.");
        Assert.AreEqual(1, erodedTriangleCount, "The erosion trace is spelling-independent.");
        Assert.AreEqual(4, ghostCount, "The boundary cycle is spelling-independent.");
    }

    /// <summary>Hull results are planar: Z and M never ride along.</summary>
    [TestMethod]
    public void CarriageNeverRidesTheHull()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT Z ((0 0 9), (6 0 9), (6 4 9), (0 4 9), (3 1 9))", out FlatGeometry operand, out _),
            "The carrying operand must parse.");
        Assert.IsTrue(GeometryConcaveHull.TryCompute(in operand, 0.5, out FlatGeometry hull), "The carrying operand computes.");

        Assert.IsFalse(hull.Is3D, "Z never rides constructive output.");
        Assert.IsFalse(hull.IsMeasured, "M never rides constructive output.");
        Assert.HasCount(0, hull.ZOrdinates, "No Z column is allocated on the result.");
        Assert.AreEqual("POLYGON ((0 0, 6 0, 6 4, 3 1, 0 4, 0 0))", WktGeometryWriter.WriteString(in hull), "The planar answer is unchanged by carriage.");
    }

    /// <summary>Erosion holds off the lattice at a far, non-representable offset.</summary>
    [TestMethod]
    public void FarOffsetErosionHoldsOffTheLattice()
    {
        //The rectangle fixture translated by the non-representable
        //1e8 + 0.1: every coordinate rounds, so the anchored tie-break area
        //and the squared-domain census genuinely exercise off-lattice
        //rounding — an integer offset would be exact end-to-end and gate
        //nothing. The target margin (4 vs ~4.581 vs 6) is wide
        //enough that rounding cannot flip eligibility.
        const double Offset = 1e8 + 0.1;
        string inputText = FormattableString.Invariant(
            $"MULTIPOINT (({Offset:R} {Offset:R}), ({Offset + 6.0:R} {Offset:R}), ({Offset + 6.0:R} {Offset + 4.0:R}), ({Offset:R} {Offset + 4.0:R}), ({Offset + 3.0:R} {Offset + 1.0:R}))");

        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), "The far-offset operand must parse.");

        Assert.IsTrue(
            GeometryConcaveHull.TryCompute(
                in operand, 0.5, out FlatGeometry hull,
                out int triangleCount, out int erodedTriangleCount, out int ghostCount, out _),
            "The far-offset operand computes.");

        Assert.AreEqual(4, triangleCount, "The far-offset triangulation matches the origin fixture.");
        Assert.AreEqual(1, erodedTriangleCount, "The far-offset erosion matches the origin fixture.");
        Assert.AreEqual(4, ghostCount, "The far-offset boundary cycle matches the origin fixture.");

        //The trace matches the origin-centered answer up to translation,
        //within an absolute tolerance the offset's rounding scale sets.
        Span<Point2d> expected =
        [
            new Point2d(0.0, 0.0), new Point2d(6.0, 0.0), new Point2d(6.0, 4.0), new Point2d(3.0, 1.0), new Point2d(0.0, 4.0), new Point2d(0.0, 0.0),
        ];

        Assert.HasCount(expected.Length, hull.Vertices, "The far-offset ring has the origin fixture's shape.");

        for(int index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index].X + Offset, hull.Vertices[index].X, 1e-6, $"Far-offset X at {index}.");
            Assert.AreEqual(expected[index].Y + Offset, hull.Vertices[index].Y, 1e-6, $"Far-offset Y at {index}.");
        }
    }

    /// <summary>A ratio that is not a number in the closed unit interval is the data-plane refusal.</summary>
    /// <param name="edgeLengthRatio">The malformed concaveness ratio.</param>
    [TestMethod]
    [DataRow(double.NaN, DisplayName = "NaN refuses")]
    [DataRow(-0.1, DisplayName = "Below the interval refuses")]
    [DataRow(1.0000000000000002, DisplayName = "Above the interval refuses")]
    [DataRow(double.PositiveInfinity, DisplayName = "Positive infinity refuses")]
    [DataRow(double.NegativeInfinity, DisplayName = "Negative infinity refuses")]
    public void MalformedRatiosRefuse(double edgeLengthRatio)
    {
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT ((0 0), (6 0), (6 4), (0 4), (3 1))", out FlatGeometry operand, out _), "The operand must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("POINT EMPTY", out FlatGeometry empty, out _), "The empty operand must parse.");

        Assert.IsFalse(GeometryConcaveHull.TryCompute(in operand, edgeLengthRatio, out FlatGeometry hull), "A malformed ratio is the data-plane refusal.");
        Assert.IsTrue(hull.Equals(default(FlatGeometry)), "The refused out-value is default.");
        Assert.IsFalse(GeometryConcaveHull.TryCompute(in empty, edgeLengthRatio, out _), "Validation precedes the operand — the empty operand refuses too.");
    }
}
