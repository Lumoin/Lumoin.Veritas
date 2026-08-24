using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The result of one maintained commit: the served-store delta the caller
/// applies to the served (base ∪ derived) store, the folded consistency verdict,
/// the floor and decision facts for provenance and refusal, and the maintenance
/// counters. Every collection is an IMMUTABLE copy sized to the facts the commit
/// touched — never a live view into the closure's per-<c>Apply</c>-cleared
/// recorded sets — so a later commit cannot clobber a delta still being
/// consumed.
/// </summary>
/// <remarks>
/// The served-delta discriminant is <see cref="OverlayOn"/> — the RL closure's
/// own consistency — never the maintenance <see cref="ReasoningMaintenanceStatistics.Mode"/>
/// (a falsity-INTRODUCING commit runs the incremental pipeline yet withdraws the
/// overlay). On EVERY commit class the applied served delta equals
/// <c>setdiff(new served target, previous served store)</c> with entered and left
/// disjoint. <see cref="IsConsistent"/> is the verdict folded with any delegated
/// beyond-RL decision; it can differ from <see cref="OverlayOn"/> when the RL
/// closure is consistent but a wired delegate condemns the beyond-RL module.
/// </remarks>
public readonly record struct ReasoningMaintainedCommit
{
    /// <summary>The triples to add to the served store — an immutable copy of the commit's served additions.</summary>
    public IReadOnlyCollection<EncodedTriple> ServedAdditions { get; init; }

    /// <summary>The triples to remove from the served store — an immutable copy of the commit's served removals.</summary>
    public IReadOnlyCollection<EncodedTriple> ServedRemovals { get; init; }

    /// <summary>Whether the derived overlay is on (the RL closure stayed consistent) rather than withdrawn (served asserted-only). The served-delta discriminant.</summary>
    public bool OverlayOn { get; init; }

    /// <summary>Whether the ontology is consistent — the RL verdict folded with any delegated beyond-RL decision. Governs provenance and the refusal veto; scoped to the RL fragment when <see cref="IsDecisive"/> is <see langword="false"/>.</summary>
    public bool IsConsistent { get; init; }

    /// <summary>The RL falsity rule that fired, or <c>null</c> when the RL closure is consistent.</summary>
    public string? InconsistencyRule { get; init; }

    /// <summary>The profile floor the post-op asserted base was detected at.</summary>
    public OwlProfiles DetectedProfiles { get; init; }

    /// <summary>The number of axioms in the beyond-RL module, or zero when the content is within RL.</summary>
    public int ModuleAxiomCount { get; init; }

    /// <summary>The named constructs a fragment-relative delegated verdict left undecided; empty when the verdict covers the module whole, the content is within RL, or no delegate is wired. Inherited unchanged on a non-re-decided commit.</summary>
    public IReadOnlyList<string> UndecidedConstructs { get; init; }

    /// <summary>How a beyond-RL decision ended, or <c>null</c> when none ran. Inherited unchanged on a non-re-decided commit while a beyond-RL module stands.</summary>
    public ReasoningDecisionOutcome? DecisionOutcome { get; init; }

    /// <summary>The work a beyond-RL decision spent, or <c>null</c> when none ran.</summary>
    public ReasoningDecisionStatistics? DecisionStatistics { get; init; }

    /// <summary>The reasoning strategy the commit resolved to. Inherited unchanged on a non-re-decided commit while a beyond-RL module stands.</summary>
    public ReasoningStrategy Strategy { get; init; }

    /// <summary>The expressiveness rung that selected the strategy. Inherited unchanged on a non-re-decided commit while a beyond-RL module stands.</summary>
    public ReasoningSelectionReason Reason { get; init; }

    /// <summary>Whether the consistency claim covers the module whole. False for a fragment-relative delegated verdict, an abstention, an undelegated beyond-RL module, and every decayed (non-re-decided) beyond-RL generation — a delegated whole-module verdict decays to fragment-relative the moment the base moves past the decided generation.</summary>
    public bool IsDecisive { get; init; }

    /// <summary>The beyond-RL module's verdict when a delegate decided it this commit, or the inherited verdict on a decayed generation, or <c>null</c>.</summary>
    public ModuleVerdict? ModuleVerdict { get; init; }

    /// <summary>The maintenance counters and mode of the commit's <c>Apply</c>.</summary>
    public ReasoningMaintenanceStatistics Statistics { get; init; }

    /// <summary>The raw number of derived triples in the closure — the served closure's derived-set size regardless of the overlay flag. A consumer reports it as the served derived count only when <see cref="OverlayOn"/> is <see langword="true"/>, and zero otherwise (matching what is served on a withdrawn overlay).</summary>
    public int DerivedCount { get; init; }

    /// <summary>Whether the commit ran a from-scratch build rather than the incremental pipeline — the inconsistency, discard-recovery, and wholesale-replace lanes; remat-class cost.</summary>
    public bool RebuildClass { get; init; }
}

