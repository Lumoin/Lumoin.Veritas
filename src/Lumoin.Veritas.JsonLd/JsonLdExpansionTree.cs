using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.JsonLd.Document;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Expands a JSON-LD document to the W3C JSON-LD 1.1 expanded form as an
/// object graph. Complements <see cref="JsonLdExpander.ExtractQuadsAsync"/>
/// which produces RDF quads; this method produces the spec-defined
/// expanded JSON-LD tree shape, suitable for re-compaction against a
/// different context via <see cref="JsonLdCompactor.Compact"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scope of this V1 implementation:
/// </para>
/// <list type="bullet">
/// <item><description>Document-level <c>@context</c> processing.</description></item>
/// <item><description>Term and compact-IRI expansion via the active context.</description></item>
/// <item><description><c>@id</c> and <c>@type</c> expansion.</description></item>
/// <item><description>Value coercion: scalars under a typed term become <c>{"@value", "@type"}</c>; under a languaged term, <c>{"@value", "@language"}</c>.</description></item>
/// <item><description>Wrapping property values in arrays per the expanded-form spec.</description></item>
/// <item><description>The <c>@list</c> container, projected to <c>{"@list": [...]}</c>.</description></item>
/// </list>
/// <para>
/// Deferred: <c>@reverse</c> properties, type-scoped and property-scoped
/// contexts (term <see cref="TermDefinition.ScopedContextEntries"/>), the
/// <c>@language</c>/<c>@index</c>/<c>@id</c>/<c>@type</c>/<c>@graph</c>
/// containers, <c>@nest</c>, protected-term enforcement, and the
/// <c>ordered</c> flag. The minimal scope covers the common documents
/// (Verifiable Credentials, DID documents, schema.org snippets) that
/// don't lean on those features.
/// </para>
/// <para>
/// Output type: <c>object?</c> — a tree of
/// <c>IReadOnlyList&lt;object?&gt;</c> arrays,
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> objects, and
/// <see cref="string"/>/<see cref="long"/>/<see cref="double"/>/<see cref="bool"/>/<c>null</c>
/// leaves. The top-level result is an array of node objects per the W3C
/// expanded-form convention. Use <see cref="JsonLdCompactor.Compact"/>
/// on the result via <see cref="ObjectGraphJsonLdAdapter.Wrap"/>.
/// </para>
/// </remarks>
public static class JsonLdExpansionTree
{
    /// <summary>
    /// Expands <paramref name="document"/> to JSON-LD expanded form as an
    /// object graph. The top-level result is an array of expanded node
    /// objects.
    /// </summary>
    /// <param name="document">The compact JSON-LD document to expand.</param>
    /// <param name="baseUrl">Optional base URL for document-relative IRIs.</param>
    /// <param name="resolver">Delegate that resolves remote <c>@context</c> URLs.</param>
    /// <param name="parser">Delegate that parses fetched context bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The expanded document as an object graph.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    public static async ValueTask<IReadOnlyList<object?>> ExpandAsync(
        JsonLdNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(parser);

        LinkedDataContext activeContext = LinkedDataContext.Empty;
        if(baseUrl is not null)
        {
            activeContext = activeContext.WithBaseIri(baseUrl);
        }

        if(document.Kind == JsonLdNodeKind.Object
            && document.TryGetProperty("@context", out JsonLdNode contextElement))
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, contextElement, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        object? expanded = await ExpandElementAsync(
            document, activeContext, baseUrl, resolver, parser, cancellationToken).ConfigureAwait(false);

