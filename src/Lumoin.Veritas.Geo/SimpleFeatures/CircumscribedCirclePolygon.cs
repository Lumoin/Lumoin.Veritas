using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The certified circumscription of a covering circle: renders a regular polygon ring whose
/// interior certifiably contains the whole disc, boundary included. The circumradius seeds at
/// the radius divided by the cosine of half the tessellation step and ratchets upward one bit
/// at a time until the emitted ring passes verification, so coverage is verified per emission,
/// never assumed — an emission that cannot be verified is refused. The verification is exact:
/// the ring's octant roster and winding, its strict convexity, the center's side of every edge,
/// and every edge line's squared distance against the squared radius are all decided by exact
/// orientation predicates and floating-point expansion arithmetic, with no epsilon anywhere.
/// The emitted polygon's circumradius exceeds the radius by at most the secant of half the
/// tessellation step minus one — under half a percent at thirty-two vertices, from the exact
/// secant form — and the circumradius must be resolvable at the center's magnitude: when every
/// vertex offset rounds away against the center's ordinates the ratchet exhausts its ceiling
/// and the rendering refuses.
/// </summary>
internal static class CircumscribedCirclePolygon
{
    /// <summary>
    /// The largest operand ordinate magnitude the rendering seam accepts. Ring ordinates reach
    /// the center magnitude plus the circumradius — at most about three times this wall — and
    /// stay inside <see cref="MaximumRingOrdinate"/>, where the verifier's degree-four exact
    /// evaluation is finite throughout; the covering-circle kernel's own degree-two guards are
    /// unreachable below this wall.
    /// </summary>
    internal const double MaximumOperandOrdinate = 1e75;

    /// <summary>
    /// The largest ring ordinate magnitude the verifier accepts: the squared edge cross product
    /// is bounded by sixty-four times the fourth power of this wall, which stays below the
    /// largest finite double with two decades of margin.
    /// </summary>
    internal const double MaximumRingOrdinate = 1e76;

    /// <summary>
    /// The smallest positive radius the verifier certifies. The degree-four chain's smallest
    /// meaningful expansion components sit near the squared cross product's unit-of-unit
    /// roundoff — about the fourth power of the radius scaled by two to the minus two hundred
    /// twelfth — and stay normal exactly when the radius is at or above about 1.1e-61, so at
    /// this wall every two-term transform in the evaluation is exact. Below it the rendering
    /// refuses rather than degrade the certificate.
    /// </summary>
    internal const double MinimumPositiveRadius = 1e-60;

    /// <summary>
    /// The ratchet's step ceiling. The seed sits within a couple of units in the last place of
    /// the exact quotient, each emitted vertex's own rounding costs at most a couple more, and
    /// a vertex's emitted distance from the center is weakly monotone in the circumradius, so
    /// the lift count a covering emission needs is a single-digit ulp count; the ceiling is an
    /// order of magnitude above it. A breach refuses the emission.
    /// </summary>
    internal const int LiftStepCeiling = 64;

    /// <summary>Components of a packed two-term transform.</summary>
    private const int LeafComponents = 2;

    /// <summary>Components of the product of two packed two-term transforms.</summary>
    private const int LeafProductComponents = 8;

    /// <summary>Scratch the leaf-shape expansion product needs alongside its result.</summary>
    private const int LeafProductScratchComponents = 12;

    /// <summary>Components of a sum of two leaf products: the cross and the squared edge length.</summary>
    private const int PairComponents = 16;

    /// <summary>Components of the squared cross product of two pair-shape expansions.</summary>
    private const int CrossSquareComponents = 512;

    /// <summary>Scratch the squared-cross expansion product needs alongside its result.</summary>
    private const int CrossSquareScratchComponents = 544;

    /// <summary>Components of the squared radius scaled through the squared edge length.</summary>
    private const int RadiusLengthComponents = 64;

    /// <summary>Scratch the radius-length expansion product needs alongside its result.</summary>
    private const int RadiusLengthScratchComponents = 68;

    /// <summary>Components of the full edge excess: the squared cross plus the negated radius-length product.</summary>
    private const int EdgeExcessComponents = 576;

