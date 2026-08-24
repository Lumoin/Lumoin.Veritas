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
internal sealed class NQuadsLanguageTagTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AcceptsSimpleLanguageTag()
    {
        Literal literal = await ReadLiteralAsync("\"chat\"@en", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("en", literal.Language!.Value.ToString());
        Assert.IsNull(literal.BaseDirection);
    }

    [TestMethod]
    public async Task AcceptsLanguageTagWithSubtag()
    {
        Literal literal = await ReadLiteralAsync("\"chat\"@en-GB", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("en-GB", literal.Language!.Value.ToString());
    }

    [TestMethod]
    public async Task AcceptsDirectionTaggedLanguage()
    {
        Literal literal = await ReadLiteralAsync("\"chat\"@en--ltr", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("en", literal.Language!.Value.ToString());
        Assert.AreEqual(TextDirection.Ltr, literal.BaseDirection);
    }

    [TestMethod]
    public async Task AcceptsRtlDirection()
    {
        Literal literal = await ReadLiteralAsync("\"shalom\"@he--rtl", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(TextDirection.Rtl, literal.BaseDirection);
    }

    [TestMethod]
    public async Task RejectsSubtagLongerThanEightCharacters()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Hello\"@cantbethislong", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsUppercaseDirection()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Hello\"@en--LTR", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsUndefinedDirection()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Hello\"@en--up", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsEmptyLanguageTag()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Hello\"@", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsExplicitLangStringDatatype()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Hello\"^^<http://www.w3.org/1999/02/22-rdf-syntax-ns#langString>", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsExplicitDirLangStringDatatype()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadLiteralAsync("\"Hello\"^^<http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString>", TestContext.CancellationToken)).ConfigureAwait(false);
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
