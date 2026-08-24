using System;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The artifact-failure fallback contract for <see cref="DurableSystemOfRecordStore.TryLoad"/>: recovery descends
/// the retained generation ladder not only on a CURRENT-pointer or manifest failure but also when a live
/// generation's SEGMENTS fail their at-rest verification. A rotted segment under a healthy live manifest no longer
/// fails the whole load terminally — an intact older generation the store retains precisely for this is served
/// instead, and the fallback to an older generation is surfaced distinctly as a rollback. Only when every retained
/// generation's artifacts are damaged does the load return the terminal failure outcome, and the ladder is finite.
/// </summary>
[TestClass]
internal sealed class PublishOrderingFallbackProbeTests
{
    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a dictionary and a small graph over a shared subject and predicate, cycling named, literal, and blank-node objects.</summary>
    /// <param name="epoch">The dictionary epoch.</param>
    /// <param name="objectCount">The number of object terms (and triples).</param>
    /// <returns>The dictionary and the encoded triples.</returns>
    private static (TermDictionary Dictionary, EncodedTriple[] Triples) BuildGraph(ulong epoch, int objectCount)
    {
        TermDictionary dictionary = new(epoch);
        TermId subject = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/s")));
        TermId predicate = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/p")));
        NamedNode xsdString = new(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"));

        EncodedTriple[] triples = new EncodedTriple[objectCount];
        for(int i = 0; i < objectCount; i++)
        {
            RdfTerm @object = (i % 3) switch
            {
                0 => new NamedNode(Utf8Strings.From($"http://example.org/o{i}")),
                1 => new Literal(Utf8Strings.From($"value{i}"), xsdString),
                _ => new BlankNode(Utf8Strings.From($"b{i}")),
            };

            triples[i] = new EncodedTriple(subject, predicate, dictionary.GetOrAdd(@object));
        }

        return (dictionary, triples);
    }

    /// <summary>Persists two generations (0 then 1) into a fresh store, so both remain fully retained on disk (RetainedGenerationCount = 4 keeps every artifact of both).</summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="pool">The byte pool.</param>
    /// <param name="firstTriples">The generation-0 triples.</param>
    private static void PersistTwoGenerations(string directory, VeritasMemoryPool<byte> pool, out EncodedTriple[] firstTriples)
    {
        FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
        DurableSystemOfRecordStore store = new(persistence, pool);

        (TermDictionary first, EncodedTriple[] firstT) = BuildGraph(0xAAAA, objectCount: 3);
        long gen0 = store.Persist(first, firstT).Generation;
        Assert.AreEqual(0L, gen0, "The first persisted generation is zero.");

        (TermDictionary second, EncodedTriple[] secondT) = BuildGraph(0xBBBB, objectCount: 5);
        long gen1 = store.Persist(second, secondT).Generation;
        Assert.AreEqual(1L, gen1, "The second persisted generation is one.");

        firstTriples = firstT;
    }

    /// <summary>Flips a single byte at the middle of every file matching a glob, so each fails its manifest-recorded whole-image digest.</summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="glob">The file glob to rot.</param>
    /// <returns>The number of files rotted.</returns>
    private static int RotFiles(string directory, string glob)
    {
        string[] files = Directory.GetFiles(directory, glob);
        Array.Sort(files, StringComparer.Ordinal);
        foreach(string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(file, bytes);
        }

        return files.Length;
    }

    /// <summary>
    /// The contract, the fix for the segment-rot finding: with generation 0 wholly intact and only generation 1's
    /// system-of-record segment rotted under a healthy live manifest, <see cref="DurableSystemOfRecordStore.TryLoad"/>
    /// falls back automatically to the intact retained generation 0, serves its exact triples, and surfaces the
    /// step down to an older generation as a rollback.
    /// </summary>
    [TestMethod]
    public void SegmentRotOnNewestGenerationFallsBackToIntactRetainedPriorAsRollback()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-fallback-probe-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool, out EncodedTriple[] firstTriples);

