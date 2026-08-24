using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A replica's dotted observed-remove view of the committed ASSERTED default graph: the entry table (every
/// present triple with its causal dots), the causal context (every dot ever observed), and the dataset StateId
/// the pair reflects. It lives beside <see cref="ReplicationIndexFeed"/> as an observer of the same committed
/// delta, advanced inside the dataset's publish critical section so ledger, feed, and store never diverge and
/// delta ordering is journal-commit ordering. Observed-remove knowledge is context coverage plus entry absence
/// — no tombstone objects exist.
/// </summary>
/// <remarks>
/// <para>
/// The ledger never decides during a fold. Every decision — which counters a local commit mints, which peer
/// dots an adopt admits, which dots a removal drops — is made when the commit's <see cref="CommitCausality"/>
/// annotation is BUILT, against the live ledger, before the linearising journal append; the append's head
/// compare-and-swap certifies the basis, because the ledger only advances inside a publish and no publish can
/// intervene between a won append and its own publish. The fold then applies the annotation verbatim, exactly
/// as journal replay does, so live advance and recovery share one code path and one semantics.
/// </para>
/// <para>
/// Every fold is idempotent PER ENTRY: an addition dot the context already covers is incorporated history —
/// possibly dropped since — and is skipped rather than re-inserted, drops remove only the named dots (a dot a
/// prior fold dropped can never re-enter, because dots are unique events and the skip blocks re-insertion),
/// and context folds are monotone joins. Recovery therefore folds ALL annotated entries in journal sequence
/// order over the loaded causality artifact with no position bookkeeping — refolds of entries the artifact
/// already incorporates are no-ops on their own. The one precondition is the journal's own: the entry stream
/// is complete and order-preserving from its start, which the self-contained and header-anchored durable logs
/// guarantee and the recovery-end StateId cross-check gates.
/// </para>
/// </remarks>
public sealed class DottedCommitLedger
{
    //The Lock is a synchronization primitive, not mutable data state; a readonly field is the idiomatic form for
    //the C# lock statement over System.Threading.Lock.
    private readonly Lock gate = new();

    /// <summary>The entry table: every present asserted default-graph triple with its causal dots. Presence is presence of at least one dot.</summary>
    private Dictionary<EncodedTriple, List<CausalDot>> Entries { get; } = [];

    /// <summary>The causal context: every dot this replica has ever observed. Dominates every dot of every present entry.</summary>
    private CausalContext Context { get; }

    /// <summary>The dataset StateId the entry table reflects.</summary>
    private NodeIdentifier StateIdStamp { get; set; }

    /// <summary>The count of committed default-graph publishes folded since this ledger was constructed; recovery folds do not count.</summary>
    private long generation;

    /// <summary>The host identity axes that have ever minted on this ledger, the current one included.</summary>
    private HashSet<ReplicaAxis> Identities { get; } = [];

    /// <summary>The host identity axis local commits mint on — supplied by the host at open, never read from store bytes.</summary>
    public ReplicaAxis Identity { get; }

    /// <summary>The dataset StateId the ledger currently reflects, read atomically.</summary>
    public NodeIdentifier StateId
    {
        get
        {
            lock(gate)
            {
                return StateIdStamp;
            }
        }
    }

    /// <summary>The ledger's fold generation, read atomically: the count of committed default-graph publishes folded since open — the liveness sibling of the maintained sketch encoder's generation. Recovery folds do not count; the generation starts at zero on every open.</summary>
    public long Generation
    {
        get
        {
            lock(gate)
            {
                return generation;
            }
        }
    }

    /// <summary>Reads the largest counter the LIVE context covers anywhere on the host identity's own axis, atomically — the identity-collision tripwire's comparison value: this replica is the only minter on its axis, so a peer presenting coverage or a dot beyond this maximum proves a second minter under the same identity. Monotone over an open — it never decreases — and a method rather than a property so the tripwire seam binds it as a method group.</summary>
    /// <returns>The overall maximum covered counter on the identity's own axis; 0 before the first mint.</returns>
    public ulong ReadOwnAxisMaximum()
    {
        lock(gate)
        {
            return Context.MaxOn(Identity);
        }
    }

