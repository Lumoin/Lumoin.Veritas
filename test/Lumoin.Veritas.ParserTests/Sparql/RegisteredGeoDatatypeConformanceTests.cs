using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.ParserTests.Geo;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The registering host's degradation battery: every cell of the unregistered-datatype baseline
/// (<see cref="UnregisteredDatatypeConformanceTests"/>) re-evaluated with <c>geo:wktLiteral</c> registered
/// at the value layer, expecting the identical outcome. The registered definition abstains on value
/// identity, so <c>=</c>/<c>!=</c> fall through to RDF term identity, the ordering operators stay type
/// errors, <c>IN</c>/<c>NOT IN</c> follow the equality contract, and <c>ORDER BY</c>/<c>DISTINCT</c> keep
/// the datatype-IRI class ranking and term-identity deduplication — the design's degradation contract as
/// executable rows: registration whose definition abstains cannot move a comparison. The GML, GeoJSON,
/// KML and DGGS serialization datatypes carry that contract inside their own rows, which evaluate one
/// query under the registering host's registry and under the empty one and compare the two answers.
/// </summary>
[TestClass]
internal sealed class RegisteredGeoDatatypeConformanceTests
{
    /// <summary>The GeoSPARQL namespace the query literals use; <c>geo:wktLiteral</c> is the registered datatype.</summary>
    private const string GeoNamespace = "http://www.opengis.net/ont/geosparql#";

    /// <summary>A second namespace of unregistered datatype IRIs for the cross-datatype and tiebreak rows.</summary>
    private const string ExampleDatatypeNamespace = "http://example.org/datatype/";

