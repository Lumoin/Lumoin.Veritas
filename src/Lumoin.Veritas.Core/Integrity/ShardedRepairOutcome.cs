namespace Lumoin.Veritas.Core.Integrity;

/// <summary>How a sharded multi-block repair attempt concluded.</summary>
public enum ShardedRepairOutcome
{
    /// <summary>Every shard completed, direction resolved, and the healed set matched the generation sketch.</summary>
    Recovered = 0,

    /// <summary>A shard's decoder did not complete within its symbol cap; the attempt is abandoned, nothing re-ingested.</summary>
    IncompleteShard = 1,

    /// <summary>A recovered item did not hash into the shard that recovered it; a corrupted or adversarial stream, rejected.</summary>
    DirectionGuardRejected = 2,

    /// <summary>The healed set did not peel empty against the generation's own sketch; the attempt is rejected.</summary>
    FaithfulnessRejected = 3,

    /// <summary>A shard's peer declared a different shard policy than the one driving this attempt. Difference-stream cancellation is undefined across mismatched policies, so nothing from that session is consumed and the attempt is refused whole — a deployment misconfiguration named as itself, not as corruption.</summary>
    PolicyMismatch = 4,

    /// <summary>The composed recovered set's count does not equal the lost ranges' total item count — the multi-block generalization of the single-block rung's per-block count gate. A coordinator-diagnosed refinement: the rung itself reports the verify-gate failure, and the coordinator's diagnostic names this precise cause.</summary>
    CountMismatch = 5,

    /// <summary>A recovered item's byte width is not the reconciliation key width, so reading it as a key would silently misread content — the corrupted-or-adversarial-stream class. A coordinator-diagnosed refinement: the rung itself reports the verify-gate failure, and the coordinator's diagnostic names this precise cause.</summary>
    MalformedItem = 6,

    /// <summary>A shard's result carries no peer policy declaration at all — the transport faulted before the peer ever declared (a refused connection, a fault before or during the header exchange). Refused ahead of the fingerprint comparison, so a transport blip is never diagnosed as a policy mismatch: <see cref="PolicyMismatch"/> means a real foreign declaration was compared. Nothing from the session is consumed and the attempt is abandoned.</summary>
    PeerUndeclared = 7,
}
