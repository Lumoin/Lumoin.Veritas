using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// The active Linked Data context for a point in document processing.
/// The structure is format-neutral and is shared by JSON-LD and CBOR-LD
/// (and any other Linked Data format that adopts JSON-LD's
/// active-context concept).
/// </summary>
/// <remarks>
/// <para>
/// An active context accumulates term definitions, vocabulary mapping,
/// base IRI, language mapping, and direction as it processes context
/// entries. Contexts are immutable: every operation that modifies context
/// state returns a new instance, leaving the original unchanged.
/// </para>
/// <para>
/// The term store is a persistent Hash Array Mapped Trie
/// (<see cref="ImmutableDictionary{TKey,TValue}"/>) so the
/// <c>With*</c> mutators share structure with their source: a single-term
/// addition costs O(log N) instead of copying the whole term map, and a
/// mutator that changes only a scalar field (e.g. <see cref="WithBaseIri"/>)
/// passes the term reference through unchanged. This makes deeply-nested
/// scoped-context stacks cheap to push and pop.
/// </para>
/// <para>
/// An inverted index keyed on namespace-IRI maintains the reverse mapping
/// from each prefix term's <see cref="TermDefinition.IriMapping"/>
/// back to the term name. Populated automatically on
/// <see cref="WithTerm"/> when a definition is registered with
/// <see cref="TermDefinition.Prefix"/> set to <c>true</c>;
/// consulted by compaction algorithms to convert absolute IRIs to their
/// compact <c>prefix:suffix</c> form. The index shares structure with its
/// source through the same persistent-map mechanism as the term store,
/// so push/pop of context frames remains O(1).
/// </para>
/// <para>
/// This corresponds to the "active context" concept defined in the
/// JSON-LD 1.1 Processing Algorithms specification.
/// </para>
/// </remarks>
public sealed class LinkedDataContext
{
    private readonly ImmutableDictionary<string, TermDefinition> terms;

    /// <summary>
    /// Inverted index keyed on namespace IRI. Each entry is the set of
    /// prefix-term names that have the keyed IRI as their
    /// <see cref="TermDefinition.IriMapping"/>. Sorted by
    /// <see cref="ShortestThenOrdinalComparer"/> so <see cref="ImmutableSortedSet{T}.Min"/>
    /// returns the JSON-LD-spec-correct best candidate for compaction
    /// (shortest term, then ordinal-least). Entries are removed when their
    /// set becomes empty so absence-of-key cleanly means "no prefix term
    /// exists for this IRI" without empty-set sentinels.
    /// </summary>
    private readonly ImmutableDictionary<string, ImmutableSortedSet<string>> prefixIndex;

    private LinkedDataContext(
        ImmutableDictionary<string, TermDefinition> terms,
        ImmutableDictionary<string, ImmutableSortedSet<string>> prefixIndex,
        string? baseIri,
        string? originalBaseUrl,
        string? vocabularyMapping,
        string? defaultLanguage,
        string? defaultBaseDirection,
        bool propagate,
        LinkedDataContext? previousContext)
    {
        this.terms = terms;
        this.prefixIndex = prefixIndex;
        BaseIri = baseIri;
        OriginalBaseUrl = originalBaseUrl;
        VocabularyMapping = vocabularyMapping;
        DefaultLanguage = defaultLanguage;
        DefaultBaseDirection = defaultBaseDirection;
        Propagate = propagate;
        PreviousContext = previousContext;
    }

    /// <summary>
    /// Gets an empty context with no mappings.
    /// </summary>
    public static LinkedDataContext Empty { get; } = new(
        terms: ImmutableDictionary<string, TermDefinition>.Empty,
        prefixIndex: ImmutableDictionary<string, ImmutableSortedSet<string>>.Empty,
        baseIri: null,
        originalBaseUrl: null,
        vocabularyMapping: null,
        defaultLanguage: null,
        defaultBaseDirection: null,
        propagate: true,
        previousContext: null);

