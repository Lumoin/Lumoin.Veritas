using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.NQuads;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Pipelines;
using System.Text;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class NQuadsRoundTripTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RoundTripNamedNodeTriple()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/subject")),
                new NamedNode(pool.Intern("http://example.org/predicate")),
                new NamedNode(pool.Intern("http://example.org/object")))
        ];

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, roundTripped);
        AssertQuadsEqual(original[0], roundTripped[0]);
    }

    [TestMethod]
    public async Task RoundTripBlankNodes()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new BlankNode(pool.Intern("b0")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new BlankNode(pool.Intern("b1")))
        ];

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, roundTripped);
        AssertQuadsEqual(original[0], roundTripped[0]);
    }

    [TestMethod]
    public async Task RoundTripStringLiteral()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new Literal(
                    pool.Intern("hello world"),
                    new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))))
        ];

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, roundTripped);
        AssertQuadsEqual(original[0], roundTripped[0]);
    }

    [TestMethod]
    public async Task RoundTripLanguageTaggedLiteral()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new Literal(
                    pool.Intern("Guten Tag"),
                    new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")),
                    pool.Intern("de")))
        ];

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, roundTripped);
        AssertQuadsEqual(original[0], roundTripped[0]);
    }

    [TestMethod]
    public async Task RoundTripLiteralWithEscapedCharacters()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new Literal(
                    pool.Intern("tab:\there\nnewline\r\"quoted\"\\backslash"),
                    new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))))
        ];

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, roundTripped);
        AssertQuadsEqual(original[0], roundTripped[0]);
    }

    [TestMethod]
    public async Task RoundTripNamedGraph()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o")),
                new NamedNode(pool.Intern("http://example.org/graph")))
        ];

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, roundTripped);
        AssertQuadsEqual(original[0], roundTripped[0]);
    }

    [TestMethod]
    public async Task RoundTripLargeSetPreservesCount()
    {
        using Utf8StringPool pool = new();
        Quad[] original = new Quad[100];
        for(int i = 0; i < original.Length; i++)
        {
            original[i] = new Quad(
                new NamedNode(pool.Intern($"http://example.org/s{i}")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern($"http://example.org/o{i}")));
        }

        List<Quad> roundTripped = await RoundTripAsync(original, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(100, roundTripped);
    }

    [TestMethod]
    public async Task RoundTripThroughSourceAwareOverloadPreservesQuads()
    {
        using Utf8StringPool pool = new();
        Quad[] original =
        [
            new(
                new NamedNode(pool.Intern("http://example.org/s1")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new NamedNode(pool.Intern("http://example.org/o1"))),
            new(
                new NamedNode(pool.Intern("http://example.org/s2")),
                new NamedNode(pool.Intern("http://example.org/p")),
                new Literal(
                    pool.Intern("hello"),
                    new NamedNode(pool.Intern("http://www.w3.org/2001/XMLSchema#string"))))
        ];

        using MemoryStream output = new();
        await NQuadsWriter.WriteAsync(
            original,
            PipeWriter.Create(output, new StreamPipeWriterOptions(leaveOpen: true)),
            TestContext.CancellationToken).ConfigureAwait(false);

        DocumentId documentId = new(0xCAFEBABE);
        using Utf8StringPool readPool = new();

        List<EmittedQuad> roundTripped = [];
        await foreach(EmittedQuad emitted in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(output.ToArray()),
            documentId, readPool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            roundTripped.Add(emitted);
        }

        Assert.HasCount(original.Length, roundTripped);
        for(int i = 0; i < original.Length; i++)
        {
            AssertQuadsEqual(original[i], roundTripped[i].Quad);
            Assert.AreEqual(documentId, roundTripped[i].Source!.Value.DocumentId);
            Assert.AreEqual(i, roundTripped[i].Source!.Value.Index);
        }
    }

    private static async Task<List<Quad>> RoundTripAsync(IEnumerable<Quad> quads, CancellationToken cancellationToken)
    {
        using MemoryStream stream = new();
        await NQuadsWriter.WriteAsync(
            quads,
            PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)),
            cancellationToken).ConfigureAwait(false);

        stream.Position = 0;
        List<Quad> results = [];

        await foreach(Quad quad in NQuadsReader.ReadAsync(
            PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true)),
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            results.Add(quad);
        }

        return results;
    }

    private static void AssertQuadsEqual(Quad expected, Quad actual)
    {
        AssertTermsEqual(expected.Subject, actual.Subject, "subject");
        Assert.AreEqual(expected.Predicate.Iri.ToString(), actual.Predicate.Iri.ToString(), "predicate IRI");
        AssertTermsEqual(expected.Object, actual.Object, "object");

        if(expected.Graph is null)
        {
            Assert.IsNull(actual.Graph, "graph should be null");
        }
        else
        {
            Assert.IsNotNull(actual.Graph, "graph should not be null");
            AssertTermsEqual(expected.Graph, actual.Graph, "graph");
        }
    }

    private static void AssertTermsEqual(RdfTerm expected, RdfTerm actual, string position)
    {
        switch(expected)
        {
            case NamedNode expectedNode:
            {
                Assert.IsInstanceOfType<NamedNode>(actual, $"{position} should be a named node");
                Assert.AreEqual(expectedNode.Iri.ToString(), ((NamedNode)actual).Iri.ToString(), $"{position} IRI");
                break;
            }
            case BlankNode expectedBlank:
            {
                Assert.IsInstanceOfType<BlankNode>(actual, $"{position} should be a blank node");
                Assert.AreEqual(expectedBlank.Label.ToString(), ((BlankNode)actual).Label.ToString(), $"{position} label");
                break;
            }
            case Literal expectedLiteral:
            {
                Assert.IsInstanceOfType<Literal>(actual, $"{position} should be a literal");
                Literal actualLiteral = (Literal)actual;
                Assert.AreEqual(expectedLiteral.Value.ToString(), actualLiteral.Value.ToString(), $"{position} value");
                Assert.AreEqual(expectedLiteral.Datatype.Iri.ToString(), actualLiteral.Datatype.Iri.ToString(), $"{position} datatype");
                Assert.AreEqual(expectedLiteral.Language?.ToString(), actualLiteral.Language?.ToString(), $"{position} language");
                break;
            }
            default:
            {
                Assert.Fail($"Unknown RDF term type at {position}: {expected.GetType().Name}");
                break;
            }
        }
    }
}
