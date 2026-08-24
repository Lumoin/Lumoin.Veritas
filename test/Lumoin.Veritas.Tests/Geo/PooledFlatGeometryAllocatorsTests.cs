using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The <see cref="PooledFlatGeometryAllocators"/> binding of the column-allocator
/// seam: a pooled parse rents its vertex-scale columns from the caller-owned
/// <see cref="VeritasMemoryPool{T}"/> instances, the built geometry returns every
/// rental on dispose, and empties rent nothing. Slab reclamation through
/// <see cref="VeritasMemoryPool{T}.TrimExcess"/> is the observable: an idle slab
/// reclaims only when all of its rentals came back.
/// </summary>
[TestClass]
internal sealed class PooledFlatGeometryAllocatorsTests
{
    /// <summary>A pooled parse rents from the bound pools and disposal returns every rental.</summary>
    [TestMethod]
    public void PooledColumnsComeFromTheBoundPoolsAndReturnOnDispose()
    {
        using var vertexPool = new VeritasMemoryPool<Point2d>();
        using var ordinatePool = new VeritasMemoryPool<double>();
        var binder = new PooledFlatGeometryAllocators(vertexPool, ordinatePool);

        Assert.IsTrue(WktGeometryReader.TryRead("POINT Z (1 2 3)", binder.Allocators, out FlatGeometry geometry, out _),
            "The pooled parse must succeed.");

        Assert.AreEqual(3.0, geometry.ZOrdinates[0], "The pooled columns carry the parsed values.");
        Assert.AreEqual(0, vertexPool.TrimExcess(), "The vertex column is still rented, so its slab must not reclaim.");
        Assert.AreEqual(0, ordinatePool.TrimExcess(), "The Z column is still rented, so its slab must not reclaim.");

        geometry.Dispose();

        Assert.AreEqual(1, vertexPool.TrimExcess(), "Disposing the geometry returns the vertex rental; the idle slab reclaims.");
        Assert.AreEqual(1, ordinatePool.TrimExcess(), "Disposing the geometry returns the ordinate rental; the idle slab reclaims.");
    }

    /// <summary>An empty geometry rents nothing from the bound pools.</summary>
    [TestMethod]
    public void EmptyGeometryRentsNothingFromThePools()
    {
        using var vertexPool = new VeritasMemoryPool<Point2d>();
        using var ordinatePool = new VeritasMemoryPool<double>();
        var binder = new PooledFlatGeometryAllocators(vertexPool, ordinatePool);

        Assert.IsTrue(WktGeometryReader.TryRead("POINT EMPTY", binder.Allocators, out FlatGeometry geometry, out _),
            "The typed empty must parse.");

        Assert.IsTrue(geometry.IsEmpty, "The typed empty still answers.");
        Assert.AreEqual(0, vertexPool.TrimExcess(), "No vertex column exists to rent, so no slab was created.");
        Assert.AreEqual(0, ordinatePool.TrimExcess(), "No ordinate column exists to rent, so no slab was created.");
    }
}
