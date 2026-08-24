using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The shared exact ring utilities of the constructive set: ring orientation by the
/// extreme-vertex corner sign and the even-odd containment probe, both over a plain
/// closed-ring span (first and last positions coincide) so operand rings and freshly
/// computed result rings read through one implementation. Every sign routes through
/// <see cref="ExactOrientation"/>; the answers are exact for the coordinates given —
/// for computed rings that means exact at the represented coordinates, with fidelity
/// to the real arrangement bounded by the noding stage's recorded grouping limit:
/// computed crossing coordinates serve only as node identities, never as
/// topology-deciding signs.
/// </summary>
internal static class RingGeometry
{
    /// <summary>
    /// Orients one closed ring by the extreme-vertex method: the exact orientation
    /// of the corner at the lexicographically smallest vertex — no summed area, so
    /// no unbounded exact arithmetic. Positive is counter-clockwise; zero means
    /// degenerate (fewer than three distinct positions, or all collinear).
    /// </summary>
    public static int Orientation(ReadOnlySpan<Point2d> ring)
    {
        int distinctCount = ring.Length - 1;

        if(distinctCount < 3)
        {
            return 0;
        }

        int extremeOffset = 0;

        for(int offset = 1; offset < distinctCount; offset++)
        {
            Point2d candidate = ring[offset];
            Point2d extreme = ring[extremeOffset];

            if(candidate.X < extreme.X || (candidate.X == extreme.X && candidate.Y < extreme.Y))
            {
                extremeOffset = offset;
            }
        }

        Point2d corner = ring[extremeOffset];
        Point2d previous = corner;
        Point2d next = corner;

        for(int step = 1; step < distinctCount; step++)
        {
            Point2d candidate = ring[(((extremeOffset - step) + (step * distinctCount)) % distinctCount)];

            if(candidate.X != corner.X || candidate.Y != corner.Y)
            {
                previous = candidate;

                break;
            }
        }

        for(int step = 1; step < distinctCount; step++)
        {
            Point2d candidate = ring[((extremeOffset + step) % distinctCount)];

            if(candidate.X != corner.X || candidate.Y != corner.Y)
            {
                next = candidate;

                break;
            }
        }

        return ExactOrientation.Orient2D(previous, corner, next);
    }

    /// <summary>
    /// The even-odd crossing-number location of a point against one closed ring:
    /// vertex coincidence and on-segment answer boundary; otherwise the rightward
    /// ray parity decides, with every side-of-edge sign routed through the exact
    /// orientation.
    /// </summary>
    public static PointPlacement LocateInRing(Point2d point, ReadOnlySpan<Point2d> ring)
    {
        int crossings = 0;

        for(int vertexIndex = 1; vertexIndex < ring.Length; vertexIndex++)
        {
            Point2d start = ring[vertexIndex - 1];
            Point2d end = ring[vertexIndex];

            if(start.X == end.X && start.Y == end.Y)
            {
                if(point.X == start.X && point.Y == start.Y)
                {
                    return PointPlacement.Boundary;
                }

                continue;
            }

            if(OnSegment(point, start, end))
            {
                return PointPlacement.Boundary;
            }

            bool startAbove = start.Y > point.Y;
            bool endAbove = end.Y > point.Y;

            if(startAbove == endAbove)
            {
                continue;
            }

            //Direct the straddling edge upward; a strict left side means the
            //edge passes to the right of the point, crossing the +X ray.
            int side = endAbove
                ? ExactOrientation.Orient2D(start, end, point)
                : ExactOrientation.Orient2D(end, start, point);

            if(side < 0)
            {
                crossings++;
            }
        }

        return crossings % 2 == 1 ? PointPlacement.Interior : PointPlacement.Exterior;
    }

    /// <summary>
    /// Whether the closed ring is collapsed — fewer than four positions, fewer than
    /// three distinct ones, or an all-collinear run: exactly the rings the
    /// constructive assembly prunes rather than emits.
    /// </summary>
    public static bool IsCollapsed(ReadOnlySpan<Point2d> ring)
    {
        return ring.Length < 4 || Orientation(ring) == 0;
    }

    /// <summary>
    /// Whether the point lies on the closed segment: exact collinearity plus
    /// containment in the segment's box on both ordinates.
    /// </summary>
    private static bool OnSegment(Point2d point, Point2d start, Point2d end)
    {
        if(ExactOrientation.Orient2D(start, end, point) != 0)
        {
            return false;
        }

        double minimumX = Math.Min(start.X, end.X);
        double maximumX = Math.Max(start.X, end.X);
        double minimumY = Math.Min(start.Y, end.Y);
        double maximumY = Math.Max(start.Y, end.Y);

        return point.X >= minimumX && point.X <= maximumX && point.Y >= minimumY && point.Y <= maximumY;
    }
}