/// <summary>
/// The durable per-engine maintenance object of the reasoned mutable engine: it
/// owns one <see cref="OwlRlMaintainedClosure"/> kept in lockstep with the
/// asserted base, the derived overlay snapshot the served store is evolved from,
/// and a <see cref="ReasoningRendezvous"/> whose floor and classification caches
/// it keeps in step with each commit. A single caller drives one instance and
/// the type is NOT thread-safe — the Sparql-layer maintenance mutex owns
/// serialization, so every method here assumes the caller holds it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Commit protocol.</b> The caller invokes <see cref="MaintainCommit"/>
/// pre-append (it runs the closure <c>Apply</c>, the rendezvous
/// <see cref="ReasoningRendezvous.Advance"/>, floor re-detection and any
/// synchronous beyond-RL re-delegation, and computes the served delta), then —
/// after it learns whether the commit linearised — calls
/// <see cref="OnCommitOutcome"/> with the outcome. The closure's atomic base
/// edit stands even when a commit fails to land, so the single-seam predicate is
/// "the delegate was INVOKED and the commit did not land ⇒ discard": on
/// <c>landed=false</c> the instance invalidates and the next
/// <see cref="MaintainCommit"/> rebuilds from the caller's committed base; on
/// <c>landed=true</c> the pending overlay, floor, decision, and trace state roll
/// FORWARD to become current. Every commit-internal state advance is held
/// pending until then; on a non-landing commit it is discarded with the
/// invalidated instance, never observed.
/// </para>
/// <para>
/// <b>The tentative asserted base is caller-supplied.</b> Each commit passes the
/// post-op asserted store — the store the dataset layer already built from the
/// commit's transitions (the system-of-record store; entailments never live
/// there). It serves three roles at O(store) cost only where the spec pays it:
/// the rendezvous generation marker on every commit (decoded on none), the
/// floor re-detection source on a schema-touching commit, and the from-scratch
/// rebuild base on a discard-recovery or wholesale-replace commit. Holding the
/// asserted base here would duplicate what the dataset owns, so the maintenance
/// object holds only the derived overlay.
/// </para>
/// <para>
/// <b>Trace.</b> <see cref="MaintainCommit"/> emits nothing; a commit's
/// <see cref="DatalogMaintenanceTraceEvent"/> and any re-delegation
/// <see cref="ReasoningDecisionTraceEvent"/> are staged and emitted only from
/// <see cref="OnCommitOutcome"/> on <c>landed=true</c>, so observability never
/// reports a phantom commit. Open-time construction emits its
/// <see cref="ReasoningTraceEvent"/> and decision event immediately, exactly as
/// <see cref="ReasoningRendezvous.MaterializeAsync"/> does.
/// </para>
/// </remarks>
public sealed class ReasoningMaintenance
{
    /// <summary>The term dictionary every store and delta is encoded with.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>The resolved RL vocabulary.</summary>
    private OwlRlTerms Terms { get; }

    /// <summary>The datatype oracle for the <c>dt-*</c> falsities.</summary>
    private OwlRlDatatypeOracle DatatypeOracle { get; }

    /// <summary>The selection policy.</summary>
    private ReasoningPolicy Policy { get; }

    /// <summary>The external description-logic seam, or <c>null</c>.</summary>
    private DescriptionLogicDelegate? DescriptionLogic { get; }

    /// <summary>The owned rendezvous whose floor and classification caches this object keeps in step.</summary>
    private ReasoningRendezvous Rendezvous { get; }

    /// <summary>The optional per-commit maintenance trace handler; emitted only after a commit lands.</summary>
    private TraceHandler<DatalogMaintenanceTraceEvent>? MaintenanceTraceHandler { get; }

    /// <summary>The optional strategy-selection trace handler, mirroring <see cref="ReasoningRendezvous.MaterializeAsync"/>; used at open.</summary>
    private TraceHandler<ReasoningTraceEvent>? ReasoningTraceHandler { get; }

    /// <summary>The optional beyond-RL decision trace handler; emitted at open, and after a re-delegating commit lands.</summary>
    private TraceHandler<ReasoningDecisionTraceEvent>? DecisionTraceHandler { get; }

    /// <summary>The clock for trace timestamps and cost measurement; non-<c>null</c> whenever any handler is wired.</summary>
    private TimeProvider? TimeProvider { get; }

    /// <summary>The correlation id stamped on emitted trace events.</summary>
    private Guid CorrelationId { get; }

    /// <summary>Sequence-number counter for trace events; a field because <see cref="Interlocked"/> requires a ref parameter.</summary>
    private long traceSequence;

    /// <summary>The adopted closure, kept in lockstep with the asserted base; replaced wholesale by a discard-recovery or wholesale-replace rebuild.</summary>
    private OwlRlMaintainedClosure Closure { get; set; }

    /// <summary>The owned derived overlay snapshot — the last-landed derived set the served store is evolved from; empty while the overlay is withdrawn. Mutated in place only when a commit lands.</summary>
    private HashSet<EncodedTriple> OverlaySnapshot { get; set; }

    /// <summary>The last-landed generation's floor and decision facts — the provenance source and the decay-rule inheritance source.</summary>
    private GenerationFacts Current { get; set; }

    /// <summary>Whether the instance is invalidated (the delegate was invoked but the commit did not land); the next commit rebuilds from the caller's committed base.</summary>
    private bool Invalidated { get; set; }

    /// <summary>The commit staged by the last <see cref="MaintainCommit"/> and not yet resolved, or <c>null</c>.</summary>
    private PendingCommit? Pending { get; set; }

    /// <summary>The initial state the caller reads at open: the served-store seed (the derived snapshot as additions) and the folded verdict with floor and decision facts for the first provenance and refusal decision.</summary>
    public ReasoningMaintainedCommit InitialState { get; private set; }

