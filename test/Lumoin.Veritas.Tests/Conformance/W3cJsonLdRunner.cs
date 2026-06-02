using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.JsonLd.Document;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Runs one W3C JSON-LD API test (currently the <c>expand</c> operation) against
/// <see cref="JsonLdExpansionTree"/>: a positive test expands the input and compares the result to the expected
/// document up to JSON structural equality; a negative test expects a <see cref="JsonLdProcessingException"/>.
/// Remote <c>@context</c> URLs under the suite's <c>baseIri</c> resolve to the vendored corpus files. The
/// expander's object-graph output is converted to a <see cref="JsonNode"/> and compared to the expected document,
/// and a mismatch reports the actual-vs-expected JSON so a failure is self-diagnosing during the ratchet.
/// </summary>
internal static class W3cJsonLdRunner
{
    /// <summary>The most of each side's JSON to include in a mismatch message (kept short so the test log stays readable).</summary>
    private const int DiffPreviewLength = 600;

    /// <summary>Runs one JSON-LD test case to an outcome.</summary>
    /// <param name="testCase">The manifest-declared case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome (Passed / Failed / Skipped).</returns>
    public static async ValueTask<W3cOutcome> RunAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        //1.0-specific behaviour is out of scope (the engine targets JSON-LD 1.1).
        if(IsJsonLd10(testCase.ProcessingMode) || IsJsonLd10(testCase.SpecVersion))
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "JSON-LD 1.0 behaviour is not modelled (engine targets 1.1).");
        }

        return testCase.Operation switch
        {
            "expand" => await RunExpandAsync(testCase, cancellationToken).ConfigureAwait(false),
            _ => new W3cOutcome(W3cOutcomeStatus.Skipped, $"Operation '{testCase.Operation}' is not wired yet.")
        };
    }

    /// <summary>Runs an <c>expand</c> case: a positive test compares the expanded output, a negative test expects an error.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunExpandAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        JsonLdNode input = StjJsonLdAdapter.Parse(await ReadUtf8Async(testCase.InputPath, cancellationToken).ConfigureAwait(false));
        string baseUrl = testCase.OptionBase ?? testCase.InputUrl;
        ContextResolverDelegate resolver = CorpusResolver(testCase);

        //The expandContext option initializes the active context from a separate
        //context document before the input is expanded. Read it and hand its
        //@context value (the document is a {"@context": …} wrapper) to the expander.
        JsonLdNode? expandContext = null;
        if(testCase.ExpandContextPath is not null)
        {
            JsonLdNode expandContextDocument = StjJsonLdAdapter.Parse(await ReadUtf8Async(testCase.ExpandContextPath, cancellationToken).ConfigureAwait(false));
            expandContext = expandContextDocument.TryGetProperty("@context", out JsonLdNode contextValue)
                ? contextValue
                : expandContextDocument;
        }

        if(testCase.IsPositive)
        {
            IReadOnlyList<object?> expanded;
            try
            {
                expanded = await JsonLdExpansionTree.ExpandAsync(input, baseUrl, resolver, StjJsonLdAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);
            }
            catch(Exception exception) when(exception is not OperationCanceledException)
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expansion threw: {exception.Message}");
            }

            JsonNode? actual = ToJsonNode(expanded);
            byte[] expectedBytes = await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false);
            JsonNode? expected = JsonNode.Parse(expectedBytes);

            return JsonEquals(actual, expected)
                ? new W3cOutcome(W3cOutcomeStatus.Passed, "Expanded output matches.")
                : new W3cOutcome(W3cOutcomeStatus.Failed, $"Expanded output does not match.\n  actual:   {Preview(actual)}\n  expected: {Preview(expected)}");
        }

        //Negative test: expansion must raise a JSON-LD processing error.
        try
        {
            await JsonLdExpansionTree.ExpandAsync(input, baseUrl, resolver, StjJsonLdAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);

            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but expansion succeeded.");
        }
        catch(JsonLdProcessingException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }
    }

    /// <summary>Builds a context resolver that maps a URL under the suite's <c>baseIri</c> to the vendored corpus file.</summary>
    /// <param name="testCase">The case (carries the base IRI and corpus directory).</param>
    /// <returns>The resolver delegate.</returns>
    private static ContextResolverDelegate CorpusResolver(JsonLdTestCase testCase)
    {
        return async (uri, cancellationToken) =>
        {
            string url = uri.AbsoluteUri;
            if(url.StartsWith(testCase.BaseIri, StringComparison.Ordinal))
            {
                string relative = url[testCase.BaseIri.Length..].Replace('/', Path.DirectorySeparatorChar);
                string path = Path.Combine(testCase.CorpusDirectory, relative);
                if(File.Exists(path))
                {
                    return new Utf8String(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
                }
            }

            return null;
        };
    }

    /// <summary>Whether a processingMode/specVersion option selects JSON-LD 1.0.</summary>
    /// <param name="value">The option value, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it names 1.0.</returns>
    private static bool IsJsonLd10(string? value)
    {
        return string.Equals(value, "json-ld-1.0", StringComparison.Ordinal);
    }

    /// <summary>Reads a file as a <see cref="Utf8String"/>.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token that aborts reading.</param>
    /// <returns>The file's bytes.</returns>
    private static async ValueTask<Utf8String> ReadUtf8Async(string path, CancellationToken cancellationToken)
    {
        return new Utf8String(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>A short single-line JSON rendering of a node for a mismatch message.</summary>
    /// <param name="node">The node, or <see langword="null"/>.</param>
    /// <returns>The truncated JSON text.</returns>
    private static string Preview(JsonNode? node)
    {
        string json = node?.ToJsonString() ?? "null";

        return json.Length <= DiffPreviewLength ? json : json[..DiffPreviewLength] + "…";
    }

    /// <summary>Converts the expander's object-graph output (dictionaries / lists / scalars) to a <see cref="JsonNode"/> for comparison.</summary>
    /// <param name="value">The object-graph value.</param>
    /// <returns>The equivalent JSON node, or <see langword="null"/> for a JSON null.</returns>
    private static JsonNode? ToJsonNode(object? value)
    {
        switch(value)
        {
            case null:
            {
                return null;
            }
            case IReadOnlyDictionary<string, object?> dictionary:
            {
                JsonObject jsonObject = [];
                foreach(KeyValuePair<string, object?> entry in dictionary)
                {
                    jsonObject[entry.Key] = ToJsonNode(entry.Value);
                }

                return jsonObject;
            }
            case IReadOnlyList<object?> list:
            {
                JsonArray jsonArray = [];
                foreach(object? element in list)
                {
                    jsonArray.Add(ToJsonNode(element));
                }

                return jsonArray;
            }
            case JsonLdJsonNumber rawNumber:
            {
                //A @json literal preserves the raw number token; parse it back
                //to a JSON number node so the lexical form is compared verbatim.
                return JsonNode.Parse(rawNumber.Raw);
            }
            case string text:
            {
                return JsonValue.Create(text);
            }
            case bool boolean:
            {
                return JsonValue.Create(boolean);
            }
            case long integer:
            {
                return JsonValue.Create(integer);
            }
            case double real:
            {
                return JsonValue.Create(real);
            }
            default:
            {
                return JsonValue.Create(value.ToString());
            }
        }
    }

    /// <summary>JSON structural equality: objects compare key-insensitively, arrays order-sensitively, scalars by their lexical JSON form (so <c>5</c> and <c>5.0</c> stay distinct, as JSON-LD requires).</summary>
    /// <param name="actual">The produced node.</param>
    /// <param name="expected">The expected node.</param>
    /// <returns><see langword="true"/> when structurally equal.</returns>
    private static bool JsonEquals(JsonNode? actual, JsonNode? expected)
    {
        if(actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        switch(actual)
        {
            case JsonObject actualObject when expected is JsonObject expectedObject:
            {
                if(actualObject.Count != expectedObject.Count)
                {
                    return false;
                }

                foreach(KeyValuePair<string, JsonNode?> entry in actualObject)
                {
                    if(!expectedObject.TryGetPropertyValue(entry.Key, out JsonNode? expectedValue) || !JsonEquals(entry.Value, expectedValue))
                    {
                        return false;
                    }
                }

                return true;
            }
            case JsonArray actualArray when expected is JsonArray expectedArray:
            {
                if(actualArray.Count != expectedArray.Count)
                {
                    return false;
                }

                for(int i = 0; i < actualArray.Count; i++)
                {
                    if(!JsonEquals(actualArray[i], expectedArray[i]))
                    {
                        return false;
                    }
                }

                return true;
            }
            case JsonValue when expected is JsonValue:
            {
                return string.Equals(actual.ToJsonString(), expected.ToJsonString(), StringComparison.Ordinal);
            }
            default:
            {
                return false;
            }
        }
    }
}
