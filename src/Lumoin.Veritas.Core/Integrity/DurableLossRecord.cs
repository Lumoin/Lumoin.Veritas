using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The durable record of the unrecoverable losses a repair named when it published a healed generation: the
/// generation it belongs to and each named loss, self-checksummed and co-versioned with the generation so a
/// generation healed with losses stays visibly lossy after a restart rather than looking pristine. A repair
/// publish writes one only when its repair named at least one loss; a loss-free heal writes none. The record is
/// named in the manifest under <see cref="Lumoin.Veritas.Core.Persistence.Manifest.ManifestFileRole.Losses"/>,
/// so the generation binds its integrity by the same whole-image digest it binds every other artifact by, and a
/// reader that does not recognise the role simply skips it.
/// </summary>
/// <remarks>
/// <para>
/// The image reuses the persistence container's framing discipline (<see cref="SegmentContainer"/>): a
/// magic-versioned header with a required-feature mask and an explicitly-selected checksum, the generation and
/// loss scalars, the per-loss records, and a single front-matter checksum trailer over all of it. The whole
/// record is front matter — it has no separately-checksummed payload blocks — so the trailer covers every byte
/// before it and one verify attests the entire record. An older reader that does not know the format refuses a
/// magic or version mismatch; the manifest's whole-image digest is the generation-level attestation the scrub
/// verify pass applies.
/// </para>
/// </remarks>
public sealed class DurableLossRecord
{
    /// <summary>The 8-byte magic identifying a Veritas durable loss-record image.</summary>
    private static ReadOnlySpan<byte> LossRecordMagic => "VTSLOSS1"u8;

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The scalar block after the header: commit generation (8) + loss count (4).</summary>
    private const int ScalarSize = sizeof(long) + sizeof(int);

    /// <summary>The fixed per-loss prefix before the variable-length artifact name: kind (4) + role code (4) + start item (8) + item count (8) + name length (4).</summary>
    private const int LossPrefixSize = sizeof(int) + sizeof(int) + sizeof(long) + sizeof(long) + sizeof(int);

    /// <summary>The name-length sentinel written for a loss whose artifact name is <see langword="null"/> (the default graph's segment), distinguished from a present empty name.</summary>
    private const int NullNameLength = -1;

    /// <summary>Creates a loss record.</summary>
    /// <param name="generation">The commit generation the losses belong to.</param>
    /// <param name="losses">The named losses this record persists.</param>
    private DurableLossRecord(long generation, IReadOnlyList<DurableLossEntry> losses)
    {
        Generation = generation;
        Losses = losses;
    }

    /// <summary>The commit generation the recorded losses belong to.</summary>
    public long Generation { get; }

    /// <summary>The named losses this record persists; never empty in a written record (a loss-free heal writes no record).</summary>
    public IReadOnlyList<DurableLossEntry> Losses { get; }

    /// <summary>The number of bytes <see cref="WriteTo"/> writes for the given losses under the given checksum.</summary>
    /// <param name="losses">The named losses to size.</param>
    /// <param name="checksum">The checksum algorithm whose front-matter trailer sizes the image.</param>
    /// <returns>The image byte size.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="losses"/> or <paramref name="checksum"/> is <see langword="null"/>.</exception>
    public static int ComputeSerializedSize(IReadOnlyList<UnrecoverableItemReport> losses, ChecksumAlgorithm checksum)
    {
        ArgumentNullException.ThrowIfNull(losses);
        ArgumentNullException.ThrowIfNull(checksum);

        int total = SegmentContainer.HeaderSize + ScalarSize;
        for(int i = 0; i < losses.Count; i++)
        {
            string? name = losses[i].ArtifactFileName;
            total += LossPrefixSize + (name is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(name));
        }

        return total + checksum.ByteWidth;
    }

