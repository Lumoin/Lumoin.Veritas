using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The path an <see cref="OwlRlMaintainedClosure.Apply"/> took to reach its
/// result — the incremental maintenance pipeline, or a from-scratch rebuild
/// entered from an inconsistent or a poisoned state.
/// </summary>
internal enum OwlRlMaintenanceMode
{
    /// <summary>The incremental overdelete / rederive / insert pipeline ran.</summary>
    Incremental,

    /// <summary>A from-scratch rebuild ran because the prior state was inconsistent.</summary>
    RebuildInconsistent,

    /// <summary>A from-scratch rebuild ran because the prior Apply left the state poisoned.</summary>
    RebuildPoisoned,
}

/// <summary>
/// The deterministic proxies of one <see cref="OwlRlMaintainedClosure.Apply"/> —
/// the pass's counters and the path it took.
/// </summary>
/// <param name="OverdeleteMarked">The number of facts marked after the overdelete fixpoint.</param>
/// <param name="DeletionRounds">The number of overdelete rounds run.</param>
/// <param name="DirectlyRederived">The number of deleted facts the head-bound matcher restored directly.</param>
/// <param name="RestoredTotal">The number of marked facts present again in the closure at completion.</param>
/// <param name="InsertRounds">The number of semi-naive insert rounds run.</param>
/// <param name="ChoiceOwnerReFires">The number of choice/list owner construct re-fires.</param>
/// <param name="BaseDemotions">The number of base additions that demoted an existing derived fact.</param>
/// <param name="BasePromotions">The number of seeded removals promoted to derived.</param>
/// <param name="Mode">The path the Apply took.</param>
internal readonly record struct OwlRlMaintenanceStatistics(
    int OverdeleteMarked,
    int DeletionRounds,
    int DirectlyRederived,
    int RestoredTotal,
    int InsertRounds,
    int ChoiceOwnerReFires,
    int BaseDemotions,
    int BasePromotions,
    OwlRlMaintenanceMode Mode);

/// <summary>
/// The net membership change one maintained <see cref="OwlRlMaintainedClosure.Apply"/>
/// recorded for a tracked set — the facts that entered and the facts that left,
/// folded so a fact that left and re-entered within that one Apply appears in
/// neither collection (<see cref="Entered"/> and <see cref="Left"/> are always
/// disjoint).
/// </summary>
/// <param name="Entered">The facts that net-entered the tracked set over the Apply.</param>
/// <param name="Left">The facts that net-left the tracked set over the Apply.</param>
/// <remarks>
/// Both collections are a live view over the closure's per-Apply record, valid
/// only until the next <see cref="OwlRlMaintainedClosure.Apply"/> clears it — a
/// consumer that must outlive the next Apply copies them first, the same
/// discipline <see cref="OwlRlResult.Derived"/> follows.
/// </remarks>
internal readonly record struct OwlRlMembershipDelta(
    IReadOnlyCollection<EncodedTriple> Entered,
    IReadOnlyCollection<EncodedTriple> Left);

/// <summary>
/// An OWL 2 RL closure kept equal to the from-scratch closure of its base
/// under incremental add and retract edits, by overdeletion, head-bound
/// rederivation, and semi-naive insertion (the DRed baseline).
/// </summary>
/// <remarks>
/// <para>
/// A single caller drives one instance; the type is not thread-safe, matching
/// the underlying closure context. The <see cref="Current"/> and Apply results'
/// <see cref="OwlRlResult.Derived"/> are a live view of the closure that stays
/// valid only until the next <see cref="Apply"/> — a comparison snapshots it
/// first.
/// </para>
/// <para>
/// While the closure is consistent, Apply runs the incremental pipeline. Once a
/// falsity fires it stays inconsistent, and every later Apply rebuilds from
/// scratch over the edited base — never worse than a full rematerialization —
/// adopting the result only when it becomes consistent again. A throw mid-Apply
/// (cancellation included) leaves the state poisoned; the base edit is atomic
/// and stands, so the next Apply rebuilds over the correct post-op base.
/// </para>
/// <para>
/// The incremental pipeline's cost is carried by the facts an op genuinely
/// touches: every marked fact pays overdelete propagation, a head-bound
/// rederivation check, physical index removal and possible re-insertion, and
/// that per-fact work costs a large constant factor more than the per-fact
/// cost of forward saturation in a from-scratch build. Additive ops and small
/// cascades therefore beat rebuilding by orders of magnitude, while a single
/// op whose surviving-derivation loss approaches a few percent of the derived
/// set — an <c>owl:sameAs</c> orbit unmerge is the canonical shape — costs as
/// much as a full rebuild. A caller choosing between maintaining and
/// rebuilding weighs its workload's cascade sizes against that envelope.
/// </para>
/// <para>
/// A maintained (incremental) Apply also records the net change to the served
/// set (<see cref="AllDelta"/>, base ∪ derived) and to the derived set
/// (<see cref="DerivedDelta"/>) at O(touched) cost, so a caller evolving an
/// external overlay applies exactly those deltas instead of diffing the whole
/// closure. Both are a live view valid only until the next Apply.
/// <see cref="HasRecordedDeltas"/> is <see langword="true"/> after an
/// incremental Apply and <see langword="false"/> after a from-scratch rebuild
/// (inconsistent or poisoned entry) or the initial construction build, which
/// swap the context wholesale and record nothing — a rebuild's consumer diffs
/// the served target itself.
/// </para>
/// </remarks>
internal sealed class OwlRlMaintainedClosure
{
    /// <summary>The runtime state of the maintained closure between Apply calls.</summary>
    private enum RuntimeMode
    {
        /// <summary>The closure is consistent and the incremental pipeline applies.</summary>
        Consistent,

