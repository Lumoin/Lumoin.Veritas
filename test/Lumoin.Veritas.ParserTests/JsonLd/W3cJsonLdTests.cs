// W3C JSON-LD 1.1 API test suite — https://github.com/w3c/json-ld-api/tree/main/tests
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/JsonLd/. See Material/JsonLd/ATTRIBUTION.md.

using System.Threading.Tasks;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.JsonLd;

/// <summary>
/// Runs the vendored W3C JSON-LD 1.1 API test suite against <c>Lumoin.Veritas.JsonLd</c>. RED-baseline +
/// ratchet (the failing count is the honest distance to full conformance, driven to 0 as gaps are closed —
/// the same model as the SPARQL eval / RDFC suites). Only the <c>expand</c> operation is wired so far;
/// <c>compact</c> and <c>toRdf</c> follow.
/// </summary>
[TestClass]
internal sealed class W3cJsonLdTests
{
    /// <summary>The ambient test context (carries the cancellation token).</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Runs one JSON-LD expansion test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [JsonLdManifestData("expand")]
    public async Task RunW3cJsonLdExpandTest(JsonLdTestCase testCase)
    {
        W3cOutcome outcome = await W3cJsonLdRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>Runs one JSON-LD toRdf (RDF extraction) test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [JsonLdManifestData("toRdf")]
    public async Task RunW3cJsonLdToRdfTest(JsonLdTestCase testCase)
    {
        W3cOutcome outcome = await W3cJsonLdRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>Runs one JSON-LD compact test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [JsonLdManifestData("compact")]
    public async Task RunW3cJsonLdCompactTest(JsonLdTestCase testCase)
    {
        W3cOutcome outcome = await W3cJsonLdRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>Runs one JSON-LD fromRdf test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [JsonLdManifestData("fromRdf")]
    public async Task RunW3cJsonLdFromRdfTest(JsonLdTestCase testCase)
    {
        W3cOutcome outcome = await W3cJsonLdRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>Runs one JSON-LD flatten test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [JsonLdManifestData("flatten")]
    public async Task RunW3cJsonLdFlattenTest(JsonLdTestCase testCase)
    {
        W3cOutcome outcome = await W3cJsonLdRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>Runs one JSON-LD frame test from the vendored manifest.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>A task that completes when the case has run and been asserted.</returns>
    [TestMethod]
    [JsonLdManifestData("frame")]
    public async Task RunW3cJsonLdFrameTest(JsonLdTestCase testCase)
    {
        W3cOutcome outcome = await W3cJsonLdRunner.RunAsync(testCase, TestContext.CancellationToken).ConfigureAwait(false);

        ConformanceAssertions.Apply(outcome);
    }
}
