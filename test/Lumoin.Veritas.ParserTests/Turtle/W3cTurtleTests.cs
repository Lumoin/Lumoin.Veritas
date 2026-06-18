// W3C RDF Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/Turtle/turtle/.
// See Material/Turtle/ATTRIBUTION.md for provenance.

using System.Threading.Tasks;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

[TestClass]
internal sealed class W3cTurtleTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [W3cManifestData("Turtle", "turtle")]
    public async Task RunW3cTurtleTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleConformanceReader.ReadAsync(stream, TurtleSyntax.Turtle, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }
}
