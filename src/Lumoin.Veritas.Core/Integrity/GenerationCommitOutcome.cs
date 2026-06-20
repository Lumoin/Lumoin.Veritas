namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The outcome of one <see cref="GenerationCommitCoordinator"/> pass over a repair report: whether a healed
/// generation was published, and when it was not, why.
/// </summary>
public enum GenerationCommitOutcome
{
    /// <summary>A healed generation was staged and atomically published, superseding the damaged one. It may still carry named system-of-record losses (the report's named losses): a co-occurring system-of-record block loss is named, not restored, so a committed heal can rebuild the derived views while the underlying loss remains recorded.</summary>
    Committed,

    /// <summary>The repair produced no re-derived artifact to publish — the held generation was clean, or its only damage was a named system-of-record loss with nothing to re-stage — so no new generation was committed.</summary>
    NothingToCommit,

    /// <summary>The live generation moved on since the repair report was taken (already healed, or otherwise superseded), so the report's findings are stale and nothing was committed.</summary>
    Superseded,

    /// <summary>The repair pass declined, or the live snapshot is degraded, so no healed generation could be published.</summary>
    Refused,
}
