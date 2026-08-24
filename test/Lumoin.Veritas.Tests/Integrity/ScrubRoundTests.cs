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
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The storage scrub's verify pass: it holds a committed manifest snapshot, verifies each artifact the
/// generation names through the format-neutral seam, emits a trace event per verdict, and records every
/// at-rest failure — a clean generation verifies every block; a corrupt system-of-record or sketch block is
/// detected and named with its role; a missing artifact is a whole-artifact loss. Coordination is none — the
/// pass is synchronous and deterministic, so there are no waits. The fixture's builders and corruptions are
/// shared via <see cref="PersistenceStagingFixture"/>; this class keeps its own two-artifact staging (a
/// generation that may omit the sketch) to exercise the missing-artifact path, and a staging that adds a
/// local-parity sidecar to exercise the scrub's parity dispatch.
/// </summary>
[TestClass]
internal sealed class ScrubRoundTests
{
    /// <summary>Stages a data segment and an optional sketch into a fresh temp-dir store and commits a generation manifest naming them; a <see langword="null"/> sketch stages a generation whose sketch file is absent (a missing artifact).</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image (possibly corrupted by the caller).</param>
    /// <param name="sketch">The sketch image, or <see langword="null"/> to omit staging it.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    private static FileSystemPersistenceStore StageGeneration(long generation, ArtifactImage segment, ArtifactImage? sketch, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-scrub-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        int width = ChecksumAlgorithm.XxHash3.ByteWidth;

        string segmentName = $"segment-{generation:D20}.dat";
        string sketchName = $"sketch-{generation:D20}.skt";
        store.WriteStaged(segmentName, segment.Bytes);
        if(sketch is not null)
        {
            store.WriteStaged(sketchName, sketch.Bytes);
        }