        /// <summary>The closure is inconsistent; every Apply rebuilds from scratch.</summary>
        Inconsistent,

        /// <summary>An Apply threw mid-pipeline; the next Apply rebuilds from scratch.</summary>
        Poisoned,
    }

    /// <summary>The resolved RL vocabulary.</summary>
    private OwlRlTerms Terms { get; }

    /// <summary>The datatype oracle, normalized so a defaulted oracle disables the datatype falsities.</summary>
    private OwlRlDatatypeOracle DatatypeOracle { get; }

    /// <summary>The adopted closure context — replaced wholesale by a rebuild.</summary>
    private OwlRlClosure.ClosureContext Context { get; set; }

    /// <summary>The runtime state after the last Apply.</summary>
    private RuntimeMode State { get; set; }

    /// <summary>The result of the last Apply, whose derived set is a live view.</summary>
    private OwlRlResult CurrentResult { get; set; }

    /// <summary>The live-view result of the maintained closure, valid until the next <see cref="Apply"/>.</summary>
    public OwlRlResult Current => CurrentResult;

    /// <summary>The statistics of the last <see cref="Apply"/>.</summary>
    public OwlRlMaintenanceStatistics Statistics { get; private set; }

    /// <summary>
    /// Whether the last <see cref="Apply"/> recorded the <see cref="AllDelta"/>
    /// and <see cref="DerivedDelta"/> membership deltas — <see langword="true"/>
    /// after an incremental Apply (the empty short-circuit included, which
    /// records an empty delta), <see langword="false"/> after a from-scratch
    /// rebuild or before the first Apply, when the deltas are empty and a
    /// consumer diffs the served target itself.
    /// </summary>
    public bool HasRecordedDeltas { get; private set; }

    /// <summary>The net change to the served set (base ∪ derived) the last incremental <see cref="Apply"/> recorded — a live view valid until the next Apply, meaningful only when <see cref="HasRecordedDeltas"/> is <see langword="true"/>.</summary>
    internal OwlRlMembershipDelta AllDelta => Context.AllDelta;

    /// <summary>The net change to the derived set the last incremental <see cref="Apply"/> recorded — a live view valid until the next Apply, meaningful only when <see cref="HasRecordedDeltas"/> is <see langword="true"/>.</summary>
    internal OwlRlMembershipDelta DerivedDelta => Context.DerivedDelta;

    /// <summary>
    /// Builds the maintained closure from an initial base by the from-scratch
    /// semi-naive materialization.
    /// </summary>
    /// <param name="initialBase">The initial base triples, schema statements included.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the <c>dt-*</c> falsities; a defaulted oracle disables them.</param>
    /// <param name="cancellationToken">A token that aborts the build between rounds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="initialBase"/> or <paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public OwlRlMaintainedClosure(IEnumerable<EncodedTriple> initialBase, OwlRlTerms terms, OwlRlDatatypeOracle datatypeOracle = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialBase);
        ArgumentNullException.ThrowIfNull(terms);

        Terms = terms;
        DatatypeOracle = datatypeOracle.LiteralsKnownDistinct is null ? OwlRlDatatypeOracle.None : datatypeOracle;
        (Context, CurrentResult) = Build(initialBase, cancellationToken);
        State = CurrentResult.IsConsistent ? RuntimeMode.Consistent : RuntimeMode.Inconsistent;
        Statistics = default;

