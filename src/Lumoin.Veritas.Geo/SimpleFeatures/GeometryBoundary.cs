using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The combinatorial boundary of a <see cref="FlatGeometry"/>, with
/// canonical-per-dimension result kinds: puntal input answers the empty collection;
/// lineal input always a multipoint by the endpoint-parity rule (a position is on the
/// boundary exactly when it is an endpoint of an odd number of member curves, so
/// closed curves answer the empty multipoint); polygonal input always a
/// multilinestring of every ring, shells and holes alike. No cardinality-dependent
/// collapse to scalar kinds exists — the result kind depends only on the input's
/// dimension. The boundary of a heterogeneous collection is undefined and answers
/// false. Results are planar XY; Z and M do not carry through.
/// </summary>
public static class GeometryBoundary
{
    /// <summary>
    /// Computes the boundary; false only for a heterogeneous collection, whose
    /// boundary is undefined in this model.
    /// </summary>
    public static bool TryCompute(in FlatGeometry geometry, out FlatGeometry boundary)
    {
        switch(geometry.Kind)
        {
            case GeometryKind.Point:
            case GeometryKind.MultiPoint:
                boundary = FlatGeometry.Empty(GeometryKind.GeometryCollection);

                return true;

            case GeometryKind.LineString:
            case GeometryKind.MultiLineString:
                boundary = ComputeLinealBoundary(in geometry);

                return true;

            case GeometryKind.Polygon:
            case GeometryKind.MultiPolygon:
                boundary = ComputePolygonalBoundary(in geometry);

                return true;

            default:
                boundary = default;

                return false;
        }
    }

    /// <summary>
    /// Endpoint parity over every curve of the root node: each part's two endpoints
    /// tally by XY value, and positions with an odd tally form the multipoint, in
    /// coordinate order.
    /// </summary>
    private static FlatGeometry ComputeLinealBoundary(in FlatGeometry geometry)
    {
        FlatGeometryNode root = geometry.Nodes[0];
        var valences = new SortedDictionary<(double X, double Y), int>();

        for(int index = 0; index < root.PartCount; index++)
        {
            FlatGeometryPart part = geometry.Parts[root.FirstPart + index];

            if(part.Length == 0)
            {
                continue;
            }

            Tally(valences, geometry.Vertices[part.Start]);
            Tally(valences, geometry.Vertices[part.Start + part.Length - 1]);
        }

        var boundaryPositions = new List<Point2d>();

        foreach(((double x, double y), int valence) in valences)
        {
            if(valence % 2 == 1)
            {
                boundaryPositions.Add(new Point2d(x, y));
            }
        }

        Point2d[] positions = new Point2d[boundaryPositions.Count];
        boundaryPositions.CopyTo(positions);

        return FlatGeometryFactory.CreateMultiPoint(positions);
    }

    /// <summary>Every ring of the root node, shells and holes alike, as a multilinestring.</summary>
    private static FlatGeometry ComputePolygonalBoundary(in FlatGeometry geometry)
    {
        FlatGeometryNode root = geometry.Nodes[0];
        var rings = new List<Point2d[]>();

        for(int index = 0; index < root.PartCount; index++)
        {
            FlatGeometryPart part = geometry.Parts[root.FirstPart + index];
            var ring = new Point2d[part.Length];
            geometry.Vertices.Slice(part.Start, part.Length).CopyTo(ring);
            rings.Add(ring);
        }

        return FlatGeometryFactory.CreateMultiLineString(rings);
    }

    /// <summary>Increments one endpoint's tally.</summary>
    private static void Tally(SortedDictionary<(double X, double Y), int> valences, Point2d position)
    {
        valences.TryGetValue((position.X, position.Y), out int valence);
        valences[(position.X, position.Y)] = valence + 1;
    }
}
