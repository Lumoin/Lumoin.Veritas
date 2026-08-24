using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:OrConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.7.3: each value node must conform to at
/// least one member shape. For each value node the evaluator runs the
/// recursion delegate against each member; the first conforming
/// member short-circuits the value. If no member conforms, the value
/// fails.
/// </para>
/// <para>
/// <b>Unresolvable references.</b> If the recursion delegate is not
/// installed or any member shape id does not resolve through the
/// registry, the evaluator emits a single
/// <see cref="Severity.Info"/> result on the outer constraint and
/// returns. A missing member is not silently treated as a non-conforming
/// alternative — that would let a loader bug flip an <c>sh:or</c>
/// outcome from violation to pass. Inability to evaluate is signalled
/// distinctly from any conformance verdict.
/// </para>
/// <para>
/// <b>Result shape.</b> Inner results are not surfaced. One outer
/// violation per value node that fails every member.
/// </para>
/// </remarks>
public static class OrEvaluator
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

        OrConstraint orConstraint = (OrConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null)
        {
            return [UnableToEvaluate(shape, constraint, focusNode, path)];
        }

        Shape[] members = new Shape[orConstraint.MemberShapeIds.Length];
        for(int i = 0; i < members.Length; i++)
        {
            if(!context.Shapes.TryGetShape(orConstraint.MemberShapeIds[i], out Shape? member))
            {
                return [UnableToEvaluate(shape, constraint, focusNode, path)];
            }
            members[i] = member;
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            bool anyMemberConformed = false;

            foreach(Shape member in members)
            {
                ImmutableArray<ValidationResult> innerResults =
                    await recurse(member, value, cancellationToken).ConfigureAwait(false);

                if(innerResults.IsEmpty)
                {
                    anyMemberConformed = true;
                    break;
                }
            }

            if(anyMemberConformed)
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
