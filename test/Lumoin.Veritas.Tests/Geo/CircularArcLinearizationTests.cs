using System;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The arc linearization kernel pinned directly, below the codec: the certified
/// circle solve, the exact per-emission checks re-verified through the same exact
/// predicates, the published constants, the seed verbatim guarantees, the split
/// machinery with its membership invariant, the wall and drift refusals with their
/// outcomes and offending indexes, and bit-for-bit determinism. Refusal rows assert
/// the outcome AND the offending seed index, and nothing random participates — every
/// row is deterministic.
/// </summary>
[TestClass]
internal sealed class CircularArcLinearizationTests
{
    /// <summary>The unit circle's expected vertex count: one east seed plus four gaps of two hundred and fifty-six chords each.</summary>
    private const int UnitCircleVertexCount = 1025;

    /// <summary>The right half-circle arc's expected vertex count: two quarter gaps of two hundred and fifty-six chords each, half-open.</summary>
    private const int HalfArcVertexCount = 512;

    /// <summary>The three-hundred-and-fifty-degree arc's expected vertex count: a two-hundred-degree gap at five hundred and twelve chords plus a fifty-degree gap at one hundred and twenty-eight, half-open.</summary>
    private const int MajorArcVertexCount = 640;

    /// <summary>The near-full-turn arc's expected vertex count: one sliver gap at a single chord plus a nearly whole-turn gap at one thousand and twenty-four chords, half-open.</summary>
    private const int NearFullTurnVertexCount = 1025;

    /// <summary>Materializes a builder's appended vertex run as a LineString for span inspection.</summary>
    private static FlatGeometry Materialize(FlatGeometryBuilder builder)
    {
        builder.AddPart(new FlatGeometryPart(0, builder.VertexCount, FlatGeometryPartRole.Line));
        builder.RootIndex = builder.AddNode(GeometryKind.LineString, hasZ: false, hasM: false, firstPart: 0, partCount: 1);

        return builder.ToGeometry();
    }

    /// <summary>
    /// The unit circle linearized from its center and radius matches the pinned
    /// polyline bit for bit, element-wise over every vertex.
    /// </summary>
    [TestMethod]
    public void TheUnitCirclePolylineMatchesThePinnedBits()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(0.0, 0.0), 1.0, builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsTrue(certified, "the unit circle must certify");
        Assert.AreEqual(CircularArcLinearizationOutcome.Certified, outcome, "the outcome must be the success value");
        Assert.AreEqual(-1, offendingSeedIndex, "a certified run names no offending seed");

        using FlatGeometry circle = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = circle.Vertices;
        long[] expected = CircularArcLinearizationFixtures.UnitCircleVertexBits;
        int vertexCount = vertices.Length;
        Assert.AreEqual(UnitCircleVertexCount, vertexCount, "the subdivision count is pinned");
        Assert.HasCount(vertexCount * 2, expected, "the fixture carries one X and one Y pattern per vertex");

