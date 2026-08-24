using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The simplicity answer per geometry kind, total over every input: empties
/// are vacuously simple; a point set is simple without duplicate positions; a
/// curve is simple without self-intersection beyond a closed curve's start
/// and end; a multi-curve additionally confines inter-member contact to
/// points on the boundary of both members, evaluated per member; polygonal
/// geometries answer through every ring's simplicity as a closed curve — so
/// a self-crossing ring answers false honestly; a collection is simple when
/// every member is, with the member-pairwise arrangement deliberately
/// unexamined until the overlay rung exists. All intersection decisions ride
/// the exact segment classifier; coincidence is value equality on the
/// ordinates.
/// </summary>
public static class GeometrySimplicity
{
    /// <summary>
    /// Whether the geometry is simple. Total: every input, including empties
    /// and collections, has a defined answer.
    /// </summary>
    public static bool IsSimple(in FlatGeometry geometry)
    {
        ReadOnlySpan<FlatGeometryNode> nodes = geometry.Nodes;

        if(nodes.Length == 0)
        {
            return true;
        }

        return NodeIsSimple(geometry, 0);
    }

    /// <summary>Answers one node of the tree, recursing through collections.</summary>
    private static bool NodeIsSimple(in FlatGeometry geometry, int nodeIndex)
    {
        FlatGeometryNode node = geometry.Nodes[nodeIndex];

        switch(node.Kind)
        {
            case GeometryKind.Point:
                return true;
            case GeometryKind.MultiPoint:
                return HasNoDuplicatePositions(geometry, node);
            case GeometryKind.LineString:
            case GeometryKind.MultiLineString:
                return LinealIsSimple(geometry, node);
            case GeometryKind.Polygon:
            case GeometryKind.MultiPolygon:
                return EveryRingIsSimple(geometry, node);
            default:
                for(int childOffset = 0; childOffset < node.ChildCount; childOffset++)
                {
                    if(!NodeIsSimple(geometry, node.FirstChild + childOffset))
                    {
                        return false;
                    }
                }

                return true;
        }
    }

    /// <summary>
    /// A point set is simple when no two members share a position — value
    /// equality on the XY ordinates only, so members distinct solely in Z or
    /// M coincide.
    /// </summary>
    private static bool HasNoDuplicatePositions(in FlatGeometry geometry, FlatGeometryNode node)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        HashSet<(double X, double Y)> seen = [];

