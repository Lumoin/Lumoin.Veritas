using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Buffer's raw-curve generator: every operand run becomes closed loops traversed
/// with the covered region on the left of travel — an open run's capsule is the
/// forward pass offset to its right, a round end cap, the reverse pass offset to
/// its right, and the closing cap; a closed run yields the outer and inner loops
/// of its tube with no caps; a point becomes one full circular fillet. Left turns
/// take counter-clockwise round fillets, right turns connect directly (the raw
/// curve may self-intersect freely — the depth extraction resolves it). Arcs
/// tessellate at the quadrant-segments quantum with the four cardinal directions
/// snapped to exact ordinates.
/// </summary>
internal static class OffsetCurveBuilder
{
    /// <summary>Appends the full-circle loop of a point operand.</summary>
    public static void AddPointCircle(Point2d center, double distance, int quadrantSegments, List<Point2d[]> loops)
    {
        int steps = quadrantSegments * 4;
        var loop = new Point2d[steps + 1];

        for(int step = 0; step < steps; step++)
        {
            loop[step] = CirclePoint(center, distance, step, quadrantSegments);
        }

        loop[steps] = loop[0];
        loops.Add(loop);
    }

    /// <summary>
    /// Appends the loops of one run: the capsule loop for an open run, the outer
    /// and inner tube loops for a closed one. The run must carry at least one
    /// positive-length segment; consecutive duplicate positions are skipped.
    /// </summary>
    public static void AddRunLoops(ReadOnlySpan<Point2d> run, double distance, int quadrantSegments, List<Point2d[]> loops)
    {
        var cleaned = new List<Point2d>(run.Length);

        foreach(Point2d position in run)
        {
            if(cleaned.Count == 0
                || cleaned[cleaned.Count - 1].X != position.X
                || cleaned[cleaned.Count - 1].Y != position.Y)
            {
                cleaned.Add(position);
            }
        }

        bool closed = cleaned.Count > 3
            && cleaned[0].X == cleaned[cleaned.Count - 1].X
            && cleaned[0].Y == cleaned[cleaned.Count - 1].Y;

        if(closed)
        {
            loops.Add(BuildClosedPass(cleaned, distance, quadrantSegments));
            cleaned.Reverse();
            loops.Add(BuildClosedPass(cleaned, distance, quadrantSegments));

            return;
        }

        loops.Add(BuildCapsule(cleaned, distance, quadrantSegments));
    }

    /// <summary>The capsule loop of an open run: two right-offset passes joined by round caps.</summary>
    private static Point2d[] BuildCapsule(List<Point2d> run, double distance, int quadrantSegments)
    {
        var loop = new List<Point2d>();
        AppendPass(loop, run, forward: true, distance, quadrantSegments);
        AppendCap(loop, run[run.Count - 1], run[run.Count - 2], distance, quadrantSegments);
        AppendPass(loop, run, forward: false, distance, quadrantSegments);
        AppendCap(loop, run[0], run[1], distance, quadrantSegments);
        loop.Add(loop[0]);

        return [.. loop];
    }

    /// <summary>One closed tube loop: the right-offset pass of a closed run, joins included.</summary>
    private static Point2d[] BuildClosedPass(List<Point2d> ring, double distance, int quadrantSegments)
    {
        //The closed pass walks every segment of the ring and joins each pair of
        //consecutive segments, the wrap-around join included; the loop closes on
        //its own first offset point.
        var loop = new List<Point2d>();
        int segmentCount = ring.Count - 1;

        for(int index = 0; index < segmentCount; index++)
        {
            Point2d start = ring[index];
            Point2d end = ring[index + 1];
            AppendOffsetSegment(loop, start, end, distance);

            Point2d next = ring[((index + 1) % segmentCount) + 1];
            AppendJoin(loop, end, start, next, distance, quadrantSegments);
        }

        loop.Add(loop[0]);

        return [.. loop];
    }

    /// <summary>Appends one direction's offset pass over the run with its interior joins.</summary>
    private static void AppendPass(List<Point2d> loop, List<Point2d> run, bool forward, double distance, int quadrantSegments)
    {
        int count = run.Count;

        for(int step = 0; step < count - 1; step++)
        {
            Point2d start = forward ? run[step] : run[count - 1 - step];
            Point2d end = forward ? run[step + 1] : run[count - 2 - step];
            AppendOffsetSegment(loop, start, end, distance);

            if(step < count - 2)
            {
                Point2d next = forward ? run[step + 2] : run[count - 3 - step];
                AppendJoin(loop, end, start, next, distance, quadrantSegments);
            }
        }
    }

