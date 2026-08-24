using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:QualifiedMaxCountConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.7.5. Sibling of
/// <see cref="QualifiedMinCountEvaluator"/>; see that evaluator for
/// the result-shape and unevaluable conventions.
/// </para>
/// </remarks>
public static class QualifiedMaxCountEvaluator
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

        QualifiedMaxCountConstraint qmc = (QualifiedMaxCountConstraint)constraint;

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

        if(count.ConformingCount <= qmc.MaxCount)
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
