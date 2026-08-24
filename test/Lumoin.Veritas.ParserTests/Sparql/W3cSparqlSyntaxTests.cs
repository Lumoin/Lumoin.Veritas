// W3C SPARQL Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/Sparql/.
// See Material/Sparql/ATTRIBUTION.md for provenance.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Runs the vendored W3C SPARQL query-syntax conformance suites against
/// <see cref="SparqlParser"/>: a positive-syntax test passes when the query
/// parses without error, a negative-syntax test passes when parsing raises a
/// <see cref="Lumoin.Veritas.Sparql.SparqlParseException"/>.
/// </summary>
/// <remarks>
/// The conformance runner is quad-oriented; a SPARQL query produces no quads, so
/// the reader here parses the query and yields an empty quad stream, propagating
/// any parse failure for the runner to observe. Update-syntax test types are
/// classified as unknown by the loader and skipped (this build parses queries).
/// </remarks>
[TestClass]
internal sealed class W3cSparqlSyntaxTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Tests this build's parser cannot decide by syntax alone, keyed by manifest <c>mf:name</c> with
    /// the reason. They need capabilities beyond a query parser — variable-scope analysis (SELECT /
    /// GROUP BY / BIND scope rules, duplicate projections), static aggregate-nesting checks, a
    /// PN_LOCAL-escape lexer refinement, or the sugar-expansion pass (a standalone blank-node property
    /// list). They are reported as inconclusive rather than failing the suite; if any ever starts
    /// passing, the run fails so the entry is removed.
    /// </summary>
    private static Dictionary<string, string> KnownGaps { get; } = new(StringComparer.Ordinal);

    /// <summary>Runs one SPARQL 1.1 query-syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-query")]
    public Task RunW3cSparql11SyntaxQueryTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 query-syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax")]
    public Task RunW3cSparql12SyntaxTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 <c>VERSION</c>-declaration syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "version")]
    public Task RunW3cSparql12VersionTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 codepoint-escape syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "codepoint-escapes")]
    public Task RunW3cSparql12CodepointEscapesTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>
    /// Runs a test case and applies the outcome, translating a known gap into an inconclusive result
    /// (and failing if a known gap unexpectedly starts passing).
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(testCase, SparqlConformanceReader.ParseQuery, TestContext.CancellationToken).ConfigureAwait(false);

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
