using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The concave hull of any operand's point set: the
/// chi-shape family's Delaunay erosion, normalized to the scale-free
/// <c>edgeLengthRatio</c> in <c>[0, 1]</c>. Candidates are the flat vertex
/// column whole — kind-blind, every part role, every collection depth — so
/// the operand side is point-set-total; the only <c>false</c> is the
/// malformed parameter (NaN or outside the unit interval), the data-plane
/// refusal bucket. The loose extreme is pinned bitwise by delegation:
/// <c>edgeLengthRatio == 1</c> returns <see cref="GeometryConvexHull.Compute"/>
/// before any triangulation work, and every operand whose distinct vertex
/// set admits no triangle answers the convex hull's own degenerate ladder
/// the same way. Below the extreme, border triangles of the internal
/// Delaunay triangulation erode — largest squared boundary edge first, ties
/// to the larger canonical area, then to the smallest sorted-index triple —
/// while an exact connectivity guard keeps the region one simple polygon;
/// the result is the boundary trace of the survivors, counter-clockwise
/// from its lexicographic minimum, retaining collinear boundary vertices.
/// Results are planar XY, never alias operand columns, and carry no Z/M.
/// </summary>
public static class GeometryConcaveHull
{
    /// <summary>Computes the concave hull; false exactly when the ratio is malformed.</summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="edgeLengthRatio">The scale-free concaveness ratio in <c>[0, 1]</c>.</param>
    /// <param name="hull">The computed hull.</param>
    /// <returns><see langword="true"/> when the ratio is well-formed.</returns>
    public static bool TryCompute(in FlatGeometry geometry, double edgeLengthRatio, out FlatGeometry hull)
    {
        return TryCompute(in geometry, edgeLengthRatio, out hull, out _, out _, out _, out _);
    }

    /// <summary>
    /// The seam-test overload: <paramref name="triangleCount"/> and
    /// <paramref name="ghostCount"/> are measured immediately after the
    /// Bowyer–Watson build completes, before any erosion round —
    /// <paramref name="ghostCount"/> is the boundary-cycle point count, so
    /// <c>triangleCount == 2n − 2 − ghostCount</c> is the Euler pin;
    /// <paramref name="triangulatedArea"/> is the build-time sum of the real
    /// triangles' absolute canonical areas — the covering identity against
    /// the convex hull's area; <paramref name="erodedTriangleCount"/> counts
    /// border deletions. All four are zero on the two delegating paths,
    /// where no mesh is constructed.
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="edgeLengthRatio">The scale-free concaveness ratio in <c>[0, 1]</c>.</param>
    /// <param name="hull">The computed hull.</param>
    /// <param name="triangleCount">The real triangle count at build time.</param>
    /// <param name="erodedTriangleCount">The number of border deletions.</param>
    /// <param name="ghostCount">The ghost count at build time.</param>
    /// <param name="triangulatedArea">The build-time absolute area sum.</param>
    /// <returns><see langword="true"/> when the ratio is well-formed.</returns>
    internal static bool TryCompute(
        in FlatGeometry geometry,
        double edgeLengthRatio,
        out FlatGeometry hull,
        out int triangleCount,
        out int erodedTriangleCount,
        out int ghostCount,
        out double triangulatedArea)
    {
        triangleCount = 0;
        erodedTriangleCount = 0;
        ghostCount = 0;
        triangulatedArea = 0.0;

        //Parameter validation precedes everything: a malformed ratio refuses
        //even on an empty operand, and hull stays default.
        if(double.IsNaN(edgeLengthRatio) || edgeLengthRatio < 0.0 || edgeLengthRatio > 1.0)
        {
            hull = default;

            return false;
        }

        //The loose extreme short-circuits to the convex hull before any
        //triangulation work — delegation is the mechanism.
        if(edgeLengthRatio == 1.0)
        {
            hull = GeometryConvexHull.Compute(in geometry);

            return true;
        }

        List<Point2d> candidates = GeometryConvexHull.CollectDistinctSorted(geometry.Vertices);
        var hullVertices = new List<Point2d>();
        GeometryConvexHull.ComputeHullVertices(in geometry, hullVertices);

        //The degenerate ladder delegates whenever the distinct vertex set
        //admits no triangle — the strict-turn hull decides exactly.
        if(hullVertices.Count < 3)
        {
            hull = GeometryConvexHull.Compute(in geometry);

            return true;
        }

        DelaunayMesh mesh = DelaunayMesh.Create(candidates, InCircleScratch.Create());
        triangleCount = mesh.RealTriangleCount;
        ghostCount = mesh.GhostCount;

        var erosion = new ErosionState(mesh);
        triangulatedArea = erosion.TriangulatedArea;
        erodedTriangleCount = erosion.Erode(edgeLengthRatio);
        hull = erosion.TraceBoundary();

        return true;
    }

