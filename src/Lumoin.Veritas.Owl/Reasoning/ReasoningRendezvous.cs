using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The result of a reasoning request: the post-commit store, the selected
/// strategy with its reason and detected profile floor, the derivation
/// outcome, and — for a TBox beyond the in-engine ceiling — the extracted
/// module and the external verdict when a delegate was wired.
/// </summary>
/// <param name="Store">The store over the post-commit snapshot (the input store when nothing was derived).</param>
/// <param name="DerivedCount">The number of triples materialized and committed.</param>
/// <param name="IsConsistent">Whether no contradiction was derived (and the module verdict, when one was obtained, agreed) — relative to the decided fragment when <see cref="UndecidedConstructs"/> is non-empty.</param>
/// <param name="InconsistencyRule">The falsity rule that fired, or <c>null</c>.</param>
/// <param name="Strategy">The selected strategy.</param>
/// <param name="Reason">The expressiveness rung that selected it.</param>
/// <param name="DetectedProfiles">The profile floor the TBox was detected at.</param>
/// <param name="Module">The beyond-ceiling module, or <c>null</c> when the TBox is within the in-engine calculi.</param>
/// <param name="ModuleVerdict">The external reasoner's verdict over <paramref name="Module"/>, or <c>null</c> when none was wired, the policy reports instead, or the decision abstained on its budget.</param>
/// <param name="DecisionOutcome">How the external decision ended, or <c>null</c> when no external reasoner ran. <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/> names the excluded remainder on <see cref="UndecidedConstructs"/>; <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> leaves <paramref name="ModuleVerdict"/> null and <paramref name="IsConsistent"/> at the value the in-engine pass derived.</param>
/// <param name="DecisionStatistics">The work the external decision spent, or <c>null</c> when no external reasoner ran.</param>
public sealed record ReasoningResult(
    HypertrieGraphStore Store,
    int DerivedCount,
    bool IsConsistent,
    string? InconsistencyRule,
    ReasoningStrategy Strategy,
    ReasoningSelectionReason Reason,
    OwlProfiles DetectedProfiles,
    ReasoningModule? Module,
    ModuleVerdict? ModuleVerdict,
    ReasoningDecisionOutcome? DecisionOutcome,
    ReasoningDecisionStatistics? DecisionStatistics)
{
    /// <summary>
    /// The named constructs of the beyond-ceiling module that remain undecided
    /// when the external verdict is fragment-relative: the delegated calculus
    /// excluded them, so <see cref="IsConsistent"/> says nothing about them.
    /// Empty when no module was delegated, when the verdict covers the module
    /// whole, when the verdict itself is inconsistent (condemnation covers the
    /// module regardless), or when the decision abstained on its budget (the
    /// abstention is then total and <see cref="DecisionOutcome"/> records it).
    /// The strings are the verdict's
    /// <see cref="ModuleVerdict.UnsupportedConstructs"/> entries, verbatim.
    /// </summary>
    public IReadOnlyList<string> UndecidedConstructs { get; init; } = [];
}

/// <summary>
/// The per-request reasoning-strategy choice, mirroring the join layer's
/// <c>QueryEngineRendezvous</c> one rung up the expressiveness ladder: it
/// detects the TBox's profile floor, selects among the strategies of
/// increasing expressiveness per a <see cref="ReasoningPolicy"/>, announces
/// every choice as a <see cref="ReasoningTraceEvent"/>, and never silently
/// drops what it cannot soundly answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Strategies.</b> Strategy R is the in-engine materialization pair —
/// the RDFS streaming pass for RDFS-shaped TBoxes, the OWL 2 RL/RDF rules
/// closure otherwise — committing derivations through the journal so the
/// entry's additions are exactly the inferred knowledge. Strategy D hands
/// the axioms beyond the RL grammar to the
/// <see cref="DescriptionLogicDelegate"/> seam; with no delegate wired they
/// are reported on the result and the trace. Strategy E — the EL
/// classifier feeding planner statistics and query-time expansion — is the
/// next strategy to wire; its selection rungs are already part of this
/// surface's contract.
/// </para>
/// <para>
/// <b>Floor detection.</b> Per commit, not per request: the store's triples
/// map to structural form and check against the profile grammars once per
/// store generation, and the resulting <see cref="ReasoningFloor"/> is
/// cached with the store reference as the generation marker. The write
/// path keeps the cache in step through <see cref="Advance"/>, the way the
/// join rendezvous's <c>Advance</c> keeps its columnar view in step: an
/// assertion-only commit carries the floor and the EL classification to
/// the new generation, anything touching schema vocabulary invalidates
/// them, and the planner statistics rebuild whenever extents change.
/// </para>
/// </remarks>
[DebuggerDisplay("ReasoningRendezvous Delegate={DescriptionLogic is not null}")]
public sealed class ReasoningRendezvous
{
    /// <summary>The selection policy.</summary>
    private ReasoningPolicy Policy { get; }

