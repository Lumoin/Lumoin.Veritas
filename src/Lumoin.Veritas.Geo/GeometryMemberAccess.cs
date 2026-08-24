using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// Member counting and extraction over a <see cref="FlatGeometry"/> for the <c>geof:numGeometries</c> and
/// <c>geof:geometryN</c> functions, under the SQL/MM convention: a collection's members are its child
/// geometries, a multi kind's members are its element geometries, and an atomic geometry — typed empties
/// included — is a one-member sequence of itself. Member numbering is one-based. Extraction always copies:
/// the extracted member owns fresh heap columns, so disposing it never touches the source geometry's
/// rentals.
/// </summary>
internal static class GeometryMemberAccess
{
    /// <summary>
    /// The member count: children for a collection, elements for a multi kind (exterior-ring groups for a
    /// multipolygon), one for an atomic geometry. Empty multi kinds and empty collections — and the
    /// uninitialized carrier, which degrades to the empty collection — count zero.
    /// </summary>
    /// <param name="geometry">The geometry whose members are counted.</param>
    /// <returns>The member count.</returns>
    public static int CountMembers(in FlatGeometry geometry)
    {
        if(geometry.Nodes.Length == 0)
        {
            return 0;
        }

        FlatGeometryNode root = geometry.Nodes[0];

        return root.Kind switch
        {
            GeometryKind.GeometryCollection => root.ChildCount,
            GeometryKind.MultiPoint or GeometryKind.MultiLineString => root.PartCount,
            GeometryKind.MultiPolygon => CountExteriorRings(geometry.Parts, root),
            _ => 1,
        };
    }

    /// <summary>
    /// Extracts the one-based <paramref name="memberNumber"/>-th member as a self-owned copy; false when
    /// the number lies outside [1, <see cref="CountMembers"/>].
    /// </summary>
    /// <param name="geometry">The geometry whose member is extracted.</param>
    /// <param name="memberNumber">The one-based member number.</param>
    /// <param name="member">The extracted member, when the number is in range.</param>
    /// <returns><see langword="true"/> when the number is in range.</returns>
    public static bool TryExtractMember(in FlatGeometry geometry, int memberNumber, out FlatGeometry member)
    {
        if(memberNumber < 1 || memberNumber > CountMembers(in geometry))
        {
            member = default;

            return false;
        }

        FlatGeometryNode root = geometry.Nodes[0];
        switch(root.Kind)
        {
            case(GeometryKind.GeometryCollection):
                member = ExtractSubtree(in geometry, root.FirstChild + memberNumber - 1);

                return true;

            case(GeometryKind.MultiPoint):
                member = ExtractPartRun(in geometry, GeometryKind.Point, root, root.FirstPart + memberNumber - 1, 1);

                return true;

            case(GeometryKind.MultiLineString):
                member = ExtractPartRun(in geometry, GeometryKind.LineString, root, root.FirstPart + memberNumber - 1, 1);

                return true;

            case(GeometryKind.MultiPolygon):
                FindPolygonGroup(geometry.Parts, root, memberNumber, out int firstPart, out int partCount);
                member = ExtractPartRun(in geometry, GeometryKind.Polygon, root, firstPart, partCount);

                return true;

            default:
                member = ExtractSubtree(in geometry, 0);

                return true;
        }
    }

