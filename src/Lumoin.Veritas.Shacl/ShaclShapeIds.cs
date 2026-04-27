using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for the SHACL shape classes
/// and the predicates that decorate shapes (<c>sh:path</c>,
/// <c>sh:severity</c>, <c>sh:message</c>, <c>sh:deactivated</c>).
/// </summary>
/// <remarks>
/// <para>
/// Shape loading needs to classify every triple in the shape graph by
/// its predicate. Doing a <see cref="TermDictionary.GetIdOrDefault(RdfTerm)"/>
/// lookup for each predicate on every incoming triple would be wasteful.
/// Instead, the loader resolves the full SHACL vocabulary once via
/// <see cref="ShaclVocabularyIds.Resolve(TermDictionary)"/> and holds
/// these <see cref="IriId"/> handles for the lifetime of the load.
/// </para>
/// <para>
/// The four non-validating properties (<c>sh:path</c>, <c>sh:severity</c>,
/// <c>sh:message</c>, <c>sh:deactivated</c>) are grouped here with the
/// shape classes because they describe the shape itself rather than any
/// specific constraint.
/// </para>
/// </remarks>
/// <param name="Shape"><c>sh:Shape</c> class IRI.</param>
/// <param name="NodeShape"><c>sh:NodeShape</c> class IRI.</param>
/// <param name="PropertyShape"><c>sh:PropertyShape</c> class IRI.</param>
/// <param name="ShapeClass"><c>sh:ShapeClass</c> class IRI (SHACL 1.2).</param>
/// <param name="Path"><c>sh:path</c> predicate IRI.</param>
/// <param name="Severity"><c>sh:severity</c> predicate IRI.</param>
/// <param name="Message"><c>sh:message</c> predicate IRI.</param>
/// <param name="Deactivated"><c>sh:deactivated</c> predicate IRI.</param>
public readonly record struct ShaclShapeIds(
    IriId Shape,
    IriId NodeShape,
    IriId PropertyShape,
    IriId ShapeClass,
    IriId Path,
    IriId Severity,
    IriId Message,
    IriId Deactivated)
{
    /// <summary>
    /// Interns every shape-related SHACL IRI into <paramref name="dictionary"/>
    /// and returns their narrowed <see cref="IriId"/> handles as a single
    /// record struct.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="TermDictionary.GetOrAdd(NamedNode)"/>, so all terms
    /// end up in the dictionary whether or not the source graph referenced
    /// them. This avoids "is this IRI present?" checks elsewhere at the
    /// cost of a handful of extra dictionary entries.
    /// </remarks>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved shape IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclShapeIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclShapeIds(
            Shape: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Shape)),
            NodeShape: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.NodeShape)),
            PropertyShape: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.PropertyShape)),
            ShapeClass: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.ShapeClass)),
            Path: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Path)),
            Severity: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Severity)),
            Message: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Message)),
            Deactivated: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.Deactivated)));
    }
}
