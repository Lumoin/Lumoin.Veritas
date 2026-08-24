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
/// shells (JSON-LD <c>ContextProcessor</c>, CBOR-LD
/// <c>CborLdActiveContextScope</c>) and consumed by the matching
/// expansion and compaction algorithms. The shape of the definition is
/// identical for JSON-LD and CBOR-LD, which is why the type lives in
/// the format-neutral LinkedData project.
/// </para>
/// <para>
/// Scoped contexts attached to a term ride on
/// <see cref="ScopedContextEntries"/> as a format-neutral POCO list,
/// pre-extracted at term-definition time. There is no format-specific
/// document-tree slot on this type.
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
    /// Gets a value indicating whether this term was explicitly defined with a
    /// null IRI mapping (<c>"term": null</c> or <c>{"@id": null}</c>).
    /// </summary>
    /// <remarks>
    /// Such a term is <em>defined</em> (so it may be <see cref="Protected"/> and
    /// participates in redefinition checks) yet expands to nothing — a key using
    /// it is dropped during expansion, and it shadows the active <c>@vocab</c>.
    /// Distinguishes a deliberate null mapping from a term simply absent.
    /// </remarks>
    public bool HasNullMapping { get; init; }

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
    /// Gets the base-direction mapping for string values at this term.
    /// </summary>
    /// <remarks>
    /// When set, plain string values at this term acquire this base direction
    /// (<c>"ltr"</c> or <c>"rtl"</c>). An explicit null clears any inherited
    /// default direction.
    /// </remarks>
    public string? DirectionMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="DirectionMapping"/> was explicitly set.
    /// </summary>
    /// <remarks>
    /// Distinguishes between a term with no direction mapping (which inherits
    /// the context default) and one explicitly set to null (which clears it).
    /// </remarks>
    public bool HasDirectionMapping { get; init; }

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
    /// Gets the base IRI associated with this term definition, if set.
    /// </summary>
    public string? BaseIri { get; init; }

    /// <summary>
    /// Gets the pre-extracted entries of the scoped context attached to
    /// this term, when present. <see langword="null"/> when the term has no
    /// scoped context.
    /// </summary>
    /// <remarks>
    /// Populated by <c>ContextProcessing</c> from
    /// <see cref="LinkedDataTermSource.ScopedContext"/> at term-definition
    /// time. Carrying the pre-extracted POCO entries here lets runtime
    /// consumers re-apply the scoped context against a different parent
    /// context without re-walking the format-specific document tree.
    /// </remarks>
    public IReadOnlyList<LinkedDataContextEntry>? ScopedContextEntries { get; init; }
}
