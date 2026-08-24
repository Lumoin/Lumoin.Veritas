using System;
using System.Collections.Generic;
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
/// The extension-function seam's evaluation rows (SPARQL §17.6): an unregistered IRI-named function call
/// evaluates to the expression ERROR VALUE — a <c>FILTER</c> drops the row, a <c>BIND</c> and a SELECT
/// expression leave the variable unbound, a <c>COALESCE</c> falls to its alternative — never an engine
/// fault; a registered function is invoked with its evaluated arguments, the invoked IRI, and the
/// evaluation context, owns its arity, and is never consulted when an argument carries an error; and the
/// built-in XSD constructor casts (§17.5) keep answering ahead of the registry.
/// </summary>
[TestClass]
internal sealed class ExtensionFunctionEvaluationTests
{
    /// <summary>The example-namespace prefix the test queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>An extension-function IRI no registry in this class ever registers.</summary>
    private const string UnknownFunction = Ex + "fn/unknown";

    /// <summary>The registered predicate function's IRI: answers <c>true</c> exactly for the lexical form <c>bob</c>.</summary>
    private const string IsBobFunction = Ex + "fn/isBob";

    /// <summary>The registered projection function's IRI: answers its first argument.</summary>
    private const string FirstFunction = Ex + "fn/first";

    /// <summary>The second IRI the shared echo implementation is registered under.</summary>
    private const string EchoAFunction = Ex + "fn/echoA";

    /// <summary>The other IRI the shared echo implementation is registered under.</summary>
    private const string EchoBFunction = Ex + "fn/echoB";

    /// <summary>The registered single-argument function's IRI whose implementation answers a fixed marker; a different argument count is its own error decision.</summary>
    private const string UnaryFunction = Ex + "fn/unary";

    /// <summary>The registered function IRI whose implementation answers the context's fixed query timestamp.</summary>
    private const string NowFunction = Ex + "fn/now";