    /// <summary>Constructs the maintenance object's immutable configuration; <see cref="InitializeAsync"/> completes the open-time build.</summary>
    /// <param name="dictionary">The term dictionary every store and delta encodes with.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the <c>dt-*</c> falsities.</param>
    /// <param name="policy">The selection policy.</param>
    /// <param name="descriptionLogic">The external description-logic seam, or <c>null</c>.</param>
    /// <param name="rendezvous">The owned rendezvous.</param>
    /// <param name="maintenanceTraceHandler">The per-commit maintenance trace handler, or <c>null</c>.</param>
    /// <param name="reasoningTraceHandler">The strategy-selection trace handler, or <c>null</c>.</param>
    /// <param name="decisionTraceHandler">The beyond-RL decision trace handler, or <c>null</c>.</param>
    /// <param name="timeProvider">The clock for trace timestamps, or <c>null</c> when no handler is wired.</param>
    /// <param name="correlationId">The correlation id stamped on emitted trace events.</param>
    private ReasoningMaintenance(
        TermDictionary dictionary,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle,
        ReasoningPolicy policy,
        DescriptionLogicDelegate? descriptionLogic,
        ReasoningRendezvous rendezvous,
        TraceHandler<DatalogMaintenanceTraceEvent>? maintenanceTraceHandler,
        TraceHandler<ReasoningTraceEvent>? reasoningTraceHandler,
        TraceHandler<ReasoningDecisionTraceEvent>? decisionTraceHandler,
        TimeProvider? timeProvider,
        Guid correlationId)
    {
        Dictionary = dictionary;
        Terms = terms;
        DatatypeOracle = datatypeOracle;
        Policy = policy;
        DescriptionLogic = descriptionLogic;
        Rendezvous = rendezvous;
        MaintenanceTraceHandler = maintenanceTraceHandler;
        ReasoningTraceHandler = reasoningTraceHandler;
        DecisionTraceHandler = decisionTraceHandler;
        TimeProvider = timeProvider;
        CorrelationId = correlationId;
        Closure = null!;
        OverlaySnapshot = [];
    }

