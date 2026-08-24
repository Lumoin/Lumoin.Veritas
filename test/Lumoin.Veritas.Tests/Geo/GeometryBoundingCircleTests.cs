using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The covering-circle surface: the exact-gated maximization walk's
/// collapse cases, the exact three-survivor rule, deletion topology on
/// integer-exact operands via the internal rounds observable, the anchored
/// circumcenter solve far from the origin, kind-blind point-set totality,
/// emptiness refusals, order-independence, the certification pass (bitwise
/// carrier pins with zero lift on the canon roster, the one-bit firing rows,
/// the direct-drive lift, and the allocation gates), and the containment and
/// minimality oracles with containment on the exact excess sign. The hull seam
/// counts pin the degenerate cycle lengths the walk keys its collapses on.
/// </summary>
[TestClass]
internal sealed class GeometryBoundingCircleTests
{
    /// <summary>The absolute bound the derived-canon comparisons hold to.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>The circle reproduces the derived canon with its pinned deletion-round count.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedX">The expected centre X.</param>
    /// <param name="expectedY">The expected centre Y.</param>
    /// <param name="expectedRadius">The expected radius.</param>
    /// <param name="expectedRounds">The expected number of deletion rounds.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", 1.0, 2.0, 0.0, 0, DisplayName = "single point is a zero circle")]
    [DataRow("MULTIPOINT ((1 1), (1 1))", 1.0, 1.0, 0.0, 0, DisplayName = "coincident positions are a zero circle")]
    [DataRow("MULTIPOINT ((0 0), (4 0))", 2.0, 0.0, 2.0, 0, DisplayName = "two points answer their diametral circle")]
    [DataRow("LINESTRING (0 0, 1 1, 5 5)", 2.5, 2.5, 3.5355339059327378, 0, DisplayName = "collinear runs answer the extreme pair")]
    [DataRow("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", 0.5, 0.5, 0.7071067811865476, 2, DisplayName = "square deletes twice to its diagonal")]
    [DataRow("POLYGON ((0 0, 4 0, 1.5 3, 0 0))", 2.0, 0.875, 2.1830311495716203, 0, DisplayName = "acute triangle answers its circumcircle")]
    [DataRow("POLYGON ((0 0, 10 0, 5 0.3, 0 0))", 5.0, 0.0, 5.0, 1, DisplayName = "obtuse triangle answers its longest side, h = 0.3")]
    [DataRow("POLYGON ((0 0, 10 0, 5 0.31, 0 0))", 5.0, 0.0, 5.0, 1, DisplayName = "obtuse triangle, h = 0.31 - keys not bit-identical")]
    [DataRow("POLYGON ((0 0, 10 0, 5 0.7, 0 0))", 5.0, 0.0, 5.0, 1, DisplayName = "obtuse triangle, h = 0.7")]
    [DataRow("POLYGON ((0 0, 10 0, 5 2.4, 0 0))", 5.0, 0.0, 5.0, 1, DisplayName = "obtuse triangle, h = 2.4")]
    [DataRow("MULTIPOINT ((-10 0), (0 -1), (10 0), (0 1))", 0.0, 0.0, 10.0, 2, DisplayName = "kite deletes twice to its non-adjacent extremes")]
    [DataRow("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", 2.3, 1.5, 2.7459060435491963, 2, DisplayName = "irregular pentagon walks to a non-adjacent circumcircle")]
    public void CircleReproducesTheDerivedCanonWithItsRoundCount(
        string inputText,
        double expectedX,
        double expectedY,
        double expectedRadius,
        int expectedRounds)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(
            GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle, out int deletionRounds, out _),
            $"boundingCircle('{inputText}') answers.");
        Assert.AreEqual(expectedX, circle.Center.X, Tolerance, $"boundingCircle('{inputText}').Center.X.");
        Assert.AreEqual(expectedY, circle.Center.Y, Tolerance, $"boundingCircle('{inputText}').Center.Y.");
        Assert.AreEqual(expectedRadius, circle.Radius, Tolerance, $"boundingCircle('{inputText}').Radius.");
        Assert.AreEqual(expectedRounds, deletionRounds, $"boundingCircle('{inputText}') deletion rounds — the pinned walk topology.");
        AssertContainmentAndMinimality(in operand, circle, inputText);
    }

    /// <summary>A regular hexagon answers its own circumcircle; the round count is spelling-dependent and stays unpinned.</summary>
    [TestMethod]
    public void RegularHexagonAnswersItsCircumcircleScalarsOnly()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "POLYGON ((1 0, 0.5 0.8660254037844386, -0.5 0.8660254037844386, -1 0, -0.5 -0.8660254037844386, 0.5 -0.8660254037844386, 1 0))",
                out FlatGeometry operand, out _),
            "The hexagon must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), "The hexagon answers.");
        Assert.AreEqual(0.0, circle.Center.X, Tolerance, "The hexagon centers on its own center.");
        Assert.AreEqual(0.0, circle.Center.Y, Tolerance, "The hexagon centers on its own center.");
        Assert.AreEqual(1.0, circle.Radius, Tolerance, "The radius is the circumradius.");
        AssertContainmentAndMinimality(in operand, circle, "hexagon");
    }

    /// <summary>The anchored circumcenter solve conditions far from the origin.</summary>
    [TestMethod]
    public void AnchoredCircumcenterSolveHoldsFarFromTheOrigin()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "POLYGON ((100000000 100000000, 100000001 100000000, 100000001 100000001, 100000000 100000001, 100000000 100000000))",
                out FlatGeometry operand, out _),
            "The offset square must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), "The offset square answers.");
        Assert.AreEqual(100000000.5, circle.Center.X, Tolerance, "The anchored solve conditions at 1e8.");
        Assert.AreEqual(100000000.5, circle.Center.Y, Tolerance, "The anchored solve conditions at 1e8.");
        Assert.AreEqual(0.7071067811865476, circle.Radius, Tolerance, "The radius is offset-free.");
        AssertContainmentAndMinimality(in operand, circle, "offset square");
    }

    /// <summary>A quarter arc's circle is diametral on its chord.</summary>
    [TestMethod]
    public void ArcOfACircleAnswersItsChordDiametral()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "MULTIPOINT ((1 0), (0.9238795325112867 0.3826834323650898), (0.7071067811865476 0.7071067811865476), (0.3826834323650898 0.9238795325112867), (0 1))",
                out FlatGeometry operand, out _),
            "The quarter-arc must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), "The arc answers.");
        Assert.AreEqual(0.5, circle.Center.X, Tolerance, "The arc's circle is diametral on its chord.");
        Assert.AreEqual(0.5, circle.Center.Y, Tolerance, "The arc's circle is diametral on its chord.");
        Assert.AreEqual(0.7071067811865476, circle.Radius, Tolerance, "The chord is the diameter.");
        AssertContainmentAndMinimality(in operand, circle, "quarter arc");
    }

    /// <summary>A near-collinear triangle answers its longest side, never a noise circumcircle.</summary>
    [TestMethod]
    public void NearCollinearTriangleAnswersItsLongestSide()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (10 0), (5 0.000000001))", out FlatGeometry operand, out _),
            "The near-collinear triangle must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), "The near-collinear triangle answers.");
        Assert.AreEqual(5.0, circle.Center.X, Tolerance, "The exact three-survivor rule holds where keys cancel.");
        Assert.AreEqual(5.0, circle.Radius, Tolerance, "The diametral answer, not a noise circumcircle.");
        AssertContainmentAndMinimality(in operand, circle, "near-collinear triangle");
    }

    /// <summary>The circle is kind-blind over the flattened vertex set at any collection depth.</summary>
    [TestMethod]
    public void CollectionsAnswerTheirFlattenedVertexSet()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead(
                "GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (-10 0), POINT (0 -1)), LINESTRING (10 0, 0 1))",
                out FlatGeometry nested, out _),
            "The nested collection must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((-10 0), (0 -1), (10 0), (0 1))", out FlatGeometry flat, out _),
            "The flat multipoint must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in nested, out BoundingCircle fromNested), "The nested collection answers.");
        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in flat, out BoundingCircle fromFlat), "The flat multipoint answers.");
        Assert.AreEqual(fromFlat, fromNested, "The circle is kind-blind over the flattened vertex set.");
    }

    /// <summary>The hull canonicalizes order, so the circle is bitwise order-independent.</summary>
    [TestMethod]
    public void VertexOrderNeverMovesTheCircle()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", out FlatGeometry forward, out _),
            "The forward multipoint must parse.");
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((2 4), (0 0), (5 2), (0 3), (4 0))", out FlatGeometry shuffled, out _),
            "The shuffled multipoint must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in forward, out BoundingCircle first), "The forward order answers.");
        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in shuffled, out BoundingCircle second), "The shuffled order answers.");
        Assert.AreEqual(first, second, "The hull canonicalizes order, so the circle is bitwise order-independent.");
    }

    /// <summary>Empty operands refuse by emptiness, never by kind.</summary>
    /// <param name="inputText">The WKT operand.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", DisplayName = "empty point")]
    [DataRow("LINESTRING EMPTY", DisplayName = "empty linestring")]
    [DataRow("POLYGON EMPTY", DisplayName = "empty polygon")]
    [DataRow("MULTIPOINT EMPTY", DisplayName = "empty multipoint")]
    [DataRow("MULTILINESTRING EMPTY", DisplayName = "empty multilinestring")]
    [DataRow("MULTIPOLYGON EMPTY", DisplayName = "empty multipolygon")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", DisplayName = "empty collection")]
    public void EmptyOperandsRefuseByEmptiness(string inputText)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsFalse(GeometryBoundingCircle.TryCompute(in operand, out _), $"boundingCircle('{inputText}') refuses by emptiness.");
    }

    /// <summary>The uninitialized carrier refuses without throwing.</summary>
    [TestMethod]
    public void DefaultOperandRefusesWithoutThrowing()
    {
        Assert.IsFalse(GeometryBoundingCircle.TryCompute(default, out _), "boundingCircle(default) refuses by emptiness.");
    }

    /// <summary>Two identical circle calls answer bitwise-identical results.</summary>
    [TestMethod]
    public void RepeatedCallsAnswerBitwiseIdentically()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", out FlatGeometry operand, out _),
            "The pentagon multipoint must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle first), "The first call answers.");
        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle second), "The second call answers.");
        Assert.AreEqual(first, second, "Two identical circle calls answer bitwise-identical results.");
    }

    /// <summary>The hull seam owns the degenerate cycle counts the walk keys its collapses on.</summary>
    [TestMethod]
    public void HullSeamOwnsItsDegenerateCounts()
    {
        var hull = new List<Point2d>();

        GeometryConvexHull.ComputeHullVertices(default, hull);
        Assert.HasCount(0, hull, "An empty operand yields the empty cycle.");

        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 2)", out FlatGeometry point, out _), "The point must parse.");
        GeometryConvexHull.ComputeHullVertices(in point, hull);
        Assert.HasCount(1, hull, "A single position yields the one-vertex cycle, never an emptied chain.");
        Assert.AreEqual(new Point2d(1, 2), hull[0], "The single position is itself.");

        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT ((1 1), (1 1))", out FlatGeometry coincident, out _), "The coincident pair must parse.");
        GeometryConvexHull.ComputeHullVertices(in coincident, hull);
        Assert.HasCount(1, hull, "Coincident positions dedup to the one-vertex cycle.");

        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 1 1, 3 3)", out FlatGeometry collinear, out _), "The collinear run must parse.");
        GeometryConvexHull.ComputeHullVertices(in collinear, hull);
        Assert.HasCount(2, hull, "A collinear operand yields its extreme pair.");

        Assert.IsTrue(WktGeometryReader.TryRead("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", out FlatGeometry square, out _), "The square must parse.");
        GeometryConvexHull.ComputeHullVertices(in square, hull);
        Assert.HasCount(4, hull, "The square yields its open four-vertex cycle.");
        Assert.AreEqual(new Point2d(0, 0), hull[0], "The cycle starts at the lexicographic minimum.");
    }

    /// <summary>
    /// Certification is a no-op on every canon row: the walk's answer already covers,
    /// the lift count is zero, and the carriers are pinned bitwise — a bit-level
    /// determinism gate over the whole collapse-topology roster.
    /// </summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedCenterXBits">The pinned center X bit pattern.</param>
    /// <param name="expectedCenterYBits">The pinned center Y bit pattern.</param>
    /// <param name="expectedRadiusBits">The pinned radius bit pattern.</param>
    [TestMethod]
    [DataRow("POINT (1 2)", 4607182418800017408L, 4611686018427387904L, 0L)]
    [DataRow("MULTIPOINT ((1 1), (1 1))", 4607182418800017408L, 4607182418800017408L, 0L)]
    [DataRow("MULTIPOINT ((0 0), (4 0))", 4611686018427387904L, 0L, 4611686018427387904L)]
    [DataRow("LINESTRING (0 0, 1 1, 5 5)", 4612811918334230528L, 4612811918334230528L, 4615143733390674624L)]
    [DataRow("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", 4602678819172646912L, 4602678819172646912L, 4604544271217802189L)]
    [DataRow("POLYGON ((0 0, 10 0, 5 0.3, 0 0))", 4617315517961601024L, 0L, 4617315517961601024L)]
    [DataRow("POLYGON ((0 0, 10 0, 5 0.31, 0 0))", 4617315517961601024L, 0L, 4617315517961601024L)]
    [DataRow("POLYGON ((0 0, 10 0, 5 0.7, 0 0))", 4617315517961601024L, 0L, 4617315517961601024L)]
    [DataRow("POLYGON ((0 0, 10 0, 5 2.4, 0 0))", 4617315517961601024L, 0L, 4617315517961601024L)]
    [DataRow("MULTIPOINT ((-10 0), (0 -1), (10 0), (0 1))", 0L, 0L, 4621819117588971520L)]
    [DataRow("POLYGON ((1 0, 0.5 0.8660254037844386, -0.5 0.8660254037844386, -1 0, -0.5 -0.8660254037844386, 0.5 -0.8660254037844386, 1 0))", 0L, 0L, 4607182418800017408L)]
    [DataRow("POLYGON ((100000000 100000000, 100000001 100000000, 100000001 100000001, 100000000 100000001, 100000000 100000000))", 4726483295917834240L, 4726483295917834240L, 4604544271217802189L)]
    [DataRow("MULTIPOINT ((1 0), (0.9238795325112867 0.3826834323650898), (0.7071067811865476 0.7071067811865476), (0.3826834323650898 0.9238795325112867), (0 1))", 4602678819172646912L, 4602678819172646912L, 4604544271217802189L)]
    [DataRow("MULTIPOINT ((0 0), (10 0), (5 0.000000001))", 4617315517961601024L, 0L, 4617315517961601024L)]
    public void CanonRowCarriersHoldTheirPinnedBitsWithNoLift(string inputText, long expectedCenterXBits, long expectedCenterYBits, long expectedRadiusBits)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle, out _, out int radiusLiftSteps), $"'{inputText}' answers.");
        Assert.AreEqual(0, radiusLiftSteps, $"'{inputText}': the walk's answer already covers, so certification must not lift.");
        Assert.AreEqual(expectedCenterXBits, BitConverter.DoubleToInt64Bits(circle.Center.X), $"'{inputText}': the center X bits are pinned.");
        Assert.AreEqual(expectedCenterYBits, BitConverter.DoubleToInt64Bits(circle.Center.Y), $"'{inputText}': the center Y bits are pinned.");
        Assert.AreEqual(expectedRadiusBits, BitConverter.DoubleToInt64Bits(circle.Radius), $"'{inputText}': the radius bits are pinned.");
    }

    /// <summary>
    /// The two operands whose walk carriers exactly exclude a vertex — the one-bit
    /// coverage shortfall a slack-based oracle cannot see and certification exists to
    /// catch. The pins: the walk's uncertified radius exactly excludes some vertex,
    /// the certified radius sits exactly one bit above it with the center bitwise
    /// unchanged, every vertex is exactly covered afterwards, and the lift fires with
    /// its pinned step count.
    /// </summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="expectedCenterXBits">The pinned center X bit pattern.</param>
    /// <param name="expectedCenterYBits">The pinned center Y bit pattern.</param>
    /// <param name="walkRadiusBits">The walk's uncertified radius bit pattern — the shortfall itself.</param>
    /// <param name="certifiedRadiusBits">The certified radius bit pattern, one bit above the walk's.</param>
    /// <param name="expectedLiftSteps">The pinned lift step count.</param>
    [TestMethod]
    [DataRow("POLYGON ((0 0, 4 0, 1.5 3, 0 0))", 4611686018427387904L, 4606056518893174784L, 4612098167935891880L, 4612098167935891881L, 2, DisplayName = "acute triangle, one bit short")]
    [DataRow("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", 4612361558371493478L, 4609434218613702656L, 4613365649517278684L, 4613365649517278685L, 1, DisplayName = "irregular pentagon, one bit short")]
    public void FiringRowsCertifyOneBitAboveTheWalkRadius(
        string inputText,
        long expectedCenterXBits,
        long expectedCenterYBits,
        long walkRadiusBits,
        long certifiedRadiusBits,
        int expectedLiftSteps)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle, out _, out int radiusLiftSteps), $"'{inputText}' answers.");
        Assert.AreEqual(expectedLiftSteps, radiusLiftSteps, $"'{inputText}': the lift fires with its pinned step count.");
        Assert.AreEqual(expectedCenterXBits, BitConverter.DoubleToInt64Bits(circle.Center.X), $"'{inputText}': the lift never moves the center.");
        Assert.AreEqual(expectedCenterYBits, BitConverter.DoubleToInt64Bits(circle.Center.Y), $"'{inputText}': the lift never moves the center.");
        Assert.AreEqual(certifiedRadiusBits, BitConverter.DoubleToInt64Bits(circle.Radius), $"'{inputText}': the certified radius is pinned.");
        Assert.AreEqual(walkRadiusBits + 1L, certifiedRadiusBits, $"'{inputText}': the certified radius sits exactly one bit above the walk's.");

        double walkRadius = BitConverter.Int64BitsToDouble(walkRadiusBits);
        bool walkRadiusExcludesAVertex = false;

        foreach(Point2d vertex in operand.Vertices)
        {
            Assert.IsLessThan(1, ExactCircleExcess.Sign(vertex, circle.Center, circle.Radius),
                $"'{inputText}': vertex ({vertex.X} {vertex.Y}) is exactly covered by the certified radius.");

            if(ExactCircleExcess.Sign(vertex, circle.Center, walkRadius) > 0)
            {
                walkRadiusExcludesAVertex = true;
            }
        }

        Assert.IsTrue(walkRadiusExcludesAVertex, $"'{inputText}': the walk's own radius exactly excludes some vertex — the shortfall certification lifts over.");
    }

    /// <summary>
    /// The certification routine, driven directly with a hand-shrunk radius: the lift
    /// fires, the center never moves, every vertex is exactly covered afterwards, and
    /// the result is minimal — one bit less no longer covers. Proves the candidate
    /// computation and the certified outcome; the seed property and the step loop get
    /// their own dedicated family gate below.
    /// </summary>
    [TestMethod]
    public void CertifyLiftsAHandShrunkRadiusToTheMinimalCoveringDouble()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", out FlatGeometry operand, out _),
            "The pentagon multipoint must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle covering), "The pentagon answers.");

        var shrunk = new BoundingCircle(covering.Center, Math.BitDecrement(Math.BitDecrement(covering.Radius)));
        BoundingCircle certified = shrunk;
        int liftSteps = GeometryBoundingCircle.Certify(in operand, ref certified);

        Assert.IsGreaterThan(0, liftSteps, "A radius two bits under the covering one must fire the lift.");
        Assert.AreEqual(shrunk.Center, certified.Center, "The lift never moves the center.");

        foreach(Point2d vertex in operand.Vertices)
        {
            Assert.IsLessThan(1, ExactCircleExcess.Sign(vertex, certified.Center, certified.Radius),
                $"Vertex ({vertex.X} {vertex.Y}) is exactly covered after the lift.");
        }

        bool oneBitLessExcludesAVertex = false;

        foreach(Point2d vertex in operand.Vertices)
        {
            if(ExactCircleExcess.Sign(vertex, certified.Center, Math.BitDecrement(certified.Radius)) > 0)
            {
                oneBitLessExcludesAVertex = true;
            }
        }

        Assert.IsTrue(oneBitLessExcludesAVertex, "The certified radius is minimal: one bit less excludes some vertex.");
    }

    /// <summary>
    /// The lift's load-bearing property, pinned directly: the square-root seed NEVER
    /// lands above the minimal covering double — the fold of a nonoverlapping
    /// increasing-magnitude expansion sits within about half a unit of rounding of
    /// the true value, so no downward correction phase can ever be reachable, which
    /// is why none exists in the routine. A deterministic operand/center family with
    /// component-rich squared distances is driven through the certification routine
    /// from a zero radius; every member asserts the seed at or below the certified
    /// radius, exact coverage, and minimality at the center, and the family must
    /// exercise the up-ratchet at least once so the step loop itself is proven live.
    /// </summary>
    [TestMethod]
    public void LiftSeedNeverOvershootsAndTheRatchetLandsOnTheMinimum()
    {
        Span<double> squared = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> maximum = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> negation = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> difference = stackalloc double[2 * ExactCircleExcess.SquaredDistanceComponents];
        Span<double> radiusNegation = stackalloc double[2];
        Span<double> excess = stackalloc double[ExactCircleExcess.ExcessComponents];
        int upExercised = 0;
        double lowQuantum = Math.Pow(2.0, -28);
        double lowerQuantum = Math.Pow(2.0, -47);

        for(int member = 1; member <= 240; member++)
        {
            //Coordinates carry well-separated low bits so the squared distances become
            //component-rich expansions - the fold's sequential rounding can then land
            //the seed on either side of the minimal covering double, which is exactly
            //what this gate needs to see both phases run.
            double ax = member + (((member % 7) + 1) * lowQuantum) + (((member % 3) + 1) * lowerQuantum);
            double ay = (member * 0.5) + (((member % 11) + 1) * lowQuantum);
            double bx = -member + (((member % 5) + 1) * lowerQuantum);
            double by = (member * 2.0) - (((member % 13) + 1) * lowQuantum);
            var center = new Point2d(
                (member % 9) * 0.125 * ((member % 2 == 0) ? 1.0 : -1.0),
                ((member % 17) * lowQuantum) + (((member % 19) + 1) * lowerQuantum));
            string text = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"MULTIPOINT (({ax:R} {ay:R}), ({bx:R} {by:R}))");

            Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry operand, out _), $"Member {member} must parse.");

            int maximumCount = 0;

            foreach(Point2d vertex in operand.Vertices)
            {
                int count = ExactCircleExcess.SquaredDistance(vertex, center, squared);

                if(maximumCount == 0 || ExactCircleExcess.CompareSquaredDistances(squared[..count], maximum[..maximumCount], negation, difference) > 0)
                {
                    squared[..count].CopyTo(maximum);
                    maximumCount = count;
                }
            }

            double approximation = 0.0;

            for(int index = 0; index < maximumCount; index++)
            {
                approximation += maximum[index];
            }

            double seed = Math.Sqrt(approximation);
            var certified = new BoundingCircle(center, 0.0);
            int liftSteps = GeometryBoundingCircle.Certify(in operand, ref certified);

            Assert.IsGreaterThan(0, liftSteps, $"Member {member}: a zero radius always lifts.");
            Assert.AreEqual(center, certified.Center, $"Member {member}: the lift never moves the center.");
            Assert.AreEqual(1, ExactCircleExcess.ExcessSign(Math.BitDecrement(certified.Radius), maximum[..maximumCount], radiusNegation, excess),
                $"Member {member}: one bit below the certified radius no longer covers the farthest vertex.");

            foreach(Point2d vertex in operand.Vertices)
            {
                Assert.IsLessThan(1, ExactCircleExcess.Sign(vertex, certified.Center, certified.Radius),
                    $"Member {member}: vertex exactly covered after the lift.");
            }

            Assert.IsLessThan(Math.BitIncrement(certified.Radius), seed,
                $"Member {member}: the seed never lands above the minimal covering double — the property that makes a downward phase dead code.");

            if(seed < certified.Radius)
            {
                upExercised++;
            }
        }

        Assert.IsGreaterThan(0, upExercised, "The family exercises the up-ratchet: some seed lands below the minimal covering double.");
    }

    /// <summary>
    /// The certification routine allocates nothing: the scan, the running maximum, and
    /// the comparisons are stackalloc spans and scalars, asserted with the exact
    /// thread-allocation counter over a warmed drive.
    /// </summary>
    [TestMethod]
    public void CertifyAllocatesNothingSteadyState()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", out FlatGeometry operand, out _),
            "The pentagon multipoint must parse.");

        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), "The pentagon answers.");

        BoundingCircle scratch = circle;

        for(int warm = 0; warm < 100; warm++)
        {
            GeometryBoundingCircle.Certify(in operand, ref scratch);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for(int index = 0; index < 1000; index++)
        {
            GeometryBoundingCircle.Certify(in operand, ref scratch);
        }

        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0L, delta, "The certification pass must not allocate; any byte is a regression in the scan, the comparisons, or the predicate.");
    }

    /// <summary>
    /// The whole kernel's per-call allocation stays at the walk's own fixed baseline
    /// over this operand — the certification pass adds zero incremental bytes on top
    /// of the walk's hull and survivor lists.
    /// </summary>
    [TestMethod]
    public void TryComputeAllocationStaysAtTheWalkBaseline()
    {
        Assert.IsTrue(
            WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", out FlatGeometry operand, out _),
            "The pentagon multipoint must parse.");

        for(int warm = 0; warm < 100; warm++)
        {
            GeometryBoundingCircle.TryCompute(in operand, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for(int index = 0; index < 1000; index++)
        {
            GeometryBoundingCircle.TryCompute(in operand, out _);
        }

        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsLessThan(488_001L, delta, "Total allocation over the thousand calls must not exceed the walk's 488-byte-per-call bound; certification adds nothing.");
    }

    /// <summary>
    /// The answer-independent gates: containment on the exact excess sign (every
    /// operand vertex inside-or-on, no slack) and minimality (the boundary support is
    /// a zero-radius point, a diametral pair, or a center-surrounding set — the check
    /// that catches every too-large answer containment waves through). Slack remains
    /// only for the touching/minimality classification below.
    /// </summary>
    /// <param name="operand">The operand the circle was computed from.</param>
    /// <param name="circle">The answered circle.</param>
    /// <param name="label">The row label named in failures.</param>
    private static void AssertContainmentAndMinimality(in FlatGeometry operand, BoundingCircle circle, string label)
    {
        double slack = (circle.Radius * 1e-9) + 1e-12;

        foreach(Point2d vertex in operand.Vertices)
        {
            Assert.IsLessThan(1, ExactCircleExcess.Sign(vertex, circle.Center, circle.Radius),
                $"'{label}': vertex ({vertex.X} {vertex.Y}) rides inside-or-on the circle, exactly.");
        }

        if(circle.Radius == 0)
        {
            return;
        }

        var touching = new List<Point2d>();

        foreach(Point2d vertex in operand.Vertices)
        {
            double distance = double.Hypot(vertex.X - circle.Center.X, vertex.Y - circle.Center.Y);

            if(Math.Abs(distance - circle.Radius) <= slack)
            {
                touching.Add(vertex);
            }
        }

        for(int first = 0; first < touching.Count; first++)
        {
            for(int second = first + 1; second < touching.Count; second++)
            {
                double separation = double.Hypot(touching[second].X - touching[first].X, touching[second].Y - touching[first].Y);

                if(separation >= (2.0 * circle.Radius) - (2.0 * slack))
                {
                    return;
                }
            }
        }

        Assert.IsGreaterThanOrEqualTo(3, touching.Count, $"'{label}': without a diametral pair the support needs three boundary points.");

        var angles = new List<double>(touching.Count);

        foreach(Point2d vertex in touching)
        {
            angles.Add(Math.Atan2(vertex.Y - circle.Center.Y, vertex.X - circle.Center.X));
        }

        angles.Sort();

        double largestGap = (angles[0] + Math.Tau) - angles[^1];

        for(int index = 1; index < angles.Count; index++)
        {
            largestGap = Math.Max(largestGap, angles[index] - angles[index - 1]);
        }

        Assert.IsLessThanOrEqualTo(
            Math.PI * (1.0 + 1e-9),
            largestGap,
            $"'{label}': the boundary support surrounds the center — no half-plane holds it all.");
    }
}
