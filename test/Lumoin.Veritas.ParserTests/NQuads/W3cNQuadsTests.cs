// W3C RDF Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/NQuads/n-quads/.
// See Material/NQuads/ATTRIBUTION.md for provenance.

using System.Threading.Tasks;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class W3cNQuadsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [W3cManifestData("NQuads", "n-quads")]
    public async Task RunW3cNQuadsTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => NQuadsReader.ReadAsync(stream, pool: null, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }
}
