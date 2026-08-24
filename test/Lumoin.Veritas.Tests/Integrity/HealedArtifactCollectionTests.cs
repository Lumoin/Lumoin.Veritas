using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// A repair publish stages its healed images (<c>{role}-{generation}</c>) and, when it names losses, a loss
/// record (<c>losses-{generation}</c>) under names outside the durable store's own artifact prefixes. These pin
/// that the store's superseded-artifact collector reclaims those healed leftovers under the same retention
/// window as ordinary generation artifacts once they fall out of it, and never while a retained generation still
/// names them — so a self-heal does not leak disk yet a live generation's artifacts are never collected.
/// </summary>
[TestClass]
internal sealed class HealedArtifactCollectionTests
{
    /// <summary>The triple count the staged generation carries (three ten-item system-of-record blocks).</summary>
    private const uint TripleCount = 30;

    /// <summary>The sketch symbol count the staged generation carries.</summary>
    private const int SketchSymbolCount = 40;

    /// <summary>The system-of-record block the loss test corrupts, so the repair names its item range lost and the heal co-versions a loss record.</summary>
    private const int CorruptBlock = 1;

    /// <summary>The staged generation's system-of-record block count.</summary>
    private const int SegmentBlockCount = 3;

    /// <summary>The first committed generation the test stages; the heal supersedes it with the next.</summary>
    private const long Generation = 7;

    /// <summary>The retained generation window the durable store and the coordinator's manifest writer both keep.</summary>
    private const int RetainedGenerations = 4;

    /// <summary>
    /// A heal stages a healed sidecar and a loss record at a generation; subsequent durable persists keep both
    /// while the heal generation stays in the retention window and collect both once it falls out, while every
    /// artifact a retained generation still names survives.
    /// </summary>
    [TestMethod]
    public async Task SupersededHealedImagesAndLossRecordsAreCollectedWhileRetainedOnesSurvive()
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
            using(RepairPassReport repair = await RunRepairAsync(store, bytePool, triplePool, clock).ConfigureAwait(false))
            {
                Assert.IsNotEmpty(repair.RederivedArtifacts, "The damaged sidecar re-derives.");
                Assert.IsNotEmpty(repair.NamedLosses, "The corrupt system-of-record block is named lost.");
                GenerationCommitReport report = new GenerationCommitCoordinator(store, ChecksumAlgorithm.XxHash3, bytePool, RetainedGenerations, null, null, clock).Commit(repair, Guid.Empty);
                Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome);
            }

            string healedSidecarPrefix = ManifestFileRole.Sidecar.Name + "-";
            Assert.HasCount(1, store.List(healedSidecarPrefix), "The heal staged one healed sidecar image.");
            Assert.HasCount(1, store.List(HealedArtifactNaming.LossRecordPrefix), "The heal staged one loss record.");

            DurableSystemOfRecordStore durable = new(store, bytePool);
            (TermDictionary dictionary, EncodedTriple[] persistTriples) = MinimalGraph();

            //Persist up to the last generation whose window still includes the healed one (generation 8): its
            //healed sidecar and loss record must survive, because a retained generation still names them.
            durable.Persist(dictionary, persistTriples);
            durable.Persist(dictionary, persistTriples);
            durable.Persist(dictionary, persistTriples);
            Assert.HasCount(1, store.List(healedSidecarPrefix), "A healed image a retained generation still names is not collected.");
            Assert.HasCount(1, store.List(HealedArtifactNaming.LossRecordPrefix), "A loss record a retained generation still names is not collected.");

            //One more persist drops the healed generation out of the window: its healed image and loss record are
            //now superseded and collected, while the retention window's own artifacts survive.
            durable.Persist(dictionary, persistTriples);
            Assert.IsEmpty(store.List(healedSidecarPrefix), "A superseded healed image is collected.");
            Assert.IsEmpty(store.List(HealedArtifactNaming.LossRecordPrefix), "A superseded loss record is collected.");
            Assert.IsNotNull(store.Read(RecordArtifactName(12)), "The live generation's system-of-record survives.");
            Assert.IsNotNull(store.Read(RecordArtifactName(9)), "A system-of-record the retention window still names survives.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    /// <summary>Builds a minimal dictionary and single triple a durable persist advances a generation with.</summary>
    /// <returns>The dictionary and its triples.</returns>
    private static (TermDictionary Dictionary, EncodedTriple[] Triples) MinimalGraph()
    {
        TermDictionary dictionary = new(0x99);
        TermId s = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/s")));
        TermId p = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/p")));
        TermId o = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/o")));

        return (dictionary, [new EncodedTriple(s, p, o)]);
    }
}
