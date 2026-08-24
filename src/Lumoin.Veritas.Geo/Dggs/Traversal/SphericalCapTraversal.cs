using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Spherical caps: all cells within a great-circle radius of a center cell, found via hierarchical
/// breadth-first search that starts at a coarse resolution and subdivides only boundary cells, keeping
/// interior cells at coarser resolutions.
/// </summary>
internal static class SphericalCapTraversal
{
    /// <summary>Safety factor applied to the equal-area circle radius to get a conservative circumradius estimate.</summary>
    private const double CellRadiusSafetyFactor = 2.0;

    /// <summary>Minimum cells a cap must contain before hierarchical subdivision is worthwhile.</summary>
    private const int MinCellsForSubdivision = 20;

    /// <summary>
    /// The base term of <see cref="EstimateCellRadius"/>'s closed form for resolutions 1 and up:
    /// <c>cellRadius(r) = BaseCellRadius / 2^(r-1)</c>, derived from
    /// <c>cellRadius = SAFETY * sqrt(cellArea / π) = SAFETY * sqrt(4πR² / (numCells × π)) = SAFETY × 2R / sqrt(numCells)</c>
    /// with <c>numCells = 60 × 4^(r-1)</c> so that <c>sqrt(numCells) = 2√15 × 2^(r-1)</c>.
    /// </summary>
    private static double BaseCellRadius { get; } = (CellRadiusSafetyFactor * Constants.AuthalicRadiusEarth) / Math.Sqrt(15);

    /// <summary>
    /// Conservative cell circumradius in meters, indexed by resolution (0-30). Entry 0 uses a distinct
    /// formula (<c>sqrt(3)</c>, not <c>sqrt(15)</c>) rather than being derived from
    /// <see cref="BaseCellRadius"/>.
    /// </summary>
    private static double[] CellRadiusTable { get; } = BuildCellRadiusTable();

    /// <summary>
    /// Converts a distance in meters to a haversine threshold value. Since haversine <c>h = sin²(d / 2R)</c>
    /// is monotonic in <c>d</c> for <c>d ∈ [0, πR]</c>, comparing <c>h ≤ threshold</c> is equivalent to
    /// comparing <c>dist ≤ radius</c> but avoids an asin/sqrt per point.
    /// </summary>
    public static double MetersToH(double meters)
    {
        double s = Math.Sin(meters / (2 * Constants.AuthalicRadiusEarth));

        return s * s;
    }

    /// <summary>
    /// Estimates a conservative cell circumradius in meters for <paramref name="resolution"/>. Guards
    /// against an out-of-range resolution explicitly rather than indexing past the table.
    /// </summary>
    public static double EstimateCellRadius(int resolution)
    {
        if(resolution < 0 || resolution > Serialization.MaxResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, $"Resolution must be between 0 and {Serialization.MaxResolution} inclusive.");
        }

