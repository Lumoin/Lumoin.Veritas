using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The multi-block extension of the peer-reconciliation repair rung. Where the single-block rung recovers one
/// lost block against one peer sketch, this rung partitions the whole generation's key space into balanced
/// prefix shards and reconciles each shard as an independent add-only session, then composes the recovered
/// items back through the coordinator's existing re-ingest path. It slots at the single-block rung's
/// <c>skipped.Count != 1</c> gate: a multi-block loss, instead of returning false, descends here.
/// </summary>
/// <remarks>
/// <para>
/// Sharding is orthogonal to block boundaries. The lost blocks' items are, by definition, unknown, so the rung
/// does not try to reconcile block by block; it reconciles the whole local-versus-peer difference, split by key
/// prefix only to keep each session's operand small — well under the session's quadratic copy-validate cost.
/// Sharding shrinks each session's operand; it does NOT lift the partition snapshot's monolithic single-buffer
/// bound, which the whole survivor set is copied into up front (see <see cref="PrefixShardPolicy.Partition"/>).
/// The peer-only half of that difference is exactly the set of lost items, which then fills the lost blocks on
/// re-ingest.
/// </para>
/// <para>
/// Because shard assignment is a pure function of the key, an item present on both replicas is assigned the
/// same shard on both, so its contributions cancel within that shard's difference stream — the property that
/// makes per-shard peeling sound. Both replicas must run a byte-identical <see cref="PrefixShardPolicy"/>,
/// and the rung enforces it through the typed handshake: the driving policy's
/// <see cref="PrefixShardPolicy.Fingerprint"/> crosses the fetch seam outbound, every
/// <see cref="ShardReconcileResult"/> carries the peer's own declaration back, and a mismatch is refused as
/// <see cref="ShardedRepairOutcome.PolicyMismatch"/> before anything from that session is consumed. The rung
/// verifies the DECLARED peer policy — a host binding that echoes the local value instead of carrying the
/// peer's own declaration defeats the check, the same trust class as a host that corrupts the difference
/// stream itself.
/// </para>
/// <para>
/// Sessions here are ADD-ONLY: each shard session is constructed over its pinned snapshot with no local remove
/// context. Add-only is byte-for-byte the pre-remove-aware protocol — cross-compatible with an older peer that
/// does not know the remove-aware completion frame — and recovers exactly the peer-only items a lossy replica is
/// missing, which is the whole of what a repair rung may do.
/// </para>
/// </remarks>
public sealed class ShardedPeerRepairRung
{
    /// <summary>The default in-flight shard window: one, i.e. sequential, the safest default because each concurrent shard needs its own point-to-point channel.</summary>
    public const int DefaultMaxConcurrentShards = 1;

