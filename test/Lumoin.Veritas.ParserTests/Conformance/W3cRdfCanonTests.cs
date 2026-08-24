// W3C RDF Dataset Canonicalization (RDFC-1.0) test suite — https://github.com/w3c/rdf-canon
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/RdfCanon/. See that folder's ATTRIBUTION.md.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Runs the vendored W3C rdf-canon (RDFC-1.0) test suite via <see cref="W3cRdfCanonRunner"/>: an
/// <c>rdfc:RDFC10EvalTest</c> compares the canonical N-Quads, an <c>rdfc:RDFC10MapTest</c> compares the
/// issued-identifier map, and an <c>rdfc:RDFC10NegativeEvalTest</c> expects a poison graph to be rejected. This is a
/// red-baseline + ratchet gate — a wrong canonical output, a wrong map, or a missing rejection fails, so the failing
/// count is the honest distance to full RDFC-1.0 conformance and may only go down.
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
