namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of one <see cref="AntiEntropySession"/> reconciliation: how it ended, surfaced as a
/// first-class return value rather than a bare success flag, so a caller and the trace channel can act on the
/// exact reason. The two completing outcomes (<see cref="Converged"/>, <see cref="AlreadyConsistent"/>) leave the
/// local replica consistent with the peer; the three declines (<see cref="PeerUnavailable"/>,
/// <see cref="PeerSketchRejected"/>, <see cref="IncompletePeel"/>) apply nothing and leave the local index
/// unchanged, so a declined session never half-applies.
/// </summary>
public enum AntiEntropyOutcome
{
    /// <summary>The whole symmetric difference was peeled and applied; the local replica grew to the union of both.</summary>
    Converged,

    /// <summary>The peel was complete and empty — the replicas already agreed — so nothing was applied and the local index was returned as-is.</summary>
    AlreadyConsistent,

    /// <summary>The peer returned no sketch image (an unreachable peer), so there was no source to reconcile against.</summary>
    PeerUnavailable,

    /// <summary>The peer's sketch image failed its verifying load — corrupt bytes or a mismatched geometry — and was refused before any combine, upholding detection-precedes-combine.</summary>
    PeerSketchRejected,

    /// <summary>The decoder could not peel the whole difference within the symbol budget (a partial peel), or the recovered set overflowed the sink; the partial result was not applied.</summary>
    IncompletePeel,
}
