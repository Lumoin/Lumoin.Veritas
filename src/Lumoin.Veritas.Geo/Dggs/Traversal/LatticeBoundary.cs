using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// One relative triple-coordinate offset applied when crossing a quintant/face boundary, tagged with
/// whether it shares an edge (vs. only a vertex) with the source cell.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({DeltaX}, {DeltaY}, {DeltaZ}, edge {IsEdgeSharing})")]
internal readonly record struct NeighborDelta(int DeltaX, int DeltaY, int DeltaZ, bool IsEdgeSharing);

/// <summary>Source-cell context shared by every boundary-neighbor case in <see cref="LatticeBoundary"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(quintant {SourceQuintant}, origin {Origin.Id}, res {Resolution})")]
internal readonly record struct BoundaryContext(
    Triple Triple,
    int Parity,
    int SourceQuintant,
    Origin Origin,
    int HilbertResolution,
    ulong MaxS,
    int MaxRow,
    int Resolution);

/// <summary>
/// Finds neighbors that lie outside a source cell's quintant: cross-quintant lateral edges, the
/// cross-face base edge, the apex (face center), and the base-left corner vertex. Within-quintant ±1
/// candidates are NOT covered here — callers (<see cref="QuintantNeighbors"/>,
/// <see cref="GlobalNeighbors"/>, <see cref="LatticeNeighbors"/>) generate those directly.
/// </summary>
internal static class LatticeBoundary
{
    /// <summary>
    /// Cross-quintant left-edge deltas (source z = 0), indexed by <c>parity · 2 + (yOdd ? 1 : 0)</c>.
    /// Applied to the swapped base triple <c>[0, y, x]</c> in the previous quintant.
    /// </summary>
    private static NeighborDelta[][] LeftEdgeDeltas { get; } =
    [
        [new NeighborDelta(0, 0, 0, true), new NeighborDelta(0, 0, 1, false)], // parity=0, yEven.
        [new NeighborDelta(0, 0, 0, true), new NeighborDelta(0, 1, 0, true), new NeighborDelta(0, -1, 1, false), new NeighborDelta(0, 1, -1, false)], // parity=0, yOdd.
        [], // parity=1, yEven.
        [new NeighborDelta(0, -1, 0, true), new NeighborDelta(0, 0, -1, false)], // parity=1, yOdd.
    ];

    /// <summary>
    /// Cross-quintant right-edge deltas (source x = 0), indexed by <c>parity · 2 + (yOdd ? 1 : 0)</c>.
    /// Applied to the swapped base triple <c>[z, y, 0]</c> in the next quintant.
    /// </summary>
    private static NeighborDelta[][] RightEdgeDeltas { get; } =
    [
        [new NeighborDelta(0, 0, 0, true), new NeighborDelta(0, 1, 0, true), new NeighborDelta(-1, 1, 0, false), new NeighborDelta(1, -1, 0, false)], // parity=0, yEven.
        [new NeighborDelta(0, 0, 0, true), new NeighborDelta(1, 0, 0, false)], // parity=0, yOdd.
        [new NeighborDelta(0, -1, 0, true), new NeighborDelta(-1, 0, 0, false)], // parity=1, yEven.
        [], // parity=1, yOdd.
    ];

    /// <summary>
    /// Cross-face base-edge deltas (source y = maxRow), indexed by parity. Applied to the mirrored
    /// position <c>[z, maxRow, x]</c> on the adjacent face.
    /// </summary>
    private static NeighborDelta[][] CrossFaceDeltas { get; } =
    [
        [new NeighborDelta(0, 0, 0, true), new NeighborDelta(1, 0, 0, true), new NeighborDelta(1, 0, -1, false)], // parity=0.
        [new NeighborDelta(0, 0, -1, true), new NeighborDelta(0, 0, 0, false)], // parity=1.
    ];

