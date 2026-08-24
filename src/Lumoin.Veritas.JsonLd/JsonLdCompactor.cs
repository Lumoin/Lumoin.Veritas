using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// Implements the JSON-LD 1.1 compaction algorithm
/// (<see href="https://www.w3.org/TR/json-ld11-api/#compaction-algorithms"/>):
/// given an expanded JSON-LD element and an active context, produces the
/// compact form where IRIs are replaced by terms or compact IRIs, value
/// objects are unwrapped to their idiomatic compact shape, and container
/// mappings (<c>@list</c>, <c>@set</c>, <c>@language</c>, <c>@index</c>,
/// <c>@id</c>, <c>@type</c>, <c>@graph</c>) reshape values into maps and
/// arrays.
/// </summary>
/// <remarks>
/// <para>
/// Term selection follows the specification's inverse-context mechanism
/// (§4.2.1 Inverse Context Creation, §4.3 IRI Compaction, §4.3.5 Term
/// Selection): the term chosen for a property depends on the
/// <em>value</em> being compacted — its type, language, direction, and
/// container profile — not merely on the property IRI. This is why the
/// same expanded property can compact to different terms for different
/// values.
/// </para>
/// <para>
/// Output shape: <see cref="object"/>?, where object maps to
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c>, array maps to
/// <c>IReadOnlyList&lt;object?&gt;</c>, and leaves are <see cref="string"/>,
/// <see cref="bool"/>, <see cref="JsonLdJsonNumber"/> (the verbatim number
/// token), or <c>null</c>. Callers serialise to JSON via their JSON adapter.
/// </para>
/// <para>
/// Deferred to follow-ups: framing (<c>@frame</c>); property- and
/// type-scoped contexts applied during compaction (they require re-running
/// context processing, which is asynchronous).
/// </para>
/// </remarks>
public static class JsonLdCompactor
{
    /// <summary>
    /// Returns the compact form of <paramref name="iri"/> against
    /// <paramref name="activeContext"/>, ignoring any value profile. This is a
    /// value-agnostic convenience over the specification's IRI-compaction
    /// machinery: (1) direct term match — find a term whose
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
            //A keyword compacts to a term that aliases it (e.g. "id" → @id), if one is defined.
            string? keywordAlias = null;
            foreach(string termName in activeContext.Terms)
            {
                if(activeContext.TryGetTerm(termName, out TermDefinition? def)
                    && def is { IriMapping: { } mapping }
                    && string.Equals(mapping, iri, StringComparison.Ordinal)
                    && IsBetterCandidate(termName, keywordAlias))
                {
                    keywordAlias = termName;
                }
            }

            return keywordAlias ?? iri;
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
    /// <param name="baseIri">The document base IRI, used for document-relative <c>@id</c> compaction; <c>null</c> leaves absolute IRIs unchanged.</param>
    /// <param name="cancellationToken">A token that aborts the operation.</param>
    /// <returns>The compacted element as an object graph.</returns>
    /// <remarks>
    /// A convenience over the resolver-taking <see cref="CompactAsync(JsonNode, LinkedDataContext, ContextResolverDelegate, ParseJsonDelegate, string, bool, CancellationToken)"/>
    /// for callers whose term-scoped contexts are inline (the case for a
    /// context built from a single in-memory document). It supplies a
    /// resolver/parser that reject remote-context fetches; use the
    /// resolver-taking overload when scoped contexts may reference remote
    /// documents.
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the compaction algorithm.")]
    public static ValueTask<object?> CompactAsync(JsonNode expanded, LinkedDataContext activeContext, string? baseIri = null, CancellationToken cancellationToken = default)
    {
        return CompactAsync(expanded, activeContext, RejectRemoteContext, RejectJsonParse, baseIri, compactArrays: true, cancellationToken);
    }

    /// <summary>
    /// Compacts <paramref name="expanded"/> against <paramref name="activeContext"/>,
    /// applying property- and type-scoped contexts during the walk (which may
    /// require resolving remote contexts through <paramref name="resolver"/>).
    /// </summary>
    /// <param name="expanded">The expanded JSON-LD element.</param>
    /// <param name="activeContext">The context against which to compact.</param>
    /// <param name="resolver">Resolves remote contexts referenced by scoped contexts.</param>
    /// <param name="parser">Parses remote context bodies.</param>
    /// <param name="baseIri">The document base IRI for document-relative <c>@id</c> compaction; <c>null</c> leaves absolute IRIs unchanged.</param>
    /// <param name="compactArrays">When <see langword="true"/> (the default), single-value arrays without a pinned container collapse to the value.</param>
    /// <param name="cancellationToken">A token that aborts the operation.</param>
    /// <returns>The compacted element as an object graph.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the compaction algorithm.")]
    public static async ValueTask<object?> CompactAsync(
        JsonNode expanded,
        LinkedDataContext activeContext,
        ContextResolverDelegate resolver,
        ParseJsonDelegate parser,
        string? baseIri = null,
        bool compactArrays = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(parser);

        CompactionEnv env = new()
        {
            DocumentBase = baseIri,
            CompactArrays = compactArrays,
            Resolver = resolver,
            Parser = parser,
            CancellationToken = cancellationToken
        };

        InverseContext inverse = BuildInverseContext(activeContext);
        object? compactedBody = await CompactElementAsync(activeContext, inverse, env, activeProperty: null, expanded).ConfigureAwait(false);
        IReadOnlyDictionary<string, object?>? emittedContext = EmitContextObject(activeContext);

        //Top-level array normalization: an empty result is the empty node object {}; a single-element
        //result inlines to that element (the element walker already unwraps a single top-level item);
        //only a multi-element result is wrapped in a @graph node object.
        if(compactedBody is IReadOnlyList<object?> topLevel)
        {
            if(topLevel.Count == 0)
            {
                compactedBody = new Dictionary<string, object?>(StringComparer.Ordinal);
            }
            else if(topLevel.Count == 1 && compactArrays)
            {
                compactedBody = topLevel[0];
            }
            else
            {
                string graphAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Graph, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Graph;
                Dictionary<string, object?> graph = new(emittedContext is null ? 1 : 2, StringComparer.Ordinal);
                if(emittedContext is not null)
                {
                    graph[JsonLdKeywords.Context] = emittedContext;
                }

                graph[graphAlias] = compactedBody;

                return graph;
            }
        }

        //When the context has user-defined terms or a vocabulary mapping, emit @context alongside the
        //compacted body so the output is self-contained.
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

        //A scalar body: wrap into a graph object so @context can attach.
        return new Dictionary<string, object?>(2, StringComparer.Ordinal)
        {
            [JsonLdKeywords.Context] = emittedContext,
            [JsonLdKeywords.Graph] = compactedBody
        };
    }

    /// <summary>A resolver for the reject-resolver convenience path: scoped contexts must be inline, so a remote fetch is unsupported.</summary>
    /// <param name="uri">The requested context URI.</param>
    /// <param name="cancellationToken">A token (unused).</param>
    /// <returns>Never returns; always throws.</returns>
    private static ValueTask<Utf8String?> RejectRemoteContext(Uri uri, CancellationToken cancellationToken)
    {
        throw new NotSupportedException($"Compacting with a remote scoped context ('{uri}') requires CompactAsync with a resolver.");
    }

    /// <summary>A parser for the reject-resolver convenience path; only reached for remote context bodies, which that path does not support.</summary>
    /// <param name="utf8Json">The raw bytes (unused).</param>
    /// <returns>Never returns; always throws.</returns>
    private static JsonNode RejectJsonParse(Utf8String utf8Json)
    {
        throw new NotSupportedException("Parsing a remote scoped context requires CompactAsync with a parser.");
    }

    /// <summary>
    /// Compacts a value-object (<c>{"@value": ...}</c>) against the given
    /// term definition. When the term's type or language mapping matches the
    /// value's coercion, the wrapper is unwrapped to the bare literal. This is
    /// a value-agnostic convenience; the full compaction algorithm
    /// (<see cref="CompactAsync(JsonNode, LinkedDataContext, ContextResolverDelegate, ParseJsonDelegate, string, bool, CancellationToken)"/>)
    /// uses the specification's value-compaction rules directly.
    /// </summary>
    /// <param name="valueObject">An expanded value object.</param>
    /// <param name="termDefinition">The term definition under which the value appears, or <c>null</c> for none.</param>
    /// <param name="activeContext">The active context, used to compact a retained <c>@type</c> IRI; <c>null</c> emits it unchanged.</param>
    /// <returns>The compact value form.</returns>
    public static object? CompactValue(JsonNode valueObject, TermDefinition? termDefinition, LinkedDataContext? activeContext = null)
    {
        if(valueObject.Kind != JsonNodeKind.Object)
        {
            //Not a value object; pass through.
            return ToObjectGraph(valueObject);
        }

        if(!valueObject.TryGetProperty(JsonLdKeywords.Value, out JsonNode valueNode))
        {
            return ToObjectGraph(valueObject);
        }

        bool hasType = valueObject.TryGetProperty(JsonLdKeywords.Type, out JsonNode typeNode);
        bool hasLanguage = valueObject.TryGetProperty(JsonLdKeywords.Language, out JsonNode languageNode);

        //If the surrounding term's coercion matches the value's type or
        //language, unwrap to the bare literal. Otherwise, emit a compact
        //value object preserving the type/language.
        if(termDefinition is not null)
        {
            if(hasType
                && typeNode.Kind == JsonNodeKind.String
                && termDefinition.TypeMapping is { } typeCoerce
                && string.Equals(typeNode.GetString(), typeCoerce, StringComparison.Ordinal))
            {
                return ToObjectGraph(valueNode);
            }
            if(hasLanguage
                && languageNode.Kind == JsonNodeKind.String
                && termDefinition.LanguageMapping is { } langCoerce
                && string.Equals(languageNode.GetString(), langCoerce, StringComparison.Ordinal))
            {
                return ToObjectGraph(valueNode);
            }
            //An untyped, unlanguaged plain string under a term with no
            //type coercion: unwrap.
            if(!hasType && !hasLanguage && valueNode.Kind != JsonNodeKind.Object)
            {
                return ToObjectGraph(valueNode);
            }
        }
        else
        {
            //No surrounding term context: unwrap plain literals only.
            if(!hasType && !hasLanguage && valueNode.Kind != JsonNodeKind.Object)
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
        if(hasType && typeNode.Kind == JsonNodeKind.String)
        {
            //A value object's @type is a datatype IRI: compact it vocab-relative so a defined term surfaces.
            string typeIri = typeNode.GetString();
            compactedObject[JsonLdKeywords.Type] = activeContext is null ? typeIri : CompactIri(activeContext, typeIri, vocab: true) ?? typeIri;
        }
        if(hasLanguage && languageNode.Kind == JsonNodeKind.String)
        {
            compactedObject[JsonLdKeywords.Language] = languageNode.GetString();
        }
        return compactedObject;
    }

