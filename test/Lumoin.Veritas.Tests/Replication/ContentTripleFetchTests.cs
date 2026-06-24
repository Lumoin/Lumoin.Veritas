using System.Collections.Generic;
using System.IO.Pipelines;
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
/// The content-hash triple-fetch transport round-trips over a real pipe: the server resolves a requested item
/// through its side-map and decodes it through its dictionary into terms, the client reads them back, and the
/// terms re-hash to the requested key (so the wire serialization preserves the triple's content). A key the server
/// does not hold is dropped from the response.
/// </summary>
[TestClass]
internal sealed class ContentTripleFetchTests
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

    /// <summary>A held key resolves to its triple over the wire and the terms re-hash to it; an unknown key is dropped.</summary>
    [TestMethod]
    public async Task ServesTriplesForHeldKeysAndSkipsUnknownOnes()
    {
        using VeritasMemoryPool<byte> pool = new();
        VeritasHash hash = VeritasHashing.Default;
        TermDictionary dictionary = new();
        NamedNode subject = Iri("http://example.org/s");
        NamedNode predicate = Iri("http://example.org/p");
        EncodedTriple held = Encode(dictionary, subject, predicate, Iri("http://example.org/o1"));
        EncodedTriple alsoHeld = Encode(dictionary, subject, predicate, Iri("http://example.org/o2"));
        ColumnarTripleIndex index = ColumnarTripleIndex.Build([held, alsoHeld]);
        ContentHashReconciliationProjection projection = new(dictionary, hash, pool);
        ContentHashSideMap sideMap = ContentHashSideMap.Build(index, projection.Projection);

        //A key the side-map does not hold (a triple the dictionary can encode but the index never indexed).
        EncodedTriple absent = Encode(dictionary, subject, predicate, Iri("http://example.org/o3"));
        ContentKey128 heldKey = projection.Project(held);
        ContentKey128 absentKey = projection.Project(absent);

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ContentTripleFetchServer server = new(sideMap, dictionary, requestPipe.Reader, responsePipe.Writer);
        Task serve = server.ServeAsync(TestContext.CancellationToken);
        ContentTripleFetchClient client = new(requestPipe.Writer, responsePipe.Reader);

        IReadOnlyList<ContentTriple> result = await client.FetchAsync([heldKey, absentKey], TestContext.CancellationToken).ConfigureAwait(false);
        await serve.ConfigureAwait(false);

        Assert.HasCount(1, result, "Only the held key resolves; the unknown key is skipped.");
        Assert.AreEqual(heldKey, projection.ProjectTerms(result[0].Subject, result[0].Predicate, result[0].Object), "The fetched triple's terms re-hash to the requested key — the wire preserves the content.");
    }
}
