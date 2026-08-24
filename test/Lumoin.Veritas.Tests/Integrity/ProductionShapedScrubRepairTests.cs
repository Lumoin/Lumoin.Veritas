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
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The storage scrub over a PRODUCTION-SHAPED generation — the manifest shape
/// <see cref="Lumoin.Veritas.Core.Persistence.DurableSystemOfRecordStore"/> commits: a term dictionary (role 6),
/// a default-graph data segment (role 1), one or more named-graph segments (role 7), and a columnar sidecar
/// (role 2). The two- and four-artifact stagings the rest of the storage self-heal tests use never name a
/// Dictionary or NamedGraphSegment role, so these roles' at-rest block verification and honest under-heal
/// closure are exercised only here: the verify pass detects a rotted dictionary or named-graph block, and the
/// repair pass names a damaged dictionary a whole-artifact loss (the decode key is not re-derivable) and a
/// damaged named graph an item-range or whole-segment loss (no parity or peer rung protects it), re-deriving
/// nothing from either and leaving the default-graph ladder untouched. The pass is synchronous and
/// deterministic, so there are no waits; the pool every artifact image is rented from is owned by the test.
/// </summary>
[TestClass]
internal sealed class ProductionShapedScrubRepairTests
{
    /// <summary>The named graph's graph-name term id, encoded into its artifact name.</summary>
    private const uint NamedGraphId = 42;

    /// <summary>Stages a clean production-shaped generation: a 20-term dictionary in 8-term blocks, a 30-triple default segment, a 25-triple named graph, and a sidecar over the default triples.</summary>
    /// <param name="pool">The pool the images and manifest buffers are rented from.</param>
    /// <param name="dictionary">The staged dictionary image (the caller disposes it).</param>
    /// <param name="dataSegment">The staged default data-segment image (the caller disposes it).</param>
    /// <param name="namedGraph">The staged named-graph image (the caller disposes it).</param>
    /// <param name="sidecar">The staged sidecar image (the caller disposes it).</param>
    /// <returns>A factory that stages the (possibly caller-corrupted) images into a fresh store; the caller corrupts before invoking it.</returns>
    private static ProductionStaging Stage(MemoryPool<byte> pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar)
    {
        EncodedTriple[] defaultTriples = SampleTriples(30);
        EncodedTriple[] namedTriples = SampleTriples(25);
        dictionary = DictionaryImage(SampleDictionary(20), blockTermCount: 8, pool);
        dataSegment = SegmentImage(defaultTriples, pool);
        namedGraph = NamedGraphSegmentImage(namedTriples, pool);
        sidecar = SidecarImage(defaultTriples, pool);

        return new ProductionStaging(dictionary, dataSegment, namedGraph, sidecar, defaultTriples);
    }

    /// <summary>A production-shaped staging closure over the four staged images, so a test corrupts them and then commits them into a fresh store in one call.</summary>
    private sealed class ProductionStaging
    {
        /// <summary>Creates the staging over the four images.</summary>
        /// <param name="dictionary">The dictionary image.</param>
        /// <param name="dataSegment">The default data-segment image.</param>
        /// <param name="namedGraph">The named-graph image.</param>
        /// <param name="sidecar">The sidecar image.</param>
        /// <param name="defaultTriples">The default-graph triples the sidecar and data segment carry.</param>
        internal ProductionStaging(ArtifactImage dictionary, ArtifactImage dataSegment, ArtifactImage namedGraph, ArtifactImage sidecar, EncodedTriple[] defaultTriples)
        {
            Dictionary = dictionary;
            DataSegment = dataSegment;
            NamedGraph = namedGraph;
            Sidecar = sidecar;
            DefaultTriples = defaultTriples;
        }

        /// <summary>The dictionary image.</summary>
        internal ArtifactImage Dictionary { get; }

        /// <summary>The default data-segment image.</summary>
        internal ArtifactImage DataSegment { get; }

        /// <summary>The named-graph image.</summary>
        internal ArtifactImage NamedGraph { get; }

        /// <summary>The sidecar image.</summary>
        internal ArtifactImage Sidecar { get; }

        /// <summary>The default-graph triples the sidecar and data segment carry.</summary>
        internal EncodedTriple[] DefaultTriples { get; }

        /// <summary>Commits the (possibly corrupted) images into a fresh temp-dir store as generation 7.</summary>
        /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
        /// <param name="directory">The created temp directory.</param>
        /// <returns>The store.</returns>
        internal FileSystemPersistenceStore Commit(MemoryPool<byte> pool, out string directory)
        {
            return StageProductionGeneration(7, Dictionary, DataSegment, [(NamedGraphId, NamedGraph)], Sidecar, pool, out directory);
        }
    }