    /// <summary>
    /// The erosion pass over a built mesh: the census, the border
    /// worklist with selection-time revalidation, the exact boundary-vertex
    /// guard, and the boundary trace. Call-local, single-owner, like the
    /// mesh it consumes.
    /// </summary>
    private sealed class ErosionState
    {
        /// <summary>
        /// Takes the census of the built mesh: the ghost ring's vertices seed the
        /// boundary-vertex flags, every real triangle's edges set the squared
        /// extremes the erosion target interpolates between, and the absolute
        /// triangle areas accumulate into the covering sum.
        /// </summary>
        /// <param name="mesh">The built mesh this pass consumes.</param>
        public ErosionState(DelaunayMesh mesh)
        {
            Mesh = mesh;
            Removed = new bool[mesh.SlotCount];
            BoundaryVertex = new bool[mesh.Candidates.Count];
            LiveByIndex = new bool[mesh.SlotCount];

            for(int slot = 0; slot < mesh.SlotCount; slot++)
            {
                LiveByIndex[slot] = mesh.IsSlotLive(slot);
            }

            //The boundary-vertex flags initialize to the ghost ring's
            //vertices — identically the boundary cycle — and the
            //census walks every edge of every real triangle while
            //accumulating the covering identity's area sum.
            double shortestSquared = double.PositiveInfinity;
            double longestSquared = 0.0;
            double areaSum = 0.0;

            for(int slot = 0; slot < mesh.SlotCount; slot++)
            {
                if(!LiveByIndex[slot])
                {
                    continue;
                }

                if(mesh.IsGhost(slot))
                {
                    BoundaryVertex[mesh.VertexAt(slot, 0)] = true;
                    BoundaryVertex[mesh.VertexAt(slot, 1)] = true;

                    continue;
                }

                for(int local = 0; local < 3; local++)
                {
                    double lengthSquared = EdgeLengthSquared(slot, local);

                    if(lengthSquared < shortestSquared)
                    {
                        shortestSquared = lengthSquared;
                    }

                    if(lengthSquared > longestSquared)
                    {
                        longestSquared = lengthSquared;
                    }
                }

                areaSum += Math.Abs(CanonicalTwiceArea(slot)) / 2.0;
            }

            ShortestSquared = shortestSquared;
            LongestSquared = longestSquared;
            TriangulatedArea = areaSum;
        }

        /// <summary>The built mesh the pass reads adjacency and geometry from.</summary>
        private DelaunayMesh Mesh { get; }

        /// <summary>Erosion never mutates adjacency; removal is a mark.</summary>
        private bool[] Removed { get; }

        /// <summary>The monotone boundary-vertex flags, grown apex-by-apex.</summary>
        private bool[] BoundaryVertex { get; }

        /// <summary>Which slots were live at build time (vacated slots never revive).</summary>
        private bool[] LiveByIndex { get; }

        /// <summary>The squared length of the shortest triangulation edge.</summary>
        private double ShortestSquared { get; }

        /// <summary>The squared length of the longest triangulation edge.</summary>
        private double LongestSquared { get; }

        /// <summary>The build-time covering sum: absolute triangle areas of the whole mesh.</summary>
        public double TriangulatedArea { get; }

        /// <summary>
        /// Runs the walk to its fixed point and returns the number of border
        /// deletions. The target interpolates in the linear domain from the
        /// two census square roots; eligibility compares in the squared
        /// domain; ratio zero maps to target zero, the hard special case.
        /// </summary>
        /// <param name="edgeLengthRatio">The scale-free concaveness ratio.</param>
        /// <returns>The number of border deletions.</returns>
        public int Erode(double edgeLengthRatio)
        {
            double targetSquared;

            if(edgeLengthRatio == 0.0)
            {
                targetSquared = 0.0;
            }
            else
            {
                double shortest = Math.Sqrt(ShortestSquared);
                double longest = Math.Sqrt(LongestSquared);
                double target = (edgeLengthRatio * (longest - shortest)) + shortest;
                targetSquared = target * target;
            }

            int eroded = 0;

            while(TrySelectRemovable(targetSquared, out int selected, out int apexVertex))
            {
                Removed[selected] = true;
                BoundaryVertex[apexVertex] = true;
                eroded++;
            }

            return eroded;
        }

