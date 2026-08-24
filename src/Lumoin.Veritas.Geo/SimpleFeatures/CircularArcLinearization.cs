using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Certified linearization of circular arcs into inscribed chord polylines: the
/// approximation is constructed by arc bisection and verified per emission by exact
/// predicates, with refusal — never a shipped approximation — wherever the arithmetic
/// cannot certify. The claims are stated against the certified circle, the computed
/// center and radius as double values: every emitted vertex lies within the published
/// drift band of that circle, checked exactly, and every emitted chord's midpoint
/// clears the exact sagitta check against the comparison radius before the chord
/// exists. The construction uses addition, subtraction, multiplication, division, and
/// square root only — every operation correctly rounded under IEEE 754 — so the output
/// is bit-identical across conforming machines; no transcendental function
/// participates. All steering decisions — degeneracy, minor against major side, the
/// diametral key, split membership — are exact-predicate signs, never rounded
/// comparisons. The kernel is two-dimensional and emits vertices in arc order through
/// an explicit bounded stack; nothing recurses and nothing allocates on the heap.
/// </summary>
internal static class CircularArcLinearization
{
    /// <summary>
    /// The largest fraction of the certified radius by which an emitted chord may sag
    /// inward from the circle: two to the negative sixteenth power. Every chord's
    /// midpoint passes an exact check against the comparison radius derived from this
    /// bound before the chord is emitted.
    /// </summary>
    internal const double MaximumRelativeSagitta = 1.52587890625e-05;

    /// <summary>
    /// The largest fraction of the certified radius by which any emitted vertex may
    /// sit off the circle, on either side: two to the negative twentieth power. Every
    /// vertex — the document's own control points included — passes two exact checks
    /// against the annulus derived from this band before it is emitted.
    /// </summary>
    internal const double MaximumRelativeVertexDrift = 9.5367431640625e-07;

    /// <summary>
    /// The bisection depth cap per gap: sixteen. A certifiable gap clears by about
    /// depth ten — the sagitta shrinks roughly fourfold per level — so the cap is
    /// generous headroom, and it doubles as the hard resource bound: a gap can emit at
    /// most two to the sixteenth plus one vertices, and an arc has at most eight gaps.
    /// </summary>
    internal const int MaximumBisectionDepth = 16;

    /// <summary>
    /// The explicit gap stack's capacity: the depth cap plus headroom for the seed
    /// gaps and their half-turn pre-splits. One descent chain holds at most one entry
    /// per depth level plus the seed entry, so the cap can never be exceeded — the
    /// depth check refuses first.
    /// </summary>
    private const int GapStackCapacity = 24;

    /// <summary>
    /// The lower magnitude wall for every ordinate, radius, and computed value the
    /// exact checks consume; zero is exempt for ordinates. The exact circle predicate
    /// is exact only while no expansion component underflows, a condition on bit
    /// quanta its documentation places at roughly ten to the negative one hundred and
    /// thirtieth; this wall stands ten orders inside it so the differences, sums, and
    /// halvings this kernel constructs stay safely evaluable.
    /// </summary>
    internal const double MinimumMagnitude = 1e-120;

    /// <summary>
    /// The upper magnitude wall. The exact circle predicate throws once a squared
    /// magnitude overflows, which its documentation places near six point seven times
    /// ten to the one hundred and fifty-third; this wall stands three orders inside it
    /// so every constructed value — differences and sums up to a few times the inputs,
    /// and their plain-double squares inside the normalization — stays finite. The
    /// walls are checked in acceptance form, so a value that is not a number fails
    /// them and the predicates' guard exceptions are unreachable from this kernel.
    /// </summary>
    internal const double MaximumMagnitude = 1e150;

