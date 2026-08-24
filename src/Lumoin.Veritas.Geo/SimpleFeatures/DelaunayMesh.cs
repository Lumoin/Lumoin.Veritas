using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The per-call cavity Bowyer–Watson Delaunay triangulation the concave hull
/// erodes: flat parallel triangle storage — edge
/// <c>e</c> opposite local vertex <c>e</c>, endpoints <c>(e+1)%3</c> and
/// <c>(e+2)%3</c> directed counter-clockwise — over the canonical
/// deduplicated, lexicographically sorted candidate list, with the exterior
/// closed by ghost triangles around one virtual infinite vertex, never a
/// finite super-structure. Insertion is in sorted order: every new point is
/// lexicographically greater than every mesh vertex, so it lies strictly
/// outside the current hull, its cavity always contains a ghost, and the
/// exact O(1) seed applies — at least one of the two ghosts created by the
/// previous insertion (or the bootstrap) gates strictly outward. The cavity
/// is strict: a real triangle joins on <see cref="ExactInCircle.Sign"/>
/// &gt; 0 only, a ghost joins on a strictly positive orientation gate, and
/// joining on zero is not permissible — the strict gate is what forbids
/// zero-area fan triangles. Every real triangle's vertex triple is a strict
/// counter-clockwise turn. A ghost stores its hull edge reversed relative to
/// the counter-clockwise boundary cycle, sentinel last, so its edge opposite
/// the sentinel faces the interior and its two sentinel edges chain the
/// ring. Built, consumed, and discarded inside one operation call; plain
/// heap state, never pooled, single-owner like the scratch carrier it holds.
/// </summary>
internal sealed class DelaunayMesh
{
    /// <summary>The virtual infinite vertex a ghost triangle's third slot carries.</summary>
    internal const int GhostVertex = -1;

    /// <summary>The transient neighbor sentinel a slot carries while its fan is being wired.</summary>
    private const int Unlinked = -2;

    /// <summary>Binds the candidate list and the exact tail's carrier; <see cref="Create"/> bootstraps the mesh.</summary>
    /// <param name="candidates">The canonical sorted candidate list.</param>
    /// <param name="scratch">The exact tail's buffer carrier.</param>
    private DelaunayMesh(List<Point2d> candidates, InCircleScratch scratch)
    {
        Candidates = candidates;
        Scratch = scratch;
    }

    /// <summary>The canonical sorted candidate list; triangle vertices index into it.</summary>
    internal List<Point2d> Candidates { get; }

    /// <summary>The exact tail's buffer carrier, one per mesh, single-owner.</summary>
    private InCircleScratch Scratch { get; }

    /// <summary>Three vertex indices per triangle slot; ghosts carry <see cref="GhostVertex"/> last.</summary>
    internal List<int> TriangleVertices { get; } = [];

    /// <summary>Three neighbor slots per triangle, edge <c>e</c> opposite local vertex <c>e</c>.</summary>
    internal List<int> TriangleNeighbors { get; } = [];

    /// <summary>Vacated slots, pushed in ascending slot index per round, consumed last-in-first-out.</summary>
    private List<int> FreeSlots { get; } = [];

    /// <summary>Per-slot liveness, maintained by allocation and release.</summary>
    private List<bool> SlotLive { get; } = [];

    /// <summary>The breadth-first worklist of the current insertion's cavity walk.</summary>
    private List<int> CavityStack { get; } = [];

    /// <summary>The slots that joined the current cavity, in discovery order.</summary>
    private List<int> CavityMembers { get; } = [];

    /// <summary>Every slot the current walk marked, joined or rejected, for the cheap reset.</summary>
    private List<int> TouchedSlots { get; } = [];

    /// <summary>The cavity's boundary edges as (start, end, across-neighbor) in vertex/slot terms.</summary>
    private List<(int Start, int End, int Across)> CavityBoundary { get; } = [];

    /// <summary>Per-slot walk state: 0 untested, 1 joined, 2 rejected.</summary>
    private List<byte> CavityState { get; } = [];

    /// <summary>The shared directed-edge map every wiring pass twins through.</summary>
    private Dictionary<(int Start, int End), (int Triangle, int Local)> DirectedEdges { get; } = [];

    /// <summary>One of the two ghosts the latest fan created — the exact seed pair.</summary>
    private int SeedGhostFirst { get; set; }

    /// <summary>The other ghost of the seed pair.</summary>
    private int SeedGhostSecond { get; set; }

    /// <summary>The number of live real triangles.</summary>
    internal int RealTriangleCount { get; private set; }

    /// <summary>The number of live ghosts — identically the boundary-cycle point count.</summary>
    internal int GhostCount { get; private set; }

    /// <summary>The number of triangle slots ever allocated, live or vacated.</summary>
    internal int SlotCount => TriangleVertices.Count / 3;

