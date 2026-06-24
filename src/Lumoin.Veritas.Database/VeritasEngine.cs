using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The Veritas database: one engine you open over data and query. It composes
/// the storage, the SPARQL query engine, the OWL reasoner, and the compute lane
/// into a coherent whole, fully wired and usable by default. The command-line,
/// MCP, and HTTP surfaces are thin transports that open one of these and serve
/// over it.
/// </summary>
/// <remarks>
/// Reasoning, when wired (the default), materialises its entailments into the
/// default graph as part of opening, so queries answer over the entailed graph.
/// The engine owns its compute lane and drains it on disposal.
/// </remarks>
public sealed class VeritasEngine: IAsyncDisposable
{
    /// <summary>The composed query engine an immutable database answers queries through, or <see langword="null"/> for a mutable database (which derives one per query off its dataset snapshot).</summary>
    private readonly SparqlQueryEngine? queryEngine;

    /// <summary>The mutable dataset a mutable database commits into and derives its per-query engine from, or <see langword="null"/> for an immutable database.</summary>
    private readonly MutableSparqlDataset? mutableDataset;

    /// <summary>The compute lane an immutable database owns and drains on disposal, or <see langword="null"/> for a mutable database (whose per-query engines run lane-less).</summary>
    private readonly IComputeLane? computeLane;

    /// <summary>Constructs an immutable database over its composed engine and lane.</summary>
    /// <param name="queryEngine">The composed query engine.</param>
    /// <param name="computeLane">The owned compute lane.</param>
    private VeritasEngine(SparqlQueryEngine queryEngine, IComputeLane computeLane)
    {
        this.queryEngine = queryEngine;
        this.computeLane = computeLane;
    }

    /// <summary>Constructs a mutable database over a mutable dataset; it derives a fresh query engine off the dataset snapshot per query, so reads see the latest committed state.</summary>
    /// <param name="mutableDataset">The mutable dataset the database commits into and queries off snapshots of.</param>
    private VeritasEngine(MutableSparqlDataset mutableDataset)
    {
        this.mutableDataset = mutableDataset;
    }

    /// <summary>
    /// Opens a database over a default graph and any named graphs, composing
    /// the configured machinery. When reasoning is wired, its entailments
    /// materialise into the default graph as part of opening.
    /// </summary>
    /// <param name="defaultGraph">The default-graph triples.</param>
    /// <param name="namedGraphs">The named graphs, each its graph-name term paired with its triples.</param>
    /// <param name="options">The configuration; <c>null</c> uses the fully-wired <see cref="VeritasEngineOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> or <paramref name="namedGraphs"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenAsync(
        IEnumerable<DataTriple> defaultGraph,
        IReadOnlyList<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs,
        VeritasEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(namedGraphs);

