using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The persisted term dictionary: the <see cref="TermId"/>-to-<see cref="RdfTerm"/> mapping serialized to a
/// self-describing, checksummed segment so a recovered generation can be decoded back to RDF terms. The
/// system-of-record (<see cref="ItemSegment"/>) and the columnar sidecar hold encoded identifiers only, so
/// without this segment a recovered generation is undecodable; the dictionary is the source of term identity
/// and is therefore <b>not re-derivable</b> from those tiers, which makes it a system-of-record-class artifact
/// (protected by its own checksums and the durable redundancy ladder, never by sidecar re-derivation).
/// </summary>
/// <remarks>
/// <para>
/// The image follows the persistence container's framing discipline — a magic-versioned header, a
/// required-feature mask, an explicitly-selected per-block <see cref="ChecksumAlgorithm"/>, a per-block
/// checksum section, and a single front-matter checksum trailer over the header, scalars, block directory, and
/// that section. Unlike <see cref="ItemSegment"/>'s fixed-stride triple records, term records are
/// <b>variable-length</b>, so a block holds a fixed <em>term count</em> with a variable byte length and the
/// segment carries a per-block <c>(byte-length)</c> directory; a block's checksum still names its exact id
/// range <c>[start, start + count)</c>. Each block payload begins with the dictionary <see cref="TermDictionary.Epoch"/>
/// folded under the block's checksum, so a zeroed or coincidentally-valid stale region from a reused store name
/// cannot self-validate against a foreign generation.
/// </para>
/// <para>
/// Blocks are written back-to-back (the segment is loaded whole on warm-start, not paged), so they are not
/// page-aligned the way the system-of-record's blocks are; the staged-then-published commit covers torn writes
/// at the file level. A triple term is encoded <b>inline</b> (its component terms written in full, walked with
/// an explicit stack — never recursion), because the dictionary interns a triple term without interning its
/// components, so a component is not guaranteed to carry its own identifier.
/// </para>
/// </remarks>
public sealed class DictionarySegment
{
    /// <summary>The 8-byte magic identifying a Veritas term-dictionary segment image.</summary>
    private static ReadOnlySpan<byte> SegmentMagic => "VTSDIC01"u8;

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The fixed shared-container header size before this format's scalars; the magic, version, required-feature mask, and checksum-algorithm id are framed by <see cref="SegmentContainer"/>.</summary>
    private const int HeaderSize = SegmentContainer.HeaderSize;

    /// <summary>The scalar block after the header: dictionary epoch (8) + term count (4) + block term count (4).</summary>
    private const int ScalarSize = sizeof(ulong) + sizeof(int) + sizeof(int);

    /// <summary>The per-block epoch prefix folded under each block's checksum for recycle-safety; an 8-byte little-endian copy of the dictionary epoch leading every block.</summary>
    private const int EpochPrefixSize = sizeof(ulong);

    /// <summary>The default number of terms per block; a block is a fixed term count with a variable byte length.</summary>
    public const int DefaultBlockTermCount = 1024;

    /// <summary>The dictionary this segment serializes; its terms are written in identifier order via <see cref="TermDictionary.Resolve(uint)"/>.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>The number of terms per block.</summary>
    private int BlockTermCount { get; }

    /// <summary>The number of terms in the dictionary.</summary>
    private int TermCount { get; }

    /// <summary>The number of blocks: every full block holds <see cref="BlockTermCount"/> terms and the last holds the remainder.</summary>
    private int BlockCount { get; }

    /// <summary>The per-block payload byte length (the epoch prefix plus the block's term records), computed once at construction so the directory and the written blocks agree.</summary>
    private int[] BlockByteLengths { get; }

