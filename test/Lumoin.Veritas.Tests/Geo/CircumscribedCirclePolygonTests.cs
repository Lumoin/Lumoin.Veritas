using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The certified circumscription surface: verified coverage over the canon operand roster with
/// the ratchet's step count bounded, the published overshoot bound held through exact excess
/// signs (no slack oracle anywhere), same-process repeat-call bit purity, the verification
/// gates driven directly through rigged rings (globally shrunk, single-vertex-pulled,
/// stride-reordered, malformed, non-finite), and the refusal walls (the exactness radius wall,
/// the subnormal radius, and the circumradius unresolvable at the center's magnitude, which
/// exhausts the ratchet ceiling). Coordinate assertions go through exact predicates or the
/// published-bound circle only — ring vertices are trigonometric samples and are never
/// bit-pinned.
/// </summary>
[TestClass]
internal sealed class CircumscribedCirclePolygonTests
{
    /// <summary>The tessellation the rendering seam uses: eight segments per quadrant.</summary>
    private const int QuadrantSegments = 8;

    /// <summary>
    /// The ratchet's contractual step bound over well-conditioned operands: the seed sits within
    /// a couple of ulps of the exact quotient and per-vertex rounding costs a couple more, so a
    /// covering emission needs a single-digit lift count.
    /// </summary>
    private const int LiftStepBound = 4;

    /// <summary>
    /// The published-bound circle's radius factor: strictly above the exact overshoot bound
    /// (the secant of half the tessellation step, about 1.00484 at thirty-two vertices) by five
    /// orders of magnitude more margin than any vertex rounding, so every legitimately emitted
    /// vertex sits strictly inside it on any machine.
    /// </summary>
    private const double PublishedBoundFactor = 1.0049;

    /// <summary>
    /// Every canon operand renders a verified covering: the ratchet stays within its
    /// contractual bound, the emitted ring re-verifies, and every ring vertex sits on-or-outside
    /// the certified circle and strictly inside the published-bound circle — both through the
    /// exact excess sign, no tolerance anywhere.
    /// </summary>
    /// <param name="inputText">The WKT operand.</param>
    [TestMethod]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", DisplayName = "reference square")]
    [DataRow("MULTIPOINT ((0 0), (1 2))", DisplayName = "two-point diametral")]
    [DataRow("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", DisplayName = "irregular pentagon")]
    [DataRow("LINESTRING (0 0, 1 1, 5 5)", DisplayName = "collinear run")]
    [DataRow("POLYGON ((100000000 100000000, 100000001 100000000, 100000001 100000001, 100000000 100000001, 100000000 100000000))", DisplayName = "offset square at 1e8")]
    [DataRow("MULTIPOINT ((0 0), (0.001 0))", DisplayName = "small-radius pair")]
    public void CanonOperandsRenderVerifiedCoverings(string inputText)
    {
        BoundingCircle circle = RenderVerified(inputText, out Point2d[] ring, out int liftSteps);

        Assert.IsLessThanOrEqualTo(LiftStepBound, liftSteps, $"'{inputText}': the ratchet stays within its contractual bound.");
        AssertRingWithinThePublishedBound(ring, circle, inputText);
    }

    /// <summary>
    /// A large-magnitude operand inside the seam's ordinate wall still renders a verified
    /// covering, and the kernel completes normally for it — the row that proves the wall is not
    /// over-broad and isolates the rendering layer from any upstream refusal.
    /// </summary>
    [TestMethod]
    public void LargeMagnitudeOperandInsideTheWallRendersAVerifiedCovering()
    {
        string ordinate = "1" + new string('0', 74);
        string inputText = $"MULTIPOINT ((-{ordinate} 0), ({ordinate} 0))";

        BoundingCircle circle = RenderVerified(inputText, out Point2d[] ring, out int liftSteps);

        Assert.IsLessThanOrEqualTo(LiftStepBound, liftSteps, "The ratchet stays within its contractual bound at 1e74.");
        AssertRingWithinThePublishedBound(ring, circle, "1e74 pair");
    }

