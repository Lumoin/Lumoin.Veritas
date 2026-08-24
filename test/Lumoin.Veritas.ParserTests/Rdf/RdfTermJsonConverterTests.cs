using System.IO;
using System.Text;
using System.Text.Json;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Rdf.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Tests for <see cref="RdfTermJsonConverter"/>: that the RDF/JSON term encoding is byte-stable, walks deeply
/// nested quoted triples without recursion, and bounds nesting with a catchable depth exception on the write side.
/// </summary>
[TestClass]
internal sealed class RdfTermJsonConverterTests
{
    /// <summary>A shallow quoted triple writes the exact RDF/JSON shape: type first, then subject/predicate/object as full term objects.</summary>
    [TestMethod]
    public void ShallowTripleTermWriteIsByteStable()
    {
        RdfTerm term = new TripleTerm(
            new NamedNode(Utf8Strings.From("http://example/s")),
            new NamedNode(Utf8Strings.From("http://example/p")),
            new NamedNode(Utf8Strings.From("http://example/o")));

        Assert.AreEqual(
            "{\"type\":\"triple\",\"subject\":{\"type\":\"uri\",\"value\":\"http://example/s\"},\"predicate\":{\"type\":\"uri\",\"value\":\"http://example/p\"},\"object\":{\"type\":\"uri\",\"value\":\"http://example/o\"}}",
            WriteTerm(term));
    }

    /// <summary>A quoted triple nested at the limit writes to completion (one "triple" discriminator per level).</summary>
    [TestMethod]
    public void DeepTripleTermWritesAtTheNestingLimit()
    {
        string json = WriteTerm(NestSubject(QuotedTripleLimits.MaxNestingDepth));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, json.Split("\"triple\"").Length - 1);
    }

    /// <summary>A quoted triple nested beyond the limit throws the catchable depth exception, not a stack overflow.</summary>
    [TestMethod]
    public void DeepTripleTermWriteBeyondTheNestingLimitThrows()
    {
        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => WriteTerm(NestSubject(QuotedTripleLimits.MaxNestingDepth + 1)));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth + 1, exception.Depth);
        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    /// <summary>A nested quoted triple round-trips through serialize then deserialize unchanged.</summary>
    [TestMethod]
    public void NestedTripleTermRoundTrips()
    {
        RdfTerm term = NestSubject(3);

        string json = JsonSerializer.Serialize(term, Options);
        RdfTerm? back = JsonSerializer.Deserialize<RdfTerm>(json, Options);

        Assert.AreEqual(term, back);
    }

    /// <summary>A triple term whose <c>type</c> discriminator appears after its components still parses (term built at the closing brace).</summary>
    [TestMethod]
    public void TripleTermParsesWithTypePropertyLast()
    {
        string json = "{\"subject\":{\"type\":\"uri\",\"value\":\"http://example/s\"},"
            + "\"predicate\":{\"type\":\"uri\",\"value\":\"http://example/p\"},"
            + "\"object\":{\"type\":\"uri\",\"value\":\"http://example/o\"},\"type\":\"triple\"}";

        RdfTerm? term = JsonSerializer.Deserialize<RdfTerm>(json, Options);

        Assert.AreEqual(new TripleTerm(Iri("s"), Iri("p"), Iri("o")), term);
    }

    /// <summary>Unknown properties — scalar and nested-object alike — are skipped, not parsed as components or pushed onto the stack.</summary>
    [TestMethod]
    public void UnknownPropertiesAreSkipped()
    {
        string json = "{\"type\":\"uri\",\"value\":\"http://example/s\",\"extra\":42,\"nested\":{\"a\":{\"b\":1}}}";

        RdfTerm? term = JsonSerializer.Deserialize<RdfTerm>(json, Options);

        Assert.AreEqual(Iri("s"), term);
    }

    /// <summary>A quad with quoted triples in subject and object round-trips through the quad converter, proving the term reader leaves the cursor on each term's closing brace so the four delegated reads stay in sync.</summary>
    [TestMethod]
    public void QuadWithTripleTermsRoundTripsThroughQuadConverter()
    {
        Quad quad = new(
            new TripleTerm(Iri("s1"), Iri("p1"), Iri("o1")),
            Iri("p"),
            new TripleTerm(Iri("s2"), Iri("p2"), Iri("o2")),
            Iri("g"));

        string json = JsonSerializer.Serialize(quad, Options);
        Quad? back = JsonSerializer.Deserialize<Quad>(json, Options);

        Assert.AreEqual(quad, back);
    }

    /// <summary>Deserializing from UTF-8 bytes copies multi-byte and escaped values straight from the reader, with no UTF-16 round-trip.</summary>
    [TestMethod]
    public void DeserializesMultiByteAndEscapedValuesFromBytes()
    {
        byte[] uri = Encoding.UTF8.GetBytes("{\"type\":\"uri\",\"value\":\"http://example.org/café\"}");
        RdfTerm? named = JsonSerializer.Deserialize<RdfTerm>(uri, Options);
        Assert.AreEqual("http://example.org/café", ((NamedNode)named!).Iri.ToString());
        Assert.AreSequenceEqual(Encoding.UTF8.GetBytes("http://example.org/café"), ((NamedNode)named).Iri.Span.ToArray());

        //A \u escape in the value is unescaped by the byte copy, not left literal.
        byte[] escaped = Encoding.UTF8.GetBytes("{\"type\":\"bnode\",\"value\":\"b\\u0030\"}");
        RdfTerm? blank = JsonSerializer.Deserialize<RdfTerm>(escaped, Options);
        Assert.AreEqual("b0", ((BlankNode)blank!).Label.ToString());

        byte[] literal = Encoding.UTF8.GetBytes("{\"type\":\"literal\",\"value\":\"café ☕\",\"language\":\"fr\"}");
        RdfTerm? lit = JsonSerializer.Deserialize<RdfTerm>(literal, Options);
        Assert.AreEqual("café ☕", ((Literal)lit!).Value.ToString());
        Assert.AreEqual("fr", ((Literal)lit).Language!.Value.ToString());
    }

    /// <summary>Serializer options with the RDF term and quad converters registered.</summary>
    private static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Builds the serializer options used by the round-trip tests.</summary>
    /// <returns>The options.</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new RdfTermJsonConverter());
        options.Converters.Add(new QuadJsonConverter());

        return options;
    }

    /// <summary>Builds an example named node from a local name.</summary>
    /// <param name="local">The local name appended to the example namespace.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From("http://example/" + local));
    }

    /// <summary>Serializes a term to compact RDF/JSON by invoking the converter directly.</summary>
    /// <param name="term">The term to serialize.</param>
    /// <returns>The RDF/JSON text.</returns>
    private static string WriteTerm(RdfTerm term)
    {
        RdfTermJsonConverter converter = new();
        using MemoryStream stream = new();
        using(Utf8JsonWriter writer = new(stream))
        {
            converter.Write(writer, term, JsonSerializerOptions.Default);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Builds a quoted triple nested <paramref name="depth"/> levels deep through the subject, with IRI leaves.</summary>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term.</returns>
    private static RdfTerm NestSubject(int depth)
    {
        NamedNode predicate = new(Utf8Strings.From("http://example/p"));
        RdfTerm leaf = new NamedNode(Utf8Strings.From("http://example/o"));

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }
}
