using System;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.JsonSchema;

namespace Lumoin.Veritas.ParserTests.Conformance;

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

    /// <summary>The directory holding the vendored draft 2020-12 metaschema documents.</summary>
    private static string MetaschemaDirectory { get; } = Path.Combine(W3cCorpusPath.LibraryDirectory("JsonSchema"), "metaschema");

    /// <summary>Resolves a <c>$ref</c> to the suite's remote schemas (<c>http://localhost:1234/</c>) or the draft 2020-12 metaschema (<c>https://json-schema.org/draft/2020-12/</c>) against the vendored directories.</summary>
    /// <param name="absoluteUri">The absolute document URI.</param>
    /// <param name="document">On success, the parsed document.</param>
    /// <returns><see langword="true"/> when the URI names a vendored document that exists.</returns>
    internal static bool LoadRemote(string absoluteUri, out JsonNode document)
    {
        const string remotesPrefix = "http://localhost:1234/";
        const string metaschemaPrefix = "https://json-schema.org/draft/2020-12/";

        if(absoluteUri.StartsWith(remotesPrefix, StringComparison.Ordinal))
        {
            return TryLoadFile(RemotesDirectory, absoluteUri[remotesPrefix.Length..], out document);
        }

        if(absoluteUri.StartsWith(metaschemaPrefix, StringComparison.Ordinal))
        {
            //The metaschema URIs carry no file extension (".../schema", ".../meta/core").
            return TryLoadFile(MetaschemaDirectory, absoluteUri[metaschemaPrefix.Length..] + ".json", out document);
        }

        document = default;

        return false;
    }

    /// <summary>Reads and parses a vendored document under a directory, by a forward-slash relative path.</summary>
    /// <param name="directory">The base directory.</param>
    /// <param name="relative">The forward-slash relative path within it.</param>
    /// <param name="document">On success, the parsed document.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    private static bool TryLoadFile(string directory, string relative, out JsonNode document)
    {
        document = default;
        string path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        if(!File.Exists(path))
        {
            return false;
        }

        document = StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(path)));

        return true;
    }
}

