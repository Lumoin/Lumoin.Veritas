using System;
using System.Buffers;
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

    /// <summary>The fixed shared-container header size before this format's scalars; the magic, version, required-feature mask, and checksum-algorithm id are framed by <see cref="SegmentContainer"/>.</summary>
    private const int HeaderSize = SegmentContainer.HeaderSize;

    /// <summary>The scalar block after the header: item count (4) + block item count (4) + block alignment (4).</summary>
    private const int ScalarSize = sizeof(int) + sizeof(int) + sizeof(int);

    /// <summary>The byte size of one row-major item record: subject, predicate, object as three little-endian 32-bit ids.</summary>
    private const int ItemByteSize = 3 * sizeof(uint);

    /// <summary>The default block alignment: one 4 KiB page, so each block is a whole-page checksum domain.</summary>
    public const int DefaultBlockAlignment = 4096;

    /// <summary>The canonical triples this segment holds, in row-major item order; a held view over the caller's contiguous triple buffer, indexed by span without an interface hop.</summary>
    private readonly ReadOnlyMemory<EncodedTriple> items;

    /// <summary>Creates a segment over the canonical triples.</summary>
    /// <param name="items">The canonical triples, in the order they are stored; held as a view, not copied.</param>
    /// <param name="blockItemCount">The number of triples per block; a block boundary is a triple boundary so a block's checksum names its exact item range.</param>
    /// <param name="blockAlignment">The byte alignment each block begins on (a page/sector multiple); <see cref="DefaultBlockAlignment"/> by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockItemCount"/> or <paramref name="blockAlignment"/> is not positive.</exception>
    public ItemSegment(ReadOnlyMemory<EncodedTriple> items, int blockItemCount, int blockAlignment = DefaultBlockAlignment)
    {
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
    public int ItemCount => items.Length;

    /// <summary>The number of item blocks: every full block holds <see cref="BlockItemCount"/> triples and the last holds the remainder.</summary>
    public int BlockCount => (int)(((long)items.Length + BlockItemCount - 1) / BlockItemCount);

    /// <summary>The byte length of a full block's item payload (<see cref="BlockItemCount"/> triples) — the uniform stride a local-parity code zero-extends every block to.</summary>
    public int MaxBlockPayloadByteCount => BlockItemCount * ItemByteSize;

    /// <summary>The byte length of block <paramref name="block"/>'s item payload — a full <see cref="MaxBlockPayloadByteCount"/> except the last block, which holds the remainder.</summary>
    /// <param name="block">The block index, in <c>[0, <see cref="BlockCount"/>)</c>.</param>
    /// <returns>The block's item-payload byte length.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="block"/> is negative or not less than <see cref="BlockCount"/>.</exception>
    public int BlockPayloadByteCount(int block)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(block);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(block, BlockCount);

        return ItemsInBlock(block) * ItemByteSize;
    }

    /// <summary>Copies block <paramref name="block"/>'s item payload — its triples as row-major little-endian 32-bit ids, byte-identical to the bytes <see cref="WriteTo"/> lays down for that block — into <paramref name="destination"/>, so a local-parity build folds the same bytes a repair later reads from the image.</summary>
    /// <param name="block">The block index, in <c>[0, <see cref="BlockCount"/>)</c>.</param>
    /// <param name="destination">The buffer to copy into; at least <see cref="BlockPayloadByteCount"/> bytes for the block.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="block"/> is out of range, or <paramref name="destination"/> is shorter than the block's payload.</exception>
    public void CopyBlockPayload(int block, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(block);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(block, BlockCount);
        int itemsInBlock = ItemsInBlock(block);
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, itemsInBlock * ItemByteSize);

        WriteBlockItems(destination, block, itemsInBlock);
    }

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
        int p = SegmentContainer.WriteHeader(destination, SegmentMagic, FormatVersionMajor, FormatVersionMinor, checksum);

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], items.Length);
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
            SegmentContainer.WriteTrailer(destination, checksum, frontMatterEnd, (int)total);
        }
    }

    /// <summary>Reconstructs a segment's triples from an image written by <see cref="WriteTo"/> into an owned, pooled <see cref="DecodedItemSegment"/>, verifying the front-matter trailer and every block's checksum before its bytes are decoded; the first block that fails is refused (an all-or-nothing read). The caller owns and disposes the returned segment. The report-not-throw counterpart that excludes a corrupt block and continues is <see cref="ReadVerifiedItems"/>.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="pool">The pool the returned triples' buffer is rented from; threaded in by the caller, who owns and disposes the segment.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The owned, pooled canonical triples, in stored order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, or a block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static DecodedItemSegment ReadFrom(ReadOnlySpan<byte> source, MemoryPool<EncodedTriple> pool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        SegmentLayout layout = ParseAndVerifyFrontMatter(source, resolveChecksum);
        int itemCount = layout.ItemCount;

        //Rent at least one element so an empty segment is valid on any pool (some reject a zero-length rent); the
        //items view is sliced to the exact, possibly-empty, count. The rent precedes the per-block verify loop,
        //which throws on a corrupt block, so the success flag returns the buffer on a failure rather than leaking it.
        IMemoryOwner<EncodedTriple> owner = pool.Rent(Math.Max(1, itemCount));
        bool decoded = false;
        try
        {
            Span<EncodedTriple> triples = owner.Memory.Span[..itemCount];
            Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            for(int block = 0; block < layout.BlockCount; block++)
            {
                if(!VerifyBlock(source, layout, block, scratch, out int start, out int itemsInBlock, out long blockOffset))
                {
                    throw new InvalidDataException($"Item block {block} (items [{start}, {start + itemsInBlock})) failed its checksum (at-rest corruption).");
                }

                DecodeBlock(source, blockOffset, itemsInBlock, triples.Slice(start, itemsInBlock));
            }

            decoded = true;
        }
        finally
        {
            if(!decoded)
            {
                owner.Dispose();
            }
        }

        return new DecodedItemSegment(owner, itemCount);
    }

    /// <summary>
    /// Decodes and verifies a segment image through a windowed source: framing, front matter, and every
    /// block read as bounded windows at long offsets, so an image larger than a single span's range
    /// decodes without ever being held contiguously. The all-or-nothing contract matches the span
    /// overload exactly — any checksum failure throws and nothing is returned.
    /// </summary>
    /// <param name="source">The segment image source.</param>
    /// <param name="pool">The pool the decoded triples are rented from; the returned segment owns the rental.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The decoded, verified triples as an owned pooled segment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, or a block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static DecodedItemSegment ReadFrom(SegmentImageSource source, MemoryPool<EncodedTriple> pool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pool);

        SegmentLayout layout = ParseFraming(source, resolveChecksum);
        if(!VerifyFrontMatter(source, layout))
        {
            throw new InvalidDataException("The item segment failed its front-matter checksum (at-rest corruption).");
        }

        int itemCount = layout.ItemCount;

        //Rent at least one element so an empty segment is valid on any pool (some reject a zero-length rent); the
        //items view is sliced to the exact, possibly-empty, count. The rent precedes the per-block verify loop,
        //which throws on a corrupt block, so the success flag returns the buffer on a failure rather than leaking it.
        IMemoryOwner<EncodedTriple> owner = pool.Rent(Math.Max(1, itemCount));
        bool decoded = false;
        try
        {
            Span<EncodedTriple> triples = owner.Memory.Span[..itemCount];
            Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            for(int block = 0; block < layout.BlockCount; block++)
            {
                int start = block * layout.BlockItemCount;
                int itemsInBlock = Math.Min(layout.BlockItemCount, layout.ItemCount - start);
                long blockOffset = layout.FirstBlock + (block * layout.BlockBytes);
                ReadOnlySpan<byte> blockItems = source.Slice(blockOffset, itemsInBlock * ItemByteSize);
                if(!BlockMatchesStoredDigest(blockItems, source, layout, block, scratch))
                {
                    throw new InvalidDataException($"Item block {block} (items [{start}, {start + itemsInBlock})) failed its checksum (at-rest corruption).");
                }

                DecodeBlockItems(blockItems, itemsInBlock, triples.Slice(start, itemsInBlock));
            }

            decoded = true;
        }
        finally
        {
            if(!decoded)
            {
                owner.Dispose();
            }
        }

        return new DecodedItemSegment(owner, itemCount);
    }

    /// <summary>Verifies an image decode-free, reporting each block's at-rest checksum verdict and the front-matter verdict rather than throwing on a per-block or front-matter failure — the format-neutral scrub seam. Framing damage (a malformed, unsupported, or truncated image) is still refused, since a block geometry that cannot be parsed cannot be walked.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The per-block and front-matter verdicts.</returns>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static ArtifactVerifyReport RunVerifyRound(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        SegmentLayout layout = ParseFraming(source, resolveChecksum);
        bool frontMatterValid = VerifyFrontMatter(source, layout);
        BlockVerdict[] verdicts = new BlockVerdict[layout.BlockCount];
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        for(int block = 0; block < layout.BlockCount; block++)
        {
            bool valid = VerifyBlock(source, layout, block, scratch, out _, out int itemsInBlock, out long blockOffset);
            verdicts[block] = new BlockVerdict(block, blockOffset, (long)itemsInBlock * ItemByteSize, valid);
        }

        bool hasChecksums = layout.Checksum is not null;

        return new ArtifactVerifyReport(layout.Checksum?.Id ?? SegmentContainer.ChecksumAlgorithmNone, hasChecksums, hasChecksums, frontMatterValid, verdicts);
    }

    /// <summary>Restores one lost block of an at-rest-corrupt image from a capacity-1 parity block: the lost block's payload is the parity XORed with every surviving block's payload, written back over a copy of the image. The restore is self-checking against the lost block's stored, un-corrupted per-block checksum — a stale parity, a parity built for a different block geometry or a different block count, or more than one truly-lost block yields <see langword="null"/> rather than a wrong block — so it requires a per-block-checksummed image and declines a checksum-free one it cannot self-verify. Only the lost block's payload is rewritten, so the stored digest and front matter the restore is checked against are the original ones.</summary>
    /// <param name="image">The at-rest-corrupt image; its framing and front matter must be intact (only a block payload is corrupt), as the checksum-gated feed read guarantees.</param>
    /// <param name="lostBlock">The index of the block to restore, in <c>[0, block count)</c>.</param>
    /// <param name="parity">The capacity-1 parity block, exactly the block stride (<see cref="MaxBlockPayloadByteCount"/>) wide; a different width cannot restore this segment and yields <see langword="null"/>.</param>
    /// <param name="parityProtectedBlockCount">The number of system-of-record blocks the parity was folded over; a value other than this image's block count is a co-version mismatch and yields <see langword="null"/>.</param>
    /// <param name="pool">The pool the returned image and a transient XOR accumulator are rented from; threaded in by the caller, who owns and disposes the returned image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>An owned, pooled image with the lost block restored when the restore self-verifies; otherwise <see langword="null"/>. The caller disposes a non-<see langword="null"/> result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lostBlock"/> is negative or not less than the block count.</exception>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static PooledArtifactImage? TryRestoreBlockFromParity(ReadOnlySpan<byte> image, int lostBlock, ReadOnlySpan<byte> parity, int parityProtectedBlockCount, MemoryPool<byte> pool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        SegmentLayout layout = ParseFraming(image, resolveChecksum);
        ArgumentOutOfRangeException.ThrowIfNegative(lostBlock);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lostBlock, layout.BlockCount);

        //The restore is verified only against the lost block's stored per-block checksum; a checksum-free image
        //cannot self-verify, so it is declined rather than returned unverified.
        if(layout.Checksum is null)
        {
            return null;
        }

        int stride = layout.BlockItemCount * ItemByteSize;
        if(parity.Length != stride || parityProtectedBlockCount != layout.BlockCount)
        {
            //A parity built for a different block geometry or block count cannot restore this segment.
            return null;
        }

        //The restored image is rented from the caller's pool; a failed self-check returns it to the pool via the
        //finally (the local is nulled out only once the verified image is handed back), rather than leaking an
        //unverified buffer. The transient XOR accumulator is a separate scratch rental disposed within the try.
        PooledArtifactImage? repaired = PooledArtifactImage.Rent(pool, image.Length);
        try
        {
            Span<byte> repairedSpan = repaired.WritableSpan;
            image.CopyTo(repairedSpan);

            using ParityBlock scratch = ParityBlock.Rent(pool, stride);
            Span<byte> accumulator = scratch.WritableSpan;
            parity.CopyTo(accumulator);
            for(int block = 0; block < layout.BlockCount; block++)
            {
                if(block == lostBlock)
                {
                    continue;
                }

                int start = block * layout.BlockItemCount;
                int itemsInBlock = Math.Min(layout.BlockItemCount, layout.ItemCount - start);
                long blockOffset = layout.FirstBlock + (block * layout.BlockBytes);
                ParityCodec.AccumulateXor(accumulator, image.Slice((int)blockOffset, itemsInBlock * ItemByteSize));
            }

            int lostStart = lostBlock * layout.BlockItemCount;
            int lostItemsInBlock = Math.Min(layout.BlockItemCount, layout.ItemCount - lostStart);
            int lostLength = lostItemsInBlock * ItemByteSize;
            long lostOffset = layout.FirstBlock + (lostBlock * layout.BlockBytes);
            accumulator[..lostLength].CopyTo(repairedSpan.Slice((int)lostOffset, lostLength));

            Span<byte> digest = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            if(!VerifyBlock(repairedSpan, layout, lostBlock, digest, out _, out _, out _))
            {
                return null;
            }

            //The restore self-verified: hand ownership of the image to the caller and null the local so the
            //finally does not return the now-borrowed buffer to the pool.
            PooledArtifactImage verified = repaired;
            repaired = null;

            return verified;
        }
        finally
        {
            repaired?.Dispose();
        }
    }

    /// <summary>Reads back the block geometry (item count, triples per block, and block alignment) an image was written with, without verifying its checksums or decoding its triples — the cheap geometry peek a parity re-derive uses to rebuild the parity over the same blocks.</summary>
    /// <param name="source">The byte image.</param>
    /// <returns>The stored item count, triples per block, and block alignment.</returns>
    /// <exception cref="InvalidDataException">The bytes are not an item segment image or are too short to carry the scalars.</exception>
    /// <exception cref="NotSupportedException">The major version is unsupported, or the host is big-endian.</exception>
    public static (int ItemCount, int BlockItemCount, int BlockAlignment) ReadGeometry(ReadOnlySpan<byte> source)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be an item segment image.");
        }

        if(!source[..SegmentMagic.Length].SequenceEqual(SegmentMagic))
        {
            throw new InvalidDataException("The bytes are not an item segment image (magic mismatch).");
        }

        byte versionMajor = source[SegmentMagic.Length];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Item segment format major version {versionMajor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        int itemCount = BinaryPrimitives.ReadInt32LittleEndian(source[HeaderSize..]);
        int blockItemCount = BinaryPrimitives.ReadInt32LittleEndian(source[(HeaderSize + sizeof(int))..]);
        int blockAlignment = BinaryPrimitives.ReadInt32LittleEndian(source[(HeaderSize + (2 * sizeof(int)))..]);

        return (itemCount, blockItemCount, blockAlignment);
    }

    /// <summary>Reads back the block count and the uniform block stride an image was written with — the same two values a co-versioned parity is matched against in <see cref="TryRestoreBlockFromParity"/>'s self-check (its protected block count against this block count, its width against this stride) — without verifying checksums or decoding triples. A verify pass reads these to confirm a parity was folded over this segment's geometry before a block is ever lost, rather than discovering the mismatch only when the restore declines.</summary>
    /// <param name="source">The byte image.</param>
    /// <returns>The stored block count and the uniform block stride (the block item count times the item record size).</returns>
    /// <exception cref="InvalidDataException">The bytes are not an item segment image, are too short to carry the scalars, or declare a non-positive block item count.</exception>
    /// <exception cref="NotSupportedException">The major version is unsupported, or the host is big-endian.</exception>
    public static (int BlockCount, int BlockStride) ReadBlockParityGeometry(ReadOnlySpan<byte> source)
    {
        (int itemCount, int blockItemCount, _) = ReadGeometry(source);
        if(itemCount < 0 || blockItemCount <= 0)
        {
            throw new InvalidDataException("The item segment declares a negative item count or a non-positive block item count.");
        }

        int blockCount = (int)(((long)itemCount + blockItemCount - 1) / blockItemCount);
        int blockStride = blockItemCount * ItemByteSize;

        return (blockCount, blockStride);
    }

    /// <summary>Parses an image's framing — magic, version, feature mask, checksum-algorithm id, and block geometry — refusing a malformed, unsupported, or truncated image, and returns the geometry needed to locate and verify every block. It does NOT verify the front-matter trailer (that is <see cref="VerifyFrontMatter"/>), so a decode-free verify can report the front-matter verdict rather than throw.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The parsed layout.</returns>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SegmentLayout ParseFraming(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be an item segment image.");
        }

        ChecksumAlgorithm? checksum = SegmentContainer.ParseHeader(source, SegmentMagic, FormatVersionMajor, resolveChecksum, "item segment");

        int p = HeaderSize;
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

        return new SegmentLayout(itemCount, blockItemCount, blockCount, checksum, HeaderSize + ScalarSize, firstBlock, blockBytes, frontMatterEnd, total);
    }

    /// <summary>Recomputes the front-matter trailer and compares it to the stored digest, reporting the verdict rather than throwing; <see langword="true"/> when the image carries no checksum.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <returns>Whether the front-matter trailer matched its stored digest.</returns>
    private static bool VerifyFrontMatter(ReadOnlySpan<byte> source, in SegmentLayout layout)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        return SegmentContainer.VerifyTrailer(source, layout.Checksum, (int)layout.FrontMatterEnd, (int)layout.Total);
    }

    /// <summary>The windowed-source counterpart of <see cref="ParseFraming(ReadOnlySpan{byte}, ResolveChecksumAlgorithmDelegate?)"/>: the header and scalars read from one small leading window, and the truncation check compares the declared total against the source length, both as longs.</summary>
    /// <param name="source">The segment image source.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The parsed layout.</returns>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, is truncated, or declares front matter past a span's range.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SegmentLayout ParseFraming(SegmentImageSource source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be an item segment image.");
        }

        ReadOnlySpan<byte> front = source.Slice(0, HeaderSize + ScalarSize);
        ChecksumAlgorithm? checksum = SegmentContainer.ParseHeader(front, SegmentMagic, FormatVersionMajor, resolveChecksum, "item segment");

        int p = HeaderSize;
        int itemCount = BinaryPrimitives.ReadInt32LittleEndian(front[p..]);
        p += sizeof(int);
        int blockItemCount = BinaryPrimitives.ReadInt32LittleEndian(front[p..]);
        p += sizeof(int);
        int blockAlignment = BinaryPrimitives.ReadInt32LittleEndian(front[p..]);
        p += sizeof(int);
        if(itemCount < 0 || blockItemCount <= 0 || blockAlignment <= 0)
        {
            throw new InvalidDataException("The item segment declares a negative item count or a non-positive block geometry.");
        }

        int blockCount = (int)(((long)itemCount + blockItemCount - 1) / blockItemCount);
        long frontMatterEnd = HeaderSize + ScalarSize + (checksum is null ? 0L : (long)blockCount * checksum.ByteWidth);

        //The front matter (header, scalars, per-block checksum section) is digested as ONE window, so a declared
        //geometry that would push it past a span's range is refused as malformed rather than sliced short.
        if(frontMatterEnd > int.MaxValue)
        {
            throw new InvalidDataException("The item segment declares front matter past a single span's range.");
        }

        long blockBytes = Align((long)blockItemCount * ItemByteSize, blockAlignment);
        long firstBlock = Align(frontMatterEnd, blockAlignment);
        long total = firstBlock + ((long)blockCount * blockBytes) + (checksum is null ? 0 : checksum.ByteWidth);
        if(total > source.Length)
        {
            throw new InvalidDataException("The item segment is truncated: its declared blocks run past the image.");
        }

        return new SegmentLayout(itemCount, blockItemCount, blockCount, checksum, HeaderSize + ScalarSize, firstBlock, blockBytes, frontMatterEnd, total);
    }

    /// <summary>The windowed-source counterpart of <see cref="VerifyFrontMatter(ReadOnlySpan{byte}, in SegmentLayout)"/>: the front matter digests as one window and the stored trailer digest reads from its long offset at the image's end.</summary>
    /// <param name="source">The segment image source.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <returns>Whether the front-matter trailer matched its stored digest.</returns>
    private static bool VerifyFrontMatter(SegmentImageSource source, in SegmentLayout layout)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        layout.Checksum.Compute(source.Slice(0, (int)layout.FrontMatterEnd), computed[..layout.Checksum.ByteWidth]);

        return computed[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.Total - layout.Checksum.ByteWidth, layout.Checksum.ByteWidth));
    }

    /// <summary>Recomputes a block window's checksum and compares it to the digest stored in the front matter's checksum section, read from the source at the block's slot; always <see langword="true"/> when the image carries no checksums.</summary>
    /// <param name="blockItems">The block's item bytes, already windowed from the source.</param>
    /// <param name="source">The segment image source the stored digest reads from.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <param name="block">The block index.</param>
    /// <param name="scratch">Scratch at least <see cref="ChecksumAlgorithm.MaximumByteWidth"/> bytes long for the recomputed digest.</param>
    /// <returns>Whether the block's checksum matched.</returns>
    private static bool BlockMatchesStoredDigest(ReadOnlySpan<byte> blockItems, SegmentImageSource source, in SegmentLayout layout, int block, Span<byte> scratch)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        layout.Checksum.Compute(blockItems, scratch[..layout.Checksum.ByteWidth]);

        return scratch[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.ChecksumSectionOffset + ((long)block * layout.Checksum.ByteWidth), layout.Checksum.ByteWidth));
    }

    /// <summary>Parses an image's framing and verifies its front-matter trailer, refusing front-matter damage so the block walk that follows can trust the geometry; the throwing counterpart used by the all-or-nothing read paths.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The verified layout.</returns>
    /// <exception cref="InvalidDataException">The image is not an item segment, is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SegmentLayout ParseAndVerifyFrontMatter(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        SegmentLayout layout = ParseFraming(source, resolveChecksum);
        if(!VerifyFrontMatter(source, layout))
        {
            throw new InvalidDataException("The item segment failed its front-matter checksum (at-rest corruption).");
        }

        return layout;
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
        DecodeBlockItems(source.Slice((int)blockOffset, itemsInBlock * ItemByteSize), itemsInBlock, destination);
    }

    /// <summary>Decodes one block window's row-major records into <paramref name="destination"/> as triples — the shared core the span and windowed-source read paths both funnel through.</summary>
    /// <param name="blockItems">The block's item bytes.</param>
    /// <param name="itemsInBlock">The number of triples the block covers.</param>
    /// <param name="destination">The span to decode into; exactly <paramref name="itemsInBlock"/> long.</param>
    private static void DecodeBlockItems(ReadOnlySpan<byte> blockItems, int itemsInBlock, Span<EncodedTriple> destination)
    {
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

        return Math.Min(BlockItemCount, items.Length - start);
    }

    /// <summary>Writes a block's triples row-major into <paramref name="destination"/> as three little-endian 32-bit ids each.</summary>
    /// <param name="destination">The block's item-byte region (exactly <paramref name="itemsInBlock"/> records long).</param>
    /// <param name="block">The block index.</param>
    /// <param name="itemsInBlock">The number of triples in the block.</param>
    private void WriteBlockItems(Span<byte> destination, int block, int itemsInBlock)
    {
        int start = block * BlockItemCount;
        ReadOnlySpan<EncodedTriple> span = items.Span;
        for(int i = 0; i < itemsInBlock; i++)
        {
            EncodedTriple triple = span[start + i];
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
        /// <param name="frontMatterEnd">The byte offset one past the front matter (header, scalars, and per-block section) the trailer covers.</param>
        /// <param name="total">The total image byte size, whose tail holds the front-matter trailer.</param>
        internal SegmentLayout(int itemCount, int blockItemCount, int blockCount, ChecksumAlgorithm? checksum, int checksumSectionOffset, long firstBlock, long blockBytes, long frontMatterEnd, long total)
        {
            ItemCount = itemCount;
            BlockItemCount = blockItemCount;
            BlockCount = blockCount;
            Checksum = checksum;
            ChecksumSectionOffset = checksumSectionOffset;
            FirstBlock = firstBlock;
            BlockBytes = blockBytes;
            FrontMatterEnd = frontMatterEnd;
            Total = total;
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

        /// <summary>The byte offset one past the front matter the trailer covers.</summary>
        internal long FrontMatterEnd { get; }

        /// <summary>The total image byte size, whose tail holds the front-matter trailer.</summary>
        internal long Total { get; }
    }
}
