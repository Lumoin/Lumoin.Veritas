using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd.Internal;

/// <summary>
/// Computes successor active contexts for the three CBOR-LD scoped-context
/// triggers — embedded <c>@context</c>, type-scoped, and property-scoped —
/// during an encoder/decoder walk. Stateless apart from the remote-context
/// delegates: the walker threads the resulting <see cref="LinkedDataContext"/>
/// through its own recursion, and the persistent-map semantics of
/// <see cref="LinkedDataContext"/> make push/pop free (the call stack IS the
/// frame stack).
/// </summary>
/// <remarks>
/// <para>
/// Each <c>With*Async</c> method applies its context via
/// <see cref="ContextProcessing.ApplyEmbeddedContextsAsync"/>, then walks
/// the resulting context's newly-added term names and registers their
/// dynamic ids via <see cref="CborLdConversionState.AssignTermId"/>. Eager
/// id assignment is what keeps encoder and decoder id tables aligned: both
/// apply contexts at the same walk points in the same order, so both
/// allocate ids deterministically.
/// </para>
/// <para>
/// Type-scoped contexts are applied in alphabetical order of the type IRIs
/// per W3C JSON-LD 1.1 §4.1.4 step 8. Sort order matters because two types
/// defining conflicting term mappings produce different results depending
/// on which order they are applied.
/// </para>
/// </remarks>
internal readonly struct CborLdActiveContextScope
{
    private readonly FetchRemoteResourceDelegate? fetcher;
    private readonly ParseRemoteContextDelegate? parser;
    private readonly ProbeContextCacheDelegate? cache;

    public CborLdActiveContextScope(
        FetchRemoteResourceDelegate? fetcher,
        ParseRemoteContextDelegate? parser,
        ProbeContextCacheDelegate? cache)
    {
        this.fetcher = fetcher;
        this.parser = parser;
        this.cache = cache;
    }

    /// <summary>
    /// Applies an embedded <c>@context</c> entry encountered at the current
    /// map. Returns the successor context; the walker threads it through
    /// the remaining property walk of the same map.
    /// </summary>
    /// <param name="current">The pre-embedded active context.</param>
    /// <param name="contextNode">The <c>@context</c> value node from the document.</param>
    /// <param name="baseUrl">The base URL in effect at this point in the walk.</param>
    /// <param name="state">Conversion state; dynamic term ids are appended here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The successor active context after applying the embedded entry.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public async ValueTask<LinkedDataContext> WithEmbeddedContextAsync(
        LinkedDataContext current,
        CborLdInputNode contextNode,
        string? baseUrl,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(contextNode);
        ArgumentNullException.ThrowIfNull(state);

        int keyCounter = 0;
        IReadOnlyList<LinkedDataContextEntry> entries =
            CborLdContextExtraction.ExtractEntries(contextNode, baseUrl, ref keyCounter);

        LinkedDataContext next = await ApplyEntriesAsync(current, entries, baseUrl, cancellationToken).ConfigureAwait(false);
        AssignNewTermIds(current, next, state);
        return next;
    }

    /// <summary>
    /// Applies the type-scoped contexts triggered by <paramref name="typeIris"/>.
    /// For each IRI in alphabetical order, finds a term whose
    /// <see cref="TermDefinition.IriMapping"/> matches and whose
    /// <see cref="TermDefinition.ScopedContextEntries"/> is non-null, then
    /// applies those entries against the running context.
    /// </summary>
    /// <param name="current">The pre-type-scoped active context.</param>
    /// <param name="typeIris">Expanded type IRI values from the map's <c>@type</c>.</param>
    /// <param name="baseUrl">The base URL in effect at this point in the walk.</param>
    /// <param name="state">Conversion state; dynamic term ids are appended here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The successor active context after all matching type-scoped contexts are applied.</returns>
    /// <remarks>
    /// The type-to-term lookup is currently an O(N) scan over the
    /// <see cref="LinkedDataContext"/>'s terms. For documents with many types
    /// per map and many terms in the active context, an inverted
    /// <c>IriMapping → term</c> index on <see cref="LinkedDataContext"/> would
    /// reduce this to O(1) per type. Deferred until profiling motivates.
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public async ValueTask<LinkedDataContext> WithTypeScopedAsync(
        LinkedDataContext current,
        IReadOnlyList<string> typeIris,
        string? baseUrl,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(typeIris);
        ArgumentNullException.ThrowIfNull(state);

        if(typeIris.Count == 0)
        {
            return current;
        }

        //Sort alphabetically per JSON-LD 1.1 §4.1.4 step 8 — application
        //order is observable when two types define conflicting term mappings.
        string[] sortedTypeIris = new string[typeIris.Count];
        for(int i = 0; i < typeIris.Count; i++)
        {
            sortedTypeIris[i] = typeIris[i];
        }

        Array.Sort(sortedTypeIris, StringComparer.Ordinal);

        LinkedDataContext running = current;
        foreach(string typeIri in sortedTypeIris)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<LinkedDataContextEntry>? scopedEntries =
                FindScopedEntriesForType(running, typeIri);
            if(scopedEntries is null)
            {
                continue;
            }

            LinkedDataContext before = running;
            running = await ApplyEntriesAsync(running, scopedEntries, baseUrl, cancellationToken).ConfigureAwait(false);
            AssignNewTermIds(before, running, state);
        }

        return running;
    }

    /// <summary>
    /// Applies a property-scoped context when descending into a value whose
    /// term carries <see cref="TermDefinition.ScopedContextEntries"/>.
    /// </summary>
    /// <param name="current">The parent active context at the descent point.</param>
    /// <param name="propertyTerm">The term definition for the property being descended into.</param>
    /// <param name="baseUrl">The base URL in effect at this point in the walk.</param>
    /// <param name="state">Conversion state; dynamic term ids are appended here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active context for the value subtree, or <paramref name="current"/> when no scoped context is attached.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "JSON-LD specification uses string URIs throughout the context processing algorithm.")]
    public async ValueTask<LinkedDataContext> WithPropertyScopedAsync(
        LinkedDataContext current,
        TermDefinition propertyTerm,
        string? baseUrl,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(propertyTerm);
        ArgumentNullException.ThrowIfNull(state);

        if(propertyTerm.ScopedContextEntries is null)
        {
            return current;
        }

        LinkedDataContext next = await ApplyEntriesAsync(
            current, propertyTerm.ScopedContextEntries, baseUrl, cancellationToken).ConfigureAwait(false);
        AssignNewTermIds(current, next, state);
        return next;
    }

    private async ValueTask<LinkedDataContext> ApplyEntriesAsync(
        LinkedDataContext current,
        IReadOnlyList<LinkedDataContextEntry> entries,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        FetchRemoteResourceDelegate effectiveFetcher = fetcher ?? ThrowingFetcher;
        ParseRemoteContextDelegate effectiveParser = parser ?? ThrowingParser;
        try
        {
            return await ContextProcessing.ApplyEmbeddedContextsAsync(
                current, entries, baseUrl,
                effectiveFetcher, effectiveParser, cache,
                cancellationToken).ConfigureAwait(false);
        }
        catch(LinkedDataProcessingException ex) when(ex is not CborLdProcessingException)
        {
            //Normalize spec-violation exceptions to the CBOR-LD-specific
            //type at the format boundary, so the encoder/decoder's public
            //API surface throws CborLdProcessingException consistently
            //rather than leaking the format-agnostic base type.
            throw new CborLdProcessingException(ex);
        }
    }

    private static IReadOnlyList<LinkedDataContextEntry>? FindScopedEntriesForType(
        LinkedDataContext context,
        string typeIri)
    {
        //O(N) scan. Could be replaced by an inverted IriMapping → term
        //index on LinkedDataContext if profiling motivates.
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

    private static void AssignNewTermIds(
        LinkedDataContext before,
        LinkedDataContext after,
        CborLdConversionState state)
    {
        foreach(string termName in after.Terms)
        {
            if(!before.TryGetTerm(termName, out _))
            {
                _ = state.AssignTermId(termName);
            }
        }
    }

    private static ValueTask<RemoteResource> ThrowingFetcher(string url, string? baseUrl, CancellationToken cancellationToken)
    {
        _ = baseUrl;
        _ = cancellationToken;
        throw new CborLdProcessingException(
            "loading remote context failed",
            string.Create(CultureInfo.InvariantCulture,
                $"Remote context '{url}' encountered but no fetcher delegate was supplied to the encoder/decoder."));
    }

    private static ValueTask<IReadOnlyDictionary<string, object?>> ThrowingParser(RemoteResource resource, CancellationToken cancellationToken)
    {
        _ = resource;
        _ = cancellationToken;
        throw new CborLdProcessingException(
            "loading remote context failed",
            "Remote context parsing was attempted but no parser delegate was supplied to the encoder/decoder.");
    }
}
