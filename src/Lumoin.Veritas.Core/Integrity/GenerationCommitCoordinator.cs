using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Closes the storage self-heal loop: it takes a <see cref="ScrubRound.RunRepairPass"/> output — the derived
/// artifacts re-derived from the verified system-of-record — and atomically publishes a HEALED generation that
/// supersedes the damaged one. <see cref="ScrubRound.RunRepairPass"/> is a generation-agnostic producer that
/// commits nothing; this coordinator is the consumer that stages each re-derived image under a fresh name,
/// carries the undamaged artifacts forward by re-listing their unchanged entries, and commits one new manifest
/// generation through the <see cref="ManifestWriter"/> atomic publish.
/// </summary>
/// <remarks>
/// <para>
/// It is the deterministic commit-ownership core: it does not schedule scrub rounds (the lane's cadence) nor
/// wait for quiescence (the await-half) — a producer wires those to it. It publishes only when the repair
/// produced a re-derived artifact (a clean generation, or one whose only damage is a named system-of-record
/// loss, publishes nothing) and only when the live generation still matches the one the repair acted on — so a
/// stale or already-healed report is skipped as superseded, never re-healed. A refused repair, or a live
/// snapshot recovered from the degraded scan, never publishes.
/// </para>
/// <para>
/// Crash safety rides on <see cref="ManifestWriter"/>: the one CURRENT-pointer rename is the single commit
/// point, so a crash leaves the prior generation wholly in force or the healed one wholly live, never a torn
/// mix. Idempotency rides on the live-generation guard: a re-run after a completed heal recovers the advanced
/// generation and skips, while a re-run after a crash before the rename recovers the still-prior generation and
/// completes the heal.
/// </para>
/// <para>
/// A named system-of-record loss is carried through in the report but not restored: the system-of-record is not
/// re-derivable, so the healed generation re-lists the corrupt segment under its prior name and the loss is
/// named for the durability layer. When a system-of-record block is lost alongside a damaged derived artifact,
/// the derived artifact is rebuilt from the surviving items and a healed generation is published carrying the
/// named loss; a system-of-record-only loss has nothing to re-derive and so publishes nothing. Re-staging a
/// pruned system-of-record and re-deriving its views to keep them consistent with the surviving items is a
/// restoring-ladder follow-on (parity, peer, or re-ingest), not this commit core.
/// </para>
/// </remarks>
public sealed class GenerationCommitCoordinator
{
    /// <summary>The store the healed generation is staged into and published through.</summary>
    private readonly PersistenceStore store;

    /// <summary>The checksum algorithm the healed manifest and its entry digests are written under.</summary>
    private readonly ChecksumAlgorithm checksum;

    /// <summary>The pool the staging digests and the manifest writer rent from.</summary>
    private readonly MemoryPool<byte> bufferPool;

    /// <summary>The number of retained per-generation CURRENT copies the manifest writer keeps.</summary>
    private readonly int retainedCurrentPointerCount;

    /// <summary>Resolves a stored checksum-algorithm id when recovering the live manifest; <see langword="null"/> uses the default resolver.</summary>
    private readonly ResolveChecksumAlgorithmDelegate? resolveChecksum;

    /// <summary>The diagnostics sink the healed-generation marker is emitted to; <see langword="null"/> emits nothing.</summary>
    private readonly TraceHandler<StorageTraceEvent>? trace;

    /// <summary>The clock the healed-generation marker is timestamped with.</summary>
    private readonly TimeProvider timeProvider;