    /// <summary>The prefix declarations every query in this battery carries.</summary>
    private const string Prefixes = "PREFIX geo: <" + GeoNamespace + "> PREFIX ex: <" + ExampleDatatypeNamespace + ">";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every equality cell keeps its baseline verdict: the registered definition abstains on value identity, so <c>=</c>/<c>!=</c> stay RDF term identity.</summary>
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
        Assert.AreEqual(expected, boolLiteral.Value.ToString(), $"Cell {cell}: wrong verdict under the registered datatype.");
    }

    /// <summary>Every ordering cell stays a type error: the value layer decides only <c>=</c>/<c>!=</c>, never an order.</summary>
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

        Assert.IsNull(result, $"Cell {cell}: the ordering comparison must stay a type error under the registered datatype.");
    }

    /// <summary>F1: an erring ordering comparison in <c>FILTER</c> still drops the row.</summary>
    [TestMethod]
    public async Task FilterOrderingComparisonDropsTheRow()
    {
        string query = Prefixes + " SELECT ?x WHERE { VALUES ?x { \"POINT(1 2)\"^^geo:wktLiteral } FILTER(?x < \"POINT(3 4)\"^^geo:wktLiteral) }";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query).ConfigureAwait(false);

        Assert.IsEmpty(solutions, "Cell F1: the erring FILTER must drop the row under the registered datatype.");
    }

    /// <summary>Every membership cell keeps the term-identity equality contract under the registered datatype.</summary>
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
        Assert.AreEqual(expected, boolLiteral.Value.ToString(), $"Cell {cell}: wrong verdict under the registered datatype.");
    }

    /// <summary>S1: the registered datatype is still its own <c>ORDER BY</c> class keyed by its datatype IRI, from both encounter orders.</summary>
    [TestMethod]
    public async Task OrderByRanksTheRegisteredClassByItsDatatypeIri()
    {
        string[] expected = ["POINT(1 2)", "5", "abc"];

        Assert.AreSequenceEqual(
            expected,
            await OrderedLexicalFormsAsync("\"POINT(1 2)\"^^geo:wktLiteral 5 \"abc\"").ConfigureAwait(false));
        Assert.AreSequenceEqual(
            expected,
            await OrderedLexicalFormsAsync("\"abc\" 5 \"POINT(1 2)\"^^geo:wktLiteral").ConfigureAwait(false));
    }

    /// <summary>S2: the datatype-IRI-then-lexical-bytes tiebreak is unmoved — the registry orders nothing.</summary>
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

    /// <summary>D1: <c>DISTINCT</c> still deduplicates by term identity — lexically-distinct literals of the registered datatype both survive, because the abstaining definition asserts no value identity.</summary>
    [TestMethod]
    public async Task DistinctKeepsLexicallyDistinctLiterals()
    {
        string query = Prefixes + " SELECT DISTINCT ?x WHERE { VALUES ?x { \"POINT(1 2)\"^^geo:wktLiteral \"POINT(1 2)\"^^geo:wktLiteral \"POINT(1.0 2.0)\"^^geo:wktLiteral } } ORDER BY ?x";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query).ConfigureAwait(false);
        string[] lexicals = [.. solutions.Select(static solution => LexicalForm(solution, "x"))];

        string[] expected = ["POINT(1 2)", "POINT(1.0 2.0)"];
        Assert.AreSequenceEqual(expected, lexicals);
    }

    /// <summary>Equality over two lexically-distinct literals whose recognition abstains answers identically under both registries: the definition asserts no value identity, so <c>=</c> stays RDF term identity.</summary>
    /// <param name="cell">The row id.</param>
    /// <param name="expression">The SPARQL comparison expression.</param>
    /// <param name="expected">The expected boolean lexical.</param>
    [TestMethod]
    [DataRow("M1", "'<Curve xmlns=\"http://www.opengis.net/gml/3.2\"/>'^^geo:gmlLiteral = '<Solid xmlns=\"http://www.opengis.net/gml/3.2\"/>'^^geo:gmlLiteral", "false")]
    [DataRow("M2", "'{\"type\": \"Point\", \"type\": \"Point\", \"coordinates\": [1, 2]}'^^geo:geoJSONLiteral = '{\"type\": \"Point\", \"type\": \"Point\", \"coordinates\": [3, 4]}'^^geo:geoJSONLiteral", "false")]
    [DataRow("M3", "'<Point><coordinates>1,2</coordinates></Point>'^^geo:kmlLiteral = '<Point><coordinates>3,4</coordinates></Point>'^^geo:kmlLiteral", "false")]
    [DataRow("M7", "'<https://w3id.org/dggs/auspix> CELL (R3234)'^^geo:dggsLiteral = '<https://w3id.org/dggs/auspix> CELL (R3235)'^^geo:dggsLiteral", "false")]
    public async Task SerializationDatatypeEqualityAnswersIdenticallyUnderBothRegistries(string cell, string expression, string expected)
    {
        RdfTerm? registered = await BindResultAsync(expression, RegisteredContext()).ConfigureAwait(false);
        RdfTerm? empty = await BindResultAsync(expression, EmptyRegistryContext()).ConfigureAwait(false);

        Assert.AreEqual(expected, AssertLiteral(registered, cell).Value.ToString(), $"Cell {cell}: wrong verdict under the registered datatype.");
        Assert.AreEqual(expected, AssertLiteral(empty, cell).Value.ToString(), $"Cell {cell}: wrong verdict under the empty registry.");
    }

    /// <summary><c>DISTINCT</c> deduplicates by term identity under both registries: the repeated literal collapses and the lexically-distinct one survives, because the abstaining definition asserts no value identity.</summary>
    /// <param name="cell">The row id.</param>
    /// <param name="valuesList">The space-separated literal list for the <c>VALUES</c> clause.</param>
    [TestMethod]
    [DataRow("M4", "'<Curve xmlns=\"http://www.opengis.net/gml/3.2\"/>'^^geo:gmlLiteral '<Curve xmlns=\"http://www.opengis.net/gml/3.2\"/>'^^geo:gmlLiteral '<Solid xmlns=\"http://www.opengis.net/gml/3.2\"/>'^^geo:gmlLiteral")]
    [DataRow("M5", "'{\"type\": \"Point\", \"type\": \"Point\", \"coordinates\": [1, 2]}'^^geo:geoJSONLiteral '{\"type\": \"Point\", \"type\": \"Point\", \"coordinates\": [1, 2]}'^^geo:geoJSONLiteral '{\"type\": \"Point\", \"type\": \"Point\", \"coordinates\": [3, 4]}'^^geo:geoJSONLiteral")]
    [DataRow("M6", "'<Point><coordinates>1,2</coordinates></Point>'^^geo:kmlLiteral '<Point><coordinates>1,2</coordinates></Point>'^^geo:kmlLiteral '<Point><coordinates>3,4</coordinates></Point>'^^geo:kmlLiteral")]
    [DataRow("M8", "'<https://w3id.org/dggs/auspix> CELL (R3234)'^^geo:dggsLiteral '<https://w3id.org/dggs/auspix> CELL (R3234)'^^geo:dggsLiteral '<https://w3id.org/dggs/auspix> CELL (R3235)'^^geo:dggsLiteral")]
    public async Task SerializationDatatypeDistinctAnswersIdenticallyUnderBothRegistries(string cell, string valuesList)
    {
        string query = Prefixes + $" SELECT DISTINCT ?x WHERE {{ VALUES ?x {{ {valuesList} }} }} ORDER BY ?x";
        IReadOnlyList<SparqlSolution> registeredSolutions = await RunAsync(query, RegisteredContext()).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> emptySolutions = await RunAsync(query, EmptyRegistryContext()).ConfigureAwait(false);
        string[] registeredLexicals = [.. registeredSolutions.Select(static solution => LexicalForm(solution, "x"))];
        string[] emptyLexicals = [.. emptySolutions.Select(static solution => LexicalForm(solution, "x"))];

        Assert.HasCount(2, registeredLexicals, $"Cell {cell}: the repeated literal collapses and the lexically-distinct one survives.");
        Assert.AreSequenceEqual(registeredLexicals, emptyLexicals);
    }

    /// <summary>Equality over two same-value, differently-spelled house <c>a5Literal</c> forms DECIDES under the registering host — the subclass declares value equality by canonical cell-set identity — while the empty registry stays at term identity and answers false.</summary>
    [TestMethod]
    public async Task A5LiteralEqualityDecidesOnlyUnderRegistration()
    {
        string expression = "'<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000 a00000000000000)'^^<https://lumoin.com/veritas/dggs/a5Literal> = '<https://lumoin.com/veritas/dggs/a5> cells (A00000000000000 0600000000000000)'^^<https://lumoin.com/veritas/dggs/a5Literal>";
        RdfTerm? registered = await BindResultAsync(expression, RegisteredContext()).ConfigureAwait(false);
        RdfTerm? empty = await BindResultAsync(expression, EmptyRegistryContext()).ConfigureAwait(false);

        Assert.AreEqual("true", AssertLiteral(registered, "M9").Value.ToString(), "The registered subclass decides canonical cell-set identity.");
        Assert.AreEqual("false", AssertLiteral(empty, "M9").Value.ToString(), "The empty registry stays at term identity.");
    }

    /// <summary><c>DISTINCT</c> stays term-level under both registries even for the value-deciding subclass: two same-value, differently-spelled <c>a5Literal</c> forms both survive, preserving the lexically-distinct discipline.</summary>
    [TestMethod]
    public async Task A5LiteralDistinctStaysTermLevel()
    {
        string valuesList = "'<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000)'^^<https://lumoin.com/veritas/dggs/a5Literal> '<https://lumoin.com/veritas/dggs/a5> cells (0600000000000000)'^^<https://lumoin.com/veritas/dggs/a5Literal>";
        string query = Prefixes + $" SELECT DISTINCT ?x WHERE {{ VALUES ?x {{ {valuesList} }} }} ORDER BY ?x";
        IReadOnlyList<SparqlSolution> registeredSolutions = await RunAsync(query, RegisteredContext()).ConfigureAwait(false);
        IReadOnlyList<SparqlSolution> emptySolutions = await RunAsync(query, EmptyRegistryContext()).ConfigureAwait(false);

        Assert.HasCount(2, registeredSolutions, "M10: DISTINCT deduplicates by term identity, so both spellings survive.");
        Assert.HasCount(2, emptySolutions, "M10: the empty registry behaves identically.");
    }

    /// <summary>Evaluates an expression through <c>BIND</c> over a one-row <c>VALUES</c> seed, returning the bound result term or <see langword="null"/> for a type error.</summary>
    /// <param name="expression">The SPARQL expression.</param>
    /// <returns>The bound result term, or <see langword="null"/> when the expression erred.</returns>
    private async Task<RdfTerm?> BindResultAsync(string expression)
    {
        return await BindResultAsync(expression, RegisteredContext()).ConfigureAwait(false);
    }

    /// <summary>Evaluates an expression through <c>BIND</c> over a one-row <c>VALUES</c> seed under a given expression context.</summary>
    /// <param name="expression">The SPARQL expression.</param>
    /// <param name="context">The expression context the evaluation runs under.</param>
    /// <returns>The bound result term, or <see langword="null"/> when the expression erred.</returns>
    private async Task<RdfTerm?> BindResultAsync(string expression, SparqlExpressionContext context)
    {
        string query = Prefixes + $" SELECT ?r WHERE {{ VALUES ?seed {{ 1 }} BIND(({expression}) AS ?r) }}";
        IReadOnlyList<SparqlSolution> solutions = await RunAsync(query, context).ConfigureAwait(false);

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

    /// <summary>Parses, translates, and evaluates a query over an empty data graph under an expression context carrying the registering host's registry.</summary>
    /// <param name="query">The query text.</param>
    /// <returns>The solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> RunAsync(string query)
    {
        return await RunAsync(query, RegisteredContext()).ConfigureAwait(false);
    }

    /// <summary>Parses, translates, and evaluates a query over an empty data graph under a given expression context.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="context">The expression context the evaluation runs under.</param>
    /// <returns>The solutions.</returns>
    private async Task<IReadOnlyList<SparqlSolution>> RunAsync(string query, SparqlExpressionContext context)
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync([], expressionContext: context, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(query, pool);

        return await engine.EvaluateAsync(algebra, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>The expression context carrying the registering host's registry.</summary>
    /// <returns>The context.</returns>
    private static SparqlExpressionContext RegisteredContext()
    {
        return SparqlExpressionContext.CreateDefault(valueDatatypes: GeoArmRegistry.SerializationRegistered);
    }

    /// <summary>The expression context carrying the empty registry, under which nothing is registered at the value layer.</summary>
    /// <returns>The context.</returns>
    private static SparqlExpressionContext EmptyRegistryContext()
    {
        return SparqlExpressionContext.CreateDefault();
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
