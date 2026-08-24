using System;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// What a storage scrub observed at one step: a round lifecycle marker, a per-block verify verdict, or the
/// outcome of a repair it queued. The kind is self-contained — it names the repair outcome (re-derive /
/// re-ingest / named loss) without depending on the repair-source ladder's rung vocabulary — so the trace
/// channel can narrate a scrub round on its own.
/// </summary>
public enum StorageTraceEventKind
{
    /// <summary>A scrub round began — the bracket-start marker a consumer renders the round's start and timing from. Round-level (role code 0); the generation it scrubs is resolved during the round and travels on the verdict events and the completion marker.</summary>
    ScrubRoundBegan,

    /// <summary>A scrub round completed — the bracket-end marker, carrying the scrubbed generation and the round's corrupt-block count.</summary>
    ScrubRoundCompleted,

    /// <summary>A block's at-rest checksum matched; the block is intact.</summary>
    BlockVerified,

    /// <summary>A block's at-rest checksum failed; the block is corrupt at rest.</summary>
    BlockCorrupt,

    /// <summary>An artifact's front-matter trailer failed; its header, scalars, or per-block section is corrupt at rest.</summary>
    FrontMatterCorrupt,

    /// <summary>A corrupt derived artifact (the columnar sidecar or the sketch) was re-derived from the verified system-of-record.</summary>
    Rederived,

    /// <summary>Recovered items were re-applied through the ordinary commit path.</summary>
    Reingested,

    /// <summary>A corrupt system-of-record block could be restored from no source; its lost items are named rather than silently dropped.</summary>
    NamedLoss,

    /// <summary>A healed generation was atomically published, superseding a damaged one; the marker carries the healed generation and the number of derived artifacts republished.</summary>
    GenerationHealed,

    /// <summary>A background scrub round abandoned with a fault (the store could not be read, a commit failed) rather than a clean verdict; a self-heal loop records this and continues to the next round rather than dying silently. Round-level (role code 0, block index -1); no generation, block, or item is scoped.</summary>
    ScrubRoundFailed,

    /// <summary>A scrub round was abandoned by cancellation after it began: the round-terminal marker pairing a <see cref="ScrubRoundBegan"/> whose work was cut short, so a trace consumer never sees a dangling round bracket. Round-level (role code 0, block index -1); emitted before the cancellation propagates to the caller.</summary>
    ScrubRoundAbandoned,

    /// <summary>A peer-source provider faulted while the repair pass resolved its restoring sources: the peer rung runs unsourced and the round continues as a local-only repair — a transport fault never aborts a viable local repair. Carries the data-segment role code; the provider's own exception is not rethrown.</summary>
    PeerSourceUnavailable,

    /// <summary>A sharded multi-block peer reconciliation concluded without recovering. For this kind the fields are outcome-scoped: <see cref="StorageTraceEvent.ByteOffset"/> carries the <see cref="Lumoin.Veritas.Core.Integrity.ShardedRepairOutcome"/> code, <see cref="StorageTraceEvent.BlockIndex"/> the failed shard index (or -1 for a whole-attempt outcome), and <see cref="StorageTraceEvent.ItemCount"/> the shards processed — so a deployment misconfiguration (a shard-policy mismatch) is named as itself on the trace, never as corruption.</summary>
    ShardedRepairRefused,
}

/// <summary>
/// A structured trace event a storage scrub emits on the diagnostics <see cref="TraceHandler{TEvent}"/>
/// channel: a verify verdict for one artifact block, or the outcome of a repair it queued. Scalar-only, so
/// emitting it is allocation-free under the <c>in</c> parameter; the artifact's role travels as its stable
/// <see cref="Lumoin.Veritas.Core.Persistence.Manifest.ManifestFileRole"/> code rather than the role value,
/// which carries a name string.
/// </summary>
/// <param name="SequenceNumber">The monotonic stream sequence number the emitter assigns.</param>
/// <param name="TimestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units, from the caller-injected time provider.</param>
/// <param name="CorrelationId">The scrub round's correlation id, shared by every event of one round.</param>
/// <param name="Kind">What the step observed.</param>
/// <param name="CommitGeneration">The manifest generation the scrub round held while observing this.</param>
/// <param name="RoleCode">The artifact's <see cref="Lumoin.Veritas.Core.Persistence.Manifest.ManifestFileRole"/> code (1 = data segment, 2 = sidecar, 3 = sketch, …).</param>
/// <param name="BlockIndex">The block's index within its artifact, or -1 when the event is whole-artifact (a front-matter or repair outcome).</param>
/// <param name="ByteOffset">The block's payload byte offset in the artifact image, or 0 when not block-scoped.</param>
/// <param name="ByteLength">The block's payload byte length, or 0 when not block-scoped.</param>
/// <param name="ItemCount">The number of items the event concerns — verified, re-ingested, or named as lost — or 0 when not item-scoped.</param>
public readonly record struct StorageTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    StorageTraceEventKind Kind,
    long CommitGeneration,
    int RoleCode,
    int BlockIndex,
    long ByteOffset,
    long ByteLength,
    long ItemCount): ITraceEvent;