    /// <summary>
    /// Initializes the rung with the shard policy and the pacing clock.
    /// </summary>
    /// <param name="policy">The partitioning policy both replicas share.</param>
    /// <param name="timeProvider">The clock the inter-shard pacing delay reads; the library adds no timers, so pacing is host policy.</param>
    /// <param name="maxConcurrentShards">The in-flight shard window, at least one; raising it above one requires one distinct channel per concurrent shard.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxConcurrentShards"/> is below one.</exception>
    public ShardedPeerRepairRung(PrefixShardPolicy policy, TimeProvider timeProvider, int maxConcurrentShards = DefaultMaxConcurrentShards)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if(maxConcurrentShards < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentShards), maxConcurrentShards, "The in-flight shard window must be at least one.");
        }

        Policy = policy;
        TimeProvider = timeProvider;
        MaxConcurrentShards = maxConcurrentShards;
    }

    /// <summary>The partitioning policy both replicas share.</summary>
    public PrefixShardPolicy Policy { get; }

    /// <summary>The clock the inter-shard pacing delay reads.</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>The in-flight shard window.</summary>
    public int MaxConcurrentShards { get; }

    /// <summary>
    /// Attempts a sharded multi-block recovery. Partitions <paramref name="survivorItems"/>, drives each shard's
    /// add-only reconciliation through <paramref name="fetch"/> in bounded-parallel waves, resolves direction
    /// per shard, composes the peer-only items, and gates the composed set through
    /// <paramref name="verifyHealed"/> before returning it for re-ingest.
    /// </summary>
    /// <param name="survivorItems">The projected keys of the surviving blocks, the local operand.</param>
    /// <param name="keyWidth">The exact key width; the reconciliation item width.</param>
    /// <param name="shardSymbolCap">The per-shard symbol ceiling that bounds a non-terminating decode into an abort.</param>
    /// <param name="verifyHealed">The whole-generation faithfulness gate over the composed recovered set.</param>
    /// <param name="fetch">The per-shard transport that runs one shard's session and returns its decoded difference; the reconciliation contract is bound host-side into this delegate.</param>
    /// <param name="interShardPacing">A delay inserted between shard waves; the host's heartbeat, or zero for none.</param>
    /// <param name="pool">The governed pool the partition and sessions rent from.</param>
    /// <param name="cancellationToken">Cancels the whole attempt.</param>
    /// <returns>The composed outcome and the items to re-ingest.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null"/>.</exception>
    public async ValueTask<ShardedPeerRepairResult> AttemptShardedPeerReconciliationAsync(
        IReadOnlyCollection<ReadOnlyMemory<byte>> survivorItems,
        int keyWidth,
        int shardSymbolCap,
        VerifyHealedSetAgainstGenerationSketchDelegate verifyHealed,
        FetchPeerShardDifferenceDelegate fetch,
        TimeSpan interShardPacing,
        MemoryPool<byte> pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(survivorItems);
        ArgumentNullException.ThrowIfNull(verifyHealed);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(pool);

        List<ReadOnlyMemory<byte>> recovered = [];
        int shardsProcessed = 0;

        //One pooled snapshot for the whole attempt; disposed after every shard has been driven, since each
        //session copies the keys it is handed at construction.
        using PrefixShardPartition partition = Policy.Partition(survivorItems, keyWidth, pool);

        //Drive shards in bounded-parallel waves. A wave launches up to MaxConcurrentShards fetches, each of which
        //the host routes to a distinct channel, then awaits the wave before the next. No recursion: a flat cursor
        //walks the shard range, an explicit inner loop fills each wave.
        int shardCount = partition.ShardCount;
        int next = 0;
        while(next < shardCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int waveSize = Math.Min(MaxConcurrentShards, shardCount - next);
            Task<ShardReconcileResult>[] wave = new Task<ShardReconcileResult>[waveSize];
            for(int lane = 0; lane < waveSize; lane++)
            {
                int shard = next + lane;
                wave[lane] = fetch(shard, Policy.Fingerprint, partition.Shard(shard), shardSymbolCap, pool, cancellationToken).AsTask();
            }

            ShardReconcileResult[] results = await Task.WhenAll(wave).ConfigureAwait(false);
            for(int lane = 0; lane < results.Length; lane++)
            {
                ShardReconcileResult result = results[lane];
                shardsProcessed++;

                //An absent declaration is refused before the fingerprint comparison: the transport faulted before
                //the peer ever declared, so there is no foreign value to name — refusing it as a policy mismatch
                //would diagnose a network blip as a deployment misconfiguration, and no honest substitute value
                //exists (echoing the local value is the recorded trust violation). Nothing is consumed.
                if(result.PeerFingerprint is not { } declaredFingerprint)
                {
                    return new ShardedPeerRepairResult(ShardedRepairOutcome.PeerUndeclared, [], shardsProcessed, result.ShardIndex);
                }

                //The peer's declared policy is verified before ANYTHING else in the result is consumed:
                //difference-stream cancellation is undefined across mismatched policies, so the completion
                //status and items of such a session are meaningless. Running ahead of the completion check
                //attributes an incomplete decode under a mismatch to the mismatch, not to the symbol cap.
                if(declaredFingerprint != Policy.Fingerprint)
                {
                    return new ShardedPeerRepairResult(ShardedRepairOutcome.PolicyMismatch, [], shardsProcessed, result.ShardIndex);
                }

                //A shard that did not converge within its cap poisons the whole attempt: a partial healed set
                //cannot be faithfulness-gated, so the rung abandons and re-ingests nothing.
                if(!result.Completed)
                {
                    return new ShardedPeerRepairResult(ShardedRepairOutcome.IncompleteShard, [], shardsProcessed, result.ShardIndex);
                }

                if(!ResolveShardDirection(result, partition.Shard(result.ShardIndex), recovered))
                {
                    return new ShardedPeerRepairResult(ShardedRepairOutcome.DirectionGuardRejected, [], shardsProcessed, result.ShardIndex);
                }
            }

            next += waveSize;

            if(interShardPacing > TimeSpan.Zero && next < shardCount)
            {
                await Task.Delay(interShardPacing, TimeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        //The authoritative gate over the composed set. Under sparse-survivor or whole-generation loss the
        //per-shard direction guard is vacuous (see ResolveShardDirection); this whole-generation check is the
        //remaining anchor, and it too loses its anchor if the generation's own sketch was lost — the open
        //question the wiring notes flag.
        if(!verifyHealed(recovered))
        {
            return new ShardedPeerRepairResult(ShardedRepairOutcome.FaithfulnessRejected, [], shardsProcessed, -1);
        }

        return new ShardedPeerRepairResult(ShardedRepairOutcome.Recovered, recovered, shardsProcessed, -1);
    }

    /// <summary>Splits one shard's decoded symmetric difference by direction, appending the peer-only items to <paramref name="recovered"/>: an item absent from the shard's local survivors is a lost item to recover, while a survivor the peer lacks is left alone by an add-only rung. Rejects the shard when any difference item does not hash into the shard that recovered it.</summary>
    /// <param name="result">The shard's decoded difference.</param>
    /// <param name="localShardItems">The shard's local survivor items, the direction anchor.</param>
    /// <param name="recovered">The composed peer-only items the accepted difference appends to.</param>
    /// <returns><see langword="false"/> when a difference item belongs to a different shard — a corrupted or adversarial stream.</returns>
    private bool ResolveShardDirection(ShardReconcileResult result, IReadOnlyList<ReadOnlyMemory<byte>> localShardItems, List<ReadOnlyMemory<byte>> recovered)
    {
        //Build the shard's local membership so the symmetric difference can be split: an item in the difference
        //and NOT locally present is peer-only (a lost item to recover); an item in the difference and locally
        //present is a survivor the peer lacks, which an ADD-ONLY rung never deletes and simply leaves.
        HashSet<ReadOnlyMemory<byte>> local = new(ByteMemoryComparer.Instance);
        for(int i = 0; i < localShardItems.Count; i++)
        {
            local.Add(localShardItems[i]);
        }

        for(int i = 0; i < result.DifferenceItems.Count; i++)
        {
            ReadOnlyMemory<byte> item = result.DifferenceItems[i];

            //The generalized direction guard, part one: a recovered item must hash into the very shard that
            //recovered it. A peel that yields an item belonging elsewhere is a corrupted or adversarial stream.
            if(Policy.ShardOf(item.Span) != result.ShardIndex)
            {
                return false;
            }

            //Part two: the survivor exclusion. Peer-only items are the recoverable half; survivors the peer
            //lacks are excluded from re-ingest here.
            //
            //OPEN QUESTION — where this guard CANNOT generalize: when the shard has no local survivors
            //(local.Count == 0), every difference item is trivially peer-only and this exclusion accepts
            //whatever the peer offered, unable to distinguish a legitimately lost item from a peer injecting an
            //arbitrary one. Sparse-survivor whole-generation loss is the degenerate case — no survivor anywhere
            //to anchor direction — and it defeats the per-shard guard entirely. The whole-generation
            //faithfulness gate is then the only defense, and it in turn has no anchor if the generation's own
            //sketch was also lost. This is a known open question: it needs an integrity anchor independent of
            //the surviving replica state (a manifest content-address of the generation's item set, or a signed
            //sketch), which is out of scope for prefix sharding.
            if(!local.Contains(item))
            {
                recovered.Add(item);
            }
        }

        return true;
    }

    /// <summary>A byte-sequence equality comparer over key handles, so the survivor membership split is by content, not by <see cref="ReadOnlyMemory{T}"/> reference identity.</summary>
    private sealed class ByteMemoryComparer: IEqualityComparer<ReadOnlyMemory<byte>>
    {
        /// <summary>The FNV-1a offset basis the byte hash starts from.</summary>
        private const ulong FnvOffsetBasis = 14695981039346656037UL;

        /// <summary>The FNV-1a prime each byte folds in with.</summary>
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>The shared instance; the comparer is stateless.</summary>
        public static ByteMemoryComparer Instance { get; } = new();

        /// <summary>Compares two key handles by byte content.</summary>
        /// <param name="x">The first key.</param>
        /// <param name="y">The second key.</param>
        /// <returns><see langword="true"/> when the byte sequences are equal.</returns>
        public bool Equals(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
        {
            return x.Span.SequenceEqual(y.Span);
        }

        /// <summary>Hashes a key handle's byte content with FNV-1a.</summary>
        /// <param name="obj">The key.</param>
        /// <returns>The content hash.</returns>
        public int GetHashCode(ReadOnlyMemory<byte> obj)
        {
            ulong hash = FnvOffsetBasis;
            ReadOnlySpan<byte> span = obj.Span;
            for(int i = 0; i < span.Length; i++)
            {
                hash = unchecked((hash ^ span[i]) * FnvPrime);
            }

            return unchecked((int)hash);
        }
    }
}
