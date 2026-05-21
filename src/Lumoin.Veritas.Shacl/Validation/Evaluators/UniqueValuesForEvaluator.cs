using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:UniqueValuesForConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.10.2: each value node must not also appear
/// as a value of any <em>other</em> focus node at any of the
/// listed predicates. This expresses key-like uniqueness: "this
/// person's email must be globally unique."
/// </para>
/// <para>
/// <b>Algorithm.</b> Per value <c>v</c> of the current focus, per
/// predicate <c>p</c> in
/// <see cref="UniqueValuesForConstraint.PredicateIds"/>: query the
/// data graph for triples <c>(?s, p, v)</c> via
/// <see cref="RdfAdjacencyAdapter.BackwardAsync"/>; if any returned
/// subject differs from the current focus node, the value collides
/// with another focus and a violation is emitted. The current focus
/// itself owning the value at any predicate is allowed — only
/// <em>other</em> focuses count as collisions per the spec.
/// </para>
/// <para>
/// <b>No cross-focus accumulator.</b> The data graph already encodes
/// the global view of every focus's values. Querying the graph
/// backward per value avoids the need for a per-run accumulator on
/// <see cref="ValidationContext"/>: each focus's evaluation reads
/// the same authoritative graph, and order of focus evaluation does
/// not affect results.
/// </para>
/// <para>
/// <b>Result shape.</b> Per-value-node. A value that collides at one
/// or more predicates produces a single result with the value as
/// <see cref="ValidationResult.ValueNode"/>; the specific colliding
/// predicate or other-focus identity is not surfaced in the result
/// (callers can re-query the data graph if they need it).
/// </para>
/// <para>
/// <b>Empty parameters.</b> When
/// <see cref="UniqueValuesForConstraint.PredicateIds"/> is empty
/// the constraint is vacuously satisfied — no predicates to check
/// against — and no results are produced.
/// </para>
/// </remarks>
public static class UniqueValuesForEvaluator
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

        UniqueValuesForConstraint uvf = (UniqueValuesForConstraint)constraint;
        if(uvf.PredicateIds.IsEmpty || valueNodes.IsEmpty)
        {
            return [];
        }

        RdfAdjacencyAdapter adapter = new(context.DataMatchOps.MatchTriples);
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            bool collides = await ValueCollidesWithAnotherFocusAsync(
                value, focusNode, uvf.PredicateIds, adapter, cancellationToken).ConfigureAwait(false);

            if(!collides)
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

    //Walks each predicate backward from value and returns true on
    //the first subject that is not the current focus. Short-circuits
    //on the first collision; downstream predicates are not queried
    //once a violation is established.
    private static async Task<bool> ValueCollidesWithAnotherFocusAsync(
        TermId value,
        TermId focusNode,
        ImmutableArray<IriId> predicateIds,
        RdfAdjacencyAdapter adapter,
        CancellationToken cancellationToken)
    {
        foreach(IriId predicate in predicateIds)
        {
            await foreach(TermId subject in adapter.BackwardAsync(
                value, predicate, cancellationToken).ConfigureAwait(false))
            {
                if(!subject.Equals(focusNode))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
