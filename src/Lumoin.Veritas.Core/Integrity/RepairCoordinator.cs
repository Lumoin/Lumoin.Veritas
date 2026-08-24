using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Parity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Drives one repair pass's per-artifact descent down the repair-source ladder without a lexical closure: the
/// damaged artifact under repair and every input it needs are instance state, and <see cref="AttemptRung"/> is
/// the ladder's per-rung attempt bound as a method group. A re-derivable artifact (the sidecar, the sketch) is
/// rebuilt from the one verified system-of-record feed at the re-derive rung; the system-of-record itself is
/// not re-derivable, but a single lost block can be restored from a borrowed parity source at the local-parity
/// rung, or lost blocks recovered from a peer replica at the peer-reconciliation rung — a single block against
/// the peer's verified sketch, a multi-block loss through sharded add-only sessions — where the healed set must
/// additionally reconcile emptily against the generation's own at-rest-verified sketch, so a peer heal
/// publishes only content the generation's own record corroborates. A system-of-record loss no rung can restore
/// (no source, more than one lost block, a stale parity, or a peer whose peel fails a gate or whose healed set
/// the generation's sketch does not corroborate) descends to a named loss at the terminal rung. Each outcome is
/// emitted as a <see cref="StorageTraceEvent"/>.
/// </summary>
internal sealed class RepairCoordinator: IDisposable
{
    /// <summary>The verified system-of-record feed every re-derive and loss-naming reads from; borrowed for the pass — the caller owns its lifetime.</summary>
    private readonly ItemSegmentFeed feed;

    /// <summary>The re-derive knobs and pools.</summary>
    private readonly RepairConfiguration configuration;

    /// <summary>The commit generation the named losses belong to.</summary>
    private readonly long generation;

    /// <summary>The term-dictionary epoch the generation's manifest records; a peer source keyed to a different epoch encodes incomparable items, so the peer-reconciliation rung declines it.</summary>
    private long DictionaryEpoch { get; }

    /// <summary>The generation's own at-rest-verified integrity sketch — the independent record of the pre-damage item set the peer-reconciliation rung verifies a healed set against — or <see langword="null"/> when the generation names none or it failed its at-rest verification, which leaves the rung unsourced so it declines fail-closed.</summary>
    private VerifiedSketch? GenerationSketch { get; }

    /// <summary>The diagnostics sink each outcome is emitted to; <see langword="null"/> emits nothing.</summary>
    private readonly TraceHandler<StorageTraceEvent>? trace;

    /// <summary>The correlation id shared by every event of this pass.</summary>
    private readonly Guid correlationId;

    /// <summary>The pass timestamp stamped on every emitted event.</summary>
    private readonly long timestampTicks;

    /// <summary>The local-parity restoring source for the system-of-record, or <see langword="null"/> when the generation carries no usable parity; borrowed for the pass.</summary>
    private readonly ParityRepairSource? paritySource;

    /// <summary>The peer-reconciliation restoring source for the system-of-record, or <see langword="null"/> when no peer was supplied; borrowed for the pass.</summary>
    private readonly PeerReconciliationSource? peerSource;

    /// <summary>The sharded multi-block peer-reconciliation restoring source the host bound for this pass, or <see langword="null"/> when none is. Pure delegate seams and scalars: the coordinator reads it and owns nothing through it.</summary>
    private ShardedPeerReconciliationSource? ShardedPeerSource { get; }

    /// <summary>One while a repair is descending the ladder. Exactly one repair may be in flight per coordinator: the per-pass state (<see cref="current"/>, <see cref="restoredItemsOwner"/>, the report lists) is written across await points, so a concurrent or reentrant <see cref="RepairAsync"/> is an invariant violation, fenced loudly rather than racing. A field, not a property: <see cref="Interlocked.CompareExchange(ref int, int, int)"/> mutates it by reference.</summary>
    private int repairInFlight;

    /// <summary>The generation's verified sketch the in-flight sharded attempt gates faithfulness against; meaningful only while that attempt runs (set before the rung is awaited, read by the bound verify method).</summary>
    private VerifiedSketch ShardedExpectedSketch { get; set; }

    /// <summary>The lost ranges' total item count the in-flight sharded attempt's composed-count gate demands; meaningful only while that attempt runs.</summary>
    private long ShardedLostItemTotal { get; set; }

    /// <summary>The precise coordinator-diagnosed cause when the bound verify method refuses a composed recovered set (<see cref="ShardedRepairOutcome.CountMismatch"/> or <see cref="ShardedRepairOutcome.MalformedItem"/>), or <see langword="null"/> when no finer detail than the rung's own outcome exists; reset before each sharded attempt.</summary>
    private ShardedRepairOutcome? ShardedRefusalDetail { get; set; }

    /// <summary>The system-of-record's block geometry — the triples per block — a parity re-derive rebuilds the parity over, so the regenerated parity protects the same blocks the original did.</summary>
    private int SystemOfRecordBlockItemCount { get; }

    /// <summary>The system-of-record's block alignment — the page boundary its blocks begin on — a re-derived parity image is framed under, so it matches the alignment of the segment it co-versions.</summary>
    private int SystemOfRecordBlockAlignment { get; }

    /// <summary>The derived artifacts regenerated this pass.</summary>
    private readonly List<RederivedArtifact> rederived = [];

    /// <summary>The item losses named this pass.</summary>
    private readonly List<UnrecoverableItemReport> losses = [];

    /// <summary>The running event sequence number, assigned in emission order.</summary>
    private long sequence;

    /// <summary>The artifact role currently under repair, read by <see cref="AttemptRung"/>.</summary>
    private ManifestFileRole current;

    /// <summary>The owned, pooled full system-of-record item set after a local-parity or peer-reconciliation restore healed the segment this pass, or <see langword="null"/> when the segment was not restored. A view re-derived after a restore folds these (via <see cref="RederiveItems"/>) so every tier reflects the same items; the coordinator disposes it at pass end, after those re-derives have run. It needs to be a mutable field because it is set during the pass and is the disposable resource the coordinator owns.</summary>
    private DecodedItemSegment? restoredItemsOwner;

