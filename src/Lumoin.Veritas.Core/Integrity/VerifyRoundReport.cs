using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The verdict of one verify round over a columnar index image: which column blobs passed their
/// checksum and which failed. A round is decode-free — it recomputes each blob's checksum and
/// compares it to the stored digest without decoding the column — so a corrupt blob is reported
/// rather than throwing from a decode kernel, letting a scrub walk record every failure in a pass
/// and queue repairs.
/// </summary>
internal readonly struct VerifyRoundReport
{
    /// <summary>Creates a report over the per-blob verdicts and the front-matter verdict.</summary>
    /// <param name="checksumAlgorithmId">The image's checksum-algorithm id (0 = none).</param>
    /// <param name="hasChecksums">Whether the image carried checksums to verify.</param>
    /// <param name="hasFrontMatterChecksum">Whether the image carried a front-matter checksum (covering the header, scalars, delta, directory, and per-blob section).</param>
    /// <param name="frontMatterValid">Whether the front-matter checksum matched; <see langword="true"/> when the image carried none.</param>
    /// <param name="blobs">The per-blob verdicts, in directory order.</param>
    internal VerifyRoundReport(byte checksumAlgorithmId, bool hasChecksums, bool hasFrontMatterChecksum, bool frontMatterValid, IReadOnlyList<BlobVerdict> blobs)
    {
        ChecksumAlgorithmId = checksumAlgorithmId;
        HasChecksums = hasChecksums;
        HasFrontMatterChecksum = hasFrontMatterChecksum;
        FrontMatterValid = frontMatterValid;
        Blobs = blobs;
    }

    /// <summary>The image's checksum-algorithm id; 0 when the image carried no checksums.</summary>
    internal byte ChecksumAlgorithmId { get; }

    /// <summary>Whether the image carried checksums; when <see langword="false"/> the verdicts are unverified.</summary>
    internal bool HasChecksums { get; }

    /// <summary>Whether the image carried a front-matter checksum over everything before the blobs (header, scalars, delta, directory, per-blob section).</summary>
    internal bool HasFrontMatterChecksum { get; }

    /// <summary>Whether the front-matter checksum matched the stored digest; <see langword="true"/> when the image carried none.</summary>
    internal bool FrontMatterValid { get; }

    /// <summary>The per-blob verdicts, in directory order.</summary>
    internal IReadOnlyList<BlobVerdict> Blobs { get; }

    /// <summary>The number of column blobs the round walked.</summary>
    internal int BlobCount => Blobs.Count;

    /// <summary>The number of blobs that failed their checksum.</summary>
    internal int CorruptCount
    {
        get
        {
            int count = 0;
            for(int i = 0; i < Blobs.Count; i++)
            {
                if(!Blobs[i].IsValid)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Whether the image was verified and every blob and the front matter passed.</summary>
    internal bool IsClean => HasChecksums && CorruptCount == 0 && (!HasFrontMatterChecksum || FrontMatterValid);

    /// <summary>Projects this columnar verdict onto the format-neutral <see cref="ArtifactVerifyReport"/> a scrub consumes, keeping each blob's image coordinates and validity and dropping the columnar-specific order, level, and role.</summary>
    /// <returns>The format-neutral report.</returns>
    internal ArtifactVerifyReport ToArtifactReport()
    {
        BlockVerdict[] blocks = new BlockVerdict[Blobs.Count];
        for(int i = 0; i < Blobs.Count; i++)
        {
            BlobVerdict blob = Blobs[i];
            blocks[i] = new BlockVerdict(blob.Index, blob.ByteOffset, blob.ByteLength, blob.IsValid);
        }

        return new ArtifactVerifyReport(ChecksumAlgorithmId, HasChecksums, HasFrontMatterChecksum, FrontMatterValid, blocks);
    }
}

/// <summary>One column blob's verify verdict: where it lives in the image and the index, and whether its checksum matched.</summary>
/// <param name="Index">The blob's position in the directory.</param>
/// <param name="OrderIndex">The permutation index the column belongs to.</param>
/// <param name="Level">The CSR descent level.</param>
/// <param name="Role">The column role (value or offset).</param>
/// <param name="ByteOffset">The blob's byte offset in the image.</param>
/// <param name="ByteLength">The blob's byte length.</param>
/// <param name="IsValid">Whether the recomputed checksum matched the stored digest (always <see langword="true"/> when the image carried no checksums).</param>
internal readonly record struct BlobVerdict(int Index, byte OrderIndex, byte Level, byte Role, long ByteOffset, long ByteLength, bool IsValid);
