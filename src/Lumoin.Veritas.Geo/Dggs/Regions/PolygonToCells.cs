using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Traversal;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Geo.Dggs.Regions;

/// <summary>
/// Fills a polygon (with optional holes) with the cells whose centers lie inside it.
/// </summary>
internal static class PolygonToCells
{
    /// <summary>Sample-interval factor applied to the cell radius: denser than <see cref="LineTraversal"/>'s 0.5, since area filling needs a tighter boundary sample than line tracing.</summary>
    private const double SampleIntervalFactor = 0.4;

    /// <summary>Signed-dot epsilon below which a boundary cell's side of a ring segment is ambiguous and must fall back to full point-in-polygon.</summary>
    private const double AmbiguousDotEpsilon = 1e-14;

    /// <summary>Divisor of the isoperimetric bound (<c>boundarySize² / (4π)</c>) on the maximum interior area for a given boundary size.</summary>
    private const double IsoperimetricDivisor = 4 * Math.PI;

    /// <summary>Minimum isoperimetric-bound interior area below which the coarse-phase flood fill's setup overhead isn't worth amortizing.</summary>
    private const int CoarsePhaseAreaThreshold = 1000;

    /// <summary>Fine breadth-first-search layer cap for the coarse phase's phase 1 (moving the frontier off the boundary before the coarse sweep).</summary>
    private const int FineBreadthFirstSearchLayerCap = 3;

    /// <summary>
    /// Finds all cells within a ring (with no holes) whose center lies inside it.
    /// </summary>
    /// <param name="ring">
    /// The ring's vertices, in geographic coordinates. May be open or closed (GeoJSON-style, first
    /// vertex repeated at the end) — closure is automatic either way.
    /// </param>
    /// <param name="resolution">The resolution to fill cells at.</param>
    /// <returns>A sorted, compacted array of cell ids whose centers lie inside the ring. Use <see cref="Compaction.Uncompact"/> to expand to <paramref name="resolution"/>.</returns>
    public static ulong[] GetCells(LonLat[] ring, int resolution)
    {
        ArgumentNullException.ThrowIfNull(ring);

        List<LonLat[]>? normalizedRings = NormalizeRings([ring]);

        return normalizedRings is null ? [] : GetCellsFromRings(normalizedRings, resolution);
    }

    /// <summary>
    /// Finds all cells within a polygon with holes whose center lies inside the outer ring and outside
    /// every hole ring.
    /// </summary>
    /// <param name="rings">
    /// GeoJSON-style rings: <c>[outer, ...holes]</c>, in geographic coordinates. Rings may be open or
    /// closed (first vertex repeated at the end) — closure is automatic either way. Holes with fewer
    /// than 3 distinct vertices are ignored.
    /// </param>
    /// <param name="resolution">The resolution to fill cells at.</param>
    /// <returns>A sorted, compacted array of cell ids whose centers lie inside the polygon. Use <see cref="Compaction.Uncompact"/> to expand to <paramref name="resolution"/>.</returns>
    public static ulong[] GetCells(LonLat[][] rings, int resolution)
    {
        ArgumentNullException.ThrowIfNull(rings);

        List<LonLat[]>? normalizedRings = NormalizeRings(rings);

        return normalizedRings is null ? [] : GetCellsFromRings(normalizedRings, resolution);
    }

    /// <summary>
    /// Strips a GeoJSON-style closing vertex (first vertex repeated at the end) if present, else
    /// returns <paramref name="ring"/> unchanged. Compares the closing vertex with EXACT floating-point
    /// equality, never an epsilon.
    /// </summary>
    private static LonLat[] StripClosing(LonLat[] ring)
    {
        int last = ring.Length - 1;
        if(last > 0 && ring[0].Longitude == ring[last].Longitude && ring[0].Latitude == ring[last].Latitude)
        {
            return ring[..last];
        }

        return ring;
    }

