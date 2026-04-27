using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:LanguageInConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.2.5: each value node must be a literal
/// whose language tag matches at least one of the BCP 47 ranges
/// listed in <see cref="LanguageInConstraint.LanguageTags"/>. Matching
/// follows RFC 4647 §3.3.1 "basic filtering": a tag matches a range
/// when the range is a case-insensitive prefix of the tag and the
/// next character in the tag (if any) is a hyphen.
/// </para>
/// <para>
/// <b>Examples.</b> Range <c>en</c> matches tags <c>en</c> and
/// <c>en-US</c> but not <c>eng</c> or <c>en-GB-x-foo</c>… well,
/// <c>en-GB-x-foo</c> does match because the prefix <c>en</c> is
/// followed by <c>-</c>. Range <c>en-US</c> matches <c>en-US</c> and
/// <c>en-US-x-twain</c> but not <c>en</c>.
/// </para>
/// <para>
/// Wildcard range <c>*</c> matches every non-empty language tag —
/// equivalent to "the value has any language tag at all".
/// </para>
/// <para>
/// Non-literal value nodes and literals without a language tag fail
/// the constraint. Violations are per-value-node.
/// </para>
/// </remarks>
public static class LanguageInEvaluator
{
    /// <summary>
    /// The evaluator function. Matches the
    /// <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    public static ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        LanguageInConstraint languageIn = (LanguageInConstraint)constraint;
        ImmutableArray<Utf8String> ranges = languageIn.LanguageTags;

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            bool conforms = term is Literal literal
                && literal.Language is { } tag
                && AnyRangeMatches(tag.ToString(), ranges);
            if(conforms)
            {
                continue;
            }

            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ValueNode = value,
                ResultPath = path,
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = shape.Messages,
            });
        }

        return ValueTask.FromResult(builder.ToImmutable());
    }

    //Returns true iff tag matches any of the supplied ranges under
    //RFC 4647 basic filtering. The wildcard range "*" matches every
    //non-empty tag.
    private static bool AnyRangeMatches(string tag, ImmutableArray<Utf8String> ranges)
    {
        if(string.IsNullOrEmpty(tag))
        {
            return false;
        }

        foreach(Utf8String range in ranges)
        {
            string rangeText = range.ToString();
            if(BasicFilteringMatch(tag, rangeText))
            {
                return true;
            }
        }

        return false;
    }

    //RFC 4647 §3.3.1 basic filtering. The range matches the tag when:
    //- Range is "*" (wildcard) and the tag is non-empty, OR
    //- Range is a case-insensitive prefix of the tag, AND
    //- Either the range and tag are exactly equal in length OR
    //  the character in the tag immediately after the prefix is '-'.
    private static bool BasicFilteringMatch(string tag, string range)
    {
        if(range.Length == 1 && range[0] == '*')
        {
            return true;
        }

        if(range.Length > tag.Length)
        {
            return false;
        }

        if(!tag.AsSpan(0, range.Length).Equals(range.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        //Prefix matches case-insensitively. Now verify the boundary:
        //either equal length (exact match) or the next character in the
        //tag is the BCP 47 subtag separator '-'.
        if(tag.Length == range.Length)
        {
            return true;
        }

        return tag[range.Length] == '-';
    }
}
