using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Buffer's winding-depth accumulator — a different label algebra over the same
/// noded graph as overlay, kept as its own accumulator. Every raw-curve edge
/// contributes +1 to the depth on its travel-left side; merged coincident edges
/// sum their contributions into one signed delta per canonical edge. The graph
/// splits into connected subgraphs processed in descending rightmost-extreme
/// order: the sector facing the open positive-X ray at a subgraph's extreme node
/// seeds from the depth the already-resolved subgraphs accumulate there — only
/// the globally rightmost subgraph seeds at the known outside depth of zero —
/// and depths propagate breadth-first around node sectors, iteratively, never
/// recursively. The covered region is everywhere the depth is positive; an edge
/// bounds the result exactly when its sides disagree about positivity.
/// </summary>
internal sealed class BufferDepth
{
    /// <summary>The per-edge summed contribution: canonical left depth minus right depth.</summary>
    private Dictionary<OverlayEdge, int> Delta { get; } = [];

    /// <summary>The resolved canonical-left depth per edge.</summary>
    private Dictionary<OverlayEdge, int> LeftDepth { get; } = [];

    /// <summary>
    /// Resolves every edge's depth and marks the result-boundary edges on the
    /// graph. False means a depth contradiction — an inconsistent represented
    /// arrangement, the honest refusal path.
    /// </summary>
    public bool TryResolve(OverlayGraph graph)
    {
        foreach(OverlayEdge edge in graph.Edges)
        {
            int delta = 0;
            List<OverlayBoundaryVote>? votes = edge.Votes[0];

            if(votes is not null)
            {
                foreach(OverlayBoundaryVote vote in votes)
                {
                    delta += vote.InteriorOnLeft ? 1 : -1;
                }
            }

            Delta[edge] = delta;
        }

        foreach(List<OverlayEdge> subgraph in ConnectedSubgraphs(graph))
        {
            Point2d extreme = ExtremeNode(subgraph);
            int seed = DepthAt(extreme);

            if(!TryPropagateSubgraph(graph, subgraph, extreme, seed))
            {
                return false;
            }
        }

        foreach(OverlayEdge edge in graph.Edges)
        {
            int left = LeftDepth[edge];
            int right = left - Delta[edge];
            edge.InAreaResult = (left > 0) != (right > 0);
            edge.ResultForward = left > 0;
        }

        return true;
    }

    /// <summary>
    /// The connected components of the graph, each ordered internally by
    /// discovery; components emit in descending extreme-node order so depths
    /// resolve outside-in.
    /// </summary>
    private static List<List<OverlayEdge>> ConnectedSubgraphs(OverlayGraph graph)
    {
        var visited = new HashSet<OverlayEdge>();
        var subgraphs = new List<List<OverlayEdge>>();

        foreach(OverlayEdge seed in graph.Edges)
        {
            if(!visited.Add(seed))
            {
                continue;
            }

            var component = new List<OverlayEdge> { seed };
            var frontier = new Queue<OverlayEdge>();
            frontier.Enqueue(seed);

            while(frontier.Count > 0)
            {
                OverlayEdge edge = frontier.Dequeue();

                foreach(Point2d node in (Span<Point2d>)[edge.Start, edge.End])
                {
                    foreach(OverlayEdge neighbor in graph.StarAt(node))
                    {
                        if(visited.Add(neighbor))
                        {
                            component.Add(neighbor);
                            frontier.Enqueue(neighbor);
                        }
                    }
                }
            }

            subgraphs.Add(component);
        }

        subgraphs.Sort((first, second) =>
        {
            Point2d firstExtreme = ExtremeNode(first);
            Point2d secondExtreme = ExtremeNode(second);

            return -OverlayGraph.ComparePoints(firstExtreme, secondExtreme);
        });

        return subgraphs;
    }

    /// <summary>The component's extreme node: maximal X, then maximal Y.</summary>
    private static Point2d ExtremeNode(List<OverlayEdge> component)
    {
        Point2d extreme = component[0].Start;

        foreach(OverlayEdge edge in component)
        {
            foreach(Point2d node in (Span<Point2d>)[edge.Start, edge.End])
            {
                if(node.X > extreme.X || (node.X == extreme.X && node.Y > extreme.Y))
                {
                    extreme = node;
                }
            }
        }

        return extreme;
    }

