using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:uniqueLang</c> — when <c>true</c>, no two value nodes may share
/// the same language tag.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.2.6. Only applies to property shapes. Named
/// <c>UniqueLanguageConstraint</c> for consistency with
/// <see cref="LanguageInConstraint"/>.
/// </remarks>
/// <param name="UniqueLanguage">Whether unique-language enforcement is active.</param>
public sealed record UniqueLanguageConstraint(bool UniqueLanguage): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.UniqueLang;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
