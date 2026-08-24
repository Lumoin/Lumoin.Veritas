using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Tests.Integrity;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The manifest + CURRENT-pointer atomic-publish commit point and its recovery, exercised over a real
/// temp-directory <see cref="FileSystemPersistenceStore"/> with deterministic, timer-free fault
/// injection. A clean commit recovers the same generation; a publish interrupted at the commit point
/// leaves the prior committed generation wholly in force (<see cref="PersistenceInvariant.PublishIsAtomic"/>);
/// recovery follows the CURRENT pointer to the latest committed generation, never the highest staged;
/// at-rest rot of the live CURRENT falls back to a retained copy; only when no CURRENT survives does
/// the named-degraded direct scan run. The manifest and CURRENT images round-trip identically across
/// the checksum selections, and a foreign or truncated image is refused rather than trusted
/// (<see cref="PersistenceInvariant.EpochConsistency"/>).
/// </summary>
[TestClass]
internal sealed class ManifestPersistenceTests
{
    /// <summary>The header byte offset of the checksum-algorithm id in both the manifest and the CURRENT pointer image: magic (8) + major (1) + minor (1).</summary>
    private const int ChecksumAlgorithmIdOffset = 8 + 1 + 1;

    /// <summary>Creates a fresh, uniquely-named temp directory for one test's store.</summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        return Directory.CreateTempSubdirectory("veritas-manifest-").FullName;
    }

    /// <summary>A directory durability barrier that does nothing — the injected substitute that keeps the tests platform-independent and timer-free.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a sample manifest generation with two entries whose checksum widths match the algorithm, so the image is well-formed for the selection under test.</summary>
    /// <param name="generation">The commit generation; also seeds the entries' epochs and checksum bytes.</param>
    /// <param name="checksum">The checksum algorithm, or <see langword="null"/> for none.</param>
    /// <returns>The manifest.</returns>
    private static Manifest SampleManifest(long generation, ChecksumAlgorithm? checksum)
    {
        int width = checksum?.ByteWidth ?? 0;
        byte[] segmentChecksum = new byte[width];
        byte[] sidecarChecksum = new byte[width];
        for(int i = 0; i < width; i++)
        {
            segmentChecksum[i] = (byte)(generation + i);
            sidecarChecksum[i] = (byte)((generation * 3) - i);
        }

        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, $"segment-{generation:D20}.dat", 0, 4096, segmentChecksum),
            new(ManifestFileRole.Sidecar, $"sidecar-{generation:D20}.idx", 0, 8192, sidecarChecksum),
        ];

        return new Manifest(generation, generation * 11, generation * 13, entries);
    }

    /// <summary>Commits one sample generation into <paramref name="store"/> through a writer with the given checksum and retention.</summary>
    /// <param name="store">The store committed into.</param>
    /// <param name="pool">The pool the writer rents staging buffers from.</param>
    /// <param name="generation">The commit generation.</param>
    /// <param name="checksum">The checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="retainedCount">The retained CURRENT-copy window.</param>
    private static void Commit(PersistenceStore store, MemoryPool<byte> pool, long generation, ChecksumAlgorithm? checksum, int retainedCount)
    {
        ManifestWriter writer = new(store, checksum, pool, retainedCount);
        writer.Commit(SampleManifest(generation, checksum));
    }

    /// <summary>Reads an artifact's bytes, asserting it exists.</summary>
    /// <param name="store">The store read from.</param>
    /// <param name="name">The artifact name.</param>
    /// <returns>The bytes.</returns>
    private static byte[] ReadExisting(PersistenceStore store, string name)
    {
        byte[]? bytes = store.Read(name);
        Assert.IsNotNull(bytes, $"The artifact '{name}' is missing.");

        return bytes;
    }

    /// <summary>Serializes a manifest into a buffer rented from the caller's pool and returns it as a pooled, owned image — the mutable fixture the refused-image cells damage in place — rather than copying the bytes out to a loose array.</summary>
    /// <param name="manifest">The manifest to serialize.</param>
    /// <param name="checksum">The checksum algorithm, or <see langword="null"/> for none.</param>
    /// <param name="imagePool">The pool the image buffer is rented from; the returned image owns the buffer and returns it on dispose.</param>
    /// <returns>The pooled image; the caller disposes it.</returns>
    private static ArtifactImage ManifestImage(Manifest manifest, ChecksumAlgorithm? checksum, MemoryPool<byte> imagePool)
    {
        int size = manifest.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = imagePool.Rent(size);
        manifest.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.DataSegment);
    }

    /// <summary>A clean commit recovers the same generation it committed, by following the live CURRENT, not degraded.</summary>
    [TestMethod]
    public void CleanPublishRecoversTheSameGenerationNotDegraded()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 7, ChecksumAlgorithm.XxHash3, retainedCount: 4);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(7, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);
            Assert.HasCount(2, result.Manifest.Entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>I4: a publish that fails at the commit point — modelled by a store that throws on <see cref="PersistenceStore.Publish"/> after the staged writes — leaves the prior committed generation wholly in force; recovery returns it, not the staged one.</summary>
    [TestMethod]
    public void CrashBeforePublishRecoversThePriorCommittedGeneration()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 4);

            FailAtStepStore crashing = new(store, PublishFailStep.BeforeRename);
            ManifestWriter writer = new(crashing, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4);
            Assert.ThrowsExactly<IOException>(() => writer.Commit(SampleManifest(2, ChecksumAlgorithm.XxHash3)));

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(1, result.Manifest.CommitGeneration, "Recovery surfaced a generation whose publish never completed.");
            Assert.IsFalse(result.IsDegraded);

            //The interrupted generation's manifest is on disk but no retained CURRENT names it — it was never committed.
            Assert.IsNull(store.Read(ManifestNaming.RetainedCurrentName(2)));
            Assert.IsNotNull(store.Read(ManifestNaming.ManifestName(2)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Recovery follows the CURRENT pointer to the latest committed generation across several commits, never the highest generation merely present on disk.</summary>
    [TestMethod]
    public void RecoveryFollowsCurrentToTheLatestCommittedGeneration()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            for(long generation = 1; generation <= 5; generation++)
            {
                Commit(store, pool, generation, ChecksumAlgorithm.XxHash3, retainedCount: 8);
            }

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(5, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>I4 / at-rest rot: when the live CURRENT is corrupted, recovery falls back to the newest verifying retained CURRENT copy and recovers the same latest committed generation, not degraded.</summary>
    [TestMethod]
    public void AtRestCurrentRotFallsBackToARetainedCurrentCopy()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 4);
            Commit(store, pool, 2, ChecksumAlgorithm.XxHash3, retainedCount: 4);

            byte[] live = ReadExisting(store, ManifestNaming.CurrentPointerName);
            live[12] ^= 0xFF;
            store.WriteStaged(ManifestNaming.CurrentPointerName, live);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(2, result.Manifest.CommitGeneration, "The latest committed generation was lost despite a surviving retained CURRENT.");
            Assert.IsFalse(result.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>When the live CURRENT and every retained CURRENT copy are corrupt, the named-degraded direct scan returns the highest verifying manifest, flagged degraded.</summary>
    [TestMethod]
    public void AllCurrentPointersLostDegradesToTheHighestValidManifest()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 8);
            Commit(store, pool, 2, ChecksumAlgorithm.XxHash3, retainedCount: 8);

            byte[] live = ReadExisting(store, ManifestNaming.CurrentPointerName);
            live[12] ^= 0xFF;
            store.WriteStaged(ManifestNaming.CurrentPointerName, live);

            foreach(string name in store.List(ManifestNaming.RetainedCurrentPrefix))
            {
                byte[] retained = ReadExisting(store, name);
                retained[12] ^= 0xFF;
                store.WriteStaged(name, retained);
            }

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(2, result.Manifest.CommitGeneration);
            Assert.IsTrue(result.IsDegraded, "The recovery was not flagged degraded despite no surviving CURRENT pointer.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>When the latest generation's manifest is corrupt, recovery skips past it — through the live CURRENT and the retained fallback — to the most recent generation whose manifest verifies.</summary>
    [TestMethod]
    public void LiveManifestRotSkipsToAnEarlierCommittedGeneration()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 8);
            Commit(store, pool, 2, ChecksumAlgorithm.XxHash3, retainedCount: 8);

            byte[] manifest = ReadExisting(store, ManifestNaming.ManifestName(2));
            manifest[12] ^= 0xFF;
            store.WriteStaged(ManifestNaming.ManifestName(2), manifest);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(1, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Garbage collection keeps the newest retention window of retained CURRENT copies and their manifests, deletes the older ones, and leaves recovery following the live CURRENT to the latest generation.</summary>
    [TestMethod]
    public void GarbageCollectionKeepsTheRetentionWindowAndRecoveryStillWorks()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            for(long generation = 1; generation <= 5; generation++)
            {
                Commit(store, pool, generation, ChecksumAlgorithm.XxHash3, retainedCount: 2);
            }

            Assert.HasCount(2, store.List(ManifestNaming.RetainedCurrentPrefix), "The retained CURRENT window was not enforced.");
            Assert.HasCount(2, store.List(ManifestNaming.ManifestPrefix), "Superseded manifests were not collected.");
            Assert.IsNotNull(store.Read(ManifestNaming.ManifestName(5)));
            Assert.IsNotNull(store.Read(ManifestNaming.ManifestName(4)));
            Assert.IsNull(store.Read(ManifestNaming.ManifestName(3)));

            RecoveryResult result = new ManifestRecovery(store).Recover();
            Assert.AreEqual(5, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An empty store has no recoverable committed state — recovery throws rather than inventing one.</summary>
    [TestMethod]
    public void EmptyStoreHasNoRecoverableState()
    {
        string directory = CreateTempDirectory();
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);

            Assert.ThrowsExactly<InvalidDataException>(() => { _ = new ManifestRecovery(store).Recover(); });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The production default durability barrier (a no-op on this platform) commits and recovers — the barrier is an injected seam, not a hard dependency.</summary>
    [TestMethod]
    public void DefaultDurabilityBarrierCommitsAndRecovers()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory);
            Commit(store, pool, 3, ChecksumAlgorithm.XxHash3, retainedCount: 4);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(3, result.Manifest.CommitGeneration);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A manifest image round-trips identically across the checksum selections — generation, epochs, and every entry's role, name, range, and checksum survive.</summary>
    [TestMethod]
    public void ManifestRoundTripsAcrossChecksumSelections()
    {
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            using VeritasMemoryPool<byte> pool = new();
            Manifest original = SampleManifest(42, checksum);
            int size = original.ComputeSerializedSize(checksum);
            using IMemoryOwner<byte> owner = pool.Rent(size);
            Span<byte> image = owner.Memory.Span[..size];
            original.WriteTo(image, checksum);

            Manifest restored = Manifest.ReadFrom(image);

            Assert.AreEqual(original.CommitGeneration, restored.CommitGeneration);
            Assert.AreEqual(original.DictionaryEpoch, restored.DictionaryEpoch);
            Assert.AreEqual(original.ProvenanceEpoch, restored.ProvenanceEpoch);
            Assert.HasCount(original.Entries.Count, restored.Entries);
            for(int i = 0; i < original.Entries.Count; i++)
            {
                Assert.AreEqual(original.Entries[i].Role, restored.Entries[i].Role);
                Assert.AreEqual(original.Entries[i].FileName, restored.Entries[i].FileName);
                Assert.AreEqual(original.Entries[i].ByteOffset, restored.Entries[i].ByteOffset);
                Assert.AreEqual(original.Entries[i].ByteLength, restored.Entries[i].ByteLength);
                Assert.IsTrue(original.Entries[i].Checksum.Span.SequenceEqual(restored.Entries[i].Checksum.Span), $"Entry {i} checksum differs (algorithm {checksum?.Name ?? "none"}).");
            }
        }
    }

    /// <summary>A CURRENT pointer image round-trips its named generation across the checksum selections.</summary>
    [TestMethod]
    public void CurrentPointerRoundTripsAcrossChecksumSelections()
    {
        foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            using VeritasMemoryPool<byte> pool = new();
            CurrentPointer original = new(123456789);
            int size = CurrentPointer.ComputeSerializedSize(checksum);
            using IMemoryOwner<byte> owner = pool.Rent(size);
            Span<byte> image = owner.Memory.Span[..size];
            original.WriteTo(image, checksum);

            CurrentPointer restored = CurrentPointer.ReadFrom(image);

            Assert.AreEqual(original.CommitGeneration, restored.CommitGeneration);
        }
    }

    /// <summary>A checksummed manifest with a single byte flipped fails its self-checksum on read — at-rest rot is refused rather than trusted.</summary>
    [TestMethod]
    public void CorruptManifestImageIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using ArtifactImage image = ManifestImage(SampleManifest(9, ChecksumAlgorithm.XxHash3), ChecksumAlgorithm.XxHash3, imagePool);
        image.WritableBytes[12] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = Manifest.ReadFrom(image.Bytes); });
    }

    /// <summary>A truncated manifest image is refused rather than mis-read.</summary>
    [TestMethod]
    public void TruncatedManifestImageIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using ArtifactImage image = ManifestImage(SampleManifest(9, ChecksumAlgorithm.XxHash3), ChecksumAlgorithm.XxHash3, imagePool);
        using ArtifactImage truncated = image.Truncated(image.Length - 6, imagePool);

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = Manifest.ReadFrom(truncated.Bytes); });
    }

    /// <summary>I5 / epoch consistency: a manifest stamped with a checksum-algorithm id no resolver knows is refused, not verified under the wrong algorithm.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmIdIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        using ArtifactImage image = ManifestImage(SampleManifest(9, ChecksumAlgorithm.XxHash3), ChecksumAlgorithm.XxHash3, imagePool);
        image.WritableBytes[ChecksumAlgorithmIdOffset] = 99;

        Assert.ThrowsExactly<NotSupportedException>(() => { _ = Manifest.ReadFrom(image.Bytes); });
    }

    /// <summary>I5: a live CURRENT pointer stamped with a checksum-algorithm id this reader cannot resolve makes recovery throw, not silently downgrade to an older committed generation — a reader incompatibility is propagated, never masked as at-rest rot.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmInLiveCurrentPropagatesThroughRecovery()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 4);

            byte[] live = ReadExisting(store, ManifestNaming.CurrentPointerName);
            live[ChecksumAlgorithmIdOffset] = 99;
            store.WriteStaged(ManifestNaming.CurrentPointerName, live);

            Assert.ThrowsExactly<NotSupportedException>(() => { _ = new ManifestRecovery(store).Recover(); });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>I5: a manifest stamped with an unresolvable checksum-algorithm id makes recovery throw rather than skip past it — the two skip sites (pointer and manifest) are independent, so the manifest one is guarded separately.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmInLiveManifestPropagatesThroughRecovery()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 4);

            byte[] manifest = ReadExisting(store, ManifestNaming.ManifestName(1));
            manifest[ChecksumAlgorithmIdOffset] = 99;
            store.WriteStaged(ManifestNaming.ManifestName(1), manifest);

            Assert.ThrowsExactly<NotSupportedException>(() => { _ = new ManifestRecovery(store).Recover(); });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The degraded scan steps past a corrupt highest manifest to the highest one that verifies: with every CURRENT lost and the latest manifest rotten, recovery returns the earlier valid generation, flagged degraded.</summary>
    [TestMethod]
    public void DegradedScanSkipsACorruptHighestManifest()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 1, ChecksumAlgorithm.XxHash3, retainedCount: 8);
            Commit(store, pool, 2, ChecksumAlgorithm.XxHash3, retainedCount: 8);

            byte[] live = ReadExisting(store, ManifestNaming.CurrentPointerName);
            live[12] ^= 0xFF;
            store.WriteStaged(ManifestNaming.CurrentPointerName, live);

            foreach(string name in store.List(ManifestNaming.RetainedCurrentPrefix))
            {
                byte[] retained = ReadExisting(store, name);
                retained[12] ^= 0xFF;
                store.WriteStaged(name, retained);
            }

            byte[] manifest = ReadExisting(store, ManifestNaming.ManifestName(2));
            manifest[12] ^= 0xFF;
            store.WriteStaged(ManifestNaming.ManifestName(2), manifest);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(1, result.Manifest.CommitGeneration);
            Assert.IsTrue(result.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>I4: a crash at the very first commit's publish point leaves a staged manifest that was never committed; with no prior generation and no retained CURRENT, recovery surfaces that orphan only as degraded, never as a clean commit.</summary>
    [TestMethod]
    public void FirstCommitCrashSurfacesTheOrphanManifestOnlyAsDegraded()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            FailAtStepStore crashing = new(store, PublishFailStep.BeforeRename);
            ManifestWriter writer = new(crashing, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4);
            Assert.ThrowsExactly<IOException>(() => writer.Commit(SampleManifest(1, ChecksumAlgorithm.XxHash3)));

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(1, result.Manifest.CommitGeneration);
            Assert.IsTrue(result.IsDegraded, "An orphan manifest with no prior commit was surfaced as a clean commit.");
            Assert.IsNull(store.Read(ManifestNaming.RetainedCurrentName(1)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The unprotected (no-checksum) selection commits and recovers end to end through the store, naming, and retention — recovery follows CURRENT identically when no self-checksum is present.</summary>
    [TestMethod]
    public void RecoveryWorksWithoutAChecksumAlgorithm()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            Commit(store, pool, 3, checksum: null, retainedCount: 4);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            Assert.AreEqual(3, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
