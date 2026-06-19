using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The P3-g per-artifact slice of the storage self-heal combination matrix: a damage cell is a
/// <c>(site, kind, rung)</c> triple over a committed generation, and every cell the dispatch table admits is
/// driven end to end through the scrub seam (<see cref="ScrubRound.RunVerifyPass"/> /
/// <see cref="ScrubRound.RunRepairPass"/>), asserting three things — the at-rest verdict (a corrupt block
/// reported with its role, or a framing/foreign-epoch refusal), the repair outcome at the artifact class's
/// terminal rung (a re-derivable sidecar or sketch rebuilt clean; a system-of-record block named lost), and a
/// differential twin (the re-derived sidecar decodes to exactly the system-of-record triples; the sketch
/// re-derive folds every item; a named loss removes exactly its item range). Each cell's
/// <see cref="PersistenceInvariant"/> citations are fixed by construction (<see cref="CitedInvariants"/>):
/// Sidecar → I1+I3+I6; Sketch → I1+I2+I3+I6; system-of-record loss → I1+I7; a foreign epoch → I1+I5.
/// <para>
/// This complements the single-artifact detection sweep in <see cref="PersistenceFaultHarnessTests"/> (every
/// column blob × byte-damage → detect): the matrix's grain is artifact × damage class × repair outcome over a
/// staged generation, not per-blob detection. Cells the single-node gate does not admit (the unbuilt parity
/// tier, the replication-arc peer rung, and — in this slice — the manifest/CURRENT/journal recovery sites and
/// their publish-path damage kinds) are enumerated and skipped with a named reason, never silently dropped, so
/// the dispatch table is auditable; the rules that constrain those recovery sites and kinds (R3-R5) are carried
/// forward-complete and activate when those sites enter the active set.
/// </para>
/// </summary>
[TestClass]
internal sealed class PersistenceCombinationMatrixTests
{
    /// <summary>The generation's triple count: 3000 system-of-record items across 300 ten-item blocks.</summary>
    private const uint TripleCount = 3000;

    /// <summary>The system-of-record block geometry: ten triples per block, so a corrupt block names a ten-item range.</summary>
    private const int BlockItemCount = 10;

    /// <summary>The system-of-record block count for the generation (<see cref="TripleCount"/> / <see cref="BlockItemCount"/>).</summary>
    private const int SegmentBlockCount = 300;

    /// <summary>The staged sketch's symbol count: forty symbols across ten four-symbol blocks.</summary>
    private const int SketchSymbolCount = 40;

    /// <summary>The staged sketch's block count (<see cref="SketchSymbolCount"/> / four symbols per block).</summary>
    private const int SketchBlockCount = 10;

    /// <summary>The committed generation the cells stage and damage.</summary>
    private const long Generation = 7;

    /// <summary>The block damaged in every block-scoped cell — a middle block, so the named item range is a non-edge interval.</summary>
    private const int CorruptBlock = 1;

    /// <summary>The number of trailing bytes a truncation cell drops, enough that the declared geometry runs past the end of every artifact format at this scale.</summary>
    private const int TruncateDropBytes = 256;

    /// <summary>The size of the enumerated cross-product: the active sites × kinds × rungs.</summary>
    private const int ExpectedCellCount = 64;

    /// <summary>The pinned number of valid single-node cells in this slice, so adding or removing one is a deliberate change.</summary>
    private const int ExpectedValidCellCount = 12;

    /// <summary>The generation's triples, built once: every cell stages a fresh image trio over these.</summary>
    private static readonly EncodedTriple[] Triples = SampleTriples(TripleCount);

    /// <summary>The damage sites a cell can land on. The first three are the per-artifact read-path sites this slice drives; <see cref="Parity"/> is the unbuilt parity tier (always skipped); the recovery sites land their cells with the manifest/CURRENT/journal tiers.</summary>
    internal enum DamageSite
    {
        /// <summary>The re-derivable columnar sidecar.</summary>
        Sidecar,

        /// <summary>The durable system-of-record item segment.</summary>
        DataSegment,

