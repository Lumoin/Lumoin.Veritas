using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class TurtleRoundTripTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RoundTripSingleIriTriple()
    {
        Quad[] input =
        [
            new(
                new NamedNode(Utf8Strings.From("http://example.org/s")),
                new NamedNode(Utf8Strings.From("http://example.org/p")),
                new NamedNode(Utf8Strings.From("http://example.org/o")))
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.Turtle, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    [TestMethod]
    public async Task RoundTripPlainStringLiteral()
    {
        Quad[] input =
        [
            new(
                new NamedNode(Utf8Strings.From("http://example.org/s")),
                new NamedNode(Utf8Strings.From("http://example.org/p")),
                new Literal(
                    Utf8Strings.From("hello"),
                    new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"))))
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.Turtle, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    [TestMethod]
    public async Task RoundTripLanguageTaggedLiteral()
    {
        Quad[] input =
        [
            new(
                new NamedNode(Utf8Strings.From("http://example.org/s")),
                new NamedNode(Utf8Strings.From("http://example.org/p")),
                new Literal(
                    Utf8Strings.From("Hallo"),
                    new NamedNode(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")),
                    Utf8Strings.From("de")))
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.Turtle, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    [TestMethod]
    public async Task RoundTripDirectionTaggedLiteral()
    {
        Quad[] input =
        [
            new(
                new NamedNode(Utf8Strings.From("http://example.org/s")),
                new NamedNode(Utf8Strings.From("http://example.org/p")),
                new Literal(
                    Utf8Strings.From("alef"),
                    new NamedNode(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString")),
                    Utf8Strings.From("he"),
                    TextDirection.Rtl))
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.Turtle, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    [TestMethod]
    public async Task RoundTripIntegerLiteral()
    {
        Quad[] input =
        [
            new(
                new NamedNode(Utf8Strings.From("http://example.org/s")),
                new NamedNode(Utf8Strings.From("http://example.org/p")),
                new Literal(
                    Utf8Strings.From("42"),
                    new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"))))
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.Turtle, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    [TestMethod]
    public async Task RoundTripTrigNamedGraph()
    {
        Quad[] input =
        [
            new(
                new NamedNode(Utf8Strings.From("http://example.org/s")),
                new NamedNode(Utf8Strings.From("http://example.org/p")),
                new NamedNode(Utf8Strings.From("http://example.org/o")),
                new NamedNode(Utf8Strings.From("http://example.org/g")))
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.TriG, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    [TestMethod]
    public async Task RoundTripMultipleSubjects()
    {
        Quad[] input =
        [
            MakeIriQuad("s1", "p", "o1"),
            MakeIriQuad("s1", "p", "o2"),
            MakeIriQuad("s2", "p", "o3")
        ];

        List<Quad> output = await RoundTripAsync(input, TurtleSyntax.Turtle, TestContext.CancellationToken).ConfigureAwait(false);

        AssertQuadSetsEqual(input, output);
    }

    private static Quad MakeIriQuad(string s, string p, string o)
    {
        return new Quad(
            new NamedNode(Utf8Strings.From("http://example.org/" + s)),
            new NamedNode(Utf8Strings.From("http://example.org/" + p)),
            new NamedNode(Utf8Strings.From("http://example.org/" + o)));
    }

    private static async Task<List<Quad>> RoundTripAsync(Quad[] input, TurtleSyntax syntax, System.Threading.CancellationToken cancellationToken)
    {
        using MemoryStream encoded = new();
        TurtleWriter.Write(input, PipeWriter.Create(encoded, new StreamPipeWriterOptions(leaveOpen: true)), syntax);
        encoded.Position = 0;

        List<Quad> result = [];
        DiagnosticBag diagnostics = new();
        await foreach(Quad quad in TurtleReader.ReadAsync(
            PipeReader.Create(encoded, new StreamPipeReaderOptions(leaveOpen: true)),
            syntax,
            diagnostics,
            pool: null,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            result.Add(quad);
        }

        Assert.IsFalse(diagnostics.HasErrors, "Round-tripped Turtle should re-parse without diagnostics.");

        return result;
    }

    private static void AssertQuadSetsEqual(Quad[] expected, List<Quad> actual)
    {
        Assert.HasCount(expected.Length, actual, "Quad count differs.");

        HashSet<string> expectedKeys = [];
        foreach(Quad q in expected)
        {
            expectedKeys.Add(QuadKey(q));
        }

        HashSet<string> actualKeys = [];
        foreach(Quad q in actual)
        {
            actualKeys.Add(QuadKey(q));
        }

        Assert.IsTrue(expectedKeys.SetEquals(actualKeys),
            $"Quad sets differ.\nExpected: {string.Join("\n", expectedKeys)}\nActual: {string.Join("\n", actualKeys)}");
    }

    private static string QuadKey(Quad q)
    {
        return $"{TermKey(q.Subject)}|{q.Predicate.Iri}|{TermKey(q.Object)}|{(q.Graph is null ? "DG" : TermKey(q.Graph))}";
    }

    private static string TermKey(RdfTerm term)
    {
        return term switch
        {
            NamedNode n => "i:" + n.Iri,
            BlankNode b => "b:" + b.Label,
            Literal l => "l:" + l.Value + ":" + (l.Language?.ToString() ?? string.Empty) + ":" + (l.BaseDirection?.ToString() ?? string.Empty) + ":" + l.Datatype.Iri,
            TripleTerm tt => "t:" + TermKey(tt.Subject) + "|" + tt.Predicate.Iri + "|" + TermKey(tt.Object),
            _ => "x:" + term
        };
    }
}
