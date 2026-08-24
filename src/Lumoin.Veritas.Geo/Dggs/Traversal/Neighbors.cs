using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Within-quintant neighbor pattern lookup, keyed by pentagon flavor (<see cref="Tiling.GetPentagonFlavor"/>,
/// 0-7): eight tables of relative <c>(offsetDeltaI, offsetDeltaJ, relativeFlipX, relativeFlipY)</c>
/// quadruples with no derivable formula, transcribed verbatim.
/// </summary>
internal static class Neighbors
{
    /// <summary>One relative-offset/relative-flip quadruple a candidate anchor must match to be a neighbor of a given flavor.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay("({OffsetDeltaI}, {OffsetDeltaJ}, {FlipX}, {FlipY})")]
    private readonly record struct NeighborPattern(int OffsetDeltaI, int OffsetDeltaJ, Flip FlipX, Flip FlipY);

    /// <summary>The eight flavor-keyed neighbor pattern tables, indexed directly by pentagon flavor (0-7).</summary>
    private static NeighborPattern[][] NeighborsByFlavor { get; } =
    [
        // Flavor 0.
        [
            new NeighborPattern(0, -2, Flip.Yes, Flip.No), new NeighborPattern(0, -2, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, -1, Flip.No, Flip.Yes), new NeighborPattern(0, -1, Flip.Yes, Flip.Yes), new NeighborPattern(0, -1, Flip.No, Flip.No),
            new NeighborPattern(1, -2, Flip.Yes, Flip.Yes),
            new NeighborPattern(1, -1, Flip.Yes, Flip.No), new NeighborPattern(1, -1, Flip.No, Flip.Yes),
            new NeighborPattern(1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(2, -1, Flip.No, Flip.Yes),
            new NeighborPattern(2, -2, Flip.Yes, Flip.Yes)
        ],

        // Flavor 1.
        [
            new NeighborPattern(-1, -1, Flip.Yes, Flip.No),
            new NeighborPattern(0, -2, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, -1, Flip.Yes, Flip.Yes), new NeighborPattern(0, -1, Flip.No, Flip.Yes),
            new NeighborPattern(0, 0, Flip.Yes, Flip.No), new NeighborPattern(0, 0, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, 1, Flip.No, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.No),
            new NeighborPattern(1, -2, Flip.Yes, Flip.Yes),
            new NeighborPattern(1, -1, Flip.No, Flip.Yes), new NeighborPattern(1, -1, Flip.Yes, Flip.Yes),
            new NeighborPattern(1, 0, Flip.No, Flip.Yes)
        ],

        // Flavor 2.
        [
            new NeighborPattern(-2, 2, Flip.Yes, Flip.Yes),
            new NeighborPattern(-2, 1, Flip.No, Flip.Yes),
            new NeighborPattern(-1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(-1, 1, Flip.No, Flip.Yes), new NeighborPattern(-1, 1, Flip.Yes, Flip.No),
            new NeighborPattern(-1, 2, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, 1, Flip.Yes, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.No),
            new NeighborPattern(0, 2, Flip.Yes, Flip.Yes), new NeighborPattern(0, 2, Flip.Yes, Flip.No)
        ],

        // Flavor 3.
        [
            new NeighborPattern(-1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(-1, 1, Flip.No, Flip.Yes), new NeighborPattern(-1, 1, Flip.Yes, Flip.Yes),
            new NeighborPattern(-1, 2, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, -1, Flip.No, Flip.Yes), new NeighborPattern(0, -1, Flip.No, Flip.No),
            new NeighborPattern(0, 0, Flip.Yes, Flip.Yes), new NeighborPattern(0, 0, Flip.Yes, Flip.No),
            new NeighborPattern(0, 1, Flip.Yes, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.Yes),
            new NeighborPattern(0, 2, Flip.Yes, Flip.Yes),
            new NeighborPattern(1, 1, Flip.Yes, Flip.No)
        ],

        // Flavor 4.
        [
            new NeighborPattern(0, -1, Flip.No, Flip.Yes), new NeighborPattern(0, -1, Flip.No, Flip.No),
            new NeighborPattern(0, 0, Flip.Yes, Flip.Yes), new NeighborPattern(0, 0, Flip.Yes, Flip.No),
            new NeighborPattern(0, 1, Flip.Yes, Flip.Yes),
            new NeighborPattern(1, 0, Flip.Yes, Flip.Yes), new NeighborPattern(1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(1, -1, Flip.No, Flip.Yes), new NeighborPattern(1, 1, Flip.Yes, Flip.No),
            new NeighborPattern(2, -1, Flip.No, Flip.Yes), new NeighborPattern(2, 0, Flip.Yes, Flip.Yes)
        ],

        // Flavor 5.
        [
            new NeighborPattern(-1, 1, Flip.Yes, Flip.No),
            new NeighborPattern(0, -1, Flip.No, Flip.Yes),
            new NeighborPattern(0, 0, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, 1, Flip.Yes, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.No),
            new NeighborPattern(0, 2, Flip.Yes, Flip.Yes), new NeighborPattern(0, 2, Flip.Yes, Flip.No),
            new NeighborPattern(1, -1, Flip.No, Flip.Yes),
            new NeighborPattern(1, 0, Flip.Yes, Flip.Yes), new NeighborPattern(1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(1, 1, Flip.Yes, Flip.Yes)
        ],

        // Flavor 6.
        [
            new NeighborPattern(-2, 0, Flip.Yes, Flip.Yes),
            new NeighborPattern(-2, 1, Flip.No, Flip.Yes),
            new NeighborPattern(-1, -1, Flip.Yes, Flip.No),
            new NeighborPattern(-1, 0, Flip.Yes, Flip.Yes), new NeighborPattern(-1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(-1, 1, Flip.No, Flip.Yes),
            new NeighborPattern(0, -1, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, 0, Flip.Yes, Flip.Yes), new NeighborPattern(0, 0, Flip.Yes, Flip.No),
            new NeighborPattern(0, 1, Flip.No, Flip.Yes), new NeighborPattern(0, 1, Flip.No, Flip.No)
        ],

        // Flavor 7.
        [
            new NeighborPattern(-1, -1, Flip.Yes, Flip.Yes),
            new NeighborPattern(-1, 0, Flip.Yes, Flip.Yes), new NeighborPattern(-1, 0, Flip.No, Flip.Yes),
            new NeighborPattern(-1, 1, Flip.No, Flip.Yes),
            new NeighborPattern(0, -2, Flip.Yes, Flip.Yes), new NeighborPattern(0, -2, Flip.Yes, Flip.No),
            new NeighborPattern(0, -1, Flip.Yes, Flip.Yes), new NeighborPattern(0, -1, Flip.No, Flip.Yes), new NeighborPattern(0, -1, Flip.No, Flip.No),
            new NeighborPattern(0, 0, Flip.Yes, Flip.Yes),
            new NeighborPattern(0, 1, Flip.No, Flip.Yes),
            new NeighborPattern(1, -1, Flip.Yes, Flip.No)
        ]
    ];

    /// <summary>
    /// Tests whether <paramref name="candidate"/> is a within-quintant neighbor of <paramref name="origin"/>:
    /// the two anchors must have different pentagon flavors, and their relative offset and relative flip
    /// (each candidate component multiplied by the matching origin component) must match one of
    /// <paramref name="origin"/>'s flavor's patterns.
    /// </summary>
    public static bool IsNeighbor(Anchor origin, Anchor candidate)
    {
        int originFlavor = Tiling.GetPentagonFlavor(origin);
        int candidateFlavor = Tiling.GetPentagonFlavor(candidate);
        if(originFlavor == candidateFlavor)
        {
            return false;
        }

        NeighborPattern[] patterns = NeighborsByFlavor[originFlavor];
        int relativeOffsetI = (int)(candidate.Offset.I - origin.Offset.I);
        int relativeOffsetJ = (int)(candidate.Offset.J - origin.Offset.J);
        Flip relativeFlipX = (Flip)((int)candidate.Flips.FlipX * (int)origin.Flips.FlipX);
        Flip relativeFlipY = (Flip)((int)candidate.Flips.FlipY * (int)origin.Flips.FlipY);

        for(int index = 0; index < patterns.Length; index++)
        {
            NeighborPattern pattern = patterns[index];
            if(pattern.OffsetDeltaI == relativeOffsetI &&
                pattern.OffsetDeltaJ == relativeOffsetJ &&
                pattern.FlipX == relativeFlipX &&
                pattern.FlipY == relativeFlipY)
            {
                return true;
            }
        }

        return false;
    }
}
