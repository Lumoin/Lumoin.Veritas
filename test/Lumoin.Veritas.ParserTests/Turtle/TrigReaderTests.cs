using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Reader tests for RDF 1.2 TriG (<see cref="TurtleSyntax.TriG"/>): named-graph blocks
/// (<c>GRAPH g { … }</c>, <c>g { … }</c>) carry the block's graph on each emitted quad, default-graph triples
/// outside any block carry no graph, and TriG-only graph blocks are rejected in <see cref="TurtleSyntax.Turtle"/> mode.
/// </summary>
[TestClass]
internal sealed class TrigReaderTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Reads a document into quads under the given syntax.</summary>
    /// <param name="text">The document text.</param>
    /// <param name="syntax">The syntax mode.</param>
    /// <param name="diagnostics">The diagnostic bag the read records into.</param>
    /// <returns>The emitted quads.</returns>
    private async Task<List<Quad>> ReadAsync(string text, TurtleSyntax syntax, DiagnosticBag diagnostics)
    {
        List<Quad> quads = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(Encoding.UTF8.GetBytes(text), syntax, diagnostics, pool: null, baseIri: "http://example.org/", cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }

    [TestMethod]
    public async Task TrigReadsNamedGraphBlock()
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = await ReadAsync(
            "@prefix : <http://example.org/> . :s :p :o . GRAPH :g { :a :b :c } :g2 { :d :e :f }",
            TurtleSyntax.TriG,
            diagnostics).ConfigureAwait(false);

        Assert.IsFalse(diagnostics.HasErrors);
        Assert.HasCount(3, quads);

        Quad defaultQuad = quads.Find(q => q.Graph is null)!;
        Assert.IsNotNull(defaultQuad);

        List<Quad> named = quads.FindAll(q => q.Graph is not null);
        Assert.HasCount(2, named);
        Assert.IsInstanceOfType<NamedNode>(named[0].Graph);
    }

    [TestMethod]
    public async Task TrigKeepsGraphIdentityPerBlock()
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = await ReadAsync(
            "@prefix : <http://example.org/> . GRAPH :g1 { :a :b :c } GRAPH :g2 { :a :b :c }",
            TurtleSyntax.TriG,
            diagnostics).ConfigureAwait(false);

        Assert.IsFalse(diagnostics.HasErrors);
        Assert.HasCount(2, quads);
        Assert.AreNotEqual(quads[0].Graph, quads[1].Graph, "the two triples belong to distinct named graphs");
    }

    [TestMethod]
    public async Task TurtleModeRejectsGraphBlock()
    {
        DiagnosticBag diagnostics = new();
        _ = await ReadAsync(
            "@prefix : <http://example.org/> . GRAPH :g { :a :b :c }",
            TurtleSyntax.Turtle,
            diagnostics).ConfigureAwait(false);

        Assert.IsTrue(diagnostics.HasErrors, "a GRAPH block is not valid Turtle");
    }
}
