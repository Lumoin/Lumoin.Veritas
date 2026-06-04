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
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
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
/// <see cref="Slice"/>, and the <see cref="UnitTable"/> identity), and property-path <see cref="Path"/> leaves
/// (via <see cref="PropertyPathEvaluator"/>; negated property sets excepted). Named graphs (<c>GRAPH</c>),
/// <c>SERVICE</c>, and <c>EXISTS</c>/<c>NOT EXISTS</c> raise <see cref="NotSupportedException"/> until their later
/// slice lands. The public surface (entry naming, result
/// model, streaming) is provisional and follows codebase convention; the W3C SPARQL 1.2 specification governs the
/// evaluation semantics.
/// </para>
/// <para>
/// <b>No recursion.</b> The algebra is evaluated over an explicit two-phase (expand/combine) work stack, mirroring
/// the translator's iterative discipline, so a deep algebra tree cannot overflow the stack. Results are
/// materialized per operator; streaming evaluation is a later concern.
/// </para>
/// <para>SPARQL 1.2 §18.5 / §18.6 [SPARQL Algebra evaluation].</para>
/// </remarks>
public sealed class SparqlQueryEngine
{
    /// <summary>A solution sequence holding the single empty solution — the result of the <see cref="UnitTable"/> identity.</summary>
    private static IReadOnlyList<SparqlSolution> SingleEmptySolution { get; } = [new SparqlSolution([])];

    /// <summary>The <c>xsd:boolean</c> datatype node, for the constant an <c>EXISTS</c>/<c>NOT EXISTS</c> rewrites to.</summary>
    private static NamedNode XsdBooleanType { get; } = new(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#boolean"));

    /// <summary>The <c>true</c> boolean literal an <c>EXISTS</c> rewrites to once resolved.</summary>
    private static Literal BooleanTrue { get; } = new(Utf8String.FromString("true"), XsdBooleanType);

    /// <summary>The <c>false</c> boolean literal an <c>EXISTS</c> rewrites to once resolved.</summary>
    private static Literal BooleanFalse { get; } = new(Utf8String.FromString("false"), XsdBooleanType);

    private readonly SparqlDataset dataset;

    private readonly TermDictionary dictionary;

    private readonly SparqlExpressionContext expressionContext;

    private readonly SparqlClient? serviceClient;

    private readonly AccessControlDelegate? accessControl;

    private readonly AccessContext? accessContext;

    /// <summary>Constructs an engine over an already-built dataset (default graph + named graphs) and the term dictionary that encoded it.</summary>
    /// <param name="dataset">The dataset the query evaluates against; its graphs are all encoded by <paramref name="dictionary"/>.</param>
    /// <param name="dictionary">The term dictionary that encoded <paramref name="dataset"/>; query constants are resolved against it.</param>
    /// <param name="expressionContext">The seams (randomness, digests, the query timestamp) the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple of every local graph read; <see langword="null"/> allows every triple at zero cost.</param>
    /// <param name="accessContext">The opaque "who is asking" context handed to <paramref name="accessControl"/> and forwarded to the <c>SERVICE</c>/<c>FROM</c>/<c>LOAD</c> IO seams; <see langword="null"/> when the evaluation carries no access context. Required when <paramref name="accessControl"/> is non-<see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlQueryEngine(SparqlDataset dataset, TermDictionary dictionary, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(dictionary);

        this.dataset = dataset;
        this.dictionary = dictionary;
        this.expressionContext = expressionContext ?? SparqlExpressionContext.CreateDefault();
        this.serviceClient = serviceClient;
        this.accessControl = accessControl;
        this.accessContext = accessContext;
    }

    /// <summary>Constructs an engine over a single default graph and no named graphs — the common case where the query has no <c>GRAPH</c> form.</summary>
    /// <param name="store">The hypertrie store holding the default graph.</param>
    /// <param name="dictionary">The term dictionary that encoded <paramref name="store"/>; query constants are resolved against it.</param>
    /// <param name="expressionContext">The seams the non-pure expression functions consume; <see langword="null"/> uses <see cref="SparqlExpressionContext.CreateDefault"/>.</param>
    /// <param name="serviceClient">The transport a <c>SERVICE</c> federation step uses; <see langword="null"/> means a non-silent <c>SERVICE</c> raises <see cref="NotSupportedException"/>.</param>
    /// <param name="accessControl">The access-control policy consulted per candidate triple; <see langword="null"/> allows every triple.</param>
    /// <param name="accessContext">The opaque "who is asking" context; <see langword="null"/> when none.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlQueryEngine(HypertrieGraphStore store, TermDictionary dictionary, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null)
        : this(SparqlDataset.FromDefaultGraph(store), dictionary, expressionContext, serviceClient, accessControl, accessContext)
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
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>An engine ready to evaluate queries against the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <see langword="null"/>.</exception>
    public static async ValueTask<SparqlQueryEngine> BuildAsync(IEnumerable<DataTriple> triples, SparqlExpressionContext? expressionContext = null, SparqlClient? serviceClient = null, AccessControlDelegate? accessControl = null, AccessContext? accessContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);

        TermDictionary dictionary = new();
        HypertrieGraphStore store = await EncodeGraphAsync(triples, dictionary, cancellationToken).ConfigureAwait(false);

        return new SparqlQueryEngine(store, dictionary, expressionContext, serviceClient, accessControl, accessContext);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(namedGraphs);

        TermDictionary dictionary = new();
        HypertrieGraphStore defaultStore = await EncodeGraphAsync(defaultGraph, dictionary, cancellationToken).ConfigureAwait(false);

        Dictionary<TermId, HypertrieGraphStore> named = new(namedGraphs.Count);
        foreach((RdfTerm name, IEnumerable<DataTriple> triples) in namedGraphs)
        {
            TermId graphName = dictionary.GetOrAdd(name);
            named[graphName] = await EncodeGraphAsync(triples, dictionary, cancellationToken).ConfigureAwait(false);
        }

