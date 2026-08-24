using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Node-local topology resolution: the throwaway fan of outgoing rays built
/// per interacting node and folded straight into the matrix. Ray directions
/// are the original segments' direction vectors — ordered by exact ordinate
/// comparisons (quadrant) and the exact direction-cross sign, never by
/// bearings from the node coordinate, so a computed crossing apex never
/// carries an ordering sign. Ring rays resolve their interior and exterior
/// sides from ring orientation combined with ring role: a shell's interior
/// lies on its bounded side, a hole's polygon-interior on its unbounded side.
/// </summary>
internal static class RelateNodeFan
{
    /// <summary>
    /// One outgoing ray: its operand, its direction as the ordered pair of
    /// original vertices, the placement along it, and the placements on its
    /// two sides relative to the direction of travel.
    /// </summary>
    /// <param name="IsFirst">True for the first operand's ray.</param>
    /// <param name="From">Direction origin — an original vertex, never the node.</param>
    /// <param name="To">Direction target — an original vertex.</param>
    /// <param name="OnLabel">Placement along the ray's stretch.</param>
    /// <param name="LeftLabel">Placement on the left of the direction.</param>
    /// <param name="RightLabel">Placement on the right of the direction.</param>
    /// <param name="IsRing">Whether the ray runs along a ring.</param>
    private readonly record struct FanRay(
        bool IsFirst,
        Point2d From,
        Point2d To,
        PointPlacement OnLabel,
        PointPlacement LeftLabel,
        PointPlacement RightLabel,
        bool IsRing);

    /// <summary>
    /// Resolves one node: raises the node-point cell from both operands'
    /// point statuses, the dimension-one cells from collinear-matched and
    /// sector-located stretches, and the dimension-two cells from the open
    /// sectors between consecutive fan directions.
    /// </summary>
    public static void Resolve(
        Point2d node,
        List<NodeSection> sections,
        RelateShape first,
        RelateShape second,
        RelateTopology topology)
    {
        topology.Raise(NodeStatus(first, node), NodeStatus(second, node), 0);

        List<FanRay> rays = BuildRays(node, sections, first, second);

        if(rays.Count == 0)
        {
            return;
        }

        rays.Sort(CompareByDirection);

        //Group rays sharing one exact direction into clusters; the cluster
        //table carries, per operand, the merged along-stretch label and the
        //merged side labels of any ring rays in the cluster.
        List<(int Start, int Count)> clusters = [];
        int clusterStart = 0;

        for(int rayIndex = 1; rayIndex <= rays.Count; rayIndex++)
        {
            if(rayIndex == rays.Count || CompareByDirection(rays[clusterStart], rays[rayIndex]) != 0)
            {
                clusters.Add((clusterStart, rayIndex - clusterStart));
                clusterStart = rayIndex;
            }
        }

        //Sector labels walk the fan cyclically: the open sector after a
        //cluster carries, per areal operand, the left label of that
        //cluster's ring rays; a lineal operand's sectors are all exterior.
        PointPlacement currentFirstLabel = InitialSectorLabel(rays, clusters, isFirst: true, first.IsAreal);
        PointPlacement currentSecondLabel = InitialSectorLabel(rays, clusters, isFirst: false, second.IsAreal);

        foreach((int start, int count) in clusters)
        {
            bool hasFirst = false;
            bool hasSecond = false;
            PointPlacement firstOn = PointPlacement.Exterior;
            PointPlacement secondOn = PointPlacement.Exterior;

            for(int rayIndex = start; rayIndex < start + count; rayIndex++)
            {
                FanRay ray = rays[rayIndex];

                if(ray.IsFirst)
                {
                    firstOn = hasFirst ? MergeOn(firstOn, ray.OnLabel) : ray.OnLabel;
                    hasFirst = true;
                }
                else
                {
                    secondOn = hasSecond ? MergeOn(secondOn, ray.OnLabel) : ray.OnLabel;
                    hasSecond = true;
                }
            }

            //The stretch along this direction: matched clusters meet the
            //other operand's stretch; unmatched ones lie in its current
            //sector.
            if(hasFirst && hasSecond)
            {
                topology.Raise(firstOn, secondOn, 1);
            }
            else if(hasFirst)
            {
                topology.Raise(firstOn, currentSecondLabel, 1);
            }
            else
            {
                topology.Raise(currentFirstLabel, secondOn, 1);
            }

            //Ring rays update their operand's sector label for the open
            //sector that follows this direction.
            if(TryMergedLeftLabel(rays, start, count, isFirst: true, out PointPlacement firstLeft))
            {
                currentFirstLabel = firstLeft;
            }

            if(TryMergedLeftLabel(rays, start, count, isFirst: false, out PointPlacement secondLeft))
            {
                currentSecondLabel = secondLeft;
            }

            //The open sector after this cluster is a two-dimensional patch.
            topology.Raise(currentFirstLabel, currentSecondLabel, 2);
        }
    }

    /// <summary>
    /// The node point's placement in one operand: an areal operand's node
    /// sits on a ring; a lineal operand's node is boundary exactly when it is
    /// an odd-valence endpoint.
    /// </summary>
    private static PointPlacement NodeStatus(RelateShape shape, Point2d node)
    {
        if(shape.IsAreal)
        {
            return PointPlacement.Boundary;
        }

        return shape.IsOddValenceEndpoint(node) ? PointPlacement.Boundary : PointPlacement.Interior;
    }

