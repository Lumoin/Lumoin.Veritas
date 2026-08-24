using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Planar-XY point-set distance between two <see cref="FlatGeometry"/> values in two
/// phases. First the containment pre-pass: when either operand has polygons, one
/// representative point per connected element of the other operand (a point member, a
/// curve's first vertex, a polygon shell's first vertex) is located against each
/// polygon, and any non-exterior hit decides distance zero — this is what makes
/// polygon interiors correct without an interior-distance concept. Then the facet
/// phase minimizes point–point, point–segment, and segment–segment distances over the
/// flattened vertex runs (polygon rings included) with envelope pre-rejection per part
/// pair and per segment pair and early exit at zero. Distance magnitudes are plain
/// double; only the crossing test's discrete side decision rides the exact-sign
/// determinant. Distance to the empty point set is undefined: any empty operand
/// answers false.
/// </summary>
public static class GeometryDistance
{
    /// <summary>
    /// Computes the distance between the two geometries' point sets; false when either
    /// operand is empty.
    /// </summary>
    public static bool TryCompute(in FlatGeometry first, in FlatGeometry second, out double distance)
    {
        if(first.IsEmpty || second.IsEmpty)
        {
            distance = 0;

            return false;
        }

        if(AnyRepresentativeInsidePolygons(in first, in second) || AnyRepresentativeInsidePolygons(in second, in first))
        {
            distance = 0;

            return true;
        }

        distance = FacetMinimum(in first, in second);

        return true;
    }

