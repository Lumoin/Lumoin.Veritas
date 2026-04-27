using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:AndConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.7.2: each value node must conform to every
/// member shape. For each value node the evaluator resolves each
/// member id through <see cref="ValidationContext.Shapes"/> and
/// recurses; if any member produces results the value is
/// non-conforming.
/// </para>
/// <para>
/// <b>Short-circuit.</b> Once any member has reported non-conformance
/// for a value node, the remaining members for that value are skipped.
/// </para>
/// <para>
/// <b>Unresolvable references.</b> If the recursion delegate is not
/// installed or any member shape id does not resolve through the
/// registry, the evaluator emits a single
/// <see cref="Severity.Info"/> result on the outer constraint and
/// returns. Inability to evaluate is signalled separately from
/// non-conformance — silently skipping an unresolvable member would
/// mask a loader bug and produce results whose meaning is uncertain.
/// </para>
/// <para>
/// <b>Result shape.</b> Inner results from member shapes are not
/// surfaced. One outer violation per non-conforming value node.
/// </para>
/// </remarks>
public static class AndEvaluator
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

        AndConstraint andConstraint = (AndConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null)
        {
            return [UnableToEvaluate(shape, constraint, focusNode, path)];
        }

        //Pre-resolve every member before any value-node iteration. A
        //single unresolvable id makes the whole constraint
        //unevaluable; failing fast here keeps the per-value loop body
        //free of resolution noise and ensures we do not partially
        //evaluate against a corrupt shape graph.
        Shape[] members = new Shape[andConstraint.MemberShapeIds.Length];
        for(int i = 0; i < members.Length; i++)
        {
            if(!context.Shapes.TryGetShape(andConstraint.MemberShapeIds[i], out Shape? member))
            {
                return [UnableToEvaluate(shape, constraint, focusNode, path)];
            }
            members[i] = member;
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            bool anyMemberFailed = false;

            foreach(Shape member in members)
            {
                ImmutableArray<ValidationResult> innerResults =
                    await recurse(member, value, cancellationToken).ConfigureAwait(false);

                if(!innerResults.IsEmpty)
                {
                    anyMemberFailed = true;
                    break;
                }
            }

            if(!anyMemberFailed)
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

    //Builds the canonical "this constraint could not be evaluated"
    //result. Severity is Info — never a violation, never silently
    //conforming. Centralised to keep the policy uniform across the
    //six recursion evaluators.
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
