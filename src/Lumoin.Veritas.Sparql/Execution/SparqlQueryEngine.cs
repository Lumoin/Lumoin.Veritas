using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution.Interception;
using Lumoin.Veritas.Sparql.Execution.Streaming;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Serialization;
using Lumoin.Veritas.Sparql.Translation;
using AstTriplePattern = Lumoin.Veritas.Sparql.Ast.TriplePattern;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;
using EncodedTriplePattern = Lumoin.Veritas.Core.Hypertrie.Query.TriplePattern;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Evaluates a translated SPARQL algebra tree (the output of <see cref="Translation.SparqlTranslator"/>) against a
/// hypertrie-backed data graph, producing decoded <see cref="SparqlSolution"/>s. This is the Milestone-C executor:
/// it bridges the SPARQL term world (<see cref="SparqlVariable"/> / <see cref="RdfTerm"/>) to the backend's encoded
/// world (<see cref="Variable"/> / <see cref="TermId"/>) and drives the worst-case-optimal join engine in
/// <see cref="HypertrieGraphStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Slice scope.</b> The executor covers basic graph patterns (a <see cref="Bgp"/>, the translator already merges
/// consecutive plain triples into one), the expression-free relational operators (<see cref="Join"/>,
/// <see cref="Union"/>, <see cref="Minus"/>), sub-<c>SELECT</c> (<see cref="ToMultiSet"/> / <see cref="ToList"/>),
/// the expression-gated operators (<see cref="Filter"/>, <c>BIND</c>/<see cref="Extend"/>, <c>OPTIONAL</c>'s
/// <see cref="LeftJoin"/> — via <see cref="SparqlExpressionEvaluator"/>), aggregation (<see cref="Group"/> +
/// <see cref="AggregateJoin"/>), the inline <c>VALUES</c> <see cref="Table"/>, and the projection/sequence
/// modifiers (<see cref="Project"/>, <see cref="Distinct"/>, <see cref="Reduced"/>, <see cref="OrderBy"/>,
/// <see cref="Slice"/>, and the <see cref="UnitTable"/> identity), property-path <see cref="Path"/> leaves
/// (via <see cref="PropertyPathEvaluator"/>; negated property sets excepted), named graphs (<c>GRAPH</c>),
/// <c>SERVICE</c> federation (through the engine's <c>SparqlClient</c>; a non-silent <c>SERVICE</c> without one
/// raises <see cref="NotSupportedException"/>), and <c>EXISTS</c>/<c>NOT EXISTS</c> through the per-site
/// compile-once plan machinery. The W3C SPARQL 1.2 specification governs the evaluation semantics.
/// </para>
/// <para>
/// <b>No recursion.</b> The algebra is evaluated over an explicit two-phase (expand/combine) work stack, mirroring
/// the translator's iterative discipline, so a deep algebra tree cannot overflow the stack. Results are
/// materialized per operator; the pull-based streaming mode beside this driver is selected by
/// <see cref="SparqlEnginePolicy.PreferStreamingOperators"/>.
/// </para>
/// <para>SPARQL 1.2 §18.5 / §18.6 [SPARQL Algebra evaluation].</para>
/// </remarks>
public sealed class SparqlQueryEngine
{
    /// <summary>A solution sequence holding the single empty solution — the result of the <see cref="UnitTable"/> identity.</summary>
    private static IReadOnlyList<SparqlSolution> SingleEmptySolution { get; } = [new SparqlSolution([])];

    /// <summary>The single-empty-solution sequence as a result table — the join identity returned by the <see cref="UnitTable"/> and by a suppressed <c>SERVICE</c>.</summary>
    private static SolutionTable SingleEmptySolutionTable { get; } = SolutionTable.FromRows(SingleEmptySolution);

    /// <summary>The <c>xsd:boolean</c> datatype node, for the constant an <c>EXISTS</c>/<c>NOT EXISTS</c> rewrites to.</summary>
    private static NamedNode XsdBooleanType { get; } = new(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#boolean"));

    /// <summary>The <c>true</c> boolean literal an <c>EXISTS</c> rewrites to once resolved.</summary>
    private static Literal BooleanTrue { get; } = new(Utf8Strings.From("true"), XsdBooleanType);

    /// <summary>The <c>false</c> boolean literal an <c>EXISTS</c> rewrites to once resolved.</summary>
    private static Literal BooleanFalse { get; } = new(Utf8Strings.From("false"), XsdBooleanType);

    /// <summary>The dataset the engine queries — its default graph and any named graphs; the composition layer reads it to validate the served data.</summary>
    public SparqlDataset Dataset { get; }

    /// <summary>The term dictionary that encoded the engine's graphs; shapes validated against the served data share it so a term denotes the same id in both.</summary>
    public TermDictionary Dictionary { get; }

    /// <summary>The engine's execution-strategy policy; the composition layer reads it to construct per-call sibling engines under the same strategy.</summary>
    public SparqlEnginePolicy EnginePolicy { get; }

    /// <summary>The shared BGP leaf machinery the engine's evaluators and the streaming pipeline pull through.</summary>
    internal BgpMachinery Machinery { get; }

    /// <summary>The engine's expression context — the streaming expression cursors evaluate conditions and binds through the same seams as the materialising row path.</summary>
    internal SparqlExpressionContext ExpressionContext { get; }

    /// <summary>The implicit timezone this engine's expression context normalizes naive temporal operands with — the value a composing host reads back to keep one timezone across the engine and its registered value indexes.</summary>
    public TimeSpan ImplicitTimezone => ExpressionContext.ImplicitTimezone;

    /// <summary>The value-layer datatype registry this engine's expression context consults for <c>=</c>/<c>!=</c> over literals of a registered datatype — the value a composing host reads back to keep one registry across the engine and the per-call engines it derives.</summary>
    public ValueDatatypeRegistry ValueDatatypes => ExpressionContext.ValueDatatypes;

    /// <summary>The extension-function registry this engine's expression context consults for IRI-named function calls — the value a composing host reads back to keep one registry across the engine and the per-call engines it derives.</summary>
    public SparqlFunctionRegistry ExtensionFunctions => ExpressionContext.ExtensionFunctions;

    /// <summary>
    /// Resolves an expression's <c>EXISTS</c>/<c>NOT EXISTS</c> occurrences for one streamed row — the
    /// pipeline cursors' entry into the same per-site compile-once machinery the materialising row path
    /// uses; the registry is the owning pipeline's, so a site inside a streamed plan compiles once for the
    /// pipeline's lifetime.
    /// </summary>
    /// <param name="expression">The expression to resolve.</param>
    /// <param name="solution">The streamed row.</param>
    /// <param name="graph">The active graph.</param>
    /// <param name="existsRegistry">The owning pipeline's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at the pipeline's position.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The expression with each <c>EXISTS</c>/<c>NOT EXISTS</c> replaced by its constant boolean result.</returns>
    internal async ValueTask<ExpressionNode> ResolveExistsForPipelineAsync(ExpressionNode expression, SparqlSolution solution, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        return await ResolveExistsAsync(expression, solution, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves the rewrite pipeline one evaluation runs: the per-call override, else the engine policy's, else the (empty) default.</summary>
    /// <param name="rewrites">The per-call override, or <see langword="null"/>.</param>
    /// <returns>The resolved pipeline; never <see langword="null"/>.</returns>
    private AlgebraRewritePipeline ResolveRewrites(AlgebraRewritePipeline? rewrites)
    {
        return rewrites ?? EnginePolicy.Rewrites ?? AlgebraRewritePipeline.Default;
    }

    /// <summary>Builds the pass-zero rule context an evaluation's rewrite run starts from; no statistics are wired yet — the slot fills when a cost-aware rule lands.</summary>
    /// <returns>The context.</returns>
    private AlgebraRewriteContext CreateRewriteContext()
    {
        return new AlgebraRewriteContext(EnginePolicy, Statistics: null, Pass: 0);
    }

    /// <summary>
    /// Creates an evaluation-scoped trace sink over the engine's construction-time handler, minting a fresh
    /// correlation id per evaluation (through the injected <see cref="Identifiers"/> source) when the handler
    /// is wired so concurrent runs stay distinguishable on a shared consumer stream; the untraced path mints
    /// nothing and every emit stays a no-op.
    /// </summary>
    /// <returns>The evaluation's trace sink.</returns>
    private SparqlExecutionTrace CreateEvaluationTrace()
    {
        return new SparqlExecutionTrace(ExecutionTrace, ExecutionTrace is null ? default : Identifiers(new IdentifierRequest(IdentifierPurpose.Correlation, default)), TimeProvider);
    }

    /// <summary>The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</summary>
    private SparqlClient? ServiceClient { get; }

    /// <summary>The access-control policy consulted per candidate triple of every local graph read; <see langword="null"/> allows every triple at zero cost.</summary>
    private AccessControlDelegate? AccessControl { get; }

    /// <summary>The opaque "who is asking" context handed to the access-control policy and forwarded to the <c>SERVICE</c>/<c>FROM</c>/<c>LOAD</c> IO seams; <see langword="null"/> when the evaluation carries no access context.</summary>
    private AccessContext? AccessContext { get; }

    /// <summary>The construction-time per-operator execution-trace handler (which evaluation strategy each operator took, with row shapes), or <see langword="null"/> for no tracing.</summary>
    private TraceHandler<SparqlExecutionTraceEvent>? ExecutionTrace { get; }

    /// <summary>The clock threaded into every backend source and trace sink this engine opens.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The injected identity source evaluation-scoped trace correlation ids are minted from (only when the construction-time trace handler is wired).</summary>
    private IdentifierDelegate Identifiers { get; }

    /// <summary>The query-time type-expansion seam, or <c>null</c> for no expansion.</summary>
    private TypeExpansionDelegate? TypeExpansion { get; }

    /// <summary>Constructs an engine over an already-built dataset (default graph + named graphs) and the term dictionary that encoded it.</summary>
    /// <param name="dataset">The dataset the query evaluates against; its graphs are all encoded by <paramref name="dictionary"/>.</param>
    /// <param name="dictionary">The term dictionary that encoded <paramref name="dataset"/>; query constants are resolved against it.</param>
    /// <param name="expressionContext">The seams (randomness, digests, the query timestamp) the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple of every local graph read; <see langword="null"/> allows every triple at zero cost.</param>
    /// <param name="accessContext">The opaque "who is asking" context handed to <paramref name="accessControl"/> and forwarded to the <c>SERVICE</c>/<c>FROM</c>/<c>LOAD</c> IO seams; <see langword="null"/> when the evaluation carries no access context. Required when <paramref name="accessControl"/> is non-<see langword="null"/>.</param>
    /// <param name="typeExpansion">The query-time type-expansion seam — Seam Q: a bound <c>rdf:type</c> pattern evaluates once per expansion class and the solutions union; <see langword="null"/> leaves patterns unexpanded.</param>
    /// <param name="executionTrace">The per-operator execution-trace handler (which evaluation strategy each operator took, with row shapes), or <see langword="null"/> for no tracing.</param>
    /// <param name="enginePolicy">The execution-strategy policy — whether eligible plans take the streaming operator pipeline; the default keeps the materialising executor everywhere.</param>
    /// <param name="timeProvider">The clock threaded into every backend source and trace sink this engine opens; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="identifiers">The identity source evaluation-scoped trace correlation ids are minted from; <see langword="null"/> uses <see cref="VeritasIdentifiers.System"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlQueryEngine(SparqlDataset dataset, TermDictionary dictionary, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null, TypeExpansionDelegate? typeExpansion = null, TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null, SparqlEnginePolicy enginePolicy = default, TimeProvider? timeProvider = null, IdentifierDelegate? identifiers = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(dictionary);

        Dataset = dataset;
        Dictionary = dictionary;
        ExpressionContext = expressionContext ?? SparqlExpressionContext.CreateDefault();
        ServiceClient = serviceClient;
        AccessControl = accessControl;
        AccessContext = accessContext;
        TypeExpansion = typeExpansion;
        ExecutionTrace = executionTrace;
        EnginePolicy = enginePolicy;
        TimeProvider = timeProvider ?? TimeProvider.System;
        Identifiers = identifiers ?? VeritasIdentifiers.System;
        Machinery = new BgpMachinery(dataset, dictionary, accessControl, accessContext, typeExpansion, TimeProvider);

        //The H1 composition guard: a registered value-index method that declares an implicit timezone
        //must agree with this engine's expression context, else a probe and a scan would order naive
        //temporal values differently — refused loudly here, never diverging silently at query time.
        Lumoin.Veritas.Core.Indexing.ValueIndexRegistry composedIndexes = dataset.DefaultGraphRendezvous.ValueIndexes;
        for(int i = 0; i < composedIndexes.Registrations.Count; i++)
        {
            if(composedIndexes.Registrations[i].Method.DeclaredImplicitTimezone is { } methodTimezone && methodTimezone != ExpressionContext.ImplicitTimezone)
            {
                throw new Lumoin.Veritas.Core.Indexing.ValueIndexRegistrationException(
                    $"A registered value-index method normalizes naive values with implicit timezone {methodTimezone} but the engine's expression context captures {ExpressionContext.ImplicitTimezone}; compose both from one timezone so a probe and a scan can never disagree.");
            }
        }
    }

    /// <summary>Constructs an engine over a single default graph and no named graphs — the common case where the query has no <c>GRAPH</c> form.</summary>
    /// <param name="store">The hypertrie store holding the default graph.</param>
    /// <param name="dictionary">The term dictionary that encoded <paramref name="store"/>; query constants are resolved against it.</param>
    /// <param name="expressionContext">The seams the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple; <see langword="null"/> allows every triple.</param>
    /// <param name="accessContext">The opaque "who is asking" context; <see langword="null"/> when none.</param>
    /// <param name="typeExpansion">The query-time type-expansion seam; <see langword="null"/> leaves patterns unexpanded.</param>
    /// <param name="executionTrace">The per-operator execution-trace handler, or <see langword="null"/> for no tracing.</param>
    /// <param name="enginePolicy">The execution-strategy policy — whether eligible plans take the streaming operator pipeline; the default keeps the materialising executor everywhere.</param>
    /// <param name="timeProvider">The clock threaded into every backend source and trace sink this engine opens; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="computeLane">An optional compute lane materialising the default graph's on-demand view off the serve path; <see langword="null"/> builds it inline.</param>
    /// <param name="initialColumnarView">A pre-built columnar view of <paramref name="store"/>'s triples (a warm-loaded durable sidecar) the engine serves from with no build, or <see langword="null"/> to build on demand.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="identifiers">The identity source evaluation-scoped trace correlation ids are minted from; <see langword="null"/> uses <see cref="VeritasIdentifiers.System"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlQueryEngine(HypertrieGraphStore store, TermDictionary dictionary, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null, TypeExpansionDelegate? typeExpansion = null, TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null, SparqlEnginePolicy enginePolicy = default, TimeProvider? timeProvider = null, IComputeLane? computeLane = null, ColumnarTripleIndex? initialColumnarView = null, ValueIndexRegistry? valueIndexes = null, IdentifierDelegate? identifiers = null)
        : this(SparqlDataset.FromDefaultGraph(store, computeLane, initialColumnarView, valueIndexes), dictionary, expressionContext, serviceClient, accessControl, accessContext, typeExpansion, executionTrace, enginePolicy, timeProvider, identifiers)
    {
    }

    /// <summary>
    /// Constructs a deferred-residency engine over a single default graph whose hypertrie is NOT built up front: it
    /// serves columnar-capable shapes from <paramref name="initialColumnarView"/> (a warm-loaded durable sidecar)
    /// and materialises the trie on demand only when a query genuinely needs it — an access-controlled query, a
    /// per-pattern self-join, or a cyclic shape without a self-index. The warm serve-from-disk start.
    /// </summary>
    /// <param name="deferredStore">The deferred build source the trie is materialised from on first demand; the engine's dataset takes it over.</param>
    /// <param name="dictionary">The term dictionary that encoded the deferred triples; query constants are resolved against it.</param>
    /// <param name="expressionContext">The seams the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple; an access-controlled query always materialises and evaluates on the trie (the columnar path has no per-candidate consultation point); <see langword="null"/> allows every triple.</param>
    /// <param name="accessContext">The opaque "who is asking" context; <see langword="null"/> when none.</param>
    /// <param name="typeExpansion">The query-time type-expansion seam; <see langword="null"/> leaves patterns unexpanded.</param>
    /// <param name="executionTrace">The per-operator execution-trace handler, or <see langword="null"/> for no tracing.</param>
    /// <param name="enginePolicy">The execution-strategy policy — whether eligible plans take the streaming operator pipeline; the default keeps the materialising executor everywhere.</param>
    /// <param name="timeProvider">The clock threaded into every backend source and trace sink this engine opens; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="computeLane">An optional compute lane materialising the default graph's on-demand view off the serve path; <see langword="null"/> builds it inline.</param>
    /// <param name="initialColumnarView">A pre-built columnar view of the deferred triples (the warm sidecar) the engine serves from with no build, or <see langword="null"/> — without one a deferred query materialises the trie.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="identifiers">The identity source evaluation-scoped trace correlation ids are minted from; <see langword="null"/> uses <see cref="VeritasIdentifiers.System"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlQueryEngine(DeferredTrieSource deferredStore, TermDictionary dictionary, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null, TypeExpansionDelegate? typeExpansion = null, TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null, SparqlEnginePolicy enginePolicy = default, TimeProvider? timeProvider = null, IComputeLane? computeLane = null, ColumnarTripleIndex? initialColumnarView = null, ValueIndexRegistry? valueIndexes = null, IdentifierDelegate? identifiers = null)
        : this(SparqlDataset.FromDeferredDefaultGraph(deferredStore, computeLane, initialColumnarView, valueIndexes), dictionary, expressionContext, serviceClient, accessControl, accessContext, typeExpansion, executionTrace, enginePolicy, timeProvider, identifiers)
    {
    }

    /// <summary>
    /// Builds an engine over a data graph given as in-memory triples: the terms are encoded into a fresh term
    /// dictionary and indexed into a new hypertrie store.
    /// </summary>
    /// <param name="triples">The data-graph triples.</param>
    /// <param name="expressionContext">The seams (randomness, digests, the query timestamp) the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple; <see langword="null"/> allows every triple.</param>
    /// <param name="accessContext">The opaque "who is asking" context; <see langword="null"/> when none.</param>
    /// <param name="enginePolicy">The execution-strategy policy — whether eligible plans take the streaming operator pipeline; the default keeps the materialising executor everywhere.</param>
    /// <param name="timeProvider">The clock threaded into every backend source and trace sink the engine opens; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="computeLane">An optional compute lane materialising the default graph's on-demand view off the serve path; <see langword="null"/> builds it inline.</param>
    /// <param name="reasoning">An optional materialisation seam run over the built store before the engine serves it, so the graph carries entailments; <see langword="null"/> serves simple-entailment results.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="identifiers">The identity source evaluation-scoped trace correlation ids are minted from; <see langword="null"/> uses <see cref="VeritasIdentifiers.System"/>.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>An engine ready to evaluate queries against the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <see langword="null"/>.</exception>
    public static async ValueTask<SparqlQueryEngine> BuildAsync(IEnumerable<DataTriple> triples, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null, TypeExpansionDelegate? typeExpansion = null, TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null, SparqlEnginePolicy enginePolicy = default, TimeProvider? timeProvider = null, IComputeLane? computeLane = null, ReasoningMaterializationDelegate? reasoning = null, ValueIndexRegistry? valueIndexes = null, IdentifierDelegate? identifiers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);

        TermDictionary dictionary = new();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(Encode(triples, dictionary), VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        if(reasoning is not null)
        {
            store = await reasoning(store, dictionary, cancellationToken).ConfigureAwait(false);
        }

        return new SparqlQueryEngine(store, dictionary, expressionContext, serviceClient, accessControl, accessContext, typeExpansion, executionTrace, enginePolicy, timeProvider, computeLane, initialColumnarView: null, valueIndexes, identifiers);
    }

    /// <summary>
    /// Builds an engine over a dataset: a default graph plus the given named graphs, every graph encoded into one
    /// shared term dictionary so a term id denotes the same term across graphs (a <c>GRAPH</c> form then selects
    /// which graph's store the enclosed pattern queries).
    /// </summary>
    /// <param name="defaultGraph">The default-graph triples.</param>
    /// <param name="namedGraphs">The named graphs, each its graph-name term paired with its triples.</param>
    /// <param name="expressionContext">The seams the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple; <see langword="null"/> allows every triple.</param>
    /// <param name="accessContext">The opaque "who is asking" context; <see langword="null"/> when none.</param>
    /// <param name="enginePolicy">The execution-strategy policy — whether eligible plans take the streaming operator pipeline; the default keeps the materialising executor everywhere.</param>
    /// <param name="timeProvider">The clock threaded into every backend source and trace sink the engine opens; <see langword="null"/> uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="computeLane">An optional compute lane materialising the default graph's on-demand view off the serve path; <see langword="null"/> builds it inline.</param>
    /// <param name="reasoning">An optional materialisation seam run over the built default graph before the engine serves it, so the default graph carries entailments; <see langword="null"/> serves simple-entailment results.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="identifiers">The identity source evaluation-scoped trace correlation ids are minted from; <see langword="null"/> uses <see cref="VeritasIdentifiers.System"/>.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>An engine ready to evaluate queries against the dataset.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static async ValueTask<SparqlQueryEngine> BuildDatasetAsync(
        IEnumerable<DataTriple> defaultGraph,
        IReadOnlyList<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs,
        SparqlExpressionContext? expressionContext = null,
        SparqlClient? serviceClient = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TypeExpansionDelegate? typeExpansion = null,
        TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null,
        SparqlEnginePolicy enginePolicy = default,
        TimeProvider? timeProvider = null,
        IComputeLane? computeLane = null,
        ReasoningMaterializationDelegate? reasoning = null,
        ValueIndexRegistry? valueIndexes = null,
        IdentifierDelegate? identifiers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(namedGraphs);

        //Every graph of the dataset encodes into one dictionary and
        //builds through one shared node arena: the per-store fixed
        //cost (pool slabs, intern table, pair-arena segments) is paid
        //once for the family, and identical subtrees across graphs
        //intern to one canonical instance.
        TermDictionary dictionary = new();
        List<IEnumerable<EncodedTriple>> encodedGraphs = new(namedGraphs.Count + 1)
        {
            Encode(defaultGraph, dictionary),
        };

        List<TermId> graphNames = new(namedGraphs.Count);
        foreach((RdfTerm name, IEnumerable<DataTriple> triples) in namedGraphs)
        {
            graphNames.Add(dictionary.GetOrAdd(name));
            encodedGraphs.Add(Encode(triples, dictionary));
        }

        IReadOnlyList<HypertrieGraphStore> stores = await HypertrieGraphStore.BuildSharedAsync(encodedGraphs, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        HypertrieGraphStore defaultStore = stores[0];
        if(reasoning is not null)
        {
            defaultStore = await reasoning(defaultStore, dictionary, cancellationToken).ConfigureAwait(false);
        }

        Dictionary<TermId, HypertrieGraphStore> named = new(namedGraphs.Count);
        for(int i = 0; i < graphNames.Count; i++)
        {
            named[graphNames[i]] = stores[i + 1];
        }

        return new SparqlQueryEngine(new SparqlDataset(defaultStore, named, computeLane, initialColumnarView: null, valueIndexes), dictionary, expressionContext, serviceClient, accessControl, accessContext, typeExpansion, executionTrace, enginePolicy, timeProvider, identifiers);
    }

    /// <summary>Encodes a graph's triples into <paramref name="dictionary"/>.</summary>
    /// <param name="triples">The graph's triples.</param>
    /// <param name="dictionary">The shared term dictionary every graph in the dataset encodes into.</param>
    /// <returns>The encoded triples.</returns>
    private static List<EncodedTriple> Encode(IEnumerable<DataTriple> triples, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in triples)
        {
            uint subject = dictionary.GetOrAdd(triple.Subject).Encoded;
            uint predicate = dictionary.GetOrAdd(triple.Predicate).Encoded;
            uint @object = dictionary.GetOrAdd(triple.Object).Encoded;
            encoded.Add(EncodedTriple.FromEncoded(subject, predicate, @object));
        }

        return encoded;
    }

    /// <summary>
    /// Whether the algebra has at least one solution — the <c>ASK</c> form. A plain BGP (the algebra an
    /// <c>ASK WHERE { triples }</c> translates to) answers by streaming its FIRST backend solution and
    /// stopping — the join drivers are lazy end to end, so no solution sequence is materialised and the
    /// early exit disposes them; an unencodable constant answers <see langword="false"/> outright (the BGP
    /// yields nothing). A BGP carrying per-solution rewrites (triple-term destructuring, self-join
    /// equalities, type expansions) and every other algebra shape evaluates normally and reports emptiness,
    /// so the fast path never changes an answer. Under
    /// <see cref="SparqlEnginePolicy.PreferStreamingOperators"/> the first-solution short-circuit widens to
    /// every streamable shape through the compiled cursor pipeline (non-streamable subtrees answer through a
    /// lazy materialise boundary), with the same answers either way.
    /// </summary>
    /// <param name="algebra">The translated algebra (from <see cref="Translation.SparqlTranslator.Translate"/>).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>Whether at least one solution exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public ValueTask<bool> EvaluateAskAsync(AlgebraOperator algebra, CancellationToken cancellationToken = default)
    {
        return EvaluateAskAsync(algebra, rewrites: null, cancellationToken);
    }

    /// <summary>
    /// Evaluates an <c>ASK</c> under a per-call rewrite pipeline override: <paramref name="rewrites"/>
    /// replaces the engine policy's pipeline for this evaluation only — <see cref="AlgebraRewritePipeline.Empty"/>
    /// force-disables, a custom pipeline force-enables exactly its rules. Semantics otherwise identical to
    /// <see cref="EvaluateAskAsync(AlgebraOperator, CancellationToken)"/>.
    /// </summary>
    /// <param name="algebra">The translated algebra (from <see cref="Translation.SparqlTranslator.Translate"/>).</param>
    /// <param name="rewrites">The per-call rewrite pipeline, or <see langword="null"/> to use the engine policy's.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>Whether at least one solution exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public async ValueTask<bool> EvaluateAskAsync(AlgebraOperator algebra, AlgebraRewritePipeline? rewrites, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);

        //The entry's evaluation-scoped trace and rewrite run happen ONCE, before any algebra inspection;
        //every internal fall-through below targets the rewrite-free core so the pass never re-applies.
        AlgebraRewritePipeline rewritePipeline = ResolveRewrites(rewrites);
        SparqlExecutionTrace trace = CreateEvaluationTrace();
        AlgebraRewriteContext context = CreateRewriteContext();
        AlgebraOperator rewritten = rewritePipeline.Rewrite(algebra, in context, trace);

        if(EnginePolicy.PreferStreamingOperators)
        {
            return await AnyStreamingAsync(rewritten, TermId.None, rewritePipeline, trace, cancellationToken).ConfigureAwait(false);
        }

        //The ASK first-solution short-circuit is an entry-strategy interception: it shares the registry's
        //trace vocabulary and its differential-isolation switch, while living here because it selects the
        //entry's whole route rather than intercepting one expand-phase node.
        if(!EnginePolicy.DisableInterceptions && rewritten is Bgp bgp)
        {
            BgpMachinery.EncodedBgp encoded = Machinery.EncodeBgp(bgp);
            if(!encoded.Encodable)
            {
                trace.EmitInterception(SparqlInterceptions.AskFirstSolutionName, SparqlExecutionOperator.Bgp, rows: 0);

                return false;
            }

            //The per-solution rewrites filter solutions after the join and type expansion changes the
            //pattern set; existence under those takes the full evaluation below.
            if(encoded.TripleTermMatches.Count == 0
                && encoded.SelfJoinEqualities.Count == 0
                && Machinery.ComputeTypeExpansions(encoded.Patterns).Count == 0)
            {
                //The default graph store is null only under deferred residency; it flows to the rendezvous as a null
                //pinned store (the rendezvous answers from its warm view or materialises the trie on demand).
                //DefaultGraphRendezvous is always present, so the per-row trie fallback is dead for the default graph.
                HypertrieGraphStore? graphStore = Dataset.Resolve(TermId.None);
                BasicGraphPattern query = new(encoded.Patterns, encoded.Registry);
                IAsyncEnumerable<Solution> stream = Machinery.OpenRowSource(query, graphStore, Dataset.DefaultGraphRendezvous, TermId.None, cancellationToken);

                await foreach(Solution _ in stream.ConfigureAwait(false))
                {
                    trace.EmitInterception(SparqlInterceptions.AskFirstSolutionName, SparqlExecutionOperator.Bgp, rows: 1);

                    return true;
                }

                trace.EmitInterception(SparqlInterceptions.AskFirstSolutionName, SparqlExecutionOperator.Bgp, rows: 0);

                return false;
            }
        }

        SolutionTable table = await EvaluateInGraphAsync(rewritten, TermId.None, trace, rewritePipeline, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, cancellationToken).ConfigureAwait(false);

        return table.Count > 0;
    }