        return CellRadiusTable[resolution];
    }

    /// <summary>
    /// Picks the coarsest resolution at or below <paramref name="targetResolution"/> where the cap
    /// contains enough cells to make hierarchical subdivision worthwhile.
    /// </summary>
    public static int PickCoarseResolution(double radius, int targetResolution)
    {
        double capAreaSquareMeters = 2 * Math.PI * Constants.AuthalicRadiusEarth * Constants.AuthalicRadiusEarth * (1 - Math.Cos(radius / Constants.AuthalicRadiusEarth));

        for(int resolution = Serialization.FirstHilbertResolution; resolution <= targetResolution; resolution++)
        {
            double cellArea = CellInfo.CellArea(resolution);
            if(capAreaSquareMeters / cellArea >= MinCellsForSubdivision)
            {
                return resolution;
            }
        }

        return targetResolution; // No coarsening benefit.
    }

    /// <summary>
    /// Computes all cells within a great-circle radius of <paramref name="cellId"/>, returning a
    /// naturally compacted result (a mix of resolutions).
    /// </summary>
    /// <param name="cellId">The center cell.</param>
    /// <param name="radius">The radius, in meters.</param>
    /// <remarks>
    /// Distance comparisons use the haversine intermediate value <c>h = sin²(d / 2R)</c> directly,
    /// avoiding an asin/sqrt per cell: pre-computed <c>h</c> thresholds replace meter-based distance
    /// checks.
    /// </remarks>
    /// <returns>A sorted, compacted array of cell ids at mixed resolutions.</returns>
    public static ulong[] SphericalCap(ulong cellId, double radius)
    {
        int targetResolution = Serialization.GetResolution(cellId);
        int coarseResolution = PickCoarseResolution(radius, targetResolution);
        Spherical center = Cell.CellToSpherical(cellId);

        // Pre-compute the haversine threshold for the exact radius.
        double hRadius = MetersToH(radius);

        // Breadth-first search at the coarse resolution with an expanded radius to capture every overlapping cell.
        ulong startCell = coarseResolution < targetResolution ? Serialization.CellToParent(cellId, coarseResolution) : cellId;
        double coarseCellRadius = EstimateCellRadius(coarseResolution);
        double hExpanded = MetersToH(radius + coarseCellRadius);
        HashSet<ulong> coarseVisited = [startCell];
        HashSet<ulong> coarseFrontier = [startCell];

        while(coarseFrontier.Count > 0)
        {
            HashSet<ulong> nextFrontier = [];
            foreach(ulong id in coarseFrontier)
            {
                foreach(ulong neighbor in GlobalNeighbors.GetGlobalCellNeighbors(id))
                {
                    if(coarseVisited.Contains(neighbor))
                    {
                        continue;
                    }

                    coarseVisited.Add(neighbor);
                    if(Origins.Haversine(center, Cell.CellToSpherical(neighbor)) <= hExpanded)
                    {
                        nextFrontier.Add(neighbor);
                    }
                }
            }

            coarseFrontier = nextFrontier;
        }

        // Recursive subdivision from coarseResolution to targetResolution. Each cell is classified by
        // comparing haversine(center, cell) against pre-computed h thresholds: interior (h <= hInner)
        // stays compacted since every descendant is inside; outside (h > hOuter) is discarded since no
        // descendant is inside; otherwise the cell is a boundary cell, subdivided to the next level.
        List<ulong> result = [];
        List<ulong> boundary = [.. coarseVisited];

        for(int resolution = coarseResolution; resolution < targetResolution; resolution++)
        {
            double cellRadius = EstimateCellRadius(resolution);
            double hInner = radius > cellRadius ? MetersToH(radius - cellRadius) : -1;
            double hOuter = MetersToH(radius + cellRadius);
            List<ulong> nextBoundary = [];

            foreach(ulong cell in boundary)
            {
                double h = Origins.Haversine(center, Cell.CellToSpherical(cell));
                if(h <= hInner)
                {
                    result.Add(cell);
                }
                else if(h > hOuter)
                {
                    // Cell's entire extent is outside the cap — discard.
                }
                else
                {
                    foreach(ulong child in Serialization.CellToChildren(cell, resolution + 1))
                    {
                        nextBoundary.Add(child);
                    }
                }
            }

            boundary = nextBoundary;
        }

        // Final target resolution: a strict haversine check.
        foreach(ulong cell in boundary)
        {
            if(Origins.Haversine(center, Cell.CellToSpherical(cell)) <= hRadius)
            {
                result.Add(cell);
            }
        }

        ulong[] output = [.. result];

        // Unsigned ascending sort — Array.Sort operates on ulong directly, never a signed-long compare.
        Array.Sort(output);

        return output;
    }

    /// <summary>Builds the 31-entry cell-radius lookup table once, at static-field initialization.</summary>
    private static double[] BuildCellRadiusTable()
    {
        double[] table = new double[Serialization.MaxResolution + 1];
        table[0] = (CellRadiusSafetyFactor * Constants.AuthalicRadiusEarth) / Math.Sqrt(3);
        for(int resolution = 1; resolution <= Serialization.MaxResolution; resolution++)
        {
            table[resolution] = BaseCellRadius / (1 << (resolution - 1));
        }

        return table;
    }
}