    /// <summary>Counts the exterior-ring parts of a multipolygon node — each opens one member polygon.</summary>
    /// <param name="parts">The source part table.</param>
    /// <param name="node">The multipolygon node.</param>
    /// <returns>The member polygon count.</returns>
    private static int CountExteriorRings(ReadOnlySpan<FlatGeometryPart> parts, FlatGeometryNode node)
    {
        int count = 0;
        for(int index = 0; index < node.PartCount; index++)
        {
            if(parts[node.FirstPart + index].Role == FlatGeometryPartRole.ExteriorRing)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Locates the one-based <paramref name="memberNumber"/>-th polygon group of a multipolygon node: its
    /// exterior ring plus the interior rings that follow it. The caller has range-checked the number, so
    /// the group always exists.
    /// </summary>
    /// <param name="parts">The source part table.</param>
    /// <param name="node">The multipolygon node.</param>
    /// <param name="memberNumber">The one-based member polygon number.</param>
    /// <param name="firstPart">The group's first part index.</param>
    /// <param name="partCount">The group's part count.</param>
    private static void FindPolygonGroup(ReadOnlySpan<FlatGeometryPart> parts, FlatGeometryNode node, int memberNumber, out int firstPart, out int partCount)
    {
        int seen = 0;
        firstPart = node.FirstPart;
        for(int index = 0; index < node.PartCount; index++)
        {
            if(parts[node.FirstPart + index].Role == FlatGeometryPartRole.ExteriorRing)
            {
                seen++;
                if(seen == memberNumber)
                {
                    firstPart = node.FirstPart + index;
                }
                else if(seen == memberNumber + 1)
                {
                    partCount = node.FirstPart + index - firstPart;

                    return;
                }
            }
        }

        partCount = node.FirstPart + node.PartCount - firstPart;
    }

    /// <summary>
    /// Builds a single-node member geometry over a contiguous part run of a multi-kind node, copying the
    /// run's vertex slices and, when the node carries them, the aligned Z and M slices.
    /// </summary>
    /// <param name="geometry">The source geometry.</param>
    /// <param name="kind">The member's element kind.</param>
    /// <param name="node">The multi-kind node owning the run.</param>
    /// <param name="firstPart">The run's first part index in the source part table.</param>
    /// <param name="partCount">The run's part count.</param>
    /// <returns>The member geometry.</returns>
    private static FlatGeometry ExtractPartRun(in FlatGeometry geometry, GeometryKind kind, FlatGeometryNode node, int firstPart, int partCount)
    {
        var newParts = new FlatGeometryPart[partCount];
        var runs = new List<VertexRun>(partCount);
        int cursor = 0;
        for(int index = 0; index < partCount; index++)
        {
            FlatGeometryPart part = geometry.Parts[firstPart + index];
            newParts[index] = new FlatGeometryPart(cursor, part.Length, part.Role);
            runs.Add(new VertexRun(part.Start, part.Length));
            cursor += part.Length;
        }

        FlatGeometryNode[] newNodes = [new FlatGeometryNode(kind, 0, 0, 0, partCount, node.HasZ, node.HasM)];

        return Build(newNodes, newParts, runs, cursor, in geometry);
    }

    /// <summary>
    /// Extracts the node subtree rooted at <paramref name="startNode"/> as a self-contained geometry: a
    /// breadth-first re-index of its nodes (a worklist scan, since a breadth-first layout keeps every
    /// collection's children contiguous), parts re-based onto fresh columns, and the vertex slices copied
    /// in emission order.
    /// </summary>
    /// <param name="geometry">The source geometry.</param>
    /// <param name="startNode">The subtree root's node index.</param>
    /// <returns>The subtree as a self-owned geometry.</returns>
    private static FlatGeometry ExtractSubtree(in FlatGeometry geometry, int startNode)
    {
        //Phase one: the new breadth-first node order. Appending each processed node's children keeps every
        //new collection's children contiguous, and the append position is the node's new first-child index.
        var order = new List<int> { startNode };
        var firstChildSlots = new List<int> { 0 };
        for(int position = 0; position < order.Count; position++)
        {
            FlatGeometryNode node = geometry.Nodes[order[position]];
            firstChildSlots[position] = node.ChildCount > 0 ? order.Count : 0;
            for(int child = 0; child < node.ChildCount; child++)
            {
                order.Add(node.FirstChild + child);
                firstChildSlots.Add(0);
            }
        }

        //Phase two: emit the nodes with re-based part runs and collect the vertex slices to copy.
        var newNodes = new FlatGeometryNode[order.Count];
        var newParts = new List<FlatGeometryPart>();
        var runs = new List<VertexRun>();
        int cursor = 0;
        for(int position = 0; position < order.Count; position++)
        {
            FlatGeometryNode node = geometry.Nodes[order[position]];
            int newFirstPart = node.PartCount > 0 ? newParts.Count : 0;
            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = geometry.Parts[node.FirstPart + index];
                newParts.Add(new FlatGeometryPart(cursor, part.Length, part.Role));
                runs.Add(new VertexRun(part.Start, part.Length));
                cursor += part.Length;
            }

            newNodes[position] = new FlatGeometryNode(node.Kind, firstChildSlots[position], node.ChildCount, newFirstPart, node.PartCount, node.HasZ, node.HasM);
        }

        return Build(newNodes, [.. newParts], runs, cursor, in geometry);
    }

    /// <summary>
    /// Assembles the member geometry: copies the recorded vertex slices into exactly-sized heap columns,
    /// carrying the aligned Z and M slices exactly when some copied node carries the ordinate.
    /// </summary>
    /// <param name="nodes">The member's node table.</param>
    /// <param name="parts">The member's part table, already re-based onto the new columns.</param>
    /// <param name="runs">The source vertex slices, in emission order.</param>
    /// <param name="vertexCount">The total vertex count over the runs.</param>
    /// <param name="source">The source geometry the slices copy from.</param>
    /// <returns>The assembled member.</returns>
    private static FlatGeometry Build(FlatGeometryNode[] nodes, FlatGeometryPart[] parts, List<VertexRun> runs, int vertexCount, in FlatGeometry source)
    {
        bool carriesZ = false;
        bool carriesM = false;
        foreach(FlatGeometryNode node in nodes)
        {
            carriesZ |= node.HasZ;
            carriesM |= node.HasM;
        }

        HeapColumnOwner<Point2d>? vertexColumn = null;
        HeapColumnOwner<double>? zColumn = null;
        HeapColumnOwner<double>? mColumn = null;
        if(vertexCount > 0)
        {
            var vertices = new Point2d[vertexCount];
            var zOrdinates = carriesZ ? new double[vertexCount] : null;
            var mOrdinates = carriesM ? new double[vertexCount] : null;
            int cursor = 0;
            foreach(VertexRun run in runs)
            {
                source.Vertices.Slice(run.Start, run.Length).CopyTo(vertices.AsSpan(cursor, run.Length));
                if(zOrdinates is not null)
                {
                    source.ZOrdinates.Slice(run.Start, run.Length).CopyTo(zOrdinates.AsSpan(cursor, run.Length));
                }

                if(mOrdinates is not null)
                {
                    source.MOrdinates.Slice(run.Start, run.Length).CopyTo(mOrdinates.AsSpan(cursor, run.Length));
                }

                cursor += run.Length;
            }

            vertexColumn = new HeapColumnOwner<Point2d>(vertices);
            zColumn = zOrdinates is null ? null : new HeapColumnOwner<double>(zOrdinates);
            mColumn = mOrdinates is null ? null : new HeapColumnOwner<double>(mOrdinates);
        }

        return new FlatGeometry(nodes, parts, vertexColumn, zColumn, mColumn);
    }

    /// <summary>One source vertex slice to copy: <see cref="Length"/> vertices from <see cref="Start"/>.</summary>
    /// <param name="Start">The slice's first vertex index in the source columns.</param>
    /// <param name="Length">The slice's vertex count.</param>
    private readonly record struct VertexRun(int Start, int Length);
}