    /// <summary>Appends the right-offset endpoints of one segment.</summary>
    private static void AppendOffsetSegment(List<Point2d> loop, Point2d start, Point2d end, double distance)
    {
        (double normalX, double normalY) = RightNormal(start, end, distance);
        loop.Add(new Point2d(start.X + normalX, start.Y + normalY));
        loop.Add(new Point2d(end.X + normalX, end.Y + normalY));
    }

    /// <summary>
    /// Appends the join at a shared vertex: a counter-clockwise fillet on a left
    /// turn (the outside of the right-offset curve), nothing on a right turn or a
    /// straight continuation — the pass's next offset point connects directly.
    /// </summary>
    private static void AppendJoin(List<Point2d> loop, Point2d corner, Point2d previous, Point2d next, double distance, int quadrantSegments)
    {
        if(ExactOrientation.Orient2D(previous, corner, next) <= 0)
        {
            return;
        }

        (double fromX, double fromY) = RightNormal(previous, corner, distance);
        (double toX, double toY) = RightNormal(corner, next, distance);
        AppendFillet(loop, corner, fromX, fromY, toX, toY, distance, quadrantSegments);
    }

    /// <summary>Appends the round cap at a run end, sweeping counter-clockwise through the run direction.</summary>
    private static void AppendCap(List<Point2d> loop, Point2d end, Point2d before, double distance, int quadrantSegments)
    {
        (double fromX, double fromY) = RightNormal(before, end, distance);
        AppendFillet(loop, end, fromX, fromY, -fromX, -fromY, distance, quadrantSegments);
    }

    /// <summary>
    /// Appends the interior points of a counter-clockwise arc around the center
    /// from one offset vector to another at the fillet quantum; the arc endpoints
    /// themselves are the passes' own offset points.
    /// </summary>
    private static void AppendFillet(
        List<Point2d> loop,
        Point2d center,
        double fromX,
        double fromY,
        double toX,
        double toY,
        double distance,
        int quadrantSegments)
    {
        double startAngle = Math.Atan2(fromY, fromX);
        double endAngle = Math.Atan2(toY, toX);

        if(endAngle <= startAngle)
        {
            endAngle += 2 * Math.PI;
        }

        double quantum = (Math.PI / 2) / quadrantSegments;
        double sweep = endAngle - startAngle;
        int steps = (int)((sweep / quantum) + 0.5);

        for(int step = 1; step < steps; step++)
        {
            double angle = startAngle + (sweep * step / steps);
            loop.Add(new Point2d(
                center.X + (distance * Math.Cos(angle)),
                center.Y + (distance * Math.Sin(angle))));
        }
    }

    /// <summary>
    /// One circle vertex at the given step of the full tessellation: the four cardinal steps
    /// answer exact ordinates, every other step is a plain trigonometric sample — not an exact
    /// value, which is why a caller needing certified coverage verifies the emitted ring
    /// instead of trusting the sampling.
    /// </summary>
    internal static Point2d CirclePoint(Point2d center, double distance, int step, int quadrantSegments)
    {
        if(step == 0)
        {
            return new Point2d(center.X + distance, center.Y);
        }

        if(step == quadrantSegments)
        {
            return new Point2d(center.X, center.Y + distance);
        }

        if(step == quadrantSegments * 2)
        {
            return new Point2d(center.X - distance, center.Y);
        }

        if(step == quadrantSegments * 3)
        {
            return new Point2d(center.X, center.Y - distance);
        }

        double angle = (Math.PI / 2) * step / quadrantSegments;

        return new Point2d(
            center.X + (distance * Math.Cos(angle)),
            center.Y + (distance * Math.Sin(angle)));
    }

    /// <summary>The right normal of a segment direction, scaled to the distance.</summary>
    private static (double X, double Y) RightNormal(Point2d start, Point2d end, double distance)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

        return (distance * deltaY / length, distance * -deltaX / length);
    }
}
