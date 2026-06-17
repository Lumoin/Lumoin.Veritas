using System;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
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

    /// <summary>The required-feature bit set when a front-matter checksum trailer is present; an older reader refuses an image whose mask carries an unknown bit.</summary>
    private const ulong FrontMatterChecksumFeature = 1UL << 0;

    /// <summary>The required features this build understands; an image whose mask sets any other bit is refused.</summary>
    private const ulong KnownRequiredFeatures = FrontMatterChecksumFeature;

    /// <summary>The checksum-algorithm id written when no checksum is selected.</summary>
    private const byte ChecksumAlgorithmNone = 0;

    /// <summary>The fixed header size: magic (8) + major (1) + minor (1) + required-feature mask (8) + checksum-algorithm id (1).</summary>
    private const int HeaderSize = 8 + 1 + 1 + 8 + 1;

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
        int p = 0;
        SketchMagic.CopyTo(destination[p..]);
        p += SketchMagic.Length;
        destination[p++] = FormatVersionMajor;
        destination[p++] = FormatVersionMinor;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], checksum is null ? 0UL : FrontMatterChecksumFeature);
        p += sizeof(ulong);
        destination[p++] = checksum?.Id ?? ChecksumAlgorithmNone;

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
            checksum.Compute(destination[..frontMatterEnd], destination.Slice((int)(total - checksum.ByteWidth), checksum.ByteWidth));
        }
    }

    /// <summary>Reconstructs the flat coded-symbol bytes from an image written by <see cref="WriteTo"/>, verifying the front-matter trailer and every block's checksum before its bytes are copied out.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The flat coded-symbol bytes, in stored order.</returns>
    /// <exception cref="InvalidDataException">The image is not a sketch segment, is malformed, or a block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static byte[] ReadFrom(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be a sketch segment image.");
        }

        int p = 0;
        if(!source[..SketchMagic.Length].SequenceEqual(SketchMagic))
        {
            throw new InvalidDataException("The bytes are not a sketch segment image (magic mismatch).");
        }

        p += SketchMagic.Length;
        byte versionMajor = source[p++];
        byte versionMinor = source[p++];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Sketch segment format version {versionMajor}.{versionMinor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        ulong featureFlags = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);
        if((featureFlags & ~KnownRequiredFeatures) != 0)
        {
            throw new NotSupportedException("The sketch segment requires a feature this build does not understand.");
        }

        byte checksumAlgorithmId = source[p++];
        ChecksumAlgorithm? checksum = null;
        if(checksumAlgorithmId != ChecksumAlgorithmNone)
        {
            checksum = (resolveChecksum ?? ChecksumAlgorithm.DefaultResolver)(checksumAlgorithmId)
                ?? throw new NotSupportedException($"The sketch segment uses checksum algorithm id {checksumAlgorithmId}, which this reader does not resolve.");
        }

        bool hasFrontMatterChecksum = (featureFlags & FrontMatterChecksumFeature) != 0;
        if(hasFrontMatterChecksum != (checksum is not null))
        {
            throw new InvalidDataException("The sketch segment's front-matter-checksum feature flag disagrees with its checksum-algorithm id.");
        }

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

        Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        if(checksum is not null)
        {
            checksum.Compute(source[..(int)frontMatterEnd], computed[..checksum.ByteWidth]);
            if(!computed[..checksum.ByteWidth].SequenceEqual(source.Slice((int)(total - checksum.ByteWidth), checksum.ByteWidth)))
            {
                throw new InvalidDataException("The sketch segment failed its front-matter checksum (at-rest corruption).");
            }
        }

        byte[] symbols = new byte[(int)symbolBytesLength];
        int checksumSectionOffset = HeaderSize + ScalarSize;
        for(int block = 0; block < blockCount; block++)
        {
            long blockOffset = firstBlock + (block * blockBytes);
            int start = block * symbolsPerBlock;
            int symbolsInBlock = Math.Min(symbolsPerBlock, symbolCount - start);
            int byteCount = symbolsInBlock * symbolWidth;
            ReadOnlySpan<byte> blockSymbols = source.Slice((int)blockOffset, byteCount);

            if(checksum is not null)
            {
                checksum.Compute(blockSymbols, computed[..checksum.ByteWidth]);
                if(!computed[..checksum.ByteWidth].SequenceEqual(source.Slice(checksumSectionOffset + (block * checksum.ByteWidth), checksum.ByteWidth)))
                {
                    throw new InvalidDataException($"Sketch block {block} (symbols [{start}, {start + symbolsInBlock})) failed its checksum (at-rest corruption).");
                }
            }

            blockSymbols.CopyTo(symbols.AsSpan(start * symbolWidth, byteCount));
        }

        return symbols;
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
}
