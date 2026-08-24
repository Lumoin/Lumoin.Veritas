using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The buffer surface of the constructive set: all points within the distance of
/// the operand, in the operand's own coordinate units. Positive distances dilate;
/// negative distances erode areal operands and empty puntal and lineal ones; a
/// zero distance answers an areal operand's regularized point set and the empty
/// polygon otherwise — the adopted reference convention, recorded as a deliberate
/// divergence from the literal specification set. The result is always polygonal.
/// Dilation and erosion compute as exact set arithmetic over the boundary's tube:
/// the union, or difference, of the operand with the depth-extracted region of its
/// offset curves — so hole collapse under dilation and shell collapse under
/// erosion, the tie at the inradius included, fall out of the algebra rather than
/// a pre-test. Arcs tessellate at the quadrant-segments quantum, round joins and
/// caps only, cardinal directions exact. Collections buffer per member and merge
/// through union. False refuses a non-finite distance or a detected noding
/// inconsistency in the offset-curve arrangement; a quadrant-segments argument
/// below one is a caller contract violation and throws.
/// </summary>
public static class GeometryBuffer
{
    /// <summary>The default arc tessellation: eight segments per quadrant.</summary>
    private const int DefaultQuadrantSegments = 8;

    /// <summary>Computes the buffer at the default arc tessellation.</summary>
    public static bool TryCompute(in FlatGeometry geometry, double distance, out FlatGeometry result)
    {
        return TryCompute(in geometry, distance, DefaultQuadrantSegments, out result);
    }