    /// <summary>
    /// Whether any representative point of <paramref name="other"/> locates non-exterior
    /// to some polygon of <paramref name="areal"/>.
    /// </summary>
    private static bool AnyRepresentativeInsidePolygons(in FlatGeometry areal, in FlatGeometry other)
    {
        foreach(FlatGeometryNode node in areal.Nodes)
        {
            if(node.Kind is not (GeometryKind.Polygon or GeometryKind.MultiPolygon))
            {
                continue;
            }

            int index = 0;

            while(index < node.PartCount)
            {
                int groupStart = index;
                index++;

                while(index < node.PartCount
                    && areal.Parts[node.FirstPart + index].Role == FlatGeometryPartRole.InteriorRing)
                {
                    index++;
                }

                if(AnyRepresentativeInsideGroup(in areal, node.FirstPart + groupStart, index - groupStart, in other))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Tests every connected-element representative of <paramref name="other"/> against one polygon group.</summary>
    private static bool AnyRepresentativeInsideGroup(
        in FlatGeometry areal, int groupFirstPart, int groupPartCount, in FlatGeometry other)
    {
        foreach(FlatGeometryNode node in other.Nodes)
        {
            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = other.Parts[node.FirstPart + index];

                //Interior rings belong to the polygon element its exterior ring already
                //represents; every other part role opens a connected element.
                if(part.Role == FlatGeometryPartRole.InteriorRing || part.Length == 0)
                {
                    continue;
                }

                Point2d representative = other.Vertices[part.Start];

                if(LocateInPolygonGroup(in areal, groupFirstPart, groupPartCount, representative) != PointLocation.Exterior)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Locates a point against one polygon group: the shell ring first, then its holes.</summary>
    private static PointLocation LocateInPolygonGroup(
        in FlatGeometry geometry, int groupFirstPart, int groupPartCount, Point2d point)
    {
        PointLocation shellLocation = LocateInRing(in geometry, geometry.Parts[groupFirstPart], point);

        if(shellLocation != PointLocation.Interior)
        {
            return shellLocation;
        }

        for(int index = 1; index < groupPartCount; index++)
        {
            PointLocation holeLocation = LocateInRing(in geometry, geometry.Parts[groupFirstPart + index], point);

            if(holeLocation == PointLocation.Boundary)
            {
                return PointLocation.Boundary;
            }

            if(holeLocation == PointLocation.Interior)
            {
                return PointLocation.Exterior;
            }
        }

        return PointLocation.Interior;
    }

    /// <summary>
    /// The crossing-number location of a point against one closed ring, counting
    /// upward edges by their starting endpoint and downward edges by their final one so
    /// shared vertices never double-count; the side-of-edge decision is exact.
    /// </summary>
    private static PointLocation LocateInRing(in FlatGeometry geometry, FlatGeometryPart ring, Point2d point)
    {
        int crossings = 0;

        for(int index = 1; index < ring.Length; index++)
        {
            Point2d segmentStart = geometry.Vertices[ring.Start + index - 1];
            Point2d segmentEnd = geometry.Vertices[ring.Start + index];

            if(segmentStart.X < point.X && segmentEnd.X < point.X)
            {
                continue;
            }

            if(point.X == segmentEnd.X && point.Y == segmentEnd.Y)
            {
                return PointLocation.Boundary;
            }

            if(segmentStart.Y == point.Y && segmentEnd.Y == point.Y)
            {
                double minX = Math.Min(segmentStart.X, segmentEnd.X);
                double maxX = Math.Max(segmentStart.X, segmentEnd.X);

                if(point.X >= minX && point.X <= maxX)
                {
                    return PointLocation.Boundary;
                }

                continue;
            }

            if((segmentStart.Y > point.Y && segmentEnd.Y <= point.Y)
                || (segmentEnd.Y > point.Y && segmentStart.Y <= point.Y))
            {
                int side = ExactSignDeterminant.SignOfDeterminant(
                    segmentEnd.X - segmentStart.X,
                    segmentEnd.Y - segmentStart.Y,
                    point.X - segmentStart.X,
                    point.Y - segmentStart.Y);

                if(side == 0)
                {
                    return PointLocation.Boundary;
                }

                if(segmentEnd.Y < segmentStart.Y)
                {
                    side = -side;
                }

                if(side > 0)
                {
                    crossings++;
                }
            }
        }

        return crossings % 2 == 1 ? PointLocation.Interior : PointLocation.Exterior;
    }

    /// <summary>The facet-phase minimum over runs and points of both operands.</summary>
    private static double FacetMinimum(in FlatGeometry first, in FlatGeometry second)
    {
        CollectFacets(in first, out FlatGeometryPart[] firstRuns, out FlatGeometryPart[] firstPoints, out double[] firstRunBounds, out double[] firstPointBounds);
        CollectFacets(in second, out FlatGeometryPart[] secondRuns, out FlatGeometryPart[] secondPoints, out double[] secondRunBounds, out double[] secondPointBounds);

        //The running minimum seeds at positive infinity and threads monotonically
        //through every loop; it is never reset per category.
        double minimum = double.PositiveInfinity;

        RunRunMinimum(in first, firstRuns, firstRunBounds, in second, secondRuns, secondRunBounds, ref minimum);
        RunPointMinimum(in first, firstRuns, firstRunBounds, in second, secondPoints, secondPointBounds, ref minimum);
        RunPointMinimum(in second, secondRuns, secondRunBounds, in first, firstPoints, firstPointBounds, ref minimum);
        PointPointMinimum(in first, firstPoints, in second, secondPoints, ref minimum);

        return minimum;
    }

    /// <summary>Splits an operand's parts into vertex runs and single points, folding each part's envelope once.</summary>
    private static void CollectFacets(
        in FlatGeometry geometry,
        out FlatGeometryPart[] runs,
        out FlatGeometryPart[] points,
        out double[] runBounds,
        out double[] pointBounds)
    {
        int runCount = 0;
        int pointCount = 0;

        foreach(FlatGeometryPart part in geometry.Parts)
        {
            if(part.Length >= 2)
            {
                runCount++;
            }
            else if(part.Length == 1)
            {
                pointCount++;
            }
        }

        runs = new FlatGeometryPart[runCount];
        points = new FlatGeometryPart[pointCount];
        runBounds = new double[runCount * 4];
        pointBounds = new double[pointCount * 4];
        int runIndex = 0;
        int pointIndex = 0;

        foreach(FlatGeometryPart part in geometry.Parts)
        {
            if(part.Length >= 2)
            {
                runs[runIndex] = part;
                FoldBounds(in geometry, part, runBounds.AsSpan(runIndex * 4, 4));
                runIndex++;
            }
            else if(part.Length == 1)
            {
                points[pointIndex] = part;
                FoldBounds(in geometry, part, pointBounds.AsSpan(pointIndex * 4, 4));
                pointIndex++;
            }
        }
    }

    /// <summary>Folds one part's envelope into a four-slot span: min X, min Y, max X, max Y.</summary>
    private static void FoldBounds(in FlatGeometry geometry, FlatGeometryPart part, Span<double> bounds)
    {
        bounds[0] = double.PositiveInfinity;
        bounds[1] = double.PositiveInfinity;
        bounds[2] = double.NegativeInfinity;
        bounds[3] = double.NegativeInfinity;

        for(int index = 0; index < part.Length; index++)
        {
            Point2d vertex = geometry.Vertices[part.Start + index];
            bounds[0] = Math.Min(bounds[0], vertex.X);
            bounds[1] = Math.Min(bounds[1], vertex.Y);
            bounds[2] = Math.Max(bounds[2], vertex.X);
            bounds[3] = Math.Max(bounds[3], vertex.Y);
        }
    }

    /// <summary>Minimizes segment–segment distances over every run pair with box pruning at both levels.</summary>
    private static void RunRunMinimum(
        in FlatGeometry first,
        FlatGeometryPart[] firstRuns,
        double[] firstRunBounds,
        in FlatGeometry second,
        FlatGeometryPart[] secondRuns,
        double[] secondRunBounds,
        ref double minimum)
    {
        for(int firstIndex = 0; firstIndex < firstRuns.Length; firstIndex++)
        {
            for(int secondIndex = 0; secondIndex < secondRuns.Length; secondIndex++)
            {
                if(BoxDistance(firstRunBounds.AsSpan(firstIndex * 4, 4), secondRunBounds.AsSpan(secondIndex * 4, 4)) > minimum)
                {
                    continue;
                }

                FlatGeometryPart firstRun = firstRuns[firstIndex];
                FlatGeometryPart secondRun = secondRuns[secondIndex];

                for(int segmentIndex = 1; segmentIndex < firstRun.Length; segmentIndex++)
                {
                    Point2d a = first.Vertices[firstRun.Start + segmentIndex - 1];
                    Point2d b = first.Vertices[firstRun.Start + segmentIndex];

                    if(SegmentBoxDistance(a, b, secondRunBounds.AsSpan(secondIndex * 4, 4)) > minimum)
                    {
                        continue;
                    }

                    for(int otherIndex = 1; otherIndex < secondRun.Length; otherIndex++)
                    {
                        Point2d c = second.Vertices[secondRun.Start + otherIndex - 1];
                        Point2d d = second.Vertices[secondRun.Start + otherIndex];
                        double candidate = SegmentToSegment(a, b, c, d);

                        if(candidate < minimum)
                        {
                            minimum = candidate;

                            if(minimum == 0)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Minimizes point–segment distances of one operand's runs against the other's points.</summary>
    private static void RunPointMinimum(
        in FlatGeometry runOperand,
        FlatGeometryPart[] runs,
        double[] runBounds,
        in FlatGeometry pointOperand,
        FlatGeometryPart[] points,
        double[] pointBounds,
        ref double minimum)
    {
        for(int runIndex = 0; runIndex < runs.Length; runIndex++)
        {
            for(int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                if(BoxDistance(runBounds.AsSpan(runIndex * 4, 4), pointBounds.AsSpan(pointIndex * 4, 4)) > minimum)
                {
                    continue;
                }

                Point2d point = pointOperand.Vertices[points[pointIndex].Start];
                FlatGeometryPart run = runs[runIndex];

                for(int segmentIndex = 1; segmentIndex < run.Length; segmentIndex++)
                {
                    Point2d a = runOperand.Vertices[run.Start + segmentIndex - 1];
                    Point2d b = runOperand.Vertices[run.Start + segmentIndex];
                    double candidate = PointToSegment(point, a, b);

                    if(candidate < minimum)
                    {
                        minimum = candidate;

                        if(minimum == 0)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Minimizes point–point distances.</summary>
    private static void PointPointMinimum(
        in FlatGeometry first, FlatGeometryPart[] firstPoints, in FlatGeometry second, FlatGeometryPart[] secondPoints, ref double minimum)
    {
        foreach(FlatGeometryPart firstPart in firstPoints)
        {
            foreach(FlatGeometryPart secondPart in secondPoints)
            {
                Point2d a = first.Vertices[firstPart.Start];
                Point2d b = second.Vertices[secondPart.Start];
                double candidate = double.Hypot(a.X - b.X, a.Y - b.Y);

                if(candidate < minimum)
                {
                    minimum = candidate;

                    if(minimum == 0)
                    {
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Point–segment distance: the projection factor clamps to the endpoints outside
    /// [0, 1], otherwise the perpendicular offset decides.
    /// </summary>
    internal static double PointToSegment(Point2d point, Point2d a, Point2d b)
    {
        if(a.X == b.X && a.Y == b.Y)
        {
            return double.Hypot(point.X - a.X, point.Y - a.Y);
        }

        double lengthSquared = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        double projection = ((point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y)) / lengthSquared;

        if(projection <= 0.0)
        {
            return double.Hypot(point.X - a.X, point.Y - a.Y);
        }

        if(projection >= 1.0)
        {
            return double.Hypot(point.X - b.X, point.Y - b.Y);
        }

        double offset = ((a.Y - point.Y) * (b.X - a.X) - (a.X - point.X) * (b.Y - a.Y)) / lengthSquared;

        return Math.Abs(offset) * Math.Sqrt(lengthSquared);
    }

    /// <summary>
    /// Segment–segment distance: zero-length operands reduce to the point case; the
    /// two-parameter line solve answers zero for an in-range crossing, and every other
    /// configuration — parallel included — is the minimum of the four
    /// endpoint-to-opposite-segment distances.
    /// </summary>
    internal static double SegmentToSegment(Point2d a, Point2d b, Point2d c, Point2d d)
    {
        if(a.X == b.X && a.Y == b.Y)
        {
            return PointToSegment(a, c, d);
        }

        if(c.X == d.X && c.Y == d.Y)
        {
            return PointToSegment(d, a, b);
        }

        bool noIntersection = false;

        if(!EnvelopesIntersect(a, b, c, d))
        {
            noIntersection = true;
        }
        else
        {
            double denominator = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);

            if(denominator == 0.0)
            {
                noIntersection = true;
            }
            else
            {
                double rNumerator = (a.Y - c.Y) * (d.X - c.X) - (a.X - c.X) * (d.Y - c.Y);
                double sNumerator = (a.Y - c.Y) * (b.X - a.X) - (a.X - c.X) * (b.Y - a.Y);
                double r = rNumerator / denominator;
                double s = sNumerator / denominator;

                if(r < 0 || r > 1 || s < 0 || s > 1)
                {
                    noIntersection = true;
                }
            }
        }

        if(noIntersection)
        {
            return Math.Min(
                Math.Min(PointToSegment(a, c, d), PointToSegment(b, c, d)),
                Math.Min(PointToSegment(c, a, b), PointToSegment(d, a, b)));
        }

        return 0.0;
    }

    /// <summary>Whether the axis-aligned boxes of the two segments overlap.</summary>
    private static bool EnvelopesIntersect(Point2d a, Point2d b, Point2d c, Point2d d)
    {
        return Math.Min(a.X, b.X) <= Math.Max(c.X, d.X)
            && Math.Max(a.X, b.X) >= Math.Min(c.X, d.X)
            && Math.Min(a.Y, b.Y) <= Math.Max(c.Y, d.Y)
            && Math.Max(a.Y, b.Y) >= Math.Min(c.Y, d.Y);
    }

    /// <summary>The distance between two four-slot envelopes; zero when they overlap.</summary>
    private static double BoxDistance(ReadOnlySpan<double> first, ReadOnlySpan<double> second)
    {
        double dx = 0;

        if(first[2] < second[0])
        {
            dx = second[0] - first[2];
        }
        else if(second[2] < first[0])
        {
            dx = first[0] - second[2];
        }

        double dy = 0;

        if(first[3] < second[1])
        {
            dy = second[1] - first[3];
        }
        else if(second[3] < first[1])
        {
            dy = first[1] - second[3];
        }

        return double.Hypot(dx, dy);
    }

    /// <summary>The distance from one segment's box to a four-slot envelope.</summary>
    private static double SegmentBoxDistance(Point2d a, Point2d b, ReadOnlySpan<double> bounds)
    {
        Span<double> segmentBounds =
        [
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y),
        ];

        return BoxDistance(segmentBounds, bounds);
    }

    /// <summary>Where a point lies relative to a ring or polygon.</summary>
    private enum PointLocation
    {
        /// <summary>Strictly inside.</summary>
        Interior,

        /// <summary>Exactly on a ring.</summary>
        Boundary,

        /// <summary>Strictly outside.</summary>
        Exterior,
    }
}
