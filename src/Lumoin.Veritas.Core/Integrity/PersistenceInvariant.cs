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
    /// <summary>I1 — no checksum-unverified bytes reach a consumer under the resolved verify mode. Reachable now: when a checksum algorithm is selected, the load-time per-blob checksum refuses an image with a corrupt column blob before its bytes are decoded, and a front-matter checksum trailer covers the header, scalars, delta, directory, and per-blob section — everything the per-blob digests do not — verified before those bytes are trusted; together they cover the whole image. An image written without a checksum algorithm carries neither and is unverified.</summary>
    DetectionPrecedesUse,

    /// <summary>I2 — a corrupt item never enters a sketch or reconciliation stream. Needs the integrity sketch (a later tier).</summary>
    DetectionPrecedesXor,

    /// <summary>I3 — a post-repair set equals the pre-damage set when the repair ladder has capacity, else exactly the enumerated loss of <see cref="LossIsNamed"/>. Needs the repair ladder.</summary>
    RepairIsFaithful,

    /// <summary>I4 — the CURRENT pointer publish is the single commit point; a torn publish leaves the prior CURRENT wholly in force, never surfacing a staged generation. Atomicity is unconditional, but when an acknowledged publish becomes durable is conditional on the post-rename directory barrier's platform reach: on Linux and the Apple platforms the barrier puts the live pointer on stable storage before the acknowledgement, whereas on Windows no public directory-fsync API exists, so the barrier is a no-op, the acknowledgement can precede the rename's durability, and a power loss shortly after can revert to the prior committed generation — left wholly intact. Needs the manifest/CURRENT write path.</summary>
    PublishIsAtomic,

    /// <summary>I5 — no segment is read under a dictionary or algorithm epoch other than the one it was written against. Reachable now: a foreign checksum-algorithm id is refused rather than mis-verified.</summary>
    EpochConsistency,

    /// <summary>I6 — every post-repair state is reachable by ordinary ingest; there are no special "repaired" states. Needs the repair ladder.</summary>
    RepairIsOrdinaryIngest,

    /// <summary>I7 — an unrecoverable item set is reported exactly, and a fenced store never returns damaged data as if intact. Needs the repair ladder and named-loss reporting.</summary>
    LossIsNamed,
}
