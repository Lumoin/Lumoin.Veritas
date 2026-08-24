using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:languageIn</c> — each value node must be a literal with a
/// language tag matching one of the tags in <see cref="LanguageTags"/>
/// (per BCP 47 basic filtering).
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.2.5.</remarks>
/// <param name="LanguageTags">The allowed language tag patterns (BCP 47).</param>
public sealed record LanguageInConstraint(ImmutableArray<Utf8String> LanguageTags): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.LanguageIn;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
