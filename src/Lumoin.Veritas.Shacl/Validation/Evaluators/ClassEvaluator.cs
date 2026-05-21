using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:ClassConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.3.1: each value node must be a SHACL instance
/// of the declared class — that is, the value must have an
/// <c>rdf:type</c> assertion to either the class itself or to a class
/// that transitively reaches the target via <c>rdfs:subClassOf</c>.
/// </para>
/// <para>
/// <b>Algorithm.</b> Funnelled through
/// <see cref="ClassHierarchyHelpers.IsInstanceOfAsync"/>. That helper
/// walks <c>rdf:type</c> assertions and tests each against the
/// target, falling back to the cached
/// <c>rdfs:subClassOf*</c> closure of each asserted type. The
/// traversal primitive dedupes via an internal visited set, so
/// cycles in the class lattice cannot cause non-termination.
/// </para>
/// <para>
/// <b>Caches.</b> Two per-run caches participate. The
/// <c>(value, targetClass)</c> membership decision is stored in
/// <see cref="ValidationContext.ClassMembershipCache"/>; the
/// <c>rdfs:subClassOf*</c> closure of each asserted type is stored
/// in <see cref="ValidationContext.SubclassClosureCache"/>. Repeated
/// queries for the same value or for values that share an asserted
/// type resolve from memory.
/// </para>
/// <para>
/// <b>Not OWL.</b> Membership here is "explicit <c>rdf:type</c>
/// assertion plus <c>rdfs:subClassOf</c> closure" — RDFS-style. OWL
/// class-expression semantics are out of scope; those need a proper
/// reasoner pass upstream of validation.
/// </para>
/// <para>
/// Violations are per-value-node.
/// </para>
/// </remarks>
public static class ClassEvaluator
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

        ClassConstraint classConstraint = (ClassConstraint)constraint;
        RdfAdjacencyAdapter adapter = new(context.DataMatchOps.MatchTriples);
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            bool isMember = await ClassHierarchyHelpers.IsInstanceOfAsync(
                value,
                classConstraint.ClassId,
                classConstraint.RdfTypeId,
                classConstraint.RdfsSubClassOfId,
                adapter,
                context.ClassMembershipCache,
                context.SubclassClosureCache,
                cancellationToken).ConfigureAwait(false);

            if(isMember)
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
