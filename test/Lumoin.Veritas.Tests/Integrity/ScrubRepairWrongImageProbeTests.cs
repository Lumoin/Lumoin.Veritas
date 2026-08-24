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
/// The repair pass's own manifest binding on the system-of-record: a wrong-but-internally-valid substitution
/// walks clean block by block, so per-block checksums alone would let the pass report itself fully recovered
/// while re-deriving views from a foreign item set. The pass re-checks the manifest's whole-image digest over
/// the block-clean re-read image and declines uniformly (SystemOfRecordUnreadable) on a mismatch — a
/// substituted image is never healed, never silently accepted, and never a re-derive source.
/// </summary>
[TestClass]
internal sealed class ScrubRepairWrongImageProbeTests
{
    /// <summary>A wrong-but-internally-valid segment substituted under the manifest-named file: verify condemns it as a whole-artifact DataSegment finding, and the repair pass refuses rather than reporting recovered a loss it neither healed nor named.</summary>
    [TestMethod]
    public async Task WrongImageSubstitutionRefusesTheRepairPass()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), bytePool);
        using ArtifactImage sidecar = SidecarImage(SampleTriples(30), bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);

        using ArtifactImage substitute = SegmentImage(WrongTriples(30), bytePool);
        try
        {
            //Precondition: the substitute is a same-length, internally-valid segment of DIFFERENT triples — every
            //per-block checksum passes, so only the manifest whole-image digest binding can tell it apart.
            Assert.AreEqual(segment.Length, substitute.Length, "Precondition: the substitute is the same length.");
            Assert.IsTrue(ItemSegment.RunVerifyRound(substitute.Bytes).IsClean, "Precondition: the substitute is internally valid.");
            store.WriteStaged($"segment-{7:D20}.dat", substitute.Bytes);

            FakeTimeProvider clock = new();
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);

            //The verify pass condemns the wrong image as a whole-artifact DataSegment loss (fix 3a).
            ScrubBlockFinding dataFinding = verify.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.AreEqual(-1, dataFinding.BlockIndex);
            Assert.IsTrue(dataFinding.IsFrontMatter, "The verify pass condemns the wrong image as a whole-artifact loss.");

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, clock, null, null, CancellationToken.None).ConfigureAwait(false);

            //A block-clean image that fails the manifest's whole-image binding is a substituted image: no rung
            //can heal from it and no re-derive may fold it, so the pass declines uniformly rather than
            //reporting recovered a loss it neither healed nor named.
            Assert.IsTrue(repair.Refused, "The pass must refuse a substituted system-of-record.");
            Assert.IsFalse(repair.IsClean, "A refused pass is never clean.");
            Assert.IsEmpty(repair.RederivedArtifacts, "Nothing is healed or re-derived from a substituted image.");
            Assert.IsEmpty(repair.NamedLosses, "A refusal acts on nothing; the verify finding remains the surfaced verdict.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A wrong-image substitution co-damaged with the sidecar: the refusal covers every role uniformly, so the sidecar is NOT re-derived from the foreign item set — a substituted image is never a re-derive source for any published view.</summary>
    [TestMethod]
    public async Task WrongImageSubstitutionNeverRederivesViewsFromForeignItems()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), bytePool);
        using ArtifactImage sidecar = SidecarImage(SampleTriples(30), bytePool);
        CorruptSidecarFrontMatter(sidecar);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);

        EncodedTriple[] wrong = WrongTriples(30);
        using ArtifactImage substitute = SegmentImage(wrong, bytePool);
        try
        {
            store.WriteStaged($"segment-{7:D20}.dat", substitute.Bytes);

            FakeTimeProvider clock = new();
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);
            Assert.Contains(static f => f.RoleCode == ManifestFileRole.DataSegment.Code, verify.CorruptBlocks, "Precondition: the wrong segment is condemned.");
            Assert.Contains(static f => f.RoleCode == ManifestFileRole.Sidecar.Code, verify.CorruptBlocks, "Precondition: the sidecar is co-damaged.");

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, clock, null, null, CancellationToken.None).ConfigureAwait(false);

            //The refusal is uniform: even the co-damaged, ordinarily re-derivable sidecar is not rebuilt,
            //because the only available item source is the substituted image's foreign set.
            Assert.IsTrue(repair.Refused, "The pass must refuse the substituted system-of-record before any re-derive runs.");
            Assert.IsFalse(repair.IsClean, "A refused pass is never clean.");
            Assert.IsEmpty(repair.RederivedArtifacts, "No view is re-derived from the foreign item set.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A line of triples distinct from <see cref="PersistenceStagingFixture.SampleTriples"/> at the same count, so a substitute segment serializes to the same length yet carries a different whole-image digest.</summary>
    /// <param name="count">The triple count.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] WrongTriples(uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(i + 1000, (i * 7) + 3, (i * 13) + 5);
        }

        return triples;
    }
}