        options ??= VeritasEngineOptions.Default;
        IComputeLane lane = ComputeLane.ForCurrentPlatform(options.Execution);
        try
        {
            ReasoningMaterializationDelegate? reasoning = options.Reasoning is { } configuration
                ? CreateReasoning(configuration)
                : null;

            SparqlQueryEngine engine = namedGraphs.Count == 0
                ? await SparqlQueryEngine.BuildAsync(defaultGraph, computeLane: lane, reasoning: reasoning, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await SparqlQueryEngine.BuildDatasetAsync(defaultGraph, namedGraphs, computeLane: lane, reasoning: reasoning, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new VeritasEngine(engine, lane);
        }
        catch
        {
            //Do not leak the lane when opening fails after it started.
            await lane.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>Opens a database over a single default graph and no named graphs.</summary>
    /// <param name="defaultGraph">The default-graph triples.</param>
    /// <param name="options">The configuration; <c>null</c> uses the fully-wired <see cref="VeritasEngineOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ValueTask<VeritasEngine> OpenAsync(
        IEnumerable<DataTriple> defaultGraph,
        VeritasEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return OpenAsync(defaultGraph, [], options, cancellationToken);
    }

    /// <summary>
    /// Opens a MUTABLE in-memory database over a default graph: SPARQL Update mutations commit into a mutable
    /// dataset over an in-memory journal, and queries answer off the latest committed snapshot (read-your-writes).
    /// This is the development, testing, and correctness engine; a durable journal backend wires in the same way.
    /// Reasoning is not materialised on the mutable engine (it serves the asserted graph); use
    /// <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> for a reasoned,
    /// query-only database.
    /// </summary>
    /// <param name="defaultGraph">The default-graph triples to seed the database with.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened mutable database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenMutableAsync(IEnumerable<DataTriple> defaultGraph, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);

        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in defaultGraph)
        {
            encoded.Add(new EncodedTriple(dictionary.GetOrAdd(triple.Subject), dictionary.GetOrAdd(triple.Predicate), dictionary.GetOrAdd(triple.Object)));
        }

        InMemoryDatasetJournal journal = new();
        MutableSparqlDataset dataset = await MutableSparqlDataset.CreateAsync(
            dictionary,
            encoded,
            namedGraphs: null,
            journalAppend: journal.AppendDelegate,
            journalRead: journal.ReadDelegate,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new VeritasEngine(dataset);
    }

    /// <summary>
    /// Evaluates a SPARQL query and returns its result — SELECT bindings or an
    /// ASK boolean — discriminated by the query's form, so a transport renders
    /// it without parsing the query to learn the form.
    /// </summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="baseIri">The base IRI relative references in the query resolve against; <c>null</c> for none.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The query result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL Update rather than a query.</exception>
    /// <exception cref="NotSupportedException"><paramref name="sparql"/> is a CONSTRUCT or DESCRIBE query, which this surface does not yet render.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<VeritasQueryResult> QueryAsync(string sparql, string? baseIri = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sparql);

        using Utf8StringPool pool = new();
        SparqlQuery query = Parse(sparql, baseIri, pool);
        AlgebraOperator algebra = SparqlTranslator.Translate((SparqlQuery)new SparqlNormalizer(pool).Normalize(query));
        SparqlQueryEngine engine = ResolveEngine();

        if(query.Form is AskQuery)
        {
            return VeritasQueryResult.ForAsk(await engine.EvaluateAskAsync(algebra, cancellationToken).ConfigureAwait(false));
        }

        if(query.Form is ConstructQuery or DescribeQuery)
        {
            throw new NotSupportedException("CONSTRUCT and DESCRIBE are not yet rendered by the database query surface; SELECT and ASK are supported.");
        }

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        return VeritasQueryResult.ForSelect(SparqlResultSet.ForSelect(SelectVariables(query), solutions));
    }

