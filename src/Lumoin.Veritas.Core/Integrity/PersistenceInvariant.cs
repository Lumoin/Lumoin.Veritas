namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The named integrity invariants the persistence layer is held to. They are collectively
/// sufficient and intentionally overlapping (not a disjoint partition), so a fault-injection cell
/// that violates one tends to trip a second — the overlap is what gives a mutation pass its
/// discriminating power. Enforcing code cites the invariant it upholds; fault-injection cells cite
/// the invariant they assert.
/// </summary>
/// <remarks>
/// <para>
/// Reachability tracks the build phase. <see cref="DetectionPrecedesUse"/> and
/// <see cref="EpochConsistency"/> are demonstrable on the single-node sidecar read path that exists
/// today; the rest need the sketch, repair ladder, manifest/CURRENT publish, or a peer, and are
/// asserted as those tiers land.
/// </para>
/// </remarks>
internal enum PersistenceInvariant
{
    /// <summary>I1 — no checksum-unverified COLUMN-BLOB bytes reach a consumer under the resolved verify mode. Reachable now: the load-time per-blob checksum refuses an image with a corrupt column blob before its bytes are decoded. The front matter and the persisted delta are not yet under a checksum — a whole-image checksum is a later tier, and in the full design the delta's integrity comes from the journal layer.</summary>
    DetectionPrecedesUse,

    /// <summary>I2 — a corrupt item never enters a sketch or reconciliation stream. Needs the integrity sketch (a later tier).</summary>
    DetectionPrecedesXor,

    /// <summary>I3 — a post-repair set equals the pre-damage set when the repair ladder has capacity, else exactly the enumerated loss of <see cref="LossIsNamed"/>. Needs the repair ladder.</summary>
    RepairIsFaithful,

    /// <summary>I4 — the CURRENT pointer publish is the single commit point; a torn publish leaves the prior CURRENT wholly in force, never surfacing a staged generation. Needs the manifest/CURRENT write path.</summary>
    PublishIsAtomic,

    /// <summary>I5 — no segment is read under a dictionary or algorithm epoch other than the one it was written against. Reachable now: a foreign checksum-algorithm id is refused rather than mis-verified.</summary>
    EpochConsistency,

    /// <summary>I6 — every post-repair state is reachable by ordinary ingest; there are no special "repaired" states. Needs the repair ladder.</summary>
    RepairIsOrdinaryIngest,

    /// <summary>I7 — an unrecoverable item set is reported exactly, and a fenced store never returns damaged data as if intact. Needs the repair ladder and named-loss reporting.</summary>
    LossIsNamed,
}
