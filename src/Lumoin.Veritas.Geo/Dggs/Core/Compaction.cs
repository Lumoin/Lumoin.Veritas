using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Compaction and expansion of cell-id sets: replacing a complete sibling group with its parent
/// (<see cref="Compact"/>) and the inverse, expanding every cell to a target resolution
/// (<see cref="Uncompact"/>).
/// </summary>
internal static class Compaction
{
    /// <summary>
    /// Expands every cell in <paramref name="cells"/> to all of its descendants at
    /// <paramref name="targetResolution"/>. If the input is sorted, the output is sorted too: a cell id
    /// encodes origin/quintant in the high bits and Hilbert position below that, so all children of a
    /// cell form a contiguous, ordered block in id space, and <c>children(A) &lt; children(B)</c>
    /// whenever <c>A &lt; B</c>.
    /// </summary>
    public static ulong[] Uncompact(ReadOnlySpan<ulong> cells, int targetResolution)
    {
        int[] resolutions = new int[cells.Length];
        long totalCount = 0;
        for(int index = 0; index < cells.Length; index++)
        {
            int resolution = Serialization.GetResolution(cells[index]);
            if(targetResolution < resolution)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetResolution),
                    targetResolution,
                    $"Cannot uncompact cell at resolution {resolution} to lower resolution {targetResolution}");
            }

            resolutions[index] = resolution;
            totalCount = checked(totalCount + (long)CellInfo.GetNumChildren(resolution, targetResolution));
        }

        ulong[] result = new ulong[totalCount];
        int offset = 0;
        for(int index = 0; index < cells.Length; index++)
        {
            ulong cell = cells[index];
            int resolution = resolutions[index];
            int numChildren = checked((int)CellInfo.GetNumChildren(resolution, targetResolution));

            if(numChildren == 1)
            {
                result[offset] = cell;
            }
            else
            {
                ulong[] children = Serialization.CellToChildren(cell, targetResolution);
                Array.Copy(children, 0, result, offset, children.Length);
            }

            offset += numChildren;
        }

        return result;
    }

    /// <summary>
    /// Compacts <paramref name="cells"/> using a forward-scanning algorithm: deduplicates and sorts
    /// once, then repeatedly replaces complete sibling groups with their parent until no more groups
    /// compact (parents stay in sorted order for free, so no re-sort is needed between passes). Output
    /// is unsigned-64 ascending; a plain sort suffices because deduplication has already made every key
    /// unique, so no tie-breaking is observable.
    /// </summary>
    public static ulong[] Compact(ReadOnlySpan<ulong> cells)
    {
        if(cells.Length == 0)
        {
            return [];
        }

        HashSet<ulong> uniqueCells = new(cells.Length);
        foreach(ulong cell in cells)
        {
            uniqueCells.Add(cell);
        }

        List<ulong> currentCells = new(uniqueCells);
        currentCells.Sort();

        bool changed = true;
        while(changed)
        {
            changed = false;
            List<ulong> result = new(currentCells.Count);
            int index = 0;

            while(index < currentCells.Count)
            {
                ulong cell = currentCells[index];
                int resolution = Serialization.GetResolution(cell);

                // Can't compact below resolution 0.
                if(resolution < 0)
                {
                    result.Add(cell);
                    index++;
                    continue;
                }

                int expectedChildren = ExpectedChildrenCount(resolution);

                if(index + expectedChildren <= currentCells.Count && HasCompleteSiblingGroup(currentCells, index, cell, resolution, expectedChildren))
                {
                    ulong parent = Serialization.CellToParent(cell);
                    result.Add(parent);
                    index += expectedChildren;
                    changed = true;
                    continue;
                }

                result.Add(cell);
                index++;
            }

            currentCells = result;
        }

        return currentCells.ToArray();
    }

    /// <summary>
    /// Number of siblings a complete group has at <paramref name="resolution"/>: 4 in the Hilbert range,
    /// 12 at the root, 5 at the first (non-Hilbert) subdivision — never a single hardcoded value.
    /// </summary>
    private static int ExpectedChildrenCount(int resolution)
    {
        if(resolution >= Serialization.FirstHilbertResolution)
        {
            return 4;
        }

        return resolution == 0 ? 12 : 5;
    }

    /// <summary>
    /// Tests whether <paramref name="currentCells"/>, starting at <paramref name="index"/>, holds a
    /// complete, contiguously-strided sibling group beginning with <paramref name="cell"/>.
    /// <see cref="Serialization.IsFirstChild"/> must be checked before trusting the stride scan — a cell
    /// that is not itself a first child could still happen to be followed by same-stride values that
    /// are not actually its siblings (order-of-check hazard).
    /// </summary>
    private static bool HasCompleteSiblingGroup(List<ulong> currentCells, int index, ulong cell, int resolution, int expectedChildren)
    {
        if(!Serialization.IsFirstChild(cell, resolution))
        {
            return false;
        }

        ulong stride = Serialization.GetStride(resolution);

        // checked: a malformed input could overflow this pointer-style arithmetic, and it must fail
        // loudly rather than wrap.
        for(int sibling = 1; sibling < expectedChildren; sibling++)
        {
            ulong expectedCell = checked(cell + ((ulong)sibling * stride));
            if(currentCells[index + sibling] != expectedCell)
            {
                return false;
            }
        }

        return true;
    }
}
