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

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// End-to-end pins for temporal conformance, keyed to the certified
/// ground-truth table: every FILTER cell evaluated through the full
/// parse → translate → execute path via <c>BIND</c> (distinguishing TRUE, FALSE, and type error), the
/// <c>ORDER BY</c>/<c>MIN</c>/<c>MAX</c> cells over <c>VALUES</c> sets in multiple encounter orders, and the
/// host-configurable implicit-timezone seam.
/// </summary>
[TestClass]
internal sealed class TemporalOrderingConformanceTests
{
    /// <summary>The XSD namespace the query literals use.</summary>
    private const string XsdNamespace = "http://www.w3.org/2001/XMLSchema#";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every FILTER cell of the certified table: the expression's value is <c>true</c>, <c>false</c>, or a type error (an unbound <c>BIND</c> result).</summary>
    /// <param name="cell">The ground-truth table cell id.</param>
    /// <param name="expression">The SPARQL comparison expression.</param>
    /// <param name="expected">The expected boolean lexical, or <see langword="null"/> for a type error.</param>
    [TestMethod]
    [DataRow("F1", "\"2020-01-01T00:30:00\"^^xsd:dateTime < \"2020-01-01T01:00:00+00:00\"^^xsd:dateTime", "true")]
    [DataRow("F2", "\"2020-01-01T00:30:00\"^^xsd:dateTime > \"2020-01-01T01:00:00+00:00\"^^xsd:dateTime", "false")]
    [DataRow("F3", "\"2020-01-01T01:00:00+00:00\"^^xsd:dateTime < \"2020-01-01T00:00:00-05:00\"^^xsd:dateTime", "true")]
    [DataRow("F4", "\"2020-01-01T06:00:00\"^^xsd:dateTime < \"2020-01-01T00:00:00-05:00\"^^xsd:dateTime", "false")]
    [DataRow("F5a", "\"2020-01-01T05:00:00\"^^xsd:dateTime = \"2020-01-01T00:00:00-05:00\"^^xsd:dateTime", "false")]
    [DataRow("F5b", "\"2020-01-01T05:00:00\"^^xsd:dateTime < \"2020-01-01T00:00:00-05:00\"^^xsd:dateTime", "false")]
    [DataRow("F5c", "\"2020-01-01T05:00:00\"^^xsd:dateTime <= \"2020-01-01T00:00:00-05:00\"^^xsd:dateTime", "true")]
    [DataRow("F6", "\"2020-03-01\"^^xsd:date < \"2020-03-02\"^^xsd:date", "true")]
    [DataRow("F7", "\"2020-03-01\"^^xsd:date < \"2020-03-01-05:00\"^^xsd:date", "true")]
    [DataRow("F8", "\"13:00:00\"^^xsd:time > \"12:00:00+00:00\"^^xsd:time", "true")]
    [DataRow("F9", "\"2020-01-01T00:00:00\"^^xsd:dateTime < \"2020-01-02\"^^xsd:date", null)]
    [DataRow("F10", "\"2020-01-01T00:00:00\"^^xsd:dateTimeStamp < \"2020-01-01T01:00:00Z\"^^xsd:dateTimeStamp", null)]
    [DataRow("F11", "\"2020-01-01T00:00:00Z\"^^xsd:dateTimeStamp < \"2020-01-01T01:00:00+00:00\"^^xsd:dateTime", "true")]
    [DataRow("F12", "\"2020-01-01\"^^xsd:date = \"2020-01-01T00:00:00\"^^xsd:dateTime", "false")]
    [DataRow("F13eq", "\"2020-01-01T00:00:00.5\"^^xsd:dateTime = \"2020-01-01T00:00:00.50\"^^xsd:dateTime", "false")]
    [DataRow("F13le", "\"2020-01-01T00:00:00.5\"^^xsd:dateTime <= \"2020-01-01T00:00:00.50\"^^xsd:dateTime", "true")]
    [DataRow("F14", "\"2020-13-45T99:99:99\"^^xsd:dateTime < \"2020-01-01T00:00:00Z\"^^xsd:dateTime", null)]
    [DataRow("F15", "\"-0001-12-31T23:00:00\"^^xsd:dateTime < \"0000-01-01T01:00:00Z\"^^xsd:dateTime", "true")]
    public async Task FilterCellsMatchTheCertifiedTable(string cell, string expression, string? expected)
    {
        RdfTerm? result = await BindResultAsync(expression, expressionContext: null).ConfigureAwait(false);

        if(expected is null)
        {
            Assert.IsNull(result, $"Cell {cell}: the comparison must be a type error (an unbound BIND result).");

            return;
        }

        Literal boolLiteral = AssertLiteral(result, cell);
        Assert.AreEqual(expected, boolLiteral.Value.ToString(), $"Cell {cell}: wrong verdict.");
        Assert.AreEqual(XsdNamespace + "boolean", boolLiteral.Datatype.Iri.ToString(), $"Cell {cell}: the verdict must be an xsd:boolean.");
    }

