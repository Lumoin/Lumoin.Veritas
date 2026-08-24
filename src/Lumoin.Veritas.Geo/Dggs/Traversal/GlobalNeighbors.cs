using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Finds all neighbors of a cell across quintant and face boundaries. Within-quintant candidates are
/// validated with <see cref="Neighbors.IsNeighbor"/> in uv space (via <see cref="QuintantNeighbors.FindQuintantNeighborS"/>);
/// cross-quintant, cross-face, apex, and corner neighbors come from <see cref="LatticeBoundary.GetBoundaryNeighbors"/>.
/// </summary>
internal static class GlobalNeighbors
{
    /// <summary>
    /// Finds the neighbors of a cell.
    /// </summary>
    /// <param name="cellId">The cell to find neighbors of.</param>
    /// <param name="edgeOnly">
    /// If <see langword="true"/>, returns only edge-sharing neighbors (5 per cell, except resolution 1 —
    /// see <see cref="GetRes1Neighbors"/>). The default <see langword="false"/> returns all neighbors
    /// including vertex-only ones (6-8 per cell).
    /// </param>
    /// <remarks>
    /// The returned order is always ascending by cell id — every code path below funnels through a final
    /// sort. That order is semantically load-bearing downstream (a
    /// point-location fallback iterates the result first-match-wins), so it is preserved end to end
    /// rather than treated as an incidental consequence of set deduplication.
    /// </remarks>
    public static ulong[] GetGlobalCellNeighbors(ulong cellId, bool edgeOnly = false)
    {
        A5Cell cell = Serialization.Deserialize(cellId);

        if(cell.Resolution == 0)
        {
            return GetRes0Neighbors(cell.Origin);
        }

        if(cell.Resolution == 1)
        {
            return GetRes1Neighbors(cell.Origin, cell.Segment, edgeOnly);
        }

        int hilbertResolution = cell.Resolution - Serialization.FirstHilbertResolution + 1;
        SegmentQuintant sourceSegmentQuintant = Origins.SegmentToQuintant(cell.Segment, cell.Origin);
        Anchor anchor = HilbertCurve.SToAnchor(cell.S, hilbertResolution, sourceSegmentQuintant.Orientation);

        // Triple coordinates are orientation-independent.
        Triple triple = TripleCoordinates.AnchorToTriple(anchor);

        // uv anchor, for within-quintant isNeighbor validation.
        Anchor? uvSourceAnchor = TripleCoordinates.TripleToAnchor(triple, hilbertResolution, Orientation.UV);

        List<ulong> neighbors = [];
        HashSet<ulong> seen = [];

        // Within-quintant: validated by IsNeighbor in uv space.
        foreach(ulong neighborS in QuintantNeighbors.FindQuintantNeighborS(triple, uvSourceAnchor, cell.S, hilbertResolution, sourceSegmentQuintant.Orientation, edgeOnly))
        {
            AddUnique(neighbors, seen, Serialization.Serialize(new A5Cell(cell.Origin, cell.Segment, neighborS, cell.Resolution)));
        }

        // Cross-quintant / cross-face / apex / corner: the shared lattice-boundary helper.
        BoundaryContext context = new(
            triple,
            TripleCoordinates.TripleParity(triple),
            sourceSegmentQuintant.Quintant,
            cell.Origin,
            hilbertResolution,
            1UL << (2 * hilbertResolution),
            (1 << hilbertResolution) - 1,
            cell.Resolution);

        foreach(ulong boundaryNeighbor in LatticeBoundary.GetBoundaryNeighbors(context, edgeOnly))
        {
            AddUnique(neighbors, seen, boundaryNeighbor);
        }

        return [.. neighbors.OrderBy(value => value)];
    }

    /// <summary>Serializes a resolution-1 cell from an origin and quintant.</summary>
    private static ulong SerializeRes1(Origin origin, int quintant)
    {
        QuintantSegment target = Origins.QuintantToSegment(quintant, origin);

        return Serialization.Serialize(new A5Cell(origin, target.Segment, 0UL, 1));
    }

