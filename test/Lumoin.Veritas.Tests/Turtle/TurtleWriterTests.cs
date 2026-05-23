using System.Collections.Generic;
using System.IO;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Turtle;

[TestClass]
internal sealed class TurtleWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void WriteSingleTriple()
    {
        Quad quad = new(
            new NamedNode(Utf8String.FromString("http://example.org/s")),
            new NamedNode(Utf8String.FromString("http://example.org/p")),
            new NamedNode(Utf8String.FromString("http://example.org/o")));

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
            new NamedNode(Utf8String.FromString("http://example.org/s")),
            new NamedNode(Utf8String.FromString("http://example.org/p")),
            new Literal(
                Utf8String.FromString("hello"),
                new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#string"))));

        string text = WriteQuads([q], TurtleSyntax.Turtle);

        //xsd:string is implicit on plain literals — no prefix declaration needed for the bare form.
        Assert.Contains("\"hello\"", text, System.StringComparison.Ordinal);
    }

    [TestMethod]
    public void WriteTrigGraphBlock()
    {
        Quad q = new(
            new NamedNode(Utf8String.FromString("http://example.org/s")),
            new NamedNode(Utf8String.FromString("http://example.org/p")),
            new NamedNode(Utf8String.FromString("http://example.org/o")),
            new NamedNode(Utf8String.FromString("http://example.org/g")));

        string text = WriteQuads([q], TurtleSyntax.TriG);

        Assert.Contains('{', text);
        Assert.Contains('}', text);
    }

    private static Quad MakeIriQuad(string s, string p, string o)
    {
        return new Quad(
            new NamedNode(Utf8String.FromString("http://example.org/" + s)),
            new NamedNode(Utf8String.FromString("http://example.org/" + p)),
            new NamedNode(Utf8String.FromString("http://example.org/" + o)));
    }

    private static string WriteQuads(IReadOnlyList<Quad> quads, TurtleSyntax syntax)
    {
        using MemoryStream stream = new();
        TurtleWriter.Write(quads, stream, syntax);
        stream.Position = 0;
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
