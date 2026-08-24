using System;
using System.Buffers;
using System.IO;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The scrub verify pass's manifest-entry attestation: before verifying an artifact's blocks, the pass binds
/// the opened image to the length and whole-image digest the manifest recorded for it, so a wrong image
/// substituted under a manifest-named file is a whole-artifact finding even though it is internally valid and
/// would pass every role verify — no load path would accept it, so neither does the scrub. The attestation
/// covers every named role, including one the scrub does not verify block-by-block, and never fires on an honest
/// staged generation whose recorded digests match its files. The fixture's builders and staging are shared via
/// <see cref="PersistenceStagingFixture"/>.
/// </summary>
[TestClass]
internal sealed class ScrubManifestEntryDigestTests
{
    /// <summary>A wrong-but-internally-valid image substituted under a manifest-named file is detected as a whole-artifact (front-matter) finding — the manifest's recorded whole-image digest does not match — and the role verify does not run over the condemned image.</summary>
    [TestMethod]
    public void SubstitutedInternallyValidImageIsDetectedAsAWholeArtifactFinding()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        using ArtifactImage sidecar = SidecarImage(SampleTriples(30), pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, pool, out string directory);

        //A different segment of the SAME item count serializes to the same length, so only the manifest's digest
        //binding — not its length — can tell it apart from the one the generation committed.
        using ArtifactImage substitute = SegmentImage(DifferentTriples(30), pool);
        try
        {
            Assert.IsTrue(ItemSegment.RunVerifyRound(substitute.Bytes).IsClean, "Precondition: the substitute image is internally valid, so only the manifest binding can detect it.");
            store.WriteStaged($"segment-{7:D20}.dat", substitute.Bytes);

            StorageTraceCapture trace = new();
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding finding = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.AreEqual(-1, finding.BlockIndex, "A wrong-image substitution is a whole-artifact finding, not a block finding.");
            Assert.IsTrue(finding.IsFrontMatter);
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.FrontMatterCorrupt && e.RoleCode == ManifestFileRole.DataSegment.Code));
            Assert.IsEmpty(
                trace.Events.Where(static e => e.RoleCode == ManifestFileRole.DataSegment.Code && (e.Kind == StorageTraceEventKind.BlockVerified || e.Kind == StorageTraceEventKind.BlockCorrupt)),
                "The role verify does not run over an image the entry-digest binding already condemned.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An image of a different length under a manifest-named file fails the entry's recorded length before any digest is recomputed, and is named a whole-artifact loss.</summary>
    [TestMethod]
    public void WrongLengthImageUnderAManifestNameIsDetected()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        using ArtifactImage sidecar = SidecarImage(SampleTriples(30), pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, pool, out string directory);

        //A forty-item segment is a longer, internally-valid image; its length alone does not match the manifest.
        using ArtifactImage longer = SegmentImage(SampleTriples(40), pool);
        try
        {
            Assert.AreNotEqual(segment.Length, longer.Length, "Precondition: the substitute image is a different length.");
            store.WriteStaged($"segment-{7:D20}.dat", longer.Bytes);

            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding finding = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.AreEqual(-1, finding.BlockIndex);
            Assert.IsTrue(finding.IsFrontMatter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A role the scrub does not verify block-by-block is attested against the manifest's digest all the same: substituting a stats artifact the generation names is detected as a whole-artifact finding, while the clean system-of-record beside it still verifies.</summary>
    [TestMethod]
    public void SubstitutedUnverifiedRoleIsAttestedByTheEntryDigestGate()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        byte[] stats = FillPattern(48, seed: 3);
        FileSystemPersistenceStore store = StageSegmentAndStats(7, segment, stats, pool, out string directory);
        try
        {
            //Overwrite the stats file with a same-length, different-content image the scrub never block-verifies.
            store.WriteStaged($"stats-{7:D20}.sts", FillPattern(48, seed: 200));

            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding finding = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.Stats.Code);
            Assert.AreEqual(-1, finding.BlockIndex);
            Assert.IsTrue(finding.IsFrontMatter, "The manifest names the stats artifact, so the scrub attests it as a whole-artifact loss.");
            Assert.IsEmpty(report.CorruptBlocks.Where(static f => f.RoleCode == ManifestFileRole.DataSegment.Code), "The honest system-of-record beside the substituted stats still verifies.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An honest staged generation whose recorded digests match its files verifies wholly clean through the entry-digest gate — the no-regression guard: the gate binds the manifest to the disk, it does not flag artifacts the generation itself wrote.</summary>
    [TestMethod]
    public void CleanStagedGenerationVerifiesCleanThroughTheEntryDigestGate()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(30);
        using ArtifactImage segment = SegmentImage(triples, pool);
        using ArtifactImage sidecar = SidecarImage(triples, pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        using ArtifactImage parity = ParityImage(triples, pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(report.IsClean, "An honest generation's files match their recorded digests, so the entry-digest gate never fires.");
            Assert.IsEmpty(report.CorruptBlocks);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Stages a clean system-of-record and an opaque stats artifact into a fresh temp-dir store and commits a manifest naming both — the stats role is one the scrub does not verify block-by-block, so it exercises the entry-digest attestation alone.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image.</param>
    /// <param name="stats">The opaque stats artifact bytes.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    private static FileSystemPersistenceStore StageSegmentAndStats(long generation, ArtifactImage segment, ReadOnlySpan<byte> stats, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-entrydigest-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        int width = ChecksumAlgorithm.XxHash3.ByteWidth;

        string segmentName = $"segment-{generation:D20}.dat";
        string statsName = $"stats-{generation:D20}.sts";
        store.WriteStaged(segmentName, segment.Bytes);
        store.WriteStaged(statsName, stats);

        using IMemoryOwner<byte> segmentDigest = Digest(segment.Bytes, pool);
        using IMemoryOwner<byte> statsDigest = Digest(stats, pool);
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segment.Length, segmentDigest.Memory[..width]),
            new(ManifestFileRole.Stats, statsName, 0, stats.Length, statsDigest.Memory[..width]),
        ];
        new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
            .Commit(new Manifest(generation, generation * 11, generation * 13, entries));

        return store;
    }

    /// <summary>A line of triples distinct from <see cref="PersistenceStagingFixture.SampleTriples"/> at the same count, so a substitute segment serializes to the same length yet carries a different whole-image digest.</summary>
    /// <param name="count">The triple count.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] DifferentTriples(uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(i + 1000, (i * 7) + 3, (i * 13) + 5);
        }

        return triples;
    }

    /// <summary>A deterministic byte pattern of the given length, keyed by a seed so two calls differ in content but not length.</summary>
    /// <param name="length">The pattern length.</param>
    /// <param name="seed">The seed that shifts the pattern so a second call yields different bytes.</param>
    /// <returns>The pattern bytes.</returns>
    private static byte[] FillPattern(int length, int seed)
    {
        byte[] bytes = new byte[length];
        for(int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 17) + seed);
        }

        return bytes;
    }
}
