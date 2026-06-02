using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.JsonLd.Document;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements a subset of the JSON-LD 1.1 compaction algorithm
/// (<see href="https://www.w3.org/TR/json-ld11-api/#compaction-algorithms"/>):
/// given an expanded JSON-LD element and an active context, produces the
/// compact form where IRIs are replaced by terms or compact IRIs and
/// value objects are unwrapped to their idiomatic compact shape.
/// </summary>
/// <remarks>
/// <para>
/// Implemented in this version:
/// </para>
/// <list type="bullet">
/// <item><description>IRI compaction — direct term match (shortest-then-ordinal),
/// compact IRI via the inverted prefix-term index on
/// <see cref="LinkedDataContext"/>, and <c>@vocab</c>-relative compaction.</description></item>
/// <item><description>Value-object compaction — <c>{"@value": ...}</c> objects
/// are unwrapped to their idiomatic compact form when the surrounding term's
/// type or language mapping permits.</description></item>
/// <item><description>Element compaction — arrays, node objects, value objects.
/// Top-level emits <c>@context</c> when the target context defines any terms
/// or vocabulary mapping.</description></item>
/// </list>
/// <para>
/// Deferred to follow-ups:
/// </para>
/// <list type="bullet">
/// <item><description>Container mappings (<c>@list</c>, <c>@set</c>, <c>@language</c>,
/// <c>@index</c>, <c>@id</c>, <c>@graph</c>, <c>@type</c>).</description></item>
/// <item><description>Framing (<c>@frame</c>).</description></item>
/// <item><description>The <c>ordered</c> flag for deterministic property ordering.</description></item>
/// <item><description>Reverse-property handling.</description></item>
/// </list>
/// <para>
/// Output shape: <see cref="object"/>?, where object maps to
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c>, array maps to
/// <c>IReadOnlyList&lt;object?&gt;</c>, and leaves are <see cref="string"/>,
/// <see cref="bool"/>, <see cref="long"/>, <see cref="double"/>, or <c>null</c>.
/// Callers serialise to JSON via their JSON adapter.
/// </para>
/// </remarks>
public static class JsonLdCompactor
{
    /// <summary>
    /// Returns the compact form of <paramref name="iri"/> against
    /// <paramref name="activeContext"/>. Order of attempted compactions:
    /// (1) direct term match — find a term whose
    /// <see cref="TermDefinition.IriMapping"/> equals the IRI, preferring the
    /// shortest term, then the ordinal-least; (2) compact IRI — find a prefix
    /// term via <see cref="LinkedDataContext.TryGetPrefixTerm"/> using a right-to-
    /// left boundary walk (<c>/</c>, <c>#</c>, <c>:</c>); (3) <c>@vocab</c>-
    /// relative — when <paramref name="vocab"/> is <see langword="true"/> and
    /// the IRI begins with the active <see cref="LinkedDataContext.VocabularyMapping"/>,
    /// strip the prefix; (4) fall through and return the IRI unchanged.
    /// </summary>
    /// <param name="activeContext">The active context against which to compact.</param>
    /// <param name="iri">The IRI to compact. JSON-LD keywords are returned unchanged.</param>
    /// <param name="vocab">Permit <c>@vocab</c>-relative compaction when <see langword="true"/>.</param>
    /// <returns>The compact form, or <paramref name="iri"/> unchanged when no compaction applies.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the compaction algorithm.")]
    [return: NotNullIfNotNull(nameof(iri))]
    public static string? CompactIri(LinkedDataContext activeContext, string? iri, bool vocab = false)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        if(iri is null)
        {
            return null;
        }
        if(IriUtils.IsKeyword(iri))
        {
            return iri;
        }

        //Phase 1: direct term match. Pick shortest-then-ordinal-least.
        string? best = null;
        foreach(string termName in activeContext.Terms)
        {
            if(activeContext.TryGetTerm(termName, out TermDefinition? def)
                && def is { IriMapping: { } mapping }
                && string.Equals(mapping, iri, StringComparison.Ordinal)
                && IsBetterCandidate(termName, best))
            {
                best = termName;
            }
        }
        if(best is not null)
        {
            return best;
        }

