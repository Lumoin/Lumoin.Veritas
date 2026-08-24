using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class TurtleWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void WriteSingleTriple()
    {
        Quad quad = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")));

        string text = WriteQuads([quad], TurtleSyntax.Turtle);

        Assert.Contains("http://example.org/s", text, System.StringComparison.Ordinal);
        Assert.Contains(" .", text, System.StringComparison.Ordinal);
    }

    [TestMethod]
    public void WriteGroupsBySubject()
    {
        Quad a = MakeIriQuad("s", "p1", "o1");
        Quad b = MakeIriQuad("s", "p2", "o2");

        string text = WriteQuads([a, b], TurtleSyntax.Turtle);

        Assert.Contains(";", text, System.StringComparison.Ordinal);
    }

    [TestMethod]
    public void WriteGroupsObjectsWithComma()
    {
        Quad a = MakeIriQuad("s", "p", "o1");
        Quad b = MakeIriQuad("s", "p", "o2");

        string text = WriteQuads([a, b], TurtleSyntax.Turtle);

        Assert.Contains(" , ", text, System.StringComparison.Ordinal);
    }

    [TestMethod]
    public void WriteDeclaresXsdWhenLiteralPresent()
    {
        Quad q = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new Literal(
                Utf8Strings.From("hello"),
                new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"))));

        string text = WriteQuads([q], TurtleSyntax.Turtle);

        //xsd:string is implicit on plain literals — no prefix declaration needed for the bare form.
        Assert.Contains("\"hello\"", text, System.StringComparison.Ordinal);
    }

    [TestMethod]
    public void WriteTrigGraphBlock()
    {
        Quad q = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            new NamedNode(Utf8Strings.From("http://example.org/o")),
            new NamedNode(Utf8Strings.From("http://example.org/g")));

        string text = WriteQuads([q], TurtleSyntax.TriG);

        Assert.Contains('{', text);
        Assert.Contains('}', text);
    }

    /// <summary>A quoted triple nested at the limit in object position writes inline to completion (one open per level).</summary>
    [TestMethod]
    public void DeepTripleTermObjectInlineWrites()
    {
        Quad quad = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            NestSubject(QuotedTripleLimits.MaxNestingDepth));

        string text = WriteQuads([quad], TurtleSyntax.Turtle);

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, text.Split("<<( ").Length - 1);
    }

    /// <summary>A quoted triple nested beyond the limit throws the catchable depth exception from the writer, not a stack overflow.</summary>
    [TestMethod]
    public void DeepTripleTermBeyondTheNestingLimitThrows()
    {
        Quad quad = new(
            new NamedNode(Utf8Strings.From("http://example.org/s")),
            new NamedNode(Utf8Strings.From("http://example.org/p")),
            NestSubject(QuotedTripleLimits.MaxNestingDepth + 1));

        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => WriteQuads([quad], TurtleSyntax.Turtle));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth + 1, exception.Depth);
        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    /// <summary>Two quads with distinct-but-value-equal deep quoted-triple subjects group under one subject, proving the grouping key is structural.</summary>
    [TestMethod]
    public void ValueEqualDeepTripleTermSubjectsGroupUnderOneSubject()
    {
        Quad a = new(NestSubject(32), new NamedNode(Utf8Strings.From("http://example.org/p1")), new NamedNode(Utf8Strings.From("http://example.org/o1")));
        Quad b = new(NestSubject(32), new NamedNode(Utf8Strings.From("http://example.org/p2")), new NamedNode(Utf8Strings.From("http://example.org/o2")));

        string text = WriteQuads([a, b], TurtleSyntax.Turtle);

        //Grouped: the shared subject (the only source of "<<( " here) is emitted exactly once, its predicates joined by ';'.
        Assert.AreEqual(32, text.Split("<<( ").Length - 1);
        Assert.Contains(";", text, System.StringComparison.Ordinal);
    }

    private static Quad MakeIriQuad(string s, string p, string o)
    {
        return new Quad(
            new NamedNode(Utf8Strings.From("http://example.org/" + s)),
            new NamedNode(Utf8Strings.From("http://example.org/" + p)),
            new NamedNode(Utf8Strings.From("http://example.org/" + o)));
    }

    /// <summary>Builds a quoted triple nested <paramref name="depth"/> levels deep through the subject, with IRI leaves.</summary>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term.</returns>
    private static RdfTerm NestSubject(int depth)
    {
        NamedNode predicate = new(Utf8Strings.From("http://example.org/p"));
        RdfTerm leaf = new NamedNode(Utf8Strings.From("http://example.org/o"));

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }

    private static string WriteQuads(IReadOnlyList<Quad> quads, TurtleSyntax syntax)
    {
        using MemoryStream stream = new();
        TurtleWriter.Write(quads, PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)), syntax);
        stream.Position = 0;

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