    /// <summary>A clean production-shaped generation verifies wholly clean — every role, including the dictionary and the named graph, is walked block by block and passes (the no-regression pin that the new roles' verify seam is real, not vacuous).</summary>
    [TestMethod]
    public void CleanProductionShapedGenerationVerifiesWholeClean()
    {
        using VeritasMemoryPool<byte> pool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                StorageTraceCapture trace = new();
                ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, trace.Capture, Guid.Empty, new FakeTimeProvider());

                Assert.IsTrue(report.IsClean, "A clean production-shaped generation must scrub clean.");
                Assert.IsEmpty(report.CorruptBlocks);
                bool dictionaryWalked = trace.Events.Any(static e => e.Kind == StorageTraceEventKind.BlockVerified && e.RoleCode == ManifestFileRole.Dictionary.Code);
                bool namedGraphWalked = trace.Events.Any(static e => e.Kind == StorageTraceEventKind.BlockVerified && e.RoleCode == ManifestFileRole.NamedGraphSegment.Code);
                Assert.IsTrue(dictionaryWalked, "The scrub must verify the dictionary's blocks (the Dictionary role is not vacuously skipped).");
                Assert.IsTrue(namedGraphWalked, "The scrub must verify the named graph's blocks (the NamedGraphSegment role is not vacuously skipped).");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>A rotted dictionary block is detected at rest by the verify pass, named with the Dictionary role and its block index — the dictionary's per-block detection seam.</summary>
    [TestMethod]
    public void CorruptDictionaryBlockIsDetectedByVerifyPass()
    {
        using VeritasMemoryPool<byte> pool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            GarbageDictionaryBlock(dictionary, block: 1);
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

                Assert.IsFalse(report.IsClean);
                ScrubBlockFinding finding = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.Dictionary.Code);
                Assert.AreEqual(1, finding.BlockIndex);
                Assert.IsFalse(finding.IsFrontMatter, "A rotted term-record block is a per-block failure, not a front-matter one.");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>A rotted named-graph block is detected at rest by the verify pass, named with the NamedGraphSegment role and its block index — the named-graph per-block detection seam.</summary>
    [TestMethod]
    public void CorruptNamedGraphBlockIsDetectedByVerifyPass()
    {
        using VeritasMemoryPool<byte> pool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            GarbageSegmentBlock(namedGraph, block: 1, blockCount: 3);
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

                Assert.IsFalse(report.IsClean);
                ScrubBlockFinding finding = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.NamedGraphSegment.Code);
                Assert.AreEqual(1, finding.BlockIndex);
                Assert.IsFalse(finding.IsFrontMatter);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>A damaged dictionary is named a WHOLE-ARTIFACT loss by the repair pass — the decode key is not re-derivable and no rung restores it — carrying the Dictionary role and the artifact name, with nothing re-derived from it and a whole-artifact named-loss event emitted.</summary>
    [TestMethod]
    public async Task CorruptDictionaryIsNamedAWholeArtifactLoss()
    {
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            GarbageDictionaryBlock(dictionary, block: 1);
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
                bool dictionaryDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Dictionary.Code);
                Assert.IsTrue(dictionaryDetected, "Precondition: the dictionary must be detected corrupt.");

                StorageTraceCapture trace = new();
                using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(pool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

                Assert.IsFalse(repair.Refused);
                Assert.IsFalse(repair.IsClean, "A named dictionary loss is not a clean outcome.");
                Assert.IsEmpty(repair.RederivedArtifacts, "Nothing is re-derived from a damaged dictionary.");
                UnrecoverableItemReport loss = repair.NamedLosses.Single();
                Assert.AreEqual(UnrecoverableItemReportKind.WholeArtifact, loss.Kind);
                Assert.AreEqual(ManifestFileRole.Dictionary.Code, loss.RoleCode);
                Assert.AreEqual(DictionaryArtifactName(7), loss.ArtifactFileName);
                Assert.AreEqual(7, loss.CommitGeneration);
                StorageTraceEvent named = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.NamedLoss && e.RoleCode == ManifestFileRole.Dictionary.Code);
                Assert.AreEqual(-1, named.BlockIndex, "A whole-artifact loss is emitted with block index -1.");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>A rotted named-graph block is named an item-range loss by the repair pass — its own verified feed excludes the block, and the range is named against the graph's artifact — with nothing re-derived from it (the named graph is outside the default-graph parity/peer ladder).</summary>
    [TestMethod]
    public async Task CorruptNamedGraphRangeIsNamedAndNothingRederived()
    {
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            //Block 1 of the 25-triple named graph covers items [10, 20).
            GarbageSegmentBlock(namedGraph, block: 1, blockCount: 3);
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
                bool namedGraphDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.NamedGraphSegment.Code);
                Assert.IsTrue(namedGraphDetected, "Precondition: the named graph must be detected corrupt.");