    /// <summary>
    /// Builds the triangulation of the canonical candidate list, which must
    /// hold at least three positions not all collinear — the degenerate
    /// ladder never reaches the mesh.
    /// </summary>
    /// <param name="candidates">The canonical sorted candidate list.</param>
    /// <param name="scratch">The exact tail's buffer carrier.</param>
    /// <returns>The built mesh.</returns>
    public static DelaunayMesh Create(List<Point2d> candidates, InCircleScratch scratch)
    {
        var mesh = new DelaunayMesh(candidates, scratch);
        mesh.Bootstrap();

        return mesh;
    }

    /// <summary>Whether the slot currently holds a ghost triangle.</summary>
    /// <param name="triangle">The triangle slot.</param>
    /// <returns><see langword="true"/> when the slot holds a ghost.</returns>
    internal bool IsGhost(int triangle)
    {
        return TriangleVertices[(3 * triangle) + 2] == GhostVertex;
    }

    /// <summary>Whether the slot holds a live triangle rather than a vacated one.</summary>
    /// <param name="slot">The triangle slot.</param>
    /// <returns><see langword="true"/> when the slot is live.</returns>
    internal bool IsSlotLive(int slot)
    {
        return SlotLive[slot];
    }

    /// <summary>The vertex index at the triangle's local slot.</summary>
    /// <param name="triangle">The triangle slot.</param>
    /// <param name="local">The local vertex slot.</param>
    /// <returns>The candidate index, or <see cref="GhostVertex"/>.</returns>
    internal int VertexAt(int triangle, int local)
    {
        return TriangleVertices[(3 * triangle) + local];
    }

    /// <summary>The neighbor across the triangle's local edge.</summary>
    /// <param name="triangle">The triangle slot.</param>
    /// <param name="local">The local edge.</param>
    /// <returns>The neighboring triangle slot.</returns>
    internal int NeighborAt(int triangle, int local)
    {
        return TriangleNeighbors[(3 * triangle) + local];
    }

    /// <summary>
    /// The maximal collinear-prefix fan bootstrap: the maximal sorted
    /// prefix on the first two candidates' line plus the first off-line
    /// candidate build the initial fan — each pair triangle oriented
    /// counter-clockwise by its exact sign — the ghost ring closes around
    /// the fan through the shared twin map, and the remaining candidates
    /// insert one by one.
    /// </summary>
    private void Bootstrap()
    {
        List<Point2d> candidates = Candidates;
        int prefixLength = 2;

        while(prefixLength < candidates.Count
            && ExactOrientation.Orient2D(candidates[0], candidates[1], candidates[prefixLength]) == 0)
        {
            prefixLength++;
        }

        Debug.Assert(prefixLength < candidates.Count, "A fully collinear candidate set never reaches the mesh.");

        int apex = prefixLength;
        DirectedEdges.Clear();

        for(int pair = 0; pair + 1 <= prefixLength - 1; pair++)
        {
            int first = pair;
            int second = pair + 1;
            int triangle = ExactOrientation.Orient2D(candidates[first], candidates[second], candidates[apex]) > 0
                ? AllocateTriangle(first, second, apex)
                : AllocateTriangle(second, first, apex);
            LinkTriangleEdges(triangle);
        }

        //Every directed edge without a twin is a fan boundary edge; its
        //ghost stores the edge reversed, sentinel last, and the same twin
        //map chains ghost-to-ghost sentinel edges and ghost-to-real hull
        //edges alike. The snapshot list keeps enumeration stable while the
        //ghost wiring adds entries.
        var boundary = new List<(int Start, int End)>();

        foreach(KeyValuePair<(int Start, int End), (int Triangle, int Local)> entry in DirectedEdges)
        {
            if(NeighborAt(entry.Value.Triangle, entry.Value.Local) == Unlinked)
            {
                boundary.Add(entry.Key);
            }
        }

        int seedFirst = -1;
        int seedSecond = -1;

        foreach((int start, int end) in boundary)
        {
            int ghost = AllocateTriangle(end, start, GhostVertex);
            LinkTriangleEdges(ghost);

            //The seed pair is the two ghosts incident to the bootstrap apex
            //— the lex-max mesh vertex when the bootstrap closes.
            if(start == apex || end == apex)
            {
                if(seedFirst < 0)
                {
                    seedFirst = ghost;
                }
                else
                {
                    seedSecond = ghost;
                }
            }
        }

        Debug.Assert(seedSecond >= 0, "The apex bounds exactly two hull edges of the bootstrap fan.");
        SeedGhostFirst = seedFirst;
        SeedGhostSecond = seedSecond;

        for(int candidate = apex + 1; candidate < candidates.Count; candidate++)
        {
            Insert(candidate);
        }
    }

