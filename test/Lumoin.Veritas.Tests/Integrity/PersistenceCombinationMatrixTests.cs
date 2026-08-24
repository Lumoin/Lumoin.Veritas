using System;
using System.Buffers;
using System.Buffers.Binary;
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
using Lumoin.Veritas.Core.Persistence.Journal;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Lumoin.Veritas.Tests.MemoryPool;
using Lumoin.Veritas.Tests.Persistence;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The P3-g slice of the storage self-heal combination matrix: a damage cell is a <c>(site, kind, rung)</c>
/// triple over a committed generation, and every cell the dispatch table admits is driven end to end through
/// the seam its artifact class recovers on. A per-artifact read-path cell runs the scrub seam
/// (<see cref="ScrubRound.RunVerifyPass"/> / <see cref="ScrubRound.RunRepairPassAsync"/>), asserting three things —
/// the at-rest verdict (a corrupt block reported with its role, or a framing/foreign-epoch refusal), the repair
/// outcome at the artifact class's terminal rung (a re-derivable sidecar or sketch rebuilt clean; a
/// system-of-record block payload restored from the co-versioned parity, or a tamper that untrusts its geometry
/// refused), and a differential twin (the re-derived sidecar decodes to exactly the system-of-record triples;
/// the sketch re-derive folds every item; the parity-restored segment decodes to exactly the original triples).
/// A manifest or CURRENT-pointer cell runs <see cref="ManifestRecovery"/>, and a journal cell runs
/// <see cref="FileBackedJournal"/> replay; both assert how recovery follows a surviving copy or truncates a
/// torn tail and names the loss. Each cell's <see cref="PersistenceInvariant"/> citations are fixed by
/// construction (<see cref="CitedInvariants"/>): Sidecar → I1+I3+I6; Sketch → I1+I2+I3+I6; system-of-record
/// peel → I1+I3; manifest/CURRENT recovery → I1+I4; journal tail loss → I1+I7; a foreign epoch → I1+I5.
/// <para>
/// This complements the single-artifact detection sweep in <see cref="PersistenceFaultHarnessTests"/> (every
/// column blob × byte-damage → detect): the matrix's grain is artifact × damage class × repair outcome over a
/// staged generation, not per-blob detection. The cells the single-node gate does not admit — the parity tier's
/// own re-derive (a later increment), the replication-arc peer rung, and the damage kinds a given site does not exercise — are
/// enumerated and skipped with a named reason, never silently dropped, so the dispatch table is auditable; the
/// rules that partition every cell (R1-R6) are forward-complete, so a rule constraining a site not yet in the
/// active set carries no live cell until that site activates.
/// </para>
/// </summary>
[TestClass]
internal sealed class PersistenceCombinationMatrixTests
{
    /// <summary>The generation's triple count: 3000 system-of-record items across 300 ten-item blocks.</summary>
    private const uint TripleCount = 3000;

    /// <summary>The system-of-record block geometry: ten triples per block, so a corrupt block is a ten-item recovery unit.</summary>
    private const int BlockItemCount = 10;

    /// <summary>The system-of-record block count for the generation (<see cref="TripleCount"/> / <see cref="BlockItemCount"/>).</summary>
    private const int SegmentBlockCount = (int)TripleCount / BlockItemCount;

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

    /// <summary>The first committed generation a recovery cell stages; recovery follows the CURRENT pointer to it.</summary>
    private const long RecoveryFirstGeneration = 1;

    /// <summary>The second generation a recovery cell stages or attempts; a torn publish of it leaves the first in force, and its at-rest manifest rot is recovered past.</summary>
    private const long RecoverySecondGeneration = 2;

    /// <summary>The triple count a recovery generation stages — small, since recovery reads the manifest and CURRENT pointer, never the artifacts.</summary>
    private const uint RecoveryTripleCount = 30;

    /// <summary>The sketch symbol count a recovery generation stages.</summary>
    private const int RecoverySketchSymbolCount = 10;

    /// <summary>The number of records a journal recovery cell appends to its durable log before injecting tail damage.</summary>
    private const int JournalRecordCount = 3;

    /// <summary>The number of trailing bytes a journal truncation cell drops — enough to cut into the last record's framed length without reaching the prior record.</summary>
    private const int JournalTruncateDropBytes = 4;

    /// <summary>The byte offset of a journal record's payload-format version, immediately past the four-byte length prefix.</summary>
    private const int JournalPayloadVersionOffset = sizeof(uint);

    /// <summary>A payload-format version past every supported version, refused at detection rather than recovered.</summary>
    private const byte ForeignJournalPayloadVersion = 99;

    /// <summary>The size of the enumerated cross-product: the active sites × kinds × rungs (7 × 8 × 4).</summary>
    private const int ExpectedCellCount = 224;

    /// <summary>The pinned number of valid cells in this slice (12 per-artifact + 6 manifest/CURRENT recovery + 4 journal recovery + 2 checksum-field), so adding or removing one is a deliberate change.</summary>
    private const int ExpectedValidCellCount = 24;

    /// <summary>The generation's triples, built once: every cell stages a fresh image trio over these.</summary>
    private static readonly EncodedTriple[] Triples = SampleTriples(TripleCount);

    /// <summary>The garbage bytes a journal whole-block-garbage cell appends past its intact records; replay discards exactly these.</summary>
    private static readonly byte[] JournalGarbageTail = [1, 2, 3, 4, 5];

