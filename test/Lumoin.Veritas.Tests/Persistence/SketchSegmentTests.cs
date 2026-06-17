using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Sketch;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The persisted integrity sketch: opaque fixed-width coded symbols round-trip through fixed-count symbol
/// blocks across the checksum selections; a block's checksum failure is detected and names the exact
/// symbol range it covers (including the partial last block's remainder); the per-block domain is the
/// payload not the padded stride (an in-padding flip is inert); front-matter rot, truncation, a foreign
/// algorithm, and a feature-flag/algorithm-id disagreement are refused; and blocks begin on a page
/// boundary observed in the raw bytes.
/// </summary>
[TestClass]
internal sealed class SketchSegmentTests
{
    /// <summary>The header (8 magic + 1 major + 1 minor + 8 feature mask + 1 algo id) and scalar (4×4) byte size before the per-block checksum section — the layout the corruption cells target.</summary>
    private const int FrontMatterBase = 19 + 16;

    /// <summary>The byte offset of the required-feature mask's least-significant byte (magic 8 + major 1 + minor 1); its bit 0 is the front-matter-checksum feature.</summary>
    private const int FeatureMaskOffset = 8 + 1 + 1;

    /// <summary>A deterministic flat coded-symbol byte stream — distinct bytes so a flipped byte is detectable and so a block's first byte ties to a known source offset.</summary>
    /// <param name="symbolCount">The symbol count.</param>
    /// <param name="symbolWidth">The byte width of one symbol.</param>
    /// <returns>The flat symbol bytes.</returns>
    private static byte[] SampleSymbols(int symbolCount, int symbolWidth)
    {
        byte[] symbols = new byte[symbolCount * symbolWidth];
        for(int i = 0; i < symbols.Length; i++)
        {
            symbols[i] = (byte)((i * 31) + 7);
        }

        return symbols;
    }

    /// <summary>Serializes a segment to a pool-rented image and returns the bytes.</summary>
    /// <param name="segment">The segment to serialize.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] ToImage(SketchSegment segment, ChecksumAlgorithm? checksum)
    {
        using VeritasMemoryPool<byte> pool = new();
        int size = (int)segment.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        segment.WriteTo(image, checksum);

        return image.ToArray();
    }

    /// <summary>The byte offset of the first symbol block: the front matter plus the per-block checksum section, rounded up to the alignment.</summary>
    /// <param name="blockCount">The block count.</param>
    /// <param name="checksumWidth">The checksum byte width.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The first block's byte offset.</returns>
    private static int FirstBlockOffset(int blockCount, int checksumWidth, int alignment)
    {
        int frontMatterEnd = FrontMatterBase + (blockCount * checksumWidth);

        return (frontMatterEnd + alignment - 1) / alignment * alignment;
    }

