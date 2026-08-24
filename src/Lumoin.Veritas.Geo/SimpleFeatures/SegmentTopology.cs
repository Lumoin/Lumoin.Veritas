using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// How two non-degenerate segments relate as point sets.
/// </summary>
internal enum SegmentRelation
{
    /// <summary>The segments share no point.</summary>
    Disjoint = 0,

    /// <summary>
    /// The segments cross at a single point interior to both; the point is
    /// computed and serves only as node identity, never as a sign source.
    /// </summary>
    ProperCrossing = 1,

    /// <summary>
    /// The segments share exactly one point that is an original vertex of at
    /// least one of them — a shared endpoint, an endpoint on the other's
    /// interior, or a single-point collinear contact.
    /// </summary>
    VertexTouch = 2,

    /// <summary>
    /// The segments are collinear and share a stretch of positive length;
    /// both stretch endpoints are original vertices.
    /// </summary>
    CollinearOverlap = 3,
}

/// <summary>
/// One classified segment-pair intersection: the relation plus up to two
/// carrier points — the touch or crossing point in
/// <see cref="FirstPoint"/>, and for a collinear overlap the stretch runs
/// from <see cref="FirstPoint"/> to <see cref="SecondPoint"/>.
/// </summary>
/// <param name="Relation">The classified relation.</param>
/// <param name="FirstPoint">Touch point, crossing point, or overlap start.</param>
/// <param name="SecondPoint">Overlap end; meaningful only for a collinear overlap.</param>
internal readonly record struct SegmentIntersection(SegmentRelation Relation, Point2d FirstPoint, Point2d SecondPoint);

/// <summary>
/// The relate engine's segment-pair classifier: every decision is a sign from
/// <see cref="ExactOrientation"/> or an exact coordinate comparison — never a
/// parametric solve — so near-parallel instability cannot reach a topology
/// decision. The all-signs-zero collinear branch resolves by endpoint
/// containment tested on both ordinates, so vertical and horizontal runs need
/// no axis choice. Both segments must be non-degenerate (distinct endpoints);
/// zero-length segments reduce to point cases upstream.
/// </summary>
internal static class SegmentTopology
{
    /// <summary>
    /// Classifies the intersection of segment
    /// (<paramref name="firstStart"/>, <paramref name="firstEnd"/>) with
    /// segment (<paramref name="secondStart"/>, <paramref name="secondEnd"/>).
    /// </summary>
    public static SegmentIntersection Classify(Point2d firstStart, Point2d firstEnd, Point2d secondStart, Point2d secondEnd)
    {
        int startSide = ExactOrientation.Orient2D(firstStart, firstEnd, secondStart);
        int endSide = ExactOrientation.Orient2D(firstStart, firstEnd, secondEnd);

        if(startSide == 0 && endSide == 0)
        {
            return ClassifyCollinear(firstStart, firstEnd, secondStart, secondEnd);
        }

        int firstStartSide = ExactOrientation.Orient2D(secondStart, secondEnd, firstStart);
        int firstEndSide = ExactOrientation.Orient2D(secondStart, secondEnd, firstEnd);

        //A zero names its point as the only candidate contact: a straight
        //segment whose endpoint lies on the other's supporting line meets
        //that line nowhere else.
        if(startSide == 0)
        {
            if(WithinBox(secondStart, firstStart, firstEnd))
            {
                return new SegmentIntersection(SegmentRelation.VertexTouch, secondStart, default);
            }

            return new SegmentIntersection(SegmentRelation.Disjoint, default, default);
        }

        if(endSide == 0)
        {
            if(WithinBox(secondEnd, firstStart, firstEnd))
            {
                return new SegmentIntersection(SegmentRelation.VertexTouch, secondEnd, default);
            }

            return new SegmentIntersection(SegmentRelation.Disjoint, default, default);
        }

        if(firstStartSide == 0)
        {
            if(WithinBox(firstStart, secondStart, secondEnd))
            {
                return new SegmentIntersection(SegmentRelation.VertexTouch, firstStart, default);
            }

            return new SegmentIntersection(SegmentRelation.Disjoint, default, default);
        }

        if(firstEndSide == 0)
        {
            if(WithinBox(firstEnd, secondStart, secondEnd))
            {
                return new SegmentIntersection(SegmentRelation.VertexTouch, firstEnd, default);
            }

            return new SegmentIntersection(SegmentRelation.Disjoint, default, default);
        }

        if(startSide != endSide && firstStartSide != firstEndSide)
        {
            return new SegmentIntersection(SegmentRelation.ProperCrossing, CrossingPoint(firstStart, firstEnd, secondStart, secondEnd), default);
        }

        return new SegmentIntersection(SegmentRelation.Disjoint, default, default);
    }

