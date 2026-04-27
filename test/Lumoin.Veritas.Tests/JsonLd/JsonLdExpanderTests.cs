using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using System.Text;

namespace Lumoin.Veritas.Tests.JsonLd;

[TestClass]
internal sealed class JsonLdExpanderTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly ContextResolverDelegate NullResolver =
        (_, _) => ValueTask.FromResult<Utf8String?>(null);

    [TestMethod]
    public async Task ExtractQuadsFromSimpleDocument()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/person/1",
                "@type": "Person"
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotEmpty(quads);
        Quad typeQuad = quads.Single(q => q.Predicate.Iri.ToString() ==
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
        Assert.AreEqual("http://example.org/person/1", ((NamedNode)typeQuad.Subject).Iri.ToString());
        Assert.AreEqual("http://schema.org/Person", ((NamedNode)typeQuad.Object).Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromStringLiteralProperty()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/person/1",
                "name": "Alice"
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad nameQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/name");
        Literal literal = (Literal)nameQuad.Object;
        Assert.AreEqual("Alice", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", literal.Datatype.Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromLanguageTaggedProperty()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {
                    "@vocab": "http://schema.org/",
                    "@language": "en"
                },
                "@id": "http://example.org/person/1",
                "name": "Alice"
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad nameQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/name");
        Literal literal = (Literal)nameQuad.Object;
        Assert.AreEqual("Alice", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", literal.Datatype.Iri.ToString());
        Assert.AreEqual("en", literal.Language?.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromBooleanLiteral()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/thing",
                "active": true
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad activeQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/active");
        Literal literal = (Literal)activeQuad.Object;
        Assert.AreEqual("true", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#boolean", literal.Datatype.Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromIntegerLiteral()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/thing",
                "count": 42
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad countQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/count");
        Literal literal = (Literal)countQuad.Object;
        Assert.AreEqual("42", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", literal.Datatype.Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromTypeCoercedIdProperty()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {
                    "@vocab": "http://schema.org/",
                    "knows": {"@id": "http://xmlns.com/foaf/0.1/knows", "@type": "@id"}
                },
                "@id": "http://example.org/alice",
                "knows": "http://example.org/bob"
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad knowsQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://xmlns.com/foaf/0.1/knows");
        Assert.IsInstanceOfType<NamedNode>(knowsQuad.Object);
        Assert.AreEqual("http://example.org/bob", ((NamedNode)knowsQuad.Object).Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromNestedObject()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/alice",
                "address": {
                    "@type": "PostalAddress",
                    "addressCountry": "FI"
                }
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        //The address quad links alice to a blank node.
        Quad addressQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/address");
        Assert.IsInstanceOfType<BlankNode>(addressQuad.Object);

        //The blank node should have a type triple.
        RdfTerm addressNode = addressQuad.Object;
        Quad typeQuad = quads.Single(q =>
            q.Subject == addressNode &&
            q.Predicate.Iri.ToString() == "http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
        Assert.AreEqual("http://schema.org/PostalAddress", ((NamedNode)typeQuad.Object).Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromNamedGraph()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/graph1",
                "@graph": {
                    "@id": "http://example.org/alice",
                    "name": "Alice"
                }
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad nameQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/name");
        Assert.IsNotNull(nameQuad.Graph);
        Assert.AreEqual("http://example.org/graph1", ((NamedNode)nameQuad.Graph!).Iri.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromMultipleTypes()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/thing",
                "@type": ["Person", "Agent"]
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        List<Quad> typeQuads = quads
            .Where(q => q.Predicate.Iri.ToString() == "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
            .ToList();

        Assert.HasCount(2, typeQuads);
    }

    [TestMethod]
    public async Task ExtractQuadsFromArrayProperty()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/thing",
                "name": ["Alice", "Alicia"]
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        List<Quad> nameQuads = quads
            .Where(q => q.Predicate.Iri.ToString() == "http://schema.org/name")
            .ToList();

        Assert.HasCount(2, nameQuads);
    }

    [TestMethod]
    public async Task ExtractQuadsFromExplicitValueObject()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "http://example.org/thing",
                "name": {"@value": "Alice", "@language": "en"}
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Quad nameQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/name");
        Literal literal = (Literal)nameQuad.Object;
        Assert.AreEqual("Alice", literal.Value.ToString());
        Assert.AreEqual("en", literal.Language?.ToString());
    }

    [TestMethod]
    public async Task ExtractQuadsFromCompactIriInContext()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {
                    "schema": "http://schema.org/",
                    "foaf": "http://xmlns.com/foaf/0.1/"
                },
                "@id": "http://example.org/alice",
                "foaf:name": "Alice",
                "schema:age": 30
            }
            """);

        List<Quad> quads = await ExtractAsync(doc, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(quads.Find(q => q.Predicate.Iri.ToString() == "http://xmlns.com/foaf/0.1/name"),
            "Expected a quad with foaf:name predicate.");
        Assert.IsNotNull(quads.Find(q => q.Predicate.Iri.ToString() == "http://schema.org/age"),
            "Expected a quad with schema:age predicate.");
    }

    [TestMethod]
    public async Task ExtractQuadsUsesBaseUrlForRelativeIds()
    {
        JsonLdNode doc = ParseJson(/*lang=json,strict*/ """
            {
                "@context": {"@vocab": "http://schema.org/"},
                "@id": "person/1",
                "name": "Alice"
            }
            """);

        using Utf8StringPool pool = new();
        List<Quad> quads = [];

        await foreach(Quad quad in JsonLdExpander.ExtractQuadsAsync(
            doc, "http://example.org/", NullResolver, StjJsonLdAdapter.Parse, pool, TestContext.CancellationToken)
            .ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        Quad nameQuad = quads.Single(q => q.Predicate.Iri.ToString() == "http://schema.org/name");
        Assert.AreEqual("http://example.org/person/1", ((NamedNode)nameQuad.Subject).Iri.ToString());
    }

    /// <summary>
    /// Parses a .NET string of JSON text into a <see cref="JsonLdNode"/> via the
    /// bundled <see cref="StjJsonLdAdapter"/>.
    /// </summary>
    /// <param name="json">The JSON text to parse.</param>
    /// <returns>The parsed document root.</returns>
    private static JsonLdNode ParseJson(string json)
    {
        Utf8String utf8 = Utf8String.WithoutPrecomputedHash(Encoding.UTF8.GetBytes(json));

        return StjJsonLdAdapter.Parse(utf8);
    }

    /// <summary>
    /// Runs the expander over <paramref name="doc"/> with a fresh string pool and
    /// the null resolver, returning every emitted quad.
    /// </summary>
    /// <param name="doc">The pre-parsed JSON-LD root node.</param>
    /// <param name="cancellationToken">Token to cancel the enumeration.</param>
    /// <returns>The full list of quads produced by the expander.</returns>
    private static async Task<List<Quad>> ExtractAsync(JsonLdNode doc, CancellationToken cancellationToken)
    {
        using Utf8StringPool pool = new();
        List<Quad> quads = [];

        await foreach(Quad quad in JsonLdExpander.ExtractQuadsAsync(
            doc, null, NullResolver, StjJsonLdAdapter.Parse, pool, cancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }
}
