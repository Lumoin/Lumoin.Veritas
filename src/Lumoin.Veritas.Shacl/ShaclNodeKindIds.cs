using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Pre-resolved <see cref="IriId"/> handles for the six SHACL node-kind
/// IRIs that appear as objects of <c>sh:nodeKind</c>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.6.3, these IRIs are the only values accepted by
/// <c>sh:nodeKind</c>. The shape loader matches the incoming object IRI
/// against these handles to dispatch into the correct
/// <see cref="NodeKind"/> enum value.
/// </remarks>
/// <param name="BlankNode"><c>sh:BlankNode</c></param>
/// <param name="IRI"><c>sh:IRI</c></param>
/// <param name="Literal"><c>sh:Literal</c></param>
/// <param name="BlankNodeOrIRI"><c>sh:BlankNodeOrIRI</c></param>
/// <param name="BlankNodeOrLiteral"><c>sh:BlankNodeOrLiteral</c></param>
/// <param name="IRIOrLiteral"><c>sh:IRIOrLiteral</c></param>
public readonly record struct ShaclNodeKindIds(
    IriId BlankNode,
    IriId IRI,
    IriId Literal,
    IriId BlankNodeOrIRI,
    IriId BlankNodeOrLiteral,
    IriId IRIOrLiteral)
{
    /// <summary>
    /// Interns every node-kind SHACL IRI into <paramref name="dictionary"/>
    /// and returns their narrowed <see cref="IriId"/> handles.
    /// </summary>
    /// <param name="dictionary">The term dictionary to populate.</param>
    /// <returns>The resolved node-kind IRI handles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <c>null</c>.</exception>
    public static ShaclNodeKindIds Resolve(TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new ShaclNodeKindIds(
            BlankNode: dictionary.GetOrAdd(new NamedNode(ShaclNodeKindVocabulary.BlankNode)),
            IRI: dictionary.GetOrAdd(new NamedNode(ShaclNodeKindVocabulary.IRI)),
            Literal: dictionary.GetOrAdd(new NamedNode(ShaclNodeKindVocabulary.Literal)),
            BlankNodeOrIRI: dictionary.GetOrAdd(new NamedNode(ShaclNodeKindVocabulary.BlankNodeOrIRI)),
            BlankNodeOrLiteral: dictionary.GetOrAdd(new NamedNode(ShaclNodeKindVocabulary.BlankNodeOrLiteral)),
            IRIOrLiteral: dictionary.GetOrAdd(new NamedNode(ShaclNodeKindVocabulary.IRIOrLiteral)));
    }
}
