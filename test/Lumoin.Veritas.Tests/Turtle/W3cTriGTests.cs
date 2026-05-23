// W3C RDF Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.Tests/Material/Turtle/trig/.
// See Material/Turtle/ATTRIBUTION.md for provenance.

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Lumoin.Veritas.Tests.Conformance;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Turtle;

[TestClass]
internal sealed class W3cTriGTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DynamicData(nameof(LoadCases), DynamicDataDisplayName = nameof(GetDisplayName))]
    public async Task RunW3cTriGTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => TurtleReader.ReadAsync(stream, TurtleSyntax.TriG, pool: null, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    public static IEnumerable<object[]> LoadCases()
    {
        string manifestPath = W3cCorpusPath.For("Turtle", "trig", "manifest.ttl");
        W3cManifest manifest = W3cManifestLoader.Load(manifestPath);
        foreach(W3cTestCase testCase in manifest.Tests)
        {
            yield return [testCase];
        }
    }

    public static string GetDisplayName(MethodInfo methodInfo, object[] data)
    {
        W3cTestCase testCase = (W3cTestCase)data[0];

        return $"{testCase.Name} ({testCase.Type})";
    }
}
