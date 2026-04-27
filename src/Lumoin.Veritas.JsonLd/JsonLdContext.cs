using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// The active JSON-LD context for a point in document processing.
/// </summary>
/// <remarks>
/// <para>
/// An active context accumulates term definitions, vocabulary mapping, base IRI,
/// language mapping, and direction as it processes <c>@context</c> entries.
/// Contexts are immutable: every operation that modifies context state returns
/// a new instance, leaving the original unchanged.
/// </para>
/// <para>
/// This corresponds to the "active context" concept defined in the
/// JSON-LD 1.1 Processing Algorithms specification.
/// </para>
/// </remarks>
public sealed class JsonLdContext
{
    private readonly Dictionary<string, TermDefinition> terms;

    private JsonLdContext(
        Dictionary<string, TermDefinition> terms,
        string? baseIri,
        string? originalBaseUrl,
        string? vocabularyMapping,
        string? defaultLanguage,
        string? defaultBaseDirection,
        bool propagate,
        JsonLdContext? previousContext)
    {
        this.terms = terms;
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
    public static JsonLdContext Empty { get; } = new(
        terms: [],
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
    public JsonLdContext? PreviousContext { get; }

    /// <summary>
    /// Gets all term names defined in this context.
    /// </summary>
    public IReadOnlyCollection<string> Terms => terms.Keys;

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
    /// Returns a new context with the given term definition added or replaced.
    /// </summary>
    /// <param name="term">The term name.</param>
    /// <param name="definition">The resolved term definition.</param>
    /// <returns>A new context containing the updated definition.</returns>
    public JsonLdContext WithTerm(string term, TermDefinition definition)
    {
        Dictionary<string, TermDefinition> updated = new(terms) { [term] = definition };
        return new JsonLdContext(updated, BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with the given term removed.
    /// </summary>
    /// <param name="term">The term name to remove.</param>
    /// <returns>A new context without the term.</returns>
    public JsonLdContext WithoutTerm(string term)
    {
        Dictionary<string, TermDefinition> updated = new(terms);
        updated.Remove(term);
        return new JsonLdContext(updated, BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated base IRI.
    /// </summary>
    public JsonLdContext WithBaseIri(string? baseIri)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            baseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated vocabulary mapping.
    /// </summary>
    public JsonLdContext WithVocabularyMapping(string? vocab)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, OriginalBaseUrl, vocab,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated default language.
    /// </summary>
    public JsonLdContext WithDefaultLanguage(string? language)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            language, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with an updated default base direction.
    /// </summary>
    public JsonLdContext WithDefaultBaseDirection(string? direction)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, direction, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with the propagate flag set.
    /// </summary>
    public JsonLdContext WithPropagate(bool propagate)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a new context with the previous context set.
    /// </summary>
    public JsonLdContext WithPreviousContext(JsonLdContext? previous)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, previous);
    }

    /// <summary>
    /// Returns a new context initialised from this one but with the original base URL recorded.
    /// </summary>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "String URIs follow the JSON-LD specification convention throughout the processing algorithm.")]
    public JsonLdContext WithOriginalBaseUrl(string? originalBaseUrl)
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, originalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }

    /// <summary>
    /// Returns a shallow copy of this context.
    /// </summary>
    internal JsonLdContext Clone()
    {
        return new JsonLdContext(new Dictionary<string, TermDefinition>(terms),
            BaseIri, OriginalBaseUrl, VocabularyMapping,
            DefaultLanguage, DefaultBaseDirection, Propagate, PreviousContext);
    }
}