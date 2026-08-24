using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.ParserTests.LinkedData;

[TestClass]
internal sealed class ContextStructureExtractionTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    public async Task AdjacencyReturnsEmptyForTermWithNoScopedContext()
    {
        LinkedDataTermSource leaf = new("k") { Iri = "http://example.org/leaf" };

        List<LinkedDataTermSource> neighbours = [];
        await foreach(LinkedDataTermSource n in ContextStructureExtraction.ScopedTermAdjacencyAsync(
            leaf, TestContext.CancellationToken).ConfigureAwait(false))
        {
            neighbours.Add(n);
        }
        Assert.IsEmpty(neighbours);
    }

    [TestMethod]
    public async Task AdjacencyYieldsTermsFromSingleScopedContext()
    {
        LinkedDataTermSource inner1 = new("inner1") { Iri = "http://example.org/i1" };
        LinkedDataTermSource inner2 = new("inner2") { Iri = "http://example.org/i2" };
        LinkedDataContextEntry scopedEntry = new(
            terms: new Dictionary<string, LinkedDataTermSource>
            {
                ["inner1"] = inner1,
                ["inner2"] = inner2
            },
            baseUrl: null,
            syntheticKey: "scoped-1");
        LinkedDataTermSource outer = new("outer")
        {
            Iri = "http://example.org/outer",
            ScopedContext = [scopedEntry]
        };

        List<LinkedDataTermSource> neighbours = [];
        await foreach(LinkedDataTermSource n in ContextStructureExtraction.ScopedTermAdjacencyAsync(
            outer, TestContext.CancellationToken).ConfigureAwait(false))
        {
            neighbours.Add(n);
        }

        Assert.HasCount(2, neighbours);
        Assert.Contains(inner1, neighbours);
        Assert.Contains(inner2, neighbours);
    }

    [TestMethod]
    public async Task AdjacencyYieldsTermsFromMultipleScopedContexts()
    {
        LinkedDataTermSource a = new("a");
        LinkedDataTermSource b = new("b");
        LinkedDataContextEntry e1 = new(
            terms: new Dictionary<string, LinkedDataTermSource> { ["a"] = a },
            baseUrl: null,
            syntheticKey: "s1");
        LinkedDataContextEntry e2 = new(
            terms: new Dictionary<string, LinkedDataTermSource> { ["b"] = b },
            baseUrl: null,
            syntheticKey: "s2");
        LinkedDataTermSource outer = new("outer") { ScopedContext = [e1, e2] };

        List<LinkedDataTermSource> neighbours = [];
        await foreach(LinkedDataTermSource n in ContextStructureExtraction.ScopedTermAdjacencyAsync(
            outer, TestContext.CancellationToken).ConfigureAwait(false))
        {
            neighbours.Add(n);
        }
        Assert.HasCount(2, neighbours);
    }

    [TestMethod]
    public async Task AdjacencySkipsRemoteUrlAndResetEntries()
    {
        LinkedDataTermSource a = new("a");
        LinkedDataContextEntry urlEntry = new("http://example.org/ctx", baseUrl: null, syntheticKey: "url-1");
        LinkedDataContextEntry resetEntry = new("reset-1");
        LinkedDataContextEntry inlineEntry = new(
            terms: new Dictionary<string, LinkedDataTermSource> { ["a"] = a },
            baseUrl: null,
            syntheticKey: "inline-1");
        LinkedDataTermSource outer = new("outer") { ScopedContext = [urlEntry, resetEntry, inlineEntry] };

        List<LinkedDataTermSource> neighbours = [];
        await foreach(LinkedDataTermSource n in ContextStructureExtraction.ScopedTermAdjacencyAsync(
            outer, TestContext.CancellationToken).ConfigureAwait(false))
        {
            neighbours.Add(n);
        }

        //Only the inline entry yields a neighbour.
        Assert.HasCount(1, neighbours);
        Assert.AreSame(a, neighbours[0]);
    }

    [TestMethod]
    public async Task AdjacencySurfacesCancellation()
    {
        LinkedDataTermSource a = new("a");
        LinkedDataContextEntry e = new(
            terms: new Dictionary<string, LinkedDataTermSource> { ["a"] = a },
            baseUrl: null,
            syntheticKey: "s");
        LinkedDataTermSource outer = new("outer") { ScopedContext = [e] };

        using CancellationTokenSource cts = new();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAsync<System.OperationCanceledException>(async () =>
        {
            await foreach(LinkedDataTermSource _ in ContextStructureExtraction.ScopedTermAdjacencyAsync(
                outer, cts.Token).ConfigureAwait(false))
            {
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public void KeyOfReturnsSyntheticKey()
    {
        LinkedDataTermSource term = new("the-key");
        Assert.AreEqual("the-key", ContextStructureExtraction.ScopedTermKey(term));
    }
}
