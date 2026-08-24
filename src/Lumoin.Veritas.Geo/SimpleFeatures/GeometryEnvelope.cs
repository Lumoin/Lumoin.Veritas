using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The planar-XY envelope of a <see cref="FlatGeometry"/>. The bounds query is
/// undefined on the empty point set and answers through a <c>Try</c> shape — no
/// inverted-range sentinel box exists in this model. The geometry-returning variant
/// applies the conventional degenerate collapse: empty input yields the empty point;
/// a zero-extent box on both axes a point; on one axis a two-point linestring; a real
/// box a counter-clockwise single-ring polygon.
/// </summary>
public static class GeometryEnvelope
{
    /// <summary>
    /// Computes the bounding box over every position; false when the geometry is empty.
    /// </summary>
    public static bool TryComputeBounds(in FlatGeometry geometry, out BoundingBox bounds)
    {
        if(geometry.IsEmpty)
        {
            bounds = default;

            return false;
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        foreach(Point2d vertex in geometry.Vertices)
        {
            if(vertex.X < minX)
            {
                minX = vertex.X;
            }

            if(vertex.X > maxX)
            {
                maxX = vertex.X;
            }

            if(vertex.Y < minY)
            {
                minY = vertex.Y;
            }

            if(vertex.Y > maxY)
            {
                maxY = vertex.Y;
            }
        }

        bounds = new BoundingBox(minX, minY, maxX, maxY);

        return true;
    }

    /// <summary>
    /// The envelope as a geometry with the degenerate collapse:
    /// <c>POINT EMPTY</c> for an empty input, a point for a zero-extent box, a
    /// two-point linestring for a box degenerate on one axis, otherwise the rectangle
    /// as a counter-clockwise single-ring polygon.
    /// </summary>
    public static FlatGeometry ComputeEnvelopeGeometry(in FlatGeometry geometry)
    {
        if(!TryComputeBounds(in geometry, out BoundingBox bounds))
        {
            return FlatGeometry.Empty(GeometryKind.Point);
        }

        bool flatX = bounds.MinX == bounds.MaxX;
        bool flatY = bounds.MinY == bounds.MaxY;

        if(flatX && flatY)
        {
            return FlatGeometryFactory.CreatePoint(new Point2d(bounds.MinX, bounds.MinY));
        }

        if(flatX || flatY)
        {
            return FlatGeometryFactory.CreateLineString(
                [new Point2d(bounds.MinX, bounds.MinY), new Point2d(bounds.MaxX, bounds.MaxY)]);
        }

        return FlatGeometryFactory.CreateRectanglePolygon(bounds);
    }
}