    /// <summary>O1: the transitivity triple sorts to the certified sequence from both encounter orders, and DESC reverses it.</summary>
    [TestMethod]
    public async Task OrderByTransitivityTripleIsEncounterIndependent()
    {
        const string Naive = "\"2020-01-01T00:30:00\"^^xsd:dateTime";
        const string UtcAware = "\"2020-01-01T01:00:00+00:00\"^^xsd:dateTime";
        const string MinusFive = "\"2020-01-01T00:00:00-05:00\"^^xsd:dateTime";
        string[] expected = ["2020-01-01T00:30:00", "2020-01-01T01:00:00+00:00", "2020-01-01T00:00:00-05:00"];

        Assert.AreSequenceEqual(expected, await OrderedLexicalFormsAsync($"{Naive} {UtcAware} {MinusFive}").ConfigureAwait(false));
        Assert.AreSequenceEqual(expected, await OrderedLexicalFormsAsync($"{MinusFive} {Naive} {UtcAware}").ConfigureAwait(false));

        string[] descending = await OrderedLexicalFormsAsync($"{Naive} {UtcAware} {MinusFive}", "ORDER BY DESC(?x)").ConfigureAwait(false);
        Assert.AreSequenceEqual(expected.Reverse().ToArray(), descending);
    }

    /// <summary>O2/R2: an instant-equal pair orders by the deterministic tiebreak (same datatype, lexical bytes) from both encounter orders.</summary>
    [TestMethod]
    public async Task OrderByInstantEqualPairIsDeterministic()
    {
        const string Zulu = "\"2020-01-01T01:00:00Z\"^^xsd:dateTime";
        const string PlusOne = "\"2020-01-01T02:00:00+01:00\"^^xsd:dateTime";
        string[] expected = ["2020-01-01T01:00:00Z", "2020-01-01T02:00:00+01:00"];

        Assert.AreSequenceEqual(expected, await OrderedLexicalFormsAsync($"{Zulu} {PlusOne}").ConfigureAwait(false));
        Assert.AreSequenceEqual(expected, await OrderedLexicalFormsAsync($"{PlusOne} {Zulu}").ConfigureAwait(false));
    }

    /// <summary>O3: fractional seconds order by value on one date.</summary>
    [TestMethod]
    public async Task OrderByFractionalSecondsOrdersByValue()
    {
        string[] ordered = await OrderedLexicalFormsAsync(
            "\"2020-01-01T00:00:00.75\"^^xsd:dateTime \"2020-01-01T00:00:00.5\"^^xsd:dateTime \"2020-01-01T00:00:00\"^^xsd:dateTime").ConfigureAwait(false);

        string[] expected = ["2020-01-01T00:00:00", "2020-01-01T00:00:00.5", "2020-01-01T00:00:00.75"];
        Assert.AreSequenceEqual(expected, ordered);
    }

