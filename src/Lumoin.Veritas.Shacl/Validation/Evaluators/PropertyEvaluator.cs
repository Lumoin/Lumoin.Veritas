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
/// Per SHACL 1.2 Core §4.8.1: <c>sh:property</c> is a per-value-node
/// check — each value node is validated, as a focus node, against the
/// nested property shape. For a node shape the value node set is the
/// focus node itself, so this collapses to "validate the focus against
/// the property shape"; for a <em>property</em> shape the value node
/// set is the path-reached nodes, so the nested property shape runs
/// once per reached node (this is what makes nested
/// <c>sh:property</c> on a property shape recurse correctly).
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

        //Validate each value node, as a focus node, against the nested
        //property shape (SHACL §4.8.1). The nested shape walks its own
        //sh:path from each value node and computes its own value nodes;
        //its results are surfaced unchanged. On a node shape the value
        //node set is just the focus node, so this is one recursion at
        //the focus; on a property shape it recurses once per reached node.
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();
        foreach(TermId value in valueNodes)
        {
            builder.AddRange(await recurse(innerShape, value, cancellationToken).ConfigureAwait(false));
        }

        return builder.ToImmutable();
    }
}
