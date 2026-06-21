using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The format-neutral verifiable-artifact seam: the system-of-record item segment and the integrity sketch
/// each expose a decode-free <c>RunVerifyRound</c> yielding the uniform <see cref="ArtifactVerifyReport"/> a
/// scrub consumes, and the columnar sidecar's <see cref="VerifyRoundReport"/> projects onto the same shape. A
/// clean image reports every block valid; a corrupt block is reported (not thrown) so one walk records every
/// failure; front-matter rot is reported, not thrown; an unchecksummed image reports itself ungated; and
/// framing damage (an unparseable image) is still refused, since a geometry that cannot be parsed cannot be
/// walked. Every artifact image is an <see cref="ArtifactImage"/> over a buffer rented from the test's pool.
/// </summary>
[TestClass]
internal sealed class ArtifactVerifyReportTests
{
    /// <summary>The item segment's front matter: header (8 magic + 1 major + 1 minor + 8 feature mask + 1 algo id) + scalars (3×4) before the per-block checksum section.</summary>
    private const int ItemFrontMatterBase = 19 + 12;

    /// <summary>The sketch segment's front matter: the same header + scalars (4×4) before the per-block checksum section.</summary>
    private const int SketchFrontMatterBase = 19 + 16;

    /// <summary>The byte size of one item record: subject, predicate, object as three little-endian 32-bit ids.</summary>
    private const int ItemByteSize = 3 * sizeof(uint);

