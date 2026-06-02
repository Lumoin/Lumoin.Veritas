using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using System.Text;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.Tests.JsonLd;

/// <summary>
/// Verifies the source-aware overload
/// <see cref="JsonLdExpander.ExtractQuadsWithSourceAsync"/>. The contract:
/// every emitted quad carries a non-null <see cref="DocumentNodeRef"/>;
/// the <see cref="DocumentNodeRef.DocumentId"/> equals the caller-supplied
/// identity; <see cref="DocumentNodeRef.Index"/> is a sequential
/// zero-based ordinal in expansion order; and the quads themselves are
/// identical to those produced by the bare
/// <see cref="JsonLdExpander.ExtractQuadsAsync"/> overload.
/// </summary>
[TestClass]
internal sealed class JsonLdExpanderSourceTests
{
    public TestContext TestContext { get; set; } = null!;

    private static ContextResolverDelegate NullResolver { get; } =
        (_, _) => ValueTask.FromResult<Utf8String?>(null);

    [TestMethod]
    public async Task ExtractQuadsWithSourceAsyncProducesEmittedQuadsWithSourceRef()
    {
        JsonNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"name": "http://example.org/name"},
                "@id": "http://example.org/alice",
                "name": "Alice"
            }
            """);

        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool = new();

        List<EmittedQuad> result = [];
        await foreach(EmittedQuad emitted in JsonLdExpander.ExtractQuadsWithSourceAsync(
            doc, documentId, baseUrl: null, NullResolver, StjJsonAdapter.Parse,
            pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            result.Add(emitted);
        }

        Assert.IsNotEmpty(result);
        foreach(EmittedQuad emitted in result)
        {
            Assert.IsNotNull(emitted.Source);
        }
    }

    [TestMethod]
    public async Task ExtractQuadsWithSourceAsyncAssignsSequentialIndexesStartingAtZero()
    {
        JsonNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"name": "http://example.org/name", "knows": "http://example.org/knows"},
                "@id": "http://example.org/alice",
                "name": "Alice",
                "knows": {"@id": "http://example.org/bob"}
            }
            """);

        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool = new();

        List<EmittedQuad> result = [];
        await foreach(EmittedQuad emitted in JsonLdExpander.ExtractQuadsWithSourceAsync(
            doc, documentId, baseUrl: null, NullResolver, StjJsonAdapter.Parse,
            pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            result.Add(emitted);
        }

        Assert.IsNotEmpty(result);
        for(int i = 0; i < result.Count; i++)
        {
            Assert.AreEqual(i, result[i].Source!.Value.Index);
        }
    }

    [TestMethod]
    public async Task ExtractQuadsWithSourceAsyncCarriesDocumentIdOnEverySource()
    {
        JsonNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"name": "http://example.org/name"},
                "@id": "http://example.org/alice",
                "name": "Alice"
            }
            """);

        DocumentId documentId = new(0xDEADBEEF);
        using Utf8StringPool pool = new();

        await foreach(EmittedQuad emitted in JsonLdExpander.ExtractQuadsWithSourceAsync(
            doc, documentId, baseUrl: null, NullResolver, StjJsonAdapter.Parse,
            pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            Assert.AreEqual(documentId, emitted.Source!.Value.DocumentId);
        }
    }

    [TestMethod]
    public async Task ExtractQuadsWithSourceAsyncProducesSameQuadsAsExtractQuadsAsync()
    {
        const string json = /*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://example.org/"},
                "@id": "http://example.org/alice",
                "@type": "Person",
                "name": "Alice",
                "knows": {"@id": "http://example.org/bob"}
            }
            """;

        DocumentId documentId = new(0xABCD);

        JsonNode doc1 = ParseJson(json);
        using Utf8StringPool pool1 = new();
        List<Quad> bare = [];
        await foreach(Quad q in JsonLdExpander.ExtractQuadsAsync(
            doc1, baseUrl: null, NullResolver, StjJsonAdapter.Parse,
            pool1, TestContext.CancellationToken).ConfigureAwait(false))
        {
            bare.Add(q);
        }

        JsonNode doc2 = ParseJson(json);
        using Utf8StringPool pool2 = new();
        List<EmittedQuad> sourced = [];
        await foreach(EmittedQuad e in JsonLdExpander.ExtractQuadsWithSourceAsync(
            doc2, documentId, baseUrl: null, NullResolver, StjJsonAdapter.Parse,
            pool2, TestContext.CancellationToken).ConfigureAwait(false))
        {
            sourced.Add(e);
        }

        Assert.HasCount(bare.Count, sourced);
        for(int i = 0; i < bare.Count; i++)
        {
            Assert.AreEqual(bare[i], sourced[i].Quad);
        }
    }

    [TestMethod]
    public async Task ExtractQuadsWithSourceAsyncRejectsNullParser()
    {
        JsonNode doc = ParseJson(/*lang=json,strict*/ """{"@id":"http://example.org/x"}""");
        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool = new();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach(EmittedQuad _ in JsonLdExpander.ExtractQuadsWithSourceAsync(
                doc, documentId, baseUrl: null, NullResolver, parser: null!,
                pool, TestContext.CancellationToken).ConfigureAwait(false))
            {
                //Iterator must throw at first MoveNextAsync.
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses a .NET string of JSON text into a <see cref="JsonNode"/> via the
    /// bundled <see cref="StjJsonAdapter"/>.
    /// </summary>
    /// <param name="json">The JSON text to parse.</param>
    /// <returns>The parsed document root.</returns>
    private static JsonNode ParseJson(string json)
    {
        Utf8String utf8 = Utf8String.WithoutPrecomputedHash(Encoding.UTF8.GetBytes(json));

        return StjJsonAdapter.Parse(utf8);
    }
}