    /// <summary>
    /// Mints a baseline annotation: one fresh dot per present triple on <paramref name="identity"/>, counters
    /// 1 through the triple count in enumeration order, and no folded context — the baseline claims no
    /// observed-remove knowledge, so it introduces no false tombstones and no false additions. Pure: the same
    /// annotation object seeds the ledger AND rides the journal entry, one source of truth for both. A store
    /// created with a host identity attaches this to its Initial entry — the Initial entry IS its baseline.
    /// </summary>
    /// <param name="identity">The host identity axis the baseline dots mint on.</param>
    /// <param name="presentTriples">The present committed asserted default-graph triples.</param>
    /// <returns>The baseline annotation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="presentTriples"/> is <see langword="null"/>.</exception>
    public static CommitCausality MintBaseline(ReplicaAxis identity, IReadOnlyList<EncodedTriple> presentTriples)
    {
        ArgumentNullException.ThrowIfNull(presentTriples);

        ImmutableArray<DottedTripleAssignment>.Builder additions = ImmutableArray.CreateBuilder<DottedTripleAssignment>(presentTriples.Count);
        ulong counter = 0;
        foreach(EncodedTriple triple in presentTriples)
        {
            counter++;
            additions.Add(new DottedTripleAssignment(triple, [new CausalDot(identity, counter)]));
        }

        return new CommitCausality(additions.MoveToImmutable(), Drops: [], FoldedContext: null, IsBaseline: true);
    }

    /// <summary>
    /// Creates a ledger for a host identity, folding a baseline annotation when one exists and stamping the
    /// dataset's ACTUAL StateId from the same state that built it — never a default stamp.
    /// </summary>
    /// <param name="identity">The host identity axis local commits mint on.</param>
    /// <param name="baseline">The baseline annotation minted by <see cref="MintBaseline"/> over the same present triples the dataset committed, or <see langword="null"/> for an empty store.</param>
    /// <param name="actualStateId">The dataset StateId of the committed state the ledger reflects.</param>
    /// <exception cref="ArgumentException"><paramref name="baseline"/> is not a baseline annotation.</exception>
    public DottedCommitLedger(ReplicaAxis identity, CommitCausality? baseline, NodeIdentifier actualStateId)
    {
        Identity = identity;
        Context = new CausalContext();
        Identities.Add(identity);
        StateIdStamp = actualStateId;

        if(baseline is { } annotation)
        {
            if(!annotation.IsBaseline)
            {
                throw new ArgumentException("A ledger seeds from a BASELINE annotation; fold ordinary commit annotations through the delta observer.", nameof(baseline));
            }

            FoldCausality(annotation);
        }
    }

    /// <summary>Creates a ledger over restored state — the recovery constructor <see cref="RestoreSnapshot"/> uses.</summary>
    /// <param name="identity">The host identity axis local commits mint on this open.</param>
    /// <param name="entries">The restored entry table.</param>
    /// <param name="context">The restored causal context; owned by the ledger from here.</param>
    /// <param name="identities">The restored identity axes; the current identity joins them.</param>
    /// <param name="stateId">The dataset StateId the restored entry table reflects.</param>
    private DottedCommitLedger(ReplicaAxis identity, ImmutableArray<DottedTripleAssignment> entries, CausalContext context, ImmutableArray<ReplicaAxis> identities, NodeIdentifier stateId)
    {
        Identity = identity;
        Context = context;
        StateIdStamp = stateId;
        foreach(ReplicaAxis axis in identities)
        {
            Identities.Add(axis);
        }

        Identities.Add(identity);
        foreach(DottedTripleAssignment assignment in entries)
        {
            List<CausalDot> dots = new(assignment.Dots.Length);
            foreach(CausalDot dot in assignment.Dots)
            {
                dots.Add(dot);
            }

            Entries[assignment.Triple] = dots;
        }
    }

