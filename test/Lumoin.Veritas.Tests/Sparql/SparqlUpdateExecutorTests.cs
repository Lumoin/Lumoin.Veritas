using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// End-to-end tests for <see cref="SparqlUpdateExecutor"/>: ground <c>INSERT</c>/<c>DELETE DATA</c> and the
/// <c>DELETE</c>/<c>INSERT … WHERE</c> modify forms mutate a <see cref="MutableSparqlDataset"/> through real
/// edit-session commits, and a later operation sees the earlier one's effect.
/// </summary>
[TestClass]
internal sealed class SparqlUpdateExecutorTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Parses and normalizes an update request.</summary>
    /// <param name="text">The update text.</param>
    /// <param name="pool">The interning pool.</param>
    /// <returns>The normalized update request.</returns>
    private static SparqlUpdateRequest ParseAndNormalize(string text, Utf8StringPool pool)
    {
        ParseResult<SparqlRequest> result = SparqlParser.ParseRequest(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text)), pool);
        Assert.IsFalse(result.HasErrors, "the update request should parse without diagnostics");

        return (SparqlUpdateRequest)new SparqlNormalizer(pool).Normalize(result.Tree);
    }

    /// <summary>Builds an empty mutable dataset.</summary>
    /// <returns>The dataset.</returns>
    private static async Task<MutableSparqlDataset> EmptyDatasetAsync()
    {
        return await MutableSparqlDataset.CreateAsync(new TermDictionary(), []).ConfigureAwait(false);
    }

    /// <summary>Reads the dataset's current quads (default graph + named graphs).</summary>
    /// <param name="dataset">The dataset.</param>
    /// <returns>The quads.</returns>
    private static List<Quad> DumpQuads(MutableSparqlDataset dataset)
    {
        List<Quad> quads = [];
        foreach(EncodedTriple triple in dataset.DefaultGraph.Match(TermId.None, TermId.None, TermId.None))
        {
            quads.Add(Decode(dataset, triple, graph: null));
        }

        foreach(TermId graphId in dataset.NamedGraphNames)
        {
            dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? store);
            RdfTerm graph = dataset.Dictionary.Resolve(graphId);
            foreach(EncodedTriple triple in store!.Match(TermId.None, TermId.None, TermId.None))
            {
                quads.Add(Decode(dataset, triple, graph));
            }
        }

        return quads;
    }

    /// <summary>Decodes an encoded triple back to a quad in the given graph.</summary>
    /// <param name="dataset">The dataset whose dictionary decodes the terms.</param>
    /// <param name="triple">The encoded triple.</param>
    /// <param name="graph">The graph term, or <see langword="null"/> for the default graph.</param>
    /// <returns>The decoded quad.</returns>
    private static Quad Decode(MutableSparqlDataset dataset, EncodedTriple triple, RdfTerm? graph)
    {
        return new Quad(
            dataset.Dictionary.Resolve(triple.Subject),
            (NamedNode)dataset.Dictionary.Resolve(triple.Predicate),
            dataset.Dictionary.Resolve(triple.Object),
            graph);
    }

    /// <summary>Parses, normalizes and executes an update against the dataset.</summary>
    /// <param name="dataset">The dataset to mutate.</param>
    /// <param name="text">The update text.</param>
    /// <param name="pool">The interning pool.</param>
    /// <returns>The asynchronous execution.</returns>
    private async Task ExecuteAsync(MutableSparqlDataset dataset, string text, Utf8StringPool pool)
    {
        SparqlUpdateRequest request = ParseAndNormalize(text, pool);
        await SparqlUpdateExecutor.ExecuteAsync(request, dataset, SparqlExpressionContext.CreateDefault(), graphSource: null, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task InsertDataAddsToDefaultGraph()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { <urn:s> <urn:p> <urn:o> }", pool).ConfigureAwait(false);

        Assert.HasCount(1, DumpQuads(dataset));
    }

    [TestMethod]
    public async Task DeleteDataRemovesAnExistingTriple()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { <urn:s> <urn:p> <urn:o> . <urn:s> <urn:p> <urn:o2> }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "DELETE DATA { <urn:s> <urn:p> <urn:o> }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.AreEqual("urn:o2", ((NamedNode)quads[0].Object).Iri.ToString());
    }

    [TestMethod]
    public async Task InsertDataIntoNamedGraphCreatesIt()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g> { <urn:s> <urn:p> <urn:o> } }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.IsNotNull(quads[0].Graph);
        Assert.AreEqual("urn:g", ((NamedNode)quads[0].Graph!).Iri.ToString());
    }

    [TestMethod]
    public async Task InsertDataLowersStandaloneBlankNodePropertyList()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { [ <urn:p> <urn:o> ] }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<BlankNode>(quads[0].Subject);
        Assert.AreEqual("urn:p", quads[0].Predicate.Iri.ToString());
        Assert.AreEqual("urn:o", ((NamedNode)quads[0].Object).Iri.ToString());
    }

    [TestMethod]
    public async Task InsertDataLowersStandaloneNodeInsideGraphGroup()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g> { [ <urn:p> <urn:o> ] } }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.IsInstanceOfType<BlankNode>(quads[0].Subject);
        Assert.AreEqual("urn:g", ((NamedNode)quads[0].Graph!).Iri.ToString());
    }

    [TestMethod]
    public async Task ModifyDeletesAndInsertsPerSolution()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { <urn:a> <urn:knows> <urn:b> . <urn:c> <urn:knows> <urn:d> }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "DELETE { ?s <urn:knows> ?o } INSERT { ?o <urn:knownby> ?s } WHERE { ?s <urn:knows> ?o }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(2, quads);
        foreach(Quad quad in quads)
        {
            Assert.AreEqual("urn:knownby", quad.Predicate.Iri.ToString(), "every knows triple should have been rewritten to knownby");
        }
    }

    [TestMethod]
    public async Task DeleteWhereRemovesMatches()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { <urn:a> <urn:p> <urn:b> . <urn:c> <urn:q> <urn:d> }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "DELETE WHERE { ?s <urn:p> ?o }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.AreEqual("urn:q", quads[0].Predicate.Iri.ToString());
    }

    [TestMethod]
    public async Task ClearDefaultEmptiesTheDefaultGraph()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { <urn:s> <urn:p> <urn:o> . GRAPH <urn:g> { <urn:a> <urn:b> <urn:c> } }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "CLEAR DEFAULT", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads, "the named graph survives CLEAR DEFAULT");
        Assert.IsNotNull(quads[0].Graph);
    }

    [TestMethod]
    public async Task DropGraphRemovesNamedGraph()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g> { <urn:a> <urn:b> <urn:c> } }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "DROP GRAPH <urn:g>", pool).ConfigureAwait(false);

        Assert.IsEmpty(DumpQuads(dataset));
    }

    [TestMethod]
    public async Task CopyReplacesDestinationKeepingSource()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g1> { <urn:s> <urn:p> <urn:o> } GRAPH <urn:g2> { <urn:x> <urn:y> <urn:z> } }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "COPY <urn:g1> TO <urn:g2>", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(2, quads, "g1 kept; g2 replaced by g1's single triple");
        foreach(Quad quad in quads)
        {
            Assert.AreEqual("urn:p", quad.Predicate.Iri.ToString());
        }
    }

    [TestMethod]
    public async Task MoveReplacesDestinationAndDropsSource()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g1> { <urn:s> <urn:p> <urn:o> } GRAPH <urn:g2> { <urn:x> <urn:y> <urn:z> } }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "MOVE <urn:g1> TO <urn:g2>", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads, "g1 dropped; g2 = g1's triple");
        Assert.AreEqual("urn:g2", ((NamedNode)quads[0].Graph!).Iri.ToString());
        Assert.AreEqual("urn:p", quads[0].Predicate.Iri.ToString());
    }

    [TestMethod]
    public async Task AddMergesSourceIntoDestination()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g1> { <urn:s> <urn:p> <urn:o> } GRAPH <urn:g2> { <urn:x> <urn:y> <urn:z> } }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "ADD <urn:g1> TO <urn:g2>", pool).ConfigureAwait(false);

        int g2Count = 0;
        foreach(Quad quad in DumpQuads(dataset))
        {
            if(quad.Graph is NamedNode named && named.Iri.ToString() == "urn:g2")
            {
                g2Count++;
            }
        }

        Assert.AreEqual(2, g2Count, "g2 keeps its own triple and gains g1's");
    }

    [TestMethod]
    public async Task WithRetargetsModifyToTheNamedGraph()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        await ExecuteAsync(dataset, "INSERT DATA { GRAPH <urn:g> { <urn:a> <urn:knows> <urn:b> } }", pool).ConfigureAwait(false);
        await ExecuteAsync(dataset, "WITH <urn:g> DELETE { ?s <urn:knows> ?o } INSERT { ?s <urn:friend> ?o } WHERE { ?s <urn:knows> ?o }", pool).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.AreEqual("urn:friend", quads[0].Predicate.Iri.ToString());
        Assert.AreEqual("urn:g", ((NamedNode)quads[0].Graph!).Iri.ToString(), "WITH retargets the unqualified template to the WITH graph");
    }

    [TestMethod]
    public async Task LoadAddsResolvedTriplesToTheDefaultGraph()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        SparqlUpdateRequest request = ParseAndNormalize("LOAD <urn:doc>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            request,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => new ValueTask<IReadOnlyList<DataTriple>>(
                (IReadOnlyList<DataTriple>)[new DataTriple(new NamedNode(Utf8String.FromString("urn:s")), new NamedNode(Utf8String.FromString("urn:p")), new NamedNode(Utf8String.FromString("urn:o")))]),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, DumpQuads(dataset));
    }
}
