using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Within-quintant neighbor finding via triple coordinates: generate ±1 candidate triples, validate them
/// with <see cref="Neighbors.IsNeighbor"/> in uv space, and convert the validated triples to s-values in
/// the requested orientation.
/// </summary>
internal static class QuintantNeighbors
{
    /// <summary>
    /// Finds a cell's within-quintant neighbors via triple-coordinate search: generates every candidate
    /// at Manhattan distance up to 3 in triple space, validates each with <see cref="Neighbors.IsNeighbor"/>
    /// in uv space (the space the neighbor pattern tables are defined in), and converts validated
    /// candidates to s-values in <paramref name="orientation"/>. This parameter is always a Hilbert
    /// resolution at every call site, so it is named <paramref name="hilbertResolution"/> here.
    /// The result is unsorted and returned in an explicit insertion-order-preserving list — it is
    /// consumed by BFS in <see cref="GlobalNeighbors"/>.
    /// </summary>
    /// <param name="sourceTriple">Triple coordinates of the source cell.</param>
    /// <param name="uvSourceAnchor">Source anchor in uv orientation, for <see cref="Neighbors.IsNeighbor"/> validation.</param>
    /// <param name="sourceS">Source s-value, excluded from the results.</param>
    /// <param name="hilbertResolution">Hilbert curve resolution level.</param>
    /// <param name="orientation">Hilbert curve orientation the result s-values are expressed in.</param>
    /// <param name="edgeOnly">If <see langword="true"/>, only edge-sharing neighbors (Manhattan distance ≤ 2).</param>
    public static List<ulong> FindQuintantNeighborS(
        Triple sourceTriple,
        Anchor? uvSourceAnchor,
        ulong sourceS,
        int hilbertResolution,
        Orientation orientation,
        bool edgeOnly)
    {
        ulong maxS = 1UL << (2 * hilbertResolution);
        int maxRow = (1 << hilbertResolution) - 1;
        List<ulong> neighbors = [];

        for(int dx = -1; dx <= 1; dx++)
        {
            for(int dy = -1; dy <= 1; dy++)
            {
                for(int dz = -1; dz <= 1; dz++)
                {
                    if(dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    // Provably always false given the loop domain (|dx|, |dy|, |dz| ≤ 1 sums to at most
                    // 3), but kept as an explicit guard rather than removed.
                    if(Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) > 3)
                    {
                        continue;
                    }

                    if(edgeOnly && Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) > 2)
                    {
                        continue;
                    }

                    Triple neighborTriple = new(sourceTriple.X + dx, sourceTriple.Y + dy, sourceTriple.Z + dz);
                    if(!TripleCoordinates.TripleInBounds(neighborTriple, maxRow))
                    {
                        continue;
                    }

                    // Validate in uv space, where the Neighbors pattern tables are defined.
                    Anchor? uvNeighborAnchor = TripleCoordinates.TripleToAnchor(neighborTriple, hilbertResolution, Orientation.UV);
                    if(uvNeighborAnchor is null || uvSourceAnchor is null)
                    {
                        continue;
                    }

                    if(!Neighbors.IsNeighbor(uvSourceAnchor.Value, uvNeighborAnchor.Value))
                    {
                        continue;
                    }

                    ulong? neighborS = TripleCoordinates.TripleToS(neighborTriple, hilbertResolution, orientation);
                    if(neighborS is not null && neighborS.Value < maxS && neighborS.Value != sourceS)
                    {
                        neighbors.Add(neighborS.Value);
                    }
                }
            }
        }

        return neighbors;
    }

    /// <summary>
    /// Fast within-quintant neighbor finding using triple coordinates: converts <paramref name="s"/> to
    /// triple coordinates (orientation-independent — the same geometric cell always has the same triple
    /// regardless of Hilbert curve orientation), generates neighbor candidates via
    /// <see cref="FindQuintantNeighborS"/>, and returns them sorted ascending.
    /// </summary>
    /// <param name="s">Cell s-value (Hilbert curve index).</param>
    /// <param name="hilbertResolution">Hilbert curve resolution level (see <see cref="FindQuintantNeighborS"/>).</param>
    /// <param name="orientation">Hilbert curve orientation.</param>
    /// <param name="edgeOnly">
    /// If <see langword="true"/>, return only edge-sharing neighbors (Manhattan distance ≤ 2); the
    /// default returns all neighbors including vertex-only ones (Manhattan distance 3).
    /// </param>
    public static ulong[] GetCellNeighbors(ulong s, int hilbertResolution, Orientation orientation = Orientation.UV, bool edgeOnly = false)
    {
        Anchor anchor = HilbertCurve.SToAnchor(s, hilbertResolution, orientation);
        Triple triple = TripleCoordinates.AnchorToTriple(anchor);
        Anchor? uvSourceAnchor = TripleCoordinates.TripleToAnchor(triple, hilbertResolution, Orientation.UV);

        List<ulong> neighbors = FindQuintantNeighborS(triple, uvSourceAnchor, s, hilbertResolution, orientation, edgeOnly);

        return [.. neighbors.OrderBy(value => value)];
    }
}
