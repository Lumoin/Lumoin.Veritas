using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.NQuads;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class NQuadsTripleTermTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ParsesObjectTripleTerm()
    {
        const string Input = "<http://example/a> <http://example/reifies> <<( <http://example/s> <http://example/p> <http://example/o> )>> .";

        List<Quad> quads = await ReadAllAsync(Input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        TripleTerm tripleTerm = (TripleTerm)quads[0].Object;
        Assert.AreEqual("http://example/s", ((NamedNode)tripleTerm.Subject).Iri.ToString());
        Assert.AreEqual("http://example/p", tripleTerm.Predicate.Iri.ToString());
        Assert.AreEqual("http://example/o", ((NamedNode)tripleTerm.Object).Iri.ToString());
    }

    [TestMethod]
    public async Task ParsesObjectTripleTermWithoutWhitespace()
    {
        const string Input = "<http://example/s><http://example/reifies><<(<http://example/s2><http://example/p2><http://example/o2>)>>.";

        List<Quad> quads = await ReadAllAsync(Input, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<TripleTerm>(quads[0].Object);
    }

    [TestMethod]
    public async Task ParsesNestedTripleTerm()
    {
        const string Input = "<http://example/s> <http://example/reifies> <<( <http://example/s2> <http://example/q2> <<( <http://example/s3> <http://example/p3> <http://example/o3> )>> )>> .";

        List<Quad> quads = await ReadAllAsync(Input, TestContext.CancellationToken).ConfigureAwait(false);

        TripleTerm outer = (TripleTerm)quads[0].Object;
        Assert.IsInstanceOfType<TripleTerm>(outer.Object);
    }

    [TestMethod]
    public async Task ParsesTripleTermWithBlankNodeObjectAdjacentToClose()
    {
        const string Input = "<http://example/s> <http://example/p> <<( <http://example/s1> <http://example/p1> _:o1 )>> .";

        List<Quad> quads = await ReadAllAsync(Input, TestContext.CancellationToken).ConfigureAwait(false);

        TripleTerm tripleTerm = (TripleTerm)quads[0].Object;
        Assert.IsInstanceOfType<BlankNode>(tripleTerm.Object);
    }

    [TestMethod]
    public async Task RejectsReifiedTripleSyntax()
    {
        const string Input = "<http://example/s> <http://example/p> << <http://example/a> <http://example/b> <http://example/c> >> .";

        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadAllAsync(Input, TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsUnterminatedTripleTerm()
    {
        const string Input = "<http://example/s> <http://example/p> <<( <http://example/a> <http://example/b> <http://example/c> .";

        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadAllAsync(Input, TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsTripleTermInSubjectPosition()
    {
        const string Input = "<<( <http://example/s> <http://example/p> <http://example/o> )>> <http://example/q> <http://example/z> .";

        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadAllAsync(Input, TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RoundTripsObjectTripleTerm()
    {
        using Utf8StringPool pool = new();
        TripleTerm tripleTerm = new(
            new NamedNode(pool.Intern("http://example/s")),
            new NamedNode(pool.Intern("http://example/p")),
            new NamedNode(pool.Intern("http://example/o")));
        Quad original = new(
            new NamedNode(pool.Intern("http://example/a")),
            new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies")),
            tripleTerm);

        using MemoryStream stream = new();
        await NQuadsWriter.WriteAsync(
            [original],
            PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)),
            TestContext.CancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        List<Quad> readBack = await ReadAllFromStreamAsync(stream, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, readBack);
        TripleTerm roundTripped = (TripleTerm)readBack[0].Object;
        Assert.AreEqual("http://example/s", ((NamedNode)roundTripped.Subject).Iri.ToString());
        Assert.AreEqual("http://example/o", ((NamedNode)roundTripped.Object).Iri.ToString());
    }

    private static async Task<List<Quad>> ReadAllAsync(string input, System.Threading.CancellationToken cancellationToken)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(input));
        return await ReadAllFromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<Quad>> ReadAllFromStreamAsync(Stream stream, System.Threading.CancellationToken cancellationToken)
    {
        List<Quad> result = [];
        await foreach(Quad quad in NQuadsReader.ReadAsync(PipeReader.Create(stream), pool: null, cancellationToken).ConfigureAwait(false))
        {
            result.Add(quad);
        }

        return result;
    }
}
