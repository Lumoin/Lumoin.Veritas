using System;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.JsonLd;
using Lumoin.Veritas.Json.Stj;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Loads a vendored W3C JSON-LD API test manifest (a <c>.jsonld</c> document — JSON with a top-level
/// <c>baseIri</c> and a <c>sequence</c> of entries) into <see cref="JsonLdTestCase"/> rows. The JSON-LD manifests
/// are not Turtle, so this is a separate loader from <see cref="W3cManifestLoader"/>; it reads the manifest
/// through the JSON-LD library's own <see cref="JsonNode"/> model and resolves each entry's file references
/// against the manifest's directory.
/// </summary>
internal static class JsonLdManifestLoader
{
    /// <summary>Loads the manifest for one operation (<c>expand</c> / <c>compact</c> / <c>toRdf</c>).</summary>
    /// <param name="operation">The operation, naming both the manifest (<c>{operation}-manifest.jsonld</c>) and the test type.</param>
    /// <returns>One case per <c>sequence</c> entry.</returns>
    public static IReadOnlyList<JsonLdTestCase> Load(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        string corpusDirectory = W3cCorpusPath.LibraryDirectory("JsonLd");
        string manifestPath = Path.Combine(corpusDirectory, $"{operation}-manifest.jsonld");
        JsonNode root = StjJsonAdapter.Parse(new Utf8String(File.ReadAllBytes(manifestPath)));

        string baseIri = root.TryGetProperty("baseIri", out JsonNode baseNode) ? baseNode.GetString() : string.Empty;
        List<JsonLdTestCase> cases = [];
        if(root.TryGetProperty("sequence", out JsonNode sequence))
        {
            foreach(JsonNode entry in sequence.EnumerateArray())
            {
                cases.Add(BuildCase(entry, operation, baseIri, corpusDirectory));
            }
        }

        return cases;
    }

    /// <summary>Builds one case from a manifest <c>sequence</c> entry.</summary>
    /// <param name="entry">The entry node.</param>
    /// <param name="operation">The operation under test.</param>
    /// <param name="baseIri">The manifest's retrieval URL space.</param>
    /// <param name="corpusDirectory">The absolute corpus directory.</param>
    /// <returns>The test case.</returns>
    private static JsonLdTestCase BuildCase(JsonNode entry, string operation, string baseIri, string corpusDirectory)
    {
        string id = StringOrNull(entry, "@id") ?? string.Empty;
        string name = StringOrNull(entry, "name") ?? id;
        //A PositiveSyntaxTest carries no expected output: it passes when the
        //input is processed without error (the runner detects "positive but no
        //expect file" as a syntax test).
        bool isPositive = TypeContains(entry, "jld:PositiveEvaluationTest") || TypeContains(entry, "jld:PositiveSyntaxTest");
        string input = StringOrNull(entry, "input") ?? string.Empty;
        string? expect = StringOrNull(entry, "expect");
        string? context = StringOrNull(entry, "context");
        string? frame = StringOrNull(entry, "frame");
        string? expectErrorCode = StringOrNull(entry, "expectErrorCode");

        string? optionBase = null;
        string? expandContext = null;
        string? processingMode = null;
        string? specVersion = null;
        string? rdfDirection = null;
        bool compactArrays = true;
        bool useNativeTypes = false;
        bool useRdfType = false;
        bool omitGraph = true;
        if(entry.TryGetProperty("option", out JsonNode option) && option.Kind == JsonNodeKind.Object)
        {
            optionBase = StringOrNull(option, "base");
            expandContext = StringOrNull(option, "expandContext");
            processingMode = StringOrNull(option, "processingMode");
            specVersion = StringOrNull(option, "specVersion");
            rdfDirection = StringOrNull(option, "rdfDirection");
            if(option.TryGetProperty("compactArrays", out JsonNode compactArraysNode) && compactArraysNode.Kind == JsonNodeKind.False)
            {
                compactArrays = false;
            }
            if(option.TryGetProperty("useNativeTypes", out JsonNode useNativeTypesNode) && useNativeTypesNode.Kind == JsonNodeKind.True)
            {
                useNativeTypes = true;
            }
            if(option.TryGetProperty("useRdfType", out JsonNode useRdfTypeNode) && useRdfTypeNode.Kind == JsonNodeKind.True)
            {
                useRdfType = true;
            }
            if(option.TryGetProperty("omitGraph", out JsonNode omitGraphNode) && omitGraphNode.Kind == JsonNodeKind.False)
            {
                omitGraph = false;
            }
        }

        return new JsonLdTestCase(
            Id: id,
            Name: name,
            Operation: operation,
            IsPositive: isPositive,
            BaseIri: baseIri,
            InputPath: Resolve(corpusDirectory, input),
            InputUrl: baseIri + input,
            ExpectPath: expect is null ? null : Resolve(corpusDirectory, expect),
            ContextPath: context is null ? null : Resolve(corpusDirectory, context),
            ExpectErrorCode: expectErrorCode,
            OptionBase: optionBase,
            ExpandContextPath: expandContext is null ? null : Resolve(corpusDirectory, expandContext),
            ProcessingMode: processingMode,
            SpecVersion: specVersion,
            RdfDirection: rdfDirection,
            CompactArrays: compactArrays,
            UseNativeTypes: useNativeTypes,
            UseRdfType: useRdfType,
            FramePath: frame is null ? null : Resolve(corpusDirectory, frame),
            OmitGraph: omitGraph,
            CorpusDirectory: corpusDirectory);
    }

    /// <summary>Whether an entry's <c>@type</c> (a string or an array of strings) contains the given type name.</summary>
    /// <param name="entry">The entry node.</param>
    /// <param name="typeName">The type name to look for.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool TypeContains(JsonNode entry, string typeName)
    {
        if(!entry.TryGetProperty("@type", out JsonNode type))
        {
            return false;
        }

        if(type.Kind == JsonNodeKind.String)
        {
            return string.Equals(type.GetString(), typeName, StringComparison.Ordinal);
        }

        if(type.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode element in type.EnumerateArray())
            {
                if(element.Kind == JsonNodeKind.String && string.Equals(element.GetString(), typeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Reads a string property, or <see langword="null"/> when absent or not a string.</summary>
    /// <param name="node">The containing object node.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The string value, or <see langword="null"/>.</returns>
    private static string? StringOrNull(JsonNode node, string property)
    {
        return node.TryGetProperty(property, out JsonNode value) && value.Kind == JsonNodeKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Resolves a manifest-relative file reference to an absolute path under the corpus directory.</summary>
    /// <param name="corpusDirectory">The absolute corpus directory.</param>
    /// <param name="relative">The manifest-relative reference (forward-slash separated).</param>
    /// <returns>The absolute path.</returns>
    private static string Resolve(string corpusDirectory, string relative)
    {
        return Path.Combine(corpusDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
