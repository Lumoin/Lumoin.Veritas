using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:NotConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.7.1: each value node must <em>not</em>
/// conform to the inner shape. For each value node the evaluator
/// runs the recursion delegate on the resolved inner shape; an empty
/// inner result means the inner conformed, which means the outer
/// <c>sh:not</c> <em>fails</em> for this value.
/// </para>
/// <para>
/// <b>Result shape.</b> Inner results are not surfaced. One outer
/// violation per value that conforms to the inner shape.
/// </para>
/// </remarks>
public static class NotEvaluator
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

        NotConstraint notConstraint = (NotConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null || !context.Shapes.TryGetShape(notConstraint.InnerShapeId, out Shape? innerShape))
        {
            return [new ValidationResult
            {
                FocusNode = focusNode,
                ResultPath = path,
                Severity = Severity.Info,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = shape.Messages,
            }];
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            ImmutableArray<ValidationResult> innerResults =
                await recurse(innerShape, value, cancellationToken).ConfigureAwait(false);

            //Inner empty results → inner conformed → outer sh:not fails.
            if(!innerResults.IsEmpty)
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