    /// <summary>Evaluates a SPARQL <c>ASK</c> query and returns whether a solution exists; a convenience over <see cref="QueryAsync"/>.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the query has at least one solution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, is a SPARQL Update, or is not an <c>ASK</c> query.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<bool> AskAsync(string sparql, CancellationToken cancellationToken = default)
    {
        VeritasQueryResult result = await QueryAsync(sparql, cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Boolean ?? throw new ArgumentException("The query is not an ASK query.", nameof(sparql));
    }

    /// <summary>
    /// Validates the served default graph against a SHACL shapes graph and
    /// returns the report. The shapes are loaded over the same dictionary the
    /// data uses, so a term denotes the same thing in both; when reasoning is
    /// wired the validation runs over the entailed graph the database serves.
    /// </summary>
    /// <param name="shapes">The SHACL shapes graph as triples.</param>
    /// <param name="cancellationToken">A token that aborts validation.</param>
    /// <returns>The validation report — whether the data conforms and, if not, the results.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shapes"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<ValidationReport> ValidateAsync(IEnumerable<DataTriple> shapes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        SparqlQueryEngine engine = ResolveEngine();
        TermDictionary dictionary = engine.Dictionary;
        HypertrieGraphStore shapesStore = await BuildShapesStore(shapes, dictionary, cancellationToken).ConfigureAwait(false);
        ShapeRegistry registry = await ShapeLoader
            .LoadAsync(shapesStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return await ShaclValidator
            .ValidateAsync(registry, engine.Dataset.DefaultGraph.AsMatchOps(), dictionary, ShaclBuiltInEvaluators.All, TimeProvider.System, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Drains and disposes the owned compute lane; a mutable database owns no lane and completes immediately.</summary>
    /// <returns>A task that completes when the lane has stopped.</returns>
    public ValueTask DisposeAsync()
    {
        return computeLane is null ? ValueTask.CompletedTask : computeLane.DisposeAsync();
    }

    /// <summary>Resolves the query engine for one read: a mutable database derives a fresh engine off its current dataset snapshot (read-your-writes), an immutable database returns its fixed engine.</summary>
    /// <returns>The query engine to evaluate against.</returns>
    private SparqlQueryEngine ResolveEngine()
    {
        return mutableDataset is not null
            ? new SparqlQueryEngine(mutableDataset.Snapshot(), mutableDataset.Dictionary)
            : queryEngine!;
    }

    /// <summary>Builds a store over the shapes triples, encoding them into the data's dictionary so shapes and data share term identity.</summary>
    /// <param name="shapes">The shapes triples.</param>
    /// <param name="dictionary">The shared dictionary.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The shapes store.</returns>
    private static async ValueTask<HypertrieGraphStore> BuildShapesStore(IEnumerable<DataTriple> shapes, TermDictionary dictionary, CancellationToken cancellationToken)
    {
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in shapes)
        {
            encoded.Add(new EncodedTriple(dictionary.GetOrAdd(triple.Subject), dictionary.GetOrAdd(triple.Predicate), dictionary.GetOrAdd(triple.Object)));
        }

        return await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses a query over the supplied pool, which must outlive the evaluation that reads the algebra's interned terms.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="baseIri">The base IRI relative references resolve against, or <c>null</c> for none.</param>
    /// <param name="pool">The pool the query parses into.</param>
    /// <returns>The parsed query.</returns>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL Update rather than a query.</exception>
    private static SparqlQuery Parse(string sparql, string? baseIri, Utf8StringPool pool)
    {
        Utf8String? parseBaseIri = baseIri is null ? null : Utf8String.FromString(baseIri);
        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(Utf8String.FromString(sparql).Memory, pool, parseBaseIri);
        if(parsed.HasErrors)
        {
            throw new ArgumentException($"The query did not parse: {DescribeFirstError(parsed.Diagnostics)}", nameof(sparql));
        }

        if(parsed.Tree is not SparqlQuery query)
        {
            throw new ArgumentException("The request is a SPARQL Update, not a query.", nameof(sparql));
        }

        return query;
    }

    /// <summary>Collects a <c>SELECT</c> query's projected variable names in head order — the result columns.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The projected variable names, empty for a non-<c>SELECT</c> query.</returns>
    private static List<Utf8String> SelectVariables(SparqlQuery query)
    {
        List<Utf8String> variables = [];
        if(query.Form is not SelectQuery select)
        {
            return variables;
        }

        foreach(SelectProjection projection in select.Projections)
        {
            Utf8String? name = projection switch
            {
                SelectVariable variable => variable.Variable.Name,
                SelectExpressionAs expressionAs => expressionAs.AsVariable.Name,
                _ => null,
            };

            if(name is { } variableName)
            {
                variables.Add(variableName);
            }
        }

        return variables;
    }

    /// <summary>Builds the engine's reasoning seam from a configuration: the SAT-backed description-logic engine behind a rendezvous, materialising its post-commit store.</summary>
    /// <param name="configuration">The reasoning configuration.</param>
    /// <returns>The materialisation delegate the engine applies at build time.</returns>
    private static ReasoningMaterializationDelegate CreateReasoning(ReasoningConfiguration configuration)
    {
        DescriptionLogicDelegate descriptionLogic = ReasoningEngines.SatBacked(configuration.Budget, configuration.SearchMode);
        ReasoningRendezvous rendezvous = new(configuration.Policy, descriptionLogic);

        return new ReasoningMaterialization(rendezvous).MaterializeAsync;
    }

    /// <summary>
    /// Materialises the post-commit reasoning store through a rendezvous, carrying the rendezvous as
    /// explicit state so the engine's <see cref="ReasoningMaterializationDelegate"/> is a bound method
    /// group rather than a lambda closing over the enclosing rendezvous.
    /// </summary>
    /// <param name="rendezvous">The reasoning rendezvous the materialisation runs through.</param>
    private sealed class ReasoningMaterialization(ReasoningRendezvous rendezvous)
    {
        /// <summary>The reasoning rendezvous the materialisation runs through.</summary>
        private ReasoningRendezvous Rendezvous { get; } = rendezvous;

        /// <summary>Materialises the reasoned store for a committed system-of-record store.</summary>
        /// <param name="store">The committed system-of-record store.</param>
        /// <param name="dictionary">The term dictionary the store is encoded against.</param>
        /// <param name="cancellationToken">A token to cancel materialisation.</param>
        /// <returns>The materialised reasoned store.</returns>
        public async ValueTask<HypertrieGraphStore> MaterializeAsync(HypertrieGraphStore store, TermDictionary dictionary, CancellationToken cancellationToken)
        {
            ReasoningResult result = await Rendezvous
                .MaterializeAsync(store, dictionary, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result.Store;
        }
    }

    /// <summary>Describes the first error diagnostic (code and message), for a parse-failure exception message.</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <returns>A one-line description of the first error, or a generic message when none carry detail.</returns>
    private static string DescribeFirstError(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} {diagnostic.Message}";
            }
        }

        return "unspecified parse error.";
    }
}