    /// <summary>O4/R1: the mixed double/integer/duration column sorts by class rank — the numerics as one value-ordered class, then the duration class — from both encounter orders.</summary>
    [TestMethod]
    public async Task OrderByMixedDatatypesFollowsTheClassRankPartition()
    {
        string[] expected = ["5", "100.0", "P1Y"];

        Assert.AreSequenceEqual(
            expected,
            await OrderedLexicalFormsAsync("\"100.0\"^^xsd:double \"5\"^^xsd:integer \"P1Y\"^^xsd:duration").ConfigureAwait(false));
        Assert.AreSequenceEqual(
            expected,
            await OrderedLexicalFormsAsync("\"P1Y\"^^xsd:duration \"5\"^^xsd:integer \"100.0\"^^xsd:double").ConfigureAwait(false));
    }

    /// <summary>O5: the deferred duration class keeps the recorded lexical fallback (P1Y before P2M).</summary>
    [TestMethod]
    public async Task OrderByDurationsKeepsTheRecordedFallback()
    {
        string[] ordered = await OrderedLexicalFormsAsync("\"P2M\"^^xsd:duration \"P1Y\"^^xsd:duration").ConfigureAwait(false);

        string[] expected = ["P1Y", "P2M"];
        Assert.AreSequenceEqual(expected, ordered);
    }