    /// <summary>The pooled image buffers backing this pass's re-derived artifacts (a restored system-of-record image so far); the coordinator owns them until <see cref="TakeImageOwners"/> hands them to the <see cref="RepairPassReport"/> and clears this list, after which the report disposes them. Any still listed when the pass ends — an exception before the transfer — are disposed by <see cref="Dispose"/>, so the emptiness of this list IS the "transferred" state (no separate flag). Get-only: the list reference is fixed, its contents are the mutable state.</summary>
    private List<PooledArtifactImage> ImageOwners { get; } = [];

    /// <summary>Creates a coordinator for one repair pass.</summary>
    /// <param name="feed">The verified system-of-record feed every re-derive and loss-naming reads from; borrowed — the caller owns its lifetime.</param>
    /// <param name="configuration">The re-derive knobs and pools.</param>
    /// <param name="generation">The commit generation the named losses belong to.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the generation's manifest records, which a peer source must match.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timestampTicks">The pass timestamp stamped on every emitted event.</param>
    /// <param name="systemOfRecordBlockItemCount">The system-of-record's triples per block, so a parity re-derive rebuilds the parity over the same block geometry.</param>
    /// <param name="systemOfRecordBlockAlignment">The system-of-record's block alignment, so a re-derived parity image is framed under the same alignment as the segment it co-versions.</param>
    /// <param name="paritySource">The local-parity restoring source for the system-of-record, or <see langword="null"/> when the generation carries no usable parity; borrowed for the pass.</param>
    /// <param name="peerSource">The peer-reconciliation restoring source for the system-of-record, or <see langword="null"/> when no peer was supplied; borrowed for the pass.</param>
    /// <param name="generationSketch">The generation's own at-rest-verified integrity sketch, or <see langword="null"/> when it names none or the sketch failed verification — the peer-reconciliation rung then declines fail-closed.</param>
    /// <param name="shardedPeerSource">The sharded multi-block peer-reconciliation restoring source, or <see langword="null"/> when no sharded transport was bound; borrowed for the pass.</param>
    internal RepairCoordinator(
        ItemSegmentFeed feed,
        RepairConfiguration configuration,
        long generation,
        long dictionaryEpoch,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        long timestampTicks,
        int systemOfRecordBlockItemCount,
        int systemOfRecordBlockAlignment,
        ParityRepairSource? paritySource = null,
        PeerReconciliationSource? peerSource = null,
        VerifiedSketch? generationSketch = null,
        ShardedPeerReconciliationSource? shardedPeerSource = null)
    {
        this.feed = feed;
        this.configuration = configuration;
        this.generation = generation;
        DictionaryEpoch = dictionaryEpoch;
        this.trace = trace;
        this.correlationId = correlationId;
        this.timestampTicks = timestampTicks;
        SystemOfRecordBlockItemCount = systemOfRecordBlockItemCount;
        SystemOfRecordBlockAlignment = systemOfRecordBlockAlignment;
        this.paritySource = paritySource;
        this.peerSource = peerSource;
        GenerationSketch = generationSketch;
        ShardedPeerSource = shardedPeerSource;
    }

    /// <summary>The derived artifacts regenerated this pass, for the caller to stage and publish.</summary>
    internal IReadOnlyList<RederivedArtifact> RederivedArtifacts => rederived;

    /// <summary>The item losses named this pass.</summary>
    internal IReadOnlyList<UnrecoverableItemReport> NamedLosses => losses;

    /// <summary>Transfers ownership of this pass's pooled image buffers to the caller (the <see cref="RepairPassReport"/>): it returns a snapshot and CLEARS the coordinator's list, so a later <see cref="Dispose"/> finds nothing to dispose (the report now owns them). Call exactly once, when the pass succeeds and the report is built.</summary>
    /// <returns>The pooled image buffers backing the re-derived artifacts.</returns>
    internal IReadOnlyList<PooledArtifactImage> TakeImageOwners()
    {
        PooledArtifactImage[] taken = [.. ImageOwners];
        ImageOwners.Clear();

        return taken;
    }

    /// <summary>Repairs the damaged artifact of role <paramref name="role"/> by descending the repair-source ladder: a derived artifact re-derives at the re-derive rung; the system-of-record descends to the terminal rung and its corrupt blocks' item ranges are named lost. Strictly sequential: the coordinator's per-pass state makes a concurrent or reentrant call an invariant violation, fenced loudly.</summary>
    /// <param name="role">The damaged artifact's role.</param>
    /// <param name="cancellationToken">Cancels a transport-bound rung cooperatively.</param>
    /// <exception cref="InvalidOperationException">A repair is already in flight on this coordinator.</exception>
    internal async ValueTask RepairAsync(ManifestFileRole role, CancellationToken cancellationToken)
    {
        if(Interlocked.CompareExchange(ref repairInFlight, 1, 0) != 0)
        {
            throw new InvalidOperationException("A repair is already in flight on this coordinator; repairs are strictly sequential per pass.");
        }

        try
        {
            current = role;
            RepairRung outcome = await RepairSourceLadder.DescendAsync(AttemptRung, cancellationToken).ConfigureAwait(false);
            if(outcome == RepairRung.NamedLoss)
            {
                NameLosses(role);
            }
        }
        finally
        {
            Volatile.Write(ref repairInFlight, 0);
        }
    }

