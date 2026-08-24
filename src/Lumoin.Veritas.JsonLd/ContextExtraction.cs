using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Walks a <see cref="JsonNode"/> representing an <c>@context</c>
/// value and produces a fully-extracted
/// <see cref="IReadOnlyList{T}"/> of <see cref="LinkedDataContextEntry"/>
/// suitable for the format-agnostic
/// <c>Lumoin.Veritas.LinkedData.ContextProcessing</c> core.
/// </summary>
/// <remarks>
/// <para>
/// The extractor performs the JSON-side reading once at the boundary;
/// downstream context-processing methods operate purely on POCOs and do
/// not need to inspect the original <see cref="JsonNode"/> tree.
/// Scoped contexts inside term definitions are recursively extracted in
/// the same pass, so the produced tree carries the full nested context
/// structure.
/// </para>
/// <para>
/// Recursion is implemented iteratively via an explicit
/// <see cref="Stack{T}"/>; no method-call recursion in the walker.
/// </para>
/// </remarks>
public static class ContextExtraction
{
    /// <summary>
    /// Extracts the entries of a single <c>@context</c> value (which may
    /// be a string URL, a null, an inline object, or an array of any of
    /// the preceding).
    /// </summary>
    /// <param name="contextNode">The <c>@context</c> value as a <see cref="JsonNode"/>.</param>
    /// <param name="baseUrl">The base URL in effect at the point this context appears.</param>
    /// <param name="keyCounter">Cross-call counter used to assign unique synthetic keys.</param>
    /// <returns>The extracted entries in document order.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static IReadOnlyList<LinkedDataContextEntry> ExtractEntries(
        JsonNode contextNode,
        string? baseUrl,
        ref int keyCounter)
    {
        List<LinkedDataContextEntry> entries = [];
        ExtractInto(entries, contextNode, baseUrl, ref keyCounter, depth: 0);
        return entries;
    }

    private static void ExtractInto(
        List<LinkedDataContextEntry> entries,
        JsonNode contextNode,
        string? baseUrl,
        ref int keyCounter,
        int depth)
    {
        switch(contextNode.Kind)
        {
            case JsonNodeKind.Null:
            {
                entries.Add(new LinkedDataContextEntry(MakeKey(ref keyCounter, depth, "reset")));
                break;
            }
            case JsonNodeKind.String:
            {
                string url = contextNode.GetString();
                entries.Add(new LinkedDataContextEntry(url, baseUrl, MakeKey(ref keyCounter, depth, "url")));
                break;
            }
            case JsonNodeKind.Array:
            {
                foreach(JsonNode element in contextNode.EnumerateArray())
                {
                    ExtractInto(entries, element, baseUrl, ref keyCounter, depth);
                }
                break;
            }
            case JsonNodeKind.Object:
            {
                entries.Add(ExtractInlineEntry(contextNode, baseUrl, ref keyCounter, depth));
                break;
            }
            default:
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidLocalContext,
                    string.Create(CultureInfo.InvariantCulture, $"@context must be a string, object, array, or null. Got '{contextNode.Kind}'."));
            }
        }
    }

    private static LinkedDataContextEntry ExtractInlineEntry(
        JsonNode objectNode,
        string? baseUrl,
        ref int keyCounter,
        int depth)
    {
        //Keyword keys are context-level directives, read and validated
        //directly below; every non-keyword key is a term definition. Only
        //JSON-LD 1.1 is modelled, so an @version present must be the number 1.1.
        if(objectNode.TryGetProperty(JsonLdKeywords.Version, out JsonNode versionNode)
            && (versionNode.Kind != JsonNodeKind.Number
                || !string.Equals(versionNode.GetRawNumber(), "1.1", StringComparison.Ordinal)))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidVersionValue,
                "@version must be the number 1.1.");
        }

        Dictionary<string, LinkedDataTermSource> terms = [];
        foreach(KeyValuePair<string, JsonNode> entry in objectNode.EnumerateObject())
        {
            //Context-level directives are read declaratively below; every other
            //key — including keyword-shaped keys such as @type or @context — is a
            //term definition (where keyword-redefinition is detected and rejected).
            if(JsonLdKeywords.IsContextDirective(entry.Key))
            {
                continue;
            }

            terms[entry.Key] = ExtractTermSource(entry.Key, entry.Value, baseUrl, depth + 1, ref keyCounter);
        }

        return new LinkedDataContextEntry(terms, baseUrl, MakeKey(ref keyCounter, depth, "inline"))
        {
            Base = objectNode.TryGetProperty(JsonLdKeywords.Base, out JsonNode baseNode)
                ? RequireStringOrNull(baseNode, JsonLdErrorCode.InvalidBaseIri, JsonLdKeywords.Base)
                : null,
            HasBase = objectNode.TryGetProperty(JsonLdKeywords.Base, out _),
            Vocab = objectNode.TryGetProperty(JsonLdKeywords.Vocab, out JsonNode vocabNode)
                ? RequireStringOrNull(vocabNode, JsonLdErrorCode.InvalidVocabMapping, JsonLdKeywords.Vocab)
                : null,
            HasVocab = objectNode.TryGetProperty(JsonLdKeywords.Vocab, out _),
            Language = objectNode.TryGetProperty(JsonLdKeywords.Language, out JsonNode languageNode)
                ? RequireStringOrNull(languageNode, JsonLdErrorCode.InvalidDefaultLanguage, JsonLdKeywords.Language)
                : null,
            HasLanguage = objectNode.TryGetProperty(JsonLdKeywords.Language, out _),
            Direction = objectNode.TryGetProperty(JsonLdKeywords.Direction, out JsonNode directionNode)
                ? RequireStringOrNull(directionNode, JsonLdErrorCode.InvalidBaseDirection, JsonLdKeywords.Direction)
                : null,
            HasDirection = objectNode.TryGetProperty(JsonLdKeywords.Direction, out _),
            Propagate = objectNode.TryGetProperty(JsonLdKeywords.Propagate, out JsonNode propagateNode)
                ? RequireBoolean(propagateNode, JsonLdErrorCode.InvalidPropagateValue, JsonLdKeywords.Propagate)
                : null,
            Protected = objectNode.TryGetProperty(JsonLdKeywords.Protected, out JsonNode protectedNode)
                ? RequireBoolean(protectedNode, JsonLdErrorCode.InvalidProtectedValue, JsonLdKeywords.Protected)
                : null,
            Import = objectNode.TryGetProperty(JsonLdKeywords.Import, out JsonNode importNode)
                ? RequireString(importNode, JsonLdErrorCode.InvalidImportValue, JsonLdKeywords.Import)
                : null
        };
    }

    private static LinkedDataTermSource ExtractTermSource(
        string termName,
        JsonNode termNode,
        string? baseUrl,
        int depth,
        ref int keyCounter)
    {
        string syntheticKey = MakeKey(ref keyCounter, depth, "term-" + termName);

        //Null value becomes a remove-the-term marker.
        if(termNode.Kind == JsonNodeKind.Null)
        {
            return new LinkedDataTermSource(syntheticKey) { IsRemoval = true };
        }

        //Simple-string form: "term": "iri".
        if(termNode.Kind == JsonNodeKind.String)
        {
            return new LinkedDataTermSource(syntheticKey) { Iri = termNode.GetString(), IsSimpleString = true };
        }

        if(termNode.Kind != JsonNodeKind.Object)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidTermDefinition,
                string.Create(CultureInfo.InvariantCulture, $"Term definition for '{termName}' must be a string, object, or null."));
        }

        //Full term-definition object: each keyword field is read and validated
        //directly from the node by its key (a declarative projection in place
        //of a per-field dispatch switch). Unknown keys are tolerated silently,
        //as the W3C algorithm warns but continues.
        bool hasId = termNode.TryGetProperty(JsonLdKeywords.Id, out JsonNode idNode);

        //An explicit "@id": null is a removal marker (W3C JSON-LD 1.1 §4.1.2);
        //the term source then leaves Iri null, indistinguishable to the
        //algorithm from "no @id specified" (both fall back to vocab expansion).
        bool isRemoval = hasId && idNode.Kind == JsonNodeKind.Null;

        //A scoped @context is extracted ahead of the projection so its nested
        //synthetic-key counter threads back into this extraction run.
        IReadOnlyList<LinkedDataContextEntry>? scopedContext = null;
        if(termNode.TryGetProperty(JsonLdKeywords.Context, out JsonNode contextNode))
        {
            int innerCounter = keyCounter;
            scopedContext = ExtractEntries(contextNode, baseUrl, ref innerCounter);
            keyCounter = innerCounter;
        }

        return new LinkedDataTermSource(syntheticKey)
        {
            Iri = hasId && !isRemoval
                ? RequireString(idNode, JsonLdErrorCode.InvalidIriMapping, "A term definition's @id")
                : null,
            Type = termNode.TryGetProperty(JsonLdKeywords.Type, out JsonNode typeNode)
                ? RequireString(typeNode, JsonLdErrorCode.InvalidTypeMappingTermDefinition, "A term definition's @type")
                : null,
            Containers = termNode.TryGetProperty(JsonLdKeywords.Container, out JsonNode containerNode)
                ? ExtractContainerList(containerNode)
                : null,
            Language = termNode.TryGetProperty(JsonLdKeywords.Language, out JsonNode languageNode)
                ? RequireStringOrNull(languageNode, JsonLdErrorCode.InvalidLanguageMapping, "A term definition's @language")
                : null,
            HasLanguageMapping = termNode.TryGetProperty(JsonLdKeywords.Language, out _),
            Direction = termNode.TryGetProperty(JsonLdKeywords.Direction, out JsonNode directionNode)
                ? RequireStringOrNull(directionNode, JsonLdErrorCode.InvalidBaseDirection, "A term definition's @direction")
                : null,
            HasDirectionMapping = termNode.TryGetProperty(JsonLdKeywords.Direction, out _),
            Reverse = termNode.TryGetProperty(JsonLdKeywords.Reverse, out _),
            ReverseIri = termNode.TryGetProperty(JsonLdKeywords.Reverse, out JsonNode reverseNode)
                ? RequireString(reverseNode, JsonLdErrorCode.InvalidIriMapping, "A term definition's @reverse")
                : null,
            Protected = termNode.TryGetProperty(JsonLdKeywords.Protected, out JsonNode protectedNode)
                && RequireBoolean(protectedNode, JsonLdErrorCode.InvalidProtectedValue, "A term definition's @protected"),
            HasProtected = termNode.TryGetProperty(JsonLdKeywords.Protected, out _),
            Prefix = termNode.TryGetProperty(JsonLdKeywords.Prefix, out JsonNode prefixNode)
                && RequireBoolean(prefixNode, JsonLdErrorCode.InvalidPrefixValue, "A term definition's @prefix"),
            Nest = termNode.TryGetProperty(JsonLdKeywords.Nest, out JsonNode nestNode)
                ? RequireString(nestNode, JsonLdErrorCode.InvalidNestValue, "A term definition's @nest")
                : null,
            Index = termNode.TryGetProperty(JsonLdKeywords.Index, out JsonNode indexNode)
                ? RequireString(indexNode, JsonLdErrorCode.InvalidTermDefinition, "A term definition's @index")
                : null,
            ScopedContext = scopedContext,
            IsRemoval = isRemoval
        };
    }

    private static List<string> ExtractContainerList(JsonNode containerNode)
    {
        List<string> result = [];
        switch(containerNode.Kind)
        {
            case JsonNodeKind.String:
            {
                result.Add(containerNode.GetString());
                break;
            }
            case JsonNodeKind.Array:
            {
                foreach(JsonNode element in containerNode.EnumerateArray())
                {
                    if(element.Kind != JsonNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidContainerMapping,
                            "@container array must contain only strings.");
                    }
                    result.Add(element.GetString());
                }
                break;
            }
            default:
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidContainerMapping,
                    "@container must be a string or array.");
            }
        }
        return result;
    }

    /// <summary>Reads a string-valued context member, throwing the given error when the value is not a JSON string.</summary>
    /// <param name="node">The member value.</param>
    /// <param name="errorCode">The error code to raise on a type mismatch.</param>
    /// <param name="description">A human-readable name of the member, used in the error message.</param>
    /// <returns>The string value.</returns>
    private static string RequireString(JsonNode node, JsonLdErrorCode errorCode, string description)
        => node.Kind == JsonNodeKind.String
            ? node.GetString()
            : throw new JsonLdProcessingException(errorCode, string.Create(CultureInfo.InvariantCulture, $"{description} must be a string."));

    /// <summary>Reads a string-or-null context member (a JSON string, or <see langword="null"/> for an explicit JSON null), throwing otherwise.</summary>
    /// <param name="node">The member value.</param>
    /// <param name="errorCode">The error code to raise on a type mismatch.</param>
    /// <param name="description">A human-readable name of the member, used in the error message.</param>
    /// <returns>The string value, or <see langword="null"/> for a JSON null.</returns>
    private static string? RequireStringOrNull(JsonNode node, JsonLdErrorCode errorCode, string description)
        => node.Kind switch
        {
            JsonNodeKind.Null => null,
            JsonNodeKind.String => node.GetString(),
            _ => throw new JsonLdProcessingException(errorCode, string.Create(CultureInfo.InvariantCulture, $"{description} must be a string or null."))
        };

    /// <summary>Reads a boolean-valued context member, throwing the given error when the value is not a JSON boolean.</summary>
    /// <param name="node">The member value.</param>
    /// <param name="errorCode">The error code to raise on a type mismatch.</param>
    /// <param name="description">A human-readable name of the member, used in the error message.</param>
    /// <returns>The boolean value.</returns>
    private static bool RequireBoolean(JsonNode node, JsonLdErrorCode errorCode, string description)
        => node.Kind is JsonNodeKind.True or JsonNodeKind.False
            ? node.GetBoolean()
            : throw new JsonLdProcessingException(errorCode, string.Create(CultureInfo.InvariantCulture, $"{description} must be a boolean."));

    private static string MakeKey(ref int keyCounter, int depth, string label)
    {
        int n = Interlocked.Increment(ref keyCounter);
        return string.Create(CultureInfo.InvariantCulture, $"k{n}-d{depth}-{label}");
    }
}
