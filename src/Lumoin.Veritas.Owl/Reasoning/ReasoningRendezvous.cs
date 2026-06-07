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
/// <param name="IsConsistent">Whether no contradiction was derived (and the module verdict, when one was obtained, agreed).</param>
/// <param name="InconsistencyRule">The falsity rule that fired, or <c>null</c>.</param>
/// <param name="Strategy">The selected strategy.</param>
/// <param name="Reason">The expressiveness rung that selected it.</param>
/// <param name="DetectedProfiles">The profile floor the TBox was detected at.</param>
/// <param name="Module">The beyond-ceiling module, or <c>null</c> when the TBox is within the in-engine calculi.</param>
/// <param name="ModuleVerdict">The external reasoner's verdict over <paramref name="Module"/>, or <c>null</c> when none was wired or the policy reports instead.</param>
public sealed record ReasoningResult(
    HypertrieGraphStore Store,
    int DerivedCount,
    bool IsConsistent,
    string? InconsistencyRule,
    ReasoningStrategy Strategy,
    ReasoningSelectionReason Reason,
    OwlProfiles DetectedProfiles,
    ReasoningModule? Module,
    ModuleVerdict? ModuleVerdict);

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
/// <b>Floor detection.</b> Per request in this version: the store's triples
/// map to structural form and check against the profile grammars. The
/// per-commit cached floor (invalidated by TBox-touching commits, the way
/// the join rendezvous's <c>Advance</c> keeps its view in step) arrives
/// with the write-path integration.
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

    /// <summary>Guards the cached per-generation pairs: (<see cref="ClassifiedStore"/>, <see cref="Classification"/>) and (<see cref="StatisticsStore"/>, <see cref="Statistics"/>).</summary>
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
    /// <param name="timeProvider">Clock for trace timestamps and cost measurement. Required when <paramref name="traceHandler"/> is non-<c>null</c>.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts detection, derivation, and the commit.</param>
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);

        if(traceHandler is not null && timeProvider is null)
        {
            throw new ArgumentException("A time provider must be supplied when a trace handler is configured.", nameof(timeProvider));
        }

        long startTicks = timeProvider?.GetUtcNow().UtcTicks ?? 0;

        //Floor detection: structural mapping plus the profile grammars.
        List<Quad> quads = DecodeStore(store, dictionary);
        OwlOntologyDocument document = OwlRdfMapper.Map(quads);
        OwlProfileReport report = OwlProfileChecker.Check(document);

        bool rdfsShaped = Policy.PreferRdfsWhenSufficient && IsRdfsShaped(document);
        bool withinRl = report.IsIn(OwlProfiles.Rl);

        ReasoningStrategy strategy;
        ReasoningSelectionReason reason;
        HypertrieGraphStore resultStore;
        int derivedCount;
        bool isConsistent = true;
        string? inconsistencyRule = null;
        ReasoningModule? module = null;
        ModuleVerdict? verdict = null;

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
                module = ExtractBeyondRlModule(document, report);

                if(DescriptionLogic is not null && Policy.DelegateBeyondRl)
                {
                    strategy = ReasoningStrategy.DescriptionLogicDelegate;
                    reason = ReasoningSelectionReason.BeyondRlDelegated;
                    verdict = await DescriptionLogic(module, cancellationToken).ConfigureAwait(false);
                    isConsistent = isConsistent && verdict.IsConsistent;
                }
                else
                {
                    reason = ReasoningSelectionReason.BeyondRlReported;
                }
            }
        }

        if(traceHandler is not null)
        {
            long elapsedMilliseconds = (timeProvider!.GetUtcNow().UtcTicks - startTicks) / TimeSpan.TicksPerMillisecond;
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
            strategy, reason, report.Memberships, module, verdict);
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

        long startTicks = timeProvider?.GetUtcNow().UtcTicks ?? 0;

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
            long elapsedMilliseconds = (timeProvider!.GetUtcNow().UtcTicks - startTicks) / TimeSpan.TicksPerMillisecond;
            ReasoningTraceEvent traceEvent = new(
                Interlocked.Increment(ref traceSequence),
                timeProvider.GetUtcNow().UtcTicks,
                correlationId,
                ReasoningStrategy.ElClassification,
                isBuilt ? ReasoningSelectionReason.ElClassificationBuilt : ReasoningSelectionReason.ElClassificationReused,
                BeyondRlAxiomCount: existing.UnsupportedConstructs.Count,
                DerivedCount: 0,
                isBuilt ? elapsedMilliseconds : 0);

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

    //Decodes the store's triples back to term form for the structural
    //mapping; the dictionary is the one the triples were encoded with.
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

    //Whether every axiom stays within the RDFS vocabulary the streaming
    //pass handles: named-term hierarchies, domains/ranges, declarations,
    //and assertions. Anything else — characteristics, equivalences,
    //expressions, equality — needs the RL closure.
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

    //The beyond-ceiling module: the axioms whose origins the RL grammar
    //flagged. Syntactic-locality module extraction — pulling in the
    //axioms a flagged axiom's entities depend on — is the planned
    //refinement; the violating set is the sound starting point.
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

        List<OwlAxiom> axioms = [];
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(violatingOrigins.Contains(axiom.Origin))
            {
                axioms.Add(axiom);
            }
        }

        return new ReasoningModule(axioms, rlViolations);
    }
}