        /// <summary>
        /// Traces the surviving set's boundary: from the lex-min
        /// surviving vertex's outgoing boundary edge — always candidate
        /// index 0, which can never be orphaned — rotating around each head
        /// vertex through surviving triangles to the next boundary edge,
        /// closing on return; emitted as a counter-clockwise
        /// shell from its lexicographic minimum, closed manually.
        /// </summary>
        /// <returns>The surviving region as a single-ring polygon.</returns>
        public FlatGeometry TraceBoundary()
        {
            //Find the boundary edge leaving candidate 0: scan its incident
            //surviving triangles for the one whose boundary edge starts
            //there. Directed boundary edges run counter-clockwise around
            //the surviving region (interior on the left).
            var cycle = new List<Point2d>();
            int currentTriangle = -1;
            int currentLocal = -1;

            for(int slot = 0; slot < Mesh.SlotCount && currentTriangle < 0; slot++)
            {
                if(!LiveByIndex[slot] || Removed[slot] || Mesh.IsGhost(slot))
                {
                    continue;
                }

                for(int local = 0; local < 3; local++)
                {
                    int across = Mesh.NeighborAt(slot, local);

                    if(!Mesh.IsGhost(across) && !Removed[across])
                    {
                        continue;
                    }

                    if(Mesh.VertexAt(slot, (local + 1) % 3) == 0)
                    {
                        currentTriangle = slot;
                        currentLocal = local;

                        break;
                    }
                }
            }

            int startTriangle = currentTriangle;
            int startLocal = currentLocal;

            do
            {
                cycle.Add(Mesh.Candidates[Mesh.VertexAt(currentTriangle, (currentLocal + 1) % 3)]);

                //Rotate around the head vertex through surviving triangles:
                //step to the edge after the head, then pivot across interior
                //edges until the next boundary edge leaves the head.
                int headLocal = (currentLocal + 2) % 3;

                while(true)
                {
                    //The candidate next edge shares the head as its start:
                    //the local edge whose start is the head is
                    //(head's local + 2) % 3.
                    int nextLocal = (headLocal + 2) % 3;
                    int across = Mesh.NeighborAt(currentTriangle, nextLocal);

                    if(Mesh.IsGhost(across) || Removed[across])
                    {
                        currentLocal = nextLocal;

                        break;
                    }

                    //Pivot into the neighbor, keeping the same head vertex.
                    int head = Mesh.VertexAt(currentTriangle, headLocal);
                    currentTriangle = across;
                    headLocal = LocalOf(across, head);
                }
            }
            while(currentTriangle != startTriangle || currentLocal != startLocal);

            var ring = new Point2d[cycle.Count + 1];

            for(int index = 0; index < cycle.Count; index++)
            {
                ring[index] = cycle[index];
            }

            ring[cycle.Count] = cycle[0];

            return FlatGeometryFactory.CreatePolygon([ring]);
        }

        /// <summary>
        /// One strict-improvement scan over the live real triangles,
        /// revalidating everything against live state at selection time —
        /// the load-bearing staleness rule: border status, the
        /// single boundary edge, size eligibility, and the apex guard are
        /// all read fresh, so no cached state can go stale. The total order
        /// is squared size, then canonical twice-area, then the smallest
        /// sorted-index triple.
        /// </summary>
        /// <param name="targetSquared">The squared eligibility threshold.</param>
        /// <param name="selected">The selected triangle slot.</param>
        /// <param name="apexVertex">The selected triangle's apex vertex.</param>
        /// <returns><see langword="true"/> when a removable border triangle was found.</returns>
        private bool TrySelectRemovable(double targetSquared, out int selected, out int apexVertex)
        {
            selected = -1;
            apexVertex = -1;
            double bestSize = double.NegativeInfinity;
            double bestArea = double.NegativeInfinity;

            for(int slot = 0; slot < Mesh.SlotCount; slot++)
            {
                if(!LiveByIndex[slot] || Removed[slot] || Mesh.IsGhost(slot))
                {
                    continue;
                }

                //A border triangle has exactly one boundary edge — an edge
                //whose across-neighbor is a ghost or a removed triangle.
                int boundaryLocal = -1;
                int boundaryEdges = 0;

                for(int local = 0; local < 3; local++)
                {
                    int across = Mesh.NeighborAt(slot, local);

                    if(Mesh.IsGhost(across) || Removed[across])
                    {
                        boundaryLocal = local;
                        boundaryEdges++;
                    }
                }

                if(boundaryEdges != 1)
                {
                    continue;
                }

                //The apex — the vertex the single boundary edge is opposite,
                //shared by the two interior edges — must not already touch
                //the boundary, or removal would pinch the region.
                int apex = Mesh.VertexAt(slot, boundaryLocal);

                if(BoundaryVertex[apex])
                {
                    continue;
                }

                double size = EdgeLengthSquared(slot, boundaryLocal);

                if(size < targetSquared)
                {
                    continue;
                }

                if(selected >= 0 && !Improves(slot, size, bestSize, bestArea, selected))
                {
                    continue;
                }

                selected = slot;
                apexVertex = apex;
                bestSize = size;
                bestArea = CanonicalTwiceArea(slot);
            }

            return selected >= 0;
        }