    /// <summary>
    /// Strips closing vertices and drops degenerate rings: <see langword="null"/> if there are no rings
    /// or the outer ring has fewer than 3 distinct vertices; holes with fewer than 3 distinct vertices
    /// are silently dropped, everything else kept.
    /// </summary>
    private static List<LonLat[]>? NormalizeRings(LonLat[][] rings)
    {
        if(rings.Length == 0)
        {
            return null;
        }

        LonLat[] outer = StripClosing(rings[0]);
        if(outer.Length < 3)
        {
            return null;
        }

        List<LonLat[]> normalizedRings = [outer];
        for(int index = 1; index < rings.Length; index++)
        {
            LonLat[] hole = StripClosing(rings[index]);
            if(hole.Length >= 3)
            {
                normalizedRings.Add(hole);
            }
        }

        return normalizedRings;
    }

    /// <summary>
    /// Runs the fill algorithm over already-normalized rings (outer ring first, then holes): dense
    /// boundary sampling, a cheap per-segment-side classification of the boundary cells with a full
    /// point-in-polygon fallback where ambiguous, a one-cell shell expansion to seed the interior, and a
    /// hierarchical flood fill through the interior.
    /// </summary>
    private static ulong[] GetCellsFromRings(List<LonLat[]> rings, int resolution)
    {
        // Authalic-sphere ring vectors — A5's internal sphere, so cell centers compare directly with no
        // geodetic-authalic round trip.
        Cartesian[][] ringVectorsList = new Cartesian[rings.Count][];
        for(int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            LonLat[] ring = rings[ringIndex];
            Cartesian[] ringVectors = new Cartesian[ring.Length];
            for(int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
            {
                ringVectors[vertexIndex] = CoordinateTransforms.ToCartesian(CoordinateTransforms.FromLonLat(ring[vertexIndex]));
            }

            ringVectorsList[ringIndex] = ringVectors;
        }

        (List<ulong> boundaryCells, HashSet<ulong> boundarySet, Dictionary<ulong, List<int>> segmentMap) = DenseSampleBoundary(rings, ringVectorsList, resolution);

        // Flattened per-segment normals and interior-side signs, indexed like the segment map. The
        // polygon interior lies on the OUTSIDE of a hole ring, so hole segments get the opposite sign.
        List<Cartesian> segmentNormals = [];
        List<int> segmentSigns = [];
        for(int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            int sign = (ringIndex == 0 ? 1 : -1) * SphericalPolygonPrimitives.RingWindingSign(ringVectorsList[ringIndex]);
            Cartesian[] normals = SphericalPolygonPrimitives.RingSegmentNormals(ringVectorsList[ringIndex]);
            foreach(Cartesian normal in normals)
            {
                segmentNormals.Add(normal);
                segmentSigns.Add(sign);
            }
        }

        List<ulong> filteredBoundary = FilterBoundaryCells(boundaryCells, segmentMap, [.. segmentNormals], [.. segmentSigns], ringVectorsList);

        // Dense sampling can leave gaps; the shell catches them, classifying each cell.
        List<ulong> shellCells = ExpandShell(boundaryCells, boundarySet);
        if(shellCells.Count == 0)
        {
            return Compaction.Compact(filteredBoundary.ToArray());
        }

        List<ulong> interiorSeeds = [];
        HashSet<ulong> visited = [.. boundarySet];
        foreach(ulong cell in shellCells)
        {
            if(PointInPolygonRings(CoordinateTransforms.ToCartesian(Cell.CellToSpherical(cell)), ringVectorsList))
            {
                interiorSeeds.Add(cell);
            }
            else
            {
                // Exterior shell (and hole interiors) join the firewall.
                visited.Add(cell);
            }
        }

        if(interiorSeeds.Count == 0)
        {
            return Compaction.Compact(filteredBoundary.ToArray());
        }

        List<ulong> interiorCells = FloodInterior(interiorSeeds, visited, boundarySet.Count, resolution);

        List<ulong> combined = [.. filteredBoundary, .. interiorCells];

        return Compaction.Compact(combined.ToArray());
    }

    /// <summary>
    /// Dense-samples boundary cells along every closed ring (outer ring, then holes) at
    /// <c>cellRadius * 0.4</c> spacing, calling <see cref="Cell.SphericalToCell"/> per sample. Records,
    /// per cell, the global (cross-ring) indices of every ring segment that sampled it.
    /// </summary>
    private static (List<ulong> BoundaryCells, HashSet<ulong> BoundarySet, Dictionary<ulong, List<int>> SegmentMap) DenseSampleBoundary(
        List<LonLat[]> rings,
        Cartesian[][] ringVectorsList,
        int resolution)
    {
        List<ulong> boundaryCells = [];
        HashSet<ulong> boundarySet = [];
        Dictionary<ulong, List<int>> segmentMap = [];
        double cellRadius = SphericalCapTraversal.EstimateCellRadius(resolution);
        double sampleInterval = cellRadius * SampleIntervalFactor;

        int segmentOffset = 0;
        for(int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            LonLat[] ring = rings[ringIndex];
            Cartesian[] ringVectors = ringVectorsList[ringIndex];

            ulong[] vertexCells = new ulong[ring.Length];
            for(int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
            {
                vertexCells[vertexIndex] = Cell.LonLatToCell(ring[vertexIndex], resolution);
            }

            for(int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
            {
                int nextVertexIndex = (vertexIndex + 1) % ring.Length;
                RecordCell(boundaryCells, boundarySet, segmentMap, vertexCells[vertexIndex], segmentOffset + vertexIndex);

                // Skip the lonLat round trip: samples are already authalic-Cartesian.
                Cartesian[] samples = GreatCircle.SampleGreatCircleArc(ringVectors[vertexIndex], ringVectors[nextVertexIndex], sampleInterval);
                foreach(Cartesian sample in samples)
                {
                    ulong sampledCell = Cell.SphericalToCell(CoordinateTransforms.ToSpherical(sample), resolution);
                    RecordCell(boundaryCells, boundarySet, segmentMap, sampledCell, segmentOffset + vertexIndex);
                }

                RecordCell(boundaryCells, boundarySet, segmentMap, vertexCells[nextVertexIndex], segmentOffset + vertexIndex);
            }

            segmentOffset += ring.Length;
        }

        return (boundaryCells, boundarySet, segmentMap);
    }

    /// <summary>Records a boundary-sampled cell, deduplicating into <paramref name="boundaryCells"/>/<paramref name="boundarySet"/> and appending the segment index if not already the last one recorded for this cell.</summary>
    private static void RecordCell(List<ulong> boundaryCells, HashSet<ulong> boundarySet, Dictionary<ulong, List<int>> segmentMap, ulong cell, int segmentIndex)
    {
        if(boundarySet.Add(cell))
        {
            boundaryCells.Add(cell);
        }

        if(segmentMap.TryGetValue(cell, out List<int>? existing))
        {
            if(existing[^1] != segmentIndex)
            {
                existing.Add(segmentIndex);
            }
        }
        else
        {
            segmentMap[cell] = [segmentIndex];
        }
    }

    /// <summary>
    /// Filters boundary cells to those whose center is inside the polygon. For each cell, checks which
    /// ring segment(s) sampled it: when every one of those segments places the cell on the interior
    /// side (a cheap signed-dot test), it's accepted immediately; when they disagree (a vertex or
    /// concave corner) or the cell wasn't recorded, falls back to full point-in-polygon.
    /// </summary>
    private static List<ulong> FilterBoundaryCells(
        List<ulong> boundaryCells,
        Dictionary<ulong, List<int>> segmentMap,
        Cartesian[] segmentNormals,
        int[] segmentSigns,
        Cartesian[][] ringVectorsList)
    {
        List<ulong> accepted = [];
        foreach(ulong cell in boundaryCells)
        {
            Cartesian cellVector = CoordinateTransforms.ToCartesian(Cell.CellToSpherical(cell));
            if(!segmentMap.TryGetValue(cell, out List<int>? segments))
            {
                if(PointInPolygonRings(cellVector, ringVectorsList))
                {
                    accepted.Add(cell);
                }

                continue;
            }

            bool allInside = true;
            bool anyInside = false;
            bool ambiguous = false;
            foreach(int segmentIndex in segments)
            {
                Cartesian normal = segmentNormals[segmentIndex];
                double dot = (normal.X * cellVector.X) + (normal.Y * cellVector.Y) + (normal.Z * cellVector.Z);
                if(Math.Abs(dot) < AmbiguousDotEpsilon)
                {
                    // On the segment within float epsilon.
                    ambiguous = true;
                    break;
                }

                if(dot * segmentSigns[segmentIndex] > 0)
                {
                    anyInside = true;
                }
                else
                {
                    allInside = false;
                }
            }

            if(ambiguous || (anyInside && !allInside))
            {
                if(PointInPolygonRings(cellVector, ringVectorsList))
                {
                    accepted.Add(cell);
                }
            }
            else if(allInside)
            {
                accepted.Add(cell);
            }
        }

        return accepted;
    }

    /// <summary>Point-in-polygon for a polygon with holes: inside the outer ring and outside every hole ring.</summary>
    private static bool PointInPolygonRings(Cartesian point, Cartesian[][] ringVectorsList)
    {
        if(!SphericalPolygonPrimitives.PointInSphericalPolygon(point, ringVectorsList[0]))
        {
            return false;
        }

        for(int ringIndex = 1; ringIndex < ringVectorsList.Length; ringIndex++)
        {
            if(SphericalPolygonPrimitives.PointInSphericalPolygon(point, ringVectorsList[ringIndex]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Buffers the boundary by one cell using 3-edge lattice neighbors. The shell matches
    /// <see cref="LatticeFloodFill"/>'s connectivity, so the firewall (boundary plus exterior shell) is
    /// a tight topological barrier for the subsequent flood.
    /// </summary>
    private static List<ulong> ExpandShell(List<ulong> boundaryCells, HashSet<ulong> boundarySet)
    {
        List<ulong> shellCells = [];
        HashSet<ulong> shellSet = [];
        foreach(ulong cell in boundaryCells)
        {
            foreach(ulong neighbor in LatticeNeighbors.GetLatticeNeighbors(cell, true))
            {
                if(boundarySet.Contains(neighbor))
                {
                    continue;
                }

                if(shellSet.Add(neighbor))
                {
                    shellCells.Add(neighbor);
                }
            }
        }

        return shellCells;
    }

    /// <summary>
    /// Hierarchical flood fill from interior seed cells: a few fine breadth-first-search layers clear
    /// the boundary, then a coarse-resolution search sweeps through the bulk, then fine search resumes
    /// to fill gaps near the boundary. The coarse phase is skipped when the polygon is too small to
    /// amortize its setup overhead.
    /// </summary>
    private static List<ulong> FloodInterior(List<ulong> interiorSeeds, HashSet<ulong> visited, int boundarySize, int resolution)
    {
        foreach(ulong cell in interiorSeeds)
        {
            visited.Add(cell);
        }

        // Isoperimetric bound: boundarySize² / (4π) is the maximum interior area for this boundary size.
        double maxInterior = ((double)boundarySize * boundarySize) / IsoperimetricDivisor;

        // Resolution 30 has a different encoding this optimization cannot use: Serialization.Serialize's
        // fourth fallback silently re-serializes any cell whose quintant number is 42 or above at
        // resolution 29 with S right-shifted by 2 (Core/Serialization.cs). That means
        // Serialization.CellToChildren(coarseCell, 30) is not guaranteed to return cells actually AT
        // resolution 30 for every coarse parent — some children can silently land one resolution short —
        // which breaks this phase's core assumption that a coarse parent's children at "resolution" are
        // exactly the fine cells it represents. The guard below is still exactly right, so the coarse
        // phase never runs at resolution 30.
        bool useCoarsePhase = resolution > Serialization.FirstHilbertResolution
            && resolution < Serialization.MaxResolution
            && maxInterior > CoarsePhaseAreaThreshold;

        if(!useCoarsePhase)
        {
            LatticeFloodFillResult result = LatticeFloodFill.TripleSpaceFloodFill(visited, interiorSeeds.ToArray(), resolution);

            return [.. interiorSeeds, .. result.InteriorCells];
        }

        int parentResolution = resolution - 1;
        HashSet<ulong> coarseFirewall = [];
        foreach(ulong cell in visited)
        {
            coarseFirewall.Add(Serialization.CellToParent(cell, parentResolution));
        }

        // Phase 1: a short fine breadth-first search to move the frontier off the boundary.
        LatticeFloodFillResult phase1 = LatticeFloodFill.TripleSpaceFloodFill(visited, interiorSeeds.ToArray(), resolution, FineBreadthFirstSearchLayerCap);

        // Phase 2: a coarse breadth-first search through the bulk interior.
        HashSet<ulong>? coarseInteriorSet = null;
        List<ulong> phase3Delta = [];
        List<ulong> coarseInteriorCells = [];
        if(phase1.FrontierCellIds.Length > 0)
        {
            HashSet<ulong> coarseSeeds = [];
            foreach(ulong cell in phase1.FrontierCellIds)
            {
                ulong parent = Serialization.CellToParent(cell, parentResolution);
                if(!coarseFirewall.Contains(parent))
                {
                    coarseSeeds.Add(parent);
                }
            }

            if(coarseSeeds.Count > 0)
            {
                HashSet<ulong> coarseVisited = [.. coarseFirewall];
                foreach(ulong seed in coarseSeeds)
                {
                    coarseVisited.Add(seed);
                }

                ulong[] coarseSeedArray = [.. coarseSeeds];
                LatticeFloodFillResult coarseResult = LatticeFloodFill.TripleSpaceFloodFill(coarseVisited, coarseSeedArray, parentResolution);
                List<ulong> coarseInterior = [.. coarseSeedArray, .. coarseResult.InteriorCells];
                coarseInteriorSet = [.. coarseInterior];
                coarseInteriorCells.AddRange(coarseInterior);

                // Children become the firewall for phase 3; the coarse parent represents them in the
                // output, so they are not emitted individually.
                foreach(ulong coarseCell in coarseInterior)
                {
                    foreach(ulong child in Serialization.CellToChildren(coarseCell, resolution))
                    {
                        if(visited.Add(child))
                        {
                            phase3Delta.Add(child);
                        }
                    }
                }
            }
        }

        // Emit fine cells only when not already covered by a coarse parent.
        List<ulong> interiorCells = [];
        if(coarseInteriorSet is null)
        {
            interiorCells.AddRange(interiorSeeds);
            interiorCells.AddRange(phase1.InteriorCells);
        }
        else
        {
            foreach(ulong cell in interiorSeeds)
            {
                if(!coarseInteriorSet.Contains(Serialization.CellToParent(cell, parentResolution)))
                {
                    interiorCells.Add(cell);
                }
            }

            foreach(ulong cell in phase1.InteriorCells)
            {
                if(!coarseInteriorSet.Contains(Serialization.CellToParent(cell, parentResolution)))
                {
                    interiorCells.Add(cell);
                }
            }

            interiorCells.AddRange(coarseInteriorCells);
        }

        // Phase 3: resume the fine breadth-first search, reusing phase 1's packed state.
        LatticeFloodFillResult phase3 = LatticeFloodFill.TripleSpaceFloodFill(phase1.State, phase3Delta.ToArray(), phase1.FrontierCellIds, resolution);
        interiorCells.AddRange(phase3.InteriorCells);

        return interiorCells;
    }
}
