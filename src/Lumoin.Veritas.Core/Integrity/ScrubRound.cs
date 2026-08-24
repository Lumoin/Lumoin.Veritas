using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Parity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The storage scrub: it holds a committed manifest snapshot and walks the artifacts that generation names —
/// the system-of-record segment, the columnar sidecar, the integrity sketch, and the local-parity sidecar —
/// verifying each through the format-neutral <see cref="ArtifactVerifyReport"/> seam, emitting a
/// <see cref="StorageTraceEvent"/> per
/// block and front-matter verdict, and RECORDING every at-rest failure (rather than throwing) so one pass
/// surfaces the whole damage set for the repair-source ladder to act on. A reader incompatibility (an unknown
/// checksum-algorithm id or unsupported version) is NOT corruption and is not recorded — it propagates,
/// mirroring the manifest recovery discipline. A resolution-witness violation (a resolver lying about
/// algorithm identity, or downgrading a reserved keyed id) propagates even out of the optional-source reads
/// that decline gracefully on an unknown id: an unknown id is legitimate version skew, but nothing legitimate
/// produces a witness violation, and the same miswired resolver serves every artifact — so the round aborts
/// loudly rather than continuing inside a compromised composition.
/// </summary>
public static class ScrubRound
{
    /// <summary>Runs one verify pass over the committed generation: recovers a manifest snapshot, and for each artifact it names binds the opened image to the manifest's recorded length and whole-image digest before verifying its blocks through the seam, emitting a trace event per verdict and returning the corrupt blocks found. A missing artifact, an image whose length or digest does not match the manifest, or framing damage is recorded as a whole-artifact (front-matter) finding; a role the scrub does not verify block-by-block (stats) is still attested against the manifest's length and digest, since the generation names it.</summary>
    /// <param name="store">The store holding the committed generation's artifacts.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="trace">The diagnostics sink each verdict is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timeProvider">The clock the pass timestamps its events with; read once per pass.</param>
    /// <returns>The pass verdict: blocks verified and the corrupt blocks found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">No committed manifest generation could be recovered.</exception>
    /// <exception cref="NotSupportedException">A recovered manifest or a scrubbed artifact uses a checksum algorithm or format version this build does not support.</exception>
    /// <exception cref="InvalidOperationException">The resolver violated the resolution witness on any read — a misrouted answer or a downgraded reserved keyed id (see <see cref="ChecksumAlgorithm.ResolveForRead"/>).</exception>
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
            //The verify reads each segment transiently — decode-free verification copies its verdicts out — so the
            //memory-mapped seam serves it with no whole-image heap copy.
            using SegmentImageSource? image = store.OpenImage(entry.FileName);
            if(image is null)
            {
                corrupt.Add(new ScrubBlockFinding(roleCode, entry.FileName, -1, 0, 0, IsFrontMatter: true));
                Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.FrontMatterCorrupt, generation, roleCode, -1, 0, 0);
                continue;
            }

            //The manifest binds each file it names to a recorded length and whole-image digest; the load paths
            //check that binding, so the scrub attests it too. A wrong image substituted under a manifest-named file
            //is internally valid and passes every role verify, yet no load path would accept it — it is a
            //whole-artifact loss, recorded here before the role verify so a condemned image is never verified as
            //though it were the generation's own. A role the scrub does not otherwise verify (stats, unknown) is
            //attested here as well: the manifest names it, so the generation vouches for its integrity.
            if(!MatchesEntry(image.Image, entry, manifest.ChecksumAlgorithm))
            {
                corrupt.Add(new ScrubBlockFinding(roleCode, entry.FileName, -1, 0, 0, IsFrontMatter: true));
                Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.FrontMatterCorrupt, generation, roleCode, -1, 0, 0);
                continue;
            }

            ArtifactVerifyReport report;
            try
            {
                ArtifactVerifyReport? verifiedReport = Verify(entry.Role, image.Image, resolveChecksum);
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

            ReadOnlySpan<BlockVerdict> blocks = report.Blocks.Span;
            for(int i = 0; i < blocks.Length; i++)
            {
                BlockVerdict verdict = blocks[i];
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

        //A parity that is CLEAN at rest yet was folded over a DIFFERENT item-set geometry than the current
        //system of record protects nothing: it passes its own at-rest checksum and its whole-image manifest
        //binding, so no per-block verdict above ever names it, it is never rebuilt, and its dead coverage
        //surfaces only when a block is actually lost and the restore's co-version self-check declines it. When
        //the generation names both a parity and its system-of-record and BOTH verified clean at rest, the
        //parity's stored geometry is compared against the segment's block layout — the same block count and
        //stride the restore's self-check matches — and a mismatch is recorded as a parity front-matter loss so
        //the ordinary re-derive rung rebuilds it from the verified feed. This is a CLEAN-pair check: a parity or
        //segment already damaged at rest drives the right repair through its own finding, and a damaged
        //segment's layout is not trustworthy to read against.
        CheckParityCoVersion(store, manifest, corrupt, trace, ref sequence, timestampTicks, correlationId, generation);

        return new ScrubRoundReport(generation, recovery.IsDegraded, verified, corrupt);
    }

    /// <summary>Records a parity front-matter loss when the generation names a parity and its system-of-record, both verified clean at rest, yet the parity's stored co-version geometry (its protected block count and stride) does not match the segment's block layout — the same block count and stride the restore's self-check matches a parity against. A stale-geometry parity is otherwise invisible to the at-rest verify, so this drives its re-derive before a lost block ever exposes its dead coverage. Does nothing when the generation names no parity or no system-of-record, when either was already found corrupt at rest (its own finding drives the repair and a damaged segment's layout is untrustworthy to read against), or when the pair's geometry agrees.</summary>
    /// <param name="store">The store holding the committed generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="corrupt">The pass's running finding list; a mismatch appends the parity front-matter loss.</param>
    /// <param name="trace">The diagnostics sink the recorded loss is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="sequence">The running trace sequence counter, advanced when a loss is emitted.</param>
    /// <param name="timestampTicks">The pass timestamp.</param>
    /// <param name="correlationId">The pass correlation id.</param>
    /// <param name="generation">The held commit generation.</param>
    private static void CheckParityCoVersion(PersistenceStore store, Manifest manifest, List<ScrubBlockFinding> corrupt, TraceHandler<StorageTraceEvent>? trace, ref long sequence, long timestampTicks, Guid correlationId, long generation)
    {
        ManifestEntry? parityEntry = null;
        ManifestEntry? dataSegmentEntry = null;
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(parityEntry is null && entry.Role == ManifestFileRole.Parity)
            {
                parityEntry = entry;
            }
            else if(dataSegmentEntry is null && entry.Role == ManifestFileRole.DataSegment)
            {
                dataSegmentEntry = entry;
            }
        }

        //The check needs both a parity and the segment it protects; a generation naming only one carries no
        //co-versioned pair. A parity or segment already found corrupt at rest is left to its own at-rest finding.
        if(parityEntry is not ManifestEntry parity || dataSegmentEntry is not ManifestEntry dataSegment
            || HasFinding(corrupt, ManifestFileRole.Parity.Code) || HasFinding(corrupt, ManifestFileRole.DataSegment.Code))
        {
            return;
        }

        //Read both images transiently — only their geometry scalars are read — so the memory-mapped seam serves
        //them with no whole-image heap copy. A clean-at-rest image the store no longer holds would already have
        //been recorded above; a vanished image here is nonetheless not co-version-checkable, so the pair stands.
        using SegmentImageSource? parityImage = store.OpenImage(parity.FileName);
        using SegmentImageSource? dataSegmentImage = store.OpenImage(dataSegment.FileName);
        if(parityImage is null || dataSegmentImage is null)
        {
            return;
        }

        (int parityStride, int parityProtectedBlockCount) = ParitySegment.ReadGeometry(parityImage.Image);
        (int segmentBlockCount, int segmentBlockStride) = ItemSegment.ReadBlockParityGeometry(dataSegmentImage.Image);

        //The parity restores a lost segment block as itself XORed with the survivors, so it must have been folded
        //over exactly this segment's block count and stride; either mismatch is dead coverage.
        if(parityProtectedBlockCount == segmentBlockCount && parityStride == segmentBlockStride)
        {
            return;
        }

        long parityLength = parityImage.Image.Length;
        corrupt.Add(new ScrubBlockFinding(ManifestFileRole.Parity.Code, parity.FileName, -1, 0, parityLength, IsFrontMatter: true));
        Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.FrontMatterCorrupt, generation, ManifestFileRole.Parity.Code, -1, 0, parityLength);
    }

    /// <summary>Whether the running finding list already names a corrupt block or front-matter loss for a given role.</summary>
    /// <param name="corrupt">The pass's running finding list.</param>
    /// <param name="roleCode">The manifest role code to look for.</param>
    /// <returns><see langword="true"/> when a finding for the role is present.</returns>
    private static bool HasFinding(List<ScrubBlockFinding> corrupt, int roleCode)
    {
        for(int i = 0; i < corrupt.Count; i++)
        {
            if(corrupt[i].RoleCode == roleCode)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Runs one repair pass over the UNION of a verify report's corrupt blocks and the damage its own re-read of the system-of-record discovers: the report routes the rungs, and the verified feed attests the system-of-record afresh, so a block that rotted between the verify pass and this pass is folded into the damage set rather than silently excluded. It re-derives each damaged re-derivable artifact (the sidecar, the sketch, or the local-parity sidecar) from the verified system-of-record, restores lost system-of-record blocks from a co-versioned parity sidecar (a single block, when the generation carries one and it verifies clean) or from a peer through the provider-supplied reconciliation sources (a single block against a fetched sketch, a multi-block loss through sharded sessions), and names the item loss of each corrupt system-of-record block no source restores, emitting a <see cref="StorageTraceEvent"/> per outcome. The term dictionary (the non-re-derivable decode key) and the named-graph segments sit outside this default-graph ladder — no parity or peer rung protects them — so a damaged one is named as a loss (a whole-artifact loss for the dictionary or an untrustworthy named-graph image, an item-range loss for a named graph's corrupt blocks) with nothing re-derived from it, leaving the default-graph ladder undisturbed. It is a generation-agnostic producer — it returns the fresh artifact images and named losses but commits nothing; the caller stages the images and publishes a new generation. The pass declines without acting when the held snapshot is degraded, when the recovered generation no longer matches the report, or when the system-of-record cannot be read or is a block-clean image that fails the manifest's whole-image binding (a substituted image no rung can heal from and no re-derive may fold).</summary>
    /// <remarks>The system-of-record is repaired before any derived view, so a view damaged in the same pass is re-derived from the healed item set — the parity-restored full set when a lost block was recovered, or the verified feed when a block was instead named lost — and every artifact in the published generation reflects the same items. Because the feed re-attests the system-of-record at repair time, a view re-derived in this pass folds the healed-or-named item set, never a set silently pruned by a block that rotted after the verify pass named the damage.</remarks>
    /// <param name="store">The store holding the committed generation's artifacts.</param>
    /// <param name="verifyReport">The verify pass's report whose corrupt blocks this pass repairs.</param>
    /// <param name="configuration">The injected re-derive knobs and pools.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="trace">The diagnostics sink each outcome is emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id shared by every event of this pass.</param>
    /// <param name="timeProvider">The clock the pass timestamps its events with; read once per pass.</param>
    /// <param name="providePeerSource">The seam that supplies the single-block peer-reconciliation restoring source, invoked with the damaged generation's recovered facts only when the system-of-record is damaged; <see langword="null"/> or a <see langword="null"/> answer leaves the rung unsourced, so the pass behaves exactly as a local-only repair. A provider that throws is treated as no source — the fault is named on the trace and the pass continues local-only. A peer heal additionally requires the generation's own sketch to verify clean at rest and to corroborate the healed set (an empty residual difference) and the peer's dictionary epoch to match the manifest's — anything less declines to a named loss.</param>
    /// <param name="provideShardedPeerSource">The seam that supplies the sharded multi-block peer-reconciliation restoring source, under the same invocation, null-answer, and fault contract as <paramref name="providePeerSource"/>.</param>
    /// <param name="cancellationToken">Cancels the transport-bound peer attempts cooperatively.</param>
    /// <returns>The repair verdict: the regenerated artifacts and named losses, or the refusal reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="verifyReport"/>, <paramref name="configuration"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">No committed manifest generation could be recovered.</exception>
    /// <exception cref="NotSupportedException">A recovered manifest or a scrubbed artifact uses a checksum algorithm or format version this build does not support.</exception>
    /// <exception cref="InvalidOperationException">The resolver violated the resolution witness on any read, including the optional parity and sketch sources that decline gracefully on an unknown id — a misrouted answer or a downgraded reserved keyed id aborts the round (see <see cref="ChecksumAlgorithm.ResolveForRead"/>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<RepairPassReport> RunRepairPassAsync(
        PersistenceStore store,
        ScrubRoundReport verifyReport,
        RepairConfiguration configuration,
        ResolveChecksumAlgorithmDelegate? resolveChecksum,
        TraceHandler<StorageTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider,
        ProvidePeerReconciliationSourceDelegate? providePeerSource,
        ProvideShardedPeerReconciliationSourceDelegate? provideShardedPeerSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(verifyReport);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);

        //A degraded snapshot may be a torn-publish orphan; re-deriving atop it is unsafe.
        if(verifyReport.IsDegradedSnapshot)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.DegradedSnapshot, [], [], []);
        }

        //A clean report has nothing to repair.
        if(verifyReport.CorruptBlocks.Count == 0)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.None, [], [], []);
        }

        RecoveryResult recovery = new ManifestRecovery(store, resolveChecksum).Recover();
        if(recovery.IsDegraded)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.DegradedSnapshot, [], [], []);
        }

        Manifest manifest = recovery.Manifest;
        if(manifest.CommitGeneration != verifyReport.CommitGeneration)
        {
            return new RepairPassReport(verifyReport.CommitGeneration, RepairRefusalReason.StaleFindings, [], [], []);
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

        //The system-of-record image is read into a pooled buffer this pass OWNS and holds for its whole lifetime:
        //the feed and geometry read it, and the parity-repair source reads surviving blocks from it across the
        //repair. It is disposed last (this using), after the coordinator/parity/feed the inner finally releases.
        using PooledSegmentImageSource? systemOfRecordSource = ReadDataSegmentImage(store, manifest, configuration.BytePool, out ManifestEntry? dataSegmentEntry);
        if(systemOfRecordSource is null)
        {
            return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.SystemOfRecordUnreadable, [], [], []);
        }

        ReadOnlyMemory<byte> systemOfRecordImage = systemOfRecordSource.ImageMemory;

        //Every repair path reads the one verified system-of-record feed: to re-derive the sidecar or the
        //sketch from the surviving triples, or to name a corrupt block's lost item range. Framing or
        //front-matter damage makes the geometry untrustworthy, so the pass declines uniformly across every
        //role (SystemOfRecordUnreadable) rather than guessing — and a sketch re-derive can never bypass that
        //refusal. The pass owns the feed for its lifetime; the coordinator borrows it.
        ItemSegmentFeed? feed = null;
        ParityBlock? parityBlock = null;
        RepairCoordinator? coordinator = null;
        try
        {
            feed = ItemSegment.ReadVerifiedItems(systemOfRecordImage.Span, configuration.TriplePool, resolveChecksum);

            //The feed re-attests the system-of-record at repair time. A block that failed its checksum in the
            //window between the verify pass and this read is excluded from the verified items here, so the damage
            //set is the UNION of what the report named and what the feed now discovers: raising the flag folds a
            //window-rotted block into the repair ladder — restored from parity or peer when possible, otherwise a
            //named loss — instead of pruning it silently from every re-derived view. Raised BEFORE the parity and
            //sketch loads, which are gated on it, so the discovered damage descends the ladder exactly like a
            //report-named block. (Cleanliness alone is the signal: an ungated feed excludes nothing, so there is no
            //silent prune to fold, and WasChecksumGated adds no coverage here.)
            if(!feed.IsClean)
            {
                dataSegmentDamaged = true;
            }

            //The manifest binds the system-of-record to a whole-image digest; a re-read image that walks CLEAN
            //block by block yet fails that binding is a substituted image — internally valid, but not the
            //artifact this generation committed. There is no lost block for a rung to restore and every
            //re-derive would fold a foreign item set into a published view, so the pass declines uniformly
            //(the untrustworthy-identity face of SystemOfRecordUnreadable) rather than reporting recovered a
            //loss it neither healed nor named. A GATED feed is ordinary rot: the whole-image digest is
            //expected to fail there, and the ladder heals or names those blocks instead.
            if(feed.IsClean && dataSegmentEntry is ManifestEntry boundEntry && !MatchesEntry(systemOfRecordImage.Span, boundEntry, manifest.ChecksumAlgorithm))
            {
                return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.SystemOfRecordUnreadable, [], [], []);
            }

            (_, int systemOfRecordBlockItemCount, int systemOfRecordBlockAlignment) = ItemSegment.ReadGeometry(systemOfRecordImage.Span);

            //A corrupt system-of-record block can be restored from a co-versioned parity sidecar, when the
            //generation carries one and it itself verifies clean at rest; a re-derivable view never needs it.
            //The pass owns the restoring parity block for its lifetime and disposes it in the finally; the
            //source's parity view stays valid through the data-segment repair, which runs before the finally.
            ParityRepairSource? paritySource = dataSegmentDamaged
                ? TryReadVerifiedParity(store, manifest, systemOfRecordImage, configuration.BytePool, resolveChecksum, out parityBlock)
                : null;

            long timestampTicks = timeProvider.GetUtcNow().UtcTicks;

            //The peer sources are host-bound per pass, resolved against the recovered generation's own facts;
            //a faulting provider leaves its rung unsourced and is named on the trace, so a transport fault
            //never aborts a viable local repair.
            long sequence = 0;
            PeerReconciliationSource? peerSource = null;
            ShardedPeerReconciliationSource? shardedPeerSource = null;
            if(dataSegmentDamaged && providePeerSource is not null)
            {
                (peerSource, bool faulted) = await ResolvePeerSourceAsync(providePeerSource, manifest.CommitGeneration, manifest.DictionaryEpoch, cancellationToken).ConfigureAwait(false);
                if(faulted)
                {
                    Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.PeerSourceUnavailable, manifest.CommitGeneration, ManifestFileRole.DataSegment.Code, -1, 0, 0);
                }
            }

            if(dataSegmentDamaged && provideShardedPeerSource is not null)
            {
                (shardedPeerSource, bool shardedFaulted) = await ResolveShardedPeerSourceAsync(provideShardedPeerSource, manifest.CommitGeneration, manifest.DictionaryEpoch, cancellationToken).ConfigureAwait(false);
                if(shardedFaulted)
                {
                    Emit(trace, ref sequence, timestampTicks, correlationId, StorageTraceEventKind.PeerSourceUnavailable, manifest.CommitGeneration, ManifestFileRole.DataSegment.Code, -1, 0, 0);
                }
            }

            //The peer bodies heal only what the generation's own records corroborate: its at-rest-verified sketch
            //is the independent pre-damage record the healed set must reconcile emptily against, so it is loaded
            //here (only when a peer body could run) and a missing or unverifiable sketch leaves those bodies
            //unsourced — they then decline fail-closed and the loss is named.
            VerifiedSketch? generationSketch = dataSegmentDamaged && (peerSource is not null || shardedPeerSource is not null)
                ? TryReadVerifiedGenerationSketch(store, manifest, configuration.SketchContract, resolveChecksum)
                : null;

            coordinator = new(feed, configuration, manifest.CommitGeneration, manifest.DictionaryEpoch, trace, correlationId, timestampTicks, systemOfRecordBlockItemCount, systemOfRecordBlockAlignment, paritySource, peerSource, generationSketch, shardedPeerSource);
            //Restore the system-of-record first: a co-damaged view re-derived afterward then folds the healed
            //full item set rather than the block-excluded feed, so every published tier reflects the same items.
            if(dataSegmentDamaged)
            {
                await coordinator.RepairAsync(ManifestFileRole.DataSegment, cancellationToken).ConfigureAwait(false);
            }

            if(sidecarDamaged)
            {
                await coordinator.RepairAsync(ManifestFileRole.Sidecar, cancellationToken).ConfigureAwait(false);
            }

            if(sketchDamaged)
            {
                await coordinator.RepairAsync(ManifestFileRole.Sketch, cancellationToken).ConfigureAwait(false);
            }

            //The local-parity sidecar is re-derivable from the system-of-record like a view, but only over an
            //INTACT segment: a segment that was also damaged is either restored — which needs an undamaged parity,
            //so the parity is not damaged here — or named lost with an incomplete item set, so a damaged or lossy
            //segment's parity awaits a pruned re-derive rather than protecting fewer blocks than the segment carries.
            if(parityDamaged && !dataSegmentDamaged)
            {
                await coordinator.RepairAsync(ManifestFileRole.Parity, cancellationToken).ConfigureAwait(false);
            }

            //The term dictionary and the named-graph segments are system-of-record-class but sit outside the
            //default-graph repair ladder: no parity or peer rung protects them and the dictionary decode key is not
            //re-derivable. A damaged one is named as a loss (an item-range loss for a named graph's corrupt blocks,
            //a whole-artifact loss for the dictionary or an untrustworthy named-graph image) and nothing is
            //re-derived from it, so the default-graph repairs above are left undisturbed.
            NameUnprotectedRoleLosses(store, manifest, verifyReport, coordinator, configuration, resolveChecksum);

            return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.None, [.. coordinator.RederivedArtifacts], [.. coordinator.NamedLosses], [.. coordinator.TakeImageOwners()]);
        }
        catch(InvalidDataException)
        {
            return new RepairPassReport(manifest.CommitGeneration, RepairRefusalReason.SystemOfRecordUnreadable, [], [], []);
        }
        finally
        {
            coordinator?.Dispose();
            parityBlock?.Dispose();
            feed?.Dispose();
        }
    }

    /// <summary>Invokes the single-block peer-source provider, converting a provider fault into an unsourced answer — the pass continues local-only and the caller names the fault on the trace. Cancellation is rethrown.</summary>
    /// <param name="provide">The host-bound provider.</param>
    /// <param name="commitGeneration">The damaged generation under repair.</param>
    /// <param name="dictionaryEpoch">The generation's term-dictionary epoch.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The source (or <see langword="null"/>) and whether the provider faulted.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A peer-source provider fault leaves the rung unsourced and the round local-only; the caller names the fault on the trace, and cancellation is rethrown ahead of the general catch.")]
    private static async ValueTask<(PeerReconciliationSource? Source, bool Faulted)> ResolvePeerSourceAsync(ProvidePeerReconciliationSourceDelegate provide, long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
    {
        try
        {
            return (await provide(commitGeneration, dictionaryEpoch, cancellationToken).ConfigureAwait(false), false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception)
        {
            return (null, true);
        }
    }

    /// <summary>Invokes the sharded peer-source provider under the same fault contract as <see cref="ResolvePeerSourceAsync"/>: a fault answers unsourced for the caller to name on the trace; cancellation is rethrown.</summary>
    /// <param name="provide">The host-bound provider.</param>
    /// <param name="commitGeneration">The damaged generation under repair.</param>
    /// <param name="dictionaryEpoch">The generation's term-dictionary epoch.</param>
    /// <param name="cancellationToken">Cancels the binding.</param>
    /// <returns>The source (or <see langword="null"/>) and whether the provider faulted.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A peer-source provider fault leaves the sharded path unsourced and the round local-only; the caller names the fault on the trace, and cancellation is rethrown ahead of the general catch.")]
    private static async ValueTask<(ShardedPeerReconciliationSource? Source, bool Faulted)> ResolveShardedPeerSourceAsync(ProvideShardedPeerReconciliationSourceDelegate provide, long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
    {
        try
        {
            return (await provide(commitGeneration, dictionaryEpoch, cancellationToken).ConfigureAwait(false), false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception)
        {
            return (null, true);
        }
    }

    /// <summary>Opens the system-of-record (data segment) image the recovered generation names into a pooled buffer the caller owns, or <see langword="null"/> when the generation names none or the store no longer holds it. The image is pooled (not the transient memory-mapped seam) because the repair retains it across the whole pass.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="pool">The pool the image buffer is rented from; the returned source returns it on disposal.</param>
    /// <returns>The pooled system-of-record image source the caller owns and disposes, or <see langword="null"/>.</returns>
    private static PooledSegmentImageSource? ReadDataSegmentImage(PersistenceStore store, Manifest manifest, MemoryPool<byte> pool, out ManifestEntry? dataSegmentEntry)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role == ManifestFileRole.DataSegment)
            {
                dataSegmentEntry = entry;

                return store.OpenPooledImage(entry.FileName, pool);
            }
        }

        dataSegmentEntry = null;

        return null;
    }

    /// <summary>Names the losses of the damaged roles the default-graph repair ladder does not cover — the term dictionary and every named-graph segment — routing each through the coordinator so the losses join the pass's report and trace under one sequence. The dictionary is named a whole-artifact loss on any damage (the decode key is not re-derivable and is useless partially decoded); each damaged named graph is named through <see cref="NameNamedGraphLoss"/>. Neither re-derives anything, so the default-graph repairs already run are untouched.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="verifyReport">The verify pass's report whose corrupt blocks route the naming.</param>
    /// <param name="coordinator">The pass coordinator the named losses and trace events are recorded through.</param>
    /// <param name="configuration">The pass configuration whose triple pool a named graph's feed rents from.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    private static void NameUnprotectedRoleLosses(PersistenceStore store, Manifest manifest, ScrubRoundReport verifyReport, RepairCoordinator coordinator, RepairConfiguration configuration, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        string? damagedDictionary = null;
        HashSet<string> damagedNamedGraphs = [];
        for(int i = 0; i < verifyReport.CorruptBlocks.Count; i++)
        {
            ScrubBlockFinding finding = verifyReport.CorruptBlocks[i];
            if(finding.RoleCode == ManifestFileRole.Dictionary.Code)
            {
                damagedDictionary ??= finding.FileName;
            }
            else if(finding.RoleCode == ManifestFileRole.NamedGraphSegment.Code)
            {
                damagedNamedGraphs.Add(finding.FileName);
            }
        }

        //Any dictionary damage names the whole artifact lost: the decode key is not re-derivable, has no restoring
        //source, and a partially-decodable dictionary cannot decode the generation, so no block is salvaged.
        if(damagedDictionary is not null)
        {
            coordinator.NameWholeArtifactLoss(ManifestFileRole.Dictionary.Code, damagedDictionary);
        }

        foreach(string fileName in damagedNamedGraphs)
        {
            NameNamedGraphLoss(store, manifest, coordinator, configuration, resolveChecksum, fileName);
        }
    }

    /// <summary>Names the loss of one damaged named-graph segment by building its own verified feed — exactly as the default graph's system-of-record is re-read at repair time — and naming each excluded item range, re-deriving nothing: parity and peer restore only the default graph, so a named graph descends straight to a named loss. A gated feed names each corrupt block's item range; a block-clean image that fails the manifest's whole-image binding, a framing/front-matter-damaged image, or a missing file names the whole segment lost, since no trustworthy item range can be enumerated from it.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest, whose entry binds the segment to a length and whole-image digest.</param>
    /// <param name="coordinator">The pass coordinator the named losses and trace events are recorded through.</param>
    /// <param name="configuration">The pass configuration whose triple pool the feed rents from.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="fileName">The store file name of the damaged named-graph segment.</param>
    /// <exception cref="NotSupportedException">The segment uses a checksum algorithm or format version this build does not support — a reader incompatibility that propagates rather than being named a loss.</exception>
    private static void NameNamedGraphLoss(PersistenceStore store, Manifest manifest, RepairCoordinator coordinator, RepairConfiguration configuration, ResolveChecksumAlgorithmDelegate? resolveChecksum, string fileName)
    {
        int roleCode = ManifestFileRole.NamedGraphSegment.Code;

        //The image is read transiently — the feed copies the verified triples out — so the memory-mapped seam
        //serves it with no whole-image heap copy.
        using SegmentImageSource? image = store.OpenImage(fileName);
        if(image is null)
        {
            coordinator.NameWholeArtifactLoss(roleCode, fileName);

            return;
        }

        ItemSegmentFeed? feed = null;
        try
        {
            feed = ItemSegment.ReadVerifiedItems(image.Image, configuration.TriplePool, resolveChecksum);
            if(!feed.IsClean)
            {
                //Per-block rot: name each excluded item range. The whole-image binding is deliberately not
                //consulted for a gated feed — its digest is expected to differ, exactly as on the default-graph path.
                coordinator.NameSegmentLosses(roleCode, fileName, feed.SkippedRanges);
            }
            else if(FindEntry(manifest, fileName) is ManifestEntry entry && !MatchesEntry(image.Image, entry, manifest.ChecksumAlgorithm))
            {
                //A block-clean image that fails the manifest's whole-image binding is a substituted or otherwise
                //untrustworthy whole segment with no lost block to enumerate, so the whole named graph is named lost.
                coordinator.NameWholeArtifactLoss(roleCode, fileName);
            }

            //A clean, bound feed carries no loss: the verify pass named the file, but the re-read finds it whole.
        }
        catch(InvalidDataException)
        {
            //Framing or front-matter damage makes the geometry untrustworthy — no item range can be enumerated —
            //so the whole named graph is named lost.
            coordinator.NameWholeArtifactLoss(roleCode, fileName);
        }
        finally
        {
            feed?.Dispose();
        }
    }

    /// <summary>Finds the manifest entry naming a given store file, or <see langword="null"/> when the manifest names none.</summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="fileName">The store file name to find.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    private static ManifestEntry? FindEntry(Manifest manifest, string fileName)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(string.Equals(entry.FileName, fileName, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Reads the generation's local-parity sidecar into a restoring source, or returns <see langword="null"/> when the generation names no parity, the store no longer holds it, or it does not itself verify clean at rest — only a parity sound at rest is trustworthy enough to restore a system-of-record block from. A framing-damaged or unsupported parity is no source rather than a pass failure, so the repair descends to a named loss.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="systemOfRecordImage">The at-rest-corrupt system-of-record image the surviving blocks are read from during the restore.</param>
    /// <param name="pool">The pool the verified parity block is rented from; the caller owns and disposes the block returned in <paramref name="parityBlock"/>.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="parityBlock">On a non-<see langword="null"/> return, the owned parity block backing the source's parity view, which the caller disposes once the restore is done; <see langword="null"/> when no usable parity is present.</param>
    /// <returns>The restoring source, or <see langword="null"/> when no usable parity is present.</returns>
    private static ParityRepairSource? TryReadVerifiedParity(PersistenceStore store, Manifest manifest, ReadOnlyMemory<byte> systemOfRecordImage, MemoryPool<byte> pool, ResolveChecksumAlgorithmDelegate? resolveChecksum, out ParityBlock? parityBlock)
    {
        parityBlock = null;
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role != ManifestFileRole.Parity)
            {
                continue;
            }

            //The parity image is read transiently — the verify and ReadFrom copy what they keep (the parity block's
            //own pooled bytes) — so the memory-mapped seam serves it with no whole-image heap copy.
            using SegmentImageSource? parityImage = store.OpenImage(entry.FileName);
            if(parityImage is null)
            {
                return null;
            }

            try
            {
                if(!ParitySegment.RunVerifyRound(parityImage.Image, resolveChecksum).IsClean)
                {
                    return null;
                }

                (_, int protectedBlockCount) = ParitySegment.ReadGeometry(parityImage.Image);
                ParityBlock parity = ParitySegment.ReadFrom(parityImage.Image, pool, resolveChecksum);
                parityBlock = parity;

                return new ParityRepairSource(systemOfRecordImage, parity.Memory, protectedBlockCount, resolveChecksum);
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

    /// <summary>Loads the generation's own integrity sketch as the peer-reconciliation rung's independent pre-damage record, or returns <see langword="null"/> when the generation names no sketch, the store no longer holds it, or it fails its at-rest verification or geometry gate — only a sketch verified sound at rest is trustworthy enough to corroborate a healed system-of-record, so anything less leaves the rung unsourced and the loss is named. A damaged or foreign-geometry sketch is no source rather than a pass failure, mirroring the parity discipline.</summary>
    /// <param name="store">The store holding the generation's artifacts.</param>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="contract">The sketch geometry the loaded sketch must match.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <returns>The generation's verified sketch, or <see langword="null"/> when no usable sketch is present.</returns>
    private static VerifiedSketch? TryReadVerifiedGenerationSketch(PersistenceStore store, Manifest manifest, SketchContract contract, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role != ManifestFileRole.Sketch)
            {
                continue;
            }

            //The sketch image is read transiently — the verifying load copies the symbol bytes out — so the
            //memory-mapped seam serves it with no whole-image heap copy.
            using SegmentImageSource? image = store.OpenImage(entry.FileName);
            if(image is null)
            {
                return null;
            }

            try
            {
                return SketchPersistence.LoadVerifiedSketch(image.Image, contract, resolveChecksum);
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

    /// <summary>Whether an opened image matches the length and whole-image digest the manifest recorded for its entry, binding the file to the generation that named it — the same length-then-digest check the durable load path applies. The digest is compared under the manifest's own checksum algorithm and only when the entry carries a digest of that algorithm's width; a manifest that carries no checksums (or a foreign-width entry) binds the length alone. A mismatch means the file under the entry's name is not the one the generation committed — a wrong-image substitution or a truncation the segment's own per-block checksums cannot see.</summary>
    /// <param name="image">The opened artifact image.</param>
    /// <param name="entry">The manifest entry that named the artifact.</param>
    /// <param name="algorithm">The manifest's own checksum algorithm, or <see langword="null"/> when it carries no checksums.</param>
    /// <returns><see langword="true"/> when the image matches the manifest's recorded length and, where comparable, digest.</returns>
    private static bool MatchesEntry(ReadOnlySpan<byte> image, ManifestEntry entry, ChecksumAlgorithm? algorithm)
    {
        if(image.Length != entry.ByteLength)
        {
            return false;
        }

        if(algorithm is null || entry.Checksum.Length != algorithm.ByteWidth)
        {
            return true;
        }

        Span<byte> recomputed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        Span<byte> digest = recomputed[..algorithm.ByteWidth];
        algorithm.Compute(image, digest);

        return digest.SequenceEqual(entry.Checksum.Span);
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

        if(role == ManifestFileRole.Dictionary)
        {
            return DictionarySegment.RunVerifyRound(image, resolveChecksum);
        }

        //A named graph's system-of-record segment shares the item-segment format with the default graph, so
        //its blocks verify through the same decode-free seam.
        if(role == ManifestFileRole.NamedGraphSegment)
        {
            return ItemSegment.RunVerifyRound(image, resolveChecksum);
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
