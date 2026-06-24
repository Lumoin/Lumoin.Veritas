using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The content-hash domain end-to-end over the real wire: a local replica reconciles against a peer built under a
/// different dictionary, pulling the peer's content-hash sketch through the content-hash sketch channel server and
/// its peer-only triples through the triple-fetch server, and converges to the union — the cross-organization
/// active-active path over pipes (no socket), deterministic, no wall-clock waits.
/// </summary>
[TestClass]
internal sealed class ContentHashChannelReconcileTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A named node for an IRI string.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8String.FromString(iri));
    }

    /// <summary>Encodes a triple of named nodes against a dictionary.</summary>
    /// <param name="dictionary">The dictionary to encode against.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="predicate">The predicate.</param>
    /// <param name="@object">The object.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Encode(TermDictionary dictionary, NamedNode subject, NamedNode predicate, NamedNode @object)
    {
        return EncodedTriple.FromEncoded(dictionary.GetOrAdd((RdfTerm)subject).Encoded, dictionary.GetOrAdd((RdfTerm)predicate).Encoded, dictionary.GetOrAdd((RdfTerm)@object).Encoded);
    }

    /// <summary>A local replica converges to the union of itself and a divergent-dictionary peer over the content-hash sketch and triple-fetch channels.</summary>
    [TestMethod]
    public async Task ConvergesToUnionOverTheContentHashChannels()
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

        TermDictionary peerDictionary = new();
        EncodedTriple peerObject3 = Encode(peerDictionary, subject, predicate, object3);
        EncodedTriple peerObject2 = Encode(peerDictionary, subject, predicate, object2);
        ColumnarTripleIndex peer = ColumnarTripleIndex.Build([peerObject3, peerObject2]);
        ContentHashReconciliationProjection peerProjection = new(peerDictionary, hash, pool);
        ContentHashSideMap peerSideMap = ContentHashSideMap.Build(peer, peerProjection.Projection);

        AsyncSketchFetchDelegate sketchFetch = async (symbolBudget, token) =>
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            ContentHashSketchChannelServer server = new(peer, peerProjection, pool, requestPipe.Reader, responsePipe.Writer);
            Task serve = server.ServeAsync(token);
            SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader);

            ReadOnlyMemory<byte> image = await client.FetchAsync(symbolBudget, token).ConfigureAwait(false);
            await serve.ConfigureAwait(false);

            return image;
        };
        AsyncContentTripleFetchDelegate tripleFetch = async (keys, token) =>
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            ContentTripleFetchServer server = new(peerSideMap, peerDictionary, requestPipe.Reader, responsePipe.Writer);
            Task serve = server.ServeAsync(token);
            ContentTripleFetchClient client = new(requestPipe.Writer, responsePipe.Reader);

            IReadOnlyList<ContentTriple> triples = await client.FetchAsync(keys, token).ConfigureAwait(false);
            await serve.ConfigureAwait(false);

            return triples;
        };

        AntiEntropySessionResult result = await ContentHashAntiEntropySession.ReconcileAsync(local, localDictionary, hash, ReplicationPolicy.Default, pool, TimeProvider.System, sketchFetch, tripleFetch, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "The replica converges over the content-hash channels.");

        ContentHashReconciliationProjection localProjection = new(localDictionary, hash, pool);
        HashSet<ContentKey128> convergedKeys = [];
        foreach(EncodedTriple triple in result.ConvergedIndex.EnumerateTriples())
        {
            convergedKeys.Add(localProjection.Project(triple));
        }

        HashSet<ContentKey128> expected = [localProjection.Project(localObject1), localProjection.Project(localObject2), peerProjection.Project(peerObject3)];
        Assert.HasCount(3, convergedKeys, "The converged index holds three distinct triples.");
        Assert.IsTrue(expected.SetEquals(convergedKeys), "The local replica converges to the union over the wire.");
    }
}
