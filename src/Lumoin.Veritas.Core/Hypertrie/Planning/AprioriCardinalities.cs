using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// A-priori cardinality statistics for class-membership patterns: per
/// class, a sound upper bound on the instances a
/// <c>?x &lt;type&gt; C</c> pattern can match once subclass structure
/// is taken into account.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the bound means.</b> An instance of a subclass is an
/// instance of every subsumer, so the true extent of a class under
/// subclass closure is the union of the asserted extents of its
/// subsumees. The bound stored here is the <em>sum</em> of those
/// asserted extents — at least as large as the union (extents
/// overlap), so it never undercounts. A planner ordering variables by
/// these bounds works from sound upper bounds, never optimistic
/// guesses.
/// </para>
/// <para>
/// <b>Producer and consumer.</b> The statistics are produced outside
/// the join layer — a TBox classification supplies the subclass
/// structure, the store supplies per-class extent counts — and reach
/// the planner through <see cref="PlannerContext.Cardinalities"/>. A
/// selectivity-aware planner recognises a pattern binding a variable
/// through <see cref="TypePredicate"/> against a constant class and
/// consults <see cref="TryGetUpperBound"/> for it. An absent entry
/// means no information, not an extent of zero; a present entry of
/// zero is real knowledge (the class can match nothing).
/// </para>
/// <para>
/// <b>Generation binding.</b> An instance describes one store
/// generation. Statistics computed against one snapshot say nothing
/// about a successor; the producer rebuilds per generation exactly as
/// it rebuilds its classification.
/// </para>
/// </remarks>
[DebuggerDisplay("AprioriCardinalities Classes={Count}")]
public sealed class AprioriCardinalities
{
    /// <summary>The encoded identifier of the class-membership predicate the bounds describe — <c>rdf:type</c> in RDF data.</summary>
    public TermId TypePredicate { get; }

    /// <summary>Per-class upper bounds keyed by encoded class identifier. Owned by this instance.</summary>
    private Dictionary<TermId, long> ClassUpperBounds { get; }

    /// <summary>The number of classes carrying a bound.</summary>
    public int Count
    {
        get
        {
            return ClassUpperBounds.Count;
        }
    }

    /// <summary>
    /// Constructs the statistics from a producer's computed bounds.
    /// </summary>
    /// <param name="typePredicate">The encoded class-membership predicate.</param>
    /// <param name="classUpperBounds">Per-class upper bounds keyed by encoded class identifier; ownership transfers to this instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="classUpperBounds"/> is <see langword="null"/>.</exception>
    public AprioriCardinalities(TermId typePredicate, Dictionary<TermId, long> classUpperBounds)
    {
        ArgumentNullException.ThrowIfNull(classUpperBounds);

        TypePredicate = typePredicate;
        ClassUpperBounds = classUpperBounds;
    }

    /// <summary>
    /// The upper bound for the class, when one is known.
    /// </summary>
    /// <param name="classId">The encoded class identifier.</param>
    /// <param name="upperBound">The sound upper bound on the class's extent under subclass closure.</param>
    /// <returns><see langword="true"/> when a bound is known for the class; <see langword="false"/> means no information.</returns>
    public bool TryGetUpperBound(TermId classId, out long upperBound)
    {
        return ClassUpperBounds.TryGetValue(classId, out upperBound);
    }
}