    /// <summary>The external SROIQ(D) reasoner seam, or <c>null</c>.</summary>
    private DescriptionLogicDelegate? DescriptionLogic { get; }

    //Sequence-number counter for trace events emitted by this rendezvous.
    //A field because Interlocked requires a ref parameter.
    private long traceSequence;

    /// <summary>
    /// The store generation the cached classification describes: the store
    /// reference is the generation marker, exactly as the join rendezvous
    /// keys its columnar view. A commit produces a new store instance and
    /// the next request rebuilds. Swapped under <see cref="ClassificationLock"/>.
    /// </summary>
    private HypertrieGraphStore? ClassifiedStore { get; set; }

    /// <summary>The cached EL classification for <see cref="ClassifiedStore"/>.</summary>
    private El.ElClassification? Classification { get; set; }

    /// <summary>The store generation the cached planner statistics describe; the same reference-as-generation-marker scheme as <see cref="ClassifiedStore"/>.</summary>
    private HypertrieGraphStore? StatisticsStore { get; set; }

    /// <summary>The cached planner statistics for <see cref="StatisticsStore"/>.</summary>
    private Core.Hypertrie.Planning.AprioriCardinalities? Statistics { get; set; }

    /// <summary>The store generation the cached floor describes; the same reference-as-generation-marker scheme as <see cref="ClassifiedStore"/>.</summary>
    private HypertrieGraphStore? FloorStore { get; set; }

    /// <summary>The cached expressiveness floor for <see cref="FloorStore"/>.</summary>
    private ReasoningFloor? Floor { get; set; }

    /// <summary>Guards the cached per-generation pairs: (<see cref="ClassifiedStore"/>, <see cref="Classification"/>), (<see cref="StatisticsStore"/>, <see cref="Statistics"/>), and (<see cref="FloorStore"/>, <see cref="Floor"/>).</summary>
    private object ClassificationLock { get; } = new();

    /// <summary>
    /// Constructs a rendezvous with the given policy and an optional
    /// external description-logic seam.
    /// </summary>
    /// <param name="policy">The selection policy. Pass <see cref="ReasoningPolicy.Default"/> for the standard behaviour.</param>
    /// <param name="descriptionLogic">The external SROIQ(D) reasoner seam, or <c>null</c> — beyond-ceiling modules are then reported, never silently dropped.</param>
    public ReasoningRendezvous(ReasoningPolicy policy, DescriptionLogicDelegate? descriptionLogic = null)
    {
        Policy = policy;
        DescriptionLogic = descriptionLogic;
    }

    /// <summary>
    /// Detects the store content's profile floor, materializes with the
    /// selected strategy, commits the derivations, and announces the
    /// decision.
    /// </summary>
    /// <param name="store">The store to reason over.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <param name="traceHandler">Optional handler receiving the <see cref="ReasoningTraceEvent"/>.</param>
    /// <param name="timeProvider">Clock for trace timestamps and cost measurement. Required when either trace handler is non-<c>null</c>.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts detection, derivation, and the commit.</param>
    /// <param name="decisionTraceHandler">Optional handler receiving the <see cref="ReasoningDecisionTraceEvent"/> for a delegated beyond-RL decision.</param>
    /// <returns>The reasoning result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dictionary"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<ReasoningResult> MaterializeAsync(
        HypertrieGraphStore store,
        TermDictionary dictionary,
        TraceHandler<ReasoningTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        TraceHandler<ReasoningDecisionTraceEvent>? decisionTraceHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        if((traceHandler is not null || decisionTraceHandler is not null) && timeProvider is null)
        {
            throw new ArgumentException("A time provider must be supplied when a trace handler is configured.", nameof(timeProvider));
        }

