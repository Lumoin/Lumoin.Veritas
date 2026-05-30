using System.Threading.Tasks;
using Lumoin.Veritas.Tests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Runs SPARQL query-evaluation tests through the full engine pipeline (parse → normalize → translate → execute)
/// and compares results to the expected fixtures with <see cref="SparqlResultComparer"/>.
/// </summary>
/// <remarks>
/// This suite currently runs the hand-authored smoke manifest under <c>Material/Sparql/eval-smoke</c>, which proves
/// the evaluation harness end-to-end ahead of vendoring the full W3C SPARQL evaluation corpus. A test the executor
/// cannot yet run (CONSTRUCT/DESCRIBE, a JSON expected fixture, an unsupported operator) is reported inconclusive,
/// not failed; a genuine wrong answer fails the suite.
/// </remarks>
[TestClass]
internal sealed class W3cSparqlEvalTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Runs one SPARQL query-evaluation smoke test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public async Task RunW3cSparqlEvalSmokeTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cSparqlEvalRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);
        ConformanceAssertions.Apply(outcome);
    }
}
