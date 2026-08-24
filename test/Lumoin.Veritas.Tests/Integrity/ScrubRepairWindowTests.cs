using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// Pins the verify-to-repair window contract: the repair pass routes its rungs by the verify pass's report but
/// re-reads the system-of-record fresh, and re-establishes the damage set from that read so a block that rotted
/// between the two passes is not silently excluded from the re-derived views. The window-rotted block descends
/// the repair ladder exactly like a report-named block — restored from parity or peer when a source is present,
/// otherwise named lost — so every re-derived view folds the healed-or-named item set, and no view is published
/// pruned-but-clean. The generation the fixture stages carries a co-versioned parity, so the window-rotted block
/// is healed rather than named, and the co-damaged sidecar is re-derived from the healed full item set.
/// </summary>
[TestClass]
internal sealed class ScrubRepairWindowTests
{
    /// <summary>A sidecar re-derive whose source segment rots between the verify and repair passes surfaces the rotted block: the repair re-reads the system-of-record, folds the newly-excluded block into the damage set, parity-restores it, and re-derives the co-damaged sidecar from the healed full item set — the published sidecar holds every item, never a silently-pruned subset.</summary>
    [TestMethod]
    public async Task SegmentRotInVerifyToRepairWindowIsHealedNotSilentlyPruned()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        CorruptSidecarFrontMatter(sidecar);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();

            //The verify pass names ONLY the sidecar; the segment is clean on disk at this instant.
            ScrubRoundReport verify1 = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);
            Assert.IsNotEmpty(verify1.CorruptBlocks, "Precondition: sidecar damage detected.");
            Assert.IsTrue(verify1.CorruptBlocks.All(static f => f.RoleCode == ManifestFileRole.Sidecar.Code), "Precondition: only the sidecar is named.");

            //The window: a system-of-record block rots after the verify pass, before the repair pass reads it.
            CorruptSegmentBlock(segment, block: 1, blockCount: 3);
            store.WriteStaged($"segment-{7:D20}.dat", segment.Bytes);

            //The repair pass re-reads the segment fresh, folds the newly-excluded block into the damage set, and
            //descends the ladder: the co-versioned parity restores the block, so nothing is named lost.
            GenerationCommitReport commit1;
            using(RepairPassReport repair1 = await ScrubRound.RunRepairPassAsync(store, verify1, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, clock, null, null, CancellationToken.None).ConfigureAwait(false))
            {
                Assert.IsFalse(repair1.Refused, "The pass acts on the re-established damage set.");
                Assert.IsEmpty(repair1.NamedLosses, "The window-rotted block is parity-restored, so no loss is named.");
                Assert.IsTrue(repair1.IsClean, "The pass reports itself fully recoverable.");

                //The window-rotted system-of-record block is surfaced and parity-restored, not silently pruned.
                RederivedArtifact restoredSegment = repair1.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
                using DecodedItemSegment healedSegment = ItemSegment.ReadFrom(restoredSegment.Image.Span, triplePool);
                Assert.AreEqual(30, healedSegment.Length, "The window-rotted block is restored, so the healed system-of-record holds all 30 items.");

                //The co-damaged sidecar is re-derived from the healed full item set, so it indexes every item.
                RederivedArtifact sidecarArtifact = repair1.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sidecar);
                ColumnarTripleIndex rebuilt = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(sidecarArtifact.Image), bytePool, triplePool);
                Assert.HasCount(30, rebuilt.EnumerateTriples().ToList(), "The re-derived sidecar folds the healed full item set, not the block-excluded feed.");

                commit1 = new GenerationCommitCoordinator(store, ChecksumAlgorithm.XxHash3, bytePool, 4, null, null, clock).Commit(repair1, Guid.Empty);
            }

            Assert.AreEqual(GenerationCommitOutcome.Committed, commit1.Outcome, "The healed generation is published.");
            Assert.AreEqual(8, commit1.Generation);

            //The next round finds nothing: the segment was healed in the window round, not left to rot forward.
            ScrubRoundReport verify2 = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);
            Assert.IsEmpty(verify2.CorruptBlocks, "The healed generation verifies wholly clean.");

            //Final state: the system-of-record and the query sidecar both index the full item set.
            RecoveryResult recovery = new ManifestRecovery(store).Recover();
            Assert.AreEqual(8, recovery.Manifest.CommitGeneration);
            ManifestEntry segmentEntry = recovery.Manifest.Entries.Single(static e => e.Role == ManifestFileRole.DataSegment);
            ManifestEntry sidecarEntry = recovery.Manifest.Entries.Single(static e => e.Role == ManifestFileRole.Sidecar);
            byte[] finalSegment = store.Read(segmentEntry.FileName)!;
            byte[] finalSidecar = store.Read(sidecarEntry.FileName)!;
            using DecodedItemSegment decoded = ItemSegment.ReadFrom(finalSegment, triplePool);
            ColumnarTripleIndex finalIndex = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(finalSidecar), bytePool, triplePool);
            Assert.AreEqual(30, decoded.Length, "The parity heal restored the full system-of-record.");
            Assert.HasCount(30, finalIndex.EnumerateTriples().ToList(), "The query sidecar indexes the full item set, healed rather than silently pruned.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
