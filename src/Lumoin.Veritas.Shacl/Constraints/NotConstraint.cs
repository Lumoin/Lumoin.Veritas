using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:not</c> — each value node must NOT conform to the shape
/// identified by <see cref="InnerShapeId"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.6.1. Evaluators resolve
/// <see cref="InnerShapeId"/> against the loader's shape registry at
/// validation time and check non-conformance.
/// </remarks>
/// <param name="InnerShapeId">The term id of the negated shape.</param>
public sealed record NotConstraint(TermId InnerShapeId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Not;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [InnerShapeId];
}
