using System;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The sharded peer-reconciliation restoring source a repair pass borrows: the multi-block extension of
/// <see cref="PeerReconciliationSource"/>. Where the single-block source carries one fetched peer sketch,
/// this source carries the rung that drives per-shard add-only sessions and the host-bound seams those
/// sessions and the faithfulness gate need; no peer sketch crosses here, because each shard session fetches
/// its own difference through <see cref="Fetch"/>. Only Core-resident delegate seams cross this boundary,
/// so the core takes no replication or network dependency.
/// </summary>
/// <remarks>
/// The typed shard-policy handshake guards the sessions — the rung refuses a declared policy mismatch
/// content-blind — but the handshake verifies the DECLARED peer policy: a host binding whose
/// <see cref="Fetch"/> echoes the local fingerprint back as the peer's declaration defeats the check, the
/// same trust class as a host that corrupts the difference stream itself. The composition root that binds
/// this source is inside the trust base; that declared-capability limit is recorded here deliberately.
/// </remarks>
/// <param name="Rung">The sharded rung that partitions the survivors and drives the per-shard sessions.</param>
/// <param name="Fetch">The per-shard transport the rung drives; the reconciliation contract is bound host-side into it.</param>
/// <param name="Recover">The host-bound seam the whole-generation faithfulness gate peels the healed-set residual with.</param>
/// <param name="Invert">The inverse of the reconciliation projection — recovers a triple from a recovered item — for the same invertible (structural) domain the local sketch is projected under.</param>
/// <param name="ShardSymbolCap">The per-shard symbol ceiling that bounds a non-terminating decode into an abort.</param>
/// <param name="InterShardPacing">The delay the rung inserts between shard waves; the host's heartbeat, or zero for none.</param>
/// <param name="DictionaryEpoch">The term-dictionary epoch the peer's items are encoded under, supplied by the host that bound the transport. Encoded identifiers are epoch-relative, so the attempt declines when this does not equal the damaged generation's manifest epoch.</param>
public readonly record struct ShardedPeerReconciliationSource(
    ShardedPeerRepairRung Rung,
    FetchPeerShardDifferenceDelegate Fetch,
    SketchReconciliationDelegates.RecoverSketchDifference Recover,
    ReconciliationItemInverseDelegate Invert,
    int ShardSymbolCap,
    TimeSpan InterShardPacing,
    long DictionaryEpoch);