        /// <summary>The re-derivable integrity sketch.</summary>
        Sketch,

        /// <summary>The optional local-parity prefix (the unbuilt P3-f tier).</summary>
        Parity,

        /// <summary>The generation manifest blob (a recovery site).</summary>
        Manifest,

        /// <summary>The CURRENT pointer (a recovery site).</summary>
        Current,

        /// <summary>A durable-journal record (a recovery site).</summary>
        JournalEntry,
    }

    /// <summary>The kinds of physical damage a cell can inject. The first four are the byte-level kinds this slice drives; the last four are the publish-path and recovery kinds the recovery sites carry.</summary>
    internal enum DamageKind
    {
        /// <summary>A single payload byte flipped.</summary>
        BitFlip,

        /// <summary>A whole block payload overwritten with garbage.</summary>
        WholeBlockGarbage,

        /// <summary>The image truncated so its declared geometry runs past the end.</summary>
        Truncate,

        /// <summary>A foreign checksum-algorithm epoch no resolver maps.</summary>
        StaleEpoch,

        /// <summary>A stored per-block checksum digest flipped (the digest, not the payload).</summary>
        ChecksumField,

        /// <summary>A publish torn before the staged generation's rename (an unpublished orphan).</summary>
        TornPublishPreRename,

        /// <summary>A publish torn before the CURRENT pointer advanced.</summary>
        TornPublishPreCurrent,

        /// <summary>A rotted CURRENT pointer.</summary>
        CurrentRot,
    }

    /// <summary>One single-node combination-matrix cell: a damage site, a damage kind, and the repair-ladder rung the cell is classified under (the terminal rung the ladder reaches for a valid cell).</summary>
    /// <param name="Site">Where the damage lands.</param>
    /// <param name="Kind">What the damage is.</param>
    /// <param name="Rung">The repair-ladder rung this cell is classified under.</param>
    internal readonly record struct MatrixCell(DamageSite Site, DamageKind Kind, RepairRung Rung);

    /// <summary>The damage sites this slice enumerates: the three read-path artifacts plus the always-skipped parity tier.</summary>
    private static readonly DamageSite[] ActiveSites = [DamageSite.Sidecar, DamageSite.DataSegment, DamageSite.Sketch, DamageSite.Parity];

    /// <summary>The damage kinds this slice enumerates: the byte-level kinds the read-path artifacts admit.</summary>
    private static readonly DamageKind[] ActiveKinds = [DamageKind.BitFlip, DamageKind.WholeBlockGarbage, DamageKind.Truncate, DamageKind.StaleEpoch];

    /// <summary>The repair-ladder rungs, the cross-product's third axis; all but the cell's terminal rung are skipped per artifact class.</summary>
    private static readonly RepairRung[] AllRungs = [RepairRung.RederiveLocally, RepairRung.LocalParity, RepairRung.PeerReconciliation, RepairRung.NamedLoss];

    /// <summary>Enumerates the full cross-product of the active sites, kinds, and rungs.</summary>
    /// <returns>Every cell, valid or skipped.</returns>
    private static IEnumerable<MatrixCell> Enumerate()
    {
        foreach(DamageSite site in ActiveSites)
        {
            foreach(DamageKind kind in ActiveKinds)
            {
                foreach(RepairRung rung in AllRungs)
                {
                    yield return new MatrixCell(site, kind, rung);
                }
            }
        }
    }

    /// <summary>The <see cref="DynamicDataAttribute"/> source: one <c>site/kind/rung</c> identity per enumerated cell, so the cell crosses the public test-method boundary as a string rather than an internal type and reads cleanly in the test name.</summary>
    /// <returns>The data rows.</returns>
    private static IEnumerable<object[]> CellRows()
    {
        foreach(MatrixCell cell in Enumerate())
        {
            yield return [$"{cell.Site}/{cell.Kind}/{cell.Rung}"];
        }
    }

