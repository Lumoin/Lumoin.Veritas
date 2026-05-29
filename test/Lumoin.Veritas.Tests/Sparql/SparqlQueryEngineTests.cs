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

    /// <summary>An operator outside this slice's supported set (here FILTER) is refused with a descriptive exception.</summary>
    [TestMethod]
    public async Task UnsupportedOperatorIsRefused()
    {
        SparqlQueryEngine engine = await BuildEngineAsync(("alice", "p", "bob")).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o FILTER(?o = ?o) }", pool);

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
