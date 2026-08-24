using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The convex hull of any operand: the smallest convex point set containing every
/// position, computed kind-blind over the flat vertex column, so every kind —
/// collections at any depth included — is a defined operand and the function is
/// total. The algorithm is the monotone chain over the
/// lexicographically sorted distinct positions with every turn decided by the exact
/// orientation, so collinear mid-edge positions are excluded as a clean binary
/// choice, never a tolerance heuristic. Degenerate operands collapse per the
/// envelope family's convention: empty input answers the empty point, one distinct
/// position a point, a collinear set the two-position linestring of its extremes,
/// and everything else a counter-clockwise single-ring polygon starting at its
/// lexicographic minimum. Results are planar XY and never alias operand columns.
/// </summary>
public static class GeometryConvexHull
{
    /// <summary>
    /// Computes the convex hull of the operand. Total: every operand, including
    /// every typed empty, every collection, and <c>default</c>, has a defined hull.
    /// </summary>
    public static FlatGeometry Compute(in FlatGeometry geometry)
    {
        var hull = new List<Point2d>();
        ComputeHullVertices(in geometry, hull);

        if(hull.Count == 0)
        {
            return FlatGeometry.Empty(GeometryKind.Point);
        }

        if(hull.Count == 1)
        {
            return FlatGeometryFactory.CreatePoint(hull[0]);
        }

        if(hull.Count == 2)
        {
            Span<Point2d> extremes = [hull[0], hull[1]];

            return FlatGeometryFactory.CreateLineString(extremes);
        }

        var ring = new Point2d[hull.Count + 1];

        for(int index = 0; index < hull.Count; index++)
        {
            ring[index] = hull[index];
        }

        ring[hull.Count] = hull[0];

        return FlatGeometryFactory.CreatePolygon([ring]);
    }

    /// <summary>
    /// Fills <paramref name="hull"/> — cleared first, the caller owns and may reuse
    /// it — with the operand's convex hull as the open counter-clockwise cycle
    /// starting at its lexicographic minimum. The count is the contract consumers
    /// key their collapses on: 0 for an empty operand, 1 for a single distinct
    /// position, 2 extremes for a collinear operand, and otherwise the full cycle
    /// without its closing duplicate. Every consecutive triple of a cycle of three
    /// or more is a strict left turn — <see cref="AppendWithTurns"/> pops on a
    /// non-positive <see cref="ExactOrientation.Orient2D"/> — and downstream key
    /// algebra (the bounding-circle walk) is defined only under that guarantee, so
    /// it is contract here, not implementation detail. The chain never runs below
    /// two distinct positions: <see cref="BuildChain"/>'s trailing removal requires
    /// at least two candidates, so the sub-two counts short-circuit to the
    /// candidate list itself. Positions are deduplicated by value equality with
    /// signed zeros canonicalized, per <see cref="CollectDistinctSorted"/>.
    /// </summary>
    internal static void ComputeHullVertices(in FlatGeometry geometry, List<Point2d> hull)
    {
        hull.Clear();
        List<Point2d> candidates = CollectDistinctSorted(geometry.Vertices);

        if(candidates.Count < 2)
        {
            hull.AddRange(candidates);

            return;
        }

        BuildChain(candidates, hull);
    }

    /// <summary>
    /// Collects the operand's positions deduplicated by value equality on the
    /// ordinates and sorted lexicographically (X, then Y). Signed zeros
    /// canonicalize to positive zero — hull output is constructed, and the
    /// canonical form keeps emission bitwise deterministic regardless of which
    /// zero an operand carried. Internal because it is the one implementation of
    /// the canonical candidate pipeline: the concave hull's triangulation consumes
    /// the same list, and a second independently-written dedup or sort would be a
    /// bitwise-divergence risk between the two hull surfaces.
    /// </summary>
    internal static List<Point2d> CollectDistinctSorted(ReadOnlySpan<Point2d> vertices)
    {
        var candidates = new List<Point2d>(vertices.Length);

        foreach(Point2d vertex in vertices)
        {
            candidates.Add(new Point2d(vertex.X + 0.0, vertex.Y + 0.0));
        }

        candidates.Sort(CompareLexicographic);

        int keep = 0;

        for(int index = 0; index < candidates.Count; index++)
        {
            if(keep > 0 && candidates[index].X == candidates[keep - 1].X && candidates[index].Y == candidates[keep - 1].Y)
            {
                continue;
            }

            candidates[keep] = candidates[index];
            keep++;
        }

        candidates.RemoveRange(keep, candidates.Count - keep);

        return candidates;
    }

    /// <summary>
    /// The two monotone chains over the sorted candidates: the lower chain left to
    /// right, the upper chain right to left, popping on every non-left turn so the
    /// concatenation is the counter-clockwise hull starting at the lexicographic
    /// minimum, collinear mid-edge positions excluded. Requires at least two
    /// candidates: the trailing removal of the wrapped-around start deletes the
    /// sole element of a one-candidate chain and has nothing to remove below that
    /// — <see cref="ComputeHullVertices"/> owns the short-circuit.
    /// </summary>
    private static void BuildChain(List<Point2d> sorted, List<Point2d> hull)
    {
        foreach(Point2d candidate in sorted)
        {
            AppendWithTurns(hull, candidate, floor: 2);
        }

        int lowerCount = hull.Count;

        for(int index = sorted.Count - 2; index >= 0; index--)
        {
            AppendWithTurns(hull, sorted[index], floor: lowerCount + 1);
        }

        hull.RemoveAt(hull.Count - 1);
    }

    /// <summary>
    /// Appends one candidate to the growing chain, first popping every tail vertex
    /// that would make a non-left turn; the floor keeps each chain from consuming
    /// the other's anchor.
    /// </summary>
    private static void AppendWithTurns(List<Point2d> chain, Point2d candidate, int floor)
    {
        while(chain.Count >= floor
            && ExactOrientation.Orient2D(chain[chain.Count - 2], chain[chain.Count - 1], candidate) <= 0)
        {
            chain.RemoveAt(chain.Count - 1);
        }

        chain.Add(candidate);
    }

    /// <summary>Lexicographic position order: X first, then Y, on plain value comparison.</summary>
    private static int CompareLexicographic(Point2d first, Point2d second)
    {
        if(first.X < second.X)
        {
            return -1;
        }

        if(first.X > second.X)
        {
            return 1;
        }

        if(first.Y < second.Y)
        {
            return -1;
        }

        if(first.Y > second.Y)
        {
            return 1;
        }

        return 0;
    }
}