    /// <summary>
    /// Linearizes a three-point arc — start, middle, end, the middle lying on the
    /// arc between the endpoints — appending the certified vertex run to
    /// <paramref name="builder"/> half-open: the start seed is never emitted (the
    /// caller owns the run's opening vertex), the intermediates, the middle seed, and
    /// the end seed are. The three control points enter the output verbatim,
    /// bit-preserved. False reports the outcome and the offending control-point
    /// index, and the builder holds exactly the vertices appended before the offense.
    /// </summary>
    public static bool TryLinearizeArc(Point2d start, Point2d middle, Point2d end, FlatGeometryBuilder builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex)
    {
        if(!TryCertifyCircle(start, middle, end, out CircleFrame frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        if(!TryCheckSeedAnnulus(start, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(middle, 1, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(end, 2, in frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        Point2d current = start;

        if(!TryEmitGap(ref current, middle, in frame, builder, out outcome)
            || !TryEmitGap(ref current, end, in frame, builder, out outcome))
        {
            offendingSeedIndex = -1;

            return false;
        }

        outcome = CircularArcLinearizationOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// Linearizes a full circle through three control points, appending the certified
    /// vertex run half-open: the first seed is never emitted (the caller owns the
    /// opening vertex), and the run walks through the second and third seeds back to
    /// the first control point, which closes the ring verbatim — the closing vertex
    /// is the opening vertex bit for bit.
    /// </summary>
    public static bool TryLinearizeCircle(Point2d first, Point2d second, Point2d third, FlatGeometryBuilder builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex)
    {
        if(!TryCertifyCircle(first, second, third, out CircleFrame frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        if(!TryCheckSeedAnnulus(first, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(second, 1, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(third, 2, in frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        Point2d current = first;

        if(!TryEmitGap(ref current, second, in frame, builder, out outcome)
            || !TryEmitGap(ref current, third, in frame, builder, out outcome)
            || !TryEmitGap(ref current, first, in frame, builder, out outcome))
        {
            offendingSeedIndex = -1;

            return false;
        }

        outcome = CircularArcLinearizationOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// Linearizes a circle given by its center and radius, appending the certified
    /// vertex run CLOSED — opening vertex included, because the document provides no
    /// vertex for the caller to have emitted. The seeds are the four cardinal points
    /// in the fixed canonical order east, north, west, south — counter-clockwise,
    /// starting at east — each one addition per ordinate, and the east cardinal
    /// closes the ring verbatim. The caller has already adjudicated the radius token:
    /// this kernel requires a finite positive radius as its caller contract.
    /// </summary>
    public static bool TryLinearizeCenterRadius(Point2d center, double radius, FlatGeometryBuilder builder, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex)
    {
        if(!OrdinateInWall(center.X) || !OrdinateInWall(center.Y))
        {
            outcome = CircularArcLinearizationOutcome.MagnitudeWall;
            offendingSeedIndex = 0;

            return false;
        }

        if(!RadiusInWall(radius))
        {
            outcome = CircularArcLinearizationOutcome.MagnitudeWall;
            offendingSeedIndex = 0;

            return false;
        }

        CircleFrame frame = CircleFrame.Create(center, radius, travel: 1);
        Point2d east = new(center.X + radius, center.Y);
        Point2d north = new(center.X, center.Y + radius);
        Point2d west = new(center.X - radius, center.Y);
        Point2d south = new(center.X, center.Y - radius);

        if(!TryCheckSeedAnnulus(east, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(north, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(west, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(south, 0, in frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        builder.AddVertex(east);

        Point2d current = east;

        if(!TryEmitGap(ref current, north, in frame, builder, out outcome)
            || !TryEmitGap(ref current, west, in frame, builder, out outcome)
            || !TryEmitGap(ref current, south, in frame, builder, out outcome)
            || !TryEmitGap(ref current, east, in frame, builder, out outcome))
        {
            offendingSeedIndex = -1;

            return false;
        }

        outcome = CircularArcLinearizationOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// The per-arc frame: the certified circle, the travel sign, and the three
    /// comparison radii, each adjusted one bit in its conservative direction exactly
    /// once so every later check is a plain exact-predicate call.
    /// </summary>
    private readonly record struct CircleFrame(Point2d Center, double Radius, int Travel, double ComparisonRadius, double AnnulusInner, double AnnulusOuter)
    {
        /// <summary>
        /// Builds the frame: the comparison radius rounds one bit upward so the
        /// product's rounding can only strengthen the sagitta check, the inner
        /// annulus radius one bit downward and the outer one bit upward so the
        /// admitted band is one bit wider than the published constants — the stated
        /// claim stays true whichever way the products rounded.
        /// </summary>
        public static CircleFrame Create(Point2d center, double radius, int travel)
        {
            double comparisonRadius = Math.BitIncrement(radius * (1.0 - MaximumRelativeSagitta));
            double annulusInner = Math.BitDecrement(radius * (1.0 - MaximumRelativeVertexDrift));
            double annulusOuter = Math.BitIncrement(radius * (1.0 + MaximumRelativeVertexDrift));

            return new CircleFrame(center, radius, travel, comparisonRadius, annulusInner, annulusOuter);
        }
    }

    /// <summary>
    /// Degeneracy checks, the anchored circumcenter solve, and the wall checks on
    /// both the inputs and the computed circle. The solve anchors every input as a
    /// difference from the middle control point so the perpendicular-bisector system
    /// conditions at any offset, and the radius is the distance from the center back
    /// to that anchor — the construction's single square root. The exact collinearity
    /// pre-check blocks exact degeneracy only; a near-degenerate triple poisons the
    /// plain-double solve into garbage, infinity, or a value that is not a number,
    /// which is why the walls run on the computed values and why the caller's seed
    /// annulus checks never trust the solve.
    /// </summary>
    private static bool TryCertifyCircle(Point2d first, Point2d second, Point2d third, out CircleFrame frame, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex)
    {
        frame = default;

        if(!OrdinateInWall(first.X) || !OrdinateInWall(first.Y))
        {
            outcome = CircularArcLinearizationOutcome.MagnitudeWall;
            offendingSeedIndex = 0;

            return false;
        }

        if(!OrdinateInWall(second.X) || !OrdinateInWall(second.Y))
        {
            outcome = CircularArcLinearizationOutcome.MagnitudeWall;
            offendingSeedIndex = 1;

            return false;
        }

        if(!OrdinateInWall(third.X) || !OrdinateInWall(third.Y))
        {
            outcome = CircularArcLinearizationOutcome.MagnitudeWall;
            offendingSeedIndex = 2;

            return false;
        }

        if(first == second)
        {
            outcome = CircularArcLinearizationOutcome.CoincidentControlPoints;
            offendingSeedIndex = 1;

            return false;
        }

        if(second == third || first == third)
        {
            outcome = CircularArcLinearizationOutcome.CoincidentControlPoints;
            offendingSeedIndex = 2;

            return false;
        }

        int travel = ExactOrientation.Orient2D(first, second, third);

        if(travel == 0)
        {
            outcome = CircularArcLinearizationOutcome.CollinearControlPoints;
            offendingSeedIndex = 2;

            return false;
        }

        double towardFirstX = first.X - second.X;
        double towardFirstY = first.Y - second.Y;
        double towardThirdX = third.X - second.X;
        double towardThirdY = third.Y - second.Y;
        double towardFirstSquared = (towardFirstX * towardFirstX) + (towardFirstY * towardFirstY);
        double towardThirdSquared = (towardThirdX * towardThirdX) + (towardThirdY * towardThirdY);
        double cross = (towardFirstX * towardThirdY) - (towardFirstY * towardThirdX);
        double offsetX = ((towardThirdY * towardFirstSquared) - (towardFirstY * towardThirdSquared)) / (2.0 * cross);
        double offsetY = ((towardFirstX * towardThirdSquared) - (towardThirdX * towardFirstSquared)) / (2.0 * cross);
        Point2d center = new(second.X + offsetX, second.Y + offsetY);
        double radius = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));

        if(!OrdinateInWall(center.X) || !OrdinateInWall(center.Y) || !RadiusInWall(radius))
        {
            outcome = CircularArcLinearizationOutcome.MagnitudeWall;
            offendingSeedIndex = -1;

            return false;
        }

        frame = CircleFrame.Create(center, radius, travel);
        outcome = CircularArcLinearizationOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// The exact annulus check on a document seed, reporting the seed's index on
    /// failure — the check that exposes a mis-solved circle through the document's
    /// own points, so the plain-double solve is never trusted, only checked.
    /// </summary>
    private static bool TryCheckSeedAnnulus(Point2d seed, int seedIndex, in CircleFrame frame, out CircularArcLinearizationOutcome outcome, out int offendingSeedIndex)
    {
        if(!AnnulusHolds(seed, in frame))
        {
            outcome = CircularArcLinearizationOutcome.VertexDrift;
            offendingSeedIndex = seedIndex;

            return false;
        }

        outcome = CircularArcLinearizationOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// Emits one gap half-open — the intermediates in arc order, then the far seed —
    /// advancing <paramref name="current"/> to the far seed. The explicit
    /// last-in-first-out stack holds pending far endpoints; the near half of every
    /// split is processed before the far half is popped, which is the visitation
    /// order argument: vertices emit strictly in arc order. Every gap decision is an
    /// exact sign — the minor-side test, the sagitta check, the split membership —
    /// and a gap only terminates by clearing its exact check or refusing.
    /// </summary>
    private static bool TryEmitGap(ref Point2d current, Point2d far, in CircleFrame frame, FlatGeometryBuilder builder, out CircularArcLinearizationOutcome outcome)
    {
        Span<Point2d> pendingTargets = stackalloc Point2d[GapStackCapacity];
        Span<int> pendingDepths = stackalloc int[GapStackCapacity];
        pendingTargets[0] = far;
        pendingDepths[0] = 0;

        int pendingCount = 1;

        while(pendingCount > 0)
        {
            Point2d target = pendingTargets[pendingCount - 1];
            int depth = pendingDepths[pendingCount - 1];
            int side = ExactOrientation.Orient2D(current, target, frame.Center);

            if(side == frame.Travel && ChordClears(current, target, in frame))
            {
                pendingCount--;
                builder.AddVertex(target);
                current = target;

                continue;
            }

            if(depth >= MaximumBisectionDepth)
            {
                outcome = CircularArcLinearizationOutcome.DepthCeiling;

                return false;
            }

            if(!TryConstructSplit(current, target, side, frame.Center, frame.Radius, frame.Travel, out Point2d split))
            {
                outcome = CircularArcLinearizationOutcome.SplitMembership;

                return false;
            }

            if(!AnnulusHolds(split, in frame))
            {
                outcome = CircularArcLinearizationOutcome.VertexDrift;

                return false;
            }

            pendingTargets[pendingCount] = split;
            pendingDepths[pendingCount] = depth + 1;
            pendingCount++;
        }

        outcome = CircularArcLinearizationOutcome.Certified;

        return true;
    }

    /// <summary>
    /// The exact sagitta check: true when the chord midpoint of the gap sits at or
    /// outside the comparison radius, which bounds the remaining sub-arc's inward sag
    /// by the published fraction. Only a minor gap may take this check — the caller
    /// gates on the exact side test first, because a near-full-turn gap's chord
    /// midpoint also sits close to the circle, on the wrong side.
    /// </summary>
    private static bool ChordClears(Point2d nearPoint, Point2d farPoint, in CircleFrame frame)
    {
        Point2d midpoint = new((nearPoint.X + farPoint.X) / 2.0, (nearPoint.Y + farPoint.Y) / 2.0);

        return ExactCircleExcess.Sign(midpoint, frame.Center, frame.ComparisonRadius) >= 0;
    }

    /// <summary>
    /// Constructs the split vertex for a gap and certifies its membership exactly.
    /// The diametral key is the exact side test: a zero side means the chord runs
    /// through the center, and the split takes the pinned perpendicular of the chord
    /// — never the midpoint direction, whose length is rounding noise there. A
    /// nonzero side splits at the midpoint direction, toward the circle for a minor
    /// gap and away for a major one. Every candidate then passes the exact membership
    /// check — the split vertex must lie on the gap's own sub-arc, the opposite side
    /// of the chord from the sub-arc's complement — and a midpoint-direction failure
    /// retries through the perpendicular, which is well conditioned exactly where the
    /// midpoint direction is noise. Both constructions failing is the membership
    /// refusal.
    /// </summary>
    internal static bool TryConstructSplit(Point2d nearPoint, Point2d farPoint, int side, Point2d center, double radius, int travel, out Point2d split)
    {
        int membershipSign = -travel;

        if(side != 0)
        {
            double midpointX = (nearPoint.X + farPoint.X) / 2.0;
            double midpointY = (nearPoint.Y + farPoint.Y) / 2.0;
            double towardMidX = midpointX - center.X;
            double towardMidY = midpointY - center.Y;
            double sign = side == travel ? 1.0 : -1.0;

            if(TryPlaceOnCircle(towardMidX * sign, towardMidY * sign, center, radius, out split)
                && ExactOrientation.Orient2D(nearPoint, farPoint, split) == membershipSign)
            {
                return true;
            }
        }

        double chordX = farPoint.X - nearPoint.X;
        double chordY = farPoint.Y - nearPoint.Y;

        if(TryPlaceOnCircle(-chordY, chordX, center, radius, out split)
            && ExactOrientation.Orient2D(nearPoint, farPoint, split) == membershipSign)
        {
            return true;
        }

        if(TryPlaceOnCircle(chordY, -chordX, center, radius, out split)
            && ExactOrientation.Orient2D(nearPoint, farPoint, split) == membershipSign)
        {
            return true;
        }

        split = default;

        return false;
    }

    /// <summary>
    /// Places a vertex on the certified circle along a direction: normalization by
    /// the direction's length, then one multiply and one add per ordinate. A
    /// zero-length or degenerate direction yields non-finite ordinates; the caller's
    /// membership check rejects them — the exact predicates never see a value the
    /// walls have not covered, because the result stays within one radius of the
    /// in-wall center whenever it is finite at all.
    /// </summary>
    private static bool TryPlaceOnCircle(double directionX, double directionY, Point2d center, double radius, out Point2d vertex)
    {
        double length = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        vertex = new Point2d(
            center.X + (radius * (directionX / length)),
            center.Y + (radius * (directionY / length)));

        return double.IsFinite(vertex.X) && double.IsFinite(vertex.Y);
    }

    /// <summary>
    /// The exact two-sided annulus check: the vertex sits at or inside the outer
    /// radius and at or outside the inner one. Two exact predicate evaluations; no
    /// rounded comparison participates.
    /// </summary>
    private static bool AnnulusHolds(Point2d vertex, in CircleFrame frame)
    {
        if(ExactCircleExcess.Sign(vertex, frame.Center, frame.AnnulusOuter) > 0)
        {
            return false;
        }

        return ExactCircleExcess.Sign(vertex, frame.Center, frame.AnnulusInner) >= 0;
    }

    /// <summary>
    /// The acceptance-form wall test for an ordinate: zero, or a magnitude between
    /// the walls. A value that is not a number fails both comparisons and is
    /// refused — the test never needs to name it.
    /// </summary>
    private static bool OrdinateInWall(double value)
    {
        if(value == 0.0)
        {
            return true;
        }

        double magnitude = Math.Abs(value);

        return magnitude >= MinimumMagnitude && magnitude <= MaximumMagnitude;
    }

    /// <summary>
    /// The acceptance-form wall test for a radius: strictly positive and between the
    /// walls — a circle of zero radius is degenerate whatever produced it, and a
    /// value that is not a number fails here too.
    /// </summary>
    private static bool RadiusInWall(double value)
    {
        return value >= MinimumMagnitude && value <= MaximumMagnitude;
    }
}
