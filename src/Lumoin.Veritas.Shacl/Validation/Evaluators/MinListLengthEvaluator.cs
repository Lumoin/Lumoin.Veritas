using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:MinListLengthConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.12: each value node that is a SHACL list
/// must have at least <see cref="MinListLengthConstraint.MinLength"/>
/// members. Value nodes that are not SHACL lists are out of scope of
/// this constraint.
/// </para>
/// <para>
/// <b>List detection and walking.</b> Both delegated to
/// <see cref="RdfCollection.TryGetMembersAsync"/>, which encodes the
/// SHACL list-interpretation rule (<c>rdf:nil</c> ⇒ empty list; has
/// <c>rdf:first</c> ⇒ walked list; otherwise not a SHACL list) and
/// returns <c>null</c> for non-list values. Walking is iterative
/// with a visited set guarding against malformed cyclic chains.
/// </para>
/// <para>
/// <b>Result shape.</b> One per-value-node violation when a list has
/// fewer members than the bound. The violating value node is
/// reported as <see cref="ValidationResult.ValueNode"/>.
/// </para>
/// </remarks>
public static class MinListLengthEvaluator
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

        MinListLengthConstraint mll = (MinListLengthConstraint)constraint;
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

            if(members.Count >= mll.MinLength)
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
