using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Internal builders for the small planar-XY geometries the operations return
/// (envelopes and boundaries). Results never carry Z or M — computation is planar by
/// the flat model's definition.
/// </summary>
internal static class FlatGeometryFactory
{
    /// <summary>A single point.</summary>
    public static FlatGeometry CreatePoint(Point2d position)
    {
        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.Point, 0, 0, 0, 1, false, false)],
            [new FlatGeometryPart(0, 1, FlatGeometryPartRole.Point)],
            new HeapColumnOwner<Point2d>([position]),
            zColumn: null,
            mColumn: null);
    }

    /// <summary>A single linestring over the given positions.</summary>
    public static FlatGeometry CreateLineString(ReadOnlySpan<Point2d> positions)
    {
        var vertices = new Point2d[positions.Length];
        positions.CopyTo(vertices);

        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.LineString, 0, 0, 0, 1, false, false)],
            [new FlatGeometryPart(0, positions.Length, FlatGeometryPartRole.Line)],
            new HeapColumnOwner<Point2d>(vertices),
            zColumn: null,
            mColumn: null);
    }

    /// <summary>The axis-aligned rectangle of the bounds as a counter-clockwise single-ring polygon.</summary>
    public static FlatGeometry CreateRectanglePolygon(BoundingBox bounds)
    {
        Point2d[] ring =
        [
            new(bounds.MinX, bounds.MinY),
            new(bounds.MaxX, bounds.MinY),
            new(bounds.MaxX, bounds.MaxY),
            new(bounds.MinX, bounds.MaxY),
            new(bounds.MinX, bounds.MinY),
        ];

        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.Polygon, 0, 0, 0, 1, false, false)],
            [new FlatGeometryPart(0, ring.Length, FlatGeometryPartRole.ExteriorRing)],
            new HeapColumnOwner<Point2d>(ring),
            zColumn: null,
            mColumn: null);
    }

    /// <summary>A multipoint over the given positions; empty input yields the typed empty.</summary>
    public static FlatGeometry CreateMultiPoint(ReadOnlySpan<Point2d> positions)
    {
        if(positions.Length == 0)
        {
            return FlatGeometry.Empty(GeometryKind.MultiPoint);
        }

        var parts = new FlatGeometryPart[positions.Length];
        var vertices = new Point2d[positions.Length];
        positions.CopyTo(vertices);

        for(int index = 0; index < positions.Length; index++)
        {
            parts[index] = new FlatGeometryPart(index, 1, FlatGeometryPartRole.Point);
        }

        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.MultiPoint, 0, 0, 0, parts.Length, false, false)],
            parts,
            new HeapColumnOwner<Point2d>(vertices),
            zColumn: null,
            mColumn: null);
    }

    /// <summary>
    /// A polygon from computed closed rings — the first is the shell, the rest its
    /// holes; an empty list yields the typed empty. Ring closure and orientation are
    /// the caller's contract (constructed shells counter-clockwise, holes clockwise).
    /// </summary>
    public static FlatGeometry CreatePolygon(List<Point2d[]> rings)
    {
        if(rings.Count == 0)
        {
            return FlatGeometry.Empty(GeometryKind.Polygon);
        }

        var parts = new FlatGeometryPart[rings.Count];
        int vertexCount = 0;

        foreach(Point2d[] ring in rings)
        {
            vertexCount += ring.Length;
        }

        var vertices = new Point2d[vertexCount];
        int cursor = 0;

        for(int index = 0; index < rings.Count; index++)
        {
            FlatGeometryPartRole role = index == 0 ? FlatGeometryPartRole.ExteriorRing : FlatGeometryPartRole.InteriorRing;
            parts[index] = new FlatGeometryPart(cursor, rings[index].Length, role);
            rings[index].CopyTo(vertices, cursor);
            cursor += rings[index].Length;
        }

        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.Polygon, 0, 0, 0, parts.Length, false, false)],
            parts,
            new HeapColumnOwner<Point2d>(vertices),
            zColumn: null,
            mColumn: null);
    }

    /// <summary>
    /// A multipolygon from computed polygons, each a shell-then-holes ring list; an
    /// empty list yields the typed empty. The positional role convention carries the
    /// grouping: every exterior ring opens a polygon, the interior rings that follow
    /// belong to it.
    /// </summary>
    public static FlatGeometry CreateMultiPolygon(List<List<Point2d[]>> polygons)
    {
        if(polygons.Count == 0)
        {
            return FlatGeometry.Empty(GeometryKind.MultiPolygon);
        }

        int partCount = 0;
        int vertexCount = 0;

        foreach(List<Point2d[]> polygon in polygons)
        {
            partCount += polygon.Count;

            foreach(Point2d[] ring in polygon)
            {
                vertexCount += ring.Length;
            }
        }

        var parts = new FlatGeometryPart[partCount];
        var vertices = new Point2d[vertexCount];
        int partCursor = 0;
        int vertexCursor = 0;

        foreach(List<Point2d[]> polygon in polygons)
        {
            for(int ringIndex = 0; ringIndex < polygon.Count; ringIndex++)
            {
                FlatGeometryPartRole role = ringIndex == 0 ? FlatGeometryPartRole.ExteriorRing : FlatGeometryPartRole.InteriorRing;
                parts[partCursor] = new FlatGeometryPart(vertexCursor, polygon[ringIndex].Length, role);
                polygon[ringIndex].CopyTo(vertices, vertexCursor);
                vertexCursor += polygon[ringIndex].Length;
                partCursor++;
            }
        }

        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.MultiPolygon, 0, 0, 0, parts.Length, false, false)],
            parts,
            new HeapColumnOwner<Point2d>(vertices),
            zColumn: null,
            mColumn: null);
    }

    /// <summary>
    /// A geometry collection over already-built non-collection members; an empty list
    /// yields the typed empty. Member columns are copied XY-only (results carry no
    /// Z or M) and part runs rebase into the one shared column; a collection-kind
    /// member is a builder bookkeeping violation and throws.
    /// </summary>
    public static FlatGeometry CreateCollection(List<FlatGeometry> members)
    {
        if(members.Count == 0)
        {
            return FlatGeometry.Empty(GeometryKind.GeometryCollection);
        }

        var builder = new FlatGeometryBuilder();
        var children = new List<int>(members.Count);

        foreach(FlatGeometry member in members)
        {
            if(member.Kind == GeometryKind.GeometryCollection)
            {
                throw new InvalidOperationException(
                    "Collection members must be non-collection geometries (simple-features builder bookkeeping).");
            }

            ReadOnlySpan<FlatGeometryNode> memberNodes = member.Nodes;
            ReadOnlySpan<Point2d> memberVertices = member.Vertices;
            int firstPart = builder.PartCount;

            foreach(FlatGeometryPart part in member.Parts)
            {
                int start = builder.VertexCount;

                for(int vertexIndex = 0; vertexIndex < part.Length; vertexIndex++)
                {
                    builder.AddVertex(memberVertices[part.Start + vertexIndex]);
                }

                builder.AddPart(new FlatGeometryPart(start, part.Length, part.Role));
            }

            children.Add(builder.AddNode(memberNodes[0].Kind, hasZ: false, hasM: false, firstPart, builder.PartCount - firstPart));
        }

        int rootIndex = builder.AddNode(GeometryKind.GeometryCollection, hasZ: false, hasM: false, firstPart: 0, partCount: 0);
        builder.SetChildren(rootIndex, children);
        builder.RootIndex = rootIndex;

        return builder.ToGeometry();
    }

    /// <summary>A multilinestring over the given position runs; an empty list yields the typed empty.</summary>
    public static FlatGeometry CreateMultiLineString(List<Point2d[]> lines)
    {
        if(lines.Count == 0)
        {
            return FlatGeometry.Empty(GeometryKind.MultiLineString);
        }

        var parts = new FlatGeometryPart[lines.Count];
        int vertexCount = 0;

        foreach(Point2d[] line in lines)
        {
            vertexCount += line.Length;
        }

        var vertices = new Point2d[vertexCount];
        int cursor = 0;

        for(int index = 0; index < lines.Count; index++)
        {
            parts[index] = new FlatGeometryPart(cursor, lines[index].Length, FlatGeometryPartRole.Line);
            lines[index].CopyTo(vertices, cursor);
            cursor += lines[index].Length;
        }

        return new FlatGeometry(
            [new FlatGeometryNode(GeometryKind.MultiLineString, 0, 0, 0, parts.Length, false, false)],
            parts,
            new HeapColumnOwner<Point2d>(vertices),
            zColumn: null,
            mColumn: null);
    }
}
