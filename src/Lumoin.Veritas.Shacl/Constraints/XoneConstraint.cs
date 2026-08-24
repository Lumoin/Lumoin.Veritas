using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:xone</c> — each value node must conform to exactly one shape in
/// <see cref="MemberShapeIds"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.6.4. The members are stored as term ids;
/// evaluators resolve each against the loader's shape registry at
/// validation time.
/// </remarks>
/// <param name="MemberShapeIds">The term ids of the alternative shapes.</param>
public sealed record XoneConstraint(ImmutableArray<TermId> MemberShapeIds): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Xone;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => MemberShapeIds;
}