    /// <summary>The damage sites a cell can land on. The first three are the per-artifact read-path sites this slice drives; <see cref="Parity"/> is the local-parity sidecar, whose own re-derive cell is a later increment (so it is skipped here, while the restore it enables is driven through the system-of-record's LocalParity cells); the recovery sites land their cells with the manifest/CURRENT/journal tiers.</summary>
    internal enum DamageSite
    {
        /// <summary>The re-derivable columnar sidecar.</summary>
        Sidecar,

        /// <summary>The durable system-of-record item segment.</summary>
        DataSegment,

        /// <summary>The re-derivable integrity sketch.</summary>
        Sketch,

        /// <summary>The optional local-parity sidecar.</summary>
        Parity,

        /// <summary>The generation manifest blob (a recovery site).</summary>
        Manifest,

        /// <summary>The CURRENT pointer (a recovery site).</summary>
        Current,

        /// <summary>A durable-journal record (a recovery site).</summary>
        JournalEntry,
    }

    /// <summary>The kinds of physical damage a cell can inject. The first four are the read-path byte-level kinds this slice drives, ChecksumField tampers a stored per-block digest, and the last three are the publish-path and recovery kinds the recovery sites carry.</summary>
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

    /// <summary>The damage sites this slice enumerates: the three read-path artifacts, the always-skipped parity tier, and the manifest, CURRENT-pointer, and journal recovery sites.</summary>
    private static readonly DamageSite[] ActiveSites = [DamageSite.Sidecar, DamageSite.DataSegment, DamageSite.Sketch, DamageSite.Parity, DamageSite.Manifest, DamageSite.Current, DamageSite.JournalEntry];

    /// <summary>The damage kinds this slice enumerates: the read-path byte-level kinds, the stored-digest checksum-field kind, and the manifest/CURRENT publish-path kinds.</summary>
    private static readonly DamageKind[] ActiveKinds = [DamageKind.BitFlip, DamageKind.WholeBlockGarbage, DamageKind.Truncate, DamageKind.StaleEpoch, DamageKind.ChecksumField, DamageKind.TornPublishPreRename, DamageKind.TornPublishPreCurrent, DamageKind.CurrentRot];

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

    /// <summary>The repair-ladder rung the artifact class at <paramref name="site"/> terminates at for damage of <paramref name="kind"/>: a re-derivable artifact re-derives locally; a system-of-record block whose payload is lost (the front matter intact) is restored from the co-versioned parity at the local-parity rung, while a tamper that leaves its geometry untrusted, and the durable journal, terminate at a named loss.</summary>
    /// <param name="site">The site whose ladder terminal rung is taken.</param>
    /// <param name="kind">The damage kind, which selects the system-of-record's rung between the parity-restorable payload loss and the un-restorable geometry tamper.</param>
    /// <returns>The terminal rung.</returns>
    private static RepairRung TerminalRung(DamageSite site, DamageKind kind) => site switch
    {
        DamageSite.Sidecar => RepairRung.RederiveLocally,
        DamageSite.Sketch => RepairRung.RederiveLocally,
        DamageSite.DataSegment => DataSegmentTerminalRung(kind),
        DamageSite.JournalEntry => RepairRung.NamedLoss,
        _ => throw new InvalidOperationException($"Site {site} has no scrub or journal terminal rung; the parity and manifest/CURRENT sites are classified on their own paths."),
    };

    /// <summary>The rung a corrupt system-of-record terminates at for a damage kind: a lost block payload (a bit flip or whole-block garbage, the front matter intact) is restored from the co-versioned parity at the local-parity rung; a tampered stored digest or truncated framing leaves the geometry untrusted and the foreign epoch is refused at detection, all filed at the named-loss terminal rung the system-of-record's ladder otherwise reaches.</summary>
    /// <param name="kind">The system-of-record damage kind.</param>
    /// <returns>The terminal rung.</returns>
    private static RepairRung DataSegmentTerminalRung(DamageKind kind) => kind switch
    {
        DamageKind.BitFlip or DamageKind.WholeBlockGarbage => RepairRung.LocalParity,
        _ => RepairRung.NamedLoss,
    };

    /// <summary>Whether the kind is a publish-path kind, which damages only the manifest/CURRENT publish path.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <returns><see langword="true"/> for a torn-publish or CURRENT-rot kind.</returns>
    private static bool IsPublishKind(DamageKind kind) => kind is DamageKind.TornPublishPreRename or DamageKind.TornPublishPreCurrent or DamageKind.CurrentRot;

    /// <summary>Whether the site is on the segment read path, where the foreign-epoch refusal lives.</summary>
    /// <param name="site">The damage site.</param>
    /// <returns><see langword="true"/> for the sidecar, system-of-record, or sketch.</returns>
    private static bool IsSegmentReadPathSite(DamageSite site) => site is DamageSite.Sidecar or DamageSite.DataSegment or DamageSite.Sketch;

    /// <summary>Whether the site refuses an unsupported epoch at detection time (I5): the segment read path refuses a foreign checksum-algorithm id, and the journal refuses an unsupported payload-format version.</summary>
    /// <param name="site">The damage site.</param>
    /// <returns><see langword="true"/> for the sidecar, system-of-record, sketch, or journal.</returns>
    private static bool RefusesUnsupportedEpoch(DamageSite site) => IsSegmentReadPathSite(site) || site == DamageSite.JournalEntry;

    /// <summary>Whether the damage kind is one the journal site exercises: a torn or garbage tail it recovers and truncates (I7), or the unsupported payload-version epoch it refuses at detection (I5) — as opposed to a publish-path or checksum-field kind, which do not apply to the journal.</summary>
    /// <param name="kind">The damage kind.</param>
    /// <returns><see langword="true"/> for the byte-level tail kinds and the foreign-epoch kind.</returns>
    private static bool IsJournalApplicableKind(DamageKind kind) => kind is DamageKind.BitFlip or DamageKind.WholeBlockGarbage or DamageKind.Truncate or DamageKind.StaleEpoch;

