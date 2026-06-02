using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd.Internal;

/// <summary>
/// Walks a <see cref="CborLdInputNode"/> representing an <c>@context</c>
/// value and produces a fully-extracted
/// <see cref="IReadOnlyList{T}"/> of <see cref="LinkedDataContextEntry"/>
/// suitable for the format-agnostic
/// <c>Lumoin.Veritas.LinkedData.ContextProcessing</c> core. Mirrors
/// <c>Lumoin.Veritas.JsonLd.ContextExtraction</c> but inspects the
/// CBOR-LD node shape (`CborLdInputString`, `CborLdInputMap`, etc.)
/// instead of <c>JsonNode</c>.
/// </summary>
/// <remarks>
/// The iterative-stack convention applies — no method-call recursion
/// in the walker. Scoped contexts inside term definitions are
/// recursively extracted in the same pass so the produced tree carries
/// the full nested context structure.
/// </remarks>
internal static class CborLdContextExtraction
{
    /// <summary>
    /// Extracts the entries of a single <c>@context</c> value (which may
    /// be a string URL, a null, an inline map, or an array of any of the
    /// preceding).
    /// </summary>
    /// <param name="contextNode">The <c>@context</c> value as a <see cref="CborLdInputNode"/>.</param>
    /// <param name="baseUrl">The base URL in effect at the point this context appears.</param>
    /// <param name="keyCounter">Cross-call counter used to assign unique synthetic keys.</param>
    /// <returns>The extracted entries in document order.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static IReadOnlyList<LinkedDataContextEntry> ExtractEntries(
        CborLdInputNode contextNode,
        string? baseUrl,
        ref int keyCounter)
    {
        List<LinkedDataContextEntry> entries = [];
        ExtractInto(entries, contextNode, baseUrl, ref keyCounter, depth: 0);
        return entries;
    }

    private static void ExtractInto(
        List<LinkedDataContextEntry> entries,
        CborLdInputNode contextNode,
        string? baseUrl,
        ref int keyCounter,
        int depth)
    {
        switch(contextNode)
        {
            case CborLdInputNull:
            {
                entries.Add(new LinkedDataContextEntry(MakeKey(ref keyCounter, depth, "reset")));
                break;
            }
            case CborLdInputString urlNode:
            {
                entries.Add(new LinkedDataContextEntry(urlNode.Value, baseUrl, MakeKey(ref keyCounter, depth, "url")));
                break;
            }
            case CborLdInputArray arrayNode:
            {
                foreach(CborLdInputNode element in arrayNode.Items)
                {
                    ExtractInto(entries, element, baseUrl, ref keyCounter, depth);
                }
                break;
            }
            case CborLdInputMap mapNode:
            {
                entries.Add(ExtractInlineEntry(mapNode, baseUrl, ref keyCounter, depth));
                break;
            }
            default:
            {
                throw new CborLdProcessingException(
                    "invalid local context",
                    string.Create(CultureInfo.InvariantCulture, $"@context must be a string, map, array, or null. Got '{contextNode.GetType().Name}'."));
            }
        }
    }

    private static LinkedDataContextEntry ExtractInlineEntry(
        CborLdInputMap mapNode,
        string? baseUrl,
        ref int keyCounter,
        int depth)
    {
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

        foreach(KeyValuePair<string, CborLdInputNode> entry in mapNode.Entries)
        {
            switch(entry.Key)
            {
                case "@version":
                {
                    break;
                }
                case "@base":
                {
                    hasBase = true;
                    @base = entry.Value is CborLdInputNull ? null : (entry.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@vocab":
                {
                    hasVocab = true;
                    vocab = entry.Value is CborLdInputNull ? null : (entry.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@language":
                {
                    hasLanguage = true;
                    language = entry.Value is CborLdInputNull ? null : (entry.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@direction":
                {
                    hasDirection = true;
                    direction = entry.Value is CborLdInputNull ? null : (entry.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@propagate":
                {
                    if(entry.Value is CborLdInputBool pb)
                    {
                        propagate = pb.Value;
                    }
                    break;
                }
                case "@protected":
                {
                    if(entry.Value is CborLdInputBool prot)
                    {
                        @protected = prot.Value;
                    }
                    break;
                }
                case "@import":
                {
                    import = (entry.Value as CborLdInputString)?.Value;
                    break;
                }
                default:
                {
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

    /// <summary>
    /// Extracts a single term definition from the value side of a
    /// term/value entry in an inline context map.
    /// </summary>
    /// <param name="termName">The term name (key).</param>
    /// <param name="termNode">The term value node.</param>
    /// <param name="baseUrl">The base URL in scope.</param>
    /// <param name="depth">The nesting depth (for synthetic-key uniqueness).</param>
    /// <param name="keyCounter">Cross-call counter for unique synthetic keys.</param>
    /// <returns>The extracted term source.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public static LinkedDataTermSource ExtractTermSource(
        string termName,
        CborLdInputNode termNode,
        string? baseUrl,
        int depth,
        ref int keyCounter)
    {
        string syntheticKey = MakeKey(ref keyCounter, depth, "term-" + termName);

        if(termNode is CborLdInputNull)
        {
            return new LinkedDataTermSource(syntheticKey) { IsRemoval = true };
        }

        if(termNode is CborLdInputString stringForm)
        {
            return new LinkedDataTermSource(syntheticKey) { Iri = stringForm.Value, IsSimpleString = true };
        }

        if(termNode is not CborLdInputMap mapForm)
        {
            throw new CborLdProcessingException(
                "invalid term definition",
                string.Create(CultureInfo.InvariantCulture, $"Term definition for '{termName}' must be a string, map, or null."));
        }

        string? iri = null;
        bool isRemoval = false;
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

        foreach(KeyValuePair<string, CborLdInputNode> field in mapForm.Entries)
        {
            switch(field.Key)
            {
                case "@id":
                {
                    if(field.Value is CborLdInputNull)
                    {
                        isRemoval = true;
                    }
                    else if(field.Value is CborLdInputString idStr)
                    {
                        iri = idStr.Value;
                    }
                    break;
                }
                case "@type":
                {
                    type = (field.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@reverse":
                {
                    reverse = true;
                    reverseIri = (field.Value as CborLdInputString)?.Value;
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
                    language = field.Value is CborLdInputNull ? null : (field.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@direction":
                {
                    direction = field.Value is CborLdInputNull ? null : (field.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@protected":
                {
                    if(field.Value is CborLdInputBool prot)
                    {
                        @protected = prot.Value;
                    }
                    break;
                }
                case "@prefix":
                {
                    if(field.Value is CborLdInputBool pb)
                    {
                        prefix = pb.Value;
                    }
                    break;
                }
                case "@nest":
                {
                    nest = (field.Value as CborLdInputString)?.Value;
                    break;
                }
                case "@index":
                {
                    index = (field.Value as CborLdInputString)?.Value;
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
                    break;
                }
            }
        }

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

    private static List<string> ExtractContainerList(CborLdInputNode containerNode)
    {
        List<string> result = [];
        switch(containerNode)
        {
            case CborLdInputString s:
            {
                result.Add(s.Value);
                break;
            }
            case CborLdInputArray arr:
            {
                foreach(CborLdInputNode element in arr.Items)
                {
                    if(element is not CborLdInputString es)
                    {
                        throw new CborLdProcessingException(
                            "invalid container mapping",
                            "@container array must contain only strings.");
                    }
                    result.Add(es.Value);
                }
                break;
            }
            default:
            {
                throw new CborLdProcessingException(
                    "invalid container mapping",
                    "@container must be a string or array.");
            }
        }
        return result;
    }

    private static string MakeKey(ref int keyCounter, int depth, string label)
    {
        int n = Interlocked.Increment(ref keyCounter);
        return string.Create(CultureInfo.InvariantCulture, $"cbor-k{n}-d{depth}-{label}");
    }
}
