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
/// Evaluator for <c>sh:UniqueMembersConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.12: when
/// <see cref="UniqueMembersConstraint.UniqueMembers"/> is <c>true</c>,
/// every value node that is a SHACL list must have no repeated
/// members. Value nodes that are not SHACL lists are out of scope of
/// this constraint, consistent with the other list-cardinality
/// evaluators.
/// </para>
/// <para>
/// <b>List detection and walking.</b> Delegated to
/// <see cref="RdfCollection.TryGetMembersAsync"/>, which encodes the
/// SHACL list-interpretation rule and returns <c>null</c> for non-list
/// values.
/// </para>
/// <para>
/// <b>Duplicate detection.</b> Members are hashed into a
/// <see cref="HashSet{T}"/> keyed on <see cref="TermId"/>; the first
/// time <see cref="HashSet{T}.Add"/> returns <c>false</c> for a value
/// is the report point. The list <c>[a, b, a, a]</c> produces
/// exactly one violation (for the first re-encounter of <c>a</c>),
/// not one per duplicate occurrence — emitting per-occurrence would
/// flood reports for lists with many repeats of the same member, and
/// the spec's example diagnostic ("Duplicate items in list" with one
/// <c>sh:value</c>) supports the per-distinct-duplicated-value
/// shape.
/// </para>
/// <para>
/// <b>Inactive constraint.</b> When
/// <see cref="UniqueMembersConstraint.UniqueMembers"/> is
/// <c>false</c>, no checks run and no results are produced.
/// </para>
/// <para>
/// <b>Result shape.</b> One per-duplicate-value violation with the
/// duplicated value as <see cref="ValidationResult.ValueNode"/>.
/// </para>
/// </remarks>
public static class UniqueMembersEvaluator
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

        UniqueMembersConstraint umc = (UniqueMembersConstraint)constraint;
        if(!umc.UniqueMembers)
        {
            //Constraint declared but inactive — nothing to do.
            return [];
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

            HashSet<TermId> seen = [];
            HashSet<TermId> reported = [];

            foreach(TermId member in members)
            {
                if(seen.Add(member))
                {
                    continue;
                }

                if(!reported.Add(member))
                {
                    //Member already reported as a duplicate within
                    //this list — do not emit a second result for it.
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
