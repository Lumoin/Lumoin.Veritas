using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:uniqueMembers</c> (SHACL 1.2) — each value node that is a SHACL
/// list must have no repeated members.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.12.</remarks>
/// <param name="UniqueMembers">Whether the unique-members constraint is active.</param>
public sealed record UniqueMembersConstraint(bool UniqueMembers): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.UniqueMembers;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
