using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:pattern</c> — each value node's string form must match the
/// regular expression <see cref="Pattern"/>, optionally with
/// <see cref="Flags"/> and <see cref="SingleLine"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.2.3. The regex dialect is the one defined by
/// XPath/XQuery 3.0 §7.6.1, which is a subset of Perl-compatible regex.
/// Flag characters are <c>s</c>, <c>m</c>, <c>i</c>, <c>x</c>, <c>q</c>.
/// </para>
/// <para>
/// <c>sh:singleLine</c> is an alias for the <c>s</c> flag; it can also be
/// expressed as a separate constraint — see <see cref="SingleLineConstraint"/>.
/// When both a flags string and an explicit <see cref="SingleLine"/> flag
/// are present, the loader folds the boolean into the flags during
/// construction by setting the <c>s</c> option on <see cref="Compiled"/>.
/// </para>
/// <para>
/// <b>Compiled regex is derived state.</b> <see cref="Compiled"/> is
/// produced from <see cref="Pattern"/>, <see cref="Flags"/>, and
/// <see cref="SingleLine"/> at construction time by the shape loader —
/// either via a user-supplied resolver (which may return a
/// <see cref="GeneratedRegexAttribute"/>-backed subclass for zero
/// startup cost) or by falling back to a <see cref="Regex"/> with
/// <see cref="RegexOptions.NonBacktracking"/> for ReDoS safety on
/// untrusted shape input. Because <see cref="Compiled"/> is fully
/// determined by the source strings, record equality and hashing
/// deliberately ignore it: two <see cref="PatternConstraint"/> values
/// with the same <see cref="Pattern"/>, <see cref="Flags"/>, and
/// <see cref="SingleLine"/> are considered equal regardless of which
/// resolver produced their compiled matchers.
/// </para>
/// </remarks>
/// <param name="Pattern">The regex pattern as a lexical string.</param>
/// <param name="Flags">Optional flag string; <c>null</c> when absent.</param>
/// <param name="SingleLine">
/// <c>true</c> when <c>sh:singleLine</c> is asserted either as a companion
/// parameter or via the <c>s</c> flag. Folded into <see cref="Compiled"/>
/// at construction; tracked as a separate field so round-tripping back to
/// RDF can distinguish a flag-string source from a boolean source.
/// </param>
/// <param name="Compiled">
/// The compiled matcher. Derived from <see cref="Pattern"/>,
/// <see cref="Flags"/>, and <see cref="SingleLine"/>; ignored by
/// equality and hashing.
/// </param>
public sealed record PatternConstraint(
    string Pattern,
    string? Flags,
    bool SingleLine,
    Regex Compiled): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Pattern;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];

    /// <summary>
    /// Structural equality by source strings and flags only. The
    /// <see cref="Compiled"/> matcher is derived state — two instances
    /// with identical sources are equal regardless of which resolver
    /// produced their matchers.
    /// </summary>
    public bool Equals(PatternConstraint? other)
    {
        if(ReferenceEquals(this, other))
        {
            return true;
        }

        if(other is null)
        {
            return false;
        }

        return string.Equals(Pattern, other.Pattern, StringComparison.Ordinal)
            && string.Equals(Flags, other.Flags, StringComparison.Ordinal)
            && SingleLine == other.SingleLine;
    }

    /// <summary>
    /// Hash derived from the source strings only; matches the fields
    /// compared by <see cref="Equals(PatternConstraint?)"/>.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(Pattern, Flags, SingleLine);
}
