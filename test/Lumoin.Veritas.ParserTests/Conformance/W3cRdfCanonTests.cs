// W3C RDF Dataset Canonicalization (RDFC-1.0) test suite — https://github.com/w3c/rdf-canon
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/RdfCanon/. See that folder's ATTRIBUTION.md.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Runs the vendored W3C rdf-canon (RDFC-1.0) test suite: each <c>rdfc:RDFC10EvalTest</c> canonicalizes its input
/// dataset and compares to the expected canonical N-Quads via <see cref="W3cRdfCanonRunner"/>. This is a
/// red-baseline + ratchet gate — a wrong canonical output fails, so the failing count is the honest distance to full
/// RDFC-1.0 conformance and may only go down. The issuer-map and negative (poison-graph) entries are reported
/// Inconclusive.
/// </summary>
[TestClass]
internal sealed class W3cRdfCanonTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Runs one rdf-canon test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [W3cRdfCanonData]
    public async Task RunW3cRdfCanonTest(W3cRdfCanonCase testCase)
    {
        W3cOutcome outcome = await W3cRdfCanonRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }
}
