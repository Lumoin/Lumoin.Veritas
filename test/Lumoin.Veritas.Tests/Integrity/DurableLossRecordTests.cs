using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Tests.MemoryPool;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The durable loss record makes a repair's named losses survive a restart: a self-checksummed,
/// manifest-adjacent artifact co-versioned with the healed generation under the
/// <see cref="ManifestFileRole.Losses"/> role. These pin the format round-trip (every loss kind exact in kind,
/// role, artifact name, and item range, including the null-name default-graph case and the range-free
/// whole-artifact case), the self-checksum refusing at-rest rot, the scrub attesting the record rather than
/// condemning it, and the ordinary load and the manifest reader skipping the role a build does not consume.
/// </summary>
[TestClass]
internal sealed class DurableLossRecordTests
{
    /// <summary>The generation the staged loss records belong to.</summary>
    private const long Generation = 42;

    /// <summary>The triple count the staged clean data segment carries.</summary>
    private const uint TripleCount = 30;

    /// <summary>A representative loss set spanning every case the format must round-trip: a range-free whole-artifact loss with a named artifact, a named item-set loss carrying a range, and a default-graph item-set loss whose artifact name is null.</summary>
    /// <param name="generation">The generation the losses belong to.</param>
    /// <returns>The losses.</returns>
    private static UnrecoverableItemReport[] SampleLosses(long generation)
    {
        return
        [
            UnrecoverableItemReport.WholeArtifact(generation, ManifestFileRole.Dictionary.Code, "dict-lost.dic"),
            UnrecoverableItemReport.ItemSet(generation, ManifestFileRole.NamedGraphSegment.Code, "nsor-lost.sor", lostItemStart: 10, lostItemCount: 5),
            UnrecoverableItemReport.ItemSet(generation, lostItemStart: 20, lostItemCount: 4),
        ];
    }

