using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// expander's object-graph output (dictionaries / lists / scalars) is compared directly against the expected
/// document parsed as a <see cref="JsonLdNode"/> — System.Text.Json is banned in this project.
/// </summary>
internal static class W3cJsonLdRunner
{
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

        //The expandContext option (expand as if the document carried this @context) is not modelled yet.
        if(testCase.ExpandContextPath is not null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "The expandContext option is not modelled yet.");
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

        if(testCase.IsPositive)
        {
            IReadOnlyList<object?> expanded;
            try
            {
                expanded = await JsonLdExpansionTree.ExpandAsync(input, baseUrl, resolver, StjJsonLdAdapter.Parse, cancellationToken).ConfigureAwait(false);
            }
            catch(Exception exception) when(exception is not OperationCanceledException)
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expansion threw: {exception.Message}");
            }

            JsonLdNode expected = StjJsonLdAdapter.Parse(await ReadUtf8Async(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false));

            return JsonEquals(expanded, expected)
                ? new W3cOutcome(W3cOutcomeStatus.Passed, "Expanded output matches.")
                : new W3cOutcome(W3cOutcomeStatus.Failed, "Expanded output does not match the expected document.");
        }

        //Negative test: expansion must raise a JSON-LD processing error.
        try
        {
            await JsonLdExpansionTree.ExpandAsync(input, baseUrl, resolver, StjJsonLdAdapter.Parse, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// JSON structural equality between the expander's object-graph output and the expected document: objects
    /// compare key-insensitively, arrays order-sensitively, scalars by value (numbers by lexical form, falling
    /// back to numeric equality).
    /// </summary>
    /// <param name="actual">The produced object-graph value (dictionary / list / scalar / null).</param>
    /// <param name="expected">The expected node.</param>
    /// <returns><see langword="true"/> when structurally equal.</returns>
    private static bool JsonEquals(object? actual, JsonLdNode expected)
    {
        return expected.Kind switch
        {
            JsonLdNodeKind.Null => actual is null,
            JsonLdNodeKind.String => actual is string text && string.Equals(text, expected.GetString(), StringComparison.Ordinal),
            JsonLdNodeKind.True => actual is true,
            JsonLdNodeKind.False => actual is false,
            JsonLdNodeKind.Number => NumberEquals(actual, expected.GetRawNumber()),
            JsonLdNodeKind.Object => ObjectEquals(actual, expected),
            JsonLdNodeKind.Array => ArrayEquals(actual, expected),
            _ => false
        };
    }

    /// <summary>Compares an object-graph value against an expected object node (same key set; values recurse).</summary>
    /// <param name="actual">The produced value.</param>
    /// <param name="expected">The expected object node.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool ObjectEquals(object? actual, JsonLdNode expected)
    {
        if(actual is not IReadOnlyDictionary<string, object?> dictionary)
        {
            return false;
        }

        int expectedCount = 0;
        foreach(KeyValuePair<string, JsonLdNode> entry in expected.EnumerateObject())
        {
            expectedCount++;
            if(!dictionary.TryGetValue(entry.Key, out object? value) || !JsonEquals(value, entry.Value))
            {
                return false;
            }
        }

        return dictionary.Count == expectedCount;
    }

    /// <summary>Compares an object-graph value against an expected array node (same length; elements recurse in order).</summary>
    /// <param name="actual">The produced value.</param>
    /// <param name="expected">The expected array node.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool ArrayEquals(object? actual, JsonLdNode expected)
    {
        if(actual is not IReadOnlyList<object?> list)
        {
            return false;
        }

        int index = 0;
        foreach(JsonLdNode element in expected.EnumerateArray())
        {
            if(index >= list.Count || !JsonEquals(list[index], element))
            {
                return false;
            }

            index++;
        }

        return index == list.Count;
    }

    /// <summary>Compares a produced numeric value against an expected raw JSON number — lexically first (so <c>5</c> and <c>5.0</c> stay distinct), then by numeric value as a fallback.</summary>
    /// <param name="actual">The produced value (a <see cref="long"/> or <see cref="double"/>).</param>
    /// <param name="rawNumber">The expected number's raw lexical form.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool NumberEquals(object? actual, string rawNumber)
    {
        string? actualText = actual switch
        {
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            double real => real.ToString("R", CultureInfo.InvariantCulture),
            int small => small.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
        if(actualText is null)
        {
            return false;
        }

        return string.Equals(actualText, rawNumber, StringComparison.Ordinal)
            || (double.TryParse(actualText, NumberStyles.Float, CultureInfo.InvariantCulture, out double actualValue)
                && double.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out double expectedValue)
                && actualValue == expectedValue);
    }
}
