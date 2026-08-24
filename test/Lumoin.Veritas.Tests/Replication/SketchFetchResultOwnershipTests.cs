using System;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Tests.MemoryPool;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The owned sketch-fetch result's disposal and stamp contract: the unavailable value carries no frame and disposes
/// as a no-op; a stamped image reports its domain and epoch, exposes its bytes, and returns its single pooled rental
/// exactly once on disposal; and a stamped decline is a frame that carries no image — distinct from the absent-peer
/// value — yet holds no rental. Proven through a pool that counts outstanding rentals.
/// </summary>
[TestClass]
internal sealed class SketchFetchResultOwnershipTests
{
    /// <summary>The dictionary epoch the stamped results in these tests carry.</summary>
    private const ulong DictionaryEpoch = 7;

    /// <summary>The unavailable result carries no frame and disposing it is a no-op, so a governance or drop decline never touches a pool.</summary>
    [TestMethod]
    public void UnavailableResultIsEmptyAndDisposesAsANoOp()
    {
        SketchFetchResult unavailable = SketchFetchResult.Unavailable;
        Assert.IsTrue(unavailable.IsUnavailable, "The unavailable result reports itself unavailable.");
        Assert.IsFalse(unavailable.HasImage, "The unavailable result carries no image.");
        Assert.IsTrue(unavailable.Image.IsEmpty, "The unavailable result exposes an empty image.");
        Assert.AreEqual(SketchChannelDomain.None, unavailable.Domain, "The unavailable result carries no wire domain.");

        unavailable.Dispose();
        unavailable.Dispose();
        Assert.IsTrue(unavailable.IsUnavailable, "Disposing the unavailable result is a no-op.");
    }

    /// <summary>A stamped image reports its domain and epoch, is available, exposes its bytes, and holds exactly one pooled rental until disposal returns it, so the receiver disposes exactly once.</summary>
    [TestMethod]
    public void StampedImageExposesBytesAndReleasesItsRentalOnDispose()
    {
        using PoisoningMemoryPool<byte> pool = new();
        byte[] bytes = [1, 2, 3, 4, 5];

        SketchFetchResult result = SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, DictionaryEpoch, bytes, pool);
        Assert.IsFalse(result.IsUnavailable, "A stamped image is available.");
        Assert.IsTrue(result.HasImage, "A stamped image carries an image.");
        Assert.AreEqual(SketchChannelDomain.Structural, result.Domain, "The stamped image carries its domain.");
        Assert.AreEqual(DictionaryEpoch, result.DictionaryEpoch, "The stamped image carries its epoch.");
        Assert.IsTrue(result.Image.Span.SequenceEqual(bytes), "The image exposes the copied bytes.");
        Assert.AreEqual(1, pool.OutstandingRentals, "The owned image holds exactly one pooled rental before disposal.");

        result.Dispose();
        Assert.AreEqual(0, pool.OutstandingRentals, "Disposing the owned image returns its rental to the pool.");
    }

    /// <summary>A stamped decline carries a domain and epoch but no image, so it is NOT unavailable (a frame arrived), holds no rental, and disposes as a no-op — the distinct state the session reads as an unavailable peer for the round.</summary>
    [TestMethod]
    public void StampedDeclineIsAFrameWithNoImageAndHoldsNoRental()
    {
        using PoisoningMemoryPool<byte> pool = new();

        SketchFetchResult decline = SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, DictionaryEpoch, ReadOnlyMemory<byte>.Empty, pool);
        Assert.IsFalse(decline.IsUnavailable, "A stamped decline is a frame, so it is not unavailable.");
        Assert.IsFalse(decline.HasImage, "A stamped decline carries no image.");
        Assert.AreEqual(SketchChannelDomain.Structural, decline.Domain, "The stamped decline carries its domain.");
        Assert.AreEqual(DictionaryEpoch, decline.DictionaryEpoch, "The stamped decline carries its epoch.");
        Assert.AreEqual(0, pool.OutstandingRentals, "A stamped decline rents nothing.");

        decline.Dispose();
        Assert.AreEqual(0, pool.OutstandingRentals, "Disposing a stamped decline is a no-op.");
    }
}
