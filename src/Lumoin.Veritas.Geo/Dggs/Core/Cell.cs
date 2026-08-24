using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Lattice;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Projections;
using Lumoin.Veritas.Geo.Dggs.Traversal;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Point-to-cell and cell-to-point conversion: the estimate pipeline (nearest-origin projection plus
/// quintant/Hilbert discretization) that lands close to the right cell, and a spiral-then-neighbor
/// fallback search that finds the exact containing cell on the rare occasions the estimate misses. No
/// result is cached across calls: caching could return a different-but-adjacent cell for a point within
/// float noise of a boundary depending on call history, and an order-dependent result is unacceptable
/// for a verifiable spatial key. Not caching also makes every function below safe under concurrent
/// callers.
/// </summary>
internal static class Cell
{
    /// <summary>
    /// Spiral perturbation radius at hilbert resolution 1, in radians of tangent-plane offset: computed
    /// as an exact degree-to-radian conversion, never a rounded radian literal. Higher resolutions scale
    /// this down by <c>1 / 2^hilbertResolution</c>.
    /// </summary>
    private const double SpiralScaleRadians = (70 * Math.PI) / 180;

    /// <summary>
    /// Largest number of entries <see cref="GlobalNeighbors.GetGlobalCellNeighbors"/> can ever return for a
    /// single cell: its resolution-1 special case (the non-<c>edgeOnly</c> branch of <c>GetRes1Neighbors</c>)
    /// produces up to 11 distinct neighbors, more than every other resolution's documented 6-8 per cell.
    /// </summary>
    private const int MaxGlobalCellNeighbors = 11;

    /// <summary>
    /// Hard upper bound on the number of candidates the spiral/neighbor fallback in
    /// <see cref="SphericalToCell"/> can ever inspect: 1 (the first estimate) + <see cref="Spiral.SampleCount"/>
    /// spiral samples (each contributing at most one new candidate) + 3 × <see cref="MaxGlobalCellNeighbors"/>
    /// (the neighbor-expansion loop walks the top 3 candidates by distance, and each can contribute at most
    /// <see cref="MaxGlobalCellNeighbors"/> new candidates) = 58.
    /// </summary>
    private const int MaxFallbackCandidates = 1 + Spiral.SampleCount + (3 * MaxGlobalCellNeighbors);

    /// <summary>
    /// Stack buffer capacity for the fallback search's inspected-key and candidate spans in
    /// <see cref="SphericalToCell"/>: <see cref="MaxFallbackCandidates"/> plus explicit slack, so a small
    /// drift in the derivation above trips the capacity assertions in <see cref="AppendInspectedKey"/> and
    /// <see cref="AppendCandidate"/> rather than corrupting memory.
    /// </summary>
    private const int FallbackBufferCapacity = 64;

    /// <summary>Converts a geographic point to the cell containing it at <paramref name="resolution"/>.</summary>
    public static ulong LonLatToCell(LonLat lonLat, int resolution)
    {
        return SphericalToCell(CoordinateTransforms.FromLonLat(lonLat), resolution);
    }

    /// <summary>
    /// Like <see cref="LonLatToCell"/>, but accepts a point already in A5's internal spherical
    /// representation (the rotated authalic frame produced by <see cref="CoordinateTransforms.FromLonLat"/>
    /// or <see cref="CoordinateTransforms.ToSpherical"/>). Skips the redundant authalic inverse/forward
    /// round trip for callers that already hold a spherical point.
    /// </summary>
    /// <remarks>
    /// The lattice-based estimate only approximates the pentagon lattice, so it may land in a cell
    /// adjacent to the true containing one. The search below recovers from that: first the estimate
    /// itself is tested directly (the common case for non-boundary points); failing that, a spiral of
    /// perturbed samples around the point is tried, each re-estimated and tested; failing that too
    /// (reachable only for points effectively at the polar singularity at very high resolutions), the
    /// direct neighbors of the closest spiral candidates are tried; and if nothing strictly contains the
    /// point even then, the closest candidate found anywhere in the search wins.
    /// </remarks>
    public static ulong SphericalToCell(Spherical spherical, int resolution)
    {
        // Resolution -1 represents WORLD_CELL, which covers the entire world.
        if(resolution == -1)
        {
            return Serialization.WorldCell;
        }

        if(resolution < Serialization.FirstHilbertResolution)
        {
            // For low resolutions there is no Hilbert curve, so the estimate is exact.
            return Serialization.Serialize(SphericalToEstimate(spherical, resolution));
        }

        // Try the original point's projection-based estimate. Common case for non-boundary points.
        A5Cell firstEstimate = SphericalToEstimate(spherical, resolution);
        ulong firstKey = Serialization.Serialize(firstEstimate);
        double firstDistance = A5CellContainsPoint(firstEstimate, spherical);
        if(firstDistance > 0)
        {
            return firstKey;
        }

        // Spiral search: perturb the point in the tangent plane to find nearby estimate cells.
        int hilbertResolution = 1 + resolution - Serialization.FirstHilbertResolution;
        double scale = SpiralScaleRadians / Math.Pow(2, hilbertResolution);

        // Fixed-capacity stack buffers avoid heap allocation: MaxFallbackCandidates (58) is a hard bound
        // on how many keys this search ever inspects, so no growth is possible and no heap allocation is
        // needed.
        Span<ulong> inspectedKeys = stackalloc ulong[FallbackBufferCapacity];
        int inspectedKeyCount = 0;
        AppendInspectedKey(inspectedKeys, ref inspectedKeyCount, firstKey);

        Span<CellDistanceCandidate> candidates = stackalloc CellDistanceCandidate[FallbackBufferCapacity];
        int candidateCount = 0;
        AppendCandidate(candidates, ref candidateCount, new CellDistanceCandidate(firstKey, firstDistance));

        Spiral spiral = new(spherical, scale);
        for(int index = 0; index < Spiral.SampleCount; index++)
        {
            A5Cell estimate = CartesianToEstimate(spiral.Sample(index), resolution);
            ulong estimateKey = Serialization.Serialize(estimate);
            if(Contains(inspectedKeys, inspectedKeyCount, estimateKey))
            {
                continue;
            }

            AppendInspectedKey(inspectedKeys, ref inspectedKeyCount, estimateKey);

            double distance = A5CellContainsPoint(estimate, spherical);
            if(distance > 0)
            {
                return estimateKey;
            }

            AppendCandidate(candidates, ref candidateCount, new CellDistanceCandidate(estimateKey, distance));
        }

        // Spiral exhausted without finding a strict container. This is reachable for points right at
        // the polar singularity at very high resolutions, where re-projecting any tangent sample snaps
        // back to a small set of cells while the geometrically-containing cell is offset by one
        // adjacency step. Fall back to direct neighbors of the closest spiral candidates, which always
        // finds it. The sort must be STABLE (descending by distance) to match tie-breaking exactly;
        // StableSortDescendingByDistance below is a stable sort, matching List<T>.OrderByDescending's
        // tie-breaking without List<T>.Sort's instability.
        StableSortDescendingByDistance(candidates, candidateCount);
        int neighborExpansionCount = Math.Min(3, candidateCount);
        for(int k = 0; k < neighborExpansionCount; k++)
        {
            // candidates[k] keeps referring to one of the top-K entries from the sort above even as the
            // loop below appends further candidates to the same buffer — appends only ever land past the
            // sorted prefix.
            ulong[] neighbors = GlobalNeighbors.GetGlobalCellNeighbors(candidates[k].CellId);
            foreach(ulong neighborKey in neighbors)
            {
                if(Contains(inspectedKeys, inspectedKeyCount, neighborKey))
                {
                    continue;
                }

                AppendInspectedKey(inspectedKeys, ref inspectedKeyCount, neighborKey);

                A5Cell neighborCell = Serialization.Deserialize(neighborKey);
                double distance = A5CellContainsPoint(neighborCell, spherical);
                if(distance > 0)
                {
                    return neighborKey;
                }

                AppendCandidate(candidates, ref candidateCount, new CellDistanceCandidate(neighborKey, distance));
            }
        }

        // True fallback: closest candidate wins, even if technically just outside.
        StableSortDescendingByDistance(candidates, candidateCount);

        return candidates[0].CellId;
    }

    /// <summary>
    /// Linear scan for <paramref name="key"/> among the first <paramref name="inspectedKeyCount"/> entries of
    /// <paramref name="inspectedKeys"/>. The fallback search in <see cref="SphericalToCell"/> never inspects
    /// more than <see cref="MaxFallbackCandidates"/> keys per call (typically under 6), so a linear scan is
    /// both correct and faster here than hashing — and, unlike a <see cref="HashSet{T}"/>, allocates nothing.
    /// </summary>
    private static bool Contains(ReadOnlySpan<ulong> inspectedKeys, int inspectedKeyCount, ulong key)
    {
        for(int index = 0; index < inspectedKeyCount; index++)
        {
            if(inspectedKeys[index] == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Appends <paramref name="key"/> to <paramref name="inspectedKeys"/> at <paramref name="inspectedKeyCount"/>
    /// and increments it. Callers are expected to have already checked <see cref="Contains"/> — this method
    /// only asserts the buffer has room; it does not deduplicate.
    /// </summary>
    private static void AppendInspectedKey(Span<ulong> inspectedKeys, ref int inspectedKeyCount, ulong key)
    {
        Debug.Assert(inspectedKeyCount < MaxFallbackCandidates, "Inspected-key count exceeded the derived MaxFallbackCandidates bound.");

        inspectedKeys[inspectedKeyCount] = key;
        inspectedKeyCount++;
    }

    /// <summary>
    /// Appends <paramref name="candidate"/> to <paramref name="candidates"/> at <paramref name="candidateCount"/>
    /// and increments it, asserting the buffer has room first.
    /// </summary>
    private static void AppendCandidate(Span<CellDistanceCandidate> candidates, ref int candidateCount, CellDistanceCandidate candidate)
    {
        Debug.Assert(candidateCount < MaxFallbackCandidates, "Candidate count exceeded the derived MaxFallbackCandidates bound.");

        candidates[candidateCount] = candidate;
        candidateCount++;
    }

    /// <summary>
    /// Sorts the first <paramref name="count"/> elements of <paramref name="candidates"/> in place, descending
    /// by <see cref="CellDistanceCandidate.Distance"/> — a stable insertion sort standing in for LINQ's
    /// <c>OrderByDescending</c> without allocating. The load-bearing contract: elements with EQUAL distances
    /// keep their original relative order, exactly as <c>OrderByDescending</c> guarantees. That is why the
    /// shift condition below is a STRICT less-than: an inserted element only displaces an already-placed
    /// one with a strictly smaller distance, never one with an equal distance, so ties never swap relative
    /// order.
    /// </summary>
    private static void StableSortDescendingByDistance(Span<CellDistanceCandidate> candidates, int count)
    {
        for(int index = 1; index < count; index++)
        {
            CellDistanceCandidate inserted = candidates[index];
            int shiftIndex = index - 1;
            while(shiftIndex >= 0 && candidates[shiftIndex].Distance < inserted.Distance)
            {
                candidates[shiftIndex + 1] = candidates[shiftIndex];
                shiftIndex--;
            }

            candidates[shiftIndex + 1] = inserted;
        }
    }

    /// <summary>Converts a cell id back to the spherical coordinates of its center (the pentagon centroid, unprojected).</summary>
    public static Spherical CellToSpherical(ulong cell)
    {
        A5Cell decoded = Serialization.Deserialize(cell);
        PentagonShape pentagon = GetPentagon(decoded);

        return DodecahedronProjection.Inverse(pentagon.GetCenter(), decoded.Origin.Id);
    }

    /// <summary>Converts a cell id to the geographic coordinates of its center.</summary>
    public static LonLat CellToLonLat(ulong cell)
    {
        // WORLD_CELL represents the entire world; return (0, 0) as a reasonable default.
        if(cell == Serialization.WorldCell)
        {
            return new LonLat(0, 0);
        }

        return CoordinateTransforms.ToLonLat(CellToSpherical(cell));
    }

    /// <summary>
    /// Converts a cell id to its boundary ring, in geographic coordinates.
    /// </summary>
    /// <param name="cellId">The cell to compute the boundary of.</param>
    /// <param name="closedRing">Pass <see langword="true"/> (the default) to close the ring by repeating the first point as the last.</param>
    /// <param name="segments">
    /// Number of segments to split each pentagon edge into before projection. Pass 0 (the default) to
    /// use <c>max(1, 2^(6 - resolution))</c>, the cell's own resolution.
    /// </param>
    public static LonLat[] CellToBoundary(ulong cellId, bool closedRing = true, int segments = 0)
    {
        // WORLD_CELL represents the entire world and is unbounded.
        if(cellId == Serialization.WorldCell)
        {
            return [];
        }

        A5Cell cell = Serialization.Deserialize(cellId);

        // Every valid resolution (0-30) makes 6 - resolution an exact double exponent, so
        // Math.Pow(2, 6 - resolution) is always either an exact power of two or, for resolution > 6,
        // a fraction below 1 that Math.Max clamps to exactly 1 — never a value truncation would
        // corrupt.
        int effectiveSegments = segments == 0 ? (int)Math.Max(1, Math.Pow(2, 6 - cell.Resolution)) : segments;

        PentagonShape pentagon = GetPentagon(cell);

        // Split each edge into segments before projection: important to do this BEFORE unprojection to
        // obtain equal-area cells.
        PentagonShape splitPentagon = pentagon.SplitEdges(effectiveSegments);
        ReadOnlySpan<Face> vertices = splitPentagon.GetVertices();

        // Unproject to obtain lon/lat coordinates. Fused loop avoids the intermediate unprojected-vertex
        // array the reference allocates separately.
        LonLat[] boundary = new LonLat[vertices.Length];
        for(int index = 0; index < vertices.Length; index++)
        {
            boundary[index] = CoordinateTransforms.ToLonLat(DodecahedronProjection.Inverse(vertices[index], cell.Origin.Id));
        }

        // Normalize longitudes to handle antimeridian crossing.
        LonLat[] normalizedBoundary = CoordinateTransforms.NormalizeLongitudes(boundary);

        LonLat[] result = closedRing ? new LonLat[normalizedBoundary.Length + 1] : normalizedBoundary;
        if(closedRing)
        {
            Array.Copy(normalizedBoundary, result, normalizedBoundary.Length);
            result[^1] = normalizedBoundary[0];
        }

        // This is a patch to make the boundary CCW; the winding order of the pentagon itself is left
        // alone throughout — the patch is applied here rather than at the root cause.
        Array.Reverse(result);

        return result;
    }

    /// <summary>
    /// Tests whether <paramref name="spherical"/> falls inside <paramref name="cell"/>. The stack-only
    /// counterpart of <see cref="GetPentagon"/> plus <see cref="PentagonShape.ContainsPoint(Face)"/>: no
    /// <see cref="PentagonShape"/> or <c>Face[]</c> is allocated for the test, which matters here because
    /// every candidate this method tests (the estimate, every spiral sample, every neighbor-fallback
    /// candidate in <see cref="SphericalToCell"/>) funnels through this one function.
    /// </summary>
    /// <returns>A positive value if strictly inside; otherwise a non-positive value proportional to the distance to the nearest edge.</returns>
    public static double A5CellContainsPoint(A5Cell cell, Spherical spherical)
    {
        Face projectedPoint = DodecahedronProjection.Forward(spherical, cell.Origin.Id);

        return A5CellContainsPointProjected(cell, projectedPoint);
    }

    /// <summary>
    /// Like <see cref="A5CellContainsPoint"/>, but takes the point already projected into
    /// <paramref name="cell"/>'s face coordinates. The batch point-to-cell kernel core calls this with
    /// the estimate's own <see cref="DodecahedronProjection.Forward"/> result: the estimate and the
    /// containment test project the identical point through the identical origin, so the projection is
    /// computed once and reused — the same bits the scalar path computes twice.
    /// </summary>
    /// <returns>A positive value if strictly inside; otherwise a non-positive value proportional to the distance to the nearest edge.</returns>
    public static double A5CellContainsPointProjected(A5Cell cell, Face projectedPoint)
    {
        Span<Face> vertices = stackalloc Face[Tiling.MaxVertexCount];
        int vertexCount = FillPentagonVertices(cell, vertices);

        return PentagonShape.ContainsPoint(vertices[..vertexCount], projectedPoint);
    }

    /// <summary>
    /// Tests whether the segment between two geographic points intersects a cell.
    /// </summary>
    /// <remarks>
    /// The test runs entirely in the cell's face-coordinate plane: both endpoints are projected via the
    /// dodecahedron projection, then checked against the pentagon's straight 2D edges. Treating the
    /// segment as straight in face coordinates is accurate when the segment is short relative to the
    /// face — equal-area projection distortion is negligible at sub-cell scales.
    /// </remarks>
    public static bool CellIntersectsSegment(ulong cellId, LonLat a, LonLat b)
    {
        if(cellId == Serialization.WorldCell)
        {
            return true;
        }

        A5Cell cell = Serialization.Deserialize(cellId);
        PentagonShape pentagon = GetPentagon(cell);
        Face faceA = DodecahedronProjection.Forward(CoordinateTransforms.FromLonLat(a), cell.Origin.Id);
        Face faceB = DodecahedronProjection.Forward(CoordinateTransforms.FromLonLat(b), cell.Origin.Id);

        return pentagon.IntersectsSegment(faceA, faceB);
    }

    /// <summary>
    /// Estimates the cell containing <paramref name="spherical"/> from its nearest dodecahedron-face
    /// origin. The lattice basis only approximates the true pentagon lattice, so this is a nearby cell,
    /// not necessarily the exact one — see the search in <see cref="SphericalToCell"/>.
    /// </summary>
    private static A5Cell SphericalToEstimate(Spherical spherical, int resolution)
    {
        Origin origin = Origins.FindNearestOrigin(spherical);
        Face dodecahedronPoint = DodecahedronProjection.Forward(spherical, origin.Id);

        return FaceToEstimate(dodecahedronPoint, origin, resolution);
    }

    /// <summary>Same as <see cref="SphericalToEstimate"/>, but takes a Cartesian unit vector directly.</summary>
    private static A5Cell CartesianToEstimate(Cartesian cartesian, int resolution)
    {
        Origin origin = Origins.FindNearestOriginCartesian(cartesian);
        Face dodecahedronPoint = DodecahedronProjection.ForwardCartesian(cartesian, origin.Id);

        return FaceToEstimate(dodecahedronPoint, origin, resolution);
    }

    /// <summary>
    /// Discretizes a face-coordinate point into its estimate cell: the quintant from its polar angle,
    /// then — for Hilbert resolutions — a rotation into the canonical (quintant 0) orientation, a scale
    /// to the target resolution's lattice, and the lattice-basis conversion down to a Hilbert curve
    /// position.
    /// </summary>
    private static A5Cell FaceToEstimate(Face dodecahedronPoint, Origin origin, int resolution)
    {
        Polar polar = CoordinateTransforms.ToPolar(dodecahedronPoint);
        int quintant = Tiling.GetQuintantPolar(polar);
        QuintantSegment quintantSegment = Origins.QuintantToSegment(quintant, origin);

        if(resolution < Serialization.FirstHilbertResolution)
        {
            // For low resolutions there is no Hilbert curve.
            return new A5Cell(origin, quintantSegment.Segment, 0UL, resolution);
        }

        // Rotate into the right fifth.
        if(quintant != 0)
        {
            double extraAngle = 2 * Constants.PiOver5 * quintant;
            Matrix2x2d rotation = Matrix2x2d.FromRotation(-extraAngle);
            Vector2d rotated = rotation.Transform(CoordinateConversions.ToVector2d(dodecahedronPoint));
            dodecahedronPoint = CoordinateConversions.ToFace(rotated);
        }

        int hilbertResolution = 1 + resolution - Serialization.FirstHilbertResolution;
        Vector2d scaled = CoordinateConversions.ToVector2d(dodecahedronPoint) * Math.Pow(2, hilbertResolution);
        dodecahedronPoint = CoordinateConversions.ToFace(scaled);

        IJ ij = CoordinateTransforms.FaceToIJ(dodecahedronPoint);
        ulong s = HilbertCurve.IJToS(ij, hilbertResolution, quintantSegment.Orientation);

        return new A5Cell(origin, quintantSegment.Segment, s, resolution);
    }

    /// <summary>
    /// Builds the exact pentagon shape (in face coordinates) a decoded cell describes. Kept private to
    /// this pipeline for now; future traversal/regions work may promote it if it needs direct access.
    /// </summary>
    private static PentagonShape GetPentagon(A5Cell cell)
    {
        SegmentQuintant segmentQuintant = Origins.SegmentToQuintant(cell.Segment, cell.Origin);
        if(cell.Resolution == Serialization.FirstHilbertResolution - 1)
        {
            return Tiling.GetQuintantVertices(segmentQuintant.Quintant);
        }

        if(cell.Resolution == Serialization.FirstHilbertResolution - 2)
        {
            return Tiling.GetFaceVertices();
        }

        int hilbertResolution = cell.Resolution - Serialization.FirstHilbertResolution + 1;
        Anchor anchor = HilbertCurve.SToAnchor(cell.S, hilbertResolution, segmentQuintant.Orientation);

        return Tiling.GetPentagonVertices(hilbertResolution, segmentQuintant.Quintant, anchor);
    }

    /// <summary>
    /// Like <see cref="GetPentagon"/>, but writes the vertices into a caller-provided
    /// <paramref name="destination"/> instead of allocating a <see cref="PentagonShape"/> — the
    /// containment hot path (<see cref="A5CellContainsPoint"/>) uses this exclusively so no per-candidate
    /// <see cref="PentagonShape"/> or <c>Face[]</c> escapes the stack.
    /// </summary>
    /// <returns>The number of vertices written to <paramref name="destination"/> (see <see cref="Tiling.MaxVertexCount"/>).</returns>
    private static int FillPentagonVertices(A5Cell cell, Span<Face> destination)
    {
        SegmentQuintant segmentQuintant = Origins.SegmentToQuintant(cell.Segment, cell.Origin);
        if(cell.Resolution == Serialization.FirstHilbertResolution - 1)
        {
            return Tiling.FillQuintantVertices(destination, segmentQuintant.Quintant);
        }

        if(cell.Resolution == Serialization.FirstHilbertResolution - 2)
        {
            return Tiling.FillFaceVertices(destination);
        }

        int hilbertResolution = cell.Resolution - Serialization.FirstHilbertResolution + 1;
        Anchor anchor = HilbertCurve.SToAnchor(cell.S, hilbertResolution, segmentQuintant.Orientation);

        return Tiling.FillPentagonVertices(destination, hilbertResolution, segmentQuintant.Quintant, anchor);
    }

    /// <summary>A candidate cell found during the spiral/neighbor fallback search, paired with its containment distance.</summary>
    private readonly record struct CellDistanceCandidate(ulong CellId, double Distance);
}
