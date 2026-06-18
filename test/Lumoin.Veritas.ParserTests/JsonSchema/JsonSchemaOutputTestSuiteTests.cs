using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.JsonSchema;

/// <summary>
/// Runs the JSON Schema output-tests suite (draft 2020-12): the validation output this engine produces,
/// in each format, must itself validate against the suite's output-constraint schema.
/// </summary>
[TestClass]
internal sealed class JsonSchemaOutputTestSuiteTests
{
    /// <summary>Runs one output-test case.</summary>
    /// <param name="testCase">The case supplied by the loader.</param>
    [TestMethod]
    [JsonSchemaOutputTestSuiteData]
    public void ProducesConformantOutput(JsonSchemaOutputTestCase testCase)
    {
        ConformanceAssertions.Apply(JsonSchemaOutputRunner.Run(testCase));
    }
}