    /// <summary>Symbols round-trip in order through the segment across the checksum selections, with the expected block count.</summary>
    [TestMethod]
    public void RoundTripsInOrderAcrossChecksumSelections()
    {
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            byte[] symbols = SampleSymbols(100, symbolWidth: 20);
            SketchSegment segment = new(symbols, symbolWidth: 20, symbolsPerBlock: 7, blockAlignment: 64);
            int blockCount = segment.BlockCount;
            Assert.AreEqual(15, blockCount, $"100 symbols in 7-symbol blocks is 15 blocks (algorithm {checksum?.Name ?? "none"}).");

            byte[] image = ToImage(segment, checksum);
            byte[] restored = SketchSegment.ReadFrom(image);

            Assert.IsTrue(symbols.AsSpan().SequenceEqual(restored), $"The symbols did not round-trip in order (algorithm {checksum?.Name ?? "none"}).");
        }
    }

    /// <summary>The size identity: <see cref="SketchSegment.ComputeSerializedSize"/> is exactly the bytes <c>WriteTo</c> lays down and exactly the bytes <c>ReadFrom</c> consumes, across the checksum selections and the empty, exact-multiple, and partial-last-block geometries; a sentinel tail past the computed size is neither written by the writer nor required by the reader.</summary>
    [TestMethod]
    public void SerializedSizeIsExactAndConsumedExactlyAcrossGeometries()
    {
        const byte Sentinel = 0xAB;
        const int Slack = 37;
        const int SymbolWidth = 16;
        (int Count, int SymbolsPerBlock)[] geometries = [(0, 8), (14, 7), (100, 7)];

        using VeritasMemoryPool<byte> pool = new();
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            foreach((int count, int symbolsPerBlock) in geometries)
            {
                byte[] symbols = SampleSymbols(count, SymbolWidth);
                SketchSegment segment = new(symbols, SymbolWidth, symbolsPerBlock, blockAlignment: 64);
                int size = (int)segment.ComputeSerializedSize(checksum);

                //Pre-fill an over-sized buffer with a sentinel and hand WriteTo only the leading `size` bytes:
                //the writer must fill exactly that prefix and never touch the sentinel tail.
                using IMemoryOwner<byte> owner = pool.Rent(size + Slack);
                Span<byte> buffer = owner.Memory.Span[..(size + Slack)];
                buffer.Fill(Sentinel);
                segment.WriteTo(buffer[..size], checksum);

                for(int i = size; i < buffer.Length; i++)
                {
                    Assert.AreEqual(Sentinel, buffer[i], $"WriteTo wrote past the computed size (algorithm {checksum?.Name ?? "none"}, {count}/{symbolsPerBlock}).");
                }

                //ReadFrom over the whole over-sized buffer recovers the symbols, so it consumes exactly the
                //declared total (== size) and tolerates the sentinel tail rather than reading to the end.
                byte[] restored = SketchSegment.ReadFrom(buffer);
                Assert.IsTrue(symbols.AsSpan().SequenceEqual(restored), $"The exact-size image did not round-trip (algorithm {checksum?.Name ?? "none"}, {count}/{symbolsPerBlock}).");
            }
        }
    }

    /// <summary>I2 / symbol-aligned detection: a byte flipped in a block's symbols fails that block's checksum, and the refusal names the exact symbol range — under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void BlockCorruptionIsDetectedAndNamesTheSymbolRange()
    {
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            byte[] symbols = SampleSymbols(30, symbolWidth: 12);
            SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);
            byte[] image = ToImage(segment, checksum);

            //Flip the first symbol byte of block 1 — symbols [10, 20).
            int firstBlock = FirstBlockOffset(segment.BlockCount, checksum.ByteWidth, 64);
            long blockBytes = ((10L * 12) + 63) / 64 * 64;
            image[firstBlock + (int)blockBytes] ^= 0xFF;

            InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.ReadFrom(image); });
            Assert.IsTrue(thrown.Message.Contains("[10, 20)", StringComparison.Ordinal), $"The refusal did not name the corrupt block's symbol range (algorithm {checksum.Name}): {thrown.Message}");
        }
    }

    /// <summary>The remainder edge of symbol-aligned detection: corrupting the partial last block names <c>[start, start + remainder)</c>, not the block stride <c>[start, start + symbolsPerBlock)</c>.</summary>
    [TestMethod]
    public void PartialLastBlockCorruptionNamesTheRemainderRange()
    {
        //25 symbols in 10-symbol blocks: blocks [0, 10), [10, 20), [20, 25) — the last holds the 5-symbol remainder.
        byte[] symbols = SampleSymbols(25, symbolWidth: 12);
        SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        //Flip the first symbol byte of the partial last block (block 2).
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        long blockBytes = ((10L * 12) + 63) / 64 * 64;
        image[firstBlock + (int)(2 * blockBytes)] ^= 0xFF;

        InvalidDataException thrown = Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.ReadFrom(image); });
        Assert.IsTrue(thrown.Message.Contains("[20, 25)", StringComparison.Ordinal), $"The refusal did not name the partial last block's remainder range: {thrown.Message}");
        Assert.IsFalse(thrown.Message.Contains("[20, 30)", StringComparison.Ordinal), $"The refusal named the block stride instead of the remainder: {thrown.Message}");
    }

    /// <summary>The per-block checksum domain is exactly the block's payload, not its aligned stride: a byte flipped in a block's trailing alignment padding is outside every checksum and the image still round-trips.</summary>
    [TestMethod]
    public void InPaddingCorruptionIsInert()
    {
        //10-symbol blocks of 12-byte symbols at 64-byte alignment: payload 120 bytes, stride Align(120, 64) = 128 — 8 padding bytes per block.
        byte[] symbols = SampleSymbols(30, symbolWidth: 12);
        SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        //Flip a byte in block 0's padding (just past its 120-byte payload), outside both that block's
        //checksum domain and the front-matter trailer.
        int firstBlock = FirstBlockOffset(segment.BlockCount, ChecksumAlgorithm.XxHash3.ByteWidth, 64);
        image[firstBlock + (10 * 12)] ^= 0xFF;

        byte[] restored = SketchSegment.ReadFrom(image);
        Assert.IsTrue(symbols.AsSpan().SequenceEqual(restored), "An inert padding-byte corruption was not tolerated, so the per-block checksum domain spans the padded stride rather than the payload.");
    }

    /// <summary>A byte corrupted in the front matter (the per-block checksum section it covers) fails the front-matter trailer, under both the 8-byte and 4-byte checksum widths.</summary>
    [TestMethod]
    public void FrontMatterCorruptionIsDetected()
    {
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            byte[] symbols = SampleSymbols(30, symbolWidth: 12);
            SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);
            byte[] image = ToImage(segment, checksum);

            //The per-block checksum section begins right after the scalars and is covered by the front-matter trailer.
            image[FrontMatterBase] ^= 0xFF;

            Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.ReadFrom(image); });
        }
    }

    /// <summary>A truncated image is refused rather than read past its end.</summary>
    [TestMethod]
    public void TruncatedImageIsRefused()
    {
        byte[] symbols = SampleSymbols(30, symbolWidth: 12);
        SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);
        byte[] truncated = image.AsSpan(0, image.Length - 100).ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.ReadFrom(truncated); });
    }

    /// <summary>An image stamped with a checksum-algorithm id no resolver knows is refused, not read under the wrong algorithm.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmIsRefused()
    {
        byte[] symbols = SampleSymbols(30, symbolWidth: 12);
        SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);
        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);

        //The checksum-algorithm id is the last header byte: magic (8) + major (1) + minor (1) + feature mask (8).
        image[18] = 99;

        Assert.ThrowsExactly<NotSupportedException>(() => { _ = SketchSegment.ReadFrom(image); });
    }

    /// <summary>The header's front-matter-checksum feature flag must agree with the presence of a checksum-algorithm id: a checksummed image with the flag cleared, or a no-checksum image with the flag set, is a header inconsistency the reader refuses.</summary>
    [TestMethod]
    public void FeatureFlagDisagreeingWithChecksumIdIsRefused()
    {
        byte[] symbols = SampleSymbols(30, symbolWidth: 12);
        SketchSegment segment = new(symbols, symbolWidth: 12, symbolsPerBlock: 10, blockAlignment: 64);

        //A checksummed image with the feature bit cleared (the algorithm id still present) is inconsistent.
        byte[] flagCleared = ToImage(segment, ChecksumAlgorithm.XxHash3);
        flagCleared[FeatureMaskOffset] = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.ReadFrom(flagCleared); });

        //A no-checksum image with the feature bit set (no algorithm id) is the symmetric inconsistency.
        byte[] flagSet = ToImage(segment, null);
        flagSet[FeatureMaskOffset] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.ReadFrom(flagSet); });
    }

    /// <summary>An empty segment round-trips to an empty symbol set.</summary>
    [TestMethod]
    public void EmptySegmentRoundTrips()
    {
        SketchSegment segment = new(ReadOnlyMemory<byte>.Empty, symbolWidth: 16, symbolsPerBlock: 8, blockAlignment: 64);
        int blockCount = segment.BlockCount;
        Assert.AreEqual(0, blockCount);

        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);
        byte[] restored = SketchSegment.ReadFrom(image);

        Assert.IsEmpty(restored);
    }

    /// <summary>Each symbol block begins on the default page boundary, observed directly in the raw bytes, and the first byte of block k is that block's first symbol byte.</summary>
    [TestMethod]
    public void BlocksAreDefaultPageAligned()
    {
        const int SymbolWidth = 8;
        const int SymbolsPerBlock = 8;
        byte[] symbols = SampleSymbols(20, SymbolWidth);
        SketchSegment segment = new(symbols, SymbolWidth, SymbolsPerBlock);
        Assert.AreEqual(SketchSegment.DefaultBlockAlignment, segment.BlockAlignment);
        int blockCount = segment.BlockCount;
        Assert.AreEqual(3, blockCount);

        //First block at page 1 (the front matter fits in well under a page); three page-sized blocks; an 8-byte trailer.
        long expected = SketchSegment.DefaultBlockAlignment + (3L * SketchSegment.DefaultBlockAlignment) + ChecksumAlgorithm.XxHash3.ByteWidth;
        Assert.AreEqual(expected, segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3));

        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);
        byte[] restored = SketchSegment.ReadFrom(image);
        Assert.IsTrue(symbols.AsSpan().SequenceEqual(restored));

        //Observe the page alignment directly in the bytes: each block begins on a 4 KiB multiple, and its
        //first byte is the first byte of that block's first symbol in the source stream.
        long blockBytes = (((long)SymbolsPerBlock * SymbolWidth) + SketchSegment.DefaultBlockAlignment - 1) / SketchSegment.DefaultBlockAlignment * SketchSegment.DefaultBlockAlignment;
        for(int block = 0; block < blockCount; block++)
        {
            long blockOffset = SketchSegment.DefaultBlockAlignment + (block * blockBytes);
            Assert.AreEqual(0L, blockOffset % SketchSegment.DefaultBlockAlignment, $"Block {block} does not begin on a page boundary.");
            Assert.AreEqual(symbols[block * SymbolsPerBlock * SymbolWidth], image[(int)blockOffset], $"Block {block}'s first byte is not its first symbol.");
        }
    }

    /// <summary>Independently of the production alignment formula (which the corruption cells reuse), a small 64-aligned image places its blocks at hand-computed byte offsets with a hand-computed total, so a regression in the shared alignment arithmetic cannot stay green.</summary>
    [TestMethod]
    public void BlockOffsetsMatchAHandComputedLayoutAtNonDefaultAlignment()
    {
        //10 symbols of 12 bytes in 4-symbol blocks at 64-byte alignment, XxHash3 (8-byte digests): blocks
        //[0, 4), [4, 8), [8, 10). Front matter = header 19 + scalars 16 + 3 digests * 8 = 59, so the first
        //block aligns to 64; each block's 48 payload bytes round up to a 64-byte stride; the image is
        //64 + 3*64 + 8 = 264 bytes. All hand-computed, independent of the production Align.
        const int SymbolWidth = 12;
        const int SymbolsPerBlock = 4;
        byte[] symbols = SampleSymbols(10, SymbolWidth);
        SketchSegment segment = new(symbols, SymbolWidth, SymbolsPerBlock, blockAlignment: 64);
        int blockCount = segment.BlockCount;
        Assert.AreEqual(3, blockCount);
        Assert.AreEqual(264L, segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3), "The hand-computed image size did not match — the alignment arithmetic drifted.");

        byte[] image = ToImage(segment, ChecksumAlgorithm.XxHash3);
        int[] blockOffsets = [64, 128, 192];
        for(int block = 0; block < blockOffsets.Length; block++)
        {
            Assert.AreEqual(symbols[block * SymbolsPerBlock * SymbolWidth], image[blockOffsets[block]], $"Block {block} does not begin at its hand-computed offset {blockOffsets[block]}.");
        }

        byte[] restored = SketchSegment.ReadFrom(image);
        Assert.IsTrue(symbols.AsSpan().SequenceEqual(restored));
    }
}
