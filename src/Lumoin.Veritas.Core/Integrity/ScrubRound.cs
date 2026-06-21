using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Parity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The storage scrub: it holds a committed manifest snapshot and walks the artifacts that generation names —
/// the system-of-record segment, the columnar sidecar, the integrity sketch, and the local-parity sidecar —
/// verifying each through the format-neutral <see cref="ArtifactVerifyReport"/> seam, emitting a
/// <see cref="StorageTraceEvent"/> per
/// block and front-matter verdict, and RECORDING every at-rest failure (rather than throwing) so one pass
/// surfaces the whole damage set for the repair-source ladder to act on. A reader incompatibility (an unknown
/// checksum-algorithm id or unsupported version) is NOT corruption and is not recorded — it propagates,
/// mirroring the manifest recovery discipline.
/// </summary>
public static class ScrubRound
{
    /// <summary>Runs one verify pass over the committed generation: recovers a manifest snapshot, reads and verifies each artifact it names through the seam, emits a trace event per verdict, and returns the corrupt blocks found. A missing or framing-damaged artifact is recorded as a whole-artifact (front-matter) finding; roles the scrub does not verify (stats) are skipped.</summary>
    /// <param name="store">The store holding the committed generation's artifacts.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="trace">The diagnostics sink each verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timeProvider">The clock the pass timestamps its events with; read once per pass.</param>
    /// <returns>The pass verdict: blocks verified and the corrupt blocks found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">No committed manifest generation could be recovered.</exception>
    /// <exception cref="NotSupportedException">A recovered manifest or a scrubbed artifact uses a checksum algorithm or format version this build does not support.</exception>
    public static ScrubRoundReport RunVerifyPass(
        PersistenceStore store,
        ResolveChecksumAlgorithmDelegate? resolveChecksum,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        RecoveryResult recovery = new ManifestRecovery(store, resolveChecksum).Recover();
        Manifest manifest = recovery.Manifest;
        long generation = manifest.CommitGeneration;
        long timestampTicks = timeProvider.GetUtcNow().UtcTicks;
        long sequence = 0;
        int verified = 0;
        List<ScrubBlockFinding> corrupt = [];

        foreach(ManifestEntry entry in manifest.Entries)
        {
            int roleCode = entry.Role.Code;
            byte[]? image = store.Read(entry.FileName);
            if(image is null)
            {
                corrupt.Add(new ScrubBlockFinding(roleCode, entry.FileName, -1, 0, 0, IsFrontMatter: true));
                Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.FrontMatterCorrupt, generation, roleCode, -1, 0, 0);
                continue;
            }

            ArtifactVerifyReport report;
            try
            {
                ArtifactVerifyReport? verifiedReport = Verify(entry.Role, image, resolveChecksum);
                if(verifiedReport is null)
                {
                    //A role this scrub does not verify (stats / unknown): no verdict, no record.
                    continue;
                }

                report = verifiedReport;
            }
            catch(InvalidDataException)
            {
                //Framing damage: the artifact's geometry cannot be trusted, so it is a whole-artifact loss.
                corrupt.Add(new ScrubBlockFinding(roleCode, entry.FileName, -1, 0, 0, IsFrontMatter: true));
                Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.FrontMatterCorrupt, generation, roleCode, -1, 0, 0);

                continue;
            }

