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
            //Record the document base as the original base URL too, so a
            //full context reset (@context: null) can restore it per spec.
            activeContext = activeContext.WithBaseIri(baseUrl).WithOriginalBaseUrl(baseUrl);
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
        //A non-propagating context (a type-scoped context, or one carrying
        //@propagate: false) from an ancestor is reverted on entry to a new
        //node object, so it does not leak into nested nodes. Value objects
        //and bare node references (@id only) do not establish a new node
        //scope and keep the context unchanged.
        if(activeContext.PreviousContext is { } revertedContext
            && !IsValueObject(obj, activeContext)
            && !IsSingleIdReference(obj, activeContext))
        {
            activeContext = revertedContext;
        }

        //Apply local @context (already applied at document root; this
        //handles embedded @context on nested nodes). A non-propagating
        //embedded context records the pre-application context so the next
        //nested node reverts to it.
        if(obj.TryGetProperty("@context", out JsonLdNode localContext))
        {
            LinkedDataContext beforeEmbedded = activeContext;
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, localContext, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
            if(!activeContext.Propagate && activeContext.PreviousContext is null)
            {
                activeContext = activeContext.WithPreviousContext(beforeEmbedded);
            }
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
        if(TryFindKeywordValue(obj, activeContext, "@list", out JsonLdNode topLevelList))
        {
            EnsureValidListOrSetObject(obj, activeContext, "@list");
            List<object?> listMembers = await ExpandMembersAsync(
                topLevelList, activeContext, baseUrl, resolver, parser, termDef: null, nestArraysAsLists: true, cancellationToken).ConfigureAwait(false);

            return new Dictionary<string, object?>(StringComparer.Ordinal) { ["@list"] = listMembers };
        }
        if(TryFindKeywordValue(obj, activeContext, "@set", out JsonLdNode topLevelSet))
        {
            EnsureValidListOrSetObject(obj, activeContext, "@set");
            return await ExpandMembersAsync(
                topLevelSet, activeContext, baseUrl, resolver, parser, termDef: null, nestArraysAsLists: false, cancellationToken).ConfigureAwait(false);
        }

        //Type-scoped contexts: when the node has @type, apply each
        //matching term's ScopedContextEntries in alphabetical order of
        //type IRIs per W3C JSON-LD 1.1 §4.1.4 step 8. The application
        //updates the active context for the rest of this node. Type-scoped
        //contexts are non-propagating, so the pre-application context is
        //recorded for nested nodes to revert to.
        //
        //The @type values themselves are expanded against the context as it
        //stands *before* the type-scoped contexts are applied (a type-scoped
        //context may nullify or override the vocabulary), so that context is
        //preserved for the @type key below.
        LinkedDataContext typeExpansionContext = activeContext;
        if(TryFindKeywordValue(obj, activeContext, "@type", out JsonLdNode typeNode))
        {
            LinkedDataContext beforeTypeScoped = activeContext;
            LinkedDataContext afterTypeScoped = await ApplyTypeScopedAsync(
                activeContext, typeNode, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);

            //A type-scoped context is non-propagating unless it set
            //@propagate: true (reflected in its Propagate flag); a
            //non-propagating one records the pre-application context so
            //nested nodes revert to it.
            activeContext = !ReferenceEquals(afterTypeScoped, beforeTypeScoped) && !afterTypeScoped.Propagate
                ? afterTypeScoped.WithPreviousContext(beforeTypeScoped)
                : afterTypeScoped;
        }

        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        await ExpandPropertiesIntoAsync(
            obj, result, activeContext, typeExpansionContext, baseUrl, resolver, parser, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Expands the properties of <paramref name="obj"/> into
    /// <paramref name="result"/> using the node's active context. Factored
    /// out so an <c>@nest</c> entry can lift a nested object's properties
    /// into the same node (same scope, no new node object).
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask ExpandPropertiesIntoAsync(
        JsonLdNode obj,
        Dictionary<string, object?> result,
        LinkedDataContext activeContext,
        LinkedDataContext typeExpansionContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        //@nest entries are processed only after every regular key, so a
        //property that appears both at the node and under an @nest accumulates
        //its base values first and the nested values afterward. Multiple @nest
        //aliases are processed in lexicographic key order.
        List<KeyValuePair<string, JsonLdNode>> deferredNests = new();

        //Keys are processed in lexicographic order (W3C JSON-LD 1.1 §4.3.2 /
        //jsonld.js): this is observable only where several keys fold into the
        //same output array (e.g. an aliased `type` and the keyword `@type`),
        //since the result object's own key order is not significant.
        List<KeyValuePair<string, JsonLdNode>> orderedProperties = new();
        foreach(KeyValuePair<string, JsonLdNode> property in obj.EnumerateObject())
        {
            orderedProperties.Add(property);
        }
        orderedProperties.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach(KeyValuePair<string, JsonLdNode> property in orderedProperties)
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
                    if(value.Kind != JsonLdNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidIdValue,
                            "A node object's @id value must be a string.");
                    }

                    if(result.ContainsKey("@id"))
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.CollidingKeywords,
                            "Two keys expanded to @id in the same node object.");
                    }

                    //A keyword-like @id (e.g. "@ignoreMe") expands to null
                    //and is preserved as such rather than echoed verbatim.
                    string raw = value.GetString();
                    result["@id"] = activeContext.ExpandIri(raw, documentRelative: true);
                    continue;
                }
                case "@type":
                {
                    //@type IRIs expand against the pre-type-scoped context.
                    //Multiple keys expanding to @type (e.g. @type plus an
                    //alias) accumulate into one type array.
                    if(ExpandTypeValue(value, typeExpansionContext) is { } types)
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
                    if(value.Kind != JsonLdNodeKind.Object)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidReverseValue,
                            "A node object's @reverse value must be a map.");
                    }

                    Dictionary<string, object?> reverseMap = await ExpandReverseAsync(
                        value, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);
                    if(reverseMap.Count > 0)
                    {
                        result["@reverse"] = reverseMap;
                    }

                    continue;
                }
                case "@index":
                {
                    //A node-level @index keeps its string value verbatim.
                    if(value.Kind != JsonLdNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidTermDefinition,
                            "An @index value must be a string.");
                    }

                    result["@index"] = value.GetString();
                    continue;
                }
                case "@nest":
                {
                    //@nest lifts a nested object's properties into the
                    //current node (same scope). Defer until all regular keys
                    //are processed so nested values append after base values.
                    deferredNests.Add(new KeyValuePair<string, JsonLdNode>(key, value));
                    continue;
                }
                case "@included":
                {
                    //@included carries additional node objects, collected
                    //into a flat array; multiple @included keys fold together.
                    object? expandedIncluded = await ExpandElementAsync(
                        value, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);
                    if(expandedIncluded is null)
                    {
                        continue;
                    }

                    List<object?> includedNodes = expandedIncluded is IReadOnlyList<object?> includedList
                        ? new List<object?>(includedList)
                        : new List<object?> { expandedIncluded };
                    foreach(object? node in includedNodes)
                    {
                        if(node is IReadOnlyDictionary<string, object?> map
                            && (map.ContainsKey("@value") || map.ContainsKey("@list") || map.ContainsKey("@set")))
                        {
                            throw new JsonLdProcessingException(
                                JsonLdErrorCode.InvalidIncludedValue,
                                "An @included value must be a node object, not a value, list, or set object.");
                        }
                    }

                    AddValues(result, "@included", includedNodes);
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
                            activeContext, scopedEntries, baseUrl, resolver, parser, cancellationToken, overrideProtected: true)
                            .ConfigureAwait(false);

                        //A property-scoped context propagates into the
                        //property's nested nodes (unlike a type-scoped one).
                        //When a type-scoped context is active it is reverted
                        //on entry to a nested node, so the property-scoped
                        //context is re-expressed on the reverted base as the
                        //revert target; that way it survives the revert
                        //rather than being discarded with the type-scoped
                        //context. With no type-scoped context active, the
                        //nested node simply must not revert.
                        if(activeContext.PreviousContext is { } revertBase)
                        {
                            propertyContext = propertyContext.WithPreviousContext(
                                await ApplyScopedEntriesAsync(
                                    revertBase, scopedEntries, baseUrl, resolver, parser, cancellationToken, overrideProtected: true)
                                    .ConfigureAwait(false));
                        }
                        else
                        {
                            propertyContext = propertyContext.WithPreviousContext(null);
                        }
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
                    //A reverse property may only take node references, never
                    //value objects or list objects.
                    if(termDef?.ReverseProperty is { } reverseIri)
                    {
                        EnsureNoReversePropertyValueObjects(valueArray);
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

        deferredNests.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
        foreach(KeyValuePair<string, JsonLdNode> nest in deferredNests)
        {
            await ExpandNestIntoAsync(
                nest.Value, result, activeContext, typeExpansionContext, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }
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
        //A term coercing to @json keeps the entire value verbatim as a JSON
        //literal (objects, arrays, scalars and null preserved as-is), never
        //expanded as JSON-LD.
        if(string.Equals(termDef?.TypeMapping, "@json", StringComparison.Ordinal))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["@value"] = ConvertJsonLiteral(value),
                ["@type"] = "@json"
            };
        }

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
                    items, item, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: isList, cancellationToken)
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

        if(isList && singleExpanded is not null && !IsExpandedListObject(singleExpanded))
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
    /// Indicates whether an already-expanded value is a list object (a map
    /// carrying an <c>@list</c> key). A <c>@list</c>-container term whose
    /// value already expanded to a list object must not be wrapped in a
    /// second list layer (W3C JSON-LD 1.1 §4.3.2 — expanded value is only
    /// converted to a list object when it is not already one).
    /// </summary>
    private static bool IsExpandedListObject(object? value)
    {
        return value is Dictionary<string, object?> map && map.ContainsKey("@list");
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
            if(TryFindKeywordValue(item, activeContext, "@list", out JsonLdNode listValue))
            {
                EnsureValidListOrSetObject(item, activeContext, "@list");
                List<object?> listMembers = await ExpandMembersAsync(
                    listValue, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: true, cancellationToken).ConfigureAwait(false);

                return new Dictionary<string, object?>(StringComparer.Ordinal) { ["@list"] = listMembers };
            }
            if(TryFindKeywordValue(item, activeContext, "@set", out JsonLdNode setValue))
            {
                EnsureValidListOrSetObject(item, activeContext, "@set");
                return await ExpandMembersAsync(
                    setValue, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: false, cancellationToken).ConfigureAwait(false);
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
        bool nestArraysAsLists,
        CancellationToken cancellationToken)
    {
        List<object?> members = new();
        if(value.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in value.EnumerateArray())
            {
                await AddExpandedMemberAsync(
                    members, item, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await AddExpandedMemberAsync(
                members, value, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists, cancellationToken).ConfigureAwait(false);
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
        bool nestArraysAsLists,
        CancellationToken cancellationToken)
    {
        //A member of a @list container that is itself an array becomes a
        //nested list object; the nesting recurses (an array of arrays of
        //arrays produces correspondingly nested @list objects).
        if(nestArraysAsLists && item.Kind == JsonLdNodeKind.Array)
        {
            List<object?> innerMembers = await ExpandMembersAsync(
                item, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: true, cancellationToken).ConfigureAwait(false);
            members.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { ["@list"] = innerMembers });
            return;
        }

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
            if(expandedKey is null)
            {
                continue;
            }

            //A reverse map describes properties of which the node is the
            //object, so its keys may not expand to keywords.
            if(IriUtils.IsKeyword(expandedKey))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidReversePropertyMap,
                    "An @reverse map may not contain a key that expands to a keyword.");
            }

            if(!expandedKey.Contains(':', StringComparison.Ordinal))
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

            if(expandedValue is null)
            {
                continue;
            }

            IReadOnlyList<object?> valueArray = WrapAsArray(expandedValue);
            EnsureNoReversePropertyValueObjects(valueArray);
            result[expandedKey] = valueArray;
        }

        return result;
    }

    /// <summary>
    /// Throws <see cref="JsonLdProcessingException"/> with the
    /// <c>invalid reverse property value</c> error when any of a reverse
    /// property's expanded values is a value object or list object — a
    /// reverse property may only point at node references.
    /// </summary>
    /// <param name="values">The expanded value array for a reverse property.</param>
    private static void EnsureNoReversePropertyValueObjects(IReadOnlyList<object?> values)
    {
        foreach(object? value in values)
        {
            if(value is IReadOnlyDictionary<string, object?> map
                && (map.ContainsKey("@value") || map.ContainsKey("@list")))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidReversePropertyValue,
                    "A reverse property's value must be a node reference, not a value or list object.");
            }
        }
    }

    /// <summary>
    /// Throws <see cref="JsonLdProcessingException"/> with the
    /// <c>invalid set or list object</c> error when a <c>@list</c> or
    /// <c>@set</c> object carries a sibling key other than the container
    /// keyword itself or <c>@index</c>.
    /// </summary>
    /// <param name="obj">The candidate list or set object.</param>
    /// <param name="activeContext">The context that expands the sibling keys.</param>
    /// <param name="container">The container keyword (<c>@list</c> or <c>@set</c>).</param>
    private static void EnsureValidListOrSetObject(JsonLdNode obj, LinkedDataContext activeContext, string container)
    {
        foreach(KeyValuePair<string, JsonLdNode> property in obj.EnumerateObject())
        {
            string? expandedKey = activeContext.ExpandIri(property.Key, vocab: true);
            if(expandedKey is null
                || string.Equals(expandedKey, container, StringComparison.Ordinal)
                || string.Equals(expandedKey, "@index", StringComparison.Ordinal))
            {
                continue;
            }

            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidSetOrListObject,
                string.Create(CultureInfo.InvariantCulture, $"A {container} object may only contain {container} and @index entries."));
        }
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
        //A map key of @none (directly or through a keyword alias) carries no
        //discriminator: its values are added without an @id/@type/@index/
        //@language marker.
        bool mapKeyIsNone = string.Equals(activeContext.ExpandIri(mapKey, vocab: true), "@none", StringComparison.Ordinal);

        //Under a @type map the key is a type whose term may carry a type-scoped
        //context; it applies (non-propagating) while expanding this key's value
        //(W3C JSON-LD 1.1 §4.3.2 / jsonld.js _expandIndexMap).
        if(isTypeMap && !mapKeyIsNone
            && activeContext.TryGetTerm(mapKey, out TermDefinition? keyTermDef)
            && keyTermDef is { ScopedContextEntries: { } keyScopedEntries })
        {
            activeContext = await ApplyScopedEntriesAsync(
                activeContext, keyScopedEntries, baseUrl, resolver, parser, cancellationToken, propagate: false)
                .ConfigureAwait(false);
        }

        //For @language container, the map key IS the language tag and
        //each item is a scalar value (or array of scalars). Build a
        //value object with @language attached.
        if(isLanguage)
        {
            if(item.Kind == JsonLdNodeKind.Null)
            {
                return null;
            }

            //Each value under a language map must be a language-tagged
            //string (or null); anything else is an invalid language map value.
            if(item.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidLanguageMapValue,
                    "A language-map value must be a string.");
            }

            Dictionary<string, object?> valueObject = new(StringComparer.Ordinal)
            {
                ["@value"] = item.GetString()
            };

            if(!mapKeyIsNone)
            {
                valueObject["@language"] = mapKey;
            }

            //A base direction (from the term, or the context default) attaches
            //to each language-map value alongside its language tag.
            string? direction = termDef is { HasDirectionMapping: true }
                ? termDef.DirectionMapping
                : activeContext.DefaultBaseDirection;
            if(direction is not null)
            {
                valueObject["@direction"] = direction;
            }

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

        //A term with a property-valued @index turns the map key into a value
        //of that property rather than an @index marker; this key is tracked so
        //the @graph wrapping below keeps it at the outer level.
        string? propertyValuedIndexKey = null;
        if(isIndex && !mapKeyIsNone)
        {
            if(termDef?.IndexMapping is { } indexProperty && !string.Equals(indexProperty, "@index", StringComparison.Ordinal))
            {
                propertyValuedIndexKey = AddPropertyValuedIndex(nodeObject, indexProperty, mapKey, activeContext);
            }
            else if(!nodeObject.ContainsKey("@index"))
            {
                //A node's own @index overrides the index-map key.
                nodeObject["@index"] = mapKey;
            }
        }

        if(isIdMap && !mapKeyIsNone && !nodeObject.ContainsKey("@id"))
        {
            //The id-map key supplies the @id only when the node object does
            //not already carry one of its own.
            string? expandedId = activeContext.ExpandIri(mapKey, documentRelative: true);
            nodeObject["@id"] = expandedId ?? mapKey;
        }

        if(isTypeMap && !mapKeyIsNone)
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
                if((isIdMap && kv.Key == "@id")
                    || (isIndex && kv.Key == "@index")
                    || (propertyValuedIndexKey is not null && string.Equals(kv.Key, propertyValuedIndexKey, StringComparison.Ordinal)))
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

    /// <summary>
    /// Implements a property-valued <c>@index</c>: the map key becomes a value
    /// of the index property on <paramref name="nodeObject"/> (prepended before
    /// any existing values), coerced by the index property's own term
    /// definition. Returns the expanded index-property IRI so the caller can
    /// keep it at the outer level of an <c>@graph</c> wrapping.
    /// </summary>
    /// <param name="nodeObject">The expanded node object receiving the property value.</param>
    /// <param name="indexProperty">The index property term (the term's <c>@index</c> value).</param>
    /// <param name="mapKey">The map key contributed as the property's value.</param>
    /// <param name="activeContext">The active context for IRI expansion and coercion.</param>
    /// <returns>The expanded index-property IRI.</returns>
    private static string AddPropertyValuedIndex(
        Dictionary<string, object?> nodeObject, string indexProperty, string mapKey, LinkedDataContext activeContext)
    {
        //A property cannot be attached to a value or list object.
        if(nodeObject.ContainsKey("@value") || nodeObject.ContainsKey("@list"))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidTermDefinition,
                "A property-valued @index cannot add a property to a value object.");
        }

        string expandedProperty = activeContext.ExpandIri(indexProperty, vocab: true) ?? indexProperty;
        TermDefinition? indexTermDef = activeContext.TryGetTerm(indexProperty, out TermDefinition? def) ? def : null;
        Dictionary<string, object?> indexValue = indexTermDef?.TypeMapping switch
        {
            "@id" => new Dictionary<string, object?>(StringComparer.Ordinal) { ["@id"] = activeContext.ExpandIri(mapKey, documentRelative: true) },
            "@vocab" => new Dictionary<string, object?>(StringComparer.Ordinal) { ["@id"] = activeContext.ExpandIri(mapKey, vocab: true, documentRelative: true) },
            _ => new Dictionary<string, object?>(StringComparer.Ordinal) { ["@value"] = mapKey }
        };

        List<object?> values = new() { indexValue };
        if(nodeObject.TryGetValue(expandedProperty, out object? existing))
        {
            if(existing is IReadOnlyList<object?> existingList)
            {
                values.AddRange(existingList);
            }
            else if(existing is not null)
            {
                values.Add(existing);
            }
        }

        nodeObject[expandedProperty] = values;

        return expandedProperty;
    }

    private static List<object?>? ExpandTypeValue(JsonLdNode value, LinkedDataContext activeContext)
    {
        //A node object's @type expands to an array of IRIs per the W3C
        //JSON-LD 1.1 expanded-form convention, even for a single type. The
        //value must be a string or an array of strings; anything else is an
        //invalid type value (a JSON null contributes no types).
        switch(value.Kind)
        {
            case JsonLdNodeKind.Null:
            {
                return null;
            }
            case JsonLdNodeKind.String:
            {
                string raw = value.GetString();
                return new List<object?> { activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw };
            }
            case JsonLdNodeKind.Array:
            {
                List<object?> items = new();
                foreach(JsonLdNode item in value.EnumerateArray())
                {
                    if(item.Kind != JsonLdNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidTypeValue,
                            "A node object's @type value must be a string or an array of strings.");
                    }

                    string raw = item.GetString();
                    items.Add(activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw);
                }

                return items;
            }
            default:
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidTypeValue,
                    "A node object's @type value must be a string or an array of strings.");
            }
        }
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
            //Native JSON numbers preserve their exact lexical form through
            //expansion (10.0 stays 10.0, not re-rendered from a parsed double);
            //canonicalisation is a toRdf concern, not an expansion one.
            JsonLdNodeKind.Number => new JsonLdJsonNumber(element.GetRawNumber()),
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
            //Language and direction coercion apply to plain (untyped) string
            //values. A term carrying an explicit @language/@direction overrides
            //(and may null out) the context's default; otherwise the default
            //applies. The two are independent — a direction can attach with no
            //language and vice versa.
            string? language = termDef is { HasLanguageMapping: true }
                ? termDef.LanguageMapping
                : activeContext.DefaultLanguage;
            if(language is not null)
            {
                valueObject["@language"] = language;
            }

            string? direction = termDef is { HasDirectionMapping: true }
                ? termDef.DirectionMapping
                : activeContext.DefaultBaseDirection;
            if(direction is not null)
            {
                valueObject["@direction"] = direction;
            }
        }

        return valueObject;
    }

    /// <summary>
    /// Expands the value of an <c>@nest</c> entry (an object or array of
    /// objects) into <paramref name="result"/>. Each nested item must be a
    /// node object (never a value object), per W3C JSON-LD 1.1.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask ExpandNestIntoAsync(
        JsonLdNode nestValue,
        Dictionary<string, object?> result,
        LinkedDataContext activeContext,
        LinkedDataContext typeExpansionContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        if(nestValue.Kind == JsonLdNodeKind.Array)
        {
            foreach(JsonLdNode item in nestValue.EnumerateArray())
            {
                await ExpandOneNestAsync(
                    item, result, activeContext, typeExpansionContext, baseUrl, resolver, parser, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        await ExpandOneNestAsync(
            nestValue, result, activeContext, typeExpansionContext, baseUrl, resolver, parser, cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask ExpandOneNestAsync(
        JsonLdNode item,
        Dictionary<string, object?> result,
        LinkedDataContext activeContext,
        LinkedDataContext typeExpansionContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        JsonLdParserDelegate parser,
        CancellationToken cancellationToken)
    {
        if(item.Kind != JsonLdNodeKind.Object || IsValueObject(item, activeContext))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidNestValue,
                "An @nest value must be a node object, not a value object or scalar.");
        }

        await ExpandPropertiesIntoAsync(
            item, result, activeContext, typeExpansionContext, baseUrl, resolver, parser, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the value of the first property whose key expands to the given
    /// keyword (directly or through a keyword alias). Used to locate an
    /// aliased <c>@type</c> so its type-scoped contexts are applied.
    /// </summary>
    /// <param name="obj">The node object.</param>
    /// <param name="activeContext">The context that expands the keys.</param>
    /// <param name="keyword">The keyword to match (e.g. <c>@type</c>).</param>
    /// <param name="value">On success, the matching property's value.</param>
    /// <returns><see langword="true"/> when a matching key is found.</returns>
    private static bool TryFindKeywordValue(JsonLdNode obj, LinkedDataContext activeContext, string keyword, out JsonLdNode value)
    {
        foreach(KeyValuePair<string, JsonLdNode> entry in obj.EnumerateObject())
        {
            if(string.Equals(activeContext.ExpandIri(entry.Key, vocab: true), keyword, StringComparison.Ordinal))
            {
                value = entry.Value;

                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>
    /// Whether the object is a value object: some key expands to a
    /// value-object-defining keyword (<c>@value</c>, or the value-only
    /// <c>@language</c>/<c>@direction</c>), directly or through an alias.
    /// </summary>
    private static bool IsValueObject(JsonLdNode obj, LinkedDataContext activeContext)
    {
        foreach(KeyValuePair<string, JsonLdNode> entry in obj.EnumerateObject())
        {
            if(activeContext.ExpandIri(entry.Key, vocab: true) is "@value" or "@language" or "@direction")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the object is a bare node reference whose only key expands to
    /// <c>@id</c> (directly or via an alias). Such references do not open a
    /// new node scope, so a non-propagating context is not reverted for them.
    /// </summary>
    private static bool IsSingleIdReference(JsonLdNode obj, LinkedDataContext activeContext)
    {
        bool sawId = false;
        foreach(KeyValuePair<string, JsonLdNode> entry in obj.EnumerateObject())
        {
            if(string.Equals(entry.Key, "@context", StringComparison.Ordinal))
            {
                continue;
            }

            if(!string.Equals(activeContext.ExpandIri(entry.Key, vocab: true), "@id", StringComparison.Ordinal))
            {
                return false;
            }

            sawId = true;
        }

        return sawId;
    }

    /// <summary>
    /// Whether the value object declares <c>@type: @json</c> (directly or
    /// via aliases of <c>@type</c> and the <c>@json</c> keyword), marking its
    /// <c>@value</c> as a verbatim JSON literal.
    /// </summary>
    private static bool IsJsonTypedValueObject(JsonLdNode value, LinkedDataContext activeContext)
    {
        foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
        {
            if(!string.Equals(activeContext.ExpandIri(entry.Key, vocab: true), "@type", StringComparison.Ordinal))
            {
                continue;
            }

            if(entry.Value.Kind == JsonLdNodeKind.String
                && string.Equals(activeContext.ExpandIri(entry.Value.GetString(), vocab: true), "@json", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates a (non-<c>@json</c>) value object against the W3C JSON-LD
    /// 1.1 expansion rules, throwing <see cref="JsonLdProcessingException"/>
    /// on a violation: a disallowed key, incompatible <c>@type</c> with
    /// <c>@language</c>/<c>@direction</c>, a non-scalar <c>@value</c>, a
    /// language- or direction-tagged non-string, a non-string
    /// <c>@language</c>/<c>@index</c>, or a <c>@type</c> that is not a single
    /// absolute IRI.
    /// </summary>
    private static void ValidateValueObject(JsonLdNode value, LinkedDataContext activeContext)
    {
        JsonLdNode valueNode = default;
        JsonLdNode typeNode = default;
        JsonLdNode languageNode = default;
        JsonLdNode indexNode = default;
        bool hasValue = false, hasType = false, hasLanguage = false, hasIndex = false, hasDirection = false;

        foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
        {
            if(string.Equals(entry.Key, "@context", StringComparison.Ordinal))
            {
                continue;
            }

            switch(activeContext.ExpandIri(entry.Key, vocab: true))
            {
                case "@value": valueNode = entry.Value; hasValue = true; break;
                case "@type": typeNode = entry.Value; hasType = true; break;
                case "@language": languageNode = entry.Value; hasLanguage = true; break;
                case "@index": indexNode = entry.Value; hasIndex = true; break;
                case "@direction": hasDirection = true; break;
                default:
                {
                    throw new JsonLdProcessingException(
                        JsonLdErrorCode.InvalidValueObject,
                        $"A value object may not contain the key '{entry.Key}'.");
                }
            }
        }

        if(hasType && (hasLanguage || hasDirection))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "A value object's @type cannot be combined with @language or @direction.");
        }

        if(hasValue && valueNode.Kind is JsonLdNodeKind.Array or JsonLdNodeKind.Object)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "A value object's @value must be a scalar or null unless typed @json.");
        }

        if((hasLanguage || hasDirection) && hasValue
            && valueNode.Kind is not (JsonLdNodeKind.String or JsonLdNodeKind.Null))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "A language- or direction-tagged value object's @value must be a string.");
        }

        if(hasLanguage && languageNode.Kind != JsonLdNodeKind.String)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "@language must be a string.");
        }

        if(hasIndex && indexNode.Kind != JsonLdNodeKind.String)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "@index must be a string.");
        }

        if(hasType)
        {
            if(typeNode.Kind != JsonLdNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidValueObject,
                    "A value object's @type must be a single string.");
            }

            string? expandedType = activeContext.ExpandIri(typeNode.GetString(), vocab: true, documentRelative: true);
            if(expandedType is null
                || (!string.Equals(expandedType, "@json", StringComparison.Ordinal)
                    && (!IriUtils.IsAbsoluteIri(expandedType) || ContainsWhitespace(expandedType))))
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidValueObject,
                    $"A value object's @type must expand to an absolute IRI; got '{typeNode.GetString()}'.");
            }
        }
    }

    /// <summary>Whether the text contains any Unicode whitespace (an IRI may not).</summary>
    /// <param name="text">The candidate IRI.</param>
    /// <returns><see langword="true"/> when whitespace is present.</returns>
    private static bool ContainsWhitespace(string text)
    {
        foreach(char character in text)
        {
            if(char.IsWhiteSpace(character))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, object?>? ExpandValueObject(JsonLdNode value, LinkedDataContext activeContext)
    {
        //A value object typed @json keeps its @value verbatim as a JSON
        //literal (objects, arrays, and null preserved), regardless of key
        //or @type aliases.
        if(IsJsonTypedValueObject(value, activeContext))
        {
            Dictionary<string, object?> jsonResult = new(StringComparer.Ordinal) { ["@type"] = "@json" };
            foreach(KeyValuePair<string, JsonLdNode> entry in value.EnumerateObject())
            {
                string? entryKey = activeContext.ExpandIri(entry.Key, vocab: true);
                if(string.Equals(entryKey, "@value", StringComparison.Ordinal))
                {
                    jsonResult["@value"] = ConvertJsonLiteral(entry.Value);
                }
                else if(string.Equals(entryKey, "@index", StringComparison.Ordinal) && entry.Value.Kind == JsonLdNodeKind.String)
                {
                    jsonResult["@index"] = entry.Value.GetString();
                }
            }

            return jsonResult;
        }

        ValidateValueObject(value, activeContext);

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

        //A value object with no @value (only @language/@direction/@index)
        //carries no value and is dropped entirely.
        if(!result.ContainsKey("@value"))
        {
            return null;
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

    /// <summary>
    /// Converts a node to a verbatim object-graph value for a <c>@json</c>
    /// literal: objects and arrays are preserved structurally, scalars and
    /// null are kept as-is. No JSON-LD expansion or coercion is applied.
    /// </summary>
    private static object? ConvertJsonLiteral(JsonLdNode node)
    {
        switch(node.Kind)
        {
            case JsonLdNodeKind.Object:
            {
                Dictionary<string, object?> map = new(StringComparer.Ordinal);
                foreach(KeyValuePair<string, JsonLdNode> entry in node.EnumerateObject())
                {
                    map[entry.Key] = ConvertJsonLiteral(entry.Value);
                }

                return map;
            }
            case JsonLdNodeKind.Array:
            {
                List<object?> items = new();
                foreach(JsonLdNode item in node.EnumerateArray())
                {
                    items.Add(ConvertJsonLiteral(item));
                }

                return items;
            }
            case JsonLdNodeKind.String:
            {
                return node.GetString();
            }
            case JsonLdNodeKind.True:
            {
                return true;
            }
            case JsonLdNodeKind.False:
            {
                return false;
            }
            case JsonLdNodeKind.Number:
            {
                //A @json literal preserves the number's exact lexical form.
                return new JsonLdJsonNumber(node.GetRawNumber());
            }
            default:
            {
                return null;
            }
        }
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

            //Type-scoped contexts are non-propagating by default; an
            //explicit @propagate: true inside the scoped context re-enables
            //propagation (honoured downstream via the result's Propagate).
            running = await ApplyScopedEntriesAsync(
                running, entriesForType, baseUrl, resolver, parser, cancellationToken, propagate: false)
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
        CancellationToken cancellationToken,
        bool propagate = true,
        bool overrideProtected = false)
    {
        return await JsonLdScopedContextHelper.ApplyAsync(
            activeContext, entries, baseUrl, resolver, parser, cancellationToken, propagate, overrideProtected)
            .ConfigureAwait(false);
    }
}
