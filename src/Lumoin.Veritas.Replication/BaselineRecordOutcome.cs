namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of the two-phase lineage-baseline coordination: the INTENT write that records the
/// claimant and the causality digest before the local durable commit, and the CONFIRM write that fills the
/// dataset StateId and the dictionary epoch after it. Both phases answer on this one ladder, because both are
/// the same obligation at different points in its life.
/// </summary>
/// <remarks>
/// The default value is <see cref="Undecided"/>, so a zero-initialized field reads as definite ignorance and
/// never as a recorded baseline. Recovery is idempotent by byte comparison at BOTH phases: minting a baseline
/// is deterministic given the identity and the present triples, so an identical retry reproduces the digest and
/// lands on <see cref="AlreadyRecorded"/>, and only a genuinely different tuple is a
/// <see cref="ConflictingLineage"/>.
/// </remarks>
public enum BaselineRecordOutcome
{
    /// <summary>Returned when consensus reached no decision within the caller's attempt budget — a missed quorum or a spent budget, including an unreachable plane. It fails open: the open proceeds, the unconfirmed baseline surfaces as a pending status, and the next open re-issues the write idempotently.</summary>
    Undecided = 0,

    /// <summary>Returned when the INTENT write landed: no baseline was recorded and the record now names the claimant axis and the causality digest, with the confirmation fields still absent.</summary>
    Recorded = 1,

    /// <summary>Returned when the CONFIRM write landed: the digest matched the recorded intent, and the dataset StateId and the dictionary epoch were filled together.</summary>
    Confirmed = 2,

    /// <summary>Returned when the write was a byte-identical repeat of what the record already carries — the crash-retry path at either phase — so nothing changed and the recorded baseline stands.</summary>
    AlreadyRecorded = 3,

    /// <summary>Returned when the record already carries a DIFFERENT baseline for the lineage: a second independent intent, a confirm whose digest does not match the intent, or a confirm against already-filled and different fields. A definite adverse answer, so the open is refused loudly rather than resolved silently; the recorded intent is superseded by an explicit operator step, which the refusal names.</summary>
    ConflictingLineage = 4,

    /// <summary>Returned when the calling replica is not a member of the chain's committed membership, so no attempt was made. A settled refusal about membership rather than an unlucky round, and distinct from <see cref="Undecided"/>'s definite ignorance.</summary>
    OutsideConfiguration = 5
}