    /// <summary>
    /// Compacts an element (§4.1). Arrays are compacted item-by-item and
    /// unwrapped to a single value when the active property carries no
    /// container; objects are routed to value compaction, list-in-list-container
    /// inlining, or node-object compaction.
    /// </summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context for term selection.</param>
    /// <param name="env">The compaction environment (base, compactArrays, resolver/parser for scoped contexts).</param>
    /// <param name="activeProperty">The compact property the element appears under, or <c>null</c>.</param>
    /// <param name="element">The expanded element.</param>
    /// <returns>The compacted element.</returns>
    private static async ValueTask<object?> CompactElementAsync(LinkedDataContext activeContext, InverseContext inverse, CompactionEnv env, string? activeProperty, JsonNode element)
    {
        if(element.Kind == JsonNodeKind.Array)
        {
            List<object?> result = new();
            foreach(JsonNode item in element.EnumerateArray())
            {
                object? compactedItem = await CompactElementAsync(activeContext, inverse, env, activeProperty, item).ConfigureAwait(false);
                if(compactedItem is null)
                {
                    continue;
                }

                result.Add(compactedItem);
            }

            //compactArrays: a single value collapses unless disabled or the active property pins a container.
            if(env.CompactArrays && result.Count == 1 && ContainerOf(activeContext, activeProperty).Count == 0)
            {
                return result[0];
            }

            return result;
        }

        if(element.Kind != JsonNodeKind.Object)
        {
            //A scalar is already compact.
            return ToObjectGraph(element);
        }

        bool isValueOrReference = IsValueNode(element) || IsSubjectReferenceNode(element);

        //The property-scoped context is looked up from the context as inherited (which may define the
        //property via an ancestor's type-scoped context), then applied on top of the reverted base.
        LinkedDataContext beforeRevert = activeContext;

        //Revert a non-propagating (type-scoped) context inherited from an ancestor on entry to a new node
        //object (W3C JSON-LD 1.1 §4.1 step 4); value objects and bare node references keep the context.
        if(!isValueOrReference && activeContext.PreviousContext is { } reverted)
        {
            activeContext = reverted;
            inverse = BuildInverseContext(activeContext);
        }

        //Apply the active property's property-scoped context (§4.1 step 5), before value compaction so a
        //value object under a coercing scoped term sees the right type/language mapping. A non-propagating
        //scoped context records the pre-application context so its nested nodes revert it.
        if(ScopedEntriesFor(beforeRevert, activeProperty) is { } propertyScoped)
        {
            LinkedDataContext beforeApply = activeContext;
            (activeContext, inverse) = await ApplyScopedAsync(activeContext, propertyScoped, env, propagate: true, overrideProtected: true).ConfigureAwait(false);
            if(!activeContext.Propagate && activeContext.PreviousContext is null)
            {
                activeContext = activeContext.WithPreviousContext(beforeApply);
            }
        }

        //Value objects and bare node references compact through value compaction.
        if(isValueOrReference)
        {
            return CompactValueElement(activeContext, inverse, env, activeProperty, element);
        }

        //A list object directly under a @list-container property inlines to a bare array.
        if(IsListNode(element) && ContainsContainer(ContainerOf(activeContext, activeProperty), JsonLdKeywords.List)
            && element.TryGetProperty(JsonLdKeywords.List, out JsonNode listInline))
        {
            return await CompactElementAsync(activeContext, inverse, env, activeProperty, listInline).ConfigureAwait(false);
        }

        return await CompactNodeObjectAsync(activeContext, inverse, env, activeProperty, element).ConfigureAwait(false);
    }

    /// <summary>Gets a term's pre-extracted scoped-context entries, or <c>null</c> when the property is unmapped or carries no scoped context.</summary>
    /// <param name="context">The active context.</param>
    /// <param name="property">The compact property (or compacted type) name.</param>
    /// <returns>The scoped entries, or <c>null</c>.</returns>
    private static IReadOnlyList<LinkedDataContextEntry>? ScopedEntriesFor(LinkedDataContext context, string? property)
    {
        return property is not null && context.TryGetTerm(property, out TermDefinition? def) && def is { ScopedContextEntries: { } entries }
            ? entries
            : null;
    }

    /// <summary>Applies scoped-context entries to <paramref name="context"/> and returns the merged context together with its freshly built inverse context.</summary>
    /// <param name="context">The context to extend.</param>
    /// <param name="entries">The scoped-context entries.</param>
    /// <param name="env">The compaction environment (base, resolver, parser, cancellation).</param>
    /// <param name="propagate">Whether the scoped context propagates to nested nodes.</param>
    /// <param name="overrideProtected">Whether the scoped context may override protected terms.</param>
    /// <returns>The merged context and its inverse.</returns>
    private static async ValueTask<(LinkedDataContext Context, InverseContext Inverse)> ApplyScopedAsync(
        LinkedDataContext context,
        IReadOnlyList<LinkedDataContextEntry> entries,
        CompactionEnv env,
        bool propagate,
        bool overrideProtected)
    {
        LinkedDataContext merged = await JsonLdScopedContextHelper.ApplyAsync(
            context, entries, env.DocumentBase, env.Resolver, env.Parser, env.CancellationToken, propagate, overrideProtected)
            .ConfigureAwait(false);
        return (merged, BuildInverseContext(merged));
    }

