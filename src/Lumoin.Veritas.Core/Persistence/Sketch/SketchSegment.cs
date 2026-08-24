using System;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Sketch;

/// <summary>
/// The persisted integrity sketch: a stream of fixed-width coded symbols stored as fixed-count symbol
/// blocks. A symbol is the opaque output of the reconciliation encoder (a content-key-wide fold plus its
/// own purity checksum); this format stores those bytes verbatim and never interprets them. Each block
/// carries exactly <see cref="SymbolsPerBlock"/> symbols (the last holds the remainder) and an at-rest
/// checksum, so a block's checksum failure names the exact symbol range <c>[start, start + count)</c> it
/// covers and the read path can refuse a corrupt block before its symbols are folded.
/// </summary>
/// <remarks>
/// <para>
/// The byte image reuses the persistence container's framing discipline — a magic-versioned header with a
/// required-feature mask, an explicitly-selected per-block <see cref="ChecksumAlgorithm"/>, a per-block
/// checksum section, and a single front-matter checksum trailer over the header and that section. It is its
/// own <see cref="Lumoin.Veritas.Core.Persistence.Manifest.ManifestFileRole"/> artifact, not the columnar
/// container and not the row-major system-of-record segment: a coded symbol is an XOR-fold across every
/// item whose walk visits it, never one-to-one with any item, so the sketch's symbol blocks deliberately do
/// not align to the system-of-record's item blocks. Each block begins on a configurable alignment boundary
/// (a page/sector multiple by default) so a torn write or a faulted page maps to exactly one checksum
/// domain; the alignment padding is inert zero-fill and is not part of any checksum.
/// </para>
/// <para>
/// At-rest detection is this format's concern: a block that fails its checksum is refused. Detection of an
/// individual symbol's purity for decoding (never fold a checksum-failed symbol) is the reconciliation
/// decoder's concern, carried inside the opaque symbol bytes, not here.
/// </para>
/// </remarks>
public sealed class SketchSegment
{
    /// <summary>The 8-byte magic identifying a Veritas integrity-sketch image.</summary>
    private static ReadOnlySpan<byte> SketchMagic => "VTSSKT01"u8;

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The fixed shared-container header size before this format's scalars; the magic, version, required-feature mask, and checksum-algorithm id are framed by <see cref="SegmentContainer"/>.</summary>
    private const int HeaderSize = SegmentContainer.HeaderSize;

    /// <summary>The scalar block after the header: symbol count (4) + symbols per block (4) + symbol width (4) + block alignment (4).</summary>
    private const int ScalarSize = sizeof(int) + sizeof(int) + sizeof(int) + sizeof(int);

    /// <summary>The default block alignment: one 4 KiB page, so each block is a whole-page checksum domain.</summary>
    public const int DefaultBlockAlignment = 4096;

    /// <summary>The flat coded-symbol bytes this segment holds, in symbol order, exactly <see cref="SymbolWidth"/> bytes each.</summary>
    private readonly ReadOnlyMemory<byte> symbols;

    /// <summary>Creates a segment over a flat coded-symbol byte stream.</summary>
    /// <param name="symbols">The coded symbols laid out back to back, each exactly <paramref name="symbolWidth"/> bytes.</param>
    /// <param name="symbolWidth">The byte width of one coded symbol.</param>
    /// <param name="symbolsPerBlock">The number of symbols per block; a block boundary is a symbol boundary so a block's checksum names its exact symbol range.</param>
    /// <param name="blockAlignment">The byte alignment each block begins on (a page/sector multiple); <see cref="DefaultBlockAlignment"/> by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolWidth"/>, <paramref name="symbolsPerBlock"/>, or <paramref name="blockAlignment"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="symbols"/> is not a whole multiple of <paramref name="symbolWidth"/>.</exception>
    public SketchSegment(ReadOnlyMemory<byte> symbols, int symbolWidth, int symbolsPerBlock, int blockAlignment = DefaultBlockAlignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolsPerBlock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockAlignment);
        if(symbols.Length % symbolWidth != 0)
        {
            throw new ArgumentException("The coded-symbol byte length is not a whole multiple of the symbol width.", nameof(symbols));
        }

