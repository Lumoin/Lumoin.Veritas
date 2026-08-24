using System;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The decode-free verify round over a columnar index image — the detection primitive a scrub
/// walk runs and the fault harness asserts against. It recomputes each column blob's checksum and
/// compares it to the stored digest WITHOUT decoding the column, so a corrupt blob is reported in
/// the round's verdict rather than throwing from a decode kernel (which would mask the integrity
/// result). Structural damage to the framing — a bad magic, version, or out-of-bounds directory —
/// is still refused outright; only per-blob checksum failures are reported, upholding
/// <see cref="PersistenceInvariant.DetectionPrecedesUse"/> at load.
/// </summary>
public sealed partial class ColumnarTripleIndex
{
    /// <summary>Runs one decode-free verify round over <paramref name="image"/>, recomputing and checking every column blob's checksum.</summary>
    /// <param name="image">The columnar index image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The per-blob verdicts for this round.</returns>
    /// <exception cref="InvalidDataException">The image framing is not a valid columnar index (magic, directory bounds, or checksum-section bounds).</exception>
    /// <exception cref="NotSupportedException">The image's major version, required features, or checksum algorithm are unsupported, or the host is big-endian.</exception>
    internal static VerifyRoundReport RunVerifyRound(ReadOnlySpan<byte> image, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(image.Length < MinimumImageSize)
        {
            throw new InvalidDataException("The bytes are too short to be a Veritas columnar index image.");
        }

        ChecksumAlgorithm? checksum = SegmentContainer.ParseHeader(image, ContainerMagic, FormatVersionMajor, resolveChecksum, "columnar index image");

        int p = HeaderSize;

        //Skip the order-set mode, backing, base triple count, and orders-present bitmask; the
        //level0-bounds flag is validated so this round refuses a graph-view image exactly as the
        //reader does, keeping the two paths in agreement on what is admissible.
        p += 1 + 1 + sizeof(int) + 1;
        byte level0BoundsPresent = image[p++];
        if(level0BoundsPresent != 0)
        {
            throw new NotSupportedException("Graph-view index images are not supported by this reader.");
        }

        p += SkipTripleSet(image[p..]);
        p += SkipTripleSet(image[p..]);

        if(image.Length - p < sizeof(int))
        {
            throw new InvalidDataException("The columnar index image is truncated before its directory.");
        }

        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(image[p..]);
        p += sizeof(int);
        if(entryCount < 0 || ((long)entryCount * DirectoryEntrySize) > image.Length - p)
        {
            throw new InvalidDataException("The columnar index directory entry count is beyond the image bounds.");
        }

        int checksumSectionOffset = p + (entryCount * DirectoryEntrySize);
        if(checksum is not null && (checksumSectionOffset + ((long)entryCount * checksum.ByteWidth)) > image.Length)
        {
            throw new InvalidDataException("The columnar index checksum section is beyond the image bounds.");
        }

        BlobVerdict[] verdicts = new BlobVerdict[entryCount];
        Span<byte> checksumScratch = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];

        //The front-matter checksum is recomputed and reported (decode-free) alongside the per-blob
        //verdicts; a truncated trailer is structural and refused, a content mismatch is reported.
        bool frontMatterValid = true;
        if(checksum is not null)
        {
            int frontMatterEnd = checksumSectionOffset + (entryCount * checksum.ByteWidth);
            if((long)frontMatterEnd + checksum.ByteWidth > image.Length)
            {
                throw new InvalidDataException("The columnar index front-matter checksum is beyond the image bounds.");
            }

            frontMatterValid = SegmentContainer.VerifyTrailer(image, checksum, frontMatterEnd, image.Length);
        }

        for(int e = 0; e < entryCount; e++)
        {
            byte orderIndex = image[p++];
            byte level = image[p++];
            byte role = image[p++];
            p++;
            ulong byteOffset = BinaryPrimitives.ReadUInt64LittleEndian(image[p..]);
            p += sizeof(ulong);
            ulong byteLength = BinaryPrimitives.ReadUInt64LittleEndian(image[p..]);
            p += sizeof(ulong);

            if(orderIndex >= Permutations.Length)
            {
                throw new InvalidDataException("The columnar index directory names an out-of-range order.");
            }

            if(role > RoleOffsets || level > 2 || SlotIndex(level, role) >= 5)
            {
                throw new InvalidDataException("The columnar index directory names an invalid column slot.");
            }

            if(byteOffset > (ulong)image.Length || byteLength > (ulong)image.Length - byteOffset)
            {
                throw new InvalidDataException("The columnar index directory names a blob beyond the image bounds.");
            }

            bool valid = true;
            if(checksum is not null)
            {
                ReadOnlySpan<byte> expected = image.Slice(checksumSectionOffset + (e * checksum.ByteWidth), checksum.ByteWidth);
                valid = VerifyBlob(image.Slice((int)byteOffset, (int)byteLength), checksum, expected, checksumScratch);
            }

            verdicts[e] = new BlobVerdict(e, orderIndex, level, role, (long)byteOffset, (long)byteLength, valid);
        }

        return new VerifyRoundReport(checksum?.Id ?? SegmentContainer.ChecksumAlgorithmNone, checksum is not null, checksum is not null, frontMatterValid, verdicts);
    }

    /// <summary>Recomputes a column blob's checksum into <paramref name="scratch"/> and compares it to the stored digest — the single per-blob verify both the decode-free round and the warm reader use, so detection is sourced once.</summary>
    /// <param name="blobBytes">The serialized column blob.</param>
    /// <param name="checksum">The resolved checksum algorithm.</param>
    /// <param name="expectedDigest">The stored digest for this blob, exactly <see cref="ChecksumAlgorithm.ByteWidth"/> bytes.</param>
    /// <param name="scratch">A scratch buffer of at least <see cref="ChecksumAlgorithm.MaximumByteWidth"/> bytes.</param>
    /// <returns><see langword="true"/> when the recomputed checksum matches the stored digest.</returns>
    private static bool VerifyBlob(ReadOnlySpan<byte> blobBytes, ChecksumAlgorithm checksum, ReadOnlySpan<byte> expectedDigest, Span<byte> scratch)
    {
        Span<byte> computed = scratch[..checksum.ByteWidth];
        checksum.Compute(blobBytes, computed);

        return computed.SequenceEqual(expectedDigest);
    }

    /// <summary>Skips a delta triple set written by the serializer and returns the bytes it occupies; validates the count lies within bounds.</summary>
    /// <param name="source">The byte image positioned at the set.</param>
    /// <returns>The bytes the set occupies.</returns>
    /// <exception cref="InvalidDataException">The set is truncated or declares a count beyond the image bounds.</exception>
    private static int SkipTripleSet(ReadOnlySpan<byte> source)
    {
        if(source.Length < sizeof(int))
        {
            throw new InvalidDataException("The columnar index image is truncated within a delta set.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(source);
        if(count < 0 || ((long)count * TripleByteSize) > source.Length - sizeof(int))
        {
            throw new InvalidDataException("The columnar index image declares a delta triple count beyond its bounds.");
        }

        return sizeof(int) + (count * TripleByteSize);
    }
}
