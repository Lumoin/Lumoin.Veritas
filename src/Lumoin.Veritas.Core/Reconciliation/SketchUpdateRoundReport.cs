using System.Collections.Generic;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The verdict of one sketch-update round: how many coded symbols were produced under the budget, how many
/// system-of-record items were folded into them, and which item ranges were excluded because their block
/// failed its checksum. The excluded ranges are the feed-face evidence of
/// <c>DetectionPrecedesXor</c> — a corrupt block's items never entered the encoder — and name exactly what a
/// later repair must restore before the sketch describes the whole generation.
/// </summary>
/// <param name="SymbolCount">The number of coded symbols produced — the round's budgeted symbol cap.</param>
/// <param name="ItemsFed">The number of verified items folded into the sketch.</param>
/// <param name="SkippedRanges">The item ranges excluded because their system-of-record block failed its checksum.</param>
/// <param name="WasChecksumGated">Whether the system-of-record carried per-block checksums, so each fed block was actually verified; when <see langword="false"/> nothing could be gated and a clean round means only that nothing was excluded, not that anything was verified.</param>
public readonly record struct SketchUpdateRoundReport(int SymbolCount, int ItemsFed, IReadOnlyList<SkippedItemRange> SkippedRanges, bool WasChecksumGated)
{
    /// <summary>Whether no items were excluded. Read together with <see cref="WasChecksumGated"/>: clean and gated means every fed block was verified; clean and ungated means the system-of-record carried no digests to verify against.</summary>
    public bool IsClean => SkippedRanges.Count == 0;
}