    /// <summary>
    /// The collinear branch: the shared stretch is delimited by the endpoints
    /// of either segment that lie within the other, so its bounds are always
    /// original vertices. Zero distinct contained endpoints is disjoint, one
    /// is a vertex touch, two delimit the overlap.
    /// </summary>
    private static SegmentIntersection ClassifyCollinear(Point2d firstStart, Point2d firstEnd, Point2d secondStart, Point2d secondEnd)
    {
        Span<Point2d> contained = stackalloc Point2d[4];
        int count = 0;

        if(WithinBox(secondStart, firstStart, firstEnd))
        {
            count = AddDistinct(contained, count, secondStart);
        }

        if(WithinBox(secondEnd, firstStart, firstEnd))
        {
            count = AddDistinct(contained, count, secondEnd);
        }

        if(WithinBox(firstStart, secondStart, secondEnd))
        {
            count = AddDistinct(contained, count, firstStart);
        }

        if(WithinBox(firstEnd, secondStart, secondEnd))
        {
            count = AddDistinct(contained, count, firstEnd);
        }

        if(count == 0)
        {
            return new SegmentIntersection(SegmentRelation.Disjoint, default, default);
        }

        if(count == 1)
        {
            return new SegmentIntersection(SegmentRelation.VertexTouch, contained[0], default);
        }

        return new SegmentIntersection(SegmentRelation.CollinearOverlap, contained[0], contained[1]);
    }

    /// <summary>
    /// Whether <paramref name="point"/> lies within the axis-aligned box of
    /// the segment (<paramref name="start"/>, <paramref name="end"/>) — the
    /// containment half of a point-on-segment answer once collinearity is
    /// already established, tested on both ordinates with exact comparisons.
    /// </summary>
    private static bool WithinBox(Point2d point, Point2d start, Point2d end)
    {
        double minimumX = Math.Min(start.X, end.X);
        double maximumX = Math.Max(start.X, end.X);
        double minimumY = Math.Min(start.Y, end.Y);
        double maximumY = Math.Max(start.Y, end.Y);

        return point.X >= minimumX && point.X <= maximumX && point.Y >= minimumY && point.Y <= maximumY;
    }

    /// <summary>
    /// Appends <paramref name="candidate"/> unless an equal-valued point is
    /// already collected, returning the new count. At most two distinct
    /// points can accumulate for collinear segments.
    /// </summary>
    private static int AddDistinct(Span<Point2d> collected, int count, Point2d candidate)
    {
        for(int index = 0; index < count; index++)
        {
            if(collected[index].X == candidate.X && collected[index].Y == candidate.Y)
            {
                return count;
            }
        }

        if(count < collected.Length)
        {
            collected[count] = candidate;

            return count + 1;
        }

        return count;
    }

    /// <summary>
    /// The proper-crossing coordinate by the two-parameter solve, in plain
    /// double: it exists only to group intersections at a shared node — no
    /// sign is ever read from it, and the denominator cannot vanish because
    /// the caller established a strict straddle both ways.
    /// </summary>
    private static Point2d CrossingPoint(Point2d firstStart, Point2d firstEnd, Point2d secondStart, Point2d secondEnd)
    {
        double firstDeltaX = firstEnd.X - firstStart.X;
        double firstDeltaY = firstEnd.Y - firstStart.Y;
        double secondDeltaX = secondEnd.X - secondStart.X;
        double secondDeltaY = secondEnd.Y - secondStart.Y;
        double denominator = (firstDeltaX * secondDeltaY) - (firstDeltaY * secondDeltaX);
        double crossingParameter = (((secondStart.X - firstStart.X) * secondDeltaY) - ((secondStart.Y - firstStart.Y) * secondDeltaX)) / denominator;

        return new Point2d(
            firstStart.X + (crossingParameter * firstDeltaX),
            firstStart.Y + (crossingParameter * firstDeltaY));
    }
}
