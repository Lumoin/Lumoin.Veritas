using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Lumoin.Veritas.Tests.MemoryPool;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The generation-commit coordinator closing the self-heal loop: it takes a repair pass's re-derived artifacts
/// and atomically publishes a healed generation that supersedes the damaged one. These drive the round end to
/// end — stage a damaged generation, verify, repair, then commit — and assert the healed generation becomes
/// live, the re-derived artifact verifies clean under a fresh name while the undamaged ones are carried forward
/// unchanged, a stale or already-healed report is superseded rather than re-healed, a clean or refused repair
/// publishes nothing, and the healed-generation marker is emitted. Fault injection is deterministic — fixed
/// bytes flipped, a fixed clock, no timers.
/// </summary>
[TestClass]
internal sealed class GenerationCommitCoordinatorTests
{
    /// <summary>The triple count each generation stages (three ten-item system-of-record blocks).</summary>
    private const uint TripleCount = 30;

    /// <summary>The sketch symbol count each generation stages.</summary>
    private const int SketchSymbolCount = 40;

    /// <summary>The first committed generation a test stages; healing supersedes it with the next.</summary>
    private const long Generation = 7;

    /// <summary>The retained per-generation CURRENT-pointer window the coordinator's manifest writer keeps.</summary>
    private const int RetainedPointers = 4;

    /// <summary>The system-of-record block count for the staged generation (<see cref="TripleCount"/> over ten items per block).</summary>
    private const int SegmentBlockCount = 3;

    /// <summary>The middle system-of-record block a named-loss test corrupts.</summary>
    private const int CorruptBlock = 1;

