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
            new NamedNode(Utf8String.FromString("http://example/s")),
            new NamedNode(Utf8String.FromString("http://example/p")),
            new NamedNode(Utf8String.FromString("http://example/o")));

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
        NamedNode predicate = new(Utf8String.FromString("http://example/p"));
        RdfTerm leaf = new NamedNode(Utf8String.FromString("http://example/o"));

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }
}
