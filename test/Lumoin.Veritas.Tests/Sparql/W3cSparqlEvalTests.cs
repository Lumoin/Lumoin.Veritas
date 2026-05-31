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

    /// <summary>
    /// Runs a query-evaluation test case and applies the outcome. A wrong answer fails (the conformance baseline);
    /// a structural non-runnable (handled by the runner) is inconclusive. Non-query-evaluation entries in a mixed
    /// manifest are skipped.
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(testCase.Type is W3cTestType.PositiveUpdateSyntax or W3cTestType.NegativeUpdateSyntax)
        {
            W3cOutcome syntaxOutcome = await W3cSparqlEvalRunner.RunUpdateSyntaxAsync(
                testCase,
                expectSuccess: testCase.Type == W3cTestType.PositiveUpdateSyntax,
                TestContext.CancellationToken).ConfigureAwait(false);
            ConformanceAssertions.Apply(syntaxOutcome);
            return;
        }

        if(testCase.Type == W3cTestType.SparqlUpdateEvaluation)
        {
            W3cOutcome updateOutcome = await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);
            ConformanceAssertions.Apply(updateOutcome);
            return;
        }

        if(testCase.Type != W3cTestType.SparqlQueryEvaluation)
        {
            Assert.Inconclusive($"Not a SPARQL query-evaluation test ('{testCase.RawTypeIri}').");
            return;
        }

        W3cOutcome outcome = await W3cSparqlEvalRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);
        ConformanceAssertions.Apply(outcome);
    }
}
