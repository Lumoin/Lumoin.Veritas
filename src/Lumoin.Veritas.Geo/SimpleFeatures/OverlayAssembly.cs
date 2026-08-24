using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>The four boolean overlay operations.</summary>
internal enum OverlayOperation
{
    /// <summary>Points in both operands.</summary>
    Intersection = 0,

    /// <summary>Points in either operand.</summary>
    Union = 1,

    /// <summary>Points in the first operand and not the second.</summary>
    Difference = 2,

    /// <summary>Points in exactly one operand.</summary>
    SymDifference = 3,
}

/// <summary>The extracted result pieces of one graph overlay, canonicalized and sorted.</summary>
internal sealed class OverlayResultPieces
{
    /// <summary>The polygons: per polygon a closed shell ring then its closed hole rings.</summary>
    public List<List<Point2d[]>> Polygons { get; } = [];

    /// <summary>The sewn line runs.</summary>
    public List<Point2d[]> Lines { get; } = [];

    /// <summary>The isolated points.</summary>
    public List<Point2d> Points { get; } = [];
}

/// <summary>
/// The extraction half of the overlay engine. The selection rule is the one
/// per-operation arithmetic: a side is in the result under the operation's boolean
/// over the two operands' side regions (on-ness counting as inside for edge and
/// point membership); an area edge is a result boundary exactly when its sides
/// disagree, directed with the result interior on its left. Faces walk directly
/// into minimal rings — at each node the next edge is the first outgoing result
/// edge clockwise from the arrival's reverse, the tightest continuation keeping
/// the interior left, so self-touching boundaries decompose per polygon topology
/// semantics without a separate pass. Holes assign to their innermost enclosing
/// shell through the exact ring probe; lines sew node-to-node into maximal runs;
/// isolated result points are the nodes covered by no emitted edge. Everything
/// emitted is canonicalized: shells counter-clockwise, holes clockwise, rings
/// rotated to their lexicographically smallest vertex, members sorted.
/// </summary>
internal static class OverlayAssembly
{
    /// <summary>
    /// Extracts the operation's result pieces from the labeled graph. False means
    /// the walk or hole assignment met an inconsistent arrangement — the honest
    /// refusal path.
    /// </summary>
    public static bool TryExtract(OverlayGraph graph, OverlayOperation operation, out OverlayResultPieces pieces)
    {
        pieces = new OverlayResultPieces();

        foreach(OverlayEdge edge in graph.Edges)
        {
            bool leftMember = Member(operation, edge.Left[0] == OverlaySideRegion.Interior, edge.Left[1] == OverlaySideRegion.Interior);
            bool rightMember = Member(operation, edge.Right[0] == OverlaySideRegion.Interior, edge.Right[1] == OverlaySideRegion.Interior);
            edge.InAreaResult = leftMember != rightMember;
            edge.ResultForward = leftMember;
        }

        foreach(OverlayEdge edge in graph.Edges)
        {
            if(edge.InAreaResult)
            {
                continue;
            }

            bool inFirst = edge.IsOn[0] || edge.Left[0] == OverlaySideRegion.Interior;
            bool inSecond = edge.IsOn[1] || edge.Left[1] == OverlaySideRegion.Interior;
            bool leftMember = Member(operation, edge.Left[0] == OverlaySideRegion.Interior, edge.Left[1] == OverlaySideRegion.Interior);

            edge.InLineResult = Member(operation, inFirst, inSecond) && !leftMember;
        }

        if(!TryExtractMarkedAreas(graph, pieces))
        {
            return false;
        }

        SewLines(graph, pieces);
        CollectPoints(graph, operation, pieces);

        return true;
    }

    /// <summary>
    /// Extracts the area pieces from a graph whose edges already carry their
    /// result marks — the shared tail behind overlay's label selection and
    /// buffer's depth threshold.
    /// </summary>
    public static bool TryExtractMarkedAreas(OverlayGraph graph, OverlayResultPieces pieces)
    {
        if(!TryWalkRings(graph, out List<List<Point2d>> shells, out List<List<Point2d>> holes))
        {
            return false;
        }

        return TryAssignHoles(shells, holes, pieces);
    }