        //Top-level result is always an array of node objects.
        if(expanded is IReadOnlyList<object?> listResult)
        {
            return listResult;
        }
        if(expanded is null)
        {
            return Array.Empty<object?>();
        }
        return new List<object?> { expanded };
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandElementAsync(
        JsonLdNode element,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch(element.Kind)
        {
            case JsonLdNodeKind.Null:
            {
                return null;
            }
            case JsonLdNodeKind.Array:
            {
                List<object?> result = new();
                foreach(JsonLdNode item in element.EnumerateArray())
                {
                    object? expandedItem = await ExpandElementAsync(
                        item, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);
                    if(expandedItem is null)
                    {
                        continue;
                    }
                    //Per spec, nested arrays from value-array expansion
                    //are flattened into the outer array.
                    if(expandedItem is IReadOnlyList<object?> nested)
                    {
                        foreach(object? nestedItem in nested)
                        {
                            result.Add(nestedItem);
                        }
                    }
                    else
                    {
                        result.Add(expandedItem);
                    }
                }
                return result;
            }
            case JsonLdNodeKind.Object:
            {
                return await ExpandObjectAsync(element, activeContext, baseUrl, resolver, parser, cancellationToken)
                    .ConfigureAwait(false);
            }
            default:
            {
                //Bare scalar at top-level: wrap as a value object (no term coercion).
                return ScalarToValueObject(element, termDef: null);
            }
        }
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandObjectAsync(
        JsonLdNode obj,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        //Apply local @context (already applied at document root; this
        //handles embedded @context on nested nodes).
        if(obj.TryGetProperty("@context", out JsonLdNode localContext))
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, localContext, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        //If the object is a value object ({"@value": ...}), it's a leaf;
        //preserve it as-is in object-graph form with @type/@language
        //expanded.
        if(obj.TryGetProperty("@value", out _))
        {
            return ExpandValueObject(obj, activeContext);
        }

        //Type-scoped contexts: when the node has @type, apply each
        //matching term's ScopedContextEntries in alphabetical order of
        //type IRIs per W3C JSON-LD 1.1 §4.1.4 step 8. The application
        //updates the active context for the rest of this node.
        if(obj.TryGetProperty("@type", out JsonLdNode typeNode))
        {
            activeContext = await ApplyTypeScopedAsync(
                activeContext, typeNode, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        Dictionary<string, object?> result = new(StringComparer.Ordinal);

        foreach(KeyValuePair<string, JsonLdNode> property in obj.EnumerateObject())
        {
            string key = property.Key;
            JsonLdNode value = property.Value;

            switch(key)
            {
                case "@context":
                {
                    //Already processed into activeContext above; drop from output.
                    continue;
                }
                case "@id":
                {
                    if(value.Kind == JsonLdNodeKind.String)
                    {
                        string raw = value.GetString();
                        string? expanded = activeContext.ExpandIri(raw, documentRelative: true);
                        result["@id"] = expanded ?? raw;
                    }
                    continue;
                }
                case "@type":
                {
                    result["@type"] = ExpandTypeValue(value, activeContext);
                    continue;
                }
                case "@graph":
                {
                    object? expandedGraph = await ExpandElementAsync(
                        value, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);
                    if(expandedGraph is IReadOnlyList<object?> graphList)
                    {
                        result["@graph"] = graphList;
                    }
                    else if(expandedGraph is not null)
                    {
                        result["@graph"] = new List<object?> { expandedGraph };
                    }

                    continue;
                }
                case "@reverse":
                {
                    //@reverse: the value is an object whose properties are
                    //expanded normally but stored under the @reverse key
                    //rather than at node level. RDF projection swaps
                    //subject/object for these.
                    if(value.Kind == JsonLdNodeKind.Object)
                    {
                        Dictionary<string, object?> reverseMap = await ExpandReverseAsync(
                            value, activeContext, baseUrl, resolver, parser, cancellationToken)
                            .ConfigureAwait(false);
                        if(reverseMap.Count > 0)
                        {
                            result["@reverse"] = reverseMap;
                        }
                    }

                    continue;
                }
                default:
                {
                    //IRI-keyed property. Expand the key.
                    string? expandedKey = activeContext.ExpandIri(key, vocab: true);
                    if(expandedKey is null)
                    {
                        continue;
                    }
                    //JSON-LD keywords other than the ones handled above
                    //are passed through unchanged.
                    if(IriUtils.IsKeyword(expandedKey) && expandedKey != "@list" && expandedKey != "@set")
                    {
                        result[expandedKey] = await ExpandElementAsync(
                            value, activeContext, baseUrl, resolver, parser, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if(!expandedKey.Contains(':', StringComparison.Ordinal) && !IriUtils.IsKeyword(expandedKey))
                    {
                        //Unmapped term; drop per spec.
                        continue;
                    }

                    TermDefinition? termDef = null;
                    if(activeContext.TryGetTerm(key, out TermDefinition? def))
                    {
                        termDef = def;
                    }

                    //Property-scoped context: when the term carries
                    //ScopedContextEntries, apply them before descending
                    //into the value subtree. The context update is local
                    //to this property; siblings see the unchanged
                    //activeContext.
                    LinkedDataContext propertyContext = activeContext;
                    if(termDef is { ScopedContextEntries: { } scopedEntries })
                    {
                        propertyContext = await ApplyScopedEntriesAsync(
                            activeContext, scopedEntries, baseUrl, resolver, parser, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    object? expandedValue = await ExpandPropertyValueAsync(
                        value, propertyContext, baseUrl, resolver, parser, termDef, cancellationToken)
                        .ConfigureAwait(false);

                    //Property values are always emitted as arrays per spec.
                    if(expandedValue is null)
                    {
                        continue;
                    }
                    result[expandedKey] = WrapAsArray(expandedValue);
                    continue;
                }
            }
        }

        return result;
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandPropertyValueAsync(
        JsonLdNode value,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        CancellationToken cancellationToken)
    {
        bool isList = termDef is not null && HasContainer(termDef, "@list");
        bool isLanguage = termDef is not null && HasContainer(termDef, "@language");
        bool isIndex = termDef is not null && HasContainer(termDef, "@index");
        bool isIdMap = termDef is not null && HasContainer(termDef, "@id");
        bool isTypeMap = termDef is not null && HasContainer(termDef, "@type");
        bool isGraph = termDef is not null && HasContainer(termDef, "@graph");

        //Map-shaped containers: the compact form is a {key: value} map
        //that expansion reshapes into an array of value/node objects,
        //attaching the discriminator (@language/@index/@id/@type) to each.
        if(value.Kind == JsonLdNodeKind.Object && (isLanguage || isIndex || isIdMap || isTypeMap))
        {
            return await ExpandMapContainerAsync(
                value, activeContext, baseUrl, resolver, parser, termDef,
                isLanguage: isLanguage, isIndex: isIndex, isIdMap: isIdMap, isTypeMap: isTypeMap, isGraph: isGraph,
                cancellationToken).ConfigureAwait(false);
        }

        if(value.Kind == JsonLdNodeKind.Array)
        {
            List<object?> items = new();
            foreach(JsonLdNode item in value.EnumerateArray())
            {
                object? expandedItem = await ExpandPropertyItemAsync(
                    item, activeContext, baseUrl, resolver, parser, termDef, cancellationToken)
                    .ConfigureAwait(false);
                if(expandedItem is null)
                {
                    continue;
                }
                items.Add(expandedItem);
            }

            //@list container: wrap the items in {"@list": [...]} per spec.
            if(isList)
            {
                Dictionary<string, object?> listObject = new(StringComparer.Ordinal)
                {
                    ["@list"] = items
                };

                return listObject;
            }

            return items;
        }

        object? singleExpanded = await ExpandPropertyItemAsync(
            value, activeContext, baseUrl, resolver, parser, termDef, cancellationToken)
            .ConfigureAwait(false);

        if(isList && singleExpanded is not null)
        {
            Dictionary<string, object?> listObject = new(StringComparer.Ordinal)
            {
                ["@list"] = new List<object?> { singleExpanded }
            };

            return listObject;
        }

        return singleExpanded;
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandPropertyItemAsync(
        JsonLdNode item,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        CancellationToken cancellationToken)
    {
        if(item.Kind == JsonLdNodeKind.Object)
        {
            return await ExpandObjectAsync(item, activeContext, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        if(item.Kind == JsonLdNodeKind.Null)
        {
            return null;
        }

        return ScalarToValueObject(item, termDef);
    }

    /// <summary>
    /// Expands the contents of an <c>@reverse</c> entry: each inner
    /// property is expanded normally and stored in the returned map. The
    /// caller attaches the result under the <c>@reverse</c> key on the
    /// surrounding node so RDF projection can swap subject and object.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<Dictionary<string, object?>> ExpandReverseAsync(
        JsonLdNode reverseObj,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);

        foreach(KeyValuePair<string, JsonLdNode> property in reverseObj.EnumerateObject())
        {
            string? expandedKey = activeContext.ExpandIri(property.Key, vocab: true);
            if(expandedKey is null
                || (!expandedKey.Contains(':', StringComparison.Ordinal) && !IriUtils.IsKeyword(expandedKey)))
            {
                continue;
            }

            TermDefinition? termDef = null;
            if(activeContext.TryGetTerm(property.Key, out TermDefinition? def))
            {
                termDef = def;
            }

            object? expandedValue = await ExpandPropertyValueAsync(
                property.Value, activeContext, baseUrl, resolver, parser, termDef, cancellationToken)
                .ConfigureAwait(false);

            if(expandedValue is not null)
            {
                result[expandedKey] = WrapAsArray(expandedValue);
            }
        }

        return result;
    }

    /// <summary>
    /// Reverses the map-shaped container compaction: takes the compact
    /// <c>{key: value}</c> form and produces the expanded array of value
    /// or node objects, attaching the discriminator field
    /// (<c>@language</c>, <c>@index</c>, <c>@id</c>, or <c>@type</c>) to
    /// each item. When combined with <c>@graph</c>, the map value is a
    /// graph object whose inner content receives the discriminator.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandMapContainerAsync(
        JsonLdNode value,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        bool isLanguage,
        bool isIndex,
        bool isIdMap,
        bool isTypeMap,
        bool isGraph,
        CancellationToken cancellationToken)
    {
        List<object?> items = new();

        foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
        {
            string mapKey = entry.Key;
            JsonLdNode mapValue = entry.Value;

            //Each map value may be a single item or an array of items.
            //Iterate uniformly.
            if(mapValue.Kind == JsonLdNodeKind.Array)
            {
                foreach(JsonLdNode innerItem in mapValue.EnumerateArray())
                {
                    object? expanded = await ExpandMapItemAsync(
                        innerItem, mapKey, activeContext, baseUrl, resolver, parser, termDef,
                        isLanguage, isIndex, isIdMap, isTypeMap, isGraph, cancellationToken).ConfigureAwait(false);
                    if(expanded is not null)
                    {
                        items.Add(expanded);
                    }
                }
            }
            else
            {
                object? expanded = await ExpandMapItemAsync(
                    mapValue, mapKey, activeContext, baseUrl, resolver, parser, termDef,
                    isLanguage, isIndex, isIdMap, isTypeMap, isGraph, cancellationToken).ConfigureAwait(false);
                if(expanded is not null)
                {
                    items.Add(expanded);
                }
            }
        }

        return items;
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandMapItemAsync(
        JsonLdNode item,
        string mapKey,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        bool isLanguage,
        bool isIndex,
        bool isIdMap,
        bool isTypeMap,
        bool isGraph,
        CancellationToken cancellationToken)
    {
        //For @language container, the map key IS the language tag and
        //each item is a scalar value (or array of scalars). Build a
        //value object with @language attached.
        if(isLanguage)
        {
            if(item.Kind == JsonLdNodeKind.Null)
            {
                return null;
            }

            Dictionary<string, object?> valueObject = new(StringComparer.Ordinal)
            {
                ["@value"] = item.Kind switch
                {
                    JsonLdNodeKind.String => item.GetString(),
                    JsonLdNodeKind.True => (object?)true,
                    JsonLdNodeKind.False => (object?)false,
                    JsonLdNodeKind.Number => ParseNumber(item.GetRawNumber()),
                    _ => null
                },
                ["@language"] = mapKey
            };

            return valueObject;
        }

        //For map containers carrying node objects, expand the item as
        //a node object then attach the discriminator field.
        object? expandedItem = await ExpandPropertyItemAsync(
            item, activeContext, baseUrl, resolver, parser, termDef, cancellationToken).ConfigureAwait(false);

        if(expandedItem is not Dictionary<string, object?> nodeObject)
        {
            //Item is a scalar or value object; wrap and attach discriminator
            //where applicable. This is the unusual case for @id/@type/@index
            //containers; the spec doesn't define a uniform behaviour for
            //scalars under these containers, so we pass through.
            return expandedItem;
        }

        if(isIndex)
        {
            nodeObject["@index"] = mapKey;
        }

        if(isIdMap)
        {
            string? expandedId = activeContext.ExpandIri(mapKey, documentRelative: true);
            nodeObject["@id"] = expandedId ?? mapKey;
        }

        if(isTypeMap)
        {
            string? expandedType = activeContext.ExpandIri(mapKey, vocab: true);
            //If the node already has a @type, append the discriminator
            //type to keep the multi-type case round-trippable.
            if(nodeObject.TryGetValue("@type", out object? existingType))
            {
                if(existingType is IReadOnlyList<object?> existingList)
                {
                    List<object?> combined = new(existingList) { expandedType ?? mapKey };
                    nodeObject["@type"] = combined;
                }
                else if(existingType is not null)
                {
                    nodeObject["@type"] = new List<object?> { existingType, expandedType ?? mapKey };
                }
                else
                {
                    nodeObject["@type"] = expandedType ?? mapKey;
                }
            }
            else
            {
                nodeObject["@type"] = expandedType ?? mapKey;
            }
        }

        //@graph layering: wrap as a graph object. When combined with
        //@id or @index, the inner content is already keyed; just wrap.
        if(isGraph && !nodeObject.ContainsKey("@graph"))
        {
            //Move the inner content under @graph, keeping the @id/@index
            //we just attached at the outer level.
            Dictionary<string, object?> outer = new(StringComparer.Ordinal);
            Dictionary<string, object?> inner = new(StringComparer.Ordinal);
            foreach(KeyValuePair<string, object?> kv in nodeObject)
            {
                if((isIdMap && kv.Key == "@id") || (isIndex && kv.Key == "@index"))
                {
                    outer[kv.Key] = kv.Value;
                }
                else
                {
                    inner[kv.Key] = kv.Value;
                }
            }
            outer["@graph"] = new List<object?> { inner };

            return outer;
        }

        return nodeObject;
    }

    private static object? ExpandTypeValue(JsonLdNode value, LinkedDataContext activeContext)
    {
        if(value.Kind == JsonLdNodeKind.String)
        {
            string raw = value.GetString();
            return activeContext.ExpandIri(raw, vocab: true) ?? raw;
        }
        if(value.Kind == JsonLdNodeKind.Array)
        {
            List<object?> items = new();
            foreach(JsonLdNode item in value.EnumerateArray())
            {
                if(item.Kind == JsonLdNodeKind.String)
                {
                    string raw = item.GetString();
                    items.Add(activeContext.ExpandIri(raw, vocab: true) ?? raw);
                }
            }
            return items;
        }
        return null;
    }

    /// <summary>
    /// Wraps a scalar JSON value as a JSON-LD value object, applying type
    /// or language coercion from the surrounding term when present.
    /// </summary>
    private static Dictionary<string, object?>? ScalarToValueObject(JsonLdNode element, TermDefinition? termDef)
    {
        object? rawValue = element.Kind switch
        {
            JsonLdNodeKind.String => element.GetString(),
            JsonLdNodeKind.True => true,
            JsonLdNodeKind.False => false,
            JsonLdNodeKind.Number => ParseNumber(element.GetRawNumber()),
            JsonLdNodeKind.Null => null,
            _ => null
        };
        if(rawValue is null && element.Kind != JsonLdNodeKind.Null)
        {
            return null;
        }

        Dictionary<string, object?> valueObject = new(StringComparer.Ordinal)
        {
            ["@value"] = rawValue
        };

        if(termDef is not null)
        {
            if(termDef.TypeMapping is { } typeMapping && !IriUtils.IsKeyword(typeMapping))
            {
                valueObject["@type"] = typeMapping;
            }
            else if(termDef.HasLanguageMapping && termDef.LanguageMapping is { } language)
            {
                valueObject["@language"] = language;
            }
        }

        return valueObject;
    }

    /// <summary>
    /// Expands an already-shaped value object, applying IRI expansion to
    /// the <c>@type</c> field while leaving the rest of the object as-is.
    /// </summary>
    private static Dictionary<string, object?> ExpandValueObject(JsonLdNode value, LinkedDataContext activeContext)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
        {
            switch(entry.Key)
            {
                case "@value":
                {
                    result["@value"] = entry.Value.Kind switch
                    {
                        JsonLdNodeKind.String => entry.Value.GetString(),
                        JsonLdNodeKind.True => (object?)true,
                        JsonLdNodeKind.False => (object?)false,
                        JsonLdNodeKind.Number => ParseNumber(entry.Value.GetRawNumber()),
                        JsonLdNodeKind.Null => null,
                        _ => null
                    };
                    break;
                }
                case "@type":
                {
                    if(entry.Value.Kind == JsonLdNodeKind.String)
                    {
                        string raw = entry.Value.GetString();
                        result["@type"] = activeContext.ExpandIri(raw, vocab: true) ?? raw;
                    }
                    break;
                }
                case "@language":
                {
                    if(entry.Value.Kind == JsonLdNodeKind.String)
                    {
                        result["@language"] = entry.Value.GetString();
                    }
                    break;
                }
                default:
                {
                    //Other keys (e.g. @index) pass through.
                    break;
                }
            }
        }
        return result;
    }

    private static IReadOnlyList<object?> WrapAsArray(object? value)
    {
        if(value is IReadOnlyList<object?> existing)
        {
            return existing;
        }
        return new List<object?> { value };
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

    private static object ParseNumber(string raw)
    {
        if(long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            return l;
        }

        if(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            return d;
        }

        return raw;
    }

    /// <summary>
    /// Applies the type-scoped contexts triggered by an <c>@type</c>
    /// entry on the current node. Each type IRI is expanded via
    /// <see cref="LinkedDataContext.ExpandIri"/> (vocab-relative), the
    /// resulting IRIs are sorted alphabetically per W3C JSON-LD 1.1
    /// §4.1.4 step 8, and each matching term's
    /// <see cref="TermDefinition.ScopedContextEntries"/> is applied in
    /// turn.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<LinkedDataContext> ApplyTypeScopedAsync(
        LinkedDataContext activeContext,
        JsonLdNode typeNode,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        //Collect raw @type values (a string or array of strings).
        List<string> rawTypes = new();
        if(typeNode.Kind == JsonLdNodeKind.String)
        {
            rawTypes.Add(typeNode.GetString());
        }
        else if(typeNode.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in typeNode.EnumerateArray())
            {
                if(item.Kind == JsonLdNodeKind.String)
                {
                    rawTypes.Add(item.GetString());
                }
            }
        }

        if(rawTypes.Count == 0)
        {
            return activeContext;
        }

        //Expand each raw type to its IRI, then sort alphabetically.
        List<string> expandedTypes = new(rawTypes.Count);
        foreach(string raw in rawTypes)
        {
            expandedTypes.Add(activeContext.ExpandIri(raw, vocab: true) ?? raw);
        }
        expandedTypes.Sort(StringComparer.Ordinal);

        LinkedDataContext running = activeContext;
        foreach(string typeIri in expandedTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<LinkedDataContextEntry>? entriesForType = FindScopedEntriesForType(running, typeIri);
            if(entriesForType is null)
            {
                continue;
            }

            running = await ApplyScopedEntriesAsync(
                running, entriesForType, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        return running;
    }

    private static IReadOnlyList<LinkedDataContextEntry>? FindScopedEntriesForType(LinkedDataContext context, string typeIri)
    {
        //O(N) scan for the term whose IriMapping matches the type IRI
        //and that carries scoped entries. Could be replaced with an
        //inverted IriMapping → term index on LinkedDataContext if profiling
        //motivates.
        foreach(string termName in context.Terms)
        {
            if(context.TryGetTerm(termName, out TermDefinition? def)
                && def is { IriMapping: { } iri, ScopedContextEntries: { } entries }
                && string.Equals(iri, typeIri, StringComparison.Ordinal))
            {
                return entries;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies pre-extracted <see cref="LinkedDataContextEntry"/> entries
    /// (e.g. from a term's <see cref="TermDefinition.ScopedContextEntries"/>)
    /// to the active context. Forwards to
    /// <see cref="JsonLdScopedContextHelper.ApplyAsync"/> which is shared
    /// with <see cref="JsonLdExpander"/>'s quad-extraction path.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<LinkedDataContext> ApplyScopedEntriesAsync(
        LinkedDataContext activeContext,
        IReadOnlyList<LinkedDataContextEntry> entries,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        return await JsonLdScopedContextHelper.ApplyAsync(
            activeContext, entries, baseUrl, resolver, parser, cancellationToken)
            .ConfigureAwait(false);
    }
}
