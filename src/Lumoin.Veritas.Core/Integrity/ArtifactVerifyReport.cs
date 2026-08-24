using System;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The verdict of one decode-free verify pass over a block-framed persistence artifact: which blocks passed
/// their at-rest checksum and which failed, plus the whole-image front-matter verdict. It is the format-neutral
/// seam a scrub consumes — the columnar sidecar, the system-of-record segment, and the integrity sketch each
/// produce one of these, so the scrub walk and the repair-source ladder reason over a single shape rather than
/// three bespoke verify results. A pass is decode-free (it recomputes each block's checksum and compares it to
/// the stored digest without decoding the block's contents), so a corrupt block is reported rather than
/// throwing from a decode kernel, letting one walk record every failure and queue repairs.
/// </summary>
public sealed class ArtifactVerifyReport
{
    /// <summary>Creates a report over the per-block verdicts and the front-matter verdict.</summary>
    /// <param name="checksumAlgorithmId">The image's checksum-algorithm id; 0 when the image carried no checksums.</param>
    /// <param name="hasChecksums">Whether the image carried per-block checksums to verify against.</param>
    /// <param name="hasFrontMatterChecksum">Whether the image carried a front-matter checksum over its header, scalars, and per-block section.</param>
    /// <param name="frontMatterValid">Whether the front-matter checksum matched; <see langword="true"/> when the image carried none.</param>
    /// <param name="blocks">The per-block verdicts, in block order; held as a contiguous span, not copied.</param>
    public ArtifactVerifyReport(byte checksumAlgorithmId, bool hasChecksums, bool hasFrontMatterChecksum, bool frontMatterValid, ReadOnlyMemory<BlockVerdict> blocks)
    {
        ChecksumAlgorithmId = checksumAlgorithmId;
        HasChecksums = hasChecksums;
        HasFrontMatterChecksum = hasFrontMatterChecksum;
        FrontMatterValid = frontMatterValid;
        Blocks = blocks;
    }

    /// <summary>The image's checksum-algorithm id; 0 when the image carried no checksums.</summary>
    public byte ChecksumAlgorithmId { get; }

    /// <summary>Whether the image carried per-block checksums; when <see langword="false"/> the verdicts could not be gated and a clean report means only that nothing was found wrong, not that anything was verified.</summary>
    public bool HasChecksums { get; }

    /// <summary>Whether the image carried a front-matter checksum over everything before its blocks.</summary>
    public bool HasFrontMatterChecksum { get; }

    /// <summary>Whether the front-matter checksum matched the stored digest; <see langword="true"/> when the image carried none.</summary>
    public bool FrontMatterValid { get; }

    /// <summary>The per-block verdicts, in block order; a contiguous value-type span the scrub walk indexes without an interface hop.</summary>
    public ReadOnlyMemory<BlockVerdict> Blocks { get; }

    /// <summary>The number of blocks the verify pass walked.</summary>
    public int BlockCount => Blocks.Length;

    /// <summary>The number of blocks that failed their checksum.</summary>
    public int CorruptCount
    {
        get
        {
            ReadOnlySpan<BlockVerdict> blocks = Blocks.Span;
            int count = 0;
            for(int i = 0; i < blocks.Length; i++)
            {
                if(!blocks[i].IsValid)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Whether the image was verified and every block and the front matter passed.</summary>
    public bool IsClean => HasChecksums && CorruptCount == 0 && (!HasFrontMatterChecksum || FrontMatterValid);
}