    /// <summary>
    /// Builds the maintenance object over an initial asserted base: floor-detects
    /// through an internally-owned <see cref="ReasoningRendezvous"/>, builds the
    /// maintained closure (one remat), runs open-time beyond-RL delegation exactly
    /// as <see cref="ReasoningRendezvous.MaterializeAsync"/> does, and establishes
    /// the overlay snapshot and the initial folded verdict. RDFS-shaped input
    /// routes through the RL maintained closure — there is no incremental RDFS
    /// pass, and the RL closure adds the consistency detection the RDFS arm lacks.
    /// </summary>
    /// <param name="initialBase">The initial asserted base triples, schema statements included.</param>
    /// <param name="dictionary">The term dictionary the base and every later delta encodes with.</param>
    /// <param name="policy">The selection policy.</param>
    /// <param name="descriptionLogic">The external description-logic seam, or <c>null</c> — beyond-RL modules are then reported, never silently dropped.</param>
    /// <param name="maintenanceTraceHandler">Optional handler receiving each landed commit's <see cref="DatalogMaintenanceTraceEvent"/>.</param>
    /// <param name="reasoningTraceHandler">Optional handler receiving the open-time strategy-selection <see cref="ReasoningTraceEvent"/>.</param>
    /// <param name="decisionTraceHandler">Optional handler receiving a delegated decision's <see cref="ReasoningDecisionTraceEvent"/> at open and after a re-delegating commit lands.</param>
    /// <param name="timeProvider">Clock for trace timestamps and cost measurement. Required when any handler is non-<c>null</c>.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts the build, delegation, and the store construction.</param>
    /// <returns>The initialised maintenance object; <see cref="InitialState"/> carries the served-store seed and the open verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="initialBase"/> or <paramref name="dictionary"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<ReasoningMaintenance> CreateAsync(
        IEnumerable<EncodedTriple> initialBase,
        TermDictionary dictionary,
        ReasoningPolicy policy,
        DescriptionLogicDelegate? descriptionLogic = null,
        TraceHandler<DatalogMaintenanceTraceEvent>? maintenanceTraceHandler = null,
        TraceHandler<ReasoningTraceEvent>? reasoningTraceHandler = null,
        TraceHandler<ReasoningDecisionTraceEvent>? decisionTraceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialBase);
        ArgumentNullException.ThrowIfNull(dictionary);

        if((maintenanceTraceHandler is not null || reasoningTraceHandler is not null || decisionTraceHandler is not null) && timeProvider is null)
        {
            throw new ArgumentException("A time provider must be supplied when a trace handler is configured.", nameof(timeProvider));
        }

        ReasoningMaintenance instance = new(
            dictionary,
            new OwlRlTerms(dictionary),
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            policy,
            descriptionLogic,
            new ReasoningRendezvous(policy, descriptionLogic),
            maintenanceTraceHandler,
            reasoningTraceHandler,
            decisionTraceHandler,
            timeProvider,
            correlationId);

        await instance.InitializeAsync([.. initialBase], cancellationToken).ConfigureAwait(false);

        return instance;
    }

    /// <summary>
    /// Maintains the closure and served store over one commit's asserted base
    /// delta, returning the served-store delta and the folded provenance. The
    /// per-commit path is synchronous CPU work on the caller's thread; the one
    /// await is a schema-touching commit's beyond-RL re-delegation, and a cancel
    /// during it fails the commit pre-append. The result's state is staged: the
    /// caller applies the returned served delta, then reports the outcome through
    /// <see cref="OnCommitOutcome"/>.
    /// </summary>
    /// <param name="baseAdded">The triples the commit added to the asserted base — the true sequential net (disjoint from <paramref name="baseRemoved"/> and from the prior base).</param>
    /// <param name="baseRemoved">The triples the commit removed from the asserted base — the true sequential net (a subset of the prior base).</param>
    /// <param name="tentativeAssertedStore">The post-op asserted store: the rendezvous generation marker, the floor re-detection source on a schema-touching commit, and the rebuild base on a discard-recovery or wholesale-replace commit.</param>
    /// <param name="wholesaleReplace">Whether the caller detected a wholesale default-graph replacement (a net retract at least the current asserted default-graph size), which rebuilds from the tentative base instead of feeding a degenerate <c>Apply</c>.</param>
    /// <param name="cancellationToken">A token that aborts the <c>Apply</c> and any re-delegation, failing the commit pre-append.</param>
    /// <returns>The served-store delta and the folded verdict with floor and decision facts.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>; the caller must report the commit as not landed.</exception>
    public async ValueTask<ReasoningMaintainedCommit> MaintainCommit(
        IReadOnlyCollection<EncodedTriple> baseAdded,
        IReadOnlyCollection<EncodedTriple> baseRemoved,
        HypertrieGraphStore tentativeAssertedStore,
        bool wholesaleReplace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseAdded);
        ArgumentNullException.ThrowIfNull(baseRemoved);
        ArgumentNullException.ThrowIfNull(tentativeAssertedStore);

        long startTimestamp = TimeProvider?.GetTimestamp() ?? 0;

        //Run the closure. A valid, non-replacing commit feeds the incremental
        //pipeline; a discard-recovery or wholesale-replace commit builds a fresh
        //closure from the caller's committed base — never the closure's own
        //diverged rebuild.
        bool wiringRebuild = Invalidated || wholesaleReplace;
        OwlRlResult closureResult;
        ReasoningMaintenanceMode reportedMode;
        if(wiringRebuild)
        {
            Closure = new OwlRlMaintainedClosure(
                tentativeAssertedStore.Match(TermId.None, TermId.None, TermId.None),
                Terms,
                DatatypeOracle,
                cancellationToken);
            closureResult = Closure.Current;
            reportedMode = ReasoningMaintenanceMode.RebuildRequested;
            Invalidated = false;
        }
        else
        {
            closureResult = Closure.Apply(baseAdded, baseRemoved, cancellationToken);
            reportedMode = MapMode(Closure.Statistics.Mode);
        }

        //Keep the rendezvous caches in step and detect this generation's floor.
        //An assertion-only commit carries the floor; anything schema-touching
        //re-detects. A wiring rebuild always re-decides (the decided generation
        //was discarded), so its floor is refreshed regardless of the carry.
        Rendezvous.Advance(tentativeAssertedStore, baseAdded, baseRemoved, Dictionary);
        ReasoningFloor? carried = wiringRebuild ? null : Rendezvous.FloorFor(tentativeAssertedStore);
        bool schemaTouching = carried is null;
        ReasoningFloor floor = carried ?? Rendezvous.DetectFloor(tentativeAssertedStore, Dictionary);

        (DecisionBundle bundle, ReasoningDecisionTraceEvent? decisionEvent) =
            await DecideAsync(floor, schemaTouching, closureResult, cancellationToken).ConfigureAwait(false);

        bool overlayOn = closureResult.IsConsistent;
        int derivedCount = closureResult.Derived.Count;

        (EncodedTriple[] servedAdded, EncodedTriple[] servedRemoved, OverlayUpdate overlayUpdate) =
            ComputeServedDelta(closureResult, overlayOn, wiringRebuild, tentativeAssertedStore, baseAdded, baseRemoved);

        ReasoningMaintenanceStatistics statistics = MapStatistics(Closure.Statistics, reportedMode);
        bool rebuildClass = reportedMode != ReasoningMaintenanceMode.Incremental;

        GenerationFacts newFacts = new(floor, bundle, overlayOn, derivedCount);

        DatalogMaintenanceTraceEvent? maintenanceEvent = null;
        if(MaintenanceTraceHandler is not null)
        {
            double elapsedMilliseconds = TimeProvider!.GetElapsedTime(startTimestamp).TotalMilliseconds;
            maintenanceEvent = DatalogMaintenanceTraceEvent.From(
                Interlocked.Increment(ref traceSequence),
                TimeProvider.GetUtcNow().UtcTicks,
                CorrelationId,
                baseAdded.Count,
                baseRemoved.Count,
                servedAdded.Length,
                servedRemoved.Length,
                statistics,
                overlayOn,
                rebuildClass,
                elapsedMilliseconds);
        }

        Pending = new PendingCommit(newFacts, overlayUpdate, maintenanceEvent, decisionEvent);

        return new ReasoningMaintainedCommit
        {
            ServedAdditions = servedAdded,
            ServedRemovals = servedRemoved,
            OverlayOn = overlayOn,
            IsConsistent = bundle.FoldedConsistent,
            InconsistencyRule = bundle.InconsistencyRule,
            DetectedProfiles = floor.Memberships,
            ModuleAxiomCount = bundle.Module?.Axioms.Count ?? 0,
            UndecidedConstructs = bundle.UndecidedConstructs,
            DecisionOutcome = bundle.DecisionOutcome,
            DecisionStatistics = bundle.DecisionStatistics,
            Strategy = bundle.Strategy,
            Reason = bundle.Reason,
            IsDecisive = bundle.IsDecisive,
            ModuleVerdict = bundle.Verdict,
            Statistics = statistics,
            DerivedCount = derivedCount,
            RebuildClass = rebuildClass,
        };
    }

    /// <summary>
    /// Reports the outcome of the commit the last <see cref="MaintainCommit"/>
    /// staged — the single outcome seam, one notification per delegate
    /// invocation. On <c>landed=true</c> the staged overlay, floor, decision, and
    /// trace state roll forward to become current, and the commit's trace events
    /// are emitted (only now, so a phantom commit is never reported). On
    /// <c>landed=false</c> — the delegate was invoked but the commit did not land
    /// — the instance is invalidated: everything the commit advanced is discarded,
    /// and the next <see cref="MaintainCommit"/> rebuilds from the caller's
    /// committed base.
    /// </summary>
    /// <param name="landed">Whether the commit linearised (published).</param>
    public void OnCommitOutcome(bool landed)
    {
        if(!landed)
        {
            Invalidate();

            return;
        }

        if(Pending is { } pending)
        {
            OverlayUpdate update = pending.OverlayUpdate;
            if(update.Replacement is not null)
            {
                OverlaySnapshot = update.Replacement;
            }
            else
            {
                foreach(EncodedTriple triple in update.Left)
                {
                    OverlaySnapshot.Remove(triple);
                }

                foreach(EncodedTriple triple in update.Entered)
                {
                    OverlaySnapshot.Add(triple);
                }
            }

            Current = pending.Facts;

            if(DecisionTraceHandler is not null && pending.DecisionEvent is { } decisionEvent)
            {
                DecisionTraceHandler(in decisionEvent);
            }

            if(MaintenanceTraceHandler is not null && pending.MaintenanceEvent is { } maintenanceEvent)
            {
                MaintenanceTraceHandler(in maintenanceEvent);
            }

            Pending = null;
        }
    }

    /// <summary>
    /// Discards the instance per the divergence rule: the closure's atomic base
    /// edit stands even when a commit fails to land, so an invoked-but-not-landed
    /// commit leaves the closure ahead of the committed base. The next
    /// <see cref="MaintainCommit"/> rebuilds from the caller's committed base
    /// rather than trusting the diverged (or poisoned) closure. The discard covers
    /// the closure AND the owned rendezvous's per-generation caches
    /// (<see cref="ReasoningRendezvous.ResetGenerationCaches"/>): a floor or
    /// classification detected against the never-landed generation must not
    /// survive to be re-keyed onto the next commit's store, or the rebuild would
    /// decide on a phantom module rather than re-detecting over the committed base.
    /// </summary>
    public void Invalidate()
    {
        Invalidated = true;
        Pending = null;
        Rendezvous.ResetGenerationCaches();
    }

    /// <summary>
    /// Completes the open-time build on the constructed instance: builds the
    /// floor-detection store and the maintained closure over the base, runs the
    /// open-time decision, seeds the overlay, and records
    /// <see cref="InitialState"/>. The floor-detection store is a throwaway — the
    /// dataset layer owns the served asserted store; this one exists only to feed
    /// the rendezvous the base to detect over.
    /// </summary>
    /// <param name="baseTriples">The materialised initial base.</param>
    /// <param name="cancellationToken">A token that aborts the build and delegation.</param>
    /// <returns>A task that completes when the open-time build is done.</returns>
    private async ValueTask InitializeAsync(List<EncodedTriple> baseTriples, CancellationToken cancellationToken)
    {
        long startTimestamp = TimeProvider?.GetTimestamp() ?? 0;

        HypertrieGraphStore detectionStore = await HypertrieGraphStore
            .BuildAsync(baseTriples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);

        ReasoningFloor floor = Rendezvous.DetectFloor(detectionStore, Dictionary);

        Closure = new OwlRlMaintainedClosure(baseTriples, Terms, DatatypeOracle, cancellationToken);
        OwlRlResult closureResult = Closure.Current;

        (DecisionBundle bundle, ReasoningDecisionTraceEvent? decisionEvent) =
            await DecideAsync(floor, schemaTouching: true, closureResult, cancellationToken).ConfigureAwait(false);

        bool overlayOn = closureResult.IsConsistent;
        int derivedCount = closureResult.Derived.Count;

        OverlaySnapshot = overlayOn ? [.. closureResult.Derived] : [];
        Current = new GenerationFacts(floor, bundle, overlayOn, derivedCount);

        if(ReasoningTraceHandler is not null)
        {
            double elapsedMilliseconds = TimeProvider!.GetElapsedTime(startTimestamp).TotalMilliseconds;
            ReasoningTraceEvent traceEvent = new(
                Interlocked.Increment(ref traceSequence),
                TimeProvider.GetUtcNow().UtcTicks,
                CorrelationId,
                bundle.Strategy,
                bundle.Reason,
                bundle.Module?.Axioms.Count ?? 0,
                overlayOn ? derivedCount : 0,
                elapsedMilliseconds);

            ReasoningTraceHandler(in traceEvent);
        }

        if(DecisionTraceHandler is not null && decisionEvent is { } openDecisionEvent)
        {
            DecisionTraceHandler(in openDecisionEvent);
        }

        InitialState = new ReasoningMaintainedCommit
        {
            ServedAdditions = overlayOn ? [.. closureResult.Derived] : [],
            ServedRemovals = [],
            OverlayOn = overlayOn,
            IsConsistent = bundle.FoldedConsistent,
            InconsistencyRule = bundle.InconsistencyRule,
            DetectedProfiles = floor.Memberships,
            ModuleAxiomCount = bundle.Module?.Axioms.Count ?? 0,
            UndecidedConstructs = bundle.UndecidedConstructs,
            DecisionOutcome = bundle.DecisionOutcome,
            DecisionStatistics = bundle.DecisionStatistics,
            Strategy = bundle.Strategy,
            Reason = bundle.Reason,
            IsDecisive = bundle.IsDecisive,
            ModuleVerdict = bundle.Verdict,
            Statistics = new ReasoningMaintenanceStatistics(0, 0, 0, 0, 0, 0, 0, 0, ReasoningMaintenanceMode.RebuildRequested),
            DerivedCount = derivedCount,
            RebuildClass = true,
        };
    }

    /// <summary>
    /// Resolves the beyond-RL decision for one generation, mirroring
    /// <see cref="ReasoningRendezvous.MaterializeAsync"/>'s fold: within RL the
    /// maintained closure is the decision procedure; a schema-touching beyond-RL
    /// commit re-delegates synchronously (or reports the module when no delegate
    /// is wired); a non-re-decided beyond-RL commit inherits the last-landed
    /// decision and decays its consistency claim to fragment-relative — a
    /// delegated whole-module verdict applies only to the generation it decided.
    /// </summary>
    /// <param name="floor">The generation's detected floor.</param>
    /// <param name="schemaTouching">Whether the commit touched schema vocabulary (re-decide) or carried the floor (decay).</param>
    /// <param name="closureResult">The RL closure result whose consistency is the fragment-relative claim.</param>
    /// <param name="cancellationToken">A token that aborts a re-delegation, failing the commit pre-append.</param>
    /// <returns>The decision bundle and the staged decision trace event, or <c>null</c> when none ran.</returns>
    private async ValueTask<(DecisionBundle Bundle, ReasoningDecisionTraceEvent? DecisionEvent)> DecideAsync(
        ReasoningFloor floor,
        bool schemaTouching,
        OwlRlResult closureResult,
        CancellationToken cancellationToken)
    {
        bool rlConsistent = closureResult.IsConsistent;
        string? inconsistencyRule = closureResult.InconsistencyRule;

        if(floor.IsWithinRl)
        {
            DecisionBundle within = new(
                Module: null,
                Verdict: null,
                DecisionOutcome: null,
                DecisionStatistics: null,
                UndecidedConstructs: [],
                Strategy: ReasoningStrategy.Rl,
                Reason: ReasoningSelectionReason.RlSufficient,
                IsDecisive: true,
                FoldedConsistent: rlConsistent,
                InconsistencyRule: inconsistencyRule);

            return (within, null);
        }

        ReasoningModule module = floor.Module!;

        if(!schemaTouching)
        {
            //Decay: a beyond-RL module stands and this commit did not re-decide.
            //Inherit the last-landed decision facts unchanged and scope the
            //consistency claim to the RL fragment.
            DecisionBundle prior = Current.Decision;
            DecisionBundle decayed = prior with
            {
                IsDecisive = false,
                FoldedConsistent = rlConsistent,
                InconsistencyRule = inconsistencyRule,
            };

            return (decayed, null);
        }

        if(DescriptionLogic is not null && Policy.DelegateBeyondRl)
        {
            long decisionStartTimestamp = TimeProvider?.GetTimestamp() ?? 0;
            ModuleDecision decision = await DescriptionLogic(module, cancellationToken).ConfigureAwait(false);

            //A budget abstention leaves no verdict to fold: the claim keeps the
            //in-engine consistency and the outcome records that the module went
            //undecided.
            bool folded = rlConsistent && (decision.Verdict?.IsConsistent ?? true);
            IReadOnlyList<string> undecided = decision.Verdict is { IsConsistent: true, UnsupportedConstructs.Count: > 0 }
                ? decision.Verdict.UnsupportedConstructs
                : [];

            ReasoningDecisionTraceEvent? decisionEvent = null;
            if(DecisionTraceHandler is not null)
            {
                double decisionElapsed = TimeProvider!.GetElapsedTime(decisionStartTimestamp).TotalMilliseconds;
                decisionEvent = ReasoningDecisionTraceEvent.From(
                    Interlocked.Increment(ref traceSequence),
                    TimeProvider.GetUtcNow().UtcTicks,
                    CorrelationId,
                    decision.Outcome,
                    decision.Statistics,
                    decisionElapsed);
            }

            DecisionBundle delegated = new(
                Module: module,
                Verdict: decision.Verdict,
                DecisionOutcome: decision.Outcome,
                DecisionStatistics: decision.Statistics,
                UndecidedConstructs: undecided,
                Strategy: ReasoningStrategy.DescriptionLogicDelegate,
                Reason: ReasoningSelectionReason.BeyondRlDelegated,
                IsDecisive: decision.Verdict?.IsDecisive ?? false,
                FoldedConsistent: folded,
                InconsistencyRule: inconsistencyRule);

            return (delegated, decisionEvent);
        }

        DecisionBundle reported = new(
            Module: module,
            Verdict: null,
            DecisionOutcome: null,
            DecisionStatistics: null,
            UndecidedConstructs: [],
            Strategy: ReasoningStrategy.Rl,
            Reason: ReasoningSelectionReason.BeyondRlReported,
            IsDecisive: false,
            FoldedConsistent: rlConsistent,
            InconsistencyRule: inconsistencyRule);

        return (reported, null);
    }

    /// <summary>
    /// Computes the immutable served-store delta and the overlay-snapshot update
    /// for one commit. An overlay-preserving incremental commit reads the
    /// closure's recorded <see cref="OwlRlMaintainedClosure.AllDelta"/> at
    /// O(touched) cost; every other class — a withdrawal flip, a consistency
    /// return, a landed rebuild — composes the served target from the asserted
    /// store and the post-op derived set and applies the universal invariant
    /// directly: the served delta equals <c>setdiff(new served target, previous
    /// served store)</c>, which resolves both overlap shapes (a prior-overlay fact
    /// this commit asserts, and a base-removed fact that stays derivable) without
    /// re-deriving them.
    /// </summary>
    /// <param name="closureResult">The closure result whose derived set is the served target's overlay part.</param>
    /// <param name="overlayOn">Whether the RL closure stayed consistent (the overlay is on).</param>
    /// <param name="wiringRebuild">Whether this commit rebuilt from the caller's committed base (never an <c>AllDelta</c> fast path).</param>
    /// <param name="tentativeAssertedStore">The post-op asserted store — the composed path's asserted target and previous-asserted reconstruction source.</param>
    /// <param name="baseAdded">The commit's base additions.</param>
    /// <param name="baseRemoved">The commit's base removals.</param>
    /// <returns>The served additions, the served removals, and the overlay-snapshot update to apply on landing.</returns>
    private (EncodedTriple[] ServedAdded, EncodedTriple[] ServedRemoved, OverlayUpdate Update) ComputeServedDelta(
        OwlRlResult closureResult,
        bool overlayOn,
        bool wiringRebuild,
        HypertrieGraphStore tentativeAssertedStore,
        IReadOnlyCollection<EncodedTriple> baseAdded,
        IReadOnlyCollection<EncodedTriple> baseRemoved)
    {
        bool fastPath = !wiringRebuild && Closure.HasRecordedDeltas && Current.OverlayOn && overlayOn;
        if(fastPath)
        {
            EncodedTriple[] servedAdded = [.. Closure.AllDelta.Entered];
            EncodedTriple[] servedRemoved = [.. Closure.AllDelta.Left];
            OverlayUpdate incremental = new(Replacement: null, [.. Closure.DerivedDelta.Entered], [.. Closure.DerivedDelta.Left]);

            return (servedAdded, servedRemoved, incremental);
        }

        HashSet<EncodedTriple> newAsserted = [.. tentativeAssertedStore.Match(TermId.None, TermId.None, TermId.None)];

        HashSet<EncodedTriple> newServedTarget = [.. newAsserted];
        if(overlayOn)
        {
            newServedTarget.UnionWith(closureResult.Derived);
        }

        HashSet<EncodedTriple> previousServed = ReconstructPreviousServed(newAsserted, baseAdded, baseRemoved);

        HashSet<EncodedTriple> added = [.. newServedTarget];
        added.ExceptWith(previousServed);
        HashSet<EncodedTriple> removed = [.. previousServed];
        removed.ExceptWith(newServedTarget);

        OverlayUpdate replacement = new(overlayOn ? [.. closureResult.Derived] : [], [], []);

        return ([.. added], [.. removed], replacement);
    }

    /// <summary>
    /// Reconstructs the previous served store — previous asserted ∪ the owned
    /// prior overlay — from the post-op asserted store and the net base delta. The
    /// net delta is disjoint (added not in the prior base, removed a subset of
    /// it), so undoing it recovers the previous asserted base; the owned overlay
    /// snapshot supplies the derived part (empty while withdrawn). This is the
    /// composed path's previous-generation term of the setdiff invariant, at
    /// O(store) cost that the rebuild-class commit already pays.
    /// </summary>
    /// <param name="newAsserted">The post-op asserted store's triples.</param>
    /// <param name="baseAdded">The commit's base additions.</param>
    /// <param name="baseRemoved">The commit's base removals.</param>
    /// <returns>The previous served store's triples.</returns>
    private HashSet<EncodedTriple> ReconstructPreviousServed(
        HashSet<EncodedTriple> newAsserted,
        IReadOnlyCollection<EncodedTriple> baseAdded,
        IReadOnlyCollection<EncodedTriple> baseRemoved)
    {
        HashSet<EncodedTriple> previousServed = [.. newAsserted];
        HashSet<EncodedTriple> addedSet = [.. baseAdded];
        previousServed.ExceptWith(addedSet);
        foreach(EncodedTriple triple in baseRemoved)
        {
            if(!addedSet.Contains(triple))
            {
                previousServed.Add(triple);
            }
        }

        previousServed.UnionWith(OverlaySnapshot);

        return previousServed;
    }

    /// <summary>Maps the closure's internal maintenance mode onto the public mode; a wiring rebuild reports its own mode and does not read this.</summary>
    /// <param name="mode">The closure's internal maintenance mode.</param>
    /// <returns>The public maintenance mode.</returns>
    private static ReasoningMaintenanceMode MapMode(OwlRlMaintenanceMode mode)
    {
        return mode switch
        {
            OwlRlMaintenanceMode.Incremental => ReasoningMaintenanceMode.Incremental,
            OwlRlMaintenanceMode.RebuildInconsistent => ReasoningMaintenanceMode.RebuildInconsistent,
            OwlRlMaintenanceMode.RebuildPoisoned => ReasoningMaintenanceMode.RebuildPoisoned,
            _ => ReasoningMaintenanceMode.Incremental
        };
    }

    /// <summary>Lifts the closure's internal statistics onto the public surface, stamping the reported mode.</summary>
    /// <param name="statistics">The closure's internal statistics.</param>
    /// <param name="mode">The reported public mode.</param>
    /// <returns>The public statistics.</returns>
    private static ReasoningMaintenanceStatistics MapStatistics(in OwlRlMaintenanceStatistics statistics, ReasoningMaintenanceMode mode)
    {
        return new ReasoningMaintenanceStatistics(
            statistics.OverdeleteMarked,
            statistics.DeletionRounds,
            statistics.DirectlyRederived,
            statistics.RestoredTotal,
            statistics.InsertRounds,
            statistics.ChoiceOwnerReFires,
            statistics.BaseDemotions,
            statistics.BasePromotions,
            mode);
    }

    /// <summary>
    /// The last-landed generation's floor and decision facts — the provenance
    /// source read between commits and the inheritance source for the decay rule.
    /// </summary>
    /// <param name="Floor">The generation's detected floor.</param>
    /// <param name="Decision">The generation's resolved decision.</param>
    /// <param name="OverlayOn">Whether the generation's overlay is on (the RL closure was consistent) — the served-delta discriminant for the next commit's composed path.</param>
    /// <param name="DerivedCount">The raw derived-set size of the generation.</param>
    private readonly record struct GenerationFacts(
        ReasoningFloor Floor,
        DecisionBundle Decision,
        bool OverlayOn,
        int DerivedCount);

    /// <summary>
    /// One generation's resolved reasoning decision: the beyond-RL module and its
    /// verdict, the outcome and its telemetry, the named undecided remainder, the
    /// strategy and reason, whether the consistency claim covers the module whole,
    /// the folded consistency, and the RL falsity rule.
    /// </summary>
    /// <param name="Module">The beyond-RL module, or <c>null</c> within RL.</param>
    /// <param name="Verdict">The module verdict when one was decided or inherited, or <c>null</c>.</param>
    /// <param name="DecisionOutcome">How a beyond-RL decision ended, or <c>null</c>.</param>
    /// <param name="DecisionStatistics">The work a beyond-RL decision spent, or <c>null</c>.</param>
    /// <param name="UndecidedConstructs">The named constructs a fragment-relative verdict left undecided.</param>
    /// <param name="Strategy">The resolved strategy.</param>
    /// <param name="Reason">The expressiveness rung that selected the strategy.</param>
    /// <param name="IsDecisive">Whether the consistency claim covers the module whole.</param>
    /// <param name="FoldedConsistent">The consistency folded with any delegated decision — the provenance verdict.</param>
    /// <param name="InconsistencyRule">The RL falsity rule that fired, or <c>null</c>.</param>
    private readonly record struct DecisionBundle(
        ReasoningModule? Module,
        ModuleVerdict? Verdict,
        ReasoningDecisionOutcome? DecisionOutcome,
        ReasoningDecisionStatistics? DecisionStatistics,
        IReadOnlyList<string> UndecidedConstructs,
        ReasoningStrategy Strategy,
        ReasoningSelectionReason Reason,
        bool IsDecisive,
        bool FoldedConsistent,
        string? InconsistencyRule);

    /// <summary>
    /// The update to apply to the owned overlay snapshot when a commit lands: a
    /// wholesale <paramref name="Replacement"/> for a flip or rebuild commit, or
    /// the <paramref name="Entered"/> and <paramref name="Left"/> derived-set delta
    /// for an overlay-preserving incremental commit.
    /// </summary>
    /// <param name="Replacement">The full new overlay snapshot, or <c>null</c> when the entered/left delta applies instead.</param>
    /// <param name="Entered">The derived facts to add to the overlay.</param>
    /// <param name="Left">The derived facts to remove from the overlay.</param>
    private readonly record struct OverlayUpdate(
        HashSet<EncodedTriple>? Replacement,
        EncodedTriple[] Entered,
        EncodedTriple[] Left);

    /// <summary>The state one <see cref="MaintainCommit"/> staged and holds until the outcome is reported — promoted to current on landing, discarded otherwise.</summary>
    /// <param name="Facts">The staged generation facts.</param>
    /// <param name="OverlayUpdate">The staged overlay-snapshot update.</param>
    /// <param name="MaintenanceEvent">The staged maintenance trace event, or <c>null</c> when no handler is wired.</param>
    /// <param name="DecisionEvent">The staged re-delegation decision trace event, or <c>null</c>.</param>
    private sealed record PendingCommit(
        GenerationFacts Facts,
        OverlayUpdate OverlayUpdate,
        DatalogMaintenanceTraceEvent? MaintenanceEvent,
        ReasoningDecisionTraceEvent? DecisionEvent);
}
