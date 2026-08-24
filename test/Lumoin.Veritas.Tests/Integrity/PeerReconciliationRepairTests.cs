using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Tests.MemoryPool;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The peer-reconciliation repair rung: a repair pass over a generation whose system-of-record lost one block —
/// with no parity sidecar to restore it — recovers the lost items from a peer replica's verified sketch, verifies
/// the healed set reconciles emptily against the generation's OWN at-rest-verified sketch (the independent
/// pre-damage record that makes faithfulness a verified property, not a trusted-peer assumption), re-ingests the
/// healed system-of-record, and re-derives any co-damaged view from the healed full item set. More than one lost
/// block, a peer that lacks the lost items, a diverged peer whose healed set the generation's sketch does not
/// corroborate, a foreign-epoch peer, or a missing/unverifiable generation sketch all descend to a named loss.
/// The pass drives the core's public repair ladder; the peer's sketch and the rateless recover/invert seams are
/// bound from the replication library, so the core takes no replication dependency. Geometry is the fixture's
/// (10-item blocks, 64-byte aligned, XxHash3); every buffer is pool-rented.
/// </summary>
[TestClass]
internal sealed class PeerReconciliationRepairTests
{
    /// <summary>The symbol budget both the survivor and peer sketches are built at — far above any single block's item count, so a faithful peer always peels the lost block completely.</summary>
    private const int SymbolCap = 1300;

    /// <summary>The dictionary epoch the staged generation's manifest records: the fixture stamps generation × 11 and every test here stages generation 7.</summary>
    private const long StagedDictionaryEpoch = 77;

    /// <summary>The repair configuration for the peer-reconciliation tests: like the fixture's, but bound to the REAL rateless encoder so the survivor sketch the rung builds reconciles with the peer's sketch (the fixture's stub encoder does not reconcile).</summary>
    /// <param name="bytePool">The byte pool the rung rents from.</param>
    /// <param name="triplePool">The triple pool the feed and healed item set rent from.</param>
    /// <returns>The configuration.</returns>
    private static RepairConfiguration PeerRepairConfig(MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
    {
        return new RepairConfiguration(
            ChecksumAlgorithm.XxHash3,
            bytePool,
            triplePool,
            SketchContract.Structural,
            symbolBudget: 16,
            StructuralReconciliationProjection.Projection,
            new RatelessSketchCodec(bytePool).Encode);
    }

    /// <summary>Persists a peer replica's triples as a verified structural sketch at the given symbol budget — the peer operand the rung combines with the local survivors' sketch.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from.</param>
    /// <param name="symbolCap">The number of symbols to persist.</param>
    /// <returns>The peer's verified sketch.</returns>
    private static VerifiedSketch PeerSketch(EncodedTriple[] peerTriples, MemoryPool<byte> pool, int symbolCap)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolCap, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchPersistence.LoadVerifiedSketch(writer.WrittenSpan, SketchContract.Structural);
    }

    /// <summary>Builds a peer-reconciliation source over a peer replica's full triple set: its verified sketch, the rateless recover seam, the structural inverse, the symbol cap, and the dictionary epoch the peer's items were encoded under. The cap budgets both the peer sketch and (through the source) the survivor sketch and the recovery.</summary>
    /// <param name="peerTriples">The peer replica's full triple set.</param>
    /// <param name="pool">The pool the peer sketch persist rents from.</param>
    /// <param name="symbolCap">The symbol budget; defaults to the generous <see cref="SymbolCap"/>, lowered by a test that drives a partial peel.</param>
    /// <param name="dictionaryEpoch">The peer's dictionary epoch; defaults to the staged generation's, overridden by the foreign-epoch decline test.</param>
    /// <returns>The peer-reconciliation source.</returns>
    private static PeerReconciliationSource PeerSource(EncodedTriple[] peerTriples, MemoryPool<byte> pool, int symbolCap = SymbolCap, long dictionaryEpoch = StagedDictionaryEpoch)
    {
        return new PeerReconciliationSource(PeerSketch(peerTriples, pool, symbolCap), new RatelessSketchCodec(pool).Recover, StructuralReconciliationProjection.Inversion, symbolCap, dictionaryEpoch);
    }

    /// <summary>Persists the generation's OWN integrity sketch over its pre-damage triples — the at-rest record the peer rung verifies a healed set against — as a stageable artifact image, replacing the fixture's opaque stub (which no verifying load under the structural contract accepts) for the tests whose rung must reach the residual verification.</summary>
    /// <param name="triples">The generation's pre-damage triple set.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from and the image is copied into.</param>
    /// <returns>The pooled sketch artifact image.</returns>
    private static ArtifactImage GenerationSketchImage(EncodedTriple[] triples, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. triples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, SymbolCap, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return ArtifactImage.Copy(writer.WrittenSpan, ManifestFileRole.Sketch, pool);
    }

