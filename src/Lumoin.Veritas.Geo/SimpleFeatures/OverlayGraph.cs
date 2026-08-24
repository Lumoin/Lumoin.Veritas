using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>The resolved region on one side of an edge for one operand.</summary>
internal enum OverlaySideRegion
{
    /// <summary>Not yet resolved.</summary>
    Unknown = 0,

    /// <summary>The open region on this side lies outside the operand.</summary>
    Exterior = 1,

    /// <summary>The open region on this side lies inside the operand.</summary>
    Interior = 2,
}

/// <summary>One areal contribution vote on a merged edge.</summary>
/// <param name="Part">The contributing ring part index in its operand.</param>
/// <param name="InteriorOnLeft">Whether the polygon interior lies left in the edge's canonical frame.</param>
/// <param name="IsHoleRing">Whether the contributing ring plays the interior-ring role.</param>
internal readonly record struct OverlayBoundaryVote(int Part, bool InteriorOnLeft, bool IsHoleRing);

/// <summary>
/// One merged edge of the overlay graph: a canonical undirected split edge carrying,
/// per operand, whether the operand lies on it and which region each canonical side
/// resolves to. Coincident split edges merge here, their contributions accumulated as
/// votes; a same-part pair of opposite votes is the collapsed slit case and takes its
/// location from the parent ring role instead of its fan. The direction anchor is one
/// contributing parent's original endpoints, chosen by an operand-independent key, so
/// angular sorting at nodes reads original coordinates.
/// </summary>
internal sealed class OverlayEdge
{
    /// <summary>Creates the merged edge over its canonical endpoints.</summary>
    public OverlayEdge(Point2d start, Point2d end)
    {
        Start = start;
        End = end;
        AnchorStart = start;
        AnchorEnd = end;
    }

    /// <summary>The canonical start: the lexicographically smaller endpoint.</summary>
    public Point2d Start { get; }

    /// <summary>The canonical end.</summary>
    public Point2d End { get; }

    /// <summary>The chosen parent's original start, oriented with the canonical direction.</summary>
    public Point2d AnchorStart { get; set; }

    /// <summary>The chosen parent's original end, oriented with the canonical direction.</summary>
    public Point2d AnchorEnd { get; set; }

    /// <summary>Whether an anchor parent has been chosen yet.</summary>
    public bool HasAnchor { get; set; }

    /// <summary>Whether each operand contributed this edge (lies on its closure).</summary>
    public bool[] IsOn { get; } = new bool[2];

    /// <summary>Whether each operand's contribution is lineal rather than areal.</summary>
    public bool[] HasLine { get; } = new bool[2];

    /// <summary>The areal votes per operand; null until the first vote.</summary>
    public List<OverlayBoundaryVote>?[] Votes { get; } = new List<OverlayBoundaryVote>?[2];

    /// <summary>The resolved left-side region per operand, in the canonical frame.</summary>
    public OverlaySideRegion[] Left { get; } = new OverlaySideRegion[2];

    /// <summary>The resolved right-side region per operand, in the canonical frame.</summary>
    public OverlaySideRegion[] Right { get; } = new OverlaySideRegion[2];

    /// <summary>Whether the edge was emitted into an area result boundary.</summary>
    public bool InAreaResult { get; set; }

    /// <summary>
    /// Whether the result-directed version runs in the canonical direction — true
    /// when the canonical left side is the member side, keeping the result
    /// interior on the directed edge's left.
    /// </summary>
    public bool ResultForward { get; set; }

    /// <summary>Whether the edge was emitted into a line result.</summary>
    public bool InLineResult { get; set; }

    /// <summary>Whether the directed forward (canonical-direction) version was walked.</summary>
    public bool ForwardVisited { get; set; }

    /// <summary>Whether the directed backward version was walked.</summary>
    public bool BackwardVisited { get; set; }

    /// <summary>The outgoing direction anchor at the given node: the parent vector, sign-adjusted.</summary>
    public (Point2d From, Point2d To) OutgoingAnchor(Point2d node)
    {
        bool atStart = node.X == Start.X && node.Y == Start.Y;

        return atStart ? (AnchorStart, AnchorEnd) : (AnchorEnd, AnchorStart);
    }

    /// <summary>The far endpoint from the given node.</summary>
    public Point2d Opposite(Point2d node)
    {
        bool atStart = node.X == Start.X && node.Y == Start.Y;

        return atStart ? End : Start;
    }
}

/// <summary>
/// The per-call overlay graph: merged edges keyed by canonical endpoint pair and
/// node stars sorted by the exact direction primitives over original parent anchors.
/// Built once per operation, walked by labeling and extraction, garbage after the
/// call — the licensed departure from the relate engine's node-local-only posture.
/// </summary>
internal sealed class OverlayGraph
{
    /// <summary>The merged edges keyed by their canonical endpoint pair.</summary>
    private Dictionary<(double StartX, double StartY, double EndX, double EndY), OverlayEdge> EdgeMap { get; } = [];

    /// <summary>The edges in deterministic insertion order.</summary>
    public List<OverlayEdge> Edges { get; } = [];

    /// <summary>The incident edges per node, unsorted until <see cref="SortStars"/>.</summary>
    public Dictionary<(double X, double Y), List<OverlayEdge>> Stars { get; } = [];

    /// <summary>The nodes that are original operand vertices — the locator fallback's representatives.</summary>
    public HashSet<(double X, double Y)> OriginalNodes { get; } = [];

