using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:NodeConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.9.1: each value node must conform to the
/// referenced node shape. The evaluator resolves
/// <see cref="NodeConstraint.NodeShapeId"/> through
/// <see cref="ValidationContext.Shapes"/> and delegates inner
/// validation to <see cref="ValidationContext.ShapeValidator"/> at
/// each value node as the focus; any result produced by the inner
/// validation indicates non-conformance.
/// </para>
/// <para>
/// <b>Result shape.</b> Inner results are <em>not</em> surfaced. The
/// evaluator emits a single outer result per non-conforming value
/// node (with that value as <see cref="ValidationResult.ValueNode"/>),
/// reporting "this value did not conform to the inner node shape".
/// </para>
/// <para>
/// Violations are per-value-node.
/// </para>
/// </remarks>
public static class NodeEvaluator
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

        NodeConstraint nodeConstraint = (NodeConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null || !context.Shapes.TryGetShape(nodeConstraint.NodeShapeId, out Shape? innerShape))
        {
            //Either recursion is not wired or the referenced shape is
            //missing from the registry. Emit one informational result
            //so the caller sees that sh:node was attempted but could
            //not run, rather than silently conforming.
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

            if(innerResults.IsEmpty)
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
