using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Geo.Dggs.Traversal;

/// <summary>
/// Per-quintant context needed to convert a packed triple key back to a cell id: the face
/// <see cref="Origin"/>, the Hilbert-curve <see cref="Segment"/> on that face, and the traversal
/// <see cref="Orientation"/> the key's coordinates were computed in.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("(origin {Origin.Id}, segment {Segment})")]
internal readonly record struct QuintantContext(Origin Origin, int Segment, Orientation Orientation);

/// <summary>
/// Per-quintant packed breadth-first-search state: the packed triple keys already visited and the
/// current frontier, reusable across phases of the same resolution (see
/// <see cref="LatticeFloodFillState"/>). A reference type because <see cref="Frontier"/> is wholesale
/// replaced every layer, and the replacement must be visible to every holder of the containing
/// <see cref="LatticeFloodFillState"/>.
/// </summary>
internal sealed class QuintantFloodState
{
    /// <summary>Creates a fresh, empty state for the given quintant context.</summary>
    public QuintantFloodState(QuintantContext context)
    {
        Context = context;
    }

    /// <summary>The origin, segment and orientation this quintant's packed keys are computed in.</summary>
    public QuintantContext Context { get; }

    /// <summary>Packed triple keys already discovered for this quintant, across every call sharing this state.</summary>
    public HashSet<long> Visited { get; } = [];

    /// <summary>The current breadth-first-search frontier for this quintant, replaced wholesale every layer.</summary>
    public List<long> Frontier { get; set; } = [];
}

/// <summary>
/// Opaque, reusable per-quintant flood-fill state threaded from one <see cref="LatticeFloodFill.TripleSpaceFloodFill(HashSet{ulong}, ReadOnlySpan{ulong}, int, int?)"/>
/// call into a later one — the mechanism <see cref="Regions.PolygonToCells"/> uses to hand the fine-BFS
/// packed state from its phase-1 call into its phase-3 resumption without re-walking already-visited
/// quintants.
/// </summary>
internal sealed class LatticeFloodFillState
{
    /// <summary>The per-quintant states, indexed by <c>origin.Id * 60 + segment</c>.</summary>
    internal Dictionary<int, QuintantFloodState> Quintants { get; } = [];
}

/// <summary>
/// The outcome of one <see cref="LatticeFloodFill.TripleSpaceFloodFill(HashSet{ulong}, ReadOnlySpan{ulong}, int, int?)"/>
/// call: the cells newly discovered as interior to the firewall, the cells left on the frontier when
/// the search stopped (either because it converged or because <c>maxLayers</c> was reached), and the
/// packed state to pass into a later resuming call.
/// </summary>
[DebuggerDisplay("(interior {InteriorCells.Length}, frontier {FrontierCellIds.Length})")]
internal readonly record struct LatticeFloodFillResult(ulong[] InteriorCells, ulong[] FrontierCellIds, LatticeFloodFillState State);