    /// <summary>A written loss record round-trips every named loss exactly: kind, role, artifact name (present and null), and item range.</summary>
    [TestMethod]
    public void RoundTripsEveryNamedLossExactly()
    {
        using VeritasMemoryPool<byte> pool = new();
        UnrecoverableItemReport[] losses = SampleLosses(Generation);
        int size = DurableLossRecord.ComputeSerializedSize(losses, ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        DurableLossRecord.WriteTo(image, Generation, losses, ChecksumAlgorithm.XxHash3);

        DurableLossRecord? record = DurableLossRecord.TryRead(image);

        Assert.IsNotNull(record);
        Assert.AreEqual(Generation, record.Generation, "The record names its generation.");
        Assert.HasCount(losses.Length, record.Losses);

        Assert.AreEqual(UnrecoverableItemReportKind.WholeArtifact, record.Losses[0].Kind);
        Assert.AreEqual(ManifestFileRole.Dictionary.Code, record.Losses[0].RoleCode);
        Assert.AreEqual("dict-lost.dic", record.Losses[0].ArtifactFileName);
        Assert.AreEqual(-1L, record.Losses[0].StartItem, "A whole-artifact loss carries no start item.");
        Assert.AreEqual(0L, record.Losses[0].ItemCount, "A whole-artifact loss carries no item count.");

        Assert.AreEqual(UnrecoverableItemReportKind.ItemSet, record.Losses[1].Kind);
        Assert.AreEqual(ManifestFileRole.NamedGraphSegment.Code, record.Losses[1].RoleCode);
        Assert.AreEqual("nsor-lost.sor", record.Losses[1].ArtifactFileName);
        Assert.AreEqual(10L, record.Losses[1].StartItem);
        Assert.AreEqual(5L, record.Losses[1].ItemCount);

        Assert.AreEqual(UnrecoverableItemReportKind.ItemSet, record.Losses[2].Kind);
        Assert.AreEqual(ManifestFileRole.DataSegment.Code, record.Losses[2].RoleCode);
        Assert.IsNull(record.Losses[2].ArtifactFileName, "The default graph's segment carries no artifact name.");
        Assert.AreEqual(20L, record.Losses[2].StartItem);
        Assert.AreEqual(4L, record.Losses[2].ItemCount);
    }

    /// <summary>At-rest rot in a loss record fails its self-checksum and reads back null, so a caller never learns corrupt losses — detection precedes use.</summary>
    [TestMethod]
    public void AtRestCorruptionReadsBackNull()
    {
        using VeritasMemoryPool<byte> pool = new();
        UnrecoverableItemReport[] losses = SampleLosses(Generation);
        int size = DurableLossRecord.ComputeSerializedSize(losses, ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        DurableLossRecord.WriteTo(image, Generation, losses, ChecksumAlgorithm.XxHash3);

        //A byte in the middle of the record is front matter the trailer covers, so flipping it fails the self-checksum.
        image[size / 2] ^= 0xFF;

        Assert.IsNull(DurableLossRecord.TryRead(image), "A tampered loss record reads back null.");
    }

    /// <summary>Bytes that are not a loss record (a magic mismatch) read back null rather than throwing.</summary>
    [TestMethod]
    public void ForeignBytesReadBackNull()
    {
        byte[] foreign = new byte[64];

        Assert.IsNull(DurableLossRecord.TryRead(foreign), "Non-loss-record bytes read back null.");
    }

    /// <summary>A scrub over a generation carrying a loss record verifies wholly clean: the record is attested by the manifest's whole-image digest like any named artifact and is not block-verified, so it is never condemned while the clean dictionary and data segment verify clean.</summary>
    [TestMethod]
    public void ScrubAttestsTheLossRecordRatherThanCondemningIt()
    {
        using VeritasMemoryPool<byte> pool = new();
        FileSystemPersistenceStore store = StageGenerationWithLossRecord(Generation, SampleLosses(Generation), pool, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsDegradedSnapshot, "The staged generation is a clean committed snapshot.");
            Assert.IsEmpty(report.CorruptBlocks, "The loss record is attested, not condemned, and the dictionary and data segment verify clean.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The ordinary durable load path loads a generation whose manifest names the Losses role without error: the role-explicit load consults only the dictionary, data segment, named graphs, and sidecar, so it skips the record a build does not consume.</summary>
    [TestMethod]
    public void OrdinaryLoadSkipsTheLossRecordRole()
    {
        using VeritasMemoryPool<byte> pool = new();
        FileSystemPersistenceStore persistence = StageGenerationWithLossRecord(Generation, SampleLosses(Generation), pool, out string directory);
        try
        {
            DurableSystemOfRecordStore store = new(persistence, pool);
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();

            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "A manifest naming the Losses role loads; the load skips the role it does not consume.");
            load.Triples?.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The manifest reader carries the Losses role by its stable code and carries a role no build recognises as an unknown-role placeholder rather than failing the generation — the format's forward-compatibility rule, on which the loss role and any later role both rely.</summary>
    [TestMethod]
    public void ManifestCarriesTheLossesRoleAndAnUnknownRoleWithoutFailing()
    {
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, "sor", 0, 10, default),
            new(ManifestFileRole.Losses, "losses", 0, 20, default),
            new(ManifestFileRole.Create(4242, "Future"), "future", 0, 30, default),
        ];
        Manifest manifest = new(3, dictionaryEpoch: 11, provenanceEpoch: 13, entries);
        int size = manifest.ComputeSerializedSize(checksum: null);
        byte[] image = new byte[size];
        manifest.WriteTo(image, checksum: null);

        Manifest read = Manifest.ReadFrom(image);

        Assert.AreEqual(ManifestFileRole.Losses.Code, read.Entries[1].Role.Code, "The Losses role round-trips by its stable code.");
        Assert.AreEqual(4242, read.Entries[2].Role.Code, "An unrecognised role is carried by its code, not failed.");
        Assert.AreEqual("Unknown", read.Entries[2].Role.Name, "An unrecognised role reads back as the unknown-role placeholder.");
    }

    /// <summary>Stages a clean committed generation — a dictionary (role 6), a default-graph data segment (role 1), and a loss record (role 8) naming <paramref name="losses"/> — into a fresh temp-dir store, each artifact bound by its manifest-recorded digest, so the scrub and load paths meet a generation that carries a loss record alongside otherwise-clean artifacts.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="losses">The named losses the loss record persists.</param>
    /// <param name="pool">The pool the images and digests are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store holding the generation.</returns>
    private static FileSystemPersistenceStore StageGenerationWithLossRecord(long generation, IReadOnlyList<UnrecoverableItemReport> losses, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-lossrecord-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);

        TermDictionary dictionary = SampleDictionary(6);
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage dictionaryImage = DictionaryImage(dictionary, blockTermCount: 4, pool);
        using ArtifactImage dataImage = SegmentImage(triples, pool);

        int lossSize = DurableLossRecord.ComputeSerializedSize(losses, ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> lossOwner = pool.Rent(lossSize);
        Span<byte> lossImage = lossOwner.Memory.Span[..lossSize];
        DurableLossRecord.WriteTo(lossImage, generation, losses, ChecksumAlgorithm.XxHash3);

        string dictionaryName = DictionaryArtifactName(generation);
        string dataName = RecordArtifactName(generation);
        string lossName = HealedArtifactNaming.LossRecordName(generation);
        store.WriteStaged(dictionaryName, dictionaryImage.Bytes);
        store.WriteStaged(dataName, dataImage.Bytes);
        store.WriteStaged(lossName, lossImage);

        List<IMemoryOwner<byte>> digests = [];
        try
        {
            ManifestEntry[] entries =
            [
                new(ManifestFileRole.Dictionary, dictionaryName, 0, dictionaryImage.Length, StageDigest(dictionaryImage.Bytes, pool, digests)),
                new(ManifestFileRole.DataSegment, dataName, 0, dataImage.Length, StageDigest(dataImage.Bytes, pool, digests)),
                new(ManifestFileRole.Losses, lossName, 0, lossSize, StageDigest(lossImage, pool, digests)),
            ];
            new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
                .Commit(new Manifest(generation, (long)dictionary.Epoch, generation * 13, entries));
        }
        finally
        {
            foreach(IMemoryOwner<byte> digest in digests)
            {
                digest.Dispose();
            }
        }

        return store;
    }

    /// <summary>Computes an artifact's manifest-entry digest into a buffer rented from <paramref name="pool"/>, records the owner in <paramref name="owners"/> for release after the commit, and returns the digest view.</summary>
    /// <param name="image">The artifact bytes.</param>
    /// <param name="pool">The pool the digest buffer is rented from.</param>
    /// <param name="owners">The list the rented digest owner is appended to for release after the commit.</param>
    /// <returns>The digest view over the rented buffer.</returns>
    private static ReadOnlyMemory<byte> StageDigest(ReadOnlySpan<byte> image, MemoryPool<byte> pool, List<IMemoryOwner<byte>> owners)
    {
        IMemoryOwner<byte> owner = Digest(image, pool);
        owners.Add(owner);

        return owner.Memory[..ChecksumAlgorithm.XxHash3.ByteWidth];
    }
}
