using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// A per-node writer check a codec supplies to the shared validation walk
/// for its format-specific representability rules, run for each node before
/// the node's ordinate checks. The check reports a refusal by value and
/// never throws; implementations are static method groups, never captures.
/// </summary>
/// <param name="geometry">The geometry under validation.</param>
/// <param name="nodeIndex">The node being validated.</param>
/// <param name="refusal">The refusal when the check fails.</param>
/// <returns>True when the node passes the check.</returns>
internal delegate bool GeometryCodecNodeCheck(in FlatGeometry geometry, int nodeIndex, out GeometryCodecRefusal refusal);

/// <summary>
/// The shared writer validation walk of the codec family, run to completion
/// before a writer's first destination write so a refused emission leaves
/// the destination untouched. The order is pinned and mixed-defect inputs
/// are deterministic: first the model nesting depth, then the nodes in
/// ascending breadth-first index — within each node the format's
/// representability check, then the measure check, then the ordinates in
/// ascending vertex index with finiteness required for X, Y, and every Z
/// slot the node declares. Writer refusals carry a byte offset of minus
/// one — there is no input text.
/// </summary>
internal static class GeometryCodecWriteValidation
{
    /// <summary>
    /// Runs the shared walk with no format-specific node check.
    /// </summary>
    /// <param name="geometry">The geometry to validate.</param>
    /// <param name="refusal">The first refusal in the pinned order.</param>
    /// <returns>True when the geometry passes.</returns>
    public static bool TryValidate(in FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryValidate(in geometry, nodeCheck: null, out refusal);
    }

    /// <summary>
    /// Runs the shared walk, interleaving the format's representability
    /// check per node ahead of the node's ordinate checks.
    /// </summary>
    /// <param name="geometry">The geometry to validate.</param>
    /// <param name="nodeCheck">
    /// The format-specific per-node check, or null when the format has none.
    /// </param>
    /// <param name="refusal">The first refusal in the pinned order.</param>
    /// <returns>True when the geometry passes.</returns>
    public static bool TryValidate(in FlatGeometry geometry, GeometryCodecNodeCheck? nodeCheck, out GeometryCodecRefusal refusal)
    {
        if(!DepthWithinBound(in geometry))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NestingTooDeep, -1);

            return false;
        }

        ReadOnlySpan<FlatGeometryNode> nodes = geometry.Nodes;

        for(int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            FlatGeometryNode node = nodes[nodeIndex];

            if(nodeCheck is not null && !nodeCheck(in geometry, nodeIndex, out refusal))
            {
                return false;
            }

            if(node.HasM)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MeasureUnrepresentable, -1);

                return false;
            }

            if(!OrdinatesFinite(in geometry, in node))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NonFiniteCoordinate, -1);

                return false;
            }
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Walks the breadth-first tree once, assigning each node the length of
    /// its root path; parents precede children in the layout, so one
    /// forward pass suffices. Thirty-one wrapping collections around a leaf
    /// pass; a path longer than the bound refuses — the same semantics the
    /// codec readers certify.
    /// </summary>
    private static bool DepthWithinBound(in FlatGeometry geometry)
    {
        ReadOnlySpan<FlatGeometryNode> nodes = geometry.Nodes;
        int[] depths = new int[nodes.Length];

        depths[0] = 1;

        for(int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            FlatGeometryNode node = nodes[nodeIndex];

            if(depths[nodeIndex] > GeometryCodecText.MaximumNestingDepth)
            {
                return false;
            }

            for(int childOffset = 0; childOffset < node.ChildCount; childOffset++)
            {
                depths[node.FirstChild + childOffset] = depths[nodeIndex] + 1;
            }
        }

        return true;
    }

    /// <summary>
    /// Answers whether every ordinate the node carries is finite: X and Y
    /// for every vertex of every part, and the Z slot wherever the node
    /// declares Z — a NaN slot under a declaring node has no encoding in
    /// any codec format.
    /// </summary>
    private static bool OrdinatesFinite(in FlatGeometry geometry, in FlatGeometryNode node)
    {
        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts.Slice(node.FirstPart, node.PartCount);

        foreach(FlatGeometryPart part in parts)
        {
            for(int index = 0; index < part.Length; index++)
            {
                int vertexIndex = part.Start + index;
                Point2d vertex = geometry.Vertices[vertexIndex];

                if(!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
                {
                    return false;
                }

                if(node.HasZ && !double.IsFinite(geometry.ZOrdinates[vertexIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
