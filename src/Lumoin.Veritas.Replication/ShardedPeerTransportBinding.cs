using System;
using System.Buffers;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Builds the sharded peer-reconciliation restoring source over the wire client — the host glue a composition
/// root binds into <see cref="SelfHealOptions.ProvideShardedPeerSource"/>. The source declares THIS endpoint's
/// live dictionary epoch: the same-lineage assertion, enforced per fetch by the wire's own epoch check, while
/// the coordinator's source-versus-manifest gate still guards an old-epoch damaged generation. Core's
/// <see langword="long"/> epoch and the wire's <see langword="ulong"/> interconvert by raw bit
/// reinterpretation — the manifest's own convention — pinned at this seam. The composition root that binds the
/// connection factory is inside the repair trust base; see the trust-boundary remark on
/// <see cref="ShardedPeerReconciliationSource"/>.
/// </summary>
public static class ShardedPeerTransportBinding
{
    /// <summary>Builds the sharded source over a connection factory.</summary>
    /// <param name="policy">The shard policy this endpoint drives and declares.</param>
    /// <param name="openConnection">The seam that opens one fresh duplex connection per shard fetch.</param>
    /// <param name="pool">The pool the recover seam and the sessions rent from; the engine's governed pool.</param>
    /// <param name="dictionaryEpoch">This endpoint's live dictionary epoch.</param>
    /// <param name="shardSymbolCap">The per-shard symbol ceiling that bounds a non-terminating decode into an abort.</param>
    /// <param name="interShardPacing">The delay the rung inserts between shard waves; zero for none.</param>
    /// <param name="timeProvider">The clock the rung's pacing and the client's fault-event timestamps read.</param>
    /// <param name="trace">The diagnostics sink declined-fetch fault events are emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id emitted fault events carry.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <param name="decodeDrainWindow">The drain window an exhausted symbol stream grants the session's consumer before an exchange winds down as out of budget, or <see langword="null"/> for the client's default.</param>
    /// <returns>The sharded source ready for the provider seam.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/>, <paramref name="openConnection"/>, <paramref name="pool"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public static ShardedPeerReconciliationSource CreateSource(
        PrefixShardPolicy policy,
        OpenPeerShardConnectionDelegate openConnection,
        MemoryPool<byte> pool,
        ulong dictionaryEpoch,
        int shardSymbolCap,
        TimeSpan interShardPacing,
        TimeProvider timeProvider,
        TraceHandler<ShardDifferenceFaultEvent>? trace = null,
        Guid correlationId = default,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength,
        TimeSpan? decodeDrainWindow = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(openConnection);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ShardDifferenceChannelClient client = new(openConnection, dictionaryEpoch, timeProvider, trace, correlationId, maxFrameLength, decodeDrainWindow);

        return new ShardedPeerReconciliationSource(
            new ShardedPeerRepairRung(policy, timeProvider),
            client.FetchShardDifferenceAsync,
            new RatelessSketchCodec(pool).Recover,
            StructuralReconciliationProjection.Inversion,
            shardSymbolCap,
            interShardPacing,
            unchecked((long)dictionaryEpoch));
    }
}