    /// <summary>Parses a cell identity (<c>site/kind/rung</c>) the data source emits back into a cell.</summary>
    /// <param name="cellId">The cell identity.</param>
    /// <returns>The cell.</returns>
    private static MatrixCell ParseCell(string cellId)
    {
        string[] parts = cellId.Split('/');

        return new MatrixCell(Enum.Parse<DamageSite>(parts[0]), Enum.Parse<DamageKind>(parts[1]), Enum.Parse<RepairRung>(parts[2]));
    }

    /// <summary>The repair-ladder rung the artifact class at <paramref name="site"/> terminates at: a re-derivable artifact re-derives locally; the system-of-record is not re-derivable, so its corruption terminates at a named loss.</summary>
    /// <param name="site">The read-path damage site.</param>
    /// <returns>The terminal rung.</returns>
    private static RepairRung TerminalRung(DamageSite site) => site switch
    {
        DamageSite.Sidecar => RepairRung.RederiveLocally,
        DamageSite.Sketch => RepairRung.RederiveLocally,
        DamageSite.DataSegment => RepairRung.NamedLoss,
        _ => throw new InvalidOperationException($"Site {site} has no read-path terminal rung; recovery sites are validated by their own recovery cells."),
    };

    /// <summary>Whether the kind is a publish-path kind, which damages only the manifest/CURRENT publish path.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <returns><see langword="true"/> for a torn-publish or CURRENT-rot kind.</returns>
    private static bool IsPublishKind(DamageKind kind) => kind is DamageKind.TornPublishPreRename or DamageKind.TornPublishPreCurrent or DamageKind.CurrentRot;

    /// <summary>Whether the site is on the segment read path, where the foreign-epoch refusal lives.</summary>
    /// <param name="site">The damage site.</param>
    /// <returns><see langword="true"/> for the sidecar, system-of-record, or sketch.</returns>
    private static bool IsSegmentReadPathSite(DamageSite site) => site is DamageSite.Sidecar or DamageSite.DataSegment or DamageSite.Sketch;

    /// <summary>Whether the kind is one a journal tail recovers (a torn or garbage tail), as opposed to a publish-path or checksum-field kind.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <returns><see langword="true"/> for the byte-level tail kinds.</returns>
    private static bool IsJournalTailKind(DamageKind kind) => kind is DamageKind.BitFlip or DamageKind.WholeBlockGarbage or DamageKind.Truncate or DamageKind.StaleEpoch;

    /// <summary>Classifies a cell: whether it is a valid single-node cell this slice drives, or — when not — a named reason it is enumerated-but-skipped. The dispatch table (R1-R6) is forward-complete; R3-R5 constrain the recovery sites and publish-path kinds that enter the active set with the manifest/CURRENT/journal tiers.</summary>
    /// <param name="cell">The cell to classify.</param>
    /// <returns>Whether the cell is valid, and the skip reason when it is not.</returns>
    private static (bool Valid, string Reason) Classify(MatrixCell cell)
    {
        //R1 — the local-parity tier is not built (P3-f); no parity image exists and the ladder declines LocalParity.
        if(cell.Site == DamageSite.Parity)
        {
            return (false, "R1 parity: the local-parity tier is not built (P3-f); the ladder declines LocalParity.");
        }

        //R2 — peer recovery belongs to the replication arc, not this single-node gate.
        if(cell.Rung == RepairRung.PeerReconciliation)
        {
            return (false, "R2 peer: peer reconciliation belongs to the replication arc, not the single-node gate.");
        }

        //R3 — torn-publish and CURRENT-rot damage only the manifest/CURRENT publish path.
        if(IsPublishKind(cell.Kind) && cell.Site != DamageSite.Manifest && cell.Site != DamageSite.Current)
        {
            return (false, "R3 publish-path: torn-publish / CURRENT-rot kinds apply only to the manifest/CURRENT publish path.");
        }

        //R4 — the foreign checksum-epoch refusal (I5) lives on the segment read path.
        if(cell.Kind == DamageKind.StaleEpoch && !IsSegmentReadPathSite(cell.Site))
        {
            return (false, "R4 epoch: the foreign checksum-algorithm refusal (I5) lives on the segment read path.");
        }

        //R5 — the journal recovers torn or garbage tails; publish and checksum-field kinds are not applicable to it.
        if(cell.Site == DamageSite.JournalEntry && !IsJournalTailKind(cell.Kind))
        {
            return (false, "R5 journal: the journal recovers torn/garbage tails; publish and checksum-field kinds are not applicable.");
        }

        //R6 — a rung applies only to the artifact class whose ladder terminates at it.
        RepairRung terminal = TerminalRung(cell.Site);
        if(cell.Rung != terminal)
        {
            return (false, $"R6 rung: {cell.Rung} does not apply to {cell.Site}; the ladder terminates at {terminal}.");
        }

        return (true, string.Empty);
    }