    /// <summary>
    /// Gets the base IRI used for resolving relative IRIs in the document.
    /// </summary>
    public string? BaseIri { get; }

    /// <summary>
    /// Gets the original base URL from which this context was loaded.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "String URIs follow the JSON-LD specification convention throughout the processing algorithm.")]
    public string? OriginalBaseUrl { get; }

    /// <summary>
    /// Gets the vocabulary mapping, used to expand terms not found in the term definitions.
    /// </summary>
    public string? VocabularyMapping { get; }

    /// <summary>
    /// Gets the default language for plain string literals.
    /// </summary>
    public string? DefaultLanguage { get; }

    /// <summary>
    /// Gets the default base direction for string values.
    /// </summary>
    public string? DefaultBaseDirection { get; }

    /// <summary>
    /// Gets a value indicating whether this context propagates to child nodes.
    /// </summary>
    public bool Propagate { get; }

    /// <summary>
    /// Gets the context that was active before a non-propagating context was applied.
    /// Used to restore the context when leaving a node that had a non-propagating context.
    /// </summary>
    public LinkedDataContext? PreviousContext { get; }

    /// <summary>
    /// Gets all term names defined in this context.
    /// </summary>
    public IEnumerable<string> Terms => terms.Keys;

    /// <summary>
    /// Attempts to retrieve the term definition for the given term name.
    /// </summary>
    /// <param name="term">The compact term name.</param>
    /// <param name="definition">The resolved definition, if found.</param>
    /// <returns><c>true</c> if the term is defined; otherwise, <c>false</c>.</returns>
    public bool TryGetTerm(string term, out TermDefinition? definition)
    {
        return terms.TryGetValue(term, out definition);
    }

