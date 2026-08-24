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
/// End-to-end pins for the shipped semantics of literals whose datatype IRI is outside the modelled XSD
/// set, keyed to the value-datatype-seam baseline row list
/// for the value-datatype seam: <c>=</c>/<c>!=</c> stay RDF term
/// identity (a boolean verdict, never a type error), the ordering operators are type errors that make
/// <c>FILTER</c> drop the row, <c>IN</c>/<c>NOT IN</c> follow the equality contract, <c>ORDER BY</c> ranks
/// each such datatype as its own class keyed by its datatype IRI with a datatype-IRI-then-lexical-bytes
/// tiebreak, and <c>DISTINCT</c> deduplicates by term identity so lexically-distinct literals both survive.
/// </summary>
[TestClass]
internal sealed class UnregisteredDatatypeConformanceTests
{
    /// <summary>The GeoSPARQL namespace the query literals use; <c>geo:wktLiteral</c> is unmodelled at the value layer.</summary>
    private const string GeoNamespace = "http://www.opengis.net/ont/geosparql#";

    /// <summary>A second namespace of unmodelled datatype IRIs for the cross-datatype and tiebreak rows.</summary>
    private const string ExampleDatatypeNamespace = "http://example.org/datatype/";

    /// <summary>The prefix declarations every query in this battery carries.</summary>
    private const string Prefixes = "PREFIX geo: <" + GeoNamespace + "> PREFIX ex: <" + ExampleDatatypeNamespace + ">";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every equality cell: <c>=</c>/<c>!=</c> on unmodelled-datatype literals is RDF term identity — a boolean verdict, never a type error.</summary>
    /// <param name="cell">The baseline row id.</param>
    /// <param name="expression">The SPARQL comparison expression.</param>
    /// <param name="expected">The expected boolean lexical.</param>
    [TestMethod]
    [DataRow("E1", "\"POINT(1 2)\"^^geo:wktLiteral = \"POINT(1 2)\"^^geo:wktLiteral", "true")]
    [DataRow("E2", "\"POINT(1 2)\"^^geo:wktLiteral = \"POINT(1.0 2.0)\"^^geo:wktLiteral", "false")]
    [DataRow("E3", "\"POINT(1 2)\"^^geo:wktLiteral != \"POINT(1 2)\"^^geo:wktLiteral", "false")]
    [DataRow("E4", "\"POINT(1 2)\"^^geo:wktLiteral != \"POINT(1.0 2.0)\"^^geo:wktLiteral", "true")]
    [DataRow("E5", "\"POINT(1 2)\"^^geo:wktLiteral = \"POINT(1 2)\"^^ex:alpha", "false")]
    [DataRow("E6", "\"POINT(1 2)\"^^geo:wktLiteral != \"POINT(1 2)\"^^ex:alpha", "true")]
    public async Task EqualityCellsKeepTermIdentity(string cell, string expression, string expected)
    {
        RdfTerm? result = await BindResultAsync(expression).ConfigureAwait(false);

        Literal boolLiteral = AssertLiteral(result, cell);
        Assert.AreEqual(expected, boolLiteral.Value.ToString(), $"Cell {cell}: wrong verdict.");
    }

    /// <summary>Every ordering cell: <c>&lt;</c> <c>&lt;=</c> <c>&gt;</c> <c>&gt;=</c> on unmodelled-datatype literals is a type error (an unbound <c>BIND</c> result), even between identical terms.</summary>
    /// <param name="cell">The baseline row id.</param>
    /// <param name="expression">The SPARQL comparison expression.</param>
    [TestMethod]
    [DataRow("O1", "\"POINT(1 2)\"^^geo:wktLiteral < \"POINT(3 4)\"^^geo:wktLiteral")]
    [DataRow("O2", "\"POINT(1 2)\"^^geo:wktLiteral <= \"POINT(1 2)\"^^geo:wktLiteral")]
    [DataRow("O3", "\"POINT(1 2)\"^^geo:wktLiteral > \"POINT(3 4)\"^^geo:wktLiteral")]
    [DataRow("O4", "\"POINT(1 2)\"^^geo:wktLiteral >= \"POINT(1 2)\"^^geo:wktLiteral")]
    public async Task OrderingCellsAreTypeErrors(string cell, string expression)
    {
        RdfTerm? result = await BindResultAsync(expression).ConfigureAwait(false);

        Assert.IsNull(result, $"Cell {cell}: the ordering comparison must be a type error (an unbound BIND result).");
    }

