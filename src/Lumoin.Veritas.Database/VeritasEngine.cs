using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
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
    /// <summary>The composed query engine the database answers queries through.</summary>
    private readonly SparqlQueryEngine queryEngine;

    /// <summary>The compute lane the engine owns; off-serve work runs on it and it drains on disposal.</summary>
    private readonly IComputeLane computeLane;

    /// <summary>Constructs the database over its composed engine and lane.</summary>
    /// <param name="queryEngine">The composed query engine.</param>
    /// <param name="computeLane">The owned compute lane.</param>
    private VeritasEngine(SparqlQueryEngine queryEngine, IComputeLane computeLane)
    {
        this.queryEngine = queryEngine;
        this.computeLane = computeLane;
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

    /// <summary>Evaluates a SPARQL <c>SELECT</c> query and returns its solutions.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The solutions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL Update rather than a query.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<IReadOnlyList<SparqlSolution>> QueryAsync(string sparql, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sparql);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(sparql, pool);

        return await queryEngine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates a SPARQL <c>ASK</c> query and returns whether a solution exists.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the query has at least one solution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL Update rather than a query.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<bool> AskAsync(string sparql, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sparql);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(sparql, pool);

        return await queryEngine.EvaluateAskAsync(algebra, cancellationToken).ConfigureAwait(false);
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

        TermDictionary dictionary = queryEngine.Dictionary;
        HypertrieGraphStore shapesStore = await BuildShapesStore(shapes, dictionary, cancellationToken).ConfigureAwait(false);
        ShapeRegistry registry = await ShapeLoader
            .LoadAsync(shapesStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return await ShaclValidator
            .ValidateAsync(registry, queryEngine.Dataset.DefaultGraph.AsMatchOps(), dictionary, ShaclBuiltInEvaluators.All, TimeProvider.System, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Drains and disposes the owned compute lane.</summary>
    /// <returns>A task that completes when the lane has stopped.</returns>
    public ValueTask DisposeAsync()
    {
        return computeLane.DisposeAsync();
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

    /// <summary>Parses, normalises, and translates a query into the executable algebra over the supplied pool, which must outlive the evaluation that reads the algebra's interned terms.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="pool">The pool the query parses into.</param>
    /// <returns>The translated algebra.</returns>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL Update rather than a query.</exception>
    private static AlgebraOperator Translate(string sparql, Utf8StringPool pool)
    {
        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(Utf8String.FromString(sparql).Memory, pool);
        if(parsed.HasErrors)
        {
            throw new ArgumentException($"The query did not parse: {DescribeFirstError(parsed.Diagnostics)}", nameof(sparql));
        }

        if(parsed.Tree is not SparqlQuery query)
        {
            throw new ArgumentException("The request is a SPARQL Update, not a query.", nameof(sparql));
        }

        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);

        return SparqlTranslator.Translate(normalized);
    }

    /// <summary>Builds the engine's reasoning seam from a configuration: the SAT-backed description-logic engine behind a rendezvous, materialising its post-commit store.</summary>
    /// <param name="configuration">The reasoning configuration.</param>
    /// <returns>The materialisation delegate the engine applies at build time.</returns>
    private static ReasoningMaterializationDelegate CreateReasoning(ReasoningConfiguration configuration)
    {
        DescriptionLogicDelegate descriptionLogic = ReasoningEngines.SatBacked(configuration.Budget, configuration.SearchMode);
        ReasoningRendezvous rendezvous = new(configuration.Policy, descriptionLogic);

        return async (store, dictionary, cancellationToken) =>
        {
            ReasoningResult result = await rendezvous
                .MaterializeAsync(store, dictionary, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result.Store;
        };
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
