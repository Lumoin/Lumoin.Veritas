using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The labeling half of the overlay engine: contributed edges resolve from their
/// accumulated votes (the collapsed-slit rule reading parent ring role where a
/// same-part pair of opposite votes cancels), lineal operands resolve to exterior
/// sides outright, and areal side regions propagate around node sectors and along
/// connected components, with the exact point locator as the fallback for
/// components that touch no edge of the operand. A label contradiction means the
/// represented arrangement is inconsistent and the operation refuses honestly.
/// </summary>
internal static class OverlayLabeling
{
    /// <summary>
    /// Resolves every edge's per-operand side regions. False means a label
    /// contradiction — the honest refusal path.
    /// </summary>
    public static bool TryResolve(OverlayGraph graph, RelateShape firstShape, RelateShape secondShape)
    {
        foreach(OverlayEdge edge in graph.Edges)
        {
            ResolveVotes(edge, operand: 0);
            ResolveVotes(edge, operand: 1);
        }

        return TryResolveOperand(graph, operand: 0, firstShape)
            && TryResolveOperand(graph, operand: 1, secondShape);
    }

    /// <summary>
    /// Resolves one contributed edge's sides for one operand from its votes: a
    /// same-part pair of opposite votes is the collapsed slit and yields the
    /// parent role's location; any surviving interior vote raises its side.
    /// </summary>
    private static void ResolveVotes(OverlayEdge edge, int operand)
    {
        List<OverlayBoundaryVote>? votes = edge.Votes[operand];

        if(votes is null)
        {
            return;
        }

        bool leftInterior = false;
        bool rightInterior = false;

        for(int index = 0; index < votes.Count; index++)
        {
            OverlayBoundaryVote vote = votes[index];
            bool collapsedPair = false;

            for(int other = 0; other < votes.Count; other++)
            {
                if(other != index
                    && votes[other].Part == vote.Part
                    && votes[other].InteriorOnLeft != vote.InteriorOnLeft)
                {
                    collapsedPair = true;

                    break;
                }
            }

            if(collapsedPair)
            {
                //The collapsed arm: a shell's slit lies in its exterior on both
                //sides (no raise), a hole's in the polygon interior on both.
                if(vote.IsHoleRing)
                {
                    leftInterior = true;
                    rightInterior = true;
                }

                continue;
            }

            if(vote.InteriorOnLeft)
            {
                leftInterior = true;
            }
            else
            {
                rightInterior = true;
            }
        }

        edge.Left[operand] = leftInterior ? OverlaySideRegion.Interior : OverlaySideRegion.Exterior;
        edge.Right[operand] = rightInterior ? OverlaySideRegion.Interior : OverlaySideRegion.Exterior;
    }

    /// <summary>
    /// Resolves the not-contributed edges for one operand: exterior everywhere for
    /// a non-areal operand, sector propagation plus the component fallback for an
    /// areal one.
    /// </summary>
    private static bool TryResolveOperand(OverlayGraph graph, int operand, RelateShape shape)
    {
        if(!shape.IsAreal)
        {
            foreach(OverlayEdge edge in graph.Edges)
            {
                if(edge.Left[operand] == OverlaySideRegion.Unknown)
                {
                    edge.Left[operand] = OverlaySideRegion.Exterior;
                    edge.Right[operand] = OverlaySideRegion.Exterior;
                }
            }

            return true;
        }

        if(!TryPropagate(graph, operand))
        {
            return false;
        }

        ResolveByLocator(graph, operand, shape);

        return true;
    }