            if(report.HasFrontMatterChecksum && !report.FrontMatterValid)
            {
                corrupt.Add(new ScrubBlockFinding(roleCode, entry.FileName, -1, 0, 0, IsFrontMatter: true));
                Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.FrontMatterCorrupt, generation, roleCode, -1, 0, 0);
            }

            for(int i = 0; i < report.Blocks.Count; i++)
            {
                BlockVerdict verdict = report.Blocks[i];
                if(verdict.IsValid)
                {
                    verified++;
                    Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.BlockVerified, generation, roleCode, verdict.BlockIndex, verdict.ByteOffset, verdict.ByteLength);
                }
                else
                {
                    corrupt.Add(new ScrubBlockFinding(roleCode, entry.FileName, verdict.BlockIndex, verdict.ByteOffset, verdict.ByteLength, IsFrontMatter: false));
                    Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.BlockCorrupt, generation, roleCode, verdict.BlockIndex, verdict.ByteOffset, verdict.ByteLength);
                }
            }
        }

        return new ScrubRoundReport(generation, recovery.IsDegraded, verified, corrupt);
    }

    /// <summary>Runs one repair pass over a verify report's corrupt blocks: re-derives each damaged re-derivable artifact (the sidecar, the sketch, or the local-parity sidecar) from the verified system-of-record, restores a single lost system-of-record block from a co-versioned parity sidecar when the generation carries one and it verifies clean, and names the item loss of each corrupt system-of-record block the parity cannot restore, emitting a <see cref="StorageTraceEvent"/> per outcome. It is a generation-agnostic producer — it returns the fresh artifact images and named losses but commits nothing; the caller stages the images and publishes a new generation. The pass declines without acting when the held snapshot is degraded, when the recovered generation no longer matches the report, or when the system-of-record cannot be read.</summary>
    /// <remarks>The system-of-record is repaired before any derived view, so a view damaged in the same pass is re-derived from the healed item set — the parity-restored full set when a lost block was recovered, or the verified feed when a block was instead named lost — and every artifact in the published generation reflects the same items.</remarks>
    /// <param name="store">The store holding the committed generation's artifacts.</param>
    /// <param name="verifyReport">The verify pass's report whose corrupt blocks this pass repairs.</param>
    /// <param name="configuration">The injected re-derive knobs and pools.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="trace">The diagnostics sink each outcome is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timeProvider">The clock the pass timestamps its events with; read once per pass.</param>
    /// <returns>The repair verdict: the regenerated artifacts and named losses, or the refusal reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="verifyReport"/>, <paramref name="configuration"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">No committed manifest generation could be recovered.</exception>
    /// <exception cref="NotSupportedException">A recovered manifest or a scrubbed artifact uses a checksum algorithm or format version this build does not support.</exception>
    public static RepairPassReport RunRepairPass(
        PersistenceStore store,
        ScrubRoundReport verifyReport,
        RepairConfiguration configuration,
        ResolveChecksumAlgorithmDelegate? resolveChecksum,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(verifyReport);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        //A degraded snapshot may be a torn-publish orphan; re-deriving atop it is unsafe.
        if(verifyReport.IsDegradedSnapshot)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.DegradedSnapshot, [], []);
        }

        //A clean report has nothing to repair.
        if(verifyReport.CorruptBlocks.Count == 0)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.None, [], []);
        }

        RecoveryResult recovery = new ManifestRecovery(store, resolveChecksum).Recover();
        if(recovery.IsDegraded)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.DegradedSnapshot, [], []);
        }

        Manifest manifest = recovery.Manifest;
        if(manifest.CommitGeneration != verifyReport.CommitGeneration)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.StaleFindings, [], []);
        }

        bool dataSegmentDamaged = false;
        bool sidecarDamaged = false;
        bool sketchDamaged = false;
        bool parityDamaged = false;
        for(int i = 0; i < verifyReport.CorruptBlocks.Count; i++)
        {
            int roleCode = verifyReport.CorruptBlocks[i].RoleCode;
            if(roleCode == ManifestFileRole.DataSegment.Code)
            {
                dataSegmentDamaged = true;
            }
            else if(roleCode == ManifestFileRole.Sidecar.Code)
            {
                sidecarDamaged = true;
            }
            else if(roleCode == ManifestFileRole.Sketch.Code)
            {
                sketchDamaged = true;
            }
            else if(roleCode == ManifestFileRole.Parity.Code)
            {
                parityDamaged = true;
            }
        }

        byte[]? systemOfRecordImage = ReadDataSegmentImage(store, manifest);
        if(systemOfRecordImage is null)
        {
            return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.SystemOfRecordUnreadable, [], []);
        }

        //Every repair path reads the one verified system-of-record feed: to re-derive the sidecar or the
        //sketch from the surviving triples, or to name a corrupt block's lost item range. Framing or
        //front-matter damage makes the geometry untrustworthy, so the pass declines uniformly across every
        //role (SystemOfRecordUnreadable) rather than guessing — and a sketch re-derive can never bypass that
        //refusal. The pass owns the feed for its lifetime; the coordinator borrows it.
        ItemSegmentFeed? feed = null;
        try
        {
            feed = ItemSegment.ReadVerifiedItems(systemOfRecordImage, configuration.TriplePool, resolveChecksum);
            (_, int systemOfRecordBlockItemCount, int systemOfRecordBlockAlignment) = ItemSegment.ReadGeometry(systemOfRecordImage);

            //A corrupt system-of-record block can be restored from a co-versioned parity sidecar, when the
            //generation carries one and it itself verifies clean at rest; a re-derivable view never needs it.
            ParityRepairSource? paritySource = dataSegmentDamaged
                ? TryReadVerifiedParity(store, manifest, systemOfRecordImage, resolveChecksum)
                : null;

            long timestampTicks = timeProvider.GetUtcNow().UtcTicks;
            RepairCoordinator coordinator = new(feed, configuration, manifest.CommitGeneration, trace, correlationId, timestampTicks, systemOfRecordBlockItemCount, systemOfRecordBlockAlignment, paritySource);
            //Restore the system-of-record first: a co-damaged view re-derived afterward then folds the healed
            //full item set rather than the block-excluded feed, so every published tier reflects the same items.
            if(dataSegmentDamaged)
            {
                coordinator.Repair(ManifestFileRole.DataSegment);
            }

            if(sidecarDamaged)
            {
                coordinator.Repair(ManifestFileRole.Sidecar);
            }

            if(sketchDamaged)
            {
                coordinator.Repair(ManifestFileRole.Sketch);
            }

            //The local-parity sidecar is re-derivable from the system-of-record like a view, but only over an
            //INTACT segment: a segment that was also damaged is either restored — which needs an undamaged parity,
            //so the parity is not damaged here — or named lost with an incomplete item set, so a damaged or lossy
            //segment's parity awaits a pruned re-derive rather than protecting fewer blocks than the segment carries.
            if(parityDamaged && !dataSegmentDamaged)
            {
                coordinator.Repair(ManifestFileRole.Parity);
            }

            return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.None, [.. coordinator.RederivedArtifacts], [.. coordinator.NamedLosses]);
        }
        catch(InvalidDataException)
        {
            return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.SystemOfRecordUnreadable, [], []);
        }
        finally
        {
            feed?.Dispose();
        }
    }

    /// <summary>Reads the system-of-record (data segment) image the recovered generation names, or <see langword="null"/> when the generation names none or the store no longer holds it.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <returns>The system-of-record image bytes, or <see langword="null"/>.</returns>
    private static byte[]? ReadDataSegmentImage(PersistenceStore store, Manifest manifest)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role == ManifestFileRole.DataSegment)
            {
                return store.Read(entry.FileName);
            }
        }

        return null;
    }

    /// <summary>Reads the generation's local-parity sidecar into a restoring source, or returns <see langword="null"/> when the generation names no parity, the store no longer holds it, or it does not itself verify clean at rest — only a parity sound at rest is trustworthy enough to restore a system-of-record block from. A framing-damaged or unsupported parity is no source rather than a pass failure, so the repair descends to a named loss.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="systemOfRecordImage">The at-rest-corrupt system-of-record image the surviving blocks are read from during the restore.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <returns>The restoring source, or <see langword="null"/> when no usable parity is present.</returns>
    private static ParityRepairSource? TryReadVerifiedParity(PersistenceStore store, Manifest manifest, ReadOnlyMemory<byte> systemOfRecordImage, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role != ManifestFileRole.Parity)
            {
                continue;
            }

            byte[]? parityImage = store.Read(entry.FileName);
            if(parityImage is null)
            {
                return null;
            }

            try
            {
                if(!ParitySegment.RunVerifyRound(parityImage, resolveChecksum).IsClean)
                {
                    return null;
                }

                (_, int protectedBlockCount) = ParitySegment.ReadGeometry(parityImage);
                byte[] parity = ParitySegment.ReadFrom(parityImage, resolveChecksum);

                return new ParityRepairSource(systemOfRecordImage, parity, protectedBlockCount, resolveChecksum);
            }
            catch(InvalidDataException)
            {
                return null;
            }
            catch(NotSupportedException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Verifies one artifact through the format-neutral seam by its manifest role, or returns <see langword="null"/> for a role the scrub does not verify (stats or an unknown role).</summary>
    /// <param name="role">The artifact's manifest role.</param>
    /// <param name="image">The artifact's byte image.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <returns>The verify report, or <see langword="null"/> when the role is not scrubbed.</returns>
    private static ArtifactVerifyReport? Verify(ManifestFileRole role, ReadOnlySpan<byte> image, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        if(role == ManifestFileRole.Sidecar)
        {
            return ColumnarTripleIndex.RunVerifyRound(image, resolveChecksum).ToArtifactReport();
        }

        if(role == ManifestFileRole.DataSegment)
        {
            return ItemSegment.RunVerifyRound(image, resolveChecksum);
        }

        if(role == ManifestFileRole.Sketch)
        {
            return SketchSegment.RunVerifyRound(image, resolveChecksum);
        }

        if(role == ManifestFileRole.Parity)
        {
            return ParitySegment.RunVerifyRound(image, resolveChecksum);
        }

        return null;
    }

    /// <summary>Emits one scrub trace event when a sink is attached, assigning the next monotonic sequence number.</summary>
    /// <param name="trace">The sink, or <see langword="null"/> to emit nothing.</param>
    /// <param name="sequence">The running sequence counter, advanced by one.</param>
    /// <param name="timestampTicks">The pass timestamp.</param>
    /// <param name="correlationId">The pass correlation id.</param>
    /// <param name="kind">The verdict kind.</param>
    /// <param name="generation">The held commit generation.</param>
    /// <param name="roleCode">The artifact's role code.</param>
    /// <param name="blockIndex">The block index, or -1 for a whole-artifact verdict.</param>
    /// <param name="byteOffset">The block's byte offset, or 0.</param>
    /// <param name="byteLength">The block's byte length, or 0.</param>
    private static void Emit(TraceHandler<StorageTraceEvent>? trace, ref long sequence, long timestampTicks, Guid correlationId, StorageTraceEventKind kind, long generation, int roleCode, int blockIndex, long byteOffset, long byteLength)
    {
        if(trace is null)
        {
            return;
        }

        StorageTraceEvent evt = new(sequence, timestampTicks, correlationId, kind, generation, roleCode, blockIndex, byteOffset, byteLength, 0);
        sequence++;
        trace(in evt);
    }
}
