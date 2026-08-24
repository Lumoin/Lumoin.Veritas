namespace Lumoin.Veritas.Replication;

/// <summary>
/// How one dotted (remove-aware) reconcile exchange ended, surfaced as a first-class value so the operator and
/// the trace act on the exact reason — the dotted lane's sibling of the add-only outcome vocabulary. The two
/// completing kinds leave the exchanged difference fully applied; every refusal names its cause; and
/// <see cref="Interrupted"/> is the durable-prefix posture made visible: the dotted session commits atomic,
/// causally self-consistent progress DURING the session, so an interrupted exchange leaves a consistent prefix
/// — never a torn state — and re-running the session converges idempotently.
/// </summary>
public enum DottedReconcileOutcomeKind
{
    /// <summary>The session completed and converged: the whole dotted symmetric difference was exchanged and applied.</summary>
    Converged,

    /// <summary>The session completed with an empty difference: the replicas' dotted sets already agreed and nothing moved.</summary>
    AlreadyConsistent,

    /// <summary>The LOCAL store refused before dialing: it is not remove-aware (no host replica identity, or awaiting the explicit baseline step). The add-only lanes remain available as an operator-explicit choice.</summary>
    LocalNotRemoveAware,

    /// <summary>The LOCAL store refused before dialing: it is remove-aware but keeps no durable dataset journal, so a crash could lose minted dots peers already cover — the dotted wire exchanges only crash-durable causal history.</summary>
    LocalNotDurable,

    /// <summary>The peer could not be reached, or died without answering: an absent reply is never inferred as unsupported.</summary>
    PeerUnavailable,

    /// <summary>The peer answered the EXPLICIT unknown-service refusal byte: its executable does not serve the dotted-difference selector.</summary>
    PeerRemoveAwareUnsupported,

    /// <summary>The peer's reply header declined the exchange; the outcome's decline reason carries the peer's named cause.</summary>
    PeerDeclined,

    /// <summary>The peer presented causal coverage or a dot beyond the local identity axis's own maximum — a second minter under this replica's identity; refused before applying the colliding knowledge. The remedy is operator-level: re-establish replica-identity distinctness, then re-clone the corrupted side.</summary>
    IdentityCollision,

    /// <summary>An adopt write-back exhausted its commit retries against a concurrently-advancing journal head; committed prefix commits stand and a re-run converges.</summary>
    ConflictExhausted,

    /// <summary>The peer violated the channel or session protocol, or a frame was malformed; the fault's class rides the trace. Nothing beyond the already-committed prefix was applied.</summary>
    ProtocolFault,

    /// <summary>The exchange ended before completion — a torn transport, a wind-down, a tripped cap. Consistent committed progress may exist; nothing is torn; re-run to converge.</summary>
    Interrupted,
}
