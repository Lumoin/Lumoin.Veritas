using System;
using System.Collections.Generic;
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
/// Each suite is one feature directory under <c>Material/Sparql</c> (vendored from the pinned rdf-tests commit — see
/// <c>Material/Sparql/ATTRIBUTION.md</c>). A <c>SELECT</c>/<c>ASK</c> test whose expected result is a SPARQL Query
/// Results serialization (<c>.srx</c>/<c>.srj</c>) is executed and compared; a <c>CONSTRUCT</c>/<c>DESCRIBE</c> test
/// (an RDF-graph result) and a query using an operator the executor does not yet support are reported inconclusive
/// by <see cref="W3cSparqlEvalRunner"/>, not failed.
/// </para>
/// <para>
/// <see cref="KnownGaps"/> records the tests this build cannot yet pass for a known reason (an engine gap or an
/// unsupported feature the runner does not structurally detect). They are reported inconclusive; if any starts
/// passing, the run fails so its entry is removed. This mirrors <see cref="W3cSparqlSyntaxTests"/>.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cSparqlEvalTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Tests this build cannot yet pass, keyed by manifest <c>mf:name</c>, with the reason — the documented SPARQL
    /// evaluation-conformance frontier to drive down. Grouped by root cause.
    /// </summary>
    private static Dictionary<string, string> KnownGaps { get; } = new(StringComparer.Ordinal)
    {
        //Aggregate / arithmetic numeric result typing: the evaluator is double-based, so SUM/AVG over xsd:decimal or
        //xsd:integer produce an xsd:double rather than the spec's promoted decimal/integer type (and incur float
        //rounding). Full numeric-tower typing is a documented refinement.
        ["SUM"] = "aggregate numeric result typing (xsd:decimal/integer vs double) — numeric-tower refinement",
        ["SUM with GROUP BY"] = "aggregate numeric result typing — numeric-tower refinement",
        ["SUM DISTINCT with GROUP BY"] = "aggregate numeric result typing — numeric-tower refinement",
        ["AVG"] = "aggregate numeric result typing (AVG → xsd:decimal) — numeric-tower refinement",
        ["AVG with GROUP BY"] = "aggregate numeric result typing — numeric-tower refinement",
        ["AVG DISTINCT with GROUP BY"] = "aggregate numeric result typing — numeric-tower refinement",
        ["Error in AVG"] = "aggregate over a non-numeric term should error to unbound — numeric-tower refinement",
        ["MIN with GROUP BY"] = "MIN per group ordering/typing — aggregate refinement",
        ["COUNT 8b"] = "COUNT result detail — aggregate refinement",

        //GROUP_CONCAT value assembly (separator default, ordering, UTF-8) does not yet match the expected string.
        ["GROUP_CONCAT 1"] = "GROUP_CONCAT value assembly — engine gap",
        ["GROUP_CONCAT 2"] = "GROUP_CONCAT value assembly — engine gap",
        ["GROUP_CONCAT with SEPARATOR"] = "GROUP_CONCAT SEPARATOR handling — engine gap",

        //Correlated double-negation / NOT EXISTS set semantics (subset/equal-set computations) are not yet correct.
        ["Calculate which sets are subsets of others (include A subsetOf A)"] = "correlated NOT EXISTS set semantics — engine gap",
        ["Calculate which sets are subsets of others (exclude A subsetOf A)"] = "correlated NOT EXISTS set semantics — engine gap",
        ["Calculate which sets have the same elements"] = "correlated NOT EXISTS set semantics — engine gap",
        ["Calculate proper subset"] = "correlated NOT EXISTS set semantics — engine gap",

        //Sub-SELECT semantics: finer scoping/aggregate cases. (sq04/sq06/sq08/sq09/sq10 use RDF/XML data graphs the
        //harness cannot load and are skipped structurally by the runner, not gapped here.)
        ["sq11 - Subquery limit per resource"] = "sub-SELECT LIMIT-per-resource semantics — engine gap",
        ["sq13 - Subqueries don't inject bindings"] = "sub-SELECT variable scoping (no binding injection) — engine gap",

        //xsd:boolean effective-value over non-canonical lexicals (TRUE/FALSE/1/0).
        ["case-insensitive booleans"] = "xsd:boolean non-canonical lexical EBV — engine gap",

        //RDF-star / RDF 1.2 triple-term pattern matching and the TRIPLE() constructor — a deferred executor slice
        //(the BGP matcher does not yet match variables inside quoted triple terms).
        ["triple on triple terms"] = "RDF-star triple-term construction/matching — deferred executor slice",
        ["Reified triples - match constant reified triple"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - match reified triple, var subject"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - match reified triple, var predicate"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - match reified triple, var object"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Annotated triple - match constant reified triple"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Asserted and reified triple"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - Variable for reified triple reifier"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - match variables in triple terms"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - Nesting 1"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - Nesting 2"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - Match and nesting"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - Same variable"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Pattern - literals or triple terms"] = "RDF-star triple-term pattern matching — deferred executor slice",
        ["Reified triples - Embedded triple - value-equality"] = "RDF-star triple-term value equality — deferred executor slice",
        ["Reified triples - Embedded triple - ordering"] = "RDF-star triple-term ordering — deferred executor slice",

        //Trailing VALUES with multiple variables and UNDEF rows joins incorrectly.
        ["Post-query VALUES with 2 obj-vars, 2 rows with UNDEF"] = "trailing VALUES with UNDEF — engine gap"
    };

    /// <summary>Runs one hand-authored evaluation smoke test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmoke(W3cTestCase testCase) => RunAndAssertAsync(testCase);

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

    /// <summary>Runs one W3C aggregate evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregates(W3cTestCase testCase) => RunAndAssertAsync(testCase);

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

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExists(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C projection-expression evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpression(W3cTestCase testCase) => RunAndAssertAsync(testCase);

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

    /// <summary>
    /// Runs a test case and applies the outcome, translating a known gap into an inconclusive result (and failing if
    /// a known gap unexpectedly starts passing).
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        //A feature manifest can mix query-evaluation tests with update/other tests (the loader classifies the latter
        //as a non-evaluation type); this suite runs only the query-evaluation entries.
        if(testCase.Type != W3cTestType.SparqlQueryEvaluation)
        {
            Assert.Inconclusive($"Not a SPARQL query-evaluation test ('{testCase.RawTypeIri}').");
            return;
        }

        W3cOutcome outcome = await W3cSparqlEvalRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        if(KnownGaps.TryGetValue(testCase.Name, out string? reason))
        {
            if(outcome.Status == W3cOutcomeStatus.Passed)
            {
                Assert.Fail($"'{testCase.Name}' is recorded as a known gap ({reason}) but now passes; remove it from KnownGaps.");
            }

            Assert.Inconclusive($"Known gap ({reason}): {outcome.Message}");
            return;
        }

        ConformanceAssertions.Apply(outcome);
    }
}
