using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Why a repair pass declined to act on a held generation rather than repairing it. A repair must not
/// re-derive or re-publish atop a snapshot it cannot prove was atomically committed, nor act on findings taken
/// against a different generation than the one it recovered, nor proceed when the system-of-record it must
/// re-derive from cannot be read at all.
/// </summary>
public enum RepairRefusalReason
{
    /// <summary>The pass acted (or had nothing to act on); it was not refused.</summary>
    None,

    /// <summary>The recovered manifest came from the degraded scan — a possible torn-publish orphan — so re-deriving atop it is unsafe.</summary>
    DegradedSnapshot,

    /// <summary>The recovered generation differs from the one the verify report was taken against, so the findings are stale.</summary>
    StaleFindings,

    /// <summary>The system-of-record image is missing or framing-damaged, so no derived artifact can be re-derived from it and no item range can be named.</summary>
    SystemOfRecordUnreadable,
}

/// <summary>
/// The verdict of one repair pass over a verify report's corrupt blocks: the derived artifacts it regenerated
/// from the verified system-of-record (for the caller to stage and publish), the item losses it named when no
/// restoring rung applied, and — when it declined entirely — why. The pass is a generation-agnostic producer:
/// it commits nothing, so a clean outcome means the corruption is fully recoverable (every damaged derived
/// artifact re-derived and nothing named lost), not that anything was published.
/// </summary>
public sealed class RepairPassReport
{
    /// <summary>Creates a repair-pass report.</summary>
    /// <param name="commitGeneration">The manifest commit generation the pass acted on.</param>
    /// <param name="refusal">Why the pass declined, or <see cref="RepairRefusalReason.None"/> when it acted.</param>
    /// <param name="rederivedArtifacts">The derived artifacts regenerated from the verified system-of-record.</param>
    /// <param name="namedLosses">The item losses named when no restoring rung applied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rederivedArtifacts"/> or <paramref name="namedLosses"/> is <see langword="null"/>.</exception>
    public RepairPassReport(long commitGeneration, RepairRefusalReason refusal, IReadOnlyList<RederivedArtifact> rederivedArtifacts, IReadOnlyList<UnrecoverableItemReport> namedLosses)
    {
        ArgumentNullException.ThrowIfNull(rederivedArtifacts);
        ArgumentNullException.ThrowIfNull(namedLosses);

        CommitGeneration = commitGeneration;
        Refusal = refusal;
        RederivedArtifacts = rederivedArtifacts;
        NamedLosses = namedLosses;
    }

    /// <summary>The manifest commit generation the pass acted on.</summary>
    public long CommitGeneration { get; }

    /// <summary>Why the pass declined to act, or <see cref="RepairRefusalReason.None"/> when it acted.</summary>
    public RepairRefusalReason Refusal { get; }

    /// <summary>Whether the pass declined to act on the held generation.</summary>
    public bool Refused => Refusal != RepairRefusalReason.None;

    /// <summary>The derived artifacts regenerated from the verified system-of-record, each for the caller to stage and name in a new generation; empty when none were damaged.</summary>
    public IReadOnlyList<RederivedArtifact> RederivedArtifacts { get; }

    /// <summary>The item losses named when no restoring rung applied (a corrupt system-of-record block); empty when nothing was lost.</summary>
    public IReadOnlyList<UnrecoverableItemReport> NamedLosses { get; }

    /// <summary>Whether the corruption is fully recoverable: the pass acted, every damaged derived artifact re-derived, and nothing was named lost.</summary>
    public bool IsClean => !Refused && NamedLosses.Count == 0;
}