    /// <summary>
    /// The streaming existence consumer: compiles the plan into a cursor pipeline and pulls ONCE — the
    /// first-row short-circuit over every streamable shape — disposing the pipeline in a <c>finally</c> so a
    /// throwing or cancelled pull still tears the chain down. A compile the cursor budget declines answers
    /// through the materialising path instead, so the route never changes an answer.
    /// </summary>
    /// <param name="algebra">The translated, already-rewritten algebra.</param>
    /// <param name="activeGraph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="rewrites">The entry's resolved rewrite pipeline, carried for nested EXISTS plan builds only — the plan itself is already rewritten.</param>
    /// <param name="trace">The entry's evaluation-scoped trace sink; the pipeline's completion walk and the materialising fallback both emit into it.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>Whether at least one solution exists.</returns>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    private async ValueTask<bool> AnyStreamingAsync(AlgebraOperator algebra, TermId activeGraph, AlgebraRewritePipeline rewrites, SparqlExecutionTrace trace, CancellationToken cancellationToken)
    {
        //A public evaluation entry: the fresh budget cell this whole evaluation (incl. nested EXISTS
        //pipelines and boundary re-entries) draws from.
        CursorBudget cursorBudget = new(StreamingPipeline.MaxCursorDepth);
        StreamingPipeline? pipeline = StreamingPipeline.TryCompile(this, Machinery, algebra, activeGraph, cursorBudget, existsDepth: 0, rewrites, trace);
        if(pipeline is null)
        {
            SolutionTable table = await EvaluateInGraphAsync(algebra, activeGraph, trace, rewrites, cursorBudget, existsDepth: 0, cancellationToken).ConfigureAwait(false);

            return table.Count > 0;
        }

        try
        {
            return await pipeline.Root.MoveNextAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Evaluates a materialise-boundary subtree for the streaming pipeline through the incumbent driver —
    /// semantics identical to the enclosing evaluation, tracing through the engine's construction-time
    /// handler, and drawing any nested pipelines from the SAME budget cell so the boundary re-entry never
    /// compiles at a fresh constant.
    /// </summary>
    /// <param name="subtree">The non-streamable algebra subtree.</param>
    /// <param name="activeGraph">The active graph the subtree evaluates under.</param>
    /// <param name="trace">The spawning evaluation's trace sink, or <see langword="null"/> when the pipeline compiled without one (a per-binding EXISTS probe); the re-entry then traces through a sink of its own.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell, carried from the boundary's position.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth at the boundary's position.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The subtree's materialised solution table.</returns>
    /// <exception cref="NotSupportedException">The subtree uses an operator this slice does not yet execute.</exception>
    internal async ValueTask<SolutionTable> EvaluateBoundaryAsync(AlgebraOperator subtree, TermId activeGraph, SparqlExecutionTrace? trace, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        //The boundary subtree is part of an already-rewritten plan, so the re-entry never re-applies the
        //pass; the engine-policy pipeline rides along only for nested EXISTS plan builds. The spawning
        //evaluation's sink keeps the boundary's events in that evaluation's correlation and sequence stream.
        return await EvaluateInGraphAsync(subtree, activeGraph, trace ?? CreateEvaluationTrace(), ResolveRewrites(null), cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates an algebra tree to its solution sequence. A <c>SELECT</c> consumes the sequence directly; an
    /// <c>ASK</c> is satisfied when the sequence is non-empty — prefer <see cref="EvaluateAskAsync"/>, which
    /// short-circuits at the first solution instead of materialising the sequence.
    /// </summary>
    /// <param name="algebra">The translated algebra (from <see cref="Translation.SparqlTranslator.Translate"/>).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public ValueTask<IReadOnlyList<SparqlSolution>> EvaluateAsync(AlgebraOperator algebra, CancellationToken cancellationToken = default)
    {
        return EvaluateAsync(algebra, rewrites: null, cancellationToken);
    }

    /// <summary>
    /// Evaluates an algebra tree under a per-call rewrite pipeline override: <paramref name="rewrites"/>
    /// replaces the engine policy's pipeline for this evaluation only — <see cref="AlgebraRewritePipeline.Empty"/>
    /// force-disables, a custom pipeline force-enables exactly its rules. Semantics otherwise identical to
    /// <see cref="EvaluateAsync(AlgebraOperator, CancellationToken)"/>.
    /// </summary>
    /// <param name="algebra">The translated algebra (from <see cref="Translation.SparqlTranslator.Translate"/>).</param>
    /// <param name="rewrites">The per-call rewrite pipeline, or <see langword="null"/> to use the engine policy's.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateAsync(AlgebraOperator algebra, AlgebraRewritePipeline? rewrites, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);

        //No per-call handler: trace through the engine's construction-time handler (if any). The trace is
        //constructed BEFORE the rewrite so rule applications share the evaluation's sequence stream.
        AlgebraRewritePipeline rewritePipeline = ResolveRewrites(rewrites);
        SparqlExecutionTrace trace = CreateEvaluationTrace();
        AlgebraRewriteContext context = CreateRewriteContext();
        AlgebraOperator rewritten = rewritePipeline.Rewrite(algebra, in context, trace);
        SolutionTable result = await EvaluateInGraphAsync(rewritten, TermId.None, trace, rewritePipeline, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, cancellationToken).ConfigureAwait(false);

        return result.AsRows();
    }

    /// <summary>
    /// Evaluates an algebra tree to its solution sequence while emitting this run's per-operator execution trace to
    /// a <em>per-call</em> handler — the editor "visualize this query" surface: one engine, one query run, its own
    /// collector, no rebuild. The handler overrides any construction-time handler for this evaluation only.
    /// </summary>
    /// <param name="algebra">The translated algebra.</param>
    /// <param name="executionTrace">The execution-trace handler for this evaluation (which strategy each operator took, with row shapes).</param>
    /// <param name="correlationId">The correlation id stamped on this evaluation's trace events; <see cref="Guid.Empty"/> leaves them ordered by sequence only. Pass a per-run id to group the events (and to join them with the Core query trace).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> or <paramref name="executionTrace"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateAsync(AlgebraOperator algebra, TraceHandler<SparqlExecutionTraceEvent> executionTrace, Guid correlationId = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);
        ArgumentNullException.ThrowIfNull(executionTrace);

        //The single decode boundary: a fully-columnar pipeline carried encoded term ids all the way here, and only
        //the surviving result rows are materialized; a pipeline that hit a row-only operator was already row-backed.
        AlgebraRewritePipeline rewritePipeline = ResolveRewrites(null);
        SparqlExecutionTrace trace = new(executionTrace, correlationId, TimeProvider);
        AlgebraRewriteContext context = CreateRewriteContext();
        AlgebraOperator rewritten = rewritePipeline.Rewrite(algebra, in context, trace);
        SolutionTable result = await EvaluateInGraphAsync(rewritten, TermId.None, trace, rewritePipeline, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, cancellationToken).ConfigureAwait(false);

        return result.AsRows();
    }

    /// <summary>
    /// Streams a query's solutions one at a time for incremental consumption (server-sent events, paging, large
    /// scans). A streamable shape — a bare basic graph pattern, or a <see cref="Project"/> and/or <see cref="Slice"/>
    /// wrapping one — is produced lazily straight from the BGP evaluator, decoded per solution, with <c>LIMIT</c>
    /// terminating the stream early. Any other shape (a blocking operator — <c>ORDER BY</c>, <c>DISTINCT</c>,
    /// <c>GROUP BY</c>/aggregate, joins, <c>OPTIONAL</c>, <c>UNION</c>, <c>FILTER</c> — or a BGP needing per-solution
    /// rewrites: triple-term destructuring, self-join equality, <c>rdf:type</c> expansion) falls back to the
    /// materialized <see cref="EvaluateAsync(AlgebraOperator, CancellationToken)"/> and yields its rows, so every
    /// query is answered correctly; only the common shape is truly incremental. The result set is identical either
    /// way.
    /// </summary>
    /// <param name="algebra">The translated algebra.</param>
    /// <param name="cancellationToken">A token that aborts streaming.</param>
    /// <returns>The solutions, yielded as they are produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    public IAsyncEnumerable<SparqlSolution> EvaluateStreamingAsync(AlgebraOperator algebra, CancellationToken cancellationToken = default)
    {
        return EvaluateStreamingAsync(algebra, rewrites: null, cancellationToken);
    }

    /// <summary>
    /// Streams a query's solutions under a per-call rewrite pipeline override: <paramref name="rewrites"/>
    /// replaces the engine policy's pipeline for this evaluation only — <see cref="AlgebraRewritePipeline.Empty"/>
    /// force-disables, a custom pipeline force-enables exactly its rules. Semantics otherwise identical to
    /// <see cref="EvaluateStreamingAsync(AlgebraOperator, CancellationToken)"/>.
    /// </summary>
    /// <param name="algebra">The translated algebra.</param>
    /// <param name="rewrites">The per-call rewrite pipeline, or <see langword="null"/> to use the engine policy's.</param>
    /// <param name="cancellationToken">A token that aborts streaming.</param>
    /// <returns>The solutions, yielded as they are produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    public async IAsyncEnumerable<SparqlSolution> EvaluateStreamingAsync(AlgebraOperator algebra, AlgebraRewritePipeline? rewrites, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);

        //The entry's evaluation-scoped trace and rewrite run happen ONCE, before any algebra inspection.
        AlgebraRewritePipeline rewritePipeline = ResolveRewrites(rewrites);
        SparqlExecutionTrace trace = CreateEvaluationTrace();
        AlgebraRewriteContext context = CreateRewriteContext();
        AlgebraOperator rewritten = rewritePipeline.Rewrite(algebra, in context, trace);

        //On-mode: compile the WHOLE plan and stream it up to its materialise-boundary frontier — the
        //general form subsuming the Slice/Project peel below (which remains the off-mode path). The result
        //set is identical either way (the order gate keeps windowed plans on the boundary where a moved
        //window could otherwise differ); the guard runs up front so an unsupported operator refuses BEFORE
        //any row is yielded, exactly as the materialised fallback does.
        if(EnginePolicy.PreferStreamingOperators)
        {
            GuardSupported(rewritten);
            CursorBudget streamingBudget = new(StreamingPipeline.MaxCursorDepth);
            StreamingPipeline? pipeline = StreamingPipeline.TryCompile(this, Machinery, rewritten, TermId.None, streamingBudget, existsDepth: 0, rewritePipeline, trace);
            if(pipeline is not null)
            {
                try
                {
                    while(await pipeline.Root.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        yield return pipeline.Root.Current;
                    }
                }
                finally
                {
                    await pipeline.DisposeAsync().ConfigureAwait(false);
                }

                yield break;
            }
        }

        //Peel an optional Slice and Project (each at most once, in either order) wrapping the BGP leaf.
        AlgebraOperator inner = rewritten;
        int offset = 0;
        int? limit = null;
        IReadOnlyList<SparqlVariable>? projection = null;
        bool peeledSlice = false;
        bool peeledProject = false;
        while(true)
        {
            if(!peeledSlice && inner is Slice slice)
            {
                peeledSlice = true;
                offset = slice.Offset;
                limit = slice.Limit;
                inner = slice.Input;
            }
            else if(!peeledProject && inner is Project project)
            {
                peeledProject = true;
                projection = project.Variables;
                inner = project.Input;
            }
            else
            {
                break;
            }
        }

        BgpMachinery.EncodedBgp? encoded = inner is Bgp bgp ? Machinery.EncodeBgp(bgp) : null;

        //The default graph store is null only under deferred residency; the BGP still streams through the
        //(always-present) rendezvous, which serves the warm view or materialises the trie on demand.
        HypertrieGraphStore? graphStore = Dataset.Resolve(TermId.None);
        bool streamable = encoded is not null
            && encoded.Encodable
            && encoded.TripleTermMatches.Count == 0
            && encoded.SelfJoinEqualities.Count == 0
            && Machinery.ComputeTypeExpansions(encoded.Patterns).Count == 0;

        if(!streamable)
        {
            //Not stream-eligible — answer through the materialising CORE with this entry's trace and rewrite
            //frame: the plan is already rewritten, so re-entering a public entry here would re-apply the pass.
            SolutionTable table = await EvaluateInGraphAsync(rewritten, TermId.None, trace, rewritePipeline, new CursorBudget(StreamingPipeline.MaxCursorDepth), existsDepth: 0, cancellationToken).ConfigureAwait(false);
            foreach(SparqlSolution row in table.AsRows())
            {
                yield return row;
            }

            yield break;
        }

        BgpMachinery.EncodedBgp encodedBgp = encoded!;
        HypertrieGraphStore? store = graphStore;
        BasicGraphPattern query = new(encodedBgp.Patterns, encodedBgp.Registry);
        IAsyncEnumerable<Solution> backend = Machinery.OpenRowSource(query, store, Dataset.DefaultGraphRendezvous, TermId.None, cancellationToken);

        HashSet<SparqlVariable>? keep = projection is null ? null : [.. projection];
        int toSkip = offset;
        int remaining = limit ?? int.MaxValue;
        await foreach(Solution solution in backend.ConfigureAwait(false))
        {
            if(remaining <= 0)
            {
                yield break;
            }

            if(toSkip > 0)
            {
                toSkip--;

                continue;
            }

            yield return Machinery.DecodeStreamedSolution(solution, encodedBgp.ToSparql, keep);
            remaining--;
        }
    }

    /// <summary>
    /// Returns the engine scoped to a query's <c>FROM</c>/<c>FROM NAMED</c> dataset clause (SPARQL 1.2 §13.2). An
    /// empty clause leaves this engine unchanged; a non-empty clause OVERRIDES the default dataset, so the returned
    /// engine evaluates against the effective dataset built from the clause graphs — the default graph is the RDF
    /// merge of the <c>FROM</c> graphs, and each <c>FROM NAMED &lt;iri&gt;</c> is a named graph keyed by that IRI
    /// (so a <c>GRAPH &lt;iri&gt;</c> form resolves to it). Every clause IRI is resolved to its triples through
    /// <paramref name="resolver"/> (the same seam <c>LOAD</c> uses); the resolver decides whether an IRI names a
    /// local graph or a remote document. The returned engine inherits this one's expression context and service
    /// client. Once obtained, evaluate against it with <see cref="EvaluateAsync(AlgebraOperator, CancellationToken)"/>
    /// (or <see cref="DescribeAsync"/>) exactly as for an engine with no dataset clause.
    /// </summary>
    /// <param name="datasetClause">The query's dataset clause.</param>
    /// <param name="resolver">The resolver each clause IRI is fetched through; required when the clause is non-empty.</param>
    /// <param name="cancellationToken">A token that aborts resolution and the effective-dataset build.</param>
    /// <returns>This engine when the clause is empty, otherwise an engine over the effective dataset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="datasetClause"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The clause is non-empty but no <paramref name="resolver"/> was supplied.</exception>
    public async ValueTask<SparqlQueryEngine> WithDatasetAsync(DatasetClause datasetClause, GraphSourceResolver? resolver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datasetClause);

        if(datasetClause.DefaultGraphs.Count == 0 && datasetClause.NamedGraphs.Count == 0)
        {
            return this;
        }

        if(resolver is null)
        {
            throw new NotSupportedException("A query with a FROM / FROM NAMED clause needs a GraphSourceResolver to resolve its dataset graphs, but none was supplied.");
        }

        //The default graph is the RDF merge of all FROM graphs (concatenation into one default-graph store); each
        //FROM NAMED graph is a named graph keyed by its (absolute) IRI. Each clause IRI streams through the resolver
        //and drains into the per-graph triple list the effective dataset is built from.
        List<DataTriple> defaultGraph = [];
        foreach(IriRef from in datasetClause.DefaultGraphs)
        {
            await foreach(DataTriple triple in resolver(from, AccessContext, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                defaultGraph.Add(triple);
            }
        }

        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = new(datasetClause.NamedGraphs.Count);
        foreach(IriRef fromNamed in datasetClause.NamedGraphs)
        {
            List<DataTriple> triples = [];
            await foreach(DataTriple triple in resolver(fromNamed, AccessContext, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                triples.Add(triple);
            }

            named.Add((new NamedNode(fromNamed.Value), triples));
        }

        return await BuildDatasetAsync(defaultGraph, named, ExpressionContext, ServiceClient, AccessControl, AccessContext, TypeExpansion, ExecutionTrace, EnginePolicy, TimeProvider, identifiers: Identifiers, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Describes the given resources as an RDF graph (SPARQL 1.2 §16.4, the <c>DESCRIBE</c> result). The exact
    /// triples are implementation-defined; <paramref name="strategy"/> chooses the description algorithm per call,
    /// defaulting to the Concise Bounded Description (<see cref="SparqlDescribe.ConciseBoundedDescription"/>).
    /// </summary>
    /// <param name="resources">The resources to describe (a <c>DESCRIBE &lt;iri&gt;</c> IRI, or the values a describe variable bound); resources absent from the data graph contribute nothing.</param>
    /// <param name="strategy">The description strategy, or <see langword="null"/> for the default CBD.</param>
    /// <param name="cancellationToken">A token that aborts the description.</param>
    /// <returns>The distinct describing triples, as default-graph quads.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    public async ValueTask<IReadOnlyList<Quad>> DescribeAsync(IReadOnlyList<RdfTerm> resources, DescribeStrategy? strategy = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);

        DescribeStrategy describe = strategy ?? SparqlDescribe.ConciseBoundedDescription;

        //DESCRIBE enumerates the default graph's neighbourhood, which needs the trie's Match ops; under deferred
        //residency that materialises the trie on demand.
        GraphMatchOps ops = Machinery.GuardMatchOps((await Dataset.RequireDefaultGraphAsync(cancellationToken).ConfigureAwait(false)).AsMatchOps());

        List<TermId> ids = new(resources.Count);
        foreach(RdfTerm resource in resources)
        {
            TermId id = Dictionary.GetIdOrDefault(resource);
            if(!id.IsNone)
            {
                ids.Add(id);
            }
        }

        List<Quad> result = [];
        HashSet<Quad> seen = [];
        await foreach(EncodedTriple triple in describe(ids, ops, Dictionary, cancellationToken).ConfigureAwait(false))
        {
            Quad quad = new(Dictionary.Resolve(triple.Subject), (NamedNode)Dictionary.Resolve(triple.Predicate), Dictionary.Resolve(triple.Object));
            if(seen.Add(quad))
            {
                result.Add(quad);
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates an algebra tree against a designated active graph: <see cref="TermId.None"/> for the dataset's
    /// default graph, or a named graph's term id inside a <c>GRAPH</c> form. <c>EXISTS</c> re-enters here
    /// with the enclosing active graph so a sub-pattern queries the same graph as its surrounding pattern.
    /// </summary>
    /// <param name="algebra">The algebra to evaluate — already rewritten by its public entry; the core never applies the pipeline.</param>
    /// <param name="activeGraph">The active graph the basic patterns query, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="trace">The evaluation's trace sink, constructed at the public entry BEFORE the rewrite pass so rewrite and operator events share one sequence stream.</param>
    /// <param name="rewrites">The evaluation's resolved rewrite pipeline, carried on the EXISTS registry for per-site plan builds — the tree itself is already rewritten.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell (public entries create it fresh; every re-entry channel threads it, so nested pipelines draw from one pool). Inert in off-mode.</param>
    /// <param name="existsDepth">The <c>EXISTS</c> re-entry depth this evaluation runs at; the defensive nesting check reads it.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    private async ValueTask<SolutionTable> EvaluateInGraphAsync(AlgebraOperator algebra, TermId activeGraph, SparqlExecutionTrace trace, AlgebraRewritePipeline rewrites, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        GuardSupported(algebra);

        //Two-phase expand/combine over an explicit stack (no recursion): each operator's children are evaluated
        //and recorded before the operator combines them. Results are keyed by (operator reference, active graph) —
        //a parsed algebra is a tree (so each operator instance appears once per graph), and a GRAPH form
        //re-evaluates the SAME sub-tree under a different active graph, so the graph is part of the key.
        Dictionary<ResultKey, SolutionTable> results = new(ResultKeyComparer.Instance);

        //One computed-term overlay per evaluation: a columnar Extend encodes its results through it, and every
        //table descended from that Extend carries the same instance, so two sibling Extends never collide on an
        //overlay id and the decode at the boundary is unambiguous. Created eagerly (it is an empty pair of
        //collections) and used only when a columnar Extend runs.
        ComputedTermOverlay overlay = new(Dictionary);

        //Per-evaluation leaf row caps: a LIMIT whose operator chain down to a BGP leaf preserves row counts
        //lets the leaf stop draining at offset+limit rows; the slice still trims the exact window, so answers
        //are unchanged — only the surplus is never drained. Allocated on the first qualifying slice.
        Dictionary<ResultKey, int>? leafRowCaps = null;

        //One EXISTS plan registry per driver invocation (sub-evaluations re-entering here create their own;
        //entries never alias across registries): each EXISTS site compiles once and is shared by all outer
        //rows. It carries the evaluation's rewrite frame so plan builds compile rewritten inner algebra and
        //emit into this evaluation's trace. Disposed in the finally — deterministic, on the evaluating thread.
        ExistsRegistry existsRegistry = new(rewrites, trace);
        try
        {
            Stack<(AlgebraOperator Node, TermId Graph, bool Combine)> work = new();
            work.Push((algebra, activeGraph, Combine: false));

            while(work.Count > 0)
            {
                (AlgebraOperator node, TermId graph, bool combine) = work.Pop();
                if(combine)
                {
                    //A GRAPH form gathers its child's per-graph results; the expression-gated operators may carry
                    //EXISTS/NOT EXISTS, whose evaluation re-enters the engine (async), so they take the async path;
                    //every other operator combines synchronously.
                    results[new ResultKey(node, graph)] = node switch
                    {
                        Graph graphNode => CombineGraph(graphNode, graph, results),
                        Join boundJoin when IsServiceBoundJoin(boundJoin) => await CombineServiceBoundJoinAsync(boundJoin, graph, results, cancellationToken).ConfigureAwait(false),
                        Filter or Extend or LeftJoin => await CombineExpressionOperatorAsync(node, graph, results, overlay, trace, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false),
                        _ => Combine(node, graph, results, ExpressionContext, trace)
                    };

                    continue;
                }

                //The shipped fast paths are the interception registry's ordered entries, consulted per
                //expand-phase operator: an ANSWERED entry records the node's table and skips the whole
                //subtree; an ANNOTATED one records a leaf row cap and the node still expands normally (the
                //cap only bounds the future leaf drain — an ancestor pops before its descendants, so the
                //cap is recorded before the leaf evaluates); declines fall through to normal expansion. An
                //entry that fires ends the consultation, which preserves the shipped preference order
                //(the leaf cap over the streaming window). The policy switch exists for differential
                //isolation — the entries are ON by default.
                if(!EnginePolicy.DisableInterceptions)
                {
                    SparqlInterceptionSite site = new(this, graph, trace, rewrites, cursorBudget, existsDepth);
                    IReadOnlyList<SparqlInterceptionEntry> interceptions = SparqlInterceptionRegistry.Default.Entries;
                    bool answered = false;
                    for(int i = 0; i < interceptions.Count; i++)
                    {
                        SparqlInterceptionEntry entry = interceptions[i];
                        SparqlInterceptionOutcome outcome = await entry.Interception(node, site, cancellationToken).ConfigureAwait(false);
                        if(outcome.Application == SparqlInterceptionApplication.Answered)
                        {
                            results[new ResultKey(node, graph)] = outcome.Table!;
                            trace.EmitInterception(entry.Name, SparqlExecutionOperators.Of(node), outcome.Table!.Count);
                            answered = true;

                            break;
                        }

                        if(outcome.Application == SparqlInterceptionApplication.Annotated)
                        {
                            leafRowCaps ??= new Dictionary<ResultKey, int>(ResultKeyComparer.Instance);
                            leafRowCaps[new ResultKey(outcome.AnnotationTarget!, graph)] = outcome.AnnotationCap;
                            trace.EmitInterception(entry.Name, SparqlExecutionOperators.Of(node), rows: -1);

                            break;
                        }
                    }

                    if(answered)
                    {
                        continue;
                    }
                }

                //A GRAPH form re-roots its child under the named graph(s) it designates, never the enclosing graph.
                if(node is Graph graphOperator)
                {
                    ExpandGraph(graphOperator, graph, work);

                    continue;
                }

                IReadOnlyList<AlgebraOperator> children = node.Children;
                if(children.Count == 0)
                {
                    int maxRows = int.MaxValue;
                    if(leafRowCaps is not null && leafRowCaps.TryGetValue(new ResultKey(node, graph), out int cappedRows))
                    {
                        maxRows = cappedRows;
                    }

                    SolutionTable leaf = await EvaluateLeafAsync(node, graph, maxRows, cancellationToken).ConfigureAwait(false);
                    results[new ResultKey(node, graph)] = leaf;

                    //The graph-querying leaves carry the only strategy decision worth a trace point; the inline
                    //VALUES / unit / SERVICE leaves are reported by their own surfaces or are not island-relevant.
                    if(node is Bgp)
                    {
                        trace.Emit(SparqlExecutionOperator.Bgp, leaf, rowsLeft: -1, rowsRight: -1);
                    }
                    else if(node is Path)
                    {
                        trace.Emit(SparqlExecutionOperator.Path, leaf, rowsLeft: -1, rowsRight: -1);
                    }
                }
                else
                {
                    work.Push((node, graph, Combine: true));
                    for(int i = children.Count - 1; i >= 0; i--)
                    {
                        work.Push((children[i], graph, Combine: false));
                    }
                }
            }

            return results[new ResultKey(algebra, activeGraph)];
        }
        finally
        {
            await existsRegistry.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Expands a <c>GRAPH</c> form onto the work stack (no recursion): a constant designator re-roots the child
    /// under that one named graph (and is skipped entirely when the dataset has no such graph), while a variable
    /// designator re-roots a copy of the child under EACH named graph, so the combine can union them and bind the
    /// graph variable per branch.
    /// </summary>
    /// <param name="graphOperator">The GRAPH operator.</param>
    /// <param name="enclosingGraph">The active graph the GRAPH form itself sits in (its combine records under this key).</param>
    /// <param name="work">The work stack to push expansion entries onto.</param>
    private void ExpandGraph(Graph graphOperator, TermId enclosingGraph, Stack<(AlgebraOperator Node, TermId Graph, bool Combine)> work)
    {
        work.Push((graphOperator, enclosingGraph, Combine: true));
        switch(graphOperator.Designator)
        {
            case GraphIriTerm iri:
            {
                //A constant GRAPH ranges over exactly one named graph; descend only when the dataset has it, so a
                //GRAPH naming an absent graph (or a non-named-graph IRI) contributes nothing — never the default.
                TermId graphName = GraphNameOf(iri);
                if(Dataset.ContainsNamedGraph(graphName))
                {
                    work.Push((graphOperator.Input, graphName, Combine: false));
                }

                break;
            }

            case GraphVariableTerm:
            {
                foreach(TermId name in Dataset.GraphNames)
                {
                    work.Push((graphOperator.Input, name, Combine: false));
                }

                break;
            }

            default:
            {
                throw new InvalidOperationException($"Unexpected GRAPH designator '{graphOperator.Designator.GetType().Name}'.");
            }
        }
    }

    /// <summary>
    /// Combines a <c>GRAPH</c> form (§18.2.2.7): a constant designator returns its child's solutions in the named
    /// graph unchanged (or empty when the graph is absent); a variable designator unions, over every named graph,
    /// the child's solutions joined with the single binding of the graph variable to that graph's name.
    /// </summary>
    /// <param name="graphOperator">The GRAPH operator.</param>
    /// <param name="enclosingGraph">The active graph the GRAPH form sits in (unused for the result, kept for symmetry with the key).</param>
    /// <param name="results">The per-(operator, graph) result map the child's branches were recorded in.</param>
    /// <returns>The GRAPH form's solution sequence.</returns>
    private SolutionTable CombineGraph(Graph graphOperator, TermId enclosingGraph, Dictionary<ResultKey, SolutionTable> results)
    {
        _ = enclosingGraph;
        switch(graphOperator.Designator)
        {
            case GraphIriTerm iri:
            {
                TermId graphName = GraphNameOf(iri);

                //A constant GRAPH passes its child's table through unchanged — including the columnar backing, so a
                //GRAPH-scoped BGP stays on the columnar island.
                return Dataset.ContainsNamedGraph(graphName) ? results[new ResultKey(graphOperator.Input, graphName)] : SolutionTable.Empty;
            }

            case GraphVariableTerm variableTerm:
            {
                List<SparqlSolution> all = [];
                foreach(TermId name in Dataset.GraphNames)
                {
                    RdfTerm graphTerm = Dictionary.Resolve(name);
                    foreach(SparqlSolution inner in results[new ResultKey(graphOperator.Input, name)].AsRows())
                    {
                        //§18.2.2.7 Graph(var, P): eval(P, g) joined with { (var, g) }. A solution already binding the
                        //variable survives only when it equals this graph's name; otherwise the variable binds to it.
                        if(inner.TryGetValue(variableTerm.Variable, out RdfTerm existing))
                        {
                            if(existing.Equals(graphTerm))
                            {
                                all.Add(inner);
                            }

                            continue;
                        }

                        List<SparqlBinding> bindings = new(inner.Bindings.Count + 1);
                        bindings.AddRange(inner.Bindings);
                        bindings.Add(new SparqlBinding(variableTerm.Variable, graphTerm));
                        all.Add(new SparqlSolution(bindings));
                    }
                }

                return SolutionTable.FromRows(all);
            }

            default:
            {
                throw new InvalidOperationException($"Unexpected GRAPH designator '{graphOperator.Designator.GetType().Name}'.");
            }
        }
    }

    /// <summary>Resolves a constant <c>GRAPH</c> designator's (already-absolute) IRI to its term id, or <see cref="TermId.None"/> when absent from the dictionary.</summary>
    /// <param name="iri">The constant graph designator.</param>
    /// <returns>The graph-name term id, or <see cref="TermId.None"/>.</returns>
    private TermId GraphNameOf(GraphIriTerm iri)
    {
        return Dictionary.GetIdOrDefault(new NamedNode(iri.Iri.Value));
    }

    /// <summary>The key identifying one operator's solution sequence under one active graph in the per-evaluation result map.</summary>
    /// <param name="Node">The algebra operator (compared by reference).</param>
    /// <param name="Graph">The active graph the operator was evaluated under.</param>
    private readonly record struct ResultKey(AlgebraOperator Node, TermId Graph);

    /// <summary>Equality over <see cref="ResultKey"/> by operator reference identity and active-graph value — algebra records have value equality, so reference identity is required to keep distinct instances apart.</summary>
    private sealed class ResultKeyComparer : IEqualityComparer<ResultKey>
    {
        /// <summary>The shared comparer instance.</summary>
        public static ResultKeyComparer Instance { get; } = new();

        /// <summary>Returns whether two keys share an operator instance and an active graph.</summary>
        /// <param name="x">The first key.</param>
        /// <param name="y">The second key.</param>
        /// <returns><see langword="true"/> when both refer to the same operator instance under the same graph.</returns>
        public bool Equals(ResultKey x, ResultKey y)
        {
            return ReferenceEquals(x.Node, y.Node) && x.Graph.Equals(y.Graph);
        }

        /// <summary>Computes a reference-identity hash of the operator combined with the active-graph hash.</summary>
        /// <param name="key">The key to hash.</param>
        /// <returns>The hash code.</returns>
        public int GetHashCode(ResultKey key)
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(key.Node), key.Graph);
        }
    }

    /// <summary>Evaluates a leaf operator (one with no algebra children) against the active graph: a <see cref="Bgp"/> or property-path <see cref="Path"/> against that graph's store, the inline <see cref="Table"/>, or the <see cref="UnitTable"/> identity.</summary>
    /// <param name="op">The leaf operator.</param>
    /// <param name="graph">The active graph, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The leaf's solution sequence.</returns>
    private async ValueTask<SolutionTable> EvaluateLeafAsync(AlgebraOperator op, TermId graph, int maxRows, CancellationToken cancellationToken)
    {
        //UnitTable / inline VALUES are graph-independent; the graph-querying leaves resolve the active graph's
        //store. The store is non-null by construction: the default graph always resolves, and a GRAPH form only
        //descends into a named graph that exists (ExpandGraph), so an absent named graph never reaches here.
        if(op is UnitTable)
        {
            return SingleEmptySolutionTable;
        }

        if(op is Table table)
        {
            return SolutionTable.FromRows(BgpMachinery.BuildTableSolutions(table.Data));
        }

        if(op is Service service)
        {
            return await EvaluateServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }

        //The default graph is null only under deferred residency before the trie is materialised; the BGP path
        //routes through the rendezvous (which serves the warm view or materialises on demand), so a null default
        //graph is admissible there. A named graph is never deferred — an absent one is an executor bug.
        HypertrieGraphStore? graphStore = Dataset.Resolve(graph);
        if(graphStore is null && !graph.IsNone)
        {
            throw new InvalidOperationException($"Active graph '{graph.Encoded}' is not in the dataset; the GRAPH expansion should not have descended into it.");
        }

        if(op is Bgp bgp)
        {
            //Default-graph patterns route through the engine
            //rendezvous, which pins this snapshot's store; named
            //graphs route through the graph-set rendezvous — the
            //shared columnar arena over all named graphs — with the
            //same pinned-store fallback discipline.
            QueryEngineRendezvous? rendezvous = graph.IsNone ? Dataset.DefaultGraphRendezvous : null;

            return await Machinery.EvaluateBgpAsync(bgp, graphStore, rendezvous, graph, maxRows, cancellationToken).ConfigureAwait(false);
        }

        if(op is Path pathOperator)
        {
            //A property path needs the trie's Match ops; under deferred residency a null default graph is
            //materialised on demand first. A named-graph path already has a concrete store.
            HypertrieGraphStore pathStore = graphStore ?? await Dataset.RequireDefaultGraphAsync(cancellationToken).ConfigureAwait(false);

            return SolutionTable.FromRows(await EvaluatePathAsync(pathOperator, pathStore, cancellationToken).ConfigureAwait(false));
        }

        //The supported-operator guard runs first, so any other leaf reaching here is an executor bug, not input.
        throw new InvalidOperationException($"Leaf operator '{op.GetType().Name}' has no evaluation rule in the executor.");
    }

    /// <summary>
    /// Evaluates a <c>SERVICE</c> by rendering its inner pattern to a self-contained query, sending it through the
    /// injected <see cref="SparqlClient"/>, and returning the endpoint's solutions (which the enclosing join then
    /// joins with the surrounding pattern). Only a constant-IRI endpoint is supported here; a variable endpoint
    /// needs bound-join evaluation (the endpoint comes from upstream bindings) and is a later slice. <c>SILENT</c>
    /// turns an unsupported endpoint, a missing transport, or a transport failure into the join identity.
    /// </summary>
    /// <param name="service">The SERVICE operator.</param>
    /// <param name="cancellationToken">A token that aborts the remote call.</param>
    /// <returns>The service's solution sequence.</returns>
    private async ValueTask<SolutionTable> EvaluateServiceAsync(Service service, CancellationToken cancellationToken)
    {
        if(service.Endpoint is not GraphIriTerm endpoint)
        {
            //A variable endpoint is resolved per left binding by the enclosing Join's bound-join
            //(CombineServiceBoundJoinAsync); on its own — with no surrounding solutions to supply the
            //endpoint — the service produces nothing.
            return SolutionTable.Empty;
        }

        if(ServiceClient is null)
        {
            return service.Silent
                ? SingleEmptySolutionTable
                : throw new NotSupportedException("SERVICE requires a SparqlClient transport, but the engine was constructed without one.");
        }

        string query = SparqlQueryTextWriter.ToSelectQuery(service.InnerPattern);
        try
        {
            SparqlResultSet results = await ServiceClient.QueryAsync(endpoint.Iri, query, AccessContext, cancellationToken).ConfigureAwait(false);

            return SolutionTable.FromRows(results.Solutions);
        }
        catch(Exception ex) when(service.Silent && ex is not OperationCanceledException)
        {
            //SILENT: a federation failure contributes the join identity, so the surrounding pattern is unaffected.
            return SingleEmptySolutionTable;
        }
    }

    /// <summary>Whether <paramref name="join"/> has a variable-endpoint SERVICE child, which is evaluated as a bound-join (the endpoint is supplied by the other side's bindings) rather than as a standalone leaf.</summary>
    /// <param name="join">The join to test.</param>
    /// <returns><see langword="true"/> when one side is a <see cref="Service"/> with a variable endpoint.</returns>
    private static bool IsServiceBoundJoin(Join join)
    {
        return join.Left is Service { Endpoint: GraphVariableTerm } || join.Right is Service { Endpoint: GraphVariableTerm };
    }

    /// <summary>
    /// Evaluates a join whose one side is a variable-endpoint <c>SERVICE</c> as a bound-join (§18.5): the other
    /// side's solutions supply the endpoint, so the incoming solutions are grouped by the IRI bound to the
    /// endpoint variable, the inner pattern is sent once per distinct endpoint, and each endpoint's results are
    /// joined back into its group. A solution whose endpoint variable is unbound (or not an IRI) cannot drive the
    /// service: under <c>SILENT</c> it passes through (the join identity), otherwise it is an error. <c>SILENT</c>
    /// likewise turns a missing transport or a per-endpoint failure into the identity.
    /// </summary>
    /// <param name="join">The join carrying the variable-endpoint SERVICE on one side.</param>
    /// <param name="graph">The active graph the join's children were evaluated under.</param>
    /// <param name="results">The map of already-evaluated (operator, graph) keys to their solution sequences.</param>
    /// <param name="cancellationToken">A token that aborts the remote calls.</param>
    /// <returns>The bound-join's solution sequence.</returns>
    private async ValueTask<SolutionTable> CombineServiceBoundJoinAsync(Join join, TermId graph, Dictionary<ResultKey, SolutionTable> results, CancellationToken cancellationToken)
    {
        (Service service, AlgebraOperator bindingChild) = join.Right is Service { Endpoint: GraphVariableTerm } rightService
            ? (rightService, join.Left)
            : ((Service)join.Left, join.Right);

        IReadOnlyList<SparqlSolution> bindings = results[new ResultKey(bindingChild, graph)].AsRows();
        SparqlVariable endpointVariable = ((GraphVariableTerm)service.Endpoint).Variable;

        //Group the incoming solutions by the IRI bound to the endpoint variable, so each distinct endpoint is
        //queried once; first-seen endpoint order is preserved. Solutions that do not bind the endpoint to an IRI
        //cannot run the service and are handled separately.
        Dictionary<Utf8String, List<SparqlSolution>> byEndpoint = [];
        List<Utf8String> endpointOrder = [];
        List<SparqlSolution> unbound = [];
        foreach(SparqlSolution binding in bindings)
        {
            if(binding.TryGetValue(endpointVariable, out RdfTerm value) && value is NamedNode endpointIri)
            {
                if(!byEndpoint.TryGetValue(endpointIri.Iri, out List<SparqlSolution>? group))
                {
                    group = [];
                    byEndpoint[endpointIri.Iri] = group;
                    endpointOrder.Add(endpointIri.Iri);
                }

                group.Add(binding);
            }
            else
            {
                unbound.Add(binding);
            }
        }

        List<SparqlSolution> joined = [];
        if(endpointOrder.Count > 0 && ServiceClient is null)
        {
            //SILENT with no transport: each group contributes the join identity (its solutions pass through).
            return service.Silent
                ? SolutionTable.FromRows(Passthrough(endpointOrder, byEndpoint, unbound))
                : throw new NotSupportedException("SERVICE with a variable endpoint requires a SparqlClient transport, but the engine was constructed without one.");
        }

        if(ServiceClient is not null)
        {
            string query = SparqlQueryTextWriter.ToSelectQuery(service.InnerPattern);
            foreach(Utf8String endpoint in endpointOrder)
            {
                List<SparqlSolution> group = byEndpoint[endpoint];
                try
                {
                    SparqlResultSet remote = await ServiceClient.QueryAsync(new IriRef(endpoint, default), query, AccessContext, cancellationToken).ConfigureAwait(false);
                    joined.AddRange(JoinRows(group, remote.Solutions));
                }
                catch(Exception ex) when(service.Silent && ex is not OperationCanceledException)
                {
                    //SILENT: a failure at one endpoint contributes the identity, so that endpoint's group passes through.
                    joined.AddRange(group);
                }
            }
        }

        //A solution that does not bind the endpoint to an IRI cannot run the service: identity under SILENT, else an error.
        if(unbound.Count > 0)
        {
            if(!service.Silent)
            {
                throw new NotSupportedException("SERVICE with a variable endpoint left the endpoint unbound (or not an IRI) in some solutions; bind it before the SERVICE, or use SERVICE SILENT.");
            }

            joined.AddRange(unbound);
        }

        return SolutionTable.FromRows(joined);
    }

    /// <summary>The bound-join identity: every grouped and unbound solution passes through unchanged (used when SILENT suppresses the service entirely).</summary>
    /// <param name="endpointOrder">The distinct endpoints in first-seen order.</param>
    /// <param name="byEndpoint">The solutions grouped by endpoint.</param>
    /// <param name="unbound">The solutions that bound no endpoint IRI.</param>
    /// <returns>All incoming solutions, unchanged.</returns>
    private static List<SparqlSolution> Passthrough(List<Utf8String> endpointOrder, Dictionary<Utf8String, List<SparqlSolution>> byEndpoint, List<SparqlSolution> unbound)
    {
        List<SparqlSolution> all = [];
        foreach(Utf8String endpoint in endpointOrder)
        {
            all.AddRange(byEndpoint[endpoint]);
        }

        all.AddRange(unbound);

        return all;
    }

    /// <summary>Combines an operator's already-evaluated child sequences (read from <paramref name="results"/> under the same active graph) into its own sequence.</summary>
    /// <param name="op">The operator to combine.</param>
    /// <param name="graph">The active graph the operator and its children were evaluated under.</param>
    /// <param name="results">The map of already-evaluated (operator, graph) keys to their solution sequences.</param>
    /// <param name="context">The seams the expression-gated operators' non-pure functions consume.</param>
    /// <returns>The operator's solution sequence.</returns>
    private static SolutionTable Combine(AlgebraOperator op, TermId graph, Dictionary<ResultKey, SolutionTable> results, SparqlExpressionContext context, SparqlExecutionTrace trace)
    {
        SolutionTable result = op switch
        {
            //Project / Distinct / Slice / Union stay on the columnar island when their input is columnar (they
            //rearrange encoded-id columns and never decode); the row-only operators bridge through AsRows.
            Project project => ApplyProject(project, results[new ResultKey(project.Input, graph)]),
            Distinct distinct => ApplyDistinct(results[new ResultKey(distinct.Input, graph)]),

            //REDUCED permits but does not require duplicate elimination; passing the table through is conformant.
            Reduced reduced => results[new ResultKey(reduced.Input, graph)],
            Slice slice => ApplySlice(slice, results[new ResultKey(slice.Input, graph)]),
            Join join => JoinSolutions(results[new ResultKey(join.Left, graph)], results[new ResultKey(join.Right, graph)]),
            Union union => UnionSolutions(results[new ResultKey(union.Left, graph)], results[new ResultKey(union.Right, graph)]),
            Minus minus => MinusSolutions(results[new ResultKey(minus.Left, graph)], results[new ResultKey(minus.Right, graph)]),
            OrderBy orderBy => OrderBySolutions(orderBy, results[new ResultKey(orderBy.Input, graph)], context),

            //ToMultiSet / ToList are multiset / sequence conversions; with no ORDER BY in scope they pass through.
            ToMultiSet toMultiSet => results[new ResultKey(toMultiSet.Input, graph)],
            ToList toList => results[new ResultKey(toList.Input, graph)],

            //Group leaves the multiset unchanged here; the partitioning into groups happens in the AggregateJoin
            //that wraps it (which reads the Group's keys directly).
            Group group => results[new ResultKey(group.Input, graph)],
            AggregateJoin aggregateJoin => EvaluateAggregateJoin(aggregateJoin, results[new ResultKey(aggregateJoin.Input, graph)], context),
            _ => throw new InvalidOperationException($"Operator '{op.GetType().Name}' has children but no combine rule in the executor.")
        };

        EmitCombineTrace(trace, op, graph, results, result);

        return result;
    }

    /// <summary>Emits the execution-trace event for a synchronously-combined operator, reading its strategy from the result's backing and its input row shapes from the recorded child results. The structural pass-throughs (REDUCED, ToMultiSet, ToList, Group) carry no strategy decision and are not traced.</summary>
    /// <param name="trace">The evaluation's trace sink.</param>
    /// <param name="op">The combined operator.</param>
    /// <param name="graph">The active graph the operator and its children were evaluated under.</param>
    /// <param name="results">The recorded per-(operator, graph) results, for reading input row counts.</param>
    /// <param name="result">The operator's result table.</param>
    private static void EmitCombineTrace(SparqlExecutionTrace trace, AlgebraOperator op, TermId graph, Dictionary<ResultKey, SolutionTable> results, SolutionTable result)
    {
        if(!trace.IsEnabled)
        {
            return;
        }

        static int Rows(AlgebraOperator child, Dictionary<ResultKey, SolutionTable> results, TermId graph) => results[new ResultKey(child, graph)].Count;

        switch(op)
        {
            case Project project: trace.Emit(SparqlExecutionOperator.Project, result, Rows(project.Input, results, graph), -1); break;
            case Distinct distinct: trace.Emit(SparqlExecutionOperator.Distinct, result, Rows(distinct.Input, results, graph), -1); break;
            case Slice slice: trace.Emit(SparqlExecutionOperator.Slice, result, Rows(slice.Input, results, graph), -1); break;
            case Join join: trace.Emit(SparqlExecutionOperator.Join, result, Rows(join.Left, results, graph), Rows(join.Right, results, graph)); break;
            case Union union: trace.Emit(SparqlExecutionOperator.Union, result, Rows(union.Left, results, graph), Rows(union.Right, results, graph)); break;
            case Minus minus: trace.Emit(SparqlExecutionOperator.Minus, result, Rows(minus.Left, results, graph), Rows(minus.Right, results, graph)); break;
            case OrderBy orderBy: trace.Emit(SparqlExecutionOperator.OrderBy, result, Rows(orderBy.Input, results, graph), -1); break;
            case AggregateJoin aggregateJoin: trace.Emit(SparqlExecutionOperator.Aggregate, result, Rows(aggregateJoin.Input, results, graph), -1); break;
            default: break;
        }
    }

    /// <summary>
    /// Answers a <c>FILTER</c>-over-BGP subtree from a registered value index: the recognizer matches
    /// (a) a single-pattern BGP <c>?s &lt;P&gt; ?v</c> with <c>P</c> a declared POINT axis and the condition one
    /// ordering comparison between <c>?v</c> and a constant of the axis family, or (b) the DECLARED
    /// interval-pair two-pattern shape <c>?o &lt;START&gt; ?s . ?o &lt;END&gt; ?e</c> with the condition the overlap
    /// conjunction (<c>?s</c> bounded above, <c>?e</c> bounded below), and answers with the probe's locators
    /// decoded to terms. Everything else — another graph, an undeclared predicate or shape, a cross-family
    /// constant (the scan errors it, so pushing it would answer what the scan refuses), an equality operator
    /// (ordering is what the axis totalizes) — returns <see langword="null"/> and the subtree evaluates
    /// normally: the route never changes an answer.
    /// </summary>
    /// <param name="filter">The filter-over-BGP operator.</param>
    /// <param name="graph">The active graph; value indexes serve the default graph only.</param>
    /// <returns>The probe-answered table, or <see langword="null"/> when the recognizer or the probe declines.</returns>
    internal SolutionTable? TryEvaluateValueIndexProbe(Filter filter, TermId graph)
    {
        if(!graph.IsNone || filter.Input is not Bgp bgp)
        {
            return null;
        }

        Lumoin.Veritas.Core.Indexing.ValueIndexRegistry registry = Dataset.DefaultGraphRendezvous.ValueIndexes;
        if(registry.IsEmpty)
        {
            return null;
        }

        return bgp.Patterns.Count switch
        {
            1 => TryPointAxisProbe(filter, bgp, registry),
            2 => TryIntervalPairProbe(filter, bgp, registry),
            _ => null,
        };
    }

    /// <summary>The point-axis arm of the value-index recognizer: one pattern, one ordering comparison on its object variable.</summary>
    /// <param name="filter">The filter operator.</param>
    /// <param name="bgp">The single-pattern BGP.</param>
    /// <param name="registry">The composed registry.</param>
    /// <returns>The probe-answered table, or <see langword="null"/>.</returns>
    private SolutionTable? TryPointAxisProbe(Filter filter, Bgp bgp, Lumoin.Veritas.Core.Indexing.ValueIndexRegistry registry)
    {
        if(bgp.Patterns[0] is not { Subject: VariableTerm subjectVariable, Predicate: ConstantTerm { Term: NamedNode predicate }, Object: VariableTerm valueVariable }
            || subjectVariable.Variable == valueVariable.Variable)
        {
            return null;
        }

        Lumoin.Veritas.Core.Indexing.ValueIndexRegistration? registration = registry.FindByPredicate(predicate.Iri);
        if(registration is null || registration.Axis.IsIntervalPair)
        {
            return null;
        }

        if(!TryReadOrderingComparison(filter.Condition, valueVariable.Variable, registration.Method.DatatypeIri, out ComparisonOp op, out Literal? constant))
        {
            return null;
        }

        Lumoin.Veritas.Core.Indexing.ValueProbeRequest request = op switch
        {
            ComparisonOp.LessThan => Lumoin.Veritas.Core.Indexing.ValueProbeRequest.Range(null, false, constant, upperInclusive: false),
            ComparisonOp.LessOrEqual => Lumoin.Veritas.Core.Indexing.ValueProbeRequest.Range(null, false, constant, upperInclusive: true),
            ComparisonOp.GreaterThan => Lumoin.Veritas.Core.Indexing.ValueProbeRequest.Range(constant, lowerInclusive: false, null, false),
            _ => Lumoin.Veritas.Core.Indexing.ValueProbeRequest.Range(constant, lowerInclusive: true, null, false),
        };

        //The caller's evaluation store pins the probe: a pinned older snapshot or a WITH-substituted
        //default graph is not the rendezvous's live store, and the probe declines to that caller's own scan.
        if(!Dataset.DefaultGraphRendezvous.TryOpenValueProbe(predicate.Iri, in request, Dictionary, Dataset.Resolve(TermId.None), out Lumoin.Veritas.Core.Indexing.ValueProbeCursor? cursor))
        {
            return null;
        }

        List<SparqlSolution> rows = [];
        using(cursor)
        {
            while(cursor!.TryAdvance(out Lumoin.Veritas.Core.Indexing.ValueProbeHit hit))
            {
                rows.Add(new SparqlSolution(
                [
                    new SparqlBinding(subjectVariable.Variable, Dictionary.Resolve(hit.Subject)),
                    new SparqlBinding(valueVariable.Variable, Dictionary.Resolve(hit.Value)),
                ]));
            }
        }

        return SolutionTable.FromRows(rows);
    }

    /// <summary>The interval-pair arm of the value-index recognizer: the DECLARED two-pattern shape on one occurrence variable, with the overlap conjunction (start bounded above, end bounded below).</summary>
    /// <param name="filter">The filter operator.</param>
    /// <param name="bgp">The two-pattern BGP.</param>
    /// <param name="registry">The composed registry.</param>
    /// <returns>The probe-answered table, or <see langword="null"/>.</returns>
    private SolutionTable? TryIntervalPairProbe(Filter filter, Bgp bgp, Lumoin.Veritas.Core.Indexing.ValueIndexRegistry registry)
    {
        if(bgp.Patterns[0] is not { Subject: VariableTerm firstOccurrence, Predicate: ConstantTerm { Term: NamedNode firstPredicate }, Object: VariableTerm firstValue }
            || bgp.Patterns[1] is not { Subject: VariableTerm secondOccurrence, Predicate: ConstantTerm { Term: NamedNode secondPredicate }, Object: VariableTerm secondValue }
            || firstOccurrence.Variable != secondOccurrence.Variable
            || firstValue.Variable == secondValue.Variable
            || firstOccurrence.Variable == firstValue.Variable
            || firstOccurrence.Variable == secondValue.Variable)
        {
            return null;
        }

        Lumoin.Veritas.Core.Indexing.ValueIndexRegistration? registration = registry.FindByPredicate(firstPredicate.Iri);
        if(registration is null || !registration.Axis.IsIntervalPair)
        {
            return null;
        }

        //Match the DECLARED pair orientation: which pattern carries the start and which the end.
        SparqlVariable startVariable;
        SparqlVariable endVariable;
        if(registration.Axis.StartPredicateIri.Equals(firstPredicate.Iri) && registration.Axis.EndPredicateIri!.Value.Equals(secondPredicate.Iri))
        {
            startVariable = firstValue.Variable;
            endVariable = secondValue.Variable;
        }
        else if(registration.Axis.StartPredicateIri.Equals(secondPredicate.Iri) && registration.Axis.EndPredicateIri!.Value.Equals(firstPredicate.Iri))
        {
            startVariable = secondValue.Variable;
            endVariable = firstValue.Variable;
        }
        else
        {
            return null;
        }

        //The overlap conjunction: the start bounded ABOVE (start <= B) and the end bounded BELOW (end >= A),
        //in either conjunct order and either operand orientation.
        if(filter.Condition is not AndExpression conjunction)
        {
            return null;
        }

        Literal? upperConstant = null;
        bool upperInclusive = false;
        Literal? lowerConstant = null;
        bool lowerInclusive = false;
        foreach(ExpressionNode conjunct in (ReadOnlySpan<ExpressionNode>)[conjunction.Left, conjunction.Right])
        {
            if(TryReadOrderingComparison(conjunct, startVariable, registration.Method.DatatypeIri, out ComparisonOp startOp, out Literal? startConstant)
                && startOp is ComparisonOp.LessThan or ComparisonOp.LessOrEqual
                && upperConstant is null)
            {
                upperConstant = startConstant;
                upperInclusive = startOp == ComparisonOp.LessOrEqual;
            }
            else if(TryReadOrderingComparison(conjunct, endVariable, registration.Method.DatatypeIri, out ComparisonOp endOp, out Literal? endConstant)
                && endOp is ComparisonOp.GreaterThan or ComparisonOp.GreaterOrEqual
                && lowerConstant is null)
            {
                lowerConstant = endConstant;
                lowerInclusive = endOp == ComparisonOp.GreaterOrEqual;
            }
            else
            {
                return null;
            }
        }

        if(upperConstant is null || lowerConstant is null)
        {
            return null;
        }

        Lumoin.Veritas.Core.Indexing.ValueProbeRequest request = Lumoin.Veritas.Core.Indexing.ValueProbeRequest.Range(lowerConstant, lowerInclusive, upperConstant, upperInclusive);

        //The caller's evaluation store pins the probe exactly as on the point arm.
        if(!Dataset.DefaultGraphRendezvous.TryOpenValueProbe(registration.Axis.StartPredicateIri, in request, Dictionary, Dataset.Resolve(TermId.None), out Lumoin.Veritas.Core.Indexing.ValueProbeCursor? cursor))
        {
            return null;
        }

        List<SparqlSolution> rows = [];
        using(cursor)
        {
            while(cursor!.TryAdvance(out Lumoin.Veritas.Core.Indexing.ValueProbeHit hit))
            {
                rows.Add(new SparqlSolution(
                [
                    new SparqlBinding(firstOccurrence.Variable, Dictionary.Resolve(hit.Subject)),
                    new SparqlBinding(startVariable, Dictionary.Resolve(hit.Value)),
                    new SparqlBinding(endVariable, Dictionary.Resolve(hit.UpperValue)),
                ]));
            }
        }

        return SolutionTable.FromRows(rows);
    }

    /// <summary>Reads one ordering comparison between <paramref name="variable"/> and a constant literal of the axis family, normalizing a constant-on-the-left form onto the variable; equality operators and cross-family constants decline.</summary>
    /// <param name="condition">The candidate expression.</param>
    /// <param name="variable">The variable the comparison must constrain.</param>
    /// <param name="axisDatatypeIri">The declared axis datatype the constant's family must match.</param>
    /// <param name="op">Receives the operator, normalized to the variable-on-the-left orientation.</param>
    /// <param name="constant">Receives the constant literal.</param>
    /// <returns><see langword="true"/> when the expression is such a comparison.</returns>
    private static bool TryReadOrderingComparison(ExpressionNode condition, SparqlVariable variable, Utf8String axisDatatypeIri, out ComparisonOp op, out Literal? constant)
    {
        op = default;
        constant = null;
        if(condition is not ComparisonExpression comparison)
        {
            return false;
        }

        ExpressionNode variableSide;
        ExpressionNode constantSide;
        bool mirrored;
        if(comparison.Left is VariableExpression)
        {
            variableSide = comparison.Left;
            constantSide = comparison.Right;
            mirrored = false;
        }
        else
        {
            variableSide = comparison.Right;
            constantSide = comparison.Left;
            mirrored = true;
        }

        if(variableSide is not VariableExpression { Variable: SparqlVariable candidate } || candidate != variable
            || constantSide is not ConstantExpression { Value: Literal literal })
        {
            return false;
        }

        //A constant on the left mirrors onto the variable: C < ?v is ?v > C, and so on.
        ComparisonOp normalized = mirrored
            ? comparison.Op switch
            {
                ComparisonOp.LessThan => ComparisonOp.GreaterThan,
                ComparisonOp.LessOrEqual => ComparisonOp.GreaterOrEqual,
                ComparisonOp.GreaterThan => ComparisonOp.LessThan,
                ComparisonOp.GreaterOrEqual => ComparisonOp.LessOrEqual,
                _ => comparison.Op,
            }
            : comparison.Op;

        if(normalized is not (ComparisonOp.LessThan or ComparisonOp.LessOrEqual or ComparisonOp.GreaterThan or ComparisonOp.GreaterOrEqual))
        {
            return false;
        }

        if(!Lumoin.Veritas.Rdf.Values.RdfValueComparer.AreSameTemporalFamily(axisDatatypeIri, literal.Datatype.Iri))
        {
            return false;
        }

        op = normalized;
        constant = literal;

        return true;
    }

    /// <summary>
    /// Evaluates a bare <c>COUNT(*)</c> over a BGP from the factorised build's cardinality, without
    /// materialising the BGP's solutions: applies when the aggregate join wraps an implicit (key-less) group
    /// over a plain BGP, every aggregation is a non-<c>DISTINCT</c> <c>COUNT(*)</c>, the active graph is the
    /// default graph (the engine rendezvous' view), and the encoded pattern carries none of the per-solution
    /// rewrites (triple-term destructuring, self-join equalities, type expansion). The rendezvous count equals
    /// the drained row count exactly, so the produced solution is identical to the normal path's; any condition
    /// this fast path cannot read returns <see langword="null"/> and the operator evaluates normally.
    /// </summary>
    /// <param name="aggregateJoin">The aggregate-join operator.</param>
    /// <param name="graph">The active graph.</param>
    /// <returns>The single-solution table binding each count variable, or <see langword="null"/> when the fast path does not apply.</returns>
    internal SolutionTable? TryEvaluateCountOnly(AggregateJoin aggregateJoin, TermId graph)
    {
        if(!graph.IsNone || Dataset.DefaultGraphRendezvous is not QueryEngineRendezvous rendezvous)
        {
            return null;
        }

        if(aggregateJoin.Input is not Group { Keys.Count: 0, Input: Bgp bgp } || aggregateJoin.Aggregations.Count == 0)
        {
            return null;
        }

        foreach(AggregateBinding aggregation in aggregateJoin.Aggregations)
        {
            if(aggregation.Aggregate is not BuiltInAggregateExpression { IsCountStar: true, IsDistinct: false })
            {
                return null;
            }
        }

        BgpMachinery.EncodedBgp encoded = Machinery.EncodeBgp(bgp);
        long count;
        if(!encoded.Encodable)
        {
            //A constant absent from the graph: the BGP yields nothing and the implicit group counts zero.
            count = 0;
        }
        else
        {
            if(encoded.TripleTermMatches.Count > 0
                || encoded.SelfJoinEqualities.Count > 0
                || Machinery.ComputeTypeExpansions(encoded.Patterns).Count > 0)
            {
                return null;
            }

            //graphStore is null under deferred residency (trie unbuilt); it flows as a null pinned store so the
            //rendezvous answers the count from its warm columnar view, or declines (null) and the operator counts
            //by normal evaluation.
            HypertrieGraphStore? graphStore = Dataset.Resolve(graph);
            BasicGraphPattern query = new(encoded.Patterns, encoded.Registry);
            if(rendezvous.TryCountBatched(graphStore, query, TimeProvider, AccessControl) is not long counted)
            {
                return null;
            }

            count = counted;
        }

        List<SparqlBinding> bindings = new(aggregateJoin.Aggregations.Count);
        foreach(AggregateBinding aggregation in aggregateJoin.Aggregations)
        {
            bindings.Add(new SparqlBinding(aggregation.Variable, SparqlExpressionEvaluator.IntegerTerm(count)));
        }

        return SolutionTable.FromRows([new SparqlSolution(bindings)]);
    }

    /// <summary>
    /// Evaluates a <c>SELECT DISTINCT</c> of star-key variables over a BGP from the factorised build's group
    /// keys, without materialising the BGP's solutions: applies when a <see cref="Distinct"/> wraps a
    /// <see cref="Project"/> directly over a plain BGP, the active graph is the default graph, the encoded
    /// pattern carries none of the per-solution rewrites, every projected variable is bound by the pattern,
    /// and the shape is a factorisable star whose key covers the projection (the rendezvous decides the last
    /// two). The produced rows equal the normal path's drained-projected-deduplicated rows exactly; any
    /// condition this fast path cannot read returns <see langword="null"/> and the operator evaluates normally.
    /// </summary>
    /// <param name="distinct">The DISTINCT operator wrapping the projection.</param>
    /// <param name="graph">The active graph.</param>
    /// <returns>The distinct projected table, or <see langword="null"/> when the fast path does not apply.</returns>
    internal SolutionTable? TryEvaluateDistinctKeys(Distinct distinct, TermId graph)
    {
        if(!graph.IsNone || Dataset.DefaultGraphRendezvous is not QueryEngineRendezvous rendezvous)
        {
            return null;
        }

        if(distinct.Input is not Project { Input: Bgp bgp } project || project.Variables.Count == 0)
        {
            return null;
        }

        BgpMachinery.EncodedBgp encoded = Machinery.EncodeBgp(bgp);
        if(encoded.Encodable
            && (encoded.TripleTermMatches.Count > 0
                || encoded.SelfJoinEqualities.Count > 0
                || Machinery.ComputeTypeExpansions(encoded.Patterns).Count > 0))
        {
            return null;
        }

        //Every projected variable must be bound by the pattern; the projection
        //map, in projection order, is the output schema.
        Dictionary<Variable, SparqlVariable> projectionMap = new(project.Variables.Count);
        List<Variable> projection = new(project.Variables.Count);
        foreach(SparqlVariable variable in project.Variables)
        {
            Variable? backend = null;
            foreach(KeyValuePair<Variable, SparqlVariable> pair in encoded.ToSparql)
            {
                if(pair.Value == variable)
                {
                    backend = pair.Key;

                    break;
                }
            }

            if(backend is not Variable bound || !projectionMap.TryAdd(bound, variable))
            {
                return null;
            }

            projection.Add(bound);
        }

        BgpMachinery.BgpColumnBuilder builder = new(projectionMap, Dictionary);
        if(!encoded.Encodable)
        {
            //A constant absent from the graph: the BGP yields nothing and the distinct projection is empty.
            return builder.Build();
        }

        //graphStore is null under deferred residency (trie unbuilt); it flows as a null pinned store so the
        //rendezvous answers the distinct keys from its warm columnar view, or declines (null) and the operator
        //deduplicates by normal evaluation.
        HypertrieGraphStore? graphStore = Dataset.Resolve(graph);
        BasicGraphPattern query = new(encoded.Patterns, encoded.Registry);
        List<SolutionBatch>? batches = rendezvous.TryDistinctKeysBatched(graphStore, query, projection, TimeProvider, AccessControl);
        if(batches is null)
        {
            return null;
        }

        foreach(SolutionBatch batch in batches)
        {
            builder.AppendBatch(batch);
        }

        return builder.Build();
    }

    /// <summary>
    /// The breaker-decline gate: whether a window's chain of count-preserving streamable operators
    /// bottoms DIRECTLY on a full pipeline breaker (<see cref="OrderBy"/>, <see cref="Group"/>,
    /// <see cref="AggregateJoin"/>) with no intervening count-changing streamable operator. Those cannot
    /// early-exit, and the off-mode columnar slice decodes only the window's survivors, so the streaming
    /// interception there is strictly worse than today. A count-changing streamable operator
    /// (<c>FILTER</c>/<c>DISTINCT</c>/<c>UNION</c>/joins) between the window and the breaker restores the
    /// early-exit value and the interception engages.
    /// </summary>
    /// <param name="input">The slice's input operator.</param>
    /// <returns><see langword="true"/> when the interception must decline.</returns>
    internal static bool SliceWindowBottomsOnBreaker(AlgebraOperator input)
    {
        AlgebraOperator current = input;
        while(true)
        {
            switch(current)
            {
                case Project project: current = project.Input; break;

                case ToList toList: current = toList.Input; break;

                case ToMultiSet toMultiSet: current = toMultiSet.Input; break;

                case Reduced reduced: current = reduced.Input; break;

                case Extend extend: current = extend.Input; break;

                case Filter or Distinct or Union or Join or LeftJoin or Minus: return false;

                case OrderBy or Group or AggregateJoin: return true;

                default: return false;
            }
        }
    }

    /// <summary>
    /// The streaming window interception (the third pipeline consumer): compiles the <c>Slice</c> subtree
    /// and drains it into a table, stopping at offset+limit — upstream production terminates once the window
    /// fills. Declines (value-based, <see langword="null"/>) when the enclosing budget cannot afford the
    /// compile (this interception is reachable at nested depth through the materialising EXISTS re-entry,
    /// so it never compiles at a fresh constant) or when the order gate rolled the window onto the
    /// materialise boundary (no early-exit gain there — the driver's normal path answers). The transient
    /// pipeline's charges are refunded once it is drained and disposed: nothing lives past this step.
    /// </summary>
    /// <param name="slice">The window operator.</param>
    /// <param name="graph">The active graph.</param>
    /// <param name="cursorBudget">The enclosing evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The enclosing evaluation's EXISTS re-entry depth.</param>
    /// <param name="rewrites">The enclosing evaluation's resolved rewrite pipeline, carried for the transient pipeline's EXISTS plan builds.</param>
    /// <param name="trace">The spawning evaluation's trace sink — the pipeline's completion walk emits into the SAME correlation and sequence stream.</param>
    /// <param name="cancellationToken">A token that aborts the drain.</param>
    /// <returns>The window's table, or <see langword="null"/> when the interception declines.</returns>
    internal async ValueTask<SolutionTable?> TryDrainSliceWindowAsync(Slice slice, TermId graph, CursorBudget cursorBudget, int existsDepth, AlgebraRewritePipeline rewrites, SparqlExecutionTrace trace, CancellationToken cancellationToken)
    {
        StreamingPipeline? pipeline = StreamingPipeline.TryCompile(this, Machinery, slice, graph, cursorBudget, existsDepth, rewrites, trace);
        if(pipeline is null)
        {
            return null;
        }

        if(pipeline.Root is not SliceCursor)
        {
            cursorBudget.Remaining += pipeline.CursorCount;
            await pipeline.DisposeAsync().ConfigureAwait(false);

            return null;
        }

        try
        {
            List<SparqlSolution> rows = [];
            while(await pipeline.Root.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(pipeline.Root.Current);
            }

            return SolutionTable.FromRows(rows);
        }
        finally
        {
            cursorBudget.Remaining += pipeline.CursorCount;
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The BGP a slice's row cap can push to: the slice's input chain must preserve row counts and row
    /// eligibility on the way down — projection and the multiset/sequence conversions do; anything else
    /// (ordering, deduplication, expressions, joins, a nested slice) stops the walk, so a cap is never
    /// recorded where draining fewer leaf rows could change the sliced answer.
    /// </summary>
    /// <param name="input">The slice's input operator.</param>
    /// <returns>The cappable BGP leaf, or <see langword="null"/>.</returns>
    internal static Bgp? TryFindCappableBgp(AlgebraOperator input)
    {
        AlgebraOperator current = input;
        while(true)
        {
            AlgebraOperator? next = current switch
            {
                Project project => project.Input,
                ToList toList => toList.Input,
                ToMultiSet toMultiSet => toMultiSet.Input,
                _ => null,
            };

            if(next is null)
            {
                return current as Bgp;
            }

            current = next;
        }
    }

    /// <summary>Restricts each solution to the projected variables, in projection order, dropping bindings for variables a solution does not bind.</summary>
    /// <param name="project">The projection.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The projected solution sequence.</returns>
    private static SolutionTable ApplyProject(Project project, SolutionTable input)
    {
        return input.IsColumnar
            ? ColumnarOperators.Project(input, project.Variables)
            : SolutionTable.FromRows(ProjectRows(project, input.AsRows()));
    }

    /// <summary>The row form of projection: restricts each solution to the projected variables, in projection order, dropping bindings for variables a solution does not bind.</summary>
    /// <param name="project">The projection.</param>
    /// <param name="input">The input solution rows.</param>
    /// <returns>The projected solution rows.</returns>
    private static List<SparqlSolution> ProjectRows(Project project, IReadOnlyList<SparqlSolution> input)
    {
        List<SparqlSolution> projected = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            List<SparqlBinding> bindings = new(project.Variables.Count);
            foreach(SparqlVariable variable in project.Variables)
            {
                if(solution.TryGetValue(variable, out RdfTerm value))
                {
                    bindings.Add(new SparqlBinding(variable, value));
                }
            }

            projected.Add(new SparqlSolution(bindings));
        }

        return projected;
    }

    /// <summary>Eliminates duplicate solutions, comparing the (order-stable, post-projection) binding sequences by value.</summary>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The distinct solution sequence, in first-appearance order.</returns>
    private static SolutionTable ApplyDistinct(SolutionTable input)
    {
        return input.IsColumnar
            ? ColumnarOperators.Distinct(input)
            : SolutionTable.FromRows(DistinctRows(input.AsRows()));
    }

    /// <summary>The row form of duplicate elimination, comparing the (order-stable, post-projection) binding sequences by value.</summary>
    /// <param name="input">The input solution rows.</param>
    /// <returns>The distinct solution rows, in first-appearance order.</returns>
    private static List<SparqlSolution> DistinctRows(IReadOnlyList<SparqlSolution> input)
    {
        List<SparqlSolution> distinct = new(input.Count);
        HashSet<SparqlSolution> seen = new(SolutionComparer.Instance);
        foreach(SparqlSolution solution in input)
        {
            if(seen.Add(solution))
            {
                distinct.Add(solution);
            }
        }

        return distinct;
    }

    /// <summary>Applies the <c>OFFSET</c>/<c>LIMIT</c> window over the solution sequence.</summary>
    /// <param name="slice">The slice.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The windowed solution sequence.</returns>
    private static SolutionTable ApplySlice(Slice slice, SolutionTable input)
    {
        return input.IsColumnar
            ? ColumnarOperators.Slice(input, slice.Offset, slice.Limit)
            : SolutionTable.FromRows(SliceRows(slice, input.AsRows()));
    }

    /// <summary>The row form of the <c>OFFSET</c>/<c>LIMIT</c> window over the solution rows.</summary>
    /// <param name="slice">The slice.</param>
    /// <param name="input">The input solution rows.</param>
    /// <returns>The windowed solution rows.</returns>
    private static IReadOnlyList<SparqlSolution> SliceRows(Slice slice, IReadOnlyList<SparqlSolution> input)
    {
        IEnumerable<SparqlSolution> windowed = input;
        if(slice.Offset > 0)
        {
            windowed = windowed.Skip(slice.Offset);
        }

        if(slice.Limit is int limit)
        {
            windowed = windowed.Take(limit);
        }

        return [.. windowed];
    }

    /// <summary>
    /// Orders a result table by the <c>ORDER BY</c> conditions (§15.1). When the input is columnar and every
    /// condition keys on a plain variable, it stays columnar — only the key columns are decoded (for the typed
    /// compare), the rows are permuted, and the columns are gathered in that order, so a following <c>LIMIT</c>
    /// decodes only the surviving rows; otherwise it bridges to the row form.
    /// </summary>
    /// <param name="orderBy">The order operator.</param>
    /// <param name="input">The input table.</param>
    /// <param name="context">The seams the order expressions' non-pure functions consume.</param>
    /// <returns>The ordered table.</returns>
    private static SolutionTable OrderBySolutions(OrderBy orderBy, SolutionTable input, SparqlExpressionContext context)
    {
        return input.IsColumnar && TryOrderByColumnar(orderBy, input, context.ImplicitTimezone, out SolutionTable ordered)
            ? ordered
            : SolutionTable.FromRows(ApplyOrderBy(orderBy, input.AsRows(), context));
    }

    /// <summary>
    /// Orders a columnar table when every <c>ORDER BY</c> condition keys on a plain variable: it decodes just those
    /// key columns to typed values, sorts the row indices by the conditions in priority order (each ascending or
    /// descending, ties stable on the original index), and gathers every column in that order. Declines (returns
    /// <see langword="false"/>) when any condition is a non-variable expression, leaving the caller to bridge to
    /// the row form. No non-key column is decoded.
    /// </summary>
    /// <param name="orderBy">The order operator.</param>
    /// <param name="input">The columnar input table.</param>
    /// <param name="implicitTimezone">The implicit timezone temporal key comparisons normalize naive operands with.</param>
    /// <param name="result">Receives the ordered columnar table when the columnar path applies.</param>
    /// <returns><see langword="true"/> when ordered columnar; otherwise <see langword="false"/>.</returns>
    private static bool TryOrderByColumnar(OrderBy orderBy, SolutionTable input, TimeSpan implicitTimezone, out SolutionTable result)
    {
        result = SolutionTable.Empty;

        //Each condition must key on a plain variable; a computed key (?x + ?y, STR(?x), ...) needs per-row
        //expression evaluation and stays on the row path.
        int conditionCount = orderBy.Conditions.Count;
        bool[] descending = new bool[conditionCount];
        RdfTerm?[][] keys = new RdfTerm?[conditionCount][];
        IReadOnlyList<SparqlVariable> schema = input.Schema;
        for(int condition = 0; condition < conditionCount; condition++)
        {
            (ExpressionNode expression, bool isDescending) = orderBy.Conditions[condition] switch
            {
                OrderAscending ascending => (ascending.Expression, false),
                OrderDescending descend => (descend.Expression, true),
                _ => (null!, false)
            };

            if(expression is not VariableExpression variable)
            {
                return false;
            }

            descending[condition] = isDescending;

            //The key column's decoded values (a variable absent from the schema is unbound in every row, which
            //orders as the unbound category — equal across all rows, so the stable index tiebreak carries them).
            RdfTerm?[] keyColumn = new RdfTerm?[input.Count];
            int columnIndex = -1;
            for(int i = 0; i < schema.Count; i++)
            {
                if(schema[i] == variable.Variable)
                {
                    columnIndex = i;

                    break;
                }
            }

            if(columnIndex >= 0)
            {
                for(int row = 0; row < input.Count; row++)
                {
                    keyColumn[row] = input.DecodeCell(columnIndex, row);
                }
            }

            keys[condition] = keyColumn;
        }

        int[] order = new int[input.Count];
        for(int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, new ColumnarOrderComparer(conditionCount, keys, descending, implicitTimezone));

        int columnCount = schema.Count;
        uint[][] orderedColumns = new uint[columnCount][];
        for(int column = 0; column < columnCount; column++)
        {
            uint[] source = input.ColumnArray(column);
            uint[] target = new uint[order.Length];
            for(int i = 0; i < order.Length; i++)
            {
                target[i] = source[order[i]];
            }

            orderedColumns[column] = target;
        }

        result = SolutionTable.Columnar(schema, orderedColumns, input.Count, input.Dictionary, input.Overlay);

        return true;
    }

    /// <summary>Orders the solution sequence by the <c>ORDER BY</c> conditions (§15.1), each ascending or descending; ties keep input order (a stable sort by original index).</summary>
    /// <param name="orderBy">The order operator.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="context">The seams the order expressions' non-pure functions consume.</param>
    /// <returns>The ordered solution sequence.</returns>
    private static List<SparqlSolution> ApplyOrderBy(OrderBy orderBy, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        List<SparqlSolution> source = [.. input];
        int[] order = new int[source.Count];
        for(int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, new SolutionOrderComparer(source, orderBy.Conditions, context));

        List<SparqlSolution> ordered = new(source.Count);
        foreach(int index in order)
        {
            ordered.Add(source[index]);
        }

        return ordered;
    }

    /// <summary>
    /// Orders columnar row indices by the decoded key columns in priority order, carrying the key columns and
    /// directions as explicit state so the sort comparison closes over no enclosing local.
    /// </summary>
    /// <param name="conditionCount">The number of order conditions.</param>
    /// <param name="keys">The decoded key column per condition, indexed by row.</param>
    /// <param name="descending">Whether each condition sorts descending.</param>
    /// <param name="implicitTimezone">The implicit timezone temporal key comparisons normalize naive operands with.</param>
    private sealed class ColumnarOrderComparer(int conditionCount, RdfTerm?[][] keys, bool[] descending, TimeSpan implicitTimezone) : IComparer<int>
    {
        /// <summary>The number of order conditions.</summary>
        private int ConditionCount { get; } = conditionCount;

        /// <summary>The decoded key column per condition, indexed by row.</summary>
        private RdfTerm?[][] Keys { get; } = keys;

        /// <summary>Whether each condition sorts descending.</summary>
        private bool[] Descending { get; } = descending;

        /// <summary>The implicit timezone temporal key comparisons normalize naive operands with.</summary>
        private TimeSpan ImplicitTimezone { get; } = implicitTimezone;

        /// <summary>Compares two row indices by the key columns in priority order, with a stable index tiebreak.</summary>
        /// <param name="left">The first row index.</param>
        /// <param name="right">The second row index.</param>
        /// <returns>The ordering of the two rows.</returns>
        public int Compare(int left, int right)
        {
            for(int condition = 0; condition < ConditionCount; condition++)
            {
                int comparison = SparqlExpressionEvaluator.CompareForOrdering(Keys[condition][left], Keys[condition][right], ImplicitTimezone);
                if(comparison != 0)
                {
                    return Descending[condition] ? -comparison : comparison;
                }
            }

            //A stable tiebreak on the original index keeps ordering deterministic where every key is equal.
            return left.CompareTo(right);
        }
    }

    /// <summary>
    /// Orders solution row indices by the order conditions, carrying the solutions, conditions, and expression
    /// context as explicit state so the sort comparison closes over no enclosing local.
    /// </summary>
    /// <param name="source">The solutions being ordered, indexed by row.</param>
    /// <param name="conditions">The order conditions, in priority order.</param>
    /// <param name="context">The seams the order expressions' non-pure functions consume.</param>
    private sealed class SolutionOrderComparer(List<SparqlSolution> source, IReadOnlyList<OrderCondition> conditions, SparqlExpressionContext context) : IComparer<int>
    {
        /// <summary>The solutions being ordered, indexed by row.</summary>
        private List<SparqlSolution> Source { get; } = source;

        /// <summary>The order conditions, in priority order.</summary>
        private IReadOnlyList<OrderCondition> Conditions { get; } = conditions;

        /// <summary>The seams the order expressions' non-pure functions consume.</summary>
        private SparqlExpressionContext Context { get; } = context;

        /// <summary>Compares two row indices by the order conditions, with a stable index tiebreak.</summary>
        /// <param name="left">The first row index.</param>
        /// <param name="right">The second row index.</param>
        /// <returns>The ordering of the two rows.</returns>
        public int Compare(int left, int right)
        {
            int comparison = CompareByConditions(Source[left], Source[right], Conditions, Context);

            //A stable tiebreak on the original index keeps ordering deterministic where the keys are equal.
            return comparison != 0 ? comparison : left.CompareTo(right);
        }
    }

    /// <summary>Compares two solutions by the order conditions in priority order, applying each condition's direction.</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <param name="conditions">The order conditions, in priority order.</param>
    /// <param name="context">The seams the order expressions' non-pure functions consume.</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> orders before, equal to, or after <paramref name="right"/>.</returns>
    private static int CompareByConditions(SparqlSolution left, SparqlSolution right, IReadOnlyList<OrderCondition> conditions, SparqlExpressionContext context)
    {
        foreach(OrderCondition condition in conditions)
        {
            (ExpressionNode expression, bool descending) = condition switch
            {
                OrderAscending ascending => (ascending.Expression, false),
                OrderDescending descendingCondition => (descendingCondition.Expression, true),
                _ => throw new InvalidOperationException($"Unexpected order-condition kind {condition.GetType().Name} during SPARQL evaluation.")
            };

            RdfTerm? leftKey = SparqlExpressionEvaluator.TryEvaluate(expression, left, context, out RdfTerm leftValue) ? leftValue : null;
            RdfTerm? rightKey = SparqlExpressionEvaluator.TryEvaluate(expression, right, context, out RdfTerm rightValue) ? rightValue : null;
            int comparison = SparqlExpressionEvaluator.CompareForOrdering(leftKey, rightKey, context.ImplicitTimezone);
            if(comparison != 0)
            {
                return descending ? -comparison : comparison;
            }
        }

        return 0;
    }

    /// <summary>
    /// Evaluates a property-path <see cref="Path"/> leaf (§18.6) by resolving the AST path to an
    /// <see cref="Rdf.PropertyPath"/> over encoded ids and delegating to <see cref="PropertyPathEvaluator"/>
    /// (forward reachability over the store's match operations). It enumerates the matching subject/object pairs
    /// per binding mode and binds the variable endpoints: a bound subject enumerates objects; a bound object
    /// enumerates subjects via the inverse path; both bound is an existence check; both variable seeds from every
    /// graph node (the per-seed enumeration — the native pair-aware closure is a later optimization).
    /// </summary>
    /// <param name="path">The path operator.</param>
    /// <param name="graphStore">The active graph's store the path reachability runs over.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The path's solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluatePathAsync(Path path, HypertrieGraphStore graphStore, CancellationToken cancellationToken)
    {
        PropertyPath rdfPath = ConvertPath(path.PathExpression);
        GraphMatchOps ops = Machinery.GuardMatchOps(graphStore.AsMatchOps());
        (TermId? subjectId, SparqlVariable? subjectVariable) = ResolveEndpoint(path.Subject);
        (TermId? objectId, SparqlVariable? objectVariable) = ResolveEndpoint(path.Object);

        //A top-level *-or-?-path is REFLEXIVE: its zero-length path pairs every term with itself, independent of the
        //data (§18.2.2.5). When an endpoint is a bound constant the reflexive contribution is that constant paired
        //with itself — and it must be emitted even when the constant is absent from the data graph (e.g. `?s :p* :o`
        //over an empty graph yields `?s = :o`). The path evaluator already includes a present start node reflexively;
        //this handles the absent-constant gap, where the dictionary look-up short-circuited the whole path to empty.
        bool reflexive = path.PathExpression is PathZeroOrMore or PathZeroOrOne;
        List<SparqlSolution> solutions = [];

        if(subjectId is TermId subject)
        {
            //Bound subject: enumerate reachable objects (filtering to the object when it too is bound).
            if(subject.IsNone)
            {
                EmitReflexiveSelfBinding(solutions, reflexive, path.Subject, path.Object, subjectVariable, objectVariable);

                return solutions;
            }

            await foreach(TermId reached in PropertyPathEvaluator.EvaluateAsync(subject, rdfPath, ops, cancellationToken).ConfigureAwait(false))
            {
                if(objectId is TermId fixedObject && reached != fixedObject)
                {
                    continue;
                }

                BindPathPair(solutions, subjectVariable, subject, objectVariable, reached);
            }
        }
        else if(objectId is TermId @object)
        {
            //Bound object, variable subject: enumerate reachable subjects via the inverse path.
            if(@object.IsNone)
            {
                EmitReflexiveSelfBinding(solutions, reflexive, path.Subject, path.Object, subjectVariable, objectVariable);

                return solutions;
            }

            await foreach(TermId reached in PropertyPathEvaluator.EvaluateAsync(@object, new InversePath(rdfPath), ops, cancellationToken).ConfigureAwait(false))
            {
                BindPathPair(solutions, subjectVariable, reached, objectVariable, @object);
            }
        }
        else
        {
            //Both endpoints variable: seed forward reachability from every node in the graph.
            foreach(TermId seed in AllNodes(graphStore))
            {
                await foreach(TermId reached in PropertyPathEvaluator.EvaluateAsync(seed, rdfPath, ops, cancellationToken).ConfigureAwait(false))
                {
                    BindPathPair(solutions, subjectVariable, seed, objectVariable, reached);
                }
            }
        }

        return solutions;
    }

    /// <summary>
    /// Emits the zero-length (reflexive) self-binding of a <c>*</c>/<c>?</c> path when a bound-constant endpoint is
    /// absent from the data graph (so the normal data-driven evaluation found nothing): the constant pairs with
    /// itself. For one bound + one variable endpoint, the variable binds to the constant's term; for two constants,
    /// a single empty solution when they are the same term (reflexive match), nothing otherwise. Does nothing for a
    /// non-reflexive path.
    /// </summary>
    /// <param name="solutions">The accumulating solution list.</param>
    /// <param name="reflexive">Whether the top-level path is reflexive (<c>*</c> or <c>?</c>).</param>
    /// <param name="subjectTerm">The subject endpoint AST term.</param>
    /// <param name="objectTerm">The object endpoint AST term.</param>
    /// <param name="subjectVariable">The subject variable, or <see langword="null"/> when the subject is a constant.</param>
    /// <param name="objectVariable">The object variable, or <see langword="null"/> when the object is a constant.</param>
    private static void EmitReflexiveSelfBinding(
        List<SparqlSolution> solutions,
        bool reflexive,
        TriplePatternTerm subjectTerm,
        TriplePatternTerm objectTerm,
        SparqlVariable? subjectVariable,
        SparqlVariable? objectVariable)
    {
        if(!reflexive)
        {
            return;
        }

        //The constant endpoint that drove us here is the one whose dictionary id was None; its RDF term is taken
        //straight from the AST (it need not be in the data). The zero-length path binds the OTHER endpoint to it.
        RdfTerm? subjectConstant = subjectTerm is ConstantTerm s ? s.Term : null;
        RdfTerm? objectConstant = objectTerm is ConstantTerm o ? o.Term : null;

        if(subjectVariable is SparqlVariable subjectVar && objectConstant is RdfTerm objectValue)
        {
            //?s :p* :o  →  ?s = :o.
            solutions.Add(new SparqlSolution([new SparqlBinding(subjectVar, objectValue)]));

            return;
        }

        if(objectVariable is SparqlVariable objectVar && subjectConstant is RdfTerm subjectValue)
        {
            //:s :p* ?o  →  ?o = :s.
            solutions.Add(new SparqlSolution([new SparqlBinding(objectVar, subjectValue)]));

            return;
        }

        //Both constants: a reflexive zero-length match holds iff they are the same term.
        if(subjectConstant is RdfTerm a && objectConstant is RdfTerm b && a.Equals(b))
        {
            solutions.Add(new SparqlSolution([]));
        }
    }

    /// <summary>Resolves a path endpoint to its bound term id (a constant) or the variable it binds.</summary>
    /// <param name="term">The endpoint term (a constant or a variable after normalization).</param>
    /// <returns>The bound term id (a constant — possibly <see cref="TermId.None"/> when absent from the data) and no variable, or no id and the variable.</returns>
    /// <exception cref="NotSupportedException">The endpoint is neither a constant nor a variable.</exception>
    private (TermId? Id, SparqlVariable? Variable) ResolveEndpoint(TriplePatternTerm term)
    {
        return term switch
        {
            ConstantTerm constant => (Dictionary.GetIdOrDefault(constant.Term), (SparqlVariable?)null),
            VariableTerm variable => ((TermId?)null, variable.Variable),
            _ => throw new NotSupportedException($"Property-path endpoint '{term.GetType().Name}' is not supported by the executor.")
        };
    }

    /// <summary>Emits a solution for one matched subject/object pair, binding whichever endpoints are variables and decoding their values; a pair with the same variable on both ends matches only when the two values coincide.</summary>
    /// <param name="solutions">The accumulating solution list.</param>
    /// <param name="subjectVariable">The subject variable, or <see langword="null"/> when the subject is a constant.</param>
    /// <param name="subjectValue">The matched subject term id.</param>
    /// <param name="objectVariable">The object variable, or <see langword="null"/> when the object is a constant.</param>
    /// <param name="objectValue">The matched object term id.</param>
    private void BindPathPair(List<SparqlSolution> solutions, SparqlVariable? subjectVariable, TermId subjectValue, SparqlVariable? objectVariable, TermId objectValue)
    {
        //The same variable on both ends (e.g. ?x path ?x) matches only the pairs where the endpoints coincide.
        if(subjectVariable is SparqlVariable both && objectVariable == both && subjectValue != objectValue)
        {
            return;
        }

        List<SparqlBinding> bindings = new(2);
        if(subjectVariable is SparqlVariable subject)
        {
            bindings.Add(new SparqlBinding(subject, Dictionary.Resolve(subjectValue)));
        }

        if(objectVariable is SparqlVariable @object && @object != subjectVariable)
        {
            bindings.Add(new SparqlBinding(@object, Dictionary.Resolve(objectValue)));
        }

        solutions.Add(new SparqlSolution(bindings));
    }

    /// <summary>Collects every distinct node appearing as a subject or object in the active graph — the seed set for a both-variable path (so <c>*</c> is reflexive over all nodes).</summary>
    /// <param name="graphStore">The active graph's store.</param>
    /// <returns>The distinct nodes.</returns>
    private static HashSet<TermId> AllNodes(HypertrieGraphStore graphStore)
    {
        HashSet<TermId> nodes = [];
        foreach(EncodedTriple triple in graphStore.Match(TermId.None, TermId.None, TermId.None))
        {
            nodes.Add(triple.Subject);
            nodes.Add(triple.Object);
        }

        return nodes;
    }

    /// <summary>
    /// Converts an AST property-path expression to the encoded <see cref="Rdf.PropertyPath"/> the
    /// <see cref="PropertyPathEvaluator"/> consumes, over an explicit post-order stack (no recursion). Predicate
    /// IRIs resolve through the term dictionary; one absent from the data becomes a <see cref="TermId.None"/>
    /// predicate that matches nothing.
    /// </summary>
    /// <param name="root">The AST path expression.</param>
    /// <returns>The encoded property path.</returns>
    private PropertyPath ConvertPath(PropertyPathExpression root)
    {
        Dictionary<PropertyPathExpression, PropertyPath> results = new(ReferenceEqualityComparer.Instance);
        Stack<(PropertyPathExpression Node, bool Combine)> work = new();
        work.Push((root, Combine: false));

        while(work.Count > 0)
        {
            (PropertyPathExpression node, bool combine) = work.Pop();
            if(combine)
            {
                results[node] = CombinePath(node, results);

                continue;
            }

            IReadOnlyList<PropertyPathExpression> children = PathChildren(node);
            if(children.Count == 0)
            {
                results[node] = ConvertLeafPath(node);
            }
            else
            {
                work.Push((node, Combine: true));
                for(int i = children.Count - 1; i >= 0; i--)
                {
                    work.Push((children[i], Combine: false));
                }
            }
        }

        return results[root];
    }

    /// <summary>Returns the sub-paths of an AST path expression; empty for a single predicate or a negated set.</summary>
    /// <param name="node">The path expression.</param>
    /// <returns>The sub-paths.</returns>
    private static IReadOnlyList<PropertyPathExpression> PathChildren(PropertyPathExpression node)
    {
        return node switch
        {
            PathInverse inverse => [inverse.Inner],
            PathSequence sequence => sequence.Steps,
            PathAlternative alternative => alternative.Alternatives,
            PathZeroOrMore zeroOrMore => [zeroOrMore.Inner],
            PathOneOrMore oneOrMore => [oneOrMore.Inner],
            PathZeroOrOne zeroOrOne => [zeroOrOne.Inner],
            _ => []
        };
    }

    /// <summary>Converts a leaf path expression: a single predicate to a <see cref="PredicatePath"/>, or a negated property set to a <see cref="NegatedPropertySet"/>, over encoded ids.</summary>
    /// <param name="node">The leaf path expression.</param>
    /// <returns>The encoded path.</returns>
    private PropertyPath ConvertLeafPath(PropertyPathExpression node)
    {
        return node switch
        {
            PathPredicate predicate => new PredicatePath(EncodePredicate(predicate.Predicate)),
            PathNegatedSet negatedSet => ConvertNegatedSet(negatedSet),
            _ => throw new InvalidOperationException($"Path expression '{node.GetType().Name}' has sub-paths but reached the leaf converter.")
        };
    }

    /// <summary>Lowers a negated property set to its encoded form, partitioning its elements into the excluded forward and inverse predicate sets.</summary>
    /// <param name="negatedSet">The AST negated property set.</param>
    /// <returns>The encoded negated property set.</returns>
    private NegatedPropertySet ConvertNegatedSet(PathNegatedSet negatedSet)
    {
        ImmutableArray<IriId>.Builder forward = ImmutableArray.CreateBuilder<IriId>();
        ImmutableArray<IriId>.Builder inverse = ImmutableArray.CreateBuilder<IriId>();
        foreach(PathNegatedElement element in negatedSet.Elements)
        {
            ImmutableArray<IriId>.Builder target = element switch
            {
                PathNegatedForward => forward,
                PathNegatedInverse => inverse,
                _ => throw new InvalidOperationException($"Unknown negated property-set element '{element.GetType().Name}'.")
            };

            target.Add(EncodePredicate(element.Predicate));
        }

        return new NegatedPropertySet(forward.ToImmutable(), inverse.ToImmutable());
    }

    /// <summary>Resolves a predicate IRI to its encoded id; an IRI absent from the data resolves to <see cref="TermId.None"/> (matching nothing).</summary>
    /// <param name="predicate">The predicate IRI.</param>
    /// <returns>The encoded predicate id.</returns>
    private IriId EncodePredicate(IriRef predicate) => IriId.FromUnchecked(Dictionary.GetIdOrDefault(new NamedNode(predicate.Value)));

    /// <summary>Combines a composite path expression from its already-converted sub-paths.</summary>
    /// <param name="node">The composite path expression.</param>
    /// <param name="results">The map of already-converted sub-paths.</param>
    /// <returns>The encoded path.</returns>
    private static PropertyPath CombinePath(PropertyPathExpression node, Dictionary<PropertyPathExpression, PropertyPath> results)
    {
        return node switch
        {
            PathInverse inverse => new InversePath(results[inverse.Inner]),
            PathSequence sequence => new SequencePath(ToPathArray(sequence.Steps, results)),
            PathAlternative alternative => new AlternativePath(ToPathArray(alternative.Alternatives, results)),
            PathZeroOrMore zeroOrMore => new ZeroOrMorePath(results[zeroOrMore.Inner]),
            PathOneOrMore oneOrMore => new OneOrMorePath(results[oneOrMore.Inner]),
            PathZeroOrOne zeroOrOne => new ZeroOrOnePath(results[zeroOrOne.Inner]),
            _ => throw new InvalidOperationException($"Path expression '{node.GetType().Name}' has no combine rule.")
        };
    }

    /// <summary>Collects the converted sub-paths of a sequence/alternative into an immutable array, in order.</summary>
    /// <param name="steps">The AST sub-paths.</param>
    /// <param name="results">The map of already-converted sub-paths.</param>
    /// <returns>The converted sub-paths.</returns>
    private static ImmutableArray<PropertyPath> ToPathArray(IReadOnlyList<PropertyPathExpression> steps, Dictionary<PropertyPathExpression, PropertyPath> results)
    {
        ImmutableArray<PropertyPath>.Builder builder = ImmutableArray.CreateBuilder<PropertyPath>(steps.Count);
        foreach(PropertyPathExpression step in steps)
        {
            builder.Add(results[step]);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Joins two result tables (§18.6 Join). When both sides are columnar and share one or two variables that every
    /// row binds, the join stays columnar — a shared-key hash join over the encoded-id columns, no decode — and
    /// emits a columnar table; otherwise it bridges to the row form.
    /// </summary>
    /// <param name="left">The left table.</param>
    /// <param name="right">The right table.</param>
    /// <returns>The joined table.</returns>
    private static SolutionTable JoinSolutions(SolutionTable left, SolutionTable right)
    {
        if(left.Count == 0 || right.Count == 0)
        {
            return SolutionTable.Empty;
        }

        if(left.IsColumnar && right.IsColumnar && ColumnarOperators.TryJoin(left, right, out SolutionTable joined))
        {
            return joined;
        }

        return SolutionTable.FromRows(JoinRows(left.AsRows(), right.AsRows()));
    }

    /// <summary>
    /// The row form of join (§18.6 Join): the merge of every pair of compatible solutions, one from each side. A
    /// hash join keyed on the shared variables (O(n+m)) when one or two variables are shared and every row binds
    /// them; otherwise (a wider or empty shared set, or partial bindings) the nested-loop compatibility merge.
    /// </summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The joined solution sequence.</returns>
    private static List<SparqlSolution> JoinRows(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        if(left.Count == 0 || right.Count == 0)
        {
            return [];
        }

        SparqlVariable[] joinVariables = SharedVariables(left, right);
        if(!IsHashJoinEligible(joinVariables, left, right))
        {
            return NestedLoopJoinSolutions(left, right);
        }

        //Build the smaller side so the in-memory index stays the cheaper of the two; roles (which side is "left"
        //for Merge) are preserved regardless of which is physically built, since shared variables agree and the
        //merged binding set is the same union either way.
        bool buildLeft = left.Count <= right.Count;
        IReadOnlyList<SparqlSolution> buildSide = buildLeft ? left : right;
        IReadOnlyList<SparqlSolution> probeSide = buildLeft ? right : left;

        SolutionHashJoinIndex index = SolutionHashJoinIndex.Build(buildSide, joinVariables);
        List<SparqlSolution> joined = [];
        foreach(SparqlSolution probe in probeSide)
        {
            for(int rowId = index.FirstMatch(probe); rowId >= 0; rowId = index.NextMatch(rowId))
            {
                SparqlSolution build = index.RowAt(rowId);
                joined.Add(buildLeft ? Merge(build, probe) : Merge(probe, build));
            }
        }

        return joined;
    }

    /// <summary>The nested-loop join (§18.6 Join) used when the shared-variable hash join does not apply: the merge of every pair of compatible solutions.</summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The joined solution sequence.</returns>
    private static List<SparqlSolution> NestedLoopJoinSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> joined = [];
        foreach(SparqlSolution outer in left)
        {
            foreach(SparqlSolution inner in right)
            {
                if(AreCompatible(outer, inner))
                {
                    joined.Add(Merge(outer, inner));
                }
            }
        }

        return joined;
    }

    /// <summary>Unions two solution sequences (§18.6 Union): the multiset of all solutions of either side, left before right.</summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The unioned solution sequence.</returns>
    private static SolutionTable UnionSolutions(SolutionTable left, SolutionTable right)
    {
        //Both columnar: stay on the island, building merged columns over the union of the two schemas. A mixed pair
        //(one side already row-backed) materializes through the row form rather than re-encoding the row side.
        return left.IsColumnar && right.IsColumnar
            ? ColumnarOperators.Union(left, right)
            : SolutionTable.FromRows(UnionRows(left.AsRows(), right.AsRows()));
    }

    /// <summary>The row form of union (§18.6 Union): the multiset of all solutions of either side, left before right.</summary>
    /// <param name="left">The left solution rows.</param>
    /// <param name="right">The right solution rows.</param>
    /// <returns>The unioned solution rows.</returns>
    private static List<SparqlSolution> UnionRows(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> all = new(left.Count + right.Count);
        all.AddRange(left);
        all.AddRange(right);

        return all;
    }

    /// <summary>
    /// Subtracts the right table from the left (§18.6 Minus). When both are columnar and share one or two variables
    /// every row binds (or share none — the disjoint-domain exception), it stays columnar: an anti-join over the
    /// encoded key columns, no decode; otherwise it bridges to the row form.
    /// </summary>
    /// <param name="left">The left (kept) table.</param>
    /// <param name="right">The right (subtracting) table.</param>
    /// <returns>The left rows that survive the subtraction.</returns>
    private static SolutionTable MinusSolutions(SolutionTable left, SolutionTable right)
    {
        if(left.Count == 0)
        {
            return SolutionTable.Empty;
        }

        if(left.IsColumnar && right.IsColumnar && ColumnarOperators.TryMinus(left, right, out SolutionTable subtracted))
        {
            return subtracted;
        }

        return SolutionTable.FromRows(MinusRows(left.AsRows(), right.AsRows()));
    }

    /// <summary>
    /// The row form of Minus (§18.6): a left solution is removed only when some right solution is compatible with it
    /// <em>and</em> shares at least one variable. The shared-variable requirement is the spec's disjoint-domain
    /// exception — a left solution with no variable in common with any right solution is kept even though disjoint
    /// mappings are technically compatible.
    /// </summary>
    /// <param name="left">The left (kept) solution sequence.</param>
    /// <param name="right">The right (subtracting) solution sequence.</param>
    /// <returns>The left solutions that survive the subtraction.</returns>
    private static List<SparqlSolution> MinusRows(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        if(left.Count == 0)
        {
            return [];
        }

        SparqlVariable[] joinVariables = SharedVariables(left, right);

        //No variable is shared with any right solution, so the disjoint-domain exception keeps every left solution.
        if(joinVariables.Length == 0)
        {
            return [.. left];
        }

        if(!IsHashJoinEligible(joinVariables, left, right))
        {
            return NestedLoopMinusSolutions(left, right);
        }

        //In the fast path every left/right pair shares exactly the join variables (both sides bind them all), so a
        //left solution is subtracted precisely when some right solution carries the same join-variable values.
        SolutionHashJoinIndex index = SolutionHashJoinIndex.Build(right, joinVariables);
        List<SparqlSolution> kept = new(left.Count);
        foreach(SparqlSolution candidate in left)
        {
            if(index.FirstMatch(candidate) < 0)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>The nested-loop minus (§18.6 Minus) used when the shared-variable hash join does not apply.</summary>
    /// <param name="left">The left (kept) solution sequence.</param>
    /// <param name="right">The right (subtracting) solution sequence.</param>
    /// <returns>The left solutions that survive the subtraction.</returns>
    private static List<SparqlSolution> NestedLoopMinusSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> kept = new(left.Count);
        foreach(SparqlSolution candidate in left)
        {
            bool removed = false;
            foreach(SparqlSolution subtractor in right)
            {
                if(SharesVariable(candidate, subtractor) && AreCompatible(candidate, subtractor))
                {
                    removed = true;

                    break;
                }
            }

            if(!removed)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    /// <summary>Combines an expression-gated operator (<see cref="Filter"/>/<see cref="Extend"/>/<see cref="LeftJoin"/>) asynchronously, since its expression may carry <c>EXISTS</c>/<c>NOT EXISTS</c> that re-enter the engine (in the same active graph).</summary>
    /// <param name="op">The operator to combine (a filter, extend, or left join).</param>
    /// <param name="graph">The active graph the operator was evaluated under; any EXISTS sub-pattern re-enters in this graph.</param>
    /// <param name="results">The map of already-evaluated (operator, graph) keys to their solution sequences.</param>
    /// <param name="overlay">The evaluation's computed-term overlay a columnar Extend encodes through.</param>
    /// <param name="trace">The evaluation's execution trace.</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The operator's solution sequence.</returns>
    private async ValueTask<SolutionTable> CombineExpressionOperatorAsync(AlgebraOperator op, TermId graph, Dictionary<ResultKey, SolutionTable> results, ComputedTermOverlay overlay, SparqlExecutionTrace trace, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        switch(op)
        {
            case Filter filter:
            {
                //A term-(in)equality against a constant IRI evaluates on the encoded column directly (no decode),
                //keeping only survivors columnar — the selective FILTER the row path would decode-then-discard.
                SolutionTable filterInput = results[new ResultKey(filter.Input, graph)];
                SolutionTable filtered;
                if(filterInput.IsColumnar && TryColumnarTermFilter(filter.Condition, filterInput, out SolutionTable columnarFiltered))
                {
                    filtered = columnarFiltered;
                }
                else
                {
                    filtered = SolutionTable.FromRows(await ApplyFilterAsync(filter, filterInput.AsRows(), graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false));
                }

                trace.Emit(SparqlExecutionOperator.Filter, filtered, filterInput.Count, rowsRight: -1);

                return filtered;
            }

            case Extend extend:
            {
                //BIND a computed value as a new column, keeping the table columnar so a following Distinct/Slice/
                //Filter decodes only survivors. The expression is evaluated per row over the decoded referenced
                //values; its result is encoded through the overlay (a data id when the term exists, else a
                //query-local id). An EXISTS-bearing expression re-enters the engine per row and stays on the row path.
                SolutionTable extendInput = results[new ResultKey(extend.Input, graph)];
                SolutionTable extended;
                if(extendInput.IsColumnar && !ContainsExists(extend.Expression) && TryExtendColumnar(extend, extendInput, overlay, out SolutionTable columnarExtended))
                {
                    extended = columnarExtended;
                }
                else
                {
                    extended = SolutionTable.FromRows(await ApplyExtendAsync(extend, extendInput.AsRows(), graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false));
                }

                trace.Emit(SparqlExecutionOperator.Extend, extended, extendInput.Count, rowsRight: -1);

                return extended;
            }

            case LeftJoin leftJoin:
            {
                //A condition-free OPTIONAL over two columnar tables stays columnar (the outer join over encoded
                //key columns). An OPTIONAL carrying a lifted inner FILTER evaluates that condition per merged row
                //and stays on the row path.
                SolutionTable leftJoinLeft = results[new ResultKey(leftJoin.Left, graph)];
                SolutionTable leftJoinRight = results[new ResultKey(leftJoin.Right, graph)];
                SolutionTable leftJoined;
                if(leftJoin.Condition is null && leftJoinLeft.IsColumnar && leftJoinRight.IsColumnar && ColumnarOperators.TryLeftJoin(leftJoinLeft, leftJoinRight, out SolutionTable columnarLeftJoined))
                {
                    leftJoined = columnarLeftJoined;
                }
                else
                {
                    leftJoined = SolutionTable.FromRows(await LeftJoinSolutionsAsync(leftJoinLeft.AsRows(), leftJoinRight.AsRows(), leftJoin.Condition, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false));
                }

                trace.Emit(SparqlExecutionOperator.LeftJoin, leftJoined, leftJoinLeft.Count, leftJoinRight.Count);

                return leftJoined;
            }

            default:
            {
                throw new InvalidOperationException($"Operator '{op.GetType().Name}' is not an expression-gated operator.");
            }
        }
    }

    /// <summary>
    /// Recognises a <c>FILTER</c> condition of the form <c>?v = &lt;iri&gt;</c> / <c>&lt;iri&gt; = ?v</c> (and the
    /// <c>!=</c> forms) and evaluates it directly on the columnar input's encoded column, gathering survivors
    /// without decoding. Sound only for an IRI constant — for an IRI, SPARQL value equality coincides with term-id
    /// equality (an IRI is never value-equal to a different term), so a literal constant or a variable-to-variable
    /// comparison (which carry value semantics across lexical forms) is left to the row path. A condition that does
    /// not match the shape returns <see langword="false"/>, and the caller evaluates it row-by-row.
    /// </summary>
    /// <param name="condition">The filter condition.</param>
    /// <param name="input">The columnar input table.</param>
    /// <param name="result">Receives the surviving columnar table when the condition matched the columnar shape.</param>
    /// <returns><see langword="true"/> when the condition was evaluated columnar; otherwise <see langword="false"/>.</returns>
    private static bool TryColumnarTermFilter(ExpressionNode condition, SolutionTable input, out SolutionTable result)
    {
        result = SolutionTable.Empty;
        if(condition is not ComparisonExpression { Op: ComparisonOp.Equal or ComparisonOp.NotEqual } comparison)
        {
            return false;
        }

        (VariableExpression? variable, ConstantExpression? constant) = (comparison.Left, comparison.Right) switch
        {
            (VariableExpression v, ConstantExpression c) => (v, c),
            (ConstantExpression c, VariableExpression v) => (v, c),
            _ => (null, null)
        };

        if(variable is null || constant is null || constant.Value is not NamedNode iri)
        {
            return false;
        }

        int columnIndex = -1;
        IReadOnlyList<SparqlVariable> schema = input.Schema;
        for(int i = 0; i < schema.Count; i++)
        {
            if(schema[i] == variable.Variable)
            {
                columnIndex = i;

                break;
            }
        }

        //A variable absent from the schema is unbound in every row; both = and != are then a type error that drops
        //every row, so the result is empty — and the columnar shape was still "handled" (no row fallback needed).
        if(columnIndex < 0)
        {
            result = SolutionTable.Empty;

            return true;
        }

        uint termId = input.Dictionary.GetIdOrDefault(iri).Encoded;
        result = ColumnarOperators.FilterByTerm(input, columnIndex, termId, comparison.Op == ComparisonOp.Equal);

        return true;
    }

    /// <summary>
    /// Evaluates a <c>BIND</c>/<c>Extend</c> over a columnar input, appending the computed value as a new encoded
    /// column and keeping the table columnar. The expression is evaluated per row over the decoded row (an error
    /// leaves the new cell unbound, matching the row form), and each value is encoded through the overlay (a data
    /// id when the term exists, else a query-local overlay id). Declines (returns <see langword="false"/>, so the
    /// caller takes the row path) when the bound variable is already a column, or when the expression constructs a
    /// blank node — <c>BNODE</c> correlation depends on per-solution identity the row path links across BIND steps,
    /// which the transient per-row decode here does not carry.
    /// </summary>
    /// <param name="extend">The extend operator.</param>
    /// <param name="input">The columnar input table.</param>
    /// <param name="overlay">The evaluation's computed-term overlay the result values are encoded through.</param>
    /// <param name="result">Receives the extended columnar table when the columnar path applies.</param>
    /// <returns><see langword="true"/> when the extend was evaluated columnar; otherwise <see langword="false"/>.</returns>
    private bool TryExtendColumnar(Extend extend, SolutionTable input, ComputedTermOverlay overlay, out SolutionTable result)
    {
        result = SolutionTable.Empty;
        IReadOnlyList<SparqlVariable> inputSchema = input.Schema;
        for(int i = 0; i < inputSchema.Count; i++)
        {
            //A BIND introduces a fresh variable; a collision is unusual (and the row path's append semantics differ),
            //so leave it to the row path.
            if(inputSchema[i] == extend.Variable)
            {
                return false;
            }
        }

        if(ContainsBlankNodeConstructor(extend.Expression))
        {
            return false;
        }

        uint[] valueColumn = new uint[input.Count];
        for(int row = 0; row < input.Count; row++)
        {
            //A failed expression leaves the cell at 0 (unbound) — the row form likewise keeps the row with the
            //variable unbound.
            if(SparqlExpressionEvaluator.TryEvaluate(extend.Expression, input.DecodeRow(row), ExpressionContext, out RdfTerm value))
            {
                valueColumn[row] = overlay.Encode(value);
            }
        }

        List<SparqlVariable> schema = new(inputSchema.Count + 1);
        schema.AddRange(inputSchema);
        schema.Add(extend.Variable);

        uint[][] columns = new uint[schema.Count][];
        for(int column = 0; column < inputSchema.Count; column++)
        {
            columns[column] = input.ColumnArray(column);
        }

        columns[inputSchema.Count] = valueColumn;
        result = SolutionTable.Columnar(schema, columns, input.Count, input.Dictionary, overlay);

        return true;
    }

    /// <summary>Whether an expression constructs a blank node (<c>BNODE</c>), whose per-solution correlation the columnar Extend cannot preserve.</summary>
    /// <param name="expression">The expression to scan.</param>
    /// <returns><see langword="true"/> when a <c>BNODE</c> built-in appears anywhere in the expression.</returns>
    private static bool ContainsBlankNodeConstructor(ExpressionNode expression)
    {
        foreach(ExpressionNode node in ExpressionWalker.Traverse(expression))
        {
            if(node is BuiltInCallExpression { Function: BuiltInFunction.BNode })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Keeps the solutions whose <c>FILTER</c> condition has effective boolean value true (§18.6 Filter); an error or non-boolean value drops the solution. Any <c>EXISTS</c>/<c>NOT EXISTS</c> in the condition is resolved per solution first.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="graph">The active graph any EXISTS sub-pattern in the condition re-enters in.</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The surviving solutions.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> ApplyFilterAsync(Filter filter, IReadOnlyList<SparqlSolution> input, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        //EXISTS resolution allocates and re-walks the condition per solution; do it only when the condition carries
        //one. An EXISTS-free condition evaluates directly against each solution.
        bool hasExists = ContainsExists(filter.Condition);
        List<SparqlSolution> kept = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            ExpressionNode condition = hasExists
                ? await ResolveExistsAsync(filter.Condition, solution, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
                : filter.Condition;
            if(SparqlExpressionEvaluator.Satisfies(condition, solution, ExpressionContext))
            {
                kept.Add(solution);
            }
        }

        return kept;
    }

    /// <summary>Binds an expression's value to a variable on each solution (§18.6 Extend / <c>BIND</c>); a solution whose expression errs is kept with the variable left unbound. Any <c>EXISTS</c>/<c>NOT EXISTS</c> in the expression is resolved per solution first.</summary>
    /// <param name="extend">The extend.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="graph">The active graph any EXISTS sub-pattern in the expression re-enters in.</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The extended solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> ApplyExtendAsync(Extend extend, IReadOnlyList<SparqlSolution> input, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        bool hasExists = ContainsExists(extend.Expression);
        List<SparqlSolution> extended = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            ExpressionNode expression = hasExists
                ? await ResolveExistsAsync(extend.Expression, solution, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
                : extend.Expression;
            if(!SparqlExpressionEvaluator.TryEvaluate(expression, solution, ExpressionContext, out RdfTerm value))
            {
                extended.Add(solution);

                continue;
            }

            List<SparqlBinding> bindings = new(solution.Bindings.Count + 1);
            bindings.AddRange(solution.Bindings);
            bindings.Add(new SparqlBinding(extend.Variable, value));
            SparqlSolution extendedSolution = new(bindings);

            //The extended row is a new object but the same solution for BNODE correlation: keep its per-row blank-node
            //scope so BNODE(key) before and after the extend (across a chain of projection/BIND extends) agrees.
            ExpressionContext.BlankNodeScope.Link(solution, extendedSolution);
            extended.Add(extendedSolution);
        }

        return extended;
    }

    /// <summary>
    /// Evaluates an <c>OPTIONAL</c> (§18.6 LeftJoin): each left solution is extended by every compatible right
    /// solution that also satisfies the optional condition, and a left solution with no such extension is kept
    /// unchanged.
    /// </summary>
    /// <param name="left">The required (left) solution sequence.</param>
    /// <param name="right">The optional (right) solution sequence.</param>
    /// <param name="condition">The optional join condition (the lifted inner <c>FILTER</c>), or <see langword="null"/> when there is none.</param>
    /// <param name="graph">The active graph any EXISTS sub-pattern in the condition re-enters in.</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The left-joined solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> LeftJoinSolutionsAsync(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right, ExpressionNode? condition, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        //With no right solutions to extend by, every left solution is kept unchanged.
        if(right.Count == 0)
        {
            return [.. left];
        }

        SparqlVariable[] joinVariables = SharedVariables(left, right);
        if(!IsHashJoinEligible(joinVariables, left, right))
        {
            return await NestedLoopLeftJoinSolutionsAsync(left, right, condition, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);
        }

        //Index the right side on the shared variables; each left solution probes for its compatible extensions
        //rather than scanning the whole right sequence. The per-pair condition handling is unchanged: a left
        //solution stays unextended (kept as-is) when no compatible right solution also satisfies the condition.
        bool conditionHasExists = condition is not null && ContainsExists(condition);
        SolutionHashJoinIndex index = SolutionHashJoinIndex.Build(right, joinVariables);
        List<SparqlSolution> result = [];
        foreach(SparqlSolution outer in left)
        {
            bool extended = false;
            for(int rowId = index.FirstMatch(outer); rowId >= 0; rowId = index.NextMatch(rowId))
            {
                SparqlSolution merged = Merge(outer, index.RowAt(rowId));
                ExpressionNode? resolved = condition is null ? null
                    : conditionHasExists ? await ResolveExistsAsync(condition, merged, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
                    : condition;
                if(resolved is null || SparqlExpressionEvaluator.Satisfies(resolved, merged, ExpressionContext))
                {
                    result.Add(merged);
                    extended = true;
                }
            }

            if(!extended)
            {
                result.Add(outer);
            }
        }

        return result;
    }

    /// <summary>The nested-loop left join (§18.6 LeftJoin) used when the shared-variable hash join does not apply.</summary>
    /// <param name="left">The required (left) solution sequence.</param>
    /// <param name="right">The optional (right) solution sequence.</param>
    /// <param name="condition">The optional join condition (the lifted inner <c>FILTER</c>), or <see langword="null"/> when there is none.</param>
    /// <param name="graph">The active graph any EXISTS sub-pattern in the condition re-enters in.</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The left-joined solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> NestedLoopLeftJoinSolutionsAsync(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right, ExpressionNode? condition, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        bool conditionHasExists = condition is not null && ContainsExists(condition);
        List<SparqlSolution> result = [];
        foreach(SparqlSolution outer in left)
        {
            bool extended = false;
            foreach(SparqlSolution inner in right)
            {
                if(!AreCompatible(outer, inner))
                {
                    continue;
                }

                SparqlSolution merged = Merge(outer, inner);
                ExpressionNode? resolved = condition is null ? null
                    : conditionHasExists ? await ResolveExistsAsync(condition, merged, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false)
                    : condition;
                if(resolved is null || SparqlExpressionEvaluator.Satisfies(resolved, merged, ExpressionContext))
                {
                    result.Add(merged);
                    extended = true;
                }
            }

            if(!extended)
            {
                result.Add(outer);
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates an <c>AggregateJoin</c> (§18.5): partitions the input into groups by the wrapped <see cref="Group"/>'s
    /// keys, then emits one solution per group binding the named grouping keys and each aggregate's per-group result.
    /// When the input is columnar and every grouping key is a plain variable (one or two of them), the partition runs
    /// on the encoded key columns in one hash pass; otherwise it bridges to the row partition. An empty <c>GROUP BY</c>
    /// (implicit grouping) always yields exactly one group, even over no input, so <c>COUNT(*)</c> over an empty
    /// pattern is 0. The aggregate fold itself decodes each group's members (its math is over RDF terms).
    /// </summary>
    /// <param name="aggregateJoin">The aggregate-join operator (its input is the grouping operator).</param>
    /// <param name="input">The grouped operand's table (the Group passthrough).</param>
    /// <param name="context">The seams the grouping-key and aggregate expressions' non-pure functions consume.</param>
    /// <returns>One solution per group.</returns>
    private static SolutionTable EvaluateAggregateJoin(AggregateJoin aggregateJoin, SolutionTable input, SparqlExpressionContext context)
    {
        IReadOnlyList<GroupCondition> keys = aggregateJoin.Input is Group group ? group.Keys : [];
        List<AggregateGroup> groups = input.IsColumnar && TryPartitionColumnar(keys, input, out List<AggregateGroup> columnarGroups)
            ? columnarGroups
            : Partition(keys, input.AsRows(), context);

        //Implicit grouping (no keys) is always a single group, even with no input rows.
        if(keys.Count == 0 && groups.Count == 0)
        {
            groups.Add(new AggregateGroup([], []));
        }

        List<SparqlSolution> result = new(groups.Count);
        foreach(AggregateGroup aggregateGroup in groups)
        {
            List<SparqlBinding> bindings = new(aggregateGroup.NamedKeys);
            foreach(AggregateBinding aggregation in aggregateJoin.Aggregations)
            {
                if(SparqlExpressionEvaluator.EvaluateAggregate(aggregation.Aggregate, aggregateGroup.Members, context) is RdfTerm value)
                {
                    bindings.Add(new SparqlBinding(aggregation.Variable, value));
                }
            }

            result.Add(new SparqlSolution(bindings));
        }

        return SolutionTable.FromRows(result);
    }

    /// <summary>
    /// Partitions a columnar table into aggregate groups by the grouping keys in one hash pass over the encoded key
    /// columns — O(n), against the row partition's O(n · groups) scan — keeping first-appearance group order. Each
    /// group's members are decoded for the aggregate fold (its math is over RDF terms). Applies only when every key
    /// is a plain variable and there are at most two of them (the packed-key width); returns <see langword="false"/>
    /// otherwise, leaving the caller to bridge to the row partition.
    /// </summary>
    /// <param name="keys">The grouping conditions.</param>
    /// <param name="input">The columnar input table.</param>
    /// <param name="groups">Receives the groups (first-appearance order) when the columnar partition applies.</param>
    /// <returns><see langword="true"/> when partitioned columnar; otherwise <see langword="false"/>.</returns>
    private static bool TryPartitionColumnar(IReadOnlyList<GroupCondition> keys, SolutionTable input, out List<AggregateGroup> groups)
    {
        groups = [];
        if(keys.Count > 2)
        {
            return false;
        }

        IReadOnlyList<SparqlVariable> schema = input.Schema;
        SparqlVariable[] keyVariables = new SparqlVariable[keys.Count];
        int[] keyColumns = new int[keys.Count];
        for(int i = 0; i < keys.Count; i++)
        {
            if(keys[i] is not GroupVariable groupVariable)
            {
                return false;
            }

            keyVariables[i] = groupVariable.Variable;
            keyColumns[i] = -1;
            for(int column = 0; column < schema.Count; column++)
            {
                if(schema[column] == groupVariable.Variable)
                {
                    keyColumns[i] = column;

                    break;
                }
            }
        }

        Dictionary<ulong, AggregateGroup> byKey = new(input.Count);
        for(int row = 0; row < input.Count; row++)
        {
            ulong key = PackGroupKey(input, keyColumns, row);
            if(!byKey.TryGetValue(key, out AggregateGroup? aggregateGroup))
            {
                //First sight of this key: capture the named-key bindings from this row (every member shares them).
                RdfTerm?[] keyValues = new RdfTerm?[keys.Count];
                List<SparqlBinding> namedKeys = [];
                for(int i = 0; i < keys.Count; i++)
                {
                    RdfTerm? value = keyColumns[i] < 0 ? null : input.DecodeCell(keyColumns[i], row);
                    keyValues[i] = value;
                    if(value is RdfTerm term)
                    {
                        namedKeys.Add(new SparqlBinding(keyVariables[i], term));
                    }
                }

                aggregateGroup = new AggregateGroup(keyValues, namedKeys);
                byKey[key] = aggregateGroup;
                groups.Add(aggregateGroup);
            }

            aggregateGroup.Members.Add(input.DecodeRow(row));
        }

        return true;
    }

    /// <summary>Packs a row's one or two grouping-key encoded ids into a key (an absent key variable, or an unbound cell, contributes <c>0</c>; the two id ranges are a faithful 64-bit composite, so distinct key tuples never collide). No keys packs to <c>0</c> — the single implicit group.</summary>
    /// <param name="input">The columnar table.</param>
    /// <param name="keyColumns">The grouping-key column indices (<c>-1</c> for a variable absent from the schema).</param>
    /// <param name="row">The row index.</param>
    /// <returns>The packed grouping key.</returns>
    private static ulong PackGroupKey(SolutionTable input, int[] keyColumns, int row)
    {
        if(keyColumns.Length == 0)
        {
            return 0;
        }

        ulong first = keyColumns[0] < 0 ? 0u : input.ColumnArray(keyColumns[0])[row];
        if(keyColumns.Length == 1)
        {
            return first << 32;
        }

        ulong second = keyColumns[1] < 0 ? 0u : input.ColumnArray(keyColumns[1])[row];

        return (first << 32) | second;
    }

    /// <summary>Partitions solutions into groups by their grouping-key values, preserving first-appearance order of groups.</summary>
    /// <param name="keys">The grouping conditions.</param>
    /// <param name="input">The solutions to partition.</param>
    /// <param name="context">The seams the grouping-key expressions' non-pure functions consume.</param>
    /// <returns>The groups, each carrying its named-key bindings and member solutions.</returns>
    private static List<AggregateGroup> Partition(IReadOnlyList<GroupCondition> keys, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        List<AggregateGroup> groups = [];
        foreach(SparqlSolution solution in input)
        {
            RdfTerm?[] keyValues = new RdfTerm?[keys.Count];
            List<SparqlBinding> namedKeys = [];
            for(int i = 0; i < keys.Count; i++)
            {
                (RdfTerm? value, SparqlVariable? named) = EvaluateGroupKey(keys[i], solution, context);
                keyValues[i] = value;
                if(named is SparqlVariable variable && value is RdfTerm term)
                {
                    namedKeys.Add(new SparqlBinding(variable, term));
                }
            }

            AggregateGroup? match = null;
            foreach(AggregateGroup candidate in groups)
            {
                if(KeysEqual(candidate.KeyValues, keyValues))
                {
                    match = candidate;

                    break;
                }
            }

            if(match is null)
            {
                groups.Add(new AggregateGroup(keyValues, namedKeys) { Members = { solution } });
            }
            else
            {
                match.Members.Add(solution);
            }
        }

        return groups;
    }

    /// <summary>Evaluates one grouping condition over a solution, returning the key value and the variable it names (if any).</summary>
    /// <param name="condition">The grouping condition.</param>
    /// <param name="solution">The solution.</param>
    /// <param name="context">The seams the grouping expression's non-pure functions consume.</param>
    /// <returns>The key value (or <see langword="null"/> when unbound/errored) and the named variable the group binds, or <see langword="null"/> for a bare grouping expression.</returns>
    private static (RdfTerm? Value, SparqlVariable? Named) EvaluateGroupKey(GroupCondition condition, SparqlSolution solution, SparqlExpressionContext context)
    {
        switch(condition)
        {
            case GroupVariable variable:
            {
                return (solution.TryGetValue(variable.Variable, out RdfTerm value) ? value : null, variable.Variable);
            }

            case GroupExpressionAs expressionAs:
            {
                return (SparqlExpressionEvaluator.TryEvaluate(expressionAs.Expression, solution, context, out RdfTerm value) ? value : null, expressionAs.AsVariable);
            }

            case GroupExpression expression:
            {
                return (SparqlExpressionEvaluator.TryEvaluate(expression.Expression, solution, context, out RdfTerm value) ? value : null, null);
            }

            default:
            {
                throw new InvalidOperationException($"Unexpected grouping-condition kind {condition.GetType().Name} during SPARQL evaluation.");
            }
        }
    }

    /// <summary>Returns whether two grouping-key tuples are equal position-by-position (unbound matches unbound; bound matches an equal term).</summary>
    /// <param name="left">The first key tuple.</param>
    /// <param name="right">The second key tuple.</param>
    /// <returns><see langword="true"/> when the tuples are equal.</returns>
    private static bool KeysEqual(RdfTerm?[] left, RdfTerm?[] right)
    {
        if(left.Length != right.Length)
        {
            return false;
        }

        for(int i = 0; i < left.Length; i++)
        {
            if(left[i] is null != (right[i] is null))
            {
                return false;
            }

            if(left[i] is RdfTerm leftTerm && !leftTerm.Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the variables shared by two solution sequences (the intersection of their schemas, each schema being
    /// the union of the variables bound across that side's solutions), in first-appearance order on the right side.
    /// These are the candidate join variables: a variable bound in only one side can never conflict, so compatibility
    /// turns entirely on the shared ones.
    /// </summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The shared variables.</returns>
    private static SparqlVariable[] SharedVariables(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        HashSet<SparqlVariable> leftVariables = [];
        foreach(SparqlSolution solution in left)
        {
            foreach(SparqlBinding binding in solution.Bindings)
            {
                leftVariables.Add(binding.Variable);
            }
        }

        HashSet<SparqlVariable> added = [];
        List<SparqlVariable> shared = [];
        foreach(SparqlSolution solution in right)
        {
            foreach(SparqlBinding binding in solution.Bindings)
            {
                if(leftVariables.Contains(binding.Variable) && added.Add(binding.Variable))
                {
                    shared.Add(binding.Variable);
                }
            }
        }

        return [.. shared];
    }

    /// <summary>
    /// Returns whether a hash join keyed on <paramref name="joinVariables"/> applies: the index supports one or two
    /// join variables, and the keying is sound only when every solution on both sides binds all of them (so equal
    /// keys exactly capture compatibility). Any other shape routes to the nested-loop fallback.
    /// </summary>
    /// <param name="joinVariables">The shared (candidate join) variables.</param>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns><see langword="true"/> when the hash join is applicable.</returns>
    private static bool IsHashJoinEligible(SparqlVariable[] joinVariables, IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        return joinVariables.Length is 1 or 2 && AllBind(left, joinVariables) && AllBind(right, joinVariables);
    }

    /// <summary>Returns whether every solution in a sequence binds all of the given variables.</summary>
    /// <param name="solutions">The solution sequence.</param>
    /// <param name="variables">The variables that must each be bound.</param>
    /// <returns><see langword="true"/> when no solution leaves any of the variables unbound.</returns>
    private static bool AllBind(IReadOnlyList<SparqlSolution> solutions, SparqlVariable[] variables)
    {
        foreach(SparqlSolution solution in solutions)
        {
            foreach(SparqlVariable variable in variables)
            {
                if(!solution.TryGetValue(variable, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Returns whether two solutions are compatible (§18.1): every variable bound in both maps to the same term.</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when the solutions agree on every shared variable.</returns>
    internal static bool AreCompatible(SparqlSolution left, SparqlSolution right)
    {
        foreach(SparqlBinding binding in left.Bindings)
        {
            if(right.TryGetValue(binding.Variable, out RdfTerm value) && !value.Equals(binding.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether two solutions bind at least one variable in common (their domains intersect).</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when some variable is bound in both.</returns>
    internal static bool SharesVariable(SparqlSolution left, SparqlSolution right)
    {
        foreach(SparqlBinding binding in left.Bindings)
        {
            if(right.TryGetValue(binding.Variable, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Merges two compatible solutions: every binding of <paramref name="left"/>, plus each binding of <paramref name="right"/> for a variable the left does not bind.</summary>
    /// <param name="left">The left solution.</param>
    /// <param name="right">The right solution.</param>
    /// <returns>The merged solution.</returns>
    internal static SparqlSolution Merge(SparqlSolution left, SparqlSolution right)
    {
        List<SparqlBinding> merged = new(left.Bindings.Count + right.Bindings.Count);
        merged.AddRange(left.Bindings);
        foreach(SparqlBinding binding in right.Bindings)
        {
            if(!left.TryGetValue(binding.Variable, out _))
            {
                merged.Add(binding);
            }
        }

        return new SparqlSolution(merged);
    }

    /// <summary>Returns whether an expression carries any <c>EXISTS</c>/<c>NOT EXISTS</c>; checked once per operator so the per-solution resolution (which allocates and re-walks the expression) is skipped when there is nothing to resolve.</summary>
    /// <param name="expression">The expression to scan.</param>
    /// <returns><see langword="true"/> when some sub-expression is an <c>EXISTS</c> or <c>NOT EXISTS</c>.</returns>
    internal static bool ContainsExists(ExpressionNode expression)
    {
        foreach(ExpressionNode node in ExpressionWalker.Traverse(expression))
        {
            if(node is ExistsExpression or NotExistsExpression)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves every <c>EXISTS</c>/<c>NOT EXISTS</c> in an expression for one solution and rewrites them to the
    /// constant boolean they evaluate to, yielding an <c>EXISTS</c>-free expression the synchronous evaluator can
    /// handle. Returns the expression unchanged when it carries no <c>EXISTS</c> (the fast path).
    /// </summary>
    /// <param name="expression">The expression (a <c>FILTER</c> condition, <c>BIND</c> expression, or join condition).</param>
    /// <param name="solution">The current solution the <c>EXISTS</c> sub-patterns are evaluated under.</param>
    /// <param name="graph">The active graph the EXISTS sub-patterns are evaluated in.</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry (each site compiles once and is shared by all outer rows).</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The expression with each <c>EXISTS</c>/<c>NOT EXISTS</c> replaced by its constant boolean result.</returns>
    private async ValueTask<ExpressionNode> ResolveExistsAsync(ExpressionNode expression, SparqlSolution solution, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        Dictionary<ExpressionNode, bool> truths = new(ReferenceEqualityComparer.Instance);
        foreach(ExpressionNode node in ExpressionWalker.Traverse(expression))
        {
            switch(node)
            {
                case ExistsExpression exists:
                {
                    truths[exists] = await ExistsAsync(exists, exists.Inner, solution, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);

                    break;
                }

                case NotExistsExpression notExists:
                {
                    truths[notExists] = !await ExistsAsync(notExists, notExists.Inner, solution, graph, existsRegistry, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        if(truths.Count == 0)
        {
            return expression;
        }

        return ExpressionWalker.Transform(
            expression,
            node => truths.TryGetValue(node, out bool truth) ? new ConstantExpression(node.Span, truth ? BooleanTrue : BooleanFalse) : node);
    }

    /// <summary>
    /// Evaluates an <c>EXISTS</c> sub-pattern under a solution (§18.6) through the site's compile-once plan:
    /// the synthetic <c>SELECT * { pattern }</c> is synthesized, normalized, and translated ONCE per site per
    /// evaluation (the registry), and each outer row then costs only the per-binding evaluation — on-mode the
    /// reused probe pipeline with the first-compatible-row short-circuit, off-mode the incumbent
    /// trailing-<c>VALUES</c> materialising path over the cached normalized AST.
    /// </summary>
    /// <param name="site">The <c>EXISTS</c>/<c>NOT EXISTS</c> expression node — the registry key.</param>
    /// <param name="pattern">The <c>EXISTS</c> sub-pattern.</param>
    /// <param name="solution">The current solution to pre-bind.</param>
    /// <param name="graph">The active graph the sub-pattern is evaluated in (so EXISTS inside a GRAPH form queries that graph).</param>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth; the defensive nesting cap reads it.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the pattern has at least one solution compatible with the current bindings.</returns>
    /// <exception cref="NotSupportedException">The EXISTS nesting exceeds <see cref="SparqlTranslator.MaxExistsNestingDepth"/> (programmatically-constructed algebra; the parser bounds parsed queries identically).</exception>
    private async ValueTask<bool> ExistsAsync(ExpressionNode site, GraphPattern pattern, SparqlSolution solution, TermId graph, ExistsRegistry existsRegistry, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        //The defensive runtime arm of the uniform nesting cap: every EXISTS level stacks driver re-entry
        //frames in both modes, so unbounded nesting is a stack hazard the cap converts to a clean error.
        //Parsed queries never reach this check — the parser records SP0053 and recovers at the same bound.
        if(existsDepth >= SparqlTranslator.MaxExistsNestingDepth)
        {
            throw new NotSupportedException($"EXISTS/NOT EXISTS nesting exceeds the maximum supported depth of {SparqlTranslator.MaxExistsNestingDepth}.");
        }

        ExistsPlan plan = GetOrCreateExistsPlan(existsRegistry, site, graph, pattern);
        if(EnginePolicy.PreferStreamingOperators)
        {
            return await AnyExistsAsync(plan, solution, graph, existsRegistry.Rewrites, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);
        }

        return await EvaluateExistsMaterialisedAsync(plan, solution, graph, existsRegistry.Rewrites, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the site's compile-once plan, creating it on first sight: the synthetic
    /// <c>SELECT * { pattern }</c> (no trailing <c>VALUES</c>) is normalized into a plan-owned pool and
    /// translated once; the emptiness-preserving core under the synthesized star projection and (on-mode)
    /// the seeding plan are derived from it. The registry owns the plan's deterministic disposal.
    /// </summary>
    /// <param name="existsRegistry">The evaluation's EXISTS plan registry.</param>
    /// <param name="site">The <c>EXISTS</c>/<c>NOT EXISTS</c> expression node — the registry key.</param>
    /// <param name="graph">The active graph the site is evaluated under (part of the key: a compiled probe binds its graph).</param>
    /// <param name="pattern">The <c>EXISTS</c> sub-pattern.</param>
    /// <returns>The site's plan.</returns>
    private ExistsPlan GetOrCreateExistsPlan(ExistsRegistry existsRegistry, ExpressionNode site, TermId graph, GraphPattern pattern)
    {
        if(existsRegistry.TryGet(site, graph, out ExistsPlan existing))
        {
            return existing;
        }

        Utf8StringPool pool = new();
        SparqlQuery normalized;
        try
        {
            normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(SynthesizeExistsQuery(pattern, SingleEmptySolution[0]));
        }
        catch
        {
            pool.Dispose();

            throw;
        }

        //The rewrite pipeline applies ONCE per site, to the inner algebra only; the core is then PEELED from
        //the already-rewritten inner (never rewritten as an independent second root), so inner and core stay
        //structurally coupled and the seeding plan's bare-BGP requirement reads the same tree the
        //materialised path evaluates. Rewrite events emit into the spawning evaluation's trace.
        AlgebraOperator inner;
        try
        {
            inner = SparqlTranslator.Translate(normalized, ExtensionFunctions.AggregateIris);
            AlgebraRewriteContext context = CreateRewriteContext();
            inner = existsRegistry.Rewrites.Rewrite(inner, in context, existsRegistry.Trace);
        }
        catch
        {
            pool.Dispose();

            throw;
        }

        AlgebraOperator core = inner is Project project ? project.Input : inner;
        BgpSeedPlan? seedPlan = EnginePolicy.PreferStreamingOperators ? BgpSeedPlan.TryBuild(core, Machinery) : null;
        ExistsPlan plan = new(pool, normalized, inner, core, seedPlan);
        existsRegistry.Add(site, graph, plan);

        return plan;
    }

    /// <summary>
    /// The on-mode per-binding <c>EXISTS</c> probe: the site's pipeline is compiled ONCE (seeded over the
    /// skeleton where the seed plan applies, else over the emptiness-preserving core) and re-armed per
    /// binding through <see cref="Streaming.SolutionCursor.ResetAsync"/>; the answer is the first pulled row
    /// COMPATIBLE with the pre-binding (shared-variable agreement — the trailing-<c>VALUES</c> join's
    /// semantics with the single pre-binding row as probe). The pipeline is owned by the registry entry and
    /// torn down by the evaluation's <c>finally</c>, never per pull, so intermediate bindings recycle
    /// through Reset (which owns the prior binding's source disposal). A budget-declined compile answers
    /// through the materialising path, drawing from the same remaining budget.
    /// </summary>
    /// <param name="plan">The site's plan.</param>
    /// <param name="solution">The pre-binding.</param>
    /// <param name="graph">The active graph.</param>
    /// <param name="rewrites">The evaluation's resolved rewrite pipeline, carried for the probe pipeline's own nested EXISTS plan builds.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth.</param>
    /// <param name="cancellationToken">A token that aborts the probe.</param>
    /// <returns><see langword="true"/> when a compatible inner row exists.</returns>
    private async ValueTask<bool> AnyExistsAsync(ExistsPlan plan, SparqlSolution solution, TermId graph, AlgebraRewritePipeline rewrites, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        if(!plan.PipelineCompileAttempted)
        {
            plan.PipelineCompileAttempted = true;
            plan.Pipeline = plan.SeedPlan is BgpSeedPlan seedPlan
                ? StreamingPipeline.TryCompileSeededExists(Machinery, seedPlan, graph, cursorBudget)
                : StreamingPipeline.TryCompile(this, Machinery, plan.CoreAlgebra, graph, cursorBudget, existsDepth + 1, rewrites);
        }

        if(plan.Pipeline is not StreamingPipeline pipeline)
        {
            return await EvaluateExistsMaterialisedAsync(plan, solution, graph, rewrites, cursorBudget, existsDepth, cancellationToken).ConfigureAwait(false);
        }

        await pipeline.Root.ResetAsync(solution).ConfigureAwait(false);
        while(await pipeline.Root.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            if(AreCompatible(solution, pipeline.Root.Current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The materialising per-binding <c>EXISTS</c> evaluation over the site's cached normalized AST: the
    /// per-solution query is the cached <c>SELECT * { pattern }</c> with the binding's single-row trailing
    /// <c>VALUES</c>, so only the pure translation and the evaluation run per row — byte-equivalent algebra
    /// to a fresh per-solution synthesize/normalize/translate, with the parse, normalization, and
    /// per-solution pool paid once per site (the off-mode evaluation path itself is unchanged).
    /// </summary>
    /// <param name="plan">The site's plan.</param>
    /// <param name="solution">The pre-binding.</param>
    /// <param name="graph">The active graph.</param>
    /// <param name="rewrites">The evaluation's resolved rewrite pipeline, threaded into the re-entered driver for its nested plan builds.</param>
    /// <param name="cursorBudget">The evaluation's shared cursor-budget cell, threaded into the re-entered driver.</param>
    /// <param name="existsDepth">The evaluation's EXISTS re-entry depth; the re-entry runs one level deeper.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the pattern has at least one solution compatible with the current bindings.</returns>
    private async ValueTask<bool> EvaluateExistsMaterialisedAsync(ExistsPlan plan, SparqlSolution solution, TermId graph, AlgebraRewritePipeline rewrites, CursorBudget cursorBudget, int existsDepth, CancellationToken cancellationToken)
    {
        //The per-binding rebuilt algebra runs UN-rewritten: rules are answer-preserving and the site's plan
        //already compiled the rewritten inner once, so per-row rewriting would only re-pay the pass on a
        //bounded path whose answer it cannot change.
        AlgebraOperator algebra = solution.Bindings.Count == 0
            ? plan.InnerAlgebra
            : SparqlTranslator.Translate(plan.NormalizedQuery with { Values = BuildSolutionValues(solution) }, ExtensionFunctions.AggregateIris);

        //An EXISTS sub-evaluation is an internal detail; it reuses the engine's construction-time trace handler (if
        //any), not a per-call one, under a correlation of its own rather than the outer run's.
        SparqlExecutionTrace trace = CreateEvaluationTrace();
        SolutionTable solutions = await EvaluateInGraphAsync(algebra, graph, trace, rewrites, cursorBudget, existsDepth + 1, cancellationToken).ConfigureAwait(false);

        return solutions.Count > 0;
    }

    /// <summary>Wraps an <c>EXISTS</c> sub-pattern in a minimal <c>SELECT *</c> query, pre-binding the current solution as a trailing single-row <c>VALUES</c>.</summary>
    /// <param name="pattern">The sub-pattern to wrap.</param>
    /// <param name="solution">The solution to pre-bind, or empty for no pre-binding.</param>
    /// <returns>The synthetic query.</returns>
    private static SparqlQuery SynthesizeExistsQuery(GraphPattern pattern, SparqlSolution solution)
    {
        return new SparqlQuery(
            SourceSpan.None,
            new Prologue(SourceSpan.None, [], [], []),
            new SelectQuery(SourceSpan.None, IsDistinct: false, IsReduced: false, IsStar: true, []),
            new DatasetClause(SourceSpan.None, [], []),
            new WhereClause(SourceSpan.None, pattern),
            new SolutionModifier(SourceSpan.None, null, null, null, null, null),
            BuildSolutionValues(solution));
    }

    /// <summary>Builds the single-row trailing <c>VALUES</c> that pre-binds a solution's bindings, or <see langword="null"/> when the solution binds nothing.</summary>
    /// <param name="solution">The solution to pre-bind.</param>
    /// <returns>The inline-data block, or <see langword="null"/>.</returns>
    private static ValuesClause? BuildSolutionValues(SparqlSolution solution)
    {
        if(solution.Bindings.Count == 0)
        {
            return null;
        }

        List<SparqlVariable> variables = new(solution.Bindings.Count);
        List<RdfTerm?> row = new(solution.Bindings.Count);
        foreach(SparqlBinding binding in solution.Bindings)
        {
            variables.Add(binding.Variable);
            row.Add(binding.Value);
        }

        return new ValuesClause(SourceSpan.None, variables, [row]);
    }

    /// <summary>One group during aggregation: its grouping-key values (for partitioning), the named-key bindings it contributes to the output solution, and its member solutions.</summary>
    private sealed class AggregateGroup
    {
        /// <summary>Constructs a group with its key tuple and named-key bindings; members start empty.</summary>
        /// <param name="keyValues">The grouping-key values, in condition order (an entry is <see langword="null"/> for an unbound/errored key).</param>
        /// <param name="namedKeys">The bindings the group's named keys contribute to its output solution.</param>
        public AggregateGroup(RdfTerm?[] keyValues, List<SparqlBinding> namedKeys)
        {
            KeyValues = keyValues;
            NamedKeys = namedKeys;
        }

        /// <summary>The grouping-key values used to match solutions into this group.</summary>
        public RdfTerm?[] KeyValues { get; }

        /// <summary>The named grouping-key bindings the group contributes to its output solution.</summary>
        public List<SparqlBinding> NamedKeys { get; }

        /// <summary>The solutions partitioned into this group.</summary>
        public List<SparqlSolution> Members { get; } = [];
    }

    /// <summary>Verifies every operator in the algebra is one this slice can execute, throwing a descriptive <see cref="NotSupportedException"/> on the first that is not.</summary>
    /// <param name="algebra">The algebra to check.</param>
    /// <exception cref="NotSupportedException">An operator outside this slice's supported set is present.</exception>
    private static void GuardSupported(AlgebraOperator algebra)
    {
        foreach(AlgebraOperator op in AlgebraWalker.Traverse(algebra))
        {
            bool supported = op is Bgp or UnitTable or Table or Project or Distinct or Reduced or Slice or OrderBy or Join or Union or Minus or ToMultiSet or ToList or Filter or Extend or LeftJoin or Group or AggregateJoin or Path or Graph or Service;
            if(!supported)
            {
                throw new NotSupportedException($"SPARQL algebra operator '{op.GetType().Name}' is not yet executable. This executor covers basic graph patterns, property paths, named graphs (GRAPH), federated query (SERVICE), JOIN/UNION/MINUS, FILTER, BIND (Extend), OPTIONAL (LeftJoin), inline VALUES, ORDER BY, aggregation (GROUP BY / HAVING), sub-SELECT (ToMultiSet), and the projection/DISTINCT/REDUCED/OFFSET/LIMIT modifiers.");
            }
        }
    }

    /// <summary>Value equality over solutions by their binding sequences — used to deduplicate under <see cref="Distinct"/> (where projection has already fixed the binding order).</summary>
    private sealed class SolutionComparer : IEqualityComparer<SparqlSolution>
    {
        /// <summary>The shared comparer instance.</summary>
        public static SolutionComparer Instance { get; } = new();

        /// <summary>Returns whether two solutions have identical binding sequences.</summary>
        /// <param name="x">The first solution.</param>
        /// <param name="y">The second solution.</param>
        /// <returns><see langword="true"/> when the binding sequences are equal.</returns>
        public bool Equals(SparqlSolution? x, SparqlSolution? y)
        {
            if(ReferenceEquals(x, y))
            {
                return true;
            }

            if(x is null || y is null || x.Bindings.Count != y.Bindings.Count)
            {
                return false;
            }

            for(int i = 0; i < x.Bindings.Count; i++)
            {
                if(!x.Bindings[i].Equals(y.Bindings[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Computes a binding-sequence hash for a solution.</summary>
        /// <param name="solution">The solution.</param>
        /// <returns>The hash code.</returns>
        public int GetHashCode(SparqlSolution solution)
        {
            HashCode hash = new();
            foreach(SparqlBinding binding in solution.Bindings)
            {
                hash.Add(binding);
            }

            return hash.ToHashCode();
        }
    }
}
