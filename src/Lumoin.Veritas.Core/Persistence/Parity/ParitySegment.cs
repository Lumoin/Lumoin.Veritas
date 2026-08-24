using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Parity;

/// <summary>
/// The persisted local-parity sidecar: the single capacity-1 parity block that protects a system-of-record
/// segment's item blocks. The parity is the byte-wise XOR of every system-of-record block's item payload, each
/// zero-extended to the block stride (<see cref="ItemSegment.MaxBlockPayloadByteCount"/>), so a lost
/// system-of-record block is restored as the parity XORed with the surviving blocks. It is co-versioned with the
/// system-of-record it protects: it records the <see cref="ProtectedBlockCount"/> it was folded over, so a repair
/// can refuse a parity that does not match the segment being repaired.
/// </summary>
/// <remarks>
/// <para>
/// The byte image reuses the persistence container's framing discipline — a magic-versioned header with a
/// required-feature mask, an explicitly-selected per-block <see cref="ChecksumAlgorithm"/>, a per-block checksum
/// section, and a single front-matter checksum trailer over the header and that section — the same shape as the
/// system-of-record segment and the integrity sketch, so one scrub walks all three through
/// <see cref="RunVerifyRound"/>. A capacity-1 parity is one block and one checksum domain: it is used atomically
/// (the whole parity is needed to restore a lost block), so a torn write anywhere in it invalidates it and the
/// repair ladder descends past the local-parity rung rather than restoring from a partial parity. The parity
/// block begins on a configurable alignment boundary so a torn write or a faulted page maps to that one checksum
/// domain; the alignment padding is inert zero-fill and is not part of any checksum.
/// </para>
/// </remarks>
public sealed class ParitySegment
{
    /// <summary>The 8-byte magic identifying a Veritas local-parity image.</summary>
    private static ReadOnlySpan<byte> ParityMagic => "VTSPAR01"u8;

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The fixed shared-container header size before this format's scalars; the magic, version, required-feature mask, and checksum-algorithm id are framed by <see cref="SegmentContainer"/>.</summary>
    private const int HeaderSize = SegmentContainer.HeaderSize;

    /// <summary>The scalar block after the header: parity length (4) + protected block count (4) + block alignment (4).</summary>
    private const int ScalarSize = sizeof(int) + sizeof(int) + sizeof(int);

    /// <summary>The default block alignment: one 4 KiB page, so the parity block is a whole-page checksum domain.</summary>
    public const int DefaultBlockAlignment = 4096;

    /// <summary>The parity block bytes — one capacity-1 parity block, exactly <see cref="ParityLength"/> bytes.</summary>
    private readonly ReadOnlyMemory<byte> parity;

