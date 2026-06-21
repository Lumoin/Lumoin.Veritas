using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Persistence.Manifest;
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
internal sealed class RepairCoordinator
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

    /// <summary>The derived artifacts regenerated this pass.</summary>
    private readonly List<RederivedArtifact> rederived = [];

    /// <summary>The item losses named this pass.</summary>
    private readonly List<UnrecoverableItemReport> losses = [];

    /// <summary>The running event sequence number, assigned in emission order.</summary>
    private long sequence;

    /// <summary>The artifact role currently under repair, read by <see cref="AttemptRung"/>.</summary>
    private ManifestFileRole current;

    /// <summary>The full system-of-record item set after a parity restore healed the segment this pass, or <see langword="null"/> when the segment was not restored — a view re-derived after a restore folds these so every tier reflects the same items.</summary>
    private ReadOnlyMemory<EncodedTriple>? restoredItems;

    /// <summary>Creates a coordinator for one repair pass.</summary>
    /// <param name="feed">The verified system-of-record feed every re-derive and loss-naming reads from; borrowed — the caller owns its lifetime.</param>
    /// <param name="configuration">The re-derive knobs and pools.</param>
    /// <param name="generation">The commit generation the named losses belong to.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timestampTicks">The pass timestamp stamped on every emitted event.</param>
    /// <param name="paritySource">The local-parity restoring source for the system-of-record, or <see langword="null"/> when the generation carries no usable parity; borrowed for the pass.</param>
    internal RepairCoordinator(
        ItemSegmentFeed feed,
        RepairConfiguration configuration,
        long generation,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        long timestampTicks,
        ParityRepairSource? paritySource = null)
    {
        this.feed = feed;
        this.configuration = configuration;
        this.generation = generation;
        this.trace = trace;
        this.correlationId = correlationId;
        this.timestampTicks = timestampTicks;
        this.paritySource = paritySource;
    }

    /// <summary>The derived artifacts regenerated this pass, for the caller to stage and publish.</summary>
    internal IReadOnlyList<RederivedArtifact> RederivedArtifacts => rederived;

    /// <summary>The item losses named this pass.</summary>
    internal IReadOnlyList<UnrecoverableItemReport> NamedLosses => losses;

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

    /// <summary>The re-derive rung: a damaged derived artifact (the sidecar, the sketch) is rebuilt from the verified system-of-record feed; the non-re-derivable system-of-record declines.</summary>
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
        byte[]? repaired = ItemSegment.TryRestoreBlockFromParity(source.SystemOfRecordImage.Span, lost.BlockIndex, source.Parity.Span, source.ProtectedBlockCount, configuration.BytePool, source.ResolveChecksum);
        if(repaired is null)
        {
            return false;
        }

        //The healed image holds the full item set; a view re-derived after this folds it, not the pruned feed.
        EncodedTriple[] healed = ItemSegment.ReadFrom(repaired, source.ResolveChecksum);
        restoredItems = healed;
        RecordRestored(ManifestFileRole.DataSegment, repaired, healed.Length);

        return true;
    }

    /// <summary>The system-of-record items a re-derive folds: the parity-restored full set when the segment was healed this pass, otherwise the verified feed, which excludes any block the segment lost.</summary>
    private ReadOnlyMemory<EncodedTriple> RederiveItems => restoredItems ?? feed.VerifiedItems;

    /// <summary>The number of system-of-record items a re-derive folds.</summary>
    private int RederiveItemCount => restoredItems?.Length ?? feed.VerifiedCount;

    /// <summary>Rebuilds the columnar sidecar from the verified system-of-record triples and records the fresh image.</summary>
    private void RederiveSidecar()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(
            MemoryMarshal.ToEnumerable(RederiveItems),
            configuration.OrderSetMode,
            configuration.ValueEncoding,
            configuration.Backing);
        ArrayBufferWriter<byte> writer = new();
        ColumnarIndexFile.Write(index, writer, configuration.Checksum);
        Record(ManifestFileRole.Sidecar, writer.WrittenMemory, RederiveItemCount);
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

        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, configuration.SketchContract, configuration.SymbolBudget, configuration.Checksum, configuration.BytePool, configuration.EncodeSketchSymbols, writer);
        Record(ManifestFileRole.Sketch, writer.WrittenMemory, count);
    }

    /// <summary>Records a regenerated artifact and emits its re-derived outcome.</summary>
    /// <param name="role">The regenerated artifact's role.</param>
    /// <param name="image">The fresh image bytes.</param>
    /// <param name="itemCount">The number of system-of-record items the artifact was rebuilt from.</param>
    private void Record(ManifestFileRole role, ReadOnlyMemory<byte> image, long itemCount)
    {
        rederived.Add(new RederivedArtifact(role, image));
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
}
