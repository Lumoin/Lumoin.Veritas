using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// with no parity sidecar to restore it — recovers the lost items from a peer replica's verified sketch, re-ingests
/// the healed system-of-record, and re-derives any co-damaged view from the healed full item set, while more than
/// one lost block or a peer that lacks the lost items descends to a named loss. The pass drives the core's public
/// repair ladder; the peer's sketch and the rateless recover/invert seams are bound from the replication library,
/// so the core takes no replication dependency. Geometry is the fixture's (10-item blocks, 64-byte aligned,
/// XxHash3); every buffer is pool-rented.
/// </summary>
[TestClass]
internal sealed class PeerReconciliationRepairTests
{
    /// <summary>The symbol budget both the survivor and peer sketches are built at — far above any single block's item count, so a faithful peer always peels the lost block completely.</summary>
    private const int SymbolCap = 1300;

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
            RatelessSketchCodec.Encode);
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
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolCap, ChecksumAlgorithm.XxHash3, pool, RatelessSketchCodec.Encode, writer);

        return SketchPersistence.LoadVerifiedSketch(writer.WrittenSpan, SketchContract.Structural);
    }

    /// <summary>Builds a peer-reconciliation source over a peer replica's full triple set: its verified sketch, the rateless recover seam, the structural inverse, and the symbol cap. The cap budgets both the peer sketch and (through the source) the survivor sketch and the recovery.</summary>
    /// <param name="peerTriples">The peer replica's full triple set.</param>
    /// <param name="pool">The pool the peer sketch persist rents from.</param>
    /// <param name="symbolCap">The symbol budget; defaults to the generous <see cref="SymbolCap"/>, lowered by a test that drives a partial peel.</param>
    /// <returns>The peer-reconciliation source.</returns>
    private static PeerReconciliationSource PeerSource(EncodedTriple[] peerTriples, MemoryPool<byte> pool, int symbolCap = SymbolCap)
    {
        return new PeerReconciliationSource(PeerSketch(peerTriples, pool, symbolCap), RatelessSketchCodec.Recover, StructuralReconciliationProjection.Inversion, symbolCap);
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

    /// <summary>A repair pass over a generation that carries no parity recovers the single lost system-of-record block from a peer's verified sketch, re-ingests the healed system-of-record, and recovers exactly the original triple set.</summary>
    [TestMethod]
    public void PeerReconciliationRestoresACorruptSystemOfRecordBlock()
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
            bool dataSegmentDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(dataSegmentDetected, "Precondition: the system-of-record block must be detected corrupt.");

            StorageTraceCapture trace = new();
            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), PeerSource(triples, bytePool));

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
    public void PeerReconciliationRestoresTheShortLastBlock()
    {
        EncodedTriple[] triples = SampleTriples(25);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            StorageTraceCapture trace = new();
            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), PeerSource(triples, bytePool));

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
    public void PeerReconciliationDeclinesWhenMoreThanOneBlockIsLost()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), PeerSource(triples, bytePool));

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
    public void PeerReconciliationDeclinesWhenThePeerLacksTheLostItems()
    {
        EncodedTriple[] triples = SampleTriples(30);
        EncodedTriple[] peerMissingTheBlock = WithoutBlock(triples, lostBlock: 1, blockItemCount: 10);
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

            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), PeerSource(peerMissingTheBlock, bytePool));

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
    public void PeerReconciliationDeclinesWhenThePeelIsPartial()
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

            //Eight coded symbols cannot peel a ten-item difference, so the decoder cannot converge — IsComplete
            //is false even though the peer is faithful and would peel completely under a larger budget.
            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), PeerSource(triples, bytePool, symbolCap: 8));

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
    public void PeerReconciliationDeclinesWhenAPeerDivergedInACountBalancedWay()
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
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), PeerSource([.. peer], bytePool));

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
    public void RestoredSystemOfRecordAndCoDamagedViewStayConsistentViaPeer()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            using RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), PeerSource(triples, bytePool));

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
    public void RestoringPeerReconciliationRepairPassReturnsEveryBufferOnReportDispose()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> imagePool = new();
        using PoisoningMemoryPool<byte> repairPool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, imagePool);
        using ArtifactImage sidecar = SidecarImage(triples, imagePool);
        using ArtifactImage sketch = SketchImage(10, imagePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, imagePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, PeerRepairConfig(repairPool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), PeerSource(triples, imagePool));
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
}
