using Lumoin.Veritas.Core;
using Lumoin.Veritas.NQuads;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Pipelines;
using System.Text;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class NQuadsReaderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ParseSimpleTriple()
    {
        const string input = "<http://example.org/s> <http://example.org/p> <http://example.org/o> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<NamedNode>(quads[0].Subject);
        Assert.AreEqual("http://example.org/s", ((NamedNode)quads[0].Subject).Iri.ToString());
        Assert.AreEqual("http://example.org/p", quads[0].Predicate.Iri.ToString());
        Assert.AreEqual("http://example.org/o", ((NamedNode)quads[0].Object).Iri.ToString());
        Assert.IsNull(quads[0].Graph);
    }

    [TestMethod]
    public async Task ParseQuadWithNamedGraph()
    {
        const string input = "<http://example.org/s> <http://example.org/p> <http://example.org/o> <http://example.org/g> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.IsNotNull(quads[0].Graph);
        Assert.IsInstanceOfType<NamedNode>(quads[0].Graph);
        NamedNode graph = (NamedNode)quads[0].Graph!;
        Assert.AreEqual("http://example.org/g", graph.Iri.ToString());
    }

    [TestMethod]
    public async Task ParseBlankNodeSubject()
    {
        const string input = "_:b0 <http://example.org/p> <http://example.org/o> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<BlankNode>(quads[0].Subject);
        Assert.AreEqual("b0", ((BlankNode)quads[0].Subject).Label.ToString());
    }

    [TestMethod]
    public async Task ParseBlankNodeObject()
    {
        const string input = "<http://example.org/s> <http://example.org/p> _:b1 .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<BlankNode>(quads[0].Object);
        Assert.AreEqual("b1", ((BlankNode)quads[0].Object).Label.ToString());
    }

    [TestMethod]
    public async Task ParseXsdStringLiteral()
    {
        const string input = "<http://example.org/s> <http://example.org/p> \"hello\"^^<http://www.w3.org/2001/XMLSchema#string> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Literal literal = (Literal)quads[0].Object;
        Assert.AreEqual("hello", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#string", literal.Datatype.Iri.ToString());
        Assert.IsNull(literal.Language);
    }

    [TestMethod]
    public async Task ParseLanguageTaggedLiteral()
    {
        const string input = "<http://example.org/s> <http://example.org/p> \"Bonjour\"@fr .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Literal literal = (Literal)quads[0].Object;
        Assert.AreEqual("Bonjour", literal.Value.ToString());
        Assert.AreEqual("fr", literal.Language!.Value.ToString());
    }

    [TestMethod]
    public async Task ParseSkipsEmptyLines()
    {
        const string input = """
            <http://example.org/s> <http://example.org/p> <http://example.org/o> .

            <http://example.org/s2> <http://example.org/p> <http://example.org/o2> .

            """;

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, quads);
    }

    [TestMethod]
    public async Task ParseSkipsCommentLines()
    {
        const string input = """
            # This is a comment.
            <http://example.org/s> <http://example.org/p> <http://example.org/o> .
            # Another comment.

            """;

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
    }

    [TestMethod]
    public async Task ParseEscapedNewlineInLiteral()
    {
        const string input = "<http://example.org/s> <http://example.org/p> \"line1\\nline2\"^^<http://www.w3.org/2001/XMLSchema#string> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("line1\nline2", ((Literal)quads[0].Object).Value.ToString());
    }

    [TestMethod]
    public async Task ParseEscapedQuoteInLiteral()
    {
        const string input = "<http://example.org/s> <http://example.org/p> \"say \\\"hello\\\"\"^^<http://www.w3.org/2001/XMLSchema#string> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("say \"hello\"", ((Literal)quads[0].Object).Value.ToString());
    }

    [TestMethod]
    public async Task ParseUnicodeEscapeInLiteral()
    {
        const string input = "<http://example.org/s> <http://example.org/p> \"caf\\u00E9\"^^<http://www.w3.org/2001/XMLSchema#string> .\n";

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("café", ((Literal)quads[0].Object).Value.ToString());
    }

    [TestMethod]
    public async Task ParseMultipleStatements()
    {
        const string input = """
            <http://example.org/s> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://example.org/Type> .
            <http://example.org/s> <http://example.org/name> "Alice"^^<http://www.w3.org/2001/XMLSchema#string> .
            <http://example.org/s> <http://example.org/age> "30"^^<http://www.w3.org/2001/XMLSchema#integer> .

            """;

        List<Quad> quads = await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(3, quads);
    }

    [TestMethod]
    public async Task ParseThrowsOnMissingStatementTerminator()
    {
        const string input = "<http://example.org/s> <http://example.org/p> <http://example.org/o>\n";

        await Assert.ThrowsExactlyAsync<NQuadsParseException>(
            async () => await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ParseThrowsOnMalformedIri()
    {
        const string input = "http://example.org/s <http://example.org/p> <http://example.org/o> .\n";

        await Assert.ThrowsExactlyAsync<NQuadsParseException>(
            async () => await ParseAsync(input, TestContext.CancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ParseInternsRepeatedTerms()
    {
        const string rdfType = "<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>";
        string input = $"""
            <http://example.org/s1> {rdfType} <http://example.org/T> .
            <http://example.org/s2> {rdfType} <http://example.org/T> .

            """;

        using Utf8StringPool pool = new();
        List<Quad> quads = await ParseAsync(input, pool, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, quads);
        Assert.AreEqual(quads[0].Predicate.Iri, quads[1].Predicate.Iri);
    }

    private static async Task<List<Quad>> ParseAsync(string input, CancellationToken cancellationToken)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(input));
        List<Quad> results = [];

        await foreach(Quad quad in NQuadsReader.ReadAsync(PipeReader.Create(stream), cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            results.Add(quad);
        }

        return results;
    }

    private static async Task<List<Quad>> ParseAsync(string input, Utf8StringPool pool, CancellationToken cancellationToken)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(input));
        List<Quad> results = [];

        await foreach(Quad quad in NQuadsReader.ReadAsync(PipeReader.Create(stream), pool, cancellationToken).ConfigureAwait(false))
        {
            results.Add(quad);
        }

        return results;
    }
}