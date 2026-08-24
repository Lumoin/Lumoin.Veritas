using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Xml;
using Lumoin.Veritas.Rdf.Json;
using IoPath = System.IO.Path;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Dispatches a SPARQL <see cref="W3cTestType.SparqlQueryEvaluation"/> test case: builds the engine over the data
/// graph (<c>qt:data</c>), runs the query (<c>qt:query</c>) through parse → normalize → translate → execute, and
/// compares the result to the expected <c>mf:result</c> fixture with <see cref="SparqlResultComparer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> <c>SELECT</c> and <c>ASK</c> results, whose expected fixture is a SPARQL Query Results XML
/// (<c>.srx</c>) file, are executed and compared. The honest-skip cases (<see cref="W3cOutcomeStatus.Skipped"/>)
/// are the features not yet built rather than defects: <c>CONSTRUCT</c>/<c>DESCRIBE</c> (the engine yields solution
/// mappings, not a constructed graph, until result construction lands), a JSON (<c>.srj</c>) expected fixture (the
/// JSON results reader is not built yet), and any query whose algebra uses an operator the executor does not yet
/// support (a <see cref="NotSupportedException"/>). A genuine wrong answer is <see cref="W3cOutcomeStatus.Failed"/>.
/// </para>
/// <para>
/// The query and data terms bridge by <see cref="RdfTerm"/> value identity (the parser and the Turtle reader
/// intern into independent pools, but the engine's term dictionary compares terms by value), so cross-pool
/// resolution is correct.
/// </para>
/// </remarks>
internal static class W3cSparqlEvalRunner
{
    /// <summary>
    /// Runs one SPARQL Update syntax test: parses the update request and checks the parse outcome against the test's
    /// polarity. A positive test must parse clean; a negative test must produce at least one diagnostic.
    /// </summary>
    /// <param name="testCase">The update-syntax test case (<c>mf:action</c> is a <c>.ru</c> file).</param>
    /// <param name="expectSuccess"><see langword="true"/> for a positive-syntax test, <see langword="false"/> for a negative one.</param>
    /// <param name="cancellationToken">A token to cancel reading and parsing.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="testCase"/> is <see langword="null"/>.</exception>
    public static async Task<W3cOutcome> RunUpdateSyntaxAsync(W3cTestCase testCase, bool expectSuccess, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Update request file not found: {testCase.InputPath}");
        }