        //The construction build swaps the context in wholesale; the deltas hold
        //nothing until the first incremental Apply records against it.
        HasRecordedDeltas = false;
    }

    /// <summary>
    /// Applies an add-set and a retract-set, returning the result over the
    /// edited base. A consistent closure runs the incremental pipeline; an
    /// inconsistent or poisoned one rebuilds from scratch.
    /// </summary>
    /// <param name="added">The facts to add.</param>
    /// <param name="retracted">The facts to retract.</param>
    /// <param name="cancellationToken">A token that aborts the Apply.</param>
    /// <returns>The result over the edited base; <see cref="Statistics"/> records the pass.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="added"/> or <paramref name="retracted"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>; the state is left poisoned.</exception>
    public OwlRlResult Apply(IReadOnlyCollection<EncodedTriple> added, IReadOnlyCollection<EncodedTriple> retracted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(retracted);

        RuntimeMode entryState = State;

        //Poison the state on entry; the outcome replaces it, so any throw
        //mid-Apply leaves it poisoned and the next Apply rebuilds. The recorded
        //deltas are invalid until an outcome path re-latches them, so a throwing
        //Apply reports none.
        State = RuntimeMode.Poisoned;
        HasRecordedDeltas = false;

        if(entryState == RuntimeMode.Consistent)
        {
            if(added.Count == 0 && retracted.Count == 0)
            {
                State = RuntimeMode.Consistent;
                Statistics = new OwlRlMaintenanceStatistics(0, 0, 0, 0, 0, 0, 0, 0, OwlRlMaintenanceMode.Incremental);

                //An op with no facts is an incremental Apply that recorded an
                //empty delta; the served set is unchanged.
                Context.ResetMembershipDeltas();
                HasRecordedDeltas = true;

                return CurrentResult;
            }

            OwlRlResult result = Context.ApplyCore(added, retracted, cancellationToken);
            State = result.IsConsistent ? RuntimeMode.Consistent : RuntimeMode.Inconsistent;
            Statistics = Context.MaintenanceStatistics;
            CurrentResult = result;
            HasRecordedDeltas = true;

            return result;
        }

        OwlRlMaintenanceMode rebuildMode = entryState == RuntimeMode.Inconsistent
            ? OwlRlMaintenanceMode.RebuildInconsistent
            : OwlRlMaintenanceMode.RebuildPoisoned;

        HashSet<EncodedTriple> newBase = Context.ComputeRebuiltBase(added, retracted);
        (OwlRlClosure.ClosureContext rebuilt, OwlRlResult rebuiltResult) = Build(newBase, cancellationToken);
        Context = rebuilt;
        CurrentResult = rebuiltResult;
        State = rebuiltResult.IsConsistent ? RuntimeMode.Consistent : RuntimeMode.Inconsistent;
        Statistics = new OwlRlMaintenanceStatistics(0, 0, 0, 0, 0, 0, 0, 0, rebuildMode);

        //A rebuild swaps the context wholesale and records no deltas; the
        //consumer diffs the served target against its prior overlay instead.
        HasRecordedDeltas = false;

        return rebuiltResult;
    }

    /// <summary>Marks a single deletion of <paramref name="frontierFact"/> and returns the facts it overdeletes — the sandboxed face-2 completeness probe; the closure is unchanged.</summary>
    /// <param name="frontierFact">The fact whose deletion is marked.</param>
    /// <returns>The marked facts, the frontier fact included.</returns>
    internal HashSet<EncodedTriple> ComputeOverdeleteMarking(EncodedTriple frontierFact)
    {
        return Context.ComputeOverdeleteMarkingCore(frontierFact);
    }

    /// <summary>Whether the named producer rule concludes <paramref name="fact"/> against the current closure — the face-3 completeness probe.</summary>
    /// <param name="rule">The rule name to test.</param>
    /// <param name="fact">The candidate fact.</param>
    /// <returns><c>true</c> when the named rule's entry confirms <paramref name="fact"/>.</returns>
    internal bool CheckRederiveEntry(string rule, EncodedTriple fact)
    {
        return Context.CheckRederiveEntry(rule, fact);
    }

    /// <summary>
    /// Builds a fresh closure context over a base by the from-scratch
    /// semi-naive materialization. The maintained closure always runs the
    /// normative rule set over the shared axiomatic table — the
    /// comprehension completion family is an entailment-path mode, the
    /// metaclass-merge vocabulary is a per-semantics mode, and neither is
    /// ever enabled here, which is what keeps the overdelete and rederive
    /// surfaces free of entries for them.
    /// </summary>
    /// <param name="baseTriples">The base triples.</param>
    /// <param name="cancellationToken">A token that aborts the build between rounds.</param>
    /// <returns>The context and its result.</returns>
    private (OwlRlClosure.ClosureContext Context, OwlRlResult Result) Build(IEnumerable<EncodedTriple> baseTriples, CancellationToken cancellationToken)
    {
        OwlRlClosure.ClosureContext context = new(
            baseTriples,
            Terms,
            DatatypeOracle,
            null,
            null,
            default,
            recordDeltas: true,
            maintainBase: true);

        context.BuildIndexes();

        bool first = true;
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(first)
            {
                context.FireRules();
                first = false;
            }
            else
            {
                context.FireRulesDelta();
            }

            if(context.InconsistencyRule is not null || !context.MergePending())
            {
                break;
            }
        }

        OwlRlResult result = new(context.Derived, context.InconsistencyRule is null, context.InconsistencyRule, context.InconsistencyPremises, context.MalformedShapeSnapshot());

        return (context, result);
    }
}
