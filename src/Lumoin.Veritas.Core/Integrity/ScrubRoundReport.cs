using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// One block (or whole-artifact framing failure) a scrub found corrupt at rest, in the format-neutral terms a
/// repair needs: which manifest artifact it lives in, where in the image, and whether the failure was the
/// artifact's front matter (header/directory) rather than a single block. A scrub records these rather than
/// throwing, so one pass surfaces every failure for the repair ladder to act on.
/// </summary>
/// <param name="RoleCode">The artifact's <see cref="Lumoin.Veritas.Core.Persistence.Manifest.ManifestFileRole"/> code (1 = data segment, 2 = sidecar, 3 = sketch, …).</param>
/// <param name="FileName">The artifact's file name within the store.</param>
/// <param name="BlockIndex">The corrupt block's index, or -1 when the failure is the artifact's front matter or the whole image is unreadable.</param>
/// <param name="ByteOffset">The corrupt block's payload byte offset, or 0 when not block-scoped.</param>
/// <param name="ByteLength">The corrupt block's payload byte length, or 0 when not block-scoped.</param>
/// <param name="IsFrontMatter"><see langword="true"/> when the failure is the artifact's front-matter trailer or an unreadable framing, rather than a single per-block checksum.</param>
public readonly record struct ScrubBlockFinding(int RoleCode, string FileName, int BlockIndex, long ByteOffset, long ByteLength, bool IsFrontMatter);

/// <summary>
/// The verdict of one scrub pass over a held manifest generation: how many artifact blocks were verified
/// intact and which were found corrupt at rest. A scrub holds a fixed manifest snapshot, walks the artifacts
/// it names through the format-neutral verify seam, and records every failure here without throwing, so the
/// repair-source ladder can act on the whole set and a clean pass is distinguishable from one that could not
/// be gated.
/// </summary>
public sealed class ScrubRoundReport
{
    /// <summary>Creates a report over a scrubbed generation.</summary>
    /// <param name="commitGeneration">The manifest commit generation the scrub held.</param>
    /// <param name="isDegradedSnapshot">Whether the held manifest came from the degraded recovery scan (a possible torn-publish orphan) rather than a CURRENT pointer.</param>
    /// <param name="blocksVerified">The number of blocks that passed their at-rest checksum.</param>
    /// <param name="corruptBlocks">The blocks (and whole-artifact framing failures) found corrupt.</param>
    public ScrubRoundReport(long commitGeneration, bool isDegradedSnapshot, int blocksVerified, IReadOnlyList<ScrubBlockFinding> corruptBlocks)
    {
        ArgumentNullException.ThrowIfNull(corruptBlocks);

        CommitGeneration = commitGeneration;
        IsDegradedSnapshot = isDegradedSnapshot;
        BlocksVerified = blocksVerified;
        CorruptBlocks = corruptBlocks;
    }

    /// <summary>The manifest commit generation the scrub held while verifying.</summary>
    public long CommitGeneration { get; }

    /// <summary>Whether the held manifest came from the degraded recovery scan (a possible torn-publish orphan) — a caller may choose to detect-only against such a snapshot.</summary>
    public bool IsDegradedSnapshot { get; }

    /// <summary>The number of artifact blocks that passed their at-rest checksum.</summary>
    public int BlocksVerified { get; }

    /// <summary>The blocks (and whole-artifact framing failures) found corrupt at rest, in walk order.</summary>
    public IReadOnlyList<ScrubBlockFinding> CorruptBlocks { get; }

    /// <summary>Whether the pass found nothing corrupt.</summary>
    public bool IsClean => CorruptBlocks.Count == 0;
}
