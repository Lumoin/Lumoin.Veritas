using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// Binds a pair of caller-owned <see cref="VeritasMemoryPool{T}"/> instances behind the
/// flat geometry model's column-allocator seam. The pool's exact-size rental contract
/// is the seam's exact-length contract, so no adapter sits between them and the
/// builder's loud length assertion can never fire on this binding. The built
/// <see cref="FlatGeometry"/> owns its rentals and returns them on
/// <see cref="FlatGeometry.Dispose"/>; the pools themselves stay the caller's to
/// dispose.
/// </summary>
public sealed class PooledFlatGeometryAllocators
{
    /// <summary>The pool the XY vertex columns rent from.</summary>
    private VeritasMemoryPool<Point2d> VertexPool { get; }

    /// <summary>The pool the Z and M ordinate columns rent from.</summary>
    private VeritasMemoryPool<double> OrdinatePool { get; }

    /// <summary>The allocator pair a pooled read passes to the reader, bound once as method groups.</summary>
    public FlatGeometryAllocators Allocators { get; }

    /// <summary>Binds the caller-owned pools; the binder itself holds no disposable state.</summary>
    /// <param name="vertexPool">The pool the XY vertex columns rent from.</param>
    /// <param name="ordinatePool">The pool the Z and M ordinate columns rent from.</param>
    public PooledFlatGeometryAllocators(VeritasMemoryPool<Point2d> vertexPool, VeritasMemoryPool<double> ordinatePool)
    {
        ArgumentNullException.ThrowIfNull(vertexPool);
        ArgumentNullException.ThrowIfNull(ordinatePool);

        VertexPool = vertexPool;
        OrdinatePool = ordinatePool;
        Allocators = new FlatGeometryAllocators(RentVertexColumn, RentOrdinateColumn);
    }

    /// <summary>Rents an exactly-sized vertex column; binds as a <see cref="ColumnAllocator{T}"/> method group.</summary>
    /// <param name="length">The exact element count to allocate.</param>
    private IMemoryOwner<Point2d> RentVertexColumn(int length)
    {
        return VertexPool.Rent(length);
    }

    /// <summary>Rents an exactly-sized ordinate column; binds as a <see cref="ColumnAllocator{T}"/> method group.</summary>
    /// <param name="length">The exact element count to allocate.</param>
    private IMemoryOwner<double> RentOrdinateColumn(int length)
    {
        return OrdinatePool.Rent(length);
    }
}