    /// <summary>The persistence invariants a valid cell asserts, fixed by its site and kind. A foreign epoch is a detection-time refusal (I1 + I5); otherwise the re-derivable sidecar/sketch cells assert faithful, ordinary-ingest repair (I1/I2/I3/I6) and the system-of-record cells assert a named loss (I1 + I7).</summary>
    /// <param name="cell">The valid cell.</param>
    /// <returns>The cited invariants.</returns>
    private static PersistenceInvariant[] CitedInvariants(MatrixCell cell)
    {
        if(cell.Kind == DamageKind.StaleEpoch)
        {
            return [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.EpochConsistency];
        }

        return cell.Site switch
        {
            DamageSite.Sidecar => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.RepairIsFaithful, PersistenceInvariant.RepairIsOrdinaryIngest],
            DamageSite.Sketch => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.DetectionPrecedesXor, PersistenceInvariant.RepairIsFaithful, PersistenceInvariant.RepairIsOrdinaryIngest],
            DamageSite.DataSegment => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.LossIsNamed],
            _ => [],
        };
    }

    /// <summary>Every cell classifies to valid-or-named-skip, the cross-product is the pinned size, the valid count is pinned, and every valid cell cites at least one invariant — so the dispatch table cannot silently change or drop a cell.</summary>
    [TestMethod]
    public void DispatchTablePartitionsEveryCellWithAReasonAndPinsTheValidCount()
    {
        int total = 0;
        int valid = 0;
        foreach(MatrixCell cell in Enumerate())
        {
            total++;
            (bool isValid, string reason) = Classify(cell);
            if(isValid)
            {
                valid++;
                Assert.IsTrue(string.IsNullOrEmpty(reason), $"A valid cell carries no skip reason: {cell}.");
                Assert.IsNotEmpty(CitedInvariants(cell), $"A valid cell must cite at least one invariant: {cell}.");
            }
            else
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(reason), $"A skipped cell must name why it is invalid: {cell}.");
            }
        }

        Assert.AreEqual(ExpectedCellCount, total, "The enumerated cross-product is the active sites × kinds × rungs.");
        Assert.AreEqual(ExpectedValidCellCount, valid, "The valid single-node cell count is pinned so adding or removing a cell is a deliberate change.");
    }

    /// <summary>Drives one matrix cell: a valid cell is staged, damaged, and asserted end to end; a skipped cell is enumerated with its named reason rather than silently dropped.</summary>
    /// <param name="cellId">The <c>site/kind/rung</c> identity of the cell to run.</param>
    [TestMethod]
    [DynamicData(nameof(CellRows))]
    public void Cell(string cellId)
    {
        MatrixCell cell = ParseCell(cellId);
        (bool valid, string reason) = Classify(cell);
        if(!valid)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason), $"A skipped cell must name why it is invalid: {cell}.");

            return;
        }

        Drive(cell);
    }

    /// <summary>Stages a generation with the cell's targeted artifact damaged, then asserts the verdict, the repair outcome, and the differential twin.</summary>
    /// <param name="cell">The valid cell to drive.</param>
    private static void Drive(MatrixCell cell)
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = cell.Site == DamageSite.DataSegment ? BuildDamagedSegment(cell.Kind, bytePool) : SegmentImage(Triples, bytePool);
        using ArtifactImage sidecar = cell.Site == DamageSite.Sidecar ? BuildDamagedSidecar(cell.Kind, bytePool) : SidecarImage(Triples, bytePool);
        using ArtifactImage sketch = cell.Site == DamageSite.Sketch ? BuildDamagedSketch(cell.Kind, bytePool) : SketchImage(SketchSymbolCount, bytePool);
        ArtifactImage damaged = cell.Site switch
        {
            DamageSite.Sidecar => sidecar,
            DamageSite.DataSegment => segment,
            DamageSite.Sketch => sketch,
            _ => throw new InvalidOperationException($"Site {cell.Site} is not a per-artifact damage site."),
        };

        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();

            //A foreign checksum epoch halts the whole verify pass (I5) before any block is gated; no repair runs.
            if(cell.Kind == DamageKind.StaleEpoch)
            {
                Assert.ThrowsExactly<NotSupportedException>(() => { _ = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock); });

                return;
            }

            //Truncation is framing damage the decode-free artifact verify refuses outright.
            if(cell.Kind == DamageKind.Truncate)
            {
                AssertArtifactRefusesTruncation(cell.Site, damaged);
            }

            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);
            Assert.IsFalse(verify.IsClean, "The damaged generation must not scrub clean.");
            AssertDetectionVerdict(cell, verify);

            StorageTraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, clock);
            AssertRepairOutcome(cell, repair, trace, bytePool, triplePool);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Builds a clean system-of-record image and applies the cell's damage kind to it.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The damaged image (the caller disposes it).</returns>
    private static ArtifactImage BuildDamagedSegment(DamageKind kind, MemoryPool<byte> pool)
    {
        ArtifactImage clean = SegmentImage(Triples, pool);
        switch(kind)
        {
            case DamageKind.BitFlip:
            {
                CorruptSegmentBlock(clean, CorruptBlock, SegmentBlockCount);

                return clean;
            }
            case DamageKind.WholeBlockGarbage:
            {
                GarbageSegmentBlock(clean, CorruptBlock, SegmentBlockCount);

                return clean;
            }
            case DamageKind.StaleEpoch:
            {
                SetForeignChecksumAlgorithmId(clean);

                return clean;
            }
            case DamageKind.Truncate:
            {
                try
                {
                    return clean.Truncated(TruncateDropBytes, pool);
                }
                finally
                {
                    clean.Dispose();
                }
            }
            default:
            {
                clean.Dispose();

                throw new InvalidOperationException($"Kind {kind} is not a driven per-artifact damage.");
            }
        }
    }

    /// <summary>Builds a clean sidecar image and applies the cell's damage kind to it.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The damaged image (the caller disposes it).</returns>
    private static ArtifactImage BuildDamagedSidecar(DamageKind kind, MemoryPool<byte> pool)
    {
        ArtifactImage clean = SidecarImage(Triples, pool);
        switch(kind)
        {
            case DamageKind.BitFlip:
            {
                CorruptSidecarFrontMatter(clean);

                return clean;
            }
            case DamageKind.WholeBlockGarbage:
            {
                GarbageSidecarBlob(clean);

                return clean;
            }
            case DamageKind.StaleEpoch:
            {
                SetForeignChecksumAlgorithmId(clean);

                return clean;
            }
            case DamageKind.Truncate:
            {
                try
                {
                    return clean.Truncated(TruncateDropBytes, pool);
                }
                finally
                {
                    clean.Dispose();
                }
            }
            default:
            {
                clean.Dispose();

                throw new InvalidOperationException($"Kind {kind} is not a driven per-artifact damage.");
            }
        }
    }

    /// <summary>Builds a clean sketch image and applies the cell's damage kind to it.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The damaged image (the caller disposes it).</returns>
    private static ArtifactImage BuildDamagedSketch(DamageKind kind, MemoryPool<byte> pool)
    {
        ArtifactImage clean = SketchImage(SketchSymbolCount, pool);
        switch(kind)
        {
            case DamageKind.BitFlip:
            {
                CorruptSketchBlock(clean, CorruptBlock, SketchBlockCount);

                return clean;
            }
            case DamageKind.WholeBlockGarbage:
            {
                GarbageSketchBlock(clean, CorruptBlock, SketchBlockCount);

                return clean;
            }
            case DamageKind.StaleEpoch:
            {
                SetForeignChecksumAlgorithmId(clean);

                return clean;
            }
            case DamageKind.Truncate:
            {
                try
                {
                    return clean.Truncated(TruncateDropBytes, pool);
                }
                finally
                {
                    clean.Dispose();
                }
            }
            default:
            {
                clean.Dispose();

                throw new InvalidOperationException($"Kind {kind} is not a driven per-artifact damage.");
            }
        }
    }

    /// <summary>Asserts the decode-free artifact verify refuses a truncated image outright at the format seam (the refusal verdict precedes any repair).</summary>
    /// <param name="site">The truncated artifact's site.</param>
    /// <param name="damaged">The truncated image.</param>
    private static void AssertArtifactRefusesTruncation(DamageSite site, ArtifactImage damaged)
    {
        switch(site)
        {
            case DamageSite.Sidecar:
            {
                Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.RunVerifyRound(damaged.Bytes); });

                break;
            }
            case DamageSite.DataSegment:
            {
                Assert.ThrowsExactly<InvalidDataException>(() => { _ = ItemSegment.RunVerifyRound(damaged.Bytes); });

                break;
            }
            case DamageSite.Sketch:
            {
                Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchSegment.RunVerifyRound(damaged.Bytes); });

                break;
            }
            default:
            {
                throw new InvalidOperationException($"Truncation has no artifact-level verify for site {site}.");
            }
        }
    }

    /// <summary>Asserts the verify pass named the damaged artifact corrupt: a system-of-record block is named with its index (or a truncated system-of-record is a whole-artifact loss); a sidecar or sketch is reported corrupt by role.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="verify">The verify report.</param>
    private static void AssertDetectionVerdict(MatrixCell cell, ScrubRoundReport verify)
    {
        switch(cell.Site)
        {
            case DamageSite.DataSegment:
            {
                ScrubBlockFinding finding = verify.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
                if(cell.Kind == DamageKind.Truncate)
                {
                    Assert.IsTrue(finding.IsFrontMatter, "A truncated system-of-record is a whole-artifact loss.");
                    Assert.AreEqual(-1, finding.BlockIndex);
                }
                else
                {
                    Assert.IsFalse(finding.IsFrontMatter);
                    Assert.AreEqual(CorruptBlock, finding.BlockIndex, "The corrupt system-of-record block is named with its index.");
                }

                break;
            }
            case DamageSite.Sidecar:
            {
                Assert.ContainsSingle(verify.CorruptBlocks.Where(static f => f.RoleCode == ManifestFileRole.Sidecar.Code));

                break;
            }
            case DamageSite.Sketch:
            {
                Assert.ContainsSingle(verify.CorruptBlocks.Where(static f => f.RoleCode == ManifestFileRole.Sketch.Code));

                break;
            }
            default:
            {
                throw new InvalidOperationException($"No detection verdict for site {cell.Site}.");
            }
        }
    }

    /// <summary>Asserts the repair outcome at the cell's terminal rung: a sidecar or sketch re-derives clean (with its differential twin); a corrupt system-of-record block is named lost (with its range); a truncated system-of-record is refused as unreadable.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="repair">The repair report.</param>
    /// <param name="trace">The captured repair trace.</param>
    /// <param name="bytePool">The byte pool the sidecar decode rents from.</param>
    /// <param name="triplePool">The triple pool the sidecar decode rents from.</param>
    private static void AssertRepairOutcome(MatrixCell cell, RepairPassReport repair, StorageTraceCapture trace, MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
    {
        switch(cell.Site)
        {
            case DamageSite.Sidecar:
            {
                AssertSidecarRederived(repair, bytePool, triplePool);

                break;
            }
            case DamageSite.Sketch:
            {
                AssertSketchRederived(repair, trace);

                break;
            }
            case DamageSite.DataSegment:
            {
                if(cell.Kind == DamageKind.Truncate)
                {
                    Assert.IsTrue(repair.Refused, "A truncated system-of-record cannot be re-derived from.");
                    Assert.AreEqual(RepairRefusalReason.SystemOfRecordUnreadable, repair.Refusal);
                    Assert.IsEmpty(repair.RederivedArtifacts);
                    Assert.IsEmpty(repair.NamedLosses);
                }
                else
                {
                    AssertNamedLoss(repair);
                }

                break;
            }
            default:
            {
                throw new InvalidOperationException($"No repair outcome for site {cell.Site}.");
            }
        }
    }

    /// <summary>Asserts the sidecar re-derived clean and decodes to exactly the system-of-record triples (the differential twin, I3 faithful + I6 ordinary ingest).</summary>
    /// <param name="repair">The repair report.</param>
    /// <param name="bytePool">The byte pool the decode rents from.</param>
    /// <param name="triplePool">The triple pool the decode rents from.</param>
    private static void AssertSidecarRederived(RepairPassReport repair, MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
    {
        Assert.IsFalse(repair.Refused);
        Assert.IsTrue(repair.IsClean, "A re-derivable sidecar corruption is fully recoverable.");
        RederivedArtifact artifact = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sidecar);
        Assert.IsTrue(ColumnarTripleIndex.RunVerifyRound(artifact.Image.Span).ToArtifactReport().IsClean, "The re-derived sidecar must verify clean.");
        ColumnarTripleIndex rebuilt = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(artifact.Image), bytePool, triplePool);
        bool faithful = new HashSet<EncodedTriple>(rebuilt.EnumerateTriples()).SetEquals(Triples);
        Assert.IsTrue(faithful, "The re-derived sidecar must contain exactly the system-of-record triples — the differential twin (I3).");
        Assert.IsEmpty(repair.NamedLosses);
    }

    /// <summary>Asserts the sketch re-derived clean and folded every verified system-of-record item (the differential twin for an opaque sketch, I3 + I6).</summary>
    /// <param name="repair">The repair report.</param>
    /// <param name="trace">The captured repair trace.</param>
    private static void AssertSketchRederived(RepairPassReport repair, StorageTraceCapture trace)
    {
        Assert.IsFalse(repair.Refused);
        Assert.IsTrue(repair.IsClean);
        RederivedArtifact artifact = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sketch);
        Assert.IsTrue(SketchSegment.RunVerifyRound(artifact.Image.Span).IsClean, "The re-derived sketch must verify clean.");
        StorageTraceEvent rederived = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Rederived);
        Assert.AreEqual((long)TripleCount, rederived.ItemCount, "The sketch re-derive folds every verified system-of-record item — the differential twin.");
        Assert.IsEmpty(repair.NamedLosses);
    }

    /// <summary>Asserts the corrupt system-of-record block is named lost exactly — its generation, its contiguous ten-item range, and that the range lies within the fixture (I1 + I7). A system-of-record loss re-derives no artifact, so the named range read back from the repair report is itself the differential against the pre-damage fixture; there is no rebuilt image to decode, unlike the sidecar and sketch twins.</summary>
    /// <param name="repair">The repair report.</param>
    private static void AssertNamedLoss(RepairPassReport repair)
    {
        Assert.IsFalse(repair.Refused);
        Assert.IsFalse(repair.IsClean, "A named system-of-record loss is not a clean outcome.");
        Assert.IsEmpty(repair.RederivedArtifacts);
        UnrecoverableItemReport loss = repair.NamedLosses.Single();
        Assert.AreEqual(UnrecoverableItemReportKind.ItemSet, loss.Kind);
        Assert.AreEqual(Generation, loss.CommitGeneration);
        Assert.AreEqual(CorruptBlock * BlockItemCount, loss.LostItemStart);
        Assert.AreEqual(BlockItemCount, loss.LostItemCount);
        //The named range — read from the repair report — names a real, in-bounds slice of the fixture it removes.
        Assert.IsTrue(loss.LostItemStart >= 0 && loss.LostItemStart + loss.LostItemCount <= Triples.Length, "The named loss range must lie within the fixture's item range.");
    }
}