    /// <summary>The XSD string datatype IRI.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The XSD boolean datatype IRI.</summary>
    private const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>An unknown extension function in a <c>FILTER</c> is an expression error, and an error condition drops the row — the query answers empty instead of faulting the evaluation.</summary>
    [TestMethod]
    public async Task UnknownFunctionInFilterDropsTheRowInsteadOfFaulting()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(SparqlFunctionRegistry.Empty).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o FILTER(<{UnknownFunction}>(?o)) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions, "An unknown extension function errs the FILTER condition, which drops every row; it must not throw.");
    }

    /// <summary>An unknown extension function in a <c>BIND</c> leaves the bound variable unbound while the solution itself survives.</summary>
    [TestMethod]
    public async Task UnknownFunctionInBindLeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(SparqlFunctionRegistry.Empty).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{UnknownFunction}>(?o) AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "The BIND error affects only ?r; both data rows survive.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsFalse(solution.TryGetValue(Variable("r"), out _), "The errored BIND leaves ?r unbound.");
        }
    }

    /// <summary>An unknown extension function as a SELECT expression leaves the projected variable unbound while the row survives.</summary>
    [TestMethod]
    public async Task UnknownFunctionInSelectExpressionLeavesTheVariableUnbound()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(SparqlFunctionRegistry.Empty).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT (<{UnknownFunction}>(?o) AS ?r) WHERE {{ ?s <{Ex}name> ?o }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions, "The projection error affects only ?r; both rows project.");
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsFalse(solution.TryGetValue(Variable("r"), out _), "The errored SELECT expression leaves ?r unbound.");
        }
    }

    /// <summary>An unknown extension function inside <c>COALESCE</c> errs that alternative only, so the coalesce falls to the next one.</summary>
    [TestMethod]
    public async Task UnknownFunctionInsideCoalesceFallsToTheAlternative()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(SparqlFunctionRegistry.Empty).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(COALESCE(<{UnknownFunction}>(?o), \"fallback\") AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.AreEqual("fallback", Lexical(solution, "r"), "COALESCE takes the first non-error alternative, so the unknown function's error selects the fallback.");
        }
    }

    /// <summary>The XSD constructor casts (§17.5) keep answering ahead of a populated registry: <c>xsd:integer</c> over a numeric string still casts.</summary>
    [TestMethod]
    public async Task XsdConstructorCastsStillEvaluateUnderAPopulatedRegistry()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<http://www.w3.org/2001/XMLSchema#integer>(\"42\") AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.AreEqual("42", Lexical(solution, "r"), "The built-in cast answers before the registry is consulted.");
        }
    }

    /// <summary>A registered function's result binds through <c>BIND</c> with its term intact.</summary>
    [TestMethod]
    public async Task RegisteredFunctionBindsItsResult()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{FirstFunction}>(?o, \"other\") AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        List<string> bound = [];
        foreach(SparqlSolution solution in solutions)
        {
            bound.Add(Lexical(solution, "r"));
        }

        Assert.Contains("alice", bound, "The function answered its first argument — the row's ?o value.");
        Assert.Contains("bob", bound);
    }

    /// <summary>A registered predicate function decides a <c>FILTER</c>: only the rows it answers true for survive.</summary>
    [TestMethod]
    public async Task RegisteredFunctionDecidesTheFilter()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o FILTER(<{IsBobFunction}>(?o)) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.ContainsSingle(solutions, "Exactly the bob row satisfies the registered predicate.");
        Assert.AreEqual("bob", Lexical(solutions[0], "o"));
    }

    /// <summary>The evaluator hands the arguments over in call order: the first-argument projection distinguishes the two positions.</summary>
    [TestMethod]
    public async Task RegisteredFunctionReceivesArgumentsInCallOrder()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{FirstFunction}>(\"left\", \"right\") AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.AreEqual("left", Lexical(solution, "r"), "The argument span is in call order, so the projection answers the first constant.");
        }
    }

    /// <summary>One implementation registered under two IRIs receives the invoked IRI: each call answers its own name.</summary>
    [TestMethod]
    public async Task RegisteredFunctionReceivesTheInvokedIri()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{EchoAFunction}>() AS ?a) BIND(<{EchoBFunction}>() AS ?b) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.AreEqual(EchoAFunction, Lexical(solution, "a"), "The shared implementation sees the invoked IRI, so each registration answers its own name.");
            Assert.AreEqual(EchoBFunction, Lexical(solution, "b"));
        }
    }

    /// <summary>An error argument errs the invocation before the function runs: the recorder counts zero invocations and the variable stays unbound.</summary>
    [TestMethod]
    public async Task ErrorArgumentErrsTheInvocationWithoutRunningTheFunction()
    {
        InvocationRecorder recorder = new();
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(UnaryFunction), recorder.Record).Kind);
        SparqlQueryEngine engine = await BuildNameEngineAsync(builder.Build()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        //?missing is never bound, so the argument evaluates to an error and the invocation errs unconsulted.
        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{UnaryFunction}>(?missing) AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsFalse(solution.TryGetValue(Variable("r"), out _), "The error argument errs the call, so ?r stays unbound.");
        }

        Assert.AreEqual(0, recorder.Count, "The function body never runs when an argument carries an error.");
    }

    /// <summary>The function owns its arity: called with a count its implementation rejects, the invocation answers the function's error value and the variable stays unbound.</summary>
    [TestMethod]
    public async Task WrongArityAnswersTheFunctionsErrorValue()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{UnaryFunction}>(\"a\", \"b\") AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsFalse(solution.TryGetValue(Variable("r"), out _), "The implementation rejects the two-argument call, and its error value leaves ?r unbound.");
        }
    }

    /// <summary>The invocation receives the evaluation context: a function reading the fixed query timestamp binds a value, proving the context parameter carries through.</summary>
    [TestMethod]
    public async Task RegisteredFunctionReceivesTheEvaluationContext()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(Registry()).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o BIND(<{NowFunction}>() AS ?r) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(2, solutions);
        foreach(SparqlSolution solution in solutions)
        {
            Assert.IsNotEmpty(Lexical(solution, "r"), "The function read the context's fixed query timestamp, so the context parameter reaches the invocation.");
        }
    }

    /// <summary>Under the empty default an IRI that a populated run would answer stays an error: the registry, not the IRI shape, decides evaluability.</summary>
    [TestMethod]
    public async Task RegisteredIriUnderTheEmptyDefaultStaysAnError()
    {
        SparqlQueryEngine engine = await BuildNameEngineAsync(SparqlFunctionRegistry.Empty).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        AlgebraOperator algebra = Translate($"SELECT * WHERE {{ ?s <{Ex}name> ?o FILTER(<{IsBobFunction}>(?o)) }}", pool);
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsEmpty(solutions, "The same IRI that decides the filter under a populated registry errs — and drops every row — under the empty default.");
    }

    /// <summary>Counts invocations through an instance method group, so the no-invocation contract is measured without any closure state.</summary>
    private sealed class InvocationRecorder
    {
        /// <summary>The number of times the recorded function body ran.</summary>
        public int Count { get; private set; }

        /// <summary>The recorded function: counts the invocation and answers its first argument.</summary>
        /// <param name="functionIri">The invoked IRI, unused.</param>
        /// <param name="arguments">The evaluated arguments.</param>
        /// <param name="context">The evaluation context, unused.</param>
        /// <returns>The first argument, or the error value for a different argument count.</returns>
        public SparqlFunctionResult Record(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
        {
            Count++;

            return arguments.Length == 1 ? SparqlFunctionResult.Of(arguments[0]) : SparqlFunctionResult.Error;
        }
    }

    /// <summary>Builds the class's populated registry: the bob predicate, the first-argument projection, the shared echo under two IRIs, the arity-strict unary marker, and the context-reading timestamp function.</summary>
    /// <returns>The registry.</returns>
    private static SparqlFunctionRegistry Registry()
    {
        SparqlFunctionRegistryBuilder builder = new();
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(IsBobFunction), IsBob).Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(FirstFunction), First).Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(EchoAFunction), EchoIri).Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(EchoBFunction), EchoIri).Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(UnaryFunction), UnaryMarker).Kind);
        Assert.AreEqual(SparqlFunctionRegistrationKind.Accepted, builder.Add(Utf8Strings.From(NowFunction), ContextNow).Kind);

        return builder.Build();
    }

    /// <summary>Answers the boolean of whether the single argument is a literal with the lexical form <c>bob</c>; a different argument count is an error.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The boolean literal, or the error value.</returns>
    private static SparqlFunctionResult IsBob(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || arguments[0] is not Literal literal)
        {
            return SparqlFunctionResult.Error;
        }

        return SparqlFunctionResult.Of(BooleanLiteral(literal.Value.Span.SequenceEqual("bob"u8)));
    }

    /// <summary>Answers the first argument unchanged; an empty argument list is an error.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The first argument, or the error value.</returns>
    private static SparqlFunctionResult First(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length > 0 ? SparqlFunctionResult.Of(arguments[0]) : SparqlFunctionResult.Error;
    }

    /// <summary>Answers the invoked IRI's text as a string literal — the shared implementation the two echo registrations bind.</summary>
    /// <param name="functionIri">The invoked IRI.</param>
    /// <param name="arguments">The evaluated arguments, unused.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The IRI-text literal.</returns>
    private static SparqlFunctionResult EchoIri(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Of(StringLiteral(functionIri));
    }

    /// <summary>Answers a fixed marker for exactly one argument; any other count is the function's own error decision.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The marker literal, or the error value.</returns>
    private static SparqlFunctionResult UnaryMarker(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 ? SparqlFunctionResult.Of(StringLiteral(Utf8Strings.From("unary"))) : SparqlFunctionResult.Error;
    }

    /// <summary>Answers the context's fixed query timestamp as a string literal, proving the context parameter reaches the invocation.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments, unused.</param>
    /// <param name="context">The evaluation context.</param>
    /// <returns>The timestamp literal.</returns>
    private static SparqlFunctionResult ContextNow(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return SparqlFunctionResult.Of(StringLiteral(Utf8Strings.From(context.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>Builds an <c>xsd:boolean</c> literal.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>The literal.</returns>
    private static Literal BooleanLiteral(bool value)
    {
        return new Literal(Utf8Strings.From(value ? "true" : "false"), new NamedNode(Utf8Strings.From(XsdBoolean)));
    }

    /// <summary>Builds an <c>xsd:string</c> literal.</summary>
    /// <param name="text">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StringLiteral(Utf8String text)
    {
        return new Literal(text, new NamedNode(Utf8Strings.From(XsdString)));
    }

    /// <summary>Builds the two-row name graph — <c>alice</c> and <c>bob</c> readings — over an engine whose expression context carries the given registry.</summary>
    /// <param name="functions">The extension-function registry the engine's expression context carries.</param>
    /// <returns>The engine.</returns>
    private async Task<SparqlQueryEngine> BuildNameEngineAsync(SparqlFunctionRegistry functions)
    {
        List<DataTriple> data =
        [
            new DataTriple(Iri(Ex + "s1"), Iri(Ex + "name"), StringLiteral(Utf8Strings.From("alice"))),
            new DataTriple(Iri(Ex + "s2"), Iri(Ex + "name"), StringLiteral(Utf8Strings.From("bob"))),
        ];

        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault(extensionFunctions: functions);

        return await SparqlQueryEngine.BuildAsync(data, expressionContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI text.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Parses, normalizes, and translates a query to algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The parse pool.</param>
    /// <returns>The algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
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