        //Phase 2: compact IRI via prefix term. Right-to-left boundary walk
        //finds the longest matching namespace prefix.
        for(int i = iri.Length - 1; i > 0; i--)
        {
            char c = iri[i];
            if(c is not ('/' or '#' or ':'))
            {
                continue;
            }
            string namespaceIri = iri[..(i + 1)];
            string suffix = iri[(i + 1)..];
            if(suffix.Length == 0)
            {
                continue;
            }
            if(activeContext.TryGetPrefixTerm(namespaceIri, out string? prefixTerm))
            {
                //Spec-correct ambiguity rule: do not emit a compact IRI
                //whose prefix:suffix form collides with an existing term
                //in the active context (that other term would expand
                //differently). Defer to the IRI in that case.
                string candidate = string.Concat(prefixTerm, ":", suffix);
                if(!activeContext.TryGetTerm(candidate, out _))
                {
                    return candidate;
                }
            }
        }

        //Phase 3: @vocab-relative.
        if(vocab && activeContext.VocabularyMapping is { } vocabIri
            && iri.Length > vocabIri.Length
            && iri.StartsWith(vocabIri, StringComparison.Ordinal))
        {
            string suffix = iri[vocabIri.Length..];
            //Result must not collide with a keyword (per spec); a stripped
            //suffix that itself looks like a keyword cannot be safely emitted.
            if(!IriUtils.IsKeyword(suffix) && !IriUtils.IsKeywordLike(suffix))
            {
                return suffix;
            }
        }

