using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The <see cref="DurableSystemOfRecordStore"/> persists a database's durable core — the term dictionary and
/// the system-of-record triples — as one generation-versioned manifest generation and recovers it on restart: a
/// persist-then-load round-trip recovers the exact dictionary (epoch, identifier assignment, every term kind)
/// and the triples in stored order; an empty store reports nothing found; a second persist supersedes the
/// first; and an at-rest-corrupt artifact is refused rather than served (detection precedes use).
/// </summary>
[TestClass]
internal sealed class DurableSystemOfRecordStoreTests
{
    /// <summary>A directory durability barrier that does nothing, so the tests do not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Builds a dictionary and a small graph over a shared subject and predicate, cycling named, literal, and blank-node objects so every leaf term kind is persisted.</summary>
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

    /// <summary>Asserts the recovered dictionary matches the source on epoch, count, and every identifier's term.</summary>
    /// <param name="source">The original dictionary.</param>
    /// <param name="restored">The recovered dictionary.</param>
    private static void AssertDictionaryRoundTrip(TermDictionary source, TermDictionary restored)
    {
        Assert.AreEqual(source.Epoch, restored.Epoch, "The dictionary epoch did not round-trip.");
        Assert.AreEqual(source.Count, restored.Count, "The dictionary term count did not round-trip.");
        for(uint id = 1; id <= (uint)source.Count; id++)
        {
            Assert.AreEqual(source.Resolve(id), restored.Resolve(id), $"Term {id} did not round-trip.");
        }
    }