    /// <summary>
    /// Inserts the candidate: the exact O(1) seed, the strict breadth-first
    /// cavity over adjacency, deletion as a set, and the uniform fan over
    /// the cavity boundary.
    /// </summary>
    /// <param name="candidate">The candidate index to insert.</param>
    private void Insert(int candidate)
    {
        Point2d point = Candidates[candidate];
        CavityStack.Clear();
        CavityMembers.Clear();
        CavityBoundary.Clear();
        EnsureCavityStateCapacity();

        if(GhostGateSign(SeedGhostFirst, point) > 0)
        {
            CavityStack.Add(SeedGhostFirst);
        }

        if(GhostGateSign(SeedGhostSecond, point) > 0)
        {
            CavityStack.Add(SeedGhostSecond);
        }

        Debug.Assert(CavityStack.Count > 0, "The seed theorem guarantees a strictly outward gate.");

        while(CavityStack.Count > 0)
        {
            int triangle = CavityStack[^1];
            CavityStack.RemoveAt(CavityStack.Count - 1);

            if(CavityState[triangle] != 0)
            {
                continue;
            }

            TouchedSlots.Add(triangle);
            bool joins = IsGhost(triangle)
                ? GhostGateSign(triangle, point) > 0
                : ExactInCircle.Sign(
                    Scratch,
                    Candidates[VertexAt(triangle, 0)],
                    Candidates[VertexAt(triangle, 1)],
                    Candidates[VertexAt(triangle, 2)],
                    point) > 0;

            if(!joins)
            {
                CavityState[triangle] = 2;

                continue;
            }

            CavityState[triangle] = 1;
            CavityMembers.Add(triangle);

            for(int local = 0; local < 3; local++)
            {
                int neighbor = NeighborAt(triangle, local);

                if(CavityState[neighbor] == 0)
                {
                    CavityStack.Add(neighbor);
                }
            }
        }

        CollectCavityBoundary();
        FanCavity(candidate, point);
        ResetTouchedSlots();
    }

    /// <summary>Collects every edge of a cavity member whose across-neighbor did not join.</summary>
    private void CollectCavityBoundary()
    {
        foreach(int member in CavityMembers)
        {
            for(int local = 0; local < 3; local++)
            {
                int across = NeighborAt(member, local);

                if(CavityState[across] == 1)
                {
                    continue;
                }

                CavityBoundary.Add((VertexAt(member, (local + 1) % 3), VertexAt(member, (local + 2) % 3), across));
            }
        }
    }

    /// <summary>
    /// Deletes the cavity as a set — slots pushed to the free list in
    /// ascending order — then fans the new point to every boundary
    /// edge uniformly: the fan of edge <c>(s, e)</c> is the triple
    /// <c>(candidate, s, e)</c> rotated sentinel-last, so a real–real edge
    /// fans a real counter-clockwise triangle and a sentinel-incident edge
    /// fans a ghost, with all adjacency — fan-to-fan, fan-to-survivor, and
    /// the two new sentinel ring edges — resolved by the shared twin map.
    /// Exactly two boundary edges are sentinel-incident; their ghosts
    /// become the next insertion's seed pair.
    /// </summary>
    /// <param name="candidate">The inserted candidate index.</param>
    /// <param name="point">The inserted position.</param>
    private void FanCavity(int candidate, Point2d point)
    {
        CavityMembers.Sort();

        foreach(int member in CavityMembers)
        {
            ReleaseTriangle(member);
        }

        DirectedEdges.Clear();
        int firstNewGhost = -1;
        int secondNewGhost = -1;

        foreach((int start, int end, int across) in CavityBoundary)
        {
            int created;

            if(start == GhostVertex)
            {
                created = AllocateTriangle(end, candidate, GhostVertex);
            }
            else if(end == GhostVertex)
            {
                created = AllocateTriangle(candidate, start, GhostVertex);
            }
            else
            {
                created = AllocateTriangle(candidate, start, end);
                Debug.Assert(
                    ExactOrientation.Orient2D(point, Candidates[start], Candidates[end]) > 0,
                    "A fan triangle is a strict counter-clockwise turn.");
            }

            if(IsGhost(created))
            {
                if(firstNewGhost < 0)
                {
                    firstNewGhost = created;
                }
                else
                {
                    secondNewGhost = created;
                }
            }

            //The survivor across the boundary edge sees the fan triangle
            //through the reversed direction; both links are set directly,
            //and the twin map wires the fan-internal edges.
            int createdLocal = FindEdgeLocal(created, start, end);
            SetNeighbor(created, createdLocal, across);
            SetNeighbor(across, FindEdgeLocal(across, end, start), created);
            LinkTriangleEdges(created);
        }

        Debug.Assert(secondNewGhost >= 0, "Exactly two sentinel-incident boundary edges bound any cavity.");
        SeedGhostFirst = firstNewGhost;
        SeedGhostSecond = secondNewGhost;
    }

