using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>Decoded source-cell state used by <see cref="LatticeNeighbors"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(quintant {Quintant}, origin {Origin.Id}, res {Resolution})")]
internal readonly record struct LatticeSource(
    Origin Origin,
    int Segment,
    ulong S,
    int Resolution,
    int HilbertResolution,
    int Quintant,
    Orientation Orientation,
    Triple Triple,
    ulong MaxS,
    int MaxRow);

/// <summary>
/// Fast lattice-based neighbor finding: skips <see cref="Neighbors.IsNeighbor"/> validation for
/// within-quintant candidates (unlike <see cref="QuintantNeighbors"/>/<see cref="GlobalNeighbors"/>),
/// falling back to <see cref="GlobalNeighbors.GetGlobalCellNeighbors"/> below the first Hilbert
/// resolution.
/// </summary>
internal static class LatticeNeighbors
{
    /// <summary>All 26 non-zero ±1 moves in 3D — vertex- and edge-sharing within-quintant candidates.</summary>
    private static (int DeltaX, int DeltaY, int DeltaZ)[] SupersetDeltas { get; } = BuildSupersetDeltas();

    /// <summary>The 3 parity-valid single-axis moves matching the lattice flood-fill's edge connectivity, for even parity.</summary>
    private static (int DeltaX, int DeltaY, int DeltaZ)[] ParityEvenDeltas { get; } = [(1, 0, 0), (0, 1, 0), (0, 0, 1)];

    /// <summary>The 3 parity-valid single-axis moves matching the lattice flood-fill's edge connectivity, for odd parity.</summary>
    private static (int DeltaX, int DeltaY, int DeltaZ)[] ParityOddDeltas { get; } = [(-1, 0, 0), (0, -1, 0), (0, 0, -1)];

    /// <summary>
    /// Finds the lattice neighbors of <paramref name="cellId"/>.
    /// </summary>
    /// <param name="cellId">The cell to find neighbors of.</param>
    /// <param name="edgeOnly">
    /// If <see langword="true"/>, returns the 3 parity-valid moves matching the lattice flood-fill's
    /// exact edge connectivity — for shell-buffering the flood-fill firewall. If <see langword="false"/>,
    /// returns the 26-cube ±1 superset (may include vertex-only touchers), for BFS that re-validates
    /// candidates downstream (e.g. line tracing).
    /// </param>
    /// <remarks>
    /// The result is unsorted and undeduped by design, unlike <see cref="QuintantNeighbors.GetCellNeighbors"/>
    /// and <see cref="GlobalNeighbors.GetGlobalCellNeighbors"/> — preserved via an insertion-order list,
    /// never bare <see cref="HashSet{T}"/> enumeration.
    /// </remarks>
    public static ulong[] GetLatticeNeighbors(ulong cellId, bool edgeOnly)
    {
        LatticeSource? decoded = DecodeSource(cellId);
        if(decoded is null)
        {
            return GlobalNeighbors.GetGlobalCellNeighbors(cellId, edgeOnly);
        }

        LatticeSource source = decoded.Value;
        (int DeltaX, int DeltaY, int DeltaZ)[] deltas = edgeOnly
            ? (TripleCoordinates.TripleParity(source.Triple) == 0 ? ParityEvenDeltas : ParityOddDeltas)
            : SupersetDeltas;

        List<ulong> result = [];
        foreach((int deltaX, int deltaY, int deltaZ) in deltas)
        {
            Triple candidate = new(source.Triple.X + deltaX, source.Triple.Y + deltaY, source.Triple.Z + deltaZ);
            if(!TripleCoordinates.TripleInBounds(candidate, source.MaxRow))
            {
                continue;
            }

            ulong? candidateS = TripleCoordinates.TripleToS(candidate, source.HilbertResolution, source.Orientation);
            if(candidateS is not null && candidateS.Value < source.MaxS && candidateS.Value != source.S)
            {
                result.Add(Serialization.Serialize(new A5Cell(source.Origin, source.Segment, candidateS.Value, source.Resolution)));
            }
        }

        // Strict lattice connectivity (edgeOnly) doesn't traverse the [-maxRow, maxRow, 0] vertex
        // corner, so skipCorners is forwarded as edgeOnly itself — keeping the firewall topology tight.
        result.AddRange(LatticeBoundary.GetBoundaryNeighbors(BuildBoundaryContext(source), edgeOnly, edgeOnly));

        return [.. result];
    }

    /// <summary>Deserializes <paramref name="cellId"/> and unpacks it into a <see cref="LatticeSource"/>; <see langword="null"/> below the first Hilbert resolution.</summary>
    private static LatticeSource? DecodeSource(ulong cellId)
    {
        A5Cell cell = Serialization.Deserialize(cellId);
        if(cell.Resolution < Serialization.FirstHilbertResolution)
        {
            return null;
        }

        int hilbertResolution = cell.Resolution - Serialization.FirstHilbertResolution + 1;
        SegmentQuintant segmentQuintant = Origins.SegmentToQuintant(cell.Segment, cell.Origin);
        Anchor anchor = HilbertCurve.SToAnchor(cell.S, hilbertResolution, segmentQuintant.Orientation);
        Triple triple = TripleCoordinates.AnchorToTriple(anchor);

        return new LatticeSource(
            cell.Origin,
            cell.Segment,
            cell.S,
            cell.Resolution,
            hilbertResolution,
            segmentQuintant.Quintant,
            segmentQuintant.Orientation,
            triple,
            1UL << (2 * hilbertResolution),
            (1 << hilbertResolution) - 1);
    }

    /// <summary>Builds the <see cref="BoundaryContext"/> used by the <see cref="LatticeBoundary"/> helpers.</summary>
    private static BoundaryContext BuildBoundaryContext(LatticeSource source)
    {
        return new BoundaryContext(
            source.Triple,
            TripleCoordinates.TripleParity(source.Triple),
            source.Quintant,
            source.Origin,
            source.HilbertResolution,
            source.MaxS,
            source.MaxRow,
            source.Resolution);
    }

    /// <summary>Builds the 26 non-zero ±1 moves in 3D, in a fixed nested-loop order (x outer, y middle, z inner).</summary>
    private static (int DeltaX, int DeltaY, int DeltaZ)[] BuildSupersetDeltas()
    {
        List<(int DeltaX, int DeltaY, int DeltaZ)> deltas = [];
        for(int deltaX = -1; deltaX <= 1; deltaX++)
        {
            for(int deltaY = -1; deltaY <= 1; deltaY++)
            {
                for(int deltaZ = -1; deltaZ <= 1; deltaZ++)
                {
                    if(deltaX == 0 && deltaY == 0 && deltaZ == 0)
                    {
                        continue;
                    }

                    deltas.Add((deltaX, deltaY, deltaZ));
                }
            }
        }

        return [.. deltas];
    }
}
