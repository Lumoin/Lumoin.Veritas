using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:minCount</c> — the number of value nodes must be at least
/// <see cref="MinCount"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.5.1.</remarks>
/// <param name="MinCount">Inclusive lower bound on the count of value nodes.</param>
public sealed record MinCountConstraint(int MinCount): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MinCount;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