        long startTimestamp = timeProvider?.GetTimestamp() ?? 0;

        ReasoningFloor floor = DetectFloor(store, dictionary);

        bool rdfsShaped = Policy.PreferRdfsWhenSufficient && floor.IsRdfsShaped;
        bool withinRl = floor.IsWithinRl;

        ReasoningStrategy strategy;
        ReasoningSelectionReason reason;
        HypertrieGraphStore resultStore;
        int derivedCount;
        bool isConsistent = true;
        string? inconsistencyRule = null;
        ReasoningModule? module = null;
        ModuleVerdict? verdict = null;
        ReasoningDecisionOutcome? decisionOutcome = null;
        ReasoningDecisionStatistics? decisionStatistics = null;

        if(rdfsShaped)
        {
            strategy = ReasoningStrategy.Rdfs;
            reason = ReasoningSelectionReason.RdfsSufficient;

            RdfsVocabularyTerms rdfsTerms = new(
                dictionary.GetOrAdd(new NamedNode(Vocabulary.Rdf.Type)),
                dictionary.GetOrAdd(new NamedNode(Rdf.RdfVocabulary.Rdfs.SubClassOf)),
                dictionary.GetOrAdd(new NamedNode(Rdf.RdfVocabulary.Rdfs.SubPropertyOf)),
                dictionary.GetOrAdd(new NamedNode(Rdf.RdfVocabulary.Rdfs.Domain)),
                dictionary.GetOrAdd(new NamedNode(Rdf.RdfVocabulary.Rdfs.Range)));

            (resultStore, derivedCount) = await RdfsMaterialization
                .MaterializeAndCommitAsync(store, rdfsTerms, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            strategy = ReasoningStrategy.Rl;
            OwlRlMaterializationResult rl = await OwlRlMaterialization
                .MaterializeAndCommitAsync(
                    store,
                    new OwlRlTerms(dictionary),
                    OwlRlDatatypeOracles.FromDictionary(dictionary),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            resultStore = rl.Store;
            derivedCount = rl.DerivedCount;
            isConsistent = rl.IsConsistent;
            inconsistencyRule = rl.InconsistencyRule;

            if(withinRl)
            {
                reason = ReasoningSelectionReason.RlSufficient;
            }
            else
            {
                module = floor.Module;

                if(DescriptionLogic is not null && Policy.DelegateBeyondRl)
                {
                    strategy = ReasoningStrategy.DescriptionLogicDelegate;
                    reason = ReasoningSelectionReason.BeyondRlDelegated;

                    long decisionStartTimestamp = timeProvider?.GetTimestamp() ?? 0;
                    ModuleDecision decision = await DescriptionLogic(module!, cancellationToken).ConfigureAwait(false);
                    decisionOutcome = decision.Outcome;
                    decisionStatistics = decision.Statistics;
                    verdict = decision.Verdict;

                    //A budget abstention leaves no verdict to fold: the result
                    //keeps the consistency the in-engine pass derived, and the
                    //outcome records that the module went undecided.
                    if(verdict is not null)
                    {
                        isConsistent = isConsistent && verdict.IsConsistent;
                    }

                    if(decisionTraceHandler is not null)
                    {
                        double decisionElapsedMilliseconds = timeProvider!.GetElapsedTime(decisionStartTimestamp).TotalMilliseconds;
                        ReasoningDecisionTraceEvent decisionEvent = ReasoningDecisionTraceEvent.From(
                            Interlocked.Increment(ref traceSequence),
                            timeProvider.GetUtcNow().UtcTicks,
                            correlationId,
                            decision.Outcome,
                            decision.Statistics,
                            decisionElapsedMilliseconds);

                        decisionTraceHandler(in decisionEvent);
                    }
                }
                else
                {
                    reason = ReasoningSelectionReason.BeyondRlReported;
                }
            }
        }

        if(traceHandler is not null)
        {
            double elapsedMilliseconds = timeProvider!.GetElapsedTime(startTimestamp).TotalMilliseconds;
            ReasoningTraceEvent traceEvent = new(
                Interlocked.Increment(ref traceSequence),
                timeProvider.GetUtcNow().UtcTicks,
                correlationId,
                strategy,
                reason,
                module?.Axioms.Count ?? 0,
                derivedCount,
                elapsedMilliseconds);

            traceHandler(in traceEvent);
        }

        return new ReasoningResult(
            resultStore, derivedCount, isConsistent, inconsistencyRule,
            strategy, reason, floor.Memberships, module, verdict,
            decisionOutcome, decisionStatistics)
        {
            //A fragment-relative consistent verdict carries its named remainder
            //onto the result; a whole-module or inconsistent verdict, a
            //budget abstention, and an undelegated module all leave it empty.
            UndecidedConstructs = verdict is { IsConsistent: true, UnsupportedConstructs.Count: > 0 }
                ? verdict.UnsupportedConstructs
                : [],
        };
    }

    /// <summary>
    /// The EL classification of the store's TBox — Strategy E: TBox
    /// preprocessing whose subclass closure feeds planner statistics and
    /// query-time expansion, never materialized triples. Built once per
    /// store generation (the store reference is the generation marker) and
    /// reused until a commit produces a successor; every call announces
    /// <see cref="ReasoningSelectionReason.ElClassificationBuilt"/> or
    /// <see cref="ReasoningSelectionReason.ElClassificationReused"/>.
    /// </summary>
    /// <param name="store">The store whose content classifies.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <param name="traceHandler">Optional handler receiving the <see cref="ReasoningTraceEvent"/>.</param>
    /// <param name="timeProvider">Clock for trace timestamps and cost measurement. Required when <paramref name="traceHandler"/> is non-<c>null</c>.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts classification.</param>
    /// <returns>The classification for this store generation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dictionary"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public El.ElClassification Classify(
        HypertrieGraphStore store,
        TermDictionary dictionary,
        TraceHandler<ReasoningTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        if(traceHandler is not null && timeProvider is null)
        {
            throw new ArgumentException("A time provider must be supplied when a trace handler is configured.", nameof(timeProvider));
        }

        long startTimestamp = timeProvider?.GetTimestamp() ?? 0;

        El.ElClassification? existing;
        lock(ClassificationLock)
        {
            existing = ReferenceEquals(ClassifiedStore, store) ? Classification : null;
        }

        bool isBuilt = existing is null;
        if(existing is null)
        {
            OwlOntologyDocument document = OwlRdfMapper.Map(DecodeStore(store, dictionary));
            existing = El.ElClassifier.Classify(document, cancellationToken);

            lock(ClassificationLock)
            {
                //A racing request may have classified the same generation
                //first; either result is correct for it, so the later
                //writer simply wins.
                ClassifiedStore = store;
                Classification = existing;
            }
        }

        if(traceHandler is not null)
        {
            double elapsedMilliseconds = timeProvider!.GetElapsedTime(startTimestamp).TotalMilliseconds;
            ReasoningTraceEvent traceEvent = new(
                Interlocked.Increment(ref traceSequence),
                timeProvider.GetUtcNow().UtcTicks,
                correlationId,
                ReasoningStrategy.ElClassification,
                isBuilt ? ReasoningSelectionReason.ElClassificationBuilt : ReasoningSelectionReason.ElClassificationReused,
                BeyondRlAxiomCount: existing.UnsupportedConstructs.Count,
                DerivedCount: 0,
                isBuilt ? elapsedMilliseconds : 0.0);

            traceHandler(in traceEvent);
        }

        return existing;
    }

