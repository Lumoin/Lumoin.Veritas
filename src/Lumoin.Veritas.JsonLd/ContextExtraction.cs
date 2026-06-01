using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Lumoin.Veritas.JsonLd.Document;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Walks a <see cref="JsonLdNode"/> representing an <c>@context</c>
/// value and produces a fully-extracted
/// <see cref="IReadOnlyList{T}"/> of <see cref="LinkedDataContextEntry"/>
/// suitable for the format-agnostic
/// <c>Lumoin.Veritas.LinkedData.ContextProcessing</c> core.
/// </summary>
/// <remarks>
/// <para>
/// The extractor performs the JSON-side reading once at the boundary;
/// downstream context-processing methods operate purely on POCOs and do
/// not need to inspect the original <see cref="JsonLdNode"/> tree.
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
    /// <param name="contextNode">The <c>@context</c> value as a <see cref="JsonLdNode"/>.</param>
    /// <param name="baseUrl">The base URL in effect at the point this context appears.</param>
    /// <param name="keyCounter">Cross-call counter used to assign unique synthetic keys.</param>
    /// <returns>The extracted entries in document order.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static IReadOnlyList<LinkedDataContextEntry> ExtractEntries(
        JsonLdNode contextNode,
        string? baseUrl,
        ref int keyCounter)
    {
        List<LinkedDataContextEntry> entries = [];
        ExtractInto(entries, contextNode, baseUrl, ref keyCounter, depth: 0);
        return entries;
    }

    private static void ExtractInto(
        List<LinkedDataContextEntry> entries,
        JsonLdNode contextNode,
        string? baseUrl,
        ref int keyCounter,
        int depth)
    {
        switch(contextNode.Kind)
        {
            case JsonLdNodeKind.Null:
            {
                entries.Add(new LinkedDataContextEntry(MakeKey(ref keyCounter, depth, "reset")));
                break;
            }
            case JsonLdNodeKind.String:
            {
                string url = contextNode.GetString();
                entries.Add(new LinkedDataContextEntry(url, baseUrl, MakeKey(ref keyCounter, depth, "url")));
                break;
            }
            case JsonLdNodeKind.Array:
            {
                foreach(JsonLdNode element in contextNode.EnumerateArray())
                {
                    ExtractInto(entries, element, baseUrl, ref keyCounter, depth);
                }
                break;
            }
            case JsonLdNodeKind.Object:
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
        JsonLdNode objectNode,
        string? baseUrl,
        ref int keyCounter,
        int depth)
    {
        //Pull out the context-level directives first; the remaining
        //properties become term definitions.
        string? @base = null;
        bool hasBase = false;
        string? vocab = null;
        bool hasVocab = false;
        string? language = null;
        bool hasLanguage = false;
        string? direction = null;
        bool hasDirection = false;
        bool? propagate = null;
        bool? @protected = null;
        string? import = null;

        Dictionary<string, LinkedDataTermSource> terms = [];

        foreach(KeyValuePair<string, JsonLdNode> entry in objectNode.EnumerateObject())
        {
            switch(entry.Key)
            {
                case "@version":
                {
                    //Validated downstream; no field needed on the entry.
                    break;
                }
                case "@base":
                {
                    hasBase = true;
                    @base = RequireStringOrNull(entry.Value, JsonLdErrorCode.InvalidBaseIri, "@base");
                    break;
                }
                case "@vocab":
                {
                    hasVocab = true;
                    vocab = RequireStringOrNull(entry.Value, JsonLdErrorCode.InvalidVocabMapping, "@vocab");
                    break;
                }
                case "@language":
                {
                    hasLanguage = true;
                    language = RequireStringOrNull(entry.Value, JsonLdErrorCode.InvalidDefaultLanguage, "@language");
                    break;
                }
                case "@direction":
                {
                    hasDirection = true;
                    direction = RequireStringOrNull(entry.Value, JsonLdErrorCode.InvalidBaseDirection, "@direction");
                    break;
                }
                case "@propagate":
                {
                    propagate = RequireBoolean(entry.Value, JsonLdErrorCode.InvalidPropagateValue, "@propagate");
                    break;
                }
                case "@protected":
                {
                    @protected = RequireBoolean(entry.Value, JsonLdErrorCode.InvalidProtectedValue, "@protected");
                    break;
                }
                case "@import":
                {
                    import = RequireString(entry.Value, JsonLdErrorCode.InvalidImportValue, "@import");
                    break;
                }
                default:
                {
                    //Regular term definition.
                    LinkedDataTermSource termSource = ExtractTermSource(
                        entry.Key, entry.Value, baseUrl, depth + 1, ref keyCounter);
                    terms[entry.Key] = termSource;
                    break;
                }
            }
        }

        return new LinkedDataContextEntry(terms, baseUrl, MakeKey(ref keyCounter, depth, "inline"))
        {
            Base = @base,
            HasBase = hasBase,
            Vocab = vocab,
            HasVocab = hasVocab,
            Language = language,
            HasLanguage = hasLanguage,
            Direction = direction,
            HasDirection = hasDirection,
            Propagate = propagate,
            Protected = @protected,
            Import = import
        };
    }

    private static LinkedDataTermSource ExtractTermSource(
        string termName,
        JsonLdNode termNode,
        string? baseUrl,
        int depth,
        ref int keyCounter)
    {
        string syntheticKey = MakeKey(ref keyCounter, depth, "term-" + termName);

        //Null value becomes a remove-the-term marker.
        if(termNode.Kind == JsonLdNodeKind.Null)
        {
            return new LinkedDataTermSource(syntheticKey) { IsRemoval = true };
        }

        //Simple-string form: "term": "iri".
        if(termNode.Kind == JsonLdNodeKind.String)
        {
            return new LinkedDataTermSource(syntheticKey) { Iri = termNode.GetString(), IsSimpleString = true };
        }

        if(termNode.Kind != JsonLdNodeKind.Object)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidTermDefinition,
                string.Create(CultureInfo.InvariantCulture, $"Term definition for '{termName}' must be a string, object, or null."));
        }

        //Full term-definition object.
        string? iri = null;
        bool isRemoval = false;
        bool iriExplicitlyNull = false;
        string? type = null;
        List<string>? containers = null;
        string? language = null;
        bool hasLanguage = false;
        string? direction = null;
        bool reverse = false;
        string? reverseIri = null;
        bool @protected = false;
        bool prefix = false;
        string? nest = null;
        string? index = null;
        IReadOnlyList<LinkedDataContextEntry>? scopedContext = null;

        foreach(KeyValuePair<string, JsonLdNode> field in termNode.EnumerateObject())
        {
            switch(field.Key)
            {
                case "@id":
                {
                    if(field.Value.Kind == JsonLdNodeKind.Null)
                    {
                        iriExplicitlyNull = true;
                        //Per JSON-LD 1.1 §4.1.2, an explicit @id: null
                        //in an object form is also a removal marker.
                        isRemoval = true;
                    }
                    else
                    {
                        iri = RequireString(field.Value, JsonLdErrorCode.InvalidIriMapping, "A term definition's @id");
                    }
                    break;
                }
                case "@type":
                {
                    type = RequireString(field.Value, JsonLdErrorCode.InvalidTypeMappingTermDefinition, "A term definition's @type");
                    break;
                }
                case "@reverse":
                {
                    reverse = true;
                    reverseIri = RequireString(field.Value, JsonLdErrorCode.InvalidIriMapping, "A term definition's @reverse");
                    break;
                }
                case "@container":
                {
                    containers = ExtractContainerList(field.Value);
                    break;
                }
                case "@language":
                {
                    hasLanguage = true;
                    language = RequireStringOrNull(field.Value, JsonLdErrorCode.InvalidLanguageMapping, "A term definition's @language");
                    break;
                }
                case "@direction":
                {
                    direction = RequireStringOrNull(field.Value, JsonLdErrorCode.InvalidBaseDirection, "A term definition's @direction");
                    break;
                }
                case "@protected":
                {
                    @protected = RequireBoolean(field.Value, JsonLdErrorCode.InvalidProtectedValue, "A term definition's @protected");
                    break;
                }
                case "@prefix":
                {
                    prefix = RequireBoolean(field.Value, JsonLdErrorCode.InvalidPrefixValue, "A term definition's @prefix");
                    break;
                }
                case "@nest":
                {
                    nest = RequireString(field.Value, JsonLdErrorCode.InvalidNestValue, "A term definition's @nest");
                    break;
                }
                case "@index":
                {
                    index = RequireString(field.Value, JsonLdErrorCode.InvalidTermDefinition, "A term definition's @index");
                    break;
                }
                case "@context":
                {
                    int innerCounter = keyCounter;
                    scopedContext = ExtractEntries(field.Value, baseUrl, ref innerCounter);
                    keyCounter = innerCounter;
                    break;
                }
                default:
                {
                    //Unknown keys are tolerated silently; the W3C algorithm warns but continues.
                    break;
                }
            }
        }

        //An explicit "@id: null" marker survives by leaving Iri null on
        //the term source; the caller distinguishes "no @id specified" via
        //the same surface (algorithm falls back to vocab expansion in
        //that case). The iriExplicitlyNull local is for documentation
        //purposes; the algorithm treats both states identically.
        _ = iriExplicitlyNull;

        return new LinkedDataTermSource(syntheticKey)
        {
            Iri = iri,
            Type = type,
            Containers = containers,
            Language = language,
            HasLanguageMapping = hasLanguage,
            Direction = direction,
            Reverse = reverse,
            ReverseIri = reverseIri,
            Protected = @protected,
            Prefix = prefix,
            Nest = nest,
            Index = index,
            ScopedContext = scopedContext,
            IsRemoval = isRemoval
        };
    }

    private static List<string> ExtractContainerList(JsonLdNode containerNode)
    {
        List<string> result = [];
        switch(containerNode.Kind)
        {
            case JsonLdNodeKind.String:
            {
                result.Add(containerNode.GetString());
                break;
            }
            case JsonLdNodeKind.Array:
            {
                foreach(JsonLdNode element in containerNode.EnumerateArray())
                {
                    if(element.Kind != JsonLdNodeKind.String)
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
    private static string RequireString(JsonLdNode node, JsonLdErrorCode errorCode, string description)
        => node.Kind == JsonLdNodeKind.String
            ? node.GetString()
            : throw new JsonLdProcessingException(errorCode, string.Create(CultureInfo.InvariantCulture, $"{description} must be a string."));

    /// <summary>Reads a string-or-null context member (a JSON string, or <see langword="null"/> for an explicit JSON null), throwing otherwise.</summary>
    /// <param name="node">The member value.</param>
    /// <param name="errorCode">The error code to raise on a type mismatch.</param>
    /// <param name="description">A human-readable name of the member, used in the error message.</param>
    /// <returns>The string value, or <see langword="null"/> for a JSON null.</returns>
    private static string? RequireStringOrNull(JsonLdNode node, JsonLdErrorCode errorCode, string description)
        => node.Kind switch
        {
            JsonLdNodeKind.Null => null,
            JsonLdNodeKind.String => node.GetString(),
            _ => throw new JsonLdProcessingException(errorCode, string.Create(CultureInfo.InvariantCulture, $"{description} must be a string or null."))
        };

    /// <summary>Reads a boolean-valued context member, throwing the given error when the value is not a JSON boolean.</summary>
    /// <param name="node">The member value.</param>
    /// <param name="errorCode">The error code to raise on a type mismatch.</param>
    /// <param name="description">A human-readable name of the member, used in the error message.</param>
    /// <returns>The boolean value.</returns>
    private static bool RequireBoolean(JsonLdNode node, JsonLdErrorCode errorCode, string description)
        => node.Kind is JsonLdNodeKind.True or JsonLdNodeKind.False
            ? node.GetBoolean()
            : throw new JsonLdProcessingException(errorCode, string.Create(CultureInfo.InvariantCulture, $"{description} must be a boolean."));

    private static string MakeKey(ref int keyCounter, int depth, string label)
    {
        int n = Interlocked.Increment(ref keyCounter);
        return string.Create(CultureInfo.InvariantCulture, $"k{n}-d{depth}-{label}");
    }
}