        for(int index = 0; index < vertices.Length; index++)
        {
            Assert.AreEqual(expected[2 * index], BitConverter.DoubleToInt64Bits(vertices[index].X), $"vertex {index} X must match the pinned bits");
            Assert.AreEqual(expected[(2 * index) + 1], BitConverter.DoubleToInt64Bits(vertices[index].Y), $"vertex {index} Y must match the pinned bits");
        }
    }

    /// <summary>
    /// The cardinal seeds emit in the canonical order — east, north, west, south,
    /// counter-clockwise — at their exact quarter positions, and the east cardinal
    /// closes the ring verbatim.
    /// </summary>
    [TestMethod]
    public void CardinalSeedsEmitInTheCanonicalOrder()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(0.0, 0.0), 1.0, builder, out _, out _);
        Assert.IsTrue(certified, "the unit circle must certify");

        using FlatGeometry circle = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = circle.Vertices;
        Assert.AreEqual(new Point2d(1.0, 0.0), vertices[0], "the east cardinal opens the ring");
        Assert.AreEqual(new Point2d(0.0, 1.0), vertices[256], "the north cardinal sits at the first quarter");
        Assert.AreEqual(new Point2d(-1.0, 0.0), vertices[512], "the west cardinal sits at the half");
        Assert.AreEqual(new Point2d(0.0, -1.0), vertices[768], "the south cardinal sits at the third quarter");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(vertices[0].X), BitConverter.DoubleToInt64Bits(vertices[^1].X), "the closing vertex repeats the opening vertex bit for bit on X");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(vertices[0].Y), BitConverter.DoubleToInt64Bits(vertices[^1].Y), "the closing vertex repeats the opening vertex bit for bit on Y");
    }

    /// <summary>
    /// Every emitted chord clears the exact sagitta check and every emitted vertex
    /// the exact annulus checks, re-verified here through the same exact predicates
    /// against the documented comparison constructions — the per-emission pillar
    /// asserted from outside the kernel.
    /// </summary>
    [TestMethod]
    public void EveryChordAndVertexPassesItsExactCheck()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(0.0, 0.0), 1.0, builder, out _, out _);
        Assert.IsTrue(certified, "the unit circle must certify");

        using FlatGeometry circle = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = circle.Vertices;
        Point2d center = new(0.0, 0.0);
        double comparisonRadius = Math.BitIncrement(1.0 * (1.0 - CircularArcLinearization.MaximumRelativeSagitta));
        double annulusInner = Math.BitDecrement(1.0 * (1.0 - CircularArcLinearization.MaximumRelativeVertexDrift));
        double annulusOuter = Math.BitIncrement(1.0 * (1.0 + CircularArcLinearization.MaximumRelativeVertexDrift));

        for(int index = 0; index < vertices.Length; index++)
        {
            Assert.IsLessThanOrEqualTo(0, ExactCircleExcess.Sign(vertices[index], center, annulusOuter), $"vertex {index} must sit at or inside the outer annulus radius");
            Assert.IsGreaterThanOrEqualTo(0, ExactCircleExcess.Sign(vertices[index], center, annulusInner), $"vertex {index} must sit at or outside the inner annulus radius");
        }

        for(int index = 0; index < vertices.Length - 1; index++)
        {
            Point2d midpoint = new((vertices[index].X + vertices[index + 1].X) / 2.0, (vertices[index].Y + vertices[index + 1].Y) / 2.0);
            Assert.IsGreaterThanOrEqualTo(0, ExactCircleExcess.Sign(midpoint, center, comparisonRadius), $"chord {index} must clear the exact sagitta check");
        }
    }

    /// <summary>
    /// The published constants carry their pinned bit patterns, and their
    /// conservative one-bit adjustments land on the pinned unit-radius comparison
    /// values — the constants and the documented adjustment directions asserted
    /// together, through runtime conversions the compiler cannot fold away.
    /// </summary>
    [TestMethod]
    public void PublishedConstantsCarryTheirPinnedValues()
    {
        Assert.AreEqual(4535124824762089472L, BitConverter.DoubleToInt64Bits(CircularArcLinearization.MaximumRelativeSagitta), "the sagitta bound is two to the negative sixteenth");
        Assert.AreEqual(4517110426252607488L, BitConverter.DoubleToInt64Bits(CircularArcLinearization.MaximumRelativeVertexDrift), "the drift band is two to the negative twentieth");
        Assert.AreEqual(2811557277646183863L, BitConverter.DoubleToInt64Bits(CircularArcLinearization.MinimumMagnitude), "the lower wall is pinned");
        Assert.AreEqual(6850974717710472879L, BitConverter.DoubleToInt64Bits(CircularArcLinearization.MaximumMagnitude), "the upper wall is pinned");

        int bisectionDepth = CircularArcLinearization.MaximumBisectionDepth;
        Assert.AreEqual(16, bisectionDepth, "the bisection depth cap is sixteen");
        Assert.AreEqual(4607182281361063937L, BitConverter.DoubleToInt64Bits(Math.BitIncrement(1.0 - CircularArcLinearization.MaximumRelativeSagitta)), "the unit-radius comparison radius rounds one bit upward");
        Assert.AreEqual(4607182410210082815L, BitConverter.DoubleToInt64Bits(Math.BitDecrement(1.0 - CircularArcLinearization.MaximumRelativeVertexDrift)), "the unit-radius inner annulus radius rounds one bit downward");
        Assert.AreEqual(4607182423094984705L, BitConverter.DoubleToInt64Bits(Math.BitIncrement(1.0 + CircularArcLinearization.MaximumRelativeVertexDrift)), "the unit-radius outer annulus radius rounds one bit upward");
    }

    /// <summary>
    /// A three-point arc's control points enter the output verbatim: the middle and
    /// end seeds sit at their exact positions in the half-open run, bit-preserved,
    /// with the count pinned and an interior vertex bit-pinned at the anchor commit.
    /// </summary>
    [TestMethod]
    public void ArcControlPointsEnterTheOutputVerbatim()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(new Point2d(0.0, -1.0), new Point2d(1.0, 0.0), new Point2d(0.0, 1.0), builder, out _, out _);
        Assert.IsTrue(certified, "the right half-circle arc must certify");

        using FlatGeometry arc = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = arc.Vertices;
        int vertexCount = vertices.Length;
        Assert.AreEqual(HalfArcVertexCount, vertexCount, "the half-open count is pinned");
        Assert.AreEqual(new Point2d(1.0, 0.0), vertices[255], "the middle control point ends the first gap verbatim");
        Assert.AreEqual(new Point2d(0.0, 1.0), vertices[511], "the end control point closes the run verbatim");
        Assert.AreEqual(4607182249242036883L, BitConverter.DoubleToInt64Bits(vertices[256].X), "the first interior vertex after the middle seed is bit-pinned on X");
        Assert.AreEqual(4573724215515480178L, BitConverter.DoubleToInt64Bits(vertices[256].Y), "the first interior vertex after the middle seed is bit-pinned on Y");
    }

    /// <summary>
    /// A three-hundred-and-fifty-degree arc — one seed gap beyond a half turn —
    /// certifies through the major-arc split without wrapping: the count is pinned,
    /// the end seed closes the run verbatim, and the certified center stays on the
    /// travel side of every emitted chord.
    /// </summary>
    [TestMethod]
    public void TheMajorArcCertifiesWithoutWrapping()
    {
        Point2d start = new(1.0, 0.0);
        Point2d middle = new(-0.9396926207859084, -0.3420201433256687);
        Point2d end = new(-0.3420201433256688, -0.9396926207859083);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(start, middle, end, builder, out CircularArcLinearizationOutcome outcome, out _);
        Assert.IsTrue(certified, "the major arc must certify");
        Assert.AreEqual(CircularArcLinearizationOutcome.Certified, outcome, "the outcome must be the success value");

        using FlatGeometry arc = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = arc.Vertices;
        int vertexCount = vertices.Length;
        Assert.AreEqual(MajorArcVertexCount, vertexCount, "the pinned count forbids a wrapped double cover");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(end.X), BitConverter.DoubleToInt64Bits(vertices[^1].X), "the end control point closes the run verbatim on X");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(end.Y), BitConverter.DoubleToInt64Bits(vertices[^1].Y), "the end control point closes the run verbatim on Y");

        int travel = ExactOrientation.Orient2D(start, middle, end);
        Point2d center = CircumcenterOf(start, middle, end);
        Point2d previous = start;

        for(int index = 0; index < vertices.Length; index++)
        {
            Assert.AreEqual(travel, ExactOrientation.Orient2D(previous, vertices[index], center), $"chord {index} must keep the certified center on the travel side — no backtracking, no wrap");
            previous = vertices[index];
        }
    }

    /// <summary>
    /// An arc whose middle control point sits a sliver behind its start, leaving one
    /// seed gap spanning nearly the whole turn: the gap's own chord midpoint clears
    /// the sagitta comparison but keeps the center on the wrong side, so only the
    /// exact minor-side gate forces the subdivision — the run certifies with the
    /// count pinned far beyond the half-arc count, and every emitted chord keeps the
    /// certified center on the travel side.
    /// </summary>
    [TestMethod]
    public void TheNearFullTurnGapSubdividesOnTheTravelSide()
    {
        Point2d start = new(1.0, 0.0);
        Point2d middle = new(0.9999995000000417, -0.0009999998333333417);
        Point2d end = new(0.9999980000006666, 0.0019999986666669333);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(start, middle, end, builder, out CircularArcLinearizationOutcome outcome, out _);
        Assert.IsTrue(certified, "the near-full-turn arc must certify");
        Assert.AreEqual(CircularArcLinearizationOutcome.Certified, outcome, "the outcome must be the success value");

        using FlatGeometry arc = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = arc.Vertices;
        int vertexCount = vertices.Length;
        Assert.AreEqual(NearFullTurnVertexCount, vertexCount, "the pinned count covers the nearly whole turn");
        Assert.IsGreaterThan(HalfArcVertexCount, vertexCount, "the wide gap subdivides far beyond the half-arc count instead of collapsing to its chord");
        Assert.AreEqual(middle, vertices[0], "the middle seed opens the run verbatim");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(end.X), BitConverter.DoubleToInt64Bits(vertices[^1].X), "the end control point closes the run verbatim on X");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(end.Y), BitConverter.DoubleToInt64Bits(vertices[^1].Y), "the end control point closes the run verbatim on Y");

        int travel = ExactOrientation.Orient2D(start, middle, end);
        Point2d center = CircumcenterOf(start, middle, end);
        double radius = Math.Sqrt(((middle.X - center.X) * (middle.X - center.X)) + ((middle.Y - center.Y) * (middle.Y - center.Y)));
        double comparisonRadius = Math.BitIncrement(radius * (1.0 - CircularArcLinearization.MaximumRelativeSagitta));
        Point2d wideMidpoint = new((middle.X + end.X) / 2.0, (middle.Y + end.Y) / 2.0);
        Assert.AreNotEqual(travel, ExactOrientation.Orient2D(middle, end, center), "the wide gap's chord keeps the center on the wrong side");
        Assert.IsGreaterThanOrEqualTo(0, ExactCircleExcess.Sign(wideMidpoint, center, comparisonRadius), "the wide gap's chord midpoint clears the sagitta comparison, so the minor-side gate alone forces the subdivision");

        Point2d previous = start;

        for(int index = 0; index < vertices.Length; index++)
        {
            Assert.AreEqual(travel, ExactOrientation.Orient2D(previous, vertices[index], center), $"chord {index} must keep the certified center on the travel side — the wide gap never ships as one chord");
            previous = vertices[index];
        }
    }

    /// <summary>
    /// The documented anchored circumcenter construction, repeated here so the
    /// family can reason about the certified circle from outside the kernel: every
    /// input a difference from the middle control point, one square-root-free solve.
    /// </summary>
    private static Point2d CircumcenterOf(Point2d first, Point2d second, Point2d third)
    {
        double towardFirstX = first.X - second.X;
        double towardFirstY = first.Y - second.Y;
        double towardThirdX = third.X - second.X;
        double towardThirdY = third.Y - second.Y;
        double towardFirstSquared = (towardFirstX * towardFirstX) + (towardFirstY * towardFirstY);
        double towardThirdSquared = (towardThirdX * towardThirdX) + (towardThirdY * towardThirdY);
        double cross = (towardFirstX * towardThirdY) - (towardFirstY * towardThirdX);
        double offsetX = ((towardThirdY * towardFirstSquared) - (towardFirstY * towardThirdSquared)) / (2.0 * cross);
        double offsetY = ((towardFirstX * towardThirdSquared) - (towardThirdX * towardFirstSquared)) / (2.0 * cross);

        return new Point2d(second.X + offsetX, second.Y + offsetY);
    }

    /// <summary>
    /// Degenerate control points refuse with their outcome and offending index, and
    /// nothing is emitted before the offense.
    /// </summary>
    [TestMethod]
    [DataRow(0.0, 0.0, 0.0, 0.0, 2.0, 0.0, (int)CircularArcLinearizationOutcome.CoincidentControlPoints, 1, DisplayName = "a start coinciding with the middle refuses at the middle")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 2.0, 0.0, (int)CircularArcLinearizationOutcome.CoincidentControlPoints, 2, DisplayName = "a middle coinciding with the end refuses at the end")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 0.0, 0.0, (int)CircularArcLinearizationOutcome.CoincidentControlPoints, 2, DisplayName = "an end coinciding with the start refuses at the end")]
    [DataRow(0.0, 0.0, 1.0, 1.0, 2.0, 2.0, (int)CircularArcLinearizationOutcome.CollinearControlPoints, 2, DisplayName = "exactly collinear control points refuse at the third")]
    public void DegenerateControlPointsRefuseWithTheirIndexes(double startX, double startY, double middleX, double middleY, double endX, double endY, int expectedOutcome, int expectedSeedIndex)
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(new Point2d(startX, startY), new Point2d(middleX, middleY), new Point2d(endX, endY), builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "a degenerate triple must refuse");
        Assert.AreEqual((CircularArcLinearizationOutcome)expectedOutcome, outcome, "the outcome names the degeneracy");
        Assert.AreEqual(expectedSeedIndex, offendingSeedIndex, "the offending control point is named");
        Assert.AreEqual(0, builder.VertexCount, "nothing is emitted before the offense");
    }

    /// <summary>
    /// The circle path shares the degeneracy refusals: coincident and exactly
    /// collinear control points refuse with their outcome and offending index, and
    /// nothing is emitted before the offense.
    /// </summary>
    [TestMethod]
    [DataRow(0.0, 0.0, 0.0, 0.0, 2.0, 0.0, (int)CircularArcLinearizationOutcome.CoincidentControlPoints, 1, DisplayName = "a first point coinciding with the second refuses at the second")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 2.0, 0.0, (int)CircularArcLinearizationOutcome.CoincidentControlPoints, 2, DisplayName = "a second point coinciding with the third refuses at the third")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 0.0, 0.0, (int)CircularArcLinearizationOutcome.CoincidentControlPoints, 2, DisplayName = "a third point coinciding with the first refuses at the third")]
    [DataRow(0.0, 0.0, 1.0, 1.0, 2.0, 2.0, (int)CircularArcLinearizationOutcome.CollinearControlPoints, 2, DisplayName = "exactly collinear circle control points refuse at the third")]
    public void DegenerateCircleControlPointsRefuseWithTheirIndexes(double firstX, double firstY, double secondX, double secondY, double thirdX, double thirdY, int expectedOutcome, int expectedSeedIndex)
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeCircle(new Point2d(firstX, firstY), new Point2d(secondX, secondY), new Point2d(thirdX, thirdY), builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "a degenerate triple must refuse on the circle path too");
        Assert.AreEqual((CircularArcLinearizationOutcome)expectedOutcome, outcome, "the outcome names the degeneracy");
        Assert.AreEqual(expectedSeedIndex, offendingSeedIndex, "the offending control point is named");
        Assert.AreEqual(0, builder.VertexCount, "nothing is emitted before the offense");
    }

    /// <summary>
    /// The magnitude walls refuse input values and computed values alike, in
    /// acceptance form — a value that is not a number fails them too.
    /// </summary>
    [TestMethod]
    public void MagnitudeWallsRefuseInputAndComputedValues()
    {
        FlatGeometryBuilder inputBuilder = new();
        bool inputCertified = CircularArcLinearization.TryLinearizeArc(new Point2d(1e200, 0.0), new Point2d(1.0, 0.0), new Point2d(0.0, 1.0), inputBuilder, out CircularArcLinearizationOutcome inputOutcome, out int inputSeed);
        Assert.IsFalse(inputCertified, "an over-wall input ordinate must refuse");
        Assert.AreEqual(CircularArcLinearizationOutcome.MagnitudeWall, inputOutcome, "the wall outcome is named");
        Assert.AreEqual(0, inputSeed, "the offending control point is the first");

        FlatGeometryBuilder nanBuilder = new();
        bool nanCertified = CircularArcLinearization.TryLinearizeArc(new Point2d(0.0, -1.0), new Point2d(double.NaN, 0.0), new Point2d(0.0, 1.0), nanBuilder, out CircularArcLinearizationOutcome nanOutcome, out int nanSeed);
        Assert.IsFalse(nanCertified, "an ordinate that is not a number must refuse at the wall");
        Assert.AreEqual(CircularArcLinearizationOutcome.MagnitudeWall, nanOutcome, "the acceptance-form wall catches the value");
        Assert.AreEqual(1, nanSeed, "the offending control point is the middle");

        FlatGeometryBuilder computedBuilder = new();
        bool computedCertified = CircularArcLinearization.TryLinearizeArc(new Point2d(0.0, 0.0), new Point2d(1e120, 1e120), new Point2d(2e120, 0.0), computedBuilder, out CircularArcLinearizationOutcome computedOutcome, out int computedSeed);
        Assert.IsFalse(computedCertified, "a solve whose numerator overflows must refuse at the computed wall");
        Assert.AreEqual(CircularArcLinearizationOutcome.MagnitudeWall, computedOutcome, "the wall outcome covers computed values");
        Assert.AreEqual(-1, computedSeed, "a computed-value wall names no seed");

        FlatGeometryBuilder zeroBuilder = new();
        bool zeroCertified = CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(0.0, 0.0), 0.0, zeroBuilder, out CircularArcLinearizationOutcome zeroOutcome, out int zeroSeed);
        Assert.IsFalse(zeroCertified, "a zero radius is degenerate whatever produced it");
        Assert.AreEqual(CircularArcLinearizationOutcome.MagnitudeWall, zeroOutcome, "the radius wall refuses zero");
        Assert.AreEqual(0, zeroSeed, "the center-and-radius seed is named");
    }

    /// <summary>
    /// An ordinate that is nonzero yet below the lower magnitude wall refuses at the
    /// wall with its offending control point named, and nothing is emitted — the
    /// sub-wall arm of the acceptance-form test, seed by seed.
    /// </summary>
    [TestMethod]
    [DataRow(1e-125, -1.0, 1.0, 0.0, 0.0, 1.0, 0, DisplayName = "a sub-wall start ordinate refuses at the first seed")]
    [DataRow(0.0, -1.0, 1.0, 1e-125, 0.0, 1.0, 1, DisplayName = "a sub-wall middle ordinate refuses at the middle seed")]
    [DataRow(0.0, -1.0, 1.0, 0.0, 1e-125, 1.0, 2, DisplayName = "a sub-wall end ordinate refuses at the end seed")]
    public void SubWallOrdinatesRefuseAtTheirSeeds(double startX, double startY, double middleX, double middleY, double endX, double endY, int expectedSeedIndex)
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(new Point2d(startX, startY), new Point2d(middleX, middleY), new Point2d(endX, endY), builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "a sub-wall ordinate must refuse");
        Assert.AreEqual(CircularArcLinearizationOutcome.MagnitudeWall, outcome, "the wall outcome covers the tiny magnitudes");
        Assert.AreEqual(expectedSeedIndex, offendingSeedIndex, "the offending control point is named");
        Assert.AreEqual(0, builder.VertexCount, "nothing is emitted before the offense");
    }

    /// <summary>
    /// A circle whose radius is small against its center's coordinate grid cannot
    /// host vertices inside the drift band: the first cardinal fails its exact
    /// annulus check and the run refuses with nothing emitted.
    /// </summary>
    [TestMethod]
    public void TheOffsetGridCircleRefusesVertexDrift()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(20000000.0, 6000000.0), 2.4e-4, builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "a sub-grid radius at web-mercator magnitudes must refuse");
        Assert.AreEqual(CircularArcLinearizationOutcome.VertexDrift, outcome, "the annulus check names the refusal");
        Assert.AreEqual(0, offendingSeedIndex, "the first cardinal seed is named");
        Assert.AreEqual(0, builder.VertexCount, "nothing is emitted before the offense");
    }

    /// <summary>
    /// A tiny arc riding a coarse coordinate grid exposes the mis-solved circle
    /// through the document's own points: the anchored solve's final center addition
    /// rounds to the grid, displacing the certified center by a fraction of the
    /// radius far beyond the drift band, and the first seed fails its exact annulus
    /// check with nothing emitted. The control points are the shortest decimal
    /// spellings of exact doubles near ten to the eighth whose spread is a couple of
    /// hundred grid steps.
    /// </summary>
    [TestMethod]
    public void TheCoarseGridArcRefusesSeedDrift()
    {
        Point2d start = new(100000000.00000095, 100000000.0000003);
        Point2d middle = new(100000000.0000009, 100000000.00000043);
        Point2d end = new(100000000.00000082, 100000000.00000057);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(start, middle, end, builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "the mis-solved circle must refuse through its own seeds");
        Assert.AreEqual(CircularArcLinearizationOutcome.VertexDrift, outcome, "the annulus check names the refusal");
        Assert.AreEqual(0, offendingSeedIndex, "the document's first seed is named");
        Assert.AreEqual(0, builder.VertexCount, "nothing is emitted before the offense");
    }

    /// <summary>
    /// The constructed-vertex twin of the coarse-grid refusal: a unit radius at a
    /// center of two to the fortieth keeps every cardinal seed exactly on the circle
    /// — the offsets are exact additions — so the seeds all pass, and the first
    /// constructed split vertex is the one the grid cannot host inside the drift
    /// band: the run refuses with only the east seed emitted and no seed named.
    /// </summary>
    [TestMethod]
    public void TheCoarseGridSplitRefusesConstructedVertexDrift()
    {
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(1099511627776.0, 0.0), 1.0, builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex);
        Assert.IsFalse(certified, "a unit radius on the two-to-the-fortieth grid must refuse at the first split");
        Assert.AreEqual(CircularArcLinearizationOutcome.VertexDrift, outcome, "the annulus check names the refusal");
        Assert.AreEqual(-1, offendingSeedIndex, "a constructed vertex names no seed");
        Assert.AreEqual(1, builder.VertexCount, "the exact cardinal seeds pass, so only the east seed is emitted before the offense");
    }

    /// <summary>
    /// A near-collinear triple that passes the exact collinearity check certifies as
    /// a giant circle whose chords clear immediately: the output is exactly the two
    /// remaining seeds, verbatim — the published bound is relative to the certified
    /// radius, and this is the honest, recorded consequence.
    /// </summary>
    [TestMethod]
    public void TheGiantNearCollinearArcCertifiesAsItsSeeds()
    {
        Point2d middle = new(1.0, 1e-100);
        Point2d end = new(2.0, 0.0);
        FlatGeometryBuilder builder = new();
        bool certified = CircularArcLinearization.TryLinearizeArc(new Point2d(0.0, 0.0), middle, end, builder, out CircularArcLinearizationOutcome outcome, out _);
        Assert.IsTrue(certified, "the giant circle must certify");
        Assert.AreEqual(CircularArcLinearizationOutcome.Certified, outcome, "the outcome must be the success value");

        using FlatGeometry arc = Materialize(builder);
        ReadOnlySpan<Point2d> vertices = arc.Vertices;
        int vertexCount = vertices.Length;
        Assert.AreEqual(2, vertexCount, "both gaps clear at once, emitting only the far seeds");
        Assert.AreEqual(middle, vertices[0], "the middle seed is verbatim");
        Assert.AreEqual(end, vertices[1], "the end seed is verbatim");
    }

    /// <summary>
    /// The split construction keys the diametral case on the exact side test and
    /// pins the perpendicular: with the chord through the center the midpoint
    /// direction is unusable, the first perpendicular sign fails the exact
    /// membership check, and the second lands the split on the gap's own sub-arc —
    /// both travel directions distinguished, and the off-center chord-through-center
    /// case resolved identically.
    /// </summary>
    [TestMethod]
    public void TheDiametralSplitKeysOnTheSideTestAndPinsThePerpendicular()
    {
        bool counterClockwise = CircularArcLinearization.TryConstructSplit(new Point2d(1.0, 0.0), new Point2d(-1.0, 0.0), 0, new Point2d(0.0, 0.0), 1.0, 1, out Point2d counterSplit);
        Assert.IsTrue(counterClockwise, "the diametral gap must split");
        Assert.AreEqual(new Point2d(0.0, 1.0), counterSplit, "counter-clockwise travel splits through the upper half");

        bool clockwise = CircularArcLinearization.TryConstructSplit(new Point2d(1.0, 0.0), new Point2d(-1.0, 0.0), 0, new Point2d(0.0, 0.0), 1.0, -1, out Point2d clockwiseSplit);
        Assert.IsTrue(clockwise, "the diametral gap must split under clockwise travel too");
        Assert.AreEqual(new Point2d(0.0, -1.0), clockwiseSplit, "clockwise travel splits through the lower half");

        bool offCenter = CircularArcLinearization.TryConstructSplit(new Point2d(1.0, 0.0), new Point2d(-1.0, 0.0), 0, new Point2d(0.25, 0.0), 1.0, 1, out Point2d offCenterSplit);
        Assert.IsTrue(offCenter, "a chord through the center line with an off-chord-midpoint center must split");
        Assert.AreEqual(new Point2d(0.25, 1.0), offCenterSplit, "the split sits on the certified circle above the chord");
    }

    /// <summary>
    /// A minor gap splits at the midpoint direction and the split passes the exact
    /// membership check on the gap's own sub-arc.
    /// </summary>
    [TestMethod]
    public void TheMinorSplitTakesTheMidpointDirection()
    {
        int side = ExactOrientation.Orient2D(new Point2d(1.0, 0.0), new Point2d(0.0, 1.0), new Point2d(0.0, 0.0));
        bool split = CircularArcLinearization.TryConstructSplit(new Point2d(1.0, 0.0), new Point2d(0.0, 1.0), side, new Point2d(0.0, 0.0), 1.0, 1, out Point2d vertex);
        Assert.IsTrue(split, "the quarter gap must split");
        Assert.AreEqual(-1, ExactOrientation.Orient2D(new Point2d(1.0, 0.0), new Point2d(0.0, 1.0), vertex), "the split vertex lies on the gap's own sub-arc");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(vertex.X), BitConverter.DoubleToInt64Bits(vertex.Y), "the quarter split is symmetric, so its ordinates carry identical bits");
    }

    /// <summary>Two runs over the same input emit bit-identical polylines — the arithmetic is correctly rounded only, so determinism is a theorem, and this row is its witness.</summary>
    [TestMethod]
    public void TwinRunsEmitIdenticalBits()
    {
        FlatGeometryBuilder firstBuilder = new();
        FlatGeometryBuilder secondBuilder = new();
        Assert.IsTrue(CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(3.5, -2.25), 7.75, firstBuilder, out _, out _), "the first run must certify");
        Assert.IsTrue(CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(3.5, -2.25), 7.75, secondBuilder, out _, out _), "the second run must certify");

        using FlatGeometry first = Materialize(firstBuilder);
        using FlatGeometry second = Materialize(secondBuilder);
        int firstCount = first.Vertices.Length;
        int secondCount = second.Vertices.Length;
        Assert.AreEqual(firstCount, secondCount, "the runs agree on the vertex count");

        for(int index = 0; index < first.Vertices.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first.Vertices[index].X), BitConverter.DoubleToInt64Bits(second.Vertices[index].X), $"vertex {index} X must agree bit for bit");
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first.Vertices[index].Y), BitConverter.DoubleToInt64Bits(second.Vertices[index].Y), $"vertex {index} Y must agree bit for bit");
        }
    }
}