    /// <summary>O6: MIN and MAX inherit the totalized comparison, so encounter order cannot change the extremes.</summary>
    [TestMethod]
    public async Task MinAndMaxAreEncounterIndependent()
    {
        const string Forward = "\"2020-01-01T00:30:00\"^^xsd:dateTime \"2020-01-01T01:00:00+00:00\"^^xsd:dateTime \"2020-01-01T00:00:00-05:00\"^^xsd:dateTime";
        const string Backward = "\"2020-01-01T00:00:00-05:00\"^^xsd:dateTime \"2020-01-01T01:00:00+00:00\"^^xsd:dateTime \"2020-01-01T00:30:00\"^^xsd:dateTime";

        foreach(string valuesList in new[] { Forward, Backward })
        {
            string query = $"PREFIX xsd: <{XsdNamespace}> SELECT (MIN(?x) AS ?min) (MAX(?x) AS ?max) WHERE {{ VALUES ?x {{ {valuesList} }} }}";
            IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, expressionContext: null).ConfigureAwait(false);

            SparqlSolution solution = solutions.Single();
            Assert.AreEqual("2020-01-01T00:30:00", LexicalForm(solution, "min"));
            Assert.AreEqual("2020-01-01T00:00:00-05:00", LexicalForm(solution, "max"));
        }
    }

    /// <summary>O7: an unbound key sorts first under ASC (§15.1: unbound precedes every RDF term), unchanged by the temporal branch.</summary>
    [TestMethod]
    public async Task OrderByUnboundKeySortsFirst()
    {
        string query = $"PREFIX xsd: <{XsdNamespace}> SELECT ?k WHERE {{ VALUES (?seed ?k) {{ (2 \"2020-01-01T01:00:00+00:00\"^^xsd:dateTime) (1 UNDEF) (3 \"2020-01-01T00:30:00\"^^xsd:dateTime) }} }} ORDER BY ?k";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, expressionContext: null).ConfigureAwait(false);

        Assert.HasCount(3, solutions);
        Assert.IsFalse(solutions[0].TryGetValue(Variable("k"), out _), "The unbound key must sort first.");
        Assert.AreEqual("2020-01-01T00:30:00", LexicalForm(solutions[1], "k"));
        Assert.AreEqual("2020-01-01T01:00:00+00:00", LexicalForm(solutions[2], "k"));
    }

    /// <summary>The implicit timezone is a host seam: the same naive/aware cell flips when the host configures +02:00 instead of the UTC default.</summary>
    [TestMethod]
    public async Task ImplicitTimezoneSeamIsHostConfigurable()
    {
        const string Expression = "\"2020-01-01T02:30:00\"^^xsd:dateTime < \"2020-01-01T01:00:00Z\"^^xsd:dateTime";

        RdfTerm? underUtc = await BindResultAsync(Expression, expressionContext: null).ConfigureAwait(false);
        Assert.AreEqual("false", AssertLiteral(underUtc, "TZ/UTC").Value.ToString());

        SparqlExpressionContext plusTwo = SparqlExpressionContext.CreateDefault(implicitTimezone: TimeSpan.FromHours(2));
        RdfTerm? underPlusTwo = await BindResultAsync(Expression, plusTwo).ConfigureAwait(false);
        Assert.AreEqual("true", AssertLiteral(underPlusTwo, "TZ/+02:00").Value.ToString());
    }

    /// <summary>An implicit timezone outside the XSD ±14:00 bound is a configuration invariant violation.</summary>
    [TestMethod]
    public void ImplicitTimezoneBeyondTheXsdBoundThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => SparqlExpressionContext.CreateDefault(implicitTimezone: TimeSpan.FromHours(15)));
    }

    /// <summary>Evaluates a comparison expression through <c>BIND</c> over a one-row <c>VALUES</c> seed, returning the bound result term or <see langword="null"/> for a type error.</summary>
    /// <param name="expression">The SPARQL comparison expression.</param>
    /// <param name="expressionContext">The expression context, or <see langword="null"/> for the engine default.</param>
    /// <returns>The bound result term, or <see langword="null"/> when the expression erred.</returns>
    private async Task<RdfTerm?> BindResultAsync(string expression, SparqlExpressionContext? expressionContext)
    {
        string query = $"PREFIX xsd: <{XsdNamespace}> SELECT ?r WHERE {{ VALUES ?seed {{ 1 }} BIND(({expression}) AS ?r) }}";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, expressionContext).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();

        return solution.TryGetValue(Variable("r"), out RdfTerm value) ? value : null;
    }

    /// <summary>Runs an <c>ORDER BY</c> query over a <c>VALUES</c> list and returns the ordered lexical forms.</summary>
    /// <param name="valuesList">The space-separated literal list for the <c>VALUES</c> clause.</param>
    /// <param name="modifier">The solution modifier; ascending <c>ORDER BY ?x</c> unless overridden.</param>
    /// <returns>The ordered lexical forms of <c>?x</c>.</returns>
    private async Task<string[]> OrderedLexicalFormsAsync(string valuesList, string modifier = "ORDER BY ?x")
    {
        string query = $"PREFIX xsd: <{XsdNamespace}> SELECT ?x WHERE {{ VALUES ?x {{ {valuesList} }} }} {modifier}";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, expressionContext: null).ConfigureAwait(false);

        return [.. solutions.Select(static solution => LexicalForm(solution, "x"))];
    }

    /// <summary>Parses, translates, and evaluates a query over an empty data graph.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="expressionContext">The expression context, or <see langword="null"/> for the engine default.</param>
    /// <returns>The solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> RunAsync(string query, SparqlExpressionContext? expressionContext)
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync([], expressionContext: expressionContext, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(query, pool);

        return await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses and translates a query to algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The string pool the parse allocates from.</param>
    /// <returns>The algebra root.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Asserts a bound term is a literal and returns it.</summary>
    /// <param name="term">The term, or <see langword="null"/> when unbound.</param>
    /// <param name="cell">The cell id for the failure message.</param>
    /// <returns>The literal.</returns>
    private static Literal AssertLiteral(RdfTerm? term, string cell)
    {
        Assert.IsNotNull(term, $"Cell {cell}: expected a bound verdict, not a type error.");
        Assert.IsInstanceOfType<Literal>(term);

        return (Literal)term;
    }

    /// <summary>The lexical form of a bound literal variable.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name without the marker.</param>
    /// <returns>The lexical form.</returns>
    private static string LexicalForm(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");
        Assert.IsInstanceOfType<Literal>(value);

        return ((Literal)value).Value.ToString();
    }

    /// <summary>Builds a SPARQL variable.</summary>
    /// <param name="name">The variable name without the marker.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }
}
