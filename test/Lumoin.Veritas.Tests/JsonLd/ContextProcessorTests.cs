using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using System.Text;

namespace Lumoin.Veritas.Tests.JsonLd;

[TestClass]
internal sealed class ContextProcessorTests
{
    public TestContext TestContext { get; set; } = null!;

    private static ContextResolverDelegate NullResolver { get; } =
        (_, _) => ValueTask.FromResult<Utf8String?>(null);

    [TestMethod]
    public async Task ProcessNullContextResetsToEmpty()
    {
        JsonLdContext initial = JsonLdContext.Empty
            .WithVocabularyMapping("http://example.org/")
            .WithDefaultLanguage("en");

        JsonLdNode doc = ParseJson("null");
        JsonLdContext result = await ContextProcessor.ProcessAsync(
            initial, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsNull(result.VocabularyMapping);
        Assert.IsNull(result.DefaultLanguage);
    }

    [TestMethod]
    public async Task ProcessInlineContextSetsVocab()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {"@vocab": "http://schema.org/"}
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("http://schema.org/", result.VocabularyMapping);
    }

    [TestMethod]
    public async Task ProcessInlineContextSetsBase()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {"@base": "http://example.org/base/"}
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("http://example.org/base/", result.BaseIri);
    }

    [TestMethod]
    public async Task ProcessInlineContextSetsLanguage()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {"@language": "fr"}
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("fr", result.DefaultLanguage);
    }

    [TestMethod]
    public async Task ProcessInlineContextDefinesSimpleTerm()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@vocab": "http://example.org/",
                "name": "http://schema.org/name"
            }
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.TryGetTerm("name", out TermDefinition? def));
        Assert.AreEqual("http://schema.org/name", def?.IriMapping);
    }

    [TestMethod]
    public async Task ProcessInlineContextDefinesExpandedTermDefinition()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "knows": {
                    "@id": "http://xmlns.com/foaf/0.1/knows",
                    "@type": "@id"
                }
            }
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.TryGetTerm("knows", out TermDefinition? def));
        Assert.AreEqual("http://xmlns.com/foaf/0.1/knows", def?.IriMapping);
        Assert.AreEqual("@id", def?.TypeMapping);
    }

    [TestMethod]
    public async Task ProcessArrayContextAppliesInOrder()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            [
                {"@vocab": "http://example.org/"},
                {"@language": "de"}
            ]
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("http://example.org/", result.VocabularyMapping);
        Assert.AreEqual("de", result.DefaultLanguage);
    }

    [TestMethod]
    public async Task ProcessNullContextInArrayResetsTerms()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            [
                {"@vocab": "http://example.org/", "name": "http://schema.org/name"},
                null,
                {"@vocab": "http://other.org/"}
            ]
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("http://other.org/", result.VocabularyMapping);
        //The term 'name' defined before the null context should be gone.
        Assert.IsFalse(result.TryGetTerm("name", out _));
    }

    [TestMethod]
    public async Task ProcessContextWithPrefixExpandsCompactIri()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "foaf": "http://xmlns.com/foaf/0.1/",
                "name": "foaf:name"
            }
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.TryGetTerm("name", out TermDefinition? def));
        Assert.AreEqual("http://xmlns.com/foaf/0.1/name", def?.IriMapping);
    }

    [TestMethod]
    public async Task ProcessContextWithDirectionSetsDirection()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {"@direction": "rtl"}
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("rtl", result.DefaultBaseDirection);
    }

    [TestMethod]
    public async Task ProcessContextWithInvalidDirectionThrows()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {"@direction": "invalid"}
            """);

        await Assert.ThrowsExactlyAsync<JsonLdProcessingException>(
            async () => await ContextProcessor.ProcessAsync(
                JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ProcessRemoteContextCallsResolver()
    {
        const string remoteContextUri = "http://example.org/context.jsonld";
        const string remoteContextJson = /*lang=json,strict*/ """
            {"@context": {"@vocab": "http://example.org/"}}
            """;

        ContextResolverDelegate resolver = (uri, _) =>
        {
            Assert.AreEqual(remoteContextUri, uri.ToString());
            Utf8String body = Utf8String.WithoutPrecomputedHash(Encoding.UTF8.GetBytes(remoteContextJson));

            return ValueTask.FromResult<Utf8String?>(body);
        };

        JsonLdNode doc = ParseJson($"\"{remoteContextUri}\"");

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, resolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual("http://example.org/", result.VocabularyMapping);
    }

    [TestMethod]
    public async Task ProcessRemoteContextThrowsWhenResolverReturnsNull()
    {
        JsonLdNode doc = ParseJson("\"http://example.org/missing.jsonld\"");

        JsonLdProcessingException ex = await Assert.ThrowsExactlyAsync<JsonLdProcessingException>(
            async () => await ContextProcessor.ProcessAsync(
                JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);

        Assert.AreEqual(JsonLdErrorCode.LoadingRemoteContextFailed, ex.ErrorCode);
    }

    [TestMethod]
    public async Task ProcessContextWithProtectedTermPreventsOverride()
    {
        JsonLdNode first = ParseJson(/*lang=json,strict*/ """
            {
                "@protected": true,
                "name": "http://schema.org/name"
            }
            """);

        JsonLdContext withProtected = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, first, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        JsonLdNode second = ParseJson(/*lang=json,strict*/ """
            {
                "name": "http://example.org/differentName"
            }
            """);

        await Assert.ThrowsExactlyAsync<JsonLdProcessingException>(
            async () => await ContextProcessor.ProcessAsync(
                withProtected, second, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ProcessContextWithContainerMapping()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "tags": {
                    "@id": "http://example.org/tags",
                    "@container": "@set"
                }
            }
            """);

        JsonLdContext result = await ContextProcessor.ProcessAsync(
            JsonLdContext.Empty, doc, null, NullResolver, StjJsonLdAdapter.Parse, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(result.TryGetTerm("tags", out TermDefinition? def));
        CollectionAssert.Contains(def?.ContainerMapping.ToList(), "@set");
    }

    /// <summary>
    /// Parses a .NET string of JSON text into a <see cref="JsonLdNode"/> via the
    /// bundled <see cref="StjJsonLdAdapter"/>. The bytes are not interned in any
    /// pool because they live only for the duration of this call.
    /// </summary>
    /// <param name="json">The JSON text to parse.</param>
    /// <returns>The parsed document root.</returns>
    private static JsonLdNode ParseJson(string json)
    {
        Utf8String utf8 = Utf8String.WithoutPrecomputedHash(Encoding.UTF8.GetBytes(json));

        return StjJsonLdAdapter.Parse(utf8);
    }
}
