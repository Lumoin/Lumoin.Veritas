using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for the SHACL property-path
/// construction predicates.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §2.3.1, complex property paths are encoded as
/// blank-node structures in the shape graph, with these predicates
/// joining each path operator to its inner path. The shape-graph path
/// parser matches these handles to dispatch into the corresponding
/// <c>PropertyPath</c> subtype from the <c>Lumoin.Veritas.Rdf</c>
/// project.
/// </remarks>
/// <param name="InversePath"><c>sh:inversePath</c></param>
/// <param name="AlternativePath"><c>sh:alternativePath</c></param>
/// <param name="ZeroOrMorePath"><c>sh:zeroOrMorePath</c></param>
/// <param name="OneOrMorePath"><c>sh:oneOrMorePath</c></param>
/// <param name="ZeroOrOnePath"><c>sh:zeroOrOnePath</c></param>
public readonly record struct ShaclPathIds(
    IriId InversePath,
    IriId AlternativePath,
    IriId ZeroOrMorePath,
    IriId OneOrMorePath,
    IriId ZeroOrOnePath)
{
    /// <summary>
    /// Interns every path-construction SHACL IRI into
    /// <paramref name="dictionary"/> and returns their narrowed
    /// <see cref="IriId"/> handles.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved path-construction IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclPathIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclPathIds(
            InversePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.InversePath)),
            AlternativePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.AlternativePath)),
            ZeroOrMorePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.ZeroOrMorePath)),
            OneOrMorePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.OneOrMorePath)),
            ZeroOrOnePath: dictionary.GetOrAdd(new NamedNode(ShaclPathVocabulary.ZeroOrOnePath)));
    }
}
