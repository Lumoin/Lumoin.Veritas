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
internal sealed class NQuadsIriSchemeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RejectsRelativeIriWithoutScheme()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadSubjectIriAsync("<//example/missing-scheme>", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsIriWithDigitFirstChar()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadSubjectIriAsync("<123://example>", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RejectsIriWithSpaceInScheme()
    {
        await Assert.ThrowsAsync<NQuadsParseException>(
            () => ReadSubjectIriAsync("<ht tp://example>", TestContext.CancellationToken)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AcceptsValidHttpScheme()
    {
        string iri = await ReadSubjectIriAsync("<http://example.org/foo>", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("http://example.org/foo", iri);
    }

    [TestMethod]
    public async Task AcceptsValidUrnScheme()
    {
        string iri = await ReadSubjectIriAsync("<urn:isbn:1234567890>", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("urn:isbn:1234567890", iri);
    }

    [TestMethod]
    public async Task AcceptsValidExoticScheme()
    {
        string iri = await ReadSubjectIriAsync("<did:example:abc>", TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("did:example:abc", iri);
    }

    private static async Task<string> ReadSubjectIriAsync(string subjectText, System.Threading.CancellationToken cancellationToken)
    {
        string line = $"{subjectText} <http://example/p> <http://example/o> .";
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(line));

        List<Quad> quads = [];
        await foreach(Quad quad in NQuadsReader.ReadAsync(PipeReader.Create(stream), pool: null, cancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return ((NamedNode)quads[0].Subject).Iri.ToString();
    }
}
