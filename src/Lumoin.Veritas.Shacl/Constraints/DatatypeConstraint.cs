using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:datatype</c> — each value node must be a literal with datatype
/// IRI equal to <see cref="DatatypeId"/>, and its lexical form must be a
/// valid value of that datatype.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.3.2.</remarks>
/// <param name="DatatypeId">The datatype IRI identifier.</param>
public sealed record DatatypeConstraint(IriId DatatypeId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Datatype;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
