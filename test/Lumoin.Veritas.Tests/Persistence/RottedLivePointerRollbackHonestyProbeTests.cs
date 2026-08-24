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
/// An adversarial probe for the recovery-fidelity honesty boundary the audit finding names: a present-but-rotten
/// live CURRENT pointer suppresses the rollback signal. In the compound-damage crash state where the live CURRENT
/// pointer is present but fails its self-checksum AND the newest generation's retained CURRENT copy and manifest
/// are also damaged, <see cref="DurableSystemOfRecordStore.TryLoad"/> falls back to an intact older committed
/// generation but stamps IsDegraded=false, CommitEvidenced=true, IsRollback=false — all three flags read clean,
/// so a host cannot detect that a newer generation existed and its live commit point rotted. This probe stages
/// that exact on-disk state and pins the observed flags; it documents the boundary of the DurableSystemOfRecordLoad
/// docstring claim that a Loaded generation is "never silently indistinguishable from committed truth."
/// </summary>
[TestClass]
internal sealed class RottedLivePointerRollbackHonestyProbeTests
{
    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a dictionary and a small graph over a shared subject and predicate.</summary>
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

    /// <summary>Persists generations 0 then 1 into a fresh store, so both remain fully retained on disk.</summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="pool">The byte pool.</param>
    /// <param name="firstTriples">The generation-0 triples for later verification.</param>
    private static void PersistTwoGenerations(string directory, VeritasMemoryPool<byte> pool, out EncodedTriple[] firstTriples)
    {
        FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
        DurableSystemOfRecordStore store = new(persistence, pool);

        (TermDictionary first, EncodedTriple[] firstT) = BuildGraph(0xAAAA, objectCount: 3);
        Assert.AreEqual(0L, store.Persist(first, firstT).Generation, "The first persisted generation is zero.");

        (TermDictionary second, EncodedTriple[] secondT) = BuildGraph(0xBBBB, objectCount: 5);
        Assert.AreEqual(1L, store.Persist(second, secondT).Generation, "The second persisted generation is one.");

        firstTriples = firstT;
    }

    /// <summary>Flips the last byte of a file — its self-checksum trailer — so it fails verification (at-rest rot) without touching its magic, version, or algorithm id.</summary>
    /// <param name="path">The file whose trailer is corrupted.</param>
    private static void RotTrailer(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// The finding, staged end to end: the live CURRENT pointer is present but rotten, and the newest generation 1's
    /// retained CURRENT copy AND manifest are also rotten, while the older committed generation 0 stays wholly
    /// intact. Recovery falls to generation 0 via the retained tier — but because the live pointer no longer reads,
    /// the rollback baseline defaults to the served generation and IsRollback is false. All three fidelity flags
    /// read clean even though a newer generation existed and its live commit point rotted: a silent rollback.
    /// </summary>
    [TestMethod]
    public void RottenLivePointerWithDamagedNewestGenerationServesSilentRollbackWithCleanFlags()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rollback-honesty-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool, out EncodedTriple[] firstTriples);

            string livePointerPath = Path.Combine(directory, ManifestNaming.CurrentPointerName);
            string retainedNewestPath = Path.Combine(directory, ManifestNaming.RetainedCurrentName(1));
            string manifestNewestPath = Path.Combine(directory, ManifestNaming.ManifestName(1));

            //Ground truth BEFORE damage: generation 1 was committed. The live CURRENT names it, and its retained
            //current-1 copy — written only after the commit rename — exists as commit evidence for generation 1.
            Assert.IsTrue(File.Exists(livePointerPath), "The live CURRENT pointer exists (it names the committed generation 1).");
            Assert.IsTrue(File.Exists(retainedNewestPath), "Generation 1's retained CURRENT copy exists — proof generation 1 was committed.");

            //Compound damage: rot the live CURRENT pointer (present but failing its self-checksum), the newest
            //generation's retained CURRENT copy, and the newest generation's manifest. Generation 0 stays intact.
            RotTrailer(livePointerPath);
            RotTrailer(retainedNewestPath);
            RotTrailer(manifestNewestPath);

            //The live pointer is still PRESENT on disk (rotten, not deleted) and the current-1 filename still attests
            //generation 1 was committed — so on-disk evidence of the lost newer generation demonstrably survives.
            Assert.IsTrue(File.Exists(livePointerPath), "The rotten live CURRENT pointer is still present on disk (rot, not deletion).");
            Assert.IsTrue(File.Exists(retainedNewestPath), "The rotten current-1 filename still attests generation 1 was committed.");

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            using DecodedItemSegment? served = load.Triples;

            //Recovery serves the intact older generation 0 — a rollback from the committed-then-rotted generation 1.
            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "The intact older generation still loads.");
            Assert.AreEqual(0L, load.Generation, "Recovery fell back to the intact generation 0 below the damaged generation 1.");
            Assert.AreEqual(0xAAAAUL, load.Dictionary!.Epoch, "Generation 0's dictionary is recovered on the fallback.");
            Assert.IsTrue(firstTriples.AsSpan().SequenceEqual(served!.Span), "Generation 0's triples are served on the fallback.");

            //The finding: all three fidelity flags read CLEAN, so a host cannot tell this Loaded generation is a
            //rollback from a newer generation whose live commit point rotted.
            Assert.IsFalse(load.IsDegraded, "IsDegraded is false: recovery followed the retained CURRENT copy, not the degraded scan.");
            Assert.IsTrue(load.CommitEvidenced, "CommitEvidenced is true: generation 0's retained CURRENT copy attests it was committed.");
            Assert.IsFalse(load.IsRollback, "IsRollback is FALSE — the rotten live pointer defaulted the baseline to the served generation, suppressing the rollback signal.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// The control that isolates the suppression to the live-pointer rot: with generation 1's manifest rotten but the
    /// live CURRENT pointer left INTACT (so it still names generation 1), the same fallback to generation 0 is
    /// correctly surfaced as a rollback. The only difference from the silent-rollback case is the readability of the
    /// live pointer, pinning it as the cause of the suppressed signal.
    /// </summary>
    [TestMethod]
    public void ReadableLivePointerSurfacesTheSameFallbackAsRollback()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-rollback-honesty-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            PersistTwoGenerations(directory, pool, out _);

            //Rot ONLY generation 1's manifest; leave the live CURRENT pointer and current-1 intact so the live
            //pointer still names generation 1 and the rollback baseline is readable.
            RotTrailer(Path.Combine(directory, ManifestNaming.ManifestName(1)));

            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);
            load.Triples?.Dispose();

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "The intact older generation loads.");
            Assert.AreEqual(0L, load.Generation, "Recovery fell back to generation 0.");
            Assert.IsTrue(load.IsRollback, "With a readable live pointer naming generation 1, the fallback to generation 0 IS surfaced as a rollback.");
            Assert.IsFalse(load.IsDegraded, "The fallback followed the retained CURRENT copy, so it is not degraded.");
            Assert.IsTrue(load.CommitEvidenced, "Generation 0's retained CURRENT copy attests it was committed.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