/// <summary>
/// Triple-space flood fill in packed integer coordinates — no per-step Hilbert-curve conversion. Uses
/// the 3 parity-valid ±1 moves; since those never cross quintant boundaries, each quintant is flooded
/// independently. The packed keys are carried as <see cref="long"/> rather than plain double-precision
/// number arithmetic, which can exceed <c>Number.MAX_SAFE_INTEGER</c> at extreme resolutions — a latent
/// precision hazard this avoids.
/// </summary>
internal static class LatticeFloodFill
{
    /// <summary>
    /// Runs a fresh flood fill, seeding visited state from every cell already in
    /// <paramref name="firewall"/>. On return, every cell found to be interior to the firewall during
    /// this call has been added to <paramref name="firewall"/> — mutation is part of the contract, not
    /// a side effect.
    /// </summary>
    /// <param name="firewall">
    /// The set of cells that block the flood; extended in place with every cell discovered as interior
    /// during this call.
    /// </param>
    /// <param name="seedCellIds">
    /// The breadth-first-search seeds. Always added to the frontier even if already visited —
    /// restarting the search from the same seeds against a previously-converged firewall is a no-op,
    /// by design.
    /// </param>
    /// <param name="resolution">The resolution the flood fill and its output cells are at.</param>
    /// <param name="maxLayers">The maximum number of breadth-first-search layers to run; <see langword="null"/> runs to convergence.</param>
    public static LatticeFloodFillResult TripleSpaceFloodFill(HashSet<ulong> firewall, ReadOnlySpan<ulong> seedCellIds, int resolution, int? maxLayers = null)
    {
        ArgumentNullException.ThrowIfNull(firewall);

        (int hilbertResolution, int maxRow, int yStride, ulong maxS) = ComputeHilbertParameters(resolution);

        LatticeFloodFillState state = new();
        foreach(ulong cellId in firewall)
        {
            (int quintantIndex, long key, QuintantContext context) = CellToQuintantKey(cellId, hilbertResolution, maxRow, yStride);
            GetOrCreateQuintant(state.Quintants, quintantIndex, context).Visited.Add(key);
        }

        return RunBreadthFirstSearch(state, firewall, seedCellIds, hilbertResolution, maxRow, yStride, maxS, resolution, maxLayers);
    }

    /// <summary>
    /// Resumes a flood fill from a <paramref name="previousState"/> returned by an earlier call,
    /// additionally marking <paramref name="delta"/> as visited before seeding the frontier. Does not
    /// mutate any external firewall set — only the fresh-firewall overload does that.
    /// </summary>
    /// <param name="previousState">The packed state returned by the call being resumed.</param>
    /// <param name="delta">Cells discovered since <paramref name="previousState"/> was captured, to mark visited before this call's search runs.</param>
    /// <param name="seedCellIds">The breadth-first-search seeds for this call.</param>
    /// <param name="resolution">The resolution the flood fill and its output cells are at.</param>
    /// <param name="maxLayers">The maximum number of breadth-first-search layers to run; <see langword="null"/> runs to convergence.</param>
    public static LatticeFloodFillResult TripleSpaceFloodFill(LatticeFloodFillState previousState, ReadOnlySpan<ulong> delta, ReadOnlySpan<ulong> seedCellIds, int resolution, int? maxLayers = null)
    {
        ArgumentNullException.ThrowIfNull(previousState);

        (int hilbertResolution, int maxRow, int yStride, ulong maxS) = ComputeHilbertParameters(resolution);

        // Stale frontier from the prior call — clear it so the seeds below drive this search.
        foreach(QuintantFloodState quintantState in previousState.Quintants.Values)
        {
            quintantState.Frontier = [];
        }

        foreach(ulong cellId in delta)
        {
            (int quintantIndex, long key, QuintantContext context) = CellToQuintantKey(cellId, hilbertResolution, maxRow, yStride);
            GetOrCreateQuintant(previousState.Quintants, quintantIndex, context).Visited.Add(key);
        }

        return RunBreadthFirstSearch(previousState, null, seedCellIds, hilbertResolution, maxRow, yStride, maxS, resolution, maxLayers);
    }

    /// <summary>Resolution-derived quantities shared by every call: Hilbert resolution, row/stride bounds and the Hilbert-position ceiling.</summary>
    private static (int HilbertResolution, int MaxRow, int YStride, ulong MaxS) ComputeHilbertParameters(int resolution)
    {
        int hilbertResolution = resolution - Serialization.FirstHilbertResolution + 1;
        int maxRow = (1 << hilbertResolution) - 1;
        int yStride = (maxRow + 1) * 2;
        ulong maxS = 1UL << (2 * hilbertResolution);

        return (hilbertResolution, maxRow, yStride, maxS);
    }