    /// <summary>Writes a self-describing, self-checksummed loss-record image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same losses and checksum).</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="generation">The commit generation the losses belong to.</param>
    /// <param name="losses">The named losses to persist.</param>
    /// <param name="checksum">The checksum algorithm the front-matter trailer is computed under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="losses"/> or <paramref name="checksum"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public static void WriteTo(Span<byte> destination, long generation, IReadOnlyList<UnrecoverableItemReport> losses, ChecksumAlgorithm checksum)
    {
        ArgumentNullException.ThrowIfNull(losses);
        ArgumentNullException.ThrowIfNull(checksum);
        LittleEndianBuffer.EnsureLittleEndian();

        int p = SegmentContainer.WriteHeader(destination, LossRecordMagic, FormatVersionMajor, FormatVersionMinor, checksum);
        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], generation);
        p += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], losses.Count);
        p += sizeof(int);

        for(int i = 0; i < losses.Count; i++)
        {
            UnrecoverableItemReport loss = losses[i];
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], (int)loss.Kind);
            p += sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], loss.RoleCode);
            p += sizeof(int);
            BinaryPrimitives.WriteInt64LittleEndian(destination[p..], loss.LostItemStart);
            p += sizeof(long);
            BinaryPrimitives.WriteInt64LittleEndian(destination[p..], loss.LostItemCount);
            p += sizeof(long);

            if(loss.ArtifactFileName is null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination[p..], NullNameLength);
                p += sizeof(int);
            }
            else
            {
                int nameBytes = System.Text.Encoding.UTF8.GetBytes(loss.ArtifactFileName, destination[(p + sizeof(int))..]);
                BinaryPrimitives.WriteInt32LittleEndian(destination[p..], nameBytes);
                p += sizeof(int) + nameBytes;
            }
        }

        //The whole record is front matter; the trailer covers every byte written so far and sits at the image tail.
        SegmentContainer.WriteTrailer(destination, checksum, p, p + checksum.ByteWidth);
    }

    /// <summary>Reconstructs a loss record from an image written by <see cref="WriteTo"/>, returning <see langword="null"/> when the bytes are not a loss record, are malformed, or fail their self-checksum (at-rest rot) — detection precedes use, and a caller learns the recorded losses only from an intact record.</summary>
    /// <param name="source">The image bytes.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The reconstructed record, or <see langword="null"/> when the image is not an intact loss record.</returns>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian — a reader incompatibility, not at-rest rot, so it propagates.</exception>
    public static DurableLossRecord? TryRead(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        ChecksumAlgorithm? checksum;
        try
        {
            checksum = SegmentContainer.ParseHeader(source, LossRecordMagic, FormatVersionMajor, resolveChecksum, "loss record");
        }
        catch(InvalidDataException)
        {
            return null;
        }

        //A loss record is always self-checksummed; an image carrying no checksum is not a valid one.
        if(checksum is null || source.Length < SegmentContainer.HeaderSize + ScalarSize + checksum.ByteWidth)
        {
            return null;
        }

        int total = source.Length;
        int frontMatterEnd = total - checksum.ByteWidth;
        if(!SegmentContainer.VerifyTrailer(source, checksum, frontMatterEnd, total))
        {
            return null;
        }

        int p = SegmentContainer.HeaderSize;
        long generation = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        int lossCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        //The declared count is bounded against the bytes that could actually carry entries BEFORE anything is
        //allocated on it, in long arithmetic: every loss occupies at least its fixed prefix, so a count the
        //remaining front matter cannot hold is a malformed image refused as null — never a count-sized
        //allocation. The checksum trailer is a rot detector, not an authenticator, so the count is untrusted
        //even under a verifying trailer.
        if(generation < 0 || lossCount < 0 || (long)lossCount * LossPrefixSize > frontMatterEnd - p)
        {
            return null;
        }

        DurableLossEntry[] losses = new DurableLossEntry[lossCount];
        for(int i = 0; i < lossCount; i++)
        {
            if(frontMatterEnd - p < LossPrefixSize)
            {
                return null;
            }

            int kindCode = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
            p += sizeof(int);
            int roleCode = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
            p += sizeof(int);
            long startItem = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
            p += sizeof(long);
            long itemCount = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
            p += sizeof(long);
            int nameLength = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
            p += sizeof(int);

            if(!Enum.IsDefined((UnrecoverableItemReportKind)kindCode) || nameLength < NullNameLength)
            {
                return null;
            }

            string? name = null;
            if(nameLength != NullNameLength)
            {
                if(nameLength > frontMatterEnd - p)
                {
                    return null;
                }

                name = System.Text.Encoding.UTF8.GetString(source.Slice(p, nameLength));
                p += nameLength;
            }

            losses[i] = new DurableLossEntry((UnrecoverableItemReportKind)kindCode, roleCode, name, startItem, itemCount);
        }

        return new DurableLossRecord(generation, losses);
    }
}
