using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Shared evaluation body for the two pair-property ordering
/// constraints (<c>sh:lessThan</c>, <c>sh:lessThanOrEquals</c>).
/// </summary>
/// <remarks>
/// <para>
/// Both constraints have the form "every value node must be in
/// relation R to every comparison-set node", where R is strictly-less
/// for <c>sh:lessThan</c> and less-or-equal for
/// <c>sh:lessThanOrEquals</c>. The two share collection of the
/// comparison set and the cartesian product walk; they differ only in
/// which <see cref="ComparisonResult"/> values they accept as
/// conforming.
/// </para>
/// <para>
/// <b>Incomparable pairs always fail.</b> If any value/comparison
/// pair compares as <see cref="ComparisonResult.Incomparable"/>
/// (different value spaces, ill-formed lexical form, NaN, indeterminate
/// datetime), that pair counts as a violation. The constraint requires
/// a determinate "less than" relation; the absence of one is
/// non-conformance.
/// </para>
/// <para>
/// <b>Result shape.</b> Per-pair. A value node compared against N
/// comparison-set nodes can produce up to N violations, one per
/// non-conforming pair. The <see cref="ValidationResult.ValueNode"/>
/// is the value-node side of the pair (the side controlled by
/// <c>sh:path</c>); the comparison-set member is implicit and not
/// surfaced because the SHACL result vocabulary has no slot for it.
/// </para>
/// </remarks>
internal static class PairOrderingEvaluatorCore
{
    /// <summary>
    /// Predicate: given the comparison of a value node to a
    /// comparison-set member, returns <c>true</c> iff that pair
    /// conforms.
    /// </summary>
    public delegate bool ConformancePredicate(ComparisonResult comparisonResult);

    public static async ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        IriId otherPredicate,
        ConformancePredicate isConforming,
        CancellationToken cancellationToken)
    {
        ImmutableArray<TermId> comparisonSet = await PairPropertyComparisonSet.CollectAsync(
            focusNode, otherPredicate, context, cancellationToken).ConfigureAwait(false);

        if(comparisonSet.Length == 0)
        {
            //Empty comparison set: the universal quantification "every
            //value must be less than every comparison value" is
            //vacuously true. Conforms.
            return [];
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();
        foreach(TermId value in valueNodes)
        {
            RdfTerm valueTerm = context.Dictionary.Resolve(value);
            foreach(TermId comparison in comparisonSet)
            {
                RdfTerm comparisonTerm = context.Dictionary.Resolve(comparison);
                ComparisonResult result = RdfValueComparer.Compare(valueTerm, comparisonTerm);
                if(isConforming(result))
                {
                    continue;
                }

                //Per-pair result (§4.8.x): a value node compared against N
                //comparison-set members yields one result per non-conforming
                //pair, not one per value. The comparison-set member is not
                //surfaced (the result vocabulary has no slot for it), so
                //distinct non-conforming pairs of the same value produce
                //value-equal results — that multiplicity is intended.
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
        }

        return builder.ToImmutable();
    }
}
