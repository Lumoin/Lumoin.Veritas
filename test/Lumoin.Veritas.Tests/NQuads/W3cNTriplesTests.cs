// W3C RDF Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.Tests/Material/NQuads/n-triples/.
// See Material/NQuads/ATTRIBUTION.md for provenance.

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Tests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.NQuads;

[TestClass]
internal sealed class W3cNTriplesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DynamicData(nameof(LoadCases), DynamicDataDisplayName = nameof(GetDisplayName))]
    public async Task RunW3cNTriplesTest(W3cTestCase testCase)
    {
        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            static (stream, ct) => NQuadsReader.ReadAsync(stream, pool: null, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    public static IEnumerable<object[]> LoadCases()
    {
        string manifestPath = W3cCorpusPath.For("NQuads", "n-triples", "manifest.ttl");
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