        //Phase 4: fall through.
        return iri;
    }

    /// <summary>
    /// Compacts <paramref name="expanded"/> against <paramref name="activeContext"/>
    /// and returns the document as an object graph. The top-level result is an
    /// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> with <c>@context</c>
    /// prepended when <paramref name="activeContext"/> defines any terms or
    /// carries a vocabulary mapping.
    /// </summary>
    /// <param name="expanded">The expanded JSON-LD element.</param>
    /// <param name="activeContext">The context against which to compact.</param>
    /// <returns>The compacted element as an object graph.</returns>
    public static object? Compact(JsonLdNode expanded, LinkedDataContext activeContext)
    {
        ArgumentNullException.ThrowIfNull(activeContext);

        object? compactedBody = CompactElement(expanded, activeContext);

        //Top-level emission: when the context has user-defined terms or a
        //vocabulary mapping, emit @context alongside the compacted body so
        //the output is self-contained. Arrays are wrapped in a node object
        //for the @context attachment.
        IReadOnlyDictionary<string, object?>? emittedContext = EmitContextObject(activeContext);
        if(emittedContext is null)
        {
            return compactedBody;
        }

        if(compactedBody is IReadOnlyDictionary<string, object?> bodyMap)
        {
            Dictionary<string, object?> withContext = new(bodyMap.Count + 1, StringComparer.Ordinal)
            {
                [JsonLdKeywords.Context] = emittedContext
            };
            foreach(KeyValuePair<string, object?> kv in bodyMap)
            {
                withContext[kv.Key] = kv.Value;
            }
            return withContext;
        }

        //Array or scalar body: wrap into a graph object so @context can attach.
        return new Dictionary<string, object?>(2, StringComparer.Ordinal)
        {
            [JsonLdKeywords.Context] = emittedContext,
            [JsonLdKeywords.Graph] = compactedBody
        };
    }

    /// <summary>
    /// Compacts a value-object (<c>{"@value": ...}</c>) against the given
    /// term definition. When the term's type or language mapping matches the
    /// value's coercion, the wrapper is unwrapped to the bare literal.
    /// </summary>
    /// <param name="valueObject">An expanded value object.</param>
    /// <param name="termDefinition">The term definition under which the value appears, or <c>null</c> for none.</param>
    /// <returns>The compact value form.</returns>
    public static object? CompactValue(JsonLdNode valueObject, TermDefinition? termDefinition)
    {
        if(valueObject.Kind != JsonLdNodeKind.Object)
        {
            //Not a value object; pass through.
            return ToObjectGraph(valueObject);
        }

        if(!valueObject.TryGetProperty(JsonLdKeywords.Value, out JsonLdNode valueNode))
        {
            return ToObjectGraph(valueObject);
        }

        bool hasType = valueObject.TryGetProperty(JsonLdKeywords.Type, out JsonLdNode typeNode);
        bool hasLanguage = valueObject.TryGetProperty(JsonLdKeywords.Language, out JsonLdNode languageNode);

        //If the surrounding term's coercion matches the value's type or
        //language, unwrap to the bare literal. Otherwise, emit a compact
        //value object preserving the type/language.
        if(termDefinition is not null)
        {
            if(hasType
                && typeNode.Kind == JsonLdNodeKind.String
                && termDefinition.TypeMapping is { } typeCoerce
                && string.Equals(typeNode.GetString(), typeCoerce, StringComparison.Ordinal))
            {
                return ToObjectGraph(valueNode);
            }
            if(hasLanguage
                && languageNode.Kind == JsonLdNodeKind.String
                && termDefinition.LanguageMapping is { } langCoerce
                && string.Equals(languageNode.GetString(), langCoerce, StringComparison.Ordinal))
            {
                return ToObjectGraph(valueNode);
            }
            //An untyped, unlanguaged plain string under a term with no
            //type coercion: unwrap.
            if(!hasType && !hasLanguage && valueNode.Kind != JsonLdNodeKind.Object)
            {
                return ToObjectGraph(valueNode);
            }
        }
        else
        {
            //No surrounding term context: unwrap plain literals only.
            if(!hasType && !hasLanguage && valueNode.Kind != JsonLdNodeKind.Object)
            {
                return ToObjectGraph(valueNode);
            }
        }

        //Preserve the value object in compact form. @type IRI gets vocab-
        //relative compaction so type IRIs surface as terms when defined.
        Dictionary<string, object?> compactedObject = new(StringComparer.Ordinal)
        {
            [JsonLdKeywords.Value] = ToObjectGraph(valueNode)
        };
        if(hasType && typeNode.Kind == JsonLdNodeKind.String)
        {
            //Compaction context is unavailable to this primitive; emit @type as-is.
            compactedObject[JsonLdKeywords.Type] = typeNode.GetString();
        }
        if(hasLanguage && languageNode.Kind == JsonLdNodeKind.String)
        {
            compactedObject[JsonLdKeywords.Language] = languageNode.GetString();
        }
        return compactedObject;
    }

    private static object? CompactElement(JsonLdNode element, LinkedDataContext activeContext)
    {
        return element.Kind switch
        {
            JsonLdNodeKind.Array => CompactArray(element, activeContext),
            JsonLdNodeKind.Object => CompactNodeObject(element, activeContext),
            _ => ToObjectGraph(element)
        };
    }

    private static List<object?> CompactArray(JsonLdNode array, LinkedDataContext activeContext)
    {
        List<object?> items = new();
        foreach(JsonLdNode item in array.EnumerateArray())
        {
            items.Add(CompactElement(item, activeContext));
        }
        return items;
    }

    private static object? CompactNodeObject(JsonLdNode obj, LinkedDataContext activeContext)
    {
        //Value object detection: a JSON-LD value object has @value as an
        //own property. Route through CompactValue.
        if(obj.TryGetProperty(JsonLdKeywords.Value, out _))
        {
            return CompactValue(obj, termDefinition: null);
        }

        Dictionary<string, object?> result = new(StringComparer.Ordinal);

        foreach(KeyValuePair<string, JsonLdNode> property in obj.EnumerateObject())
        {
            string expandedKey = property.Key;
            JsonLdNode expandedValue = property.Value;

            switch(expandedKey)
            {
                case var k when JsonLdKeywords.IsId(k):
                {
                    //@id values are resource IRIs; compact in document-IRI mode
                    //(no @vocab relative; an @id is never an unqualified term).
                    if(expandedValue.Kind == JsonLdNodeKind.String)
                    {
                        result[JsonLdKeywords.Id] = CompactIri(activeContext, expandedValue.GetString(), vocab: false);
                    }
                    else
                    {
                        result[JsonLdKeywords.Id] = ToObjectGraph(expandedValue);
                    }
                    break;
                }
                case var k when JsonLdKeywords.IsType(k):
                {
                    //@type values are class IRIs; vocab-relative compaction applies.
                    result[JsonLdKeywords.Type] = CompactTypeValue(expandedValue, activeContext);
                    break;
                }
                case var k when JsonLdKeywords.IsContext(k) || JsonLdKeywords.IsGraph(k):
                {
                    //Pass through container keys without IRI compaction on the key
                    //itself. @graph's contents are compacted as a normal element.
                    if(JsonLdKeywords.IsGraph(expandedKey))
                    {
                        result[JsonLdKeywords.Graph] = CompactElement(expandedValue, activeContext);
                    }
                    else
                    {
                        result[expandedKey] = ToObjectGraph(expandedValue);
                    }

                    break;
                }
                case var k when JsonLdKeywords.IsReverse(k):
                {
                    //@reverse value is an object of expanded predicates →
                    //value arrays. Compact each predicate to its term/compact
                    //IRI form and emit under @reverse on the output.
                    if(expandedValue.Kind == JsonLdNodeKind.Object)
                    {
                        Dictionary<string, object?> compactedReverse = new(StringComparer.Ordinal);

                        foreach(KeyValuePair<string, JsonLdNode> inner in expandedValue.EnumerateObject())
                        {
                            string compactInnerKey = CompactIri(activeContext, inner.Key, vocab: true) ?? inner.Key;
                            TermDefinition? innerTerm = null;
                            if(activeContext.TryGetTerm(compactInnerKey, out TermDefinition? innerDef))
                            {
                                innerTerm = innerDef;
                            }

                            compactedReverse[compactInnerKey] = CompactValueOrElement(inner.Value, activeContext, innerTerm);
                        }

                        result[JsonLdKeywords.Reverse] = compactedReverse;
                    }

                    break;
                }
                default:
                {
                    //IRI key: compact to its term or compact-IRI form.
                    string compactKey = CompactIri(activeContext, expandedKey, vocab: true) ?? expandedKey;

                    //If the compact key resolves to a term in the active
                    //context, value compaction uses that term's coercion rules.
                    TermDefinition? termDef = null;
                    if(activeContext.TryGetTerm(compactKey, out TermDefinition? def))
                    {
                        termDef = def;
                    }

                    result[compactKey] = CompactValueOrElement(expandedValue, activeContext, termDef);
                    break;
                }
            }
        }

        return result;
    }

    private static object? CompactValueOrElement(JsonLdNode element, LinkedDataContext activeContext, TermDefinition? termDef)
    {
        bool isSet = termDef is not null && HasContainer(termDef, JsonLdKeywords.Set);
        bool isList = termDef is not null && HasContainer(termDef, JsonLdKeywords.List);
        bool isLanguage = termDef is not null && HasContainer(termDef, JsonLdKeywords.Language);
        bool isIndex = termDef is not null && HasContainer(termDef, JsonLdKeywords.Index);
        bool isIdMap = termDef is not null && HasContainer(termDef, JsonLdKeywords.Id);
        bool isTypeMap = termDef is not null && HasContainer(termDef, JsonLdKeywords.Type);
        bool isGraph = termDef is not null && HasContainer(termDef, JsonLdKeywords.Graph);

        //@list container: expanded form is {"@list": [...]}; compact form
        //is a bare array of compacted items.
        if(isList)
        {
            return CompactListContainer(element, activeContext, termDef);
        }

        //@language container: expanded form is an array of {"@value", "@language"}
        //value objects; compact form is {"langTag": value, ...} map.
        if(isLanguage)
        {
            return CompactLanguageContainer(element, activeContext, termDef);
        }

        //Map-shaped containers route through a unified collector that
        //extracts each item's discriminator (@index, @id, or @type),
        //compacts it, and uses it as the map key. @graph is layered on
        //top: when combined with @id or @index, the inner values become
        //{"@graph": [...]} objects; alone, it wraps each value as a graph.
        if(isIndex || isIdMap || isTypeMap)
        {
            return CompactMapContainer(element, activeContext, termDef,
                useIndex: isIndex, useId: isIdMap, useType: isTypeMap, useGraph: isGraph);
        }
        if(isGraph)
        {
            return CompactGraphContainerAlone(element, activeContext, termDef);
        }

        if(element.Kind == JsonLdNodeKind.Array)
        {
            //Expanded values are arrays per spec; compact each item. When the
            //resulting list has a single item, unwrap (the default
            //compactArrays = true behaviour) — unless @container is @set,
            //which forces the array shape.
            List<object?> compactedItems = new();
            foreach(JsonLdNode item in element.EnumerateArray())
            {
                compactedItems.Add(CompactItem(item, activeContext, termDef));
            }
            if(compactedItems.Count == 1 && !isSet)
            {
                return compactedItems[0];
            }
            return compactedItems;
        }

        object? single = CompactItem(element, activeContext, termDef);
        if(isSet)
        {
            return new List<object?> { single };
        }
        return single;
    }

    private static bool HasContainer(TermDefinition def, string container)
    {
        foreach(string entry in def.ContainerMapping)
        {
            if(string.Equals(entry, container, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Compacts a value under a <c>@list</c>-container term. The expanded
    /// form wraps each list as <c>{"@list": [item1, item2, ...]}</c>; the
    /// compact form is the bare array. RDF round-trip projects the bare
    /// array back to an <c>rdf:List</c> chain on re-expansion.
    /// </summary>
    private static object? CompactListContainer(JsonLdNode element, LinkedDataContext activeContext, TermDefinition? termDef)
    {
        if(element.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in element.EnumerateArray())
            {
                if(item.Kind == JsonLdNodeKind.Object
                    && item.TryGetProperty(JsonLdKeywords.List, out JsonLdNode listArray)
                    && listArray.Kind == JsonLdNodeKind.Array)
                {
                    return MaterialiseListItems(listArray, activeContext, termDef);
                }
            }
            //No @list wrapper found in the expanded array: defensive fallback.
            return CompactItem(element, activeContext, termDef);
        }
        if(element.Kind == JsonLdNodeKind.Object
            && element.TryGetProperty(JsonLdKeywords.List, out JsonLdNode innerList)
            && innerList.Kind == JsonLdNodeKind.Array)
        {
            return MaterialiseListItems(innerList, activeContext, termDef);
        }
        return CompactItem(element, activeContext, termDef);
    }

    private static List<object?> MaterialiseListItems(JsonLdNode listArray, LinkedDataContext activeContext, TermDefinition? termDef)
    {
        List<object?> compacted = new();
        foreach(JsonLdNode listItem in listArray.EnumerateArray())
        {
            compacted.Add(CompactItem(listItem, activeContext, termDef));
        }
        return compacted;
    }

    /// <summary>
    /// Compacts a value under a <c>@language</c>-container term. Expanded
    /// form is an array of <c>{"@value": "...", "@language": "tag"}</c>
    /// value objects; compact form is a <c>{tag: value}</c> map. When a
    /// single language carries multiple values, the map value is an array.
    /// Items lacking <c>@language</c> are excluded from the map; if no
    /// items match, falls back to standard value compaction.
    /// </summary>
    private static object? CompactLanguageContainer(JsonLdNode element, LinkedDataContext activeContext, TermDefinition? termDef)
    {
        Dictionary<string, List<object?>> bucketed = new(StringComparer.Ordinal);

        if(element.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in element.EnumerateArray())
            {
                CollectLanguageValue(item, bucketed);
            }
        }
        else
        {
            CollectLanguageValue(element, bucketed);
        }

        if(bucketed.Count == 0)
        {
            //No @language-tagged values found; fall back to the standard
            //compaction so untagged values are emitted as-is.
            return CompactItem(element, activeContext, termDef);
        }

        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, List<object?>> bucket in bucketed)
        {
            result[bucket.Key] = bucket.Value.Count == 1 ? bucket.Value[0] : bucket.Value;
        }
        return result;
    }

    private static void CollectLanguageValue(JsonLdNode item, Dictionary<string, List<object?>> bucketed)
    {
        if(item.Kind != JsonLdNodeKind.Object)
        {
            return;
        }
        if(!item.TryGetProperty(JsonLdKeywords.Value, out JsonLdNode valueNode))
        {
            return;
        }
        if(!item.TryGetProperty(JsonLdKeywords.Language, out JsonLdNode languageNode)
            || languageNode.Kind != JsonLdNodeKind.String)
        {
            return;
        }
        string tag = languageNode.GetString();
        if(!bucketed.TryGetValue(tag, out List<object?>? bucket))
        {
            bucket = new List<object?>();
            bucketed[tag] = bucket;
        }
        bucket.Add(ToObjectGraph(valueNode));
    }

    /// <summary>
    /// Compacts a value under one of the map-shaped containers (<c>@index</c>,
    /// <c>@id</c>, <c>@type</c>) with optional <c>@graph</c> layering.
    /// Walks the expanded array, extracts each item's discriminator value,
    /// compacts it as appropriate (vocab-relative for <c>@type</c>, IRI for
    /// <c>@id</c>, plain string for <c>@index</c>), and uses it as the map
    /// key. When the same key appears multiple times the value becomes an
    /// array.
    /// </summary>
    /// <remarks>
    /// Per W3C JSON-LD 1.1 §4.6.4–§4.6.6, the discriminator value is
    /// removed from the item's compact form because it is now carried by
    /// the map key. <c>@graph</c> combined with one of the map containers
    /// wraps each item as <c>{"@graph": [...]}</c> under the map key.
    /// </remarks>
    private static object? CompactMapContainer(
        JsonLdNode element,
        LinkedDataContext activeContext,
        TermDefinition? termDef,
        bool useIndex,
        bool useId,
        bool useType,
        bool useGraph)
    {
        Dictionary<string, List<object?>> bucketed = new(StringComparer.Ordinal);

        if(element.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in element.EnumerateArray())
            {
                CollectMapValue(item, activeContext, termDef, useIndex, useId, useType, useGraph, bucketed);
            }
        }
        else
        {
            CollectMapValue(element, activeContext, termDef, useIndex, useId, useType, useGraph, bucketed);
        }

        if(bucketed.Count == 0)
        {
            //No items carried the required discriminator; fall back to
            //standard compaction so the value is at least preserved.
            return CompactItem(element, activeContext, termDef);
        }

        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, List<object?>> bucket in bucketed)
        {
            result[bucket.Key] = bucket.Value.Count == 1 ? bucket.Value[0] : bucket.Value;
        }
        return result;
    }

    private static void CollectMapValue(
        JsonLdNode item,
        LinkedDataContext activeContext,
        TermDefinition? termDef,
        bool useIndex,
        bool useId,
        bool useType,
        bool useGraph,
        Dictionary<string, List<object?>> bucketed)
    {
        if(item.Kind != JsonLdNodeKind.Object)
        {
            return;
        }

        //Extract the map key from the appropriate discriminator. Index
        //wins when explicitly set; otherwise @id, otherwise @type.
        string? mapKey = null;
        if(useIndex && item.TryGetProperty(JsonLdKeywords.Index, out JsonLdNode indexNode)
            && indexNode.Kind == JsonLdNodeKind.String)
        {
            mapKey = indexNode.GetString();
        }
        else if(useId && item.TryGetProperty(JsonLdKeywords.Id, out JsonLdNode idNode)
            && idNode.Kind == JsonLdNodeKind.String)
        {
            //@id keys compact in document-IRI mode (no @vocab-relative).
            mapKey = CompactIri(activeContext, idNode.GetString(), vocab: false);
        }
        else if(useType && item.TryGetProperty(JsonLdKeywords.Type, out JsonLdNode typeNode))
        {
            //@type may be a single string or an array; the spec uses the
            //first type when multiple are present.
            if(typeNode.Kind == JsonLdNodeKind.String)
            {
                mapKey = CompactIri(activeContext, typeNode.GetString(), vocab: true);
            }
            else if(typeNode.Kind == JsonLdNodeKind.Array)
            {
                foreach(JsonLdNode typeItem in typeNode.EnumerateArray())
                {
                    if(typeItem.Kind == JsonLdNodeKind.String)
                    {
                        mapKey = CompactIri(activeContext, typeItem.GetString(), vocab: true);
                        break;
                    }
                }
            }
        }

        if(mapKey is null)
        {
            return;
        }

        //Compact the item itself; strip the discriminator field(s) used
        //above so they don't appear redundantly in the value.
        object? compactedItem = CompactNodeObject(item, activeContext);
        if(compactedItem is Dictionary<string, object?> dict)
        {
            if(useIndex)
            {
                dict.Remove(JsonLdKeywords.Index);
            }
            if(useId)
            {
                dict.Remove(JsonLdKeywords.Id);
            }
            if(useType)
            {
                //Only remove @type when it is the key. If @type has
                //multiple values and we used the first, leave the rest.
                if(item.TryGetProperty(JsonLdKeywords.Type, out JsonLdNode typeNode)
                    && typeNode.Kind == JsonLdNodeKind.String)
                {
                    dict.Remove(JsonLdKeywords.Type);
                }
                //For array @type: the spec is permissive; leaving the
                //array in place preserves the other types.
            }
        }

        //@graph layering: when combined with a map container, the item's
        //compact form is wrapped as {"@graph": ...} under the map key.
        if(useGraph)
        {
            compactedItem = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [JsonLdKeywords.Graph] = compactedItem
            };
        }

        if(!bucketed.TryGetValue(mapKey, out List<object?>? bucket))
        {
            bucket = new List<object?>();
            bucketed[mapKey] = bucket;
        }
        bucket.Add(compactedItem);
    }

    /// <summary>
    /// Compacts a value under <c>@container: @graph</c> alone (no <c>@id</c>
    /// or <c>@index</c> companion). Each item is preserved as a graph
    /// object; single-item arrays unwrap unless the term also carries
    /// <c>@set</c> semantics elsewhere.
    /// </summary>
    private static object? CompactGraphContainerAlone(JsonLdNode element, LinkedDataContext activeContext, TermDefinition? termDef)
    {
        _ = termDef;
        if(element.Kind == JsonLdNodeKind.Array)
        {
            List<object?> wrapped = new();
            foreach(JsonLdNode item in element.EnumerateArray())
            {
                wrapped.Add(WrapAsGraph(item, activeContext));
            }
            return wrapped.Count == 1 ? wrapped[0] : wrapped;
        }
        return WrapAsGraph(element, activeContext);
    }

    private static object WrapAsGraph(JsonLdNode item, LinkedDataContext activeContext)
    {
        //If the item is already a graph object, compact it. Otherwise
        //wrap its compact form in {"@graph": [...]}.
        if(item.Kind == JsonLdNodeKind.Object && item.TryGetProperty(JsonLdKeywords.Graph, out _))
        {
            object? compactedGraph = CompactNodeObject(item, activeContext);
            return compactedGraph ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        object? compactedItem = CompactNodeObject(item, activeContext);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [JsonLdKeywords.Graph] = compactedItem
        };
    }

    private static object? CompactItem(JsonLdNode item, LinkedDataContext activeContext, TermDefinition? termDef)
    {
        if(item.Kind == JsonLdNodeKind.Object)
        {
            if(item.TryGetProperty(JsonLdKeywords.Value, out _))
            {
                return CompactValue(item, termDef);
            }
            return CompactNodeObject(item, activeContext);
        }
        if(item.Kind == JsonLdNodeKind.Array)
        {
            return CompactArray(item, activeContext);
        }
        return ToObjectGraph(item);
    }

    private static object? CompactTypeValue(JsonLdNode typeValue, LinkedDataContext activeContext)
    {
        if(typeValue.Kind == JsonLdNodeKind.String)
        {
            return CompactIri(activeContext, typeValue.GetString(), vocab: true);
        }
        if(typeValue.Kind == JsonLdNodeKind.Array)
        {
            List<object?> items = new();
            foreach(JsonLdNode item in typeValue.EnumerateArray())
            {
                if(item.Kind == JsonLdNodeKind.String)
                {
                    items.Add(CompactIri(activeContext, item.GetString(), vocab: true));
                }
                else
                {
                    items.Add(ToObjectGraph(item));
                }
            }
            return items.Count == 1 ? items[0] : items;
        }
        return ToObjectGraph(typeValue);
    }

    private static Dictionary<string, object?>? EmitContextObject(LinkedDataContext activeContext)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        if(activeContext.VocabularyMapping is { } vocabMapping)
        {
            result[JsonLdKeywords.Vocab] = vocabMapping;
        }
        if(activeContext.BaseIri is { } baseIri)
        {
            result[JsonLdKeywords.Base] = baseIri;
        }
        if(activeContext.DefaultLanguage is { } lang)
        {
            result[JsonLdKeywords.Language] = lang;
        }
        foreach(string termName in activeContext.Terms)
        {
            if(!activeContext.TryGetTerm(termName, out TermDefinition? def) || def is null)
            {
                continue;
            }
            //Emit a compact term definition: bare IRI string when only
            //IriMapping is set, otherwise an object preserving the meaningful
            //fields. Type-coerced or language-coerced terms emit as objects.
            object? termValue = EmitTermDefinition(def);
            if(termValue is not null)
            {
                result[termName] = termValue;
            }
        }
        return result.Count == 0 ? null : result;
    }

    private static object? EmitTermDefinition(TermDefinition def)
    {
        if(def.IriMapping is null)
        {
            return null;
        }

        bool hasExtras = def.TypeMapping is not null
            || def.HasLanguageMapping
            || def.ContainerMapping.Count > 0
            || def.Prefix
            || def.Protected
            || def.ReverseProperty is not null
            || def.NestValue is not null
            || def.IndexMapping is not null;

        if(!hasExtras)
        {
            return def.IriMapping;
        }

        Dictionary<string, object?> full = new(StringComparer.Ordinal)
        {
            [JsonLdKeywords.Id] = def.IriMapping
        };
        if(def.TypeMapping is { } typeMapping)
        {
            full[JsonLdKeywords.Type] = typeMapping;
        }
        if(def.HasLanguageMapping)
        {
            full[JsonLdKeywords.Language] = def.LanguageMapping;
        }
        if(def.ContainerMapping.Count > 0)
        {
            full[JsonLdKeywords.Container] = def.ContainerMapping.Count == 1 ? def.ContainerMapping[0] : (object?)def.ContainerMapping;
        }
        if(def.Prefix)
        {
            full[JsonLdKeywords.Prefix] = true;
        }
        if(def.Protected)
        {
            full[JsonLdKeywords.Protected] = true;
        }
        if(def.ReverseProperty is { } reverse)
        {
            full[JsonLdKeywords.Reverse] = reverse;
        }
        if(def.NestValue is { } nest)
        {
            full[JsonLdKeywords.Nest] = nest;
        }
        if(def.IndexMapping is { } index)
        {
            full[JsonLdKeywords.Index] = index;
        }
        return full;
    }

    private static object? ToObjectGraph(JsonLdNode node)
    {
        return node.Kind switch
        {
            JsonLdNodeKind.Null => null,
            JsonLdNodeKind.String => node.GetString(),
            JsonLdNodeKind.True => true,
            JsonLdNodeKind.False => false,
            JsonLdNodeKind.Number => ParseNumberLiteral(node.GetRawNumber()),
            JsonLdNodeKind.Array => MaterialiseArray(node),
            JsonLdNodeKind.Object => MaterialiseObject(node),
            _ => null
        };
    }

    private static List<object?> MaterialiseArray(JsonLdNode array)
    {
        List<object?> items = new();
        foreach(JsonLdNode item in array.EnumerateArray())
        {
            items.Add(ToObjectGraph(item));
        }
        return items;
    }

    private static Dictionary<string, object?> MaterialiseObject(JsonLdNode obj)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonLdNode> property in obj.EnumerateObject())
        {
            result[property.Key] = ToObjectGraph(property.Value);
        }
        return result;
    }

    private static object ParseNumberLiteral(string raw)
    {
        if(long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long longValue))
        {
            return longValue;
        }
        if(double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double doubleValue))
        {
            return doubleValue;
        }
        //Fall back to the raw lexical form so no precision is silently dropped.
        return raw;
    }

    private static bool IsBetterCandidate(string candidate, string? current)
    {
        if(current is null)
        {
            return true;
        }
        int lengthCompare = candidate.Length.CompareTo(current.Length);
        if(lengthCompare != 0)
        {
            return lengthCompare < 0;
        }
        return string.CompareOrdinal(candidate, current) < 0;
    }
}