    /// <summary>Healing a generation whose sidecar is damaged publishes the next generation and makes it live.</summary>
    [TestMethod]
    public async Task HealsADamagedGenerationAndItBecomesLive()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        FileSystemPersistenceStore store = StageDamagedSidecarGeneration(bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            Assert.IsTrue(repair.IsClean, "A damaged-sidecar repair re-derives cleanly.");

            GenerationCommitReport report = Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);

            Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome);
            Assert.AreEqual(Generation + 1, report.Generation, "The healed generation is the next after the damaged one.");
            Assert.Contains(ManifestFileRole.Sidecar, report.RepublishedRoles, "The re-derived sidecar is republished.");

            RecoveryResult recovered = new ManifestRecovery(store).Recover();
            Assert.AreEqual(Generation + 1, recovered.Manifest.CommitGeneration, "Recovery follows CURRENT to the healed generation.");
            Assert.IsFalse(recovered.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The commit boundary is pool-clean: after a heal stages the report's re-derived images into a new generation and the report is disposed, every buffer the repair pass rented from the repair pool is returned (OutstandingRentals == 0) — proven under a poisoning pool. Guards the report-owned image views read during Commit against premature recycling.</summary>
    [TestMethod]
    public async Task HealReturnsEveryRepairPoolBufferOnReportDispose()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using PoisoningMemoryPool<byte> repairPool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        FileSystemPersistenceStore store = StageDamagedSidecarGeneration(bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);
            using(RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(repairPool, triplePool), null, null, Guid.Empty, clock, null, null, CancellationToken.None).ConfigureAwait(false))
            {
                Assert.IsTrue(repair.IsClean, "A damaged-sidecar repair re-derives cleanly.");
                GenerationCommitReport report = Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);
                Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome);
            }

            Assert.AreEqual(0, repairPool.OutstandingRentals, "Disposing the report after the commit must return every buffer the repair pass rented from the repair pool.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The healed generation re-lists every artifact: the re-derived sidecar verifies clean under a fresh generation-stamped name, while the undamaged system-of-record and sketch are carried forward under their prior names.</summary>
    [TestMethod]
    public async Task RederivesTheDamagedArtifactAndCarriesTheRestForwardUnchanged()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        FileSystemPersistenceStore store = StageDamagedSidecarGeneration(bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            ManifestEntry priorSegment = ReadEntry(store, ManifestFileRole.DataSegment);
            ManifestEntry priorSidecar = ReadEntry(store, ManifestFileRole.Sidecar);
            ManifestEntry priorSketch = ReadEntry(store, ManifestFileRole.Sketch);

            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);

            ManifestEntry healedSegment = ReadEntry(store, ManifestFileRole.DataSegment);
            ManifestEntry healedSidecar = ReadEntry(store, ManifestFileRole.Sidecar);
            ManifestEntry healedSketch = ReadEntry(store, ManifestFileRole.Sketch);

            //The undamaged artifacts are carried forward by name, never re-staged.
            Assert.AreEqual(priorSegment.FileName, healedSegment.FileName, "The undamaged system-of-record is carried forward unchanged.");
            Assert.AreEqual(priorSketch.FileName, healedSketch.FileName, "The undamaged sketch is carried forward unchanged.");

            //The re-derived sidecar is staged under a fresh name and verifies clean.
            Assert.AreNotEqual(priorSidecar.FileName, healedSidecar.FileName, "The re-derived sidecar is staged under a fresh name.");
            byte[] sidecarImage = store.Read(healedSidecar.FileName) ?? throw new InvalidOperationException("The healed sidecar is missing from the store.");
            Assert.IsTrue(ColumnarTripleIndex.RunVerifyRound(sidecarImage).ToArtifactReport().IsClean, "The re-derived sidecar verifies clean.");

            //The carried-forward survivors' bytes are still present and verify clean after the commit's collection pass.
            byte[] segmentImage = store.Read(healedSegment.FileName) ?? throw new InvalidOperationException("The carried-forward system-of-record is missing.");
            Assert.IsTrue(ItemSegment.RunVerifyRound(segmentImage).IsClean, "The carried-forward system-of-record still verifies clean.");
            byte[] sketchImage = store.Read(healedSketch.FileName) ?? throw new InvalidOperationException("The carried-forward sketch is missing.");
            Assert.IsTrue(SketchSegment.RunVerifyRound(sketchImage).IsClean, "The carried-forward sketch still verifies clean.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Committing the same repair report a second time, after it has already been healed, is superseded rather than re-healed: the live generation has moved past the report's, so nothing is published.</summary>
    [TestMethod]
    public async Task ASecondCommitOfAnAlreadyHealedReportIsSuperseded()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        FileSystemPersistenceStore store = StageDamagedSidecarGeneration(bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            GenerationCommitCoordinator coordinator = Coordinator(store, bytePool, clock, trace: null);

            GenerationCommitReport first = coordinator.Commit(repair, Guid.Empty);
            Assert.AreEqual(GenerationCommitOutcome.Committed, first.Outcome);

            GenerationCommitReport second = coordinator.Commit(repair, Guid.Empty);
            Assert.AreEqual(GenerationCommitOutcome.Superseded, second.Outcome, "A stale report is not re-healed.");
            Assert.AreEqual(Generation + 1, second.Generation, "The live generation stayed at the healed one.");
            Assert.AreEqual(Generation + 1, new ManifestRecovery(store).Recover().Manifest.CommitGeneration, "No further generation was published.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A clean generation repairs to nothing, so the coordinator commits no new generation.</summary>
    [TestMethod]
    public async Task ACleanReportCommitsNothing()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);

            GenerationCommitReport report = Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);

            Assert.AreEqual(GenerationCommitOutcome.NothingToCommit, report.Outcome, "A clean generation publishes nothing.");
            Assert.AreEqual(Generation, new ManifestRecovery(store).Recover().Manifest.CommitGeneration, "The live generation is unchanged.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A repair the system-of-record's framing damage forced to refuse publishes nothing, carrying the refusal reason through.</summary>
    [TestMethod]
    public async Task ARefusedRepairDoesNotPublish()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        ArtifactImage cleanSegment = SegmentImage(triples, bytePool);
        using ArtifactImage truncatedSegment = cleanSegment.Truncated(256, bytePool);
        cleanSegment.Dispose();
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        FileSystemPersistenceStore store = StageGeneration(Generation, truncatedSegment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            Assert.IsTrue(repair.Refused, "A truncated system-of-record forces the repair to refuse.");

            GenerationCommitReport report = Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);

            Assert.AreEqual(GenerationCommitOutcome.Refused, report.Outcome);
            Assert.AreEqual(RepairRefusalReason.SystemOfRecordUnreadable, report.Refusal, "The refusal reason is carried through.");
            Assert.AreEqual(Generation, new ManifestRecovery(store).Recover().Manifest.CommitGeneration, "The live generation is unchanged.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Publishing a healed generation emits the healed-generation lifecycle marker, carrying the healed generation and the count of republished artifacts.</summary>
    [TestMethod]
    public async Task EmitsTheHealedGenerationMarker()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        FileSystemPersistenceStore store = StageDamagedSidecarGeneration(bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            StorageTraceCapture trace = new();

            Coordinator(store, bytePool, clock, trace.Capture).Commit(repair, Guid.Empty);

            StorageTraceEvent marker = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.GenerationHealed);
            Assert.AreEqual(Generation + 1, marker.CommitGeneration, "The marker names the healed generation.");
            Assert.AreEqual(1L, marker.ItemCount, "The marker carries the count of republished artifacts.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A generation whose system-of-record loses a block while its sidecar is also damaged heals the re-derivable sidecar and publishes the generation, carrying the named system-of-record loss through on the committed path; the system-of-record itself is named lost, not re-derived.</summary>
    [TestMethod]
    public async Task HealsTheDerivedArtifactAndCarriesANamedSystemOfRecordLossThrough()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        CorruptSegmentBlock(segment, CorruptBlock, SegmentBlockCount);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            Assert.IsNotEmpty(repair.RederivedArtifacts, "The damaged sidecar is re-derived from the surviving items.");
            Assert.IsNotEmpty(repair.NamedLosses, "The corrupt system-of-record block is named lost.");

            GenerationCommitReport report = Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);

            Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome);
            Assert.Contains(ManifestFileRole.Sidecar, report.RepublishedRoles, "The re-derived sidecar is republished.");
            Assert.DoesNotContain(ManifestFileRole.DataSegment, report.RepublishedRoles, "The system-of-record is named lost, not re-derived.");
            Assert.IsNotEmpty(report.NamedLosses, "The named system-of-record loss is carried through on the committed path.");
            Assert.AreEqual(Generation + 1, new ManifestRecovery(store).Recover().Manifest.CommitGeneration, "The healed generation is live.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A heal whose repair named an unrecoverable system-of-record loss publishes a durable loss record co-versioned with the healed generation, so the loss survives a cold reopen exact in kind, role, artifact name, and item range rather than the generation looking pristine after restart.</summary>
    [TestMethod]
    public async Task ARepairThatNamesLossesPublishesADurableLossRecordReadableAfterReopen()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        CorruptSegmentBlock(segment, CorruptBlock, SegmentBlockCount);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            List<(UnrecoverableItemReportKind Kind, int RoleCode, string? Name, long Start, long Count)> expected = [];
            using(RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false))
            {
                Assert.IsNotEmpty(repair.NamedLosses, "The corrupt system-of-record block is named lost.");
                foreach(UnrecoverableItemReport loss in repair.NamedLosses)
                {
                    expected.Add((loss.Kind, loss.RoleCode, loss.ArtifactFileName, loss.LostItemStart, loss.LostItemCount));
                }

                GenerationCommitReport report = Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);
                Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome);
                Assert.IsNotEmpty(report.NamedLosses, "The committed heal carries the named losses.");
            }

            //Reopen the store cold — a fresh store over the same directory — so the assertion proves durability, not in-memory retention.
            DurableSystemOfRecordStore reopened = new(new FileSystemPersistenceStore(directory, NoOpBarrier), bytePool);
            DurableLossRecord? record = reopened.TryReadRecordedLosses();

            Assert.IsNotNull(record, "The healed generation's losses are durable across a cold reopen.");
            Assert.AreEqual(Generation + 1, record.Generation, "The record names the healed generation.");
            Assert.HasCount(expected.Count, record.Losses);
            for(int i = 0; i < expected.Count; i++)
            {
                DurableLossEntry entry = record.Losses[i];
                Assert.AreEqual(expected[i].Kind, entry.Kind, "The loss kind round-trips exactly.");
                Assert.AreEqual(expected[i].RoleCode, entry.RoleCode, "The loss role round-trips exactly.");
                Assert.AreEqual(expected[i].Name, entry.ArtifactFileName, "The loss artifact name round-trips exactly.");
                Assert.AreEqual(expected[i].Start, entry.StartItem, "The loss start item round-trips exactly.");
                Assert.AreEqual(expected[i].Count, entry.ItemCount, "The loss item count round-trips exactly.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A heal whose repair named no loss (a re-derivable sidecar rebuilt from an intact system-of-record) writes no loss record, so a clean heal reads back no recorded losses and is not mistaken for lossy — a present record is exactly the visibly-lossy signal.</summary>
    [TestMethod]
    public async Task ALossFreeHealWritesNoLossRecord()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        FileSystemPersistenceStore store = StageDamagedSidecarGeneration(bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false);
            Assert.IsTrue(repair.IsClean, "A damaged-sidecar-only repair names no losses.");

            Coordinator(store, bytePool, clock, trace: null).Commit(repair, Guid.Empty);

            Assert.IsEmpty(store.List(HealedArtifactNaming.LossRecordPrefix), "A loss-free heal stages no loss record.");
            DurableSystemOfRecordStore reopened = new(new FileSystemPersistenceStore(directory, NoOpBarrier), bytePool);
            Assert.IsNull(reopened.TryReadRecordedLosses(), "A loss-free healed generation reads back no recorded losses.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A coordinator built with a zero retained-pointer count is rejected at construction — the same lower bound its manifest writer enforces — rather than failing at the first heal after artifacts have already been staged.</summary>
    [TestMethod]
    public void RejectsAZeroRetainedPointerCountAtConstruction()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        string directory = Directory.CreateTempSubdirectory("veritas-coordinator-ctor-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => { _ = new GenerationCommitCoordinator(store, ChecksumAlgorithm.XxHash3, bytePool, 0, null, null, new FakeTimeProvider()); });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Stages a generation whose sidecar's front matter is corrupt — re-derivable from the clean system-of-record — while the system-of-record and sketch are intact.</summary>
    /// <param name="bytePool">The pool the images are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store holding the damaged generation.</returns>
    private static FileSystemPersistenceStore StageDamagedSidecarGeneration(MemoryPool<byte> bytePool, out string directory)
    {
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        CorruptSidecarFrontMatter(sidecar);

        return StageGeneration(Generation, segment, sidecar, sketch, bytePool, out directory);
    }

    /// <summary>Runs the verify and repair passes over the store's committed generation.</summary>
    /// <param name="store">The store holding the committed generation.</param>
    /// <param name="bytePool">The byte pool the repair rents from.</param>
    /// <param name="triplePool">The triple pool the repair feed rents from.</param>
    /// <param name="clock">The clock the passes timestamp with.</param>
    /// <returns>The repair pass report.</returns>
    private static async ValueTask<RepairPassReport> RunRepairAsync(FileSystemPersistenceStore store, MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool, TimeProvider clock)
    {
        ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);

        return await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, clock, null, null, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Builds a generation-commit coordinator over the store under the fixture's XxHash3 checksum.</summary>
    /// <param name="store">The store the coordinator commits into.</param>
    /// <param name="bytePool">The pool the coordinator rents from.</param>
    /// <param name="clock">The clock the healed-generation marker is timestamped with.</param>
    /// <param name="trace">The diagnostics sink, or <see langword="null"/>.</param>
    /// <returns>The coordinator.</returns>
    private static GenerationCommitCoordinator Coordinator(FileSystemPersistenceStore store, MemoryPool<byte> bytePool, TimeProvider clock, TraceHandler<StorageTraceEvent>? trace)
    {
        return new GenerationCommitCoordinator(store, ChecksumAlgorithm.XxHash3, bytePool, RetainedPointers, null, trace, clock);
    }

    /// <summary>Reads the live manifest's entry for a role.</summary>
    /// <param name="store">The store to recover the live manifest from.</param>
    /// <param name="role">The role whose entry is read.</param>
    /// <returns>The entry.</returns>
    private static ManifestEntry ReadEntry(FileSystemPersistenceStore store, ManifestFileRole role)
    {
        Manifest manifest = new ManifestRecovery(store).Recover().Manifest;
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role == role)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"The live manifest has no {role.Name} entry.");
    }
}