    /// <summary>The ladder's per-rung attempt: the re-derive rung rebuilds a damaged derived artifact from the verified system-of-record feed; the local-parity rung restores a single lost system-of-record block from the parity source; the peer-reconciliation rung restores lost blocks from a peer — single-block against a fetched sketch, multi-block through sharded sessions. A rung with no source, or a corruption it does not restore, declines so the ladder descends. Deliberately a manual dispatch rather than an <see langword="async"/> method: the local rungs answer a synchronously-completed value task with no allocation, unconditionally.</summary>
    /// <param name="rung">The rung being attempted.</param>
    /// <param name="cancellationToken">Cancels the transport-bound peer attempt cooperatively.</param>
    /// <returns><see langword="true"/> when the rung restored the corruption.</returns>
    private ValueTask<bool> AttemptRung(RepairRung rung, CancellationToken cancellationToken)
    {
        return rung switch
        {
            RepairRung.RederiveLocally => new ValueTask<bool>(AttemptRederive()),
            RepairRung.LocalParity => new ValueTask<bool>(AttemptLocalParity()),
            RepairRung.PeerReconciliation => AttemptPeerReconciliationAsync(cancellationToken),
            //No restoring rung applied; the ladder descends to a named loss.
            _ => new ValueTask<bool>(false),
        };
    }

    /// <summary>The re-derive rung: a damaged derived artifact (the sidecar, the sketch, or the local-parity sidecar) is rebuilt from the verified system-of-record feed; the non-re-derivable system-of-record declines.</summary>
    /// <returns><see langword="true"/> when the artifact was re-derived.</returns>
    private bool AttemptRederive()
    {
        if(current == ManifestFileRole.Sidecar)
        {
            RederiveSidecar();

            return true;
        }

        if(current == ManifestFileRole.Sketch)
        {
            RederiveSketch();

            return true;
        }

        if(current == ManifestFileRole.Parity)
        {
            RederiveParity();

            return true;
        }

        //The system-of-record is the canonical source, not a re-derivable view.
        return false;
    }

    /// <summary>The local-parity rung: a single lost system-of-record block is restored from the borrowed parity source — the parity XORed with the surviving blocks, self-checked against the lost block's stored checksum — and the healed image is recorded as a re-ingested artifact. It applies only to the system-of-record, and only when exactly one block is lost (a capacity-1 parity restores one block); no parity source, more than one lost block, or a parity that fails the restore's self-check declines so the ladder descends to a named loss.</summary>
    /// <returns><see langword="true"/> when the lost block was restored.</returns>
    private bool AttemptLocalParity()
    {
        if(current != ManifestFileRole.DataSegment)
        {
            return false;
        }

        if(paritySource is not ParityRepairSource source)
        {
            return false;
        }

        IReadOnlyList<SkippedItemRange> skipped = feed.SkippedRanges;
        if(skipped.Count != 1)
        {
            return false;
        }

        SkippedItemRange lost = skipped[0];
        PooledArtifactImage? repaired = ItemSegment.TryRestoreBlockFromParity(source.SystemOfRecordImage.Span, lost.BlockIndex, source.Parity.Span, source.ProtectedBlockCount, configuration.BytePool, source.ResolveChecksum);
        if(repaired is null)
        {
            return false;
        }

        try
        {
            //The healed image holds the full item set; a view re-derived after this folds it, not the pruned feed.
            //The decode is owned by the coordinator (restoredItemsOwner) and disposed at pass end, after the
            //co-damaged re-derives that fold restoredItems have run.
            DecodedItemSegment healed = ItemSegment.ReadFrom(repaired.Span, configuration.TriplePool, source.ResolveChecksum);
            restoredItemsOwner = healed;
            RecordRestored(ManifestFileRole.DataSegment, repaired.Memory, healed.Length);

            //Ownership of the restored image transfers to the owned-images list — which transfers to the report on
            //success, or is disposed by Dispose if the pass aborts later. Null the local so the finally disposes it
            //only when a throw above prevented the transfer.
            ImageOwners.Add(repaired);
            repaired = null;
        }
        finally
        {
            repaired?.Dispose();
        }

        return true;
    }