    /// <summary>
    /// Seeds the frontier from <paramref name="seedCellIds"/>, runs the packed-key breadth-first search
    /// to convergence or <paramref name="maxLayers"/>, then converts the discoveries and remaining
    /// frontier back to cell ids.
    /// </summary>
    private static LatticeFloodFillResult RunBreadthFirstSearch(
        LatticeFloodFillState state,
        HashSet<ulong>? firewallToMutate,
        ReadOnlySpan<ulong> seedCellIds,
        int hilbertResolution,
        int maxRow,
        int yStride,
        ulong maxS,
        int resolution,
        int? maxLayers)
    {
        Dictionary<int, QuintantFloodState> quintants = state.Quintants;

        foreach(ulong cellId in seedCellIds)
        {
            (int quintantIndex, long key, QuintantContext context) = CellToQuintantKey(cellId, hilbertResolution, maxRow, yStride);
            QuintantFloodState quintantState = GetOrCreateQuintant(quintants, quintantIndex, context);
            quintantState.Visited.Add(key);
            quintantState.Frontier.Add(key);
        }

        // Discovered keys per quintant for THIS call only (excludes discoveries from any prior call).
        Dictionary<int, List<long>> discoveredPerQuintant = [];

        int layers = 0;
        bool hasWork = true;
        while(hasWork && (maxLayers is null || layers < maxLayers.Value))
        {
            hasWork = false;
            foreach((int quintantIndex, QuintantFloodState quintantState) in quintants)
            {
                if(quintantState.Frontier.Count == 0)
                {
                    continue;
                }

                if(!discoveredPerQuintant.TryGetValue(quintantIndex, out List<long>? discovered))
                {
                    discovered = [];
                    discoveredPerQuintant[quintantIndex] = discovered;
                }

                List<long> nextFrontier = [];
                foreach(long key in quintantState.Frontier)
                {
                    long parity = key % 2;
                    long yPart = (key - parity) % yStride;
                    long y = yPart / 2;
                    long x = ((key - yPart - parity) / yStride) - maxRow;
                    long step = parity == 0 ? 1 : -1;
                    long newParity = 1 - parity;
                    long yLimit = y - newParity;

                    // Move in x: triple becomes (x+step, y, z); z = parity - x - y is unchanged.
                    long nx = x + step;
                    long nzX = parity - x - y;
                    if(nx <= 0 && nzX <= 0 && nx >= -yLimit && nzX >= -yLimit)
                    {
                        long nk = ((nx + maxRow) * yStride) + (y * 2) + newParity;
                        if(quintantState.Visited.Add(nk))
                        {
                            discovered.Add(nk);
                            nextFrontier.Add(nk);
                        }
                    }

                    // Move in y: triple becomes (x, y+step, z); z is unchanged.
                    long ny = y + step;
                    long nzY = parity - x - y;
                    long nyLimit = ny - newParity;
                    if(ny >= 0 && ny <= maxRow && nzY <= 0 && x >= -nyLimit && nzY >= -nyLimit)
                    {
                        long nk = ((x + maxRow) * yStride) + (ny * 2) + newParity;
                        if(quintantState.Visited.Add(nk))
                        {
                            discovered.Add(nk);
                            nextFrontier.Add(nk);
                        }
                    }

                    // Move in z: triple becomes (x, y, z+step); the packed key shape (x, y, parity) is
                    // identical to the x and y moves' starting point apart from the parity flip.
                    long z = parity - x - y;
                    long nz = z + step;
                    if(nz <= 0 && x >= -yLimit && nz >= -yLimit)
                    {
                        long nk = ((x + maxRow) * yStride) + (y * 2) + newParity;
                        if(quintantState.Visited.Add(nk))
                        {
                            discovered.Add(nk);
                            nextFrontier.Add(nk);
                        }
                    }
                }

                quintantState.Frontier = nextFrontier;
                if(nextFrontier.Count > 0)
                {
                    hasWork = true;
                }
            }

            layers++;
        }

        List<ulong> interiorCells = [];
        List<ulong> frontierCellIds = [];

        foreach((int quintantIndex, QuintantFloodState quintantState) in quintants)
        {
            if(discoveredPerQuintant.TryGetValue(quintantIndex, out List<long>? discovered))
            {
                foreach(long key in discovered)
                {
                    ulong? cellId = PackedKeyToCellId(key, quintantState.Context, hilbertResolution, maxRow, yStride, maxS, resolution);
                    if(cellId is not null)
                    {
                        interiorCells.Add(cellId.Value);
                        firewallToMutate?.Add(cellId.Value);
                    }
                }
            }

            foreach(long key in quintantState.Frontier)
            {
                ulong? cellId = PackedKeyToCellId(key, quintantState.Context, hilbertResolution, maxRow, yStride, maxS, resolution);
                if(cellId is not null)
                {
                    frontierCellIds.Add(cellId.Value);
                }
            }
        }

        return new LatticeFloodFillResult([.. interiorCells], [.. frontierCellIds], state);
    }

