using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Segment;

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

    /// <summary>Serializes a segment to a pool-rented image and returns the bytes.</summary>
    /// <param name="segment">The segment to serialize.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] ToImage(ItemSegment segment, ChecksumAlgorithm? checksum)
    {
        using VeritasMemoryPool<byte> pool = new();
        int size = (int)segment.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        segment.WriteTo(image, checksum);

        return image.ToArray();
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
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            EncodedTriple[] triples = SampleTriples(100);
            ItemSegment segment = new(triples, blockItemCount: 7, blockAlignment: 64);
            int blockCount = segment.BlockCount;
            Assert.AreEqual(15, blockCount, $"100 triples in 7-item blocks is 15 blocks (algorithm {checksum?.Name ?? "none"}).");

            byte[] image = ToImage(segment, checksum);
            EncodedTriple[] restored = ItemSegment.ReadFrom(image);

            Assert.HasCount(triples.Length, restored);
            Assert.IsTrue(triples.AsSpan().SequenceEqual(restored), $"The triples did not round-trip in order (algorithm {checksum?.Name ?? "none"}).");
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
                EncodedTriple[] restored = ItemSegment.ReadFrom(buffer);
                Assert.HasCount(triples.Length, restored);
                Assert.IsTrue(triples.AsSpan().SequenceEqual(restored), $"The exact-size image did not round-trip (algorithm {checksum?.Name ?? "none"}, {count}/{blockItemCount}).");
            }
        }
    }

    /// <summary>I1 / item-aligned detection: a byte flipped in a block's items fails that block's checksum, and the refusal names the exact item range the block covers — under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void BlockCorruptionIsDetectedAndNamesTheItemRange()
    {
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            EncodedTriple[] triples = SampleTriples(30);
            ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
            byte[] image = ToImage(segment, checksum);

            //Flip the first item byte of block 1 — items [10, 20).
            int firstBlock = FirstBlockOffset(segment.BlockCount, checksum.ByteWidth, 64);
            long blockBytes = ((10L * 12) + 63) / 64 * 64;
            image[firstBlock + (int)blockBytes] ^= 0xFF;

            InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.ReadFrom(image); });
            Assert.IsTrue(thrown.Message.Contains("[10, 20)", StringComparison.Ordinal), $"The refusal did not name the corrupt block's item range (algorithm {checksum.Name}): {thrown.Message}");
        }
    }

    /// <summary>The remainder edge of item-aligned detection: corrupting the partial last block names <c>[start, start + remainder)</c>, not the block stride <c>[start, start + blockItemCount)</c>.</summary>
    [TestMethod]
    public void PartialLastBlockCorruptionNamesTheRemainderRange()
    {
        //25 triples in 10-item blocks: blocks [0, 10), [10, 20), [20, 25) — the last holds the 5-item remainder.
        EncodedTriple[] triples = SampleTriples(25);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        //Flip the first item byte of the partial last block (block 2).
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        long blockBytes = ((10L * 12) + 63) / 64 * 64;
        image[firstBlock + (int)(2 * blockBytes)] ^= 0xFF;

        InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.ReadFrom(image); });
        Assert.IsTrue(thrown.Message.Contains("[20, 25)", StringComparison.Ordinal), $"The refusal did not name the partial last block's remainder range: {thrown.Message}");
        Assert.IsFalse(thrown.Message.Contains("[20, 30)", StringComparison.Ordinal), $"The refusal named the block stride instead of the remainder: {thrown.Message}");
    }

    /// <summary>The per-block checksum domain is exactly the block's payload, not its aligned stride: a byte flipped in a block's trailing alignment padding is outside every checksum and the image still round-trips — the property that keeps one block's detection from reaching into a neighbour's bytes.</summary>
    [TestMethod]
    public void InPaddingCorruptionIsInert()
    {
        //10-item blocks at 64-byte alignment: payload 120 bytes, stride Align(120, 64) = 128 — 8 padding bytes per block.
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        //Flip a byte in block 0's padding (just past its 120-byte payload), outside both that block's
        //checksum domain and the front-matter trailer.
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        image[firstBlock + (10 * 12)] ^= 0xFF;

        EncodedTriple[] restored = ItemSegment.ReadFrom(image);
        Assert.IsTrue(triples.AsSpan().SequenceEqual(restored), "An inert padding-byte corruption was not tolerated, so the per-block checksum domain spans the padded stride rather than the payload.");
    }

    /// <summary>A byte corrupted in the front matter (the per-block checksum section it covers) fails the front-matter trailer, under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void FrontMatterCorruptionIsDetected()
    {
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            EncodedTriple[] triples = SampleTriples(30);
            ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
            byte[] image = ToImage(segment, checksum);

            //The per-block checksum section begins right after the scalars and is covered by the front-matter trailer.
            image[FrontMatterBase] ^= 0xFF;

            Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.ReadFrom(image); });
        }
    }

    /// <summary>A truncated image is refused rather than read past its end.</summary>
    [TestMethod]
    public void TruncatedImageIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);
        byte[] truncated = image.AsSpan(0, image.Length - 100).ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.ReadFrom(truncated); });
    }

    /// <summary>An image stamped with a checksum-algorithm id no resolver knows is refused, not read under the wrong algorithm.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        //The checksum-algorithm id is the last header byte: magic (8) + major (1) + minor (1) + feature mask (8).
        image[18] = 99;

        Assert.ThrowsExactly<NotSupportedException>(() => { _ = ItemSegment.ReadFrom(image); });
    }

    /// <summary>The header's front-matter-checksum feature flag must agree with the presence of a checksum-algorithm id: a checksummed image with the flag cleared, or a no-checksum image with the flag set, is a header inconsistency the reader refuses.</summary>
    [TestMethod]
    public void FeatureFlagDisagreeingWithChecksumIdIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);

        //A checksummed image with the feature bit cleared (the algorithm id still present) is inconsistent.
        byte[] flagCleared = ToImage(segment, ChecksumAlgorithm.XxHash3);
        flagCleared[FeatureMaskOffset] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.ReadFrom(flagCleared); });

        //A no-checksum image with the feature bit set (no algorithm id) is the symmetric inconsistency.
        byte[] flagSet = ToImage(segment, null);
        flagSet[FeatureMaskOffset] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.ReadFrom(flagSet); });
    }

    /// <summary>An empty segment round-trips to an empty triple set.</summary>
    [TestMethod]
    public void EmptySegmentRoundTrips()
    {
        ItemSegment segment = new([], blockItemCount: 8, blockAlignment: 64);
        int blockCount = segment.BlockCount;
        Assert.AreEqual(0, blockCount);

        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);
        EncodedTriple[] restored = ItemSegment.ReadFrom(image);

        Assert.IsEmpty(restored);
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
        using IMemoryOwner<byte> owner = pool.Rent((int)expected);
        Span<byte> image = owner.Memory.Span[..(int)expected];
        segment.WriteTo(image, ChecksumAlgorithm.XxHash3);
        EncodedTriple[] restored = ItemSegment.ReadFrom(image);
        Assert.IsTrue(triples.AsSpan().SequenceEqual(restored));

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
        EncodedTriple[] triples = SampleTriples(100);
        ItemSegment segment = new(triples, blockItemCount: 7, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        EncodedTriple[] recovered = ItemSegment.ReadFrom(image);
        ColumnarTripleIndex sidecar = ColumnarTripleIndex.Build(recovered);

        HashSet<EncodedTriple> expected = [.. triples];
        HashSet<EncodedTriple> actual = [.. sidecar.EnumerateTriples()];
        Assert.IsTrue(expected.SetEquals(actual), "The columnar sidecar rebuilt from the segment differs from the system-of-record triples.");
    }
}
