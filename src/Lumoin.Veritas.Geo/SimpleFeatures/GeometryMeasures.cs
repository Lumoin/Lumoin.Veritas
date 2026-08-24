using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Planar-XY area and length of a <see cref="FlatGeometry"/>, plain double per the
/// house numeric split (measures never escalate; only topology-deciding signs do).
/// Area: ring area by the shoelace sum with first-vertex anchor translation —
/// subtracting the ring's first vertex before multiplying keeps magnitudes small far
/// from the origin — with a polygon counting its shell minus its holes; puntal and
/// lineal kinds have zero area. Length: the segment-length sum, with a polygon
/// contributing its full perimeter (shell and holes); puntal kinds have zero length.
/// Multi kinds and collections sum their members; empties measure zero.
/// </summary>
public static class GeometryMeasures
{
    /// <summary>The planar area in squared coordinate units.</summary>
    public static double Area(in FlatGeometry geometry)
    {
        double total = 0;

        foreach(FlatGeometryNode node in geometry.Nodes)
        {
            if(node.Kind is not (GeometryKind.Polygon or GeometryKind.MultiPolygon))
            {
                continue;
            }

            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = geometry.Parts[node.FirstPart + index];
                double ringArea = Math.Abs(SignedRingArea(geometry.Vertices, part));

                total += part.Role == FlatGeometryPartRole.ExteriorRing ? ringArea : -ringArea;
            }
        }

        return total;
    }

    /// <summary>The planar length in coordinate units; polygonal kinds contribute their perimeter.</summary>
    public static double Length(in FlatGeometry geometry)
    {
        double total = 0;

        foreach(FlatGeometryNode node in geometry.Nodes)
        {
            if(node.Kind is GeometryKind.Point or GeometryKind.MultiPoint or GeometryKind.GeometryCollection)
            {
                continue;
            }

            for(int index = 0; index < node.PartCount; index++)
            {
                total += PolylineLength(geometry.Vertices, geometry.Parts[node.FirstPart + index]);
            }
        }

        return total;
    }

    /// <summary>
    /// Twice-halved anchored shoelace over one ring part: each term multiplies the
    /// anchor-translated x by the y difference of the vertex's neighbors.
    /// </summary>
    internal static double SignedRingArea(ReadOnlySpan<Point2d> vertices, FlatGeometryPart part)
    {
        if(part.Length < 3)
        {
            return 0;
        }

        double anchorX = vertices[part.Start].X;
        double sum = 0;

        for(int index = 1; index < part.Length - 1; index++)
        {
            double x = vertices[part.Start + index].X - anchorX;
            double previousY = vertices[part.Start + index - 1].Y;
            double nextY = vertices[part.Start + index + 1].Y;
            sum += x * (previousY - nextY);
        }

        return sum / 2.0;
    }

    /// <summary>The segment-length sum of one vertex run.</summary>
    private static double PolylineLength(ReadOnlySpan<Point2d> vertices, FlatGeometryPart part)
    {
        double total = 0;

        for(int index = 1; index < part.Length; index++)
        {
            Point2d previous = vertices[part.Start + index - 1];
            Point2d current = vertices[part.Start + index];
            total += double.Hypot(current.X - previous.X, current.Y - previous.Y);
        }

        return total;
    }
}