    /// <summary>Classifies a cell: whether it is a valid single-node cell this slice drives, or — when not — a named reason it is enumerated-but-skipped. The dispatch table (R1-R6) is forward-complete; R3-R5 constrain the recovery sites and publish-path kinds that enter the active set with the manifest/CURRENT/journal tiers.</summary>
    /// <param name="cell">The cell to classify.</param>
    /// <returns>Whether the cell is valid, and the skip reason when it is not.</returns>
    private static (bool Valid, string Reason) Classify(MatrixCell cell)
    {
        //R1 — driving a DAMAGED parity (its own re-derive) is a later increment; the local-parity restore the
        //parity enables is exercised here by the system-of-record's payload-loss cells at the LocalParity rung.
        if(cell.Site == DamageSite.Parity)
        {
            return (false, "R1 parity: the damaged-parity re-derive cell is a later increment; the parity's restore capability is exercised by the system-of-record LocalParity cells.");
        }

        //R2 — peer recovery belongs to the replication arc, not this single-node gate.
        if(cell.Rung == RepairRung.PeerReconciliation)
        {
            return (false, "R2 peer: peer reconciliation belongs to the replication arc, not the single-node gate.");
        }

        //The manifest/CURRENT recovery sites are validated by ManifestRecovery, not the repair-ladder scrub, so
        //their kind applicability and nominal rung are classified on their own path.
        if(cell.Site == DamageSite.Manifest || cell.Site == DamageSite.Current)
        {
            return ClassifyRecoverySite(cell);
        }

        //R3 — torn-publish and CURRENT-rot are publish-path kinds; on a read-path artifact they do not apply.
        if(IsPublishKind(cell.Kind))
        {
            return (false, "R3 publish-path: torn-publish / CURRENT-rot kinds apply only to the manifest/CURRENT publish path.");
        }

        //R4 — the unsupported-epoch refusal (I5) lives where a site verifies an epoch: the segment read path (a foreign checksum-algorithm id) and the journal (an unsupported payload-format version).
        if(cell.Kind == DamageKind.StaleEpoch && !RefusesUnsupportedEpoch(cell.Site))
        {
            return (false, "R4 epoch: the unsupported-epoch refusal (I5) lives where a site verifies an epoch — the segment read path and the journal.");
        }

        //R5 — the journal exercises torn/garbage tails (recovered) and the foreign-epoch refusal; publish and checksum-field kinds are not applicable to it.
        if(cell.Site == DamageSite.JournalEntry && !IsJournalApplicableKind(cell.Kind))
        {
            return (false, "R5 journal: the journal exercises torn/garbage tails and the foreign-epoch refusal; publish and checksum-field kinds are not applicable.");
        }

        //R7 — the checksum-field digest seam is driven on the segment formats whose per-block digest section the fixture can target; the sidecar's per-blob digest location is not exposed by the verify report, so it is represented by the system-of-record and sketch checksum-field cells.
        if(cell.Kind == DamageKind.ChecksumField && cell.Site == DamageSite.Sidecar)
        {
            return (false, "R7 checksum-field: the sidecar's per-blob digest location is not exposed by the verify report; the digest-side detector is represented by the system-of-record and sketch checksum-field cells.");
        }

        //R6 — a rung applies only to the artifact class (and, for the system-of-record, the damage kind) whose ladder terminates at it.
        RepairRung terminal = TerminalRung(cell.Site, cell.Kind);
        if(cell.Rung != terminal)
        {
            return (false, $"R6 rung: {cell.Rung} does not apply to {cell.Site}/{cell.Kind}; the ladder terminates at {terminal}.");
        }

        return (true, string.Empty);
    }

    /// <summary>Classifies a manifest/CURRENT recovery cell: recovery recovers from a local surviving copy (the nominal <see cref="RepairRung.RederiveLocally"/> rung), and each site admits only the kinds whose recovery it exercises — the manifest admits torn-publish, at-rest blob rot (BitFlip), and the foreign-epoch refusal; the CURRENT pointer admits its at-rest rot (CurrentRot) and the foreign-epoch refusal; other byte-damage kinds skip as represented by those single cells.</summary>
    /// <param name="cell">The recovery cell; its site is <see cref="DamageSite.Manifest"/> or <see cref="DamageSite.Current"/>.</param>
    /// <returns>Whether the cell is valid, and the skip reason when it is not.</returns>
    private static (bool Valid, string Reason) ClassifyRecoverySite(MatrixCell cell)
    {
        if(cell.Rung != RepairRung.RederiveLocally)
        {
            return (false, $"R6 rung: recovery recovers from a local surviving CURRENT/manifest copy at {RepairRung.RederiveLocally}, not via {cell.Rung}.");
        }

        if(cell.Site == DamageSite.Manifest)
        {
            return cell.Kind switch
            {
                DamageKind.TornPublishPreRename or DamageKind.TornPublishPreCurrent => (true, string.Empty),
                DamageKind.BitFlip => (true, string.Empty),
                DamageKind.StaleEpoch => (true, string.Empty),
                DamageKind.CurrentRot => (false, "Rm manifest: CURRENT-rot is the CURRENT-pointer fault, surfaced at the Current site."),
                _ => (false, "Rm manifest: at-rest manifest rot is represented by the BitFlip cell; any image byte damage fails the self-checksum identically."),
            };
        }

        return cell.Kind switch
        {
            DamageKind.CurrentRot => (true, string.Empty),
            DamageKind.StaleEpoch => (true, string.Empty),
            DamageKind.TornPublishPreRename or DamageKind.TornPublishPreCurrent => (false, "Rc current: torn-publish is a manifest-publish fault, surfaced at the Manifest site."),
            _ => (false, "Rc current: CURRENT-pointer at-rest rot is represented by the CurrentRot cell; the pointer is a single self-checksummed record."),
        };
    }

