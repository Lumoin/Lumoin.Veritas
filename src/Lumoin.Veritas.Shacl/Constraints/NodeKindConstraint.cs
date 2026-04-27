using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:nodeKind</c> — each value node must have the specified kind
/// (IRI, literal, blank node, or one of the unions).
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.3.3.</remarks>
/// <param name="Kind">The required node kind.</param>
public sealed record NodeKindConstraint(NodeKind Kind): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.NodeKind;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