        for(int partOffset = 0; partOffset < node.PartCount; partOffset++)
        {
            FlatGeometryPart part = parts[node.FirstPart + partOffset];
            Point2d vertex = vertices[part.Start];

            if(!seen.Add((vertex.X, vertex.Y)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A lineal geometry is simple when every member curve is simple on its
    /// own and every inter-member contact is a point on the boundary of both
    /// members — each member's own endpoints if it is open, nothing if it is
    /// closed, never the operand's aggregate parity table.
    /// </summary>
    private static bool LinealIsSimple(in FlatGeometry geometry, FlatGeometryNode node)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;

        for(int partOffset = 0; partOffset < node.PartCount; partOffset++)
        {
            if(!PartIsSimpleCurve(geometry, parts[node.FirstPart + partOffset]))
            {
                return false;
            }
        }

        for(int firstOffset = 0; firstOffset < node.PartCount; firstOffset++)
        {
            for(int secondOffset = firstOffset + 1; secondOffset < node.PartCount; secondOffset++)
            {
                if(!MembersMeetOnlyAtSharedBoundaries(geometry, parts[node.FirstPart + firstOffset], parts[node.FirstPart + secondOffset]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Every ring of the polygonal node, checked as a closed curve.</summary>
    private static bool EveryRingIsSimple(in FlatGeometry geometry, FlatGeometryNode node)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;

        for(int partOffset = 0; partOffset < node.PartCount; partOffset++)
        {
            if(!PartIsSimpleCurve(geometry, parts[node.FirstPart + partOffset]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One curve's self-simplicity over its positive-length segments:
    /// consecutive segments (wrapping when the curve is closed) may share
    /// exactly their common vertex; any other contact — a crossing, a
    /// collinear overlap, a touch between non-consecutive segments — is a
    /// self-intersection.
    /// </summary>
    private static bool PartIsSimpleCurve(in FlatGeometry geometry, FlatGeometryPart part)
    {
        List<(Point2d Start, Point2d End)> segments = CollectSegments(geometry, part);

        if(segments.Count < 2)
        {
            return true;
        }

        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        Point2d firstVertex = vertices[part.Start];
        Point2d lastVertex = vertices[part.Start + part.Length - 1];
        bool closed = part.Length >= 2 && firstVertex.X == lastVertex.X && firstVertex.Y == lastVertex.Y;

        for(int firstIndex = 0; firstIndex < segments.Count; firstIndex++)
        {
            for(int secondIndex = firstIndex + 1; secondIndex < segments.Count; secondIndex++)
            {
                bool adjacent = secondIndex == firstIndex + 1
                    || (closed && firstIndex == 0 && secondIndex == segments.Count - 1);

                SegmentIntersection intersection = SegmentTopology.Classify(
                    segments[firstIndex].Start, segments[firstIndex].End,
                    segments[secondIndex].Start, segments[secondIndex].End);

                if(intersection.Relation == SegmentRelation.Disjoint)
                {
                    continue;
                }

                if(!adjacent || intersection.Relation != SegmentRelation.VertexTouch)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// One member pair of a multi-curve: every contact must be a single
    /// point that is an endpoint of both members, and both members must be
    /// open — a closed member has an empty boundary and admits no contact.
    /// </summary>
    private static bool MembersMeetOnlyAtSharedBoundaries(in FlatGeometry geometry, FlatGeometryPart firstPart, FlatGeometryPart secondPart)
    {
        List<(Point2d Start, Point2d End)> firstSegments = CollectSegments(geometry, firstPart);
        List<(Point2d Start, Point2d End)> secondSegments = CollectSegments(geometry, secondPart);

        foreach((Point2d firstStart, Point2d firstEnd) in firstSegments)
        {
            foreach((Point2d secondStart, Point2d secondEnd) in secondSegments)
            {
                SegmentIntersection intersection = SegmentTopology.Classify(firstStart, firstEnd, secondStart, secondEnd);

                if(intersection.Relation == SegmentRelation.Disjoint)
                {
                    continue;
                }

                if(intersection.Relation != SegmentRelation.VertexTouch)
                {
                    return false;
                }

                if(!IsOpenPartEndpoint(geometry, firstPart, intersection.FirstPoint)
                    || !IsOpenPartEndpoint(geometry, secondPart, intersection.FirstPoint))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the point is an endpoint of the member and the member is open
    /// — the member's own boundary under the Mod-2 rule evaluated per
    /// element.
    /// </summary>
    private static bool IsOpenPartEndpoint(in FlatGeometry geometry, FlatGeometryPart part, Point2d point)
    {
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        Point2d firstVertex = vertices[part.Start];
        Point2d lastVertex = vertices[part.Start + part.Length - 1];

        if(firstVertex.X == lastVertex.X && firstVertex.Y == lastVertex.Y)
        {
            return false;
        }

        bool atFirst = point.X == firstVertex.X && point.Y == firstVertex.Y;
        bool atLast = point.X == lastVertex.X && point.Y == lastVertex.Y;

        return atFirst || atLast;
    }

    /// <summary>The part's positive-length segments in chain order.</summary>
    private static List<(Point2d Start, Point2d End)> CollectSegments(in FlatGeometry geometry, FlatGeometryPart part)
    {
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        List<(Point2d Start, Point2d End)> segments = [];

        for(int vertexIndex = 1; vertexIndex < part.Length; vertexIndex++)
        {
            Point2d start = vertices[part.Start + vertexIndex - 1];
            Point2d end = vertices[part.Start + vertexIndex];

            if(start.X != end.X || start.Y != end.Y)
            {
                segments.Add((start, end));
            }
        }

        return segments;
    }
}
