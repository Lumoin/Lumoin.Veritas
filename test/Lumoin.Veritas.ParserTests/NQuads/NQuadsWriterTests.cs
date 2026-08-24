using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.NQuads;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Pipelines;
using System.Text;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class NQuadsWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task WriteTripleProducesCorrectLine()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/subject")),
            new NamedNode(pool.Intern("http://example.org/predicate")),
            new NamedNode(pool.Intern("http://example.org/object")));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(
            "<http://example.org/subject> <http://example.org/predicate> <http://example.org/object> .\n",
            output);
    }

    [TestMethod]
    public async Task WriteQuadWithGraphProducesCorrectLine()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new NamedNode(pool.Intern("http://example.org/o")),
            new NamedNode(pool.Intern("http://example.org/g")));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(
            "<http://example.org/s> <http://example.org/p> <http://example.org/o> <http://example.org/g> .\n",
            output);
    }

    [TestMethod]
    public async Task WriteBlankNodeSubjectProducesCorrectLine()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new BlankNode(pool.Intern("b0")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new NamedNode(pool.Intern("http://example.org/o")));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(
            "_:b0 <http://example.org/p> <http://example.org/o> .\n",
            output);
    }

    [TestMethod]
    public async Task WriteStringLiteralWithXsdDatatypeProducesCorrectLine()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("hello"),
                new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(
            "<http://example.org/s> <http://example.org/p> \"hello\"^^<http://www.w3.org/2001/XMLSchema#string> .\n",
            output);
    }

    [TestMethod]
    public async Task WriteLanguageTaggedLiteralProducesCorrectLine()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("Bonjour"),
                new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")),
                pool.Intern("fr")));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(
            "<http://example.org/s> <http://example.org/p> \"Bonjour\"@fr .\n",
            output);
    }

    [TestMethod]
    public async Task WriteMultipleQuadsProducesMultipleLines()
    {
        using Utf8StringPool pool = new();
        Quad[] quads =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s1")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o1"))),
            new(
                new NamedNode(pool.Intern("http://example.org/s2")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o2")))
        ];

        string output = await SerializeAsync(quads, TestContext.CancellationToken).ConfigureAwait(false);

        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, lines);
    }

    [TestMethod]
    public async Task WriteEmptySequenceProducesEmptyOutput()
    {
        string output = await SerializeAsync([], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(string.Empty, output);
    }

    [TestMethod]
    public async Task WriteLiteralEscapesNewline()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("line1\nline2"),
                new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.Contains("\\n", output, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task WriteLiteralEscapesQuote()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("say \"hello\""),
                new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.Contains("\\\"", output, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task WriteLiteralEscapesBackslash()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("path\\to\\file"),
                new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.Contains("\\\\", output, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task DeeplyNestedTripleTermThrowsDepthLimitRatherThanOverflowing()
    {
        using Utf8StringPool pool = new();

        //A nesting depth the old recursive writer would have overflowed the call stack on; the iterative
        //writer walks it over an explicit stack and caps it with a catchable exception instead.
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            NestTripleTerms(pool, 2000));

        TripleTermDepthLimitException exception = await Assert.ThrowsExactlyAsync<TripleTermDepthLimitException>(
            async () => await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    [TestMethod]
    public async Task TripleTermNestedAtTheLimitSerializes()
    {
        using Utf8StringPool pool = new();

        //Nesting exactly at the limit is valid and serializes to completion: one quoted-triple open per level.
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            NestTripleTerms(pool, QuotedTripleLimits.MaxNestingDepth));

        string output = await SerializeAsync([quad], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, output.Split("<<( ").Length - 1);
    }

    /// <summary>Builds an RDF-star term nested <paramref name="depth"/> quoted triples deep through the subject.</summary>
    /// <param name="pool">The pool that interns the term IRIs.</param>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term, or a plain named node when <paramref name="depth"/> is zero.</returns>
    private static RdfTerm NestTripleTerms(Utf8StringPool pool, int depth)
    {
        NamedNode predicate = new(pool.Intern("http://example.org/p"));
        NamedNode leaf = new(pool.Intern("http://example.org/o"));

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }

    private static async Task<string> SerializeAsync(IEnumerable<Quad> quads, CancellationToken cancellationToken)
    {
        using MemoryStream stream = new();
        await NQuadsWriter.WriteAsync(
            quads,
            PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)),
            cancellationToken).ConfigureAwait(false);

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
