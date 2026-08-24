using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:and</c> — each value node must conform to every shape in
/// <see cref="MemberShapeIds"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.6.2. The members are stored as term ids;
/// evaluators resolve each against the loader's shape registry at
/// validation time.
/// </remarks>
/// <param name="MemberShapeIds">The term ids of the conjoined shapes.</param>
public sealed record AndConstraint(ImmutableArray<TermId> MemberShapeIds): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.And;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => MemberShapeIds;
}
