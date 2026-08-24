using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Analysis;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Serialization;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The extension-aggregate seam below the engine: the parser accepts the argument list's leading
/// <c>DISTINCT</c> for IRI calls only (the grammar reserves it for custom aggregate calls) and keeps
/// rejecting it in every other list; the translator lifts a declared IRI's call into aggregation across
/// the projection, <c>HAVING</c>, <c>ORDER BY</c>, and sub-<c>SELECT</c> positions under the frozen
/// declared-IRI profile, and under the empty profile translates exactly as before; and the scope
/// analyzer accepts a recognized aggregate call's argument variables as aggregated while still flagging
/// naked variables and nested aggregates in every built-in/extension combination.
/// </summary>
[TestClass]
internal sealed class ExtensionAggregateTests
{
    /// <summary>The declared aggregate-function IRI the rows recognize.</summary>
    private const string AggIri = "http://example.org/fn/agg";

    /// <summary>The declared-IRI profile carrying exactly <see cref="AggIri"/>.</summary>
    private static IReadOnlySet<Utf8String> Profile { get; } = new HashSet<Utf8String> { Utf8Strings.From(AggIri) };

    /// <summary>The empty declared-IRI profile — the pure-SPARQL posture.</summary>
    private static IReadOnlySet<Utf8String> EmptyProfile { get; } = new HashSet<Utf8String>();

    /// <summary>A leading <c>DISTINCT</c> in an IRI call's argument list parses cleanly and the call carries the flag.</summary>
    [TestMethod]
    public void DistinctInAnIriCallParsesWithTheFlag()
    {
        ParseResult<SparqlRequest> result = ParseText($"SELECT (<{AggIri}>(DISTINCT ?x) AS ?y) WHERE {{ ?s ?p ?x }}");

        Assert.IsFalse(result.HasErrors);
        FunctionCallExpression call = (FunctionCallExpression)((SelectExpressionAs)((SelectQuery)((SparqlQuery)result.Tree!).Form).Projections[0]).Expression;
        Assert.IsTrue(call.IsDistinct);
        Assert.HasCount(1, call.Arguments);
    }

    /// <summary>The built-in, <c>IF</c>, <c>COALESCE</c>, and membership-test argument lists keep rejecting a leading <c>DISTINCT</c> — their productions never allowed it.</summary>
    /// <param name="query">The query whose argument list carries the illegal keyword.</param>
    [TestMethod]
    [DataRow("SELECT * WHERE { ?s ?p ?x FILTER(STRLEN(DISTINCT ?x) > 0) }")]
    [DataRow("SELECT * WHERE { ?s ?p ?x BIND(IF(DISTINCT ?x, 1, 2) AS ?y) }")]
    [DataRow("SELECT * WHERE { ?s ?p ?x BIND(COALESCE(DISTINCT ?x, 1) AS ?y) }")]
    [DataRow("SELECT * WHERE { ?s ?p ?x FILTER(?x IN (DISTINCT ?x)) }")]
    public void DistinctOutsideAnIriCallStaysAParseError(string query)
    {
        Assert.IsTrue(ParseText(query).HasErrors, "DISTINCT is legal only in an IRI call's argument list.");
    }

    /// <summary><c>DISTINCT</c> over no argument violates the argument-list production: the parse diagnoses it and still builds the faithful flagged zero-argument call.</summary>
    [TestMethod]
    public void DistinctOverNoArgumentIsDiagnosedAndTheFlaggedNodeSurvives()
    {
        ParseResult<SparqlRequest> result = ParseText($"SELECT (<{AggIri}>(DISTINCT) AS ?y) WHERE {{ ?s ?p ?x }}");

        Assert.IsNotEmpty(result.Diagnostics);
        FunctionCallExpression call = (FunctionCallExpression)((SelectExpressionAs)((SelectQuery)((SparqlQuery)result.Tree!).Form).Projections[0]).Expression;
        Assert.IsTrue(call.IsDistinct);
        Assert.IsEmpty(call.Arguments);
    }

    /// <summary>The federated SERVICE writer renders a call's leading <c>DISTINCT</c> back into the argument list.</summary>
    [TestMethod]
    public void ServiceWriterRendersTheDistinctCall()
    {
        ParseResult<SparqlRequest> result = ParseText($"SELECT * WHERE {{ ?s ?p ?x FILTER(<{AggIri}>(DISTINCT ?x)) }}");
        Assert.IsFalse(result.HasErrors);

        string rendered = SparqlQueryTextWriter.ToSelectQuery(((SparqlQuery)result.Tree!).Where.Pattern);

        Assert.Contains("(DISTINCT ", rendered, StringComparison.Ordinal);
    }

