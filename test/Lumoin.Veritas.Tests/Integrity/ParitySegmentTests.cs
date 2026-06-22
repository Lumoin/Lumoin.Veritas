using System;
using System.Buffers;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Parity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Tests.MemoryPool;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The local-parity segment: <see cref="ParitySegment.BuildParity"/> folds the capacity-1 parity as the XOR of
/// every system-of-record block payload, the image round-trips through write and read with and without a
/// checksum, a decode-free verify reports the parity block's and front matter's verdicts, and the built parity
/// composes with <see cref="ParityCodec"/> to recover a lost system-of-record block — the byte-match between the
/// build-time block payloads and the serialized image's block payloads being what makes that recovery sound.
/// Geometry is the fixture's (10-item blocks, 64-byte aligned, XxHash3); every buffer is pool-rented.
/// </summary>
[TestClass]
internal sealed class ParitySegmentTests
{
    /// <summary>A system-of-record with three blocks: two full (10 items) and a short last block (5 items).</summary>
    private const uint TripleCount = 25;

    /// <summary>The fixture's items-per-block.</summary>
    private const int BlockItemCount = 10;

    /// <summary>The fixture's block alignment.</summary>
    private const int BlockAlignment = 64;

    [TestMethod]
    public void BuildParityIsTheXorOfEverySystemOfRecordBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);
        int stride = systemOfRecord.MaxBlockPayloadByteCount;

        using ParityBlock parity = ParityBlock.Rent(pool, stride);
        int protectedBlockCount = ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, pool);
        Assert.AreEqual(systemOfRecord.BlockCount, protectedBlockCount);

        //The reference parity is the naive byte-wise XOR of each block payload, zero-extended to the stride.
        using ParityBlock expected = ParityBlock.Rent(pool, stride);
        using ParityBlock blockScratch = ParityBlock.Rent(pool, stride);
        expected.WritableSpan.Clear();
        for(int block = 0; block < systemOfRecord.BlockCount; block++)
        {
            int payloadLength = systemOfRecord.BlockPayloadByteCount(block);
            Span<byte> payload = blockScratch.WritableSpan[..payloadLength];
            systemOfRecord.CopyBlockPayload(block, payload);
            for(int i = 0; i < payloadLength; i++)
            {
                expected.WritableSpan[i] ^= payload[i];
            }
        }

        Assert.IsTrue(parity.Span.SequenceEqual(expected.Span));
    }

    [TestMethod]
    public void WriteToThenReadFromRoundTripsTheParityBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        ItemSegment systemOfRecord = new(triples, BlockItemCount, BlockAlignment);

        using ParityBlock parity = ParityBlock.Rent(pool, systemOfRecord.MaxBlockPayloadByteCount);
        int protectedBlockCount = ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, pool);
        ParitySegment segment = new(parity.Memory, protectedBlockCount, BlockAlignment);

        using ArtifactImage image = WriteImage(segment, ChecksumAlgorithm.XxHash3, pool);
        using ParityBlock readBack = ParitySegment.ReadFrom(image.Bytes, pool);
        Assert.IsTrue(parity.Span.SequenceEqual(readBack.Span));
    }

    [TestMethod]
    public void WriteToThenReadFromRoundTripsWithoutAChecksum()
    {
        using VeritasMemoryPool<byte> pool = new();
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);

        using ParityBlock parity = ParityBlock.Rent(pool, systemOfRecord.MaxBlockPayloadByteCount);
        int protectedBlockCount = ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, pool);
        ParitySegment segment = new(parity.Memory, protectedBlockCount, BlockAlignment);

        using ArtifactImage image = WriteImage(segment, null, pool);
        using ParityBlock readBack = ParitySegment.ReadFrom(image.Bytes, pool);
        Assert.IsTrue(parity.Span.SequenceEqual(readBack.Span));
    }

    [TestMethod]
    public void ReadFromReturnsAnOwnedBlockReleasedOnDispose()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using PoisoningMemoryPool<byte> blockPool = new();
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), imagePool);

        using(ParityBlock block = ParitySegment.ReadFrom(image.Bytes, blockPool))
        {
            Assert.AreEqual(1, blockPool.OutstandingRentals);
            Assert.IsGreaterThan(0, block.Length);
        }

        Assert.AreEqual(0, blockPool.OutstandingRentals, "The owned parity block must return its buffer to the pool on dispose.");
    }

    [TestMethod]
    public void RunVerifyRoundReportsACleanImage()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), pool);

        ArtifactVerifyReport report = ParitySegment.RunVerifyRound(image.Bytes);
        Assert.IsTrue(report.IsClean);
        Assert.AreEqual(1, report.BlockCount);
        Assert.AreEqual(0, report.CorruptCount);
        Assert.IsTrue(report.FrontMatterValid);
    }

    [TestMethod]
    public void RunVerifyRoundDetectsACorruptParityBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), pool);
        CorruptParityBlock(image);

        ArtifactVerifyReport report = ParitySegment.RunVerifyRound(image.Bytes);
        Assert.IsFalse(report.IsClean);
        Assert.AreEqual(1, report.CorruptCount);
        Assert.IsFalse(report.Blocks.Span[0].IsValid);

        //The block payload changed, not the front matter, so the trailer still verifies.
        Assert.IsTrue(report.FrontMatterValid);
    }

    [TestMethod]
    public void RunVerifyRoundDetectsFrontMatterDamage()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), pool);
        CorruptParityFrontMatter(image);

        ArtifactVerifyReport report = ParitySegment.RunVerifyRound(image.Bytes);
        Assert.IsFalse(report.IsClean);
        Assert.IsFalse(report.FrontMatterValid);
    }

    [TestMethod]
    public void ReadFromRefusesACorruptParityBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), pool);
        GarbageParityBlock(image);

        Assert.ThrowsExactly<System.IO.InvalidDataException>(() => ParitySegment.ReadFrom(image.Bytes, pool));
    }

    [TestMethod]
    public void ReadFromRefusesATruncatedImage()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), pool);

        //Drop the final byte: the declared block now runs past the image, so the framing is refused.
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() => ParitySegment.ReadFrom(image.Bytes[..(image.Length - 1)], pool));
    }

    [TestMethod]
    public void ReadGeometryReportsTheParityLengthAndProtectedBlockCount()
    {
        using VeritasMemoryPool<byte> pool = new();
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);
        using ArtifactImage image = ParityImage(SampleTriples(TripleCount), pool);

        (int parityLength, int protectedBlockCount) = ParitySegment.ReadGeometry(image.Bytes);
        Assert.AreEqual(systemOfRecord.MaxBlockPayloadByteCount, parityLength);
        Assert.AreEqual(systemOfRecord.BlockCount, protectedBlockCount);
    }

    [TestMethod]
    public void EncodingThenRestoringRecoversEachLostSystemOfRecordBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);
        int stride = systemOfRecord.MaxBlockPayloadByteCount;
        int blockCount = systemOfRecord.BlockCount;

        using ParityBlock parity = ParityBlock.Rent(pool, stride);
        ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, pool);

        //Materialize every block payload once; a restore folds the survivors against the parity.
        ParityBlock[] blocks = new ParityBlock[blockCount];
        try
        {
            for(int block = 0; block < blockCount; block++)
            {
                int payloadLength = systemOfRecord.BlockPayloadByteCount(block);
                blocks[block] = ParityBlock.Rent(pool, payloadLength);
                systemOfRecord.CopyBlockPayload(block, blocks[block].WritableSpan);
            }

            using ParityBlock restored = ParityBlock.Rent(pool, stride);
            for(int lost = 0; lost < blockCount; lost++)
            {
                ReadOnlyMemory<byte>[] survivors = new ReadOnlyMemory<byte>[blockCount - 1];
                int s = 0;
                for(int block = 0; block < blockCount; block++)
                {
                    if(block != lost)
                    {
                        survivors[s++] = blocks[block].Memory;
                    }
                }

                ParityCodec.Restore(parity.Span, survivors, restored.WritableSpan);

                ReadOnlySpan<byte> lostPayload = blocks[lost].Span;
                Assert.IsTrue(restored.Span[..lostPayload.Length].SequenceEqual(lostPayload), $"lost block {lost} not recovered.");
                Assert.AreEqual(-1, restored.Span[lostPayload.Length..].IndexOfAnyExcept((byte)0), $"lost block {lost} padding not zero.");
            }
        }
        finally
        {
            foreach(ParityBlock block in blocks)
            {
                block?.Dispose();
            }
        }
    }

    [TestMethod]
    public void CopyBlockPayloadMatchesTheSerializedImageBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        ItemSegment systemOfRecord = new(triples, BlockItemCount, BlockAlignment);
        using ArtifactImage image = SegmentImage(triples, pool);

        //The image's block payloads are at the aligned offsets the fixture's corruptors use; the build-time
        //CopyBlockPayload bytes must match them, or a parity built one way cannot restore from the other.
        int blockCount = systemOfRecord.BlockCount;
        int frontMatterEnd = 19 + 12 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(systemOfRecord.MaxBlockPayloadByteCount);

        using ParityBlock blockScratch = ParityBlock.Rent(pool, systemOfRecord.MaxBlockPayloadByteCount);
        for(int block = 0; block < blockCount; block++)
        {
            int payloadLength = systemOfRecord.BlockPayloadByteCount(block);
            Span<byte> payload = blockScratch.WritableSpan[..payloadLength];
            systemOfRecord.CopyBlockPayload(block, payload);
            ReadOnlySpan<byte> imageBlock = image.Bytes.Slice(firstBlock + (block * stride), payloadLength);
            Assert.IsTrue(payload.SequenceEqual(imageBlock), $"block {block} copy does not match the image.");
        }
    }

    [TestMethod]
    public void ConstructorRejectsAnEmptyParity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => new ParitySegment(ReadOnlyMemory<byte>.Empty, 1));
    }

    [TestMethod]
    public void ConstructorRejectsANonPositiveProtectedBlockCount()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () => new ParitySegment(new byte[8], 0));
    }

    [TestMethod]
    public void BuildParityRejectsAnEmptySystemOfRecord()
    {
        using VeritasMemoryPool<byte> pool = new();
        ItemSegment empty = new(Array.Empty<EncodedTriple>(), BlockItemCount, BlockAlignment);

        Assert.ThrowsExactly<ArgumentException>(() => ParitySegment.BuildParity(empty, new byte[BlockItemCount * 12], pool));
    }

    [TestMethod]
    public void BuildParityRejectsAMisSizedDestination()
    {
        using VeritasMemoryPool<byte> pool = new();
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ParitySegment.BuildParity(systemOfRecord, new byte[5], pool));
    }

    [TestMethod]
    public void BlockPayloadByteCountRejectsAnOutOfRangeBlock()
    {
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => systemOfRecord.BlockPayloadByteCount(systemOfRecord.BlockCount));
    }

    [TestMethod]
    public void CopyBlockPayloadRejectsAShortDestination()
    {
        ItemSegment systemOfRecord = new(SampleTriples(TripleCount), BlockItemCount, BlockAlignment);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => systemOfRecord.CopyBlockPayload(0, new byte[1]));
    }

    /// <summary>Serializes a parity segment into a pooled <see cref="ArtifactImage"/>, the artifact-image semantic value the verify and read paths consume.</summary>
    /// <param name="segment">The segment to serialize.</param>
    /// <param name="checksum">The checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <returns>The pooled parity image.</returns>
    private static ArtifactImage WriteImage(ParitySegment segment, ChecksumAlgorithm? checksum, MemoryPool<byte> pool)
    {
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.Parity);
    }
}