                StorageTraceCapture trace = new();
                using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(pool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

                Assert.IsFalse(repair.Refused);
                Assert.IsFalse(repair.IsClean);
                Assert.IsEmpty(repair.RederivedArtifacts, "A named graph is re-derived from no source; its loss is named.");
                UnrecoverableItemReport loss = repair.NamedLosses.Single();
                Assert.AreEqual(UnrecoverableItemReportKind.ItemSet, loss.Kind);
                Assert.AreEqual(ManifestFileRole.NamedGraphSegment.Code, loss.RoleCode);
                Assert.AreEqual(NamedGraphArtifactName(7, NamedGraphId), loss.ArtifactFileName);
                Assert.AreEqual(10, loss.LostItemStart);
                Assert.AreEqual(10, loss.LostItemCount);
                Assert.AreEqual(7, loss.CommitGeneration);
                StorageTraceEvent named = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.NamedLoss && e.RoleCode == ManifestFileRole.NamedGraphSegment.Code);
                Assert.AreEqual(10, named.ItemCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>A named graph whose front matter is damaged is named a WHOLE-SEGMENT loss — its geometry cannot be trusted, so no item range is enumerated — carrying the NamedGraphSegment role and the graph's artifact name, with nothing re-derived.</summary>
    [TestMethod]
    public async Task CorruptNamedGraphFrontMatterIsNamedAWholeSegmentLoss()
    {
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            //Tampering the stored per-block digest fails the front-matter trailer, so the geometry is untrustworthy.
            CorruptSegmentChecksumField(namedGraph, block: 0);
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
                bool namedGraphDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.NamedGraphSegment.Code);
                Assert.IsTrue(namedGraphDetected, "Precondition: the named graph must be detected corrupt.");

                using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(pool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

                Assert.IsFalse(repair.Refused);
                Assert.IsEmpty(repair.RederivedArtifacts);
                UnrecoverableItemReport loss = repair.NamedLosses.Single();
                Assert.AreEqual(UnrecoverableItemReportKind.WholeArtifact, loss.Kind);
                Assert.AreEqual(ManifestFileRole.NamedGraphSegment.Code, loss.RoleCode);
                Assert.AreEqual(NamedGraphArtifactName(7, NamedGraphId), loss.ArtifactFileName);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Damage to the unprotected roles does NOT disturb the default-graph ladder: with the sidecar (default, re-derivable), the dictionary, and the named graph all damaged in one generation, the repair pass re-derives the sidecar from the intact default system-of-record while naming the dictionary a whole-artifact loss and the named graph an item-range loss — one pass, three independent outcomes.</summary>
    [TestMethod]
    public async Task UnprotectedRoleDamageDoesNotDisturbTheDefaultGraphLadder()
    {
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        ProductionStaging staging = Stage(pool, out ArtifactImage dictionary, out ArtifactImage dataSegment, out ArtifactImage namedGraph, out ArtifactImage sidecar);
        using(dictionary)
        using(dataSegment)
        using(namedGraph)
        using(sidecar)
        {
            CorruptSidecarFrontMatter(sidecar);
            GarbageDictionaryBlock(dictionary, block: 0);
            GarbageSegmentBlock(namedGraph, block: 1, blockCount: 3);
            FileSystemPersistenceStore store = staging.Commit(pool, out string directory);
            try
            {
                ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
                using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(pool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

                Assert.IsFalse(repair.Refused);

                //The default-graph re-derivable artifact is healed exactly as it would be without the unprotected-role
                //damage present: the sidecar re-derives from the intact default system-of-record and verifies clean
                //and faithful.
                RederivedArtifact rederivedSidecar = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sidecar);
                Assert.IsTrue(ColumnarTripleIndex.RunVerifyRound(rederivedSidecar.Image.Span).ToArtifactReport().IsClean, "The default-graph sidecar re-derive must be undisturbed by the unprotected-role damage.");
                ColumnarTripleIndex rebuilt = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(rederivedSidecar.Image), pool, triplePool);
                Assert.IsTrue(new HashSet<EncodedTriple>(rebuilt.EnumerateTriples()).SetEquals(staging.DefaultTriples), "The re-derived sidecar must carry exactly the default-graph triples.");

                //The two unprotected roles are named independently, neither re-deriving anything.
                bool dictionaryNamed = repair.NamedLosses.Any(static l => l.Kind == UnrecoverableItemReportKind.WholeArtifact && l.RoleCode == ManifestFileRole.Dictionary.Code);
                bool namedGraphNamed = repair.NamedLosses.Any(static l => l.Kind == UnrecoverableItemReportKind.ItemSet && l.RoleCode == ManifestFileRole.NamedGraphSegment.Code && l.LostItemCount == 10);
                Assert.IsTrue(dictionaryNamed, "The damaged dictionary must be named a whole-artifact loss.");
                Assert.IsTrue(namedGraphNamed, "The damaged named graph's block must be named an item-range loss.");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
