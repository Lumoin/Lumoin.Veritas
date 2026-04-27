using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:PropertyConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.9.2: the current focus node must conform to
/// the nested property shape. Unlike <see cref="NodeEvaluator"/>, this
/// is not a per-value-node check — it is a "this shape also carries
/// the nested property shape as a sub-condition" relation, evaluated
/// at the current focus.
/// </para>
/// <para>
/// <b>Result shape.</b> Inner results are <em>surfaced directly</em>.
/// The nested property shape's violations become the outer shape's
/// violations: this is the mechanism by which nested property shapes
/// contribute their own structured diagnostics (path, value node,
/// severity, source shape) to the overall report. The outer
/// <see cref="PropertyConstraint"/> itself emits no additional result.
/// </para>
/// </remarks>
public static class PropertyEvaluator
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
        _ = valueNodes;
        cancellationToken.ThrowIfCancellationRequested();

        PropertyConstraint propertyConstraint = (PropertyConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;
        if(recurse is null || !context.Shapes.TryGetShape(propertyConstraint.PropertyShapeId, out Shape? innerShape))
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

        //Evaluate the nested property shape at the current focus. The
        //nested shape has its own sh:path; its evaluator will compute
        //its own value nodes. We hand the inner results back unchanged.
        return await recurse(innerShape, focusNode, cancellationToken).ConfigureAwait(false);
    }
}
