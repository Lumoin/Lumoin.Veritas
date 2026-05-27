using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.Tests.Pipelines;

/// <summary>
/// Verifies the pipe readers handle tokens and lines that straddle buffer-segment
/// boundaries. Each input is fed through a <see cref="PipeReader"/> configured with a
/// tiny 16-byte buffer, so every IRI, literal, and line longer than 16 bytes spans
/// multiple <see cref="System.Buffers.ReadOnlySequence{T}"/> segments and exercises the
/// multi-segment materialisation paths.
/// </summary>
[TestClass]
internal sealed class PipeBoundaryParseTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NQuadsParsesLineSpanningSegmentBoundaries()
    {
        const string Input = "<http://example.org/subject> <http://example.org/predicate> <http://example.org/object> .\n";

        List<Quad> quads = await ReadNQuadsAsync(Input).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("http://example.org/subject", ((NamedNode)quads[0].Subject).Iri.ToString());
        Assert.AreEqual("http://example.org/predicate", quads[0].Predicate.Iri.ToString());
        Assert.AreEqual("http://example.org/object", ((NamedNode)quads[0].Object).Iri.ToString());
    }

    [TestMethod]
    public async Task NQuadsParsesLiteralValueSpanningSegmentBoundaries()
    {
        const string Input = "<http://example.org/s> <http://example.org/p> \"a long literal value that crosses several boundaries\" .\n";

        List<Quad> quads = await ReadNQuadsAsync(Input).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("a long literal value that crosses several boundaries", ((Literal)quads[0].Object).Value.ToString());
    }

    [TestMethod]
    public async Task NQuadsParsesBlankNodeLabelSpanningSegmentBoundaries()
    {
        const string Input = "_:averylongblanknodelabelidentifier <http://example.org/p> <http://example.org/o> .\n";

        List<Quad> quads = await ReadNQuadsAsync(Input).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("averylongblanknodelabelidentifier", ((BlankNode)quads[0].Subject).Label.ToString());
    }

    [TestMethod]
    public async Task NQuadsParsesMultipleLinesAcrossBoundaries()
    {
        const string Input =
            "<http://example.org/s1> <http://example.org/p> <http://example.org/o1> .\n" +
            "<http://example.org/s2> <http://example.org/p> <http://example.org/o2> .\n" +
            "<http://example.org/s3> <http://example.org/p> <http://example.org/o3> .\n";

        List<Quad> quads = await ReadNQuadsAsync(Input).ConfigureAwait(false);

        Assert.HasCount(3, quads);
        Assert.AreEqual("http://example.org/s1", ((NamedNode)quads[0].Subject).Iri.ToString());
        Assert.AreEqual("http://example.org/s3", ((NamedNode)quads[2].Subject).Iri.ToString());
    }

    [TestMethod]
    public async Task NQuadsParsesFinalLineWithoutTrailingNewline()
    {
        const string Input = "<http://example.org/subject> <http://example.org/predicate> <http://example.org/object> .";

        List<Quad> quads = await ReadNQuadsAsync(Input).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("http://example.org/object", ((NamedNode)quads[0].Object).Iri.ToString());
    }

    [TestMethod]
    public async Task TurtleParsesDocumentSpanningSegmentBoundaries()
    {
        const string Input = "@prefix ex: <http://example.org/> .\nex:s ex:p ex:o .\n";

        List<Quad> quads = await ReadTurtleAsync(Input, TurtleSyntax.Turtle).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("http://example.org/s", ((NamedNode)quads[0].Subject).Iri.ToString());
        Assert.AreEqual("http://example.org/o", ((NamedNode)quads[0].Object).Iri.ToString());
    }

    [TestMethod]
    public async Task TurtleParsesLongStringLiteralSpanningSegmentBoundaries()
    {
        const string Input = "@prefix ex: <http://example.org/> .\nex:s ex:p \"\"\"a very long literal value spanning many buffer segments here\"\"\" .\n";

        List<Quad> quads = await ReadTurtleAsync(Input, TurtleSyntax.Turtle).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("a very long literal value spanning many buffer segments here", ((Literal)quads[0].Object).Value.ToString());
    }

    [TestMethod]
    public async Task TrigParsesNamedGraphSpanningSegmentBoundaries()
    {
        const string Input = "@prefix ex: <http://example.org/> .\nex:g {\nex:s ex:p ex:o .\n}\n";

        List<Quad> quads = await ReadTurtleAsync(Input, TurtleSyntax.TriG).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.AreEqual("http://example.org/g", ((NamedNode)quads[0].Graph!).Iri.ToString());
    }

    private static PipeReader Fragmented(string text)
    {
        //A 16-byte buffer forces any token or line longer than 16 bytes to span segments.
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        return PipeReader.Create(new MemoryStream(bytes), new StreamPipeReaderOptions(bufferSize: 16, minimumReadSize: 1));
    }

    private async Task<List<Quad>> ReadNQuadsAsync(string text)
    {
        List<Quad> quads = [];
        await foreach(Quad quad in NQuadsReader.ReadAsync(Fragmented(text), pool: null, TestContext.CancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }

    private async Task<List<Quad>> ReadTurtleAsync(string text, TurtleSyntax syntax)
    {
        List<Quad> quads = [];
        DiagnosticBag diagnostics = new();
        await foreach(Quad quad in TurtleReader.ReadAsync(
            Fragmented(text),
            syntax,
            diagnostics,
            pool: null,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return quads;
    }
}