    /// <summary>Converts a cell id into its quintant index and packed triple key.</summary>
    private static (int QuintantIndex, long Key, QuintantContext Context) CellToQuintantKey(ulong cellId, int hilbertResolution, int maxRow, int yStride)
    {
        A5Cell cell = Serialization.Deserialize(cellId);
        SegmentQuintant segmentQuintant = Origins.SegmentToQuintant(cell.Segment, cell.Origin);
        Anchor anchor = HilbertCurve.SToAnchor(cell.S, hilbertResolution, segmentQuintant.Orientation);
        Triple triple = TripleCoordinates.AnchorToTriple(anchor);
        int parity = triple.X + triple.Y + triple.Z;
        int quintantIndex = (cell.Origin.Id * 60) + cell.Segment;
        long key = PackTripleKey(triple.X, triple.Y, parity, maxRow, yStride);

        return (quintantIndex, key, new QuintantContext(cell.Origin, cell.Segment, segmentQuintant.Orientation));
    }

    /// <summary>
    /// Converts a packed triple key back to a cell id, or <see langword="null"/> if it doesn't map to a
    /// valid cell within <paramref name="maxS"/>.
    /// </summary>
    private static ulong? PackedKeyToCellId(long key, QuintantContext context, int hilbertResolution, int maxRow, int yStride, ulong maxS, int resolution)
    {
        Triple triple = UnpackTripleKey(key, maxRow, yStride);
        ulong? s = TripleCoordinates.TripleToS(triple, hilbertResolution, context.Orientation);
        if(s is null || s.Value >= maxS)
        {
            return null;
        }

        return Serialization.Serialize(new A5Cell(context.Origin, context.Segment, s.Value, resolution));
    }

    /// <summary>
    /// Packs a triple as a single key for fast visited-set lookup:
    /// <c>(x + maxRow) * yStride + y * 2 + parity</c>, where <c>parity = (x + y + z) ∈ {0, 1}</c>.
    /// </summary>
    private static long PackTripleKey(int x, int y, int parity, int maxRow, int yStride)
    {
        return ((long)(x + maxRow) * yStride) + ((long)y * 2) + parity;
    }

    /// <summary>Inverse of <see cref="PackTripleKey"/> — recovers a <see cref="Triple"/> from a packed key.</summary>
    private static Triple UnpackTripleKey(long key, int maxRow, int yStride)
    {
        long parity = key % 2;
        long yPart = (key - parity) % yStride;
        long y = yPart / 2;
        long x = ((key - yPart - parity) / yStride) - maxRow;
        long z = parity - x - y;

        return new Triple((int)x, (int)y, (int)z);
    }

    /// <summary>Returns the existing <see cref="QuintantFloodState"/> for a quintant index, creating an empty one if absent.</summary>
    private static QuintantFloodState GetOrCreateQuintant(Dictionary<int, QuintantFloodState> quintants, int quintantIndex, QuintantContext context)
    {
        if(!quintants.TryGetValue(quintantIndex, out QuintantFloodState? quintantState))
        {
            quintantState = new QuintantFloodState(context);
            quintants[quintantIndex] = quintantState;
        }

        return quintantState;
    }
}
