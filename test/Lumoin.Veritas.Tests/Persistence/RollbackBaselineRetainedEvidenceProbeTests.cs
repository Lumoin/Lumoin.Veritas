using System;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// Pins the rollback baseline as the strongest SURVIVING commit evidence, not merely the live pointer: a
/// verifying retained CURRENT copy is written only after the commit rename, so it proves its generation was
/// committed even when the live pointer and that generation's manifest are both gone. Serving an older intact
/// generation past such evidence is a rollback and must be flagged — with or without the live pointer — so a
/// host can never mistake evidence-attested stale service for committed truth.
/// </summary>
[TestClass]
internal sealed class RollbackBaselineRetainedEvidenceProbeTests
{
    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a small dictionary and graph over a shared subject and predicate.</summary>
    /// <param name="epoch">The dictionary epoch.</param>
    /// <param name="objectCount">The number of object terms (and triples).</param>
    /// <returns>The dictionary and the encoded triples.</returns>
    private static (TermDictionary Dictionary, EncodedTriple[] Triples) BuildGraph(ulong epoch, int objectCount)
    {
        TermDictionary dictionary = new(epoch);
        TermId subject = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/s")));
        TermId predicate = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/p")));

        EncodedTriple[] triples = new EncodedTriple[objectCount];
        for(int i = 0; i < objectCount; i++)
        {
            TermId @object = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From($"http://example.org/o{i}")));
            triples[i] = new EncodedTriple(subject, predicate, @object);
        }

        return (dictionary, triples);
    }

    /// <summary>Persists two generations (0 then 1) into a fresh store, so both remain fully retained on disk.</summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="pool">The byte pool.</param>
    private static void PersistTwoGenerations(string directory, VeritasMemoryPool<byte> pool)
    {
        FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
        DurableSystemOfRecordStore store = new(persistence, pool);

        (TermDictionary first, EncodedTriple[] firstT) = BuildGraph(0xAAAA, objectCount: 3);
        Assert.AreEqual(0L, store.Persist(first, firstT).Generation, "The first persisted generation is zero.");

        (TermDictionary second, EncodedTriple[] secondT) = BuildGraph(0xBBBB, objectCount: 5);
        Assert.AreEqual(1L, store.Persist(second, secondT).Generation, "The second persisted generation is one.");
    }

    /// <summary>Reads the retained CURRENT copy for a generation and reports whether it verifies and names that generation — the surviving commit evidence.</summary>
    /// <param name="persistence">The store.</param>
    /// <param name="generation">The generation whose retained copy is checked.</param>
    /// <returns><see langword="true"/> when a verifying retained current-{generation} names that generation.</returns>
    private static bool RetainedCopyAttestsCommit(FileSystemPersistenceStore persistence, long generation)
    {
        byte[]? bytes = persistence.Read(ManifestNaming.RetainedCurrentName(generation));
        if(bytes is null)
        {
            return false;
        }

        try
        {
            return CurrentPointer.ReadFrom(bytes).CommitGeneration == generation;
        }
        catch(InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// The live CURRENT is deleted, generation 1's MANIFEST is rotted (so generation 1 cannot load) yet its
    /// retained current-1 copy survives and attests generation 1 was committed, and generation 0 is wholly
    /// intact: the baseline reads the surviving retained evidence, so the generation-0 serve is flagged a
    /// rollback even with no live pointer at all.
    /// </summary>
    [TestMethod]
    public void LiveCurrentLostNewerRetainedEvidenceStillFlagsRollback()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rollback-baseline-probe-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool);

            //Rot generation 1's manifest self-checksum trailer so its manifest is unreadable, but leave the retained
            //current-1 pointer and generation 0 wholly intact.
            string[] manifests = Directory.GetFiles(directory, "manifest-*");
            Array.Sort(manifests, StringComparer.Ordinal);
            Assert.HasCount(2, manifests, "Both generations' manifests are retained on disk.");
            byte[] manifestBytes = File.ReadAllBytes(manifests[1]);
            manifestBytes[^1] ^= 0xFF;
            File.WriteAllBytes(manifests[1], manifestBytes);

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);

            //Lose the live CURRENT pointer (the only difference from the committed rollback-detected case), leaving
            //the retained current-1 copy as the strongest surviving evidence that generation 1 was committed.
            persistence.Delete(ManifestNaming.CurrentPointerName);
            Assert.IsNull(persistence.Read(ManifestNaming.CurrentPointerName), "The live CURRENT pointer is gone.");
            Assert.IsTrue(RetainedCopyAttestsCommit(persistence, 1), "The retained current-1 copy survives and attests generation 1 was committed.");
            Assert.IsTrue(RetainedCopyAttestsCommit(persistence, 0), "The retained current-0 copy survives for generation 0.");

            DurableSystemOfRecordStore store = new(persistence, pool);
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);
            load.Triples?.Dispose();

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "Generation 0 is intact and loads.");
            Assert.AreEqual(0L, load.Generation, "The served generation is the intact older generation 0.");

            //The verifying retained current-1 proves generation 1 was committed and is being rolled back past;
            //the baseline reads that evidence, so the serve is honest: committed, older, flagged.
            Assert.IsFalse(load.IsDegraded, "The retained-pointer recovery is not degraded.");
            Assert.IsTrue(load.CommitEvidenced, "Generation 0's retained copy attests it was committed.");
            Assert.IsTrue(load.IsRollback, "The surviving retained current-1 evidence makes the generation-0 serve a flagged rollback, live pointer or not.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The control that isolates the single differing input: the identical state EXCEPT the live CURRENT (naming
    /// generation 1) survives. With the live pointer readable the rollback baseline is generation 1, so serving
    /// generation 0 is correctly surfaced as a rollback. This pins that the ONLY thing flipping IsRollback is
    /// whether the live pointer — not the surviving retained evidence — is present.
    /// </summary>
    [TestMethod]
    public void LiveCurrentPresentSameStateFlagsRollback()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rollback-baseline-control-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool);

            string[] manifests = Directory.GetFiles(directory, "manifest-*");
            Array.Sort(manifests, StringComparer.Ordinal);
            byte[] manifestBytes = File.ReadAllBytes(manifests[1]);
            manifestBytes[^1] ^= 0xFF;
            File.WriteAllBytes(manifests[1], manifestBytes);

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);

            //Keep the live CURRENT pointer (names generation 1); only generation 1's manifest is rotted.
            Assert.IsNotNull(persistence.Read(ManifestNaming.CurrentPointerName), "The live CURRENT pointer survives and names generation 1.");

            DurableSystemOfRecordStore store = new(persistence, pool);
            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);
            load.Triples?.Dispose();

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "Generation 0 is intact and loads.");
            Assert.AreEqual(0L, load.Generation, "The served generation is the intact older generation 0.");
            Assert.IsTrue(load.IsRollback, "With the live pointer readable the rollback is correctly surfaced.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
