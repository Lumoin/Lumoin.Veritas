using System.Buffers;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The column-allocator seam: a pooling host binds its own allocators as named
/// delegates and owns disposal; the exact-length contract is enforced loudly; empties
/// rent nothing; the heap default's disposal is a no-op.
/// </summary>
[TestClass]
internal sealed class FlatGeometryAllocatorsTests
{
    /// <summary>A pooling host's columns rent exactly and return on dispose.</summary>
    [TestMethod]
    public void PooledColumnsAreRentedExactlyAndReturnedOnDispose()
    {
        var allocator = new CountingAllocator();
        var allocators = new FlatGeometryAllocators(allocator.RentVertices, allocator.RentOrdinates);

        Assert.IsTrue(WktGeometryReader.TryRead("POINT Z (1 2 3)", allocators, out FlatGeometry geometry, out _),
            "The pooled parse must succeed.");

        Assert.AreEqual(2, allocator.Live, "One vertex column and one Z column are rented, nothing else.");
        Assert.AreEqual(3.0, geometry.ZOrdinates[0], "The pooled columns carry the parsed values.");

        geometry.Dispose();

        Assert.AreEqual(0, allocator.Live, "Dispose returns every rental to its allocator.");
    }

    /// <summary>An empty geometry rents no columns.</summary>
    [TestMethod]
    public void EmptyGeometryRentsNoColumns()
    {
        var allocator = new CountingAllocator();
        var allocators = new FlatGeometryAllocators(allocator.RentVertices, allocator.RentOrdinates);

        Assert.IsTrue(WktGeometryReader.TryRead("POINT EMPTY", allocators, out FlatGeometry geometry, out _));

        Assert.AreEqual(0, allocator.Live, "The empty point set has no columns to rent.");
        Assert.IsTrue(geometry.IsEmpty, "The typed empty still answers.");
    }

    /// <summary>An oversized rental breaks slice meaning and throws, never truncates.</summary>
    [TestMethod]
    public void OversizedRentalViolatesTheExactLengthContractLoudly()
    {
        var allocators = new FlatGeometryAllocators(OversizedAllocator.RentVertices, OversizedAllocator.RentOrdinates);

        Assert.Throws<InvalidOperationException>(
            () => WktGeometryReader.TryRead("POINT(1 2)", allocators, out _, out _),
            "An allocator returning more than the requested length breaks slice meaning and must throw, never truncate.");
    }

    /// <summary>The heap default's disposal is a no-op.</summary>
    [TestMethod]
    public void HeapDefaultDisposalIsANoOp()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT(1 2)", out FlatGeometry geometry, out _));

        geometry.Dispose();

        Assert.AreEqual("POINT (1 2)", WktGeometryWriter.WriteString(in geometry),
            "Heap-backed columns have no return path; disposal changes nothing.");
    }

    /// <summary>A pooling stand-in counting live rentals; methods bind as the seam's named delegates.</summary>
    private sealed class CountingAllocator
    {
        /// <summary>The rentals not yet returned.</summary>
        public int Live { get; set; }

        /// <summary>Rents a counted vertex column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
        public CountingOwner<Point2d> RentVertices(int length)
        {
            Live++;

            return new CountingOwner<Point2d>(this, new Point2d[length]);
        }

        /// <summary>Rents a counted ordinate column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
        public CountingOwner<double> RentOrdinates(int length)
        {
            Live++;

            return new CountingOwner<double>(this, new double[length]);
        }
    }

    /// <summary>A rental that reports its return to the counting allocator.</summary>
    private sealed class CountingOwner<T>(CountingAllocator allocator, T[] array): IMemoryOwner<T>
    {
        /// <summary>The allocator the return reports to.</summary>
        private CountingAllocator Allocator { get; } = allocator;

        /// <summary>The rented storage.</summary>
        private T[] Backing { get; } = array;

        /// <inheritdoc/>
        public Memory<T> Memory => Backing;

        /// <inheritdoc/>
        public void Dispose()
        {
            Allocator.Live--;
        }
    }

    /// <summary>An allocator that over-allocates by one element, violating the exact-length contract.</summary>
    private static class OversizedAllocator
    {
        /// <summary>Rents one element too many for the vertex column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
        public static CountingOwner<Point2d> RentVertices(int length)
        {
            return new CountingOwner<Point2d>(new CountingAllocator(), new Point2d[length + 1]);
        }

        /// <summary>Rents one element too many for an ordinate column; binds covariantly as a <see cref="ColumnAllocator{T}"/>.</summary>
        public static CountingOwner<double> RentOrdinates(int length)
        {
            return new CountingOwner<double>(new CountingAllocator(), new double[length + 1]);
        }
    }
}
