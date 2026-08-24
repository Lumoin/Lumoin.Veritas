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
internal sealed class NQuadsWhitespaceToleranceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ParsesLiteralWithWhitespaceBeforeLanguageTag()
    {
        Literal literal = await ReadLiteralAsync("\"Alice\" @en", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("Alice", literal.Value.ToString());
        Assert.AreEqual("en", literal.Language!.Value.ToString());
    }

    [TestMethod]
    public async Task ParsesLiteralWithWhitespaceBeforeDatatypeMarker()
    {
        Literal literal = await ReadLiteralAsync("\"2\"  ^^  <http://www.w3.org/2001/XMLSchema#integer>", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("2", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", literal.Datatype.Iri.ToString());
    }

    [TestMethod]
    public async Task ParsesLiteralWithWhitespaceAroundDatatypeMarker()
    {
        Literal literal = await ReadLiteralAsync("\"x\" ^^ <http://example/dt>", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("http://example/dt", literal.Datatype.Iri.ToString());
    }

    [TestMethod]
    public async Task RejectsWhitespaceWithinLanguageTag()
    {
        //White space is permitted before the tag, not after the '@' inside it.
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Alice\"@ en", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    private static async Task<Literal> ReadLiteralAsync(string objectText, System.Threading.CancellationToken cancellationToken)
    {
        string line = $"<http://example/s> <http://example/p> {objectText} .";
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(line));

        List<Quad> quads = [];
        await foreach(Quad quad in NQuadsReader.ReadAsync(PipeReader.Create(stream), pool: null, cancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return (Literal)quads[0].Object;
    }
}