    /// <summary>
    /// Compacts a node object (§4.1 step 6 onward). On entry the active context
    /// has already been reverted and had the property-scoped context applied
    /// (in <see cref="CompactElement"/>); here the node's type-scoped contexts
    /// are applied, then its keys are walked. <c>@type</c> values are compacted
    /// against the pre-type-scoped context, the rest against the post-type-scoped
    /// context.
    /// </summary>
    /// <param name="activeContext">The active context (reverted + property-scoped).</param>
    /// <param name="inverse">The inverse context matching <paramref name="activeContext"/>.</param>
    /// <param name="env">The compaction environment.</param>
    /// <param name="activeProperty">The compact property the node appears under.</param>
    /// <param name="element">The expanded node object.</param>
    /// <returns>The compacted node object.</returns>
    private static async ValueTask<Dictionary<string, object?>> CompactNodeObjectAsync(LinkedDataContext activeContext, InverseContext inverse, CompactionEnv env, string? activeProperty, JsonNode element)
    {
        bool insideReverse = string.Equals(activeProperty, JsonLdKeywords.Reverse, StringComparison.Ordinal);

        //The context before type-scoped contexts: @type values compact against it (a type-scoped context
        //may redefine the vocabulary, which must not affect the type IRIs that triggered it).
        LinkedDataContext inputContext = activeContext;
        InverseContext inputInverse = inverse;

        //Type-scoped contexts (§4.1): for each of the node's compacted types in lexicographic order, apply
        //its term's scoped context (non-propagating). The pre-application context is recorded so nested
        //nodes revert it on entry.
        if(element.TryGetProperty(JsonLdKeywords.Type, out JsonNode nodeTypes))
        {
            LinkedDataContext beforeTypeScoped = activeContext;
            foreach(string typeIri in SortedTypeIris(nodeTypes))
            {
                string? compactedType = CompactIriCore(inputContext, inputInverse, typeIri, value: null, vocab: true, reverse: false);
                if(ScopedEntriesFor(inputContext, compactedType) is { } typeScoped)
                {
                    (activeContext, inverse) = await ApplyScopedAsync(activeContext, typeScoped, env, propagate: false, overrideProtected: false).ConfigureAwait(false);
                }
            }

            if(!ReferenceEquals(activeContext, beforeTypeScoped) && !activeContext.Propagate)
            {
                activeContext = activeContext.WithPreviousContext(beforeTypeScoped);
                inverse = BuildInverseContext(activeContext);
            }
        }

        Dictionary<string, object?> result = new(StringComparer.Ordinal);

        foreach(string expandedProperty in SortedKeys(element))
        {
            element.TryGetProperty(expandedProperty, out JsonNode expandedValue);

            if(JsonLdKeywords.IsId(expandedProperty))
            {
                object? compactedValue = CompactIdValues(activeContext, inverse, env, expandedValue);
                result[CompactIriCore(activeContext, inverse, JsonLdKeywords.Id, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Id] = compactedValue;
                continue;
            }

            if(JsonLdKeywords.IsType(expandedProperty))
            {
                //@type values are compacted against the pre-type-scoped context.
                object? compactedValue = CompactTypeValues(inputContext, inputInverse, expandedValue);
                string typeAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Type, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Type;
                bool typeAsSet = ContainsContainer(ContainerOf(activeContext, typeAlias), JsonLdKeywords.Set);
                bool isArray = typeAsSet || (compactedValue is IReadOnlyList<object?> && expandedValue.Kind == JsonNodeKind.Array && IsEmptyArray(expandedValue));
                AddValue(result, typeAlias, compactedValue, propertyIsArray: isArray);
                continue;
            }

            if(JsonLdKeywords.IsReverse(expandedProperty))
            {
                await CompactReverseAsync(activeContext, inverse, env, expandedValue, result).ConfigureAwait(false);
                continue;
            }

            if(JsonLdKeywords.IsContext(expandedProperty))
            {
                //An embedded @context is carried by the surrounding API layer, not re-emitted here.
                continue;
            }

            if(string.Equals(expandedProperty, JsonLdKeywords.Index, StringComparison.Ordinal))
            {
                //Drop @index when the value is already keyed by an @index container.
                if(ContainsContainer(ContainerOf(activeContext, activeProperty), JsonLdKeywords.Index))
                {
                    continue;
                }

                string indexAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Index, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Index;
                AddValue(result, indexAlias, ToObjectGraph(expandedValue), propertyIsArray: false);
                continue;
            }

            //Remaining keywords other than @graph/@list/@included pass through under their alias verbatim.
            if(!IsArrayValuedKeyword(expandedProperty) && IriUtils.IsKeyword(expandedProperty))
            {
                string alias = CompactIriCore(activeContext, inverse, expandedProperty, value: null, vocab: true, reverse: false) ?? expandedProperty;
                AddValue(result, alias, ToObjectGraph(expandedValue), propertyIsArray: false);
                continue;
            }

            //An IRI property. The expanded value is an array (expansion guarantees this).
            if(expandedValue.Kind != JsonNodeKind.Array)
            {
                await CompactPropertyItemAsync(activeContext, inverse, env, result, expandedProperty, expandedValue, insideReverse).ConfigureAwait(false);
                continue;
            }

            if(IsEmptyArray(expandedValue))
            {
                //An empty array is preserved: choose the term as for an empty value and emit [].
                string emptyProperty = CompactIriCore(activeContext, inverse, expandedProperty, value: expandedValue, vocab: true, reverse: insideReverse) ?? expandedProperty;
                Dictionary<string, object?> emptyTarget = NestTarget(activeContext, result, emptyProperty);
                AddValue(emptyTarget, emptyProperty, new List<object?>(), propertyIsArray: true);
                continue;
            }

            foreach(JsonNode expandedItem in expandedValue.EnumerateArray())
            {
                await CompactPropertyItemAsync(activeContext, inverse, env, result, expandedProperty, expandedItem, insideReverse).ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>Returns the node's <c>@type</c> IRIs in lexicographic order (a single string yields one entry).</summary>
    /// <param name="typeNode">The expanded <c>@type</c> value (string or array).</param>
    /// <returns>The sorted type IRIs.</returns>
    private static List<string> SortedTypeIris(JsonNode typeNode)
    {
        List<string> types = new();
        if(typeNode.Kind == JsonNodeKind.String)
        {
            types.Add(typeNode.GetString());
        }
        else if(typeNode.Kind == JsonNodeKind.Array)
        {
            foreach(JsonNode item in typeNode.EnumerateArray())
            {
                if(item.Kind == JsonNodeKind.String)
                {
                    types.Add(item.GetString());
                }
            }
        }

        types.Sort(StringComparer.Ordinal);
        return types;
    }

    /// <summary>Compacts a single expanded value of an IRI property (§4.1 step 7.6.x): selects the per-value term, applies container behaviour (@list, @graph, language/index/id/type maps), and adds the result.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="result">The node object under construction.</param>
    /// <param name="expandedProperty">The expanded property IRI.</param>
    /// <param name="expandedItem">The single expanded value.</param>
    /// <param name="insideReverse">Whether the property is being compacted inside an <c>@reverse</c>.</param>
    private static async ValueTask CompactPropertyItemAsync(
        LinkedDataContext activeContext,
        InverseContext inverse,
        CompactionEnv env,
        Dictionary<string, object?> result,
        string expandedProperty,
        JsonNode expandedItem,
        bool insideReverse)
    {
        string itemActiveProperty = CompactIriCore(activeContext, inverse, expandedProperty, value: expandedItem, vocab: true, reverse: insideReverse) ?? expandedProperty;
        Dictionary<string, object?> nestResult = NestTarget(activeContext, result, itemActiveProperty);
        IReadOnlyList<string> container = ContainerOf(activeContext, itemActiveProperty);

        bool isList = IsListNode(expandedItem);
        bool isGraph = IsGraphNode(expandedItem);

        JsonNode inner = expandedItem;
        if(isList)
        {
            expandedItem.TryGetProperty(JsonLdKeywords.List, out inner);
        }
        else if(isGraph)
        {
            expandedItem.TryGetProperty(JsonLdKeywords.Graph, out inner);
        }

        object? compactedItem = await CompactElementAsync(activeContext, inverse, env, itemActiveProperty, inner).ConfigureAwait(false);

        if(isList)
        {
            List<object?> listItems = compactedItem as List<object?> ?? new List<object?> { compactedItem };
            if(!ContainsContainer(container, JsonLdKeywords.List))
            {
                string listAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.List, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.List;
                Dictionary<string, object?> listObject = new(StringComparer.Ordinal)
                {
                    [listAlias] = listItems
                };
                if(expandedItem.TryGetProperty(JsonLdKeywords.Index, out JsonNode listIndex))
                {
                    string indexAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Index, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Index;
                    listObject[indexAlias] = ToObjectGraph(listIndex);
                }

                compactedItem = listObject;
            }
            else
            {
                AddValue(nestResult, itemActiveProperty, listItems, valueIsArray: true);
                return;
            }
        }

        if(isGraph)
        {
            CompactGraphItem(activeContext, inverse, env, nestResult, itemActiveProperty, expandedItem, compactedItem, container);
            return;
        }

        if(ContainsContainer(container, JsonLdKeywords.Language)
            || ContainsContainer(container, JsonLdKeywords.Index)
            || ContainsContainer(container, JsonLdKeywords.Id)
            || ContainsContainer(container, JsonLdKeywords.Type))
        {
            CompactMapItem(activeContext, inverse, env, nestResult, itemActiveProperty, expandedItem, compactedItem, container);
            return;
        }

        bool asArray = !env.CompactArrays
            || ContainsContainer(container, JsonLdKeywords.Set)
            || ContainsContainer(container, JsonLdKeywords.List)
            || (compactedItem is IReadOnlyList<object?> emptyList && emptyList.Count == 0)
            || string.Equals(expandedProperty, JsonLdKeywords.List, StringComparison.Ordinal)
            || string.Equals(expandedProperty, JsonLdKeywords.Graph, StringComparison.Ordinal);
        AddValue(nestResult, itemActiveProperty, compactedItem, propertyIsArray: asArray);
    }

    /// <summary>Applies graph-container behaviour (§4.1 step 7.6.6): index/id graph maps, simple-graph inlining, or wrapping under the <c>@graph</c> alias.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="env">The compaction environment (for the compactArrays flag).</param>
    /// <param name="nestResult">The target object the property is added to.</param>
    /// <param name="itemActiveProperty">The compact property.</param>
    /// <param name="expandedItem">The expanded graph object.</param>
    /// <param name="compactedItem">The already-compacted graph contents.</param>
    /// <param name="container">The property's container mapping.</param>
    private static void CompactGraphItem(
        LinkedDataContext activeContext,
        InverseContext inverse,
        CompactionEnv env,
        Dictionary<string, object?> nestResult,
        string itemActiveProperty,
        JsonNode expandedItem,
        object? compactedItem,
        IReadOnlyList<string> container)
    {
        bool hasGraph = ContainsContainer(container, JsonLdKeywords.Graph);
        bool simpleGraph = IsSimpleGraphNode(expandedItem);
        bool setArray = !env.CompactArrays || ContainsContainer(container, JsonLdKeywords.Set);

        if(hasGraph && (ContainsContainer(container, JsonLdKeywords.Id)
            || (ContainsContainer(container, JsonLdKeywords.Index) && simpleGraph)))
        {
            Dictionary<string, object?> mapObject = GetOrCreateMap(nestResult, itemActiveProperty);
            string? key = ContainsContainer(container, JsonLdKeywords.Id)
                ? GetStringProperty(expandedItem, JsonLdKeywords.Id)
                : GetStringProperty(expandedItem, JsonLdKeywords.Index);
            key ??= CompactIriCore(activeContext, inverse, JsonLdKeywords.None, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.None;
            AddValue(mapObject, key, compactedItem, propertyIsArray: setArray);
            return;
        }

        if(hasGraph && simpleGraph)
        {
            object? value = compactedItem;
            if(value is IReadOnlyList<object?> included && included.Count > 1)
            {
                value = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [JsonLdKeywords.Included] = included
                };
            }

            AddValue(nestResult, itemActiveProperty, value, propertyIsArray: setArray);
            return;
        }

        object? wrapped = compactedItem;
        if(env.CompactArrays && wrapped is IReadOnlyList<object?> single && single.Count == 1)
        {
            wrapped = single[0];
        }

        string graphAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Graph, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Graph;
        Dictionary<string, object?> graphObject = new(StringComparer.Ordinal)
        {
            [graphAlias] = wrapped
        };
        if(expandedItem.TryGetProperty(JsonLdKeywords.Id, out JsonNode graphId))
        {
            string idAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Id, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Id;
            graphObject[idAlias] = ToObjectGraph(graphId);
        }
        if(expandedItem.TryGetProperty(JsonLdKeywords.Index, out JsonNode graphIndex))
        {
            string indexAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Index, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Index;
            graphObject[indexAlias] = ToObjectGraph(graphIndex);
        }

        AddValue(nestResult, itemActiveProperty, graphObject, propertyIsArray: setArray);
    }

    /// <summary>Applies language/index/id/type map-container behaviour (§4.1 step 7.6.7): keys the compacted value by its language, index, id, or type discriminator.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="nestResult">The target object the property is added to.</param>
    /// <param name="itemActiveProperty">The compact property.</param>
    /// <param name="expandedItem">The expanded value.</param>
    /// <param name="compactedItem">The already-compacted value.</param>
    /// <param name="container">The property's container mapping.</param>
    private static void CompactMapItem(
        LinkedDataContext activeContext,
        InverseContext inverse,
        CompactionEnv env,
        Dictionary<string, object?> nestResult,
        string itemActiveProperty,
        JsonNode expandedItem,
        object? compactedItem,
        IReadOnlyList<string> container)
    {
        Dictionary<string, object?> mapObject = GetOrCreateMap(nestResult, itemActiveProperty);
        string? key = null;

        if(ContainsContainer(container, JsonLdKeywords.Language))
        {
            if(compactedItem is Dictionary<string, object?> valueMap && valueMap.TryGetValue(JsonLdKeywords.Value, out object? bare))
            {
                compactedItem = bare;
            }

            key = GetStringProperty(expandedItem, JsonLdKeywords.Language);
        }
        else if(ContainsContainer(container, JsonLdKeywords.Index))
        {
            //The index property is expanded, then re-compacted to the form it carries in the compacted item.
            string indexKey = IndexOf(activeContext, itemActiveProperty) ?? JsonLdKeywords.Index;
            string expandedIndex = activeContext.ExpandIri(indexKey, vocab: true) ?? indexKey;
            string containerKey = CompactIriCore(activeContext, inverse, expandedIndex, value: null, vocab: true, reverse: false) ?? expandedIndex;
            if(string.Equals(indexKey, JsonLdKeywords.Index, StringComparison.Ordinal))
            {
                key = GetStringProperty(expandedItem, JsonLdKeywords.Index);
                if(compactedItem is Dictionary<string, object?> indexed)
                {
                    indexed.Remove(containerKey);
                }
            }
            else
            {
                //The property carries the compacted index key (containerKey), but a typed index term
                //(e.g. @type: @vocab) can surface under its term name instead — fall back to the raw key.
                key = ExtractPropertyValuedIndexKey(compactedItem, containerKey, indexKey);
            }
        }
        else if(ContainsContainer(container, JsonLdKeywords.Id))
        {
            string idAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Id, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Id;
            if(compactedItem is Dictionary<string, object?> idMap)
            {
                key = idMap.TryGetValue(idAlias, out object? idValue) ? idValue as string : null;
                idMap.Remove(idAlias);
            }
        }
        else if(ContainsContainer(container, JsonLdKeywords.Type))
        {
            string typeAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Type, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Type;
            if(compactedItem is Dictionary<string, object?> typeMap)
            {
                key = ExtractFirstAndTrim(typeMap, typeAlias);

                //A node carrying only its (now hoisted) @type re-compacts as a bare reference to its @id.
                if(typeMap.Count == 1 && expandedItem.TryGetProperty(JsonLdKeywords.Id, out JsonNode reId))
                {
                    compactedItem = CompactNodeReference(activeContext, inverse, env, itemActiveProperty, reId);
                }
            }
        }

        key ??= CompactIriCore(activeContext, inverse, JsonLdKeywords.None, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.None;
        AddValue(mapObject, key, compactedItem, propertyIsArray: ContainsContainer(container, JsonLdKeywords.Set));
    }

    /// <summary>Compacts an <c>@reverse</c> map (§4.1 step 7.5): compacts each reverse predicate, hoisting double-reversed properties up and keeping genuine reverses under the <c>@reverse</c> alias.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="expandedValue">The expanded reverse map.</param>
    /// <param name="result">The node object under construction.</param>
    private static async ValueTask CompactReverseAsync(LinkedDataContext activeContext, InverseContext inverse, CompactionEnv env, JsonNode expandedValue, Dictionary<string, object?> result)
    {
        if(await CompactElementAsync(activeContext, inverse, env, JsonLdKeywords.Reverse, expandedValue).ConfigureAwait(false) is not Dictionary<string, object?> compactedValue)
        {
            return;
        }

        //Hoist properties whose term is itself a reverse property (a double reverse becomes a forward property).
        List<string> keys = new(compactedValue.Keys);
        foreach(string compactedProperty in keys)
        {
            if(activeContext.TryGetTerm(compactedProperty, out TermDefinition? def) && def is { ReverseProperty: not null })
            {
                bool useArray = ContainsContainer(ContainerOf(activeContext, compactedProperty), JsonLdKeywords.Set);
                AddValue(result, compactedProperty, compactedValue[compactedProperty], propertyIsArray: useArray);
                compactedValue.Remove(compactedProperty);
            }
        }

        if(compactedValue.Count > 0)
        {
            string reverseAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Reverse, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Reverse;
            AddValue(result, reverseAlias, compactedValue, propertyIsArray: false);
        }
    }

    /// <summary>Compacts a value object or node reference (§4.3.x Value Compaction): unwraps to a scalar when the term's coercion absorbs the value's type/language/direction, otherwise emits the aliased value/node-reference object.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="activeProperty">The compact property the value appears under.</param>
    /// <param name="value">The expanded value object or node reference.</param>
    /// <returns>The compacted value.</returns>
    private static object? CompactValueElement(LinkedDataContext activeContext, InverseContext inverse, CompactionEnv env, string? activeProperty, JsonNode value)
    {
        if(IsValueNode(value))
        {
            string? type = TypeMappingOf(activeContext, activeProperty);
            string? language = LanguageOf(activeContext, activeProperty);
            string? direction = DirectionOf(activeContext, activeProperty);
            bool containerIsIndex = ContainsContainer(ContainerOf(activeContext, activeProperty), JsonLdKeywords.Index);

            bool hasIndex = value.TryGetProperty(JsonLdKeywords.Index, out _);
            bool preserveIndex = hasIndex && !containerIsIndex;

            value.TryGetProperty(JsonLdKeywords.Value, out JsonNode rawValue);
            bool hasValueType = value.TryGetProperty(JsonLdKeywords.Type, out JsonNode valueType);
            bool hasValueLanguage = value.TryGetProperty(JsonLdKeywords.Language, out JsonNode valueLanguage);
            bool hasValueDirection = value.TryGetProperty(JsonLdKeywords.Direction, out JsonNode valueDirection);
            bool typeIsNone = string.Equals(type, JsonLdKeywords.None, StringComparison.Ordinal);

            if(!preserveIndex && !typeIsNone)
            {
                if(hasValueType && type is not null && string.Equals(valueType.GetString(), type, StringComparison.Ordinal))
                {
                    return ToObjectGraph(rawValue);
                }
                if(hasValueLanguage && StringEquals(valueLanguage, language) && hasValueDirection && StringEquals(valueDirection, direction))
                {
                    return ToObjectGraph(rawValue);
                }
                if(hasValueLanguage && StringEquals(valueLanguage, language))
                {
                    return ToObjectGraph(rawValue);
                }
                if(hasValueDirection && StringEquals(valueDirection, direction))
                {
                    return ToObjectGraph(rawValue);
                }
            }

            int keyCount = KeyCount(value);
            bool isValueOnlyKey = keyCount == 1 || (keyCount == 2 && hasIndex && !preserveIndex);
            bool hasDefaultLanguage = activeContext.DefaultLanguage is not null;
            bool isValueString = rawValue.Kind == JsonNodeKind.String;
            bool hasNullMapping = activeContext.TryGetTerm(activeProperty ?? string.Empty, out TermDefinition? propertyDef)
                && propertyDef is { HasLanguageMapping: true, LanguageMapping: null };

            if(isValueOnlyKey && !typeIsNone && (!hasDefaultLanguage || !isValueString || hasNullMapping))
            {
                return ToObjectGraph(rawValue);
            }

            Dictionary<string, object?> rval = new(StringComparer.Ordinal);
            if(preserveIndex)
            {
                value.TryGetProperty(JsonLdKeywords.Index, out JsonNode indexNode);
                rval[CompactIriCore(activeContext, inverse, JsonLdKeywords.Index, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Index] = ToObjectGraph(indexNode);
            }
            if(hasValueType)
            {
                string typeAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Type, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Type;
                rval[typeAlias] = CompactIriCore(activeContext, inverse, valueType.GetString(), value: null, vocab: true, reverse: false);
            }
            else if(hasValueLanguage)
            {
                string languageAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Language, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Language;
                rval[languageAlias] = ToObjectGraph(valueLanguage);
            }
            if(hasValueDirection)
            {
                string directionAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Direction, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Direction;
                rval[directionAlias] = ToObjectGraph(valueDirection);
            }

            string valueAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Value, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Value;
            rval[valueAlias] = ToObjectGraph(rawValue);
            return rval;
        }

        //Node reference.
        value.TryGetProperty(JsonLdKeywords.Id, out JsonNode idNode);
        return CompactNodeReference(activeContext, inverse, env, activeProperty, idNode);
    }

    /// <summary>Compacts a bare node reference's <c>@id</c> (§ Value Compaction, subject-reference case): a scalar under an <c>@id</c>/<c>@vocab</c>-coerced property or a <c>@graph</c> alias, otherwise the aliased <c>{"@id": ...}</c> object.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="activeProperty">The compact property the reference appears under.</param>
    /// <param name="idNode">The reference's <c>@id</c> value.</param>
    /// <returns>The compacted reference.</returns>
    private static object? CompactNodeReference(LinkedDataContext activeContext, InverseContext inverse, CompactionEnv env, string? activeProperty, JsonNode idNode)
    {
        string? expandedProperty = activeContext.ExpandIri(activeProperty, vocab: true);
        string? referenceType = TypeMappingOf(activeContext, activeProperty);
        string? compacted = CompactIriCore(
            activeContext, inverse, idNode.GetString(), value: null,
            vocab: string.Equals(referenceType, "@vocab", StringComparison.Ordinal), reverse: false,
            documentBase: activeContext.BaseIri ?? env.DocumentBase);

        if(string.Equals(referenceType, "@id", StringComparison.Ordinal)
            || string.Equals(referenceType, "@vocab", StringComparison.Ordinal)
            || string.Equals(expandedProperty, JsonLdKeywords.Graph, StringComparison.Ordinal))
        {
            return compacted;
        }

        string idAlias = CompactIriCore(activeContext, inverse, JsonLdKeywords.Id, value: null, vocab: true, reverse: false) ?? JsonLdKeywords.Id;
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [idAlias] = compacted
        };
    }

    /// <summary>Compacts the value(s) of an <c>@id</c> entry (document-relative), unwrapping a single value to a scalar.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="expandedValue">The expanded <c>@id</c> value.</param>
    /// <returns>The compacted id, a scalar for a single value or a list for several.</returns>
    private static object? CompactIdValues(LinkedDataContext activeContext, InverseContext inverse, CompactionEnv env, JsonNode expandedValue)
    {
        string? effectiveBase = activeContext.BaseIri ?? env.DocumentBase;
        if(expandedValue.Kind == JsonNodeKind.String)
        {
            return CompactIriCore(activeContext, inverse, expandedValue.GetString(), value: null, vocab: false, reverse: false, documentBase: effectiveBase);
        }

        if(expandedValue.Kind != JsonNodeKind.Array)
        {
            return ToObjectGraph(expandedValue);
        }

        List<object?> compacted = new();
        foreach(JsonNode item in expandedValue.EnumerateArray())
        {
            compacted.Add(item.Kind == JsonNodeKind.String
                ? CompactIriCore(activeContext, inverse, item.GetString(), value: null, vocab: false, reverse: false, documentBase: effectiveBase)
                : ToObjectGraph(item));
        }

        return compacted.Count == 1 ? compacted[0] : compacted;
    }

    /// <summary>Compacts the value(s) of a <c>@type</c> entry (vocab-relative), unwrapping a single value to a scalar.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="expandedValue">The expanded <c>@type</c> value.</param>
    /// <returns>The compacted type, a scalar for a single value or a list for several.</returns>
    private static object? CompactTypeValues(LinkedDataContext activeContext, InverseContext inverse, JsonNode expandedValue)
    {
        if(expandedValue.Kind == JsonNodeKind.String)
        {
            return CompactIriCore(activeContext, inverse, expandedValue.GetString(), value: null, vocab: true, reverse: false);
        }

        if(expandedValue.Kind != JsonNodeKind.Array)
        {
            return ToObjectGraph(expandedValue);
        }

        List<object?> compacted = new();
        foreach(JsonNode item in expandedValue.EnumerateArray())
        {
            compacted.Add(item.Kind == JsonNodeKind.String
                ? CompactIriCore(activeContext, inverse, item.GetString(), value: null, vocab: true, reverse: false)
                : ToObjectGraph(item));
        }

        return compacted.Count == 1 ? compacted[0] : compacted;
    }

    /// <summary>
    /// Compacts an IRI or keyword (§4.3): keyword-alias fast path, then
    /// value-aware term selection through the inverse context, then
    /// <c>@vocab</c>-relative compaction, then compact-IRI (CURIE) selection,
    /// falling through to the IRI unchanged.
    /// </summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="iri">The IRI or keyword to compact.</param>
    /// <param name="value">The value being compacted, used to select the most specific term; <c>null</c> for keyword/property keys.</param>
    /// <param name="vocab">Whether vocabulary-relative compaction (and term selection) applies.</param>
    /// <param name="reverse">Whether a reverse property is being compacted.</param>
    /// <param name="documentBase">The base IRI for document-relative compaction; reserved for relative-IRI handling.</param>
    /// <returns>The compact term, prefix, keyword alias, or the IRI unchanged; <c>null</c> when <paramref name="iri"/> is <c>null</c>.</returns>
    private static string? CompactIriCore(
        LinkedDataContext activeContext,
        InverseContext inverse,
        string? iri,
        JsonNode? value,
        bool vocab,
        bool reverse,
        string? documentBase = null)
    {
        if(iri is null)
        {
            return null;
        }

        //A keyword may compact to a simple alias registered in the inverse context.
        if(IriUtils.IsKeyword(iri)
            && inverse.Entries.TryGetValue(iri, out Dictionary<string, TypeLanguageMaps>? keywordEntry)
            && keywordEntry.TryGetValue(JsonLdKeywords.None, out TypeLanguageMaps? keywordMaps)
            && keywordMaps.Type.TryGetValue(JsonLdKeywords.None, out string? keywordAlias))
        {
            return keywordAlias;
        }

        if(vocab && inverse.Entries.ContainsKey(iri))
        {
            string defaultLanguage = activeContext.DefaultLanguage ?? JsonLdKeywords.None;
            List<string> containers = new();
            bool valueIsObject = value is { Kind: JsonNodeKind.Object };

            if(valueIsObject && HasProperty(value, JsonLdKeywords.Index) && !HasProperty(value, JsonLdKeywords.Graph))
            {
                containers.Add("@index");
                containers.Add("@index@set");
            }

            if(IsGraphNode(value))
            {
                if(HasProperty(value, JsonLdKeywords.Index))
                {
                    containers.Add("@graph@index");
                    containers.Add("@graph@index@set");
                    containers.Add("@index");
                    containers.Add("@index@set");
                }
                if(HasProperty(value, JsonLdKeywords.Id))
                {
                    containers.Add("@graph@id");
                    containers.Add("@graph@id@set");
                }

                containers.Add("@graph");
                containers.Add("@graph@set");
                containers.Add("@set");

                if(!HasProperty(value, JsonLdKeywords.Index))
                {
                    containers.Add("@graph@index");
                    containers.Add("@graph@index@set");
                    containers.Add("@index");
                    containers.Add("@index@set");
                }
                if(!HasProperty(value, JsonLdKeywords.Id))
                {
                    containers.Add("@graph@id");
                    containers.Add("@graph@id@set");
                }
            }
            else if(valueIsObject && !IsValueNode(value))
            {
                containers.Add("@id");
                containers.Add("@id@set");
                containers.Add("@type");
                containers.Add("@set@type");
            }

            string typeOrLanguage = JsonLdKeywords.Language;
            string typeOrLanguageValue = "@null";

            if(reverse)
            {
                typeOrLanguage = JsonLdKeywords.Type;
                typeOrLanguageValue = JsonLdKeywords.Reverse;
                containers.Add("@set");
            }
            else if(IsListNode(value))
            {
                SelectListTypeLanguage(value!.Value, defaultLanguage, containers, ref typeOrLanguage, ref typeOrLanguageValue);
            }
            else
            {
                if(IsValueNode(value))
                {
                    JsonNode v = value!.Value;
                    bool hasIndex = HasProperty(value, JsonLdKeywords.Index);
                    if(v.TryGetProperty(JsonLdKeywords.Language, out JsonNode lang) && !hasIndex)
                    {
                        containers.Add("@language");
                        containers.Add("@language@set");
                        typeOrLanguageValue = lang.Kind == JsonNodeKind.String ? lang.GetString() : "@null";
                        if(v.TryGetProperty(JsonLdKeywords.Direction, out JsonNode dir) && dir.Kind == JsonNodeKind.String)
                        {
                            typeOrLanguageValue = string.Concat(typeOrLanguageValue, "_", dir.GetString());
                        }
                    }
                    else if(v.TryGetProperty(JsonLdKeywords.Direction, out JsonNode dir) && !hasIndex)
                    {
                        typeOrLanguageValue = dir.Kind == JsonNodeKind.String ? string.Concat("_", dir.GetString()) : "_";
                    }
                    else if(v.TryGetProperty(JsonLdKeywords.Type, out JsonNode vt))
                    {
                        typeOrLanguage = JsonLdKeywords.Type;
                        typeOrLanguageValue = vt.Kind == JsonNodeKind.String ? vt.GetString() : "@null";
                    }
                }
                else
                {
                    typeOrLanguage = JsonLdKeywords.Type;
                    typeOrLanguageValue = JsonLdKeywords.Id;
                }

                containers.Add("@set");
            }

            containers.Add("@none");

            //An index map can key on @none even without an explicit @index, so it is a low-priority fallback.
            if(valueIsObject && !HasProperty(value, JsonLdKeywords.Index))
            {
                containers.Add("@index");
                containers.Add("@index@set");
            }

            //A value with neither type nor language can ride a @language map.
            if(IsValueNode(value) && KeyCount(value!.Value) == 1)
            {
                containers.Add("@language");
                containers.Add("@language@set");
            }

            string? term = SelectTerm(activeContext, inverse, iri, value, containers, typeOrLanguage, typeOrLanguageValue);
            if(term is not null)
            {
                return term;
            }
        }

        //Vocabulary-relative compaction.
        if(vocab && activeContext.VocabularyMapping is { } vocabIri
            && iri.Length > vocabIri.Length
            && iri.StartsWith(vocabIri, StringComparison.Ordinal) && !string.Equals(iri, vocabIri, StringComparison.Ordinal))
        {
            string suffix = iri[vocabIri.Length..];
            if(!activeContext.TryGetTerm(suffix, out _))
            {
                return suffix;
            }
        }

        //Compact-IRI (CURIE) selection: shortest-then-least over usable prefix:suffix forms.
        string? choice = null;
        foreach(string term in activeContext.Terms)
        {
            if(term.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }
            if(!activeContext.TryGetTerm(term, out TermDefinition? def) || def is not { Prefix: true, IriMapping: { } termIri })
            {
                continue;
            }
            if(iri.Length <= termIri.Length || !iri.StartsWith(termIri, StringComparison.Ordinal))
            {
                continue;
            }

            string curie = string.Concat(term, ":", iri[termIri.Length..]);
            bool curieIsTerm = activeContext.TryGetTerm(curie, out TermDefinition? curieDef);
            bool usable = !curieIsTerm || (value is null && curieDef is { IriMapping: { } cm } && string.Equals(cm, iri, StringComparison.Ordinal));
            if(usable && (choice is null || CompareShortestLeast(curie, choice) < 0))
            {
                choice = curie;
            }
        }
        if(choice is not null)
        {
            return choice;
        }

        //An absolute IRI that begins with "prefix:" of a defined prefix term would be misread as a compact IRI.
        foreach(string term in activeContext.Terms)
        {
            if(activeContext.TryGetTerm(term, out TermDefinition? def) && def is { Prefix: true }
                && iri.StartsWith(string.Concat(term, ":"), StringComparison.Ordinal))
            {
                throw new JsonLdProcessingException($"Absolute IRI '{iri}' is confused with prefix '{term}'.");
            }
        }

        //Document-relative compaction against the base IRI.
        if(!vocab && documentBase is not null)
        {
            string relative = RemoveBase(documentBase, iri);

            //A relative IRI that reads like a keyword (e.g. "@special") is disambiguated with a "./" prefix.
            return LooksLikeKeyword(relative) ? string.Concat("./", relative) : relative;
        }

        return iri;
    }

    /// <summary>
    /// Expresses <paramref name="iri"/> as a reference relative to
    /// <paramref name="baseIri"/> (RFC 3986 §5.3 reversed): strips a shared
    /// scheme/authority/path prefix and prepends <c>../</c> for each diverging
    /// base path segment. Returns <paramref name="iri"/> unchanged when it does
    /// not share the base's root.
    /// </summary>
    /// <param name="baseIri">The base IRI.</param>
    /// <param name="iri">The absolute IRI to relativise.</param>
    /// <returns>The relative reference, or the IRI unchanged.</returns>
    private static string RemoveBase(string baseIri, string iri)
    {
        (string? baseScheme, string? baseAuthority, string basePath, _, _) = ParseIri(baseIri);
        (string? iriScheme, string? iriAuthority, string iriPath, string? iriQuery, string? iriFragment) = ParseIri(iri);

        string root = baseScheme is not null && baseAuthority is not null
            ? string.Concat(baseScheme, "://", baseAuthority)
            : string.Empty;

        if(root.Length == 0 || !iri.StartsWith(root, StringComparison.Ordinal)
            || iriScheme is null || !string.Equals(iriScheme, baseScheme, StringComparison.Ordinal))
        {
            return iri;
        }

        List<string> baseSegments = new(basePath.Split('/'));
        List<string> iriSegments = new(iriPath.Split('/'));
        int last = iriFragment is not null || iriQuery is not null ? 0 : 1;

        while(baseSegments.Count > 0 && iriSegments.Count > last)
        {
            if(!string.Equals(baseSegments[0], iriSegments[0], StringComparison.Ordinal))
            {
                break;
            }

            baseSegments.RemoveAt(0);
            iriSegments.RemoveAt(0);
        }

        System.Text.StringBuilder builder = new();
        if(baseSegments.Count > 0)
        {
            //The base's final segment is the document itself, not a directory; it does not contribute a "../".
            baseSegments.RemoveAt(baseSegments.Count - 1);
            for(int i = 0; i < baseSegments.Count; i++)
            {
                builder.Append("../");
            }
        }

        builder.Append(string.Join('/', iriSegments));
        if(iriQuery is not null)
        {
            builder.Append('?').Append(iriQuery);
        }
        if(iriFragment is not null)
        {
            builder.Append('#').Append(iriFragment);
        }

        string result = builder.ToString();
        return result.Length == 0 ? "./" : result;
    }

    /// <summary>Splits an IRI into scheme, authority, path, query, and fragment using the RFC 3986 generic-syntax boundaries.</summary>
    /// <param name="iri">The IRI to split.</param>
    /// <returns>The five components; scheme/authority are <c>null</c> when absent, path is always present (possibly empty).</returns>
    private static (string? Scheme, string? Authority, string Path, string? Query, string? Fragment) ParseIri(string iri)
    {
        string rest = iri;
        string? fragment = null;
        int hash = rest.IndexOf('#', StringComparison.Ordinal);
        if(hash >= 0)
        {
            fragment = rest[(hash + 1)..];
            rest = rest[..hash];
        }

        string? query = null;
        int question = rest.IndexOf('?', StringComparison.Ordinal);
        if(question >= 0)
        {
            query = rest[(question + 1)..];
            rest = rest[..question];
        }

        string? scheme = null;
        string? authority = null;
        int schemeSep = rest.IndexOf("://", StringComparison.Ordinal);
        if(schemeSep >= 0)
        {
            scheme = rest[..schemeSep];
            string afterScheme = rest[(schemeSep + 3)..];
            int slash = afterScheme.IndexOf('/', StringComparison.Ordinal);
            if(slash >= 0)
            {
                authority = afterScheme[..slash];
                rest = afterScheme[slash..];
            }
            else
            {
                authority = afterScheme;
                rest = string.Empty;
            }
        }

        return (scheme, authority, rest, query, fragment);
    }

    /// <summary>Whether a relative reference reads like a JSON-LD keyword (<c>@</c> followed by ASCII letters), which must be escaped with a <c>./</c> prefix.</summary>
    /// <param name="value">The relative reference.</param>
    /// <returns><see langword="true"/> when keyword-like.</returns>
    private static bool LooksLikeKeyword(string value)
    {
        if(value.Length < 2 || value[0] != '@')
        {
            return false;
        }

        for(int i = 1; i < value.Length; i++)
        {
            if(!char.IsAsciiLetter(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Determines the type/language profile of a list value for term selection (§4.3 step 3.7), finding the type or language common to all members.</summary>
    /// <param name="value">The list object.</param>
    /// <param name="defaultLanguage">The active default language.</param>
    /// <param name="containers">The container-preference list to extend.</param>
    /// <param name="typeOrLanguage">The selected type/language axis (updated).</param>
    /// <param name="typeOrLanguageValue">The selected type/language value (updated).</param>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "BCP47 language subtags are canonically lower-case per the JSON-LD / i18n specifications.")]
    private static void SelectListTypeLanguage(JsonNode value, string defaultLanguage, List<string> containers, ref string typeOrLanguage, ref string typeOrLanguageValue)
    {
        if(!HasProperty(value, JsonLdKeywords.Index))
        {
            containers.Add("@list");
        }

        value.TryGetProperty(JsonLdKeywords.List, out JsonNode list);
        bool empty = list.Kind != JsonNodeKind.Array || IsEmptyArray(list);
        if(empty)
        {
            typeOrLanguage = "@any";
            typeOrLanguageValue = "@none";
            return;
        }

        string? commonLanguage = null;
        string? commonType = null;
        foreach(JsonNode item in list.EnumerateArray())
        {
            string itemLanguage = "@none";
            string itemType = "@none";
            bool itemIsValue = IsValueNode(item);
            if(itemIsValue)
            {
                if(item.TryGetProperty(JsonLdKeywords.Direction, out JsonNode dir) && dir.Kind == JsonNodeKind.String)
                {
                    string lang = item.TryGetProperty(JsonLdKeywords.Language, out JsonNode l) && l.Kind == JsonNodeKind.String ? l.GetString().ToLowerInvariant() : string.Empty;
                    itemLanguage = string.Concat(lang, "_", dir.GetString());
                }
                else if(item.TryGetProperty(JsonLdKeywords.Language, out JsonNode l) && l.Kind == JsonNodeKind.String)
                {
                    itemLanguage = l.GetString().ToLowerInvariant();
                }
                else if(item.TryGetProperty(JsonLdKeywords.Type, out JsonNode t) && t.Kind == JsonNodeKind.String)
                {
                    itemType = t.GetString();
                }
                else
                {
                    itemLanguage = "@null";
                }
            }
            else
            {
                itemType = JsonLdKeywords.Id;
            }

            if(commonLanguage is null)
            {
                commonLanguage = itemLanguage;
            }
            else if(!string.Equals(itemLanguage, commonLanguage, StringComparison.Ordinal) && itemIsValue)
            {
                commonLanguage = "@none";
            }

            if(commonType is null)
            {
                commonType = itemType;
            }
            else if(!string.Equals(itemType, commonType, StringComparison.Ordinal))
            {
                commonType = "@none";
            }

            if(string.Equals(commonLanguage, "@none", StringComparison.Ordinal) && string.Equals(commonType, "@none", StringComparison.Ordinal))
            {
                break;
            }
        }

        commonLanguage ??= "@none";
        commonType ??= "@none";
        if(!string.Equals(commonType, "@none", StringComparison.Ordinal))
        {
            typeOrLanguage = JsonLdKeywords.Type;
            typeOrLanguageValue = commonType;
        }
        else
        {
            typeOrLanguageValue = commonLanguage;
        }
    }

    /// <summary>Selects the preferred term from the inverse context (§4.3.5 Term Selection) for the given containers and type/language preference.</summary>
    /// <param name="activeContext">The active context (used to sub-compact an <c>@id</c> value).</param>
    /// <param name="inverse">The inverse context.</param>
    /// <param name="iri">The IRI to select a term for.</param>
    /// <param name="value">The value being compacted.</param>
    /// <param name="containers">The ordered container preferences.</param>
    /// <param name="typeOrLanguage">The type/language axis.</param>
    /// <param name="typeOrLanguageValue">The preferred type/language value.</param>
    /// <returns>The selected term, or <c>null</c> when none matches.</returns>
    private static string? SelectTerm(
        LinkedDataContext activeContext,
        InverseContext inverse,
        string iri,
        JsonNode? value,
        List<string> containers,
        string typeOrLanguage,
        string typeOrLanguageValue)
    {
        if(typeOrLanguageValue is null)
        {
            typeOrLanguageValue = "@null";
        }

        List<string> prefs = new();
        bool idLike = (string.Equals(typeOrLanguageValue, JsonLdKeywords.Id, StringComparison.Ordinal)
            || string.Equals(typeOrLanguageValue, JsonLdKeywords.Reverse, StringComparison.Ordinal))
            && value is { Kind: JsonNodeKind.Object } && HasProperty(value, JsonLdKeywords.Id);

        if(idLike)
        {
            if(string.Equals(typeOrLanguageValue, JsonLdKeywords.Reverse, StringComparison.Ordinal))
            {
                prefs.Add(JsonLdKeywords.Reverse);
            }

            value!.Value.TryGetProperty(JsonLdKeywords.Id, out JsonNode idNode);
            string? term = CompactIriCore(activeContext, inverse, idNode.GetString(), value: null, vocab: true, reverse: false);
            if(term is not null && activeContext.TryGetTerm(term, out TermDefinition? def) && def is { IriMapping: { } mapping }
                && string.Equals(mapping, idNode.GetString(), StringComparison.Ordinal))
            {
                prefs.Add("@vocab");
                prefs.Add(JsonLdKeywords.Id);
            }
            else
            {
                prefs.Add(JsonLdKeywords.Id);
                prefs.Add("@vocab");
            }
        }
        else
        {
            prefs.Add(typeOrLanguageValue);
            int underscore = typeOrLanguageValue.IndexOf('_', StringComparison.Ordinal);
            if(underscore > 0)
            {
                prefs.Add(string.Concat("_", typeOrLanguageValue[(underscore + 1)..]));
            }
        }
        prefs.Add("@none");

        if(!inverse.Entries.TryGetValue(iri, out Dictionary<string, TypeLanguageMaps>? containerMap))
        {
            return null;
        }

        foreach(string container in containers)
        {
            if(!containerMap.TryGetValue(container, out TypeLanguageMaps? maps))
            {
                continue;
            }

            Dictionary<string, string> valueMap = maps.For(typeOrLanguage);
            foreach(string pref in prefs)
            {
                if(valueMap.TryGetValue(pref, out string? selected))
                {
                    return selected;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the inverse context (§4.2.1): a map from IRI to container to
    /// type/language axis to value to the preferred term, used to drive
    /// value-aware term selection during compaction.
    /// </summary>
    /// <param name="activeContext">The active context to invert.</param>
    /// <returns>The inverse context.</returns>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "BCP47 language subtags are canonically lower-case per the JSON-LD / i18n specifications.")]
    private static InverseContext BuildInverseContext(LinkedDataContext activeContext)
    {
        InverseContext inverse = new();
        string defaultLanguage = (activeContext.DefaultLanguage ?? JsonLdKeywords.None).ToLowerInvariant();
        string? defaultDirection = activeContext.DefaultBaseDirection;

        List<string> terms = new(activeContext.Terms);
        terms.Sort(ShortestThenLeast);

        foreach(string term in terms)
        {
            if(!activeContext.TryGetTerm(term, out TermDefinition? def) || def is null || def.IriMapping is null)
            {
                continue;
            }

            string container = JoinContainer(def.ContainerMapping);
            string iri = def.IriMapping;

            if(!inverse.Entries.TryGetValue(iri, out Dictionary<string, TypeLanguageMaps>? entry))
            {
                entry = new Dictionary<string, TypeLanguageMaps>(StringComparer.Ordinal);
                inverse.Entries[iri] = entry;
            }
            if(!entry.TryGetValue(container, out TypeLanguageMaps? maps))
            {
                maps = new TypeLanguageMaps();
                entry[container] = maps;
            }

            AddPreferredTerm(maps.Any, JsonLdKeywords.None, term);

            if(def.ReverseProperty is not null)
            {
                AddPreferredTerm(maps.Type, JsonLdKeywords.Reverse, term);
            }
            else if(string.Equals(def.TypeMapping, JsonLdKeywords.None, StringComparison.Ordinal))
            {
                AddPreferredTerm(maps.Any, JsonLdKeywords.None, term);
                AddPreferredTerm(maps.Language, JsonLdKeywords.None, term);
                AddPreferredTerm(maps.Type, JsonLdKeywords.None, term);
            }
            else if(def.TypeMapping is { } typeMapping)
            {
                AddPreferredTerm(maps.Type, typeMapping, term);
            }
            else if(def is { HasLanguageMapping: true, HasDirectionMapping: true })
            {
                AddLanguageDirectionTerm(maps.Language, def.LanguageMapping, def.DirectionMapping, term);
            }
            else if(def.HasLanguageMapping)
            {
                AddPreferredTerm(maps.Language, (def.LanguageMapping ?? "@null").ToLowerInvariant(), term);
            }
            else if(def.HasDirectionMapping)
            {
                AddPreferredTerm(maps.Language, def.DirectionMapping is { } d ? string.Concat("_", d) : JsonLdKeywords.None, term);
            }
            else if(defaultDirection is { } dir)
            {
                AddPreferredTerm(maps.Language, string.Concat("_", dir), term);
                AddPreferredTerm(maps.Language, JsonLdKeywords.None, term);
                AddPreferredTerm(maps.Type, JsonLdKeywords.None, term);
            }
            else
            {
                AddPreferredTerm(maps.Language, defaultLanguage, term);
                AddPreferredTerm(maps.Language, JsonLdKeywords.None, term);
                AddPreferredTerm(maps.Type, JsonLdKeywords.None, term);
            }
        }

        return inverse;
    }

    /// <summary>Adds a language/direction-keyed preferred term for a term carrying both a language and a direction mapping.</summary>
    /// <param name="languageMap">The language axis map.</param>
    /// <param name="language">The term's language mapping (possibly null).</param>
    /// <param name="direction">The term's direction mapping (possibly null).</param>
    /// <param name="term">The term name.</param>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "BCP47 language subtags are canonically lower-case per the JSON-LD / i18n specifications.")]
    private static void AddLanguageDirectionTerm(Dictionary<string, string> languageMap, string? language, string? direction, string term)
    {
        string key = (language, direction) switch
        {
            ({ } l, { } d) => string.Concat(l, "_", d).ToLowerInvariant(),
            ({ } l, null) => l.ToLowerInvariant(),
            (null, { } d) => string.Concat("_", d),
            _ => "@null"
        };
        AddPreferredTerm(languageMap, key, term);
    }

    /// <summary>Records <paramref name="term"/> as the preferred term for <paramref name="key"/> unless one is already present (shorter/earlier terms win because they are visited first).</summary>
    /// <param name="map">The type/language value map.</param>
    /// <param name="key">The type/language value key.</param>
    /// <param name="term">The candidate term.</param>
    private static void AddPreferredTerm(Dictionary<string, string> map, string key, string term)
    {
        if(!map.ContainsKey(key))
        {
            map[key] = term;
        }
    }

    /// <summary>Joins a container mapping into its inverse-context key: the sorted concatenation of its values, or <c>@none</c> when empty.</summary>
    /// <param name="containerMapping">The term's container mapping.</param>
    /// <returns>The joined container key.</returns>
    private static string JoinContainer(IReadOnlyList<string> containerMapping)
    {
        if(containerMapping.Count == 0)
        {
            return JsonLdKeywords.None;
        }
        if(containerMapping.Count == 1)
        {
            return containerMapping[0];
        }

        List<string> sorted = new(containerMapping);
        sorted.Sort(StringComparer.Ordinal);
        return string.Concat(sorted);
    }

    /// <summary>Resolves the object a property's compacted value is added to: a nested <c>@nest</c> map when the term declares one, otherwise the node object itself.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="result">The node object under construction.</param>
    /// <param name="itemActiveProperty">The compact property.</param>
    /// <returns>The target object.</returns>
    private static Dictionary<string, object?> NestTarget(LinkedDataContext activeContext, Dictionary<string, object?> result, string itemActiveProperty)
    {
        if(activeContext.TryGetTerm(itemActiveProperty, out TermDefinition? def) && def is { NestValue: { } nest })
        {
            //The nest target must resolve to @nest (W3C JSON-LD 1.1 §4.1, invalid @nest value).
            if(!string.Equals(activeContext.ExpandIri(nest, vocab: true), JsonLdKeywords.Nest, StringComparison.Ordinal))
            {
                throw new JsonLdProcessingException(JsonLdErrorCode.InvalidNestValue,
                    $"Nested property '{itemActiveProperty}' must have an @nest value resolving to @nest, but '{nest}' does not.");
            }

            if(result.TryGetValue(nest, out object? existing) && existing is Dictionary<string, object?> nestMap)
            {
                return nestMap;
            }

            Dictionary<string, object?> created = new(StringComparer.Ordinal);
            result[nest] = created;
            return created;
        }

        return result;
    }

    /// <summary>Gets the map object stored at <paramref name="property"/> in <paramref name="target"/>, creating an empty one when absent.</summary>
    /// <param name="target">The owning object.</param>
    /// <param name="property">The map property.</param>
    /// <returns>The (existing or new) map object.</returns>
    private static Dictionary<string, object?> GetOrCreateMap(Dictionary<string, object?> target, string property)
    {
        if(target.TryGetValue(property, out object? existing) && existing is Dictionary<string, object?> map)
        {
            return map;
        }

        Dictionary<string, object?> created = new(StringComparer.Ordinal);
        target[property] = created;
        return created;
    }

    /// <summary>Extracts the first value of a property-valued <c>@index</c> term from a compacted item and trims it from the item, returning the key or <c>null</c> when not a string.</summary>
    /// <param name="compactedItem">The compacted item.</param>
    /// <param name="indexKey">The compacted index property key to look up first.</param>
    /// <param name="fallbackKey">An alternate index property key tried when <paramref name="indexKey"/> is absent.</param>
    /// <returns>The map key, or <c>null</c>.</returns>
    private static string? ExtractPropertyValuedIndexKey(object? compactedItem, string indexKey, string fallbackKey)
    {
        if(compactedItem is not Dictionary<string, object?> item)
        {
            return null;
        }

        if(!item.TryGetValue(indexKey, out object? raw))
        {
            indexKey = fallbackKey;
            if(!item.TryGetValue(indexKey, out raw))
            {
                return null;
            }
        }

        List<object?> values = raw as List<object?> ?? new List<object?> { raw };
        if(values.Count == 0 || values[0] is not string key)
        {
            return null;
        }

        switch(values.Count)
        {
            case 1:
                item.Remove(indexKey);
                break;
            case 2:
                item[indexKey] = values[1];
                break;
            default:
                item[indexKey] = new List<object?>(values.GetRange(1, values.Count - 1));
                break;
        }

        return key;
    }

    /// <summary>Extracts the first entry of an aliased type list from a compacted item, trims it, and returns it as the map key.</summary>
    /// <param name="item">The compacted item map.</param>
    /// <param name="typeAlias">The compacted <c>@type</c> alias.</param>
    /// <returns>The first type, or <c>null</c>.</returns>
    private static string? ExtractFirstAndTrim(Dictionary<string, object?> item, string typeAlias)
    {
        if(!item.TryGetValue(typeAlias, out object? raw))
        {
            return null;
        }

        List<object?> values = raw as List<object?> ?? new List<object?> { raw };
        string? key = values.Count > 0 ? values[0] as string : null;

        switch(values.Count)
        {
            case <= 1:
                item.Remove(typeAlias);
                break;
            case 2:
                item[typeAlias] = values[1];
                break;
            default:
                item[typeAlias] = new List<object?>(values.GetRange(1, values.Count - 1));
                break;
        }

        return key;
    }

    /// <summary>Adds <paramref name="value"/> to <paramref name="subject"/> under <paramref name="property"/> (a faithful subset of the specification's add-value rule, duplicates always allowed).</summary>
    /// <param name="subject">The owning object.</param>
    /// <param name="property">The property name.</param>
    /// <param name="value">The value to add.</param>
    /// <param name="propertyIsArray">Whether the property should always hold an array.</param>
    /// <param name="valueIsArray">Whether the value itself is the array to store directly.</param>
    private static void AddValue(Dictionary<string, object?> subject, string property, object? value, bool propertyIsArray = false, bool valueIsArray = false)
    {
        if(valueIsArray)
        {
            subject[property] = value;
            return;
        }

        if(value is List<object?> list)
        {
            if(list.Count == 0 && propertyIsArray && !subject.ContainsKey(property))
            {
                subject[property] = new List<object?>();
            }
            foreach(object? item in list)
            {
                AddValue(subject, property, item, propertyIsArray);
            }
            return;
        }

        if(subject.TryGetValue(property, out object? existing))
        {
            if(existing is not List<object?> existingList)
            {
                existingList = new List<object?> { existing };
                subject[property] = existingList;
            }

            existingList.Add(value);
            return;
        }

        subject[property] = propertyIsArray ? new List<object?> { value } : value;
    }

    /// <summary>Gets the container mapping of a compact property (or term), or an empty list when the property is null or unmapped.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="property">The compact property name.</param>
    /// <returns>The container mapping.</returns>
    private static IReadOnlyList<string> ContainerOf(LinkedDataContext activeContext, string? property)
    {
        if(property is not null && activeContext.TryGetTerm(property, out TermDefinition? def) && def is not null)
        {
            return def.ContainerMapping;
        }

        return [];
    }

    /// <summary>Gets the type coercion of a compact property, or <c>null</c> when unmapped.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="property">The compact property name.</param>
    /// <returns>The type mapping, or <c>null</c>.</returns>
    private static string? TypeMappingOf(LinkedDataContext activeContext, string? property)
    {
        return property is not null && activeContext.TryGetTerm(property, out TermDefinition? def) && def is not null ? def.TypeMapping : null;
    }

    /// <summary>Gets the language coercion of a compact property, falling back to the context default language.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="property">The compact property name.</param>
    /// <returns>The language mapping or default language, or <c>null</c>.</returns>
    private static string? LanguageOf(LinkedDataContext activeContext, string? property)
    {
        if(property is not null && activeContext.TryGetTerm(property, out TermDefinition? def) && def is { HasLanguageMapping: true })
        {
            return def.LanguageMapping;
        }

        return activeContext.DefaultLanguage;
    }

    /// <summary>Gets the direction coercion of a compact property, falling back to the context default direction.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="property">The compact property name.</param>
    /// <returns>The direction mapping or default direction, or <c>null</c>.</returns>
    private static string? DirectionOf(LinkedDataContext activeContext, string? property)
    {
        if(property is not null && activeContext.TryGetTerm(property, out TermDefinition? def) && def is { HasDirectionMapping: true })
        {
            return def.DirectionMapping;
        }

        return activeContext.DefaultBaseDirection;
    }

    /// <summary>Gets the <c>@index</c> property term of a compact property, or <c>null</c> when none.</summary>
    /// <param name="activeContext">The active context.</param>
    /// <param name="property">The compact property name.</param>
    /// <returns>The index property, or <c>null</c>.</returns>
    private static string? IndexOf(LinkedDataContext activeContext, string? property)
    {
        return property is not null && activeContext.TryGetTerm(property, out TermDefinition? def) && def is not null ? def.IndexMapping : null;
    }

    /// <summary>Whether <paramref name="container"/> contains the given container keyword.</summary>
    /// <param name="container">The container mapping.</param>
    /// <param name="keyword">The container keyword.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsContainer(IReadOnlyList<string> container, string keyword)
    {
        foreach(string entry in container)
        {
            if(string.Equals(entry, keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a node has a given property (false for a null node).</summary>
    /// <param name="node">The node, or <c>null</c>.</param>
    /// <param name="name">The property name.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HasProperty(JsonNode? node, string name)
    {
        return node is { Kind: JsonNodeKind.Object } && node.Value.TryGetProperty(name, out _);
    }

    /// <summary>Gets a string-valued property of a node, or <c>null</c> when absent or non-string.</summary>
    /// <param name="node">The node.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The string, or <c>null</c>.</returns>
    private static string? GetStringProperty(JsonNode node, string name)
    {
        return node.TryGetProperty(name, out JsonNode value) && value.Kind == JsonNodeKind.String ? value.GetString() : null;
    }

    /// <summary>Compares a node's string value to a target string (both must be present and equal).</summary>
    /// <param name="node">The node holding a candidate string.</param>
    /// <param name="target">The target string, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool StringEquals(JsonNode node, string? target)
    {
        return target is not null && node.Kind == JsonNodeKind.String && string.Equals(node.GetString(), target, StringComparison.Ordinal);
    }

    /// <summary>Counts the members of an object node.</summary>
    /// <param name="node">The object node.</param>
    /// <returns>The member count.</returns>
    private static int KeyCount(JsonNode node)
    {
        int count = 0;
        foreach(KeyValuePair<string, JsonNode> _ in node.EnumerateObject())
        {
            count++;
        }

        return count;
    }

    /// <summary>Returns an object's member names sorted ordinally.</summary>
    /// <param name="node">The object node.</param>
    /// <returns>The sorted key list.</returns>
    private static List<string> SortedKeys(JsonNode node)
    {
        List<string> keys = new();
        foreach(KeyValuePair<string, JsonNode> member in node.EnumerateObject())
        {
            keys.Add(member.Key);
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>Whether an array node has no items.</summary>
    /// <param name="node">The array node.</param>
    /// <returns><see langword="true"/> when empty.</returns>
    private static bool IsEmptyArray(JsonNode node)
    {
        foreach(JsonNode _ in node.EnumerateArray())
        {
            return false;
        }

        return true;
    }

    /// <summary>Whether an expanded property is one of the array-valued keywords (<c>@graph</c>, <c>@list</c>, <c>@included</c>) that undergo array processing rather than verbatim aliasing.</summary>
    /// <param name="keyword">The expanded property.</param>
    /// <returns><see langword="true"/> for an array-valued keyword.</returns>
    private static bool IsArrayValuedKeyword(string keyword)
    {
        return JsonLdKeywords.IsGraph(keyword)
            || string.Equals(keyword, JsonLdKeywords.List, StringComparison.Ordinal)
            || string.Equals(keyword, JsonLdKeywords.Included, StringComparison.Ordinal);
    }

    /// <summary>Whether a node is a value object (<c>{"@value": ...}</c>).</summary>
    /// <param name="node">The node, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when a value object.</returns>
    private static bool IsValueNode(JsonNode? node)
    {
        return node is { Kind: JsonNodeKind.Object } && node.Value.TryGetProperty(JsonLdKeywords.Value, out _);
    }

    /// <summary>Whether a node is a list object (<c>{"@list": ...}</c>).</summary>
    /// <param name="node">The node, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when a list object.</returns>
    private static bool IsListNode(JsonNode? node)
    {
        return node is { Kind: JsonNodeKind.Object } && node.Value.TryGetProperty(JsonLdKeywords.List, out _);
    }

    /// <summary>Whether a node is a graph object: it has <c>@graph</c> and, apart from <c>@id</c>/<c>@index</c>, no other members.</summary>
    /// <param name="node">The node, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when a graph object.</returns>
    private static bool IsGraphNode(JsonNode? node)
    {
        if(node is not { Kind: JsonNodeKind.Object } || !node.Value.TryGetProperty(JsonLdKeywords.Graph, out _))
        {
            return false;
        }

        foreach(KeyValuePair<string, JsonNode> member in node.Value.EnumerateObject())
        {
            if(!JsonLdKeywords.IsGraph(member.Key) && !JsonLdKeywords.IsId(member.Key) && !string.Equals(member.Key, JsonLdKeywords.Index, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a node is a simple graph object: a graph object with no <c>@id</c>.</summary>
    /// <param name="node">The node, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when a simple graph object.</returns>
    private static bool IsSimpleGraphNode(JsonNode? node)
    {
        return IsGraphNode(node) && !HasProperty(node, JsonLdKeywords.Id);
    }

    /// <summary>Whether a node is a bare node reference (an object whose only member is <c>@id</c>).</summary>
    /// <param name="node">The node, or <c>null</c>.</param>
    /// <returns><see langword="true"/> when a node reference.</returns>
    private static bool IsSubjectReferenceNode(JsonNode? node)
    {
        if(node is not { Kind: JsonNodeKind.Object })
        {
            return false;
        }

        int count = 0;
        bool hasId = false;
        foreach(KeyValuePair<string, JsonNode> member in node.Value.EnumerateObject())
        {
            count++;
            hasId |= JsonLdKeywords.IsId(member.Key);
        }

        return count == 1 && hasId;
    }

    /// <summary>Total order over term names: shorter first, then ordinal-least. Mirrors the inverse-context term ranking.</summary>
    /// <param name="x">The first term.</param>
    /// <param name="y">The second term.</param>
    /// <returns>A negative, zero, or positive comparison result.</returns>
    private static int ShortestThenLeast(string x, string y)
    {
        int lengthCompare = x.Length.CompareTo(y.Length);
        return lengthCompare != 0 ? lengthCompare : string.CompareOrdinal(x, y);
    }

    /// <summary>Compares two compact-IRI candidates: shorter first, then ordinal-least.</summary>
    /// <param name="a">The first candidate.</param>
    /// <param name="b">The second candidate.</param>
    /// <returns>A negative, zero, or positive comparison result.</returns>
    private static int CompareShortestLeast(string a, string b)
    {
        int lengthCompare = a.Length.CompareTo(b.Length);
        return lengthCompare != 0 ? lengthCompare : string.CompareOrdinal(a, b);
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

    private static object? ToObjectGraph(JsonNode node)
    {
        return node.Kind switch
        {
            JsonNodeKind.Null => null,
            JsonNodeKind.String => node.GetString(),
            JsonNodeKind.True => true,
            JsonNodeKind.False => false,
            //Numbers preserve their raw lexical token (compaction does not canonicalise @value numbers,
            //and @json literals must round-trip verbatim); the carrier serialises back to the exact form.
            JsonNodeKind.Number => new JsonLdJsonNumber(node.GetRawNumber()),
            JsonNodeKind.Array => MaterialiseArray(node),
            JsonNodeKind.Object => MaterialiseObject(node),
            _ => null
        };
    }

    private static List<object?> MaterialiseArray(JsonNode array)
    {
        List<object?> items = new();
        foreach(JsonNode item in array.EnumerateArray())
        {
            items.Add(ToObjectGraph(item));
        }
        return items;
    }

    private static Dictionary<string, object?> MaterialiseObject(JsonNode obj)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach(KeyValuePair<string, JsonNode> property in obj.EnumerateObject())
        {
            result[property.Key] = ToObjectGraph(property.Value);
        }
        return result;
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

    /// <summary>
    /// The per-compaction environment: the constants that flow unchanged
    /// through the recursive walk (document base, the compactArrays flag, and
    /// the resolver/parser/cancellation needed to process scoped contexts).
    /// </summary>
    private sealed class CompactionEnv
    {
        /// <summary>Gets the document base IRI for document-relative <c>@id</c> compaction, or <c>null</c>.</summary>
        public string? DocumentBase { get; init; }

        /// <summary>Gets a value indicating whether single-value arrays without a pinned container collapse to the value.</summary>
        public bool CompactArrays { get; init; }

        /// <summary>Gets the resolver for remote contexts referenced by scoped contexts.</summary>
        public required ContextResolverDelegate Resolver { get; init; }

        /// <summary>Gets the parser for remote context bodies.</summary>
        public required ParseJsonDelegate Parser { get; init; }

        /// <summary>Gets the token that aborts the operation.</summary>
        public CancellationToken CancellationToken { get; init; }
    }

    /// <summary>
    /// The inverse context (§4.2.1): IRI → container key → type/language maps.
    /// </summary>
    private sealed class InverseContext
    {
        /// <summary>Gets the IRI-keyed entries of the inverse context.</summary>
        public Dictionary<string, Dictionary<string, TypeLanguageMaps>> Entries { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The three type/language axes of an inverse-context container entry,
    /// each mapping a type/language value to its preferred term.
    /// </summary>
    private sealed class TypeLanguageMaps
    {
        /// <summary>Gets the <c>@language</c> axis map (language value → term).</summary>
        public Dictionary<string, string> Language { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the <c>@type</c> axis map (type value → term).</summary>
        public Dictionary<string, string> Type { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the <c>@any</c> axis map (always keyed by <c>@none</c>).</summary>
        public Dictionary<string, string> Any { get; } = new(StringComparer.Ordinal);

        /// <summary>Selects the axis map for a type/language selector.</summary>
        /// <param name="typeOrLanguage">One of <c>@type</c>, <c>@language</c>, or <c>@any</c>.</param>
        /// <returns>The corresponding axis map.</returns>
        public Dictionary<string, string> For(string typeOrLanguage)
        {
            return typeOrLanguage switch
            {
                _ when JsonLdKeywords.IsType(typeOrLanguage) => Type,
                _ when JsonLdKeywords.IsLanguage(typeOrLanguage) => Language,
                _ => Any
            };
        }
    }
}