        /// <summary>The strict-improvement comparison over the selection total order.</summary>
        /// <param name="slot">The candidate triangle slot.</param>
        /// <param name="size">The candidate's squared boundary-edge length.</param>
        /// <param name="bestSize">The incumbent's squared boundary-edge length.</param>
        /// <param name="bestArea">The incumbent's canonical twice-area.</param>
        /// <param name="bestSlot">The incumbent triangle slot.</param>
        /// <returns><see langword="true"/> when the candidate strictly improves.</returns>
        private bool Improves(int slot, double size, double bestSize, double bestArea, int bestSlot)
        {
            if(size != bestSize)
            {
                return size > bestSize;
            }

            double area = CanonicalTwiceArea(slot);

            if(area != bestArea)
            {
                return area > bestArea;
            }

            return CompareIndexTriples(slot, bestSlot) < 0;
        }

        /// <summary>
        /// The canonical tie-break twice-area: <c>(b − a) × (c − a)</c>
        /// with the triple rotated so the lowest sorted-index vertex leads,
        /// counter-clockwise order preserved — canonical anchor and
        /// canonical evaluation order, both required, so the key is a
        /// function of the vertex triple, never of stored rotation.
        /// </summary>
        /// <param name="slot">The triangle slot.</param>
        /// <returns>The canonical twice-area.</returns>
        private double CanonicalTwiceArea(int slot)
        {
            int lowestLocal = 0;

            for(int local = 1; local < 3; local++)
            {
                if(Mesh.VertexAt(slot, local) < Mesh.VertexAt(slot, lowestLocal))
                {
                    lowestLocal = local;
                }
            }

            Point2d anchor = Mesh.Candidates[Mesh.VertexAt(slot, lowestLocal)];
            Point2d second = Mesh.Candidates[Mesh.VertexAt(slot, (lowestLocal + 1) % 3)];
            Point2d third = Mesh.Candidates[Mesh.VertexAt(slot, (lowestLocal + 2) % 3)];

            return ((second.X - anchor.X) * (third.Y - anchor.Y)) - ((second.Y - anchor.Y) * (third.X - anchor.X));
        }

        /// <summary>Orders two triangles by their ascending sorted-index triples, the selection order's third key.</summary>
        /// <param name="first">The first triangle slot.</param>
        /// <param name="second">The second triangle slot.</param>
        /// <returns>The comparison result.</returns>
        private int CompareIndexTriples(int first, int second)
        {
            Span<int> firstTriple = [Mesh.VertexAt(first, 0), Mesh.VertexAt(first, 1), Mesh.VertexAt(first, 2)];
            Span<int> secondTriple = [Mesh.VertexAt(second, 0), Mesh.VertexAt(second, 1), Mesh.VertexAt(second, 2)];
            firstTriple.Sort();
            secondTriple.Sort();

            for(int position = 0; position < 3; position++)
            {
                int comparison = firstTriple[position].CompareTo(secondTriple[position]);

                if(comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        /// <summary>The squared length of the triangle's local edge.</summary>
        /// <param name="slot">The triangle slot.</param>
        /// <param name="local">The local edge.</param>
        /// <returns>The squared edge length.</returns>
        private double EdgeLengthSquared(int slot, int local)
        {
            Point2d start = Mesh.Candidates[Mesh.VertexAt(slot, (local + 1) % 3)];
            Point2d end = Mesh.Candidates[Mesh.VertexAt(slot, (local + 2) % 3)];
            double deltaX = end.X - start.X;
            double deltaY = end.Y - start.Y;

            return (deltaX * deltaX) + (deltaY * deltaY);
        }

        /// <summary>The local slot of the vertex within the triangle.</summary>
        /// <param name="triangle">The triangle slot.</param>
        /// <param name="vertex">The vertex index to locate.</param>
        /// <returns>The local slot, or -1 when the vertex is absent.</returns>
        private int LocalOf(int triangle, int vertex)
        {
            for(int local = 0; local < 3; local++)
            {
                if(Mesh.VertexAt(triangle, local) == vertex)
                {
                    return local;
                }
            }

            return -1;
        }
    }
}
