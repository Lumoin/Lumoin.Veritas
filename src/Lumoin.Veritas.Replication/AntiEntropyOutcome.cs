namespace Lumoin.Veritas.Replication;

/// <summary>
/// The value-based outcome of one <see cref="AntiEntropySession"/> reconciliation: how it ended, surfaced as a
/// first-class return value rather than a bare success flag, so a caller and the trace channel can act on the
/// exact reason. The two completing outcomes (<see cref="Converged"/>, <see cref="AlreadyConsistent"/>) leave the
/// local replica consistent with the peer; the declines (<see cref="PeerUnavailable"/>,
/// <see cref="PeerSketchRejected"/>, <see cref="IncompletePeel"/>, <see cref="PeerTriplesIncomplete"/>,
/// <see cref="PeerContractMismatch"/>, <see cref="PeerEpochMismatch"/>, <see cref="FalseDecodeBoundExceeded"/>)
/// apply nothing and leave the local index unchanged, so a declined session never half-applies. That
/// never-half-applies guarantee is scoped to these ONE-SHOT lanes, whose apply happens only after the whole
/// exchange completes; the dotted (remove-aware) lane commits atomic, causally self-consistent progress DURING
/// its session by design, and its interruption posture is the durable-prefix contract on
/// <see cref="DottedReconcileOutcomeKind.Interrupted"/>.
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

    /// <summary>A content-hash reconcile recovered the difference but the peer did not return a triple for every peer-only item (it may have mutated between serving its sketch and answering the by-key fetch, or returned a triple that does not hash to the requested key); nothing was applied, so a later round retries. Only the content-hash session produces this — the structural session inverts every recovered key locally and never fetches.</summary>
    PeerTriplesIncomplete,

    /// <summary>The peer's stamped reconciliation domain does not match the session's contract — a structural session reached a content-hash peer or the reverse, or a content-hash peer advertised a non-zero dictionary epoch its epoch-independent domain reserves at zero. Combining would invert or re-hash bytes shaped for a different domain, so the session refuses at the wire stamp before any combine and nothing was applied.</summary>
    PeerContractMismatch,

    /// <summary>Structural only: the peer's stamped dictionary epoch differs from the local one, so its term identifiers number a different dictionary. Combining would mis-relate encoded identifiers, so the session refuses before any XOR and nothing was applied.</summary>
    PeerEpochMismatch,

    /// <summary>The peel claimed completeness but the decoder's false-decode probability bound exceeded the policy ceiling (<see cref="ReplicationPolicy.MaxFalseDecodeProbability"/>): the evidence a masquerading symbol did not forge the peel is insufficient to act on, so nothing was applied — even an implausibly-bounded "already consistent" claim is refused rather than laundered.</summary>
    FalseDecodeBoundExceeded,
}