    /// <summary>A few hundred distinct triples so a segment spans several blocks.</summary>
    /// <param name="count">The triple count.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] SampleTriples(uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(i, (i * 7) + 1, (i * 13) + 2);
        }

        return triples;
    }

    /// <summary>Serializes an item segment into a pooled image.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="blockItemCount">The triples per block.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The pooled image.</returns>
    private static ArtifactImage ItemImage(EncodedTriple[] triples, int blockItemCount, ChecksumAlgorithm? checksum, MemoryPool<byte> pool)
    {
        ItemSegment segment = new(triples, blockItemCount, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.DataSegment);
    }

    /// <summary>Serializes a sketch segment over deterministic opaque symbol bytes into a pooled image.</summary>
    /// <param name="symbolCount">The number of symbols.</param>
    /// <param name="symbolWidth">The symbol byte width.</param>
    /// <param name="symbolsPerBlock">The symbols per block.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The pooled image.</returns>
    private static ArtifactImage SketchImage(int symbolCount, int symbolWidth, int symbolsPerBlock, ChecksumAlgorithm? checksum, MemoryPool<byte> pool)
    {
        int symbolBytes = symbolCount * symbolWidth;
        using IMemoryOwner<byte> symbolOwner = pool.Rent(Math.Max(1, symbolBytes));
        Span<byte> symbols = symbolOwner.Memory.Span[..symbolBytes];
        for(int i = 0; i < symbols.Length; i++)
        {
            symbols[i] = (byte)((i * 31) + 7);
        }

        SketchSegment segment = new(symbolOwner.Memory[..symbolBytes], symbolWidth, symbolsPerBlock, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.Sketch);
    }

    /// <summary>The byte offset of the first block: the front matter plus the per-block checksum section, rounded up to the alignment.</summary>
    /// <param name="frontMatterBase">The header + scalar size before the per-block section.</param>
    /// <param name="blockCount">The block count.</param>
    /// <param name="checksumWidth">The checksum byte width.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The first block's byte offset.</returns>
    private static int FirstBlockOffset(int frontMatterBase, int blockCount, int checksumWidth, int alignment)
    {
        int frontMatterEnd = frontMatterBase + (blockCount * checksumWidth);

        return (frontMatterEnd + alignment - 1) / alignment * alignment;
    }

    /// <summary>The aligned byte stride between block starts.</summary>
    /// <param name="blockPayloadBytes">A full block's payload byte count.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The block stride.</returns>
    private static long BlockStride(long blockPayloadBytes, int alignment)
    {
        return (blockPayloadBytes + alignment - 1) / alignment * alignment;
    }

    /// <summary>A clean item segment reports every block valid, the front matter valid, and the whole report clean.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundCleanReportsAllBlocksValid()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage(SampleTriples(30), blockItemCount: 10, ChecksumAlgorithm.XxHash3, pool);

        ArtifactVerifyReport report = ItemSegment.RunVerifyRound(image.Bytes);

        Assert.AreEqual(3, report.BlockCount);
        Assert.AreEqual(0, report.CorruptCount);
        Assert.IsTrue(report.HasChecksums);
        Assert.IsTrue(report.FrontMatterValid);
        Assert.IsTrue(report.IsClean);
        foreach(BlockVerdict verdict in report.Blocks.Span)
        {
            Assert.IsTrue(verdict.IsValid);
        }
    }

    /// <summary>A byte flipped in one item block is reported as that block's failure without throwing, leaving the other blocks valid.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundReportsACorruptBlockWithoutThrowing()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage(SampleTriples(30), blockItemCount: 10, ChecksumAlgorithm.XxHash3, pool);

        //Flip the first payload byte of block 1.
        int firstBlock = FirstBlockOffset(ItemFrontMatterBase, 3, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        image.WritableBytes[firstBlock + (int)BlockStride(10L * ItemByteSize, 64)] ^= 0xFF;

        ArtifactVerifyReport report = ItemSegment.RunVerifyRound(image.Bytes);

        Assert.IsFalse(report.IsClean);
        Assert.AreEqual(1, report.CorruptCount);
        Assert.IsFalse(report.Blocks.Span[1].IsValid, "Block 1 must be reported corrupt.");
        Assert.IsTrue(report.Blocks.Span[0].IsValid);
        Assert.IsTrue(report.Blocks.Span[2].IsValid);
        Assert.AreEqual(1, report.Blocks.Span[1].BlockIndex);
    }

    /// <summary>Front-matter rot is reported (the front-matter verdict is false) rather than thrown — the scrub records a corrupt artifact instead of crashing the walk.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundReportsFrontMatterRotWithoutThrowing()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage(SampleTriples(30), blockItemCount: 10, ChecksumAlgorithm.XxHash3, pool);

        //Corrupt the trailing front-matter digest itself — inside the trailer's coverage but outside every block
        //payload and the per-block section, so ONLY the front-matter verdict flips and every block stays valid.
        image.WritableBytes[^1] ^= 0xFF;

        ArtifactVerifyReport report = ItemSegment.RunVerifyRound(image.Bytes);

        Assert.IsFalse(report.FrontMatterValid, "Front-matter rot must be reported, not thrown.");
        Assert.AreEqual(0, report.CorruptCount, "Only the front matter is corrupt; every block stays valid.");
        Assert.IsFalse(report.IsClean);
        foreach(BlockVerdict verdict in report.Blocks.Span)
        {
            Assert.IsTrue(verdict.IsValid);
        }
    }

    /// <summary>A magic mismatch is unparseable geometry the verify still refuses outright, not a reportable per-block verdict.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundRefusesFramingDamage()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage(SampleTriples(30), blockItemCount: 10, ChecksumAlgorithm.XxHash3, pool);
        image.WritableBytes[0] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.RunVerifyRound(image.Bytes); });
    }

    /// <summary>An unchecksummed image reports itself ungated: every block reads valid (nothing to verify against), but the report is not clean because it carried no checksums.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundReportsNotGatedWithoutChecksums()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage(SampleTriples(30), blockItemCount: 10, null, pool);

        ArtifactVerifyReport report = ItemSegment.RunVerifyRound(image.Bytes);

        Assert.IsFalse(report.HasChecksums);
        Assert.IsFalse(report.IsClean, "An unchecksummed image cannot be clean-because-verified.");
        Assert.AreEqual(0, report.CorruptCount);
    }

    /// <summary>A clean sketch segment reports every block valid, the front matter valid, and the whole report clean.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundCleanReportsAllBlocksValid()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 10, symbolWidth: 24, symbolsPerBlock: 4, ChecksumAlgorithm.XxHash3, pool);

        ArtifactVerifyReport report = SketchSegment.RunVerifyRound(image.Bytes);

        Assert.AreEqual(3, report.BlockCount);
        Assert.AreEqual(0, report.CorruptCount);
        Assert.IsTrue(report.HasChecksums);
        Assert.IsTrue(report.FrontMatterValid);
        Assert.IsTrue(report.IsClean);
    }

    /// <summary>A byte flipped in one sketch block is reported as that block's failure without throwing.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundReportsACorruptBlockWithoutThrowing()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 10, symbolWidth: 24, symbolsPerBlock: 4, ChecksumAlgorithm.XxHash3, pool);

        //Flip the first payload byte of block 1 (symbols [4, 8)); a full block is 4 symbols × 24 bytes = 96 payload bytes.
        int firstBlock = FirstBlockOffset(SketchFrontMatterBase, 3, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        image.WritableBytes[firstBlock + (int)BlockStride(4L * 24, 64)] ^= 0xFF;

        ArtifactVerifyReport report = SketchSegment.RunVerifyRound(image.Bytes);

        Assert.IsFalse(report.IsClean);
        Assert.AreEqual(1, report.CorruptCount);
        Assert.IsFalse(report.Blocks.Span[1].IsValid, "Block 1 must be reported corrupt.");
        Assert.IsTrue(report.Blocks.Span[0].IsValid);
        Assert.IsTrue(report.Blocks.Span[2].IsValid);
    }

    /// <summary>A magic mismatch is refused by the sketch verify, not read as a degraded report.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundRefusesFramingDamage()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 10, symbolWidth: 24, symbolsPerBlock: 4, ChecksumAlgorithm.XxHash3, pool);
        image.WritableBytes[0] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.RunVerifyRound(image.Bytes); });
    }

    /// <summary>Front-matter rot in a sketch image is reported (the front-matter verdict is false) without throwing, leaving every block valid — the report-not-throw front-matter path on the second refactored format.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundReportsFrontMatterRotWithoutThrowing()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 10, symbolWidth: 24, symbolsPerBlock: 4, ChecksumAlgorithm.XxHash3, pool);

        //Corrupt the trailing front-matter digest — inside the trailer's coverage but outside every block payload.
        image.WritableBytes[^1] ^= 0xFF;

        ArtifactVerifyReport report = SketchSegment.RunVerifyRound(image.Bytes);

        Assert.IsFalse(report.FrontMatterValid, "Front-matter rot must be reported, not thrown.");
        Assert.AreEqual(0, report.CorruptCount, "Only the front matter is corrupt; every block stays valid.");
        Assert.IsFalse(report.IsClean);
    }

    /// <summary>An unchecksummed sketch image reports itself ungated: every block reads valid, but the report is not clean because it carried no checksums — the clean-because-verified vs clean-because-nothing-to-gate distinction on the second format.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundReportsNotGatedWithoutChecksums()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 10, symbolWidth: 24, symbolsPerBlock: 4, null, pool);

        ArtifactVerifyReport report = SketchSegment.RunVerifyRound(image.Bytes);

        Assert.IsFalse(report.HasChecksums);
        Assert.IsFalse(report.IsClean, "An unchecksummed sketch image cannot be clean-because-verified.");
        Assert.AreEqual(0, report.CorruptCount);
    }

    /// <summary>A truncated item image is refused by the decode-free verify — the framing-truncation guard propagates rather than folding into a report.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundRefusesTruncation()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage(SampleTriples(30), blockItemCount: 10, ChecksumAlgorithm.XxHash3, pool);
        using ArtifactImage truncated = image.Truncated(100, pool);

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.RunVerifyRound(truncated.Bytes); });
    }

    /// <summary>A truncated sketch image is refused by the decode-free verify.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundRefusesTruncation()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 10, symbolWidth: 24, symbolsPerBlock: 4, ChecksumAlgorithm.XxHash3, pool);
        using ArtifactImage truncated = image.Truncated(100, pool);

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.RunVerifyRound(truncated.Bytes); });
    }

    /// <summary>An empty but checksummed item image is clean-because-verified: zero blocks, a valid front matter, and a clean report — distinct from the empty-ungated case where the absence of checksums forces not-clean.</summary>
    [TestMethod]
    public void ItemSegmentRunVerifyRoundEmptyGatedIsClean()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = ItemImage([], blockItemCount: 8, ChecksumAlgorithm.XxHash3, pool);

        ArtifactVerifyReport report = ItemSegment.RunVerifyRound(image.Bytes);

        Assert.AreEqual(0, report.BlockCount);
        Assert.IsTrue(report.HasChecksums);
        Assert.IsTrue(report.FrontMatterValid);
        Assert.IsTrue(report.IsClean, "An empty checksummed image is clean-because-verified.");
    }

    /// <summary>An empty but checksummed sketch image is clean-because-verified.</summary>
    [TestMethod]
    public void SketchSegmentRunVerifyRoundEmptyGatedIsClean()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage image = SketchImage(symbolCount: 0, symbolWidth: 24, symbolsPerBlock: 4, ChecksumAlgorithm.XxHash3, pool);

        ArtifactVerifyReport report = SketchSegment.RunVerifyRound(image.Bytes);

        Assert.AreEqual(0, report.BlockCount);
        Assert.IsTrue(report.HasChecksums);
        Assert.IsTrue(report.FrontMatterValid);
        Assert.IsTrue(report.IsClean, "An empty checksummed sketch image is clean-because-verified.");
    }

    /// <summary>The columnar verdict projects onto the format-neutral report, keeping each blob's image coordinates and validity and folding into the same clean / corrupt-count semantics.</summary>
    [TestMethod]
    public void VerifyRoundReportToArtifactReportMapsBlobsAndVerdicts()
    {
        BlobVerdict[] blobs =
        [
            new(0, OrderIndex: 0, Level: 0, Role: 0, ByteOffset: 64, ByteLength: 128, IsValid: true),
            new(1, OrderIndex: 0, Level: 1, Role: 1, ByteOffset: 192, ByteLength: 256, IsValid: false),
            new(2, OrderIndex: 1, Level: 0, Role: 0, ByteOffset: 448, ByteLength: 64, IsValid: true),
        ];
        VerifyRoundReport columnar = new(checksumAlgorithmId: 1, hasChecksums: true, hasFrontMatterChecksum: true, frontMatterValid: true, blobs);

        ArtifactVerifyReport report = columnar.ToArtifactReport();

        Assert.AreEqual(3, report.BlockCount);
        Assert.AreEqual(1, report.CorruptCount);
        Assert.IsFalse(report.IsClean);
        Assert.IsTrue(report.HasChecksums);
        Assert.IsTrue(report.FrontMatterValid);
        Assert.AreEqual(1, report.ChecksumAlgorithmId);
        Assert.AreEqual(new BlockVerdict(0, 64, 128, true), report.Blocks.Span[0]);
        Assert.AreEqual(new BlockVerdict(1, 192, 256, false), report.Blocks.Span[1]);
        Assert.AreEqual(new BlockVerdict(2, 448, 64, true), report.Blocks.Span[2]);
    }
}