    /// <summary>Registers one split edge, merging it into an existing coincident edge if present.</summary>
    public void AddEdge(in OverlaySegment segment, Point2d start, Point2d end)
    {
        Point2d normalizedStart = OverlayNoding.NormalizeNode(start);
        Point2d normalizedEnd = OverlayNoding.NormalizeNode(end);
        bool forward = ComparePoints(normalizedStart, normalizedEnd) <= 0;
        Point2d canonicalStart = forward ? normalizedStart : normalizedEnd;
        Point2d canonicalEnd = forward ? normalizedEnd : normalizedStart;
        var key = (canonicalStart.X, canonicalStart.Y, canonicalEnd.X, canonicalEnd.Y);

        if(!EdgeMap.TryGetValue(key, out OverlayEdge? edge))
        {
            edge = new OverlayEdge(canonicalStart, canonicalEnd);
            EdgeMap[key] = edge;
            Edges.Add(edge);
            Attach(canonicalStart, edge);
            Attach(canonicalEnd, edge);
        }

        if((start.X == segment.Start.X && start.Y == segment.Start.Y) || (start.X == segment.End.X && start.Y == segment.End.Y))
        {
            OriginalNodes.Add((normalizedStart.X, normalizedStart.Y));
        }

        if((end.X == segment.Start.X && end.Y == segment.Start.Y) || (end.X == segment.End.X && end.Y == segment.End.Y))
        {
            OriginalNodes.Add((normalizedEnd.X, normalizedEnd.Y));
        }

        //The segment's travel direction maps onto the canonical frame: when the
        //split ran start-to-end in canonical order the frames agree.
        bool alignedWithCanonical = forward;

        edge.IsOn[segment.Operand] = true;

        if(segment.IsBoundary)
        {
            edge.Votes[segment.Operand] ??= [];

            if(!segment.IsCollapsedRing)
            {
                bool interiorOnCanonicalLeft = alignedWithCanonical ? segment.InteriorOnLeft : !segment.InteriorOnLeft;
                edge.Votes[segment.Operand]!.Add(new OverlayBoundaryVote(segment.Part, interiorOnCanonicalLeft, segment.IsHoleRing));
            }
            else
            {
                //A collapsed ring bounds no area; recording the same-part pair of
                //opposite votes routes it through the collapsed-slit rule, which
                //reads the parent role — a degenerate shell reads exterior on both
                //sides, a degenerate hole interior — recorded best-effort semantics.
                edge.Votes[segment.Operand]!.Add(new OverlayBoundaryVote(segment.Part, InteriorOnLeft: true, segment.IsHoleRing));
                edge.Votes[segment.Operand]!.Add(new OverlayBoundaryVote(segment.Part, InteriorOnLeft: false, segment.IsHoleRing));
            }
        }
        else
        {
            edge.HasLine[segment.Operand] = true;
        }

        ChooseAnchor(edge, segment, alignedWithCanonical);
    }

    /// <summary>Sorts every node star counter-clockwise by the exact direction primitives.</summary>
    public void SortStars()
    {
        foreach(KeyValuePair<(double X, double Y), List<OverlayEdge>> star in Stars)
        {
            var node = new Point2d(star.Key.X, star.Key.Y);
            star.Value.Sort((first, second) => CompareOutgoing(node, first, second));
        }
    }

    /// <summary>The sorted star at a node.</summary>
    public List<OverlayEdge> StarAt(Point2d node)
    {
        return Stars[(node.X, node.Y)];
    }

    /// <summary>Lexicographic point order: X first, then Y.</summary>
    public static int ComparePoints(Point2d first, Point2d second)
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

        return first.Y > second.Y ? 1 : 0;
    }

    /// <summary>Attaches the edge to a node's star.</summary>
    private void Attach(Point2d node, OverlayEdge edge)
    {
        if(!Stars.TryGetValue((node.X, node.Y), out List<OverlayEdge>? star))
        {
            star = [];
            Stars[(node.X, node.Y)] = star;
        }

        star.Add(edge);
    }

    /// <summary>
    /// Keeps the direction anchor from the contributing parent with the smallest
    /// operand-independent endpoint key, oriented to the canonical frame, so the
    /// choice never depends on operand order.
    /// </summary>
    private static void ChooseAnchor(OverlayEdge edge, in OverlaySegment segment, bool alignedWithCanonical)
    {
        Point2d parentStart = alignedWithCanonical ? segment.Start : segment.End;
        Point2d parentEnd = alignedWithCanonical ? segment.End : segment.Start;

        if(edge.HasAnchor)
        {
            int order = ComparePoints(parentStart, edge.AnchorStart);

            if(order > 0 || (order == 0 && ComparePoints(parentEnd, edge.AnchorEnd) >= 0))
            {
                return;
            }
        }

        edge.AnchorStart = parentStart;
        edge.AnchorEnd = parentEnd;
        edge.HasAnchor = true;
    }

    /// <summary>Counter-clockwise angular comparison of two edges' outgoing anchors at a node.</summary>
    private static int CompareOutgoing(Point2d node, OverlayEdge first, OverlayEdge second)
    {
        (Point2d firstFrom, Point2d firstTo) = first.OutgoingAnchor(node);
        (Point2d secondFrom, Point2d secondTo) = second.OutgoingAnchor(node);
        int firstClass = ExactOrientation.DirectionClass(firstFrom, firstTo);
        int secondClass = ExactOrientation.DirectionClass(secondFrom, secondTo);

        if(firstClass != secondClass)
        {
            return firstClass < secondClass ? -1 : 1;
        }

        int cross = ExactOrientation.DirectionCrossSign(firstFrom, firstTo, secondFrom, secondTo);

        if(cross > 0)
        {
            return -1;
        }

        if(cross < 0)
        {
            return 1;
        }

        //Distinct star edges cannot share an exact direction in a fully noded
        //arrangement; a represented tie falls back to the canonical endpoints for
        //a deterministic, operand-independent order.
        return ComparePoints(first.Opposite(node), second.Opposite(node));
    }
}
