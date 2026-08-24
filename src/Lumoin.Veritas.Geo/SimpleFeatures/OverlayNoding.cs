using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>One input segment of the overlay noding pass, with its labeling context.</summary>
/// <param name="Operand">Which operand contributed the segment: 0 or 1.</param>
/// <param name="Part">The contributing part's index in its operand.</param>
/// <param name="Start">The original start vertex.</param>
/// <param name="End">The original end vertex.</param>
/// <param name="IsBoundary">Whether the segment is an areal ring edge rather than a line edge.</param>
/// <param name="InteriorOnLeft">For a ring edge, whether the polygon interior lies left of travel.</param>
/// <param name="IsHoleRing">For a ring edge, whether the ring plays the interior-ring role.</param>
/// <param name="IsCollapsedRing">For a ring edge, whether its ring is degenerate (zero area).</param>
internal readonly record struct OverlaySegment(
    int Operand,
    int Part,
    Point2d Start,
    Point2d End,
    bool IsBoundary,
    bool InteriorOnLeft,
    bool IsHoleRing,
    bool IsCollapsedRing);

/// <summary>
/// The merged-operand noding pass of the constructive set: every segment of both
/// operands is classified pairwise (cross-operand and same-operand — an operand's own
/// self-intersections node too), split points accumulate per source segment ordered
/// along it, and every segment rewrites into split edges. One crossing computation
/// serves node identity and the emitted vertex; candidate pairs order by an
/// operand-independent key before classification so the coordinate is a function of
/// the unordered pair; node identity is value equality with signed zeros normalized
/// to positive zero. The validation scan re-checks the split edges at their
/// represented coordinates and reports rather than emitting a broken arrangement.
/// </summary>
internal static class OverlayNoding
{
    /// <summary>
    /// Collects the noding segments of one non-collection operand: line parts from
    /// lineal kinds, ring edges from areal kinds with the interior side resolved
    /// from ring orientation combined with ring role, zero-length segments skipped.
    /// </summary>
    public static void CollectSegments(in FlatGeometry geometry, int operand, List<OverlaySegment> segments)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        for(int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            FlatGeometryPart part = parts[partIndex];

            if(part.Role is FlatGeometryPartRole.Point || part.Length < 2)
            {
                continue;
            }

            bool isBoundary = part.Role is FlatGeometryPartRole.ExteriorRing or FlatGeometryPartRole.InteriorRing;
            bool isHole = part.Role is FlatGeometryPartRole.InteriorRing;
            bool interiorOnLeft = false;
            bool collapsed = false;

            if(isBoundary)
            {
                int orientation = RingGeometry.Orientation(vertices.Slice(part.Start, part.Length));

                if(orientation == 0)
                {
                    collapsed = true;
                }
                else
                {
                    //A shell's interior lies on its bounded side, a hole's
                    //polygon-interior on its unbounded side: counter-clockwise
                    //travel bounds on the left, so the interior side is left
                    //exactly when orientation and role agree.
                    interiorOnLeft = isHole ? orientation < 0 : orientation > 0;
                }
            }

            for(int vertexIndex = 1; vertexIndex < part.Length; vertexIndex++)
            {
                Point2d start = vertices[part.Start + vertexIndex - 1];
                Point2d end = vertices[part.Start + vertexIndex];

                if(start.X == end.X && start.Y == end.Y)
                {
                    continue;
                }

                segments.Add(new OverlaySegment(operand, partIndex, start, end, isBoundary, interiorOnLeft, isHole, collapsed));
            }
        }
    }

    /// <summary>
    /// Nodes the merged segment set and emits split edges into the graph. Returns
    /// false when the validation scan finds the represented arrangement is not a
    /// planar subdivision — the honest refusal, never a broken result.
    /// </summary>
    public static bool TryBuildGraph(List<OverlaySegment> segments, OverlayGraph graph)
    {
        var splits = new List<Point2d>[segments.Count];
        double[] bounds = BuildSegmentBounds(segments);

        for(int first = 0; first < segments.Count; first++)
        {
            for(int second = first + 1; second < segments.Count; second++)
            {
                if(!BoxesOverlap(bounds, first, second))
                {
                    continue;
                }

                ClassifyPair(segments, splits, first, second);
            }
        }

        var edges = new List<(int Segment, Point2d Start, Point2d End)>();

        for(int index = 0; index < segments.Count; index++)
        {
            AppendSplitEdges(segments[index], index, splits[index], edges);
        }

        if(!ValidateNoded(edges))
        {
            return false;
        }

        foreach((int segmentIndex, Point2d start, Point2d end) in edges)
        {
            graph.AddEdge(segments[segmentIndex], start, end);
        }

        return true;
    }

    /// <summary>Normalizes a computed or shared node coordinate: signed zeros collapse to positive zero.</summary>
    public static Point2d NormalizeNode(Point2d point)
    {
        return new Point2d(point.X + 0.0, point.Y + 0.0);
    }

    /// <summary>
    /// Classifies one candidate pair and records its split points. The pair enters
    /// classification in an operand-independent order — lexicographic on the two
    /// segments' original endpoints — so the crossing coordinate is a function of
    /// the unordered pair and commutativity stays bitwise.
    /// </summary>
    private static void ClassifyPair(List<OverlaySegment> segments, List<Point2d>[] splits, int first, int second)
    {
        OverlaySegment left = segments[first];
        OverlaySegment right = segments[second];
        bool swap = ComparePairKey(left, right) > 0;
        OverlaySegment lower = swap ? right : left;
        OverlaySegment upper = swap ? left : right;

        SegmentIntersection intersection = SegmentTopology.Classify(lower.Start, lower.End, upper.Start, upper.End);

        if(intersection.Relation == SegmentRelation.Disjoint)
        {
            return;
        }

        if(intersection.Relation == SegmentRelation.ProperCrossing)
        {
            Point2d crossing = NormalizeNode(ClampIntoBoxes(intersection.FirstPoint, lower, upper));
            RecordSplit(splits, first, segments[first], crossing);
            RecordSplit(splits, second, segments[second], crossing);

            return;
        }

        Point2d touch = NormalizeNode(intersection.FirstPoint);
        RecordSplit(splits, first, segments[first], touch);
        RecordSplit(splits, second, segments[second], touch);

        if(intersection.Relation == SegmentRelation.CollinearOverlap)
        {
            Point2d overlapEnd = NormalizeNode(intersection.SecondPoint);
            RecordSplit(splits, first, segments[first], overlapEnd);
            RecordSplit(splits, second, segments[second], overlapEnd);
        }
    }

    /// <summary>The operand-independent pair key: lexicographic on original endpoints.</summary>
    private static int ComparePairKey(OverlaySegment first, OverlaySegment second)
    {
        int start = ComparePoints(first.Start, second.Start);

        if(start != 0)
        {
            return start;
        }

        return ComparePoints(first.End, second.End);
    }

    /// <summary>Lexicographic point order: X first, then Y.</summary>
    private static int ComparePoints(Point2d first, Point2d second)
    {
        if(first.X < second.X)
        {
            return -1;
        }

        if(first.X > second.X)
        {
            return 1;
        }

        if(first.Y < second.Y)
        {
            return -1;
        }

        return first.Y > second.Y ? 1 : 0;
    }

    /// <summary>
    /// Clamps a computed crossing into the intersection of both segments' boxes: a
    /// point outside either box is a certain rounding artifact, and the clamp keeps
    /// the dominant-ordinate split key a total order along each parent. It does not
    /// certify the true crossing order — the recorded fidelity limit.
    /// </summary>
    private static Point2d ClampIntoBoxes(Point2d point, OverlaySegment first, OverlaySegment second)
    {
        double minimumX = Math.Max(Math.Min(first.Start.X, first.End.X), Math.Min(second.Start.X, second.End.X));
        double maximumX = Math.Min(Math.Max(first.Start.X, first.End.X), Math.Max(second.Start.X, second.End.X));
        double minimumY = Math.Max(Math.Min(first.Start.Y, first.End.Y), Math.Min(second.Start.Y, second.End.Y));
        double maximumY = Math.Min(Math.Max(first.Start.Y, first.End.Y), Math.Max(second.Start.Y, second.End.Y));

        return new Point2d(
            Math.Clamp(point.X, minimumX, maximumX),
            Math.Clamp(point.Y, minimumY, maximumY));
    }

    /// <summary>Records one split point on a segment unless it coincides with an endpoint.</summary>
    private static void RecordSplit(List<Point2d>[] splits, int index, OverlaySegment segment, Point2d point)
    {
        if((point.X == segment.Start.X && point.Y == segment.Start.Y)
            || (point.X == segment.End.X && point.Y == segment.End.Y))
        {
            return;
        }

        splits[index] ??= [];
        splits[index].Add(point);
    }

    /// <summary>
    /// Rewrites one segment into its split edges: split coordinates order along the
    /// parent's dominant axis in the parent's own direction by exact ordinate
    /// comparison, duplicates collapse by value equality, and the chain runs from
    /// the original start through the splits to the original end.
    /// </summary>
    private static void AppendSplitEdges(
        OverlaySegment segment,
        int segmentIndex,
        List<Point2d>? segmentSplits,
        List<(int Segment, Point2d Start, Point2d End)> edges)
    {
        if(segmentSplits is null || segmentSplits.Count == 0)
        {
            edges.Add((segmentIndex, segment.Start, segment.End));

            return;
        }

        double deltaX = segment.End.X - segment.Start.X;
        double deltaY = segment.End.Y - segment.Start.Y;
        bool dominantX = Math.Abs(deltaX) >= Math.Abs(deltaY);
        bool ascending = dominantX ? deltaX > 0 : deltaY > 0;

        segmentSplits.Sort((first, second) =>
        {
            double firstKey = dominantX ? first.X : first.Y;
            double secondKey = dominantX ? second.X : second.Y;
            int order = firstKey < secondKey ? -1 : firstKey > secondKey ? 1 : 0;

            return ascending ? order : -order;
        });

        Point2d previous = segment.Start;

        foreach(Point2d split in segmentSplits)
        {
            if(split.X == previous.X && split.Y == previous.Y)
            {
                continue;
            }

            edges.Add((segmentIndex, previous, split));
            previous = split;
        }

        if(previous.X != segment.End.X || previous.Y != segment.End.Y)
        {
            edges.Add((segmentIndex, previous, segment.End));
        }
    }

    /// <summary>
    /// The honesty gate: re-scans the split edges at their represented coordinates
    /// for any residual proper crossing, interior touch, or collinear overlap
    /// between edges that do not share an endpoint (edges sharing an endpoint can
    /// only offend by overlapping collinearly). A residual means the represented
    /// arrangement is not a planar subdivision.
    /// </summary>
    public static bool ValidateNoded(List<(int Segment, Point2d Start, Point2d End)> edges)
    {
        for(int first = 0; first < edges.Count; first++)
        {
            for(int second = first + 1; second < edges.Count; second++)
            {
                (int _, Point2d firstStart, Point2d firstEnd) = edges[first];
                (int _, Point2d secondStart, Point2d secondEnd) = edges[second];

                if(Math.Max(Math.Min(firstStart.X, firstEnd.X), Math.Min(secondStart.X, secondEnd.X))
                    > Math.Min(Math.Max(firstStart.X, firstEnd.X), Math.Max(secondStart.X, secondEnd.X))
                    || Math.Max(Math.Min(firstStart.Y, firstEnd.Y), Math.Min(secondStart.Y, secondEnd.Y))
                    > Math.Min(Math.Max(firstStart.Y, firstEnd.Y), Math.Max(secondStart.Y, secondEnd.Y)))
                {
                    continue;
                }

                bool identical =
                    (PointsEqual(firstStart, secondStart) && PointsEqual(firstEnd, secondEnd))
                    || (PointsEqual(firstStart, secondEnd) && PointsEqual(firstEnd, secondStart));

                if(identical)
                {
                    //Coincident duplicates are legitimate: the graph merges them
                    //into one edge carrying both contributions.
                    continue;
                }

                bool sharesEndpoint =
                    PointsEqual(firstStart, secondStart) || PointsEqual(firstStart, secondEnd)
                    || PointsEqual(firstEnd, secondStart) || PointsEqual(firstEnd, secondEnd);

                SegmentIntersection intersection = SegmentTopology.Classify(firstStart, firstEnd, secondStart, secondEnd);

                if(intersection.Relation == SegmentRelation.Disjoint)
                {
                    continue;
                }

                if(intersection.Relation == SegmentRelation.CollinearOverlap)
                {
                    return false;
                }

                if(!sharesEndpoint)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Value equality on both ordinates.</summary>
    private static bool PointsEqual(Point2d first, Point2d second)
    {
        return first.X == second.X && first.Y == second.Y;
    }

    /// <summary>The flat per-segment box table for the pairwise scans.</summary>
    private static double[] BuildSegmentBounds(List<OverlaySegment> segments)
    {
        var bounds = new double[segments.Count * 4];

        for(int index = 0; index < segments.Count; index++)
        {
            OverlaySegment segment = segments[index];
            bounds[(index * 4) + 0] = Math.Min(segment.Start.X, segment.End.X);
            bounds[(index * 4) + 1] = Math.Min(segment.Start.Y, segment.End.Y);
            bounds[(index * 4) + 2] = Math.Max(segment.Start.X, segment.End.X);
            bounds[(index * 4) + 3] = Math.Max(segment.Start.Y, segment.End.Y);
        }

        return bounds;
    }

    /// <summary>Whether two segments' boxes overlap or touch.</summary>
    private static bool BoxesOverlap(double[] bounds, int first, int second)
    {
        return bounds[(first * 4) + 0] <= bounds[(second * 4) + 2]
            && bounds[(second * 4) + 0] <= bounds[(first * 4) + 2]
            && bounds[(first * 4) + 1] <= bounds[(second * 4) + 3]
            && bounds[(second * 4) + 1] <= bounds[(first * 4) + 3];
    }
}
