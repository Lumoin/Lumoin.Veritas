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
        HypertrieGraphStore defaultGraph = await MutableSparqlDataset.BuildGraphAsync([]).ConfigureAwait(false);

        return new MutableSparqlDataset(new TermDictionary(), defaultGraph, new Dictionary<TermId, HypertrieGraphStore>());
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
        await SparqlUpdateExecutor.ExecuteAsync(request, dataset, SparqlExpressionContext.CreateDefault(), TestContext.CancellationToken).ConfigureAwait(false);
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
}
