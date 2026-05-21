using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Shared helper for the four pair-property constraints
/// (<c>sh:equals</c>, <c>sh:disjoint</c>, <c>sh:lessThan</c>,
/// <c>sh:lessThanOrEquals</c>). Collects the value-node set found at
/// the constraint's <em>other</em> predicate from the same focus node.
/// </summary>
/// <remarks>
/// <para>
/// The pair-property constraints compare the value-node set produced
/// by the property shape's <c>sh:path</c> against a separately named
/// predicate's value-node set, both rooted at the same focus node.
/// This helper performs only the second collection — the first is
/// already supplied to the evaluator as the <c>valueNodes</c>
/// parameter.
/// </para>
/// <para>
/// The other-predicate is always a single IRI per SHACL Core spec
/// (the constraint records carry an <see cref="IriId"/>, not a
/// <see cref="PropertyPath"/>), so a single
/// <c>MatchTriplesAsync</c> call suffices — no recursive path
/// machinery.
/// </para>
/// </remarks>
internal static class PairPropertyComparisonSet
{
    /// <summary>
    /// Collects the set of object terms reachable from
    /// <paramref name="focusNode"/> via predicate
    /// <paramref name="otherPredicate"/> in the data graph.
    /// </summary>
    public static async ValueTask<ImmutableArray<TermId>> CollectAsync(
        TermId focusNode,
        IriId otherPredicate,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        //HashSet for de-duplication; SHACL pair-property constraints
        //operate on sets, not multisets. Same focus node can have the
        //same object via the same predicate multiple times in some
        //source serialisations, but only one is in the value set.
        HashSet<TermId> seen = [];
        ImmutableArray<TermId>.Builder builder = ImmutableArray.CreateBuilder<TermId>();

        await foreach(EncodedTriple triple in context.DataMatchOps.MatchTriples(
            focusNode, otherPredicate.Value, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            if(seen.Add(triple.Object))
            {
                builder.Add(triple.Object);
            }
        }

        return builder.ToImmutable();
    }
}
