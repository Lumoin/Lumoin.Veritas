using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:class</c> — each value node must be a SHACL instance of
/// <see cref="ClassId"/>, i.e. it has a type that is
/// <see cref="ClassId"/> or a transitive <c>rdfs:subClassOf</c> descendant.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.3.1.</remarks>
/// <param name="ClassId">The class IRI identifier.</param>
/// <param name="RdfTypeId">The <c>rdf:type</c> predicate identifier.</param>
/// <param name="RdfsSubClassOfId">The <c>rdfs:subClassOf</c> predicate identifier.</param>
public sealed record ClassConstraint(
    IriId ClassId,
    IriId RdfTypeId,
    IriId RdfsSubClassOfId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Class;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