    /// <summary>
    /// Breadth-first sector propagation: at each node the sorted star is walked
    /// counter-clockwise from an edge with a known sector value; every sector
    /// assigns the facing side of the next edge, unknown edges take both sides and
    /// carry the value to their far node. A conflicting assignment is a
    /// contradiction and fails the operation.
    /// </summary>
    private static bool TryPropagate(OverlayGraph graph, int operand)
    {
        var queue = new Queue<(double X, double Y)>();

        foreach(KeyValuePair<(double X, double Y), List<OverlayEdge>> star in graph.Stars)
        {
            queue.Enqueue(star.Key);
        }

        while(queue.Count > 0)
        {
            (double X, double Y) key = queue.Dequeue();
            var node = new Point2d(key.X, key.Y);
            List<OverlayEdge> star = graph.Stars[key];
            int anchor = -1;

            for(int index = 0; index < star.Count; index++)
            {
                if(OutgoingLeft(star[index], node, operand) != OverlaySideRegion.Unknown)
                {
                    anchor = index;

                    break;
                }
            }

            if(anchor < 0)
            {
                continue;
            }

            OverlaySideRegion sector = OutgoingLeft(star[anchor], node, operand);

            for(int step = 1; step <= star.Count; step++)
            {
                OverlayEdge edge = star[(anchor + step) % star.Count];
                OverlaySideRegion right = OutgoingRight(edge, node, operand);

                if(right == OverlaySideRegion.Unknown)
                {
                    SetBothSides(edge, operand, sector);
                    Point2d far = edge.Opposite(node);
                    queue.Enqueue((far.X, far.Y));
                }
                else if(right != sector)
                {
                    return false;
                }

                sector = OutgoingLeft(edge, node, operand);
            }
        }

        return true;
    }

    /// <summary>
    /// The component fallback: every edge still unknown for the operand belongs to
    /// a connected component that touches no edge of it, so location is constant
    /// across the component and one representative original node — the smallest,
    /// for determinism — speaks for the whole.
    /// </summary>
    private static void ResolveByLocator(OverlayGraph graph, int operand, RelateShape shape)
    {
        var visited = new HashSet<OverlayEdge>();

        foreach(OverlayEdge seed in graph.Edges)
        {
            if(seed.Left[operand] != OverlaySideRegion.Unknown || !visited.Add(seed))
            {
                continue;
            }

            var component = new List<OverlayEdge> { seed };
            var frontier = new Queue<OverlayEdge>();
            frontier.Enqueue(seed);
            Point2d representative = default;
            bool hasRepresentative = false;

            while(frontier.Count > 0)
            {
                OverlayEdge edge = frontier.Dequeue();

                foreach(Point2d node in (Span<Point2d>)[edge.Start, edge.End])
                {
                    if(graph.OriginalNodes.Contains((node.X, node.Y))
                        && (!hasRepresentative || OverlayGraph.ComparePoints(node, representative) < 0))
                    {
                        representative = node;
                        hasRepresentative = true;
                    }

                    foreach(OverlayEdge neighbor in graph.StarAt(node))
                    {
                        if(neighbor.Left[operand] == OverlaySideRegion.Unknown && visited.Add(neighbor))
                        {
                            component.Add(neighbor);
                            frontier.Enqueue(neighbor);
                        }
                    }
                }
            }

            if(!hasRepresentative)
            {
                representative = seed.Start;
            }

            //Boundary cannot answer here — a component touching the operand would
            //carry its edges — but the guarded mapping keeps a represented-
            //coordinate surprise inside the recorded best-effort posture.
            PointPlacement placement = shape.Locate(representative);
            OverlaySideRegion region = placement == PointPlacement.Exterior
                ? OverlaySideRegion.Exterior
                : OverlaySideRegion.Interior;

            foreach(OverlayEdge edge in component)
            {
                SetBothSides(edge, operand, region);
            }
        }
    }

    /// <summary>The edge's left side for the operand in its outgoing frame at the node.</summary>
    private static OverlaySideRegion OutgoingLeft(OverlayEdge edge, Point2d node, int operand)
    {
        bool atStart = node.X == edge.Start.X && node.Y == edge.Start.Y;

        return atStart ? edge.Left[operand] : edge.Right[operand];
    }

    /// <summary>The edge's right side for the operand in its outgoing frame at the node.</summary>
    private static OverlaySideRegion OutgoingRight(OverlayEdge edge, Point2d node, int operand)
    {
        bool atStart = node.X == edge.Start.X && node.Y == edge.Start.Y;

        return atStart ? edge.Right[operand] : edge.Left[operand];
    }

    /// <summary>Assigns both canonical sides of a not-contributed edge.</summary>
    private static void SetBothSides(OverlayEdge edge, int operand, OverlaySideRegion region)
    {
        edge.Left[operand] = region;
        edge.Right[operand] = region;
    }
}