    /// <summary>
    /// Derives the outgoing rays from the deduplicated sections: a node at a
    /// segment endpoint yields one ray toward the other endpoint, a node
    /// interior to the segment yields both. Direction pairs are the original
    /// segment vertices, sign-adjusted by travel; degenerate segments never
    /// reach the scanner, so every ray has a real direction.
    /// </summary>
    private static List<FanRay> BuildRays(Point2d node, List<NodeSection> sections, RelateShape first, RelateShape second)
    {
        List<FanRay> rays = [];
        HashSet<NodeSection> seen = [];

        foreach(NodeSection section in sections)
        {
            if(!seen.Add(section))
            {
                continue;
            }

            RelateShape shape = section.IsFirst ? first : second;
            FlatGeometryPart part = shape.Parts[section.PartIndex];
            Point2d start = shape.Vertices[part.Start + section.SegmentIndex];
            Point2d end = shape.Vertices[part.Start + section.SegmentIndex + 1];
            bool isRing = part.Role is FlatGeometryPartRole.ExteriorRing or FlatGeometryPartRole.InteriorRing;
            bool nodeAtStart = node.X == start.X && node.Y == start.Y;
            bool nodeAtEnd = node.X == end.X && node.Y == end.Y;

            if(!nodeAtEnd)
            {
                //Forward travel along the stored segment direction.
                rays.Add(CreateRay(section.IsFirst, shape, section.PartIndex, start, end, isRing, forward: true));
            }

            if(!nodeAtStart)
            {
                //Backward travel: the direction pair flips.
                rays.Add(CreateRay(section.IsFirst, shape, section.PartIndex, end, start, isRing, forward: false));
            }
        }

        return rays;
    }

    /// <summary>
    /// Builds one ray with its labels: line rays run interior with exterior
    /// sides; ring rays run boundary with sides resolved from orientation,
    /// role, and travel direction — a degenerate ring's orientation reads as
    /// counter-clockwise, the best-effort answer outside the validity
    /// contract.
    /// </summary>
    private static FanRay CreateRay(bool isFirst, RelateShape shape, int partIndex, Point2d from, Point2d to, bool isRing, bool forward)
    {
        if(!isRing)
        {
            return new FanRay(isFirst, from, to, PointPlacement.Interior, PointPlacement.Exterior, PointPlacement.Exterior, IsRing: false);
        }

        int orientation = shape.RingOrientation(partIndex);

        if(orientation == 0)
        {
            orientation = 1;
        }

        bool isShell = shape.Parts[partIndex].Role == FlatGeometryPartRole.ExteriorRing;
        int sideSign = orientation * (isShell ? 1 : -1) * (forward ? 1 : -1);
        PointPlacement left = sideSign > 0 ? PointPlacement.Interior : PointPlacement.Exterior;
        PointPlacement right = sideSign > 0 ? PointPlacement.Exterior : PointPlacement.Interior;

        return new FanRay(isFirst, from, to, PointPlacement.Boundary, left, right, IsRing: true);
    }

    /// <summary>
    /// Angular comparison of two rays by their direction vectors: quadrant
    /// class by exact ordinate comparisons, then the exact direction-cross
    /// sign within a class. Zero means one shared direction.
    /// </summary>
    private static int CompareByDirection(FanRay firstRay, FanRay secondRay)
    {
        int firstClass = DirectionClass(firstRay.From, firstRay.To);
        int secondClass = DirectionClass(secondRay.From, secondRay.To);

        if(firstClass != secondClass)
        {
            return firstClass < secondClass ? -1 : 1;
        }

        int cross = ExactOrientation.DirectionCrossSign(firstRay.From, firstRay.To, secondRay.From, secondRay.To);

        if(cross > 0)
        {
            return -1;
        }

        if(cross < 0)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The eight-way angular class of a direction — the promoted shared
    /// primitive, see <see cref="ExactOrientation.DirectionClass"/>.
    /// </summary>
    private static int DirectionClass(Point2d from, Point2d to)
    {
        return ExactOrientation.DirectionClass(from, to);
    }

    /// <summary>
    /// The sector label in force before the first cluster: the merged left
    /// label of the operand's last ring-ray cluster in cyclic order, or
    /// exterior when the operand bounds no area at this node.
    /// </summary>
    private static PointPlacement InitialSectorLabel(List<FanRay> rays, List<(int Start, int Count)> clusters, bool isFirst, bool isAreal)
    {
        if(!isAreal)
        {
            return PointPlacement.Exterior;
        }

        for(int clusterIndex = clusters.Count - 1; clusterIndex >= 0; clusterIndex--)
        {
            (int start, int count) = clusters[clusterIndex];

            if(TryMergedLeftLabel(rays, start, count, isFirst, out PointPlacement label))
            {
                return label;
            }
        }

        return PointPlacement.Exterior;
    }

    /// <summary>
    /// The merged left label of one operand's ring rays within a cluster:
    /// interior wins over exterior when same-direction rings disagree (an
    /// out-of-contract arrangement). False when the cluster carries no ring
    /// ray of the operand.
    /// </summary>
    private static bool TryMergedLeftLabel(List<FanRay> rays, int start, int count, bool isFirst, out PointPlacement label)
    {
        bool found = false;
        label = PointPlacement.Exterior;

        for(int rayIndex = start; rayIndex < start + count; rayIndex++)
        {
            FanRay ray = rays[rayIndex];

            if(ray.IsFirst != isFirst || !ray.IsRing)
            {
                continue;
            }

            if(!found || ray.LeftLabel == PointPlacement.Interior)
            {
                label = ray.LeftLabel;
            }

            found = true;
        }

        return found;
    }

    /// <summary>
    /// Merges two along-stretch labels of one operand sharing a direction:
    /// boundary wins over interior — a ring stretch outranks a line stretch
    /// when both run the same way.
    /// </summary>
    private static PointPlacement MergeOn(PointPlacement current, PointPlacement candidate)
    {
        if(current == PointPlacement.Boundary || candidate == PointPlacement.Boundary)
        {
            return PointPlacement.Boundary;
        }

        return current;
    }
}
