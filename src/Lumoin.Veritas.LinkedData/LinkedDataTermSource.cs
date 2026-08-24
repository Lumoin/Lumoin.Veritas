using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// A format-neutral description of a term as it appears in an inline
/// context. Carries the full field set required by W3C JSON-LD 1.1
/// §4.1.2 Create Term Definition without referring to JSON or CBOR
/// node types. The format-specific shell extracts these from its
/// document tree before invoking the active-context processing core.
/// </summary>
/// <seealso href="https://www.w3.org/TR/json-ld11-api/#create-term-definition"/>
public sealed class LinkedDataTermSource
{
    /// <summary>
    /// Initialises a new term source. The <paramref name="syntheticKey"/>
    /// must be unique within a single extraction run; depth-tagged keys
    /// keep scoped terms from colliding with same-named outer terms.
    /// </summary>
    /// <param name="syntheticKey">A stable deduplication key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="syntheticKey"/> is <see langword="null"/>.</exception>
    public LinkedDataTermSource(string syntheticKey)
    {
        ArgumentNullException.ThrowIfNull(syntheticKey);
        SyntheticKey = syntheticKey;
    }

    /// <summary>Gets the stable deduplication key for scoped-context walks.</summary>
    public string SyntheticKey { get; }

    /// <summary>Gets or initialises the IRI mapping for the term (the <c>@id</c> value), or <see langword="null"/> when the term has none.</summary>
    public string? Iri { get; init; }

    /// <summary>Gets or initialises the type mapping (an IRI or one of the type keywords <c>@id</c>, <c>@vocab</c>, <c>@json</c>, <c>@none</c>).</summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets or initialises the container mapping. JSON-LD 1.1 §4.1.2 permits
    /// a single keyword or an array of keywords; the list normalises both
    /// shapes. <see langword="null"/> when <c>@container</c> is absent.
    /// </summary>
    public IReadOnlyList<string>? Containers { get; init; }

    /// <summary>Gets or initialises the language mapping. <see langword="null"/> is meaningful only when <see cref="HasLanguageMapping"/> is <see langword="true"/>.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Indicates whether the term carries a <c>@language</c> mapping at all
    /// (distinguishing "<c>@language: null</c>" explicit-no-language from
    /// "no <c>@language</c> entry").
    /// </summary>
    public bool HasLanguageMapping { get; init; }

    /// <summary>Gets or initialises the direction (<c>"ltr"</c>, <c>"rtl"</c>, or <see langword="null"/>). <see langword="null"/> is meaningful only when <see cref="HasDirectionMapping"/> is <see langword="true"/>.</summary>
    public string? Direction { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Direction"/> was explicitly set (distinguishes an inherited default from an explicit null).</summary>
    public bool HasDirectionMapping { get; init; }

    /// <summary>Indicates whether the term is a reverse property mapping (an <c>@reverse</c> term).</summary>
    public bool Reverse { get; init; }

    /// <summary>Gets or initialises the reverse-property IRI when <see cref="Reverse"/> is <see langword="true"/>.</summary>
    public string? ReverseIri { get; init; }

    /// <summary>Indicates whether the term is marked <c>@protected</c>. Meaningful only when <see cref="HasProtected"/> is <see langword="true"/>.</summary>
    public bool Protected { get; init; }

    /// <summary>Gets a value indicating whether <c>@protected</c> was set on the term itself (overriding the context-level default).</summary>
    public bool HasProtected { get; init; }

    /// <summary>Indicates whether the term is marked <c>@prefix</c>.</summary>
    public bool Prefix { get; init; }

    /// <summary>Gets or initialises the <c>@nest</c> value (the nest property the term sits inside).</summary>
    public string? Nest { get; init; }

    /// <summary>Gets or initialises the <c>@index</c> value (the index property for indexed maps).</summary>
    public string? Index { get; init; }

    /// <summary>Gets or initialises the scoped context for this term, pre-extracted to format-neutral POCOs.</summary>
    public IReadOnlyList<LinkedDataContextEntry>? ScopedContext { get; init; }

    /// <summary>
    /// Indicates whether this term-source carries a removal marker — i.e.
    /// the source-document value was <c>null</c> or an object whose
    /// <c>@id</c> was explicitly <c>null</c>. Per W3C JSON-LD 1.1 §4.1.2
    /// such a source removes the term from the active context.
    /// </summary>
    public bool IsRemoval { get; init; }

    /// <summary>
    /// Indicates whether this term-source was extracted from the
    /// simple-string form (<c>"term": "iri"</c>). The compact-IRI-prefix
    /// recursion only applies in this form.
    /// </summary>
    public bool IsSimpleString { get; init; }
}