    /// <summary>
    /// Renders the certified circumscribing ring of the circle: one vertex per tessellation
    /// step over the four quadrants, counter-clockwise from the positive-X cardinal with the
    /// four cardinals snapped to exact ordinates, closed on a copy of the first position. The
    /// circumradius
    /// seeds at the radius divided by the cosine of half the tessellation step and ratchets one
    /// bit upward per unverified emission up to <see cref="LiftStepCeiling"/>. A radius below
    /// <see cref="MinimumPositiveRadius"/>, a non-finite seed, a wall violation, or a ceiling
    /// breach refuses the rendering.
    /// </summary>
    /// <param name="circle">The certified covering circle to circumscribe; the radius must be positive.</param>
    /// <param name="quadrantSegments">The arc tessellation: vertices per quadrant.</param>
    /// <param name="ring">The emitted closed ring; valid only on a true return.</param>
    /// <param name="circumradiusLiftSteps">The ratchet's step count: zero when the seed emission verifies; on a false return, the count at the point of refusal.</param>
    /// <returns><see langword="true"/> when the emitted ring verifiably covers the circle.</returns>
    public static bool TryRender(BoundingCircle circle, int quadrantSegments, out Point2d[] ring, out int circumradiusLiftSteps)
    {
        int steps = quadrantSegments * 4;
        ring = new Point2d[steps + 1];
        circumradiusLiftSteps = 0;
        if(!double.IsFinite(circle.Radius) || circle.Radius < MinimumPositiveRadius)
        {
            return false;
        }

        double circumradius = circle.Radius / Math.Cos(Math.PI / steps);
        if(!double.IsFinite(circumradius))
        {
            return false;
        }

        while(true)
        {
            for(int step = 0; step < steps; step++)
            {
                ring[step] = OffsetCurveBuilder.CirclePoint(circle.Center, circumradius, step, quadrantSegments);
            }

            ring[steps] = ring[0];
            CircleCoverageVerdict verdict = Verify(ring, circle.Center, circle.Radius);
            if(verdict == CircleCoverageVerdict.Covers)
            {
                return true;
            }

            if(verdict == CircleCoverageVerdict.WallViolation || circumradiusLiftSteps >= LiftStepCeiling)
            {
                return false;
            }

            circumradius = Math.BitIncrement(circumradius);
            circumradiusLiftSteps++;
        }
    }

    /// <summary>
    /// The one-shot coverage verification of an emitted ring against the circle, in gate order:
    /// the finiteness-and-wall sweep over every input; the ring shape (a closed ring of a whole
    /// number of quadrants, its closure position bit-equal to its first); the octant roster and
    /// winding (every vertex direction from the center in its construction-expected octant
    /// class with strictly counter-clockwise consecutive steps, so the ring winds exactly once
    /// and a star-shaped multi-winding traversal can never verify); strict convexity at every
    /// vertex; and per edge, the center strictly on the interior side and the exact comparison
    /// of the squared edge cross product against the squared radius times the squared edge
    /// length. Every sign is exact; every expansion production's dominant component is checked
    /// finite before it feeds forward.
    /// </summary>
    /// <param name="ring">The closed ring under verification.</param>
    /// <param name="center">The circle's center.</param>
    /// <param name="radius">The circle's radius.</param>
    /// <returns>The verdict.</returns>
    internal static CircleCoverageVerdict Verify(ReadOnlySpan<Point2d> ring, Point2d center, double radius)
    {
        if(!double.IsFinite(radius) || radius < MinimumPositiveRadius || !IsInsideRingWall(center))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        foreach(Point2d vertex in ring)
        {
            if(!IsInsideRingWall(vertex))
            {
                return CircleCoverageVerdict.WallViolation;
            }
        }

        int count = ring.Length - 1;
        if(count < 4 || count % 4 != 0 || !AreBitEqual(ring[count], ring[0]))
        {
            return CircleCoverageVerdict.Short;
        }

        int quadrantSegments = count / 4;

        for(int index = 0; index < count; index++)
        {
            int quadrant = index / quadrantSegments;
            int expectedClass = index % quadrantSegments == 0 ? 2 * quadrant : (2 * quadrant) + 1;
            if(ExactOrientation.DirectionClass(center, ring[index]) != expectedClass)
            {
                return CircleCoverageVerdict.Short;
            }

            if(ExactOrientation.DirectionCrossSign(center, ring[index], center, ring[(index + 1) % count]) <= 0)
            {
                return CircleCoverageVerdict.Short;
            }
        }

        for(int index = 0; index < count; index++)
        {
            if(ExactOrientation.Orient2D(ring[index], ring[(index + 1) % count], ring[(index + 2) % count]) <= 0)
            {
                return CircleCoverageVerdict.Short;
            }
        }

        for(int index = 0; index < count; index++)
        {
            Point2d start = ring[index];
            Point2d end = ring[(index + 1) % count];
            if(ExactOrientation.Orient2D(start, end, center) <= 0)
            {
                return CircleCoverageVerdict.Short;
            }

            CircleCoverageVerdict edge = ClassifyEdgeDistance(start, end, center, radius);
            if(edge != CircleCoverageVerdict.Covers)
            {
                return edge;
            }
        }

        return CircleCoverageVerdict.Covers;
    }