    /// <summary>The orientation gate of a ghost against its stored, reversed hull edge.</summary>
    /// <param name="ghost">The ghost triangle slot.</param>
    /// <param name="point">The queried position.</param>
    /// <returns>The orientation sign.</returns>
    private int GhostGateSign(int ghost, Point2d point)
    {
        return ExactOrientation.Orient2D(Candidates[VertexAt(ghost, 0)], Candidates[VertexAt(ghost, 1)], point);
    }

    /// <summary>Allocates a slot from the free list or appends, updating the live counts.</summary>
    /// <param name="a">The first vertex index.</param>
    /// <param name="b">The second vertex index.</param>
    /// <param name="c">The third vertex index, <see cref="GhostVertex"/> for a ghost.</param>
    /// <returns>The allocated slot.</returns>
    private int AllocateTriangle(int a, int b, int c)
    {
        int slot;

        if(FreeSlots.Count > 0)
        {
            slot = FreeSlots[^1];
            FreeSlots.RemoveAt(FreeSlots.Count - 1);
            int basis = 3 * slot;
            TriangleVertices[basis] = a;
            TriangleVertices[basis + 1] = b;
            TriangleVertices[basis + 2] = c;
            TriangleNeighbors[basis] = Unlinked;
            TriangleNeighbors[basis + 1] = Unlinked;
            TriangleNeighbors[basis + 2] = Unlinked;
            SlotLive[slot] = true;
        }
        else
        {
            slot = SlotCount;
            TriangleVertices.Add(a);
            TriangleVertices.Add(b);
            TriangleVertices.Add(c);
            TriangleNeighbors.Add(Unlinked);
            TriangleNeighbors.Add(Unlinked);
            TriangleNeighbors.Add(Unlinked);
            SlotLive.Add(true);
        }

        if(c == GhostVertex)
        {
            GhostCount++;
        }
        else
        {
            RealTriangleCount++;
        }

        return slot;
    }

    /// <summary>Releases a cavity member's slot to the free list.</summary>
    /// <param name="slot">The triangle slot to vacate.</param>
    private void ReleaseTriangle(int slot)
    {
        if(IsGhost(slot))
        {
            GhostCount--;
        }
        else
        {
            RealTriangleCount--;
        }

        FreeSlots.Add(slot);
        SlotLive[slot] = false;
    }

    /// <summary>Sets one directional neighbor link.</summary>
    /// <param name="triangle">The triangle slot.</param>
    /// <param name="local">The local edge.</param>
    /// <param name="neighbor">The neighboring triangle slot.</param>
    private void SetNeighbor(int triangle, int local, int neighbor)
    {
        TriangleNeighbors[(3 * triangle) + local] = neighbor;
    }

    /// <summary>The local edge of the triangle whose directed endpoints are the given pair.</summary>
    /// <param name="triangle">The triangle slot.</param>
    /// <param name="start">The directed edge's start vertex.</param>
    /// <param name="end">The directed edge's end vertex.</param>
    /// <returns>The local edge index.</returns>
    private int FindEdgeLocal(int triangle, int start, int end)
    {
        for(int local = 0; local < 3; local++)
        {
            if(VertexAt(triangle, (local + 1) % 3) == start && VertexAt(triangle, (local + 2) % 3) == end)
            {
                return local;
            }
        }

        Debug.Fail("The directed edge belongs to the triangle by construction.");

        return -1;
    }

    /// <summary>
    /// Registers the triangle's directed edges in the shared twin map,
    /// linking mutual neighbors whenever a reversed twin is already present
    /// and the edge is still unwired.
    /// </summary>
    /// <param name="triangle">The triangle slot to wire.</param>
    private void LinkTriangleEdges(int triangle)
    {
        for(int local = 0; local < 3; local++)
        {
            if(NeighborAt(triangle, local) != Unlinked)
            {
                continue;
            }

            int start = VertexAt(triangle, (local + 1) % 3);
            int end = VertexAt(triangle, (local + 2) % 3);

            if(DirectedEdges.TryGetValue((end, start), out (int Triangle, int Local) twin))
            {
                SetNeighbor(triangle, local, twin.Triangle);
                SetNeighbor(twin.Triangle, twin.Local, triangle);
            }
            else
            {
                DirectedEdges[(start, end)] = (triangle, local);
            }
        }
    }

    /// <summary>Grows the per-slot walk state to cover every allocated slot.</summary>
    private void EnsureCavityStateCapacity()
    {
        while(CavityState.Count < SlotCount)
        {
            CavityState.Add(0);
        }
    }

    /// <summary>Clears exactly the walk marks this insertion set.</summary>
    private void ResetTouchedSlots()
    {
        foreach(int slot in TouchedSlots)
        {
            CavityState[slot] = 0;
        }

        TouchedSlots.Clear();
    }
}