    /// <summary>
    /// The planner's a-priori cardinality statistics for the store's
    /// generation — the Strategy E feed into the join layer's
    /// <c>PlannerContext</c>: subclass closure × per-class asserted
    /// extent counts, computed by <see cref="El.ElPlannerStatistics"/>
    /// over the same cached classification <see cref="Classify"/>
    /// maintains. Built once per store generation and reused until a
    /// commit produces a successor.
    /// </summary>
    /// <param name="store">The store whose content the statistics describe.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <param name="traceHandler">Optional handler receiving the classification's <see cref="ReasoningTraceEvent"/>.</param>
    /// <param name="timeProvider">Clock for trace timestamps and cost measurement. Required when <paramref name="traceHandler"/> is non-<c>null</c>.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts classification and the statistics sweep.</param>
    /// <returns>The statistics for this store generation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="dictionary"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public Core.Hypertrie.Planning.AprioriCardinalities PlannerStatistics(
        HypertrieGraphStore store,
        TermDictionary dictionary,
        TraceHandler<ReasoningTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        Core.Hypertrie.Planning.AprioriCardinalities? existing;
        lock(ClassificationLock)
        {
            existing = ReferenceEquals(StatisticsStore, store) ? Statistics : null;
        }

        if(existing is null)
        {
            El.ElClassification classification = Classify(store, dictionary, traceHandler, timeProvider, correlationId, cancellationToken);
            existing = El.ElPlannerStatistics.Build(classification, store, dictionary);

            lock(ClassificationLock)
            {
                //A racing request may have built statistics for the
                //same generation first; either result is correct for
                //it, so the later writer simply wins.
                StatisticsStore = store;
                Statistics = existing;
            }
        }

