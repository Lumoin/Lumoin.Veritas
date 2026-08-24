using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.JsonSchema;

/// <summary>
/// Runs the vendored JSON Schema Test Suite (draft 2020-12) against
/// <see cref="Lumoin.Veritas.JsonSchema.JsonSchemaValidator"/>.
/// </summary>
/// <remarks>
/// This is a RED-baseline + ratchet gate: a case where the validator's verdict differs from the
/// suite's expected <c>valid</c> flag is a real <see cref="W3cOutcomeStatus.Failed"/>, so the failing
/// count is the honest distance to full draft 2020-12 conformance and may only go down as keywords are
/// implemented. Unimplemented keywords are ignored (annotations), so they surface as over-acceptance
/// failures rather than silent passes.
/// </remarks>
[TestClass]
internal sealed class JsonSchemaTestSuiteTests
{
    /// <summary>Runs one draft 2020-12 suite case.</summary>
    /// <param name="testCase">The case supplied by the suite loader.</param>
    [TestMethod]
    [JsonSchemaTestSuiteData]
    public void RunDraft202012(JsonSchemaTestCase testCase)
    {
        ConformanceAssertions.Apply(JsonSchemaConformanceRunner.Run(testCase));
    }
}