        this.symbols = symbols;
        SymbolWidth = symbolWidth;
        SymbolsPerBlock = symbolsPerBlock;
        BlockAlignment = blockAlignment;
    }

    /// <summary>The byte width of one coded symbol.</summary>
    public int SymbolWidth { get; }

    /// <summary>The number of symbols per block.</summary>
    public int SymbolsPerBlock { get; }

    /// <summary>The byte alignment each block begins on.</summary>
    public int BlockAlignment { get; }

    /// <summary>The number of coded symbols in the segment.</summary>
    public int SymbolCount => symbols.Length / SymbolWidth;

    /// <summary>The number of symbol blocks: every full block holds <see cref="SymbolsPerBlock"/> symbols and the last holds the remainder.</summary>
    public int BlockCount => (int)(((long)SymbolCount + SymbolsPerBlock - 1) / SymbolsPerBlock);

    /// <summary>The number of bytes <see cref="WriteTo"/> writes under the given checksum selection.</summary>
    /// <param name="checksum">The per-block checksum algorithm whose section and self-trailer size the image, or <see langword="null"/> for none.</param>
    /// <returns>The image byte size.</returns>
    public long ComputeSerializedSize(ChecksumAlgorithm? checksum)
    {
        int blockCount = BlockCount;
        long frontMatterEnd = FrontMatterSize(blockCount, checksum);
        long firstBlock = Align(frontMatterEnd, BlockAlignment);
        long blockBytes = Align((long)SymbolsPerBlock * SymbolWidth, BlockAlignment);

        return firstBlock + ((long)blockCount * blockBytes) + (checksum is null ? 0 : checksum.ByteWidth);
    }

    /// <summary>Writes this segment's self-describing symbol-block image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same <paramref name="checksum"/>); alignment padding is zero-filled.</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="checksum">The per-block checksum algorithm to stamp and compute, or <see langword="null"/> for no checksums.</param>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public void WriteTo(Span<byte> destination, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        int blockCount = BlockCount;
        int symbolCount = SymbolCount;
        int p = SegmentContainer.WriteHeader(destination, SketchMagic, FormatVersionMajor, FormatVersionMinor, checksum);

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], symbolCount);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], SymbolsPerBlock);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], SymbolWidth);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], BlockAlignment);
        p += sizeof(int);

        //The per-block checksum section follows the scalars; reserve it now and backfill once each block
        //is written, so each digest is over the bytes actually laid down.
        int checksumSectionOffset = p;
        int frontMatterEnd = (int)FrontMatterSize(blockCount, checksum);

        //Zero-fill the gap from the front matter to the first aligned block, then every block's payload and
        //its trailing alignment padding, so the image carries no stale buffer bytes.
        destination[frontMatterEnd..].Clear();

        long blockBytes = Align((long)SymbolsPerBlock * SymbolWidth, BlockAlignment);
        long firstBlock = Align(frontMatterEnd, BlockAlignment);
        ReadOnlySpan<byte> symbolBytes = symbols.Span;
        for(int block = 0; block < blockCount; block++)
        {
            long blockOffset = firstBlock + (block * blockBytes);
            int symbolsInBlock = SymbolsInBlock(block);
            int byteCount = symbolsInBlock * SymbolWidth;
            int sourceOffset = (block * SymbolsPerBlock) * SymbolWidth;
            symbolBytes.Slice(sourceOffset, byteCount).CopyTo(destination.Slice((int)blockOffset, byteCount));

            if(checksum is not null)
            {
                checksum.Compute(
                    destination.Slice((int)blockOffset, byteCount),
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

    /// <summary>Reconstructs the flat coded-symbol bytes from an image written by <see cref="WriteTo"/>, verifying the front-matter trailer and every block's checksum before its bytes are copied out; the first block that fails is refused (an all-or-nothing read). The report-not-throw, decode-free counterpart is <see cref="RunVerifyRound"/>.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The flat coded-symbol bytes, in stored order.</returns>
    /// <exception cref="InvalidDataException">The image is not a sketch segment, is malformed, or a block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static byte[] ReadFrom(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        SketchLayout layout = ParseAndVerifyFrontMatter(source, resolveChecksum);
        byte[] symbols = new byte[layout.SymbolCount * layout.SymbolWidth];
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        for(int block = 0; block < layout.BlockCount; block++)
        {
            if(!VerifyBlock(source, layout, block, scratch, out int start, out int symbolsInBlock, out long blockOffset))
            {
                throw new InvalidDataException($"Sketch block {block} (symbols [{start}, {start + symbolsInBlock})) failed its checksum (at-rest corruption).");
            }

            int byteCount = symbolsInBlock * layout.SymbolWidth;
            source.Slice((int)blockOffset, byteCount).CopyTo(symbols.AsSpan(start * layout.SymbolWidth, byteCount));
        }

        return symbols;
    }

    /// <summary>Verifies an image decode-free, reporting each block's at-rest checksum verdict and the front-matter verdict rather than throwing on a per-block or front-matter failure — the format-neutral scrub seam. Framing damage (a malformed, unsupported, or truncated image) is still refused, since a block geometry that cannot be parsed cannot be walked.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The per-block and front-matter verdicts.</returns>
    /// <exception cref="InvalidDataException">The image is not a sketch segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static ArtifactVerifyReport RunVerifyRound(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        SketchLayout layout = ParseFraming(source, resolveChecksum);
        bool frontMatterValid = VerifyFrontMatter(source, layout);
        BlockVerdict[] verdicts = new BlockVerdict[layout.BlockCount];
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        for(int block = 0; block < layout.BlockCount; block++)
        {
            bool valid = VerifyBlock(source, layout, block, scratch, out _, out int symbolsInBlock, out long blockOffset);
            verdicts[block] = new BlockVerdict(block, blockOffset, (long)symbolsInBlock * layout.SymbolWidth, valid);
        }

        bool hasChecksums = layout.Checksum is not null;

        return new ArtifactVerifyReport(layout.Checksum?.Id ?? SegmentContainer.ChecksumAlgorithmNone, hasChecksums, hasChecksums, frontMatterValid, verdicts);
    }

    /// <summary>Parses an image's framing — magic, version, feature mask, checksum-algorithm id, and block geometry — refusing a malformed, unsupported, or truncated image, and returns the geometry needed to locate and verify every block. It does NOT verify the front-matter trailer (that is <see cref="VerifyFrontMatter"/>), so a decode-free verify can report the front-matter verdict rather than throw.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The parsed layout.</returns>
    /// <exception cref="InvalidDataException">The image is not a sketch segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SketchLayout ParseFraming(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be a sketch segment image.");
        }

        ChecksumAlgorithm? checksum = SegmentContainer.ParseHeader(source, SketchMagic, FormatVersionMajor, resolveChecksum, "sketch segment");

        int p = HeaderSize;
        int symbolCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int symbolsPerBlock = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int symbolWidth = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int blockAlignment = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(symbolCount < 0 || symbolsPerBlock <= 0 || symbolWidth <= 0 || blockAlignment <= 0)
        {
            throw new InvalidDataException("The sketch segment declares a negative symbol count or a non-positive block geometry.");
        }

        long symbolBytesLength = (long)symbolCount * symbolWidth;
        if(symbolBytesLength > source.Length)
        {
            throw new InvalidDataException("The sketch segment declares more symbol bytes than the image holds.");
        }

        int blockCount = (int)(((long)symbolCount + symbolsPerBlock - 1) / symbolsPerBlock);
        long frontMatterEnd = HeaderSize + ScalarSize + (checksum is null ? 0L : (long)blockCount * checksum.ByteWidth);
        long blockBytes = Align((long)symbolsPerBlock * symbolWidth, blockAlignment);
        long firstBlock = Align(frontMatterEnd, blockAlignment);
        long total = firstBlock + ((long)blockCount * blockBytes) + (checksum is null ? 0 : checksum.ByteWidth);
        if(total > source.Length)
        {
            throw new InvalidDataException("The sketch segment is truncated: its declared blocks run past the image.");
        }

        return new SketchLayout(symbolCount, symbolsPerBlock, symbolWidth, blockCount, checksum, HeaderSize + ScalarSize, firstBlock, blockBytes, frontMatterEnd, total);
    }

    /// <summary>Recomputes the front-matter trailer and compares it to the stored digest, reporting the verdict rather than throwing; <see langword="true"/> when the image carries no checksum.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <returns>Whether the front-matter trailer matched its stored digest.</returns>
    private static bool VerifyFrontMatter(ReadOnlySpan<byte> source, in SketchLayout layout)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        return SegmentContainer.VerifyTrailer(source, layout.Checksum, (int)layout.FrontMatterEnd, (int)layout.Total);
    }

    /// <summary>Parses an image's framing and verifies its front-matter trailer, refusing front-matter damage so the block walk that follows can trust the geometry; the throwing counterpart used by the all-or-nothing read path.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The verified layout.</returns>
    /// <exception cref="InvalidDataException">The image is not a sketch segment, is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SketchLayout ParseAndVerifyFrontMatter(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        SketchLayout layout = ParseFraming(source, resolveChecksum);
        if(!VerifyFrontMatter(source, layout))
        {
            throw new InvalidDataException("The sketch segment failed its front-matter checksum (at-rest corruption).");
        }

        return layout;
    }

    /// <summary>Recomputes block <paramref name="block"/>'s checksum and compares it to the stored digest, reporting the verdict rather than throwing; the block's symbol range and byte offset are set regardless of the verdict.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <param name="block">The block index.</param>
    /// <param name="scratch">Scratch at least <see cref="ChecksumAlgorithm.MaximumByteWidth"/> bytes long for the recomputed digest.</param>
    /// <param name="start">The block's first symbol index.</param>
    /// <param name="symbolsInBlock">The number of symbols the block covers.</param>
    /// <param name="blockOffset">The block's byte offset in the image.</param>
    /// <returns>Whether the block's checksum matched; always <see langword="true"/> when the image carries no checksums.</returns>
    private static bool VerifyBlock(ReadOnlySpan<byte> source, in SketchLayout layout, int block, Span<byte> scratch, out int start, out int symbolsInBlock, out long blockOffset)
    {
        start = block * layout.SymbolsPerBlock;
        symbolsInBlock = Math.Min(layout.SymbolsPerBlock, layout.SymbolCount - start);
        blockOffset = layout.FirstBlock + (block * layout.BlockBytes);
        if(layout.Checksum is null)
        {
            return true;
        }

        int byteCount = symbolsInBlock * layout.SymbolWidth;
        ReadOnlySpan<byte> blockSymbols = source.Slice((int)blockOffset, byteCount);
        layout.Checksum.Compute(blockSymbols, scratch[..layout.Checksum.ByteWidth]);

        return scratch[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.ChecksumSectionOffset + (block * layout.Checksum.ByteWidth), layout.Checksum.ByteWidth));
    }

    /// <summary>Reads back the symbol geometry (symbol width and symbols per block) an image was written with, without verifying its checksums or copying its symbols — the cheap geometry peek a verifying loader uses to confirm a sketch matches an expected contract.</summary>
    /// <param name="source">The byte image.</param>
    /// <returns>The stored symbol width and symbols per block.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a sketch segment image or are too short to carry the scalars.</exception>
    /// <exception cref="NotSupportedException">The major version is unsupported, or the host is big-endian.</exception>
    public static (int SymbolWidth, int SymbolsPerBlock) ReadGeometry(ReadOnlySpan<byte> source)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be a sketch segment image.");
        }

        if(!source[..SketchMagic.Length].SequenceEqual(SketchMagic))
        {
            throw new InvalidDataException("The bytes are not a sketch segment image (magic mismatch).");
        }

        byte versionMajor = source[SketchMagic.Length];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Sketch segment format major version {versionMajor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        int symbolsPerBlock = BinaryPrimitives.ReadInt32LittleEndian(source[(HeaderSize + sizeof(int))..]);
        int symbolWidth = BinaryPrimitives.ReadInt32LittleEndian(source[(HeaderSize + (2 * sizeof(int)))..]);

        return (symbolWidth, symbolsPerBlock);
    }

    /// <summary>The byte size of the header, scalars, and per-block checksum section — everything the front-matter trailer covers, before the first aligned block.</summary>
    /// <param name="blockCount">The number of blocks.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for none.</param>
    /// <returns>The front-matter byte size.</returns>
    private static long FrontMatterSize(int blockCount, ChecksumAlgorithm? checksum)
    {
        return HeaderSize + ScalarSize + (checksum is null ? 0L : (long)blockCount * checksum.ByteWidth);
    }

    /// <summary>The number of symbols block <paramref name="block"/> holds — a full <see cref="SymbolsPerBlock"/> except the last block, which holds the remainder.</summary>
    /// <param name="block">The block index.</param>
    /// <returns>The symbol count in the block.</returns>
    private int SymbolsInBlock(int block)
    {
        int start = block * SymbolsPerBlock;

        return Math.Min(SymbolsPerBlock, SymbolCount - start);
    }

    /// <summary>Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.</summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The alignment (positive).</param>
    /// <returns>The aligned value.</returns>
    private static long Align(long value, long alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    /// <summary>The framing-verified geometry of a sketch-segment image: everything needed to locate and checksum-verify every block once the header and front-matter trailer have passed.</summary>
    private readonly struct SketchLayout
    {
        /// <summary>Creates a layout.</summary>
        /// <param name="symbolCount">The number of coded symbols the segment holds.</param>
        /// <param name="symbolsPerBlock">The number of symbols per block.</param>
        /// <param name="symbolWidth">The byte width of one coded symbol.</param>
        /// <param name="blockCount">The number of blocks.</param>
        /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</param>
        /// <param name="checksumSectionOffset">The byte offset of the per-block checksum section.</param>
        /// <param name="firstBlock">The byte offset of the first aligned block.</param>
        /// <param name="blockBytes">The aligned byte stride between block starts.</param>
        /// <param name="frontMatterEnd">The byte offset one past the front matter (header, scalars, and per-block section) the trailer covers.</param>
        /// <param name="total">The total image byte size, whose tail holds the front-matter trailer.</param>
        internal SketchLayout(int symbolCount, int symbolsPerBlock, int symbolWidth, int blockCount, ChecksumAlgorithm? checksum, int checksumSectionOffset, long firstBlock, long blockBytes, long frontMatterEnd, long total)
        {
            SymbolCount = symbolCount;
            SymbolsPerBlock = symbolsPerBlock;
            SymbolWidth = symbolWidth;
            BlockCount = blockCount;
            Checksum = checksum;
            ChecksumSectionOffset = checksumSectionOffset;
            FirstBlock = firstBlock;
            BlockBytes = blockBytes;
            FrontMatterEnd = frontMatterEnd;
            Total = total;
        }

        /// <summary>The number of coded symbols the segment holds.</summary>
        internal int SymbolCount { get; }

        /// <summary>The number of symbols per block.</summary>
        internal int SymbolsPerBlock { get; }

        /// <summary>The byte width of one coded symbol.</summary>
        internal int SymbolWidth { get; }

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
