using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Expands a JSON-LD document to the W3C JSON-LD 1.1 expanded form as an
/// object graph. Complements <see cref="JsonLdExpander.ExtractQuadsAsync"/>
/// which produces RDF quads; this method produces the spec-defined
/// expanded JSON-LD tree shape, suitable for re-compaction against a
/// different context via <see cref="JsonLdCompactor.CompactAsync(JsonNode, LinkedDataContext, string, System.Threading.CancellationToken)"/>.
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
/// expanded-form convention. Use <see cref="JsonLdCompactor.CompactAsync(JsonNode, LinkedDataContext, string, System.Threading.CancellationToken)"/>
/// on the result via <see cref="ObjectGraphJsonLdAdapter.Wrap"/>.
/// </para>
/// </remarks>
public static class JsonLdExpansionTree
{
    /// <summary>
    /// The frame-expansion mode for the current expansion run (W3C JSON-LD 1.1
    /// API, the <c>frameExpansion</c>/<c>isFrame</c> flag): when set, the
    /// expander keeps empty-object wildcards, framing keywords, and free-floating
    /// nodes, and relaxes the <c>@id</c>/<c>@type</c>/<c>@value</c> scalar
    /// constraints. It is an ambient per-run flag (set once at the public entry
    /// and read at the handful of decision points) rather than a parameter
    /// threaded through every recursive method; <see cref="AsyncLocal{T}"/>
    /// scopes it to the executing expansion so parallel runs do not interfere.
    /// </summary>
    private static readonly System.Threading.AsyncLocal<bool> frameExpansionScope = new();

    /// <summary>Gets a value indicating whether the current expansion run is in frame-expansion mode.</summary>
    private static bool FrameExpansion => frameExpansionScope.Value;

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
    public static ValueTask<IReadOnlyList<object?>> ExpandAsync(
        JsonNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken = default)
    {
        return ExpandAsync(document, baseUrl, resolver, parser, expandContext: null, cancellationToken);
    }

