using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:MinCountConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.5.1: a violation is produced when the number
/// of value nodes is less than <see cref="MinCountConstraint.MinCount"/>.
/// </para>
/// <para>
/// The violation is set-level — it pertains to the whole value-node
/// collection, not any specific member — so the emitted result leaves
/// <see cref="ValidationResult.ValueNode"/> unset. The severity is
/// inherited from the owning shape.
/// </para>
/// </remarks>
public static class MinCountEvaluator
{
    /// <summary>
    /// The evaluator function. Matches the
    /// <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    public static ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
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
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        MinCountConstraint minCount = (MinCountConstraint)constraint;

        if(valueNodes.Length >= minCount.MinCount)
        {
            return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        }

        ValidationResult result = new()
        {
            FocusNode = focusNode,
            ResultPath = path,
            Severity = shape.Severity,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = shape.Messages,
        };
        return ValueTask.FromResult(ImmutableArray.Create(result));
    }
}