    /// <summary>F1: an erring ordering comparison in <c>FILTER</c> drops the row.</summary>
    [TestMethod]
    public async Task FilterOrderingComparisonDropsTheRow()
    {
        string query = Prefixes + " SELECT ?x WHERE { VALUES ?x { \"POINT(1 2)\"^^geo:wktLiteral } FILTER(?x < \"POINT(3 4)\"^^geo:wktLiteral) }";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query).ConfigureAwait(false);

        Assert.IsEmpty(solutions, "Cell F1: the erring FILTER must drop the row.");
    }

    /// <summary>Every membership cell: <c>IN</c>/<c>NOT IN</c> on unmodelled-datatype literals follow the term-identity equality contract.</summary>
    /// <param name="cell">The baseline row id.</param>
    /// <param name="expression">The SPARQL membership expression.</param>
    /// <param name="expected">The expected boolean lexical.</param>
    [TestMethod]
    [DataRow("I1", "\"POINT(1 2)\"^^geo:wktLiteral IN (\"POINT(3 4)\"^^geo:wktLiteral, \"POINT(1 2)\"^^geo:wktLiteral)", "true")]
    [DataRow("I2", "\"POINT(1 2)\"^^geo:wktLiteral IN (\"POINT(1.0 2.0)\"^^geo:wktLiteral, \"POINT(3 4)\"^^geo:wktLiteral)", "false")]
    [DataRow("I3", "\"POINT(1 2)\"^^geo:wktLiteral NOT IN (\"POINT(1.0 2.0)\"^^geo:wktLiteral, \"POINT(3 4)\"^^geo:wktLiteral)", "true")]
    [DataRow("I4", "\"POINT(1 2)\"^^geo:wktLiteral NOT IN (\"POINT(3 4)\"^^geo:wktLiteral, \"POINT(1 2)\"^^geo:wktLiteral)", "false")]
    public async Task MembershipCellsKeepTermIdentity(string cell, string expression, string expected)
    {
        RdfTerm? result = await BindResultAsync(expression).ConfigureAwait(false);

        Literal boolLiteral = AssertLiteral(result, cell);
        Assert.AreEqual(expected, boolLiteral.Value.ToString(), $"Cell {cell}: wrong verdict.");
    }

    /// <summary>S1: an unmodelled datatype is its own <c>ORDER BY</c> class keyed by its datatype IRI — <c>geo:</c> before the numeric class key, numerics before strings — from both encounter orders.</summary>
    [TestMethod]
    public async Task OrderByRanksTheUnknownClassByItsDatatypeIri()
    {
        string[] expected = ["POINT(1 2)", "5", "abc"];

        Assert.AreSequenceEqual(
            expected,
            await OrderedLexicalFormsAsync("\"POINT(1 2)\"^^geo:wktLiteral 5 \"abc\"").ConfigureAwait(false));
        Assert.AreSequenceEqual(
            expected,
            await OrderedLexicalFormsAsync("\"abc\" 5 \"POINT(1 2)\"^^geo:wktLiteral").ConfigureAwait(false));
    }

    /// <summary>S2: within the unmodelled classes the order is datatype IRI first, then lexical bytes, from both encounter orders.</summary>
    [TestMethod]
    public async Task OrderByTiebreakIsDatatypeIriThenLexicalBytes()
    {
        const string Forward = "\"b\"^^ex:alpha \"a\"^^ex:beta \"a\"^^ex:alpha";
        const string Backward = "\"a\"^^ex:alpha \"a\"^^ex:beta \"b\"^^ex:alpha";
        string[] expectedLexicals = ["a", "b", "a"];
        string[] expectedDatatypes =
        [
            ExampleDatatypeNamespace + "alpha",
            ExampleDatatypeNamespace + "alpha",
            ExampleDatatypeNamespace + "beta",
        ];

        foreach(string valuesList in new[] { Forward, Backward })
        {
            IReadOnlyList<SparqlSolution> solutions = await OrderedSolutionsAsync(valuesList).ConfigureAwait(false);
            string[] lexicals = [.. solutions.Select(static solution => LexicalForm(solution, "x"))];
            string[] datatypes = [.. solutions.Select(static solution => DatatypeIri(solution, "x"))];

            Assert.AreSequenceEqual(expectedLexicals, lexicals);
            Assert.AreSequenceEqual(expectedDatatypes, datatypes);
        }
    }

    /// <summary>D1: <c>DISTINCT</c> deduplicates by term identity — identical literals collapse, lexically-distinct literals of one unmodelled datatype both survive.</summary>
    [TestMethod]
    public async Task DistinctKeepsLexicallyDistinctLiterals()
    {
        string query = Prefixes + " SELECT DISTINCT ?x WHERE { VALUES ?x { \"POINT(1 2)\"^^geo:wktLiteral \"POINT(1 2)\"^^geo:wktLiteral \"POINT(1.0 2.0)\"^^geo:wktLiteral } } ORDER BY ?x";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query).ConfigureAwait(false);
        string[] lexicals = [.. solutions.Select(static solution => LexicalForm(solution, "x"))];

        string[] expected = ["POINT(1 2)", "POINT(1.0 2.0)"];
        Assert.AreSequenceEqual(expected, lexicals);
    }

    /// <summary>Evaluates an expression through <c>BIND</c> over a one-row <c>VALUES</c> seed, returning the bound result term or <see langword="null"/> for a type error.</summary>
    /// <param name="expression">The SPARQL expression.</param>
    /// <returns>The bound result term, or <see langword="null"/> when the expression erred.</returns>
    private async Task<RdfTerm?> BindResultAsync(string expression)
    {
        string query = Prefixes + $" SELECT ?r WHERE {{ VALUES ?seed {{ 1 }} BIND(({expression}) AS ?r) }}";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query).ConfigureAwait(false);

        SparqlSolution solution = solutions.Single();

        return solution.TryGetValue(Variable("r"), out RdfTerm value) ? value : null;
    }

    /// <summary>Runs an ascending <c>ORDER BY</c> query over a <c>VALUES</c> list and returns the ordered lexical forms of <c>?x</c>.</summary>
    /// <param name="valuesList">The space-separated literal list for the <c>VALUES</c> clause.</param>
    /// <returns>The ordered lexical forms.</returns>
    private async Task<string[]> OrderedLexicalFormsAsync(string valuesList)
    {
        IReadOnlyList<SparqlSolution> solutions = await OrderedSolutionsAsync(valuesList).ConfigureAwait(false);

        return [.. solutions.Select(static solution => LexicalForm(solution, "x"))];
    }

    /// <summary>Runs an ascending <c>ORDER BY ?x</c> query over a <c>VALUES</c> list and returns the ordered solutions.</summary>
    /// <param name="valuesList">The space-separated literal list for the <c>VALUES</c> clause.</param>
    /// <returns>The ordered solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> OrderedSolutionsAsync(string valuesList)
    {
        string query = Prefixes + $" SELECT ?x WHERE {{ VALUES ?x {{ {valuesList} }} }} ORDER BY ?x";

        return await RunAsync(query).ConfigureAwait(false);
    }

    /// <summary>Parses, translates, and evaluates a query over an empty data graph under the engine-default expression context.</summary>
    /// <param name="query">The query text.</param>
    /// <returns>The solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> RunAsync(string query)
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync([], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
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

    /// <summary>The datatype IRI of a bound literal variable.</summary>
    /// <param name="solution">The solution.</param>
    /// <param name="variableName">The variable name without the marker.</param>
    /// <returns>The datatype IRI.</returns>
    private static string DatatypeIri(SparqlSolution solution, string variableName)
    {
        Assert.IsTrue(solution.TryGetValue(Variable(variableName), out RdfTerm value), $"Expected ?{variableName} to be bound.");
        Assert.IsInstanceOfType<Literal>(value);

        return ((Literal)value).Datatype.Iri.ToString();
    }

    /// <summary>Builds a SPARQL variable.</summary>
    /// <param name="name">The variable name without the marker.</param>
    /// <returns>The variable.</returns>
    private static SparqlVariable Variable(string name)
    {
        return new SparqlVariable(Utf8Strings.From(name));
    }
}
