using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
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

namespace Lumoin.Veritas.ParserTests.Sparql;

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
    public async Task ModifyWhereEvaluatesServiceThroughTheFederationSeam()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        //The remote endpoint: a self-contained engine reached through
        //the in-process transport — the same seam an HTTP transport
        //implements.
        SparqlQueryEngine remote = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(new NamedNode(Utf8Strings.From("urn:remote-s")), new NamedNode(Utf8Strings.From("urn:remote-p")), new NamedNode(Utf8Strings.From("urn:remote-o")))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Federation.SparqlTestEndpoint endpoint = new("remote", remote);
        SparqlClient serviceClient = new((_, query, _, cancellationToken) => endpoint.ExecuteAsync(query, cancellationToken));

        SparqlUpdateRequest request = ParseAndNormalize(
            "INSERT { ?s ?p ?o } WHERE { SERVICE <http://remote.example/sparql> { ?s ?p ?o } }",
            pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            request,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            serviceClient: serviceClient,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads);
        Assert.AreEqual("urn:remote-s", ((NamedNode)quads[0].Subject).Iri.ToString());
        Assert.AreEqual("urn:remote-o", ((NamedNode)quads[0].Object).Iri.ToString());
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
            graphSource: static (source, accessContext, ct) => AsyncTripleStream(Triple("urn:s", "urn:p", "urn:o")),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, DumpQuads(dataset));
    }

    /// <summary>A LOAD whose resolver is a genuine async stream — a real asynchronous yield point precedes each triple, not a wrapped list — loads every triple.</summary>
    [TestMethod]
    public async Task LoadFromATrueAsyncStreamLoadsEveryTriple()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        SparqlUpdateRequest request = ParseAndNormalize("LOAD <urn:doc>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            request,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => AsyncTripleStream(
                Triple("urn:s1", "urn:p", "urn:o1"),
                Triple("urn:s2", "urn:p", "urn:o2"),
                Triple("urn:s3", "urn:p", "urn:o3")),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(3, DumpQuads(dataset), "Every triple of a genuinely asynchronous source stream is loaded.");
    }

    /// <summary>LOAD is atomic across the whole enumeration: a resolver that throws MID-stream (after yielding triples) applies nothing — the non-silent form propagates the failure, the silent form swallows it, and either way the target is unchanged because nothing is applied until the stream completes. The dictionary boundary is pinned honestly alongside: terms yielded before the failure ARE minted (encoding is per-arriving-triple into the shared dictionary — the same eager minting every update operation exhibits when a later step fails), and an unreferenced term id is inert, so the quad-level atomicity above is the whole observable contract.</summary>
    [TestMethod]
    public async Task LoadIsAtomicWhenTheResolverThrowsMidStream()
    {
        using Utf8StringPool pool = new();

        MutableSparqlDataset nonSilent = await EmptyDatasetAsync().ConfigureAwait(false);
        SparqlUpdateRequest load = ParseAndNormalize("LOAD <urn:doc>", pool);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await SparqlUpdateExecutor.ExecuteAsync(
                load,
                nonSilent,
                SparqlExpressionContext.CreateDefault(),
                graphSource: static (source, accessContext, ct) => ThrowingTripleStream(Triple("urn:s1", "urn:p", "urn:o1"), Triple("urn:s2", "urn:p", "urn:o2")),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.IsEmpty(DumpQuads(nonSilent), "A non-silent LOAD whose source fails mid-stream propagates and applies nothing.");

        MutableSparqlDataset silent = await EmptyDatasetAsync().ConfigureAwait(false);
        int termCountBefore = silent.Dictionary.Count;
        SparqlUpdateRequest loadSilent = ParseAndNormalize("LOAD SILENT <urn:doc>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            loadSilent,
            silent,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => ThrowingTripleStream(Triple("urn:s1", "urn:p", "urn:o1"), Triple("urn:s2", "urn:p", "urn:o2")),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsEmpty(DumpQuads(silent), "A silent LOAD whose source fails mid-stream swallows the failure and applies nothing.");
        Assert.IsGreaterThanOrEqualTo(termCountBefore + 5, silent.Dictionary.Count, "The five distinct terms yielded before the failure are minted though no quad was applied — the eager-minting boundary every update operation shares.");
    }

    /// <summary>A cancellation raised MID-stream propagates out of LOAD and applies nothing — even a LOAD SILENT never swallows an <see cref="OperationCanceledException"/>.</summary>
    [TestMethod]
    public async Task LoadPropagatesCancellationRaisedMidStream()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);
        using CancellationTokenSource cts = new();

        SparqlUpdateRequest request = ParseAndNormalize("LOAD SILENT <urn:doc>", pool);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await SparqlUpdateExecutor.ExecuteAsync(
                request,
                dataset,
                SparqlExpressionContext.CreateDefault(),
                graphSource: (source, accessContext, token) => CancellingTripleStream(cts, token),
                cancellationToken: cts.Token).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.IsEmpty(DumpQuads(dataset), "A cancellation mid-stream applies nothing; a SILENT LOAD does not swallow OperationCanceledException.");
    }

    /// <summary>Under <see cref="SparqlUpdateOptions.ContextualAssertionLoad"/>, a plain <c>LOAD</c> imports the document as a contextual assertion: the triples land in a freshly minted blank-node graph, the default graph gains exactly ONE provenance triple naming the graph's source, and nothing else reaches the default graph.</summary>
    [TestMethod]
    public async Task ContextualAssertionLoadLandsInAFreshBlankNodeGraphWithProvenance()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        SparqlUpdateRequest request = ParseAndNormalize("LOAD <urn:doc>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            request,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => AsyncTripleStream(Triple("urn:s", "urn:p", "urn:o")),
            updateOptions: new SparqlUpdateOptions(ContextualAssertionLoad: true),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(2, quads, "The contextual load applies the content quad and the provenance quad, nothing else.");

        Quad? content = null;
        Quad? provenance = null;
        foreach(Quad quad in quads)
        {
            if(quad.Graph is null)
            {
                provenance = quad;
            }
            else
            {
                content = quad;
            }
        }

        Assert.IsNotNull(content, "The imported triple lands in a named graph.");
        Assert.IsInstanceOfType<BlankNode>(content.Graph, "The context graph is a freshly minted blank node.");
        Assert.AreEqual("urn:s", ((NamedNode)content.Subject).Iri.ToString());
        Assert.AreEqual("urn:o", ((NamedNode)content.Object).Iri.ToString());

        Assert.IsNotNull(provenance, "The default graph carries the provenance triple.");
        Assert.AreEqual(content.Graph, provenance.Subject, "The provenance triple's subject is the context graph's own blank node.");
        Assert.AreEqual("http://www.w3.org/ns/prov#wasDerivedFrom", provenance.Predicate.Iri.ToString());
        Assert.AreEqual("urn:doc", ((NamedNode)provenance.Object).Iri.ToString(), "The provenance triple names the source document.");
    }

    /// <summary>Two contextual loads mint two DISTINCT blank-node context graphs — the freshness probe never reuses a label the dataset already holds, so imports never merge into one context.</summary>
    [TestMethod]
    public async Task ContextualAssertionLoadMintsADistinctGraphPerLoad()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        SparqlUpdateOptions contextual = new(ContextualAssertionLoad: true);
        SparqlUpdateRequest first = ParseAndNormalize("LOAD <urn:doc1>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            first,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => AsyncTripleStream(Triple("urn:s1", "urn:p", "urn:o1")),
            updateOptions: contextual,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlUpdateRequest second = ParseAndNormalize("LOAD <urn:doc2>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            second,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => AsyncTripleStream(Triple("urn:s2", "urn:p", "urn:o2")),
            updateOptions: contextual,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(4, quads, "Each contextual load applies one content quad and one provenance quad.");

        List<RdfTerm> contextGraphs = [];
        foreach(Quad quad in quads)
        {
            if(quad.Graph is BlankNode graph && !contextGraphs.Contains(graph))
            {
                contextGraphs.Add(graph);
            }
        }

        Assert.HasCount(2, contextGraphs, "The two imports land in two distinct blank-node context graphs.");
    }

    /// <summary>An explicit <c>LOAD … INTO GRAPH</c> destination wins over the contextual-assertion option: the triples land in the named target and no provenance triple or blank-node graph appears.</summary>
    [TestMethod]
    public async Task ContextualAssertionLoadLeavesLoadIntoUntouched()
    {
        using Utf8StringPool pool = new();
        MutableSparqlDataset dataset = await EmptyDatasetAsync().ConfigureAwait(false);

        SparqlUpdateRequest request = ParseAndNormalize("LOAD <urn:doc> INTO GRAPH <urn:g>", pool);
        await SparqlUpdateExecutor.ExecuteAsync(
            request,
            dataset,
            SparqlExpressionContext.CreateDefault(),
            graphSource: static (source, accessContext, ct) => AsyncTripleStream(Triple("urn:s", "urn:p", "urn:o")),
            updateOptions: new SparqlUpdateOptions(ContextualAssertionLoad: true),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        List<Quad> quads = DumpQuads(dataset);
        Assert.HasCount(1, quads, "An explicit destination applies exactly the content quad — no provenance triple.");
        Assert.IsNotNull(quads[0].Graph, "The triple lands in the named target graph.");
        Assert.AreEqual("urn:g", ((NamedNode)quads[0].Graph!).Iri.ToString());
    }

    /// <summary>An IRI term from its string.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>A triple of IRI terms.</summary>
    /// <param name="subject">The subject IRI.</param>
    /// <param name="predicate">The predicate IRI.</param>
    /// <param name="object">The object IRI.</param>
    /// <returns>The triple.</returns>
    private static DataTriple Triple(string subject, string predicate, string @object)
    {
        return new DataTriple(Iri(subject), Iri(predicate), Iri(@object));
    }

    /// <summary>Streams the given triples as a genuine async source, with a real asynchronous yield point before each — a true async stream rather than a wrapped list.</summary>
    /// <param name="triples">The triples to stream.</param>
    /// <returns>The async triple stream.</returns>
    private static async IAsyncEnumerable<DataTriple> AsyncTripleStream(params DataTriple[] triples)
    {
        foreach(DataTriple triple in triples)
        {
            await Task.Yield();
            yield return triple;
        }
    }

    /// <summary>Streams the given triples, then throws — a resolution/parse failure that surfaces MID-stream after some triples have already been yielded.</summary>
    /// <param name="triples">The triples to stream before failing.</param>
    /// <returns>The async triple stream that fails after the last triple.</returns>
    private static async IAsyncEnumerable<DataTriple> ThrowingTripleStream(params DataTriple[] triples)
    {
        foreach(DataTriple triple in triples)
        {
            await Task.Yield();
            yield return triple;
        }

        throw new InvalidOperationException("the LOAD source became unreadable mid-stream");
    }

    /// <summary>Streams one triple, then cancels the trigger and observes the token — a cancellation raised MID-stream after a triple has been yielded.</summary>
    /// <param name="trigger">The source cancelled after the first triple.</param>
    /// <param name="cancellationToken">The token the stream observes (the LOAD's token).</param>
    /// <returns>The async triple stream that cancels after the first triple.</returns>
    private static async IAsyncEnumerable<DataTriple> CancellingTripleStream(CancellationTokenSource trigger, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return Triple("urn:s1", "urn:p", "urn:o1");
        await trigger.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        yield return Triple("urn:s2", "urn:p", "urn:o2");
    }
}
