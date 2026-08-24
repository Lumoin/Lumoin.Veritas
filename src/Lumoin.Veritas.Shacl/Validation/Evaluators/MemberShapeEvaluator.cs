using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:MemberShapeConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.12: each member of every value node that is
/// a SHACL list must conform to the shape referenced by
/// <see cref="MemberShapeConstraint.MemberShapeId"/>. Value nodes
/// that are not SHACL lists are out of scope, consistent with the
/// other list-family evaluators.
/// </para>
/// <para>
/// <b>List detection and walking.</b> Delegated to
/// <see cref="RdfCollection.TryGetMembersAsync"/>.
/// </para>
/// <para>
/// <b>Recursion.</b> Inner-shape validation is delegated to
/// <see cref="ValidationContext.ShapeValidator"/>, which carries the
/// orchestrator's cycle guard. Each member is validated as a focus
/// node against the resolved inner shape.
/// </para>
/// <para>
/// <b>Result shape.</b> Mirrors <see cref="NodeEvaluator"/>: inner
/// results are <em>not</em> surfaced. For each list member that
/// fails inner validation, the evaluator emits a single outer result
/// with the member as <see cref="ValidationResult.ValueNode"/>,
/// attributing the violation to <c>sh:MemberShapeConstraintComponent</c>.
/// The outer evaluator owns the diagnostic; inner causes can be
/// recovered by re-running validation against the inner shape
/// directly when needed.
/// </para>
/// <para>
/// <b>Unwired or missing inner shape.</b> If
/// <see cref="ValidationContext.ShapeValidator"/> is <c>null</c> (test
/// isolation) or the referenced inner shape is absent from the
/// registry, the evaluator emits a single
/// <see cref="Severity.Info"/> result with no value node, signalling
/// "constraint was attempted but could not run". Same sentinel
/// convention as <see cref="NodeEvaluator"/> and
/// <see cref="PropertyEvaluator"/>.
/// </para>
/// </remarks>
public static class MemberShapeEvaluator
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

        MemberShapeConstraint msc = (MemberShapeConstraint)constraint;
        ShapeValidatorDelegate? recurse = context.ShapeValidator;

        if(recurse is null || !context.Shapes.TryGetShape(msc.MemberShapeId, out Shape? innerShape))
        {
            //Either recursion is not wired or the referenced shape is
            //missing from the registry. Emit one informational result
            //so the caller sees that sh:memberShape was attempted but
            //could not run, rather than silently conforming. Matches
            //the sentinel emitted by NodeEvaluator and PropertyEvaluator.
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
            RdfCollectionRead? read = await RdfCollection.TryReadAsync(
                value,
                context.RdfFirstId,
                context.RdfRestId,
                context.RdfNilId,
                context.DataMatchOps.MatchTriples,
                cancellationToken).ConfigureAwait(false);

            if(read is null)
            {
                //Not a SHACL list — out of scope per spec.
                continue;
            }

            IReadOnlyList<TermId> members = read.Value.Members;

            foreach(TermId member in members)
            {
                ImmutableArray<ValidationResult> innerResults =
                    await recurse(innerShape, member, cancellationToken).ConfigureAwait(false);

                if(innerResults.IsEmpty)
                {
                    continue;
                }

                builder.Add(new ValidationResult
                {
                    FocusNode = focusNode,
                    ValueNode = member,
                    ResultPath = path,
                    Severity = shape.Severity,
                    SourceShape = shape.Id,
                    SourceConstraintComponent = constraint.ConstraintComponentIri,
                    Messages = shape.Messages,
                });
            }
        }

        return builder.ToImmutable();
    }
}
