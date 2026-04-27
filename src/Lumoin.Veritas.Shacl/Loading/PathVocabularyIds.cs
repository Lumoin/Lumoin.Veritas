using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Pre-resolved identifiers for the SHACL path-operator predicates
/// (<see href="https://www.w3.org/TR/shacl12-core/#property-paths">SHACL
/// 1.2 Core §2.3.1</see>). Typed as <see cref="IriId"/> because every
/// operator predicate is an IRI.
/// </summary>
/// <remarks>
/// Resolved once at loader startup and passed to
/// <see cref="ShapePathParser.ParseAsync"/> for every property shape.
/// Sequence paths do not appear here — they are expressed as RDF lists
/// and handled via <see cref="RdfListIds"/>.
/// </remarks>
/// <param name="InversePath">The <c>sh:inversePath</c> predicate identifier.</param>
/// <param name="AlternativePath">The <c>sh:alternativePath</c> predicate identifier.</param>
/// <param name="ZeroOrMorePath">The <c>sh:zeroOrMorePath</c> predicate identifier.</param>
/// <param name="OneOrMorePath">The <c>sh:oneOrMorePath</c> predicate identifier.</param>
/// <param name="ZeroOrOnePath">The <c>sh:zeroOrOnePath</c> predicate identifier.</param>
internal readonly record struct PathVocabularyIds(
    IriId InversePath,
    IriId AlternativePath,
    IriId ZeroOrMorePath,
    IriId OneOrMorePath,
    IriId ZeroOrOnePath);