    /// <summary>
    /// Attempts to find a registered prefix term whose
    /// <see cref="TermDefinition.IriMapping"/> equals
    /// <paramref name="namespaceIri"/>. When multiple prefix terms share
    /// the same IRI mapping, the JSON-LD-spec-correct candidate is
    /// returned (shortest term first, then ordinal-least).
    /// </summary>
    /// <param name="namespaceIri">The namespace IRI to look up, typically a
    /// boundary-delimited prefix of an absolute IRI being compacted.</param>
    /// <param name="prefixTerm">The selected prefix term name, when found.</param>
    /// <returns><c>true</c> if a prefix term is registered for the IRI; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Designed for the JSON-LD compaction algorithm's boundary-hash walk:
    /// given an absolute IRI, split it at each viable namespace boundary
    /// (<c>/</c>, <c>#</c>, <c>:</c>), call this method with the prefix
    /// portion, and on the first hit emit <c>prefixTerm:suffix</c>. The
    /// query is O(1) amortised on the underlying hash trie.
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "String URIs follow the JSON-LD specification convention throughout the processing algorithm.")]
    public bool TryGetPrefixTerm(string namespaceIri, [NotNullWhen(true)] out string? prefixTerm)
    {
        if(prefixIndex.TryGetValue(namespaceIri, out ImmutableSortedSet<string>? candidates) && candidates.Count > 0)
        {
            prefixTerm = candidates.Min!;
            return true;
        }

        prefixTerm = null;
        return false;
    }

    /// <summary>
    /// Returns a new context with the given term definition added or replaced.
    /// </summary>
    /// <param name="term">The term name.</param>
    /// <param name="definition">The resolved term definition.</param>
    /// <returns>A new context containing the updated definition.</returns>
    public LinkedDataContext WithTerm(string term, TermDefinition definition)
    {
        ImmutableDictionary<string, ImmutableSortedSet<string>> updatedIndex = prefixIndex;

        //If the previous definition at this term was a prefix definition,
        //remove its inverted-index entry before adding the new one.
        if(terms.TryGetValue(term, out TermDefinition? existing)
            && existing is { Prefix: true, IriMapping: { } existingIri })
        {
            updatedIndex = RemoveFromPrefixIndex(updatedIndex, existingIri, term);
        }

        //Add the new definition's inverted-index entry when it is a prefix term.
        if(definition is { Prefix: true, IriMapping: { } newIri })
        {
            updatedIndex = AddToPrefixIndex(updatedIndex, newIri, term);
        }

        return new LinkedDataContext(terms.SetItem(term, definition), updatedIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with the given term removed.
    /// </summary>
    /// <param name="term">The term name to remove.</param>
    /// <returns>A new context without the term.</returns>
    public LinkedDataContext WithoutTerm(string term)
    {
        ImmutableDictionary<string, ImmutableSortedSet<string>> updatedIndex = prefixIndex;
        if(terms.TryGetValue(term, out TermDefinition? existing)
            && existing is { Prefix: true, IriMapping: { } existingIri })
        {
            updatedIndex = RemoveFromPrefixIndex(updatedIndex, existingIri, term);
        }

        return new LinkedDataContext(terms.Remove(term), updatedIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated base IRI.
    /// </summary>
    public LinkedDataContext WithBaseIri(string? baseIri)
    {
        return new LinkedDataContext(terms, prefixIndex,
            baseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated vocabulary mapping.
    /// </summary>
    public LinkedDataContext WithVocabularyMapping(string? vocab)
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, OriginalBaseUrl, vocab,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated default language.
    /// </summary>
    public LinkedDataContext WithDefaultLanguage(string? language)
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            language, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated default base direction.
    /// </summary>
    public LinkedDataContext WithDefaultBaseDirection(string? direction)
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, direction, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with the propagate flag set.
    /// </summary>
    public LinkedDataContext WithPropagate(bool propagate)
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with the previous context set.
    /// </summary>
    public LinkedDataContext WithPreviousContext(LinkedDataContext? previous)
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, previous);
    }

    /// <summary>
    /// Returns a new context initialised from this one but with the original base URL recorded.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "String URIs follow the JSON-LD specification convention throughout the processing algorithm.")]
    public LinkedDataContext WithOriginalBaseUrl(string? originalBaseUrl)
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, originalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new wrapper instance over the same persistent term store.
    /// </summary>
    /// <remarks>
    /// With the persistent-map term store, a clone shares structure with
    /// its source rather than copying; the returned instance differs in
    /// reference identity but is observationally equivalent to the source.
    /// </remarks>
    public LinkedDataContext Clone()
    {
        return new LinkedDataContext(terms, prefixIndex,
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Expands a compact IRI, keyword, or term against this active context.
    /// </summary>
    /// <param name="value">The value to expand.</param>
    /// <param name="vocab">
    /// When <c>true</c>, expansion uses <see cref="VocabularyMapping"/> for
    /// unknown terms.
    /// </param>
    /// <param name="documentRelative">
    /// When <c>true</c>, relative IRIs are resolved against
    /// <see cref="BaseIri"/>.
    /// </param>
    /// <returns>The expanded IRI or keyword, or <c>null</c> if the value cannot be expanded.</returns>
    public string? ExpandIri(string? value, bool vocab = false, bool documentRelative = false)
    {
        if(value is null || IriUtils.IsKeyword(value))
        {
            return value;
        }

        //If a keyword-like value appears that is not a known keyword, return null.
        if(IriUtils.IsKeywordLike(value))
        {
            return null;
        }

        //If the value is in the term definitions, resolve it. A term whose
        //mapping is itself a keyword (a keyword alias) resolves regardless
        //of vocab; the general IRI mapping resolves only in vocab-relative
        //expansion (per the IRI Expansion algorithm), so a term name is not
        //consulted when expanding an @id or other document-relative value.
        if(TryGetTerm(value, out TermDefinition? definition))
        {
            //A term defined with an explicit null IRI mapping shadows the
            //vocabulary and expands to nothing (W3C JSON-LD 1.1 §4.2.2).
            if(definition is { HasNullMapping: true })
            {
                return null;
            }

            if(definition?.IriMapping is { } iriMapping && IriUtils.IsKeyword(iriMapping))
            {
                return iriMapping;
            }

            if(vocab && definition?.IriMapping is not null)
            {
                //Only return the mapping directly if it is already an absolute IRI or a keyword.
                //If it is still a compact IRI (e.g. "foaf:name" stored before foaf was defined),
                //fall through to the compact IRI expansion below.
                if(IriUtils.IsAbsoluteIri(definition.IriMapping) || IriUtils.IsKeyword(definition.IriMapping))
                {
                    return definition.IriMapping;
                }
            }
        }

        //Handle compact IRIs of the form prefix:suffix.
        int colonIndex = value.IndexOf(':', System.StringComparison.Ordinal);
        if(colonIndex > 0)
        {
            string prefix = value[..colonIndex];
            string suffix = value[(colonIndex + 1)..];

            //_: is the blank node prefix — do not expand.
            if(prefix == "_")
            {
                return value;
            }

            //A double-slash indicates an absolute IRI.
            if(suffix.StartsWith("//", System.StringComparison.Ordinal))
            {
                return value;
            }

            //A term is usable as a compact-IRI prefix only when its prefix
            //flag is set (W3C JSON-LD 1.1 §4.2.2 IRI Expansion step 6). A
            //defined-but-non-prefix term (e.g. "@prefix": false) leaves the
            //value to be treated as a plain absolute IRI below.
            if(TryGetTerm(prefix, out TermDefinition? prefixDefinition)
                && prefixDefinition is { Prefix: true, IriMapping: { } prefixIri }
                && !IriUtils.IsKeyword(prefixIri))
            {
                return prefixIri + suffix;
            }

            //If the prefix is not a mapped term, check if the whole value is an absolute IRI.
            if(IriUtils.IsAbsoluteIri(value))
            {
                return value;
            }
        }

        //Vocabulary-relative expansion.
        if(vocab && VocabularyMapping is not null)
        {
            return VocabularyMapping + value;
        }

        //Document-relative expansion.
        if(documentRelative && BaseIri is not null)
        {
            return IriUtils.ResolveIri(BaseIri, value);
        }

        return value;
    }

    private static ImmutableDictionary<string, ImmutableSortedSet<string>> AddToPrefixIndex(
        ImmutableDictionary<string, ImmutableSortedSet<string>> index, string iri, string term)
    {
        ImmutableSortedSet<string> existing = index.TryGetValue(iri, out ImmutableSortedSet<string>? set)
            ? set
            : ImmutableSortedSet.Create<string>(ShortestThenOrdinalComparer.Instance);
        return index.SetItem(iri, existing.Add(term));
    }

    private static ImmutableDictionary<string, ImmutableSortedSet<string>> RemoveFromPrefixIndex(
        ImmutableDictionary<string, ImmutableSortedSet<string>> index, string iri, string term)
    {
        if(!index.TryGetValue(iri, out ImmutableSortedSet<string>? existing))
        {
            return index;
        }

        ImmutableSortedSet<string> updated = existing.Remove(term);
        return updated.IsEmpty ? index.Remove(iri) : index.SetItem(iri, updated);
    }

    /// <summary>
    /// Total order over term names used to rank prefix-term candidates: a
    /// shorter term wins; ties break by ordinal comparison. Matches the
    /// JSON-LD 1.1 compaction algorithm's preferred-term selection rule.
    /// </summary>
    private sealed class ShortestThenOrdinalComparer: IComparer<string>
    {
        public static ShortestThenOrdinalComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if(x is null)
            {
                return y is null ? 0 : -1;
            }

            if(y is null)
            {
                return 1;
            }

            int lengthCompare = x.Length.CompareTo(y.Length);
            return lengthCompare != 0 ? lengthCompare : string.CompareOrdinal(x, y);
        }
    }
}
