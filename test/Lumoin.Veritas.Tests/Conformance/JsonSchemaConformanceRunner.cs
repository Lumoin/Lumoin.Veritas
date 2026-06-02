using System;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
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
            result = JsonSchemaValidator.Validate(testCase.Schema, testCase.Data, LoadRemote);
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

    /// <summary>The directory holding the suite's shared remote schemas.</summary>
    private static string RemotesDirectory { get; } = Path.Combine(W3cCorpusPath.LibraryDirectory("JsonSchema"), "remotes");

    /// <summary>Resolves a <c>$ref</c> to the suite's remote-schema base (<c>http://localhost:1234/</c>) against the vendored <c>remotes/</c> directory.</summary>
    /// <param name="absoluteUri">The absolute document URI.</param>
    /// <param name="document">On success, the parsed remote document.</param>
    /// <returns><see langword="true"/> when the URI names a vendored remote schema that exists.</returns>
    private static bool LoadRemote(string absoluteUri, out JsonNode document)
    {
        document = default;
        const string prefix = "http://localhost:1234/";
        if(!absoluteUri.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string relative = absoluteUri[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        string path = Path.Combine(RemotesDirectory, relative);
        if(!File.Exists(path))
        {
            return false;
        }

        document = StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(path)));

        return true;
    }
}

