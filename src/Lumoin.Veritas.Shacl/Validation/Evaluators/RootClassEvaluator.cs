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
/// Evaluator for <c>sh:RootClassConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.3.4: <c>sh:rootClass</c> is semantically
/// narrower than <c>sh:class</c>. A value node conforms when:
/// </para>
/// <list type="number">
///   <item><description>
///   The value is a SHACL instance of
///   <see cref="RootClassConstraint.ClassId"/>, i.e. there exists a
///   direct type <c>t</c> such that <c>value rdf:type t</c> and
///   either <c>t == ClassId</c> or
///   <c>t rdfs:subClassOf+ ClassId</c>.
///   </description></item>
///   <item><description>
///   No <em>proper</em> SHACL superclass of
///   <see cref="RootClassConstraint.ClassId"/> is also a SHACL type
///   of the value. Equivalently: <c>ClassId</c> is the "root" of the
///   value's class hierarchy as viewed through the data graph.
///   </description></item>
/// </list>
/// <para>
/// Concretely, a value typed as <c>ex:Mammal</c> conforms to
/// <c>sh:rootClass ex:Mammal</c> only if no triple
/// <c>ex:Mammal rdfs:subClassOf ex:Animal</c> (or any other
/// <c>rdfs:subClassOf</c> chain above) is asserted in the data
/// graph — once a superclass exists, the constraint considers
/// <c>ex:Mammal</c> not to be a root for this value.
/// </para>
/// <para>
/// <b>Algorithm and caches.</b> Both the instance-of decision and
/// every <c>rdfs:subClassOf*</c> closure are funnelled through
/// <see cref="ClassHierarchyHelpers"/>, sharing
/// <see cref="ValidationContext.ClassMembershipCache"/> and
/// <see cref="ValidationContext.SubclassClosureCache"/> with
/// <see cref="ClassEvaluator"/>. The strict-superclass set of
/// <c>ClassId</c> is materialised once via
/// <see cref="ClassHierarchyHelpers.GetStrictSuperclassesAsync"/>.
/// For each value: the instance check is one cached decision; the
/// "no proper superclass applies" check walks the value's direct
/// <c>rdf:type</c> assertions and, for each, consults the cached
/// closure of that asserted type and tests
/// <see cref="HashSet{T}.Overlaps"/> against the precomputed root
/// closure.
/// </para>
/// <para>
/// <b>Result shape.</b> Per-value-node. Each non-conforming value
/// produces one result with the value as
/// <see cref="ValidationResult.ValueNode"/>. Both failure modes —
/// value is not a SHACL instance of <c>ClassId</c> at all, and a
/// proper superclass applies — produce the same kind of result,
/// distinguishable in the report only via the message slot.
/// </para>
/// </remarks>
public static class RootClassEvaluator
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

        RootClassConstraint rcc = (RootClassConstraint)constraint;
        RdfAdjacencyAdapter adapter = new(context.DataMatchOps.MatchTriples);

        //Strict superclasses of the declared root class. This is the
        //set whose intersection with the value's SHACL types must be
        //empty — any overlap means a class above the root applies.
        HashSet<TermId> superclassesOfRoot = await ClassHierarchyHelpers.GetStrictSuperclassesAsync(
            rcc.ClassId.Value, rcc.RdfsSubClassOfId, adapter,
            context.SubclassClosureCache, cancellationToken).ConfigureAwait(false);

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            bool conforms = await ValueConformsToRootClassAsync(
                value, rcc, superclassesOfRoot, adapter, context, cancellationToken).ConfigureAwait(false);

            if(conforms)
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

    //Returns true iff value satisfies both the instance-of and the
    //no-proper-superclass-applies conditions. The two checks are
    //independent: the first uses the cached SHACL-instance-of helper;
    //the second walks the value's direct rdf:type assertions and for
    //each tests cache-backed closure overlap with superclassesOfRoot.
    private static async ValueTask<bool> ValueConformsToRootClassAsync(
        TermId value,
        RootClassConstraint rcc,
        HashSet<TermId> superclassesOfRoot,
        RdfAdjacencyAdapter adapter,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        bool isInstance = await ClassHierarchyHelpers.IsInstanceOfAsync(
            value, rcc.ClassId, rcc.RdfTypeId, rcc.RdfsSubClassOfId, adapter,
            context.ClassMembershipCache, context.SubclassClosureCache,
            cancellationToken).ConfigureAwait(false);

        if(!isInstance)
        {
            return false;
        }

        //"No proper superclass of ClassId applies": for each direct
        //rdf:type of the value, check that neither the asserted type
        //itself nor any of its strict superclasses lies in
        //superclassesOfRoot.
        await foreach(TermId directType in adapter.ForwardAsync(
            value, rcc.RdfTypeId, cancellationToken).ConfigureAwait(false))
        {
            if(superclassesOfRoot.Contains(directType))
            {
                return false;
            }

            HashSet<TermId> directTypeSuperclasses = await ClassHierarchyHelpers.GetStrictSuperclassesAsync(
                directType, rcc.RdfsSubClassOfId, adapter,
                context.SubclassClosureCache, cancellationToken).ConfigureAwait(false);

            if(directTypeSuperclasses.Overlaps(superclassesOfRoot))
            {
                return false;
            }
        }

        return true;
    }
}
