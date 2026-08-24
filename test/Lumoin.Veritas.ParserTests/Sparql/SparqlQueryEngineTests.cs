using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlQueryEngine"/> (Milestone C slice 1): end-to-end parse → normalize → translate →
/// execute of basic-graph-pattern SELECT/ASK queries against a hypertrie-backed data graph, plus the
/// projection/DISTINCT/slice modifiers and the not-yet-supported-operator guard.
/// </summary>
[TestClass]
internal sealed class SparqlQueryEngineTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A single triple pattern binds its subject and object variables to the matching triple's terms.</summary>
    [TestMethod]
    public async Task SingleTriplePatternBindsSubjectAndObject()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "alice", ValueIri(solution, "s"));
        Assert.AreEqual(Ex + "bob", ValueIri(solution, "o"));
    }

    /// <summary>
    /// Two group graph patterns joined on a shared variable run through the columnar operator-layer join: each
    /// group is a columnar basic-graph-pattern table, and the join merges them on the shared variable's encoded-id
    /// column without decoding. The result matches the row-layer join's semantics.
    /// </summary>
    [TestMethod]
    public async Task TwoColumnarGroupsJoinOnSharedVariable()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "knows", "b"), ("b", "knows", "c")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        //Separate { } groups do not merge into one BGP; the translator joins them, so each side reaches the join
        //as a columnar BGP table and the columnar hash join (shared ?b) fires.
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { { ?a :knows ?b } { ?b :knows ?c } }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Left ?a :knows ?b binds {(a,b),(b,c)}; right ?b :knows ?c binds {(a,b),(b,c)}. Joining on ?b keeps the
        //single compatible pair: left ?b=b meets right ?b=b, yielding (?a=a, ?b=b, ?c=c).
        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "a", ValueIri(solution, "a"));
        Assert.AreEqual(Ex + "b", ValueIri(solution, "b"));
        Assert.AreEqual(Ex + "c", ValueIri(solution, "c"));
    }

    /// <summary>
    /// A <c>FILTER(?v = &lt;iri&gt;)</c> over a columnar basic graph pattern is evaluated on the encoded column (the
    /// columnar term-equality fast path): only the rows bound to that IRI survive, and the unmatched rows are
    /// dropped without decoding.
    /// </summary>
    [TestMethod]
    public async Task ColumnarTermEqualityFilterKeepsMatchingRows()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(
            ("a", "knows", "b"), ("a", "knows", "c"), ("x", "knows", "y")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o FILTER(?s = :a) }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Only the two :a-subject rows survive; the :x row is filtered out.
        Assert.HasCount(2, solutions);
        Assert.IsTrue(solutions.All(s => ValueIri(s, "s") == Ex + "a"));
        string[] objects = [.. solutions.Select(s => ValueIri(s, "o"))];
        Assert.Contains(Ex + "b", objects);
        Assert.Contains(Ex + "c", objects);
    }

    /// <summary>A <c>FILTER(?v != &lt;iri&gt;)</c> over a columnar basic graph pattern keeps the rows bound to a different IRI.</summary>
    [TestMethod]
    public async Task ColumnarTermInequalityFilterDropsMatchingRows()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(
            ("a", "knows", "b"), ("x", "knows", "y")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o FILTER(?s != :a) }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "x", ValueIri(solution, "s"));
        Assert.AreEqual(Ex + "y", ValueIri(solution, "o"));
    }

    /// <summary>A columnar <c>BIND(?o AS ?friend)</c> copies the source variable's encoded column — the computed term already exists in the data, so the overlay reuses its data id (canonical reuse) and the new column equals the source.</summary>
    [TestMethod]
    public async Task ColumnarBindReusesDataIdForExistingTerm()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "knows", "b")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o BIND(?o AS ?friend) }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "b", ValueIri(solution, "o"));
        Assert.AreEqual(Ex + "b", ValueIri(solution, "friend"));
    }

    /// <summary>A columnar <c>BIND</c> producing a term not in the data (a <c>STR</c> string literal) is encoded through the query-scoped overlay; a following <c>DISTINCT</c> dedups those overlay ids and the boundary decodes them back — proving the overlay round-trips through the columnar operators.</summary>
    [TestMethod]
    public async Task ColumnarBindDistinctDedupsComputedTermsThroughOverlay()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "knows", "b"), ("c", "knows", "b"), ("a", "knows", "d")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?str WHERE { ?s :knows ?o BIND(STR(?o) AS ?str) }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Objects b, b, d → STR → two distinct strings; the duplicate "…/b" is removed on the overlay id.
        string[] values = [.. solutions.Select(s => Cast<Literal>(Value(s, "str")).Value.ToString())];
        Assert.HasCount(2, values);
        Assert.Contains(Ex + "b", values);
        Assert.Contains(Ex + "d", values);
    }

    /// <summary>A columnar <c>ORDER BY ?var</c> orders the rows by the key column's term order, decoding only that key column to compare.</summary>
    [TestMethod]
    public async Task ColumnarOrderByVariableOrdersRows()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "knows", "c"), ("a", "knows", "b"), ("a", "knows", "d")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { ?s :knows ?o } ORDER BY ?o", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //IRIs order by their string, so b < c < d.
        string[] ordered = [.. solutions.Select(s => ValueIri(s, "o"))];
        Assert.AreSequenceEqual(new[] { Ex + "b", Ex + "c", Ex + "d" }, ordered);
    }

    /// <summary>A columnar <c>ORDER BY DESC(?var)</c> followed by <c>LIMIT</c> orders descending and keeps the window — the survivors decode after the sort and slice.</summary>
    [TestMethod]
    public async Task ColumnarOrderByDescendingThenLimit()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "knows", "c"), ("a", "knows", "b"), ("a", "knows", "d")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { ?s :knows ?o } ORDER BY DESC(?o) LIMIT 2", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        string[] ordered = [.. solutions.Select(s => ValueIri(s, "o"))];
        Assert.AreSequenceEqual(new[] { Ex + "d", Ex + "c" }, ordered);
    }

    /// <summary>A condition-free <c>OPTIONAL</c> over columnar patterns evaluates as the columnar left join: a matched left row is extended, an unmatched one is kept with the optional variable unbound.</summary>
    [TestMethod]
    public async Task ColumnarOptionalExtendsMatchesAndKeepsUnmatched()
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [
                new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
                new DataTriple(Iri("a"), Iri("knows"), Iri("c")),
                new DataTriple(Iri("b"), Iri("name"), StringLiteral("B")),
            ], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o OPTIONAL { ?o :name ?n } }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        SparqlSolution matched = solutions.Single(s => ValueIri(s, "o") == Ex + "b");
        Assert.AreEqual("B", Cast<Literal>(Value(matched, "n")).Value.ToString());

        SparqlSolution unmatched = solutions.Single(s => ValueIri(s, "o") == Ex + "c");
        Assert.IsFalse(unmatched.TryGetValue(Variable("n"), out _));
    }

    /// <summary>A <c>GROUP BY ?var</c> over a columnar pattern partitions on the key column and counts each group, binding the group key and the per-group aggregate.</summary>
    [TestMethod]
    public async Task ColumnarGroupByCountsPerGroup()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "livesIn", "x"), ("b", "livesIn", "x"), ("c", "livesIn", "y")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?city (COUNT(?s) AS ?n) WHERE { ?s :livesIn ?city } GROUP BY ?city", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        SparqlSolution cityX = solutions.Single(s => ValueIri(s, "city") == Ex + "x");
        Assert.AreEqual("2", Cast<Literal>(Value(cityX, "n")).Value.ToString());

        SparqlSolution cityY = solutions.Single(s => ValueIri(s, "city") == Ex + "y");
        Assert.AreEqual("1", Cast<Literal>(Value(cityY, "n")).Value.ToString());
    }

    /// <summary>The execution trace is per-call pluggable: an engine built with no handler still reports this one run's per-operator strategy when a handler is passed to <c>EvaluateAsync</c> — the editor "visualize this query" surface. A single-pattern BGP feeding DISTINCT runs columnar end to end.</summary>
    [TestMethod]
    public async Task ExecutionTraceReportsColumnarOperators()
    {
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);

        //Engine built WITHOUT a handler; the handler is supplied per call.
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b")), new DataTriple(Iri("a"), Iri("knows"), Iri("c"))],
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?o WHERE { ?s :knows ?o }", pool);
        _ = await engine.EvaluateAsync(algebra, handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        SparqlExecutionTraceEvent bgp = events.Single(e => e.Operator == SparqlExecutionOperator.Bgp);
        Assert.AreEqual(SparqlExecutionStrategy.Columnar, bgp.Strategy);
        SparqlExecutionTraceEvent distinct = events.Single(e => e.Operator == SparqlExecutionOperator.Distinct);
        Assert.AreEqual(SparqlExecutionStrategy.Columnar, distinct.Strategy);
        Assert.AreEqual(2, distinct.RowsLeft);
    }

    /// <summary>A FILTER whose condition is not a term-(in)equality against an IRI bridges to the row form; the execution trace reports that operator's strategy as Row.</summary>
    [TestMethod]
    public async Task ExecutionTraceReportsRowBridgeForGeneralFilter()
    {
        List<SparqlExecutionTraceEvent> events = [];
        TraceHandler<SparqlExecutionTraceEvent> handler = (in SparqlExecutionTraceEvent traceEvent) => events.Add(traceEvent);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("knows"), Iri("b"))],
            executionTrace: handler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o FILTER(isIRI(?o)) }", pool);
        _ = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlExecutionTraceEvent filter = events.Single(e => e.Operator == SparqlExecutionOperator.Filter);
        Assert.AreEqual(SparqlExecutionStrategy.Row, filter.Strategy);
    }

    /// <summary>A constant-endpoint SERVICE joins the remote endpoint's solutions (returned through the injected transport) with the surrounding pattern.</summary>
    [TestMethod]
    public async Task ServiceJoinsRemoteSolutions()
    {
        using Utf8StringPool pool = new();
        SparqlResultSet remote = SparqlResultSet.ForSelect(
            [Utf8Strings.From("o"), Utf8Strings.From("z")],
            [
                new SparqlSolution([new SparqlBinding(Variable("o"), Iri("b")), new SparqlBinding(Variable("z"), Iri("c"))]),
                new SparqlSolution([new SparqlBinding(Variable("o"), Iri("x")), new SparqlBinding(Variable("z"), Iri("y"))]),
            ]);
        SparqlClient client = new((endpoint, query, accessContext, cancellationToken) => ValueTask.FromResult(remote));
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))], serviceClient: client, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { :a :p ?o . SERVICE <http://remote/sparql> { ?o :q ?z } }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Local binds ?o=:b; only the remote solution whose ?o is :b joins (the ?o=:x remote row is dropped).
        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "b", ValueIri(solution, "o"));
        Assert.AreEqual(Ex + "c", ValueIri(solution, "z"));
    }

    /// <summary>A SERVICE SILENT whose transport fails contributes the join identity, so the surrounding solutions survive.</summary>
    [TestMethod]
    public async Task ServiceSilentSurvivesTransportFailure()
    {
        using Utf8StringPool pool = new();
        SparqlClient failing = new((endpoint, query, accessContext, cancellationToken) => throw new System.InvalidOperationException("endpoint unreachable"));
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))], serviceClient: failing, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { :a :p ?o . SERVICE SILENT <http://remote/sparql> { ?o :q ?z } }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "b", ValueIri(solution, "o"));
        Assert.IsFalse(solution.TryGetValue(Variable("z"), out _));
    }

    /// <summary>A non-silent SERVICE with no transport supplied is refused.</summary>
    [TestMethod]
    public async Task ServiceWithoutTransportIsRefused()
    {
        using Utf8StringPool pool = new();
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { :a :p ?o . SERVICE <http://remote/sparql> { ?o :q ?z } }", pool);

        await Assert.ThrowsExactlyAsync<System.NotSupportedException>(
            async () => await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>A variable-endpoint SERVICE is a bound-join: each left binding's endpoint IRI is queried separately and the per-endpoint results join back into that binding (so endpoints are not crossed).</summary>
    [TestMethod]
    public async Task ServiceVariableEndpointResolvesPerBinding()
    {
        using Utf8StringPool pool = new();
        List<IriRef> calledEndpoints = [];
        SparqlClient client = new((endpoint, query, accessContext, cancellationToken) =>
        {
            calledEndpoints.Add(endpoint);

            //Each endpoint answers about a different subject; if the engine crossed endpoints, the join would drop the row.
            SparqlSolution row = endpoint.Value.Equals(Utf8Strings.From(Ex + "remote1"))
                ? new SparqlSolution([new SparqlBinding(Variable("x"), Iri("a")), new SparqlBinding(Variable("v"), Iri("v1"))])
                : new SparqlSolution([new SparqlBinding(Variable("x"), Iri("b")), new SparqlBinding(Variable("v"), Iri("v2"))]);

            return ValueTask.FromResult(SparqlResultSet.ForSelect([Utf8Strings.From("x"), Utf8Strings.From("v")], [row]));
        });
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("ep"), Iri("remote1")), new DataTriple(Iri("b"), Iri("ep"), Iri("remote2"))],
            serviceClient: client, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?x :ep ?e . SERVICE ?e { ?x :z ?v } }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, calledEndpoints);
        Assert.HasCount(2, solutions);

        SparqlSolution forA = solutions.Single(s => ValueIri(s, "x") == Ex + "a");
        Assert.AreEqual(Ex + "remote1", ValueIri(forA, "e"));
        Assert.AreEqual(Ex + "v1", ValueIri(forA, "v"));

        SparqlSolution forB = solutions.Single(s => ValueIri(s, "x") == Ex + "b");
        Assert.AreEqual(Ex + "remote2", ValueIri(forB, "e"));
        Assert.AreEqual(Ex + "v2", ValueIri(forB, "v"));
    }

    /// <summary>A variable-endpoint SERVICE SILENT whose endpoint variable is never bound contributes the join identity, so the surrounding solutions survive and the transport is never called.</summary>
    [TestMethod]
    public async Task ServiceVariableEndpointSilentPassesThroughWhenUnbound()
    {
        using Utf8StringPool pool = new();
        SparqlClient client = new((endpoint, query, accessContext, cancellationToken) => throw new System.InvalidOperationException("the transport must not be called for an unbound endpoint"));
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("ep"), Iri("remote1")), new DataTriple(Iri("b"), Iri("ep"), Iri("remote2"))],
            serviceClient: client, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?x :ep ?e . SERVICE SILENT ?unbound { ?x :z ?v } }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        Assert.IsTrue(solutions.All(s => !s.TryGetValue(Variable("v"), out _)));
    }

    /// <summary>A non-silent variable-endpoint SERVICE whose endpoint variable is unbound is refused (the endpoint cannot be determined).</summary>
    [TestMethod]
    public async Task ServiceVariableEndpointUnboundIsRefused()
    {
        using Utf8StringPool pool = new();
        SparqlClient client = new((endpoint, query, accessContext, cancellationToken) => ValueTask.FromResult(SparqlResultSet.ForSelect([], [])));
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("ep"), Iri("remote1"))], serviceClient: client, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?x :ep ?e . SERVICE ?unbound { ?x :z ?v } }", pool);

        await Assert.ThrowsExactlyAsync<System.NotSupportedException>(
            async () => await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>A single FROM graph becomes the default graph, overriding the engine's own data (§13.2).</summary>
    [TestMethod]
    public async Task FromSingleGraphScopesAndOverridesDefaultGraph()
    {
        using Utf8StringPool pool = new();
        SparqlQueryEngine baseEngine = await BuildEngineAsync(("base", "p", "base")).ConfigureAwait(false);
        DatasetClause clause = new(default, [FromIri("g1")], []);
        GraphSourceResolver resolver = (source, accessContext, cancellationToken) => GraphFor(source, ("g1", [new DataTriple(Iri("a"), Iri("p"), Iri("b"))]));

        SparqlQueryEngine scoped = await baseEngine.WithDatasetAsync(clause, resolver, TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s ?p ?o }", pool);
        IReadOnlyList<SparqlSolution> solutions = await scoped.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "a", ValueIri(solutions.Single(), "s"));
    }

    /// <summary>Multiple FROM graphs merge into one default graph.</summary>
    [TestMethod]
    public async Task MultipleFromGraphsMergeIntoTheDefaultGraph()
    {
        using Utf8StringPool pool = new();
        SparqlQueryEngine baseEngine = await BuildEngineAsync(("base", "p", "base")).ConfigureAwait(false);
        DatasetClause clause = new(default, [FromIri("g1"), FromIri("g2")], []);
        GraphSourceResolver resolver = (source, accessContext, cancellationToken) => GraphFor(source,
            ("g1", [new DataTriple(Iri("a"), Iri("p"), Iri("b"))]),
            ("g2", [new DataTriple(Iri("c"), Iri("p"), Iri("d"))]));

        SparqlQueryEngine scoped = await baseEngine.WithDatasetAsync(clause, resolver, TestContext.CancellationToken).ConfigureAwait(false);
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s ?p ?o }", pool);
        IReadOnlyList<SparqlSolution> solutions = await scoped.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        string[] subjects = [.. solutions.Select(solution => ValueIri(solution, "s"))];
        Assert.HasCount(2, subjects);
        Assert.Contains(Ex + "a", subjects);
        Assert.Contains(Ex + "c", subjects);
    }

    /// <summary>A FROM NAMED graph is exposed under its IRI to a GRAPH form, and (with no FROM) the default graph is empty.</summary>
    [TestMethod]
    public async Task FromNamedExposesGraphAndLeavesDefaultEmpty()
    {
        using Utf8StringPool pool = new();
        SparqlQueryEngine baseEngine = await BuildEngineAsync(("base", "p", "base")).ConfigureAwait(false);
        DatasetClause clause = new(default, [], [FromIri("g1")]);
        GraphSourceResolver resolver = (source, accessContext, cancellationToken) => GraphFor(source, ("g1", [new DataTriple(Iri("a"), Iri("p"), Iri("b"))]));

        SparqlQueryEngine scoped = await baseEngine.WithDatasetAsync(clause, resolver, TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator named = Translate("SELECT * WHERE { GRAPH <http://example.org/g1> { ?s ?p ?o } }", pool);
        IReadOnlyList<SparqlSolution> namedSolutions = await scoped.EvaluateAsync(named, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(Ex + "a", ValueIri(namedSolutions.Single(), "s"));

        AlgebraOperator defaultGraph = Translate("SELECT * WHERE { ?s ?p ?o }", pool);
        IReadOnlyList<SparqlSolution> defaultSolutions = await scoped.EvaluateAsync(defaultGraph, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsEmpty(defaultSolutions);
    }

    /// <summary>A FROM / FROM NAMED clause with no resolver is refused; an empty clause returns the same engine unchanged.</summary>
    [TestMethod]
    public async Task DatasetClauseRequiresResolverUnlessEmpty()
    {
        SparqlQueryEngine baseEngine = await BuildEngineAsync(("base", "p", "base")).ConfigureAwait(false);

        DatasetClause withFrom = new(default, [FromIri("g1")], []);
        await Assert.ThrowsExactlyAsync<System.NotSupportedException>(
            async () => await baseEngine.WithDatasetAsync(withFrom, resolver: null, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        DatasetClause empty = new(default, [], []);
        SparqlQueryEngine same = await baseEngine.WithDatasetAsync(empty, resolver: null, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreSame(baseEngine, same);
    }

    /// <summary>An access-control policy that denies every candidate triple yields no solutions (the local read is gated per candidate at descent leaf).</summary>
    [TestMethod]
    public async Task AccessControlDenyAllYieldsNoSolutions()
    {
        using Utf8StringPool pool = new();
        AccessControlDelegate denyAll = (request, cancellationToken) => new ValueTask<AccessDecision>(AccessDecision.Deny);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))],
            accessControl: denyAll, accessContext: new TestAccessContext("auditor"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s ?p ?o }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions);
    }

    /// <summary>The caller-supplied opaque AccessContext is handed through to the access-control policy unchanged (the PIC "who is asking").</summary>
    [TestMethod]
    public async Task AccessContextFlowsToThePolicy()
    {
        using Utf8StringPool pool = new();
        TestAccessContext supplied = new("auditor");
        AccessContext? seen = null;
        AccessControlDelegate capture = (request, cancellationToken) =>
        {
            seen = request.Context;

            return new ValueTask<AccessDecision>(AccessDecision.Allow);
        };
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), Iri("b"))],
            accessControl: capture, accessContext: supplied, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s ?p ?o }", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, solutions);
        Assert.AreSame(supplied, seen);
    }

    /// <summary>Access control gates a property-path read (which goes through the match-ops path, not the BGP join): a deny-all policy makes a transitive path yield nothing.</summary>
    [TestMethod]
    public async Task AccessControlGatesPropertyPathTraversal()
    {
        using Utf8StringPool pool = new();
        DataTriple[] data = [new DataTriple(Iri("a"), Iri("p"), Iri("b")), new DataTriple(Iri("b"), Iri("p"), Iri("c"))];
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { :a :p+ ?o }", pool);

        SparqlQueryEngine open = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(2, await open.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false));

        AccessControlDelegate denyAll = (request, cancellationToken) => new ValueTask<AccessDecision>(AccessDecision.Deny);
        SparqlQueryEngine gated = await SparqlQueryEngine.BuildAsync(data, accessControl: denyAll, accessContext: new TestAccessContext("auditor"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsEmpty(await gated.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>Access control gates a DESCRIBE (which goes through the match-ops path): a deny-all policy makes the description empty.</summary>
    [TestMethod]
    public async Task AccessControlGatesDescribe()
    {
        DataTriple[] data = [new DataTriple(Iri("a"), Iri("p"), Iri("b"))];
        IReadOnlyList<RdfTerm> resources = [Iri("a")];

        SparqlQueryEngine open = await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, await open.DescribeAsync(resources, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));

        AccessControlDelegate denyAll = (request, cancellationToken) => new ValueTask<AccessDecision>(AccessDecision.Deny);
        SparqlQueryEngine gated = await SparqlQueryEngine.BuildAsync(data, accessControl: denyAll, accessContext: new TestAccessContext("auditor"), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsEmpty(await gated.DescribeAsync(resources, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>Two triples sharing a variable join (the translator merges them into one BGP); the join variable is bound consistently and projected away when not selected.</summary>
    [TestMethod]
    public async Task TwoTriplePatternsJoinOnSharedVariable()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "knows", "bob"), ("bob", "knows", "carol")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?a ?c WHERE { ?a :knows ?b . ?b :knows ?c }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "alice", ValueIri(solution, "a"));
        Assert.AreEqual(Ex + "carol", ValueIri(solution, "c"));

        //?b is the join midpoint but not projected, so it is absent from the solution.
        Assert.IsFalse(solution.TryGetValue(Variable("b"), out _));
    }

    /// <summary>An ASK pattern that matches yields at least one solution; one that cannot match yields none.</summary>
    [TestMethod]
    public async Task AskYieldsSolutionsWhenThePatternMatches()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator matching = Translate("PREFIX : <http://example.org/> ASK { ?s :p ?o }", pool);
        AlgebraOperator notMatching = Translate("PREFIX : <http://example.org/> ASK { ?s :absent ?o }", pool);

        IReadOnlyList<SparqlSolution> matchingSolutions = await engine.EvaluateAsync(matching, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> notMatchingSolutions = await engine.EvaluateAsync(notMatching, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotEmpty(matchingSolutions);
        Assert.IsEmpty(notMatchingSolutions);
    }

    /// <summary>A constant predicate absent from the data graph makes the whole BGP match nothing.</summary>
    [TestMethod]
    public async Task ConstantAbsentFromDataYieldsNoSolutions()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :missing ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions);
    }

    /// <summary>DISTINCT eliminates duplicate solutions left after projection; without it the duplicates remain.</summary>
    [TestMethod]
    public async Task DistinctEliminatesDuplicateSolutions()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "shared"), ("bob", "p", "shared")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator withoutDistinct = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { ?s :p ?o }", pool);
        AlgebraOperator withDistinct = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?o WHERE { ?s :p ?o }", pool);

        //Both subjects map ?o to the same term, so projecting ?o yields two equal solutions; DISTINCT keeps one.
        Assert.HasCount(2, await engine.EvaluateAsync(withoutDistinct, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.ContainsSingle(await engine.EvaluateAsync(withDistinct, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>OFFSET/LIMIT window the solution sequence (the count is well-defined even though, without ORDER BY, which solutions survive is not).</summary>
    [TestMethod]
    public async Task OffsetAndLimitWindowTheSolutions()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "one"), ("alice", "p", "two"), ("alice", "p", "three")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { ?s :p ?o } LIMIT 1 OFFSET 1", pool);

        Assert.ContainsSingle(await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>A UNION yields the solutions of either branch.</summary>
    [TestMethod]
    public async Task UnionYieldsSolutionsOfEitherBranch()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob"), ("carol", "q", "dave")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { { ?s :p ?o } UNION { ?s :q ?o } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
    }

    /// <summary>MINUS removes left solutions that have a compatible right solution sharing a variable, and keeps the rest.</summary>
    [TestMethod]
    public async Task MinusRemovesCompatibleSharedVariableSolutions()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "x"), ("bob", "p", "y"), ("alice", "q", "x")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o MINUS { ?s :q ?o } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //{?s=alice,?o=x} is removed (the MINUS branch has it); {?s=bob,?o=y} stays (no compatible right solution).
        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "bob", ValueIri(solution, "s"));
    }

    /// <summary>A sub-SELECT joins into the enclosing pattern (Join over a ToMultiSet), correlating on the shared variable.</summary>
    [TestMethod]
    public async Task SubSelectJoinsIntoEnclosingPattern()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "q", "bob"), ("alice", "p", "thing")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?a :q ?b . { SELECT ?a WHERE { ?a :p ?o } } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "alice", ValueIri(solution, "a"));
        Assert.AreEqual(Ex + "bob", ValueIri(solution, "b"));
    }

    /// <summary>A FILTER keeps only the solutions whose numeric condition holds.</summary>
    [TestMethod]
    public async Task FilterKeepsSolutionsSatisfyingTheCondition()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("alice"), Iri("age"), IntegerLiteral(20)),
            new DataTriple(Iri("bob"), Iri("age"), IntegerLiteral(15))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :age ?a FILTER(?a > 18) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "alice", ValueIri(solution, "s"));
    }

    /// <summary>A BIND binds a computed value to a variable; integer arithmetic yields an xsd:integer.</summary>
    [TestMethod]
    public async Task BindBindsAComputedValue()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("alice"), Iri("age"), IntegerLiteral(20))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :age ?a BIND(?a + 1 AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.IsTrue(solution.TryGetValue(Variable("x"), out RdfTerm value));
        Assert.AreEqual("21", Cast<Literal>(value).Value.ToString());
    }

    /// <summary>An OPTIONAL extends solutions with the optional values where present, and keeps the others unextended.</summary>
    [TestMethod]
    public async Task OptionalExtendsWherePresentAndKeepsOtherwise()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "x"), ("alice", "q", "y"), ("bob", "p", "z")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o OPTIONAL { ?s :q ?v } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //alice has the optional ?v; bob does not but is still kept.
        Assert.HasCount(2, solutions);
        SparqlSolution alice = solutions.Single(s => ValueIri(s, "s") == Ex + "alice");
        SparqlSolution bob = solutions.Single(s => ValueIri(s, "s") == Ex + "bob");
        Assert.AreEqual(Ex + "y", ValueIri(alice, "v"));
        Assert.IsFalse(bob.TryGetValue(Variable("v"), out _));
    }

    /// <summary>ORDER BY sorts solutions by the key; DESC reverses the order.</summary>
    [TestMethod]
    public async Task OrderBySortsByKeyAscendingAndDescending()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("a"), Iri("p"), IntegerLiteral(3)),
            new DataTriple(Iri("b"), Iri("p"), IntegerLiteral(1)),
            new DataTriple(Iri("c"), Iri("p"), IntegerLiteral(2))).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator ascending = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o } ORDER BY ?o", pool);
        AlgebraOperator descending = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o } ORDER BY DESC(?o)", pool);

        IReadOnlyList<SparqlSolution> ascendingSolutions = await engine.EvaluateAsync(ascending, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> descendingSolutions = await engine.EvaluateAsync(descending, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreSequenceEqual(new[] { Ex + "b", Ex + "c", Ex + "a" }, Subjects(ascendingSolutions));
        Assert.AreSequenceEqual(new[] { Ex + "a", Ex + "c", Ex + "b" }, Subjects(descendingSolutions));
    }

    /// <summary>ORDER BY compares triple-term keys component-wise — subject, then predicate, then object, each by the SPARQL ordering — so subjects break ties by predicate, numeric objects order by value, and a literal object sorts before a nested triple-term object.</summary>
    [TestMethod]
    public async Task OrderByComparesTripleTermsComponentWise()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("a"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("s"), Iri("p"), IntegerLiteral(-456))),
            new DataTriple(Iri("b"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("s"), Iri("p"), IntegerLiteral(123))),
            new DataTriple(Iri("f"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("s"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("x"), Iri("y"), Iri("z")))),
            new DataTriple(Iri("c"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("s1"), Iri("a"), IntegerLiteral(999))),
            new DataTriple(Iri("e"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("s1"), Iri("p"), IntegerLiteral(999))),
            new DataTriple(Iri("d"), Iri("p"), new Lumoin.Veritas.Core.TripleTerm(Iri("s2"), Iri("p"), IntegerLiteral(900)))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator ordered = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?v } ORDER BY ?v", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(ordered, TestContext.CancellationToken).ConfigureAwait(false);

        //?v keys ascending: subject :s < :s1 < :s2; within :s the objects -456 < 123 < <<x y z>> (a literal object
        //sorts before a triple-term object); within :s1 the predicate :a < :p. So the carrier subjects order a,b,f,c,e,d.
        Assert.AreSequenceEqual(new[] { Ex + "a", Ex + "b", Ex + "f", Ex + "c", Ex + "e", Ex + "d" }, Subjects(solutions));
    }

    /// <summary>A quoted triple term with a variable component matches structurally — the variable binds to the stored term's component — and that binding joins the rest of the basic graph pattern.</summary>
    [TestMethod]
    public async Task VariableBearingTripleTermBindsComponentAndJoins()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("a"), Iri("type"), Iri("Thing")),
            new DataTriple(Iri("x"), Iri("q"), new Lumoin.Veritas.Core.TripleTerm(Iri("a"), Iri("b"), Iri("c"))),
            new DataTriple(Iri("y"), Iri("q"), new Lumoin.Veritas.Core.TripleTerm(Iri("e"), Iri("b"), Iri("c")))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :type :Thing . ?x :q <<( ?s :b :c )>> }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //?s must both be a :Thing and occur as the subject of a stored <<( ?s :b :c )>> term. Only :a satisfies both
        //(:e is a triple-term subject but not a :Thing); the join is on the destructured component variable.
        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "a", ValueIri(solution, "s"));
    }

    /// <summary>An inline VALUES block yields one solution per row, binding the listed variable.</summary>
    [TestMethod]
    public async Task InlineValuesYieldsOneSolutionPerRow()
    {
        SparqlQueryEngine engine = await EngineFromAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { VALUES ?x { :a :b } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        List<string> values = new(solutions.Count);
        foreach(SparqlSolution solution in solutions)
        {
            values.Add(ValueIri(solution, "x"));
        }

        values.Sort(StringComparer.Ordinal);
        Assert.AreSequenceEqual(new[] { Ex + "a", Ex + "b" }, values);
    }

    /// <summary>A FILTER using the CONTAINS string function keeps only the matching solutions.</summary>
    [TestMethod]
    public async Task FilterWithContainsKeepsMatchingStrings()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("alice"), Iri("name"), StringLiteral("Alice")),
            new DataTriple(Iri("bob"), Iri("name"), StringLiteral("Bob"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :name ?n FILTER(CONTAINS(?n, \"ice\")) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "alice", ValueIri(solution, "s"));
    }

    /// <summary>A BIND using the UCASE function binds the upper-cased string.</summary>
    [TestMethod]
    public async Task BindWithUpperCaseUpperCasesTheString()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("alice"), Iri("name"), StringLiteral("alice"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?u WHERE { ?s :name ?n BIND(UCASE(?n) AS ?u) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.IsTrue(solution.TryGetValue(Variable("u"), out RdfTerm value));
        Assert.AreEqual("ALICE", Cast<Literal>(value).Value.ToString());
    }

    /// <summary>A FILTER using the isIRI type test keeps only the IRI-bound solutions.</summary>
    [TestMethod]
    public async Task FilterWithIsIriKeepsOnlyIriBindings()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("alice"), Iri("p"), Iri("bob")),
            new DataTriple(Iri("alice"), Iri("p"), StringLiteral("a literal"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { ?s :p ?o FILTER(isIRI(?o)) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "bob", ValueIri(solution, "o"));
    }

    /// <summary>STRLEN counts Unicode code points, not UTF-8 bytes (here "café" — four code points, five bytes).</summary>
    [TestMethod]
    public async Task StrLenCountsCodePointsNotBytes()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("alice"), Iri("name"), StringLiteral("café"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?len WHERE { ?s :name ?n BIND(STRLEN(?n) AS ?len) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.IsTrue(solution.TryGetValue(Variable("len"), out RdfTerm value));
        Assert.AreEqual("4", Cast<Literal>(value).Value.ToString());
    }

    /// <summary>UCASE maps non-ASCII characters by the locale-independent Unicode case mapping ("café" → "CAFÉ").</summary>
    [TestMethod]
    public async Task UpperCaseMapsNonAsciiByUnicode()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("alice"), Iri("name"), StringLiteral("café"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?u WHERE { ?s :name ?n BIND(UCASE(?n) AS ?u) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.IsTrue(solution.TryGetValue(Variable("u"), out RdfTerm value));
        Assert.AreEqual("CAFÉ", Cast<Literal>(value).Value.ToString());
    }

    /// <summary>GROUP BY with COUNT(*) yields one solution per group with the group's count.</summary>
    [TestMethod]
    public async Task GroupByWithCountCountsPerGroup()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "knows", "bob"), ("alice", "knows", "carol"), ("dave", "knows", "eve")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s (COUNT(*) AS ?c) WHERE { ?s :knows ?o } GROUP BY ?s", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        Assert.AreEqual("2", LiteralValue(solutions.Single(s => ValueIri(s, "s") == Ex + "alice"), "c"));
        Assert.AreEqual("1", LiteralValue(solutions.Single(s => ValueIri(s, "s") == Ex + "dave"), "c"));
    }

    /// <summary>An implicit-group COUNT(*) (no GROUP BY) yields one solution counting all matches, and 0 over an empty pattern.</summary>
    [TestMethod]
    public async Task ImplicitGroupCountCountsAllAndZeroWhenEmpty()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "knows", "bob"), ("alice", "knows", "carol"), ("dave", "knows", "eve")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator all = Translate("PREFIX : <http://example.org/> SELECT (COUNT(*) AS ?c) WHERE { ?s :knows ?o }", pool);
        AlgebraOperator none = Translate("PREFIX : <http://example.org/> SELECT (COUNT(*) AS ?c) WHERE { ?s :nobody ?o }", pool);

        Assert.AreEqual("3", LiteralValue((await engine.EvaluateAsync(all, TestContext.CancellationToken).ConfigureAwait(false)).Single(), "c"));
        Assert.AreEqual("0", LiteralValue((await engine.EvaluateAsync(none, TestContext.CancellationToken).ConfigureAwait(false)).Single(), "c"));
    }

    /// <summary>An implicit-group COUNT(*) over a multi-pattern star answers from the factorised cardinality (the count-only fast path) and agrees with the drained solution count.</summary>
    [TestMethod]
    public async Task ImplicitGroupCountOverAStarMatchesTheDrainedCount()
    {
        //Two subjects with two objects on each of three predicates: the star
        //fans out to 2 · 2³ = 16 solutions.
        SparqlQueryEngine engine = await BuildEngineAsync(
            ("alice", "p1", "a1"), ("alice", "p1", "a2"),
            ("alice", "p2", "b1"), ("alice", "p2", "b2"),
            ("alice", "p3", "c1"), ("alice", "p3", "c2"),
            ("dave", "p1", "d1"), ("dave", "p1", "d2"),
            ("dave", "p2", "e1"), ("dave", "p2", "e2"),
            ("dave", "p3", "f1"), ("dave", "p3", "f2")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator counted = Translate("PREFIX : <http://example.org/> SELECT (COUNT(*) AS ?c) WHERE { ?s :p1 ?o1 . ?s :p2 ?o2 . ?s :p3 ?o3 }", pool);
        AlgebraOperator drained = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p1 ?o1 . ?s :p2 ?o2 . ?s :p3 ?o3 }", pool);

        IReadOnlyList<SparqlSolution> countSolutions = await engine.EvaluateAsync(counted, TestContext.CancellationToken).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> drainedSolutions = await engine.EvaluateAsync(drained, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(16, drainedSolutions);
        Assert.AreEqual("16", LiteralValue(countSolutions.Single(), "c"));
    }

    /// <summary>A DISTINCT projection onto the star key answers from the factorised group keys (one row per centre), and a DISTINCT onto a branch variable still answers correctly through the normal path.</summary>
    [TestMethod]
    public async Task DistinctStarKeyProjectionMatchesTheGroupKeys()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(
            ("alice", "p1", "a1"), ("alice", "p1", "a2"),
            ("alice", "p2", "b1"), ("alice", "p2", "b2"),
            ("alice", "p3", "c1"), ("alice", "p3", "c2"),
            ("dave", "p1", "d1"), ("dave", "p1", "d2"),
            ("dave", "p2", "e1"), ("dave", "p2", "e2"),
            ("dave", "p3", "f1"), ("dave", "p3", "f2")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        //The key projection: two centres, two rows — the factorised fast path.
        AlgebraOperator keys = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?s WHERE { ?s :p1 ?o1 . ?s :p2 ?o2 . ?s :p3 ?o3 }", pool);
        IReadOnlyList<SparqlSolution> keySolutions = await engine.EvaluateAsync(keys, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, keySolutions);
        Assert.Contains(s => ValueIri(s, "s") == Ex + "alice", keySolutions);
        Assert.Contains(s => ValueIri(s, "s") == Ex + "dave", keySolutions);

        //A branch projection is outside the key; the normal path answers it.
        AlgebraOperator branches = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?o1 WHERE { ?s :p1 ?o1 . ?s :p2 ?o2 . ?s :p3 ?o3 }", pool);
        IReadOnlyList<SparqlSolution> branchSolutions = await engine.EvaluateAsync(branches, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(4, branchSolutions);
    }

    /// <summary>ASK over a plain BGP short-circuits at the first solution: true on a matching pattern, false on a non-matching combination and on an absent constant, and a FILTER shape still answers through the full evaluation.</summary>
    [TestMethod]
    public async Task AskShortCircuitsOnAPlainPatternAndFallsBackElsewhere()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "knows", "bob"), ("alice", "knows", "carol"), ("dave", "knows", "eve")).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        //The streamed fast path: a match exists; a present predicate with no
        //two-hop continuation does not; an absent constant cannot encode.
        Assert.IsTrue(await engine.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?o }", pool), TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await engine.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?m . ?m :knows ?o }", pool), TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await engine.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :nobody ?o }", pool), TestContext.CancellationToken).ConfigureAwait(false));

        //A FILTER wraps the BGP in an expression operator — the fallback path.
        Assert.IsTrue(await engine.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?o FILTER(?o != :bob) }", pool), TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await engine.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?o FILTER(?s = :nobody) }", pool), TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>A LIMIT over a plain pattern caps the leaf's drain and yields exactly the window; OFFSET windows hold; and DISTINCT between the slice and the pattern declines the cap, so deduplication still sees every row.</summary>
    [TestMethod]
    public async Task LimitCapsTheLeafAndDistinctDeclinesTheCap()
    {
        //Two subjects with two objects on each of three predicates: 16 solutions,
        //grouped by subject in the pipeline's output order.
        SparqlQueryEngine engine = await BuildEngineAsync(
            ("alice", "p1", "a1"), ("alice", "p1", "a2"),
            ("alice", "p2", "b1"), ("alice", "p2", "b2"),
            ("alice", "p3", "c1"), ("alice", "p3", "c2"),
            ("dave", "p1", "d1"), ("dave", "p1", "d2"),
            ("dave", "p2", "e1"), ("dave", "p2", "e2"),
            ("dave", "p3", "f1"), ("dave", "p3", "f2")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        const string starPattern = "{ ?s :p1 ?o1 . ?s :p2 ?o2 . ?s :p3 ?o3 }";

        AlgebraOperator limited = Translate($"PREFIX : <http://example.org/> SELECT ?s WHERE {starPattern} LIMIT 3", pool);
        Assert.HasCount(3, await engine.EvaluateAsync(limited, TestContext.CancellationToken).ConfigureAwait(false));

        //OFFSET past most of the result: 16 solutions leave 2 after skipping 14.
        AlgebraOperator window = Translate($"PREFIX : <http://example.org/> SELECT ?s WHERE {starPattern} OFFSET 14 LIMIT 5", pool);
        Assert.HasCount(2, await engine.EvaluateAsync(window, TestContext.CancellationToken).ConfigureAwait(false));

        //DISTINCT between the slice and the pattern (on a branch variable, so
        //no fast path answers it): the first three raw rows bind at most two
        //distinct ?o1 values, so a wrongly-capped leaf could never produce
        //three — three rows proves the cap declined and dedup saw every row.
        AlgebraOperator distinctLimited = Translate($"PREFIX : <http://example.org/> SELECT DISTINCT ?o1 WHERE {starPattern} LIMIT 3", pool);
        Assert.HasCount(3, await engine.EvaluateAsync(distinctLimited, TestContext.CancellationToken).ConfigureAwait(false));
    }

    /// <summary>GROUP BY an arithmetic expression bound to a variable partitions by the expression's value (the cross product of :p {0,1,2} × :q {0,1,2} sums to 0..4, five groups).</summary>
    [TestMethod]
    public async Task GroupByArithmeticExpressionPartitionsByValue()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("s"), Iri("p"), IntegerLiteral(0)),
            new DataTriple(Iri("s"), Iri("p"), IntegerLiteral(1)),
            new DataTriple(Iri("s"), Iri("p"), IntegerLiteral(2)),
            new DataTriple(Iri("s"), Iri("q"), IntegerLiteral(0)),
            new DataTriple(Iri("s"), Iri("q"), IntegerLiteral(1)),
            new DataTriple(Iri("s"), Iri("q"), IntegerLiteral(2))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o12 (COUNT(?o1) AS ?c) WHERE { ?s :p ?o1 ; :q ?o2 } GROUP BY ((?o1 + ?o2) AS ?o12)", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Sums 0,1,2,3,4 — five groups; counts 1,2,3,2,1.
        Assert.HasCount(5, solutions);
    }

    /// <summary>Two predicates on one subject with distinct object variables yield the full cross product of their objects (3 × 3 = 9).</summary>
    [TestMethod]
    public async Task SharedSubjectDistinctObjectsCrossProduct()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("s"), Iri("p"), IntegerLiteral(0)),
            new DataTriple(Iri("s"), Iri("p"), IntegerLiteral(1)),
            new DataTriple(Iri("s"), Iri("p"), IntegerLiteral(2)),
            new DataTriple(Iri("s"), Iri("q"), IntegerLiteral(0)),
            new DataTriple(Iri("s"), Iri("q"), IntegerLiteral(1)),
            new DataTriple(Iri("s"), Iri("q"), IntegerLiteral(2))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o1 ?o2 WHERE { ?s :p ?o1 ; :q ?o2 }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(9, solutions);
    }

    /// <summary>GROUP BY with SUM totals the grouped numeric values.</summary>
    [TestMethod]
    public async Task GroupByWithSumTotalsPerGroup()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("a"), Iri("val"), IntegerLiteral(1)),
            new DataTriple(Iri("a"), Iri("val"), IntegerLiteral(2)),
            new DataTriple(Iri("b"), Iri("val"), IntegerLiteral(5))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s (SUM(?v) AS ?t) WHERE { ?s :val ?v } GROUP BY ?s", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        Assert.AreEqual("3", LiteralValue(solutions.Single(s => ValueIri(s, "s") == Ex + "a"), "t"));
        Assert.AreEqual("5", LiteralValue(solutions.Single(s => ValueIri(s, "s") == Ex + "b"), "t"));
    }

    /// <summary>GROUP_CONCAT over an implicit group joins the values' lexical forms with the default single space.</summary>
    [TestMethod]
    public async Task GroupConcatJoinsWithDefaultSpace()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("s"), Iri("p"), StringLiteral("1")),
            new DataTriple(Iri("s"), Iri("p"), StringLiteral("22"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT (GROUP_CONCAT(?o) AS ?g) WHERE { ?s :p ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        string concatenated = LiteralValue(solution, "g");
        Assert.IsTrue(concatenated is "1 22" or "22 1", $"Expected '1 22' or '22 1' but got '{concatenated}'.");
    }

    /// <summary>HAVING filters the groups by an aggregate condition.</summary>
    [TestMethod]
    public async Task HavingFiltersGroupsByAggregate()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "knows", "bob"), ("alice", "knows", "carol"), ("dave", "knows", "eve")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :knows ?o } GROUP BY ?s HAVING(COUNT(*) > 1)", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Only alice has more than one match.
        SparqlSolution solution = solutions.Single();
        Assert.AreEqual(Ex + "alice", ValueIri(solution, "s"));
    }

    /// <summary>A one-or-more property path between two variables enumerates every connected (subject, object) pair, including transitive reach.</summary>
    [TestMethod]
    public async Task OneOrMorePathEnumeratesConnectedPairs()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "p", "b"), ("b", "p", "c")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s ?o WHERE { ?s :p+ ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //a→b, a→c (transitively), b→c.
        Assert.HasCount(3, solutions);
        Assert.Contains(s => ValueIri(s, "s") == Ex + "a" && ValueIri(s, "o") == Ex + "c", solutions, "Expected the transitive a→c pair.");
    }

    /// <summary>A one-or-more path from a bound subject enumerates the reachable objects.</summary>
    [TestMethod]
    public async Task OneOrMorePathFromBoundSubjectEnumeratesReachable()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "p", "b"), ("b", "p", "c")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { :a :p+ ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        Assert.Contains(s => ValueIri(s, "o") == Ex + "b", solutions);
        Assert.Contains(s => ValueIri(s, "o") == Ex + "c", solutions);
    }

    /// <summary>A zero-or-more path is reflexive: every node reaches itself in zero steps.</summary>
    [TestMethod]
    public async Task ZeroOrMorePathIsReflexive()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("a", "p", "b")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s ?o WHERE { ?s :p* ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //(a,a), (a,b), (b,b) — both nodes reach themselves, and a reaches b.
        Assert.HasCount(3, solutions);
        Assert.Contains(s => ValueIri(s, "s") == Ex + "a" && ValueIri(s, "o") == Ex + "a", solutions, "Expected the reflexive a→a pair.");
    }

    /// <summary>An operator outside the supported set (here a SERVICE remote-endpoint block) is refused with a descriptive exception.</summary>
    [TestMethod]
    public async Task UnsupportedOperatorIsRefused()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { SERVICE <http://example.org/sparql> { ?s :p ?o } }", pool);

        NotSupportedException? caught = null;
        try
        {
            _ = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
        }
        catch(NotSupportedException exception)
        {
            caught = exception;
        }

        Assert.IsNotNull(caught);
    }

    /// <summary>A constant <c>GRAPH &lt;iri&gt;</c> queries exactly that named graph — never the default graph or a sibling named graph.</summary>
    [TestMethod]
    public async Task GraphWithConstantIriQueriesThatNamedGraph()
    {
        SparqlQueryEngine engine = await DatasetEngineAsync(
            [new DataTriple(Iri("d"), Iri("p"), Iri("default"))],
            ("g1", new DataTriple(Iri("s"), Iri("p"), Iri("one"))),
            ("g2", new DataTriple(Iri("s"), Iri("p"), Iri("two")))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { GRAPH :g1 { ?s :p ?o } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "one", ValueIri(solutions.Single(), "o"));
    }

    /// <summary>A variable <c>GRAPH ?g</c> ranges over every named graph (never the default), binding <c>?g</c> to each graph's name and unioning the per-graph matches.</summary>
    [TestMethod]
    public async Task GraphWithVariableBindsGraphNameAndRangesOverNamedGraphs()
    {
        SparqlQueryEngine engine = await DatasetEngineAsync(
            [new DataTriple(Iri("d"), Iri("p"), Iri("default"))],
            ("g1", new DataTriple(Iri("s"), Iri("p"), Iri("one"))),
            ("g2", new DataTriple(Iri("s"), Iri("p"), Iri("two")))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?g ?o WHERE { GRAPH ?g { ?s :p ?o } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //One row per named graph; the default graph's :default triple is excluded, and ?g binds each graph name.
        Assert.HasCount(2, solutions);
        Assert.Contains(s => ValueIri(s, "g") == Ex + "g1" && ValueIri(s, "o") == Ex + "one", solutions, "Expected the g1 row.");
        Assert.Contains(s => ValueIri(s, "g") == Ex + "g2" && ValueIri(s, "o") == Ex + "two", solutions, "Expected the g2 row.");
    }

    /// <summary>A constant <c>GRAPH</c> naming an IRI that is not a named graph in the dataset contributes no solutions (it does not fall back to the default graph).</summary>
    [TestMethod]
    public async Task GraphWithIriNotInDatasetYieldsNoSolutions()
    {
        SparqlQueryEngine engine = await DatasetEngineAsync(
            [new DataTriple(Iri("s"), Iri("p"), Iri("default"))],
            ("g1", new DataTriple(Iri("s"), Iri("p"), Iri("one")))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?o WHERE { GRAPH :missing { ?s :p ?o } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions);
    }

    /// <summary>CONCAT joins its string arguments' lexical forms.</summary>
    [TestMethod]
    public async Task ConcatJoinsStringArguments()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("hello"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :p ?v BIND(CONCAT(?v, \" \", \"world\") AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("hello world", LiteralValue(solutions.Single(), "x"));
    }

    /// <summary>SUBSTR takes a 1-based code-point substring of the given length.</summary>
    [TestMethod]
    public async Task SubstrTakesCodePointSubstring()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("hello"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :p ?v BIND(SUBSTR(?v, 2, 3) AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("ell", LiteralValue(solutions.Single(), "x"));
    }

    /// <summary>STRBEFORE and STRAFTER split a string at the first occurrence of the separator.</summary>
    [TestMethod]
    public async Task StrBeforeAndStrAfterSplitAtSeparator()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("user@host"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(
            "PREFIX : <http://example.org/> SELECT ?before ?after WHERE { ?s :p ?v BIND(STRBEFORE(?v, \"@\") AS ?before) BIND(STRAFTER(?v, \"@\") AS ?after) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();
        Assert.AreEqual("user", LiteralValue(solution, "before"));
        Assert.AreEqual("host", LiteralValue(solution, "after"));
    }

    /// <summary>ENCODE_FOR_URI percent-encodes the reserved characters and leaves the unreserved ones intact.</summary>
    [TestMethod]
    public async Task EncodeForUriPercentEncodesReservedCharacters()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("a b/c"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :p ?v BIND(ENCODE_FOR_URI(?v) AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("a%20b%2Fc", LiteralValue(solutions.Single(), "x"));
    }

    /// <summary>REGEX in a FILTER keeps only the solutions whose value matches the pattern.</summary>
    [TestMethod]
    public async Task RegexFilterKeepsMatchingSolutions()
    {
        SparqlQueryEngine engine = await EngineFromAsync(
            new DataTriple(Iri("a"), Iri("p"), StringLiteral("hello")),
            new DataTriple(Iri("b"), Iri("p"), StringLiteral("world"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?v FILTER(REGEX(?v, \"^h\")) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "a", ValueIri(solutions.Single(), "s"));
    }

    /// <summary>REPLACE rewrites every regex match in the value.</summary>
    [TestMethod]
    public async Task ReplaceRewritesEveryMatch()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("hello"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :p ?v BIND(REPLACE(?v, \"l\", \"L\") AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("heLLo", LiteralValue(solutions.Single(), "x"));
    }

    /// <summary>REGEX consults the injected regular-expression seam rather than a fixed engine.</summary>
    [TestMethod]
    public async Task RegexUsesInjectedResolverSeam()
    {
        //A resolver that ignores the pattern and matches only "X": a pattern that could never match "X"
        //under the built-in engine still keeps the row, proving the seam decided the match.
        SparqlRegexResolver resolver = static (pattern, flags) => new Regex("X");
        SparqlExpressionContext context = new(StubRandomness, StubHash, StubNow, regexResolver: resolver);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(
            [new DataTriple(Iri("a"), Iri("p"), StringLiteral("X"))], context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?v FILTER(REGEX(?v, \"^zzz$\")) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "a", ValueIri(solutions.Single(), "s"));
    }

    /// <summary>IRI builds a named node from a string lexical form.</summary>
    [TestMethod]
    public async Task IriBuildsNamedNodeFromString()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral(Ex + "z"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :p ?v BIND(IRI(?v) AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "z", ValueIri(solutions.Single(), "x"));
    }

    /// <summary>STRDT builds a literal with the given datatype from a simple-string lexical form.</summary>
    [TestMethod]
    public async Task StrDtBuildsTypedLiteral()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("42"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(
            "PREFIX : <http://example.org/> PREFIX xsd: <http://www.w3.org/2001/XMLSchema#> SELECT ?x WHERE { ?s :p ?v BIND(STRDT(?v, xsd:integer) AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(solutions.Single().TryGetValue(Variable("x"), out RdfTerm value));
        Literal literal = Cast<Literal>(value);
        Assert.AreEqual("42", literal.Value.ToString());
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#integer", literal.Datatype.Iri.ToString());
    }

    /// <summary>STRLANG builds a language-tagged literal from a simple-string lexical form and tag.</summary>
    [TestMethod]
    public async Task StrLangBuildsLanguageTaggedLiteral()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("hi"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?x WHERE { ?s :p ?v BIND(STRLANG(?v, \"en\") AS ?x) }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(solutions.Single().TryGetValue(Variable("x"), out RdfTerm value));
        Literal literal = Cast<Literal>(value);
        Assert.AreEqual("hi", literal.Value.ToString());
        Assert.AreEqual("en", literal.Language?.ToString());
    }

    /// <summary>The date-time accessors return the integer components of an xsd:dateTime in its own timezone.</summary>
    [TestMethod]
    public async Task DateTimeAccessorsReturnComponents()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), DateTimeLiteral("2011-01-10T14:45:13.815-05:00"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(
            "PREFIX : <http://example.org/> SELECT ?y ?mo ?d ?h ?mi ?sec WHERE { ?s :p ?v BIND(YEAR(?v) AS ?y) BIND(MONTH(?v) AS ?mo) BIND(DAY(?v) AS ?d) BIND(HOURS(?v) AS ?h) BIND(MINUTES(?v) AS ?mi) BIND(SECONDS(?v) AS ?sec) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        Assert.AreEqual("2011", LiteralValue(solution, "y"));
        Assert.AreEqual("1", LiteralValue(solution, "mo"));
        Assert.AreEqual("10", LiteralValue(solution, "d"));
        Assert.AreEqual("14", LiteralValue(solution, "h"));
        Assert.AreEqual("45", LiteralValue(solution, "mi"));
        Assert.AreEqual("13.815", LiteralValue(solution, "sec"));
    }

    /// <summary>TZ returns the timezone designator and TIMEZONE returns it as an xsd:dayTimeDuration.</summary>
    [TestMethod]
    public async Task TzAndTimezoneReturnTheOffset()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), DateTimeLiteral("2011-01-10T14:45:13-05:00"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?tz ?dur WHERE { ?s :p ?v BIND(TZ(?v) AS ?tz) BIND(TIMEZONE(?v) AS ?dur) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        Assert.AreEqual("-05:00", LiteralValue(solution, "tz"));
        Assert.AreEqual("-PT5H", LiteralValue(solution, "dur"));
    }

    /// <summary>TRIPLE builds a triple term and SUBJECT/PREDICATE/OBJECT read its components back.</summary>
    [TestMethod]
    public async Task TripleBuilderAndComponentAccessorsRoundTrip()
    {
        SparqlQueryEngine engine = await EngineFromAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(
            "PREFIX : <http://example.org/> SELECT ?subj ?pred ?obj WHERE { BIND(TRIPLE(:a, :b, :c) AS ?t) BIND(SUBJECT(?t) AS ?subj) BIND(PREDICATE(?t) AS ?pred) BIND(OBJECT(?t) AS ?obj) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        Assert.AreEqual(Ex + "a", ValueIri(solution, "subj"));
        Assert.AreEqual(Ex + "b", ValueIri(solution, "pred"));
        Assert.AreEqual(Ex + "c", ValueIri(solution, "obj"));
    }

    /// <summary>FILTER EXISTS keeps only the solutions for which the sub-pattern (under the current bindings) has a match.</summary>
    [TestMethod]
    public async Task FilterExistsKeepsSolutionsWithAMatch()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "o1"), ("alice", "q", "o2"), ("bob", "p", "o3")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o FILTER EXISTS { ?s :q ?x } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "alice", ValueIri(solutions.Single(), "s"));
    }

    /// <summary>FILTER NOT EXISTS keeps only the solutions for which the sub-pattern (under the current bindings) has no match.</summary>
    [TestMethod]
    public async Task FilterNotExistsKeepsSolutionsWithoutAMatch()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "o1"), ("alice", "q", "o2"), ("bob", "p", "o3")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o FILTER NOT EXISTS { ?s :q ?x } }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(Ex + "bob", ValueIri(solutions.Single(), "s"));
    }

    /// <summary>The hash functions return the lowercase hex digest of the value (here exercised through the default in-process digest seam).</summary>
    [TestMethod]
    public async Task HashFunctionsReturnHexDigest()
    {
        SparqlQueryEngine engine = await EngineFromAsync(new DataTriple(Iri("a"), Iri("p"), StringLiteral("abc"))).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?md5 ?sha WHERE { ?s :p ?v BIND(MD5(?v) AS ?md5) BIND(SHA256(?v) AS ?sha) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", LiteralValue(solution, "md5"));
        Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", LiteralValue(solution, "sha"));
    }

    /// <summary>RAND draws its uniform double from the injected randomness seam.</summary>
    [TestMethod]
    public async Task RandUsesInjectedRandomnessSeam()
    {
        SparqlExpressionContext context = new(StubRandomness, StubHash, StubNow);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync([new DataTriple(Iri("a"), Iri("p"), Iri("b"))], context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?r WHERE { ?s :p ?v BIND(RAND() AS ?r) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        //RAND() returns an xsd:double; its canonical lexical form for 0.25 is the mantissa-and-exponent "2.5E-1".
        Assert.AreEqual("2.5E-1", LiteralValue(solution, "r"));
    }

    /// <summary>
    /// A blank node in a query pattern is a non-distinguished join variable, not a ground value: it joins across its
    /// occurrences and is never projected. Here a reified-triple subject <c>&lt;&lt;:a :b :c&gt;&gt;</c> (which
    /// normalizes to a fresh-reifier blank node joined via <c>rdf:reifies</c>) matches the reifier the data's reified
    /// triple introduced, yielding both that reifier's triples.
    /// </summary>
    [TestMethod]
    public async Task ReifiedTripleSubjectMatchesViaBlankNodeJoin()
    {
        //Data: << :a :b :c >> :q :z .  →  _:r rdf:reifies <<(:a :b :c)>> ; _:r :q :z .
        SparqlQueryEngine engine = await EngineFromTurtleAsync("PREFIX : <http://example.org/>\n<< :a :b :c >> :q :z .\n").ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { << :a :b :c >> ?p ?o }", pool);

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        //Two rows: (?p = rdf:reifies, ?o = <<(:a :b :c)>>) and (?p = :q, ?o = :z).
        Assert.HasCount(2, solutions);
        Assert.Contains(s => ValueIri(s, "p") == "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies", solutions, "Expected a row binding ?p to rdf:reifies.");
        Assert.Contains(s => ValueIri(s, "p") == Ex + "q" && ValueIri(s, "o") == Ex + "z", solutions, "Expected a row binding ?p=:q, ?o=:z.");
    }

    /// <summary>UUID and STRUUID draw their UUID from the injected randomness seam (UUID as a urn:uuid: IRI, STRUUID as the bare string).</summary>
    [TestMethod]
    public async Task UuidAndStrUuidUseInjectedRandomnessSeam()
    {
        SparqlExpressionContext context = new(StubRandomness, StubHash, StubNow);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync([new DataTriple(Iri("a"), Iri("p"), Iri("b"))], context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?u ?su WHERE { ?s :p ?v BIND(UUID() AS ?u) BIND(STRUUID() AS ?su) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        Assert.AreEqual("urn:uuid:" + StubGuid.ToString("D"), ValueIri(solution, "u"));
        Assert.AreEqual(StubGuid.ToString("D"), LiteralValue(solution, "su"));
    }

    /// <summary>NOW returns one fixed query-execution instant: every NOW() in a query agrees, and the value is an xsd:dateTime.</summary>
    [TestMethod]
    public async Task NowIsConstantAcrossTheQuery()
    {
        SparqlExpressionContext context = new(StubRandomness, StubHash, StubNow);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync([new DataTriple(Iri("a"), Iri("p"), Iri("b"))], context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?a ?b WHERE { ?s :p ?v BIND(NOW() AS ?a) BIND(NOW() AS ?b) }", pool);

        SparqlSolution solution = (await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false)).Single();

        Assert.AreEqual(LiteralValue(solution, "a"), LiteralValue(solution, "b"));
        Assert.IsTrue(solution.TryGetValue(Variable("a"), out RdfTerm value));
        Assert.AreEqual("http://www.w3.org/2001/XMLSchema#dateTime", Cast<Literal>(value).Datatype.Iri.ToString());
    }

    /// <summary>CONSTRUCT instantiates the template per solution, dropping ill-formed triples (an unbound variable, or a literal in the subject position).</summary>
    [TestMethod]
    public void ConstructInstantiatesTemplateSkippingIllFormedTriples()
    {
        //Template: ?s :p ?o.
        TriplePattern[] template =
        [
            new TriplePattern(
                Lumoin.Veritas.Core.Sourcing.SourceSpan.None,
                new VariableTerm(Lumoin.Veritas.Core.Sourcing.SourceSpan.None, Variable("s")),
                new ConstantTerm(Lumoin.Veritas.Core.Sourcing.SourceSpan.None, Iri("p")),
                new VariableTerm(Lumoin.Veritas.Core.Sourcing.SourceSpan.None, Variable("o")))
        ];

        SparqlSolution wellFormed = new([new SparqlBinding(Variable("s"), Iri("a")), new SparqlBinding(Variable("o"), Iri("b"))]);
        SparqlSolution literalSubject = new([new SparqlBinding(Variable("s"), StringLiteral("x")), new SparqlBinding(Variable("o"), Iri("c"))]);
        SparqlSolution unboundObject = new([new SparqlBinding(Variable("s"), Iri("a"))]);

        List<Quad> quads = SparqlGraphConstruction.Construct(template, [wellFormed, literalSubject, unboundObject]);

        //Only the first solution yields a legal triple; the literal-subject and unbound-object solutions are dropped.
        Assert.ContainsSingle(quads);
        Assert.AreEqual(Ex + "a", Cast<NamedNode>(quads[0].Subject).Iri.ToString());
        Assert.AreEqual(Ex + "b", Cast<NamedNode>(quads[0].Object).Iri.ToString());
    }

    /// <summary>DESCRIBE's default strategy is the Concise Bounded Description: a resource's triples plus those of the blank nodes it reaches.</summary>
    [TestMethod]
    public async Task DescribeReturnsConciseBoundedDescription()
    {
        //:a :p _:b ; :r :d .  _:b :q :c .  Describing :a follows the blank node _:b.
        SparqlQueryEngine engine = await EngineFromTurtleAsync("PREFIX : <http://example.org/>\n:a :p [ :q :c ] ; :r :d .\n").ConfigureAwait(false);

        IReadOnlyList<Quad> description = await engine.DescribeAsync([Iri("a")], strategy: null, TestContext.CancellationToken).ConfigureAwait(false);

        //Three triples: (:a :p _:b), (:a :r :d), and the followed (_:b :q :c).
        Assert.HasCount(3, description);
        Assert.Contains(q => q.Predicate.Iri.ToString() == Ex + "r" && Cast<NamedNode>(q.Object).Iri.ToString() == Ex + "d", description, "Expected the direct :a :r :d triple.");
        Assert.Contains(q => q.Predicate.Iri.ToString() == Ex + "q" && Cast<NamedNode>(q.Object).Iri.ToString() == Ex + "c", description, "Expected the followed blank-node :q :c triple.");
    }

    /// <summary>A deterministic stub UUID for the injected-randomness tests.</summary>
    private static Guid StubGuid { get; } = new("12345678-1234-1234-1234-1234567890ab");

    /// <summary>A fixed query-execution instant for the injected-NOW tests.</summary>
    private static DateTimeOffset StubNow { get; } = new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>A deterministic randomness seam: a fixed UUID, otherwise the uniform double 0.25.</summary>
    /// <param name="request">The randomness request.</param>
    /// <returns>The stubbed value.</returns>
    private static RandomnessValue StubRandomness(in RandomnessRequest request)
    {
        return request.Kind == RandomnessKind.Uuid
            ? new RandomnessValue(RandomnessKind.Uuid, 0, StubGuid, default)
            : new RandomnessValue(RandomnessKind.UniformDouble, 0.25, default, default);
    }

    /// <summary>A no-op digest seam for tests that do not exercise hashing.</summary>
    /// <param name="algorithm">The (ignored) algorithm.</param>
    /// <param name="data">The (ignored) data.</param>
    /// <returns>An empty digest.</returns>
    private static byte[] StubHash(SparqlHashAlgorithm algorithm, ReadOnlySpan<byte> data) => [];

    /// <summary>Builds an <c>xsd:dateTime</c> literal term.</summary>
    /// <param name="lexical">The dateTime lexical form.</param>
    /// <returns>The literal term.</returns>
    private static Literal DateTimeLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#dateTime")));
    }

    /// <summary>Builds an engine over a data graph of IRI triples under the example namespace.</summary>
    /// <param name="triples">The triples, each given as local names completed with the example prefix.</param>
    /// <returns>An engine over the graph.</returns>
    private async Task<SparqlQueryEngine> BuildEngineAsync(params (string Subject, string Predicate, string Object)[] triples)
    {
        List<DataTriple> data = new(triples.Length);
        foreach((string subject, string predicate, string @object) in triples)
        {
            data.Add(new DataTriple(Iri(subject), Iri(predicate), Iri(@object)));
        }

        return await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an engine over an explicit set of data triples (used when objects are literals rather than IRIs).</summary>
    /// <param name="triples">The data triples.</param>
    /// <returns>An engine over the graph.</returns>
    private async Task<SparqlQueryEngine> EngineFromAsync(params DataTriple[] triples)
    {
        return await SparqlQueryEngine.BuildAsync(triples, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an engine over a dataset: a default graph plus named graphs, each named by an example-namespace IRI local name.</summary>
    /// <param name="defaultGraph">The default-graph triples.</param>
    /// <param name="namedGraphs">The named graphs, each a graph-name local name paired with its triples.</param>
    /// <returns>An engine over the dataset.</returns>
    private async Task<SparqlQueryEngine> DatasetEngineAsync(IEnumerable<DataTriple> defaultGraph, params (string GraphName, DataTriple Triple)[] namedGraphs)
    {
        List<(RdfTerm Name, IEnumerable<DataTriple> Triples)> named = new(namedGraphs.Length);
        foreach((string graphName, DataTriple triple) in namedGraphs)
        {
            named.Add((Iri(graphName), new[] { triple }));
        }

        return await SparqlQueryEngine.BuildDatasetAsync(defaultGraph, named, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an engine over a data graph given as Turtle text (parsed through the production reader, so RDF-1.2 reification/triple-term sugar lowers exactly as in the conformance harness).</summary>
    /// <param name="turtle">The Turtle data-graph source.</param>
    /// <returns>An engine over the parsed graph.</returns>
    private async Task<SparqlQueryEngine> EngineFromTurtleAsync(string turtle)
    {
        Lumoin.Veritas.Core.Diagnostics.DiagnosticBag diagnostics = new();
        List<DataTriple> data = [];
        await foreach(Quad quad in Lumoin.Veritas.Turtle.TurtleReader.ReadAsync(
            System.Text.Encoding.UTF8.GetBytes(turtle),
            Lumoin.Veritas.Turtle.TurtleSyntax.Turtle,
            diagnostics,
            pool: null,
            baseIri: "http://example.org/",
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            data.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        Assert.IsFalse(diagnostics.HasErrors, "The Turtle data graph should parse without error.");

        return await SparqlQueryEngine.BuildAsync(data, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an <c>xsd:integer</c> literal term.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal term.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer")));
    }

    /// <summary>Builds an <c>xsd:string</c> literal term.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The literal term.</returns>
    private static Literal StringLiteral(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));
    }

    /// <summary>Parses, normalizes, and translates a query to its algebra.</summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping parsed and lowered handles alive for the query's evaluation.</param>
    /// <returns>The query's algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Builds an example-namespace dataset-clause IRI from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The IRI reference.</returns>
    private static IriRef FromIri(string localName)
    {
        return new IriRef(Utf8Strings.From(Ex + localName), default);
    }

    /// <summary>A minimal concrete <see cref="AccessContext"/> for the access-control threading tests (its content is opaque to the engine).</summary>
    /// <param name="Who">The caller identity carried by the context.</param>
    private sealed record TestAccessContext(string Who) : AccessContext;

    /// <summary>A canned <see cref="GraphSourceResolver"/> body: streams the triples of whichever example-namespace graph the source IRI names, or the empty graph.</summary>
    /// <param name="source">The clause IRI to resolve.</param>
    /// <param name="graphs">The available graphs as (local-name, triples) pairs.</param>
    /// <returns>The named graph's triples, or the empty graph, as an async stream.</returns>
    private static async IAsyncEnumerable<DataTriple> GraphFor(IriRef source, params (string LocalName, DataTriple[] Triples)[] graphs)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach((string localName, DataTriple[] triples) in graphs)
        {
            if(source.Value.Equals(Utf8Strings.From(Ex + localName)))
            {
                foreach(DataTriple triple in triples)
                {
                    yield return triple;
                }

                yield break;
            }
        }
    }

    /// <summary>Builds a SPARQL variable from its name (without the leading marker).</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }

    /// <summary>Returns the <c>?s</c> IRI of each solution, in order — for asserting an ORDER BY result sequence.</summary>
    /// <param name="solutions">The solutions, in result order.</param>
    /// <returns>The bound <c>?s</c> IRIs, in order.</returns>
    private static string[] Subjects(IReadOnlyList<SparqlSolution> solutions)
    {
        string[] subjects = new string[solutions.Count];
        for(int i = 0; i < solutions.Count; i++)
        {
            subjects[i] = ValueIri(solutions[i], "s");
        }

        return subjects;
    }

    /// <summary>Returns the lexical value of the literal the named variable is bound to, asserting it is bound to a literal.</summary>
    /// <param name="solution">The solution to read.</param>
    /// <param name="variableName">The variable name.</param>
    /// <returns>The bound literal's lexical value.</returns>
    private static string LiteralValue(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");

        return Cast<Literal>(value).Value.ToString();
    }

    /// <summary>Returns the IRI string the named variable is bound to, asserting it is bound to a named node.</summary>
    /// <param name="solution">The solution to read.</param>
    /// <param name="variableName">The variable name.</param>
    /// <returns>The bound IRI as a string.</returns>
    private static string ValueIri(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");

        return Cast<NamedNode>(value).Iri.ToString();
    }

    /// <summary>Returns the bound value of a variable in a solution, asserting it is bound.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name (without the marker).</param>
    /// <returns>The bound term.</returns>
    private static RdfTerm Value(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");

        return value;
    }

    /// <summary>Asserts a value is of the expected type and returns it cast to that type.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    private static T Cast<T>(object value)
    {
        Assert.IsInstanceOfType<T>(value);

        return (T)value;
    }
}
