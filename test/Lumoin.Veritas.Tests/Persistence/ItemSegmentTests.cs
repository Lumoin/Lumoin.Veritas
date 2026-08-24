using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Tests.Integrity;
using Lumoin.Veritas.Tests.MemoryPool;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The system-of-record row-major item segment: triples round-trip through fixed-count item blocks
/// across the checksum selections; a block's checksum failure is detected and names the exact item
/// range it covers (the item-aligned detection a column blob cannot give); front-matter rot, truncation,
/// and a foreign algorithm are refused; blocks begin on a page boundary; and the columnar index is a
/// re-derivable sidecar rebuilt from the segment's triples.
/// </summary>
[TestClass]
internal sealed class ItemSegmentTests
{
    /// <summary>The header (8 magic + 1 major + 1 minor + 8 feature mask + 1 algo id) and scalar (3×4) byte size before the per-block checksum section — the layout the corruption cells target.</summary>
    private const int FrontMatterBase = 19 + 12;

    /// <summary>The byte offset of the required-feature mask's least-significant byte (magic 8 + major 1 + minor 1); its bit 0 is the front-matter-checksum feature.</summary>
    private const int FeatureMaskOffset = 8 + 1 + 1;

    /// <summary>A few hundred distinct triples so the segment spans many blocks with a partial last block.</summary>
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

