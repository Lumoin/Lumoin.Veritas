using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:maxCount</c> — the number of value nodes must be at most
/// <see cref="MaxCount"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.5.2.</remarks>
/// <param name="MaxCount">Inclusive upper bound on the count of value nodes.</param>
public sealed record MaxCountConstraint(int MaxCount): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MaxCount;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
