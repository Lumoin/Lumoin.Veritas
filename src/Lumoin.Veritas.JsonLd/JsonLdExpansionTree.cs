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

        //The document's own @context is processed by ExpandObjectAsync when
        //it descends into the root object; processing it here as well would
        //apply it twice (harmless for an absolute @vocab, but a relative
        //@vocab would concatenate against itself).
        object? expanded = await ExpandElementAsync(
            document, activeContext, baseUrl, resolver, parser, cancellationToken).ConfigureAwait(false);

        //A document expanding to a map whose only entry is @graph is
        //replaced by the graph contents (the wrapper carries no node).
        if(expanded is IReadOnlyDictionary<string, object?> { Count: 1 } onlyGraph
            && onlyGraph.TryGetValue("@graph", out object? graphValue))
        {
            return graphValue as IReadOnlyList<object?> ?? Array.Empty<object?>();
        }

        //Top-level result is always an array of node objects, with
        //free-floating nodes (and bare value/list objects) dropped.
        if(expanded is IReadOnlyList<object?> listResult)
        {
            return DropFreeFloating(listResult);
        }
        if(expanded is null)
        {
            return Array.Empty<object?>();
        }
        return DropFreeFloating(new List<object?> { expanded });
    }

    /// <summary>
    /// Drops free-floating entries from a top-level or <c>@graph</c> array
    /// per the expansion algorithm: an empty node, a node whose only entry
    /// is <c>@id</c>, and bare <c>@value</c>/<c>@list</c> objects carry no
    /// assertion at graph level and are removed.
    /// </summary>
    private static List<object?> DropFreeFloating(IReadOnlyList<object?> nodes)
    {
        List<object?> kept = new(nodes.Count);
        foreach(object? node in nodes)
        {
            if(node is IReadOnlyDictionary<string, object?> map && IsFreeFloating(map))
            {
                continue;
            }

            kept.Add(node);
        }

        return kept;
    }

    /// <summary>
    /// Whether a node map is free-floating at graph level: empty, a sole
    /// <c>@id</c>, or a map whose keys are confined to <c>@value</c>/<c>@list</c>.
    /// </summary>
    private static bool IsFreeFloating(IReadOnlyDictionary<string, object?> map)
    {
        if(map.Count == 0)
        {
            return true;
        }

        //A value object or list object carries no node assertion at graph
        //level, and a node whose only entry is @id is a bare reference; all
        //are dropped.
        if(map.ContainsKey("@value") || map.ContainsKey("@list"))
        {
            return true;
        }

        return map.Count == 1 && map.ContainsKey("@id");
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
                return ScalarToValueObject(element, termDef: null, activeContext);
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

        //If the object is a value object (some key expands to @value, even
        //via an alias), it's a leaf preserved in object-graph form with
        //@type/@language expanded.
        if(IsValueObject(obj, activeContext))
        {
            return ExpandValueObject(obj, activeContext);
        }

        //A bare @list / @set object reached outside a coercing property
        //(e.g. at the top level): expand its members and re-wrap (@list) or
        //unwrap (@set). No term coercion applies here.
        if(obj.TryGetProperty("@list", out JsonLdNode topLevelList))
        {
            List<object?> listMembers = await ExpandMembersAsync(
                topLevelList, activeContext, baseUrl, resolver, parser, termDef: null, cancellationToken).ConfigureAwait(false);

            return new Dictionary<string, object?>(StringComparer.Ordinal) { ["@list"] = listMembers };
        }
        if(obj.TryGetProperty("@set", out JsonLdNode topLevelSet))
        {
            return await ExpandMembersAsync(
                topLevelSet, activeContext, baseUrl, resolver, parser, termDef: null, cancellationToken).ConfigureAwait(false);
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

            if(string.Equals(key, "@context", StringComparison.Ordinal))
            {
                //Already processed into activeContext above; drop from output.
                continue;
            }

            //Expand the key first so keyword aliases (a term mapping to
            //@id/@type/@graph/...) dispatch to the keyword handling rather
            //than being treated as ordinary IRI properties.
            string? expandedKey = activeContext.ExpandIri(key, vocab: true);
            if(expandedKey is null)
            {
                continue;
            }

            switch(expandedKey)
            {
                case "@id":
                {
                    if(value.Kind == JsonLdNodeKind.String)
                    {
                        //A keyword-like @id (e.g. "@ignoreMe") expands to null
                        //and is preserved as such rather than echoed verbatim.
                        string raw = value.GetString();
                        result["@id"] = activeContext.ExpandIri(raw, documentRelative: true);
                    }
                    continue;
                }
                case "@type":
                {
                    //Multiple keys expanding to @type (e.g. @type plus an
                    //alias) accumulate into one type array.
                    if(ExpandTypeValue(value, activeContext) is { } types)
                    {
                        AddValues(result, "@type", types);
                    }
                    continue;
                }
                case "@graph":
                {
                    object? expandedGraph = await ExpandElementAsync(
                        value, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);
                    if(expandedGraph is IReadOnlyList<object?> graphList)
                    {
                        result["@graph"] = DropFreeFloating(graphList);
                    }
                    else if(expandedGraph is not null)
                    {
                        result["@graph"] = DropFreeFloating(new List<object?> { expandedGraph });
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

                    IReadOnlyList<object?> valueArray = WrapAsArray(expandedValue);

                    //A term defined as a reverse property routes its values
                    //under the node's @reverse map, keyed by the reverse IRI.
                    if(termDef?.ReverseProperty is { } reverseIri)
                    {
                        AddReverseValues(result, reverseIri, valueArray);
                        continue;
                    }

                    //Multiple keys expanding to the same IRI merge into one
                    //value array rather than overwriting.
                    AddValues(result, expandedKey, valueArray);
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
                await AddExpandedMemberAsync(
                    items, item, activeContext, baseUrl, resolver, parser, termDef, cancellationToken)
                    .ConfigureAwait(false);
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

            //@graph container: each value becomes its own graph object.
            if(isGraph)
            {
                List<object?> graphs = new(items.Count);
                foreach(object? item in items)
                {
                    graphs.Add(WrapInGraph(item));
                }

                return graphs;
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

        if(isGraph && singleExpanded is not null)
        {
            return WrapInGraph(singleExpanded);
        }

        return singleExpanded;
    }

    /// <summary>
    /// Wraps an expanded value in a graph object (<c>{"@graph": [value]}</c>)
    /// for a term whose container includes <c>@graph</c>.
    /// </summary>
    private static Dictionary<string, object?> WrapInGraph(object? value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["@graph"] = new List<object?> { value }
        };
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
            //An explicit @list object expands its members (coerced by the
            //surrounding term) and re-wraps them; an explicit @set object
            //expands to its bare member list, which the caller splices into
            //the property array (the @set wrapper never survives expansion).
            if(item.TryGetProperty("@list", out JsonLdNode listValue))
            {
                List<object?> listMembers = await ExpandMembersAsync(
                    listValue, activeContext, baseUrl, resolver, parser, termDef, cancellationToken).ConfigureAwait(false);

                return new Dictionary<string, object?>(StringComparer.Ordinal) { ["@list"] = listMembers };
            }
            if(item.TryGetProperty("@set", out JsonLdNode setValue))
            {
                return await ExpandMembersAsync(
                    setValue, activeContext, baseUrl, resolver, parser, termDef, cancellationToken).ConfigureAwait(false);
            }

            return await ExpandObjectAsync(item, activeContext, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }

        if(item.Kind == JsonLdNodeKind.Null)
        {
            return null;
        }

        return ScalarToValueObject(item, termDef, activeContext);
    }

    /// <summary>
    /// Expands the members of an explicit <c>@list</c> or <c>@set</c> value
    /// into a flat list, coercing each member by the surrounding term and
    /// dropping nulls. A nested <c>@set</c> member (itself expanding to a
    /// list) is spliced in, so sets of sets collapse per the spec.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<List<object?>> ExpandMembersAsync(
        JsonLdNode value,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        CancellationToken cancellationToken)
    {
        List<object?> members = new();
        if(value.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in value.EnumerateArray())
            {
                await AddExpandedMemberAsync(
                    members, item, activeContext, baseUrl, resolver, parser, termDef, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await AddExpandedMemberAsync(
                members, value, activeContext, baseUrl, resolver, parser, termDef, cancellationToken).ConfigureAwait(false);
        }

        return members;
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask AddExpandedMemberAsync(
        List<object?> members,
        JsonLdNode item,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        TermDefinition? termDef,
        CancellationToken cancellationToken)
    {
        object? expanded = await ExpandPropertyItemAsync(
            item, activeContext, baseUrl, resolver, parser, termDef, cancellationToken).ConfigureAwait(false);
        if(expanded is null)
        {
            return;
        }

        if(expanded is IReadOnlyList<object?> nested)
        {
            members.AddRange(nested);
            return;
        }

        members.Add(expanded);
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

        //Map containers are expanded with their keys in lexicographic order,
        //matching the expanded-form convention the W3C fixtures use.
        List<KeyValuePair<string, JsonLdNode>> entries = new();
        foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
        {
            entries.Add(entry);
        }
        entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach(KeyValuePair<string, JsonLdNode> entry in entries)
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
            //The discriminator type is prepended to the node's @type set,
            //which is always an array in expanded form.
            List<object?> types = new() { expandedType ?? mapKey };
            if(nodeObject.TryGetValue("@type", out object? existingType))
            {
                if(existingType is IReadOnlyList<object?> existingList)
                {
                    types.AddRange(existingList);
                }
                else if(existingType is not null)
                {
                    types.Add(existingType);
                }
            }
            nodeObject["@type"] = types;
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

    private static List<object?>? ExpandTypeValue(JsonLdNode value, LinkedDataContext activeContext)
    {
        //A node object's @type expands to an array of IRIs per the W3C
        //JSON-LD 1.1 expanded-form convention, even for a single type.
        if(value.Kind == JsonLdNodeKind.String)
        {
            string raw = value.GetString();
            return new List<object?> { activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw };
        }
        if(value.Kind == JsonLdNodeKind.Array)
        {
            List<object?> items = new();
            foreach(JsonLdNode item in value.EnumerateArray())
            {
                if(item.Kind == JsonLdNodeKind.String)
                {
                    string raw = item.GetString();
                    items.Add(activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw);
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
    private static Dictionary<string, object?>? ScalarToValueObject(
        JsonLdNode element, TermDefinition? termDef, LinkedDataContext activeContext)
    {
        //A string under a term coercing to @id or @vocab expands to a node
        //reference {"@id": <expanded IRI>}, not a value object. @id is
        //document-relative; @vocab is vocabulary-relative.
        if(element.Kind == JsonLdNodeKind.String && termDef?.TypeMapping is { } coercion)
        {
            if(string.Equals(coercion, "@id", StringComparison.Ordinal))
            {
                //A keyword-like value (e.g. "@ignoreMe") expands to null and
                //is preserved as such, not echoed back verbatim.
                string raw = element.GetString();
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["@id"] = activeContext.ExpandIri(raw, documentRelative: true)
                };
            }
            if(string.Equals(coercion, "@vocab", StringComparison.Ordinal))
            {
                string raw = element.GetString();
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["@id"] = activeContext.ExpandIri(raw, vocab: true, documentRelative: true)
                };
            }
        }

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

        if(termDef?.TypeMapping is { } typeMapping && !IriUtils.IsKeyword(typeMapping))
        {
            valueObject["@type"] = typeMapping;
        }
        else if(element.Kind == JsonLdNodeKind.String)
        {
            //Language coercion applies to plain string values that are not
            //typed. A term carrying an explicit @language overrides (and may
            //null out) the context's default; otherwise the default applies.
            string? language = termDef is { HasLanguageMapping: true }
                ? termDef.LanguageMapping
                : activeContext.DefaultLanguage;
            if(language is not null)
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
    /// <summary>
    /// Whether the object is a value object: some key expands to
    /// <c>@value</c>, directly or through a keyword alias.
    /// </summary>
    private static bool IsValueObject(JsonLdNode obj, LinkedDataContext activeContext)
    {
        foreach(KeyValuePair<string, JsonLdNode> entry in obj.EnumerateObject())
        {
            if(string.Equals(activeContext.ExpandIri(entry.Key, vocab: true), "@value", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, object?>? ExpandValueObject(JsonLdNode value, LinkedDataContext activeContext)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
        {
            //Keys are matched on their expanded keyword so aliases of
            //@value/@type/@language/@index/@direction are honoured.
            string? expandedKey = activeContext.ExpandIri(entry.Key, vocab: true);
            switch(expandedKey)
            {
                case "@value":
                {
                    //A value object whose @value is JSON null carries no
                    //value and is dropped entirely.
                    if(entry.Value.Kind == JsonLdNodeKind.Null)
                    {
                        return null;
                    }

                    result["@value"] = entry.Value.Kind switch
                    {
                        JsonLdNodeKind.String => entry.Value.GetString(),
                        JsonLdNodeKind.True => (object?)true,
                        JsonLdNodeKind.False => (object?)false,
                        JsonLdNodeKind.Number => ParseNumber(entry.Value.GetRawNumber()),
                        _ => null
                    };
                    break;
                }
                case "@type":
                {
                    //A value object's @type is a single IRI, not an array.
                    if(entry.Value.Kind == JsonLdNodeKind.String)
                    {
                        string raw = entry.Value.GetString();
                        result["@type"] = activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw;
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
                case "@index":
                {
                    if(entry.Value.Kind == JsonLdNodeKind.String)
                    {
                        result["@index"] = entry.Value.GetString();
                    }
                    break;
                }
                case "@direction":
                {
                    if(entry.Value.Kind == JsonLdNodeKind.String)
                    {
                        result["@direction"] = entry.Value.GetString();
                    }
                    break;
                }
                default:
                {
                    //A value object admits no other keys; drop them.
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Adds a value array under a key, merging with any values already
    /// present so multiple keys expanding to the same IRI accumulate.
    /// </summary>
    private static void AddValues(Dictionary<string, object?> target, string key, IReadOnlyList<object?> values)
    {
        if(target.TryGetValue(key, out object? existing) && existing is IReadOnlyList<object?> existingList)
        {
            List<object?> merged = new(existingList);
            merged.AddRange(values);
            target[key] = merged;

            return;
        }

        target[key] = values;
    }

    /// <summary>
    /// Adds reverse-property values into the node's <c>@reverse</c> map,
    /// creating it on first use and merging repeated reverse IRIs.
    /// </summary>
    private static void AddReverseValues(Dictionary<string, object?> target, string reverseIri, IReadOnlyList<object?> values)
    {
        if(target.TryGetValue("@reverse", out object? existing) && existing is Dictionary<string, object?> reverseMap)
        {
            AddValues(reverseMap, reverseIri, values);

            return;
        }

        Dictionary<string, object?> created = new(StringComparer.Ordinal);
        AddValues(created, reverseIri, values);
        target["@reverse"] = created;
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