    /// <summary>Computes the buffer with an explicit arc tessellation.</summary>
    public static bool TryCompute(in FlatGeometry geometry, double distance, int quadrantSegments, out FlatGeometry result)
    {
        if(quadrantSegments < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quadrantSegments), quadrantSegments, "At least one arc segment per quadrant is required.");
        }

        if(!double.IsFinite(distance))
        {
            result = default;

            return false;
        }

        if(geometry.Kind == GeometryKind.GeometryCollection)
        {
            return TryComputeCollection(in geometry, distance, quadrantSegments, out result);
        }

        return TryComputeMember(in geometry, distance, quadrantSegments, out result);
    }

    /// <summary>Buffers a collection per member and merges through union.</summary>
    private static bool TryComputeCollection(in FlatGeometry geometry, double distance, int quadrantSegments, out FlatGeometry result)
    {
        var members = new List<FlatGeometry>();
        GeometryOverlay.FlattenIntoMembers(in geometry, members);
        FlatGeometry merged = FlatGeometry.Empty(GeometryKind.Polygon);

        foreach(FlatGeometry member in members)
        {
            if(!TryComputeMember(in member, distance, quadrantSegments, out FlatGeometry buffered))
            {
                result = default;

                return false;
            }

            if(buffered.IsEmpty)
            {
                continue;
            }

            if(merged.IsEmpty)
            {
                merged = buffered;

                continue;
            }

            if(!GeometryOverlay.TryComputeBinary(in merged, in buffered, OverlayOperation.Union, out FlatGeometry union))
            {
                result = default;

                return false;
            }

            merged.Dispose();
            buffered.Dispose();
            merged = union;
        }

        result = merged;

        return true;
    }

    /// <summary>Buffers one non-collection operand.</summary>
    private static bool TryComputeMember(in FlatGeometry geometry, double distance, int quadrantSegments, out FlatGeometry result)
    {
        if(geometry.IsEmpty)
        {
            result = FlatGeometry.Empty(GeometryKind.Polygon);

            return true;
        }

        GeometryKind kind = geometry.Kind;
        bool areal = kind is GeometryKind.Polygon or GeometryKind.MultiPolygon;

        if(!areal)
        {
            //Puntal and lineal operands have no interior to erode: a zero or
            //negative distance empties them. The result kind stays declared
            //polygonal, a recorded divergence from the standard's literal
            //all-points-within-distance set, under which the zero-distance
            //buffer would be the operand itself.
            if(distance <= 0)
            {
                result = FlatGeometry.Empty(GeometryKind.Polygon);

                return true;
            }

            return TryTube(in geometry, distance, quadrantSegments, out result);
        }

        if(distance == 0)
        {
            result = GeometryOverlay.CanonicalRebuild(in geometry);

            return true;
        }

        if(!TryTube(in geometry, Math.Abs(distance), quadrantSegments, out FlatGeometry tube))
        {
            result = default;

            return false;
        }

        FlatGeometry rebuilt = GeometryOverlay.CanonicalRebuild(in geometry);
        OverlayOperation operation = distance > 0 ? OverlayOperation.Union : OverlayOperation.Difference;

        if(!GeometryOverlay.TryComputeBinary(in rebuilt, in tube, operation, out FlatGeometry combined))
        {
            result = default;

            return false;
        }

        rebuilt.Dispose();
        tube.Dispose();
        result = KeepPolygonal(in combined);

        return true;
    }

    /// <summary>
    /// The depth-extracted tube of an operand's parts: raw offset loops for every
    /// run, circles for point parts and degenerate runs, noded and validated on
    /// the shared substrate, extracted by the winding-depth threshold.
    /// </summary>
    private static bool TryTube(in FlatGeometry geometry, double distance, int quadrantSegments, out FlatGeometry result)
    {
        var loops = new List<Point2d[]>();
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts;
        ReadOnlySpan<Point2d> vertices = geometry.Vertices;

        foreach(FlatGeometryPart part in parts)
        {
            if(part.Length == 0)
            {
                continue;
            }

            bool degenerate = true;

            for(int index = 1; index < part.Length; index++)
            {
                if(vertices[part.Start + index - 1].X != vertices[part.Start + index].X
                    || vertices[part.Start + index - 1].Y != vertices[part.Start + index].Y)
                {
                    degenerate = false;

                    break;
                }
            }

            if(degenerate)
            {
                OffsetCurveBuilder.AddPointCircle(vertices[part.Start], distance, quadrantSegments, loops);
            }
            else
            {
                OffsetCurveBuilder.AddRunLoops(vertices.Slice(part.Start, part.Length), distance, quadrantSegments, loops);
            }
        }

        var segments = new List<OverlaySegment>();

        for(int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            Point2d[] loop = loops[loopIndex];

            for(int index = 1; index < loop.Length; index++)
            {
                Point2d start = loop[index - 1];
                Point2d end = loop[index];

                if(start.X == end.X && start.Y == end.Y)
                {
                    continue;
                }

                segments.Add(new OverlaySegment(
                    Operand: 0,
                    Part: loopIndex,
                    start,
                    end,
                    IsBoundary: true,
                    InteriorOnLeft: true,
                    IsHoleRing: false,
                    IsCollapsedRing: false));
            }
        }

        var graph = new OverlayGraph();

        if(!OverlayNoding.TryBuildGraph(segments, graph))
        {
            result = default;

            return false;
        }

        graph.SortStars();
        var depth = new BufferDepth();

        if(!depth.TryResolve(graph))
        {
            result = default;

            return false;
        }

        var pieces = new OverlayResultPieces();

        if(!OverlayAssembly.TryExtractMarkedAreas(graph, pieces))
        {
            result = default;

            return false;
        }

        if(pieces.Polygons.Count == 0)
        {
            result = FlatGeometry.Empty(GeometryKind.Polygon);

            return true;
        }

        result = pieces.Polygons.Count == 1
            ? FlatGeometryFactory.CreatePolygon(pieces.Polygons[0])
            : FlatGeometryFactory.CreateMultiPolygon(pieces.Polygons);

        return true;
    }

    /// <summary>
    /// Keeps the polygonal part of a combined result: measure-zero residue of a
    /// degenerate operand drops from the declared-polygonal answer — recorded
    /// best-effort semantics.
    /// </summary>
    private static FlatGeometry KeepPolygonal(in FlatGeometry combined)
    {
        if(combined.Kind is GeometryKind.Polygon or GeometryKind.MultiPolygon)
        {
            return combined;
        }

        var members = new List<FlatGeometry>();
        GeometryOverlay.FlattenIntoMembers(in combined, members);
        var polygonal = new List<FlatGeometry>();

        foreach(FlatGeometry member in members)
        {
            if(member.Kind is GeometryKind.Polygon or GeometryKind.MultiPolygon && !member.IsEmpty)
            {
                polygonal.Add(member);
            }
        }

        if(polygonal.Count == 0)
        {
            return FlatGeometry.Empty(GeometryKind.Polygon);
        }

        if(polygonal.Count == 1)
        {
            return polygonal[0];
        }

        var polygons = new List<List<Point2d[]>>();

        foreach(FlatGeometry member in polygonal)
        {
            ReadOnlySpan<FlatGeometryPart> parts = member.Parts;
            ReadOnlySpan<Point2d> vertices = member.Vertices;
            List<Point2d[]>? current = null;

            foreach(FlatGeometryPart part in parts)
            {
                var ring = new Point2d[part.Length];
                vertices.Slice(part.Start, part.Length).CopyTo(ring);

                if(part.Role == FlatGeometryPartRole.ExteriorRing)
                {
                    current = [ring];
                    polygons.Add(current);
                }
                else
                {
                    current?.Add(ring);
                }
            }
        }

        polygons.Sort((left, right) => OverlayGraph.ComparePoints(left[0][0], right[0][0]));

        return FlatGeometryFactory.CreateMultiPolygon(polygons);
    }
}
