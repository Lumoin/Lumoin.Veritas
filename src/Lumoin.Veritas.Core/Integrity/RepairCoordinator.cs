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
/// the source — not re-derivable — so its corruption descends every restoring rung and is named a loss at the
/// terminal rung. Each outcome is emitted as a <see cref="StorageTraceEvent"/>.
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

    /// <summary>The derived artifacts regenerated this pass.</summary>
    private readonly List<RederivedArtifact> rederived = [];

    /// <summary>The item losses named this pass.</summary>
    private readonly List<UnrecoverableItemReport> losses = [];

    /// <summary>The running event sequence number, assigned in emission order.</summary>
    private long sequence;

    /// <summary>The artifact role currently under repair, read by <see cref="AttemptRung"/>.</summary>
    private ManifestFileRole current;

    /// <summary>Creates a coordinator for one repair pass.</summary>
    /// <param name="feed">The verified system-of-record feed every re-derive and loss-naming reads from; borrowed — the caller owns its lifetime.</param>
    /// <param name="configuration">The re-derive knobs and pools.</param>
    /// <param name="generation">The commit generation the named losses belong to.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timestampTicks">The pass timestamp stamped on every emitted event.</param>
    internal RepairCoordinator(
        ItemSegmentFeed feed,
        RepairConfiguration configuration,
        long generation,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        long timestampTicks)
    {
        this.feed = feed;
        this.configuration = configuration;
        this.generation = generation;
        this.trace = trace;
        this.correlationId = correlationId;
        this.timestampTicks = timestampTicks;
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

    /// <summary>The ladder's per-rung attempt: the re-derive rung rebuilds a damaged derived artifact from the verified system-of-record feed; the local-parity and peer rungs are not yet sourced and decline, as does the re-derive rung for the non-re-derivable system-of-record.</summary>
    /// <param name="rung">The rung being attempted.</param>
    /// <returns><see langword="true"/> when the rung restored the corruption.</returns>
    private bool AttemptRung(RepairRung rung)
    {
        if(rung != RepairRung.RederiveLocally)
        {
            //Local parity and peer reconciliation have no source until later tiers land; they decline so the
            //ladder descends.
            return false;
        }

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

    /// <summary>Rebuilds the columnar sidecar from the verified system-of-record triples and records the fresh image.</summary>
    private void RederiveSidecar()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(
            MemoryMarshal.ToEnumerable(feed.VerifiedItems),
            configuration.OrderSetMode,
            configuration.ValueEncoding,
            configuration.Backing);
        ArrayBufferWriter<byte> writer = new();
        ColumnarIndexFile.Write(index, writer, configuration.Checksum);
        Record(ManifestFileRole.Sidecar, writer.WrittenMemory, feed.VerifiedCount);
    }

    /// <summary>Rebuilds the integrity sketch by projecting the verified system-of-record triples into reconciliation items and folding them through the host encoder, and records the fresh image.</summary>
    private void RederiveSketch()
    {
        int count = feed.VerifiedCount;
        long itemByteCount = (long)count * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The system-of-record holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemOwner = configuration.BytePool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..count];
        ReadOnlySpan<EncodedTriple> verified = feed.VerifiedItems.Span;
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
