using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The reasoned mutable engine's per-commit maintenance hook, invoked inside
/// <see cref="DatasetEditSession.CommitAsync"/> BEFORE the linearising journal
/// append: given the commit's net asserted default-graph delta and the session's
/// tentative post-op default-graph store, it evolves the maintained closure and
/// returns the served-store delta the commit applies to the served (base ∪
/// derived) store. A peer of <see cref="ReasoningMaterializationDelegate"/> — the
/// query engine defines the hook and never depends on a reasoner, and a
/// composition root supplies the reasoner-backed binding; leaving it unwired
/// makes every commit byte-identical to a non-reasoning engine (the served store
/// stays the asserted store).
/// </summary>
/// <remarks>
/// The delegate runs under the dataset's maintenance mutex, so its per-invocation
/// state is single-threaded. The Sparql layer reads ONLY
/// <see cref="MaintainedCommitDelta.ServedAdditions"/> and
/// <see cref="MaintainedCommitDelta.ServedRemovals"/> and the
/// <see cref="MaintainedCommitDelta.OverlayOn"/> flag; the opaque
/// <see cref="MaintainedCommitDelta.ReasoningState"/> payload is round-tripped
/// into the published dataset state untouched.
/// </remarks>
/// <param name="baseAdded">The triples the commit added to the asserted default graph — the true sequential net, disjoint from <paramref name="baseRemoved"/>.</param>
/// <param name="baseRemoved">The triples the commit removed from the asserted default graph — the true sequential net.</param>
/// <param name="tentativeAssertedStore">The session's tentative post-op asserted default-graph store: the rendezvous generation marker, the floor re-detection source, and the rebuild base.</param>
/// <param name="wholesaleReplace">Whether the caller detected a wholesale default-graph replacement (the net retract set covers the entire pre-commit asserted default graph), which rebuilds from <paramref name="tentativeAssertedStore"/> instead of feeding a degenerate incremental apply.</param>
/// <param name="cancellationToken">A token that aborts maintenance; observed pre-append, so a cancel fails the commit before it linearises.</param>
/// <returns>The served-store delta, the overlay-on flag, and the opaque reasoning-state payload.</returns>
public delegate ValueTask<MaintainedCommitDelta> ClosureMaintenanceDelegate(
    IReadOnlyCollection<EncodedTriple> baseAdded,
    IReadOnlyCollection<EncodedTriple> baseRemoved,
    HypertrieGraphStore tentativeAssertedStore,
    bool wholesaleReplace,
    CancellationToken cancellationToken);

/// <summary>
/// The single commit-outcome seam of a reasoned mutable engine, fired exactly
/// once per <see cref="ClosureMaintenanceDelegate"/> invocation with the
/// predicate "the delegate was invoked and the commit did/did not land". The
/// dataset latches <c>landed=true</c> immediately after the atomic publish and
/// reports the outcome in one finally before the maintenance mutex releases, so a
/// delegate/apply throw and a post-delegate append conflict are both reported as
/// invoked-but-not-landed. The Database-layer handler invalidates the maintenance
/// instance on <c>landed=false</c> (the next commit rebuilds from the
/// then-committed asserted base) and rolls the staged state forward on
/// <c>landed=true</c>. A commit that skipped the delegate (a stale session, or a
/// named-graph-only commit) fires nothing.
/// </summary>
/// <param name="landed">Whether the commit the delegate maintained linearised (published).</param>
public delegate void ClosureMaintenanceOutcomeDelegate(bool landed);

/// <summary>
/// The Sparql-owned result of one <see cref="ClosureMaintenanceDelegate"/>
/// invocation: the served-store delta the commit applies to the served (base ∪
/// derived) store, the overlay-on flag, and an opaque reasoning-state payload the
/// Database layer round-trips into the published dataset state. Both delta
/// collections are IMMUTABLE copies sized to the facts the commit touched — never
/// a live view into the closure's per-apply-cleared recorded sets — so a later
/// commit cannot clobber a delta still being consumed.
/// </summary>
/// <remarks>
/// On EVERY commit class the applied served delta equals
/// <c>setdiff(new served target, previous served store)</c> with the entered and
/// left sets disjoint; the Sparql layer applies it verbatim to the served store
/// and never inspects the reasoning payload.
/// </remarks>
public readonly record struct MaintainedCommitDelta
{
    /// <summary>The triples to add to the served store — an immutable copy of the commit's served additions.</summary>
    public IReadOnlyCollection<EncodedTriple> ServedAdditions { get; init; }

    /// <summary>The triples to remove from the served store — an immutable copy of the commit's served removals.</summary>
    public IReadOnlyCollection<EncodedTriple> ServedRemovals { get; init; }

    /// <summary>Whether the derived overlay is on (the closure stayed consistent) rather than withdrawn (served asserted-only). Informational for the Sparql layer; the served delta already encodes the withdrawal.</summary>
    public bool OverlayOn { get; init; }

    /// <summary>The opaque reasoning-state payload the Database layer round-trips into the published dataset state and reads back for provenance; the Sparql layer never inspects it.</summary>
    public object? ReasoningState { get; init; }
}