    /// <summary>The peer-reconciliation rung's router: reads the lost-range count ONCE and routes presence-aware. A single lost block prefers the single-block body when its source is present — strictly stronger there, since it adds the per-block count and peer-only pre-filters — and otherwise runs the sharded body, so a deployment that binds only the sharded transport still repairs a single lost block; a multi-block loss routes to the sharded body alone; an intact segment declines both. Each body gates on ITS OWN source's presence, epoch, and the generation sketch — a declined body never falls through to the other, so which capability served a repair is always the routed one.</summary>
    /// <param name="cancellationToken">Cancels the transport-bound sharded body cooperatively.</param>
    /// <returns><see langword="true"/> when a peer body recovered, verified, and re-ingested the lost items.</returns>
    private async ValueTask<bool> AttemptPeerReconciliationAsync(CancellationToken cancellationToken)
    {
        if(current != ManifestFileRole.DataSegment)
        {
            return false;
        }

        IReadOnlyList<SkippedItemRange> skipped = feed.SkippedRanges;
        if(skipped.Count == 0)
        {
            return false;
        }

        if(skipped.Count == 1 && peerSource is not null)
        {
            return AttemptSingleBlockPeerReconciliation(skipped[0]);
        }

        return await AttemptShardedPeerReconciliationAsync(skipped, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The single-block peer body: one lost system-of-record block is recovered from a peer's verified sketch — the local survivors' sketch combined with the peer's, peeled to their symmetric difference — and the healed full item set is re-ingested as the system-of-record, but only after the healed set is verified FAITHFUL against the generation's own at-rest-verified sketch (an empty residual difference proves the heal restored exactly the pre-damage set, not merely a plausible superset of the survivors). It applies only when the peer's dictionary epoch matches the generation's, only when the generation's own sketch is present and verified, and only when the peel is complete, recovers exactly the lost block's item count, recovers only peer-only items, and reconciles emptily against the generation's own sketch; any other outcome declines so the ladder descends to a named loss rather than publishing unverified peer content.</summary>
    /// <param name="lost">The one lost range the recovery must account for.</param>
    /// <returns><see langword="true"/> when the lost block's items were recovered, verified faithful, and re-ingested.</returns>
    private bool AttemptSingleBlockPeerReconciliation(SkippedItemRange lost)
    {
        if(peerSource is not PeerReconciliationSource source)
        {
            return false;
        }

        //Encoded identifiers are dictionary-epoch-relative: a peer keyed to a foreign epoch projects items in a
        //different encoding space, so its sketch is incomparable with the local survivors' even when the peer's
        //logical content is identical.
        if(source.DictionaryEpoch != DictionaryEpoch)
        {
            return false;
        }

        //The generation's own sketch is the independent record of the pre-damage item set; without one verified
        //at rest, the healed set cannot be proven faithful, so the rung declines rather than trusting the peer.
        if(GenerationSketch is not VerifiedSketch expected)
        {
            return false;
        }

        VerifiedSketch localSketch = PersistSurvivorSketch(source.SymbolCap);

        int sinkCapacity = localSketch.SymbolCount + source.PeerSketch.SymbolCount;
        long sinkByteCount = (long)sinkCapacity * ContentKey128.ByteWidth;
        if(sinkByteCount > Array.MaxLength)
        {
            return false;
        }

        using IMemoryOwner<byte> recoveredOwner = configuration.BytePool.Rent((int)Math.Max(1, sinkByteCount));
        Span<ContentKey128> recovered = MemoryMarshal.Cast<byte, ContentKey128>(recoveredOwner.Memory.Span)[..sinkCapacity];
        SketchDifference difference = source.Recover(localSketch, source.PeerSketch, source.SymbolCap, recovered);

        //A complete peel that recovered exactly the lost block's item count is the necessary size check; a partial
        //peel, an overflowed sink, or a different recovered count means the peer cannot restore exactly this block,
        //so the rung declines and the loss is named.
        bool restorable = difference.IsComplete && difference.RecoveredCount == lost.ItemCount && difference.RecoveredCount <= recovered.Length;
        if(!restorable)
        {
            return false;
        }

        //Direction guard: the recovered symmetric difference must be ENTIRELY peer-only items. The recovered set
        //is the symmetric difference of the survivors and the peer, so it carries both peer-not-survivor items
        //(what a faithful peer supplies) AND survivor-not-peer items (a peer that also lost a survivor). A
        //matching item count can hide that mix — a survivor among the recovered set means the heal below would
        //duplicate that survivor and silently drop a genuinely-lost item the peer never had — so a recovered
        //survivor declines to a named loss instead of publishing a corrupt system-of-record. Under a faithful
        //peer (the survivors are a subset of the peer) no recovered item is a survivor, so a clean single-block
        //restore is never rejected.
        if(!RecoveredItemsArePeerOnly(recovered[..difference.RecoveredCount]))
        {
            return false;
        }

        //Faithfulness verification: the count and peer-only gates prove the survivors are a subset of the peer
        //and the sizes balance, but a same-epoch diverged peer can satisfy both while substituting foreign items
        //for genuinely-lost ones. The generation's own sketch encodes the exact pre-damage item set, so the
        //healed set is verified against it before anything is staged: an empty residual difference proves the
        //heal restores that set; anything else declines to a named loss. Verified before restoredItemsOwner is
        //assigned, so a declined heal never leaks into a later view re-derive.
        if(!HealedSetMatchesGenerationSketch(recovered[..difference.RecoveredCount], expected, source))
        {
            return false;
        }

        int survivorCount = feed.VerifiedCount;
        int healedCount = survivorCount + difference.RecoveredCount;

        //The healed full item set is the survivors plus the recovered lost items. The coordinator owns the buffer
        //through restoredItemsOwner from here, so any later throw returns it via Dispose; a co-damaged view
        //re-derived after this folds the healed set, not the block-excluded feed.
        IMemoryOwner<EncodedTriple> healedOwner = configuration.TriplePool.Rent(Math.Max(1, healedCount));
        restoredItemsOwner = new DecodedItemSegment(healedOwner, healedCount);
        Span<EncodedTriple> healed = healedOwner.Memory.Span[..healedCount];
        feed.VerifiedItems.Span.CopyTo(healed);
        for(int i = 0; i < difference.RecoveredCount; i++)
        {
            healed[survivorCount + i] = source.Invert(recovered[i]);
        }

        StageAndRecordHealedImage(healedCount);

        return true;
    }

    /// <summary>The multi-block face of the peer-reconciliation rung: partitions the surviving key set by prefix shard, drives per-shard add-only sessions against the peer through the host-bound transport, and re-ingests the composed peer-only difference — but only after the composed set passes the exact-width and composed-count gates and peels to an EMPTY residual against the generation's own at-rest-verified sketch, the same authoritative gate the single-block body ends in. It requires the sharded source, a matching dictionary epoch, and the generation sketch, each read from the sharded source itself; a refused rung outcome is named on the trace — a policy mismatch as the deployment misconfiguration it is, never as corruption — and declines to the ladder's descent.</summary>
    /// <param name="skipped">The lost ranges the healed set must account for.</param>
    /// <param name="cancellationToken">Cancels the shard exchanges cooperatively.</param>
    /// <returns><see langword="true"/> when the lost blocks' items were recovered, verified faithful, and re-ingested.</returns>
    private async ValueTask<bool> AttemptShardedPeerReconciliationAsync(IReadOnlyList<SkippedItemRange> skipped, CancellationToken cancellationToken)
    {
        if(ShardedPeerSource is not ShardedPeerReconciliationSource source)
        {
            return false;
        }

        //Encoded identifiers are dictionary-epoch-relative: a peer keyed to a foreign epoch projects items in a
        //different encoding space, so its difference streams are incomparable with the local survivors' even when
        //the peer's logical content is identical.
        if(source.DictionaryEpoch != DictionaryEpoch)
        {
            return false;
        }

        //Without the generation's own verified sketch the composed set cannot be proven faithful — under sparse
        //survivors the per-shard direction guard is vacuous, so the whole-generation gate is the defense that
        //must exist — and the attempt declines fail-closed.
        if(GenerationSketch is not VerifiedSketch expected)
        {
            return false;
        }

        long lostItemTotal = 0;
        for(int i = 0; i < skipped.Count; i++)
        {
            lostItemTotal += skipped[i].ItemCount;
        }

        int survivorCount = feed.VerifiedCount;
        long survivorByteCount = (long)survivorCount * ContentKey128.ByteWidth;
        if(survivorByteCount > Array.MaxLength)
        {
            return false;
        }

        //One pooled projection of the survivors backs every per-key slice handed to the rung, so the rental
        //spans the whole awaited attempt; the using scopes it around the await.
        using IMemoryOwner<byte> survivorOwner = configuration.BytePool.Rent((int)Math.Max(1, survivorByteCount));
        Memory<byte> survivorMemory = survivorOwner.Memory;
        List<ReadOnlyMemory<byte>> survivorKeys = new(survivorCount);
        for(int i = 0; i < survivorCount; i++)
        {
            Memory<byte> slot = survivorMemory.Slice(i * ContentKey128.ByteWidth, ContentKey128.ByteWidth);
            configuration.Project(feed.VerifiedItems.Span[i]).WriteBytes(slot.Span);
            survivorKeys.Add(slot);
        }

        ShardedExpectedSketch = expected;
        ShardedLostItemTotal = lostItemTotal;
        ShardedRefusalDetail = null;
        ShardedPeerRepairResult result = await source.Rung.AttemptShardedPeerReconciliationAsync(
            survivorKeys,
            ContentKey128.ByteWidth,
            source.ShardSymbolCap,
            VerifyComposedHealedSet,
            source.Fetch,
            source.InterShardPacing,
            configuration.BytePool,
            cancellationToken).ConfigureAwait(false);

        if(result.Outcome != ShardedRepairOutcome.Recovered)
        {
            EmitShardedRefusal(result);

            return false;
        }

        //The verify gate ran inside the rung ahead of the Recovered outcome, so every recovered item's width and
        //the composed count are proven here; the composition reads them on that proof.
        int recoveredCount = result.RecoveredItems.Count;
        int healedCount = survivorCount + recoveredCount;
        IMemoryOwner<EncodedTriple> healedOwner = configuration.TriplePool.Rent(Math.Max(1, healedCount));
        restoredItemsOwner = new DecodedItemSegment(healedOwner, healedCount);
        Span<EncodedTriple> healed = healedOwner.Memory.Span[..healedCount];
        feed.VerifiedItems.Span.CopyTo(healed);
        for(int i = 0; i < recoveredCount; i++)
        {
            healed[survivorCount + i] = source.Invert(ContentKey128.FromBytes(result.RecoveredItems[i].Span));
        }

        StageAndRecordHealedImage(healedCount);

        return true;
    }

    /// <summary>The whole-generation faithfulness gate the sharded attempt binds as a method group over instance state: exact key widths first (<see cref="ContentKey128.FromBytes(ReadOnlySpan{byte})"/> reads only the first sixteen bytes of a longer span, so a wrong-width item would misread content rather than fail — the corrupted-stream class, recorded precisely), then the composed count against the lost ranges' total (the multi-block form of the single-block per-block count gate), then the authoritative check: the healed projection — the survivors plus the recovered keys — must peel to an EMPTY residual against the generation's own sketch. A refusal records its precise cause on <see cref="ShardedRefusalDetail"/> for the trace diagnostic.</summary>
    /// <param name="recoveredItems">The composed peer-only items the rung proposes to re-ingest.</param>
    /// <returns><see langword="true"/> when the composed set is exactly the generation's lost content.</returns>
    private bool VerifyComposedHealedSet(IReadOnlyCollection<ReadOnlyMemory<byte>> recoveredItems)
    {
        foreach(ReadOnlyMemory<byte> item in recoveredItems)
        {
            if(item.Length != ContentKey128.ByteWidth)
            {
                ShardedRefusalDetail = ShardedRepairOutcome.MalformedItem;

                return false;
            }
        }

        if(recoveredItems.Count != ShardedLostItemTotal)
        {
            ShardedRefusalDetail = ShardedRepairOutcome.CountMismatch;

            return false;
        }

        if(ShardedPeerSource is not ShardedPeerReconciliationSource source)
        {
            return false;
        }

        int survivorCount = feed.VerifiedCount;
        long healedByteCount = ((long)survivorCount + recoveredItems.Count) * ContentKey128.ByteWidth;
        if(healedByteCount > Array.MaxLength)
        {
            return false;
        }

        int healedCount = survivorCount + recoveredItems.Count;
        using IMemoryOwner<byte> healedOwner = configuration.BytePool.Rent((int)Math.Max(1, healedByteCount));
        Span<ContentKey128> healed = MemoryMarshal.Cast<byte, ContentKey128>(healedOwner.Memory.Span)[..healedCount];
        ReadOnlySpan<EncodedTriple> survivors = feed.VerifiedItems.Span;
        for(int i = 0; i < survivorCount; i++)
        {
            healed[i] = configuration.Project(survivors[i]);
        }

        int next = survivorCount;
        foreach(ReadOnlyMemory<byte> item in recoveredItems)
        {
            healed[next] = ContentKey128.FromBytes(item.Span);
            next++;
        }

        return HealedProjectionPeelsEmpty(healed, ShardedExpectedSketch, source.Recover);
    }

    /// <summary>Stages a fresh system-of-record image over the healed item set held in <see cref="restoredItemsOwner"/> — under the segment's block geometry, since recovered items are a set, not the original blocks' bytes — records it as re-ingested, and transfers the image's ownership to <see cref="ImageOwners"/>, which transfers to the report on success or is disposed by <see cref="Dispose"/> if the pass aborts later. Serves both peer bodies; the ownership-transfer discipline lives here once.</summary>
    /// <param name="healedCount">The healed item count the image is recorded with.</param>
    /// <exception cref="InvalidOperationException">No healed item set is staged on the coordinator.</exception>
    private void StageAndRecordHealedImage(int healedCount)
    {
        if(restoredItemsOwner is not DecodedItemSegment healedSet)
        {
            throw new InvalidOperationException("The healed item set must be staged on the coordinator before its image is recorded.");
        }

        ItemSegment segment = new(healedSet.Memory, SystemOfRecordBlockItemCount, SystemOfRecordBlockAlignment);
        int size = (int)segment.ComputeSerializedSize(configuration.Checksum);
        using SlabBufferWriter writer = new(configuration.BytePool);
        segment.WriteTo(writer.GetSpan(size)[..size], configuration.Checksum);
        writer.Advance(size);

        //Null the local so the finally disposes it only when a throw above prevented the transfer.
        PooledArtifactImage? image = PooledArtifactImage.Own(writer.Detach(), size);
        try
        {
            RecordRestored(ManifestFileRole.DataSegment, image.Memory, healedCount);
            ImageOwners.Add(image);
            image = null;
        }
        finally
        {
            image?.Dispose();
        }
    }

    /// <summary>Emits the sharded attempt's refused conclusion when a sink is attached, under the kind's field contract: the <see cref="ShardedRepairOutcome"/> code travels in the byte-offset field — the coordinator-diagnosed refinement on <see cref="ShardedRefusalDetail"/> when one exists, else the rung's own outcome — the failed shard index in the block-index field, and the shards processed in the item count.</summary>
    /// <param name="result">The refused rung result.</param>
    private void EmitShardedRefusal(ShardedPeerRepairResult result)
    {
        if(trace is null)
        {
            return;
        }

        ShardedRepairOutcome outcome = ShardedRefusalDetail ?? result.Outcome;
        StorageTraceEvent evt = new(sequence, timestampTicks, correlationId, StorageTraceEventKind.ShardedRepairRefused, generation, ManifestFileRole.DataSegment.Code, result.FailedShardIndex, (long)outcome, 0, result.ShardsProcessed);
        sequence++;
        trace(in evt);
    }

    /// <summary>Projects the verified survivor triples into reconciliation items, persists them as a structural sketch through the host encoder, and loads the image back as a verified sketch — the local operand the peer-reconciliation decode combines with the peer's sketch.</summary>
    /// <param name="symbolCap">The number of symbols to persist — the source's symbol budget.</param>
    /// <returns>The survivors' verified sketch.</returns>
    /// <exception cref="InvalidDataException">The survivor set holds more items than a single projected-item buffer can address.</exception>
    private VerifiedSketch PersistSurvivorSketch(int symbolCap)
    {
        int count = feed.VerifiedCount;
        long itemByteCount = (long)count * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The system-of-record holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemOwner = configuration.BytePool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..count];
        ReadOnlySpan<EncodedTriple> survivors = feed.VerifiedItems.Span;
        for(int i = 0; i < count; i++)
        {
            items[i] = configuration.Project(survivors[i]);
        }

        using SlabBufferWriter writer = new(configuration.BytePool);
        SketchPersistence.PersistSketch(items, configuration.SketchContract, symbolCap, configuration.Checksum, configuration.BytePool, configuration.EncodeSketchSymbols, writer);
        int imageLength = writer.BytesWritten;
        using IMemoryOwner<byte> imageOwner = writer.Detach();

        return SketchPersistence.LoadVerifiedSketch(imageOwner.Memory.Span[..imageLength], configuration.SketchContract);
    }

    /// <summary>Whether every recovered difference item is a peer-only item — none is already among the verified survivors. The recovered set is the symmetric difference of the survivors and the peer, so a survivor appearing in it is the survivors-direction of a diverged peer; under a faithful peer whose item set includes all survivors, the recovered set is entirely peer-only.</summary>
    /// <param name="recovered">The recovered symmetric-difference items.</param>
    /// <returns><see langword="true"/> when no recovered item is a survivor; <see langword="false"/> when at least one is.</returns>
    private bool RecoveredItemsArePeerOnly(ReadOnlySpan<ContentKey128> recovered)
    {
        ReadOnlySpan<EncodedTriple> survivors = feed.VerifiedItems.Span;
        HashSet<ContentKey128> survivorKeys = new(survivors.Length);
        for(int i = 0; i < survivors.Length; i++)
        {
            survivorKeys.Add(configuration.Project(survivors[i]));
        }

        for(int i = 0; i < recovered.Length; i++)
        {
            if(survivorKeys.Contains(recovered[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the healed item set — the survivors plus the recovered items — reconciles to an EMPTY difference against the generation's own at-rest-verified sketch. The healed set's projection is exactly what a heal would publish (the recovered keys ARE the projections of the items the inverse re-ingests), so persisting it through the same deterministic encoder at the stored sketch's symbol budget and peeling the two yields the symmetric difference between the healed set and the pre-damage set: a complete peel of ZERO items proves them equal (an equal-set reconciliation completes on the first combined symbol), while a non-empty or incomplete residual means the heal would publish content the generation's own record does not corroborate. The equality is probabilistic with the sketch's own bounds — a non-empty difference reads as empty only if the differing items' XOR-folded sums AND keyed checksums both cancel, the non-crypto collision event the sketch machinery already accepts for detection — so this rung is sound against divergence and bit rot, not against an adversary with write access to the peer sketch and the store.</summary>
    /// <param name="recovered">The recovered symmetric-difference items the heal would re-ingest.</param>
    /// <param name="expected">The generation's own at-rest-verified sketch — the independent pre-damage record.</param>
    /// <param name="source">The peer source whose recover seam runs the residual peel.</param>
    /// <returns><see langword="true"/> when the residual difference is complete and empty; <see langword="false"/> otherwise, declining the heal.</returns>
    private bool HealedSetMatchesGenerationSketch(ReadOnlySpan<ContentKey128> recovered, VerifiedSketch expected, PeerReconciliationSource source)
    {
        int survivorCount = feed.VerifiedCount;
        long healedByteCount = ((long)survivorCount + recovered.Length) * ContentKey128.ByteWidth;
        if(healedByteCount > Array.MaxLength)
        {
            return false;
        }

        int healedCount = survivorCount + recovered.Length;
        using IMemoryOwner<byte> healedOwner = configuration.BytePool.Rent((int)Math.Max(1, healedByteCount));
        Span<ContentKey128> healed = MemoryMarshal.Cast<byte, ContentKey128>(healedOwner.Memory.Span)[..healedCount];
        ReadOnlySpan<EncodedTriple> survivors = feed.VerifiedItems.Span;
        for(int i = 0; i < survivorCount; i++)
        {
            healed[i] = configuration.Project(survivors[i]);
        }

        recovered.CopyTo(healed[survivorCount..]);

        return HealedProjectionPeelsEmpty(healed, expected, source.Recover);
    }

    /// <summary>The authoritative tail both faithfulness gates share: persists the healed projection through the deterministic host encoder at the stored sketch's own symbol budget, loads it back verified, and peels the residual against the generation's sketch. Only the residual's completeness and count matter — an empty sink is enough, since a non-zero count declines regardless of what the items are; a complete peel of ZERO items proves the healed set equal to the pre-damage set within the sketch machinery's accepted collision bounds.</summary>
    /// <param name="healed">The healed set's projected keys — the survivors plus everything a heal would re-ingest.</param>
    /// <param name="expected">The generation's own at-rest-verified sketch.</param>
    /// <param name="recover">The host-bound seam that combines the sketches and peels the residual.</param>
    /// <returns><see langword="true"/> when the residual difference is complete and empty.</returns>
    private bool HealedProjectionPeelsEmpty(ReadOnlySpan<ContentKey128> healed, VerifiedSketch expected, SketchReconciliationDelegates.RecoverSketchDifference recover)
    {
        using SlabBufferWriter writer = new(configuration.BytePool);
        SketchPersistence.PersistSketch(healed, configuration.SketchContract, expected.SymbolCount, configuration.Checksum, configuration.BytePool, configuration.EncodeSketchSymbols, writer);
        int imageLength = writer.BytesWritten;
        using IMemoryOwner<byte> imageOwner = writer.Detach();
        VerifiedSketch healedSketch = SketchPersistence.LoadVerifiedSketch(imageOwner.Memory.Span[..imageLength], configuration.SketchContract);
        SketchDifference residual = recover(healedSketch, expected, expected.SymbolCount, []);

        return residual.IsComplete && residual.RecoveredCount == 0;
    }

    /// <summary>The system-of-record items a re-derive folds: the restored full set when a local-parity or peer-reconciliation restore healed the segment this pass, otherwise the verified feed, which excludes any block the segment lost.</summary>
    private ReadOnlyMemory<EncodedTriple> RederiveItems => restoredItemsOwner?.Memory ?? feed.VerifiedItems;

    /// <summary>The number of system-of-record items a re-derive folds.</summary>
    private int RederiveItemCount => restoredItemsOwner?.Length ?? feed.VerifiedCount;

    /// <summary>Rebuilds the columnar sidecar from the verified system-of-record triples and records the fresh image.</summary>
    private void RederiveSidecar()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(
            MemoryMarshal.ToEnumerable(RederiveItems),
            configuration.OrderSetMode,
            configuration.ValueEncoding,
            configuration.Backing);
        using SlabBufferWriter writer = new(configuration.BytePool);
        ColumnarIndexFile.Write(index, writer, configuration.Checksum);
        int size = writer.BytesWritten;
        RecordOwnedImage(ManifestFileRole.Sidecar, writer.Detach(), size, RederiveItemCount);
    }

    /// <summary>Rebuilds the integrity sketch by projecting the verified system-of-record triples into reconciliation items and folding them through the host encoder, and records the fresh image.</summary>
    private void RederiveSketch()
    {
        int count = RederiveItemCount;
        long itemByteCount = (long)count * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The system-of-record holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemOwner = configuration.BytePool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..count];
        ReadOnlySpan<EncodedTriple> verified = RederiveItems.Span;
        for(int i = 0; i < count; i++)
        {
            items[i] = configuration.Project(verified[i]);
        }

        using SlabBufferWriter writer = new(configuration.BytePool);
        SketchPersistence.PersistSketch(items, configuration.SketchContract, configuration.SymbolBudget, configuration.Checksum, configuration.BytePool, configuration.EncodeSketchSymbols, writer);
        int size = writer.BytesWritten;
        RecordOwnedImage(ManifestFileRole.Sketch, writer.Detach(), size, count);
    }

    /// <summary>Rebuilds the local-parity sidecar from the verified system-of-record triples — folding the capacity-1 parity over the same block geometry the system-of-record uses — and records the fresh image.</summary>
    private void RederiveParity()
    {
        ItemSegment systemOfRecord = new(RederiveItems, SystemOfRecordBlockItemCount, SystemOfRecordBlockAlignment);
        using ParityBlock parity = ParityBlock.Rent(configuration.BytePool, systemOfRecord.MaxBlockPayloadByteCount);
        int protectedBlockCount = ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, configuration.BytePool);
        ParitySegment segment = new(parity.Memory, protectedBlockCount, SystemOfRecordBlockAlignment);
        int size = (int)segment.ComputeSerializedSize(configuration.Checksum);
        using SlabBufferWriter writer = new(configuration.BytePool);
        segment.WriteTo(writer.GetSpan(size)[..size], configuration.Checksum);
        writer.Advance(size);
        RecordOwnedImage(ManifestFileRole.Parity, writer.Detach(), size, RederiveItemCount);
    }

    /// <summary>Takes ownership of a freshly serialized image buffer (detached from a pooled writer), records the regenerated artifact over a view into it, and emits the re-derived outcome. The image transfers to <see cref="ImageOwners"/> here — which transfers to the report on success or is disposed by <see cref="Dispose"/> on an aborted pass — so it is never wrapped into a flagged local that could leak.</summary>
    /// <param name="role">The regenerated artifact's role.</param>
    /// <param name="owner">The rented buffer the image was serialized into; ownership transfers to this coordinator.</param>
    /// <param name="length">The serialized image byte length.</param>
    /// <param name="itemCount">The number of system-of-record items the artifact was rebuilt from.</param>
    private void RecordOwnedImage(ManifestFileRole role, IMemoryOwner<byte> owner, int length, long itemCount)
    {
        ImageOwners.Add(PooledArtifactImage.Own(owner, length));
        rederived.Add(new RederivedArtifact(role, ImageOwners[^1].Memory));
        Emit(StorageTraceEventKind.Rederived, role.Code, blockIndex: -1, itemCount);
    }

    /// <summary>Records a system-of-record image healed by a local-parity or peer-reconciliation restore and emits its re-ingested outcome — the recovered items re-applied through the ordinary system-of-record image rather than re-derived from a surviving authority.</summary>
    /// <param name="role">The healed artifact's role (the system-of-record).</param>
    /// <param name="image">The healed image bytes.</param>
    /// <param name="itemCount">The number of system-of-record items the healed image holds.</param>
    private void RecordRestored(ManifestFileRole role, ReadOnlyMemory<byte> image, long itemCount)
    {
        rederived.Add(new RederivedArtifact(role, image));
        Emit(StorageTraceEventKind.Reingested, role.Code, blockIndex: -1, itemCount);
    }

    /// <summary>Names the lost item ranges of a corrupt system-of-record: each block the feed excluded is reported as an item-set loss and emitted.</summary>
    /// <param name="role">The system-of-record role.</param>
    private void NameLosses(ManifestFileRole role)
    {
        IReadOnlyList<SkippedItemRange> skipped = feed.SkippedRanges;
        for(int i = 0; i < skipped.Count; i++)
        {
            SkippedItemRange range = skipped[i];
            losses.Add(UnrecoverableItemReport.ItemSet(generation, range.StartItem, range.ItemCount));
            Emit(StorageTraceEventKind.NamedLoss, role.Code, range.BlockIndex, range.ItemCount);
        }
    }

    /// <summary>Names each excluded item range of a damaged NON-default system-of-record segment — a named graph — as an item-set loss tied to its artifact, re-deriving nothing. The default-graph repair ladder's parity and peer rungs restore only the default graph, so a named graph is not routed through the ladder at all: a caller builds the segment's own verified feed and passes its excluded ranges here, and each is named against the artifact so a caller learns exactly which graph lost which items. Emits a NamedLoss event per range.</summary>
    /// <param name="roleCode">The damaged segment's <see cref="ManifestFileRole"/> code.</param>
    /// <param name="artifactFileName">The store file name naming exactly which named-graph segment lost the ranges.</param>
    /// <param name="skipped">The item ranges the segment's own verified feed excluded because their block failed its checksum.</param>
    internal void NameSegmentLosses(int roleCode, string artifactFileName, IReadOnlyList<SkippedItemRange> skipped)
    {
        for(int i = 0; i < skipped.Count; i++)
        {
            SkippedItemRange range = skipped[i];
            losses.Add(UnrecoverableItemReport.ItemSet(generation, roleCode, artifactFileName, range.StartItem, range.ItemCount));
            Emit(StorageTraceEventKind.NamedLoss, roleCode, range.BlockIndex, range.ItemCount);
        }
    }

    /// <summary>Names a whole non-re-derivable artifact as an unrecoverable loss, re-deriving nothing: the term dictionary is the decode key with no restoring source, and a named-graph segment whose whole image cannot be trusted is protected by no parity or peer rung, so the entire artifact is named rather than partially healed. Emits a single whole-artifact NamedLoss event (block index -1).</summary>
    /// <param name="roleCode">The lost artifact's <see cref="ManifestFileRole"/> code.</param>
    /// <param name="artifactFileName">The store file name naming exactly which artifact was lost.</param>
    internal void NameWholeArtifactLoss(int roleCode, string artifactFileName)
    {
        losses.Add(UnrecoverableItemReport.WholeArtifact(generation, roleCode, artifactFileName));
        Emit(StorageTraceEventKind.NamedLoss, roleCode, blockIndex: -1, itemCount: 0);
    }

    /// <summary>Emits one repair outcome when a sink is attached, assigning the next monotonic sequence number.</summary>
    /// <param name="kind">The outcome kind.</param>
    /// <param name="roleCode">The artifact's role code.</param>
    /// <param name="blockIndex">The block index, or -1 for a whole-artifact outcome.</param>
    /// <param name="itemCount">The number of items the outcome covers.</param>
    private void Emit(StorageTraceEventKind kind, int roleCode, int blockIndex, long itemCount)
    {
        if(trace is null)
        {
            return;
        }

        StorageTraceEvent evt = new(sequence, timestampTicks, correlationId, kind, generation, roleCode, blockIndex, 0, 0, itemCount);
        sequence++;
        trace(in evt);
    }

    /// <summary>Returns the pass's pooled buffers to their pools: the restored item-set decode always (set by a local-parity or peer-reconciliation restore), and any re-derived image buffers still owned here. On success <see cref="TakeImageOwners"/> already cleared the list (the report owns them), so this disposes images only when the pass threw before the transfer — a restored-then-aborted pass never leaks the restored image. The borrowed feed and repair sources are the caller's to dispose.</summary>
    public void Dispose()
    {
        restoredItemsOwner?.Dispose();

        for(int i = 0; i < ImageOwners.Count; i++)
        {
            ImageOwners[i].Dispose();
        }
    }
}
