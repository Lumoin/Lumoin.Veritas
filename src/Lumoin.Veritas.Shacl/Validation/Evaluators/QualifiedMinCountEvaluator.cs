using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:QualifiedMinCountConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.7.4. The number of value nodes that conform
/// to the inner shape (and that, when
/// <see cref="QualifiedMinCountConstraint.Disjoint"/> is <c>true</c>,
/// do not also conform to any sibling qualified value shape) must be
/// at least <see cref="QualifiedMinCountConstraint.MinCount"/>.
/// </para>
/// <para>
/// <b>Result shape.</b> One outer violation when the count is below
/// the bound. The result targets the focus node (with no specific
/// value node) because the constraint is a count over the value-node
/// set, not a property of any individual value. Inner results from
/// the counting recursion are not surfaced.
/// </para>
/// <para>
/// <b>Unevaluable.</b> When the recursion delegate is missing or the
/// inner shape cannot be resolved, the evaluator emits one
/// <see cref="Severity.Info"/> result rather than silently passing or
/// failing — matching the convention used by
/// <see cref="NodeEvaluator"/> and <see cref="AndEvaluator"/>.
/// </para>
/// </remarks>
public static class QualifiedMinCountEvaluator
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

        QualifiedMinCountConstraint qmc = (QualifiedMinCountConstraint)constraint;

        QualifiedValueShapeCounting.CountResult count = await QualifiedValueShapeCounting.CountAsync(
            shape, qmc.ValueShapeId, qmc.Disjoint, valueNodes, context, cancellationToken).ConfigureAwait(false);

        if(!count.WasEvaluable)
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

        if(count.ConformingCount >= qmc.MinCount)
        {
            return [];
        }

        return [new ValidationResult
        {
            FocusNode = focusNode,
            ResultPath = path,
            Severity = shape.Severity,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = shape.Messages,
        }];
    }
}
