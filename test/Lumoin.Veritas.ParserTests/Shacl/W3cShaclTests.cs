// W3C SHACL 1.2 test suite — https://w3c.github.io/data-shapes/data-shapes-test-suite/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/Shacl/data-shapes-test-suite/tests/.
// See Material/Shacl/ATTRIBUTION.md for provenance.

using System.Threading.Tasks;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Runs the vendored W3C SHACL 1.2 test suite: each <c>sht:Validate</c> entry
/// validates its data graph against its shapes graph and compares the produced
/// report to the manifest's inline expected report via
/// <see cref="W3cShaclRunner"/>.
/// </summary>
[TestClass]
internal sealed class W3cShaclTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Runs one SHACL validation test from the vendored manifest.
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [W3cManifestData("Shacl", "data-shapes-test-suite/tests")]
    public async Task RunW3cShaclTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cShaclRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }
}
