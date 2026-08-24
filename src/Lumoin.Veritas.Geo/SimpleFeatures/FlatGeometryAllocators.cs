using System;
using System.Buffers;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The allocator pair a <see cref="FlatGeometry"/> build rents its vertex-scale
/// columns through: XY vertices and the optional Z/M ordinate columns. The node and
/// part tables stay small owned arrays — the vertex columns are where the bytes are.
/// <see cref="Default"/> allocates plain heap arrays (no-op disposal); a pooling host
/// passes its own delegates and then owns the built geometry's lifetime through
/// <see cref="FlatGeometry.Dispose"/>.
/// </summary>
/// <param name="VertexColumns">Allocates the XY vertex column.</param>
/// <param name="OrdinateColumns">Allocates a Z or M ordinate column.</param>
public readonly record struct FlatGeometryAllocators(
    ColumnAllocator<Point2d> VertexColumns,
    ColumnAllocator<double> OrdinateColumns)
{
    /// <summary>Heap-array allocation; disposal is a no-op.</summary>
    public static FlatGeometryAllocators Default { get; } = new(RentVertexHeap, RentOrdinateHeap);

    /// <summary>Allocates a heap-backed vertex column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
    private static HeapColumnOwner<Point2d> RentVertexHeap(int length)
    {
        return new HeapColumnOwner<Point2d>(new Point2d[length]);
    }

    /// <summary>Allocates a heap-backed ordinate column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
    private static HeapColumnOwner<double> RentOrdinateHeap(int length)
    {
        return new HeapColumnOwner<double>(new double[length]);
    }
}

/// <summary>
/// The default column owner: a plain heap array behind the same
/// <see cref="IMemoryOwner{T}"/> currency pooled rentals use, so the carrier holds one
/// shape regardless of the allocator. Disposal is a no-op — the array is garbage.
/// </summary>
internal sealed class HeapColumnOwner<T>(T[] array): IMemoryOwner<T>
{
    /// <summary>The heap array behind the owner.</summary>
    private T[] Backing { get; } = array;

    /// <inheritdoc/>
    public Memory<T> Memory => Backing;

    /// <inheritdoc/>
    public void Dispose()
    {
        //Heap arrays have no return path; the collector owns them.
    }
}
