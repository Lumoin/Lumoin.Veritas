using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The system-of-record tier: the canonical triples stored row-major as fixed-count item blocks. A
/// block holds exactly <see cref="BlockItemCount"/> triples (the last block holds the remainder), so a
/// block boundary is a triple boundary and a single block's checksum failure names the exact item range
/// <c>[start, start + count)</c> it covers — the item-aligned detection the column-major sidecar cannot
/// give (a column blob is one CSR coordinate spanning every triple in its groups, never a bounded item
/// set). This is the durable truth; the columnar index is a re-derivable sidecar rebuilt from these
/// triples, and the two tiers' checksum semantics stay distinct.
/// </summary>
/// <remarks>
/// <para>
/// The byte image reuses the persistence container's framing discipline — a magic-versioned header with
/// a required-feature mask, an explicitly-selected per-block <see cref="ChecksumAlgorithm"/>, a per-block
/// checksum section, and a single front-matter checksum trailer over the header and that section — but
/// carries a row-major payload rather than columnar columns, so it is its own focused format, not the
/// columnar container. Each block begins on a configurable alignment boundary (a page/sector multiple by
/// default) so a torn write or a faulted page maps to exactly one checksum domain rather than straddling
/// two blocks; the alignment padding is inert zero-fill and is not part of any checksum.
/// </para>
/// </remarks>
public sealed partial class ItemSegment
{
    /// <summary>The 8-byte magic identifying a Veritas system-of-record item segment image.</summary>
    private static ReadOnlySpan<byte> SegmentMagic => "VTSSOR01"u8;

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The required-feature bit set when a front-matter checksum trailer is present; an older reader refuses an image whose mask carries an unknown bit.</summary>
    private const ulong FrontMatterChecksumFeature = 1UL << 0;

    /// <summary>The required features this build understands; an image whose mask sets any other bit is refused.</summary>
    private const ulong KnownRequiredFeatures = FrontMatterChecksumFeature;

    /// <summary>The checksum-algorithm id written when no checksum is selected.</summary>
    private const byte ChecksumAlgorithmNone = 0;

    /// <summary>The fixed header size: magic (8) + major (1) + minor (1) + required-feature mask (8) + checksum-algorithm id (1).</summary>
    private const int HeaderSize = 8 + 1 + 1 + 8 + 1;

    /// <summary>The scalar block after the header: item count (4) + block item count (4) + block alignment (4).</summary>
    private const int ScalarSize = sizeof(int) + sizeof(int) + sizeof(int);

    /// <summary>The byte size of one row-major item record: subject, predicate, object as three little-endian 32-bit ids.</summary>
    private const int ItemByteSize = 3 * sizeof(uint);

    /// <summary>The default block alignment: one 4 KiB page, so each block is a whole-page checksum domain.</summary>
    public const int DefaultBlockAlignment = 4096;

    /// <summary>The canonical triples this segment holds, in row-major item order.</summary>
    private readonly IReadOnlyList<EncodedTriple> items;

