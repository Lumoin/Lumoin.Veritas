using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// The cells-to-geometry bridge between the house A5 flavour's cell sets and the planar Simple
/// Features floor: a canonical cell sequence materializes as the polygon (one cell) or multipolygon
/// (several cells, in canonical order) of the cells' boundary rings in CRS84 longitude/latitude
/// degrees, and a planar geometry converts to the cell set covering it at a stated resolution.
/// </summary>
/// <remarks>
/// <para>
/// Materialization refuses, as a value, every cell whose boundary is not planar-faithful in CRS84:
/// the boundary evaluator unwraps ring longitudes around the ring's own centroid, so an
/// antimeridian-straddling or polar cell carries vertices outside the canonical longitude range, and
/// planar functions over such coordinates would silently disagree with canonically-ranged geometry.
/// The refusal predicate is any vertex outside the canonical CRS84 ranges, or a ring longitude
/// extent of 180 degrees or more, or the world cell's empty boundary. The named fix path is geodesic
/// splitting when coordinate-transformation machinery lands.
/// </para>
/// <para>
/// Materialization also refuses a sequence containing an ancestor and its own descendant: their
/// pentagons overlap structurally, and a multipolygon with interior-overlapping members is outside
/// the floor's computation contract. Edge-adjacent cells of one resolution share boundary edges and
/// compute as given. Ring orientation is normalized computationally — each ring's signed area is
/// evaluated and the ring reversed when negative — so the shells-counter-clockwise contract of the
/// factory never rides an upstream convention.
/// </para>
/// </remarks>
public static class A5CellGeometry
{
    /// <summary>The inclusive longitude walls of the canonical CRS84 range, in degrees.</summary>
    private const double LongitudeWall = 180.0;

    /// <summary>The inclusive latitude walls of the canonical CRS84 range, in degrees.</summary>
    private const double LatitudeWall = 90.0;

    /// <summary>
    /// Materializes a canonical cell sequence as a planar CRS84 geometry: one cell as its boundary
    /// polygon, several cells as the multipolygon of their boundary polygons in sequence order.
    /// </summary>
    /// <param name="canonicalCells">The deduplicated, ascending-sorted cell sequence; must be non-empty.</param>
    /// <param name="geometry">The materialized geometry.</param>
    /// <returns><see langword="false"/> on an empty sequence, a nested ancestor/descendant pair, or a planar-faithfulness refusal.</returns>
    public static bool TryBuildGeometry(ReadOnlySpan<A5CellId> canonicalCells, out FlatGeometry geometry)
    {
        geometry = default;
        if(canonicalCells.Length == 0 || ContainsNestedPair(canonicalCells))
        {
            return false;
        }

        var polygons = new List<List<Point2d[]>>(canonicalCells.Length);
        foreach(A5CellId cell in canonicalCells)
        {
            LonLat[] boundary = A5.CellToBoundary(cell, closedRing: true, segments: 0);
            if(boundary.Length == 0 || !IsPlanarFaithful(boundary))
            {
                return false;
            }

            Point2d[] ring = new Point2d[boundary.Length];
            for(int index = 0; index < boundary.Length; index++)
            {
                ring[index] = new Point2d(boundary[index].Longitude, boundary[index].Latitude);
            }

            NormalizeToCounterClockwise(ring);
            polygons.Add([ring]);
        }

        geometry = polygons.Count == 1
            ? FlatGeometryFactory.CreatePolygon(polygons[0])
            : FlatGeometryFactory.CreateMultiPolygon(polygons);

        return true;
    }

