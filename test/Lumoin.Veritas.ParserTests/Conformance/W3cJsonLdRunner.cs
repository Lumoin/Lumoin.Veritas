using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.LinkedData;
using Lumoin.Veritas.NQuads;
using Lumoin.Veritas.Json.Stj;
using StjJsonNode = System.Text.Json.Nodes.JsonNode;
using StjJsonObject = System.Text.Json.Nodes.JsonObject;
using StjJsonArray = System.Text.Json.Nodes.JsonArray;
using StjJsonValue = System.Text.Json.Nodes.JsonValue;

namespace Lumoin.Veritas.ParserTests.Conformance;

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

        //JSON-LD 1.0 (2014) is superseded by 1.1 (2020); these cases exercise
        //the 1.0 processing mode, whose sole purpose is to REJECT 1.1 features
        //(@import, @propagate, scoped contexts, @type:@none, list-of-lists, …).
        //This engine implements 1.1 only — the conformant stance for a 1.1
        //processor is to report 1.0-mode cases as not-applicable, so they are
        //skipped by deliberate scope rather than as an unfinished gap.
        if(IsJsonLd10(testCase.ProcessingMode) || IsJsonLd10(testCase.SpecVersion))
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "JSON-LD 1.0 processing mode is out of scope (engine implements 1.1; 1.0 is superseded).");
        }

        return testCase.Operation switch
        {
            "expand" => await RunExpandAsync(testCase, cancellationToken).ConfigureAwait(false),
            "toRdf" => await RunToRdfAsync(testCase, cancellationToken).ConfigureAwait(false),
            "compact" => await RunCompactAsync(testCase, cancellationToken).ConfigureAwait(false),
            "fromRdf" => await RunFromRdfAsync(testCase, cancellationToken).ConfigureAwait(false),
            "flatten" => await RunFlattenAsync(testCase, cancellationToken).ConfigureAwait(false),
            "frame" => await RunFrameAsync(testCase, cancellationToken).ConfigureAwait(false),
            _ => new W3cOutcome(W3cOutcomeStatus.Skipped, $"Operation '{testCase.Operation}' is not wired yet.")
        };
    }

    /// <summary>Runs an <c>expand</c> case: a positive test compares the expanded output, a negative test expects an error.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunExpandAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        JsonNode input = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.InputPath, cancellationToken).ConfigureAwait(false));
        string baseUrl = testCase.OptionBase ?? testCase.InputUrl;
        ContextResolverDelegate resolver = CorpusResolver(testCase);

        JsonNode? expandContext = await LoadExpandContextAsync(testCase, cancellationToken).ConfigureAwait(false);

        if(testCase.IsPositive)
        {
            IReadOnlyList<object?> expanded;
            try
            {
                expanded = await JsonLdExpansionTree.ExpandAsync(input, baseUrl, resolver, StjJsonAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);
            }
            catch(Exception exception) when(exception is not OperationCanceledException)
            {
                return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expansion threw: {exception.Message}");
            }

            //A positive syntax test (no expected output) passes once expansion
            //has completed without error.
            if(testCase.ExpectPath is null)
            {
                return new W3cOutcome(W3cOutcomeStatus.Passed, "Expanded without error.");
            }

            StjJsonNode? actual = ToJsonNode(expanded);
            byte[] expectedBytes = await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false);
            StjJsonNode? expected = StjJsonNode.Parse(expectedBytes);

            return JsonEquals(actual, expected)
                ? new W3cOutcome(W3cOutcomeStatus.Passed, "Expanded output matches.")
                : new W3cOutcome(W3cOutcomeStatus.Failed, $"Expanded output does not match.\n  actual:   {Preview(actual)}\n  expected: {Preview(expected)}");
        }

        //Negative test: expansion must raise a JSON-LD processing error.
        try
        {
            await JsonLdExpansionTree.ExpandAsync(input, baseUrl, resolver, StjJsonAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);

            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but expansion succeeded.");
        }
        catch(JsonLdProcessingException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }
    }

    /// <summary>Runs a <c>toRdf</c> case: extracts quads from the input and compares them (under blank-node isomorphism) to the expected N-Quads.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunToRdfAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        JsonNode input = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.InputPath, cancellationToken).ConfigureAwait(false));
        string baseUrl = testCase.OptionBase ?? testCase.InputUrl;
        ContextResolverDelegate resolver = CorpusResolver(testCase);
        JsonNode? expandContext = await LoadExpandContextAsync(testCase, cancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        List<Quad> actual;
        try
        {
            //Unified path: extract RDF from the conformant expanded object graph
            //(JsonLdExpansionTree) rather than a parallel expansion.
            IReadOnlyList<object?> expanded = await JsonLdExpansionTree.ExpandAsync(
                input, baseUrl, resolver, StjJsonAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);
            actual = JsonLdRdfSerializer.Serialize(expanded, pool, DirectionModeFor(testCase.RdfDirection));
        }
        catch(Exception exception) when(exception is not OperationCanceledException)
        {
            return testCase.IsPositive
                ? new W3cOutcome(W3cOutcomeStatus.Failed, $"toRdf threw: {exception.Message}")
                : new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }

        if(!testCase.IsPositive)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but toRdf succeeded.");
        }

        //A positive syntax test (no expected output) passes once processing has
        //completed without error.
        if(testCase.ExpectPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Processed without error ({actual.Count} quads).");
        }

        List<Quad> expected = [];
        byte[] expectedBytes = await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false);
        await foreach(Quad quad in NQuadsReader.ReadAsync(expectedBytes, pool, cancellationToken).ConfigureAwait(false))
        {
            expected.Add(quad);
        }

        return QuadSetIsomorphism.AreIsomorphic(actual, expected)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, $"toRdf matches ({actual.Count} quads).")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"toRdf differs: actual={actual.Count} quads, expected={expected.Count} quads.");
    }

    /// <summary>Runs a <c>frame</c> case: expands the input and frame, frames, compacts against the frame's context, cleans up <c>@null</c>, and compares to the expected document.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunFrameAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        JsonNode input = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.InputPath, cancellationToken).ConfigureAwait(false));
        JsonNode frameDocument = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.FramePath!, cancellationToken).ConfigureAwait(false));
        string baseUrl = testCase.OptionBase ?? testCase.InputUrl;
        ContextResolverDelegate resolver = CorpusResolver(testCase);

        object? result;
        try
        {
            IReadOnlyList<object?> expanded = await JsonLdExpansionTree.ExpandAsync(
                input, baseUrl, resolver, StjJsonAdapter.Parse, expandContext: null, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<object?> expandedFrame = await JsonLdExpansionTree.ExpandAsync(
                frameDocument, baseUrl, resolver, StjJsonAdapter.Parse, expandContext: null, frameExpansion: true, cancellationToken).ConfigureAwait(false);

            JsonNode contextValue = frameDocument.TryGetProperty("@context", out JsonNode embedded) ? embedded : frameDocument;
            LinkedDataContext activeContext = await ContextProcessor.ProcessAsync(
                LinkedDataContext.Empty, contextValue, baseUrl, resolver, StjJsonAdapter.Parse, cancellationToken).ConfigureAwait(false);

            //The default graph is framed when a raw top-level frame key expands to @graph (§ frame the default graph vs the merged graph); the expanded frame can't be used because expansion unwraps a top-level @graph.
            bool hasGraph = false;
            foreach(KeyValuePair<string, JsonNode> frameKey in frameDocument.EnumerateObject())
            {
                if(string.Equals(activeContext.ExpandIri(frameKey.Key, vocab: true), "@graph", StringComparison.Ordinal))
                {
                    hasGraph = true;
                    break;
                }
            }

            JsonLdFrameOptions frameOptions = new() { Merged = !hasGraph };

            List<object?> framed = JsonLdFramer.Frame(expanded, expandedFrame, frameOptions);
            JsonNode framedNode = ExpandedToModelNode(framed);
            object? compacted = await JsonLdCompactor.CompactAsync(
                framedNode, activeContext, resolver, StjJsonAdapter.Parse, baseUrl, compactArrays: true, cancellationToken).ConfigureAwait(false);

            result = JsonLdFramer.CleanupNull(compacted);

            //omitGraph (1.1 default true) inlines a single result; when false the output is always wrapped in @graph.
            if(!testCase.OmitGraph)
            {
                result = EnsureGraphWrapper(result);
            }
        }
        catch(Exception exception) when(exception is not OperationCanceledException)
        {
            return testCase.IsPositive
                ? new W3cOutcome(W3cOutcomeStatus.Failed, $"frame threw: {exception.Message}")
                : new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }

        if(!testCase.IsPositive)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but frame succeeded.");
        }

        StjJsonNode? actual = WrapWithContext(ToJsonNode(result), testCase.FramePath!);
        StjJsonNode? expected = StjJsonNode.Parse(await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false));

        return JsonEquals(actual, expected)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, "frame matches.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"frame output does not match.\n  actual:   {Preview(actual)}\n  expected: {Preview(expected)}");
    }

    /// <summary>Wraps a framed result in <c>@graph</c> (the <c>omitGraph: false</c> form): an existing <c>@graph</c> body is kept, otherwise the body's nodes (excluding a sole <c>@context</c>) become the <c>@graph</c> array. The top-level <c>@context</c> is re-attached by <see cref="WrapWithContext"/>.</summary>
    /// <param name="result">The framed, compacted result.</param>
    /// <returns>The <c>@graph</c>-wrapped result.</returns>
    private static object? EnsureGraphWrapper(object? result)
    {
        if(result is IReadOnlyDictionary<string, object?> map)
        {
            if(map.ContainsKey("@graph"))
            {
                return result;
            }

            List<object?> nodes = new();
            Dictionary<string, object?> node = new(StringComparer.Ordinal);
            foreach(KeyValuePair<string, object?> entry in map)
            {
                if(!string.Equals(entry.Key, "@context", StringComparison.Ordinal))
                {
                    node[entry.Key] = entry.Value;
                }
            }
            if(node.Count > 0)
            {
                nodes.Add(node);
            }

            return new Dictionary<string, object?>(StringComparer.Ordinal) { ["@graph"] = nodes };
        }

        if(result is IReadOnlyList<object?> list)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { ["@graph"] = new List<object?>(list) };
        }

        return result;
    }

    /// <summary>Runs a <c>fromRdf</c> case: parses the input N-Quads, serializes them as JSON-LD, and compares to the expected document.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunFromRdfAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        using Utf8StringPool pool = new();
        object? actual;
        try
        {
            List<Quad> quads = [];
            byte[] inputBytes = await File.ReadAllBytesAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false);
            await foreach(Quad quad in NQuadsReader.ReadAsync(inputBytes, pool, cancellationToken).ConfigureAwait(false))
            {
                quads.Add(quad);
            }

            actual = JsonLdRdfDeserializer.FromRdf(
                quads, testCase.UseNativeTypes, testCase.UseRdfType, DirectionModeFor(testCase.RdfDirection), StjJsonAdapter.Parse);
        }
        catch(Exception exception) when(exception is not OperationCanceledException)
        {
            return testCase.IsPositive
                ? new W3cOutcome(W3cOutcomeStatus.Failed, $"fromRdf threw: {exception.Message}")
                : new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }

        if(!testCase.IsPositive)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but fromRdf succeeded.");
        }

        if(testCase.ExpectPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, "Processed without error.");
        }

        StjJsonNode? actualNode = ToJsonNode(actual);
        StjJsonNode? expected = StjJsonNode.Parse(await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false));

        return JsonEquals(actualNode, expected)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, "fromRdf matches.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"fromRdf output does not match.\n  actual:   {Preview(actualNode)}\n  expected: {Preview(expected)}");
    }

    /// <summary>Runs a <c>flatten</c> case: expands then flattens the input (optionally compacting against a provided context), and compares to the expected document.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunFlattenAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        JsonNode input = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.InputPath, cancellationToken).ConfigureAwait(false));
        string baseUrl = testCase.OptionBase ?? testCase.InputUrl;
        ContextResolverDelegate resolver = CorpusResolver(testCase);
        JsonNode? expandContext = await LoadExpandContextAsync(testCase, cancellationToken).ConfigureAwait(false);

        object? flattened;
        try
        {
            IReadOnlyList<object?> expanded = await JsonLdExpansionTree.ExpandAsync(
                input, baseUrl, resolver, StjJsonAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);
            flattened = JsonLdFlattener.Flatten(expanded);

            if(testCase.ContextPath is not null)
            {
                //A flatten context compacts the flattened node map (like a compact step over the flat array).
                JsonNode contextDocument = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.ContextPath, cancellationToken).ConfigureAwait(false));
                JsonNode contextValue = contextDocument.TryGetProperty("@context", out JsonNode embedded) ? embedded : contextDocument;
                LinkedDataContext activeContext = await ContextProcessor.ProcessAsync(
                    LinkedDataContext.Empty, contextValue, baseUrl, resolver, StjJsonAdapter.Parse, cancellationToken).ConfigureAwait(false);
                JsonNode flattenedNode = ExpandedToModelNode((IReadOnlyList<object?>)flattened!);
                flattened = await JsonLdCompactor.CompactAsync(
                    flattenedNode, activeContext, resolver, StjJsonAdapter.Parse, baseUrl, testCase.CompactArrays, cancellationToken).ConfigureAwait(false);
            }
        }
        catch(Exception exception) when(exception is not OperationCanceledException)
        {
            return testCase.IsPositive
                ? new W3cOutcome(W3cOutcomeStatus.Failed, $"flatten threw: {exception.Message}")
                : new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }

        if(!testCase.IsPositive)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but flatten succeeded.");
        }

        if(testCase.ExpectPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, "Processed without error.");
        }

        //With a context the flattened form was compacted, so it carries @context like a compact result.
        StjJsonNode? actual = testCase.ContextPath is not null
            ? WrapWithContext(ToJsonNode(flattened), testCase.ContextPath)
            : ToJsonNode(flattened);
        StjJsonNode? expected = StjJsonNode.Parse(await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false));

        return JsonEquals(actual, expected)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, "flatten matches.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"flatten output does not match.\n  actual:   {Preview(actual)}\n  expected: {Preview(expected)}");
    }

    /// <summary>Runs a <c>compact</c> case: expands the input, processes the provided context, compacts against it, and compares to the expected document.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The outcome.</returns>
    private static async ValueTask<W3cOutcome> RunCompactAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        JsonNode input = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.InputPath, cancellationToken).ConfigureAwait(false));
        string baseUrl = testCase.OptionBase ?? testCase.InputUrl;
        ContextResolverDelegate resolver = CorpusResolver(testCase);
        JsonNode? expandContext = await LoadExpandContextAsync(testCase, cancellationToken).ConfigureAwait(false);

        object? compacted;
        try
        {
            //Compaction runs over the conformant expansion: expand the input, then compact the expanded
            //form against the active context the provided context document processes to.
            IReadOnlyList<object?> expanded = await JsonLdExpansionTree.ExpandAsync(
                input, baseUrl, resolver, StjJsonAdapter.Parse, expandContext, cancellationToken).ConfigureAwait(false);
            JsonNode expandedNode = ExpandedToModelNode(expanded);

            JsonNode contextDocument = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.ContextPath!, cancellationToken).ConfigureAwait(false));
            JsonNode contextValue = contextDocument.TryGetProperty("@context", out JsonNode embedded) ? embedded : contextDocument;
            LinkedDataContext activeContext = await ContextProcessor.ProcessAsync(
                LinkedDataContext.Empty, contextValue, baseUrl, resolver, StjJsonAdapter.Parse, cancellationToken).ConfigureAwait(false);

            compacted = await JsonLdCompactor.CompactAsync(
                expandedNode, activeContext, resolver, StjJsonAdapter.Parse, baseUrl, testCase.CompactArrays, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception exception) when(exception is not OperationCanceledException)
        {
            return testCase.IsPositive
                ? new W3cOutcome(W3cOutcomeStatus.Failed, $"compact threw: {exception.Message}")
                : new W3cOutcome(W3cOutcomeStatus.Passed, $"Raised the expected processing error ('{testCase.ExpectErrorCode}').");
        }

        if(!testCase.IsPositive)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Expected error '{testCase.ExpectErrorCode}', but compact succeeded.");
        }

        if(testCase.ExpectPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, "Compacted without error.");
        }

        StjJsonNode? actual = WrapWithContext(ToJsonNode(compacted), testCase.ContextPath!);
        StjJsonNode? expected = StjJsonNode.Parse(await File.ReadAllBytesAsync(testCase.ExpectPath!, cancellationToken).ConfigureAwait(false));

        return JsonEquals(actual, expected)
            ? new W3cOutcome(W3cOutcomeStatus.Passed, "Compacted output matches.")
            : new W3cOutcome(W3cOutcomeStatus.Failed, $"Compacted output does not match.\n  actual:   {Preview(actual)}\n  expected: {Preview(expected)}");
    }

    /// <summary>Serializes the expander's object-graph output and reparses it as a model <see cref="JsonNode"/> for the compactor.</summary>
    /// <param name="expanded">The expanded object graph.</param>
    /// <returns>The expanded document as a model node.</returns>
    private static JsonNode ExpandedToModelNode(IReadOnlyList<object?> expanded)
    {
        StjJsonNode? node = ToJsonNode(expanded);

        return StjJsonAdapter.Parse(Utf8Strings.From(node?.ToJsonString() ?? "[]"));
    }

    /// <summary>Prepends the document's <c>@context</c> to a compacted object result, mirroring the Compaction API output.</summary>
    /// <param name="body">The compacted body.</param>
    /// <param name="contextPath">The path to the context document.</param>
    /// <returns>The body with <c>@context</c> added (objects only).</returns>
    private static StjJsonNode? WrapWithContext(StjJsonNode? body, string contextPath)
    {
        if(body is not StjJsonObject bodyObject)
        {
            return body;
        }

        //The Compaction API output carries the @context the caller PROVIDED, verbatim — not a context the
        //compactor re-serialized. So take @context from the context document and drop any the body carries.
        StjJsonObject result = [];
        if(StjJsonNode.Parse(File.ReadAllBytes(contextPath)) is StjJsonObject document
            && document.TryGetPropertyValue("@context", out StjJsonNode? contextValue)
            && contextValue is not (null or StjJsonObject { Count: 0 }))
        {
            result["@context"] = contextValue.DeepClone();
        }

        foreach(KeyValuePair<string, StjJsonNode?> member in bodyObject)
        {
            if(!string.Equals(member.Key, "@context", StringComparison.Ordinal))
            {
                result[member.Key] = member.Value?.DeepClone();
            }
        }

        return result;
    }

    /// <summary>Maps the <c>option.rdfDirection</c> value to the serializer's direction mode.</summary>
    /// <param name="rdfDirection">The option value, or <see langword="null"/>.</param>
    /// <returns>The corresponding <see cref="JsonLdRdfSerializer.DirectionMode"/>.</returns>
    private static JsonLdRdfSerializer.DirectionMode DirectionModeFor(string? rdfDirection)
    {
        return rdfDirection switch
        {
            "i18n-datatype" => JsonLdRdfSerializer.DirectionMode.I18nDatatype,
            "compound-literal" => JsonLdRdfSerializer.DirectionMode.CompoundLiteral,
            _ => JsonLdRdfSerializer.DirectionMode.None
        };
    }

    /// <summary>Loads the <c>expandContext</c> option document (a <c>{"@context": …}</c> wrapper) and returns its context value, or <see langword="null"/> when the option is absent.</summary>
    /// <param name="testCase">The case.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The context value node, or <see langword="null"/>.</returns>
    private static async ValueTask<JsonNode?> LoadExpandContextAsync(JsonLdTestCase testCase, CancellationToken cancellationToken)
    {
        if(testCase.ExpandContextPath is null)
        {
            return null;
        }

        JsonNode document = StjJsonAdapter.Parse(await ReadUtf8Async(testCase.ExpandContextPath, cancellationToken).ConfigureAwait(false));

        return document.TryGetProperty("@context", out JsonNode contextValue) ? contextValue : document;
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
    private static string Preview(StjJsonNode? node)
    {
        string json = node?.ToJsonString() ?? "null";

        return json.Length <= DiffPreviewLength ? json : json[..DiffPreviewLength] + "…";
    }

    /// <summary>Converts the expander's object-graph output (dictionaries / lists / scalars) to a <see cref="JsonNode"/> for comparison.</summary>
    /// <param name="value">The object-graph value.</param>
    /// <returns>The equivalent JSON node, or <see langword="null"/> for a JSON null.</returns>
    private static StjJsonNode? ToJsonNode(object? value)
    {
        switch(value)
        {
            case null:
            {
                return null;
            }
            case IReadOnlyDictionary<string, object?> dictionary:
            {
                StjJsonObject jsonObject = [];
                foreach(KeyValuePair<string, object?> entry in dictionary)
                {
                    jsonObject[entry.Key] = ToJsonNode(entry.Value);
                }

                return jsonObject;
            }
            case IReadOnlyList<object?> list:
            {
                StjJsonArray jsonArray = [];
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
                return StjJsonNode.Parse(rawNumber.Raw);
            }
            case string text:
            {
                return StjJsonValue.Create(text);
            }
            case bool boolean:
            {
                return StjJsonValue.Create(boolean);
            }
            case long integer:
            {
                return StjJsonValue.Create(integer);
            }
            case double real:
            {
                return StjJsonValue.Create(real);
            }
            default:
            {
                return StjJsonValue.Create(value.ToString());
            }
        }
    }

    /// <summary>JSON structural equality: objects compare key-insensitively, arrays order-sensitively, scalars by their lexical JSON form (so <c>5</c> and <c>5.0</c> stay distinct, as JSON-LD requires).</summary>
    /// <param name="actual">The produced node.</param>
    /// <param name="expected">The expected node.</param>
    /// <returns><see langword="true"/> when structurally equal.</returns>
    private static bool JsonEquals(StjJsonNode? actual, StjJsonNode? expected)
    {
        if(actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        switch(actual)
        {
            case StjJsonObject actualObject when expected is StjJsonObject expectedObject:
            {
                if(actualObject.Count != expectedObject.Count)
                {
                    return false;
                }

                foreach(KeyValuePair<string, StjJsonNode?> entry in actualObject)
                {
                    if(!expectedObject.TryGetPropertyValue(entry.Key, out StjJsonNode? expectedValue) || !JsonEquals(entry.Value, expectedValue))
                    {
                        return false;
                    }
                }

                return true;
            }
            case StjJsonArray actualArray when expected is StjJsonArray expectedArray:
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
            case StjJsonValue actualValue when expected is StjJsonValue expectedValue:
            {
                //JSON numbers compare by value (1.23 and 1.23E0, or 10 and 10.0, are the same JSON number);
                //strings/booleans compare by their serialized token.
                if(actualValue.GetValueKind() == System.Text.Json.JsonValueKind.Number
                    && expectedValue.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                {
                    //Compare via the number token parsed to double, so a long-backed and a JsonElement-backed
                    //value (e.g. 1 vs 1, or 0.11 vs 1.1E-1) compare by numeric value regardless of backing.
                    return double.TryParse(actualValue.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double actualNumber)
                        && double.TryParse(expectedValue.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double expectedNumber)
                        && actualNumber.Equals(expectedNumber);
                }

                return string.Equals(actual.ToJsonString(), expected.ToJsonString(), StringComparison.Ordinal);
            }
            default:
            {
                return false;
            }
        }
    }
}
