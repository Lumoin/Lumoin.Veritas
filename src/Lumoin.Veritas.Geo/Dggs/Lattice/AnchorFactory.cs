using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// Reconstructs the quaternary digit — and from it a complete <see cref="Anchor"/> — from an offset
/// and flip pair alone, using empirically discovered lookup tables. Useful when only partial anchor
/// information is available, for example from a neighbor-offset table.
/// </summary>
internal static class AnchorFactory
{
    /// <summary>
    /// Empirically discovered lookup table, verified to hold across all six orientations at
    /// resolutions 3 through 9; transcribed verbatim.
    /// Indexed [i parity][j parity][flip 0 index][flip 1 index].
    /// </summary>
    private static int[][][][] Group2Lookup { get; } =
    [
        [
            [[0, 3], [3, 0]],
            [[3, 2], [2, 3]]
        ],
        [
            [[2, 1], [1, 2]],
            [[1, 0], [0, 1]]
        ]
    ];

    /// <summary>
    /// Empirically discovered lookup table for odd-i offsets, transcribed verbatim.
    /// Indexed [j parity][flip 0 index][flip 1 index].
    /// </summary>
    private static int[][][] OddILookup { get; } =
    [
        [[3, 1], [1, 3]],
        [[1, 3], [3, 1]]
    ];

    /// <summary>
    /// Deduces the quaternary digit from an offset and flip pair. The anchor components form a
    /// constrained system where only 16 of the 64 possible (digit, i parity, j parity, flip 0,
    /// flip 1) combinations actually occur; this enables full anchor reconstruction from partial
    /// information. Parity is read via bitwise <c>&amp;</c> against 1, deliberately not <c>%</c> 2 —
    /// the two diverge for negative operands.
    /// </summary>
    public static int ComputeQ(IJ offset, FlipPair flips, Orientation orientation = Orientation.UV)
    {
        int iParity = (int)offset.I & 1;
        int jParity = (int)offset.J & 1;

        // Maps Flip.Yes (-1) to 0 and Flip.No (1) to 1.
        int flip0Index = ((int)flips.FlipX + 1) >> 1;
        int flip1Index = ((int)flips.FlipY + 1) >> 1;

        if(IsGroup2Orientation(orientation))
        {
            return Group2Lookup[iParity][jParity][flip0Index][flip1Index];
        }

        if(iParity == 0)
        {
            return jParity == 0 ? 0 : 2;
        }

        return OddILookup[jParity][flip0Index][flip1Index];
    }

    /// <summary>
    /// Builds a complete <see cref="Anchor"/> by deducing its quaternary digit from the given offset
    /// and flips — useful when constructing neighbor anchors whose offset and flips are known but
    /// whose digit is not.
    /// </summary>
    public static Anchor OffsetFlipsToAnchor(IJ offset, FlipPair flips, Orientation orientation = Orientation.UV)
    {
        int q = ComputeQ(offset, flips, orientation);

        return new Anchor(q, offset, flips);
    }

    /// <summary>
    /// Determines which of the two lookup-table groups an orientation uses: group 2 covers <c>uw</c>
    /// and <c>wu</c>; every other orientation uses group 1 (the default, even-i fast path plus
    /// <see cref="OddILookup"/>).
    /// </summary>
    private static bool IsGroup2Orientation(Orientation orientation)
    {
        return orientation == Orientation.UW || orientation == Orientation.WU;
    }
}
