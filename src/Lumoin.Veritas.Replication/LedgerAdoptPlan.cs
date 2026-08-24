using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of <see cref="DottedCommitLedger.PrepareAdopt"/>: the dataset delta a reconcile write-back
/// applies and the causality annotation its commit carries. The delta and the annotation can differ — a dot
/// union onto an already-present triple, a partial drop that leaves survivors, or a bare terminal context fold
/// each change causal knowledge without changing the committed triple set, and commit as a causality-only
/// entry with an empty delta.
/// </summary>
public sealed class LedgerAdoptPlan
{
    /// <summary>The plan that adopts nothing: the peer knowledge was already covered. No commit is made.</summary>
    public static LedgerAdoptPlan Empty { get; } = new([], [], null);

    /// <summary>The triples the write-back adds to the default graph — peer additions absent locally whose dots survived the guard.</summary>
    public IReadOnlyCollection<EncodedTriple> EffectiveAdditions { get; }

    /// <summary>The triples the write-back removes from the default graph — peer drops that cancel every present dot of their entry.</summary>
    public IReadOnlyCollection<EncodedTriple> EffectiveRemovals { get; }

    /// <summary>The annotation the write-back's commit carries, or <see langword="null"/> on the empty plan.</summary>
    public CommitCausality? Causality { get; }

    /// <summary>Whether the plan carries anything to commit.</summary>
    public bool HasWork
    {
        get
        {
            return Causality is not null;
        }
    }

    /// <summary>Creates a plan.</summary>
    /// <param name="effectiveAdditions">The triples the write-back adds.</param>
    /// <param name="effectiveRemovals">The triples the write-back removes.</param>
    /// <param name="causality">The annotation the commit carries, or <see langword="null"/> for the empty plan.</param>
    internal LedgerAdoptPlan(IReadOnlyCollection<EncodedTriple> effectiveAdditions, IReadOnlyCollection<EncodedTriple> effectiveRemovals, CommitCausality? causality)
    {
        EffectiveAdditions = effectiveAdditions;
        EffectiveRemovals = effectiveRemovals;
        Causality = causality;
    }
}
