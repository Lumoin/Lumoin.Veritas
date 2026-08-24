using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for SHACL target predicates.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §5.1, a shape declares focus nodes via one of five
/// target predicates. The shape loader matches incoming triples against
/// these <see cref="IriId"/>s to dispatch into the correct <c>Target</c>
/// subtype.
/// </remarks>
/// <param name="TargetClass"><c>sh:targetClass</c> predicate IRI.</param>
/// <param name="TargetNode"><c>sh:targetNode</c> predicate IRI.</param>
/// <param name="TargetSubjectsOf"><c>sh:targetSubjectsOf</c> predicate IRI.</param>
/// <param name="TargetObjectsOf"><c>sh:targetObjectsOf</c> predicate IRI.</param>
/// <param name="TargetWhere"><c>sh:targetWhere</c> predicate IRI (SHACL 1.2).</param>
public readonly record struct ShaclTargetIds(
    IriId TargetClass,
    IriId TargetNode,
    IriId TargetSubjectsOf,
    IriId TargetObjectsOf,
    IriId TargetWhere)
{
    /// <summary>
    /// Interns every target-related SHACL IRI into <paramref name="dictionary"/>
    /// and returns their narrowed <see cref="IriId"/> handles.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved target IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclTargetIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclTargetIds(
            TargetClass: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetClass)),
            TargetNode: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetNode)),
            TargetSubjectsOf: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetSubjectsOf)),
            TargetObjectsOf: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetObjectsOf)),
            TargetWhere: dictionary.GetOrAdd(new NamedNode(ShaclCoreVocabulary.TargetWhere)));
    }
}