            //Rot the NEWEST generation's segment ONLY; a single byte flip fails the manifest's recorded whole-image
            //digest. Generation 0's segment stays intact so the fallback has an intact prior generation to reach.
            string[] recordArtifacts = Directory.GetFiles(directory, "sor-*.sor");
            Array.Sort(recordArtifacts, StringComparer.Ordinal);
            Assert.HasCount(2, recordArtifacts, "Both generations' system-of-record artifacts are retained on disk.");
            byte[] newest = File.ReadAllBytes(recordArtifacts[1]);
            newest[newest.Length / 2] ^= 0xFF;
            File.WriteAllBytes(recordArtifacts[1], newest);

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome,
                "A rotted newest segment falls back to the intact retained prior generation rather than failing terminally.");
            Assert.AreEqual(0L, load.Generation, "The intact prior generation 0 is served.");
            Assert.IsTrue(load.IsRollback, "Serving generation 0 when the live pointer names generation 1 is surfaced as a rollback.");
            Assert.IsFalse(load.IsDegraded, "The fallback followed the retained CURRENT copy, so the load is not degraded.");
            Assert.IsTrue(load.CommitEvidenced, "The retained CURRENT copy attests generation 0 was committed.");
            Assert.AreEqual(0xAAAAUL, load.Dictionary!.Epoch, "Generation 0's dictionary is recovered on the fallback.");

            using DecodedItemSegment recovered = load.Triples!;
            Assert.IsTrue(firstTriples.AsSpan().SequenceEqual(recovered.Span), "Generation 0's triples are intact and served on the fallback.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// When every retained generation's system-of-record segment is rotted, the ladder is exhausted and the load
    /// returns the terminal failure outcome (the failure of the generation the load first reached), never looping
    /// forever descending a ladder that cannot yield an intact generation.
    /// </summary>
    [TestMethod]
    public void AllGenerationsSegmentRottedReturnsTerminalRejectedWithoutLooping()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-fallback-probe-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool, out _);

            //Rot BOTH generations' system-of-record segments, so no candidate on the ladder can load fully.
            Assert.AreEqual(2, RotFiles(directory, "sor-*.sor"), "Both generations' system-of-record artifacts are rotted.");

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Rejected, load.Outcome,
                "With every generation's segment rotted the ladder is exhausted and the terminal failure outcome is returned.");
            Assert.AreEqual(1L, load.Generation, "The reported failure is the live generation the load first reached.");
            Assert.IsFalse(load.IsRollback, "A terminal failure serves nothing, so it is not a rollback.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The contrast that pins the tiering: rotting the NEWEST generation's MANIFEST (not its segment) also drives
    /// the retained fallback — the live pointer names generation 1 but its manifest is unreadable, so recovery
    /// follows the retained CURRENT copy to the intact generation 0, served as a rollback.
    /// </summary>
    [TestMethod]
    public void ManifestRotOnNewestGenerationAutoFallsBackToRetainedPrior()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-fallback-probe-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool, out EncodedTriple[] firstTriples);

            //Rot the NEWEST generation's manifest self-checksum trailer; ManifestRecovery skips it and walks the
            //retained CURRENT copies newest-first to the intact generation 0.
            string[] manifests = Directory.GetFiles(directory, "manifest-*");
            Array.Sort(manifests, StringComparer.Ordinal);
            Assert.HasCount(2, manifests, "Both generations' manifests are retained on disk.");
            byte[] bytes = File.ReadAllBytes(manifests[1]);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(manifests[1], bytes);

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome,
                "A rotted MANIFEST triggers the retained fallback, like a rotted segment.");
            Assert.AreEqual(0L, load.Generation, "Recovery fell back to the intact prior generation 0.");
            Assert.IsTrue(load.IsRollback, "Serving generation 0 when the live pointer names generation 1 is a rollback.");
            Assert.IsFalse(load.IsDegraded, "The fallback followed the retained CURRENT copy, so it is not degraded.");
            Assert.AreEqual(0xAAAAUL, load.Dictionary!.Epoch, "Generation 0's dictionary is recovered on the fallback.");

            using DecodedItemSegment recovered = load.Triples!;
            Assert.IsTrue(firstTriples.AsSpan().SequenceEqual(recovered.Span), "Generation 0's triples are served on the fallback.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// No-regression: a clean store with two intact generations loads the live generation exactly — not degraded,
    /// not rolled back, commit-evidenced — so the new fidelity flags never falsely flag a healthy open.
    /// </summary>
    [TestMethod]
    public void CleanStoreLoadsLiveGenerationExactlyWithNoDegradationOrRollback()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-fallback-probe-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool, out _);

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);
            load.Triples?.Dispose();

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "A clean store loads.");
            Assert.AreEqual(1L, load.Generation, "The live committed generation 1 is served.");
            Assert.IsFalse(load.IsDegraded, "A clean live-CURRENT load is not degraded.");
            Assert.IsTrue(load.CommitEvidenced, "A pointer-followed load is commit-evidenced.");
            Assert.IsFalse(load.IsRollback, "Serving the live generation is not a rollback.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
