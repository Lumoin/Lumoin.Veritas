using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:maxListLength</c> (SHACL 1.2) — each value node that is a SHACL
/// list must have at most <see cref="MaxLength"/> members.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.12.</remarks>
/// <param name="MaxLength">Inclusive upper bound on list length.</param>
public sealed record MaxListLengthConstraint(int MaxLength): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MaxListLength;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