    /// <summary>The operation's boolean over the two operands' memberships.</summary>
    public static bool Member(OverlayOperation operation, bool inFirst, bool inSecond)
    {
        return operation switch
        {
            OverlayOperation.Intersection => inFirst && inSecond,
            OverlayOperation.Union => inFirst || inSecond,
            OverlayOperation.Difference => inFirst && !inSecond,
            OverlayOperation.SymDifference => inFirst ^ inSecond,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    /// <summary>
    /// Walks every directed result-boundary edge into its minimal ring: from the
    /// arrival node the next edge is the first outgoing result edge clockwise from
    /// the arrival's reverse in the exact-sorted star. Positive rings are shells,
    /// negative rings holes; degenerate rings prune.
    /// </summary>
    private static bool TryWalkRings(OverlayGraph graph, out List<List<Point2d>> shells, out List<List<Point2d>> holes)
    {
        shells = [];
        holes = [];
        int totalDirected = 0;

        foreach(OverlayEdge edge in graph.Edges)
        {
            if(edge.InAreaResult)
            {
                totalDirected++;
            }
        }

        foreach(OverlayEdge start in graph.Edges)
        {
            if(!start.InAreaResult || DirectedVisited(start))
            {
                continue;
            }

            var ring = new List<Point2d>();
            OverlayEdge edge = start;
            Point2d from = ResultOrigin(edge);
            int guard = totalDirected + 1;

            while(guard-- > 0)
            {
                MarkDirectedVisited(edge);
                ring.Add(from);
                Point2d node = edge.Opposite(from);

                if(!TryNextClockwise(graph, edge, node, out OverlayEdge next))
                {
                    return false;
                }

                if(next == start)
                {
                    break;
                }

                edge = next;
                from = node;
            }

            if(guard < 0)
            {
                return false;
            }

            int orientation = RingGeometry.Orientation(CloseRing(ring));

            if(orientation > 0)
            {
                shells.Add(ring);
            }
            else if(orientation < 0)
            {
                holes.Add(ring);
            }
        }

        return true;
    }

    /// <summary>
    /// The node the edge's directed result version leaves from: the canonical
    /// start when the canonical left side is the member side.
    /// </summary>
    private static Point2d ResultOrigin(OverlayEdge edge)
    {
        return edge.ResultForward ? edge.Start : edge.End;
    }

    /// <summary>Whether the edge's single directed result version was walked.</summary>
    private static bool DirectedVisited(OverlayEdge edge)
    {
        return edge.ResultForward ? edge.ForwardVisited : edge.BackwardVisited;
    }

    /// <summary>Marks the edge's directed result version walked.</summary>
    private static void MarkDirectedVisited(OverlayEdge edge)
    {
        if(edge.ResultForward)
        {
            edge.ForwardVisited = true;
        }
        else
        {
            edge.BackwardVisited = true;
        }
    }

    /// <summary>
    /// Finds the continuation at a node: scanning clockwise (descending in the
    /// counter-clockwise star order) from the arrival edge, the first result edge
    /// whose directed version leaves this node. In a consistent arrangement the
    /// continuation is unique, so the walk revisits an edge only when it closes at
    /// its start — the caller's termination test; the guard counter catches the
    /// inconsistent remainder.
    /// </summary>
    private static bool TryNextClockwise(OverlayGraph graph, OverlayEdge arrival, Point2d node, out OverlayEdge next)
    {
        List<OverlayEdge> star = graph.StarAt(node);
        int arrivalIndex = star.IndexOf(arrival);

        for(int step = 1; step <= star.Count; step++)
        {
            OverlayEdge candidate = star[(arrivalIndex - step + (step * star.Count)) % star.Count];

            if(!candidate.InAreaResult)
            {
                continue;
            }

            Point2d origin = ResultOrigin(candidate);

            if(origin.X == node.X && origin.Y == node.Y)
            {
                next = candidate;

                return true;
            }
        }

        next = arrival;

        return false;
    }

    /// <summary>
    /// Assigns every hole to its innermost enclosing shell — among the shells
    /// containing the hole, the one of smallest ring area, ties broken by the
    /// smaller canonical start vertex — and emits canonicalized polygons sorted by
    /// shell start vertex. A parentless hole is an inconsistent arrangement.
    /// </summary>
    private static bool TryAssignHoles(List<List<Point2d>> shells, List<List<Point2d>> holes, OverlayResultPieces pieces)
    {
        var shellRings = new List<(Point2d[] Closed, double Area, Point2d MinVertex, List<Point2d[]> Holes)>();

        foreach(List<Point2d> shell in shells)
        {
            Point2d[] closed = CanonicalizeRing(shell, wantCounterClockwise: true);
            shellRings.Add((closed, RingAreaMagnitude(closed), closed[0], []));
        }

        foreach(List<Point2d> hole in holes)
        {
            Point2d[] closed = CanonicalizeRing(hole, wantCounterClockwise: false);
            int chosen = -1;

            for(int index = 0; index < shellRings.Count; index++)
            {
                if(!RingContains(shellRings[index].Closed, closed))
                {
                    continue;
                }

                if(chosen < 0
                    || shellRings[index].Area < shellRings[chosen].Area
                    || (shellRings[index].Area == shellRings[chosen].Area
                        && OverlayGraph.ComparePoints(shellRings[index].MinVertex, shellRings[chosen].MinVertex) < 0))
                {
                    chosen = index;
                }
            }

            if(chosen < 0)
            {
                return false;
            }

            shellRings[chosen].Holes.Add(closed);
        }

        shellRings.Sort((first, second) => OverlayGraph.ComparePoints(first.MinVertex, second.MinVertex));

        foreach((Point2d[] closed, double _, Point2d _, List<Point2d[]> shellHoles) in shellRings)
        {
            shellHoles.Sort((first, second) => OverlayGraph.ComparePoints(first[0], second[0]));
            var rings = new List<Point2d[]>(shellHoles.Count + 1) { closed };
            rings.AddRange(shellHoles);
            pieces.Polygons.Add(rings);
        }

        return true;
    }

    /// <summary>
    /// Whether the shell ring contains the hole ring: any hole vertex strictly
    /// exterior refutes containment; otherwise the hole is inside or touching,
    /// and both belong to the shell.
    /// </summary>
    private static bool RingContains(Point2d[] shell, Point2d[] hole)
    {
        for(int index = 0; index < hole.Length - 1; index++)
        {
            if(RingGeometry.LocateInRing(hole[index], shell) == PointPlacement.Exterior)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Sews the line-result edges into maximal node-to-node runs, canonicalized and sorted.</summary>
    private static void SewLines(OverlayGraph graph, OverlayResultPieces pieces)
    {
        var adjacency = new Dictionary<(double X, double Y), List<OverlayEdge>>();

        foreach(OverlayEdge edge in graph.Edges)
        {
            if(!edge.InLineResult)
            {
                continue;
            }

            AttachLine(adjacency, edge.Start, edge);
            AttachLine(adjacency, edge.End, edge);
        }

        var visited = new HashSet<OverlayEdge>();
        var runs = new List<Point2d[]>();

        //Open runs first, seeded at nodes whose line degree is not two, then any
        //remaining closed loops.
        for(int pass = 0; pass < 2; pass++)
        {
            foreach(KeyValuePair<(double X, double Y), List<OverlayEdge>> entry in adjacency)
            {
                if(pass == 0 && entry.Value.Count == 2)
                {
                    continue;
                }

                foreach(OverlayEdge seed in entry.Value)
                {
                    if(visited.Contains(seed))
                    {
                        continue;
                    }

                    runs.Add(WalkRun(adjacency, visited, new Point2d(entry.Key.X, entry.Key.Y), seed));
                }
            }
        }

        runs.Sort(CompareRuns);
        pieces.Lines.AddRange(runs);
    }

    /// <summary>Walks one line run from a seed node until a junction, an end, or closure.</summary>
    private static Point2d[] WalkRun(
        Dictionary<(double X, double Y), List<OverlayEdge>> adjacency,
        HashSet<OverlayEdge> visited,
        Point2d start,
        OverlayEdge seed)
    {
        var run = new List<Point2d> { start };
        Point2d node = start;
        OverlayEdge edge = seed;

        while(true)
        {
            visited.Add(edge);
            node = edge.Opposite(node);
            run.Add(node);
            List<OverlayEdge> incident = adjacency[(node.X, node.Y)];

            if(incident.Count != 2)
            {
                break;
            }

            OverlayEdge next = incident[0] == edge ? incident[1] : incident[0];

            if(visited.Contains(next))
            {
                break;
            }

            edge = next;
        }

        return CanonicalizeRun(run);
    }

    /// <summary>
    /// Canonicalizes one run: an open run flows from its lexicographically smaller
    /// endpoint; a closed run rotates to its smallest vertex and takes the
    /// direction whose second vertex is the smaller.
    /// </summary>
    public static Point2d[] CanonicalizeRun(List<Point2d> run)
    {
        bool closed = run.Count > 2
            && run[0].X == run[run.Count - 1].X && run[0].Y == run[run.Count - 1].Y;

        if(!closed)
        {
            if(OverlayGraph.ComparePoints(run[run.Count - 1], run[0]) < 0)
            {
                run.Reverse();
            }

            return [.. run];
        }

        run.RemoveAt(run.Count - 1);
        int minimum = 0;

        for(int index = 1; index < run.Count; index++)
        {
            if(OverlayGraph.ComparePoints(run[index], run[minimum]) < 0)
            {
                minimum = index;
            }
        }

        var rotated = new Point2d[run.Count + 1];

        for(int index = 0; index < run.Count; index++)
        {
            rotated[index] = run[(minimum + index) % run.Count];
        }

        rotated[run.Count] = rotated[0];

        if(OverlayGraph.ComparePoints(rotated[rotated.Length - 2], rotated[1]) < 0)
        {
            Array.Reverse(rotated);
        }

        return rotated;
    }

    /// <summary>Collects the isolated result points: nodes in the result covered by no emitted edge.</summary>
    private static void CollectPoints(OverlayGraph graph, OverlayOperation operation, OverlayResultPieces pieces)
    {
        foreach(KeyValuePair<(double X, double Y), List<OverlayEdge>> entry in graph.Stars)
        {
            bool covered = false;
            bool inFirst = false;
            bool inSecond = false;

            foreach(OverlayEdge edge in entry.Value)
            {
                //Covered by an emitted edge, or by the result's area interior —
                //an adjacent member sector absorbs its bounding node: a point
                //already covered by a higher-dimensional result member
                //vanishes into it.
                covered |= edge.InAreaResult || edge.InLineResult
                    || Member(operation, edge.Left[0] == OverlaySideRegion.Interior, edge.Left[1] == OverlaySideRegion.Interior)
                    || Member(operation, edge.Right[0] == OverlaySideRegion.Interior, edge.Right[1] == OverlaySideRegion.Interior);
                inFirst |= edge.IsOn[0]
                    || edge.Left[0] == OverlaySideRegion.Interior || edge.Right[0] == OverlaySideRegion.Interior;
                inSecond |= edge.IsOn[1]
                    || edge.Left[1] == OverlaySideRegion.Interior || edge.Right[1] == OverlaySideRegion.Interior;
            }

            if(!covered && Member(operation, inFirst, inSecond))
            {
                pieces.Points.Add(new Point2d(entry.Key.X, entry.Key.Y));
            }
        }

        pieces.Points.Sort((first, second) => OverlayGraph.ComparePoints(first, second));
    }

    /// <summary>Canonicalizes one ring: rotated to its smallest vertex, oriented as asked, closed.</summary>
    public static Point2d[] CanonicalizeRing(List<Point2d> open, bool wantCounterClockwise)
    {
        int minimum = 0;

        for(int index = 1; index < open.Count; index++)
        {
            if(OverlayGraph.ComparePoints(open[index], open[minimum]) < 0)
            {
                minimum = index;
            }
        }

        var closed = new Point2d[open.Count + 1];

        for(int index = 0; index < open.Count; index++)
        {
            closed[index] = open[(minimum + index) % open.Count];
        }

        closed[open.Count] = closed[0];
        int orientation = RingGeometry.Orientation(closed);

        if((orientation > 0) != wantCounterClockwise && orientation != 0)
        {
            //Reversal keeps the start vertex: reverse the interior then re-close.
            Array.Reverse(closed, 1, closed.Length - 2);
        }

        return closed;
    }

    /// <summary>The plain-double ring area magnitude — a tie-breaking measure, its sign never read.</summary>
    private static double RingAreaMagnitude(Point2d[] closed)
    {
        double doubledArea = 0;

        for(int index = 1; index < closed.Length; index++)
        {
            doubledArea += ((closed[index - 1].X - closed[0].X) * (closed[index].Y - closed[0].Y))
                - ((closed[index].X - closed[0].X) * (closed[index - 1].Y - closed[0].Y));
        }

        return Math.Abs(doubledArea);
    }

    /// <summary>Attaches a line edge to its node's adjacency list.</summary>
    private static void AttachLine(Dictionary<(double X, double Y), List<OverlayEdge>> adjacency, Point2d node, OverlayEdge edge)
    {
        if(!adjacency.TryGetValue((node.X, node.Y), out List<OverlayEdge>? incident))
        {
            incident = [];
            adjacency[(node.X, node.Y)] = incident;
        }

        incident.Add(edge);
    }

    /// <summary>Deterministic run order: by first vertex, then second.</summary>
    private static int CompareRuns(Point2d[] first, Point2d[] second)
    {
        int order = OverlayGraph.ComparePoints(first[0], second[0]);

        if(order != 0)
        {
            return order;
        }

        return OverlayGraph.ComparePoints(first[1], second[1]);
    }

    /// <summary>The closed form of an open ring list.</summary>
    private static Point2d[] CloseRing(List<Point2d> open)
    {
        var closed = new Point2d[open.Count + 1];

        for(int index = 0; index < open.Count; index++)
        {
            closed[index] = open[index];
        }

        closed[open.Count] = open[0];

        return closed;
    }
}