    /// <summary>
    /// Two renders of one input answer bit-identical rings and identical lift counts — the
    /// same-process repeat-call purity gate. Cross-machine bit identity is not claimed here:
    /// the non-cardinal vertices are trigonometric samples outside the pure-arithmetic
    /// determinism datum.
    /// </summary>
    [TestMethod]
    public void RepeatedRendersAnswerBitIdenticalRingsInProcess()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("MULTIPOINT ((0 0), (4 0), (5 2), (2 4), (0 3))", out FlatGeometry operand, out _), "The pentagon must parse.");
        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), "The pentagon answers a circle.");

        Assert.IsTrue(CircumscribedCirclePolygon.TryRender(circle, QuadrantSegments, out Point2d[] first, out int firstLifts), "The first render verifies.");
        Assert.IsTrue(CircumscribedCirclePolygon.TryRender(circle, QuadrantSegments, out Point2d[] second, out int secondLifts), "The second render verifies.");

        Assert.AreEqual(firstLifts, secondLifts, "Both renders take the same lift count.");
        Assert.HasCount(first.Length, second, "Both rings carry the same position count.");

        for(int index = 0; index < first.Length; index++)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first[index].X), BitConverter.DoubleToInt64Bits(second[index].X), $"Position {index} X bits agree.");
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first[index].Y), BitConverter.DoubleToInt64Bits(second[index].Y), $"Position {index} Y bits agree.");
        }
    }

    /// <summary>A ring shrunk globally toward the center no longer covers and answers the short verdict.</summary>
    [TestMethod]
    public void HandShrunkRingAnswersShort()
    {
        BoundingCircle circle = RenderVerified("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out Point2d[] ring, out _);

        for(int index = 0; index < ring.Length - 1; index++)
        {
            ring[index] = ScaleTowardCenter(ring[index], circle.Center, 0.999);
        }

        ring[^1] = ring[0];

        Assert.AreEqual(CircleCoverageVerdict.Short, CircumscribedCirclePolygon.Verify(ring, circle.Center, circle.Radius), "A globally shrunk ring cannot cover.");
    }

    /// <summary>A single vertex pulled toward the center defeats its edges' distance gates — the per-edge check, not just a global scale, is load-bearing.</summary>
    [TestMethod]
    public void SingleVertexPulledInwardAnswersShort()
    {
        BoundingCircle circle = RenderVerified("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out Point2d[] ring, out _);

        ring[5] = ScaleTowardCenter(ring[5], circle.Center, 0.99);

        Assert.AreEqual(CircleCoverageVerdict.Short, CircumscribedCirclePolygon.Verify(ring, circle.Center, circle.Radius), "A single pulled vertex defeats coverage.");
    }

    /// <summary>
    /// A stride-reordered ring visits the octants out of their canonical order — a star-shaped
    /// multi-winding traversal whose every turn is still consistently counter-clockwise — and
    /// the octant-roster gate refuses it: the winding premise of the coverage argument is a
    /// checked gate, never an assumption about the emitter.
    /// </summary>
    [TestMethod]
    public void StrideReorderedRingAnswersShort()
    {
        BoundingCircle circle = RenderVerified("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out Point2d[] ring, out _);
        var reordered = new Point2d[ring.Length];

        for(int index = 0; index < ring.Length - 1; index++)
        {
            reordered[index] = ring[(index * 3) % (ring.Length - 1)];
        }

        reordered[^1] = reordered[0];

        Assert.AreEqual(CircleCoverageVerdict.Short, CircumscribedCirclePolygon.Verify(reordered, circle.Center, circle.Radius), "An out-of-order octant roster refuses.");
    }

    /// <summary>A ring of the wrong shape — too short, a partial quadrant, or an open closure — answers the short verdict.</summary>
    [TestMethod]
    public void MalformedRingShapesAnswerShort()
    {
        BoundingCircle circle = RenderVerified("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out Point2d[] ring, out _);

        Assert.AreEqual(CircleCoverageVerdict.Short, CircumscribedCirclePolygon.Verify(ring.AsSpan(0, 3), circle.Center, circle.Radius), "A three-position span is no ring.");
        Assert.AreEqual(CircleCoverageVerdict.Short, CircumscribedCirclePolygon.Verify(ring.AsSpan(0, 31), circle.Center, circle.Radius), "A partial-quadrant count refuses.");

        ring[^1] = new Point2d(ring[^1].X + 1.0, ring[^1].Y);

        Assert.AreEqual(CircleCoverageVerdict.Short, CircumscribedCirclePolygon.Verify(ring, circle.Center, circle.Radius), "An open closure refuses.");
    }

    /// <summary>
    /// Non-finite inputs answer the wall violation from the verifier's own finiteness sweep,
    /// wherever they sit — the orientation predicates downstream carry no finiteness guards of
    /// their own, so the sweep is load-bearing.
    /// </summary>
    [TestMethod]
    public void NonFiniteInputsAnswerTheWallViolation()
    {
        BoundingCircle circle = RenderVerified("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out Point2d[] ring, out _);

        Point2d saved = ring[5];
        ring[5] = new Point2d(double.NaN, saved.Y);

        Assert.AreEqual(CircleCoverageVerdict.WallViolation, CircumscribedCirclePolygon.Verify(ring, circle.Center, circle.Radius), "A NaN ring ordinate violates the wall.");

        ring[5] = saved;

        Assert.AreEqual(
            CircleCoverageVerdict.WallViolation,
            CircumscribedCirclePolygon.Verify(ring, new Point2d(double.NaN, circle.Center.Y), circle.Radius),
            "A NaN center ordinate violates the wall.");
        Assert.AreEqual(CircleCoverageVerdict.WallViolation, CircumscribedCirclePolygon.Verify(ring, circle.Center, double.NaN), "A NaN radius violates the wall.");
        Assert.AreEqual(CircleCoverageVerdict.WallViolation, CircumscribedCirclePolygon.Verify(ring, circle.Center, double.PositiveInfinity), "An infinite radius violates the wall.");

        ring[5] = new Point2d(2e76, saved.Y);

        Assert.AreEqual(CircleCoverageVerdict.WallViolation, CircumscribedCirclePolygon.Verify(ring, circle.Center, circle.Radius), "A ring ordinate beyond the wall violates it.");
    }

    /// <summary>
    /// A positive radius below the exactness wall refuses the rendering and the verification
    /// alike: beneath it both sides of the edge comparison underflow toward zero and a silent
    /// false certificate would replace the exact one, so the wall answer is refusal.
    /// </summary>
    [TestMethod]
    public void RadiiBelowTheExactnessWallRefuse()
    {
        var center = new Point2d(0.0, 0.0);

        Assert.IsFalse(CircumscribedCirclePolygon.TryRender(new BoundingCircle(center, 1e-61), QuadrantSegments, out _, out int liftSteps), "A radius below the wall refuses.");
        Assert.AreEqual(0, liftSteps, "The wall refusal happens before any lift.");
        Assert.IsFalse(CircumscribedCirclePolygon.TryRender(new BoundingCircle(center, double.Epsilon), QuadrantSegments, out _, out _), "A subnormal radius refuses.");

        BoundingCircle circle = RenderVerified("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", out Point2d[] ring, out _);

        Assert.AreEqual(CircleCoverageVerdict.WallViolation, CircumscribedCirclePolygon.Verify(ring, circle.Center, 1e-61), "The verifier's own radius wall answers the violation.");
    }

    /// <summary>
    /// A circumradius unresolvable at the center's magnitude — every vertex offset rounds away
    /// against the center's ordinates — collapses the emitted ring, every emission stays short,
    /// and the ratchet exhausts its ceiling into refusal with the step count at the point of
    /// refusal.
    /// </summary>
    [TestMethod]
    public void UnresolvableCircumradiusExhaustsTheRatchetAndRefuses()
    {
        var circle = new BoundingCircle(new Point2d(1e15, 0.0), 1e-3);

        Assert.IsFalse(CircumscribedCirclePolygon.TryRender(circle, QuadrantSegments, out _, out int liftSteps), "An unresolvable circumradius refuses.");
        Assert.AreEqual(CircumscribedCirclePolygon.LiftStepCeiling, liftSteps, "The refusal reports the step count at the ceiling.");
    }

    /// <summary>Parses the operand, computes its certified circle, renders the certified circumscription, and re-verifies the emitted ring.</summary>
    /// <param name="inputText">The WKT operand.</param>
    /// <param name="ring">The emitted closed ring.</param>
    /// <param name="liftSteps">The ratchet's step count.</param>
    /// <returns>The certified circle.</returns>
    private static BoundingCircle RenderVerified(string inputText, out Point2d[] ring, out int liftSteps)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(inputText, out FlatGeometry operand, out _), $"'{inputText}' must parse.");
        Assert.IsTrue(GeometryBoundingCircle.TryCompute(in operand, out BoundingCircle circle), $"'{inputText}' answers a circle.");
        Assert.IsTrue(CircumscribedCirclePolygon.TryRender(circle, QuadrantSegments, out ring, out liftSteps), $"'{inputText}' renders a verified covering.");
        Assert.AreEqual(CircleCoverageVerdict.Covers, CircumscribedCirclePolygon.Verify(ring, circle.Center, circle.Radius), $"'{inputText}': the emitted ring re-verifies.");

        return circle;
    }

    /// <summary>
    /// Asserts every ring vertex on-or-outside the certified circle and strictly inside the
    /// published-bound circle, both through the exact excess sign.
    /// </summary>
    /// <param name="ring">The emitted ring.</param>
    /// <param name="circle">The certified circle.</param>
    /// <param name="label">The row label named in failures.</param>
    private static void AssertRingWithinThePublishedBound(Point2d[] ring, BoundingCircle circle, string label)
    {
        double boundRadius = circle.Radius * PublishedBoundFactor;

        foreach(Point2d vertex in ring)
        {
            Assert.IsGreaterThan(-1, ExactCircleExcess.Sign(vertex, circle.Center, circle.Radius), $"'{label}': a ring vertex sits on-or-outside the certified circle, exactly.");
            Assert.IsLessThan(0, ExactCircleExcess.Sign(vertex, circle.Center, boundRadius), $"'{label}': a ring vertex sits strictly inside the published-bound circle, exactly.");
        }
    }

    /// <summary>Moves a position toward the center by the given factor of its offset.</summary>
    /// <param name="position">The position to move.</param>
    /// <param name="center">The center to move toward.</param>
    /// <param name="factor">The offset factor to keep.</param>
    /// <returns>The moved position.</returns>
    private static Point2d ScaleTowardCenter(Point2d position, Point2d center, double factor)
    {
        return new Point2d(center.X + (factor * (position.X - center.X)), center.Y + (factor * (position.Y - center.Y)));
    }
}