    /// <summary>Restores a ledger from a snapshot a persist serialized — the at-rest causality artifact's content.</summary>
    /// <param name="identity">The host identity axis local commits mint on this open.</param>
    /// <param name="snapshot">The snapshot read from the causality artifact.</param>
    /// <returns>The restored ledger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    public static DottedCommitLedger RestoreSnapshot(ReplicaAxis identity, DottedLedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DottedCommitLedger(identity, snapshot.Entries, snapshot.Context.Clone(), snapshot.Identities, snapshot.StateId);
    }

    /// <summary>
    /// The committed-delta observer target: folds one commit's causality annotation and stamps the new StateId,
    /// called inside the dataset's publish critical section so the ledger advances in journal-commit order,
    /// atomically with the store swap and the feed. The annotation is applied verbatim; the fold decides
    /// nothing.
    /// </summary>
    /// <param name="additions">The triples the commit added; carried by the annotation's addition assignments.</param>
    /// <param name="removals">The triples the commit removed; carried by the annotation's drop assignments.</param>
    /// <param name="stateId">The dataset StateId the commit produced.</param>
    /// <param name="causality">The commit's annotation; <see langword="null"/> only when the commit moved no default-graph content.</param>
    /// <exception cref="InvalidOperationException">A default-graph delta arrived with no annotation — a commit bypassed the causality builder on a remove-aware store.</exception>
    public void OnDefaultGraphDelta(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId, CommitCausality? causality)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        lock(gate)
        {
            if(causality is { } annotation)
            {
                FoldCausality(annotation);
            }
            else if(additions.Count > 0 || removals.Count > 0)
            {
                throw new InvalidOperationException("A committed default-graph delta reached the dotted commit ledger with no causality annotation; on a remove-aware store every default-graph commit is annotated.");
            }

            generation++;
            StateIdStamp = stateId;
        }
    }

    /// <summary>
    /// Builds a locally-authored commit's causality annotation against the live ledger: one fresh dot per net
    /// addition on the host identity's axis, counters continuing from the context's maximum on that axis, and
    /// for each net removal the removed triple's present dots. Read-only — the ledger advances only when the
    /// commit publishes and the annotation folds. The journal append's head compare-and-swap certifies the
    /// basis; a competing commit that publishes first fails this commit's append and the annotation is rebuilt.
    /// </summary>
    /// <param name="additions">The commit's net default-graph additions.</param>
    /// <param name="removals">The commit's net default-graph removals.</param>
    /// <returns>The annotation, or <see langword="null"/> when the commit moves no default-graph content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="additions"/> or <paramref name="removals"/> is <see langword="null"/>.</exception>
    /// <exception cref="EditSessionConcurrencyException">A net removal names a triple the entry table does not hold: the committed set has moved past the building session's base (the entry was removed by a competing commit), so the session's own append is bound to fail its head compare-and-swap — the conflict is raised early, under the contract the commit retry facades key on.</exception>
    public CommitCausality? BuildLocalCausality(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        if(additions.Count == 0 && removals.Count == 0)
        {
            return null;
        }

        lock(gate)
        {
            ImmutableArray<DottedTripleAssignment> minted = [];
            if(additions.Count > 0)
            {
                //Continuity derives from the context's overall maximum on the local axis, so a counter is never
                //reused even against non-contiguous coverage no local history produces.
                ulong counter = Context.MaxOn(Identity);
                ImmutableArray<DottedTripleAssignment>.Builder builder = ImmutableArray.CreateBuilder<DottedTripleAssignment>(additions.Count);
                foreach(EncodedTriple triple in additions)
                {
                    counter++;
                    builder.Add(new DottedTripleAssignment(triple, [new CausalDot(Identity, counter)]));
                }

                minted = builder.MoveToImmutable();
            }

            ImmutableArray<DottedTripleAssignment> drops = [];
            if(removals.Count > 0)
            {
                ImmutableArray<DottedTripleAssignment>.Builder builder = ImmutableArray.CreateBuilder<DottedTripleAssignment>(removals.Count);
                foreach(EncodedTriple triple in removals)
                {
                    //The ledger mirrors the committed set, so a missing entry means a competing commit removed
                    //the triple after this session read its base — the session's append cannot succeed, and the
                    //conflict surfaces here under the retry contract rather than as a spurious invariant fault.
                    if(!Entries.TryGetValue(triple, out List<CausalDot>? dots))
                    {
                        throw new EditSessionConcurrencyException("A net default-graph removal names a triple the dotted commit ledger no longer holds: a competing commit removed it first. Rebase and retry the request against the new state.");
                    }

                    builder.Add(new DottedTripleAssignment(triple, [.. dots]));
                }

                drops = builder.MoveToImmutable();
            }

            return new CommitCausality(minted, drops, FoldedContext: null, IsBaseline: false);
        }
    }

    /// <summary>
    /// Plans a reconcile write-back's adoption of peer knowledge against the live ledger — the commit-time
    /// adopt-guard. A peer dot is admitted only when the live context does not cover it (a covered dot became a
    /// local tombstone mid-flight; its entry is skipped, value-based); a peer drop removes only the named dots
    /// still present, and a drop that leaves survivors keeps the triple (add-wins over assertion events); the
    /// peer context always folds. Read-only and re-run per write-back attempt: the plan's basis is certified by
    /// the same head compare-and-swap as every annotation, so the retry loop is safe by construction.
    /// </summary>
    /// <param name="peerAdditions">The peer entries classified as genuine adds, each with its peer dots; no triple here may also appear in <paramref name="peerDrops"/>.</param>
    /// <param name="peerDrops">The peer-commanded removals, each with the dots it cancels; no triple here may also appear in <paramref name="peerAdditions"/>.</param>
    /// <param name="peerContext">The peer causal context the session exchanged; folded whole.</param>
    /// <returns>The plan: the dataset delta to apply and the annotation to commit, or an empty plan when the peer knowledge adds nothing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A triple appears in both <paramref name="peerAdditions"/> and <paramref name="peerDrops"/>: the addition and removal effects are each planned against the same pre-commit entries, so one call naming a triple on both sides could commit a dataset removal the annotation's surviving dots contradict — ledger and dataset would diverge. The apply seams plan one side per call.</exception>
    public LedgerAdoptPlan PrepareAdopt(IReadOnlyList<DottedTripleAssignment> peerAdditions, IReadOnlyList<DottedTripleAssignment> peerDrops, CausalContext peerContext)
    {
        ArgumentNullException.ThrowIfNull(peerAdditions);
        ArgumentNullException.ThrowIfNull(peerDrops);
        ArgumentNullException.ThrowIfNull(peerContext);
        if(peerAdditions.Count > 0 && peerDrops.Count > 0)
        {
            HashSet<EncodedTriple> additionTriples = new(peerAdditions.Count);
            foreach(DottedTripleAssignment peerAddition in peerAdditions)
            {
                additionTriples.Add(peerAddition.Triple);
            }

            foreach(DottedTripleAssignment peerDrop in peerDrops)
            {
                if(additionTriples.Contains(peerDrop.Triple))
                {
                    throw new ArgumentException("One adopt plans a triple as either an addition or a drop, never both: the two effects are each planned against the same pre-commit entries, so a triple on both sides could commit a dataset removal the annotation's surviving dots contradict.", nameof(peerDrops));
                }
            }
        }

        lock(gate)
        {
            List<EncodedTriple> effectiveAdditions = [];
            List<EncodedTriple> effectiveRemovals = [];
            ImmutableArray<DottedTripleAssignment>.Builder adoptedAdditions = ImmutableArray.CreateBuilder<DottedTripleAssignment>();
            ImmutableArray<DottedTripleAssignment>.Builder adoptedDrops = ImmutableArray.CreateBuilder<DottedTripleAssignment>();

            foreach(DottedTripleAssignment peerAddition in peerAdditions)
            {
                ImmutableArray<CausalDot>.Builder surviving = ImmutableArray.CreateBuilder<CausalDot>(peerAddition.Dots.Length);
                foreach(CausalDot dot in peerAddition.Dots)
                {
                    if(!Context.Covers(dot))
                    {
                        surviving.Add(dot);
                    }
                }

                if(surviving.Count == 0)
                {
                    continue;
                }

                adoptedAdditions.Add(new DottedTripleAssignment(peerAddition.Triple, surviving.DrainToImmutable()));
                if(!Entries.ContainsKey(peerAddition.Triple))
                {
                    effectiveAdditions.Add(peerAddition.Triple);
                }
            }

            foreach(DottedTripleAssignment peerDrop in peerDrops)
            {
                if(!Entries.TryGetValue(peerDrop.Triple, out List<CausalDot>? presentDots))
                {
                    continue;
                }

                ImmutableArray<CausalDot>.Builder cancelled = ImmutableArray.CreateBuilder<CausalDot>(peerDrop.Dots.Length);
                foreach(CausalDot dot in peerDrop.Dots)
                {
                    if(presentDots.Contains(dot))
                    {
                        cancelled.Add(dot);
                    }
                }

                if(cancelled.Count == 0)
                {
                    continue;
                }

                bool removesWholeEntry = cancelled.Count == presentDots.Count;
                adoptedDrops.Add(new DottedTripleAssignment(peerDrop.Triple, cancelled.DrainToImmutable()));
                if(removesWholeEntry)
                {
                    effectiveRemovals.Add(peerDrop.Triple);
                }
            }

            bool contextAddsKnowledge = !peerContext.CoveredBy(Context);
            if(adoptedAdditions.Count == 0 && adoptedDrops.Count == 0 && !contextAddsKnowledge)
            {
                return LedgerAdoptPlan.Empty;
            }

            CommitCausality causality = new(
                adoptedAdditions.DrainToImmutable(),
                adoptedDrops.DrainToImmutable(),
                FoldedContext: peerContext.Clone(),
                IsBaseline: false);

            return new LedgerAdoptPlan(effectiveAdditions, effectiveRemovals, causality);
        }
    }

    /// <summary>
    /// Folds one journal entry during recovery: an <see cref="EditSessionEntryKind.Initial"/> or
    /// <see cref="EditSessionEntryKind.Committed"/> entry stamps its child StateId, and its annotation — when it
    /// carries one — folds under the per-entry idempotence of <see cref="FoldCausality"/>. Recovery calls this
    /// for every entry in journal sequence order over the loaded causality artifact; a refold of an entry the
    /// artifact already incorporates is a no-op on its own (its addition dots are covered and skipped), so the
    /// final stamp equals the journal head exactly when ledger and journal describe one history.
    /// </summary>
    /// <param name="entry">The journal entry to fold.</param>
    public void FoldRecoveredEntry(in DatasetJournalEntry entry)
    {
        if(entry.EntryKind is not (EditSessionEntryKind.Initial or EditSessionEntryKind.Committed))
        {
            return;
        }

        lock(gate)
        {
            if(entry.Causality is { } annotation)
            {
                FoldCausality(annotation);
            }

            StateIdStamp = entry.ChildId;
        }
    }

    /// <summary>Reads the entry table, context, identities, and StateId atomically — the snapshot a persist serializes or a reconcile session pins. Unaffected by later commits.</summary>
    /// <returns>The snapshot.</returns>
    public DottedLedgerSnapshot Snapshot()
    {
        lock(gate)
        {
            ImmutableArray<DottedTripleAssignment>.Builder entries = ImmutableArray.CreateBuilder<DottedTripleAssignment>(Entries.Count);
            foreach((EncodedTriple triple, List<CausalDot> dots) in Entries)
            {
                entries.Add(new DottedTripleAssignment(triple, [.. dots]));
            }

            ImmutableArray<ReplicaAxis>.Builder identities = ImmutableArray.CreateBuilder<ReplicaAxis>(Identities.Count);
            foreach(ReplicaAxis axis in Identities)
            {
                identities.Add(axis);
            }

            return new DottedLedgerSnapshot(identities.MoveToImmutable(), entries.MoveToImmutable(), Context.Clone(), StateIdStamp);
        }
    }

    /// <summary>
    /// Applies one annotation to the entry table and context under the gate. Addition dots the context does
    /// NOT yet cover union into their entry and extend the context; an addition dot the context already covers
    /// is history the ledger has already incorporated — and possibly dropped since — so it is skipped, which is
    /// what makes every fold idempotent PER ENTRY: a covered dot can never re-enter the table, so refolding any
    /// already-incorporated annotation is a no-op on its own, with no reliance on a later drop refolding too. A
    /// live commit never carries a covered addition dot — local mints continue past the context maximum, the
    /// adopt-guard admits only uncovered peer dots, and the causality commit gate keeps both bases live through
    /// the publish. Drop dots fold into the context unconditionally (a drop is an observation of those dots,
    /// entry present or not) and remove exactly the named dots; an entry with no dots left leaves the table.
    /// The folded peer context joins monotonically, and a baseline's minting axes join the identity set.
    /// </summary>
    /// <param name="annotation">The annotation to fold.</param>
    private void FoldCausality(CommitCausality annotation)
    {
        foreach(DottedTripleAssignment assignment in annotation.Additions)
        {
            List<CausalDot>? dots = Entries.TryGetValue(assignment.Triple, out List<CausalDot>? present) ? present : null;
            foreach(CausalDot dot in assignment.Dots)
            {
                if(annotation.IsBaseline)
                {
                    Identities.Add(dot.Axis);
                }

                if(dots is not null && dots.Contains(dot))
                {
                    continue;
                }

                if(Context.Covers(dot))
                {
                    continue;
                }

                if(dots is null)
                {
                    dots = new List<CausalDot>(assignment.Dots.Length);
                    Entries[assignment.Triple] = dots;
                }

                dots.Add(dot);
                Context.Fold(dot);
            }
        }

        foreach(DottedTripleAssignment assignment in annotation.Drops)
        {
            foreach(CausalDot dot in assignment.Dots)
            {
                Context.Fold(dot);
            }

            if(!Entries.TryGetValue(assignment.Triple, out List<CausalDot>? dots))
            {
                continue;
            }

            foreach(CausalDot dot in assignment.Dots)
            {
                dots.Remove(dot);
            }

            if(dots.Count == 0)
            {
                Entries.Remove(assignment.Triple);
            }
        }

        if(annotation.FoldedContext is { } peerContext)
        {
            Context.Merge(peerContext);
        }
    }
}
