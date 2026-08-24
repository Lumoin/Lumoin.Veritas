using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:minInclusive</c> — every value node must be greater than or equal
/// to <see cref="Bound"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.1.3.</remarks>
/// <param name="Bound">
/// The inclusive lower bound, as a term identifier. Evaluators reject
/// non-literal bounds per §6.1.
/// </param>
public sealed record MinInclusiveConstraint(TermId Bound): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MinInclusive;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
