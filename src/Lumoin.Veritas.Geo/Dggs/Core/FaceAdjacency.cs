using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Adjacent-face lookup: <c>Table[originId][quintant]</c> gives the primary neighboring dodecahedron
/// face id and quintant sharing that quintant's base edge. The values were determined empirically from
/// cell boundary vertex sharing at resolution 4 — there is no closed-form derivation — and are
/// transcribed verbatim.
/// </summary>
internal static class FaceAdjacency
{
    /// <summary>The full 12×5 table of <c>(adjacentOriginId, adjacentQuintant)</c> pairs, indexed by origin id then quintant.</summary>
    public static readonly (int AdjacentOriginId, int AdjacentQuintant)[][] Table =
    [
        [(1, 2), (4, 3), (5, 4), (6, 0), (11, 1)], // Origin 0.
        [(2, 3), (4, 4), (0, 0), (11, 0), (10, 1)], // Origin 1.
        [(9, 2), (3, 0), (4, 0), (1, 0), (10, 0)], // Origin 2.
        [(2, 1), (9, 1), (8, 1), (5, 1), (4, 1)], // Origin 3.
        [(2, 2), (3, 4), (5, 0), (0, 1), (1, 1)], // Origin 4.
        [(4, 2), (3, 3), (8, 0), (6, 1), (0, 2)], // Origin 5.
        [(0, 3), (5, 3), (8, 4), (7, 1), (11, 2)], // Origin 6.
        [(11, 3), (6, 3), (8, 3), (9, 4), (10, 3)], // Origin 7.
        [(5, 2), (3, 2), (9, 0), (7, 2), (6, 2)], // Origin 8.
        [(8, 2), (3, 1), (2, 0), (10, 4), (7, 3)], // Origin 9.
        [(2, 4), (1, 4), (11, 4), (7, 4), (9, 3)], // Origin 10.
        [(1, 3), (0, 4), (6, 4), (7, 0), (10, 2)], // Origin 11.
    ];
}