        return new SparqlQueryEngine(new SparqlDataset(defaultStore, named), dictionary, expressionContext, serviceClient, accessControl, accessContext);
    }

    /// <summary>Encodes a graph's triples into <paramref name="dictionary"/> and indexes them into a fresh hypertrie store.</summary>
    /// <param name="triples">The graph's triples.</param>
    /// <param name="dictionary">The shared term dictionary every graph in the dataset encodes into.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The indexed graph store.</returns>
    private static async ValueTask<HypertrieGraphStore> EncodeGraphAsync(IEnumerable<DataTriple> triples, TermDictionary dictionary, CancellationToken cancellationToken)
    {
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in triples)
        {
            uint subject = dictionary.GetOrAdd(triple.Subject).Encoded;
            uint predicate = dictionary.GetOrAdd(triple.Predicate).Encoded;
            uint @object = dictionary.GetOrAdd(triple.Object).Encoded;
            encoded.Add(EncodedTriple.FromEncoded(subject, predicate, @object));
        }

        return await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates an algebra tree to its solution sequence. A <c>SELECT</c> consumes the sequence directly; an
    /// <c>ASK</c> is satisfied when the sequence is non-empty.
    /// </summary>
    /// <param name="algebra">The translated algebra (from <see cref="Translation.SparqlTranslator.Translate"/>).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="algebra"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    public async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateAsync(AlgebraOperator algebra, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);

        return await EvaluateInGraphAsync(algebra, TermId.None, cancellationToken).ConfigureAwait(false);
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
        //FROM NAMED graph is a named graph keyed by its (absolute) IRI.
        List<DataTriple> defaultGraph = [];
        foreach(IriRef from in datasetClause.DefaultGraphs)
        {
            defaultGraph.AddRange(await resolver(from, accessContext, cancellationToken).ConfigureAwait(false));
        }

        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = new(datasetClause.NamedGraphs.Count);
        foreach(IriRef fromNamed in datasetClause.NamedGraphs)
        {
            IReadOnlyList<DataTriple> triples = await resolver(fromNamed, accessContext, cancellationToken).ConfigureAwait(false);
            named.Add((new NamedNode(fromNamed.Value), triples));
        }

        return await BuildDatasetAsync(defaultGraph, named, expressionContext, serviceClient, accessControl, accessContext, cancellationToken).ConfigureAwait(false);
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
        GraphMatchOps ops = GuardMatchOps(dataset.DefaultGraph.AsMatchOps());

        List<TermId> ids = new(resources.Count);
        foreach(RdfTerm resource in resources)
        {
            TermId id = dictionary.GetIdOrDefault(resource);
            if(!id.IsNone)
            {
                ids.Add(id);
            }
        }

        List<Quad> result = [];
        HashSet<Quad> seen = [];
        await foreach(EncodedTriple triple in describe(ids, ops, dictionary, cancellationToken).ConfigureAwait(false))
        {
            Quad quad = new(dictionary.Resolve(triple.Subject), (NamedNode)dictionary.Resolve(triple.Predicate), dictionary.Resolve(triple.Object));
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
    /// <param name="algebra">The algebra to evaluate.</param>
    /// <param name="activeGraph">The active graph the basic patterns query, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The materialized solution sequence.</returns>
    /// <exception cref="NotSupportedException">The algebra uses an operator this slice does not yet execute.</exception>
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateInGraphAsync(AlgebraOperator algebra, TermId activeGraph, CancellationToken cancellationToken)
    {
        GuardSupported(algebra);

        //Two-phase expand/combine over an explicit stack (no recursion): each operator's children are evaluated
        //and recorded before the operator combines them. Results are keyed by (operator reference, active graph) —
        //a parsed algebra is a tree (so each operator instance appears once per graph), and a GRAPH form
        //re-evaluates the SAME sub-tree under a different active graph, so the graph is part of the key.
        Dictionary<ResultKey, IReadOnlyList<SparqlSolution>> results = new(ResultKeyComparer.Instance);
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
                    Filter or Extend or LeftJoin => await CombineExpressionOperatorAsync(node, graph, results, cancellationToken).ConfigureAwait(false),
                    _ => Combine(node, graph, results, expressionContext)
                };

                continue;
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
                results[new ResultKey(node, graph)] = await EvaluateLeafAsync(node, graph, cancellationToken).ConfigureAwait(false);
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
                if(dataset.TryGetNamedGraph(graphName, out _))
                {
                    work.Push((graphOperator.Input, graphName, Combine: false));
                }

                break;
            }

            case GraphVariableTerm:
            {
                foreach(TermId name in dataset.GraphNames)
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
    private IReadOnlyList<SparqlSolution> CombineGraph(Graph graphOperator, TermId enclosingGraph, Dictionary<ResultKey, IReadOnlyList<SparqlSolution>> results)
    {
        _ = enclosingGraph;
        switch(graphOperator.Designator)
        {
            case GraphIriTerm iri:
            {
                TermId graphName = GraphNameOf(iri);

                return dataset.TryGetNamedGraph(graphName, out _) ? results[new ResultKey(graphOperator.Input, graphName)] : [];
            }

            case GraphVariableTerm variableTerm:
            {
                List<SparqlSolution> all = [];
                foreach(TermId name in dataset.GraphNames)
                {
                    RdfTerm graphTerm = dictionary.Resolve(name);
                    foreach(SparqlSolution inner in results[new ResultKey(graphOperator.Input, name)])
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

                return all;
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
        return dictionary.GetIdOrDefault(new NamedNode(iri.Iri.Value));
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
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateLeafAsync(AlgebraOperator op, TermId graph, CancellationToken cancellationToken)
    {
        //UnitTable / inline VALUES are graph-independent; the graph-querying leaves resolve the active graph's
        //store. The store is non-null by construction: the default graph always resolves, and a GRAPH form only
        //descends into a named graph that exists (ExpandGraph), so an absent named graph never reaches here.
        if(op is UnitTable)
        {
            return SingleEmptySolution;
        }

        if(op is Table table)
        {
            return BuildTableSolutions(table.Data);
        }

        if(op is Service service)
        {
            return await EvaluateServiceAsync(service, cancellationToken).ConfigureAwait(false);
        }

        HypertrieGraphStore graphStore = dataset.Resolve(graph)
            ?? throw new InvalidOperationException($"Active graph '{graph.Encoded}' is not in the dataset; the GRAPH expansion should not have descended into it.");

        if(op is Bgp bgp)
        {
            //Default-graph patterns route through the engine
            //rendezvous, which pins this snapshot's store; named
            //graphs query their store directly.
            QueryEngineRendezvous? rendezvous = graph.IsNone ? dataset.DefaultGraphRendezvous : null;

            return await EvaluateBgpAsync(bgp, graphStore, rendezvous, cancellationToken).ConfigureAwait(false);
        }

        if(op is Path pathOperator)
        {
            return await EvaluatePathAsync(pathOperator, graphStore, cancellationToken).ConfigureAwait(false);
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
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateServiceAsync(Service service, CancellationToken cancellationToken)
    {
        if(service.Endpoint is not GraphIriTerm endpoint)
        {
            //A variable endpoint is resolved per left binding by the enclosing Join's bound-join
            //(CombineServiceBoundJoinAsync); on its own — with no surrounding solutions to supply the
            //endpoint — the service produces nothing.
            return [];
        }

        if(serviceClient is null)
        {
            return service.Silent
                ? SingleEmptySolution
                : throw new NotSupportedException("SERVICE requires a SparqlClient transport, but the engine was constructed without one.");
        }

        string query = SparqlQueryTextWriter.ToSelectQuery(service.InnerPattern);
        try
        {
            SparqlResultSet results = await serviceClient.QueryAsync(endpoint.Iri, query, accessContext, cancellationToken).ConfigureAwait(false);

            return results.Solutions;
        }
        catch(Exception ex) when(service.Silent && ex is not OperationCanceledException)
        {
            //SILENT: a federation failure contributes the join identity, so the surrounding pattern is unaffected.
            return SingleEmptySolution;
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
    private async ValueTask<IReadOnlyList<SparqlSolution>> CombineServiceBoundJoinAsync(Join join, TermId graph, Dictionary<ResultKey, IReadOnlyList<SparqlSolution>> results, CancellationToken cancellationToken)
    {
        (Service service, AlgebraOperator bindingChild) = join.Right is Service { Endpoint: GraphVariableTerm } rightService
            ? (rightService, join.Left)
            : ((Service)join.Left, join.Right);

        IReadOnlyList<SparqlSolution> bindings = results[new ResultKey(bindingChild, graph)];
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
        if(endpointOrder.Count > 0 && serviceClient is null)
        {
            //SILENT with no transport: each group contributes the join identity (its solutions pass through).
            return service.Silent
                ? Passthrough(endpointOrder, byEndpoint, unbound)
                : throw new NotSupportedException("SERVICE with a variable endpoint requires a SparqlClient transport, but the engine was constructed without one.");
        }

        if(serviceClient is not null)
        {
            string query = SparqlQueryTextWriter.ToSelectQuery(service.InnerPattern);
            foreach(Utf8String endpoint in endpointOrder)
            {
                List<SparqlSolution> group = byEndpoint[endpoint];
                try
                {
                    SparqlResultSet remote = await serviceClient.QueryAsync(new IriRef(endpoint, default), query, accessContext, cancellationToken).ConfigureAwait(false);
                    joined.AddRange(JoinSolutions(group, remote.Solutions));
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

        return joined;
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
    private static IReadOnlyList<SparqlSolution> Combine(AlgebraOperator op, TermId graph, Dictionary<ResultKey, IReadOnlyList<SparqlSolution>> results, SparqlExpressionContext context)
    {
        return op switch
        {
            Project project => ApplyProject(project, results[new ResultKey(project.Input, graph)]),
            Distinct distinct => ApplyDistinct(results[new ResultKey(distinct.Input, graph)]),

            //REDUCED permits but does not require duplicate elimination; passing the sequence through is conformant.
            Reduced reduced => results[new ResultKey(reduced.Input, graph)],
            Slice slice => ApplySlice(slice, results[new ResultKey(slice.Input, graph)]),
            Join join => JoinSolutions(results[new ResultKey(join.Left, graph)], results[new ResultKey(join.Right, graph)]),
            Union union => UnionSolutions(results[new ResultKey(union.Left, graph)], results[new ResultKey(union.Right, graph)]),
            Minus minus => MinusSolutions(results[new ResultKey(minus.Left, graph)], results[new ResultKey(minus.Right, graph)]),
            OrderBy orderBy => ApplyOrderBy(orderBy, results[new ResultKey(orderBy.Input, graph)], context),

            //ToMultiSet / ToList are multiset / sequence conversions; with no ORDER BY in scope they pass through.
            ToMultiSet toMultiSet => results[new ResultKey(toMultiSet.Input, graph)],
            ToList toList => results[new ResultKey(toList.Input, graph)],

            //Group leaves the multiset unchanged here; the partitioning into groups happens in the AggregateJoin
            //that wraps it (which reads the Group's keys directly).
            Group group => results[new ResultKey(group.Input, graph)],
            AggregateJoin aggregateJoin => EvaluateAggregateJoin(aggregateJoin, results[new ResultKey(aggregateJoin.Input, graph)], context),
            _ => throw new InvalidOperationException($"Operator '{op.GetType().Name}' has children but no combine rule in the executor.")
        };
    }

    /// <summary>Evaluates a basic graph pattern against the active graph: encodes it to the backend, runs the worst-case-optimal join engine over <paramref name="graphStore"/> — through <paramref name="rendezvous"/> when one applies, pinning the store so the engine choice never outruns this snapshot — and decodes each solution.</summary>
    /// <param name="bgp">The basic graph pattern.</param>
    /// <param name="graphStore">The active graph's store.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The BGP's solution sequence; empty when a constant in the pattern is absent from the active graph.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> EvaluateBgpAsync(Bgp bgp, HypertrieGraphStore graphStore, QueryEngineRendezvous? rendezvous, CancellationToken cancellationToken)
    {
        VariableRegistry registry = new();
        Dictionary<SparqlVariable, Variable> toBackend = [];
        Dictionary<Variable, SparqlVariable> toSparql = [];

        //A blank node in a query pattern is a non-distinguished (existential) join variable: occurrences sharing a
        //label join like a variable, but it is never projected. Map each label to a fresh backend variable that is
        //registered for the join yet absent from `toSparql`, so Decode leaves it out of the output solution.
        Dictionary<Utf8String, Variable> blankToBackend = [];
        List<EncodedTriplePattern> patterns = new(bgp.Patterns.Count);

        //A pattern position that is a quoted triple term with a variable inside cannot encode to a single bound
        //value; each such position becomes a fresh internal join variable plus a destructuring task applied to the
        //solutions the leapfrog produces (see docs/decisions/sparql-join-and-triple-term-matching.md). Empty for the
        //overwhelmingly common case of no variable-bearing triple terms, so the common path below stays untouched.
        List<TripleTermMatch> tripleTermMatches = [];

        foreach(AstTriplePattern triple in bgp.Patterns)
        {
            //A constant absent from the data graph can never match, so the whole BGP yields nothing.
            if(!TryEncodePosition(triple.Subject, registry, toBackend, toSparql, blankToBackend, tripleTermMatches, out PatternPosition subject)
                || !TryEncodePosition(triple.Predicate, registry, toBackend, toSparql, blankToBackend, tripleTermMatches, out PatternPosition predicate)
                || !TryEncodePosition(triple.Object, registry, toBackend, toSparql, blankToBackend, tripleTermMatches, out PatternPosition @object))
            {
                return [];
            }

            patterns.Add(new EncodedTriplePattern(subject, predicate, @object));
        }

        BasicGraphPattern query = new(patterns, registry);
        bool hasTripleTermMatches = tripleTermMatches.Count > 0;
        List<SparqlSolution> solutions = [];
        IAsyncEnumerable<Solution> backendSolutions = rendezvous is not null
            ? rendezvous.Query(graphStore, query, TimeProvider.System, accessControl: accessControl, accessContext: accessContext, cancellationToken: cancellationToken)
            : graphStore.Query(query, TimeProvider.System, accessControl: accessControl, accessContext: accessContext, cancellationToken: cancellationToken);

        await foreach(Solution solution in backendSolutions.ConfigureAwait(false))
        {
            if(!hasTripleTermMatches)
            {
                solutions.Add(Decode(solution, toSparql));

                continue;
            }

            //Destructure each variable-bearing triple-term position: resolve the matched triple-term value and unify
            //its components against the solution (binding fresh component variables, checking already-bound ones).
            //A non-triple-term value or a component mismatch drops the solution.
            List<VariableBinding> bindings = [.. solution.Bindings];
            if(TryApplyTripleTermMatches(tripleTermMatches, bindings))
            {
                solutions.Add(DecodeBindings(bindings, toSparql));
            }
        }

        return solutions;
    }

    /// <summary>
    /// Wraps a store's match operations with the engine's access-control policy so the read paths that go through
    /// <see cref="GraphMatchOps"/> rather than the BGP <c>Query</c> — property paths
    /// (<see cref="PropertyPathEvaluator"/>) and <c>DESCRIBE</c> — are gated identically: each matched triple is
    /// consulted and only an <see cref="AccessDecision.Allow"/> keeps it (<see cref="AccessDecision.Deny"/> and
    /// <see cref="AccessDecision.NotFound"/> drop it, exactly as in a basic graph pattern). With no policy the
    /// operations are returned unchanged, so the common path pays nothing.
    /// </summary>
    /// <param name="ops">The store's unguarded match operations.</param>
    /// <returns>The same operations when there is no policy, otherwise an access-filtered wrapper.</returns>
    private GraphMatchOps GuardMatchOps(GraphMatchOps ops)
    {
        if(accessControl is null)
        {
            return ops;
        }

        return new GraphMatchOps(
            (subject, predicate, @object, cancellationToken) => FilterByAccessAsync(ops.MatchTriples(subject, predicate, @object, cancellationToken), cancellationToken),
            (subjects, predicate, @object, cancellationToken) => FilterByAccessAsync(ops.MatchTriplesBySubjects(subjects, predicate, @object, cancellationToken), cancellationToken),
            (subject, predicate, objects, cancellationToken) => FilterByAccessAsync(ops.MatchTriplesByObjects(subject, predicate, objects, cancellationToken), cancellationToken));
    }

    /// <summary>Yields only the matched triples the access-control policy allows; a <c>Deny</c> or <c>NotFound</c> drops the triple, mirroring the BGP gate.</summary>
    /// <param name="triples">The unguarded matched triples.</param>
    /// <param name="cancellationToken">A token that aborts enumeration and the access consultation.</param>
    /// <returns>The allowed triples.</returns>
    private async IAsyncEnumerable<EncodedTriple> FilterByAccessAsync(IAsyncEnumerable<EncodedTriple> triples, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple triple in triples.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            AccessDecision decision = await accessControl!(new AccessRequest(triple, accessContext!), cancellationToken).ConfigureAwait(false);
            if(decision == AccessDecision.Allow)
            {
                yield return triple;
            }
        }
    }

    /// <summary>
    /// Encodes one triple-pattern term to a backend <see cref="PatternPosition"/>: a variable to a registered
    /// <see cref="Variable"/>, a constant to its bound <see cref="TermId"/>, a ground quoted triple term to its one
    /// interned <see cref="TermId"/>, and a variable-bearing quoted triple term to a fresh join variable plus a
    /// destructuring task appended to <paramref name="tripleTermMatches"/>.
    /// </summary>
    /// <param name="term">The term to encode.</param>
    /// <param name="registry">The registry minting backend variables for this pattern.</param>
    /// <param name="toBackend">The accumulating SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The accumulating backend-variable to SPARQL-variable map (for decoding).</param>
    /// <param name="blankToBackend">The accumulating blank-node-label to backend-variable map (blank nodes are non-projected join variables).</param>
    /// <param name="tripleTermMatches">The accumulating list of post-join destructuring tasks; a variable-bearing quoted triple term appends one.</param>
    /// <param name="position">Receives the encoded position on success.</param>
    /// <returns><see langword="true"/> when the term encodes to a matchable position; <see langword="false"/> when a constant (or ground triple term) is absent from the data graph.</returns>
    /// <exception cref="NotSupportedException">The term is none of a constant, a variable, or a quoted triple term (e.g. a property path reached this position).</exception>
    private bool TryEncodePosition(
        TriplePatternTerm term,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        Dictionary<Utf8String, Variable> blankToBackend,
        List<TripleTermMatch> tripleTermMatches,
        out PatternPosition position)
    {
        switch(term)
        {
            case VariableTerm variableTerm:
            {
                position = PatternPosition.OfVariable(GetOrAddVariable(variableTerm.Variable, registry, toBackend, toSparql));

                return true;
            }

            //A blank node in a query pattern is a non-distinguished join variable, not a ground value: occurrences
            //of the same label join, but it is never projected (it is not added to `toSparql`).
            case ConstantTerm { Term: BlankNode blank }:
            {
                position = PatternPosition.OfVariable(GetOrAddBlankVariable(blank.Label, registry, blankToBackend));

                return true;
            }

            case ConstantTerm constantTerm:
            {
                TermId id = dictionary.GetIdOrDefault(constantTerm.Term);
                if(id.IsNone)
                {
                    position = default;

                    return false;
                }

                position = PatternPosition.Bound(id);

                return true;
            }

            case AstTripleTerm tripleTerm when TryBuildGroundTripleTerm(tripleTerm, out Core.TripleTerm ground):
            {
                //A fully-ground quoted triple term (no variables inside) is a single RDF 1.2 triple term value:
                //encode it as one bound TermId, exactly as the data-graph triple term was interned, so it matches by
                //value.
                TermId id = dictionary.GetIdOrDefault(ground);
                if(id.IsNone)
                {
                    position = default;

                    return false;
                }

                position = PatternPosition.Bound(id);

                return true;
            }

            case AstTripleTerm tripleTerm:
            {
                //A quoted triple term with a variable (or blank-node join variable) inside cannot encode to one
                //bound value. Encode the position as a fresh internal join variable the leapfrog binds to whatever
                //triple-term value occupies it, and record a destructuring task that unifies the term's components
                //against the solution after the join (Option B — see
                //docs/decisions/sparql-join-and-triple-term-matching.md). The target is never recorded in `toSparql`,
                //so it is a join variable that is not projected.
                Variable target = registry.GetOrAdd("<<tt " + tripleTermMatches.Count.ToString(CultureInfo.InvariantCulture) + ">>");
                TripleComponent pattern = BuildTripleTermComponent(tripleTerm, registry, toBackend, toSparql, blankToBackend);
                tripleTermMatches.Add(new TripleTermMatch(target, pattern));
                position = PatternPosition.OfVariable(target);

                return true;
            }

            default:
            {
                throw new NotSupportedException($"Triple-pattern term '{term.GetType().Name}' is not executable; this engine handles constants, variables, and quoted triple terms.");
            }
        }
    }

    /// <summary>
    /// Builds the RDF 1.2 <see cref="Core.TripleTerm"/> value of a quoted triple term whose every position is a
    /// constant (its subject may itself be a ground quoted triple term), walking the nesting over an explicit stack
    /// (no recursion). Returns <see langword="false"/> when any position is a variable or otherwise non-constant —
    /// such a pattern needs structural matching rather than a single encoded value.
    /// </summary>
    /// <param name="tripleTerm">The quoted triple term.</param>
    /// <param name="ground">Receives the ground triple-term value on success.</param>
    /// <returns><see langword="true"/> when the quoted triple term is fully ground.</returns>
    private static bool TryBuildGroundTripleTerm(AstTripleTerm tripleTerm, out Core.TripleTerm ground)
    {
        ground = null!;

        //Post-order build: a position is either a constant (its RdfTerm taken directly), or a nested quoted triple
        //term (built first, then used as this term's subject/object). A predicate must resolve to a NamedNode.
        Dictionary<AstTripleTerm, Core.TripleTerm> built = new(ReferenceEqualityComparer.Instance);
        Stack<(AstTripleTerm Term, bool Build)> work = new();
        work.Push((tripleTerm, Build: false));

        while(work.Count > 0)
        {
            (AstTripleTerm term, bool build) = work.Pop();
            AstTriplePattern inner = term.Inner;

            if(!build)
            {
                work.Push((term, Build: true));

                //Only a nested quoted triple term needs to be built before this one; constants are read in place.
                if(inner.Subject is AstTripleTerm nestedSubject)
                {
                    work.Push((nestedSubject, Build: false));
                }

                if(inner.Object is AstTripleTerm nestedObject)
                {
                    work.Push((nestedObject, Build: false));
                }

                continue;
            }

            if(!TryGroundPosition(inner.Subject, built, out RdfTerm subjectTerm)
                || inner.Predicate is not ConstantTerm { Term: NamedNode predicateNode }
                || !TryGroundPosition(inner.Object, built, out RdfTerm objectTerm))
            {
                return false;
            }

            built[term] = new Core.TripleTerm(subjectTerm, predicateNode, objectTerm);
        }

        ground = built[tripleTerm];

        return true;
    }

    /// <summary>Resolves a quoted-triple-term inner position to its ground RDF term: a constant's term, or an already-built nested triple term; fails on a variable or other non-ground term.</summary>
    /// <param name="position">The inner position.</param>
    /// <param name="built">The map of already-built nested triple terms.</param>
    /// <param name="term">Receives the ground term on success.</param>
    /// <returns><see langword="true"/> when the position is ground.</returns>
    private static bool TryGroundPosition(TriplePatternTerm position, Dictionary<AstTripleTerm, Core.TripleTerm> built, out RdfTerm term)
    {
        term = position switch
        {
            //A blank node in a query triple term is a non-distinguished join variable, not a ground value, so a
            //triple term containing one is NOT ground — it falls through to structural (destructuring) matching.
            ConstantTerm { Term: BlankNode } => null!,
            ConstantTerm constant => constant.Term,
            AstTripleTerm nested => built[nested],
            _ => null!
        };

        return term is not null;
    }

    /// <summary>Returns the backend variable for a SPARQL variable, minting and recording it (both map directions) on first sight.</summary>
    /// <param name="variable">The SPARQL variable.</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="toBackend">The SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map.</param>
    /// <returns>The backend variable for <paramref name="variable"/>.</returns>
    private static Variable GetOrAddVariable(
        SparqlVariable variable,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql)
    {
        if(toBackend.TryGetValue(variable, out Variable existing))
        {
            return existing;
        }

        Variable backend = registry.GetOrAdd(variable.Name.ToString());
        toBackend[variable] = backend;
        toSparql[backend] = variable;

        return backend;
    }

    /// <summary>
    /// Returns the backend join variable for a query blank node, minting one per distinct label on first sight.
    /// Unlike <see cref="GetOrAddVariable"/> it records no entry in the backend→SPARQL map, so the blank node joins
    /// across its occurrences but is never decoded into the output solution (a blank node is non-distinguished).
    /// </summary>
    /// <param name="label">The blank node's label.</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="blankToBackend">The blank-label to backend-variable map.</param>
    /// <returns>The backend variable for the blank node.</returns>
    private static Variable GetOrAddBlankVariable(Utf8String label, VariableRegistry registry, Dictionary<Utf8String, Variable> blankToBackend)
    {
        if(blankToBackend.TryGetValue(label, out Variable existing))
        {
            return existing;
        }

        //A label that can never collide with a real SPARQL variable name (which never contains a space).
        Variable backend = registry.GetOrAdd("_:bnode " + label.ToString());
        blankToBackend[label] = backend;

        return backend;
    }

    /// <summary>Decodes a backend solution to a SPARQL solution, mapping each backend variable to its SPARQL variable and resolving each encoded value to its RDF term.</summary>
    /// <param name="solution">The backend solution.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map for this pattern.</param>
    /// <returns>The decoded SPARQL solution.</returns>
    private SparqlSolution Decode(Solution solution, Dictionary<Variable, SparqlVariable> toSparql)
    {
        return DecodeBindings(solution.Bindings, toSparql);
    }

    /// <summary>Decodes a backend binding list to a SPARQL solution, mapping each projected backend variable to its SPARQL variable and resolving each encoded value to its RDF term; unprojected (join-only) variables are dropped.</summary>
    /// <param name="bindings">The backend bindings (a leapfrog solution's bindings, possibly extended by triple-term destructuring).</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map for this pattern.</param>
    /// <returns>The decoded SPARQL solution.</returns>
    private SparqlSolution DecodeBindings(IReadOnlyList<VariableBinding> bindings, Dictionary<Variable, SparqlVariable> toSparql)
    {
        List<SparqlBinding> result = new(bindings.Count);
        foreach(VariableBinding binding in bindings)
        {
            if(toSparql.TryGetValue(binding.Variable, out SparqlVariable variable))
            {
                result.Add(new SparqlBinding(variable, dictionary.Resolve(binding.Value)));
            }
        }

        return new SparqlSolution(result);
    }

    /// <summary>
    /// Builds the destructuring pattern for a variable-bearing quoted triple term: a tree of
    /// <see cref="TripleTermComponent"/> mirroring the term, with each leaf a backend variable (for a query variable
    /// or blank node) or a constant RDF term. Walks the nesting over an explicit stack (no recursion), reusing the
    /// pattern's backend-variable maps so a component variable joins its other occurrences.
    /// </summary>
    /// <param name="root">The quoted triple term (known to contain at least one variable).</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="toBackend">The SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map.</param>
    /// <param name="blankToBackend">The blank-node-label to backend-variable map.</param>
    /// <returns>The root triple-term component.</returns>
    private static TripleComponent BuildTripleTermComponent(
        AstTripleTerm root,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        Dictionary<Utf8String, Variable> blankToBackend)
    {
        Dictionary<AstTripleTerm, TripleComponent> built = new(ReferenceEqualityComparer.Instance);
        Stack<(AstTripleTerm Term, bool Build)> work = new();
        work.Push((root, Build: false));

        while(work.Count > 0)
        {
            (AstTripleTerm term, bool build) = work.Pop();
            AstTriplePattern inner = term.Inner;

            if(!build)
            {
                work.Push((term, Build: true));

                //Only a nested quoted triple term must be built before this one; the other positions read in place.
                if(inner.Subject is AstTripleTerm nestedSubject)
                {
                    work.Push((nestedSubject, Build: false));
                }

                if(inner.Object is AstTripleTerm nestedObject)
                {
                    work.Push((nestedObject, Build: false));
                }

                continue;
            }

            TripleTermComponent subject = ToTripleTermComponent(inner.Subject, built, registry, toBackend, toSparql, blankToBackend);
            TripleTermComponent predicate = ToTripleTermComponent(inner.Predicate, built, registry, toBackend, toSparql, blankToBackend);
            TripleTermComponent objectComponent = ToTripleTermComponent(inner.Object, built, registry, toBackend, toSparql, blankToBackend);
            built[term] = new TripleComponent(subject, predicate, objectComponent);
        }

        return built[root];
    }

    /// <summary>Maps one quoted-triple-term inner position to a <see cref="TripleTermComponent"/>: a variable / blank node to a (join) variable component, a constant to a constant component, a nested triple term to its already-built component.</summary>
    /// <param name="position">The inner position.</param>
    /// <param name="built">The map of already-built nested triple-term components.</param>
    /// <param name="registry">The registry minting backend variables.</param>
    /// <param name="toBackend">The SPARQL-variable to backend-variable map.</param>
    /// <param name="toSparql">The backend-variable to SPARQL-variable map.</param>
    /// <param name="blankToBackend">The blank-node-label to backend-variable map.</param>
    /// <returns>The component.</returns>
    /// <exception cref="NotSupportedException">The position is a property path (not valid inside a quoted triple term).</exception>
    private static TripleTermComponent ToTripleTermComponent(
        TriplePatternTerm position,
        Dictionary<AstTripleTerm, TripleComponent> built,
        VariableRegistry registry,
        Dictionary<SparqlVariable, Variable> toBackend,
        Dictionary<Variable, SparqlVariable> toSparql,
        Dictionary<Utf8String, Variable> blankToBackend)
    {
        return position switch
        {
            VariableTerm variableTerm => new VariableComponent(GetOrAddVariable(variableTerm.Variable, registry, toBackend, toSparql)),
            ConstantTerm { Term: BlankNode blank } => new VariableComponent(GetOrAddBlankVariable(blank.Label, registry, blankToBackend)),
            ConstantTerm constant => new ConstantComponent(constant.Term),
            AstTripleTerm nested => built[nested],
            _ => throw new NotSupportedException($"Quoted-triple-term component '{position.GetType().Name}' is not supported.")
        };
    }

    /// <summary>Applies every triple-term destructuring task to a candidate solution: resolves each target's matched value and unifies its components, extending <paramref name="bindings"/> with fresh component bindings. Returns <see langword="false"/> (dropping the solution) when any target is unbound, is not a triple term, or has a component that does not unify.</summary>
    /// <param name="matches">The destructuring tasks for this pattern.</param>
    /// <param name="bindings">The candidate solution's backend bindings, extended in place on success.</param>
    /// <returns><see langword="true"/> when every task unifies.</returns>
    private bool TryApplyTripleTermMatches(List<TripleTermMatch> matches, List<VariableBinding> bindings)
    {
        foreach(TripleTermMatch match in matches)
        {
            if(!TryLookupBinding(bindings, match.Target, out TermId boundId))
            {
                return false;
            }

            if(!TryUnifyTripleTermComponent(match.Pattern, dictionary.Resolve(boundId), bindings))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Unifies a <see cref="TripleTermComponent"/> pattern against an actual RDF term, binding fresh variable components and checking already-bound ones, descending nested triple terms over an explicit stack (no recursion). Returns <see langword="false"/> on the first mismatch.</summary>
    /// <param name="root">The component pattern.</param>
    /// <param name="rootActual">The actual term to unify against.</param>
    /// <param name="bindings">The bindings, extended in place when a variable component binds.</param>
    /// <returns><see langword="true"/> when the pattern unifies with the term.</returns>
    private bool TryUnifyTripleTermComponent(TripleTermComponent root, RdfTerm rootActual, List<VariableBinding> bindings)
    {
        Stack<(TripleTermComponent Component, RdfTerm Actual)> work = new();
        work.Push((root, rootActual));

        while(work.Count > 0)
        {
            (TripleTermComponent component, RdfTerm actual) = work.Pop();
            bool unified = component switch
            {
                ConstantComponent constant => constant.Term.Equals(actual),
                VariableComponent variable => TryBindOrCheck(variable.Variable, actual, bindings),
                TripleComponent triple => TryDescendTripleComponent(triple, actual, work),
                _ => throw new InvalidOperationException($"Unexpected triple-term component '{component.GetType().Name}'.")
            };
            if(!unified)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Binds a variable component to the actual term's id when unbound, or checks equality with its existing binding. Interns the actual term (a no-op when already interned); equal RDF terms share an id, so the comparison is by value.</summary>
    /// <param name="variable">The backend variable.</param>
    /// <param name="actual">The actual term.</param>
    /// <param name="bindings">The bindings, extended in place when the variable is unbound.</param>
    /// <returns><see langword="true"/> when the variable was unbound (now bound) or already equals the actual term.</returns>
    private bool TryBindOrCheck(Variable variable, RdfTerm actual, List<VariableBinding> bindings)
    {
        TermId actualId = dictionary.GetOrAdd(actual);
        if(TryLookupBinding(bindings, variable, out TermId existing))
        {
            return existing == actualId;
        }

        bindings.Add(new VariableBinding(variable, actualId));

        return true;
    }

    /// <summary>Descends a nested triple-term component: when the actual term is itself a triple term, pushes its three component/term pairs onto <paramref name="work"/> for unification; otherwise the match fails.</summary>
    /// <param name="triple">The nested triple-term component.</param>
    /// <param name="actual">The actual term, which must be a <see cref="Core.TripleTerm"/> to match.</param>
    /// <param name="work">The unification work stack to push the component pairs onto.</param>
    /// <returns><see langword="true"/> when the actual term is a triple term (children pushed); <see langword="false"/> otherwise.</returns>
    private static bool TryDescendTripleComponent(TripleComponent triple, RdfTerm actual, Stack<(TripleTermComponent Component, RdfTerm Actual)> work)
    {
        if(actual is not Core.TripleTerm tripleTerm)
        {
            return false;
        }

        work.Push((triple.Subject, tripleTerm.Subject));
        work.Push((triple.Predicate, tripleTerm.Predicate));
        work.Push((triple.Object, tripleTerm.Object));

        return true;
    }

    /// <summary>Looks up a backend variable's bound value in a binding list.</summary>
    /// <param name="bindings">The bindings to scan.</param>
    /// <param name="variable">The backend variable.</param>
    /// <param name="value">Receives the bound value when found.</param>
    /// <returns><see langword="true"/> when the variable is bound.</returns>
    private static bool TryLookupBinding(List<VariableBinding> bindings, Variable variable, out TermId value)
    {
        foreach(VariableBinding binding in bindings)
        {
            if(binding.Variable == variable)
            {
                value = binding.Value;

                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>Restricts each solution to the projected variables, in projection order, dropping bindings for variables a solution does not bind.</summary>
    /// <param name="project">The projection.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <returns>The projected solution sequence.</returns>
    private static List<SparqlSolution> ApplyProject(Project project, IReadOnlyList<SparqlSolution> input)
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
    private static List<SparqlSolution> ApplyDistinct(IReadOnlyList<SparqlSolution> input)
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
    private static IReadOnlyList<SparqlSolution> ApplySlice(Slice slice, IReadOnlyList<SparqlSolution> input)
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

        Array.Sort(order, (left, right) =>
        {
            int comparison = CompareByConditions(source[left], source[right], orderBy.Conditions, context);

            //A stable tiebreak on the original index keeps ordering deterministic where the keys are equal.
            return comparison != 0 ? comparison : left.CompareTo(right);
        });

        List<SparqlSolution> ordered = new(source.Count);
        foreach(int index in order)
        {
            ordered.Add(source[index]);
        }

        return ordered;
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
            int comparison = SparqlExpressionEvaluator.CompareForOrdering(leftKey, rightKey);
            if(comparison != 0)
            {
                return descending ? -comparison : comparison;
            }
        }

        return 0;
    }

    /// <summary>Builds the solution sequence of an inline <c>VALUES</c> block: one solution per row, binding each variable to its row value and leaving <c>UNDEF</c> (a <see langword="null"/> entry) unbound.</summary>
    /// <param name="data">The inline-data block.</param>
    /// <returns>The block's solution sequence.</returns>
    private static List<SparqlSolution> BuildTableSolutions(ValuesClause data)
    {
        List<SparqlSolution> solutions = new(data.Rows.Count);
        foreach(IReadOnlyList<RdfTerm?> row in data.Rows)
        {
            List<SparqlBinding> bindings = new(data.Variables.Count);
            for(int i = 0; i < data.Variables.Count && i < row.Count; i++)
            {
                if(row[i] is RdfTerm term)
                {
                    bindings.Add(new SparqlBinding(data.Variables[i], term));
                }
            }

            solutions.Add(new SparqlSolution(bindings));
        }

        return solutions;
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
        GraphMatchOps ops = GuardMatchOps(graphStore.AsMatchOps());
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
            ConstantTerm constant => (dictionary.GetIdOrDefault(constant.Term), (SparqlVariable?)null),
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
            bindings.Add(new SparqlBinding(subject, dictionary.Resolve(subjectValue)));
        }

        if(objectVariable is SparqlVariable @object && @object != subjectVariable)
        {
            bindings.Add(new SparqlBinding(@object, dictionary.Resolve(objectValue)));
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
    private IriId EncodePredicate(IriRef predicate) => IriId.FromUnchecked(dictionary.GetIdOrDefault(new NamedNode(predicate.Value)));

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

    /// <summary>Joins two solution sequences (§18.6 Join): the merge of every pair of compatible solutions, one from each side.</summary>
    /// <param name="left">The left solution sequence.</param>
    /// <param name="right">The right solution sequence.</param>
    /// <returns>The joined solution sequence.</returns>
    private static List<SparqlSolution> JoinSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
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
    private static List<SparqlSolution> UnionSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
    {
        List<SparqlSolution> all = new(left.Count + right.Count);
        all.AddRange(left);
        all.AddRange(right);

        return all;
    }

    /// <summary>
    /// Subtracts the right sequence from the left (§18.6 Minus): a left solution is removed only when some right
    /// solution is compatible with it <em>and</em> shares at least one variable. The shared-variable requirement is
    /// the spec's disjoint-domain exception — a left solution with no variable in common with any right solution is
    /// kept even though disjoint mappings are technically compatible.
    /// </summary>
    /// <param name="left">The left (kept) solution sequence.</param>
    /// <param name="right">The right (subtracting) solution sequence.</param>
    /// <returns>The left solutions that survive the subtraction.</returns>
    private static List<SparqlSolution> MinusSolutions(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right)
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
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The operator's solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> CombineExpressionOperatorAsync(AlgebraOperator op, TermId graph, Dictionary<ResultKey, IReadOnlyList<SparqlSolution>> results, CancellationToken cancellationToken)
    {
        switch(op)
        {
            case Filter filter:
            {
                return await ApplyFilterAsync(filter, results[new ResultKey(filter.Input, graph)], graph, cancellationToken).ConfigureAwait(false);
            }

            case Extend extend:
            {
                return await ApplyExtendAsync(extend, results[new ResultKey(extend.Input, graph)], graph, cancellationToken).ConfigureAwait(false);
            }

            case LeftJoin leftJoin:
            {
                return await LeftJoinSolutionsAsync(results[new ResultKey(leftJoin.Left, graph)], results[new ResultKey(leftJoin.Right, graph)], leftJoin.Condition, graph, cancellationToken).ConfigureAwait(false);
            }

            default:
            {
                throw new InvalidOperationException($"Operator '{op.GetType().Name}' is not an expression-gated operator.");
            }
        }
    }

    /// <summary>Keeps the solutions whose <c>FILTER</c> condition has effective boolean value true (§18.6 Filter); an error or non-boolean value drops the solution. Any <c>EXISTS</c>/<c>NOT EXISTS</c> in the condition is resolved per solution first.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="input">The input solution sequence.</param>
    /// <param name="graph">The active graph any EXISTS sub-pattern in the condition re-enters in.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The surviving solutions.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> ApplyFilterAsync(Filter filter, IReadOnlyList<SparqlSolution> input, TermId graph, CancellationToken cancellationToken)
    {
        List<SparqlSolution> kept = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            ExpressionNode condition = await ResolveExistsAsync(filter.Condition, solution, graph, cancellationToken).ConfigureAwait(false);
            if(SparqlExpressionEvaluator.Satisfies(condition, solution, expressionContext))
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
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The extended solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> ApplyExtendAsync(Extend extend, IReadOnlyList<SparqlSolution> input, TermId graph, CancellationToken cancellationToken)
    {
        List<SparqlSolution> extended = new(input.Count);
        foreach(SparqlSolution solution in input)
        {
            ExpressionNode expression = await ResolveExistsAsync(extend.Expression, solution, graph, cancellationToken).ConfigureAwait(false);
            if(!SparqlExpressionEvaluator.TryEvaluate(expression, solution, expressionContext, out RdfTerm value))
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
            expressionContext.BlankNodeScope.Link(solution, extendedSolution);
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
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The left-joined solution sequence.</returns>
    private async ValueTask<IReadOnlyList<SparqlSolution>> LeftJoinSolutionsAsync(IReadOnlyList<SparqlSolution> left, IReadOnlyList<SparqlSolution> right, ExpressionNode? condition, TermId graph, CancellationToken cancellationToken)
    {
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
                ExpressionNode? resolved = condition is null ? null : await ResolveExistsAsync(condition, merged, graph, cancellationToken).ConfigureAwait(false);
                if(resolved is null || SparqlExpressionEvaluator.Satisfies(resolved, merged, expressionContext))
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
    /// Evaluates an <c>AggregateJoin</c> (§18.5): partitions the input solutions into groups by the wrapped
    /// <see cref="Group"/>'s keys, then emits one solution per group binding the named grouping keys and each
    /// aggregate's per-group result. An empty <c>GROUP BY</c> (implicit grouping) always yields exactly one group,
    /// even over no input solutions, so <c>COUNT(*)</c> over an empty pattern is 0.
    /// </summary>
    /// <param name="aggregateJoin">The aggregate-join operator (its input is the grouping operator).</param>
    /// <param name="input">The grouped operand's solutions (the Group passthrough).</param>
    /// <param name="context">The seams the grouping-key and aggregate expressions' non-pure functions consume.</param>
    /// <returns>One solution per group.</returns>
    private static List<SparqlSolution> EvaluateAggregateJoin(AggregateJoin aggregateJoin, IReadOnlyList<SparqlSolution> input, SparqlExpressionContext context)
    {
        IReadOnlyList<GroupCondition> keys = aggregateJoin.Input is Group group ? group.Keys : [];
        List<AggregateGroup> groups = Partition(keys, input, context);

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

        return result;
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

    /// <summary>Returns whether two solutions are compatible (§18.1): every variable bound in both maps to the same term.</summary>
    /// <param name="left">The first solution.</param>
    /// <param name="right">The second solution.</param>
    /// <returns><see langword="true"/> when the solutions agree on every shared variable.</returns>
    private static bool AreCompatible(SparqlSolution left, SparqlSolution right)
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
    private static bool SharesVariable(SparqlSolution left, SparqlSolution right)
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
    private static SparqlSolution Merge(SparqlSolution left, SparqlSolution right)
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

    /// <summary>
    /// Resolves every <c>EXISTS</c>/<c>NOT EXISTS</c> in an expression for one solution and rewrites them to the
    /// constant boolean they evaluate to, yielding an <c>EXISTS</c>-free expression the synchronous evaluator can
    /// handle. Returns the expression unchanged when it carries no <c>EXISTS</c> (the fast path).
    /// </summary>
    /// <param name="expression">The expression (a <c>FILTER</c> condition, <c>BIND</c> expression, or join condition).</param>
    /// <param name="solution">The current solution the <c>EXISTS</c> sub-patterns are evaluated under.</param>
    /// <param name="graph">The active graph the EXISTS sub-patterns are evaluated in.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The expression with each <c>EXISTS</c>/<c>NOT EXISTS</c> replaced by its constant boolean result.</returns>
    private async ValueTask<ExpressionNode> ResolveExistsAsync(ExpressionNode expression, SparqlSolution solution, TermId graph, CancellationToken cancellationToken)
    {
        Dictionary<ExpressionNode, bool> truths = new(ReferenceEqualityComparer.Instance);
        foreach(ExpressionNode node in ExpressionWalker.Traverse(expression))
        {
            switch(node)
            {
                case ExistsExpression exists:
                {
                    truths[exists] = await ExistsAsync(exists.Inner, solution, graph, cancellationToken).ConfigureAwait(false);

                    break;
                }

                case NotExistsExpression notExists:
                {
                    truths[notExists] = !await ExistsAsync(notExists.Inner, solution, graph, cancellationToken).ConfigureAwait(false);

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
    /// Evaluates an <c>EXISTS</c> sub-pattern under a solution (§18.6): the pattern is wrapped in a synthetic
    /// <c>SELECT *</c> with the solution pre-bound as a single-row <c>VALUES</c> (so it sees the current bindings),
    /// normalized, translated, and run; <c>EXISTS</c> holds when the result is non-empty.
    /// </summary>
    /// <param name="pattern">The <c>EXISTS</c> sub-pattern.</param>
    /// <param name="solution">The current solution to pre-bind.</param>
    /// <param name="graph">The active graph the sub-pattern is evaluated in (so EXISTS inside a GRAPH form queries that graph).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the pattern has at least one solution compatible with the current bindings.</returns>
    private async ValueTask<bool> ExistsAsync(GraphPattern pattern, SparqlSolution solution, TermId graph, CancellationToken cancellationToken)
    {
        SparqlQuery query = SynthesizeExistsQuery(pattern, solution);
        using Utf8StringPool pool = new();
        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);
        AlgebraOperator algebra = SparqlTranslator.Translate(normalized);
        IReadOnlyList<SparqlSolution> solutions = await EvaluateInGraphAsync(algebra, graph, cancellationToken).ConfigureAwait(false);

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

    /// <summary>A post-join destructuring task for a variable-bearing quoted triple term: the fresh join variable the leapfrog binds to the matched triple-term value, and the component pattern to unify against that value.</summary>
    /// <param name="Target">The fresh backend join variable bound to the matched triple-term value.</param>
    /// <param name="Pattern">The triple-term component pattern to unify against the resolved value.</param>
    private sealed record TripleTermMatch(Variable Target, TripleComponent Pattern);

    /// <summary>A node in a quoted-triple-term destructuring pattern: a constant term, a (join) variable, or a nested triple term.</summary>
    private abstract record TripleTermComponent;

    /// <summary>A constant position in a triple-term pattern, matched by RDF-term equality.</summary>
    /// <param name="Term">The constant RDF term.</param>
    private sealed record ConstantComponent(RdfTerm Term): TripleTermComponent;

    /// <summary>A variable (or blank-node join variable) position in a triple-term pattern, unified against the actual term.</summary>
    /// <param name="Variable">The backend join variable.</param>
    private sealed record VariableComponent(Variable Variable): TripleTermComponent;

    /// <summary>A nested quoted triple term inside a triple-term pattern; the actual term must itself be a triple term, matched component-wise.</summary>
    /// <param name="Subject">The subject component.</param>
    /// <param name="Predicate">The predicate component.</param>
    /// <param name="Object">The object component.</param>
    private sealed record TripleComponent(TripleTermComponent Subject, TripleTermComponent Predicate, TripleTermComponent Object): TripleTermComponent;

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
