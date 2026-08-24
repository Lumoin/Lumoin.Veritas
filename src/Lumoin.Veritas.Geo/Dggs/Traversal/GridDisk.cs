using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Breadth-first-search grid disks — every cell within a fixed number of neighbor hops of a center
/// cell, compacted.
/// </summary>
internal static class GridDisk
{
    /// <summary>
    /// Computes the grid disk of edge-sharing neighbors within <paramref name="k"/> hops of
    /// <paramref name="cellId"/>, including the center cell.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="k"/> degenerates gracefully to the same result as <c>k = 0</c> — the
    /// ring loop's bounds never execute — rather than throwing.
    /// </remarks>
    /// <returns>A sorted, compacted array of cell ids in the disk.</returns>
    public static ulong[] GetGridDisk(ulong cellId, int k)
    {
        return RunBreadthFirstSearch(cellId, k, edgeOnly: true);
    }

    /// <summary>
    /// Computes the grid disk of all neighbors (edge- and vertex-sharing) within <paramref name="k"/>
    /// hops of <paramref name="cellId"/>, including the center cell.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="k"/> degenerates gracefully to the same result as <c>k = 0</c> — the
    /// ring loop's bounds never execute — rather than throwing.
    /// </remarks>
    /// <returns>A sorted, compacted array of cell ids in the disk.</returns>
    public static ulong[] GetGridDiskVertex(ulong cellId, int k)
    {
        return RunBreadthFirstSearch(cellId, k, edgeOnly: false);
    }

    /// <summary>
    /// Runs the shared breadth-first search backing both <see cref="GetGridDisk"/> and
    /// <see cref="GetGridDiskVertex"/>: a sliding-window dedup keeps only the previous and current
    /// frontier rings in memory (breadth-first search guarantees cells two or more rings behind the
    /// frontier can never be re-discovered); evicted interior cells are periodically compacted to
    /// reduce memory pressure.
    /// </summary>
    private static ulong[] RunBreadthFirstSearch(ulong cellId, int k, bool edgeOnly)
    {
        if(k == 0)
        {
            return [cellId];
        }

        List<ulong> interior = [];
        HashSet<ulong> previousFrontier = [];
        HashSet<ulong> frontier = [cellId];

        for(int ring = 1; ring <= k; ring++)
        {
            HashSet<ulong> nextFrontier = [];
            foreach(ulong id in frontier)
            {
                foreach(ulong neighbor in GlobalNeighbors.GetGlobalCellNeighbors(id, edgeOnly))
                {
                    if(!previousFrontier.Contains(neighbor) && !frontier.Contains(neighbor) && !nextFrontier.Contains(neighbor))
                    {
                        nextFrontier.Add(neighbor);
                    }
                }
            }

            // Evict previousFrontier — these cells are two or more rings behind the new frontier and
            // can never be re-discovered by the search.
            foreach(ulong id in previousFrontier)
            {
                interior.Add(id);
            }

            // Progressively compact the interior to reduce memory pressure.
            if(interior.Count > 100)
            {
                interior = [.. Compaction.Compact(interior.ToArray())];
            }

            previousFrontier = frontier;
            frontier = nextFrontier;
        }

        // Merge the remaining boundary rings with the compacted interior.
        foreach(ulong id in previousFrontier)
        {
            interior.Add(id);
        }

        foreach(ulong id in frontier)
        {
            interior.Add(id);
        }

        return Compaction.Compact(interior.ToArray());
    }
}