    /// <summary>Persisting a generation then loading it recovers the exact dictionary and the triples in stored order.</summary>
    [TestMethod]
    public void PersistThenLoadRoundTripsDictionaryAndTriples()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            (TermDictionary dictionary, EncodedTriple[] triples) = BuildGraph(0x0123456789ABCDEF, objectCount: 6);
            DurableSystemOfRecordCommit commit = store.Persist(dictionary, triples);
            Assert.AreEqual(0L, commit.Generation, "The first persisted generation is zero.");
            Assert.AreEqual(dictionary.Epoch, commit.DictionaryEpoch);
            Assert.AreEqual(dictionary.Count, commit.TermCount);
            Assert.AreEqual(triples.Length, commit.TripleCount);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome);
            Assert.AreEqual(0L, load.Generation);
            Assert.IsNull(load.Sidecar, "No sidecar was persisted, so none is recovered.");
            AssertDictionaryRoundTrip(dictionary, load.Dictionary!);

            using DecodedItemSegment recovered = load.Triples!;
            Assert.IsTrue(triples.AsSpan().SequenceEqual(recovered.Span), "The system-of-record triples did not round-trip in order.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Loading from a store that holds no committed generation reports nothing found rather than throwing.</summary>
    [TestMethod]
    public void LoadFromEmptyStoreIsNotFound()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.NotFound, load.Outcome);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A second persist supersedes the first: the generation increments and a load recovers the latest committed generation's dictionary and triples.</summary>
    [TestMethod]
    public void LatestGenerationIsLoaded()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            (TermDictionary first, EncodedTriple[] firstTriples) = BuildGraph(0xAAAA, objectCount: 3);
            store.Persist(first, firstTriples);

            (TermDictionary second, EncodedTriple[] secondTriples) = BuildGraph(0xBBBB, objectCount: 5);
            DurableSystemOfRecordCommit secondCommit = store.Persist(second, secondTriples);
            Assert.AreEqual(1L, secondCommit.Generation, "The second persisted generation is one.");

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome);
            Assert.AreEqual(1L, load.Generation, "TryLoad recovers the latest committed generation.");
            Assert.AreEqual(0xBBBBUL, load.Dictionary!.Epoch, "The latest generation's dictionary is recovered.");

            using DecodedItemSegment recovered = load.Triples!;
            Assert.IsTrue(secondTriples.AsSpan().SequenceEqual(recovered.Span), "The latest generation's triples are recovered.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>An at-rest-corrupt system-of-record artifact is refused on load rather than served, bound to the generation that named it by the manifest's recorded digest.</summary>
    [TestMethod]
    public void CorruptRecordArtifactIsRejected()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            (TermDictionary dictionary, EncodedTriple[] triples) = BuildGraph(0xCCCC, objectCount: 6);
            store.Persist(dictionary, triples);

            string[] recordArtifacts = Directory.GetFiles(directory, "sor-*.sor");
            Assert.HasCount(1, recordArtifacts, "Exactly one system-of-record artifact was persisted.");
            byte[] bytes = File.ReadAllBytes(recordArtifacts[0]);
            //The first item block begins on the 4096-byte page boundary, so this corrupts a checksum-covered region.
            bytes[4096] ^= 0xFF;
            File.WriteAllBytes(recordArtifacts[0], bytes);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Rejected, load.Outcome, "A corrupt artifact must be refused, not served.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A persisted columnar sidecar is recovered warm and holds the persisted triples — the Elias-Fano index reloads with no re-sort or re-pack.</summary>
    [TestMethod]
    public void PersistsAndRecoversTheColumnarSidecar()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            (TermDictionary dictionary, EncodedTriple[] triples) = BuildGraph(0xEE, objectCount: 6);
            ColumnarTripleIndex sidecar = ColumnarTripleIndex.Build(triples);
            store.Persist(dictionary, triples, sidecar: sidecar);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome);
            Assert.IsNotNull(load.Sidecar, "The persisted columnar sidecar is recovered warm.");

            using DecodedItemSegment recovered = load.Triples!;
            HashSet<EncodedTriple> expected = [.. triples];
            HashSet<EncodedTriple> actual = [.. load.Sidecar!.EnumerateTriples()];
            Assert.IsTrue(expected.SetEquals(actual), "The recovered columnar sidecar holds the persisted triples.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A corrupt sidecar drops to null and the load still succeeds — the columnar index is re-derivable, so its damage is never a load failure.</summary>
    [TestMethod]
    public void CorruptSidecarDropsToNullWithoutFailingTheLoad()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            (TermDictionary dictionary, EncodedTriple[] triples) = BuildGraph(0xEE, objectCount: 6);
            store.Persist(dictionary, triples, sidecar: ColumnarTripleIndex.Build(triples));

            string[] sidecarArtifacts = Directory.GetFiles(directory, "cidx-*.cidx");
            Assert.HasCount(1, sidecarArtifacts, "Exactly one columnar sidecar artifact was persisted.");
            byte[] bytes = File.ReadAllBytes(sidecarArtifacts[0]);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(sidecarArtifacts[0], bytes);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome, "The dictionary and system-of-record still load, so the generation loads.");
            Assert.IsNull(load.Sidecar, "A corrupt re-derivable sidecar drops to null rather than failing the load.");

            using DecodedItemSegment recovered = load.Triples!;
            Assert.IsTrue(triples.AsSpan().SequenceEqual(recovered.Span), "The system-of-record is unaffected by the corrupt sidecar.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Persisting a default graph plus named graphs then loading recovers each graph's triples keyed by its graph-name term id — the per-graph segments round-trip.</summary>
    [TestMethod]
    public void PersistThenLoadRoundTripsNamedGraphs()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            TermDictionary dictionary = new(0x6A6);
            TermId s = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/s")));
            TermId p = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/p")));
            TermId o = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/o")));
            TermId g1 = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/g1")));
            TermId g2 = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/g2")));
            TermId a = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/a")));
            TermId b = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/b")));

            EncodedTriple[] defaultTriples = [new(s, p, o)];
            EncodedTriple[] g1Triples = [new(a, p, b)];
            EncodedTriple[] g2Triples = [new(b, p, a), new(a, p, o)];
            List<(TermId GraphName, ReadOnlyMemory<EncodedTriple> Triples)> namedGraphs =
            [
                (g1, g1Triples),
                (g2, g2Triples),
            ];

            DurableSystemOfRecordCommit commit = store.Persist(dictionary, defaultTriples, namedGraphs);
            Assert.AreEqual(4, commit.TripleCount, "The total triple count spans the default and both named graphs.");

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Loaded, load.Outcome);
            using DecodedItemSegment recoveredDefault = load.Triples!;
            HashSet<EncodedTriple> recoveredDefaultSet = [.. recoveredDefault.Span];

            Assert.HasCount(2, load.NamedGraphs, "Both named graphs are recovered.");
            Dictionary<TermId, HashSet<EncodedTriple>> recoveredNamed = [];
            foreach((TermId graphName, DecodedItemSegment segment) in load.NamedGraphs)
            {
                recoveredNamed[graphName] = [.. segment.Span];
                segment.Dispose();
            }

            Assert.IsTrue(new HashSet<EncodedTriple>(defaultTriples).SetEquals(recoveredDefaultSet), "The default graph round-trips.");
            Assert.IsTrue(new HashSet<EncodedTriple>(g1Triples).SetEquals(recoveredNamed[g1]), "Named graph g1 round-trips keyed by its term id.");
            Assert.IsTrue(new HashSet<EncodedTriple>(g2Triples).SetEquals(recoveredNamed[g2]), "Named graph g2 round-trips keyed by its term id.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A corrupt named-graph artifact fails the load — named graphs are system-of-record-class, not re-derivable like the columnar sidecar, so their at-rest rot is refused rather than dropped.</summary>
    [TestMethod]
    public void CorruptNamedGraphArtifactIsRejected()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-sor-").FullName;
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            FileSystemPersistenceStore persistence = new(directory, NoOpBarrier);
            DurableSystemOfRecordStore store = new(persistence, pool);

            TermDictionary dictionary = new(0x6A6);
            TermId s = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/s")));
            TermId p = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/p")));
            TermId o = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/o")));
            TermId g = dictionary.GetOrAdd((RdfTerm)new NamedNode(Utf8Strings.From("http://example.org/g")));

            EncodedTriple[] defaultTriples = [new(s, p, o)];
            List<(TermId GraphName, ReadOnlyMemory<EncodedTriple> Triples)> namedGraphs = [(g, new EncodedTriple[] { new(s, p, g) })];
            store.Persist(dictionary, defaultTriples, namedGraphs);

            string[] namedArtifacts = Directory.GetFiles(directory, "nsor-*.sor");
            Assert.HasCount(1, namedArtifacts, "Exactly one named-graph artifact was persisted.");
            byte[] bytes = File.ReadAllBytes(namedArtifacts[0]);
            bytes[bytes.Length / 2] ^= 0xFF;
            File.WriteAllBytes(namedArtifacts[0], bytes);

            using Utf8StringPool termPool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            DurableSystemOfRecordLoad load = store.TryLoad(termPool, triplePool);

            Assert.AreEqual(DurableSystemOfRecordLoadOutcome.Rejected, load.Outcome, "A corrupt named-graph segment fails the load (system-of-record-class, not re-derivable).");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
