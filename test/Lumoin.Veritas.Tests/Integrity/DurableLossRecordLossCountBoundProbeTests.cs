using System;
using System.Buffers.Binary;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// Pins the loss-count allocation guard in <see cref="DurableLossRecord.TryRead"/>: the declared count is
/// bounded against the bytes that could actually carry entries BEFORE anything is allocated on it, so a
/// forged image with a valid header, a huge declared loss count, an empty body, and a freshly-recomputed
/// trailer (the trailer is a rot detector, not an authenticator — a constructing adversary always writes a
/// matching one) reads back as <see langword="null"/> under the documented malformed-image contract, never a
/// count-sized allocation.
/// </summary>
[TestClass]
internal sealed class DurableLossRecordLossCountBoundProbeTests
{
    /// <summary>The shared segment header size mirrored from SegmentContainer: magic (8) + major (1) + minor (1) + feature mask (8) + checksum id (1).</summary>
    private const int HeaderSize = 8 + 1 + 1 + 8 + 1;

    /// <summary>The byte offset of the 4-byte loss-count scalar: it follows the header and the 8-byte generation scalar.</summary>
    private const int LossCountOffset = HeaderSize + sizeof(long);

    /// <summary>
    /// A forged loss-record image whose header and trailer verify but whose declared loss count exceeds what
    /// the remaining front matter could hold reads back as <see langword="null"/> — the count bound runs in
    /// long arithmetic before the allocation, so no adversarial count sizes memory.
    /// </summary>
    [TestMethod]
    public void HugeDeclaredLossCountReadsBackNullWithoutAllocating()
    {
        ChecksumAlgorithm checksum = ChecksumAlgorithm.XxHash3;

        //Start from a valid loss-free image: correct magic/version/feature header, generation, a zero count, and a valid trailer.
        int size = DurableLossRecord.ComputeSerializedSize(Array.Empty<UnrecoverableItemReport>(), checksum);
        byte[] image = new byte[size];
        DurableLossRecord.WriteTo(image, generation: 1, Array.Empty<UnrecoverableItemReport>(), checksum);

        //Forge the declared loss count to the maximum while the body stays empty.
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(LossCountOffset), int.MaxValue);

        //Recompute the front-matter trailer over the tampered bytes so the non-keyed checksum verify accepts them.
        int frontMatterEnd = size - checksum.ByteWidth;
        checksum.Compute(image.AsSpan(0, frontMatterEnd), image.AsSpan(frontMatterEnd, checksum.ByteWidth));

        DurableLossRecord? record = DurableLossRecord.TryRead(image);

        Assert.IsNull(record, "A declared count the remaining front matter cannot hold is a malformed image, refused as null before any allocation.");
    }
}