    /// <summary>The survivors of a one-block loss: the triples with the lost block's items removed — the item set a peer that also lost the block would hold.</summary>
    /// <param name="triples">The full triple set.</param>
    /// <param name="lostBlock">The lost block index.</param>
    /// <param name="blockItemCount">The items per block.</param>
    /// <returns>The triples outside the lost block.</returns>
    private static EncodedTriple[] WithoutBlock(EncodedTriple[] triples, int lostBlock, int blockItemCount)
    {
        int start = lostBlock * blockItemCount;
        int end = Math.Min(start + blockItemCount, triples.Length);
        List<EncodedTriple> survivors = [.. triples[..start]];
        survivors.AddRange(triples[end..]);

        return [.. survivors];
    }

    /// <summary>Wraps one pre-built peer source as the pass's provider seam.</summary>
    /// <param name="source">The source every invocation answers.</param>
    /// <returns>The bound provider delegate.</returns>
    private static ProvidePeerReconciliationSourceDelegate Provide(PeerReconciliationSource source)
    {
        return new FixedPeerSourceProvider(source).ProvideAsync;
    }

    /// <summary>Binds one pre-built peer source as the provider seam without a lexical closure: the source travels as instance state and <see cref="ProvideAsync"/> is the bound method group.</summary>
    /// <param name="source">The source every invocation answers.</param>
    private sealed class FixedPeerSourceProvider(PeerReconciliationSource source)
    {
        /// <summary>The source every invocation answers.</summary>
        private PeerReconciliationSource Source { get; } = source;

        /// <summary>Answers the fixed source; a fixed binding reads none of the seam's parameters.</summary>
        /// <param name="commitGeneration">The damaged generation under repair.</param>
        /// <param name="dictionaryEpoch">The generation's dictionary epoch.</param>
        /// <param name="cancellationToken">Unused by a fixed binding.</param>
        /// <returns>The fixed source.</returns>
        public ValueTask<PeerReconciliationSource?> ProvideAsync(long commitGeneration, long dictionaryEpoch, CancellationToken cancellationToken)
        {
            return new ValueTask<PeerReconciliationSource?>(Source);
        }
    }

