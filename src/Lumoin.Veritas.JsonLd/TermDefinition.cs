using System.Collections.Generic;

namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// The resolved definition of a single term within a JSON-LD context.
/// </summary>
/// <remarks>
/// <para>
/// A term definition maps a compact term to its expanded IRI and controls
/// how values at that term are interpreted: their datatype, language,
/// container type, and so on.
/// </para>
/// <para>
/// Term definitions are produced by the context processing algorithm and
/// consumed by the expansion and compaction algorithms.
/// </para>
/// </remarks>
public sealed class TermDefinition
{
    /// <summary>
    /// Gets the IRI or keyword that this term expands to.
    /// </summary>
    /// <remarks>
    /// May be <c>null</c> for a term that has been explicitly set to null
    /// (indicating that the term is not mapped in the active context).
    /// </remarks>
    public string? IriMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether this term may be used as a compact IRI prefix.
    /// </summary>
    /// <remarks>
    /// When <c>true</c>, the term may appear in the prefix position of a compact IRI
    /// of the form <c>prefix:suffix</c>.
    /// </remarks>
    public bool Prefix { get; init; }

    /// <summary>
    /// Gets the type coercion applied to values at this term.
    /// </summary>
    /// <remarks>
    /// When set to <c>@id</c>, string values are interpreted as IRIs.
    /// When set to <c>@vocab</c>, string values are expanded against the active vocabulary.
    /// When set to a datatype IRI, literal values are given that datatype.
    /// </remarks>
    public string? TypeMapping { get; init; }

    /// <summary>
    /// Gets the language mapping for string values at this term.
    /// </summary>
    /// <remarks>
    /// When set, plain string values at this term acquire this language tag.
    /// An explicit null clears any inherited language mapping.
    /// </remarks>
    public string? LanguageMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="LanguageMapping"/> was explicitly set.
    /// </summary>
    /// <remarks>
    /// Distinguishes between a term with no language mapping and one explicitly set to null.
    /// </remarks>
    public bool HasLanguageMapping { get; init; }

    /// <summary>
    /// Gets the container mapping for values at this term.
    /// </summary>
    /// <remarks>
    /// Controls how arrays and objects are interpreted:
    /// <c>@list</c>, <c>@set</c>, <c>@language</c>, <c>@index</c>,
    /// <c>@id</c>, <c>@graph</c>, or <c>@type</c>.
    /// </remarks>
    public IReadOnlyList<string> ContainerMapping { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this term definition is protected.
    /// </summary>
    /// <remarks>
    /// Protected term definitions cannot be overridden by subsequent contexts
    /// unless the overriding definition is identical.
    /// </remarks>
    public bool Protected { get; init; }

    /// <summary>
    /// Gets the reverse property IRI, present when this is a reverse property definition.
    /// </summary>
    public string? ReverseProperty { get; init; }

    /// <summary>
    /// Gets the nest term, when <c>@nest</c> is specified in the term definition.
    /// </summary>
    public string? NestValue { get; init; }

    /// <summary>
    /// Gets the index mapping term, when <c>@index</c> is specified.
    /// </summary>
    public string? IndexMapping { get; init; }

    /// <summary>
    /// Gets the context associated with this term for type-scoped context processing.
    /// </summary>
    /// <remarks>
    /// Stored as a detached <see cref="JsonLdNode"/> whose lifetime is independent
    /// of any document the term definition was extracted from. The node was produced
    /// via the navigator's clone delegate at the time the term definition was created.
    /// </remarks>
    public JsonLdNode? LocalContext { get; init; }

    /// <summary>
    /// Gets the base IRI associated with this term definition, if set.
    /// </summary>
    public string? BaseIri { get; init; }
}
