using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The extension-aggregate seam through the engine: a registered aggregate folds each group's evaluated
/// values under the engine's own discipline — unbound members drop as <c>COUNT</c> drops them, an
/// argument error over bound data fails the whole aggregate, <c>DISTINCT</c> deduplicates by RDF term
/// identity so two lexically distinct forms of one value stay two members, the empty implicit group
/// passes through to the fold, a wrong arity answers the error value, a <c>DISTINCT</c>-marked call
/// that stays scalar errs on the cast path and the registry path alike, and the bare-<c>COUNT(*)</c>
/// fast path declines a join that also carries an extension aggregate.
/// </summary>
[TestClass]
internal sealed class ExtensionAggregateSparqlTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The registered aggregate answering the lexicographically smallest lexical form, or the <c>EMPTY</c> marker over the empty group.</summary>
    private const string MinAggregate = Ex + "fn/aggMin";

    /// <summary>The registered aggregate answering the group's value count as <c>xsd:integer</c>.</summary>
    private const string CountAggregate = Ex + "fn/aggCount";

    /// <summary>The registered scalar-only function, for the DISTINCT-marked-scalar rows.</summary>
    private const string ScalarOnly = Ex + "fn/scalarOnly";

    /// <summary>The XSD string datatype IRI.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The XSD integer datatype IRI.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The registry carrying the two aggregate faces and the scalar-only function.</summary>
    private static SparqlFunctionRegistry Functions { get; } = BuildFunctions();

    /// <summary>A registered aggregate folds each explicit group's values.</summary>
    [TestMethod]
    public async Task AggregateFoldsPerGroup()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT ?s (<{MinAggregate}>(?o) AS ?r) WHERE {{ ?s <{Ex}name> ?o }} GROUP BY ?s", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "One solution per subject group.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.AreEqual("alice", Lexical(solution, "r"), "Both groups' lexicographic minimum is alice.");
        }
    }

    /// <summary>A registered aggregate alone triggers the implicit single group and folds every value.</summary>
    [TestMethod]
    public async Task ImplicitGroupFoldsAllValues()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{MinAggregate}>(?o) AS ?r) WHERE {{ ?s <{Ex}name> ?o }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.AreEqual("alice", Lexical(solutions[0], "r"));
    }

    /// <summary><c>DISTINCT</c> deduplicates the fold's inputs by term identity: four name readings carry three distinct terms.</summary>
    [TestMethod]
    public async Task DistinctDeduplicatesTheFoldInputs()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator plain = Translate($"SELECT (<{CountAggregate}>(?o) AS ?r) WHERE {{ ?s <{Ex}name> ?o }}", pool);
        AlgebraOperator distinct = Translate($"SELECT (<{CountAggregate}>(DISTINCT ?o) AS ?r) WHERE {{ ?s <{Ex}name> ?o }}", pool);

        Assert.AreEqual("4", Lexical((await engine.EvaluateAsync(plain, TestContext.CancellationToken).ConfigureAwait(false))[0], "r"));
        Assert.AreEqual("3", Lexical((await engine.EvaluateAsync(distinct, TestContext.CancellationToken).ConfigureAwait(false))[0], "r"));
    }

    /// <summary>Term-identity <c>DISTINCT</c> keeps two lexically distinct forms of one value as two members — the pinned seam contract.</summary>
    [TestMethod]
    public async Task LexicallyDistinctFormsOfOneValueStayTwoMembers()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{CountAggregate}>(DISTINCT ?n) AS ?r) WHERE {{ ?s <{Ex}num> ?n }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("2", Lexical(solutions[0], "r"), "\"1\" and \"01\" are one integer value but two RDF terms; term-identity DISTINCT keeps both.");
    }

    /// <summary>The empty implicit group passes through to the fold, which owns its empty-group answer.</summary>
    [TestMethod]
    public async Task EmptyGroupPassesThroughToTheFold()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{MinAggregate}>(?o) AS ?r) WHERE {{ ?s <{Ex}none> ?o }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "The implicit group exists even over no rows.");
        Assert.AreEqual("EMPTY", Lexical(solutions[0], "r"), "The fold saw the empty group and answered its own marker.");
    }

    /// <summary>A member whose argument variable is unbound drops from the fold — the discipline <c>COUNT</c> shares over <c>OPTIONAL</c>-shaped data.</summary>
    [TestMethod]
    public async Task UnboundMemberDropsFromTheFold()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{CountAggregate}>(?o) AS ?r) WHERE {{ ?s <{Ex}name> ?n OPTIONAL {{ ?s <{Ex}size> ?o }} }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("2", Lexical(solutions[0], "r"), "Only the two rows binding ?o contribute; the unbound rows drop.");
    }

    /// <summary>An argument error over a bound member fails the whole aggregate — an answer over silently fewer members would describe a different group.</summary>
    [TestMethod]
    public async Task BoundMemberErrorFailsTheWholeAggregate()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{CountAggregate}>(<{XsdInteger}>(?o)) AS ?r) WHERE {{ ?s <{Ex}name> ?o }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _), "The cast errs over bound names, so the whole aggregate answers the error value.");
    }

    /// <summary>A recognized aggregate call folds exactly one expression: zero or two arguments answer the error value.</summary>
    /// <param name="arguments">The call's argument-list text.</param>
    [TestMethod]
    [DataRow("()")]
    [DataRow("(?o, ?o)")]
    public async Task WrongArityAnswersTheErrorValue(string arguments)
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{MinAggregate}>{arguments} AS ?r) WHERE {{ ?s <{Ex}name> ?o }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("r"), out _));
    }

    /// <summary>A <c>DISTINCT</c>-marked call that stays scalar answers the error value on the registry path and the XSD-cast path alike.</summary>
    /// <param name="call">The DISTINCT-marked scalar call text.</param>
    [TestMethod]
    [DataRow($"<{ScalarOnly}>(DISTINCT ?o)")]
    [DataRow($"<{XsdInteger}>(DISTINCT \"42\")")]
    public async Task DistinctMarkedScalarCallErrs(string call)
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND({call} AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(4, solutions, "The BIND error affects only ?r; every data row survives.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsFalse(solution.TryGetValue(Variable("r"), out _), "The grammar reserves the argument-list DISTINCT for aggregate calls; honoring it as a scalar would silently mean something else.");
        }
    }

    /// <summary>The bare-<c>COUNT(*)</c> fast path declines a join that also carries an extension aggregate, and the general path answers both.</summary>
    [TestMethod]
    public async Task CountOnlyFastPathBailsOnExtensionAggregates()
    {
        SparqlQueryEngine engine = await BuildEngineAsync().ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (COUNT(*) AS ?n) (<{CountAggregate}>(?o) AS ?m) WHERE {{ ?s <{Ex}name> ?o }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions);
        Assert.AreEqual("4", Lexical(solutions[0], "n"));
        Assert.AreEqual("4", Lexical(solutions[0], "m"));
    }

    /// <summary>Builds the registry: the two aggregate faces and the scalar-only function, every registration accepted.</summary>
    /// <returns>The frozen registry.</returns>
    private static SparqlFunctionRegistry BuildFunctions()
    {
        SparqlFunctionRegistryBuilder builder = new();
        builder.Add(new SparqlFunctionEntry(Utf8Strings.From(MinAggregate), Scalar: null, Aggregate: MinLexicalFold));
        builder.Add(new SparqlFunctionEntry(Utf8Strings.From(CountAggregate), Scalar: null, Aggregate: CountFold));
        builder.Add(Utf8Strings.From(ScalarOnly), ScalarMarker);

        foreach(SparqlFunctionRegistration outcome in builder.Outcomes)
        {
            Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, outcome.Kind);
        }

        return builder.Build();
    }

    /// <summary>Folds the group to its lexicographically smallest lexical form, or the <c>EMPTY</c> marker over the empty group.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The minimum-lexical string literal, or the marker.</returns>
    private static SparqlFunctionResult MinLexicalFold(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        if(group.Values.Length == 0)
        {
            return SparqlFunctionResult.Of(StringLiteral("EMPTY"));
        }

        string? minimum = null;
        foreach(RdfTerm value in group.Values)
        {
            if(value is not Literal literal)
            {
                return SparqlFunctionResult.Error;
            }

            string lexical = literal.Value.ToString();
            if(minimum is null || string.CompareOrdinal(lexical, minimum) < 0)
            {
                minimum = lexical;
            }
        }

        return SparqlFunctionResult.Of(StringLiteral(minimum!));
    }

    /// <summary>Folds the group to its value count as <c>xsd:integer</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The count literal.</returns>
    private static SparqlFunctionResult CountFold(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Of(new Literal(Utf8Strings.From(group.Values.Length.ToString(CultureInfo.InvariantCulture)), new NamedNode(Utf8Strings.From(XsdInteger))));
    }

    /// <summary>The scalar-only marker implementation; the rows exercising it assert the error value, never an answer.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments, unused.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>A fixed marker literal.</returns>
    private static SparqlFunctionResult ScalarMarker(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Of(StringLiteral("scalar"));
    }

    /// <summary>Builds an <c>xsd:string</c> literal.</summary>
    /// <param name="text">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StringLiteral(string text)
    {
        return new Literal(Utf8Strings.From(text), new NamedNode(Utf8Strings.From(XsdString)));
    }

    /// <summary>Builds the shared data graph over an engine whose expression context carries the registry.</summary>
    /// <returns>The engine.</returns>
    private async Task<SparqlQueryEngine> BuildEngineAsync()
    {
        List<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "name"), StringLiteral("alice")),
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "name"), StringLiteral("amy")),
            new DataTriple(Iri(Ex + "s2"), Iri(Ex + "name"), StringLiteral("bob")),
            new DataTriple(Iri(Ex + "s2"), Iri(Ex + "name"), StringLiteral("alice")),
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "size"), StringLiteral("5")),
            new DataTriple(Iri(Ex + "n1"), Iri(Ex + "num"), new Literal(Utf8Strings.From("1"), new NamedNode(Utf8Strings.From(XsdInteger)))),
            new DataTriple(Iri(Ex + "n2"), Iri(Ex + "num"), new Literal(Utf8Strings.From("01"), new NamedNode(Utf8Strings.From(XsdInteger)))),
        ];

        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault(extensionFunctions: Functions);

        return await SparqlQueryEngine.BuildAsync(data, expressionContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Parses, normalizes, and translates a query under the registry's declared aggregate profile.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The caller-held parse pool.</param>
    /// <returns>The algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query, Functions.AggregateIris);
    }

    /// <summary>Builds a SPARQL variable from its name (without the leading marker).</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }

    /// <summary>Returns the lexical form of a variable's bound literal, asserting it is bound to a literal.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name (without the marker).</param>
    /// <returns>The lexical form.</returns>
    private static string Lexical(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");
        Assert.IsInstanceOfType<Literal>(value);

        return ((Literal)value).Value.ToString();
    }
}
