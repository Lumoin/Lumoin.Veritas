using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Tests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Runs the vendored W3C SPARQL query-evaluation suites through the full engine pipeline (parse → normalize →
/// translate → execute) and compares results to the expected fixtures with <see cref="SparqlResultComparer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The complete official SPARQL query-evaluation corpus is the fixed measuring stick: every suite the upstream
/// <c>manifest-sparql11-query.ttl</c> / sparql12 manifest references is vendored (see
/// <c>Material/Sparql/ATTRIBUTION.md</c>), and a test that produces a wrong answer <b>fails</b> (red) — the failing
/// count is the honest distance to full conformance, driven down as features land (the SHACL suite's baseline model,
/// not the syntax suite's known-gap model).
/// </para>
/// <para>
/// A test the harness cannot run structurally is reported inconclusive, not failed, by
/// <see cref="W3cSparqlEvalRunner"/>: a <c>CONSTRUCT</c>/<c>DESCRIBE</c> graph result, a query using an operator the
/// executor does not yet support (<c>GRAPH</c>/<c>SERVICE</c>), a data graph in a format the harness cannot read
/// (RDF/XML, TriG), or a non-query-evaluation entry in a mixed manifest.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cSparqlEvalTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Runs one hand-authored evaluation smoke test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmoke(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C aggregate evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregates(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>BIND</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bind")]
    public Task RunBind(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>VALUES</c>/<c>BINDINGS</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bindings")]
    public Task RunBindings(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C cast evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "cast")]
    public Task RunCast(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>CONSTRUCT</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "construct")]
    public Task RunConstruct(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExists(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C built-in function evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "functions")]
    public Task RunFunctions(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>GROUP BY</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "grouping")]
    public Task RunGrouping(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C negation (<c>MINUS</c>/<c>NOT EXISTS</c>) evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "negation")]
    public Task RunNegation(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C projection-expression evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpression(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C property-path evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "property-path")]
    public Task RunPropertyPath(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C sub-<c>SELECT</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "subquery")]
    public Task RunSubquery(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 expression evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "expression")]
    public Task RunExpression(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-triple-terms")]
    public Task RunEvalTripleTerms(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 grouping evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-grouping")]
    public Task RunSparql12Grouping(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 RDF-1.1-literals evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-rdf11")]
    public Task RunSparql12Rdf11(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 language-and-base-direction evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "lang-basedir")]
    public Task RunLangBaseDirection(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.1 JSON results-format test (the expected result is a <c>.srj</c> serialization).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "json-res")]
    public Task RunJsonResults(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.1 CSV/TSV results-format test (the expected result is a <c>.csv</c> or <c>.tsv</c> serialization).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "csv-tsv-res")]
    public Task RunCsvTsvResults(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term positive-syntax test (covers the <c>INSERT DATA</c> update-syntax entries).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-triple-terms-positive")]
    public Task RunSyntaxTripleTermsPositive(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term negative-syntax test (covers the negative update-syntax entries).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-triple-terms-negative")]
    public Task RunSyntaxTripleTermsNegative(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>INSERT</c>/<c>DELETE DATA</c> (basic-update) test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "basic-update")]
    public Task RunUpdateBasic(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE DATA</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-data")]
    public Task RunUpdateDeleteData(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete")]
    public Task RunUpdateDelete(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE WHERE</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-where")]
    public Task RunUpdateDeleteWhere(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE</c>/<c>INSERT</c> (modify) test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-insert")]
    public Task RunUpdateDeleteInsert(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>ADD</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "add")]
    public Task RunUpdateAdd(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>CLEAR</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "clear")]
    public Task RunUpdateClear(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>COPY</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "copy")]
    public Task RunUpdateCopy(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>MOVE</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "move")]
    public Task RunUpdateMove(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DROP</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "drop")]
    public Task RunUpdateDrop(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>SILENT</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "update-silent")]
    public Task RunUpdateSilent(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update positive/negative syntax test (set 1).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-update-1")]
    public Task RunSyntaxUpdate1(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update positive/negative syntax test (set 2).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-update-2")]
    public Task RunSyntaxUpdate2(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>
    /// Runs one manifest entry and applies the outcome, dispatching on its type so every entry in a mixed
    /// manifest runs with the right semantics — query / update evaluation, or query / update syntax. A wrong
    /// answer fails (the conformance baseline); a structural non-runnable (handled by the runner) is
    /// inconclusive.
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <param name="updateSyntaxSuite">
    /// Whether this suite is an update suite: a generic <c>PositiveSyntaxTest11</c>/<c>NegativeSyntaxTest11</c>
    /// entry (the W3C manifests do not distinguish query from update syntax by type) is then a SPARQL Update
    /// syntax test, parsed as an update rather than a query.
    /// </param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase, bool updateSyntaxSuite = false)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        W3cOutcome outcome = testCase.Type switch
        {
            W3cTestType.SparqlQueryEvaluation => await W3cSparqlEvalRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.SparqlUpdateEvaluation => await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.PositiveUpdateSyntax => await W3cSparqlEvalRunner.RunUpdateSyntaxAsync(testCase, expectSuccess: true, TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.NegativeUpdateSyntax => await W3cSparqlEvalRunner.RunUpdateSyntaxAsync(testCase, expectSuccess: false, TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.PositiveSyntax or W3cTestType.NegativeSyntax => await RunSyntaxAsync(testCase, updateSyntaxSuite).ConfigureAwait(false),
            _ => new W3cOutcome(W3cOutcomeStatus.Skipped, $"Unhandled W3C test type '{testCase.RawTypeIri}'.")
        };

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>
    /// Runs a generic positive/negative syntax entry as a query syntax test, or — in an update suite — as a
    /// SPARQL Update syntax test (the manifests type both as <c>PositiveSyntaxTest11</c>/<c>NegativeSyntaxTest11</c>).
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <param name="updateSyntaxSuite">Whether to parse as an update rather than a query.</param>
    /// <returns>The outcome.</returns>
    private Task<W3cOutcome> RunSyntaxAsync(W3cTestCase testCase, bool updateSyntaxSuite)
        => updateSyntaxSuite
            ? W3cSparqlEvalRunner.RunUpdateSyntaxAsync(testCase, expectSuccess: testCase.Type == W3cTestType.PositiveSyntax, TestContext.CancellationToken)
            : W3cTestRunner.RunAsync(testCase, SparqlConformanceReader.ParseQuery, TestContext.CancellationToken);
}
