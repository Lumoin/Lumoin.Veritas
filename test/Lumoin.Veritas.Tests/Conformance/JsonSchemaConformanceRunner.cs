using System;
using Lumoin.Veritas.JsonSchema;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Runs one JSON Schema Test Suite case against <see cref="JsonSchemaValidator"/> and reports the
/// outcome. A case passes when the validator's verdict matches the suite's expected <c>valid</c> flag.
/// </summary>
internal static class JsonSchemaConformanceRunner
{
    /// <summary>Runs one case to an outcome.</summary>
    /// <param name="testCase">The suite case.</param>
    /// <returns>The outcome (Passed / Failed).</returns>
    public static W3cOutcome Run(JsonSchemaTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        ValidationResult result;
        try
        {
            result = JsonSchemaValidator.Validate(testCase.Schema, testCase.Data);
        }
        catch(Exception exception)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Validator threw: {exception.Message}");
        }

        if(result.IsValid == testCase.ExpectedValid)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Verdict matches (valid={testCase.ExpectedValid}).");
        }

        return new W3cOutcome(
            W3cOutcomeStatus.Failed,
            $"Expected valid={testCase.ExpectedValid}, got valid={result.IsValid} ({result.Errors.Count} error(s)).");
    }
}
