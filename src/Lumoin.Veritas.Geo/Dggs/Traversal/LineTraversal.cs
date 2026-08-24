using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Traces cells along a polyline of great-circle-connected waypoints.
/// </summary>
internal static class LineTraversal
{
    /// <summary>Sample-interval factor applied to the cell radius (denser than <see cref="Regions.PolygonToCells"/>'s 0.4 — line tracing needs less margin than area filling).</summary>
    private const double SampleIntervalFactor = 0.5;

    /// <summary>
    /// Traces cells along a polyline defined by a sequence of <paramref name="waypoints"/>.
    /// </summary>
    /// <param name="waypoints">The polyline's waypoints, in geographic coordinates.</param>
    /// <param name="resolution">The resolution to trace cells at.</param>
    /// <remarks>
    /// Consecutive waypoints are connected with great-circle arcs, each sampled at half-cell-radius
    /// intervals; for every consecutive sample pair, a strict local breadth-first search finds every
    /// cell whose pentagon the straight 2D segment between the two samples touches, projected onto each
    /// candidate cell's face. Endpoint <see cref="LonLat"/> values are used exactly as passed, while
    /// every interior sample instead round-trips through Cartesian and spherical coordinates. Output
    /// order is the traversal order along the polyline, not a sorted order — semantically load-bearing
    /// downstream, so an insertion-order list plus a dedup set is used throughout, never bare
    /// <see cref="HashSet{T}"/> enumeration.
    /// </remarks>
    /// <returns>The unique cell ids touched by the polyline, in traversal order.</returns>
    public static ulong[] LineStringToCells(ReadOnlySpan<LonLat> waypoints, int resolution)
    {
        if(waypoints.Length == 0)
        {
            return [];
        }

        if(waypoints.Length == 1)
        {
            return [Cell.LonLatToCell(waypoints[0], resolution)];
        }

        HashSet<ulong> seen = [];
        List<ulong> result = [];
        double cellRadius = SphericalCapTraversal.EstimateCellRadius(resolution);
        double sampleInterval = cellRadius * SampleIntervalFactor;

        for(int index = 0; index < waypoints.Length - 1; index++)
        {
            LonLat start = waypoints[index];
            LonLat end = waypoints[index + 1];
            Cartesian startVector = CoordinateTransforms.ToCartesian(CoordinateTransforms.FromLonLat(start));
            Cartesian endVector = CoordinateTransforms.ToCartesian(CoordinateTransforms.FromLonLat(end));

            // Sample the great circle at half-cell-radius spacing. Endpoints are always included, even
            // for short hops, so the start-to-end pair is present regardless.
            Cartesian[] interior = GreatCircle.SampleGreatCircleArc(startVector, endVector, sampleInterval);
            int subsegmentCount = interior.Length + 1;
            LonLat[] samples = new LonLat[subsegmentCount + 1];
            samples[0] = start;
            samples[subsegmentCount] = end;
            for(int sampleIndex = 0; sampleIndex < interior.Length; sampleIndex++)
            {
                samples[sampleIndex + 1] = CoordinateTransforms.ToLonLat(CoordinateTransforms.ToSpherical(interior[sampleIndex]));
            }

            ulong[] sampleCells = new ulong[samples.Length];
            for(int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                sampleCells[sampleIndex] = Cell.LonLatToCell(samples[sampleIndex], resolution);
            }

            // Walk pairwise. Each (P_j, P_j+1) sub-segment is short enough that its projection onto any
            // nearby cell's face is essentially straight, so exact 2D segment-versus-pentagon
            // intersection applies.
            for(int subsegmentIndex = 0; subsegmentIndex < subsegmentCount; subsegmentIndex++)
            {
                LonLat segmentStart = samples[subsegmentIndex];
                LonLat segmentEnd = samples[subsegmentIndex + 1];
                ulong cellA = sampleCells[subsegmentIndex];
                ulong cellB = sampleCells[subsegmentIndex + 1];

                AddUnique(result, seen, cellA);
                AddUnique(result, seen, cellB);
                if(cellA == cellB)
                {
                    continue;
                }

                // Strict local breadth-first search: expand neighbors of every cell known to touch this
                // sub-segment, keeping anything whose pentagon the sub-segment crosses. Terminates as
                // soon as no new touching cells are found — typically 1-2 hops, since a sub-segment no
                // longer than half a cell radius reaches at most a couple of cells beyond its endpoint cells.
                HashSet<ulong> visited = [cellA, cellB];
                List<ulong> frontier = [cellA, cellB];
                while(frontier.Count > 0)
                {
                    List<ulong> next = [];
                    foreach(ulong cell in frontier)
                    {
                        foreach(ulong neighbor in LatticeNeighbors.GetLatticeNeighbors(cell, false))
                        {
                            if(!visited.Add(neighbor))
                            {
                                continue;
                            }

                            if(Cell.CellIntersectsSegment(neighbor, segmentStart, segmentEnd))
                            {
                                AddUnique(result, seen, neighbor);
                                next.Add(neighbor);
                            }
                        }
                    }

                    frontier = next;
                }
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Appends <paramref name="value"/> to <paramref name="orderedList"/> only if not already present,
    /// tracked via the accompanying <paramref name="seen"/> set — an explicit insertion-order-preserving
    /// list+set pair rather than bare <see cref="HashSet{T}"/> enumeration.
    /// </summary>
    private static void AddUnique(List<ulong> orderedList, HashSet<ulong> seen, ulong value)
    {
        if(seen.Add(value))
        {
            orderedList.Add(value);
        }
    }
}
