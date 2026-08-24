using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The content-hash reconcile converges two replicas built under DIFFERENT dictionaries to their union: the same
/// triple cancels by content even though each replica numbered its terms differently, the peer-only difference is
/// fetched as terms and re-encoded into the local dictionary, and the local replica ends holding the union. The
/// in-memory fetch stubs stand in for the wire (the peer serves its own content-hash sketch and resolves its
/// peer-only keys through its own side-map and dictionary).
/// </summary>
[TestClass]
internal sealed class ContentHashAntiEntropySessionTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A named node for an IRI string.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Encodes a triple of named nodes against a dictionary, assigning identifiers as needed.</summary>
    /// <param name="dictionary">The dictionary to encode against.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <param name="@object">The object.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermDictionary dictionary, NamedNode subject, NamedNode predicate, NamedNode @object)
    {
        return EncodedTriple.FromEncoded(dictionary.GetOrAdd((RdfTerm)subject).Encoded, dictionary.GetOrAdd((RdfTerm)predicate).Encoded, dictionary.GetOrAdd((RdfTerm)@object).Encoded);
    }

    /// <summary>Builds a replica's content-hash sketch image at a budget and wraps it as an owned fetch result — the value the peer fetch returns.</summary>
    /// <param name="index">The replica whose triples are projected.</param>
    /// <param name="projection">The replica's content-hash projection.</param>
    /// <param name="symbolBudget">The symbol budget.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from and the owned image is rented from.</param>
    /// <returns>The content-hash sketch image as an owned <see cref="SketchFetchResult"/>.</returns>
    private static SketchFetchResult ContentHashSketchImage(ColumnarTripleIndex index, ContentHashReconciliationProjection projection, int symbolBudget, MemoryPool<byte> pool)
    {
        List<ContentKey128> items = [];
        foreach(EncodedTriple triple in index.EnumerateTriples())
        {
            items.Add(projection.Project(triple));
        }

        ContentKey128[] itemArray = [.. items];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(itemArray, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchChannelStamps.OwnedImage(SketchChannelDomain.ContentHash, 0, writer.WrittenMemory, pool);
    }

    /// <summary>Two replicas built under different dictionaries converge to the union: the shared triple cancels, the peer-only triple is fetched and re-encoded, and the local replica ends holding all three triples by content.</summary>
    [TestMethod]
    public async Task ConvergesToUnionAcrossDivergentDictionaries()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasHash hash = VeritasHashing.Default;
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        NamedNode object1 = Iri("http://example.org/o1");
        NamedNode object2 = Iri("http://example.org/o2");
        NamedNode object3 = Iri("http://example.org/o3");

        TermDictionary localDictionary = new();
        EncodedTriple localObject1 = Encode(localDictionary, subject, predicate, object1);
        EncodedTriple localObject2 = Encode(localDictionary, subject, predicate, object2);
        ColumnarTripleIndex local = ColumnarTripleIndex.Build([localObject1, localObject2]);

        //The peer numbers its terms in a different order, so the same triples get different identifiers there.
        TermDictionary peerDictionary = new();
        EncodedTriple peerObject3 = Encode(peerDictionary, subject, predicate, object3);
        EncodedTriple peerObject2 = Encode(peerDictionary, subject, predicate, object2);
        ColumnarTripleIndex peer = ColumnarTripleIndex.Build([peerObject3, peerObject2]);

        ContentHashReconciliationProjection peerProjection = new(peerDictionary, hash, pool);
        ContentHashSideMap peerSideMap = ContentHashSideMap.Build(peer, peerProjection.Projection);

        int sketchFetchCalls = 0;
        AsyncSketchFetchDelegate sketchFetch = (symbolBudget, token) =>
        {
            sketchFetchCalls++;

            return new ValueTask<SketchFetchResult>(ContentHashSketchImage(peer, peerProjection, symbolBudget, pool));
        };
        AsyncContentTripleFetchDelegate tripleFetch = (keys, onTriple, token) =>
        {
            foreach(ContentKey128 key in keys)
            {
                if(peerSideMap.TryResolve(key, out EncodedTriple triple))
                {
                    ContentTriple resolved = new(peerDictionary.Resolve(triple.Subject.Encoded), peerDictionary.Resolve(triple.Predicate.Encoded), peerDictionary.Resolve(triple.Object.Encoded));
                    onTriple(in resolved);
                }
            }

            return ValueTask.CompletedTask;
        };

        AntiEntropySessionResult result = await ContentHashAntiEntropySession.ReconcileAsync(local, localDictionary, hash, ReplicationPolicy.Default, pool, TimeProvider.System, sketchFetch, tripleFetch, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "Diverged replicas converge in the content-hash domain.");
        Assert.AreEqual(1, sketchFetchCalls, "The peer sketch was fetched once.");

        ContentHashReconciliationProjection localProjection = new(localDictionary, hash, pool);
        HashSet<ContentKey128> convergedKeys = [];
        foreach(EncodedTriple triple in result.ConvergedIndex.EnumerateTriples())
        {
            convergedKeys.Add(localProjection.Project(triple));
        }

        HashSet<ContentKey128> expected = [localProjection.Project(localObject1), localProjection.Project(localObject2), peerProjection.Project(peerObject3)];
        Assert.HasCount(3, convergedKeys, "The converged index holds three distinct triples.");
        Assert.IsTrue(expected.SetEquals(convergedKeys), "The local replica converges to the union (by content).");
    }

    /// <summary>When the peer cannot return a triple for every peer-only key, the reconcile declines (PeerTriplesIncomplete) and leaves the local index unchanged — it never half-applies, so a later round retries.</summary>
    [TestMethod]
    public async Task ShortTripleFetchDeclinesWithoutApplying()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasHash hash = VeritasHashing.Default;
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        NamedNode object1 = Iri("http://example.org/o1");
        NamedNode object3 = Iri("http://example.org/o3");

        TermDictionary localDictionary = new();
        EncodedTriple localObject1 = Encode(localDictionary, subject, predicate, object1);
        ColumnarTripleIndex local = ColumnarTripleIndex.Build([localObject1]);

        TermDictionary peerDictionary = new();
        EncodedTriple peerObject3 = Encode(peerDictionary, subject, predicate, object3);
        ColumnarTripleIndex peer = ColumnarTripleIndex.Build([peerObject3]);
        ContentHashReconciliationProjection peerProjection = new(peerDictionary, hash, pool);

        AsyncSketchFetchDelegate sketchFetch = (symbolBudget, token) => new ValueTask<SketchFetchResult>(ContentHashSketchImage(peer, peerProjection, symbolBudget, pool));
        AsyncContentTripleFetchDelegate noTriples = (keys, onTriple, token) => ValueTask.CompletedTask;

        AntiEntropySessionResult result = await ContentHashAntiEntropySession.ReconcileAsync(local, localDictionary, hash, ReplicationPolicy.Default, pool, TimeProvider.System, sketchFetch, noTriples, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerTriplesIncomplete, result.Outcome, "A peer that resolves no peer-only triple declines.");
        Assert.IsFalse(result.IsComplete, "A declined reconcile is not complete.");
        Assert.AreSame(local, result.ConvergedIndex, "A declined content-hash reconcile leaves the local index unchanged.");
    }
}