    /// <summary>Creates a segment over a single capacity-1 parity block.</summary>
    /// <param name="parity">The parity block bytes: the byte-wise XOR of the protected system-of-record blocks, the block stride wide.</param>
    /// <param name="protectedBlockCount">The number of system-of-record blocks the parity was folded over; the repair matches this against the segment it restores.</param>
    /// <param name="blockAlignment">The byte alignment the parity block begins on (a page/sector multiple); <see cref="DefaultBlockAlignment"/> by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parity"/> is empty, or <paramref name="protectedBlockCount"/> or <paramref name="blockAlignment"/> is not positive.</exception>
    public ParitySegment(ReadOnlyMemory<byte> parity, int protectedBlockCount, int blockAlignment = DefaultBlockAlignment)
    {
        ArgumentOutOfRangeException.ThrowIfZero(parity.Length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(protectedBlockCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockAlignment);

        this.parity = parity;
        ProtectedBlockCount = protectedBlockCount;
        BlockAlignment = blockAlignment;
    }

    /// <summary>The parity block byte length — the system-of-record block stride this parity protects.</summary>
    public int ParityLength => parity.Length;

    /// <summary>The number of system-of-record blocks the parity was folded over.</summary>
    public int ProtectedBlockCount { get; }

    /// <summary>The byte alignment the parity block begins on.</summary>
    public int BlockAlignment { get; }

    /// <summary>The number of bytes <see cref="WriteTo"/> writes under the given checksum selection.</summary>
    /// <param name="checksum">The per-block checksum algorithm whose section and self-trailer size the image, or <see langword="null"/> for none.</param>
    /// <returns>The image byte size.</returns>
    public long ComputeSerializedSize(ChecksumAlgorithm? checksum)
    {
        long frontMatterEnd = FrontMatterSize(checksum);
        long firstBlock = Align(frontMatterEnd, BlockAlignment);
        long blockBytes = Align(parity.Length, BlockAlignment);

        return firstBlock + blockBytes + (checksum is null ? 0 : checksum.ByteWidth);
    }

    /// <summary>Writes this segment's self-describing parity image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same <paramref name="checksum"/>); alignment padding is zero-filled.</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="checksum">The per-block checksum algorithm to stamp and compute, or <see langword="null"/> for no checksums.</param>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public void WriteTo(Span<byte> destination, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        int p = SegmentContainer.WriteHeader(destination, ParityMagic, FormatVersionMajor, FormatVersionMinor, checksum);

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], parity.Length);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], ProtectedBlockCount);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], BlockAlignment);
        p += sizeof(int);

        //The single per-block checksum follows the scalars; reserve it now and backfill once the parity block
        //is written, so the digest is over the bytes actually laid down.
        int checksumSectionOffset = p;
        int frontMatterEnd = (int)FrontMatterSize(checksum);

        //Zero-fill the gap from the front matter to the aligned parity block and its trailing padding, so the
        //image carries no stale buffer bytes.
        destination[frontMatterEnd..].Clear();

        long firstBlock = Align(frontMatterEnd, BlockAlignment);
        parity.Span.CopyTo(destination.Slice((int)firstBlock, parity.Length));

        if(checksum is not null)
        {
            checksum.Compute(
                destination.Slice((int)firstBlock, parity.Length),
                destination.Slice(checksumSectionOffset, checksum.ByteWidth));

            //The front-matter trailer covers the header, scalars, and the per-block checksum — everything the
            //block digest does not — and sits at the image tail after the parity block.
            long total = ComputeSerializedSize(checksum);
            SegmentContainer.WriteTrailer(destination, checksum, frontMatterEnd, (int)total);
        }
    }

    /// <summary>Reconstructs the parity block from an image written by <see cref="WriteTo"/> into an owned, pooled <see cref="ParityBlock"/>, verifying the front-matter trailer and the block's checksum before its bytes are copied out; the caller owns and disposes the returned block. The report-not-throw, decode-free counterpart is <see cref="RunVerifyRound"/>.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="pool">The pool the returned parity block's buffer is rented from; threaded in by the caller, who owns and disposes the block.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The owned, pooled parity block, exactly the protected block stride long.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The image is not a parity segment, is malformed, or the block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static ParityBlock ReadFrom(ReadOnlySpan<byte> source, MemoryPool<byte> pool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ParityLayout layout = ParseAndVerifyFrontMatter(source, resolveChecksum);
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        if(!VerifyBlock(source, layout, scratch))
        {
            throw new InvalidDataException("The parity block failed its checksum (at-rest corruption).");
        }

        //The rent is the last fallible step: nothing between it and the return throws, so a verified block is
        //never rented and then leaked.
        ParityBlock block = ParityBlock.Rent(pool, layout.ParityLength);
        source.Slice((int)layout.FirstBlock, layout.ParityLength).CopyTo(block.WritableSpan);

        return block;
    }

    /// <summary>Verifies an image decode-free, reporting the parity block's at-rest checksum verdict and the front-matter verdict rather than throwing on a failure — the format-neutral scrub seam. Framing damage (a malformed, unsupported, or truncated image) is still refused, since a geometry that cannot be parsed cannot be walked.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The block and front-matter verdicts.</returns>
    /// <exception cref="InvalidDataException">The image is not a parity segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static ArtifactVerifyReport RunVerifyRound(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ParityLayout layout = ParseFraming(source, resolveChecksum);
        bool frontMatterValid = VerifyFrontMatter(source, layout);
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        bool valid = VerifyBlock(source, layout, scratch);
        BlockVerdict[] verdicts = [new BlockVerdict(0, layout.FirstBlock, layout.ParityLength, valid)];
        bool hasChecksums = layout.Checksum is not null;

        return new ArtifactVerifyReport(layout.Checksum?.Id ?? SegmentContainer.ChecksumAlgorithmNone, hasChecksums, hasChecksums, frontMatterValid, verdicts);
    }

    /// <summary>Reads back the parity geometry (the parity length and the protected block count) an image was written with, without verifying its checksum or copying its bytes — the cheap geometry peek a repair uses to confirm a parity matches the system-of-record it is asked to restore.</summary>
    /// <param name="source">The byte image.</param>
    /// <returns>The stored parity length and protected block count.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a parity segment image or are too short to carry the scalars.</exception>
    /// <exception cref="NotSupportedException">The major version is unsupported, or the host is big-endian.</exception>
    public static (int ParityLength, int ProtectedBlockCount) ReadGeometry(ReadOnlySpan<byte> source)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be a parity segment image.");
        }

        if(!source[..ParityMagic.Length].SequenceEqual(ParityMagic))
        {
            throw new InvalidDataException("The bytes are not a parity segment image (magic mismatch).");
        }

        byte versionMajor = source[ParityMagic.Length];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Parity segment format major version {versionMajor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        int parityLength = BinaryPrimitives.ReadInt32LittleEndian(source[HeaderSize..]);
        int protectedBlockCount = BinaryPrimitives.ReadInt32LittleEndian(source[(HeaderSize + sizeof(int))..]);

        return (parityLength, protectedBlockCount);
    }

    /// <summary>Folds the capacity-1 parity over <paramref name="systemOfRecord"/>'s block payloads into <paramref name="parityDestination"/>: the byte-wise XOR of every block's item payload, each zero-extended to the block stride. The result is the parity bytes a <see cref="ParitySegment"/> is constructed over.</summary>
    /// <param name="systemOfRecord">The system-of-record segment to protect; it must hold at least one block.</param>
    /// <param name="parityDestination">The destination for the parity bytes, exactly <see cref="ItemSegment.MaxBlockPayloadByteCount"/> long (the block stride).</param>
    /// <param name="scratchPool">The pool a transient per-block scratch buffer is rented from; threaded in by the caller.</param>
    /// <returns>The protected block count (<see cref="ItemSegment.BlockCount"/>), to construct the segment with.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="systemOfRecord"/> or <paramref name="scratchPool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="systemOfRecord"/> holds no blocks.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parityDestination"/> is not exactly the block stride long.</exception>
    public static int BuildParity(ItemSegment systemOfRecord, Span<byte> parityDestination, MemoryPool<byte> scratchPool)
    {
        ArgumentNullException.ThrowIfNull(systemOfRecord);
        ArgumentNullException.ThrowIfNull(scratchPool);
        int blockCount = systemOfRecord.BlockCount;
        if(blockCount == 0)
        {
            throw new ArgumentException("Cannot build a parity over a system-of-record that holds no blocks.", nameof(systemOfRecord));
        }

        int stride = systemOfRecord.MaxBlockPayloadByteCount;
        ArgumentOutOfRangeException.ThrowIfNotEqual(parityDestination.Length, stride);

        parityDestination.Clear();
        using ParityBlock scratch = ParityBlock.Rent(scratchPool, stride);
        for(int block = 0; block < blockCount; block++)
        {
            int payloadLength = systemOfRecord.BlockPayloadByteCount(block);
            Span<byte> payload = scratch.WritableSpan[..payloadLength];
            systemOfRecord.CopyBlockPayload(block, payload);
            ParityCodec.AccumulateXor(parityDestination, payload);
        }

        return blockCount;
    }

    /// <summary>Parses an image's framing — magic, version, feature mask, checksum-algorithm id, and parity geometry — refusing a malformed, unsupported, or truncated image, and returns the geometry needed to locate and verify the parity block. It does NOT verify the front-matter trailer (that is <see cref="VerifyFrontMatter"/>), so a decode-free verify can report the front-matter verdict rather than throw.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The parsed layout.</returns>
    /// <exception cref="InvalidDataException">The image is not a parity segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static ParityLayout ParseFraming(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be a parity segment image.");
        }

        ChecksumAlgorithm? checksum = SegmentContainer.ParseHeader(source, ParityMagic, FormatVersionMajor, resolveChecksum, "parity segment");

        int p = HeaderSize;
        int parityLength = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int protectedBlockCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int blockAlignment = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(parityLength <= 0 || protectedBlockCount <= 0 || blockAlignment <= 0)
        {
            throw new InvalidDataException("The parity segment declares a non-positive parity length, protected block count, or alignment.");
        }

        long frontMatterEnd = HeaderSize + ScalarSize + (checksum is null ? 0L : checksum.ByteWidth);
        long firstBlock = Align(frontMatterEnd, blockAlignment);
        long blockBytes = Align(parityLength, blockAlignment);
        long total = firstBlock + blockBytes + (checksum is null ? 0 : checksum.ByteWidth);
        if(total > source.Length)
        {
            throw new InvalidDataException("The parity segment is truncated: its declared block runs past the image.");
        }

        return new ParityLayout(parityLength, protectedBlockCount, checksum, HeaderSize + ScalarSize, firstBlock, frontMatterEnd, total);
    }

    /// <summary>Recomputes the front-matter trailer and compares it to the stored digest, reporting the verdict rather than throwing; <see langword="true"/> when the image carries no checksum.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <returns>Whether the front-matter trailer matched its stored digest.</returns>
    private static bool VerifyFrontMatter(ReadOnlySpan<byte> source, in ParityLayout layout)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        return SegmentContainer.VerifyTrailer(source, layout.Checksum, (int)layout.FrontMatterEnd, (int)layout.Total);
    }

    /// <summary>Parses an image's framing and verifies its front-matter trailer, refusing front-matter damage so the block read that follows can trust the geometry; the throwing counterpart used by the all-or-nothing read path.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The verified layout.</returns>
    /// <exception cref="InvalidDataException">The image is not a parity segment, is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static ParityLayout ParseAndVerifyFrontMatter(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        ParityLayout layout = ParseFraming(source, resolveChecksum);
        if(!VerifyFrontMatter(source, layout))
        {
            throw new InvalidDataException("The parity segment failed its front-matter checksum (at-rest corruption).");
        }

        return layout;
    }

    /// <summary>Recomputes the parity block's checksum and compares it to the stored digest, reporting the verdict rather than throwing; always <see langword="true"/> when the image carries no checksum.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <param name="scratch">Scratch at least <see cref="ChecksumAlgorithm.MaximumByteWidth"/> bytes long for the recomputed digest.</param>
    /// <returns>Whether the parity block's checksum matched.</returns>
    private static bool VerifyBlock(ReadOnlySpan<byte> source, in ParityLayout layout, Span<byte> scratch)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        ReadOnlySpan<byte> block = source.Slice((int)layout.FirstBlock, layout.ParityLength);
        layout.Checksum.Compute(block, scratch[..layout.Checksum.ByteWidth]);

        return scratch[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.ChecksumSectionOffset, layout.Checksum.ByteWidth));
    }

    /// <summary>The byte size of the header, scalars, and per-block checksum — everything the front-matter trailer covers, before the aligned parity block.</summary>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <returns>The front-matter byte size.</returns>
    private static long FrontMatterSize(ChecksumAlgorithm? checksum)
    {
        return HeaderSize + ScalarSize + (checksum is null ? 0L : checksum.ByteWidth);
    }

    /// <summary>Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.</summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The alignment (positive).</param>
    /// <returns>The aligned value.</returns>
    private static long Align(long value, long alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    /// <summary>The framing-verified geometry of a parity-segment image: everything needed to locate and checksum-verify the parity block once the header and front-matter trailer have passed.</summary>
    private readonly struct ParityLayout
    {
        /// <summary>Creates a layout.</summary>
        /// <param name="parityLength">The parity block byte length.</param>
        /// <param name="protectedBlockCount">The number of system-of-record blocks the parity protects.</param>
        /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</param>
        /// <param name="checksumSectionOffset">The byte offset of the per-block checksum.</param>
        /// <param name="firstBlock">The byte offset of the aligned parity block.</param>
        /// <param name="frontMatterEnd">The byte offset one past the front matter (header, scalars, and per-block checksum) the trailer covers.</param>
        /// <param name="total">The total image byte size, whose tail holds the front-matter trailer.</param>
        internal ParityLayout(int parityLength, int protectedBlockCount, ChecksumAlgorithm? checksum, int checksumSectionOffset, long firstBlock, long frontMatterEnd, long total)
        {
            ParityLength = parityLength;
            ProtectedBlockCount = protectedBlockCount;
            Checksum = checksum;
            ChecksumSectionOffset = checksumSectionOffset;
            FirstBlock = firstBlock;
            FrontMatterEnd = frontMatterEnd;
            Total = total;
        }

        /// <summary>The parity block byte length.</summary>
        internal int ParityLength { get; }

        /// <summary>The number of system-of-record blocks the parity protects.</summary>
        internal int ProtectedBlockCount { get; }

        /// <summary>The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</summary>
        internal ChecksumAlgorithm? Checksum { get; }

        /// <summary>The byte offset of the per-block checksum.</summary>
        internal int ChecksumSectionOffset { get; }

        /// <summary>The byte offset of the aligned parity block.</summary>
        internal long FirstBlock { get; }

        /// <summary>The byte offset one past the front matter the trailer covers.</summary>
        internal long FrontMatterEnd { get; }

        /// <summary>The total image byte size, whose tail holds the front-matter trailer.</summary>
        internal long Total { get; }
    }
}
