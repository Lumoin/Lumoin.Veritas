using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:XoneConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.7.4: each value node must conform to
/// <em>exactly one</em> member shape. For each value node the
/// evaluator counts conforming members; the outer result succeeds
/// iff the count is exactly 1.
/// </para>
/// <para>
/// <b>Short-circuit.</b> As soon as the conforming count reaches 2 —
/// already a failure — the remaining members are skipped. Zero
/// conformances is also a failure but can only be decided after all
/// members are tested.
/// </para>
/// <para>
/// <b>Unresolvable references.</b> If the recursion delegate is not
/// installed or any member shape id does not resolve through the
/// registry, the evaluator emits a single
/// <see cref="Severity.Info"/> result on the outer constraint and
/// returns. Skipping an unresolvable member would change the
/// effective conforming-count denominator and therefore the outcome,
/// so the constraint is reported as unevaluable rather than allowed
/// to produce a verdict whose meaning is uncertain.
/// </para>
/// <para>
/// <b>Result shape.</b> Inner results are not surfaced. One outer
/// violation per value node whose conforming-member count is not 1.
/// </para>
/// </remarks>
public static class XoneEvaluator
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

        XoneConstraint xoneConstraint = (XoneConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null)
        {
            return [UnableToEvaluate(shape, constraint, focusNode, path)];
        }

        Shape[] members = new Shape[xoneConstraint.MemberShapeIds.Length];
        for(int i = 0; i < members.Length; i++)
        {
            if(!context.Shapes.TryGetShape(xoneConstraint.MemberShapeIds[i], out Shape? member))
            {
                return [UnableToEvaluate(shape, constraint, focusNode, path)];
            }
            members[i] = member;
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            int conforming = 0;

            foreach(Shape member in members)
            {
                ImmutableArray<ValidationResult> innerResults =
                    await recurse(member, value, cancellationToken).ConfigureAwait(false);

                if(innerResults.IsEmpty)
                {
                    conforming++;
                    if(conforming > 1)
                    {
                        break;
                    }
                }
            }

            if(conforming == 1)
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

    private static ValidationResult UnableToEvaluate(
        Shape shape, ConstraintComponent constraint, TermId focusNode, PropertyPath? path)
        => new()
        {
            FocusNode = focusNode,
            ResultPath = path,
            Severity = Severity.Info,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = shape.Messages,
        };
}