    /// <summary>
    /// Expands a JSON-LD document, first initializing the active context from a
    /// caller-supplied <paramref name="expandContext"/> (the JSON-LD 1.1 API
    /// <c>expandContext</c> option) before the document's own <c>@context</c>.
    /// </summary>
    /// <param name="document">The root document node.</param>
    /// <param name="baseUrl">Optional base URL for document-relative IRIs.</param>
    /// <param name="resolver">Delegate that resolves remote <c>@context</c> URLs.</param>
    /// <param name="parser">Delegate that parses fetched context bytes.</param>
    /// <param name="expandContext">A context value used to seed the active context, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The expanded document as an object graph.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    public static ValueTask<IReadOnlyList<object?>> ExpandAsync(
        JsonNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        JsonNode? expandContext,
        CancellationToken cancellationToken = default)
    {
        frameExpansionScope.Value = false;
        return ExpandInternalAsync(document, baseUrl, resolver, parser, expandContext, cancellationToken);
    }

    /// <summary>
    /// Expands a document in frame-expansion mode (the JSON-LD 1.1 API
    /// <c>frameExpansion</c> option) when <paramref name="frameExpansion"/> is
    /// set: framing keywords, empty-object wildcards, and free-floating nodes
    /// are retained for the framing algorithm.
    /// </summary>
    /// <param name="document">The frame (or document) node to expand.</param>
    /// <param name="baseUrl">Optional base URL for document-relative IRIs.</param>
    /// <param name="resolver">Delegate that resolves remote <c>@context</c> URLs.</param>
    /// <param name="parser">Delegate that parses fetched context bytes.</param>
    /// <param name="expandContext">A context value used to seed the active context, or <see langword="null"/>.</param>
    /// <param name="frameExpansion">Whether to expand in frame mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The expanded document as an object graph.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    public static ValueTask<IReadOnlyList<object?>> ExpandAsync(
        JsonNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        JsonNode? expandContext,
        bool frameExpansion,
        CancellationToken cancellationToken = default)
    {
        frameExpansionScope.Value = frameExpansion;
        return ExpandInternalAsync(document, baseUrl, resolver, parser, expandContext, cancellationToken);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<IReadOnlyList<object?>> ExpandInternalAsync(
        JsonNode document,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        JsonNode? expandContext,
        CancellationToken cancellationToken)
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

        //The expandContext API option initializes the active context before
        //the document is processed (W3C JSON-LD 1.1 API §"expandContext"); the
        //document's own @context, if any, then extends it.
        if(expandContext is { } seedContext)
        {
            activeContext = await ContextProcessor.ProcessAsync(
                activeContext, seedContext, baseUrl, resolver, parser, cancellationToken).ConfigureAwait(false);
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
            && onlyGraph.TryGetValue(JsonLdKeywords.Graph, out object? graphValue))
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
    /// <summary>Expands a frame value object's non-string <c>@type</c>: an empty object is a wildcard, an array becomes its expanded IRIs (wildcard members kept).</summary>
    /// <param name="value">The frame <c>@type</c> value.</param>
    /// <param name="activeContext">The active context.</param>
    /// <returns>The expanded <c>@type</c> pattern.</returns>
    private static object? ExpandFrameTypeValue(JsonNode value, LinkedDataContext activeContext)
    {
        if(value.Kind == JsonNodeKind.Array)
        {
            List<object?> types = new();
            foreach(JsonNode item in value.EnumerateArray())
            {
                if(item.Kind == JsonNodeKind.String)
                {
                    string raw = item.GetString();
                    types.Add(activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw);
                }
                else if(item.Kind == JsonNodeKind.Object && IsEmptyObject(item))
                {
                    types.Add(new Dictionary<string, object?>(StringComparer.Ordinal));
                }
            }

            return types;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>Normalises a BCP47 language tag to lower case, the canonical form JSON-LD expansion stores.</summary>
    /// <param name="language">The language tag.</param>
    /// <returns>The lower-cased tag.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "BCP47 language subtags are canonically lower-case per the JSON-LD / i18n specifications.")]
    private static string LowercaseLanguage(string language)
    {
        return language.ToLowerInvariant();
    }

    /// <summary>Whether a node is an object with no members (a frame wildcard).</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when an empty object.</returns>
    private static bool IsEmptyObject(JsonNode node)
    {
        if(node.Kind != JsonNodeKind.Object)
        {
            return false;
        }

        foreach(KeyValuePair<string, JsonNode> _ in node.EnumerateObject())
        {
            return false;
        }

        return true;
    }

    /// <summary>Materializes a frame's non-scalar <c>@value</c> (wildcard object or array) into the object-graph form the framer matches against.</summary>
    /// <param name="node">The frame <c>@value</c> node.</param>
    /// <returns>The materialized value.</returns>
    private static object? MaterializeFrameValue(JsonNode node)
    {
        return node.Kind switch
        {
            JsonNodeKind.Null => null,
            JsonNodeKind.String => node.GetString(),
            JsonNodeKind.True => true,
            JsonNodeKind.False => false,
            JsonNodeKind.Number => new JsonLdJsonNumber(node.GetRawNumber()),
            JsonNodeKind.Array => MaterializeFrameArray(node),
            JsonNodeKind.Object => MaterializeFrameObject(node),
            _ => null
        };
    }

    /// <summary>Materializes a frame array value.</summary>
    /// <param name="node">The array node.</param>
    /// <returns>The materialized list.</returns>
    private static List<object?> MaterializeFrameArray(JsonNode node)
    {
        List<object?> items = new();
        foreach(JsonNode item in node.EnumerateArray())
        {
            items.Add(MaterializeFrameValue(item));
        }

        return items;
    }

    /// <summary>Materializes a frame object value.</summary>
    /// <param name="node">The object node.</param>
    /// <returns>The materialized map.</returns>
    private static Dictionary<string, object?> MaterializeFrameObject(JsonNode node)
    {
        Dictionary<string, object?> map = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> member in node.EnumerateObject())
        {
            map[member.Key] = MaterializeFrameValue(member.Value);
        }

        return map;
    }

    /// <summary>Expands a frame's non-string <c>@id</c> value: an empty object stays a wildcard, an array becomes the expanded IRIs (both as an array).</summary>
    /// <param name="value">The frame <c>@id</c> value.</param>
    /// <param name="activeContext">The active context.</param>
    /// <returns>The expanded <c>@id</c> array.</returns>
    private static List<object?> ExpandFrameIdValue(JsonNode value, LinkedDataContext activeContext)
    {
        if(value.Kind == JsonNodeKind.Array)
        {
            List<object?> ids = new();
            foreach(JsonNode item in value.EnumerateArray())
            {
                if(item.Kind == JsonNodeKind.String)
                {
                    ids.Add(activeContext.ExpandIri(item.GetString(), documentRelative: true));
                }
            }

            return ids;
        }

        //An empty object is a wildcard @id pattern (matches any node with an @id).
        return new List<object?> { new Dictionary<string, object?>(StringComparer.Ordinal) };
    }

    private static List<object?> DropFreeFloating(IReadOnlyList<object?> nodes)
    {
        //Frame expansion keeps free-floating nodes (a frame's wildcard/@id-only entries must survive).
        if(FrameExpansion)
        {
            return new List<object?>(nodes);
        }

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
        if(map.ContainsKey(JsonLdKeywords.Value) || map.ContainsKey(JsonLdKeywords.List))
        {
            return true;
        }

        return map.Count == 1 && map.ContainsKey(JsonLdKeywords.Id);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandElementAsync(
        JsonNode element,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch(element.Kind)
        {
            case JsonNodeKind.Null:
            {
                return null;
            }
            case JsonNodeKind.Array:
            {
                List<object?> result = new();
                foreach(JsonNode item in element.EnumerateArray())
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
            case JsonNodeKind.Object:
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
        JsonNode obj,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken,
        bool suppressContextRevert = false)
    {
        //A non-propagating context (a type-scoped context, or one carrying
        //@propagate: false) from an ancestor is reverted on entry to a new
        //node object, so it does not leak into nested nodes. Value objects
        //and bare node references (@id only) do not establish a new node
        //scope and keep the context unchanged.
        //A node that is the direct value of a property whose non-propagating
        //scoped context was just applied keeps that context; only its nested
        //nodes revert (suppressContextRevert is set for that first node).
        if(!suppressContextRevert
            && activeContext.PreviousContext is { } revertedContext
            && !IsValueObject(obj, activeContext)
            && !IsSingleIdReference(obj, activeContext))
        {
            activeContext = revertedContext;
        }

        //Apply local @context (already applied at document root; this
        //handles embedded @context on nested nodes). A non-propagating
        //embedded context records the pre-application context so the next
        //nested node reverts to it.
        if(obj.TryGetProperty(JsonLdKeywords.Context, out JsonNode localContext))
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
        if(TryFindKeywordValue(obj, activeContext, JsonLdKeywords.List, out JsonNode topLevelList))
        {
            EnsureValidListOrSetObject(obj, activeContext, JsonLdKeywords.List);
            List<object?> listMembers = await ExpandMembersAsync(
                topLevelList, activeContext, baseUrl, resolver, parser, termDef: null, nestArraysAsLists: true, cancellationToken).ConfigureAwait(false);

            Dictionary<string, object?> listObject = new(StringComparer.Ordinal) { [JsonLdKeywords.List] = listMembers };

            //A list object preserves a sibling @index (a node-level @index keeps its verbatim string value).
            if(TryFindKeywordValue(obj, activeContext, JsonLdKeywords.Index, out JsonNode listIndex) && listIndex.Kind == JsonNodeKind.String)
            {
                listObject[JsonLdKeywords.Index] = listIndex.GetString();
            }

            return listObject;
        }
        if(TryFindKeywordValue(obj, activeContext, JsonLdKeywords.Set, out JsonNode topLevelSet))
        {
            EnsureValidListOrSetObject(obj, activeContext, JsonLdKeywords.Set);
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
        if(TryFindKeywordValue(obj, activeContext, JsonLdKeywords.Type, out JsonNode typeNode))
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
        JsonNode obj,
        Dictionary<string, object?> result,
        LinkedDataContext activeContext,
        LinkedDataContext typeExpansionContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken)
    {
        //@nest entries are processed only after every regular key, so a
        //property that appears both at the node and under an @nest accumulates
        //its base values first and the nested values afterward. Multiple @nest
        //aliases are processed in lexicographic key order.
        List<KeyValuePair<string, JsonNode>> deferredNests = new();

        //Keys are processed in lexicographic order (W3C JSON-LD 1.1 §4.3.2):
        //this is observable only where several keys fold into the same output
        //array (e.g. an aliased `type` and the keyword `@type`), since the
        //result object's own key order is not significant.
        List<KeyValuePair<string, JsonNode>> orderedProperties = new();
        foreach(KeyValuePair<string, JsonNode> property in obj.EnumerateObject())
        {
            orderedProperties.Add(property);
        }
        orderedProperties.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach(KeyValuePair<string, JsonNode> property in orderedProperties)
        {
            string key = property.Key;
            JsonNode value = property.Value;

            if(string.Equals(key, JsonLdKeywords.Context, StringComparison.Ordinal))
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
                case var k when JsonLdKeywords.IsId(k):
                {
                    //In frame-expansion mode @id may be a wildcard ({}) or an array of IRIs, kept as an array.
                    if(FrameExpansion && value.Kind != JsonNodeKind.String)
                    {
                        result[JsonLdKeywords.Id] = ExpandFrameIdValue(value, activeContext);
                        continue;
                    }

                    if(value.Kind != JsonNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidIdValue,
                            "A node object's @id value must be a string.");
                    }

                    if(result.ContainsKey(JsonLdKeywords.Id))
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.CollidingKeywords,
                            "Two keys expanded to @id in the same node object.");
                    }

                    //A keyword-like @id (e.g. "@ignoreMe") expands to null
                    //and is preserved as such rather than echoed verbatim.
                    string raw = value.GetString();
                    object? expandedId = activeContext.ExpandIri(raw, documentRelative: true);
                    result[JsonLdKeywords.Id] = FrameExpansion ? new List<object?> { expandedId } : expandedId;
                    continue;
                }
                case var k when JsonLdKeywords.IsType(k):
                {
                    //@type IRIs expand against the pre-type-scoped context.
                    //Multiple keys expanding to @type (e.g. @type plus an
                    //alias) accumulate into one type array.
                    if(ExpandTypeValue(value, typeExpansionContext) is { } types)
                    {
                        AddValues(result, JsonLdKeywords.Type, types);
                    }
                    continue;
                }
                case var k when JsonLdKeywords.IsGraph(k):
                {
                    object? expandedGraph = await ExpandElementAsync(
                        value, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);
                    if(expandedGraph is IReadOnlyList<object?> graphList)
                    {
                        result[JsonLdKeywords.Graph] = DropFreeFloating(graphList);
                    }
                    else if(expandedGraph is not null)
                    {
                        result[JsonLdKeywords.Graph] = DropFreeFloating(new List<object?> { expandedGraph });
                    }

                    continue;
                }
                case var k when JsonLdKeywords.IsReverse(k):
                {
                    //@reverse: the value is an object whose properties are
                    //expanded normally but stored under the @reverse key
                    //rather than at node level. RDF projection swaps
                    //subject/object for these.
                    if(value.Kind != JsonNodeKind.Object)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidReverseValue,
                            "A node object's @reverse value must be a map.");
                    }

                    await ExpandReverseIntoAsync(
                        result, value, activeContext, baseUrl, resolver, parser, cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }
                case var k when JsonLdKeywords.IsIndex(k):
                {
                    //A node-level @index keeps its string value verbatim.
                    if(value.Kind != JsonNodeKind.String)
                    {
                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidTermDefinition,
                            "An @index value must be a string.");
                    }

                    result[JsonLdKeywords.Index] = value.GetString();
                    continue;
                }
                case var k when JsonLdKeywords.IsNest(k):
                {
                    //@nest lifts a nested object's properties into the
                    //current node (same scope). Defer until all regular keys
                    //are processed so nested values append after base values.
                    deferredNests.Add(new KeyValuePair<string, JsonNode>(key, value));
                    continue;
                }
                case var k when JsonLdKeywords.IsIncluded(k):
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
                            && (map.ContainsKey(JsonLdKeywords.Value) || map.ContainsKey(JsonLdKeywords.List) || map.ContainsKey(JsonLdKeywords.Set)))
                        {
                            throw new JsonLdProcessingException(
                                JsonLdErrorCode.InvalidIncludedValue,
                                "An @included value must be a node object, not a value, list, or set object.");
                        }
                    }

                    AddValues(result, JsonLdKeywords.Included, includedNodes);
                    continue;
                }
                default:
                {
                    //JSON-LD keywords other than the ones handled above
                    //are passed through unchanged.
                    if(JsonLdKeywords.IsKeyword(expandedKey) && !JsonLdKeywords.IsList(expandedKey) && !JsonLdKeywords.IsSet(expandedKey))
                    {
                        result[expandedKey] = await ExpandElementAsync(
                            value, activeContext, baseUrl, resolver, parser, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if(!expandedKey.Contains(':', StringComparison.Ordinal) && !JsonLdKeywords.IsKeyword(expandedKey))
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
                        //A scoped context carrying @propagate: false applies to
                        //this property's value but reverts to the pre-scoped
                        //context on entry to any nested node.
                        if(!propertyContext.Propagate)
                        {
                            propertyContext = propertyContext.WithPreviousContext(activeContext);
                        }
                        else if(activeContext.PreviousContext is { } revertBase)
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

                    //When a property-scoped context does not propagate, the
                    //scoped context still applies to this property's direct
                    //value node — only the value's nested nodes revert — so the
                    //value node suppresses its own context revert.
                    bool suppressValueRevert = termDef is { ScopedContextEntries: not null } && !propertyContext.Propagate;
                    object? expandedValue = await ExpandPropertyValueAsync(
                        value, propertyContext, baseUrl, resolver, parser, termDef, cancellationToken, suppressValueRevert)
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
        foreach(KeyValuePair<string, JsonNode> nest in deferredNests)
        {
            //A @nest-alias term may carry a property-scoped context that
            //applies to the nested properties (W3C JSON-LD 1.1 §4.3.2).
            LinkedDataContext nestContext = activeContext;
            if(activeContext.TryGetTerm(nest.Key, out TermDefinition? nestTermDef)
                && nestTermDef is { ScopedContextEntries: { } nestScopedEntries })
            {
                nestContext = await ApplyScopedEntriesAsync(
                    activeContext, nestScopedEntries, baseUrl, resolver, parser, cancellationToken, overrideProtected: true)
                    .ConfigureAwait(false);
            }

            await ExpandNestIntoAsync(
                nest.Value, result, nestContext, typeExpansionContext, baseUrl, resolver, parser, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandPropertyValueAsync(
        JsonNode value,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        TermDefinition? termDef,
        CancellationToken cancellationToken,
        bool suppressContextRevert = false)
    {
        //A term coercing to @json keeps the entire value verbatim as a JSON
        //literal (objects, arrays, scalars and null preserved as-is), never
        //expanded as JSON-LD.
        if(string.Equals(termDef?.TypeMapping, JsonLdKeywords.Json, StringComparison.Ordinal))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [JsonLdKeywords.Value] = ConvertJsonLiteral(value),
                [JsonLdKeywords.Type] = JsonLdKeywords.Json
            };
        }

        bool isList = termDef is not null && HasContainer(termDef, JsonLdKeywords.List);
        bool isLanguage = termDef is not null && HasContainer(termDef, JsonLdKeywords.Language);
        bool isIndex = termDef is not null && HasContainer(termDef, JsonLdKeywords.Index);
        bool isIdMap = termDef is not null && HasContainer(termDef, JsonLdKeywords.Id);
        bool isTypeMap = termDef is not null && HasContainer(termDef, JsonLdKeywords.Type);
        bool isGraph = termDef is not null && HasContainer(termDef, JsonLdKeywords.Graph);

        //Map-shaped containers: the compact form is a {key: value} map
        //that expansion reshapes into an array of value/node objects,
        //attaching the discriminator (@language/@index/@id/@type) to each.
        if(value.Kind == JsonNodeKind.Object && (isLanguage || isIndex || isIdMap || isTypeMap))
        {
            //A @type-container map reverts the active type-scoped context to its
            //previous context before expanding (W3C JSON-LD 1.1 §4.3.2): the map
            //keys are themselves types whose own scoped contexts apply on the
            //reverted base, so an enclosing type-scoped context must not leak in.
            LinkedDataContext mapContext = isTypeMap && activeContext.PreviousContext is { } previous
                ? previous
                : activeContext;

            return await ExpandMapContainerAsync(
                value, mapContext, baseUrl, resolver, parser, termDef,
                isLanguage: isLanguage, isIndex: isIndex, isIdMap: isIdMap, isTypeMap: isTypeMap, isGraph: isGraph,
                cancellationToken).ConfigureAwait(false);
        }

        if(value.Kind == JsonNodeKind.Array)
        {
            List<object?> items = new();
            foreach(JsonNode item in value.EnumerateArray())
            {
                await AddExpandedMemberAsync(
                    items, item, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: isList, cancellationToken, suppressContextRevert)
                    .ConfigureAwait(false);
            }

            //@list container: wrap the items in {"@list": [...]} per spec.
            if(isList)
            {
                Dictionary<string, object?> listObject = new(StringComparer.Ordinal)
                {
                    [JsonLdKeywords.List] = items
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
            value, activeContext, baseUrl, resolver, parser, termDef, cancellationToken, suppressContextRevert)
            .ConfigureAwait(false);

        if(isList && singleExpanded is not null && !IsExpandedListObject(singleExpanded))
        {
            Dictionary<string, object?> listObject = new(StringComparer.Ordinal)
            {
                [JsonLdKeywords.List] = new List<object?> { singleExpanded }
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
        return value is Dictionary<string, object?> map && map.ContainsKey(JsonLdKeywords.List);
    }

    /// <summary>
    /// Wraps an expanded value in a graph object (<c>{"@graph": [value]}</c>)
    /// for a term whose container includes <c>@graph</c>.
    /// </summary>
    private static Dictionary<string, object?> WrapInGraph(object? value)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [JsonLdKeywords.Graph] = new List<object?> { value }
        };
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask<object?> ExpandPropertyItemAsync(
        JsonNode item,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        TermDefinition? termDef,
        CancellationToken cancellationToken,
        bool suppressContextRevert = false)
    {
        if(item.Kind == JsonNodeKind.Object)
        {
            //An explicit @list object expands its members (coerced by the
            //surrounding term) and re-wraps them; an explicit @set object
            //expands to its bare member list, which the caller splices into
            //the property array (the @set wrapper never survives expansion).
            if(TryFindKeywordValue(item, activeContext, JsonLdKeywords.List, out JsonNode listValue))
            {
                EnsureValidListOrSetObject(item, activeContext, JsonLdKeywords.List);
                List<object?> listMembers = await ExpandMembersAsync(
                    listValue, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: true, cancellationToken).ConfigureAwait(false);

                Dictionary<string, object?> listObject = new(StringComparer.Ordinal) { [JsonLdKeywords.List] = listMembers };

                //A list object preserves a sibling @index (a node-level @index keeps its verbatim string value).
                if(TryFindKeywordValue(item, activeContext, JsonLdKeywords.Index, out JsonNode listIndex) && listIndex.Kind == JsonNodeKind.String)
                {
                    listObject[JsonLdKeywords.Index] = listIndex.GetString();
                }

                return listObject;
            }
            if(TryFindKeywordValue(item, activeContext, JsonLdKeywords.Set, out JsonNode setValue))
            {
                EnsureValidListOrSetObject(item, activeContext, JsonLdKeywords.Set);
                return await ExpandMembersAsync(
                    setValue, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: false, cancellationToken).ConfigureAwait(false);
            }

            return await ExpandObjectAsync(item, activeContext, baseUrl, resolver, parser, cancellationToken, suppressContextRevert)
                .ConfigureAwait(false);
        }

        if(item.Kind == JsonNodeKind.Null)
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
        JsonNode value,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        TermDefinition? termDef,
        bool nestArraysAsLists,
        CancellationToken cancellationToken)
    {
        List<object?> members = new();
        if(value.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode item in value.EnumerateArray())
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
        JsonNode item,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        TermDefinition? termDef,
        bool nestArraysAsLists,
        CancellationToken cancellationToken,
        bool suppressContextRevert = false)
    {
        //A member of a @list container that is itself an array becomes a
        //nested list object; the nesting recurses (an array of arrays of
        //arrays produces correspondingly nested @list objects).
        if(nestArraysAsLists && item.Kind == JsonNodeKind.Array)
        {
            List<object?> innerMembers = await ExpandMembersAsync(
                item, activeContext, baseUrl, resolver, parser, termDef, nestArraysAsLists: true, cancellationToken).ConfigureAwait(false);
            members.Add(new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.List] = innerMembers });
            return;
        }

        object? expanded = await ExpandPropertyItemAsync(
            item, activeContext, baseUrl, resolver, parser, termDef, cancellationToken, suppressContextRevert).ConfigureAwait(false);
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
    /// Expands the contents of an <c>@reverse</c> entry into the surrounding
    /// node <paramref name="result"/>: an ordinary inner property is stored
    /// under the node's <c>@reverse</c> map (subject/object swapped for RDF),
    /// while a key that is itself a reverse property cancels the two reversals
    /// and is added as an ordinary forward property (W3C JSON-LD 1.1 §4.3.2).
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "baseUrl follows the JSON-LD specification convention of string URIs.")]
    private static async ValueTask ExpandReverseIntoAsync(
        Dictionary<string, object?> result,
        JsonNode reverseObj,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, JsonNode>> ordered = new();
        foreach(KeyValuePair<string, JsonNode> property in reverseObj.EnumerateObject())
        {
            ordered.Add(property);
        }
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach(KeyValuePair<string, JsonNode> property in ordered)
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

            //A reverse-of-reverse property cancels back to a forward property
            //on the node; an ordinary property goes under the @reverse map.
            if(termDef?.ReverseProperty is not null)
            {
                AddValues(result, expandedKey, valueArray);
            }
            else
            {
                EnsureNoReversePropertyValueObjects(valueArray);
                AddReverseValues(result, expandedKey, valueArray);
            }
        }
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
                && (map.ContainsKey(JsonLdKeywords.Value) || map.ContainsKey(JsonLdKeywords.List)))
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
    private static void EnsureValidListOrSetObject(JsonNode obj, LinkedDataContext activeContext, string container)
    {
        foreach(KeyValuePair<string, JsonNode> property in obj.EnumerateObject())
        {
            string? expandedKey = activeContext.ExpandIri(property.Key, vocab: true);
            if(expandedKey is null
                || string.Equals(expandedKey, container, StringComparison.Ordinal)
                || string.Equals(expandedKey, JsonLdKeywords.Index, StringComparison.Ordinal))
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
        JsonNode value,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
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
        List<KeyValuePair<string, JsonNode>> entries = new();
        foreach(KeyValuePair<string, JsonNode> entry in value.EnumerateObject())
        {
            entries.Add(entry);
        }
        entries.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        foreach(KeyValuePair<string, JsonNode> entry in entries)
        {
            string mapKey = entry.Key;
            JsonNode mapValue = entry.Value;

            //Each map value may be a single item or an array of items.
            //Iterate uniformly.
            if(mapValue.Kind == JsonNodeKind.Array)
            {
                foreach(JsonNode innerItem in mapValue.EnumerateArray())
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
        JsonNode item,
        string mapKey,
        LinkedDataContext activeContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
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
        bool mapKeyIsNone = string.Equals(activeContext.ExpandIri(mapKey, vocab: true), JsonLdKeywords.None, StringComparison.Ordinal);

        //Under a @type map the key is a type whose term may carry a type-scoped
        //context; it applies (non-propagating) while expanding this key's value
        //(W3C JSON-LD 1.1 §4.3.2).
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
            if(item.Kind == JsonNodeKind.Null)
            {
                return null;
            }

            //Each value under a language map must be a language-tagged
            //string (or null); anything else is an invalid language map value.
            if(item.Kind != JsonNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidLanguageMapValue,
                    "A language-map value must be a string.");
            }

            Dictionary<string, object?> valueObject = new(StringComparer.Ordinal)
            {
                [JsonLdKeywords.Value] = item.GetString()
            };

            if(!mapKeyIsNone)
            {
                valueObject[JsonLdKeywords.Language] = mapKey;
            }

            //A base direction (from the term, or the context default) attaches
            //to each language-map value alongside its language tag.
            string? direction = termDef is { HasDirectionMapping: true }
                ? termDef.DirectionMapping
                : activeContext.DefaultBaseDirection;
            if(direction is not null)
            {
                valueObject[JsonLdKeywords.Direction] = direction;
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
            if(termDef?.IndexMapping is { } indexProperty && !string.Equals(indexProperty, JsonLdKeywords.Index, StringComparison.Ordinal))
            {
                propertyValuedIndexKey = AddPropertyValuedIndex(nodeObject, indexProperty, mapKey, activeContext);
            }
            else if(!nodeObject.ContainsKey(JsonLdKeywords.Index))
            {
                //A node's own @index overrides the index-map key.
                nodeObject[JsonLdKeywords.Index] = mapKey;
            }
        }

        if(isIdMap && !mapKeyIsNone && !nodeObject.ContainsKey(JsonLdKeywords.Id))
        {
            //The id-map key supplies the @id only when the node object does
            //not already carry one of its own.
            string? expandedId = activeContext.ExpandIri(mapKey, documentRelative: true);
            nodeObject[JsonLdKeywords.Id] = expandedId ?? mapKey;
        }

        if(isTypeMap && !mapKeyIsNone)
        {
            string? expandedType = activeContext.ExpandIri(mapKey, vocab: true);
            //The discriminator type is prepended to the node's @type set,
            //which is always an array in expanded form.
            List<object?> types = new() { expandedType ?? mapKey };
            if(nodeObject.TryGetValue(JsonLdKeywords.Type, out object? existingType))
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
            nodeObject[JsonLdKeywords.Type] = types;
        }

        //@graph layering: wrap as a graph object. When combined with
        //@id or @index, the inner content is already keyed; just wrap.
        if(isGraph && !nodeObject.ContainsKey(JsonLdKeywords.Graph))
        {
            //Move the inner content under @graph, keeping the @id/@index
            //we just attached at the outer level.
            Dictionary<string, object?> outer = new(StringComparer.Ordinal);
            Dictionary<string, object?> inner = new(StringComparer.Ordinal);
            foreach(KeyValuePair<string, object?> kv in nodeObject)
            {
                if((isIdMap && kv.Key == JsonLdKeywords.Id)
                    || (isIndex && kv.Key == JsonLdKeywords.Index)
                    || (propertyValuedIndexKey is not null && string.Equals(kv.Key, propertyValuedIndexKey, StringComparison.Ordinal)))
                {
                    outer[kv.Key] = kv.Value;
                }
                else
                {
                    inner[kv.Key] = kv.Value;
                }
            }
            outer[JsonLdKeywords.Graph] = new List<object?> { inner };

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
        if(nodeObject.ContainsKey(JsonLdKeywords.Value) || nodeObject.ContainsKey(JsonLdKeywords.List))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidTermDefinition,
                "A property-valued @index cannot add a property to a value object.");
        }

        string expandedProperty = activeContext.ExpandIri(indexProperty, vocab: true) ?? indexProperty;
        TermDefinition? indexTermDef = activeContext.TryGetTerm(indexProperty, out TermDefinition? def) ? def : null;
        Dictionary<string, object?> indexValue = indexTermDef?.TypeMapping switch
        {
            { } coercion when JsonLdKeywords.IsId(coercion) => new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = activeContext.ExpandIri(mapKey, documentRelative: true) },
            { } coercion when JsonLdKeywords.IsVocab(coercion) => new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Id] = activeContext.ExpandIri(mapKey, vocab: true, documentRelative: true) },
            _ => new Dictionary<string, object?>(StringComparer.Ordinal) { [JsonLdKeywords.Value] = mapKey }
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

    private static List<object?>? ExpandTypeValue(JsonNode value, LinkedDataContext activeContext)
    {
        //A node object's @type expands to an array of IRIs per the W3C
        //JSON-LD 1.1 expanded-form convention, even for a single type. The
        //value must be a string or an array of strings; anything else is an
        //invalid type value (a JSON null contributes no types).
        switch(value.Kind)
        {
            case JsonNodeKind.Null:
            {
                return null;
            }
            case JsonNodeKind.String:
            {
                string raw = value.GetString();
                return new List<object?> { activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw };
            }
            case JsonNodeKind.Array:
            {
                List<object?> items = new();
                foreach(JsonNode item in value.EnumerateArray())
                {
                    if(item.Kind != JsonNodeKind.String)
                    {
                        //A frame's @type array may carry an empty-object wildcard.
                        if(FrameExpansion && item.Kind == JsonNodeKind.Object && IsEmptyObject(item))
                        {
                            items.Add(new Dictionary<string, object?>(StringComparer.Ordinal));
                            continue;
                        }

                        throw new JsonLdProcessingException(
                            JsonLdErrorCode.InvalidTypeValue,
                            "A node object's @type value must be a string or an array of strings.");
                    }

                    string raw = item.GetString();
                    items.Add(activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw);
                }

                return items;
            }
            case JsonNodeKind.Object when FrameExpansion:
            {
                //A frame's @type may be a wildcard ({}) or carry an @default type used when a node has none.
                if(value.TryGetProperty(JsonLdKeywords.Default, out JsonNode defaultNode))
                {
                    return new List<object?>
                    {
                        new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            [JsonLdKeywords.Default] = ExpandTypeValue(defaultNode, activeContext) ?? []
                        }
                    };
                }

                return new List<object?> { new Dictionary<string, object?>(StringComparer.Ordinal) };
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
        JsonNode element, TermDefinition? termDef, LinkedDataContext activeContext)
    {
        //A string under a term coercing to @id or @vocab expands to a node
        //reference {"@id": <expanded IRI>}, not a value object. @id is
        //document-relative; @vocab is vocabulary-relative.
        if(element.Kind == JsonNodeKind.String && termDef?.TypeMapping is { } coercion)
        {
            if(string.Equals(coercion, JsonLdKeywords.Id, StringComparison.Ordinal))
            {
                //A keyword-like value (e.g. "@ignoreMe") expands to null and
                //is preserved as such, not echoed back verbatim.
                string raw = element.GetString();
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [JsonLdKeywords.Id] = activeContext.ExpandIri(raw, documentRelative: true)
                };
            }
            if(string.Equals(coercion, JsonLdKeywords.Vocab, StringComparison.Ordinal))
            {
                string raw = element.GetString();
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [JsonLdKeywords.Id] = activeContext.ExpandIri(raw, vocab: true, documentRelative: true)
                };
            }
        }

        object? rawValue = element.Kind switch
        {
            JsonNodeKind.String => element.GetString(),
            JsonNodeKind.True => true,
            JsonNodeKind.False => false,
            //Native JSON numbers preserve their exact lexical form through
            //expansion (10.0 stays 10.0, not re-rendered from a parsed double);
            //canonicalisation is a toRdf concern, not an expansion one.
            JsonNodeKind.Number => new JsonLdJsonNumber(element.GetRawNumber()),
            JsonNodeKind.Null => null,
            _ => null
        };
        if(rawValue is null && element.Kind != JsonNodeKind.Null)
        {
            return null;
        }

        Dictionary<string, object?> valueObject = new(StringComparer.Ordinal)
        {
            [JsonLdKeywords.Value] = rawValue
        };

        if(termDef?.TypeMapping is { } typeMapping && !IriUtils.IsKeyword(typeMapping))
        {
            valueObject[JsonLdKeywords.Type] = typeMapping;
        }
        else if(element.Kind == JsonNodeKind.String)
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
                valueObject[JsonLdKeywords.Language] = language;
            }

            string? direction = termDef is { HasDirectionMapping: true }
                ? termDef.DirectionMapping
                : activeContext.DefaultBaseDirection;
            if(direction is not null)
            {
                valueObject[JsonLdKeywords.Direction] = direction;
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
        JsonNode nestValue,
        Dictionary<string, object?> result,
        LinkedDataContext activeContext,
        LinkedDataContext typeExpansionContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken)
    {
        if(nestValue.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode item in nestValue.EnumerateArray())
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
        JsonNode item,
        Dictionary<string, object?> result,
        LinkedDataContext activeContext,
        LinkedDataContext typeExpansionContext,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken)
    {
        if(item.Kind != JsonNodeKind.Object || IsValueObject(item, activeContext))
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
    private static bool TryFindKeywordValue(JsonNode obj, LinkedDataContext activeContext, string keyword, out JsonNode value)
    {
        foreach(KeyValuePair<string, JsonNode> entry in obj.EnumerateObject())
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
    private static bool IsValueObject(JsonNode obj, LinkedDataContext activeContext)
    {
        foreach(KeyValuePair<string, JsonNode> entry in obj.EnumerateObject())
        {
            string? expandedKey = activeContext.ExpandIri(entry.Key, vocab: true);
            if(JsonLdKeywords.IsValue(expandedKey) || JsonLdKeywords.IsLanguage(expandedKey) || JsonLdKeywords.IsDirection(expandedKey))
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
    private static bool IsSingleIdReference(JsonNode obj, LinkedDataContext activeContext)
    {
        bool sawId = false;
        foreach(KeyValuePair<string, JsonNode> entry in obj.EnumerateObject())
        {
            if(string.Equals(entry.Key, JsonLdKeywords.Context, StringComparison.Ordinal))
            {
                continue;
            }

            if(!string.Equals(activeContext.ExpandIri(entry.Key, vocab: true), JsonLdKeywords.Id, StringComparison.Ordinal))
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
    private static bool IsJsonTypedValueObject(JsonNode value, LinkedDataContext activeContext)
    {
        foreach(KeyValuePair<string, JsonNode> entry in value.EnumerateObject())
        {
            if(!string.Equals(activeContext.ExpandIri(entry.Key, vocab: true), JsonLdKeywords.Type, StringComparison.Ordinal))
            {
                continue;
            }

            if(entry.Value.Kind == JsonNodeKind.String
                && string.Equals(activeContext.ExpandIri(entry.Value.GetString(), vocab: true), JsonLdKeywords.Json, StringComparison.Ordinal))
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
    private static void ValidateValueObject(JsonNode value, LinkedDataContext activeContext)
    {
        JsonNode valueNode = default;
        JsonNode typeNode = default;
        JsonNode languageNode = default;
        JsonNode indexNode = default;
        bool hasValue = false, hasType = false, hasLanguage = false, hasIndex = false, hasDirection = false;

        foreach(KeyValuePair<string, JsonNode> entry in value.EnumerateObject())
        {
            if(JsonLdKeywords.IsContext(entry.Key))
            {
                continue;
            }

            switch(activeContext.ExpandIri(entry.Key, vocab: true))
            {
                case var k when JsonLdKeywords.IsValue(k): valueNode = entry.Value; hasValue = true; break;
                case var k when JsonLdKeywords.IsType(k): typeNode = entry.Value; hasType = true; break;
                case var k when JsonLdKeywords.IsLanguage(k): languageNode = entry.Value; hasLanguage = true; break;
                case var k when JsonLdKeywords.IsIndex(k): indexNode = entry.Value; hasIndex = true; break;
                case var k when JsonLdKeywords.IsDirection(k): hasDirection = true; break;
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

        //Frame expansion permits a non-scalar @value (an empty-object wildcard or an array of patterns).
        if(!FrameExpansion && hasValue && valueNode.Kind is JsonNodeKind.Array or JsonNodeKind.Object)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "A value object's @value must be a scalar or null unless typed @json.");
        }

        if(!FrameExpansion && (hasLanguage || hasDirection) && hasValue
            && valueNode.Kind is not (JsonNodeKind.String or JsonNodeKind.Null))
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "A language- or direction-tagged value object's @value must be a string.");
        }

        //Frame expansion permits @language to be an array or an empty-object wildcard.
        if(!FrameExpansion && hasLanguage && languageNode.Kind != JsonNodeKind.String)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "@language must be a string.");
        }

        if(hasIndex && indexNode.Kind != JsonNodeKind.String)
        {
            throw new JsonLdProcessingException(
                JsonLdErrorCode.InvalidValueObject,
                "@index must be a string.");
        }

        //Frame expansion permits a value object's @type to be an array or an empty-object wildcard.
        if(hasType && !FrameExpansion)
        {
            if(typeNode.Kind != JsonNodeKind.String)
            {
                throw new JsonLdProcessingException(
                    JsonLdErrorCode.InvalidValueObject,
                    "A value object's @type must be a single string.");
            }

            string? expandedType = activeContext.ExpandIri(typeNode.GetString(), vocab: true, documentRelative: true);
            if(expandedType is null
                || (!string.Equals(expandedType, JsonLdKeywords.Json, StringComparison.Ordinal)
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

    private static Dictionary<string, object?>? ExpandValueObject(JsonNode value, LinkedDataContext activeContext)
    {
        //A value object typed @json keeps its @value verbatim as a JSON
        //literal (objects, arrays, and null preserved), regardless of key
        //or @type aliases.
        if(IsJsonTypedValueObject(value, activeContext))
        {
            Dictionary<string, object?> jsonResult = new(StringComparer.Ordinal) { [JsonLdKeywords.Type] = JsonLdKeywords.Json };
            foreach(KeyValuePair<string, JsonNode> entry in value.EnumerateObject())
            {
                string? entryKey = activeContext.ExpandIri(entry.Key, vocab: true);
                if(string.Equals(entryKey, JsonLdKeywords.Value, StringComparison.Ordinal))
                {
                    jsonResult[JsonLdKeywords.Value] = ConvertJsonLiteral(entry.Value);
                }
                else if(string.Equals(entryKey, JsonLdKeywords.Index, StringComparison.Ordinal) && entry.Value.Kind == JsonNodeKind.String)
                {
                    jsonResult[JsonLdKeywords.Index] = entry.Value.GetString();
                }
            }

            return jsonResult;
        }

        ValidateValueObject(value, activeContext);

        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> entry in value.EnumerateObject())
        {
            //Keys are matched on their expanded keyword so aliases of
            //@value/@type/@language/@index/@direction are honoured.
            string? expandedKey = activeContext.ExpandIri(entry.Key, vocab: true);
            switch(expandedKey)
            {
                case var k when JsonLdKeywords.IsValue(k):
                {
                    //A value object whose @value is JSON null carries no
                    //value and is dropped entirely.
                    if(entry.Value.Kind == JsonNodeKind.Null)
                    {
                        return null;
                    }

                    result[JsonLdKeywords.Value] = entry.Value.Kind switch
                    {
                        JsonNodeKind.String => entry.Value.GetString(),
                        JsonNodeKind.True => (object?)true,
                        JsonNodeKind.False => (object?)false,
                        //A @value number preserves its exact lexical form through expansion (10.0 stays 10.0),
                        //matching the bare-scalar path; numeric canonicalisation is a toRdf concern.
                        JsonNodeKind.Number => new JsonLdJsonNumber(entry.Value.GetRawNumber()),
                        //In frame mode a non-scalar @value (wildcard {} or array) is kept verbatim for matching.
                        JsonNodeKind.Object or JsonNodeKind.Array when FrameExpansion => MaterializeFrameValue(entry.Value),
                        _ => null
                    };
                    break;
                }
                case var k when JsonLdKeywords.IsType(k):
                {
                    //A value object's @type is a single IRI, except in frame mode where it may be a
                    //wildcard ({}) or an array of IRIs kept for matching.
                    if(entry.Value.Kind == JsonNodeKind.String)
                    {
                        string raw = entry.Value.GetString();
                        result[JsonLdKeywords.Type] = activeContext.ExpandIri(raw, vocab: true, documentRelative: true) ?? raw;
                    }
                    else if(FrameExpansion)
                    {
                        result[JsonLdKeywords.Type] = ExpandFrameTypeValue(entry.Value, activeContext);
                    }
                    break;
                }
                case var k when JsonLdKeywords.IsLanguage(k):
                {
                    if(entry.Value.Kind == JsonNodeKind.String)
                    {
                        //A language tag is normalised to lower case during expansion (W3C JSON-LD 1.1 §4.3.2).
                        result[JsonLdKeywords.Language] = LowercaseLanguage(entry.Value.GetString());
                    }
                    else if(FrameExpansion)
                    {
                        //A frame's @language may be a wildcard ({}) or an array of language tags.
                        result[JsonLdKeywords.Language] = entry.Value.Kind == JsonNodeKind.Array
                            ? MaterializeFrameValue(entry.Value)
                            : new Dictionary<string, object?>(StringComparer.Ordinal);
                    }
                    break;
                }
                case var k when JsonLdKeywords.IsIndex(k):
                {
                    if(entry.Value.Kind == JsonNodeKind.String)
                    {
                        result[JsonLdKeywords.Index] = entry.Value.GetString();
                    }
                    break;
                }
                case var k when JsonLdKeywords.IsDirection(k):
                {
                    if(entry.Value.Kind == JsonNodeKind.String)
                    {
                        result[JsonLdKeywords.Direction] = entry.Value.GetString();
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
        if(!result.ContainsKey(JsonLdKeywords.Value))
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
        if(target.TryGetValue(JsonLdKeywords.Reverse, out object? existing) && existing is Dictionary<string, object?> reverseMap)
        {
            AddValues(reverseMap, reverseIri, values);

            return;
        }

        Dictionary<string, object?> created = new(StringComparer.Ordinal);
        AddValues(created, reverseIri, values);
        target[JsonLdKeywords.Reverse] = created;
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
    private static object? ConvertJsonLiteral(JsonNode node)
    {
        switch(node.Kind)
        {
            case JsonNodeKind.Object:
            {
                Dictionary<string, object?> map = new(StringComparer.Ordinal);
                foreach(KeyValuePair<string, JsonNode> entry in node.EnumerateObject())
                {
                    map[entry.Key] = ConvertJsonLiteral(entry.Value);
                }

                return map;
            }
            case JsonNodeKind.Array:
            {
                List<object?> items = new();
                foreach(JsonNode item in node.EnumerateArray())
                {
                    items.Add(ConvertJsonLiteral(item));
                }

                return items;
            }
            case JsonNodeKind.String:
            {
                return node.GetString();
            }
            case JsonNodeKind.True:
            {
                return true;
            }
            case JsonNodeKind.False:
            {
                return false;
            }
            case JsonNodeKind.Number:
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
        JsonNode typeNode,
        string? baseUrl,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        CancellationToken cancellationToken)
    {
        //Collect raw @type values (a string or array of strings).
        List<string> rawTypes = new();
        if(typeNode.Kind == JsonNodeKind.String)
        {
            rawTypes.Add(typeNode.GetString());
        }
        else if(typeNode.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode item in typeNode.EnumerateArray())
            {
                if(item.Kind == JsonNodeKind.String)
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

            //Each type's scoped context is looked up in the context active
            //BEFORE type-scoped processing began: a prior type's null-reset
            //context may have cleared the term that carries the next type's
            //scoped context (W3C JSON-LD 1.1 §4.1.4 — types resolved against
            //the previous context).
            IReadOnlyList<LinkedDataContextEntry>? entriesForType = FindScopedEntriesForType(activeContext, typeIri);
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
        ParseJsonDelegate parser,
        CancellationToken cancellationToken,
        bool propagate = true,
        bool overrideProtected = false)
    {
        return await JsonLdScopedContextHelper.ApplyAsync(
            activeContext, entries, baseUrl, resolver, parser, cancellationToken, propagate, overrideProtected)
            .ConfigureAwait(false);
    }
}