    /// <summary>
    /// The exact per-edge distance comparison: the sign of the squared cross product of the
    /// edge vector with the center's reach, minus the squared radius times the squared edge
    /// length — non-negative exactly when the edge's supporting line sits at or beyond the
    /// radius from the center. The caller has already established the center strictly on the
    /// edge's interior side, so the cross is positive and squaring preserves the comparison.
    /// Every difference enters exactly through a two-term transform, every product and sum is
    /// an exact expansion operation, and every production's dominant component is checked
    /// finite before it feeds forward.
    /// </summary>
    /// <param name="start">The edge's start position.</param>
    /// <param name="end">The edge's end position.</param>
    /// <param name="center">The circle's center.</param>
    /// <param name="radius">The circle's radius.</param>
    /// <returns><see cref="CircleCoverageVerdict.Covers"/> when the edge line clears the radius.</returns>
    private static CircleCoverageVerdict ClassifyEdgeDistance(Point2d start, Point2d end, Point2d center, double radius)
    {
        Span<double> edgeX = stackalloc double[LeafComponents];
        Span<double> edgeY = stackalloc double[LeafComponents];
        Span<double> reachX = stackalloc double[LeafComponents];
        Span<double> reachY = stackalloc double[LeafComponents];
        Span<double> leafScratch = stackalloc double[LeafProductScratchComponents];
        Span<double> firstProduct = stackalloc double[LeafProductComponents];
        Span<double> secondProduct = stackalloc double[LeafProductComponents];
        Span<double> cross = stackalloc double[PairComponents];
        Span<double> squaredLength = stackalloc double[PairComponents];
        Span<double> crossSquare = stackalloc double[CrossSquareComponents];
        Span<double> crossSquareScratch = stackalloc double[CrossSquareScratchComponents];
        Span<double> radiusSquare = stackalloc double[LeafComponents];
        Span<double> radiusLength = stackalloc double[RadiusLengthComponents];
        Span<double> radiusLengthScratch = stackalloc double[RadiusLengthScratchComponents];
        Span<double> excess = stackalloc double[EdgeExcessComponents];

        (double highX, double lowX) = ExpansionArithmetic.TwoDiff(end.X, start.X);
        int edgeXCount = Pack(highX, lowX, edgeX);
        (double highY, double lowY) = ExpansionArithmetic.TwoDiff(end.Y, start.Y);
        int edgeYCount = Pack(highY, lowY, edgeY);
        (double reachHighX, double reachLowX) = ExpansionArithmetic.TwoDiff(center.X, start.X);
        int reachXCount = Pack(reachHighX, reachLowX, reachX);
        (double reachHighY, double reachLowY) = ExpansionArithmetic.TwoDiff(center.Y, start.Y);
        int reachYCount = Pack(reachHighY, reachLowY, reachY);

        int firstCount = ExpansionArithmetic.Product(edgeX[..edgeXCount], reachY[..reachYCount], leafScratch, firstProduct);
        if(!double.IsFinite(firstProduct[firstCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        int secondCount = ExpansionArithmetic.Product(edgeY[..edgeYCount], reachX[..reachXCount], leafScratch, secondProduct);
        if(!double.IsFinite(secondProduct[secondCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        for(int index = 0; index < secondCount; index++)
        {
            secondProduct[index] = -secondProduct[index];
        }

        int crossCount = ExpansionArithmetic.Sum(firstProduct[..firstCount], secondProduct[..secondCount], cross);
        if(!double.IsFinite(cross[crossCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        int lengthXCount = ExpansionArithmetic.Product(edgeX[..edgeXCount], edgeX[..edgeXCount], leafScratch, firstProduct);
        if(!double.IsFinite(firstProduct[lengthXCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        int lengthYCount = ExpansionArithmetic.Product(edgeY[..edgeYCount], edgeY[..edgeYCount], leafScratch, secondProduct);
        if(!double.IsFinite(secondProduct[lengthYCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        int squaredLengthCount = ExpansionArithmetic.Sum(firstProduct[..lengthXCount], secondProduct[..lengthYCount], squaredLength);
        if(!double.IsFinite(squaredLength[squaredLengthCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        int crossSquareCount = ExpansionArithmetic.Product(cross[..crossCount], cross[..crossCount], crossSquareScratch, crossSquare);
        if(!double.IsFinite(crossSquare[crossSquareCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        (double radiusHigh, double radiusLow) = ExpansionArithmetic.TwoProduct(radius, radius);
        if(!double.IsFinite(radiusHigh))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        int radiusSquareCount = Pack(radiusHigh, radiusLow, radiusSquare);
        int radiusLengthCount = ExpansionArithmetic.Product(radiusSquare[..radiusSquareCount], squaredLength[..squaredLengthCount], radiusLengthScratch, radiusLength);
        if(!double.IsFinite(radiusLength[radiusLengthCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        for(int index = 0; index < radiusLengthCount; index++)
        {
            radiusLength[index] = -radiusLength[index];
        }

        int excessCount = ExpansionArithmetic.Sum(crossSquare[..crossSquareCount], radiusLength[..radiusLengthCount], excess);
        if(!double.IsFinite(excess[excessCount - 1]))
        {
            return CircleCoverageVerdict.WallViolation;
        }

        return ExpansionArithmetic.Sign(excess[..excessCount]) >= 0
            ? CircleCoverageVerdict.Covers
            : CircleCoverageVerdict.Short;
    }

    /// <summary>Whether both ordinates are finite and inside the verifier's ring wall.</summary>
    /// <param name="position">The position under test.</param>
    /// <returns><see langword="true"/> when the position is inside the wall.</returns>
    private static bool IsInsideRingWall(Point2d position)
    {
        return double.IsFinite(position.X)
            && double.IsFinite(position.Y)
            && Math.Abs(position.X) <= MaximumRingOrdinate
            && Math.Abs(position.Y) <= MaximumRingOrdinate;
    }

    /// <summary>Whether two positions carry bit-identical ordinates — the closure test, where a signed-zero mismatch would serialize two different positions.</summary>
    /// <param name="first">The first position.</param>
    /// <param name="second">The second position.</param>
    /// <returns><see langword="true"/> when the positions are bit-identical.</returns>
    private static bool AreBitEqual(Point2d first, Point2d second)
    {
        return BitConverter.DoubleToInt64Bits(first.X) == BitConverter.DoubleToInt64Bits(second.X)
            && BitConverter.DoubleToInt64Bits(first.Y) == BitConverter.DoubleToInt64Bits(second.Y);
    }

    /// <summary>
    /// Packs a two-term transform's halves into a zero-eliminated expansion, low component
    /// first so magnitudes increase; a zero value packs as the single zero component every
    /// expansion consumer accepts.
    /// </summary>
    /// <param name="high">The transform's high half.</param>
    /// <param name="low">The transform's low half.</param>
    /// <param name="result">The span the packed expansion writes into.</param>
    /// <returns>The packed component count.</returns>
    private static int Pack(double high, double low, Span<double> result)
    {
        int written = 0;
        if(low != 0.0)
        {
            result[written] = low;
            written++;
        }

        if(high != 0.0 || written == 0)
        {
            result[written] = high;
            written++;
        }

        return written;
    }
}
