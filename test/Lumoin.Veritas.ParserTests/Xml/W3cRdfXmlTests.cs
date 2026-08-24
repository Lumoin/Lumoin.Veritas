// W3C RDF/XML Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/Rdf/rdf-xml/ and .../rdf-xml-12/.
// See Material/Rdf/ATTRIBUTION.md for provenance.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Runs the vendored W3C RDF/XML conformance suites against <see cref="Lumoin.Veritas.Xml.RdfXmlReader"/>:
/// an <c>rdft:TestXMLEval</c> entry passes when the parsed quads are isomorphic to the expected N-Triples,
/// an <c>rdft:TestXMLNegativeSyntax</c> entry passes when the reader rejects the document.
/// </summary>
/// <remarks>
/// <para>
/// The reader resolves relative references against a document base IRI. The W3C suites define that base
/// through each manifest's <c>mf:assumedTestBase</c> HTTP IRI composed with the fixture's path, not the
/// fixture's on-disk <c>file://</c> URL, so the suite computes the base per test and passes it through a
/// closure to <see cref="RdfXmlConformanceReader"/> (the shared <see cref="W3cTestRunner.InputReader"/>
/// delegate carries no base).
/// </para>
/// <para>
/// Tests this build cannot yet satisfy are recorded in <see cref="KnownGaps"/> and reported as
/// inconclusive so the suite stays green while the gaps are ratcheted down; a gap that starts passing
/// fails the run so its entry is removed.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cRdfXmlTests
{
    private const string Rdf11AssumedBase = "https://w3c.github.io/rdf-tests/rdf/rdf11/rdf-xml/";
    private const string Rdf12AssumedBase = "https://w3c.github.io/rdf-tests/rdf/rdf12/rdf-xml/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Tests this build cannot yet satisfy, keyed by manifest <c>mf:name</c> with the reason. They are
    /// reported as inconclusive rather than failing the suite; if any starts passing, the run fails so the
    /// entry is removed and the ratchet advances.
    /// </summary>
    private static Dictionary<string, string> KnownGaps { get; } = new(StringComparer.Ordinal)
    {
    };

    /// <summary>Runs one RDF 1.1 RDF/XML conformance test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Rdf", "rdf-xml")]
    public Task RunW3cRdfXml11Test(W3cTestCase testCase) => RunAndAssertAsync(testCase, "rdf-xml", Rdf11AssumedBase);

    /// <summary>Runs one RDF 1.2 RDF/XML conformance test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Rdf", "rdf-xml-12")]
    public Task RunW3cRdfXml12Test(W3cTestCase testCase) => RunAndAssertAsync(testCase, "rdf-xml-12", Rdf12AssumedBase);

    /// <summary>
    /// Runs a test case with the per-test base IRI and applies the outcome, translating a known gap into
    /// an inconclusive result (and failing if a known gap unexpectedly starts passing).
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <param name="suiteFolder">The suite subfolder under <c>Material/Rdf</c>.</param>
    /// <param name="assumedBase">The suite's <c>mf:assumedTestBase</c> HTTP IRI.</param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase, string suiteFolder, string assumedBase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        string baseIri = ComposeBaseIri(testCase, suiteFolder, assumedBase);
        W3cOutcome outcome = await W3cTestRunner.RunAsync(
            testCase,
            (input, ct) => RdfXmlConformanceReader.ReadAsync(input, baseIri, ct),
            TestContext.CancellationToken).ConfigureAwait(false);

        if(KnownGaps.TryGetValue(testCase.Name, out string? reason))
        {
            if(outcome.Status == W3cOutcomeStatus.Passed)
            {
                Assert.Fail($"'{testCase.Name}' is recorded as a known gap ({reason}) but now passes; remove it from KnownGaps.");
            }

            Assert.Inconclusive($"Known gap ({reason}): {outcome.Message}");
            return;
        }

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>
    /// Composes a fixture's document base IRI as the suite's <c>mf:assumedTestBase</c> plus the fixture's
    /// path relative to the suite root, so a fixture nested in a sub-folder (RDF 1.1) or the RDF 1.2
    /// <c>eval/</c> directory resolves to the IRI the expected file was authored against.
    /// </summary>
    /// <param name="testCase">The test case whose input fixture is being read.</param>
    /// <param name="suiteFolder">The suite subfolder under <c>Material/Rdf</c>.</param>
    /// <param name="assumedBase">The suite's <c>mf:assumedTestBase</c> HTTP IRI.</param>
    /// <returns>The composed base IRI.</returns>
    private static string ComposeBaseIri(W3cTestCase testCase, string suiteFolder, string assumedBase)
    {
        string suiteRoot = Path.GetDirectoryName(W3cCorpusPath.For("Rdf", suiteFolder, "manifest.ttl"))!;
        string relative = Path.GetRelativePath(suiteRoot, testCase.InputPath).Replace('\\', '/');
        return assumedBase + relative;
    }
}