    /// <summary>Creates a generation-commit coordinator over a store.</summary>
    /// <param name="store">The store the healed generation is staged into and published through.</param>
    /// <param name="checksum">The checksum algorithm the healed manifest and its entry digests are written under.</param>
    /// <param name="bufferPool">The pool the staging digests and the manifest writer rent from.</param>
    /// <param name="retainedCurrentPointerCount">The number of retained per-generation CURRENT copies the manifest writer keeps.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id when recovering the live manifest; <see langword="null"/> uses the default resolver.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="timeProvider">The clock the healed-generation marker is timestamped with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="checksum"/>, <paramref name="bufferPool"/>, or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retainedCurrentPointerCount"/> is less than 1 — the same lower bound the <see cref="ManifestWriter"/> it builds enforces, checked here so a misconfiguration fails at construction rather than at the first heal.</exception>
    public GenerationCommitCoordinator(
        PersistenceStore store,
        ChecksumAlgorithm checksum,
        MemoryPool<byte> bufferPool,
        int retainedCurrentPointerCount,
        ResolveChecksumAlgorithmDelegate? resolveChecksum,
        TraceHandler<StorageTraceEvent>? trace,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(bufferPool);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedCurrentPointerCount, 1);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.checksum = checksum;
        this.bufferPool = bufferPool;
        this.retainedCurrentPointerCount = retainedCurrentPointerCount;
        this.resolveChecksum = resolveChecksum;
        this.trace = trace;
        this.timeProvider = timeProvider;
    }

    /// <summary>Publishes a healed generation from a repair report when there is one to publish: a refused repair or a degraded live snapshot declines, a report with no re-derived artifact has nothing to commit, a report whose generation the live one has already moved past is superseded, and otherwise the re-derived artifacts are staged and a new generation is atomically committed.</summary>
    /// <param name="repairReport">The repair pass output to publish.</param>
    /// <param name="correlationId">The correlation id the healed-generation marker carries.</param>
    /// <returns>The commit verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="repairReport"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">No committed manifest generation could be recovered.</exception>
    /// <exception cref="NotSupportedException">The recovered manifest uses a checksum algorithm or format version this build does not support.</exception>
    public GenerationCommitReport Commit(RepairPassReport repairReport, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(repairReport);

        if(repairReport.Refused)
        {
            return Declined(GenerationCommitOutcome.Refused, repairReport.CommitGeneration, repairReport, repairReport.Refusal);
        }

        if(repairReport.RederivedArtifacts.Count == 0)
        {
            return Declined(GenerationCommitOutcome.NothingToCommit, repairReport.CommitGeneration, repairReport, RepairRefusalReason.None);
        }

        RecoveryResult recovery = new ManifestRecovery(store, resolveChecksum).Recover();
        if(recovery.IsDegraded)
        {
            return Declined(GenerationCommitOutcome.Refused, repairReport.CommitGeneration, repairReport, RepairRefusalReason.DegradedSnapshot);
        }

        Manifest live = recovery.Manifest;
        if(live.CommitGeneration != repairReport.CommitGeneration)
        {
            return Declined(GenerationCommitOutcome.Superseded, live.CommitGeneration, repairReport, RepairRefusalReason.None);
        }

        return PublishHealedGeneration(live, repairReport, correlationId);
    }

    /// <summary>Stages every re-derived artifact, re-lists the manifest with the re-derived entries replaced in place, and commits the next generation through the atomic publish.</summary>
    /// <param name="live">The live manifest the healed generation supersedes.</param>
    /// <param name="repairReport">The repair pass output.</param>
    /// <param name="correlationId">The correlation id the healed-generation marker carries.</param>
    /// <returns>The committed verdict.</returns>
    private GenerationCommitReport PublishHealedGeneration(Manifest live, RepairPassReport repairReport, Guid correlationId)
    {
        IReadOnlyList<RederivedArtifact> rederived = repairReport.RederivedArtifacts;
        IReadOnlyList<UnrecoverableItemReport> losses = repairReport.NamedLosses;
        long healedGeneration = live.CommitGeneration + 1;
        int digestWidth = checksum.ByteWidth;
        bool hasLosses = losses.Count > 0;

        //The entry digests live in this rented buffer until the manifest is serialized inside Commit, so the
        //buffer outlives that call; each re-derived entry's checksum, and the loss record's when there is one,
        //is a slice of it.
        using IMemoryOwner<byte> digestOwner = bufferPool.Rent((rederived.Count + (hasLosses ? 1 : 0)) * digestWidth);

        Dictionary<int, ManifestEntry> rederivedByRole = new(rederived.Count);
        ManifestFileRole[] republishedRoles = new ManifestFileRole[rederived.Count];
        for(int i = 0; i < rederived.Count; i++)
        {
            RederivedArtifact artifact = rederived[i];
            string name = HealedArtifactNaming.HealedArtifactName(artifact.Role, healedGeneration);
            store.WriteStaged(name, artifact.Image.Span);
            Memory<byte> digest = digestOwner.Memory.Slice(i * digestWidth, digestWidth);
            checksum.Compute(artifact.Image.Span, digest.Span);
            rederivedByRole[artifact.Role.Code] = new ManifestEntry(artifact.Role, name, 0, artifact.Image.Length, digest);
            republishedRoles[i] = artifact.Role;
        }

        //Re-list every prior entry: a re-derived role's entry is replaced in place; an undamaged role's entry is
        //carried forward unchanged, its file still living in the store under the prior generation's name. A prior
        //loss record is never carried forward — this generation's loss record is regenerated fresh from this
        //repair's named losses below, so a heal that named none drops any earlier record and is not lossy.
        List<ManifestEntry> healedEntries = new(live.Entries.Count + (hasLosses ? 1 : 0));
        for(int i = 0; i < live.Entries.Count; i++)
        {
            ManifestEntry prior = live.Entries[i];
            if(prior.Role.Code == ManifestFileRole.Losses.Code)
            {
                continue;
            }

            healedEntries.Add(rederivedByRole.TryGetValue(prior.Role.Code, out ManifestEntry replacement) ? replacement : prior);
        }

        if(hasLosses)
        {
            healedEntries.Add(StageLossRecord(losses, healedGeneration, digestOwner.Memory.Slice(rederived.Count * digestWidth, digestWidth)));
        }

        new ManifestWriter(store, checksum, bufferPool, retainedCurrentPointerCount)
            .Commit(new Manifest(healedGeneration, live.DictionaryEpoch, live.ProvenanceEpoch, healedEntries));

        EmitHealedMarker(healedGeneration, rederived.Count, correlationId);

        return new GenerationCommitReport(GenerationCommitOutcome.Committed, healedGeneration, republishedRoles, losses, RepairRefusalReason.None);
    }

    /// <summary>Serializes the repair's named losses into a self-checksummed loss record, stages it under the generation's loss-record name, and returns its manifest entry (role <see cref="ManifestFileRole.Losses"/>) so the healed generation co-versions the record and stays visibly lossy across a restart. The record's digest is computed into <paramref name="digest"/>, which the caller keeps rented through the manifest commit.</summary>
    /// <param name="losses">The named losses to persist; non-empty.</param>
    /// <param name="healedGeneration">The healed generation the losses belong to.</param>
    /// <param name="digest">The digest slice the record's whole-image checksum is computed into; it outlives the commit that copies it into the manifest image.</param>
    /// <returns>The loss record's manifest entry.</returns>
    private ManifestEntry StageLossRecord(IReadOnlyList<UnrecoverableItemReport> losses, long healedGeneration, Memory<byte> digest)
    {
        int size = DurableLossRecord.ComputeSerializedSize(losses, checksum);
        string name = HealedArtifactNaming.LossRecordName(healedGeneration);

        using IMemoryOwner<byte> image = bufferPool.Rent(size);
        Span<byte> imageSpan = image.Memory.Span[..size];
        DurableLossRecord.WriteTo(imageSpan, healedGeneration, losses, checksum);
        store.WriteStaged(name, imageSpan);
        checksum.Compute(imageSpan, digest.Span);

        return new ManifestEntry(ManifestFileRole.Losses, name, 0, size, digest);
    }

    /// <summary>Builds a non-committed verdict, carrying the repair's named losses through unchanged.</summary>
    /// <param name="outcome">The non-committed outcome.</param>
    /// <param name="generation">The generation the verdict concerns.</param>
    /// <param name="repairReport">The repair pass output whose named losses are carried through.</param>
    /// <param name="refusal">The repair refusal reason, or <see cref="RepairRefusalReason.None"/>.</param>
    /// <returns>The non-committed verdict.</returns>
    private static GenerationCommitReport Declined(GenerationCommitOutcome outcome, long generation, RepairPassReport repairReport, RepairRefusalReason refusal)
    {
        return new GenerationCommitReport(outcome, generation, [], repairReport.NamedLosses, refusal);
    }

    /// <summary>Emits the healed-generation lifecycle marker when a sink is attached. The marker is whole-generation (role code 0, block index -1); it carries the healed generation and the number of artifacts republished.</summary>
    /// <param name="healedGeneration">The generation that was published.</param>
    /// <param name="republishedCount">The number of re-derived artifacts republished.</param>
    /// <param name="correlationId">The correlation id the marker carries.</param>
    private void EmitHealedMarker(long healedGeneration, int republishedCount, Guid correlationId)
    {
        if(trace is null)
        {
            return;
        }

        StorageTraceEvent evt = new(0, timeProvider.GetUtcNow().UtcTicks, correlationId, StorageTraceEventKind.GenerationHealed, healedGeneration, 0, -1, 0, 0, republishedCount);
        trace(in evt);
    }
}
