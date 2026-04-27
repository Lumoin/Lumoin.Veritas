using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:rootClass</c> (SHACL 1.2) — constrains the root class of a value
/// node. Semantically narrower than <c>sh:class</c>: the value node must
/// be a direct instance of <see cref="ClassId"/> or its descendant, and
/// no class above <see cref="ClassId"/> should apply.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.3.4.</remarks>
/// <param name="ClassId">The root-class IRI identifier.</param>
/// <param name="RdfTypeId">The <c>rdf:type</c> predicate identifier.</param>
/// <param name="RdfsSubClassOfId">The <c>rdfs:subClassOf</c> predicate identifier.</param>
public sealed record RootClassConstraint(
    IriId ClassId,
    IriId RdfTypeId,
    IriId RdfsSubClassOfId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.RootClass;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
