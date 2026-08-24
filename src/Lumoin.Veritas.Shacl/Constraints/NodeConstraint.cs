using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:node</c> — each value node must conform to the shape identified
/// by <see cref="NodeShapeId"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.9.1. The reference is stored as a term id;
/// evaluators resolve it against the loader's shape registry at
/// validation time.
/// </remarks>
/// <param name="NodeShapeId">
/// The term id of the referenced shape. The SHACL spec does not
/// formally constrain the referenced shape to be a
/// <see cref="NodeShape"/>, though the parameter name suggests that
/// intent; evaluators accept either kind.
/// </param>
public sealed record NodeConstraint(TermId NodeShapeId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Node;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [NodeShapeId];
}
