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
/// Evaluator for <c>sh:ClosedConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.10.1: when <c>sh:closed</c> is <c>true</c>,
/// the focus node must not have outgoing triples whose predicates lie
/// outside the set of predicates explicitly named by the shape's own
/// property shapes (when those use a simple predicate path) plus the
/// <c>sh:ignoredProperties</c> list.
/// </para>
/// <para>
/// <b>Allowed-predicate set.</b> Built from three sources:
/// </para>
/// <list type="number">
///   <item><description>Each <see cref="PropertyConstraint"/> on the current shape — its referenced property shape is resolved via <see cref="ValidationContext.Shapes"/>; if that property shape's path is a <see cref="PredicatePath"/>, its predicate IRI joins the allowed set.</description></item>
///   <item><description>Property shapes with a non-predicate path (sequence, alternative, inverse, etc.) do not contribute predicates to the allowed set per spec.</description></item>
///   <item><description>The <see cref="ClosedConstraint.IgnoredPredicateIds"/> list — explicitly allowed predicates regardless of shape coverage.</description></item>
/// </list>
/// <para>
/// <b>Result shape.</b> Per-violation. One result per outgoing triple
/// whose predicate is not in the allowed set, with the triple's
/// object as <see cref="ValidationResult.ValueNode"/>. The
/// <see cref="ValidationResult.ResultPath"/> is set to a
/// <see cref="PredicatePath"/> for the offending predicate, allowing
/// downstream tools to locate the violating triple precisely.
/// </para>
/// <para>
/// <b>When sh:closed is false.</b> The constraint is declared but
/// inactive; the evaluator emits no results without performing any
/// graph traversal.
/// </para>
/// </remarks>
public static class ClosedEvaluator
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

        ClosedConstraint closed = (ClosedConstraint)constraint;
        if(!closed.Closed)
        {
            return [];
        }

        HashSet<long> allowedEncoded = BuildAllowedSet(shape, closed, context);

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        await foreach(EncodedTriple triple in context.DataMatchOps.MatchTriples(
            focusNode, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            if(allowedEncoded.Contains(triple.Predicate.Encoded))
            {
                continue;
            }

            //Surface the offending triple's predicate as the result
            //path so downstream tooling can pinpoint the violation.
            IriId predicate = new(new TermId(triple.Predicate.Encoded));
            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ValueNode = triple.Object,
                ResultPath = new PredicatePath(predicate),
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = shape.Messages,
            });
        }

        return builder.ToImmutable();
    }

    //Builds the allowed-predicate set as encoded longs for fast
    //membership tests during the data-graph walk. Three sources
    //contribute: simple-predicate paths from each referenced property
    //shape, plus the explicit ignored-properties list. Property
    //shapes with complex paths (sequence, alternative, inverse, etc.)
    //contribute nothing per SHACL §6.10.1.
    private static HashSet<long> BuildAllowedSet(
        Shape shape, ClosedConstraint closed, ValidationContext context)
    {
        HashSet<long> allowed = [];

        foreach(ConstraintComponent c in shape.Constraints)
        {
            if(c is not PropertyConstraint property)
            {
                continue;
            }

            if(!context.Shapes.TryGetShape(property.PropertyShapeId, out Shape? referenced))
            {
                //Loader-time integrity issue; treat as no contribution
                //rather than throwing. The unresolved shape will be
                //surfaced through other evaluators' channels.
                continue;
            }

            if(referenced is not PropertyShape propertyShape)
            {
                //sh:property pointing at a non-property-shape; loader
                //should have classified it as PropertyShape, but be
                //defensive.
                continue;
            }

            if(propertyShape.Path is PredicatePath predicatePath)
            {
                allowed.Add(predicatePath.Predicate.Value.Encoded);
            }
        }

        foreach(IriId ignored in closed.IgnoredPredicateIds)
        {
            allowed.Add(ignored.Value.Encoded);
        }

        return allowed;
    }
}
