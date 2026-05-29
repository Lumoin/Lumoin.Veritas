using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

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

        CollectionAssert.AreEqual(new[] { Ex + "b", Ex + "c", Ex + "a" }, Subjects(ascendingSolutions));
        CollectionAssert.AreEqual(new[] { Ex + "a", Ex + "c", Ex + "b" }, Subjects(descendingSolutions));
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
        CollectionAssert.AreEqual(new[] { Ex + "a", Ex + "b" }, values);
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

    /// <summary>An operator outside the supported set (here a GRAPH named-graph block) is refused with a descriptive exception.</summary>
    [TestMethod]
    public async Task UnsupportedOperatorIsRefused()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { GRAPH ?g { ?s :p ?o } }", pool);

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

        return await SparqlQueryEngine.BuildAsync(data, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an engine over an explicit set of data triples (used when objects are literals rather than IRIs).</summary>
    /// <param name="triples">The data triples.</param>
    /// <returns>An engine over the graph.</returns>
    private async Task<SparqlQueryEngine> EngineFromAsync(params DataTriple[] triples)
    {
        return await SparqlQueryEngine.BuildAsync(triples, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an <c>xsd:integer</c> literal term.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal term.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8String.FromString(value.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#integer")));
    }

    /// <summary>Builds an <c>xsd:string</c> literal term.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The literal term.</returns>
    private static Literal StringLiteral(string value)
    {
        return new Literal(Utf8String.FromString(value), new NamedNode(Utf8String.FromString("http://www.w3.org/2001/XMLSchema#string")));
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
        return new NamedNode(Utf8String.FromString(Ex + localName));
    }

    /// <summary>Builds a SPARQL variable from its name (without the leading marker).</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8String.FromString(name));
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