    /// <summary>A repair pass over a generation that carries no parity recovers the single lost system-of-record block from a peer's verified sketch, re-ingests the healed system-of-record, and recovers exactly the original triple set.</summary>
    [TestMethod]
    public async Task PeerReconciliationRestoresACorruptSystemOfRecordBlock()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool dataSegmentDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(dataSegmentDetected, "Precondition: the system-of-record block must be detected corrupt.");

            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsTrue(repair.IsClean, "A peer-recoverable system-of-record block is fully recoverable.");
            Assert.IsEmpty(repair.NamedLosses);
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            Assert.IsTrue(ItemSegment.RunVerifyRound(restored.Image.Span).IsClean, "The healed system-of-record must verify clean.");
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            HashSet<EncodedTriple> recoveredSet = [.. recovered.Span];
            Assert.IsTrue(recoveredSet.SetEquals(triples), "The healed system-of-record must hold exactly the original triple set (a faithful recovery).");
            StorageTraceEvent reingested = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Reingested);
            Assert.AreEqual(ManifestFileRole.DataSegment.Code, reingested.RoleCode);
            Assert.AreEqual(30, reingested.ItemCount, "The healed system-of-record holds all 30 items.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A repair pass recovers the SHORT last block from a peer and re-ingests the full item set.</summary>
    [TestMethod]
    public async Task PeerReconciliationRestoresTheShortLastBlock()
    {
        EncodedTriple[] triples = SampleTriples(25);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.IsClean);
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            HashSet<EncodedTriple> recoveredSet = [.. recovered.Span];
            Assert.IsTrue(recoveredSet.SetEquals(triples), "The restored short-last-block segment must hold all 25 original triples.");
            StorageTraceEvent reingested = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Reingested);
            Assert.AreEqual(25, reingested.ItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The single-block cap: a capacity-for-one rung cannot recover two lost blocks from a peer, so both are named lost and nothing is re-derived.</summary>
    [TestMethod]
    public async Task PeerReconciliationDeclinesWhenMoreThanOneBlockIsLost()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A capacity-for-one peer rung cannot restore two lost blocks.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.HasCount(2, repair.NamedLosses);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A peer that also lacks the lost block's items recovers an empty difference, which does not match the lost block's item count, so the rung declines and the loss is named rather than half-healed.</summary>
    [TestMethod]
    public async Task PeerReconciliationDeclinesWhenThePeerLacksTheLostItems()
    {
        EncodedTriple[] triples = SampleTriples(30);
        EncodedTriple[] peerMissingTheBlock = WithoutBlock(triples, lostBlock: 1, blockItemCount: 10);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(peerMissingTheBlock, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.IsClean, "A peer missing the lost items cannot restore the block.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A symbol budget below the lost block's item count cannot fully peel the difference, so the decoder does not converge and the rung declines on the completeness gate — a partial peel never half-heals. This is the test that fails if the IsComplete term of the gate is dropped.</summary>
    [TestMethod]
    public async Task PeerReconciliationDeclinesWhenThePeelIsPartial()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            //Eight coded symbols cannot peel a ten-item difference, so the decoder cannot converge — IsComplete
            //is false even though the peer is faithful and would peel completely under a larger budget.
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool, symbolCap: 8)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.IsClean, "A partial peel must not heal the block.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A count-balanced diverged peer — one that lacks a surviving item AND lacks one of the lost block's items, so the symmetric difference still has the lost block's cardinality — is rejected by the peer-only direction guard, not healed: blindly concatenating that difference would duplicate the missing survivor and drop the unrecovered lost item. The rung declines and names the loss.</summary>
    [TestMethod]
    public async Task PeerReconciliationDeclinesWhenAPeerDivergedInACountBalancedWay()
    {
        EncodedTriple[] triples = SampleTriples(30);

        //The peer lacks survivor index 0 and one of the lost block's items (index 19), and carries the other
        //nine lost items (indices 10..18). Its symmetric difference with the survivors is then {item 0} (a
        //survivor the peer lacks) plus {items 10..18} (nine peer-only lost items) = ten items, matching the lost
        //block's count, so only the direction guard — not the count check — can reject it.
        List<EncodedTriple> peer = [];
        for(int i = 1; i < 30; i++)
        {
            if(i == 19)
            {
                continue;
            }

            peer.Add(triples[i]);
        }

        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource([.. peer], bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.IsClean, "A count-balanced diverged peer must be rejected, not healed into a duplicate-and-drop system-of-record.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>When a peer restores a lost system-of-record block AND a view is damaged in the same pass, the re-derived view folds the healed full item set, so the published system-of-record and view index the same triples.</summary>
    [TestMethod]
    public async Task RestoredSystemOfRecordAndCoDamagedViewStayConsistentViaPeer()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.IsClean);
            Assert.IsEmpty(repair.NamedLosses);
            RederivedArtifact restoredSegment = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restoredSegment.Image.Span, triplePool);
            HashSet<EncodedTriple> recoveredSet = [.. recovered.Span];
            Assert.IsTrue(recoveredSet.SetEquals(triples), "The peer-restored system-of-record must hold all 30 triples.");

            RederivedArtifact rederivedSidecar = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sidecar);
            ColumnarTripleIndex rebuilt = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(rederivedSidecar.Image), bytePool, triplePool);
            bool sidecarFaithful = new HashSet<EncodedTriple>(rebuilt.EnumerateTriples()).SetEquals(triples);
            Assert.IsTrue(sidecarFaithful, "The co-damaged sidecar must be re-derived from the peer-healed full item set, not the pruned feed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The peer-reconciliation path is pool-clean: a repair pass that recovers a lost block hands the healed image to the report (which keeps it rented) and returns every other rented buffer, so disposing the report returns EVERY buffer the pass rented from the repair pool — proven under a poisoning pool that counts outstanding rentals.</summary>
    [TestMethod]
    public async Task RestoringPeerReconciliationRepairPassReturnsEveryBufferOnReportDispose()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> imagePool = new();
        using PoisoningMemoryPool<byte> repairPool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, imagePool);
        using ArtifactImage sidecar = SidecarImage(triples, imagePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, imagePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, imagePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(repairPool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, imagePool)), null, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.IsTrue(repair.IsClean, "The lost block must be recovered from the peer.");
                Assert.IsGreaterThan(0, repairPool.OutstandingRentals, "The report must keep the healed image rented until it is disposed.");
            }
            finally
            {
                repair.Dispose();
            }

            Assert.AreEqual(0, repairPool.OutstandingRentals, "Disposing the report must return every buffer the peer-reconciliation repair pass rented from the repair pool.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Pins the rung's faithfulness boundary on a same-epoch peer that diverged INSIDE the lost region: the peer
    /// holds nine of the lost block's ten items plus one foreign item, so its symmetric difference with the
    /// survivors is count-balanced and entirely peer-only — every gate short of the sketch-residual verification
    /// accepts it, because subset-of-peer plus count-match is strictly weaker than recovering exactly the lost
    /// items. The healed set would silently drop the never-replicated lost item and adopt the foreign one, so it
    /// cannot reconcile emptily against the generation's own sketch: the rung declines and the whole block is
    /// named lost rather than published as the peer's content verbatim.
    /// </summary>
    [TestMethod]
    public async Task DivergedSupersetPeerIsDeclinedToANamedLoss()
    {
        EncodedTriple[] triples = SampleTriples(30);
        EncodedTriple foreign = EncodedTriple.FromEncoded(9999, 8888, 7777);
        List<EncodedTriple> peer = [];
        for(int i = 0; i < 30; i++)
        {
            if(i == 19)
            {
                continue;
            }

            peer.Add(triples[i]);
        }

        peer.Add(foreign);

        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource([.. peer], bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A diverged peer's healed set is not corroborated by the generation's own sketch, so the heal must decline.");
            Assert.IsEmpty(repair.RederivedArtifacts, "Nothing is published from an uncorroborated heal.");
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount, "The whole lost block is named lost rather than substituted with the peer's content.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Pins the rung's faithfulness boundary on the full-substitution shape: a same-epoch peer holding the
    /// survivors plus k fresh items disjoint from the lost block's k items. The symmetric difference is exactly
    /// the fresh items — count-balanced, entirely peer-only — so every gate short of the sketch-residual
    /// verification passes; the healed set would substitute the fresh items for the whole lost block. Its
    /// residual against the generation's own sketch is those twenty items, not empty, so the rung declines and
    /// the block is named lost. Same boundary as the diverged-superset pin, at maximal divergence: not one
    /// recovered item belongs to the lost block.
    /// </summary>
    [TestMethod]
    public async Task SubstitutionSupersetPeerIsDeclinedToANamedLoss()
    {
        EncodedTriple[] wide = SampleTriples(40);
        EncodedTriple[] triples = wide[..30];
        EncodedTriple[] survivors = [.. wide[..10], .. wide[20..30]];
        EncodedTriple[] fresh = wide[30..40];
        EncodedTriple[] peer = [.. survivors, .. fresh];

        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool detected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(detected, "Precondition: the lost system-of-record block must be detected.");

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(peer, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A substitution peer's healed set is not corroborated by the generation's own sketch, so the heal must decline.");
            Assert.IsEmpty(repair.RederivedArtifacts, "Nothing is published from an uncorroborated heal.");
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount, "The whole lost block is named lost rather than substituted with the peer's fresh items.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The rung trusts the generation's own record, never the peer: even a FAITHFUL peer is declined when the staged sketch was persisted over a different triple set, because the otherwise-perfect heal cannot reconcile emptily against a record that does not describe the system-of-record. The independent record is load-bearing, not advisory.</summary>
    [TestMethod]
    public async Task FaithfulPeerIsDeclinedWhenTheGenerationSketchDescribesDifferentContent()
    {
        EncodedTriple[] triples = SampleTriples(30);
        EncodedTriple[] other = [.. triples[..29], EncodedTriple.FromEncoded(9999, 8888, 7777)];
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(other, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A heal the generation's own sketch does not corroborate must decline, even from a faithful peer.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The rung requires the generation's own sketch as its independent record: a generation whose sketch cannot be loaded under the structural contract (here the fixture's opaque stub, whose 4-symbols-per-block geometry the contract-gated load refuses) leaves the rung unsourced, so even a faithful peer declines to a named loss — fail closed, never an unverifiable heal.</summary>
    [TestMethod]
    public async Task FaithfulPeerIsDeclinedWhenTheGenerationSketchIsNotLoadable()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "Without a loadable generation sketch there is no independent record, so the peer heal must decline.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A peer keyed to a foreign dictionary epoch is declined at the seam: encoded identifiers are epoch-relative, so its projected items live in a different encoding space and its sketch is incomparable with the local survivors' — even when the peer's logical content is faithful.</summary>
    [TestMethod]
    public async Task FaithfulPeerIsDeclinedWhenItsDictionaryEpochDiffers()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool, dictionaryEpoch: StagedDictionaryEpoch + 1)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A foreign-epoch peer's items are incomparable, so the rung must decline.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A co-damaged sketch cannot corroborate a peer heal: with a system-of-record block AND the sketch's front matter both corrupt at rest, the rung has no verified independent record, so the block is named lost and the sketch is re-derived from the pruned survivors — the pass never substitutes an unverifiable heal for the named loss.</summary>
    [TestMethod]
    public async Task PeerHealDeclinesWhenTheGenerationSketchIsCoDamaged()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = GenerationSketchImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        CorruptSketchChecksumField(sketch, block: 0);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool sketchDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Sketch.Code);
            Assert.IsTrue(sketchDetected, "Precondition: the co-damaged sketch must be detected corrupt.");

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), Provide(PeerSource(triples, bytePool)), null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A co-damaged sketch leaves the peer rung without a verified record, so the loss is named.");
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
            RederivedArtifact rederivedSketch = repair.RederivedArtifacts.Single();
            Assert.AreEqual(ManifestFileRole.Sketch, rederivedSketch.Role, "Only the sketch is re-derived, from the pruned survivors; the system-of-record is not healed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
