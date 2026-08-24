using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:MaxListLengthConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.12. Sibling of
/// <see cref="MinListLengthEvaluator"/>; see that evaluator for
/// list-detection and result-shape conventions. List detection and
/// walking go through
/// <see cref="RdfCollection.TryGetMembersAsync"/>.
/// </para>
/// </remarks>
public static class MaxListLengthEvaluator
{
    /// <summary>
    /// The evaluator function. Matches the
    /// <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    public static async ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
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

        MaxListLengthConstraint mll = (MaxListLengthConstraint)constraint;
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfCollectionRead? read = await RdfCollection.TryReadAsync(
                value,
                context.RdfFirstId,
                context.RdfRestId,
                context.RdfNilId,
                context.DataMatchOps.MatchTriples,
                cancellationToken).ConfigureAwait(false);

            if(read is null)
            {
                continue;
            }

            IReadOnlyList<TermId> members = read.Value.Members;

            if(members.Count <= mll.MaxLength)
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

        return builder.ToImmutable();
    }
}