    /// <summary>Gets the neighbors of a resolution-0 cell (a dodecahedron face).</summary>
    private static ulong[] GetRes0Neighbors(Origin origin)
    {
        List<ulong> neighbors = [];
        HashSet<ulong> seen = [];

        for(int quintant = 0; quintant < 5; quintant++)
        {
            int adjacentFaceId = FaceAdjacency.Table[origin.Id][quintant].AdjacentOriginId;
            AddUnique(neighbors, seen, Serialization.Serialize(new A5Cell(Origins.All[adjacentFaceId], 0, 0UL, 0)));
        }

        return [.. neighbors.OrderBy(value => value)];
    }

    /// <summary>
    /// Gets the neighbors of a resolution-1 cell (a quintant). The <paramref name="edgeOnly"/> early
    /// return yields only 3 members, contradicting the general "5 per cell" edge-neighbor claim — that
    /// generalization does not hold at resolution 1 specifically.
    /// </summary>
    private static ulong[] GetRes1Neighbors(Origin origin, int segment, bool edgeOnly)
    {
        SegmentQuintant sourceSegmentQuintant = Origins.SegmentToQuintant(segment, origin);
        int quintant = sourceSegmentQuintant.Quintant;
        List<ulong> neighbors = [];
        HashSet<ulong> seen = [];

        // Left and right quintant on the same face (A, B).
        int leftQuintant = (quintant - 1 + 5) % 5;
        int rightQuintant = (quintant + 1) % 5;
        AddUnique(neighbors, seen, SerializeRes1(origin, leftQuintant));
        AddUnique(neighbors, seen, SerializeRes1(origin, rightQuintant));

        // Adjacent quintant on adjacent face (C).
        (int adjacentFaceId, int adjacentQuintant) = FaceAdjacency.Table[origin.Id][quintant];
        Origin adjacentOrigin = Origins.All[adjacentFaceId];
        AddUnique(neighbors, seen, SerializeRes1(adjacentOrigin, adjacentQuintant));

        if(edgeOnly)
        {
            return [.. neighbors.OrderBy(value => value)];
        }

        // Remaining neighbors on face.
        AddUnique(neighbors, seen, SerializeRes1(origin, (quintant - 2 + 5) % 5));
        AddUnique(neighbors, seen, SerializeRes1(origin, (quintant + 2) % 5));

        // Left & right quintant neighbors of C.
        AddUnique(neighbors, seen, SerializeRes1(adjacentOrigin, (adjacentQuintant - 1 + 5) % 5));
        AddUnique(neighbors, seen, SerializeRes1(adjacentOrigin, (adjacentQuintant + 1) % 5));

        // Two neighbors each from adjacent faces of A & B.
        (int leftAdjacentFaceId, int leftAdjacentQuintant) = FaceAdjacency.Table[origin.Id][leftQuintant];
        Origin leftAdjacentOrigin = Origins.All[leftAdjacentFaceId];
        AddUnique(neighbors, seen, SerializeRes1(leftAdjacentOrigin, leftAdjacentQuintant));
        AddUnique(neighbors, seen, SerializeRes1(leftAdjacentOrigin, (leftAdjacentQuintant - 1 + 5) % 5));

        (int rightAdjacentFaceId, int rightAdjacentQuintant) = FaceAdjacency.Table[origin.Id][rightQuintant];
        Origin rightAdjacentOrigin = Origins.All[rightAdjacentFaceId];
        AddUnique(neighbors, seen, SerializeRes1(rightAdjacentOrigin, rightAdjacentQuintant));
        AddUnique(neighbors, seen, SerializeRes1(rightAdjacentOrigin, (rightAdjacentQuintant + 1) % 5));

        return [.. neighbors.OrderBy(value => value)];
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
