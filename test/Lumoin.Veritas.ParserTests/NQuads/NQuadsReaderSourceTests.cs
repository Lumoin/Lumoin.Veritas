using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.NQuads;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class NQuadsReaderSourceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ReadWithSourceAsyncProducesEmittedQuadsWithSourceRef()
    {
        const string nquads = """
            <http://example.org/s> <http://example.org/p> <http://example.org/o> .
            """;

        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool = new();

        List<EmittedQuad> result = [];
        await foreach(EmittedQuad emitted in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(nquads)),
            documentId, pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            result.Add(emitted);
        }

        Assert.HasCount(1, result);
        Assert.IsNotNull(result[0].Source);
    }

    [TestMethod]
    public async Task ReadWithSourceAsyncAssignsSequentialIndexesStartingAtZero()
    {
        const string nquads = """
            <http://example.org/s1> <http://example.org/p> <http://example.org/o1> .
            <http://example.org/s2> <http://example.org/p> <http://example.org/o2> .
            <http://example.org/s3> <http://example.org/p> <http://example.org/o3> .
            """;

        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool = new();

        List<EmittedQuad> result = [];
        await foreach(EmittedQuad emitted in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(nquads)),
            documentId, pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            result.Add(emitted);
        }

        Assert.HasCount(3, result);
        Assert.AreEqual(0, result[0].Source!.Value.Index);
        Assert.AreEqual(1, result[1].Source!.Value.Index);
        Assert.AreEqual(2, result[2].Source!.Value.Index);
    }

    [TestMethod]
    public async Task ReadWithSourceAsyncCarriesDocumentIdOnEverySource()
    {
        const string nquads = """
            <http://example.org/s1> <http://example.org/p> <http://example.org/o1> .
            <http://example.org/s2> <http://example.org/p> <http://example.org/o2> .
            """;

        DocumentId documentId = new(0xDEADBEEF);
        using Utf8StringPool pool = new();

        await foreach(EmittedQuad emitted in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(nquads)),
            documentId, pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            Assert.AreEqual(documentId, emitted.Source!.Value.DocumentId);
        }
    }

    [TestMethod]
    public async Task ReadWithSourceAsyncDoesNotConsumeIndexesForCommentsOrBlankLines()
    {
        const string nquads = """
            # a comment

            <http://example.org/s1> <http://example.org/p> <http://example.org/o1> .
            # another comment
            <http://example.org/s2> <http://example.org/p> <http://example.org/o2> .
            """;

        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool = new();

        List<EmittedQuad> result = [];
        await foreach(EmittedQuad emitted in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(nquads)),
            documentId, pool, TestContext.CancellationToken).ConfigureAwait(false))
        {
            result.Add(emitted);
        }

        Assert.HasCount(2, result);
        Assert.AreEqual(0, result[0].Source!.Value.Index);
        Assert.AreEqual(1, result[1].Source!.Value.Index);
    }

    [TestMethod]
    public async Task ReadWithSourceAsyncProducesSameQuadsAsReadAsync()
    {
        const string nquads = """
            <http://example.org/s1> <http://example.org/p> "literal value"@en .
            _:b1 <http://example.org/p> <http://example.org/o> .
            """;

        DocumentId documentId = new(0xABCD);
        using Utf8StringPool pool1 = new();
        using Utf8StringPool pool2 = new();

        List<Quad> bare = [];
        await foreach(Quad q in NQuadsReader.ReadAsync(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(nquads)),
            pool1, TestContext.CancellationToken).ConfigureAwait(false))
        {
            bare.Add(q);
        }

        List<EmittedQuad> sourced = [];
        await foreach(EmittedQuad e in NQuadsReader.ReadWithSourceAsync(
            new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(nquads)),
            documentId, pool2, TestContext.CancellationToken).ConfigureAwait(false))
        {
            sourced.Add(e);
        }

        Assert.HasCount(bare.Count, sourced);
        for(int i = 0; i < bare.Count; i++)
        {
            Assert.AreEqual(bare[i], sourced[i].Quad);
        }
    }
}