    /// <summary>Serializes a segment into a buffer rented from the caller's pool and returns it as a pooled, owned image — the data-segment artifact — rather than copying the bytes out to a loose array.</summary>
    /// <param name="segment">The segment to serialize.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="imagePool">The pool the image buffer is rented from; the returned image owns the buffer and returns it on dispose.</param>
    /// <returns>The pooled image; the caller disposes it.</returns>
    private static ArtifactImage ToImage(ItemSegment segment, ChecksumAlgorithm? checksum, MemoryPool<byte> imagePool)
    {
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = imagePool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.DataSegment);
    }

    /// <summary>The byte offset of the first item block: the front matter plus the per-block checksum section, rounded up to the alignment.</summary>
    /// <param name="blockCount">The block count.</param>
    /// <param name="checksumWidth">The checksum byte width.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The first block's byte offset.</returns>
    private static int FirstBlockOffset(int blockCount, int checksumWidth, int alignment)
    {
        int frontMatterEnd = FrontMatterBase + (blockCount * checksumWidth);

        return (frontMatterEnd + alignment - 1) / alignment * alignment;
    }

    /// <summary>Triples round-trip in order through the segment across the checksum selections, with the expected block count.</summary>
    [TestMethod]
    public void RoundTripsInOrderAcrossChecksumSelections()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            EncodedTriple[] triples = SampleTriples(100);
            ItemSegment segment = new(triples, blockItemCount: 7, blockAlignment: 64);
            int blockCount = segment.BlockCount;
            Assert.AreEqual(15, blockCount, $"100 triples in 7-item blocks is 15 blocks (algorithm {checksum?.Name ?? "none"}).");

            using ArtifactImage image = ToImage(segment, checksum, imagePool);
            using DecodedItemSegment restored = ItemSegment.ReadFrom(image.Bytes, triplePool);

            Assert.AreEqual(triples.Length, restored.Length);
            Assert.IsTrue(triples.AsSpan().SequenceEqual(restored.Span), $"The triples did not round-trip in order (algorithm {checksum?.Name ?? "none"}).");
        }
    }

    /// <summary>The size identity: <see cref="ItemSegment.ComputeSerializedSize"/> is exactly the bytes <c>WriteTo</c> lays down and exactly the bytes <c>ReadFrom</c> consumes, across the checksum selections and the empty, exact-multiple, and partial-last-block geometries; a sentinel tail past the computed size is neither written by the writer nor required by the reader.</summary>
    [TestMethod]
    public void SerializedSizeIsExactAndConsumedExactlyAcrossGeometries()
    {
        const byte Sentinel = 0xAB;
        const int Slack = 37;
        (uint Count, int BlockItemCount)[] geometries = [(0u, 8), (14u, 7), (100u, 7)];

        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            foreach((uint count, int blockItemCount) in geometries)
            {
                EncodedTriple[] triples = SampleTriples(count);
                ItemSegment segment = new(triples, blockItemCount, blockAlignment: 64);
                int size = (int)segment.ComputeSerializedSize(checksum);

                //Pre-fill an over-sized buffer with a sentinel and hand WriteTo only the leading `size` bytes:
                //the writer must fill exactly that prefix and never touch the sentinel tail.
                using IMemoryOwner<byte> owner = pool.Rent(size + Slack);
                Span<byte> buffer = owner.Memory.Span[..(size + Slack)];
                buffer.Fill(Sentinel);
                segment.WriteTo(buffer[..size], checksum);

                for(int i = size; i < buffer.Length; i++)
                {
                    Assert.AreEqual(Sentinel, buffer[i], $"WriteTo wrote past the computed size (algorithm {checksum?.Name ?? "none"}, {count}/{blockItemCount}).");
                }

                //ReadFrom over the whole over-sized buffer recovers the triples, so it consumes exactly the
                //declared total (== size) and tolerates the sentinel tail rather than reading to the end.
                using DecodedItemSegment restored = ItemSegment.ReadFrom(buffer, triplePool);
                Assert.AreEqual(triples.Length, restored.Length);
                Assert.IsTrue(triples.AsSpan().SequenceEqual(restored.Span), $"The exact-size image did not round-trip (algorithm {checksum?.Name ?? "none"}, {count}/{blockItemCount}).");
            }
        }
    }

    /// <summary>I1 / item-aligned detection: a byte flipped in a block's items fails that block's checksum, and the refusal names the exact item range the block covers — under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void BlockCorruptionIsDetectedAndNamesTheItemRange()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            EncodedTriple[] triples = SampleTriples(30);
            ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
            using ArtifactImage image = ToImage(segment, checksum, imagePool);

            //Flip the first item byte of block 1 — items [10, 20).
            int firstBlock = FirstBlockOffset(segment.BlockCount, checksum.ByteWidth, 64);
            long blockBytes = ((10L * 12) + 63) / 64 * 64;
            image.WritableBytes[firstBlock + (int)blockBytes] ^= 0xFF;

            InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(image.Bytes, triplePool));
            Assert.IsTrue(thrown.Message.Contains("[10, 20)", StringComparison.Ordinal), $"The refusal did not name the corrupt block's item range (algorithm {checksum.Name}): {thrown.Message}");
        }
    }

    /// <summary>The remainder edge of item-aligned detection: corrupting the partial last block names <c>[start, start + remainder)</c>, not the block stride <c>[start, start + blockItemCount)</c>.</summary>
    [TestMethod]
    public void PartialLastBlockCorruptionNamesTheRemainderRange()
    {
        //25 triples in 10-item blocks: blocks [0, 10), [10, 20), [20, 25) — the last holds the 5-item remainder.
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(25);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        //Flip the first item byte of the partial last block (block 2).
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        long blockBytes = ((10L * 12) + 63) / 64 * 64;
        image.WritableBytes[firstBlock + (int)(2 * blockBytes)] ^= 0xFF;

        InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(image.Bytes, triplePool));
        Assert.IsTrue(thrown.Message.Contains("[20, 25)", StringComparison.Ordinal), $"The refusal did not name the partial last block's remainder range: {thrown.Message}");
        Assert.IsFalse(thrown.Message.Contains("[20, 30)", StringComparison.Ordinal), $"The refusal named the block stride instead of the remainder: {thrown.Message}");
    }

    /// <summary>The per-block checksum domain is exactly the block's payload, not its aligned stride: a byte flipped in a block's trailing alignment padding is outside every checksum and the image still round-trips — the property that keeps one block's detection from reaching into a neighbour's bytes.</summary>
    [TestMethod]
    public void InPaddingCorruptionIsInert()
    {
        //10-item blocks at 64-byte alignment: payload 120 bytes, stride Align(120, 64) = 128 — 8 padding bytes per block.
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        //Flip a byte in block 0's padding (just past its 120-byte payload), outside both that block's
        //checksum domain and the front-matter trailer.
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        image.WritableBytes[firstBlock + (10 * 12)] ^= 0xFF;

        using DecodedItemSegment restored = ItemSegment.ReadFrom(image.Bytes, triplePool);
        Assert.IsTrue(triples.AsSpan().SequenceEqual(restored.Span), "An inert padding-byte corruption was not tolerated, so the per-block checksum domain spans the padded stride rather than the payload.");
    }

    /// <summary>A byte corrupted in the front matter (the per-block checksum section it covers) fails the front-matter trailer, under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void FrontMatterCorruptionIsDetected()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            EncodedTriple[] triples = SampleTriples(30);
            ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
            using ArtifactImage image = ToImage(segment, checksum, imagePool);

            //The per-block checksum section begins right after the scalars and is covered by the front-matter trailer.
            image.WritableBytes[FrontMatterBase] ^= 0xFF;

            Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(image.Bytes, triplePool));
        }
    }

    /// <summary>A truncated image is refused rather than read past its end.</summary>
    [TestMethod]
    public void TruncatedImageIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);
        using ArtifactImage truncated = image.Truncated(100, imagePool);

        Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(truncated.Bytes, triplePool));
    }

    /// <summary>An image stamped with a checksum-algorithm id no resolver knows is refused, not read under the wrong algorithm.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        //The checksum-algorithm id is the last header byte: magic (8) + major (1) + minor (1) + feature mask (8).
        image.WritableBytes[18] = 99;

        Assert.ThrowsExactly<NotSupportedException>(() => ItemSegment.ReadFrom(image.Bytes, triplePool));
    }

    /// <summary>The header's front-matter-checksum feature flag must agree with the presence of a checksum-algorithm id: a checksummed image with the flag cleared, or a no-checksum image with the flag set, is a header inconsistency the reader refuses.</summary>
    [TestMethod]
    public void FeatureFlagDisagreeingWithChecksumIdIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);

        //A checksummed image with the feature bit cleared (the algorithm id still present) is inconsistent.
        using ArtifactImage flagCleared = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);
        flagCleared.WritableBytes[FeatureMaskOffset] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(flagCleared.Bytes, triplePool));

        //A no-checksum image with the feature bit set (no algorithm id) is the symmetric inconsistency.
        using ArtifactImage flagSet = ToImage(segment, null, imagePool);
        flagSet.WritableBytes[FeatureMaskOffset] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(flagSet.Bytes, triplePool));
    }

    /// <summary>An empty segment round-trips to an empty triple set.</summary>
    [TestMethod]
    public void EmptySegmentRoundTrips()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        ItemSegment segment = new(ReadOnlyMemory<EncodedTriple>.Empty, blockItemCount: 8, blockAlignment: 64);
        int blockCount = segment.BlockCount;
        Assert.AreEqual(0, blockCount);

        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);
        using DecodedItemSegment restored = ItemSegment.ReadFrom(image.Bytes, triplePool);

        Assert.AreEqual(0, restored.Length);
    }

    /// <summary>Each item block begins on the default page boundary, so the image lays the blocks out page-aligned.</summary>
    [TestMethod]
    public void BlocksAreDefaultPageAligned()
    {
        EncodedTriple[] triples = SampleTriples(20);
        ItemSegment segment = new(triples, blockItemCount: 8);
        Assert.AreEqual(ItemSegment.DefaultBlockAlignment, segment.BlockAlignment);
        int blockCount = segment.BlockCount;
        Assert.AreEqual(3, blockCount);

        //First block at page 1 (the front matter fits in well under a page); three page-sized blocks; an 8-byte trailer.
        long expected = ItemSegment.DefaultBlockAlignment + (3L * ItemSegment.DefaultBlockAlignment) + ChecksumAlgorithm.XxHash3.ByteWidth;
        Assert.AreEqual(expected, segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3));

        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using IMemoryOwner<byte> owner = pool.Rent((int)expected);
        Span<byte> image = owner.Memory.Span[..(int)expected];
        segment.WriteTo(image, ChecksumAlgorithm.XxHash3);
        using DecodedItemSegment restored = ItemSegment.ReadFrom(image, triplePool);
        Assert.IsTrue(triples.AsSpan().SequenceEqual(restored.Span));

        //Observe the page alignment directly in the bytes rather than only through the size formula: each
        //block begins on a 4 KiB multiple, and the first record of block k decodes to that block's first
        //triple (whose subject is k * blockItemCount under the sample generator).
        long blockBytes = (((8L * 12) + ItemSegment.DefaultBlockAlignment - 1) / ItemSegment.DefaultBlockAlignment) * ItemSegment.DefaultBlockAlignment;
        for(int block = 0; block < blockCount; block++)
        {
            long blockOffset = ItemSegment.DefaultBlockAlignment + (block * blockBytes);
            Assert.AreEqual(0L, blockOffset % ItemSegment.DefaultBlockAlignment, $"Block {block} does not begin on a page boundary.");

            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(image[(int)blockOffset..]);
            Assert.AreEqual((uint)(block * 8), subject, $"Block {block}'s first record is not its first triple.");
        }
    }

    /// <summary>The columnar index is a re-derivable sidecar: rebuilding it from the segment's recovered triples yields the same triple set — the system-of-record is the truth, the index is rebuilt from it.</summary>
    [TestMethod]
    public void ColumnarIndexIsRederivableFromTheSegment()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(100);
        ItemSegment segment = new(triples, blockItemCount: 7, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        using DecodedItemSegment recovered = ItemSegment.ReadFrom(image.Bytes, triplePool);
        ColumnarTripleIndex sidecar = ColumnarTripleIndex.Build(MemoryMarshal.ToEnumerable(recovered.Memory));

        HashSet<EncodedTriple> expected = [.. triples];
        HashSet<EncodedTriple> actual = [.. sidecar.EnumerateTriples()];
        Assert.IsTrue(expected.SetEquals(actual), "The columnar sidecar rebuilt from the segment differs from the system-of-record triples.");
    }

    /// <summary>The all-or-nothing read hands back a pooled, owned segment that returns its buffer to the pool on dispose — proven under a poisoning pool that counts outstanding rentals.</summary>
    [TestMethod]
    public void ReadFromReturnsAnOwnedSegmentReleasedOnDispose()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using PoisoningMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        using(DecodedItemSegment decoded = ItemSegment.ReadFrom(image.Bytes, triplePool))
        {
            Assert.AreEqual(1, triplePool.OutstandingRentals);
            Assert.AreEqual(triples.Length, decoded.Length);
        }

        Assert.AreEqual(0, triplePool.OutstandingRentals, "The decoded item segment must return its buffer to the pool on dispose.");
    }

    /// <summary>The byte size of one row-major item record: subject, predicate, object as three little-endian 32-bit ids.</summary>
    private const int ItemByteSize = 3 * sizeof(uint);

    /// <summary>The aligned byte stride between block starts for the given geometry.</summary>
    /// <param name="blockItemCount">The triples per block.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The block stride.</returns>
    private static long BlockStride(int blockItemCount, int alignment)
    {
        return (((long)blockItemCount * ItemByteSize) + alignment - 1) / alignment * alignment;
    }

    /// <summary>The checksum-gated feed read returns every triple in stored order, with no exclusions, when every block passes.</summary>
    [TestMethod]
    public void ReadVerifiedItemsReturnsAllTriplesWhenClean()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(100);
        ItemSegment segment = new(triples, blockItemCount: 7, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        using VeritasMemoryPool<EncodedTriple> pool = new();
        using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool);

        Assert.AreEqual(triples.Length, feed.VerifiedCount);
        Assert.IsTrue(feed.IsClean);
        Assert.IsTrue(feed.WasChecksumGated, "A checksummed segment's clean read must report itself as gated.");
        Assert.IsEmpty(feed.SkippedRanges);
        Assert.IsTrue(triples.AsSpan().SequenceEqual(feed.VerifiedItems.Span), "The clean feed read did not return the triples in stored order.");
    }

    /// <summary>I2 feed face: a byte flipped in one block's items excludes exactly that block — its item range is named and its triples are absent, while every other block's triples are returned in order.</summary>
    [TestMethod]
    public void ReadVerifiedItemsExcludesACorruptBlockAndNamesItsRange()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        //Flip the first item byte of block 1 — items [10, 20).
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        image.WritableBytes[firstBlock + (int)BlockStride(10, 64)] ^= 0xFF;

        using VeritasMemoryPool<EncodedTriple> pool = new();
        using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool);

        Assert.AreEqual(20, feed.VerifiedCount, "The corrupt block's ten items must be excluded.");
        Assert.IsFalse(feed.IsClean);
        Assert.HasCount(1, feed.SkippedRanges);
        Assert.AreEqual(new SkippedItemRange(1, 10, 10), feed.SkippedRanges[0], "The excluded block's range was not named exactly.");

        //The surviving triples are blocks 0 and 2 — items [0, 10) followed by [20, 30) — in order.
        EncodedTriple[] expected = [.. triples[..10], .. triples[20..]];
        Assert.IsTrue(expected.AsSpan().SequenceEqual(feed.VerifiedItems.Span), "The surviving triples are not the clean blocks in order.");
    }

    /// <summary>Two corrupt blocks are both excluded and both named; only the clean blocks' triples survive.</summary>
    [TestMethod]
    public void ReadVerifiedItemsExcludesMultipleCorruptBlocks()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(50);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        //Flip the first item byte of blocks 0 and 3 — items [0, 10) and [30, 40).
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        long stride = BlockStride(10, 64);
        image.WritableBytes[firstBlock] ^= 0xFF;
        image.WritableBytes[firstBlock + (int)(3 * stride)] ^= 0xFF;

        using VeritasMemoryPool<EncodedTriple> pool = new();
        using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool);

        Assert.AreEqual(30, feed.VerifiedCount);
        Assert.HasCount(2, feed.SkippedRanges);
        Assert.AreEqual(new SkippedItemRange(0, 0, 10), feed.SkippedRanges[0]);
        Assert.AreEqual(new SkippedItemRange(3, 30, 10), feed.SkippedRanges[1]);

        EncodedTriple[] expected = [.. triples[10..30], .. triples[40..]];
        Assert.IsTrue(expected.AsSpan().SequenceEqual(feed.VerifiedItems.Span));
    }

    /// <summary>An image with no checksums cannot gate per block, so the feed read returns every triple with no exclusions — the gate exists only where there is a digest to verify against.</summary>
    [TestMethod]
    public void ReadVerifiedItemsWithoutChecksumsReturnsAll()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, null, imagePool);

        using VeritasMemoryPool<EncodedTriple> pool = new();
        using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool);

        Assert.AreEqual(triples.Length, feed.VerifiedCount);
        Assert.IsTrue(feed.IsClean);
        Assert.IsFalse(feed.WasChecksumGated, "Without per-block checksums nothing can be gated, and the read must say so rather than appear verified.");
        Assert.IsTrue(triples.AsSpan().SequenceEqual(feed.VerifiedItems.Span));
    }

    /// <summary>Front-matter rot is untrusted geometry the feed read refuses outright — it does not skip-and-continue, because a corrupt block geometry cannot be safely walked.</summary>
    [TestMethod]
    public void ReadVerifiedItemsRefusesFrontMatterDamage()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);
        image.WritableBytes[FrontMatterBase] ^= 0xFF;

        using VeritasMemoryPool<EncodedTriple> pool = new();
        Assert.ThrowsExactly<InvalidDataException>(() => { using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool); });
    }

    /// <summary>A magic mismatch is refused by the feed read, not read as a degraded segment.</summary>
    [TestMethod]
    public void ReadVerifiedItemsRefusesForeignImage()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);
        image.WritableBytes[0] ^= 0xFF;

        using VeritasMemoryPool<EncodedTriple> pool = new();
        Assert.ThrowsExactly<InvalidDataException>(() => { using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool); });
    }

    /// <summary>A truncated image is refused by the feed read rather than walked past its end.</summary>
    [TestMethod]
    public void ReadVerifiedItemsRefusesTruncation()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);
        using ArtifactImage truncated = image.Truncated(100, imagePool);

        using VeritasMemoryPool<EncodedTriple> pool = new();
        Assert.ThrowsExactly<InvalidDataException>(() => { using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(truncated.Bytes, pool); });
    }

    /// <summary>An empty segment feeds no triples and reports clean.</summary>
    [TestMethod]
    public void ReadVerifiedItemsOnEmptySegment()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        ItemSegment segment = new(ReadOnlyMemory<EncodedTriple>.Empty, blockItemCount: 8, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        using VeritasMemoryPool<EncodedTriple> pool = new();
        using ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool);

        Assert.AreEqual(0, feed.VerifiedCount);
        Assert.IsTrue(feed.IsClean);
        Assert.IsEmpty(feed.VerifiedItems.Span.ToArray());
    }

    /// <summary>The pooled feed buffer is returned on dispose, and reading the verified items afterward is refused rather than reading recycled memory.</summary>
    [TestMethod]
    public void ReadVerifiedItemsThrowsAfterDispose()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ArtifactImage image = ToImage(segment, ChecksumAlgorithm.XxHash3, imagePool);

        using VeritasMemoryPool<EncodedTriple> pool = new();
        ItemSegmentFeed feed = ItemSegment.ReadVerifiedItems(image.Bytes, pool);
        feed.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => { _ = feed.VerifiedItems; });
    }
}