        using Utf8StringPool pool = new();
        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(
            await File.ReadAllBytesAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false),
            pool,
            Utf8Strings.From(new Uri(IoPath.GetFullPath(testCase.InputPath)).AbsoluteUri));

        //A positive test must also produce an update request (not a query) — a parse that silently yields a query
        //AST from an update file would be a misclassification, not a pass.
        if(expectSuccess)
        {
            return parsed.HasErrors
                ? new W3cOutcome(W3cOutcomeStatus.Failed, $"Update request should parse but did not: {DescribeFirstError(parsed.Diagnostics)}")
                : parsed.Tree is SparqlUpdateRequest
                    ? new W3cOutcome(W3cOutcomeStatus.Passed, "Update request parsed.")
                    : new W3cOutcome(W3cOutcomeStatus.Failed, "Update request parsed as a query, not an update.");
        }

        return parsed.HasErrors
            ? new W3cOutcome(W3cOutcomeStatus.Passed, "Malformed update request was rejected.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, "Malformed update request parsed without error.");
    }

    /// <summary>
    /// Runs one SPARQL Update evaluation test: parses the <c>ut:request</c>, builds a mutable dataset from the initial
    /// data (<c>ut:data</c> / <c>ut:graphData</c>), executes the update through edit-session commits, and compares the
    /// resulting dataset to the expected one (<c>mf:result</c>'s <c>ut:data</c>) up to blank-node isomorphism.
    /// </summary>
    /// <param name="testCase">The update-evaluation test case.</param>
    /// <param name="enginePolicy">The execution-strategy policy the update's WHERE-clause engines are built under; a differential arm passes its policy so update fixtures exercise the same route as the query arms.</param>
    /// <param name="valueIndexes">The composed value-index registry the dataset carries, or <see langword="null"/> for none — the decline arm passes a registry matching nothing in the corpus.</param>
    /// <param name="cancellationToken">A token to cancel reading, parsing, and execution.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="testCase"/> is <see langword="null"/>.</exception>
    public static async Task<W3cOutcome> RunUpdateEvalAsync(W3cTestCase testCase, SparqlEnginePolicy enginePolicy = default, ValueIndexRegistry? valueIndexes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Update request file not found: {testCase.InputPath}");
        }

        //A SPARQL Update evaluation test always declares mf:result; an empty result node (`[]`) means the expected
        //dataset is empty (compared against, not skipped), so there is no "no fixture" skip here.
        using Utf8StringPool pool = new();
        string baseUri = new Uri(IoPath.GetFullPath(testCase.InputPath)).AbsoluteUri;
        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(
            await File.ReadAllBytesAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false),
            pool,
            Utf8Strings.From(baseUri));
        if(parsed.HasErrors)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Update request did not parse: {DescribeFirstError(parsed.Diagnostics)}");
        }

        if(parsed.Tree is not SparqlUpdateRequest update)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, "The request parsed as a query, not an update.");
        }

        MutableSparqlDataset dataset;
        List<Quad> expected;
        try
        {
            //Initial dataset: the default graph (ut:data) plus the named graphs — both the ut:graphData entries
            //(keyed by their rdfs:label graph name) and any named graphs a single TriG/N-Quads ut:data file carries.
            List<DataTriple> data = await LoadDataAsync(testCase.QueryDataPath, cancellationToken).ConfigureAwait(false);
            List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = await LoadLabelledGraphsAsync(testCase.UpdateInputGraphs, cancellationToken).ConfigureAwait(false);
            named.AddRange(await LoadNamedFromFileAsync(testCase.QueryDataPath, cancellationToken).ConfigureAwait(false));
            dataset = await BuildMutableDatasetAsync(data, named, valueIndexes, cancellationToken).ConfigureAwait(false);

            //Expected dataset: the result's default graph (ut:data) plus its labelled named graphs (ut:graphData).
            expected = testCase.ExpectedPath is not null ? await ReadQuadsAsync(testCase.ExpectedPath, cancellationToken).ConfigureAwait(false) : [];
            foreach((string graphName, string path) in testCase.UpdateExpectedGraphs ?? [])
            {
                RdfTerm graph = new NamedNode(Utf8Strings.From(graphName));
                foreach(Quad quad in await ReadQuadsAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    expected.Add(quad.Graph is null ? quad with { Graph = graph } : quad);
                }
            }
        }
        catch(InvalidOperationException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Could not load a graph (unsupported format or malformed): {ex.Message}");
        }

        SparqlUpdateRequest normalized = (SparqlUpdateRequest)new SparqlNormalizer(pool).Normalize(update);
        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault().WithBaseIri(Utf8Strings.From(baseUri));
        try
        {
            await SparqlUpdateExecutor.ExecuteAsync(normalized, dataset, context, LocalFileGraphSource, enginePolicy: enginePolicy, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch(NotSupportedException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Update uses a feature the executor does not yet support: {ex.Message}");
        }

        List<Quad> actual = DumpDataset(dataset);

        return QuadSetIsomorphism.AreIsomorphic(actual, expected)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, $"Resulting dataset matches ({actual.Count} quad(s)).")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"Resulting dataset does not match expected (actual {actual.Count} vs expected {expected.Count} quad(s)).");
    }

    /// <summary>
    /// The <c>LOAD</c> source resolver the conformance harness uses: maps a (post-base-resolution, absolute) source
    /// IRI to its local file and streams the document's default-graph triples. No network — the suite's sources are
    /// sibling files of the request, addressed by <c>file:</c> IRIs after the parser resolves them against the request
    /// base. A missing or unreadable source surfaces as an exception (the executor swallows it only for <c>LOAD SILENT</c>).
    /// </summary>
    /// <param name="source">The source IRI.</param>
    /// <param name="accessContext">The access context (unused by this file-backed resolver).</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The source document's triples, streamed as they are read.</returns>
    private static async IAsyncEnumerable<DataTriple> LocalFileGraphSource(IriRef source, AccessContext? accessContext, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string path = new Uri(source.Value.ToString()).LocalPath;
        foreach(Quad quad in await ReadQuadsAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if(quad.Graph is null)
            {
                yield return new DataTriple(quad.Subject, quad.Predicate, quad.Object);
            }
        }
    }

    /// <summary>Loads a SPARQL Update test's labelled named graphs — (graph-name, file) pairs from <c>ut:graphData</c> — keying each graph by its <c>rdfs:label</c> name (not the file IRI).</summary>
    /// <param name="graphs">The (graph-name, file) pairs, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The named graphs as (name, triples) pairs.</returns>
    private static async Task<List<(RdfTerm Name, IEnumerable<DataTriple> Triples)>> LoadLabelledGraphsAsync(IReadOnlyList<(string GraphName, string Path)>? graphs, CancellationToken cancellationToken)
    {
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = [];
        if(graphs is null)
        {
            return named;
        }

        foreach((string graphName, string path) in graphs)
        {
            RdfTerm name = new NamedNode(Utf8Strings.From(graphName));
            named.Add((name, await LoadDataAsync(path, cancellationToken).ConfigureAwait(false)));
        }

        return named;
    }

    /// <summary>Builds a mutable dataset by encoding the initial default and named graphs into one shared dictionary, all graphs over one shared arena and one dataset journal.</summary>
    /// <param name="defaultTriples">The default-graph triples.</param>
    /// <param name="namedGraphs">The named graphs as (name, triples) pairs.</param>
    /// <param name="cancellationToken">A token to cancel the build.</param>
    /// <returns>The mutable dataset.</returns>
    private static async Task<MutableSparqlDataset> BuildMutableDatasetAsync(
        List<DataTriple> defaultTriples,
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs,
        ValueIndexRegistry? valueIndexes,
        CancellationToken cancellationToken)
    {
        TermDictionary dictionary = new();
        Dictionary<TermId, IReadOnlyList<EncodedTriple>> named = [];
        foreach((RdfTerm name, IEnumerable<DataTriple> triples) in namedGraphs)
        {
            named[dictionary.GetOrAdd(name)] = Encode(triples, dictionary);
        }

        return await MutableSparqlDataset.CreateAsync(dictionary, Encode(defaultTriples, dictionary), named, valueIndexes: valueIndexes, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes triples into the shared dictionary as encoded triples.</summary>
    /// <param name="triples">The triples to encode.</param>
    /// <param name="dictionary">The shared dictionary.</param>
    /// <returns>The encoded triples.</returns>
    private static List<EncodedTriple> Encode(IEnumerable<DataTriple> triples, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in triples)
        {
            encoded.Add(EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(triple.Subject).Encoded,
                dictionary.GetOrAdd(triple.Predicate).Encoded,
                dictionary.GetOrAdd(triple.Object).Encoded));
        }

        return encoded;
    }

    /// <summary>Reads a mutable dataset's current quads (default graph + named graphs), decoding through its dictionary.</summary>
    /// <param name="dataset">The dataset.</param>
    /// <returns>The dataset's quads.</returns>
    private static List<Quad> DumpDataset(MutableSparqlDataset dataset)
    {
        List<Quad> quads = [];
        foreach(EncodedTriple triple in dataset.DefaultGraph.Match(TermId.None, TermId.None, TermId.None))
        {
            quads.Add(DecodeQuad(dataset, triple, graph: null));
        }

        foreach(TermId graphId in dataset.NamedGraphNames)
        {
            dataset.TryGetNamedGraph(graphId, out HypertrieGraphStore? store);
            RdfTerm graph = dataset.Dictionary.Resolve(graphId);
            foreach(EncodedTriple triple in store!.Match(TermId.None, TermId.None, TermId.None))
            {
                quads.Add(DecodeQuad(dataset, triple, graph));
            }
        }

        return quads;
    }

    /// <summary>Decodes an encoded triple to a quad in the given graph through the dataset's dictionary.</summary>
    /// <param name="dataset">The dataset whose dictionary decodes the terms.</param>
    /// <param name="triple">The encoded triple.</param>
    /// <param name="graph">The graph term, or <see langword="null"/> for the default graph.</param>
    /// <returns>The decoded quad.</returns>
    private static Quad DecodeQuad(MutableSparqlDataset dataset, EncodedTriple triple, RdfTerm? graph)
    {
        return new Quad(
            dataset.Dictionary.Resolve(triple.Subject),
            (NamedNode)dataset.Dictionary.Resolve(triple.Predicate),
            dataset.Dictionary.Resolve(triple.Object),
            graph);
    }

    /// <summary>Runs one SPARQL query-evaluation test case under one differential arm: the engine is built under
    /// <paramref name="enginePolicy"/>, and <paramref name="throughStreamingEntry"/> additionally drains every
    /// SELECT/ASK through <see cref="SparqlQueryEngine.EvaluateStreamingAsync"/> — the arm that routes the
    /// whole-plan cursor pipeline through the corpus oracle. All arms must produce identical outcomes per
    /// fixture (the answer-identity gate).</summary>
    /// <param name="testCase">The test case to run.</param>
    /// <param name="enginePolicy">The execution-strategy policy the engine is built under; the default is the off-mode baseline arm.</param>
    /// <param name="throughStreamingEntry">Whether SELECT/ASK evaluate by draining the streaming entry instead of the materialising entries (CONSTRUCT/DESCRIBE are unaffected — no production path reaches the cursors through them).</param>
    /// <param name="valueIndexes">The composed value-index registry the engine carries, or <see langword="null"/> for none — the decline arm passes a registry matching nothing in the corpus.</param>
    /// <param name="cancellationToken">A token to cancel parsing, building, and evaluation.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="testCase"/> is <see langword="null"/>.</exception>
    public static async Task<W3cOutcome> RunAsync(W3cTestCase testCase, SparqlEnginePolicy enginePolicy = default, bool throughStreamingEntry = false, ValueIndexRegistry? valueIndexes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Query file not found: {testCase.InputPath}");
        }

        if(testCase.ExpectedPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "Evaluation test declares no mf:result fixture.");
        }

        if(!File.Exists(testCase.ExpectedPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected result file not found: {testCase.ExpectedPath}");
        }

        if(testCase.QueryDataPath is not null && !File.Exists(testCase.QueryDataPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Data graph file not found: {testCase.QueryDataPath}");
        }

        using Utf8StringPool pool = new();

        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(
            await File.ReadAllBytesAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false),
            pool,
            Utf8Strings.From(new Uri(IoPath.GetFullPath(testCase.InputPath)).AbsoluteUri));
        if(parsed.HasErrors)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Query did not parse: {DescribeFirstError(parsed.Diagnostics)}");
        }

        if(parsed.Tree is not SparqlQuery query)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "SPARQL Update requests are not evaluated by this build.");
        }

        List<DataTriple> data;
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs;
        try
        {
            data = await LoadDataAsync(testCase.QueryDataPath, cancellationToken).ConfigureAwait(false);
            namedGraphs = await LoadNamedGraphsAsync(testCase.GraphDataPaths, cancellationToken).ConfigureAwait(false);

            //A single TriG / N-Quads data file can itself carry named graphs (beyond the default graph already in
            //`data`); fold those into the dataset too.
            namedGraphs.AddRange(await LoadNamedFromFileAsync(testCase.QueryDataPath, cancellationToken).ConfigureAwait(false));
        }
        catch(InvalidOperationException ex)
        {
            //A graph (default or named) is in a format this harness cannot read (RDF/XML, TriG) or is malformed:
            //the test cannot be set up, so it is skipped structurally rather than reported as an engine failure.
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Could not load a graph (unsupported format or malformed): {ex.Message}");
        }

        //An entailment test evaluates over the regime's entailed graph, and its expected result holds under
        //each regime the action lists — so evaluating under any one implemented regime suffices. The RDF, RDFS,
        //and D regimes evaluate over the finite RDFS closure (axiomatic rules included); an OWL RDF-Based test
        //whose sd:EntailmentProfile sanctions pr:RL additionally composes the RL rules closure (the regimes
        //specification conditions the OWL RDF-Based regime on a profile). A test offering only OWL Direct
        //Semantics, an unsanctioned OWL RDF-Based, or RIF skips.
        if(testCase.EntailmentRegimes is { Count: > 0 } regimes)
        {
            bool rlSanctioned = ContainsIri(regimes, "http://www.w3.org/ns/entailment/OWL-RDF-Based")
                && testCase.EntailmentProfiles is { } profiles
                && ContainsIri(profiles, "http://www.w3.org/ns/owl-profile/RL");

            if(!HasImplementedRegime(regimes) && !rlSanctioned)
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Entailment regime(s) not implemented: {string.Join(", ", regimes)}.");
            }

            data = ExpandRegimeClosure(data, includeRlClosure: rlSanctioned);
        }

        //IRI()/URI() resolve a relative argument against the query's effective base — the last in-query BASE, else
        //the external (file-URL) base the parser was given. Thread it through the expression context.
        Utf8String? baseIri = query.Prologue.Bases is { Count: > 0 } bases
            ? bases[^1].Iri.Value
            : Utf8Strings.From(new Uri(IoPath.GetFullPath(testCase.InputPath)).AbsoluteUri);
        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault().WithBaseIri(baseIri);

        //A test with qt:graphData evaluates against a dataset (default graph + named graphs); otherwise the single
        //default graph. GRAPH forms resolve their designator against the same file-URL space.
        SparqlQueryEngine engine = namedGraphs.Count == 0
            ? await SparqlQueryEngine.BuildAsync(data, context, enginePolicy: enginePolicy, valueIndexes: valueIndexes, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await SparqlQueryEngine.BuildDatasetAsync(data, namedGraphs, context, enginePolicy: enginePolicy, valueIndexes: valueIndexes, cancellationToken: cancellationToken).ConfigureAwait(false);

        //An in-query FROM / FROM NAMED clause defines the dataset (SPARQL §13.2), OVERRIDING the manifest's
        //qt:data / qt:graphData: the engine itself resolves each clause IRI (a sibling file) and scopes to the
        //effective dataset — default graph = the merge of the FROM graphs, named graphs = the FROM NAMED graphs.
        try
        {
            engine = await engine.WithDatasetAsync(query.Dataset, ResolveGraphFromFileAsync, cancellationToken).ConfigureAwait(false);
        }
        catch(InvalidOperationException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Could not load a FROM / FROM NAMED graph (unsupported format or malformed): {ex.Message}");
        }

        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);

        //CONSTRUCT / DESCRIBE produce an RDF graph; compare it to the expected graph up to blank-node isomorphism
        //(via RDFC-1.0) — the same equivalence the SHACL report comparison uses.
        if(normalized.Form is ConstructQuery or DescribeQuery)
        {
            List<Quad> actualGraph;
            try
            {
                actualGraph = await BuildGraphResultAsync(engine, normalized, cancellationToken).ConfigureAwait(false);
            }
            catch(NotSupportedException ex)
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Query uses a feature the executor does not yet support: {ex.Message}");
            }

            List<Quad> expectedGraph;
            try
            {
                expectedGraph = await LoadGraphAsync(testCase.ExpectedPath!, cancellationToken).ConfigureAwait(false);
            }
            catch(InvalidOperationException ex)
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Expected graph fixture is in a format this harness cannot read: {ex.Message}");
            }

            return QuadSetIsomorphism.AreIsomorphic(actualGraph, expectedGraph)
                ? new W3cOutcome(W3cOutcomeStatus.Passed, $"Graph matches ({actualGraph.Count} quad(s)).")
                : new W3cOutcome(W3cOutcomeStatus.Failed, $"Graph result does not match expected (actual {actualGraph.Count} vs expected {expectedGraph.Count} quad(s)).");
        }

        SparqlResultSet actual;
        try
        {
            AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
            if(throughStreamingEntry)
            {
                //The streaming arm: SELECT and ASK both drain the streaming entry into a list, so the
                //whole-plan cursor pipeline (and its materialise-boundary fallbacks) answers the fixture.
                List<SparqlSolution> streamed = [];
                await foreach(SparqlSolution solution in engine.EvaluateStreamingAsync(algebra, cancellationToken).ConfigureAwait(false))
                {
                    streamed.Add(solution);
                }

                actual = query.Form is AskQuery
                    ? SparqlResultSet.ForAsk(streamed.Count > 0)
                    : BuildResultSet(query, streamed);
            }
            else if(query.Form is AskQuery)
            {
                //ASK goes through the engine's short-circuiting entry, so every ASK fixture
                //exercises the first-solution fast path against the expected boolean.
                actual = SparqlResultSet.ForAsk(await engine.EvaluateAskAsync(algebra, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);
                actual = BuildResultSet(query, solutions);
            }
        }
        catch(NotSupportedException ex)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Query uses a feature the executor does not yet support: {ex.Message}");
        }

        //The solution-result serializations (.srx / .srj / .tsv) parse back to a result set and compare by value;
        //the lossy CSV (.csv) is compared by re-serializing the actual result and matching the text. Any other
        //extension is a graph result, not compared here.
        string extension = IoPath.GetExtension(testCase.ExpectedPath);
        byte[] expectedBytes = await File.ReadAllBytesAsync(testCase.ExpectedPath, cancellationToken).ConfigureAwait(false);

        if(string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return CompareCsv(actual, expectedBytes);
        }

        bool ordered = query.Modifier.Order is not null;
        SparqlResultSet expected;

        //A SELECT/ASK result given as an RDF graph (the W3C rs:ResultSet vocabulary in Turtle / N-Triples) is read
        //into a result set and value-compared, just like the .srx/.srj/.tsv serializations.
        if(string.Equals(extension, ".ttl", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".nt", StringComparison.OrdinalIgnoreCase))
        {
            List<Quad> resultGraph;
            try
            {
                resultGraph = await LoadGraphAsync(testCase.ExpectedPath!, cancellationToken).ConfigureAwait(false);
            }
            catch(InvalidOperationException ex)
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, $"Expected result-set graph is in a format this harness cannot read: {ex.Message}");
            }

            try
            {
                expected = SparqlResultSetGraphReader.Read(resultGraph);
            }
            catch(FormatException ex)
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected result-set graph did not parse: {ex.Message}");
            }
        }
        else
        {
            try
            {
                expected = extension switch
                {
                    _ when string.Equals(extension, ".srj", StringComparison.OrdinalIgnoreCase) => SparqlResultsJsonReader.Read(expectedBytes),
                    _ when string.Equals(extension, ".srx", StringComparison.OrdinalIgnoreCase) => SparqlResultsXmlReader.Read(expectedBytes),
                    _ when string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase) => SparqlResultsTsvReader.Read(expectedBytes),
                    _ => throw new NotSupportedException($"Expected result format '{extension}' (a graph result) is not yet compared.")
                };
            }
            catch(NotSupportedException ex)
            {
                return new W3cOutcome(W3cOutcomeStatus.Skipped, ex.Message);
            }
            catch(FormatException ex)
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected SPARQL results fixture did not parse: {ex.Message}");
            }
        }

        bool equal = SparqlResultComparer.AreEquivalent(actual, expected, ordered);
        string note = actual.IsBoolean
            ? $"ASK actual={actual.Boolean}, expected={expected.Boolean}"
            : $"SELECT actual={actual.Solutions.Count} rows, expected={expected.Solutions.Count} rows, ordered={ordered}";

        return equal
            ? new W3cOutcome(W3cOutcomeStatus.Passed, note)
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"Result does not match expected: {note}");
    }

    /// <summary>
    /// Compares a SELECT result against an expected CSV fixture. CSV is lossy (no datatypes, implementation-specific
    /// blank-node labels) and has no faithful reader, so the actual result is re-serialized to CSV and matched as
    /// text: the columns are taken from the expected header (which also supplies the head variables for a
    /// <c>SELECT *</c>), line endings are normalised, and blank-node labels are collapsed to a placeholder. The CSV
    /// fixtures order their rows with <c>ORDER BY</c>, so the row order is determinate.
    /// </summary>
    /// <param name="actual">The engine's result set.</param>
    /// <param name="expectedBytes">The expected CSV fixture bytes.</param>
    /// <returns>The comparison outcome.</returns>
    private static W3cOutcome CompareCsv(SparqlResultSet actual, byte[] expectedBytes)
    {
        if(actual.IsBoolean)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, "A CSV result-format test expects a SELECT result, but the query produced an ASK boolean.");
        }

        string expectedText = System.Text.Encoding.UTF8.GetString(expectedBytes);
        SparqlResultSet projected = SparqlResultSet.ForSelect(CsvHeaderColumns(expectedText), actual.Solutions);

        string actualCsv = NormalizeCsv(SparqlResultsDelimitedWriter.WriteToString(projected, SparqlDelimitedResultsFormat.Csv));
        string expectedCsv = NormalizeCsv(expectedText);

        return string.Equals(actualCsv, expectedCsv, StringComparison.Ordinal)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, $"CSV matches ({actual.Solutions.Count} row(s)).")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"CSV result does not match expected.\n--- actual ---\n{actualCsv}\n--- expected ---\n{expectedCsv}");
    }

    /// <summary>Reads the column (variable) names from a CSV results fixture's header line.</summary>
    /// <param name="csv">The CSV fixture text.</param>
    /// <returns>The header column names, in order.</returns>
    private static List<Utf8String> CsvHeaderColumns(string csv)
    {
        int newline = csv.IndexOf('\n', StringComparison.Ordinal);
        string header = (newline >= 0 ? csv[..newline] : csv).TrimEnd('\r');

        List<Utf8String> columns = [];
        foreach(string field in header.Split(','))
        {
            columns.Add(Utf8Strings.From(field.Trim('"')));
        }

        return columns;
    }

    /// <summary>Normalises delimited result text for comparison: line endings to LF (no trailing newline) and every blank-node label collapsed to a placeholder.</summary>
    /// <param name="text">The delimited text.</param>
    /// <returns>The normalised text.</returns>
    private static string NormalizeCsv(string text)
    {
        string lf = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).TrimEnd('\n');

        return System.Text.RegularExpressions.Regex.Replace(lf, "_:[^,\n]+", "_:");
    }

    /// <summary>Builds the result set for a SELECT (head variables + solutions) query; ASK goes through the engine's short-circuiting entry instead.</summary>
    /// <param name="query">The parsed query.</param>
    /// <param name="solutions">The engine's solution sequence.</param>
    /// <returns>The result set.</returns>
    private static SparqlResultSet BuildResultSet(SparqlQuery query, IReadOnlyList<SparqlSolution> solutions)
    {
        List<Utf8String> variables = [];
        if(query.Form is SelectQuery select)
        {
            foreach(SelectProjection projection in select.Projections)
            {
                switch(projection)
                {
                    case SelectVariable variable:
                    {
                        variables.Add(variable.Variable.Name);
                        break;
                    }
                    case SelectExpressionAs expressionAs:
                    {
                        variables.Add(expressionAs.AsVariable.Name);
                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }
        }

        return SparqlResultSet.ForSelect(variables, solutions);
    }

    /// <summary>Builds the RDF-graph result of a CONSTRUCT (template instantiation) or DESCRIBE (resource description) query.</summary>
    /// <param name="engine">The engine over the data graph.</param>
    /// <param name="normalized">The normalized query (its form is CONSTRUCT or DESCRIBE).</param>
    /// <param name="cancellationToken">A token to cancel evaluation.</param>
    /// <returns>The result graph's quads.</returns>
    private static async Task<List<Quad>> BuildGraphResultAsync(SparqlQueryEngine engine, SparqlQuery normalized, CancellationToken cancellationToken)
    {
        switch(normalized.Form)
        {
            case ConstructQuery construct:
            {
                AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
                IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

                return SparqlGraphConstruction.Construct(construct.Template, solutions);
            }

            case DescribeQuery describe:
            {
                List<RdfTerm> resources = await ResolveDescribeResourcesAsync(engine, normalized, describe, cancellationToken).ConfigureAwait(false);

                return [.. await engine.DescribeAsync(resources, strategy: null, cancellationToken).ConfigureAwait(false)];
            }

            default:
            {
                return [];
            }
        }
    }

    /// <summary>Resolves a DESCRIBE query's target resources: its explicit IRIs, plus the values its describe variables (or all variables, for <c>DESCRIBE *</c>) bind in the WHERE solutions.</summary>
    /// <param name="engine">The engine over the data graph.</param>
    /// <param name="normalized">The normalized DESCRIBE query.</param>
    /// <param name="describe">The DESCRIBE form.</param>
    /// <param name="cancellationToken">A token to cancel evaluation.</param>
    /// <returns>The distinct resources to describe.</returns>
    private static async Task<List<RdfTerm>> ResolveDescribeResourcesAsync(SparqlQueryEngine engine, SparqlQuery normalized, DescribeQuery describe, CancellationToken cancellationToken)
    {
        List<RdfTerm> resources = [];
        HashSet<RdfTerm> seen = [];
        bool needsSolutions = describe.IsStar;
        foreach(DescribeTarget target in describe.Targets)
        {
            if(target is DescribeIri iri)
            {
                RdfTerm resource = new NamedNode(iri.Iri.Value);
                if(seen.Add(resource))
                {
                    resources.Add(resource);
                }
            }
            else
            {
                needsSolutions = true;
            }
        }

        if(needsSolutions)
        {
            AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
            IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);
            foreach(SparqlSolution solution in solutions)
            {
                foreach(SparqlBinding binding in solution.Bindings)
                {
                    if(IsDescribed(describe, binding.Variable) && seen.Add(binding.Value))
                    {
                        resources.Add(binding.Value);
                    }
                }
            }
        }

        return resources;
    }

    /// <summary>Returns whether a bound variable is described: every variable for <c>DESCRIBE *</c>, else only the named describe variables.</summary>
    /// <param name="describe">The DESCRIBE form.</param>
    /// <param name="variable">The variable to test.</param>
    /// <returns><see langword="true"/> when the variable's bindings are described.</returns>
    private static bool IsDescribed(DescribeQuery describe, SparqlVariable variable)
    {
        if(describe.IsStar)
        {
            return true;
        }

        foreach(DescribeTarget target in describe.Targets)
        {
            if(target is DescribeVariable describeVariable && describeVariable.Variable == variable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Loads an expected graph-result fixture (Turtle <c>.ttl</c> / N-Triples <c>.nt</c> / N-Quads <c>.nq</c>) into quads.</summary>
    /// <param name="path">The expected-result file path.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The expected graph's quads.</returns>
    /// <exception cref="InvalidOperationException">The fixture is in a format this harness cannot read, or did not parse.</exception>
    private static Task<List<Quad>> LoadGraphAsync(string path, CancellationToken cancellationToken)
    {
        return ReadQuadsAsync(path, cancellationToken);
    }

    /// <summary>
    /// Reads an RDF document into quads, dispatching on file extension: N-Quads (<c>.nq</c>), Turtle / N-Triples
    /// (<c>.ttl</c> / <c>.nt</c>), or TriG (<c>.trig</c> — Turtle plus named-graph blocks). A <c>.trig</c> / <c>.nq</c>
    /// file carries named graphs in each quad's <see cref="Quad.Graph"/>; <c>.ttl</c> / <c>.nt</c> yields default-graph
    /// quads only. RDF/XML (<c>.rdf</c>) and other formats are unreadable here.
    /// </summary>
    /// <param name="path">The document file path.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The document's quads.</returns>
    /// <exception cref="InvalidOperationException">The format is unreadable, or the document did not parse.</exception>
    private static async Task<List<Quad>> ReadQuadsAsync(string path, CancellationToken cancellationToken)
    {
        string extension = IoPath.GetExtension(path);
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        List<Quad> quads = [];

        if(string.Equals(extension, ".nq", StringComparison.OrdinalIgnoreCase))
        {
            await foreach(Quad quad in NQuadsReader.ReadAsync(bytes, pool: null, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            return quads;
        }

        //Turtle, its N-Triples subset, and TriG all parse with the Turtle reader; TriG mode additionally accepts the
        //named-graph blocks (GRAPH g { … } / g { … } / { … }) whose triples carry the block's graph.
        if(string.Equals(extension, ".ttl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".nt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase))
        {
            TurtleSyntax syntax = string.Equals(extension, ".trig", StringComparison.OrdinalIgnoreCase) ? TurtleSyntax.TriG : TurtleSyntax.Turtle;
            string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
            DiagnosticBag diagnostics = new();
            await foreach(Quad quad in TurtleReader.ReadAsync(bytes, syntax, diagnostics, pool: null, baseIri: baseIri, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            if(diagnostics.HasErrors)
            {
                throw new InvalidOperationException($"Graph '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}");
            }

            return quads;
        }

        if(string.Equals(extension, ".rdf", StringComparison.OrdinalIgnoreCase))
        {
            string baseIri = new Uri(IoPath.GetFullPath(path)).AbsoluteUri;
            DiagnosticBag diagnostics = new();
            quads.AddRange(RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri)));
            if(diagnostics.HasErrors)
            {
                throw new InvalidOperationException($"Graph '{path}' did not parse: {DescribeFirstError(diagnostics.Diagnostics)}");
            }

            return quads;
        }

        throw new InvalidOperationException($"Graph fixture format '{extension}' is not readable by this harness.");
    }

    /// <summary>The <see cref="GraphSourceResolver"/> the engine resolves an in-query <c>FROM</c> / <c>FROM NAMED</c> clause IRI through: it names a sibling file the harness loads and streams (the engine itself does the merge and dataset assembly).</summary>
    /// <param name="source">The dataset-clause IRI (already absolute).</param>
    /// <param name="accessContext">The access context (unused by this file-backed resolver).</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The graph's triples, streamed as they are read.</returns>
    private static async IAsyncEnumerable<DataTriple> ResolveGraphFromFileAsync(IriRef source, AccessContext? accessContext, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(DataTriple triple in await LoadDataAsync(GraphPath(source), cancellationToken).ConfigureAwait(false))
        {
            yield return triple;
        }
    }

    /// <summary>Resolves a dataset-clause IRI (already absolute) to its local file path.</summary>
    /// <param name="graph">The dataset-clause IRI.</param>
    /// <returns>The local file path.</returns>
    private static string GraphPath(IriRef graph)
    {
        return new Uri(graph.Value.ToString()).LocalPath;
    }

    /// <summary>Loads the data graph from a Turtle file into the engine's triple model; an absent path is the empty graph.</summary>
    /// <param name="dataPath">The data-graph file path, or <see langword="null"/> for an empty graph.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The data triples.</returns>
    /// <exception cref="InvalidOperationException">The data graph did not parse.</exception>
    private static async Task<List<DataTriple>> LoadDataAsync(string? dataPath, CancellationToken cancellationToken)
    {
        List<DataTriple> data = [];
        if(dataPath is null)
        {
            return data;
        }

        //The default graph is the quads with no graph component; a TriG/N-Quads file may also carry named graphs,
        //loaded separately by LoadNamedFromFileAsync.
        foreach(Quad quad in await ReadQuadsAsync(dataPath, cancellationToken).ConfigureAwait(false))
        {
            if(quad.Graph is null)
            {
                data.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
            }
        }

        return data;
    }

    /// <summary>Loads the named graphs carried inside a single TriG / N-Quads data file (the quads with a graph component), grouped by graph name; a Turtle file or absent path yields none.</summary>
    /// <param name="dataPath">The data-file path, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The named graphs as (graph-name, triples) pairs.</returns>
    private static async Task<List<(RdfTerm Name, IEnumerable<DataTriple> Triples)>> LoadNamedFromFileAsync(string? dataPath, CancellationToken cancellationToken)
    {
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = [];
        if(dataPath is null)
        {
            return named;
        }

        Dictionary<RdfTerm, List<DataTriple>> byGraph = [];
        foreach(Quad quad in await ReadQuadsAsync(dataPath, cancellationToken).ConfigureAwait(false))
        {
            if(quad.Graph is RdfTerm graph)
            {
                if(!byGraph.TryGetValue(graph, out List<DataTriple>? triples))
                {
                    triples = [];
                    byGraph[graph] = triples;
                }

                triples.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
            }
        }

        foreach((RdfTerm name, List<DataTriple> triples) in byGraph)
        {
            named.Add((name, triples));
        }

        return named;
    }

    /// <summary>Loads each <c>qt:graphData</c> named-graph file, keyed by its own file IRI — the IRI a query's <c>GRAPH &lt;file&gt;</c> (or projected graph variable) resolves to.</summary>
    /// <param name="graphDataPaths">The named-graph data files, or <see langword="null"/>/empty when the test declares none.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The named graphs as (graph-name, triples) pairs.</returns>
    /// <exception cref="InvalidOperationException">A named-graph file did not parse.</exception>
    private static async Task<List<(RdfTerm Name, IEnumerable<DataTriple> Triples)>> LoadNamedGraphsAsync(IReadOnlyList<string>? graphDataPaths, CancellationToken cancellationToken)
    {
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs = [];
        if(graphDataPaths is null)
        {
            return namedGraphs;
        }

        foreach(string path in graphDataPaths)
        {
            RdfTerm name = new NamedNode(Utf8Strings.From(new Uri(IoPath.GetFullPath(path)).AbsoluteUri));
            List<DataTriple> triples = await LoadDataAsync(path, cancellationToken).ConfigureAwait(false);
            namedGraphs.Add((name, triples));
        }

        return namedGraphs;
    }

    /// <summary>The entailment-regime IRIs the harness implements — the regimes whose answers the finite RDFS closure produces.</summary>
    private static string[] ImplementedEntailmentRegimes { get; } =
    [
        "http://www.w3.org/ns/entailment/RDF",
        "http://www.w3.org/ns/entailment/RDFS",
        "http://www.w3.org/ns/entailment/D",
    ];

    /// <summary>Returns whether any of the test's declared regimes is one the harness implements.</summary>
    /// <param name="regimes">The declared regime IRIs.</param>
    /// <returns><see langword="true"/> when at least one declared regime is implemented.</returns>
    private static bool HasImplementedRegime(IReadOnlyList<string> regimes)
    {
        foreach(string regime in regimes)
        {
            foreach(string implemented in ImplementedEntailmentRegimes)
            {
                if(string.Equals(regime, implemented, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether the IRI list contains the given IRI by ordinal comparison.</summary>
    /// <param name="iris">The IRI list.</param>
    /// <param name="iri">The IRI to find.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsIri(IReadOnlyList<string> iris, string iri)
    {
        foreach(string candidate in iris)
        {
            if(string.Equals(candidate, iri, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Expands a data graph to its finite RDFS-regime closure: the schema-driven rules plus the axiomatic
    /// rules (rdf1, axiomatic typing, rdfs6/8/10/12/13), which together produce the RDF/RDFS/D-regime answers
    /// the W3C entailment suite expects. With <paramref name="includeRlClosure"/> the OWL 2 RL/RDF rules run
    /// over the same encoding, producing the pr:RL-sanctioned OWL RDF-Based answers. A derived conclusion
    /// whose subject is a literal (a range typing of a literal object) is not an RDF triple and is dropped.
    /// </summary>
    /// <param name="data">The base data triples.</param>
    /// <param name="includeRlClosure">Whether to compose the OWL 2 RL rules closure over the RDFS closure.</param>
    /// <returns>The base triples plus the derived closure.</returns>
    private static List<DataTriple> ExpandRegimeClosure(List<DataTriple> data, bool includeRlClosure = false)
    {
        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = new(data.Count);
        foreach(DataTriple triple in data)
        {
            encoded.Add(EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(triple.Subject).Encoded,
                dictionary.GetOrAdd(triple.Predicate).Encoded,
                dictionary.GetOrAdd(triple.Object).Encoded));
        }

        RdfsVocabularyTerms terms = new(
            Type: dictionary.GetOrAdd(new NamedNode(Vocabulary.Rdf.Type)),
            SubClassOf: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SubClassOf)),
            SubPropertyOf: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.SubPropertyOf)),
            Domain: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Domain)),
            Range: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Range)),
            Property: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdf.Property)),
            Class: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Class)),
            Resource: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Resource)),
            ContainerMembershipProperty: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.ContainerMembershipProperty)),
            Member: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Member)),
            Datatype: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.Datatype)),
            Literal: dictionary.GetOrAdd(new NamedNode(RdfVocabulary.Rdfs.LiteralClass)));

        HashSet<EncodedTriple> closure = [.. encoded];
        closure.UnionWith(RdfsMaterialization.MaterializeToFixpoint(encoded, terms));

        if(includeRlClosure)
        {
            Lumoin.Veritas.Owl.Rl.OwlRlTerms rlTerms = new(dictionary);
            closure.UnionWith(Lumoin.Veritas.Owl.Rl.OwlRlClosure.Compute(closure, rlTerms).Derived);
        }

        List<DataTriple> expanded = [.. data];
        HashSet<EncodedTriple> baseSet = [.. encoded];
        foreach(EncodedTriple derived in closure)
        {
            if(baseSet.Contains(derived))
            {
                continue;
            }

            RdfTerm subject = dictionary.Resolve(derived.Subject);
            if(subject is Literal)
            {
                continue;
            }

            expanded.Add(new DataTriple(subject, dictionary.Resolve(derived.Predicate), dictionary.Resolve(derived.Object)));
        }

        return expanded;
    }

    /// <summary>Describes the first error-severity diagnostic for a failure message.</summary>
    /// <param name="diagnostics">The diagnostics to scan.</param>
    /// <returns>A one-line description of the first error, or a generic note when none is error-severity.</returns>
    private static string DescribeFirstError(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code}: {diagnostic.Message}";
            }
        }

        return "an unspecified parse error";
    }
}