        return existing;
    }

    /// <summary>
    /// The cached expressiveness floor for the store's generation, or
    /// <c>null</c> when no detection has run against it — the
    /// observability window onto the per-commit cache, paralleling what
    /// the trace bus announces.
    /// </summary>
    /// <param name="store">The store generation to look up.</param>
    /// <returns>The cached floor, or <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ReasoningFloor? FloorFor(HypertrieGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        lock(ClassificationLock)
        {
            return ReferenceEquals(FloorStore, store) ? Floor : null;
        }
    }

    /// <summary>
    /// Keeps the per-generation caches in step with a commit, the
    /// reasoning analog of the join rendezvous's <c>Advance</c>. An
    /// assertion-only delta — plain individual assertions touching no
    /// schema vocabulary — cannot move the expressiveness floor or the
    /// TBox-only classification, so both carry to the new generation;
    /// anything else invalidates them for re-detection at the next
    /// request. The planner statistics never carry: assertions change the
    /// extent counts they sum, and they rebuild cheaply from the carried
    /// classification.
    /// </summary>
    /// <remarks>
    /// The contract mirrors the join rendezvous: commits arrive in order
    /// from the single write path, so the caches this instance holds
    /// always describe the pre-commit generation when a delta arrives.
    /// </remarks>
    /// <param name="newStore">The post-commit store generation.</param>
    /// <param name="additions">The triples the commit added.</param>
    /// <param name="removals">The triples the commit removed.</param>
    /// <param name="dictionary">The term dictionary the delta's triples were encoded with.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public void Advance(
        HypertrieGraphStore newStore,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals,
        TermDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(newStore);
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);
        ArgumentNullException.ThrowIfNull(dictionary);

        bool assertionOnly = IsAssertionOnly(additions, dictionary) && IsAssertionOnly(removals, dictionary);

        lock(ClassificationLock)
        {
            if(assertionOnly)
            {
                ClassifiedStore = Classification is null ? null : newStore;
                FloorStore = Floor is null ? null : newStore;
            }
            else
            {
                ClassifiedStore = null;
                Classification = null;
                FloorStore = null;
                Floor = null;
            }

            StatisticsStore = null;
            Statistics = null;
        }
    }

    /// <summary>
    /// Resets every per-generation cache to its un-detected state, under
    /// <see cref="ClassificationLock"/>: the floor, the EL classification, and the
    /// planner statistics, each with its store-generation marker. The caches
    /// describe generations of a lineage the owner has discarded — a maintained
    /// commit that ran detection but never landed — so a discard drops them
    /// wholesale. Leaving an entry would let the next <see cref="Advance"/> re-key
    /// it (an assertion-only carry preserves a non-null cache under a new store
    /// reference) onto a fresh generation the entry does not describe, and the
    /// following <see cref="DetectFloor"/> would then hit it in place of
    /// re-detecting, deciding a rebuilt generation on a phantom floor.
    /// </summary>
    internal void ResetGenerationCaches()
    {
        lock(ClassificationLock)
        {
            ClassifiedStore = null;
            Classification = null;
            StatisticsStore = null;
            Statistics = null;
            FloorStore = null;
            Floor = null;
        }
    }

    /// <summary>
    /// The cached per-generation floor detection: structural mapping plus
    /// the profile grammars, run once per store generation; the module is
    /// extracted at detection so the cache carries everything selection
    /// reads without holding the decoded document. Detection populates the
    /// same store-keyed cache <see cref="Advance"/> maintains, so the
    /// maintained lane forces re-detection over a schema-touching commit's
    /// post-op asserted store through this seam and leaves the floor cached
    /// for the planner's next request.
    /// </summary>
    /// <param name="store">The store generation to detect.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <returns>The floor for the generation.</returns>
    internal ReasoningFloor DetectFloor(HypertrieGraphStore store, TermDictionary dictionary)
    {
        ReasoningFloor? existing;
        lock(ClassificationLock)
        {
            existing = ReferenceEquals(FloorStore, store) ? Floor : null;
        }

        if(existing is null)
        {
            List<Quad> quads = DecodeStore(store, dictionary);
            OwlOntologyDocument document = OwlRdfMapper.Map(quads);
            OwlProfileReport report = OwlProfileChecker.Check(document);
            bool withinRl = report.IsIn(OwlProfiles.Rl);

            existing = new ReasoningFloor(
                report.Memberships,
                IsRdfsShaped(document),
                withinRl,
                withinRl ? null : ExtractBeyondRlModule(document, report));

            lock(ClassificationLock)
            {
                //A racing request may have detected the same generation
                //first; either result is correct for it, so the later
                //writer simply wins.
                FloorStore = store;
                Floor = existing;
            }
        }

        return existing;
    }

    /// <summary>
    /// Whether every triple of the delta is a plain individual assertion:
    /// no term in the reserved RDF/RDFS/OWL namespaces anywhere, and a
    /// class-membership object that is a named, non-reserved class. Such
    /// triples map to assertion axioms within every profile grammar and
    /// the RDFS shape, so they cannot move the floor; everything else is
    /// treated as schema-touching — conservative, never unsound.
    /// </summary>
    /// <param name="delta">The commit's added or removed triples.</param>
    /// <param name="dictionary">The term dictionary the delta's triples were encoded with.</param>
    /// <returns><see langword="true"/> when no triple can carry schema structure.</returns>
    private static bool IsAssertionOnly(IReadOnlyCollection<EncodedTriple> delta, TermDictionary dictionary)
    {
        foreach(EncodedTriple triple in delta)
        {
            if(dictionary.Resolve(triple.Subject) is NamedNode subject && IsReservedVocabulary(subject.Iri))
            {
                return false;
            }

            if(dictionary.Resolve(triple.Predicate) is not NamedNode predicate)
            {
                return false;
            }

            if(predicate.Iri.Equals(Vocabulary.Rdf.Type))
            {
                if(dictionary.Resolve(triple.Object) is not NamedNode typeObject || IsReservedVocabulary(typeObject.Iri))
                {
                    return false;
                }
            }
            else if(IsReservedVocabulary(predicate.Iri) || (dictionary.Resolve(triple.Object) is NamedNode objectNode && IsReservedVocabulary(objectNode.Iri)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the IRI lives in the <c>rdf:</c>, <c>rdfs:</c>, or
    /// <c>owl:</c> namespace — the vocabulary whose triples can carry
    /// schema structure.
    /// </summary>
    /// <param name="iri">The IRI to test.</param>
    /// <returns><see langword="true"/> for a reserved-namespace IRI.</returns>
    private static bool IsReservedVocabulary(Utf8String iri)
    {
        ReadOnlySpan<byte> span = iri.Span;

        return span.StartsWith(RdfNamespace.Span) || span.StartsWith(RdfsNamespace.Span) || span.StartsWith(OwlNamespace.Span);
    }

    /// <summary>The <c>rdf:</c> namespace prefix in UTF-8 form for the reserved-vocabulary scan.</summary>
    private static Utf8String RdfNamespace { get; } = Utf8Strings.From(Rdf.RdfVocabulary.Rdf.Namespace);

    /// <summary>The <c>rdfs:</c> namespace prefix in UTF-8 form for the reserved-vocabulary scan.</summary>
    private static Utf8String RdfsNamespace { get; } = Utf8Strings.From(Rdf.RdfVocabulary.Rdfs.Namespace);

    /// <summary>The <c>owl:</c> namespace prefix in UTF-8 form for the reserved-vocabulary scan.</summary>
    private static Utf8String OwlNamespace { get; } = Utf8Strings.From(OwlVocabulary.Namespace);

    /// <summary>
    /// Decodes the store's triples back to term form for the structural
    /// mapping; the dictionary is the one the triples were encoded with.
    /// </summary>
    /// <param name="store">The store to decode.</param>
    /// <param name="dictionary">The term dictionary the store's triples were encoded with.</param>
    /// <returns>The decoded quads.</returns>
    private static List<Quad> DecodeStore(HypertrieGraphStore store, TermDictionary dictionary)
    {
        List<Quad> quads = [];
        foreach(EncodedTriple triple in store.Match(TermId.None, TermId.None, TermId.None))
        {
            if(dictionary.Resolve(triple.Predicate) is NamedNode predicate)
            {
                quads.Add(new Quad(dictionary.Resolve(triple.Subject), predicate, dictionary.Resolve(triple.Object), Graph: null));
            }
        }

        return quads;
    }

    /// <summary>
    /// Whether every axiom stays within the RDFS vocabulary the streaming
    /// pass handles: named-term hierarchies, domains/ranges, declarations,
    /// and assertions. Anything else — characteristics, equivalences,
    /// expressions, equality — needs the RL closure.
    /// </summary>
    /// <param name="document">The mapped ontology document.</param>
    /// <returns><see langword="true"/> when the RDFS streaming pass covers the content.</returns>
    private static bool IsRdfsShaped(OwlOntologyDocument document)
    {
        foreach(OwlAxiom axiom in document.Axioms)
        {
            bool rdfsShaped = axiom switch
            {
                OwlDeclarationAxiom => true,
                OwlImportAxiom => true,
                OwlAnnotationAssertionAxiom => true,
                OwlSubAnnotationPropertyOfAxiom => true,
                OwlAnnotationPropertyDomainAxiom => true,
                OwlAnnotationPropertyRangeAxiom => true,
                OwlSubClassOfAxiom subClass => subClass.SubClass is OwlClassReference && subClass.SuperClass is OwlClassReference,
                OwlSubObjectPropertyOfAxiom subProperty => !subProperty.SubProperty.IsInverse && !subProperty.SuperProperty.IsInverse,
                OwlSubDataPropertyOfAxiom => true,
                OwlObjectPropertyDomainAxiom domain => !domain.Property.IsInverse && domain.Domain is OwlClassReference,
                OwlObjectPropertyRangeAxiom range => !range.Property.IsInverse && range.Range is OwlClassReference,
                OwlDataPropertyDomainAxiom dataDomain => dataDomain.Domain is OwlClassReference,
                OwlDataPropertyRangeAxiom dataRange => dataRange.Range is OwlDatatypeReference,
                OwlClassAssertionAxiom assertion => assertion.Class is OwlClassReference,
                OwlObjectPropertyAssertionAxiom => true,
                OwlDataPropertyAssertionAxiom => true,
                _ => false
            };

            if(!rdfsShaped)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The beyond-ceiling module: the axioms whose origins the RL grammar
    /// flagged, widened to their syntactic ⊥-locality module so the
    /// external reasoner sees every axiom its verdict over the flagged
    /// signature depends on.
    /// </summary>
    /// <param name="document">The mapped ontology document.</param>
    /// <param name="report">The profile report whose RL findings anchor the module.</param>
    /// <returns>The module and its findings.</returns>
    private static ReasoningModule ExtractBeyondRlModule(OwlOntologyDocument document, OwlProfileReport report)
    {
        List<OwlProfileViolation> rlViolations = [];
        HashSet<Quad> violatingOrigins = [];
        foreach(OwlProfileViolation violation in report.Violations)
        {
            if(violation.Profile == OwlProfiles.Rl)
            {
                rlViolations.Add(violation);
                if(violation.Origin is Quad origin)
                {
                    violatingOrigins.Add(origin);
                }
            }
        }

        List<OwlAxiom> seeds = [];
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(violatingOrigins.Contains(axiom.Origin))
            {
                seeds.Add(axiom);
            }
        }

        return new ReasoningModule(SyntacticLocalityModule.Extract(document, seeds), rlViolations);
    }
}
