using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The shared construction protocol behind every <see cref="FlatGeometry"/> producer:
/// accumulates scratch nodes, parts, and vertex columns, then lays the node tree out
/// breadth-first into the immutable carrier columns. Part and vertex indices are final
/// at append time — only nodes renumber in the finish pass. The WKT reader drives the
/// full protocol (Z/M slots, allocator seam); the operation builders drive the planar
/// subset (<see cref="AddVertex(Point2d)"/>, <see cref="ToGeometry()"/>) whose results
/// are heap-backed and carry no Z or M by the flat model's definition.
/// </summary>
internal sealed class FlatGeometryBuilder
{
    /// <summary>The nodes recorded in append order, re-laid breadth-first at the end.</summary>
    private List<ScratchNode> ScratchNodes { get; } = [];

    /// <summary>The part table, final at append time.</summary>
    private List<FlatGeometryPart> PartList { get; } = [];

    /// <summary>The XY scratch column.</summary>
    private List<Point2d> VertexList { get; } = [];

    /// <summary>The Z scratch column, NaN where a position carries none.</summary>
    private List<double> ZList { get; } = [];

    /// <summary>The M scratch column, NaN where a position carries none.</summary>
    private List<double> MList { get; } = [];

    /// <summary>The scratch index of the root tagged geometry.</summary>
    public int RootIndex { get; set; } = -1;

    /// <summary>The parts recorded so far.</summary>
    public int PartCount => PartList.Count;

    /// <summary>The vertices recorded so far.</summary>
    public int VertexCount => VertexList.Count;

    /// <summary>Records a node and returns its scratch index.</summary>
    public int AddNode(GeometryKind kind, bool hasZ, bool hasM, int firstPart, int partCount)
    {
        ScratchNodes.Add(new ScratchNode(kind, hasZ, hasM, firstPart, partCount, Children: null));

        return ScratchNodes.Count - 1;
    }

    /// <summary>Attaches the completed member list of a collection node.</summary>
    public void SetChildren(int nodeIndex, List<int> children)
    {
        ScratchNode node = ScratchNodes[nodeIndex];
        ScratchNodes[nodeIndex] = node with { Children = children };
    }

    /// <summary>Records one part.</summary>
    public void AddPart(FlatGeometryPart part)
    {
        PartList.Add(part);
    }

    /// <summary>Records one vertex with its ordinate slots.</summary>
    public void AddVertex(Point2d vertex, double z, double m)
    {
        VertexList.Add(vertex);
        ZList.Add(z);
        MList.Add(m);
    }

    /// <summary>Records one planar vertex; the ordinate slots stay empty.</summary>
    public void AddVertex(Point2d vertex)
    {
        AddVertex(vertex, double.NaN, double.NaN);
    }

    /// <summary>Whether two recorded vertices coincide on XY — the ring-closure test.</summary>
    public bool VerticesEqualXy(int first, int second)
    {
        return VertexList[first].X == VertexList[second].X && VertexList[first].Y == VertexList[second].Y;
    }

    /// <summary>
    /// Lays the scratch tree out breadth-first and materializes heap-backed columns —
    /// the operation-result path (results stay heap-backed; the allocator-seam
    /// extension to operation results is a recorded follow-up).
    /// </summary>
    public FlatGeometry ToGeometry()
    {
        return ToGeometry(FlatGeometryAllocators.Default);
    }

    /// <summary>
    /// Lays the scratch tree out breadth-first and materializes the columns,
    /// renting the vertex-scale ones through the allocator seam with the
    /// exact-length contract enforced.
    /// </summary>
    public FlatGeometry ToGeometry(FlatGeometryAllocators allocators)
    {
        if(RootIndex < 0 || RootIndex >= ScratchNodes.Count)
        {
            throw new InvalidOperationException(
                $"The builder's root index ({RootIndex}) does not name a recorded node (simple-features builder bookkeeping).");
        }

        var order = new List<int>(ScratchNodes.Count) { RootIndex };
        var firstChildAt = new int[ScratchNodes.Count];

        for(int cursor = 0; cursor < order.Count; cursor++)
        {
            ScratchNode node = ScratchNodes[order[cursor]];

            if(node.Children is not null)
            {
                firstChildAt[order[cursor]] = order.Count;
                order.AddRange(node.Children);
            }
        }

        var finalNodes = new FlatGeometryNode[order.Count];

        for(int index = 0; index < order.Count; index++)
        {
            ScratchNode node = ScratchNodes[order[index]];

            if(node.FirstPart < 0 || node.PartCount < 0 || node.FirstPart + node.PartCount > PartList.Count)
            {
                throw new InvalidOperationException(
                    $"A node's part run [{node.FirstPart}, {node.FirstPart + node.PartCount}) escapes the part table of {PartList.Count} (simple-features builder bookkeeping).");
            }

            finalNodes[index] = new FlatGeometryNode(
                node.Kind,
                node.Children is not null ? firstChildAt[order[index]] : 0,
                node.Children?.Count ?? 0,
                node.FirstPart,
                node.PartCount,
                node.HasZ,
                node.HasM);
        }

        foreach(FlatGeometryPart part in PartList)
        {
            if(part.Start < 0 || part.Length < 0 || part.Start + part.Length > VertexList.Count)
            {
                throw new InvalidOperationException(
                    $"A part's vertex run [{part.Start}, {part.Start + part.Length}) escapes the vertex column of {VertexList.Count} (simple-features builder bookkeeping).");
            }
        }

        bool anyZ = false;
        bool anyM = false;

        foreach(FlatGeometryNode node in finalNodes)
        {
            anyZ |= node.HasZ;
            anyM |= node.HasM;
        }

        System.Buffers.IMemoryOwner<Point2d>? vertexColumn = null;

        if(VertexList.Count > 0)
        {
            vertexColumn = RentExact(allocators.VertexColumns, VertexList.Count);
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(VertexList).CopyTo(vertexColumn.Memory.Span);
        }

        return new FlatGeometry(
            finalNodes,
            PartList.ToArray(),
            vertexColumn,
            anyZ ? RentOrdinates(allocators.OrdinateColumns, ZList) : null,
            anyM ? RentOrdinates(allocators.OrdinateColumns, MList) : null);
    }

    /// <summary>Rents one ordinate column and fills it from its scratch list.</summary>
    private static System.Buffers.IMemoryOwner<double> RentOrdinates(ColumnAllocator<double> allocator, List<double> ordinates)
    {
        System.Buffers.IMemoryOwner<double> column = RentExact(allocator, ordinates.Count);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ordinates).CopyTo(column.Memory.Span);

        return column;
    }

    /// <summary>Rents through the seam and enforces the exact-length contract loudly.</summary>
    private static System.Buffers.IMemoryOwner<T> RentExact<T>(ColumnAllocator<T> allocator, int length)
    {
        System.Buffers.IMemoryOwner<T> owner = allocator(length);
        int rentedLength = owner.Memory.Length;

        if(rentedLength != length)
        {
            owner.Dispose();

            throw new InvalidOperationException(
                $"The column allocator must return exactly the requested length ({length}), but returned {rentedLength} (simple-features column rental).");
        }

        return owner;
    }

    /// <summary>A node under construction; children exist only on collections.</summary>
    private readonly record struct ScratchNode(
        GeometryKind Kind, bool HasZ, bool HasM, int FirstPart, int PartCount, List<int>? Children);
}
