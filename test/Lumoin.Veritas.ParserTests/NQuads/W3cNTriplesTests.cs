// W3C RDF Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/NQuads/n-triples/.
// See Material/NQuads/ATTRIBUTION.md for provenance.

using System.Threading.Tasks;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.NQuads;

[TestClass]
internal sealed class W3cNTriplesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [W3cManifestData("NQuads", "n-triples")]
    public async Task RunW3cNTriplesTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => NQuadsReader.ReadAsync(stream, pool: null, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }
}