    /// <summary>Creates a segment over a dictionary.</summary>
    /// <param name="dictionary">The dictionary whose terms are serialized, in identifier order.</param>
    /// <param name="blockTermCount">The number of terms per block; a block boundary is a term boundary so a block's checksum names its exact identifier range.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockTermCount"/> is not positive.</exception>
    public DictionarySegment(TermDictionary dictionary, int blockTermCount = DefaultBlockTermCount)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockTermCount);

        Dictionary = dictionary;
        BlockTermCount = blockTermCount;
        TermCount = dictionary.Count;
        BlockCount = TermCount == 0 ? 0 : ((TermCount + blockTermCount - 1) / blockTermCount);
        BlockByteLengths = ComputeBlockByteLengths();
    }

    /// <summary>The number of bytes <see cref="WriteTo"/> writes under the given checksum selection.</summary>
    /// <param name="checksum">The per-block checksum algorithm whose section and self-trailer size the image, or <see langword="null"/> for none.</param>
    /// <returns>The image byte size.</returns>
    /// <exception cref="InvalidOperationException">The image would exceed the single-image size limit; the dictionary must be split across generations (the growth story).</exception>
    public int ComputeSerializedSize(ChecksumAlgorithm? checksum)
    {
        int checksumWidth = checksum?.ByteWidth ?? 0;
        long directoryBytes = (long)BlockCount * sizeof(int);
        long checksumSectionBytes = (long)BlockCount * checksumWidth;
        long payloadBytes = 0;
        for(int b = 0; b < BlockCount; b++)
        {
            payloadBytes += BlockByteLengths[b];
        }

        long total = HeaderSize + ScalarSize + directoryBytes + checksumSectionBytes + payloadBytes + checksumWidth;
        if(total > Array.MaxLength)
        {
            throw new InvalidOperationException("The dictionary segment exceeds the single-image size limit; split the dictionary across generations.");
        }

        return (int)total;
    }

    /// <summary>Writes this dictionary's self-describing image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same <paramref name="checksum"/>).</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="checksum">The per-block checksum algorithm to stamp and compute, or <see langword="null"/> for no checksums.</param>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public void WriteTo(Span<byte> destination, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        int checksumWidth = checksum?.ByteWidth ?? 0;
        int directoryBytes = BlockCount * sizeof(int);
        int checksumSectionOffset = HeaderSize + ScalarSize + directoryBytes;
        int frontMatterEnd = checksumSectionOffset + (BlockCount * checksumWidth);

        int p = SegmentContainer.WriteHeader(destination, SegmentMagic, FormatVersionMajor, FormatVersionMinor, checksum);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], Dictionary.Epoch);
        p += sizeof(ulong);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], TermCount);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], BlockTermCount);
        p += sizeof(int);

        for(int b = 0; b < BlockCount; b++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], BlockByteLengths[b]);
            p += sizeof(int);
        }

        //The per-block checksum section between the directory and the blocks is backfilled below, one digest per
        //block, so every byte of it is written; the blocks begin at the front-matter end.
        int blockOffset = frontMatterEnd;
        Stack<RdfTerm> work = new();
        for(int b = 0; b < BlockCount; b++)
        {
            int blockStart = blockOffset;
            int q = blockStart;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[q..], Dictionary.Epoch);
            q += EpochPrefixSize;

            int firstId = (b * BlockTermCount) + 1;
            int lastId = Math.Min((b + 1) * BlockTermCount, TermCount);
            for(int id = firstId; id <= lastId; id++)
            {
                q += TermRecordCodec.Write(Dictionary.Resolve((uint)id), destination[q..], work);
            }

            int blockLength = q - blockStart;
            if(blockLength != BlockByteLengths[b])
            {
                throw new InvalidOperationException("A dictionary block's written length diverged from the computed layout.");
            }

            if(checksum is not null)
            {
                checksum.Compute(
                    destination.Slice(blockStart, blockLength),
                    destination.Slice(checksumSectionOffset + (b * checksumWidth), checksumWidth));
            }

            blockOffset += blockLength;
        }

        if(checksum is not null)
        {
            //The front-matter trailer covers the header, scalars, directory, and per-block section — everything
            //the per-block digests do not — and sits at the image tail after the last block.
            int total = blockOffset + checksumWidth;
            SegmentContainer.WriteTrailer(destination, checksum, frontMatterEnd, total);
        }
    }

    /// <summary>Reconstructs a dictionary from an image written by <see cref="WriteTo"/>, verifying the front-matter trailer and every block's checksum before its records are decoded; the term bytes are interned into <paramref name="pool"/> (so identical IRIs and datatypes are shared), and the identifiers are restored exactly by re-adding the terms in stored order.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="pool">The pool the reconstructed terms' bytes are interned into; it must outlive the returned dictionary, which holds views over its memory.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The reconstructed dictionary, carrying the persisted epoch and the exact identifier assignment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The image is not a dictionary segment, is malformed or truncated, carries a foreign per-block epoch, or a block or the front matter fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static TermDictionary ReadFrom(ReadOnlySpan<byte> source, Utf8StringPool pool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        SegmentLayout layout = ParseAndVerifyFrontMatter(source, resolveChecksum);

        TermDictionary dictionary = new(layout.Epoch);
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        Stack<TermRecordCodec.TripleFrame> frames = new();
        int blockOffset = layout.BlocksOffset;
        for(int b = 0; b < layout.BlockCount; b++)
        {
            int blockLength = layout.BlockByteLengths[b];
            ReadOnlySpan<byte> block = source.Slice(blockOffset, blockLength);
            if(layout.Checksum is not null)
            {
                layout.Checksum.Compute(block, scratch[..layout.Checksum.ByteWidth]);
                if(!scratch[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.ChecksumSectionOffset + (b * layout.Checksum.ByteWidth), layout.Checksum.ByteWidth)))
                {
                    throw new InvalidDataException($"Dictionary block {b} failed its checksum (at-rest corruption).");
                }
            }

            int q = 0;
            ulong blockEpoch = BinaryPrimitives.ReadUInt64LittleEndian(block[q..]);
            q += EpochPrefixSize;
            if(blockEpoch != layout.Epoch)
            {
                throw new InvalidDataException($"Dictionary block {b} carries a foreign epoch (a recycled or mismatched generation).");
            }

            int firstId = (b * layout.BlockTermCount) + 1;
            int lastId = Math.Min((b + 1) * layout.BlockTermCount, layout.TermCount);
            for(int id = firstId; id <= lastId; id++)
            {
                RdfTerm term = TermRecordCodec.Read(block[q..], out int consumed, pool, frames);
                q += consumed;
                dictionary.GetOrAdd(term);
            }

            if(q != blockLength)
            {
                throw new InvalidDataException($"Dictionary block {b} has trailing or insufficient bytes after its declared term count.");
            }

            blockOffset += blockLength;
        }

        if(dictionary.Count != layout.TermCount)
        {
            throw new InvalidDataException("The dictionary segment's reconstructed term count does not match its header (a duplicate or malformed term).");
        }

        return dictionary;
    }

    /// <summary>Verifies an image decode-free, reporting each block's at-rest checksum verdict and the front-matter verdict rather than throwing on a per-block or front-matter failure — the same format-neutral scrub seam <see cref="ItemSegment.RunVerifyRound"/> gives, so a scrub walks the term dictionary block by block. Framing damage (a malformed, unsupported, or truncated image) is still refused, since a block geometry that cannot be parsed cannot be walked. The block-epoch prefix folded under each block is part of the block's checksum domain, so a foreign epoch surfaces as a block-checksum failure here rather than a decode-time refusal.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The per-block and front-matter verdicts.</returns>
    /// <exception cref="InvalidDataException">The image is not a dictionary segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    public static ArtifactVerifyReport RunVerifyRound(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        SegmentLayout layout = ParseFraming(source, resolveChecksum);
        bool frontMatterValid = VerifyFrontMatter(source, layout);
        BlockVerdict[] verdicts = new BlockVerdict[layout.BlockCount];
        Span<byte> scratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        int blockOffset = layout.BlocksOffset;
        for(int b = 0; b < layout.BlockCount; b++)
        {
            int blockLength = layout.BlockByteLengths[b];
            bool valid = VerifyBlock(source, layout, b, blockOffset, blockLength, scratch);
            verdicts[b] = new BlockVerdict(b, blockOffset, blockLength, valid);
            blockOffset += blockLength;
        }

        bool hasChecksums = layout.Checksum is not null;

        return new ArtifactVerifyReport(layout.Checksum?.Id ?? SegmentContainer.ChecksumAlgorithmNone, hasChecksums, hasChecksums, frontMatterValid, verdicts);
    }

    /// <summary>Computes each block's payload byte length — the epoch prefix plus the encoded size of the block's terms — walking each term with a reused work stack so no per-term allocation is incurred.</summary>
    /// <returns>The per-block payload byte lengths, in block order.</returns>
    /// <exception cref="InvalidOperationException">A single block exceeds the maximum block size; reduce the block term count.</exception>
    private int[] ComputeBlockByteLengths()
    {
        int[] lengths = new int[BlockCount];
        Stack<RdfTerm> work = new();
        for(int b = 0; b < BlockCount; b++)
        {
            int firstId = (b * BlockTermCount) + 1;
            int lastId = Math.Min((b + 1) * BlockTermCount, TermCount);
            long blockLength = EpochPrefixSize;
            for(int id = firstId; id <= lastId; id++)
            {
                blockLength += TermRecordCodec.ComputeSize(Dictionary.Resolve((uint)id), work);
            }

            if(blockLength > Array.MaxLength)
            {
                throw new InvalidOperationException("A dictionary block exceeds the maximum block size; reduce the block term count.");
            }

            lengths[b] = (int)blockLength;
        }

        return lengths;
    }

    /// <summary>Parses an image's framing — magic, version, feature mask, checksum-algorithm id, scalars, and the per-block directory — refusing a malformed, unsupported, or truncated image, and returns the geometry needed to locate and verify every block. It does NOT verify the front-matter trailer (that is <see cref="VerifyFrontMatter"/>), so a decode-free verify can report the front-matter verdict rather than throw.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The parsed layout.</returns>
    /// <exception cref="InvalidDataException">The image is not a dictionary segment, is malformed, or is truncated.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SegmentLayout ParseFraming(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize + ScalarSize)
        {
            throw new InvalidDataException("The bytes are too short to be a dictionary segment image.");
        }

        ChecksumAlgorithm? checksum = SegmentContainer.ParseHeader(source, SegmentMagic, FormatVersionMajor, resolveChecksum, "dictionary segment");

        int p = HeaderSize;
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);
        int termCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        int blockTermCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(termCount < 0 || blockTermCount <= 0)
        {
            throw new InvalidDataException("The dictionary segment declares a negative term count or a non-positive block geometry.");
        }

        //The ceiling division runs in long so adversarial scalars near int.MaxValue can neither wrap the sum
        //negative nor collapse the block count to zero; the quotient always fits an int because it never
        //exceeds the (validated non-negative) term count. An absurd-but-positive count then fails the
        //truncation guard below as a refused image, never a wrong-clean verdict.
        int blockCount = termCount == 0 ? 0 : (int)((((long)termCount) + blockTermCount - 1) / blockTermCount);
        int checksumWidth = checksum?.ByteWidth ?? 0;
        long directoryBytes = (long)blockCount * sizeof(int);
        long checksumSectionBytes = (long)blockCount * checksumWidth;
        long frontMatterEnd = HeaderSize + ScalarSize + directoryBytes + checksumSectionBytes;
        if(frontMatterEnd + checksumWidth > source.Length)
        {
            throw new InvalidDataException("The dictionary segment is truncated within its front matter.");
        }

        int[] blockByteLengths = new int[blockCount];
        long payloadBytes = 0;
        int directoryOffset = HeaderSize + ScalarSize;
        for(int b = 0; b < blockCount; b++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(source[(directoryOffset + (b * sizeof(int)))..]);
            if(length < EpochPrefixSize)
            {
                throw new InvalidDataException("The dictionary segment declares a block smaller than its epoch prefix.");
            }

            blockByteLengths[b] = length;
            payloadBytes += length;
        }

        int checksumSectionOffset = directoryOffset + (int)directoryBytes;
        long total = frontMatterEnd + payloadBytes + checksumWidth;
        if(total > source.Length)
        {
            throw new InvalidDataException("The dictionary segment is truncated: its declared blocks run past the image.");
        }

        return new SegmentLayout(epoch, termCount, blockTermCount, blockCount, blockByteLengths, checksum, checksumSectionOffset, (int)frontMatterEnd, (int)total);
    }

    /// <summary>Parses an image's framing and verifies its front-matter trailer, refusing front-matter damage so the block walk that follows can trust the geometry; the throwing counterpart used by the all-or-nothing read path.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The verified layout.</returns>
    /// <exception cref="InvalidDataException">The image is not a dictionary segment, is malformed, truncated, or fails its front-matter checksum.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    private static SegmentLayout ParseAndVerifyFrontMatter(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        SegmentLayout layout = ParseFraming(source, resolveChecksum);
        if(!VerifyFrontMatter(source, layout))
        {
            throw new InvalidDataException("The dictionary segment failed its front-matter checksum (at-rest corruption).");
        }

        return layout;
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

        return SegmentContainer.VerifyTrailer(source, layout.Checksum, layout.BlocksOffset, layout.Total);
    }

    /// <summary>Recomputes block <paramref name="block"/>'s checksum — over the epoch prefix and its term records — and compares it to the stored digest, reporting the verdict rather than throwing; always <see langword="true"/> when the image carries no checksums.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="layout">The parsed layout.</param>
    /// <param name="block">The block index.</param>
    /// <param name="blockOffset">The block's byte offset in the image.</param>
    /// <param name="blockLength">The block's payload byte length.</param>
    /// <param name="scratch">Scratch at least <see cref="ChecksumAlgorithm.MaximumByteWidth"/> bytes long for the recomputed digest.</param>
    /// <returns>Whether the block's checksum matched.</returns>
    private static bool VerifyBlock(ReadOnlySpan<byte> source, in SegmentLayout layout, int block, int blockOffset, int blockLength, Span<byte> scratch)
    {
        if(layout.Checksum is null)
        {
            return true;
        }

        ReadOnlySpan<byte> blockBytes = source.Slice(blockOffset, blockLength);
        layout.Checksum.Compute(blockBytes, scratch[..layout.Checksum.ByteWidth]);

        return scratch[..layout.Checksum.ByteWidth].SequenceEqual(source.Slice(layout.ChecksumSectionOffset + (block * layout.Checksum.ByteWidth), layout.Checksum.ByteWidth));
    }

    /// <summary>The framing-verified geometry of a dictionary segment image: everything needed to locate and checksum-verify every block once the header and front-matter trailer have passed.</summary>
    private readonly struct SegmentLayout
    {
        /// <summary>Creates a layout.</summary>
        /// <param name="epoch">The dictionary epoch.</param>
        /// <param name="termCount">The number of terms.</param>
        /// <param name="blockTermCount">The number of terms per block.</param>
        /// <param name="blockCount">The number of blocks.</param>
        /// <param name="blockByteLengths">The per-block payload byte lengths.</param>
        /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</param>
        /// <param name="checksumSectionOffset">The byte offset of the per-block checksum section.</param>
        /// <param name="blocksOffset">The byte offset of the first block (the front-matter end).</param>
        /// <param name="total">The total image byte size, whose tail holds the front-matter trailer.</param>
        internal SegmentLayout(ulong epoch, int termCount, int blockTermCount, int blockCount, int[] blockByteLengths, ChecksumAlgorithm? checksum, int checksumSectionOffset, int blocksOffset, int total)
        {
            Epoch = epoch;
            TermCount = termCount;
            BlockTermCount = blockTermCount;
            BlockCount = blockCount;
            BlockByteLengths = blockByteLengths;
            Checksum = checksum;
            ChecksumSectionOffset = checksumSectionOffset;
            BlocksOffset = blocksOffset;
            Total = total;
        }

        /// <summary>The dictionary epoch the image was written under.</summary>
        internal ulong Epoch { get; }

        /// <summary>The number of terms.</summary>
        internal int TermCount { get; }

        /// <summary>The number of terms per block.</summary>
        internal int BlockTermCount { get; }

        /// <summary>The number of blocks.</summary>
        internal int BlockCount { get; }

        /// <summary>The per-block payload byte lengths.</summary>
        internal int[] BlockByteLengths { get; }

        /// <summary>The per-block checksum algorithm, or <see langword="null"/> when the image carries none.</summary>
        internal ChecksumAlgorithm? Checksum { get; }

        /// <summary>The byte offset of the per-block checksum section.</summary>
        internal int ChecksumSectionOffset { get; }

        /// <summary>The byte offset of the first block (the front-matter end).</summary>
        internal int BlocksOffset { get; }

        /// <summary>The total image byte size, whose tail holds the front-matter trailer.</summary>
        internal int Total { get; }
    }
}
