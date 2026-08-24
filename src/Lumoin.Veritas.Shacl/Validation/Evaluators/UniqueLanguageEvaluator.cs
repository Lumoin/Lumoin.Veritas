using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:UniqueLangConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.2.6: when
/// <see cref="UniqueLangConstraint.UniqueLang"/> is <c>true</c>, no
/// two value nodes that are literals with a language tag may share
/// the same language tag. When the flag is <c>false</c> the
/// constraint is trivially satisfied.
/// </para>
/// <para>
/// <b>Result shape.</b> Set-level: this constraint is about the value
/// set as a whole, not individual values. The evaluator emits one
/// outer result per language tag that occurs more than once. The
/// result has no <see cref="ValidationResult.ValueNode"/> set,
/// matching the convention used for other set-level constraints
/// (<c>sh:minCount</c>, <c>sh:maxCount</c>).
/// </para>
/// <para>
/// <b>Tag comparison.</b> Language tag equality is case-insensitive
/// per BCP 47. Two tags <c>en-US</c> and <c>EN-us</c> are the same
/// tag for purposes of this constraint.
/// </para>
/// <para>
/// <b>Tag-less literals.</b> Literals without a language tag, IRIs,
/// and blank nodes do not participate in the comparison — the
/// constraint only constrains tagged literals.
/// </para>
/// </remarks>
public static class UniqueLanguageEvaluator
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

        UniqueLanguageConstraint uniqueLang = (UniqueLanguageConstraint)constraint;
        if(!uniqueLang.UniqueLanguage)
        {
            return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        }

        //Two-pass over the value set: first pass tallies tag
        //occurrences case-insensitively; second pass emits one result
        //per tag that appears more than once. Single allocation for
        //the dictionary; all comparisons are ordinal-ignore-case so
        //no per-key culture work.
        Dictionary<string, int> tagCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            if(term is not Literal literal || literal.Language is not { } tag)
            {
                continue;
            }

            string tagText = tag.ToString();
            tagCounts[tagText] = tagCounts.TryGetValue(tagText, out int existing) ? existing + 1 : 1;
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();
        foreach(KeyValuePair<string, int> entry in tagCounts)
        {
            if(entry.Value < 2)
            {
                continue;
            }

            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ResultPath = path,
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = shape.Messages,
            });
        }

        return ValueTask.FromResult(builder.ToImmutable());
    }
}