    /// <summary>A declared IRI's call lifts under an explicit <c>GROUP BY</c>: the aggregate join binds the promoted node and the projection rewrites to its result variable.</summary>
    [TestMethod]
    public void RecognizedCallLiftsUnderExplicitGroupBy()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?s (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }} GROUP BY ?s", pool, Profile);

        AggregateJoin aggregateJoin = Single<AggregateJoin>(algebra);
        AggregateBinding binding = Assert.ContainsSingle(aggregateJoin.Aggregations);
        ExtensionAggregateExpression aggregate = (ExtensionAggregateExpression)binding.Aggregate;
        Assert.IsTrue(aggregate.FunctionIri.Value.Span.SequenceEqual(Encoding.UTF8.GetBytes(AggIri)));
        Assert.HasCount(1, aggregate.Arguments);
        Assert.IsFalse(aggregate.IsDistinct);
    }

    /// <summary>A declared IRI's call alone triggers implicit grouping: the group has no keys and the join carries the one binding.</summary>
    [TestMethod]
    public void RecognizedCallTriggersImplicitGrouping()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }}", pool, Profile);

        AggregateJoin aggregateJoin = Single<AggregateJoin>(algebra);
        Assert.IsEmpty(((Group)aggregateJoin.Input).Keys);
        Assert.ContainsSingle(aggregateJoin.Aggregations);
    }

    /// <summary>A declared IRI's call in <c>HAVING</c> lifts and the filter runs over the aggregate join.</summary>
    [TestMethod]
    public void HavingSiteLifts()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?s WHERE {{ ?s ?p ?x }} GROUP BY ?s HAVING(<{AggIri}>(?x) = 1)", pool, Profile);

        AggregateJoin aggregateJoin = Single<AggregateJoin>(algebra);
        AggregateBinding binding = Assert.ContainsSingle(aggregateJoin.Aggregations);
        Assert.IsInstanceOfType<ExtensionAggregateExpression>(binding.Aggregate);
        Assert.ContainsSingle(AlgebraWalker.Traverse(algebra).OfType<Filter>().ToList(), "HAVING becomes one filter over the aggregate join.");
    }

    /// <summary>A declared IRI's call in <c>ORDER BY</c> lifts and the order key reads its result variable.</summary>
    [TestMethod]
    public void OrderBySiteLifts()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?s WHERE {{ ?s ?p ?x }} GROUP BY ?s ORDER BY <{AggIri}>(?x)", pool, Profile);

        Assert.ContainsSingle(Single<AggregateJoin>(algebra).Aggregations);
        OrderBy orderBy = Single<OrderBy>(algebra);
        OrderCondition condition = Assert.ContainsSingle(orderBy.Conditions);
        Assert.IsInstanceOfType<VariableExpression>(((OrderAscending)condition).Expression, "The order key rewrote to the aggregate's result variable.");
    }

    /// <summary>Textually identical calls in the projection and <c>HAVING</c> share one binding and one result variable.</summary>
    [TestMethod]
    public void DuplicateCallsShareOneBinding()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?s (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }} GROUP BY ?s HAVING(<{AggIri}>(?x) = 1)", pool, Profile);

        Assert.ContainsSingle(Single<AggregateJoin>(algebra).Aggregations);
    }

    /// <summary>Same-IRI calls over different arguments get two separate bindings — the argument trees discriminate the structural key.</summary>
    [TestMethod]
    public void DifferentArgumentCallsGetTwoBindings()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT (<{AggIri}>(?a) AS ?u) (<{AggIri}>(?b) AS ?v) WHERE {{ ?s ?a ?b }}", pool, Profile);

        Assert.HasCount(2, Single<AggregateJoin>(algebra).Aggregations);
    }

    /// <summary>An aggregate call appearing only inside a sub-<c>SELECT</c>'s projection lifts identically — the profile threads through the translator's own recursion.</summary>
    [TestMethod]
    public void SubSelectOnlyAggregateLifts()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?y WHERE {{ {{ SELECT (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }} }} }}", pool, Profile);

        AggregateJoin aggregateJoin = Single<AggregateJoin>(algebra);
        Assert.IsInstanceOfType<ExtensionAggregateExpression>(Assert.ContainsSingle(aggregateJoin.Aggregations).Aggregate);
    }

    /// <summary>An undeclared IRI's call stays a scalar function call — even under <c>GROUP BY</c> — so shipped behavior is preserved exactly.</summary>
    [TestMethod]
    public void UndeclaredIriStaysScalar()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate($"SELECT ?s (<{AggIri}>(?s) AS ?y) WHERE {{ ?s ?p ?x }} GROUP BY ?s", pool, EmptyProfile);

        Assert.IsEmpty(Single<AggregateJoin>(algebra).Aggregations, "An undeclared IRI lifts nothing; the grouped query carries no aggregate binding.");
    }

    /// <summary>Under the empty profile the profiled translation and the pure-SPARQL overload produce the same ungrouped shape.</summary>
    [TestMethod]
    public void EmptyProfileMatchesThePureOverload()
    {
        const string Query = "SELECT (<http://example.org/fn/other>(?x) AS ?y) WHERE { ?s ?p ?x }";

        using Utf8StringPool profiledPool = new();
        AlgebraOperator profiled = Translate(Query, profiledPool, EmptyProfile);
        using Utf8StringPool purePool = new();
        AlgebraOperator pure = Translate(Query, purePool, aggregateFunctionIris: null);

        Assert.IsEmpty(AlgebraWalker.Traverse(profiled).OfType<AggregateJoin>().ToList());
        Assert.IsEmpty(AlgebraWalker.Traverse(pure).OfType<AggregateJoin>().ToList());
    }

    /// <summary>The flagship shapes pass scope analysis under the profile: a recognized aggregate call's argument variables are aggregated, not naked.</summary>
    /// <param name="query">The valid grouped query.</param>
    [TestMethod]
    [DataRow($"SELECT ?s (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }} GROUP BY ?s")]
    [DataRow($"SELECT (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }}")]
    public void RecognizedAggregatePassesScopeAnalysisUnderTheProfile(string query)
    {
        Assert.IsFalse(AnalyzeText(query, Profile).HasErrors);
    }

    /// <summary>Under the empty profile the same call is an ordinary scalar over a non-key variable, and the grouped-scope check flags it — today's answer, reproduced.</summary>
    [TestMethod]
    public void NakedVariableStillFlagsUnderTheEmptyProfile()
    {
        Assert.IsTrue(AnalyzeText($"SELECT ?s (<{AggIri}>(?x) AS ?y) WHERE {{ ?s ?p ?x }} GROUP BY ?s", EmptyProfile).HasErrors);
    }

    /// <summary>Nested aggregation is flagged in every built-in/extension combination.</summary>
    /// <param name="query">The query nesting one aggregate inside another.</param>
    [TestMethod]
    [DataRow($"SELECT (SUM(<{AggIri}>(?x)) AS ?y) WHERE {{ ?s ?p ?x }}")]
    [DataRow($"SELECT (<{AggIri}>(SUM(?x)) AS ?y) WHERE {{ ?s ?p ?x }}")]
    [DataRow($"SELECT (<{AggIri}>(<{AggIri}>(?x)) AS ?y) WHERE {{ ?s ?p ?x }}")]
    public void NestedAggregatesFlagInEveryCombination(string query)
    {
        Assert.IsTrue(AnalyzeText(query, Profile).HasErrors, "An aggregate may not nest inside another aggregate.");
    }

    /// <summary>Parses a query text to a result carrying the diagnostics.</summary>
    /// <param name="text">The query text.</param>
    /// <returns>The parse result.</returns>
    private static ParseResult<SparqlRequest> ParseText(string text)
    {
        return SparqlParser.ParseRequest(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Parses, normalizes, and translates a query under a declared-IRI profile (the pure overload when <paramref name="aggregateFunctionIris"/> is <see langword="null"/>).</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The caller-held parse pool, which must outlive the returned algebra's reads.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs, or <see langword="null"/> for the pure overload.</param>
    /// <returns>The algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool, IReadOnlySet<Utf8String>? aggregateFunctionIris)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return aggregateFunctionIris is null ? SparqlTranslator.Translate(query) : SparqlTranslator.Translate(query, aggregateFunctionIris);
    }

    /// <summary>Parses a query and runs the scope analyzer under a declared-IRI profile, returning the combined diagnostics.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="aggregateFunctionIris">The declared aggregate-function IRIs.</param>
    /// <returns>The diagnostic bag.</returns>
    private static DiagnosticBag AnalyzeText(string text, IReadOnlySet<Utf8String> aggregateFunctionIris)
    {
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        ParseResult<SparqlRequest> result = parser.ParseToResult();
        Assert.IsFalse(result.HasErrors, "The analyzer rows require a clean parse.");

        SparqlScopeAnalyzer.Analyze(result.Tree!, diagnostics, aggregateFunctionIris);

        return diagnostics;
    }

    /// <summary>Returns the single operator of a type in an algebra tree, asserting exactly one exists.</summary>
    /// <typeparam name="TOperator">The operator type.</typeparam>
    /// <param name="algebra">The algebra root.</param>
    /// <returns>The single instance.</returns>
    private static TOperator Single<TOperator>(AlgebraOperator algebra)
        where TOperator : AlgebraOperator
    {
        return Assert.ContainsSingle(AlgebraWalker.Traverse(algebra).OfType<TOperator>().ToList());
    }
}