    /// <summary>Creates a segment over the canonical triples.</summary>
    /// <param name="items">The canonical triples, in the order they are stored.</param>
    /// <param name="blockItemCount">The number of triples per block; a block boundary is a triple boundary so a block's checksum names its exact item range.</param>
    /// <param name="blockAlignment">The byte alignment each block begins on (a page/sector multiple); <see cref="DefaultBlockAlignment"/> by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockItemCount"/> or <paramref name="blockAlignment"/> is not positive.</exception>
    public ItemSegment(IReadOnlyList<EncodedTriple> items, int blockItemCount, int blockAlignment = DefaultBlockAlignment)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockAlignment);

        this.items = items;
        BlockItemCount = blockItemCount;
        BlockAlignment = blockAlignment;
    }

    /// <summary>The number of triples per block.</summary>
    public int BlockItemCount { get; }

    /// <summary>The byte alignment each block begins on.</summary>
    public int BlockAlignment { get; }

    /// <summary>The number of triples in the segment.</summary>
    public int ItemCount => items.Count;

    /// <summary>The number of item blocks: every full block holds <see cref="BlockItemCount"/> triples and the last holds the remainder.</summary>
    public int BlockCount => (int)(((long)items.Count + BlockItemCount - 1) / BlockItemCount);

    /// <summary>The number of bytes <see cref="WriteTo"/> writes under the given checksum selection.</summary>
    /// <param name="checksum">The per-block checksum algorithm whose section and self-trailer size the image, or <see langword="null"/> for none.</param>
    /// <returns>The image byte size.</returns>
    public long ComputeSerializedSize(ChecksumAlgorithm? checksum)
    {
        int blockCount = BlockCount;
        long frontMatterEnd = FrontMatterSize(blockCount, checksum);
        long firstBlock = Align(frontMatterEnd, BlockAlignment);
        long blockBytes = Align((long)BlockItemCount * ItemByteSize, BlockAlignment);

        return firstBlock + ((long)blockCount * blockBytes) + (checksum is null ? 0 : checksum.ByteWidth);
    }

    /// <summary>Writes this segment's self-describing row-major image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same <paramref name="checksum"/>); alignment padding is zero-filled.</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="checksum">The per-block checksum algorithm to stamp and compute, or <see langword="null"/> for no checksums.</param>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public void WriteTo(Span<byte> destination, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        int blockCount = BlockCount;
        int p = 0;
        SegmentMagic.CopyTo(destination[p..]);
        p += SegmentMagic.Length;
        destination[p++] = FormatVersionMajor;
        destination[p++] = FormatVersionMinor;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], checksum is null ? 0UL : FrontMatterChecksumFeature);
        p += sizeof(ulong);
        destination[p++] = checksum?.Id ?? ChecksumAlgorithmNone;

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], items.Count);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], BlockItemCount);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], BlockAlignment);
        p += sizeof(int);

        //The per-block checksum section follows the scalars; reserve it now and backfill once each block
        //is written, so each digest is over the bytes actually laid down.
        int checksumSectionOffset = p;
        int frontMatterEnd = (int)FrontMatterSize(blockCount, checksum);

        //Zero-fill the gap from the front matter to the first aligned block, then every block's payload
        //and its trailing alignment padding, so the image carries no stale buffer bytes.
        destination[frontMatterEnd..].Clear();

        long blockBytes = Align((long)BlockItemCount * ItemByteSize, BlockAlignment);
        long firstBlock = Align(frontMatterEnd, BlockAlignment);
        for(int block = 0; block < blockCount; block++)
        {
            long blockOffset = firstBlock + (block * blockBytes);
            int itemsInBlock = ItemsInBlock(block);
            WriteBlockItems(destination.Slice((int)blockOffset, itemsInBlock * ItemByteSize), block, itemsInBlock);

            if(checksum is not null)
            {
                checksum.Compute(
                    destination.Slice((int)blockOffset, itemsInBlock * ItemByteSize),
                    destination.Slice(checksumSectionOffset + (block * checksum.ByteWidth), checksum.ByteWidth));
            }
        }

        if(checksum is not null)
        {
            //The front-matter trailer covers the header, scalars, and per-block section — everything the
            //per-block digests do not — and sits at the image tail after the last block.
            long total = ComputeSerializedSize(checksum);
            checksum.Compute(destination[..frontMatterEnd], destination.Slice((int)(total - checksum.ByteWidth), checksum.ByteWidth));
        }
    }

    /// <summary>Reconstructs a segment's triples from an image written by <see cref="WriteTo"/>, verifying the front-matter trailer and every block's checksum before its bytes are decoded; the first block that fails is refused (an all-or-nothing read). The report-not-throw counterpart that excludes a corrupt block and continues is <see cref="ReadVerifiedItems"/>.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The canonical triples, in stored order.</returns>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, or a block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static EncodedTriple[] ReadFrom(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        SegmentLayout layout = ParseAndVerifyFrontMatter(source, resolveChecksum);
        EncodedTriple[] triples = new EncodedTriple[layout.ItemCount];
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        for(int block = 0; block < layout.BlockCount; block++)
        {
            if(!VerifyBlock(source, layout, block, scratch, out int start, out int itemsInBlock, out long blockOffset))
            {
                throw new InvalidDataException($"Item block {block} (items [{start}, {start + itemsInBlock})) failed its checksum (at-rest corruption).");
            }

            DecodeBlock(source, blockOffset, itemsInBlock, triples.AsSpan(start, itemsInBlock));
        }

        return triples;
    }

    /// <summary>Parses an image's framing and verifies its front-matter trailer, returning the geometry needed to locate and verify every block; framing, version, feature, algorithm, truncation, and front-matter damage are refused here so the block walk that follows can trust the geometry.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The verified layout.</returns>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SegmentLayout ParseAndVerifyFrontMatter(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be an item segment image.");
        }

        int p = 0;
        if(!source[..SegmentMagic.Length].SequenceEqual(SegmentMagic))
        {
            throw new InvalidDataException("The bytes are not an item segment image (magic mismatch).");
        }

        p += SegmentMagic.Length;
        byte versionMajor = source[p++];
        byte versionMinor = source[p++];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Item segment format version {versionMajor}.{versionMinor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        ulong featureFlags = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);
        if((featureFlags & ~KnownRequiredFeatures) != 0)
        {
            throw new NotSupportedException("The item segment requires a feature this build does not understand.");
        }

        byte checksumAlgorithmId = source[p++];
        ChecksumAlgorithm? checksum = null;
        if(checksumAlgorithmId != ChecksumAlgorithmNone)
        {
            checksum = (resolveChecksum ?? ChecksumAlgorithm.DefaultResolver)(checksumAlgorithmId)
                ?? throw new NotSupportedException($"The item segment uses checksum algorithm id {checksumAlgorithmId}, which this reader does not resolve.");
        }

        bool hasFrontMatterChecksum = (featureFlags & FrontMatterChecksumFeature) != 0;
        if(hasFrontMatterChecksum != (checksum is not null))
        {
            throw new InvalidDataException("The item segment's front-matter-checksum feature flag disagrees with its checksum-algorithm id.");
        }

        int itemCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int blockItemCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int blockAlignment = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(itemCount < 0 || blockItemCount <= 0 || blockAlignment <= 0)
        {
            throw new InvalidDataException("The item segment declares a negative item count or a non-positive block geometry.");
        }

        int blockCount = (int)(((long)itemCount + blockItemCount - 1) / blockItemCount);
        long frontMatterEnd = HeaderSize + ScalarSize + (checksum is null ? 0L : (long)blockCount * checksum.ByteWidth);
        long blockBytes = Align((long)blockItemCount * ItemByteSize, blockAlignment);
        long firstBlock = Align(frontMatterEnd, blockAlignment);
        long total = firstBlock + ((long)blockCount * blockBytes) + (checksum is null ? 0 : checksum.ByteWidth);
        if(total > source.Length)
        {
            throw new InvalidDataException("The item segment is truncated: its declared blocks run past the image.");
        }

        if(checksum is not null)
        {
            Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            checksum.Compute(source[..(int)frontMatterEnd], computed[..checksum.ByteWidth]);
            if(!computed[..checksum.ByteWidth].SequenceEqual(source.Slice((int)(total - checksum.ByteWidth), checksum.ByteWidth)))
            {
                throw new InvalidDataException("The item segment failed its front-matter checksum (at-rest corruption).");
            }
        }

        return new SegmentLayout(itemCount, blockItemCount, blockCount, checksum, HeaderSize + ScalarSize, firstBlock, blockBytes);
    }

    /// <summary>Recomputes block <paramref name="block"/>'s checksum and compares it to the stored digest, reporting the verdict rather than throwing so a caller can either refuse the image or exclude the block; the block's item range and byte offset are set regardless of the verdict.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The verified layout.</param>
    /// <param name="block">The block index.</param>
    /// <param name="scratch">Scratch at least <see cref="ChecksumAlgorithm.MaximumByteWidth"/> bytes long for the recomputed digest.</param>
    /// <param name="start">The block's first item index.</param>
    /// <param name="itemsInBlock">The number of triples the block covers.</param>
    /// <param name="blockOffset">The block's byte offset in the image.</param>
    /// <returns>Whether the block's checksum matched; always <see langword="true"/> when the image carries no checksums.</returns>
    private static bool VerifyBlock(ReadOnlySpan<byte> source, in SegmentLayout layout, int block, Span<byte> scratch, out int start, out int itemsInBlock, out long blockOffset)
    {
        start = block * layout.BlockItemCount;
        itemsInBlock = Math.Min(layout.BlockItemCount, layout.ItemCount - start);
        blockOffset = layout.FirstBlock + (block * layout.BlockBytes);
        if(layout.Checksum is null)
        {
            return true;
        }

        ReadOnlySpan<byte> blockItems = source.Slice((int)blockOffset, itemsInBlock * ItemByteSize);
        layout.Checksum.Compute(blockItems, scratch[..layout.Checksum.ByteWidth]);

        return scratch[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.ChecksumSectionOffset + (block * layout.Checksum.ByteWidth), layout.Checksum.ByteWidth));
    }

    /// <summary>Decodes a verified block's row-major records into <paramref name="destination"/> as triples.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="blockOffset">The block's byte offset in the image.</param>
    /// <param name="itemsInBlock">The number of triples the block covers.</param>
    /// <param name="destination">The span to decode into; exactly <paramref name="itemsInBlock"/> long.</param>
    private static void DecodeBlock(ReadOnlySpan<byte> source, long blockOffset, int itemsInBlock, Span<EncodedTriple> destination)
    {
        ReadOnlySpan<byte> blockItems = source.Slice((int)blockOffset, itemsInBlock * ItemByteSize);
        for(int i = 0; i < itemsInBlock; i++)
        {
            int itemOffset = i * ItemByteSize;
            uint subject = BinaryPrimitives.ReadUInt32LittleEndian(blockItems[itemOffset..]);
            uint predicate = BinaryPrimitives.ReadUInt32LittleEndian(blockItems[(itemOffset + sizeof(uint))..]);
            uint @object = BinaryPrimitives.ReadUInt32LittleEndian(blockItems[(itemOffset + (2 * sizeof(uint)))..]);
            destination[i] = EncodedTriple.FromEncoded(subject, predicate, @object);
        }
    }

    /// <summary>The byte size of the header, scalars, and per-block checksum section — everything the front-matter trailer covers, before the first aligned block.</summary>
    /// <param name="blockCount">The number of blocks.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <returns>The front-matter byte size.</returns>
    private static long FrontMatterSize(int blockCount, ChecksumAlgorithm? checksum)
    {
        return HeaderSize + ScalarSize + (checksum is null ? 0L : (long)blockCount * checksum.ByteWidth);
    }

    /// <summary>The number of triples block <paramref name="block"/> holds — a full <see cref="BlockItemCount"/> except the last block, which holds the remainder.</summary>
    /// <param name="block">The block index.</param>
    /// <returns>The triple count in the block.</returns>
    private int ItemsInBlock(int block)
    {
        int start = block * BlockItemCount;

        return Math.Min(BlockItemCount, items.Count - start);
    }

    /// <summary>Writes a block's triples row-major into <paramref name="destination"/> as three little-endian 32-bit ids each.</summary>
    /// <param name="destination">The block's item-byte region (exactly <paramref name="itemsInBlock"/> records long).</param>
    /// <param name="block">The block index.</param>
    /// <param name="itemsInBlock">The number of triples in the block.</param>
    private void WriteBlockItems(Span<byte> destination, int block, int itemsInBlock)
    {
        int start = block * BlockItemCount;
        for(int i = 0; i < itemsInBlock; i++)
        {
            EncodedTriple triple = items[start + i];
            int itemOffset = i * ItemByteSize;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[itemOffset..], triple.Subject.Encoded);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[(itemOffset + sizeof(uint))..], triple.Predicate.Encoded);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[(itemOffset + (2 * sizeof(uint)))..], triple.Object.Encoded);
        }
    }

    /// <summary>Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.</summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The alignment (positive).</param>
    /// <returns>The aligned value.</returns>
    private static long Align(long value, long alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    /// <summary>The framing-verified geometry of an item-segment image: everything needed to locate and checksum-verify every block once the header and front-matter trailer have passed.</summary>
    private readonly struct SegmentLayout
    {
        /// <summary>Creates a layout.</summary>
        /// <param name="itemCount">The number of triples the segment holds.</param>
        /// <param name="blockItemCount">The number of triples per block.</param>
        /// <param name="blockCount">The number of blocks.</param>
        /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</param>
        /// <param name="checksumSectionOffset">The byte offset of the per-block checksum section.</param>
        /// <param name="firstBlock">The byte offset of the first aligned block.</param>
        /// <param name="blockBytes">The aligned byte stride between block starts.</param>
        internal SegmentLayout(int itemCount, int blockItemCount, int blockCount, ChecksumAlgorithm? checksum, int checksumSectionOffset, long firstBlock, long blockBytes)
        {
            ItemCount = itemCount;
            BlockItemCount = blockItemCount;
            BlockCount = blockCount;
            Checksum = checksum;
            ChecksumSectionOffset = checksumSectionOffset;
            FirstBlock = firstBlock;
            BlockBytes = blockBytes;
        }

        /// <summary>The number of triples the segment holds.</summary>
        internal int ItemCount { get; }

        /// <summary>The number of triples per block.</summary>
        internal int BlockItemCount { get; }

        /// <summary>The number of blocks.</summary>
        internal int BlockCount { get; }

        /// <summary>The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</summary>
        internal ChecksumAlgorithm? Checksum { get; }

        /// <summary>The byte offset of the per-block checksum section.</summary>
        internal int ChecksumSectionOffset { get; }

        /// <summary>The byte offset of the first aligned block.</summary>
        internal long FirstBlock { get; }

        /// <summary>The aligned byte stride between block starts.</summary>
        internal long BlockBytes { get; }
    }
}