    /// <summary>
    /// Returns every neighbor that lies outside the source cell's quintant. The result may contain
    /// duplicates and its order is not stable — callers deduplicate (via a set) or accept duplicates if
    /// their downstream pipeline tolerates them.
    /// </summary>
    /// <param name="context">Source-cell context.</param>
    /// <param name="edgeOnly">Drop the apex's non-adjacent quintants and other vertex-only neighbors.</param>
    /// <param name="skipCorners">
    /// Drop the <c>[-maxRow, maxRow, 0]</c> corner — used when the caller's connectivity (e.g. lattice
    /// ±1 moves) doesn't traverse that vertex.
    /// </param>
    public static List<ulong> GetBoundaryNeighbors(BoundaryContext context, bool edgeOnly, bool skipCorners = false)
    {
        List<ulong> output = [];
        Triple triple = context.Triple;
        int parity = context.Parity;
        int sourceQuintant = context.SourceQuintant;
        Origin origin = context.Origin;
        int maxRow = context.MaxRow;
        bool yOdd = triple.Y % 2 != 0;
        int deltaIndex = (parity * 2) + (yOdd ? 1 : 0);

        // Left edge (z=0): neighbor in previous quintant at swapped [0, y, x].
        if(triple.Z == 0)
        {
            int targetQuintant = (sourceQuintant - 1 + 5) % 5;
            QuintantSegment target = Origins.QuintantToSegment(targetQuintant, origin);
            PushDeltas(output, new Triple(0, triple.Y, triple.X), LeftEdgeDeltas[deltaIndex], edgeOnly, target.Orientation, origin, target.Segment, context);
        }

        // Right edge (x=0): neighbor in next quintant at swapped [z, y, 0].
        if(triple.X == 0)
        {
            int targetQuintant = (sourceQuintant + 1) % 5;
            QuintantSegment target = Origins.QuintantToSegment(targetQuintant, origin);
            PushDeltas(output, new Triple(triple.Z, triple.Y, 0), RightEdgeDeltas[deltaIndex], edgeOnly, target.Orientation, origin, target.Segment, context);
        }

        // Base edge (y=maxRow): neighbor on adjacent face at mirrored [z, maxRow, x].
        if(triple.Y == maxRow)
        {
            (int adjacentFaceId, int adjacentQuintant) = FaceAdjacency.Table[origin.Id][sourceQuintant];
            Origin adjacentOrigin = Origins.All[adjacentFaceId];
            QuintantSegment target = Origins.QuintantToSegment(adjacentQuintant, adjacentOrigin);
            PushDeltas(output, new Triple(triple.Z, maxRow, triple.X), CrossFaceDeltas[parity], edgeOnly, target.Orientation, adjacentOrigin, target.Segment, context);
        }

        // Apex [0,0,0]: cells from all 5 quintants meet at the face center.
        if(triple.X == 0 && triple.Y == 0 && triple.Z == 0)
        {
            for(int quintant = 0; quintant < 5; quintant++)
            {
                if(quintant == sourceQuintant)
                {
                    continue;
                }

                int distance = Math.Min((quintant - sourceQuintant + 5) % 5, (sourceQuintant - quintant + 5) % 5);
                if(edgeOnly && distance != 1)
                {
                    continue;
                }

                QuintantSegment target = Origins.QuintantToSegment(quintant, origin);
                PushTriple(output, triple, target.Orientation, origin, target.Segment, context);
            }
        }

        // Base-left corner [-maxRow, maxRow, 0]: 3 dodecahedron faces meet at this vertex. The
        // symmetric base-right corner is implicitly covered: its cross-quintant and cross-face paths
        // land on the [-maxRow, maxRow, 0] cell of neighboring quintants. This case fires its two
        // pushes UNCONDITIONALLY, ignoring edgeOnly, unlike the four cases above; there is no matching
        // base-right-corner case — both asymmetries are intentional.
        if(!skipCorners && triple.X == -maxRow && triple.Y == maxRow && triple.Z == 0)
        {
            // Vertex neighbor 1: across the previous quintant's base edge.
            int previousQuintant = (sourceQuintant - 1 + 5) % 5;
            (int previousAdjacentFaceId, int previousAdjacentQuintant) = FaceAdjacency.Table[origin.Id][previousQuintant];
            Origin previousAdjacentOrigin = Origins.All[previousAdjacentFaceId];
            QuintantSegment previousTarget = Origins.QuintantToSegment(previousAdjacentQuintant, previousAdjacentOrigin);
            PushTriple(output, triple, previousTarget.Orientation, previousAdjacentOrigin, previousTarget.Segment, context);

            // Vertex neighbor 2: adjacent quintant on the primary cross-face.
            (int crossFaceId, int crossQuintant) = FaceAdjacency.Table[origin.Id][sourceQuintant];
            Origin crossOrigin = Origins.All[crossFaceId];
            int nextCrossQuintant = (crossQuintant + 1) % 5;
            QuintantSegment crossTarget = Origins.QuintantToSegment(nextCrossQuintant, crossOrigin);
            PushTriple(output, triple, crossTarget.Orientation, crossOrigin, crossTarget.Segment, context);
        }

        return output;
    }

    /// <summary>If <paramref name="triple"/> maps to a valid cell, appends its cell id to <paramref name="output"/>.</summary>
    private static void PushTriple(List<ulong> output, Triple triple, Orientation orientation, Origin origin, int segment, BoundaryContext context)
    {
        if(!TripleCoordinates.TripleInBounds(triple, context.MaxRow))
        {
            return;
        }

        ulong? s = TripleCoordinates.TripleToS(triple, context.HilbertResolution, orientation);
        if(s is null || s.Value >= context.MaxS)
        {
            return;
        }

        output.Add(Serialization.Serialize(new A5Cell(origin, segment, s.Value, context.Resolution)));
    }

    /// <summary>Applies a delta table to a base triple, appending each valid cell to <paramref name="output"/>.</summary>
    private static void PushDeltas(
        List<ulong> output,
        Triple baseTriple,
        NeighborDelta[] deltas,
        bool edgeOnly,
        Orientation orientation,
        Origin origin,
        int segment,
        BoundaryContext context)
    {
        foreach(NeighborDelta delta in deltas)
        {
            if(edgeOnly && !delta.IsEdgeSharing)
            {
                continue;
            }

            Triple candidate = new(baseTriple.X + delta.DeltaX, baseTriple.Y + delta.DeltaY, baseTriple.Z + delta.DeltaZ);
            PushTriple(output, candidate, orientation, origin, segment, context);
        }
    }
}