    /// <summary>
    /// Converts a planar CRS84 geometry to the cell set covering it at <paramref name="resolution"/>:
    /// points through point-to-cell assignment, linestrings through line traversal, polygons through
    /// region fill with holes subtracted, and multi kinds and collections member by member over a
    /// bounded worklist. Three-dimensional and measured geometries are refused — the cell
    /// representation is planar and would silently discard the extra ordinates — as is any non-finite
    /// vertex.
    /// </summary>
    /// <param name="geometry">The geometry to convert.</param>
    /// <param name="resolution">The target resolution, 0 through <see cref="A5.MaxResolution"/>.</param>
    /// <param name="cellsToAppendTo">The covering cells, appended unordered and possibly with duplicates.</param>
    /// <returns><see langword="false"/> on a refused geometry or an out-of-range resolution.</returns>
    public static bool TryConvertGeometry(in FlatGeometry geometry, int resolution, List<A5CellId> cellsToAppendTo)
    {
        ArgumentNullException.ThrowIfNull(cellsToAppendTo);

        if(resolution < 0 || resolution > A5.MaxResolution || geometry.Is3D || geometry.IsMeasured)
        {
            return false;
        }

        foreach(Point2d vertex in geometry.Vertices)
        {
            if(!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
            {
                return false;
            }
        }

        ReadOnlySpan<FlatGeometryNode> nodes = geometry.Nodes;
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        var worklist = new Stack<int>();
        worklist.Push(0);
        while(worklist.Count > 0)
        {
            FlatGeometryNode node = nodes[worklist.Pop()];
            if(node.Kind == GeometryKind.GeometryCollection)
            {
                for(int child = 0; child < node.ChildCount; child++)
                {
                    worklist.Push(node.FirstChild + child);
                }

                continue;
            }

            int partIndex = node.FirstPart;
            int partEnd = node.FirstPart + node.PartCount;
            while(partIndex < partEnd)
            {
                FlatGeometryPart part = parts[partIndex];
                switch(part.Role)
                {
                    case FlatGeometryPartRole.Point:
                        cellsToAppendTo.Add(A5.LonLatToCell(ToLonLat(vertices[part.Start]), resolution));
                        partIndex++;
                        break;

                    case FlatGeometryPartRole.Line:
                        cellsToAppendTo.AddRange(A5.LineStringToCells(ToLonLatRun(vertices, part), resolution));
                        partIndex++;
                        break;

                    case FlatGeometryPartRole.ExteriorRing:
                        int ringEnd = partIndex + 1;
                        while(ringEnd < partEnd && parts[ringEnd].Role == FlatGeometryPartRole.InteriorRing)
                        {
                            ringEnd++;
                        }

                        LonLat[][] rings = new LonLat[ringEnd - partIndex][];
                        for(int ring = 0; ring < rings.Length; ring++)
                        {
                            rings[ring] = ToLonLatRun(vertices, parts[partIndex + ring]);
                        }

                        cellsToAppendTo.AddRange(A5.PolygonToCells(rings, resolution));
                        partIndex = ringEnd;
                        break;

                    default:
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Answers whether the ascending-sorted sequence contains a cell together with one of its own
    /// ancestors, walking each cell's ancestor chain (a bounded loop over at most the resolution
    /// count) against the sequence's id set.
    /// </summary>
    /// <param name="canonicalCells">The ascending-sorted cell sequence.</param>
    /// <returns><see langword="true"/> when a nested pair is present.</returns>
    private static bool ContainsNestedPair(ReadOnlySpan<A5CellId> canonicalCells)
    {
        if(canonicalCells.Length < 2)
        {
            return false;
        }

        var present = new HashSet<ulong>(canonicalCells.Length);
        foreach(A5CellId cell in canonicalCells)
        {
            present.Add(cell.Value);
        }

        foreach(A5CellId cell in canonicalCells)
        {
            int resolution = Serialization.GetResolution(cell.Value);
            for(int ancestorResolution = resolution - 1; ancestorResolution >= -1; ancestorResolution--)
            {
                if(present.Contains(Serialization.CellToParent(cell.Value, ancestorResolution)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Answers whether every vertex of a boundary ring sits inside the canonical CRS84 ranges and the
    /// ring's longitude extent stays under 180 degrees.
    /// </summary>
    /// <param name="boundary">The boundary ring in longitude/latitude degrees.</param>
    /// <returns><see langword="true"/> when the ring is planar-faithful.</returns>
    private static bool IsPlanarFaithful(LonLat[] boundary)
    {
        double minimumLongitude = double.PositiveInfinity;
        double maximumLongitude = double.NegativeInfinity;
        foreach(LonLat vertex in boundary)
        {
            if(vertex.Longitude < -LongitudeWall || vertex.Longitude > LongitudeWall
                || vertex.Latitude < -LatitudeWall || vertex.Latitude > LatitudeWall)
            {
                return false;
            }

            minimumLongitude = Math.Min(minimumLongitude, vertex.Longitude);
            maximumLongitude = Math.Max(maximumLongitude, vertex.Longitude);
        }

        return maximumLongitude - minimumLongitude < LongitudeWall;
    }

    /// <summary>
    /// Normalizes a closed ring to counter-clockwise orientation by the shoelace signed area,
    /// reversing in place when the area is negative.
    /// </summary>
    /// <param name="ring">The closed ring to normalize.</param>
    private static void NormalizeToCounterClockwise(Point2d[] ring)
    {
        double doubledArea = 0;
        for(int index = 0; index < ring.Length - 1; index++)
        {
            doubledArea += (ring[index].X * ring[index + 1].Y) - (ring[index + 1].X * ring[index].Y);
        }

        if(doubledArea < 0)
        {
            Array.Reverse(ring);
        }
    }

    /// <summary>Copies a part's vertex run as longitude/latitude coordinates.</summary>
    /// <param name="vertices">The geometry's vertex column.</param>
    /// <param name="part">The part whose run to copy.</param>
    /// <returns>The run as coordinates.</returns>
    private static LonLat[] ToLonLatRun(ReadOnlySpan<Point2d> vertices, FlatGeometryPart part)
    {
        LonLat[] run = new LonLat[part.Length];
        for(int index = 0; index < part.Length; index++)
        {
            run[index] = ToLonLat(vertices[part.Start + index]);
        }

        return run;
    }

    /// <summary>Reads a planar CRS84 vertex as a longitude/latitude coordinate.</summary>
    /// <param name="vertex">The planar vertex.</param>
    /// <returns>The coordinate.</returns>
    private static LonLat ToLonLat(Point2d vertex)
    {
        return new LonLat(vertex.X, vertex.Y);
    }
}
