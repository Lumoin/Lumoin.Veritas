using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
/// rung — a system-of-record loss the parity cannot restore (no parity source, more than one lost block, a
/// stale parity) descends to a named loss at the terminal rung. Each outcome is emitted as a
/// <see cref="StorageTraceEvent"/>.
/// </summary>
internal sealed class RepairCoordinator: IDisposable
{
    /// <summary>The verified system-of-record feed every re-derive and loss-naming reads from; borrowed for the pass — the caller owns its lifetime.</summary>
    private readonly ItemSegmentFeed feed;

    /// <summary>The re-derive knobs and pools.</summary>
    private readonly RepairConfiguration configuration;

    /// <summary>The commit generation the named losses belong to.</summary>
    private readonly long generation;

    /// <summary>The diagnostics sink each outcome is emitted to; <see langword="null"/> emits nothing.</summary>
    private readonly TraceHandler<StorageTraceEvent>? trace;

    /// <summary>The correlation id shared by every event of this pass.</summary>
    private readonly Guid correlationId;

    /// <summary>The pass timestamp stamped on every emitted event.</summary>
    private readonly long timestampTicks;

    /// <summary>The local-parity restoring source for the system-of-record, or <see langword="null"/> when the generation carries no usable parity; borrowed for the pass.</summary>
    private readonly ParityRepairSource? paritySource;

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

    /// <summary>The owned, pooled full system-of-record item set after a parity restore healed the segment this pass, or <see langword="null"/> when the segment was not restored. A view re-derived after a restore folds these (via <see cref="RederiveItems"/>) so every tier reflects the same items; the coordinator disposes it at pass end, after those re-derives have run. It needs to be a mutable field because it is set during the pass and is the disposable resource the coordinator owns.</summary>
    private DecodedItemSegment? restoredItemsOwner;

    /// <summary>The pooled image buffers backing this pass's re-derived artifacts (a restored system-of-record image so far); the coordinator owns them until <see cref="TakeImageOwners"/> hands them to the <see cref="RepairPassReport"/> and clears this list, after which the report disposes them. Any still listed when the pass ends — an exception before the transfer — are disposed by <see cref="Dispose"/>, so the emptiness of this list IS the "transferred" state (no separate flag). Get-only: the list reference is fixed, its contents are the mutable state.</summary>
    private List<PooledArtifactImage> ImageOwners { get; } = [];

    /// <summary>Creates a coordinator for one repair pass.</summary>
    /// <param name="feed">The verified system-of-record feed every re-derive and loss-naming reads from; borrowed — the caller owns its lifetime.</param>
    /// <param name="configuration">The re-derive knobs and pools.</param>
    /// <param name="generation">The commit generation the named losses belong to.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timestampTicks">The pass timestamp stamped on every emitted event.</param>
    /// <param name="systemOfRecordBlockItemCount">The system-of-record's triples per block, so a parity re-derive rebuilds the parity over the same block geometry.</param>
    /// <param name="systemOfRecordBlockAlignment">The system-of-record's block alignment, so a re-derived parity image is framed under the same alignment as the segment it co-versions.</param>
    /// <param name="paritySource">The local-parity restoring source for the system-of-record, or <see langword="null"/> when the generation carries no usable parity; borrowed for the pass.</param>
    internal RepairCoordinator(
        ItemSegmentFeed feed,
        RepairConfiguration configuration,
        long generation,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        long timestampTicks,
        int systemOfRecordBlockItemCount,
        int systemOfRecordBlockAlignment,
        ParityRepairSource? paritySource = null)
    {
        this.feed = feed;
        this.configuration = configuration;
        this.generation = generation;
        this.trace = trace;
        this.correlationId = correlationId;
        this.timestampTicks = timestampTicks;
        SystemOfRecordBlockItemCount = systemOfRecordBlockItemCount;
        SystemOfRecordBlockAlignment = systemOfRecordBlockAlignment;
        this.paritySource = paritySource;
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

    /// <summary>Repairs the damaged artifact of role <paramref name="role"/> by descending the repair-source ladder: a derived artifact re-derives at the re-derive rung; the system-of-record descends to the terminal rung and its corrupt blocks' item ranges are named lost.</summary>
    /// <param name="role">The damaged artifact's role.</param>
    internal void Repair(ManifestFileRole role)
    {
        current = role;
        RepairRung outcome = RepairSourceLadder.Descend(AttemptRung);
        if(outcome == RepairRung.NamedLoss)
        {
            NameLosses(role);
        }
    }

    /// <summary>The ladder's per-rung attempt: the re-derive rung rebuilds a damaged derived artifact from the verified system-of-record feed; the local-parity rung restores a single lost system-of-record block from the parity source; the peer rung is not yet sourced and declines.</summary>
    /// <param name="rung">The rung being attempted.</param>
    /// <returns><see langword="true"/> when the rung restored the corruption.</returns>
    private bool AttemptRung(RepairRung rung)
    {
        if(rung == RepairRung.RederiveLocally)
        {
            return AttemptRederive();
        }

        if(rung == RepairRung.LocalParity)
        {
            return AttemptLocalParity();
        }

        //Peer reconciliation has no source, so it declines and the ladder descends.
        return false;
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

    /// <summary>The system-of-record items a re-derive folds: the parity-restored full set when the segment was healed this pass, otherwise the verified feed, which excludes any block the segment lost.</summary>
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

    /// <summary>Records a system-of-record image healed by a parity restore and emits its re-ingested outcome — the recovered block re-applied through the ordinary system-of-record image rather than re-derived.</summary>
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

    /// <summary>Returns the pass's pooled buffers to their pools: the parity-restored item-set decode always, and any re-derived image buffers still owned here. On success <see cref="TakeImageOwners"/> already cleared the list (the report owns them), so this disposes images only when the pass threw before the transfer — a restored-then-aborted pass never leaks the restored image. The borrowed feed and parity source are the caller's to dispose.</summary>
    public void Dispose()
    {
        restoredItemsOwner?.Dispose();

        for(int i = 0; i < ImageOwners.Count; i++)
        {
            ImageOwners[i].Dispose();
        }
    }
}
