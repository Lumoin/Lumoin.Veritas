using System.Collections.Generic;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// The resolved definition of a single term within an active Linked Data
/// context. Maps a compact term to its expanded IRI and controls how
/// values at that term are interpreted: their datatype, language,
/// container type, and so on.
/// </summary>
/// <remarks>
/// <para>
/// Term definitions are produced by the format-specific context-processing
/// algorithm (currently the JSON-LD <c>ContextProcessor</c>) and consumed
/// by the matching expansion and compaction algorithms. The shape of the
/// definition is identical for JSON-LD and CBOR-LD, which is why the type
/// lives in the format-neutral LinkedData project.
/// </para>
/// <para>
/// <see cref="LocalContext"/> carries the embedded context value associated
/// with this term for type-scoped or property-scoped context processing.
/// Typed as <see cref="ILinkedDataDocumentNode"/> so the slot is
/// format-neutral; the format-specific node type (e.g. JSON-LD's
/// <c>JsonLdNode</c>) implements that interface and is cast back at the
/// format boundary.
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
    /// Gets the document-tree node carrying the embedded context associated
    /// with this term for type-scoped or property-scoped context processing.
    /// </summary>
    /// <remarks>
    /// Stored as a detached node whose lifetime is independent of any
    /// document the term definition was extracted from. Format adapters
    /// produce the node via their parser's clone operation at the time the
    /// term definition is created. The format-specific node type (e.g.
    /// <c>JsonLdNode</c>) implements <see cref="ILinkedDataDocumentNode"/>
    /// and is cast back at the format boundary.
    /// </remarks>
    public ILinkedDataDocumentNode? LocalContext { get; init; }

    /// <summary>
    /// Gets the base IRI associated with this term definition, if set.
    /// </summary>
    public string? BaseIri { get; init; }
}
