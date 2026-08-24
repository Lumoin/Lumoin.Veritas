using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:minListLength</c> (SHACL 1.2) — each value node that is a SHACL
/// list must have at least <see cref="MinLength"/> members.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.12.</remarks>
/// <param name="MinLength">Inclusive lower bound on list length.</param>
public sealed record MinListLengthConstraint(int MinLength): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MinListLength;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