        ReadOnlySpan<byte> sketchDigestSource = sketch is null ? segment.Bytes : sketch.Bytes;
        using IMemoryOwner<byte> segmentDigest = Digest(segment.Bytes, pool);
        using IMemoryOwner<byte> sketchDigest = Digest(sketchDigestSource, pool);
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segment.Length, segmentDigest.Memory[..width]),
            new(ManifestFileRole.Sketch, sketchName, 0, sketch?.Length ?? 0, sketchDigest.Memory[..width]),
        ];
        new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
            .Commit(new Manifest(generation, generation * 11, generation * 13, entries));

        return store;
    }

    /// <summary>Stages a data segment, a sketch, and a local-parity sidecar into a fresh temp-dir store and commits a generation manifest naming all three, so the scrub walks a generation that carries a parity artifact.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image.</param>
    /// <param name="sketch">The sketch image.</param>
    /// <param name="parity">The local-parity image (possibly corrupted by the caller).</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    private static FileSystemPersistenceStore StageGenerationWithParity(long generation, ArtifactImage segment, ArtifactImage sketch, ArtifactImage parity, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-scrub-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        int width = ChecksumAlgorithm.XxHash3.ByteWidth;

        string segmentName = $"segment-{generation:D20}.dat";
        string sketchName = $"sketch-{generation:D20}.skt";
        string parityName = $"parity-{generation:D20}.par";
        store.WriteStaged(segmentName, segment.Bytes);
        store.WriteStaged(sketchName, sketch.Bytes);
        store.WriteStaged(parityName, parity.Bytes);

        using IMemoryOwner<byte> segmentDigest = Digest(segment.Bytes, pool);
        using IMemoryOwner<byte> sketchDigest = Digest(sketch.Bytes, pool);
        using IMemoryOwner<byte> parityDigest = Digest(parity.Bytes, pool);
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segment.Length, segmentDigest.Memory[..width]),
            new(ManifestFileRole.Sketch, sketchName, 0, sketch.Length, sketchDigest.Memory[..width]),
            new(ManifestFileRole.Parity, parityName, 0, parity.Length, parityDigest.Memory[..width]),
        ];
        new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
            .Commit(new Manifest(generation, generation * 11, generation * 13, entries));

        return store;
    }

    /// <summary>A clean generation verifies every block, reports clean, and emits only verified verdicts.</summary>
    [TestMethod]
    public void CleanGenerationVerifiesEveryBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sketch, pool, out string directory);
        try
        {
            StorageTraceCapture trace = new();
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(report.IsClean, "A clean generation must scrub clean.");
            Assert.AreEqual(7, report.CommitGeneration);
            Assert.IsFalse(report.IsDegradedSnapshot);
            //3 system-of-record blocks (30 triples / 10) + 3 sketch blocks (10 symbols / 4).
            Assert.AreEqual(6, report.BlocksVerified);
            Assert.IsEmpty(report.CorruptBlocks);
            Assert.IsTrue(trace.Events.All(static e => e.Kind == StorageTraceEventKind.BlockVerified), "Every emitted verdict must be a verified block.");
            Assert.HasCount(6, trace.Events);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt system-of-record block is detected, named with its role and block index, and emitted as a corrupt verdict — while every other block still verifies.</summary>
    [TestMethod]
    public void CorruptSystemOfRecordBlockIsDetectedAndNamed()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        using ArtifactImage sketch = SketchImage(10, pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sketch, pool, out string directory);
        try
        {
            StorageTraceCapture trace = new();
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            Assert.HasCount(1, report.CorruptBlocks);
            ScrubBlockFinding finding = report.CorruptBlocks[0];
            Assert.AreEqual(ManifestFileRole.DataSegment.Code, finding.RoleCode);
            Assert.AreEqual(1, finding.BlockIndex);
            Assert.IsFalse(finding.IsFrontMatter);
            //The other two system-of-record blocks plus the three sketch blocks still verify.
            Assert.AreEqual(5, report.BlocksVerified);
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.BlockCorrupt));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt sketch block is detected and named with the sketch role.</summary>
    [TestMethod]
    public void CorruptSketchBlockIsDetected()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        CorruptSketchBlock(sketch, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sketch, pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            Assert.HasCount(1, report.CorruptBlocks);
            Assert.AreEqual(ManifestFileRole.Sketch.Code, report.CorruptBlocks[0].RoleCode);
            Assert.AreEqual(1, report.CorruptBlocks[0].BlockIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An artifact the manifest names but the store no longer holds is a whole-artifact loss, named with block index -1.</summary>
    [TestMethod]
    public void MissingArtifactIsNamedAsAWholeArtifactLoss()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sketch: null, pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding missing = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.Sketch.Code);
            Assert.AreEqual(-1, missing.BlockIndex);
            Assert.IsTrue(missing.IsFrontMatter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Framing damage (a magic mismatch) makes the artifact unreadable, named as a whole-artifact loss rather than crashing the pass.</summary>
    [TestMethod]
    public void FramingDamageIsNamedAsAWholeArtifactLoss()
    {
        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(30), pool);
        segment.WritableBytes[0] ^= 0xFF;
        using ArtifactImage sketch = SketchImage(10, pool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sketch, pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding framing = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.AreEqual(-1, framing.BlockIndex);
            Assert.IsTrue(framing.IsFrontMatter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A clean generation that carries a local-parity sidecar verifies the parity block too, through the scrub's parity dispatch.</summary>
    [TestMethod]
    public void CleanGenerationVerifiesTheParityBlock()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(30);
        using ArtifactImage segment = SegmentImage(triples, pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        using ArtifactImage parity = ParityImage(triples, pool);
        FileSystemPersistenceStore store = StageGenerationWithParity(7, segment, sketch, parity, pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(report.IsClean);
            //3 system-of-record blocks (30 / 10) + 3 sketch blocks (10 / 4) + 1 parity block.
            Assert.AreEqual(7, report.BlocksVerified);
            Assert.IsEmpty(report.CorruptBlocks);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt parity block is detected and named with the parity role through the scrub's parity dispatch, while every other block still verifies.</summary>
    [TestMethod]
    public void CorruptParityBlockIsDetectedAndNamed()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(30);
        using ArtifactImage segment = SegmentImage(triples, pool);
        using ArtifactImage sketch = SketchImage(10, pool);
        using ArtifactImage parity = ParityImage(triples, pool);
        CorruptParityBlock(parity);
        FileSystemPersistenceStore store = StageGenerationWithParity(7, segment, sketch, parity, pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            Assert.HasCount(1, report.CorruptBlocks);
            ScrubBlockFinding finding = report.CorruptBlocks[0];
            Assert.AreEqual(ManifestFileRole.Parity.Code, finding.RoleCode);
            Assert.AreEqual(0, finding.BlockIndex);
            Assert.IsFalse(finding.IsFrontMatter);
            //The 3 system-of-record blocks and 3 sketch blocks still verify.
            Assert.AreEqual(6, report.BlocksVerified);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
