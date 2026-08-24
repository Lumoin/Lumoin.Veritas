using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// Adversarial probe for the barrier-fidelity commit-durability finding: the production commit-point
/// directory barrier (<see cref="AtomicPublish.DefaultBarrier"/>) is a no-op on Windows, so the
/// CURRENT-pointer rename that publishes a generation is never forced to stable storage by library code,
/// and recovery — which follows the live CURRENT — serves the prior generation when that rename is lost,
/// silently dropping an acknowledged generation.
///
/// This cannot reproduce real power-loss write reordering; it pins the two verifiable halves:
/// (1) the production barrier performs no directory flush on this host, and
/// (2) given the exact on-disk state that a lost CURRENT rename leaves — the new generation's manifest
///     present but CURRENT still naming the prior generation — recovery returns the prior generation and
///     orphans the acknowledged one.
/// </summary>
[TestClass]
internal sealed class BarrierFidelityCommitDurabilityProbeTests
{
    /// <summary>A directory durability barrier that does nothing; used for the staging store so the probe is platform-independent.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a well-formed sample manifest for a generation under the given checksum.</summary>
    /// <param name="generation">The commit generation; also seeds the entries' checksum bytes.</param>
    /// <param name="checksum">The checksum algorithm.</param>
    /// <returns>The manifest.</returns>
    private static Manifest SampleManifest(long generation, ChecksumAlgorithm checksum)
    {
        int width = checksum.ByteWidth;
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

    /// <summary>Stages a generation's manifest image durably under its final name WITHOUT publishing CURRENT — the exact on-disk residue a CURRENT rename that never reached stable storage leaves behind.</summary>
    /// <param name="store">The store staged into.</param>
    /// <param name="pool">The buffer pool.</param>
    /// <param name="manifest">The manifest to stage.</param>
    /// <param name="checksum">The checksum algorithm.</param>
    private static void StageManifestOnly(PersistenceStore store, MemoryPool<byte> pool, Manifest manifest, ChecksumAlgorithm checksum)
    {
        int size = manifest.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        manifest.WriteTo(owner.Memory.Span[..size], checksum);
        store.WriteStaged(ManifestNaming.ManifestName(manifest.CommitGeneration), owner.Memory.Span[..size]);
    }

    /// <summary>
    /// The production <see cref="AtomicPublish.DefaultBarrier"/> issues no directory flush on Windows: a
    /// non-existent directory path — which the Linux/Apple open+fsync path would reject with an
    /// <see cref="IOException"/> — returns silently, proving the early-out no-op branch is taken. On the
    /// platforms where the barrier does real work, the same call throws, which pins the per-OS contrast.
    /// </summary>
    [TestMethod]
    public void ProductionDefaultBarrierIsANoOpOnWindows()
    {
        string parent = Directory.CreateTempSubdirectory("veritas-barrier-noop-").FullName;
        try
        {
            //A child path that is never created, so no directory exists at it to open or flush.
            string nonexistentDirectory = Path.Combine(parent, "never-created-child");

            if(OperatingSystem.IsWindows())
            {
                //No throw: the barrier early-returns before opening anything, so it flushes no directory metadata.
                AtomicPublish.DefaultBarrier(nonexistentDirectory);
            }
            else
            {
                //Elsewhere the barrier really opens + fsyncs the directory, so a non-existent path is an IOException.
                Assert.ThrowsExactly<IOException>(() => AtomicPublish.DefaultBarrier(nonexistentDirectory));
            }
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    /// <summary>
    /// A committed generation whose CURRENT-pointer rename is lost (its manifest durably on disk but CURRENT
    /// still naming the prior generation) is dropped by recovery: recovery follows the live CURRENT to the
    /// prior generation and never consults the orphaned newer manifest. This is the recovery outcome the
    /// no-op Windows barrier permits after a power loss between the rename and NTFS committing it.
    /// </summary>
    [TestMethod]
    public void LostCurrentRenameDropsTheAcknowledgedGenerationOnRecovery()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-barrier-probe-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            ChecksumAlgorithm checksum = ChecksumAlgorithm.XxHash3;

            //Generation 1 is committed cleanly: manifest-1, CURRENT -> 1, retained current-1.
            new ManifestWriter(store, checksum, pool, retainedCurrentPointerCount: 4).Commit(SampleManifest(1, checksum));

            //Generation 2 is "acknowledged" to the caller, but its CURRENT rename never reached stable storage:
            //stage manifest-2 (its content was flushed before the publish) and leave CURRENT still naming 1.
            StageManifestOnly(store, pool, SampleManifest(2, checksum), checksum);

            RecoveryResult result = new ManifestRecovery(store).Recover();

            //Recovery serves generation 1 — the acknowledged generation 2 is lost.
            Assert.AreEqual(1, result.Manifest.CommitGeneration, "Recovery did NOT drop the generation whose CURRENT rename was lost; the durability window may be closed elsewhere.");
            Assert.IsFalse(result.IsDegraded, "Recovery took the primary live-CURRENT path, not the degraded scan that could have found manifest-2.");

            //Generation 2's manifest sits on disk, fully written and unreferenced — an orphan recovery never reaches.
            Assert.IsNotNull(store.Read(ManifestNaming.ManifestName(2)), "The acknowledged generation's manifest should still be on disk, orphaned.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