    /// <summary>
    /// The depth of the plane at a point, summed from the already-resolved edges
    /// crossing the open positive-X ray: an upward crossing contributes its delta,
    /// a downward one subtracts it — the plane at infinity is depth zero.
    /// </summary>
    private int DepthAt(Point2d point)
    {
        int depth = 0;

        foreach(KeyValuePair<OverlayEdge, int> resolved in LeftDepth)
        {
            OverlayEdge edge = resolved.Key;
            Point2d start = edge.Start;
            Point2d end = edge.End;
            int delta = Delta[edge];

            if(delta == 0 || start.Y == end.Y)
            {
                continue;
            }

            bool upward = end.Y > start.Y;
            Point2d low = upward ? start : end;
            Point2d high = upward ? end : start;

            if(!(low.Y <= point.Y && point.Y < high.Y))
            {
                continue;
            }

            double crossingX = low.X + ((high.X - low.X) * ((point.Y - low.Y) / (high.Y - low.Y)));

            if(crossingX > point.X)
            {
                depth += upward ? delta : -delta;
            }
        }

        return depth;
    }

    /// <summary>
    /// Breadth-first depth propagation over one subgraph from its extreme node,
    /// whose positive-X-facing wrap sector carries the seed depth.
    /// </summary>
    private bool TryPropagateSubgraph(OverlayGraph graph, List<OverlayEdge> component, Point2d extreme, int seed)
    {
        var componentSet = new HashSet<OverlayEdge>(component);
        var pendingNodes = new Queue<Point2d>();
        var knownSectors = new Dictionary<(double X, double Y), int>
        {
            [(extreme.X, extreme.Y)] = seed,
        };
        pendingNodes.Enqueue(extreme);

        while(pendingNodes.Count > 0)
        {
            Point2d node = pendingNodes.Dequeue();
            List<OverlayEdge> star = graph.StarAt(node);
            int sector;
            int anchor;

            if(knownSectors.TryGetValue((node.X, node.Y), out int wrapDepth))
            {
                //The recorded value is the wrap sector between the star's last
                //and first edges in counter-clockwise order.
                sector = wrapDepth;
                anchor = star.Count - 1;
                knownSectors.Remove((node.X, node.Y));
            }
            else
            {
                anchor = -1;
                sector = 0;

                for(int index = 0; index < star.Count; index++)
                {
                    if(TryOutgoingLeft(star[index], node, out int known))
                    {
                        anchor = index;
                        sector = known;

                        break;
                    }
                }

                if(anchor < 0)
                {
                    continue;
                }
            }

            for(int step = 1; step <= star.Count; step++)
            {
                OverlayEdge edge = star[(anchor + step) % star.Count];

                if(!componentSet.Contains(edge))
                {
                    continue;
                }

                int deltaOut = OutgoingDelta(edge, node);

                if(TryOutgoingRight(edge, node, out int knownRight))
                {
                    if(knownRight != sector)
                    {
                        return false;
                    }
                }
                else
                {
                    SetOutgoing(edge, node, sector + deltaOut);
                    Point2d far = edge.Opposite(node);
                    pendingNodes.Enqueue(far);
                }

                sector = OutgoingLeftValue(edge, node);
            }
        }

        //Every component edge must have resolved: an unreached edge would mean a
        //disconnected piece inside its own component, which cannot happen.
        foreach(OverlayEdge edge in component)
        {
            if(!LeftDepth.ContainsKey(edge))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The edge's delta in its outgoing frame at the node.</summary>
    private int OutgoingDelta(OverlayEdge edge, Point2d node)
    {
        bool atStart = node.X == edge.Start.X && node.Y == edge.Start.Y;

        return atStart ? Delta[edge] : -Delta[edge];
    }

    /// <summary>Reads the outgoing-frame left depth when resolved.</summary>
    private bool TryOutgoingLeft(OverlayEdge edge, Point2d node, out int depth)
    {
        if(!LeftDepth.TryGetValue(edge, out int left))
        {
            depth = 0;

            return false;
        }

        bool atStart = node.X == edge.Start.X && node.Y == edge.Start.Y;
        depth = atStart ? left : left - Delta[edge];

        return true;
    }

    /// <summary>Reads the outgoing-frame right depth when resolved.</summary>
    private bool TryOutgoingRight(OverlayEdge edge, Point2d node, out int depth)
    {
        if(!LeftDepth.TryGetValue(edge, out int left))
        {
            depth = 0;

            return false;
        }

        bool atStart = node.X == edge.Start.X && node.Y == edge.Start.Y;
        depth = atStart ? left - Delta[edge] : left;

        return true;
    }

    /// <summary>The outgoing-frame left depth of a resolved edge.</summary>
    private int OutgoingLeftValue(OverlayEdge edge, Point2d node)
    {
        TryOutgoingLeft(edge, node, out int depth);

        return depth;
    }

    /// <summary>Stores an outgoing-frame left depth in canonical terms.</summary>
    private void SetOutgoing(OverlayEdge edge, Point2d node, int outgoingLeft)
    {
        bool atStart = node.X == edge.Start.X && node.Y == edge.Start.Y;
        LeftDepth[edge] = atStart ? outgoingLeft : outgoingLeft + Delta[edge];
    }
}