    /// <summary>The persistence invariants a valid cell asserts, fixed by its site and kind. A foreign epoch is a detection-time refusal (I1 + I5); the re-derivable sidecar/sketch cells assert faithful, ordinary-ingest repair (I1/I2/I3/I6); the system-of-record and journal-tail cells assert a named loss (I1 + I7); and the manifest/CURRENT recovery cells assert atomic-publish recovery (I1 + I4).</summary>
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
            DamageSite.DataSegment => DataSegmentInvariants(cell.Kind),
            DamageSite.JournalEntry => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.LossIsNamed],
            DamageSite.Manifest => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.PublishIsAtomic],
            DamageSite.Current => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.PublishIsAtomic],
            _ => [],
        };
    }

    /// <summary>The invariants a system-of-record cell asserts by its damage kind: a parity-restored block payload (a bit flip or whole-block garbage) is recovered faithfully — detection precedes use (I1) and the peel is faithful (I3); a tampered stored digest or truncated framing is detected and refused (I1), naming no loss and re-deriving nothing.</summary>
    /// <param name="kind">The system-of-record damage kind (a foreign epoch is cited on the epoch path before this is reached).</param>
    /// <returns>The cited invariants.</returns>
    private static PersistenceInvariant[] DataSegmentInvariants(DamageKind kind) => kind switch
    {
        DamageKind.BitFlip or DamageKind.WholeBlockGarbage => [PersistenceInvariant.DetectionPrecedesUse, PersistenceInvariant.RepairIsFaithful],
        _ => [PersistenceInvariant.DetectionPrecedesUse],
    };

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

    /// <summary>The landing-gate completeness census: every named persistence invariant is certified by at least one valid cell, so a regression that drops the only cell asserting an invariant — or a newly named invariant left unexercised — fails here. I1 (detection-precedes-use), I2 (detection-precedes-xor), I3 (repair-is-faithful), I5 (epoch-consistency), and I7 (loss-is-named) are asserted directly by a damaged-then-verified cell; I4 (publish-is-atomic) by the manifest/CURRENT recovery cells; I6 (repair-is-ordinary-ingest) by the re-derive differential twins. (The deeper structural form of I6 — a full edit-session re-ingest — is a rung-2/3 follow-on, not a single-node cell.)</summary>
    [TestMethod]
    public void EveryNamedInvariantIsCertifiedByAValidCell()
    {
        HashSet<PersistenceInvariant> certified = [];
        foreach(MatrixCell cell in Enumerate())
        {
            if(Classify(cell).Valid)
            {
                foreach(PersistenceInvariant invariant in CitedInvariants(cell))
                {
                    certified.Add(invariant);
                }
            }
        }

        foreach(PersistenceInvariant invariant in Enum.GetValues<PersistenceInvariant>())
        {
            Assert.Contains(invariant, certified, $"Invariant {invariant} must be cited by at least one valid cell, so the gate certifies it.");
        }
    }

    /// <summary>Drives one matrix cell: a valid cell is staged, damaged, and asserted end to end through its recovery seam; a skipped cell is enumerated with its named reason rather than silently dropped. The journal cells take the asynchronous file-backed replay seam; every other valid cell drives synchronously.</summary>
    /// <param name="cellId">The <c>site/kind/rung</c> identity of the cell to run.</param>
    /// <returns>The cell-drive task.</returns>
    [TestMethod]
    [DynamicData(nameof(CellRows))]
    public async Task Cell(string cellId)
    {
        MatrixCell cell = ParseCell(cellId);
        (bool valid, string reason) = Classify(cell);
        if(!valid)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason), $"A skipped cell must name why it is invalid: {cell}.");

            return;
        }

        if(cell.Site == DamageSite.JournalEntry)
        {
            await DriveJournal(cell).ConfigureAwait(false);

            return;
        }

        await DriveAsync(cell).ConfigureAwait(false);
    }

    /// <summary>Stages a generation with the cell's targeted artifact damaged, then asserts the verdict, the repair outcome, and the differential twin; a manifest/CURRENT recovery cell takes the recovery path instead of the scrub path.</summary>
    /// <param name="cell">The valid cell to drive.</param>
    private static async Task DriveAsync(MatrixCell cell)
    {
        if(cell.Site == DamageSite.Manifest || cell.Site == DamageSite.Current)
        {
            DriveRecovery(cell);

            return;
        }

        using VeritasMemoryPool<byte> bytePool = new();
        //The repair pass rents from a poisoning pool so every valid matrix cell asserts the pass returns every
        //buffer it rented (OutstandingRentals == 0) once the report is disposed — STEP-4 enforcement across the
        //whole damage x rung matrix. Staging and assertion reads use the separate bytePool.
        using PoisoningMemoryPool<byte> repairPool = new();
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

        //A system-of-record cell carries a co-versioned parity so a lost block payload is restored at the
        //local-parity rung rather than named lost; the other sites do not exercise the parity, so they omit it.
        using ArtifactImage? parity = cell.Site == DamageSite.DataSegment ? ParityImage(Triples, bytePool) : null;
        FileSystemPersistenceStore store;
        string directory;
        if(parity is null)
        {
            store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out directory);
        }
        else
        {
            store = StageGeneration(Generation, segment, sidecar, sketch, parity, bytePool, out directory);
        }

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
            using(RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(repairPool, triplePool), null, trace.Capture, Guid.Empty, clock, null, null, CancellationToken.None).ConfigureAwait(false))
            {
                AssertRepairOutcome(cell, repair, trace, bytePool, triplePool);
            }

            Assert.AreEqual(0, repairPool.OutstandingRentals, "The repair pass must return every buffer it rented from the repair pool once the report is disposed.");
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
            case DamageKind.ChecksumField:
            {
                CorruptSegmentChecksumField(clean, CorruptBlock);

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
            case DamageKind.ChecksumField:
            {
                CorruptSketchChecksumField(clean, CorruptBlock);

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

    /// <summary>Asserts the verify pass named the damaged artifact corrupt: a tampered stored digest is named at both the front-matter and the block grain; otherwise a system-of-record block is named with its index (or a truncated system-of-record is a whole-artifact loss) and a sidecar or sketch is reported corrupt by role.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="verify">The verify report.</param>
    private static void AssertDetectionVerdict(MatrixCell cell, ScrubRoundReport verify)
    {
        if(cell.Kind == DamageKind.ChecksumField)
        {
            AssertChecksumFieldDetected(cell, verify);

            return;
        }

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

    /// <summary>Asserts the repair outcome at the cell's terminal rung: a sidecar or sketch re-derives clean (with its differential twin); a corrupt system-of-record block payload is restored from the co-versioned parity and re-ingested (with its differential twin); a truncated system-of-record is refused as unreadable; a tampered system-of-record digest is likewise refused while a tampered sketch digest re-derives clean from the surviving system-of-record.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="repair">The repair report.</param>
    /// <param name="trace">The captured repair trace.</param>
    /// <param name="bytePool">The byte pool the sidecar decode rents from.</param>
    /// <param name="triplePool">The triple pool the sidecar decode rents from.</param>
    private static void AssertRepairOutcome(MatrixCell cell, RepairPassReport repair, StorageTraceCapture trace, MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
    {
        if(cell.Kind == DamageKind.ChecksumField)
        {
            AssertChecksumFieldRepair(cell, repair, trace);

            return;
        }

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
                    AssertDataSegmentRestored(repair, trace);
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

    /// <summary>Asserts the corrupt system-of-record block is restored from the co-versioned parity and re-ingested faithfully: the repair is clean (no loss named), the healed image verifies clean and decodes to exactly the original triples (the peel's differential twin, I1 + I3), and a re-ingest outcome is emitted for the whole healed item set.</summary>
    /// <param name="repair">The repair report.</param>
    /// <param name="trace">The captured repair trace.</param>
    private static void AssertDataSegmentRestored(RepairPassReport repair, StorageTraceCapture trace)
    {
        Assert.IsFalse(repair.Refused);
        Assert.IsTrue(repair.IsClean, "A parity-restorable system-of-record block is fully recoverable.");
        Assert.IsEmpty(repair.NamedLosses);
        RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
        Assert.IsTrue(ItemSegment.RunVerifyRound(restored.Image.Span).IsClean, "The restored system-of-record must verify clean.");
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
        bool faithful = recovered.Span.SequenceEqual(Triples);
        Assert.IsTrue(faithful, "The restored system-of-record must hold exactly the original triples — the peel's differential twin (I3).");
        StorageTraceEvent reingested = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Reingested);
        Assert.AreEqual((long)TripleCount, reingested.ItemCount, "The healed system-of-record holds every item.");
    }

    /// <summary>Asserts a tampered stored digest is named at both grains: the front-matter trailer covers the per-block digest section, so a flipped digest fails the trailer (a front-matter finding) and the block's own recompute (a block finding named with its index) — proving the stored digests cannot be forged to mask a corruption.</summary>
    /// <param name="cell">The checksum-field cell; its site is the system-of-record or the sketch.</param>
    /// <param name="verify">The verify report.</param>
    private static void AssertChecksumFieldDetected(MatrixCell cell, ScrubRoundReport verify)
    {
        List<ScrubBlockFinding> findings = cell.Site == DamageSite.DataSegment
            ? verify.CorruptBlocks.Where(static f => f.RoleCode == ManifestFileRole.DataSegment.Code).ToList()
            : verify.CorruptBlocks.Where(static f => f.RoleCode == ManifestFileRole.Sketch.Code).ToList();
        Assert.HasCount(2, findings, "A tampered stored digest is named at both the front-matter and the block grain.");
        Assert.ContainsSingle(findings.Where(static f => f.IsFrontMatter));
        ScrubBlockFinding block = findings.Single(static f => !f.IsFrontMatter);
        Assert.AreEqual(CorruptBlock, block.BlockIndex, "The block whose stored digest was tampered is named with its index.");
    }

    /// <summary>Asserts the repair outcome for a tampered stored digest: the system-of-record's tampered digest fails its front-matter trailer, so its geometry is untrusted and the whole artifact is refused as unreadable; a tampered sketch digest re-derives clean from the surviving system-of-record, which the tampering never touched.</summary>
    /// <param name="cell">The checksum-field cell; its site is the system-of-record or the sketch.</param>
    /// <param name="repair">The repair report.</param>
    /// <param name="trace">The captured repair trace.</param>
    private static void AssertChecksumFieldRepair(MatrixCell cell, RepairPassReport repair, StorageTraceCapture trace)
    {
        if(cell.Site == DamageSite.DataSegment)
        {
            Assert.IsTrue(repair.Refused, "A tampered system-of-record digest leaves its geometry untrusted.");
            Assert.AreEqual(RepairRefusalReason.SystemOfRecordUnreadable, repair.Refusal);
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.IsEmpty(repair.NamedLosses);

            return;
        }

        AssertSketchRederived(repair, trace);
    }

    /// <summary>Drives a manifest/CURRENT recovery cell: it stages a first committed generation, injects the cell's fault, and asserts how <see cref="ManifestRecovery"/> follows the CURRENT pointer to a committed generation — leaving the prior generation in force on a torn publish (I4), falling back to a retained CURRENT past pointer rot (I4), skipping at-rest manifest rot to an earlier generation, or refusing a foreign epoch (I5).</summary>
    /// <param name="cell">The recovery cell to drive (its site is Manifest or Current).</param>
    private static void DriveRecovery(MatrixCell cell)
    {
        using VeritasMemoryPool<byte> bytePool = new();
        EncodedTriple[] triples = SampleTriples(RecoveryTripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(RecoverySketchSymbolCount, bytePool);
        FileSystemPersistenceStore store = StageGeneration(RecoveryFirstGeneration, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            if(cell.Site == DamageSite.Manifest)
            {
                AssertManifestRecovery(cell.Kind, store, segment, sidecar, sketch, bytePool);
            }
            else
            {
                AssertCurrentRecovery(cell.Kind, store, segment, sidecar, sketch, bytePool);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Asserts the manifest-site recovery outcome for a kind: torn-publish leaves the prior generation in force (I4); at-rest blob rot is skipped to the earlier generation; a foreign epoch is refused (I5).</summary>
    /// <param name="kind">The damage kind.</param>
    /// <param name="store">The store holding the first committed generation.</param>
    /// <param name="segment">The data-segment image a second generation is staged from.</param>
    /// <param name="sidecar">The sidecar image a second generation is staged from.</param>
    /// <param name="sketch">The sketch image a second generation is staged from.</param>
    /// <param name="bytePool">The pool the second generation's commit rents from.</param>
    private static void AssertManifestRecovery(DamageKind kind, FileSystemPersistenceStore store, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, MemoryPool<byte> bytePool)
    {
        switch(kind)
        {
            case DamageKind.TornPublishPreRename:
            {
                AssertTornPublishLeavesPriorInForce(store, segment, sidecar, sketch, bytePool, PublishFailStep.BeforeRename);

                break;
            }
            case DamageKind.TornPublishPreCurrent:
            {
                AssertTornPublishLeavesPriorInForce(store, segment, sidecar, sketch, bytePool, PublishFailStep.BeforeCurrentStaging);

                break;
            }
            case DamageKind.BitFlip:
            {
                CommitGeneration(store, RecoverySecondGeneration, segment, sidecar, sketch, bytePool);
                CorruptManifestBlob(store, RecoverySecondGeneration);
                RecoveryResult recovered = new ManifestRecovery(store).Recover();
                Assert.AreEqual(RecoveryFirstGeneration, recovered.Manifest.CommitGeneration, "At-rest manifest rot is skipped; recovery returns the earlier committed generation.");
                Assert.IsFalse(recovered.IsDegraded);

                break;
            }
            case DamageKind.StaleEpoch:
            {
                SetForeignManifestAlgorithmId(store, RecoveryFirstGeneration);
                Assert.ThrowsExactly<NotSupportedException>(() => { _ = new ManifestRecovery(store).Recover(); });

                break;
            }
            default:
            {
                throw new InvalidOperationException($"No manifest recovery drive for kind {kind}.");
            }
        }
    }

    /// <summary>Asserts the CURRENT-site recovery outcome: pointer rot falls back to the retained copy and recovers the committed generation (I4); a foreign epoch is refused (I5).</summary>
    /// <param name="kind">The damage kind.</param>
    /// <param name="store">The store holding the first committed generation.</param>
    /// <param name="segment">The data-segment image a second generation is staged from.</param>
    /// <param name="sidecar">The sidecar image a second generation is staged from.</param>
    /// <param name="sketch">The sketch image a second generation is staged from.</param>
    /// <param name="bytePool">The pool the second generation's commit rents from.</param>
    private static void AssertCurrentRecovery(DamageKind kind, FileSystemPersistenceStore store, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, MemoryPool<byte> bytePool)
    {
        switch(kind)
        {
            case DamageKind.CurrentRot:
            {
                CommitGeneration(store, RecoverySecondGeneration, segment, sidecar, sketch, bytePool);
                CorruptCurrentPointer(store);
                RecoveryResult recovered = new ManifestRecovery(store).Recover();
                Assert.AreEqual(RecoverySecondGeneration, recovered.Manifest.CommitGeneration, "CURRENT-pointer rot falls back to the retained copy and recovers the latest committed generation.");
                Assert.IsFalse(recovered.IsDegraded);

                break;
            }
            case DamageKind.StaleEpoch:
            {
                SetForeignCurrentAlgorithmId(store);
                Assert.ThrowsExactly<NotSupportedException>(() => { _ = new ManifestRecovery(store).Recover(); });

                break;
            }
            default:
            {
                throw new InvalidOperationException($"No CURRENT recovery drive for kind {kind}.");
            }
        }
    }

    /// <summary>Asserts a torn publish of the second generation leaves the first committed generation wholly in force (I4): recovery follows the surviving CURRENT to the first generation, while the orphaned second-generation manifest exists on disk and surfaces only via the named-degraded direct scan — so the orphan is never mistaken for a committed generation.</summary>
    /// <param name="store">The store holding the first committed generation.</param>
    /// <param name="segment">The data-segment image the second generation is staged from.</param>
    /// <param name="sidecar">The sidecar image the second generation is staged from.</param>
    /// <param name="sketch">The sketch image the second generation is staged from.</param>
    /// <param name="bytePool">The pool the second generation's commit rents from.</param>
    /// <param name="failStep">The publish step the torn commit crashes before.</param>
    private static void AssertTornPublishLeavesPriorInForce(FileSystemPersistenceStore store, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, MemoryPool<byte> bytePool, PublishFailStep failStep)
    {
        FailAtStepStore crashing = new(store, failStep);
        Assert.ThrowsExactly<IOException>(() => CommitGeneration(crashing, RecoverySecondGeneration, segment, sidecar, sketch, bytePool));

        RecoveryResult recovered = new ManifestRecovery(store).Recover();
        Assert.AreEqual(RecoveryFirstGeneration, recovered.Manifest.CommitGeneration, "A torn publish leaves the prior committed generation wholly in force (I4).");
        Assert.IsFalse(recovered.IsDegraded);

        RecoveryResult degraded = new ManifestRecovery(store).RecoverFromDegradedScan();
        Assert.AreEqual(RecoverySecondGeneration, degraded.Manifest.CommitGeneration, "The orphaned generation's manifest exists on disk and surfaces only via the named-degraded scan.");
        Assert.IsTrue(degraded.IsDegraded);
    }

    /// <summary>Drives a journal recovery cell: it builds a durable file-backed log, injects the cell's tail damage, and asserts how <see cref="FileBackedJournal"/> replay recovers it — a torn or garbage tail is recovered through its last intact operation and named as an <see cref="UnrecoverableItemReportKind.OperationRange"/> loss with its discarded byte range (I1 + I7), and an unsupported payload-format epoch is refused at detection without truncating the log (I1 + I5). The journal is a file-backed append-only log recovered on open, a different drive shape from the scrub seam and the manifest/CURRENT recovery; the drive is asynchronous because the journal append delegate is.</summary>
    /// <param name="cell">The valid journal cell to drive (its site is <see cref="DamageSite.JournalEntry"/>, its terminal rung <see cref="RepairRung.NamedLoss"/>).</param>
    /// <returns>The drive task.</returns>
    private static async Task DriveJournal(MatrixCell cell)
    {
        using VeritasMemoryPool<byte> bytePool = new();
        string directory = Directory.CreateTempSubdirectory("veritas-matrix-journal-").FullName;
        try
        {
            string path = Path.Combine(directory, "journal.log");
            FakeTimeProvider clock = new();

            //Build a durable record chain, then close the handle before corrupting the file on disk.
            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, clock, bytePool))
            {
                await AppendJournalChain(journal, JournalRecordCount).ConfigureAwait(false);
            }

            byte[] intact = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
            if(cell.Kind == DamageKind.StaleEpoch)
            {
                AssertJournalRefusesUnsupportedEpoch(path, intact, bytePool, clock);

                return;
            }

            AssertJournalTailRecovers(cell.Kind, path, intact, bytePool, clock);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Injects the cell's tail damage into the durable log and asserts replay recovers the intact prefix and names the operation-range loss: a flipped byte in the last record fails its checksum, and a final record cut short of its framed length, both drop the last operation; an appended garbage tail keeps every intact record and discards only the appended bytes. Each kind injects a distinct on-disk damage shape and asserts the file-state outcome it produces (the recovered length, the last intact sequence, and an exact or positive discarded byte count); no two kinds coincide in both the detection that fires and the outcome it yields, so all three are driven.</summary>
    /// <param name="kind">The journal tail damage kind.</param>
    /// <param name="path">The log file path.</param>
    /// <param name="intact">The intact log bytes captured before damage.</param>
    /// <param name="bytePool">The pool the reopened journal rents from.</param>
    /// <param name="clock">The journal's clock.</param>
    private static void AssertJournalTailRecovers(DamageKind kind, string path, byte[] intact, MemoryPool<byte> bytePool, TimeProvider clock)
    {
        switch(kind)
        {
            case DamageKind.WholeBlockGarbage:
            {
                //A garbage tail appended past every intact record: replay keeps all records and discards only the appended bytes.
                byte[] torn = [.. intact, .. JournalGarbageTail];
                File.WriteAllBytes(path, torn);
                AssertReopenRecovers(path, bytePool, clock, expectedLength: JournalRecordCount, expectedRecoveredThrough: JournalRecordCount - 1L, expectedDiscardedBytes: JournalGarbageTail.Length);
                Assert.AreEqual(intact.Length, new FileInfo(path).Length, "The appended garbage tail is physically truncated back to the intact length.");
                AssertSecondReopenIsClean(path, bytePool, clock, JournalRecordCount);

                break;
            }
            case DamageKind.BitFlip:
            {
                //A byte flipped in the last record fails its checksum: replay recovers the prior operations and drops the last.
                byte[] flipped = [.. intact];
                flipped[^1] ^= 0xFF;
                File.WriteAllBytes(path, flipped);
                AssertReopenRecovers(path, bytePool, clock, expectedLength: JournalRecordCount - 1, expectedRecoveredThrough: JournalRecordCount - 2L, expectedDiscardedBytes: null);
                AssertSecondReopenIsClean(path, bytePool, clock, JournalRecordCount - 1);

                break;
            }
            case DamageKind.Truncate:
            {
                //The last record cut short of its framed length: replay reads the intact prefix and drops the incomplete final record.
                byte[] cut = intact[..^JournalTruncateDropBytes];
                File.WriteAllBytes(path, cut);
                AssertReopenRecovers(path, bytePool, clock, expectedLength: JournalRecordCount - 1, expectedRecoveredThrough: JournalRecordCount - 2L, expectedDiscardedBytes: null);
                AssertSecondReopenIsClean(path, bytePool, clock, JournalRecordCount - 1);

                break;
            }
            default:
            {
                throw new InvalidOperationException($"Kind {kind} is not a driven journal tail damage.");
            }
        }
    }

    /// <summary>Reopens the journal and asserts replay recovered the intact prefix and surfaced a named operation-range loss — the recovered length, the last intact operation sequence, and the discarded byte count (exact when the damage size is known, positive otherwise), all read from the recovery report and the reopened journal rather than from constants.</summary>
    /// <param name="path">The log file path.</param>
    /// <param name="bytePool">The pool the reopened journal rents from.</param>
    /// <param name="clock">The journal's clock.</param>
    /// <param name="expectedLength">The number of records replay is expected to recover.</param>
    /// <param name="expectedRecoveredThrough">The sequence number of the last intact operation.</param>
    /// <param name="expectedDiscardedBytes">The exact discarded byte count when the damage size is known, or <see langword="null"/> to assert only that it is positive.</param>
    private static void AssertReopenRecovers(string path, MemoryPool<byte> bytePool, TimeProvider clock, int expectedLength, long expectedRecoveredThrough, long? expectedDiscardedBytes)
    {
        using FileBackedJournal recovered = new(path, ChecksumAlgorithm.XxHash3, clock, bytePool);
        Assert.AreEqual(expectedLength, recovered.Length, "Replay recovers the intact prefix.");
        UnrecoverableItemReport? report = recovered.RecoveryReport;
        Assert.IsNotNull(report, "A torn or corrupt journal tail must surface a recovery report.");
        Assert.AreEqual(UnrecoverableItemReportKind.OperationRange, report.Kind, "A journal tail loss is a named operation-range loss.");
        Assert.AreEqual(expectedRecoveredThrough, report.RecoveredThroughSequence, "The report names the last intact operation sequence.");
        if(expectedDiscardedBytes is long discarded)
        {
            Assert.AreEqual(discarded, report.DiscardedByteCount, "The report names the discarded byte count exactly.");
        }
        else
        {
            Assert.IsGreaterThan(0L, report.DiscardedByteCount, "A recovered tail names a positive discarded byte count.");
        }
    }

    /// <summary>Asserts a second reopen is clean: the torn tail was physically truncated on the first recovery, so replay finds an intact log and reports no further loss, and the recovered prefix length persists.</summary>
    /// <param name="path">The log file path.</param>
    /// <param name="bytePool">The pool the reopened journal rents from.</param>
    /// <param name="clock">The journal's clock.</param>
    /// <param name="expectedLength">The recovered prefix length that persists across the reopen.</param>
    private static void AssertSecondReopenIsClean(string path, MemoryPool<byte> bytePool, TimeProvider clock, int expectedLength)
    {
        using FileBackedJournal reopened = new(path, ChecksumAlgorithm.XxHash3, clock, bytePool);
        Assert.IsNull(reopened.RecoveryReport, "The torn tail was physically truncated, so a second reopen is clean.");
        Assert.AreEqual(expectedLength, reopened.Length, "The recovered prefix persists across the second reopen.");
    }

    /// <summary>Asserts the I7 asymmetry for the journal: a checksum-valid record carrying an unsupported payload-format version is refused at detection (the constructor throws <see cref="NotSupportedException"/>), never truncated as if it were a torn tail, so a newer log opened by an older build is not silently shortened. The first record's version byte is bumped past the supported version and the record re-sealed so it still checksum-verifies — a valid record the replay must refuse.</summary>
    /// <param name="path">The log file path.</param>
    /// <param name="intact">The intact log bytes captured before damage.</param>
    /// <param name="bytePool">The pool the reopened journal rents from.</param>
    /// <param name="clock">The journal's clock.</param>
    private static void AssertJournalRefusesUnsupportedEpoch(string path, byte[] intact, MemoryPool<byte> bytePool, TimeProvider clock)
    {
        byte[] foreignVersion = [.. intact];
        foreignVersion[JournalPayloadVersionOffset] = ForeignJournalPayloadVersion;
        RecomputeFirstRecordChecksum(foreignVersion, ChecksumAlgorithm.XxHash3);
        File.WriteAllBytes(path, foreignVersion);

        Assert.ThrowsExactly<NotSupportedException>(() => { using FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, clock, bytePool); });
        Assert.AreEqual(intact.Length, new FileInfo(path).Length, "An unsupported payload-format epoch is refused, never truncated.");
    }

    /// <summary>Re-seals the first record of a log after its bytes were mutated: it recomputes the record's checksum over its length prefix and payload so the record verifies and replay reaches the version check rather than failing the checksum first.</summary>
    /// <param name="log">The log bytes, whose first record is re-sealed in place.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    private static void RecomputeFirstRecordChecksum(byte[] log, ChecksumAlgorithm checksum)
    {
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(log);
        int checksummedLength = sizeof(uint) + (int)payloadLength;
        checksum.Compute(log.AsSpan(0, checksummedLength), log.AsSpan(checksummedLength, checksum.ByteWidth));
    }
}
