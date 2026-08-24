using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>How a saturation run ended: at the fixpoint, or stopped early on the inference budget.</summary>
internal enum SaturationOutcome
{
    /// <summary>The structure reached its fixpoint — every applicable rule instance is redundant.</summary>
    Completed = 0,

    /// <summary>The inference budget was reached before the fixpoint; the structure is partial and no verdict may be read.</summary>
    BudgetExhausted = 1,
}

/// <summary>The vr key join's outcome: every comparison was decisive and the engine sits at its post-join fixpoint (a merge-forced <c>⊥</c> reads off the inconsistency probe), a data comparison abstained, or the budget stopped a re-saturation pass.</summary>
internal enum RootKeyJoinOutcome
{
    /// <summary>Every comparison was decisive; any fired merges rode the engine as root-fact continuations and the join reached a fixpoint with no further pair firing. An inconsistency a fired merge forced reads off the module's inconsistency probe.</summary>
    Clean = 0,

    /// <summary>A data-key value comparison answered <c>Indeterminate</c> — the module delegates named (<c>KeyValueComparisonIndeterminate</c>), the property recorded on the engine.</summary>
    Indeterminate = 1,

    /// <summary>A re-saturation pass exhausted the inference budget; the structure is partial and no verdict may be read.</summary>
    BudgetExhausted = 2,
}

/// <summary>Receives one landed Eq application — the landing context and the acting rewrite's source and replacement terms, so an attached probe can attribute landings by context class and by rewrite shape — the per-landing measurement seam a probe attaches through <see cref="ContextSaturationEngine.EqLandingProbe"/>. Probes read distributions; the shipped statistics stay scalar aggregates. Never attached in production.</summary>
/// <param name="context">The context the Eq conclusion landed in.</param>
/// <param name="fromTerm">The acting rewrite's source term <c>s1</c>.</param>
/// <param name="replacement">The acting rewrite's replacement term <c>t1</c>.</param>
internal delegate void EqLandingProbeDelegate(Context context, DlTerm fromTerm, DlTerm replacement);

/// <summary>Receives each saturation engine a module decision constructs, immediately after creation and before seeding — the measurement seam through which a probe attaches per-landing observers and holds the engine for post-run distribution reads. A key-merge fixpoint constructs one engine per round, so the probe is invoked per round and the final round's engine is the one whose statistics land in the decision. Never attached in production.</summary>
/// <param name="engine">The created engine.</param>
internal delegate void SaturationEngineProbeDelegate(ContextSaturationEngine engine);

/// <summary>How the single mutation point resolved one offered conclusion — the outcome the offer-counting call sites read, whose <see cref="Inserted"/> value is the boolean face's <see langword="true"/>.</summary>
internal enum ClauseOfferOutcome
{
    /// <summary>The conclusion passed every gate and was inserted.</summary>
    Inserted,

    /// <summary>The containment gate absorbed the conclusion into an exact duplicate — the live set's fast path.</summary>
    ExactDuplicate,

    /// <summary>The containment gate absorbed the conclusion into a strictly more general clause — an index-drawn subsumer or the live empty clause.</summary>
    Subsumed,

    /// <summary>Head normalization dropped the conclusion as a tautology.</summary>
    TautologyDropped,

    /// <summary>The in-saturation grammar guard refused the conclusion's head shape.</summary>
    OutOfGrammar,
}

/// <summary>One row of the per-root-class population probe read: the root-class context, its home individual, and its live clause count. A measurement projection <see cref="ContextSaturationEngine.AppendRootClassPopulation"/> fills; the shipped statistics stay scalar aggregates.</summary>
/// <param name="ContextId">The root-class context's id.</param>
/// <param name="HomeIndividual">The home individual id, or <c>-1</c> for the single root <c>vr</c>.</param>
/// <param name="HomeIndividualName">The rendered home individual, or <c>vr</c> for the single root.</param>
/// <param name="LiveClauses">The context's live clause count at the read.</param>
internal readonly record struct RootClassPopulationRow(int ContextId, int HomeIndividual, string HomeIndividualName, int LiveClauses);

/// <summary>
/// The consequence-based context-saturation engine for the SRIQ context
/// calculus (KR 2016, Table 2;
/// <see href="https://arxiv.org/abs/1602.04498"/>), disjunctive heads included.
/// It saturates a <see cref="ContextStructure"/> under the published rules —
/// Core, Hyper, Succ, Pred, Elim, Eq, Ineq, and Factor (equality factoring) —
/// as ordered resolution against the SELECTED head literal: every non-empty
/// head has a unique maximal literal under the total selection order
/// (<see cref="ContextTermOrder.SelectHeadIndex"/>), a clause participates as a
/// premise only through that literal, and its remaining head literals are
/// carried into each conclusion as residual disjuncts. The engine runs an
/// eager-Hyper two-queue worklist, the cautious successor strategy (Definition
/// 6), and subset-subsumption redundancy (Definition 4), then reads the
/// consistency and subsumption verdicts off the saturated structure. The engine
/// mints no symbol after setup: the frozen signature is the termination
/// substrate. Production reach is bounded upstream by the module survey and the
/// reasoner's second gate; the clause grammar the engine accepts is the full
/// disjunctive one.
/// </summary>
/// <remarks>
/// The engine restores the Top/Bottom atom semantics the clausifier leaves as
/// ordinary interned atoms: every context is seeded <c>⊤→Top(x)</c> at
/// creation, and one virtual ontology clause <c>Bottom(x)→⊥</c> joins the
/// clause set — both exact tautologies of the intended semantics, sound
/// relative to any core. House rules hold throughout: no recursion (explicit
/// queues and odometers), value-based control flow (containment and
/// applicability are return values), pooled scratch buffers at the join hot
/// loops, and no LINQ in the saturation loops.
/// </remarks>
internal sealed class ContextSaturationEngine
{
    /// <summary>The provenance origin of a clause derived by a rule rather than clausified from an axiom.</summary>
    private const int DerivedOrigin = -1;

    /// <summary>The context structure being saturated.</summary>
    private ContextStructure Structure { get; }

    /// <summary>The module's context term order: the selection of every clause's maximal head literal and the orientation the equality machinery relies on both read it.</summary>
    private ContextTermOrder Order { get; }

    /// <summary>The data-demand marker descriptors, keyed by marker concept-atom id — the side table the data-obligation rule reconstructs the sidecar obligations from; empty for a module carrying no admitted data restriction.</summary>
    private IReadOnlyDictionary<int, DataDemandDescriptor> DataDemandDescriptors { get; }

    /// <summary>The module's data-property RBox handed to the shared datatype sidecar (<see cref="DataPropertyBox.Empty"/> when no data-property axiom).</summary>
    private DataPropertyBox DataBox { get; }

    /// <summary>The registered-datatype set the datatype sidecar consults where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> when the host registered none. Set independently of the clausification, which is registry-blind.</summary>
    private DatatypeRegistry Registry { get; }

    /// <summary>The scope of the central-variable-versus-individual <c>x ≈ o</c> Eq paramodulation — the creation-time knob deciding whether the rewrite of a named individual down to the central variable fires in every context (the reference behaviour) or only in the read-off contexts (the default). Verdict-neutral for both read-off surfaces; it changes only the equated-enumeration clause traffic.</summary>
    private NominalParamodulationScope ParamodulationScope { get; }

    /// <summary>The r-Pred ground-relevance mode — the creation-time knob deciding whether a root clause propagates into a target that cannot discharge its ground body conjuncts (the published unrestricted behaviour, the default) or is filtered at the offer with downward compensation and re-offer triggers. Verdict-neutral for both read-off surfaces; it changes only the root-propagation clause traffic.</summary>
    private RootPropagationRelevance PropagationRelevance { get; }

    /// <summary>The root-tier topology — the creation-time knob deciding whether ground nominal reasoning concentrates in the one distinguished root context <c>vr</c> (the published single-root behaviour, the default) or fragments into one nominal-root context per individual with the inter-nominal carrier meeting cross-individual evidence. Verdict-neutral for both read-off surfaces (H-RF-1); it changes only where the root-tier inferences run.</summary>
    private RootContextTopology Topology { get; }

    /// <summary>Whether the module carries nominal jurisdiction — interned individual constants exist, so the root tier exists and the four nominal rules can fire. Reads the individual census once per dispatch; under the fragmented topology the scalar root slot stays unminted, so the census is the topology-neutral gate.</summary>
    private bool HasRootMachinery
    {
        get
        {
            return Symbols.IndividualCount > 0;
        }
    }

    /// <summary>The grammar-and-band kind of a context under the engine's topology — the (root-class, topology) key of the clause grammar and the term-order band: ordinary contexts are topology-blind; a root-class context is the published single root or a per-individual nominal root, never both within one engine.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The context's grammar kind.</returns>
    private ContextGrammarKind GrammarKindOf(Context context)
    {
        if(!context.IsRoot)
        {
            return ContextGrammarKind.Ordinary;
        }

        return Topology == RootContextTopology.SingleRoot ? ContextGrammarKind.Root : ContextGrammarKind.NominalRoot;
    }

    /// <summary>Whether a context runs the constant-anchored root machinery — the single root's Hyper odometer, dual-individual fan-out, and per-constant Pred sigma. A nominal-root context does NOT: its entry translation respells own constants central, so the ordinary central-anchored machinery serves it.</summary>
    /// <param name="context">The context.</param>
    /// <returns><see langword="true"/> for the single root context under the single-root topology.</returns>
    private bool UsesConstantAnchoredRoot(Context context)
    {
        return context.IsRoot && Topology == RootContextTopology.SingleRoot;
    }

    /// <summary>The reusable buffer of the distinct foreign individuals an inter-nominal carrier firing images toward, in first-mention head order.</summary>
    private List<int> ScratchCarrierForeigns { get; } = [];

    /// <summary>The reusable body buffer of the D2 entry translation.</summary>
    private List<DlLiteral> ScratchEntryBody { get; } = [];

    /// <summary>The reusable head buffer of the D2 entry translation.</summary>
    private List<DlLiteral> ScratchEntryHead { get; } = [];

    /// <summary>The reusable buffer of an inter-nominal carrier image's head literals under the <c>[x/src][o_i/x]</c> substitution.</summary>
    private List<DlLiteral> ScratchCarrierHead { get; } = [];

    /// <summary>The inter-nominal carrier landings — carrier-(1) images inserted into a foreign nominal-root context; zero under the single-root default.</summary>
    private long InterNominalPropagations { get; set; }

    /// <summary>The inter-nominal carrier offers absorbed by the redundancy discipline — the duplicate-image absorption the carrier-cascade convergence argument leans on; zero under the single-root default.</summary>
    private long InterNominalRedundant { get; set; }

    /// <summary>The scopable rewrites the license-scoped atom axis blocked in a query-initialized context because the acted-on target literal was not a query atom; zero under every other scope.</summary>
    private long EqScopeBlockedQueryAtom { get; set; }

    /// <summary>The scopable rewrites the license-scoped context axis blocked in a root-class context under the fragmented topology because the acting equality clause carried no push provenance; zero under every other scope and under the single-root topology, whose root stays fully exempt.</summary>
    private long EqScopeBlockedRootClass { get; set; }

    /// <summary>The push-provenance tag joins the redundancy discipline performed — an absorbed pushed derivation OR-ing its tag onto an untagged surviving absorber, which is then re-enqueued so a previously push-gated rewrite over it is retried; zero under every scope but the license-scoped one.</summary>
    private long EqScopeTagJoins { get; set; }

    /// <summary>The query-initialized context ids in which the license-scoped atom axis blocked a scopable rewrite — the per-context face of the blocked-live latch's query-surface arm: such a context may not certify a satisfiable or non-subsumption read. <see langword="null"/> until the first block.</summary>
    private HashSet<int>? EqScopeBlockedQueryContexts { get; set; }

    /// <summary>The concept atoms of the subsumption sweep's signature classes, recorded at query-context creation under the license scope only — the query-atom set the license-scoped atom axis admits as rewrite targets (the <c>∆_Q</c> analogue the sweep inspects), the Bottom atom included as the read-off-bearing shape. <see langword="null"/> under every other scope, so the shipped paths carry no signature-set work.</summary>
    private HashSet<int>? QueryAtomSignatureAtoms { get; set; }

    /// <summary>Whether the conclusion the sink is currently handing to <see cref="AddClause"/> is a push-landing arrival — set around the three push landings' insertions, consumed by the tag computation; always <see langword="false"/> when the tag machinery is dark.</summary>
    private bool ArrivalIsPush { get; set; }

    /// <summary>Whether THIS arrival's own eliminating step dropped a disjunct that did not participate in the inference — the per-arrival origin declaration the sink folds into the conclusion's <c>DerivedUnderChoice</c> tag. Left <see langword="false"/> by every rule whose residual subtracts the matched participant by construction, the Eq rewrite included since its acting-literal witnesses guarantee a genuine declaration; set <see langword="true"/> only by the inter-nominal carrier when it relays a <c>DerivedUnderChoice</c> source image. The owning site sets and resets it around its single <see cref="AddClause"/> call, exactly as the push landings own <see cref="ArrivalIsPush"/>; every other caller reads it <see langword="false"/>.</summary>
    private bool ArrivalDerivedUnderChoice { get; set; }

    /// <summary>The reusable buffer of the same-context premise clause ids a rule conclusion derives from — the sink's tag-inheritance input where a rule's premise count is dynamic.</summary>
    private List<int> ScratchPremiseIds { get; } = [];

    /// <summary>Whether the push-provenance tag machinery runs: only the license-scoped widening under the fragmented topology reads the tag, so every other cell skips the tag writes, the redundancy joins, and the re-enqueues entirely — dead weight zero on every shipped path.</summary>
    private bool PushTagMachineryLive
    {
        get
        {
            return ParamodulationScope == NominalParamodulationScope.LicenseScoped && Topology == RootContextTopology.PerIndividualRoots;
        }
    }

    /// <summary>The blocked-live latch's consistency-surface arm: whether a scopable rewrite was blocked in a consistency read-off context — under the license scope only root-class contexts can block there (trivial and ground contexts stay exempt), so the root-class counter carries the flag. A run whose fixpoint would assert CONSISTENT while this holds must abstain with attribution instead; a derived inconsistency stays decisive (the widened scope only removes inferences).</summary>
    internal bool HasEqScopeBlockedConsistencyReadOff
    {
        get
        {
            return EqScopeBlockedRootClass > 0;
        }
    }

    /// <summary>The blocked-live latch's query-surface arm: whether any query-initialized context had a scopable rewrite blocked — such a run may not certify a satisfiable or non-subsumption read off the affected surface, so the module's positive verdict is withheld with attribution.</summary>
    internal bool HasEqScopeBlockedQueryReadOff
    {
        get
        {
            return EqScopeBlockedQueryContexts is { Count: > 0 };
        }
    }

    /// <summary>The reusable buffer of a context's live data-demand markers, sorted for the memo signature.</summary>
    private List<int> ScratchLiveDemands { get; } = [];

    /// <summary>The reusable buffer of a context's live data-demand obligations, reconstructed as the sidecar's <c>AlcConcept</c> shapes.</summary>
    private List<AlcConcept> ScratchDataConcepts { get; } = [];

    /// <summary>The reusable map from a reconstructed demand obligation to its marker atom, so a sidecar clash core maps back to the contributing marker clauses.</summary>
    private Dictionary<AlcConcept, int> ScratchConceptToMarker { get; } = [];

    /// <summary>The last live-demand-set signature the data-obligation rule decided per context — the per-context memo that skips re-deciding a context whose demand set did not change.</summary>
    private Dictionary<int, int[]> LastDataSignature { get; } = [];

    /// <summary>The conflict marker sets the sidecar has proven unsatisfiable, per context — the re-emission index that lets a later-landing contributor clause for an already-known conflict emit its clash combinations without re-deciding the oracle.</summary>
    private Dictionary<int, List<int[]>> DataConflictSets { get; } = [];

    /// <summary>The refuted disjunctive data markers per context, each mapped to its narrowing core — the unit-pool markers whose obligations jointly refute it. The disjunctive analogue of <see cref="DataConflictSets"/>: a later-landing contributor or disjunctive clause for an already-refuted marker re-emits its narrowing combinations off this record without re-deciding the oracle, and the fixpoint certification excludes exactly these markers from the survivor joint set.</summary>
    private Dictionary<int, Dictionary<int, int[]>> DisjunctiveConflictSets { get; } = [];

    /// <summary>The live disjunctive data-marker clause ids per non-root context — the concrete re-trigger index that makes the pool-growth re-probe and the contributor re-emission O(affected clauses of the one context) rather than a full structure scan. Lists may carry tombstoned ids; readers filter by liveness.</summary>
    private Dictionary<int, List<int>> LiveDisjunctiveMarkerClauses { get; } = [];

    /// <summary>The per-clause probe memo of the disjunctive data-obligation rule — the sorted live-demand pool signature each disjunctive clause was last probed against, keyed by (context, clause); never the unit rule's per-context signature memo, whose key cannot distinguish two disjunctive clauses of one context. A matching pool signature skips the oracle; each (clause, signature) pair is probed at most once, which bounds the probe count by the finite live-marker powerset.</summary>
    private Dictionary<(int ContextId, int ClauseId), int[]> DisjunctiveProbeSignatures { get; } = [];

    /// <summary>The reusable buffer of a context's sorted live unit demands collected for a disjunctive probe or the fixpoint certification — separate from <see cref="ScratchLiveDemands"/>, which the unit rule owns across its own call chain.</summary>
    private List<int> ScratchDisjunctivePool { get; } = [];

    /// <summary>The reusable buffer of the obligations handed to a disjunctive probe or joint certification decide.</summary>
    private List<AlcConcept> ScratchDisjunctiveConcepts { get; } = [];

    /// <summary>The reusable map from a probed obligation back to its marker atom, so a probe's clash core maps to the refuted marker and its narrowing core.</summary>
    private Dictionary<AlcConcept, int> ScratchDisjunctiveConceptToMarker { get; } = [];

    /// <summary>The reusable buffer of the survivor markers a fixpoint certification joins beyond the unit pool.</summary>
    private List<int> ScratchSurvivorMarkers { get; } = [];

    /// <summary>The reusable residual-head buffer of a narrowing emission — the parent's head minus its refuted data markers, constant across the contributor combinations of one emission.</summary>
    private List<DlLiteral> ScratchResidualHead { get; } = [];

    /// <summary>Whether a data obligation came back undecided at some context during saturation — the delegation signal the reasoner reads (§3.4): a completed saturation without a derived <c>⊥</c> but with an undecided data obligation delegates rather than claim a fragment-relative context verdict.</summary>
    public bool HasUndecidedDataObligation { get; private set; }

    /// <summary>Whether some ground representative's membership in a key class rides only a multi-literal live head at the completed fixpoint — the key join cannot decide whether the key is forced there, so the reasoner delegates rather than claim a consistent module whose forced merge may be missing. A marker DISTINCT from <see cref="HasUndecidedDataObligation"/>.</summary>
    public bool HasUndecidedKeyObligation { get; private set; }

    /// <summary>The interned atoms of the named key classes the module's <c>HasKey</c> descriptors name — the key-obligation latch scans ground contexts for these on disjunctive heads; empty when the module carries no named-class key (an <c>owl:Thing</c> key's membership is never uncertain).</summary>
    private HashSet<int> KeyClassAtoms { get; } = [];

    /// <summary>The module's <c>HasKey</c> ground key descriptors — the vr key join's per-descriptor candidate and agreement source; empty on a module carrying no admitted <c>HasKey</c> axiom.</summary>
    private IReadOnlyList<GroundKeyDescriptor> KeyDescriptors { get; }

    /// <summary>The asserted data-key values per individual key and data property IRI — the value side of the vr key join, compared in the datatype value space, pooled across a spelling's ≈-class at read time.</summary>
    private IReadOnlyDictionary<Utf8String, Dictionary<Utf8String, List<Literal>>> KeyValueStore { get; }

    /// <summary>Whether the vr key join is armed on this engine: the module carries nominal jurisdiction AND at least one <c>HasKey</c> descriptor — a pairing the <c>KeyOnNominalModule</c> guard routes past into intake under the production-on switch (the reasoner threads it on), so this reads <see langword="true"/> on such a module and the root latch, the off-fold backstop, and the root join all run. A nominal module without <c>HasKey</c> and an ordinary <c>HasKey</c> module both read <see langword="false"/>.</summary>
    private bool RootKeyJoinArmed { get; }

    /// <summary>Whether a named key-class membership rides a MULTI-literal live head at a root-class constant at the completed fixpoint (P-GC1): the root key join cannot decide whether the key is forced there, so the reasoner delegates the module named (<c>KeyMembershipUndecidedOnRoot</c>). The root-tier sibling of <see cref="HasUndecidedKeyObligation"/>; set only on an armed engine.</summary>
    public bool HasUndecidedRootKeyObligation { get; private set; }

    /// <summary>The class representative of the individual whose uncertain root key-class membership latched <see cref="HasUndecidedRootKeyObligation"/> — the latch's ≈-resolved diagnostic sample, <c>-1</c> when the latch never fired.</summary>
    public int UndecidedRootKeyIndividual { get; private set; } = -1;

    /// <summary>Whether a live root-class ground equality between two key candidates (or demand-bearing constants) has sides the ≈-class surface did not merge at the completed fixpoint: the off-fold equality relay leaves an identity the read-time union cannot see, so the reasoner delegates the module named (<c>RootEqualityOutsideFold</c>). Set only on an armed engine.</summary>
    public bool HasRootEqualityOutsideFold { get; private set; }

    /// <summary>The number of live root-class heads carrying an off-fold equality between key-candidate or demand-bearing constants at the completed fixpoint — the count behind the <see cref="HasRootEqualityOutsideFold"/> boolean latch, so a corpus run records how many heads drove the backstop delegation rather than only that one did. Zero on an unarmed engine and when the fold covered every root equality.</summary>
    public long RootEqualityOutsideFoldHeads { get; private set; }

    /// <summary>Whether a <c>DerivedUnderChoice</c> root equality was refused a guard site at some context during saturation (the origin-bit relay guard's general latch): a root equality whose derivation dropped a non-participating disjunct rides a choice, so the ≈-class fold, the Pred relay, the r-Pred broadcast, and the unconditional-head projection all refuse it and the reasoner delegates the module named (<c>RootEqualityRidesAChoice</c>) rather than trust a merge an unrecorded drop manufactured. General (no key-join gate), so it fires for a plain nominal module; monotone and sticky — armed inline at the refusing check, never reset. Dark on every path where the Eq acting literal stays well-formed.</summary>
    public bool HasRootEqualityRidesAChoice { get; private set; }

    /// <summary>The number of guard-site refusals of a <c>DerivedUnderChoice</c> root equality across the saturation — the count behind the <see cref="HasRootEqualityRidesAChoice"/> boolean latch, so a corpus run records how many refusals drove the delegation rather than only that one did. Zero when no choice-riding equality was refused.</summary>
    public long RootEqualityRidesAChoiceHeads { get; private set; }

    /// <summary>Whether the conditionality-loss lint is armed on this engine — the default-off, zero-cost-unarmed census switch. When <see langword="false"/> the lint block at the <see cref="AddClause"/> funnel pays one predicted-not-taken branch and never walks <c>premiseIds</c> a second time; the switch is internal and test-only, flipped through the redrive seam, and is not tied to any options-seam value.</summary>
    private bool ConditionalityLintArmed { get; set; }

    /// <summary>Whether the conditionality-loss lint fired at any derivation step during saturation — the sticky boolean latch behind <see cref="WellKnownEpistemicReasons.ConditionalityLossLint.ConditionalityDropped"/> (code 30000). This is a ground-truth-free mechanism census that fires on harmless-by-design instances of the strict head-disjunct narrowing, not a verdict signal; it performs no registry lookup on the hot path — code 30000 is the wire identity a later projecting host joins to this count. Zero-latched on an unarmed engine.</summary>
    public bool HasConditionalityDropped { get; private set; }

    /// <summary>The number of derivation steps across the saturation whose normalized head was strictly narrower in choice-conditions than their widest premise — the count behind the <see cref="HasConditionalityDropped"/> boolean latch, so a corpus run records how many steps the lint observed rather than only that one did. Zero on an unarmed engine.</summary>
    public long ConditionalityDroppedCount { get; private set; }

    /// <summary>The data property IRI a root key-join value comparison answered <c>Indeterminate</c> — the join can neither merge nor treat the values as distinct, so the reasoner delegates the module named (<c>KeyValueComparisonIndeterminate</c>); the default when no comparison was indeterminate.</summary>
    public Utf8String RootKeyIndeterminateProperty { get; private set; }

    /// <summary>Whether a delegate-backed (self-certified) registered datatype decided a data obligation at some context during saturation — the provenance signal the reasoner names on the module remainder.</summary>
    public bool HasSelfCertifiedDataDecision { get; private set; }

    /// <summary>The deduplicated backing store of <see cref="UndecidedDataObligationProperties"/>.</summary>
    private List<string> UndecidedDataObligationPropertiesList { get; } = [];

    /// <summary>
    /// Diagnostics only: the demand property IRIs, deduplicated, of the live-demand
    /// set recorded each time <see cref="HasUndecidedDataObligation"/> is set —
    /// which data properties were live when the sidecar could not decide the
    /// obligation. Never recorded on a budget stop, since a budget-stopped
    /// invocation does not own the undecided outcome.
    /// </summary>
    public IReadOnlyList<string> UndecidedDataObligationProperties => UndecidedDataObligationPropertiesList;

    /// <summary>The non-empty-body ontology clauses, indexed for the Hyper given-clause trigger, including the virtual <c>Bottom(x)→⊥</c> clause.</summary>
    private List<DlClause> NonEmptyOntologyClauses { get; } = [];

    /// <summary>The indexes into <see cref="NonEmptyOntologyClauses"/> of the Nom-eligible ontology clauses — non-empty heads consisting entirely of a-equalities (the DL4 counting shape); the Nom rule joins their bodies in the root context under <c>σ(x) = o</c>.</summary>
    private List<int> NomEligibleOntologyClauses { get; } = [];

    /// <summary>Whether each non-empty ontology clause is Nom-eligible, by index — the Nom dispatch filter over the shared Hyper trigger indexes.</summary>
    private List<bool> NomEligibleByClause { get; } = [];

    /// <summary>The Nom sibling count <c>K</c>: <c>K + 1</c> is the largest neighbour-variable index across the module's DL-clauses (too few siblings loses completeness, oversized breaks the budget wedge); computed once at engine construction from the frozen clause set.</summary>
    private int NomSiblingCount { get; }

    /// <summary>The root-Pred-eligible clause ids of the root context, keyed by each individual its nonground <c>Sur</c> body atoms name — the r-Pred per-<c>oi</c> sweep; lists may carry tombstoned ids.</summary>
    private Dictionary<int, List<int>> RootPredEligibleByIndividual { get; } = [];

    /// <summary>The empty-body ontology clauses, fired once into every context at creation (Hyper with no premises).</summary>
    private List<DlClause> EmptyBodyOntologyClauses { get; } = [];

    /// <summary>The ontology body positions holding a concept atom <c>B(x)</c>, keyed by the concept id — the Hyper trigger lookup for a landed concept head.</summary>
    private Dictionary<int, List<(int ClauseIndex, int Position)>> OntologyConceptBody { get; } = [];

    /// <summary>The ontology body positions holding a role atom, keyed by the role symbol and whether the central variable is its first argument — the Hyper trigger lookup for a landed role head.</summary>
    private Dictionary<(int RoleSymbol, bool CentralFirst), List<(int ClauseIndex, int Position)>> OntologyRoleBody { get; } = [];

    /// <summary>The successor-trigger atoms Succ ranges over: <c>Su(O)</c> from the clausifier plus the virtual clause's <c>Bottom(x)</c> body atom, so a Bottom successor filler can condemn its predecessor.</summary>
    private List<DlLiteral> SuccessorTriggers { get; } = [];

    /// <summary>The predecessor-trigger atoms <c>Pr(O)</c> deciding Pred eligibility: the clausifier's set plus <c>Bottom(y)</c> for the virtual clause's Bottom occurrence (KR 2016 Definition 2 over the extended clause set).</summary>
    private HashSet<DlLiteral> PredecessorTriggers { get; }

    /// <summary>The unique concept filler of each function symbol that occurs in the ontology clause heads in exactly one concept atom <c>B(f(x))</c>; the cautious strategy reads it. Absence means no unique filler.</summary>
    private Dictionary<int, int> UniqueFillerByFunction { get; } = [];

    /// <summary>The DISTINCT concept filler atoms of <see cref="UniqueFillerByFunction"/>, deduplicated at construction — the complete single-atom-core candidate set of the cautious successor strategy, whose by-core resolution takes a filler atom and nothing else. Two functions sharing one filler describe one candidate core, so the values are folded rather than listed per function.</summary>
    private List<int> DistinctFillerAtoms { get; } = [];

    /// <summary>The signature-bounded ceiling of the cautious registry's single-atom-core fill — the number of distinct filler atoms any run could ever register a core context for; fixed at construction because the signature is frozen.</summary>
    private int CautiousCoreCeiling
    {
        get
        {
            return DistinctFillerAtoms.Count;
        }
    }

    /// <summary>The filler cores the context registry currently holds a context for — the fill numerator read against <see cref="CautiousCoreCeiling"/>, computed at read time by probing the registry. REGISTRY STATE is what is counted, never provenance: a filler core a ground or query tier registered counts as filled, because the fill question is existence.</summary>
    private int CautiousCoresRegistered
    {
        get
        {
            return CountCautiousCoresRegistered();
        }
    }

    /// <summary>The rule queue of clause-landed events, drained completely between Succ candidates (the eager-Hyper discipline).</summary>
    private Queue<(int ContextId, int ClauseId)> RuleQueue { get; } = [];

    /// <summary>The EAGER rule queue of Join-derived clause-landed events, drained before <see cref="RuleQueue"/> — eager Join is a termination condition of the nominal calculus (arXiv:1805.01396 Proposition 1: the ground-clash ladder must close before other work piles up).</summary>
    private Queue<(int ContextId, int ClauseId)> EagerRuleQueue { get; } = [];

    /// <summary>Whether the clause currently being added derives from the Join rule, routing its landed event to <see cref="EagerRuleQueue"/>.</summary>
    private bool EnqueueEagerly { get; set; }

    /// <summary>The Succ candidate queue of <c>(context, trigger)</c> pairs — the trigger is the packed <c>f(x)</c> term in an ordinary context or the packed <c>f(o)</c> term on the root context (the rule fires per anchoring constant there) — processed one candidate per rule-queue drain.</summary>
    private Queue<(int ContextId, DlTerm Trigger)> SuccQueue { get; } = [];

    /// <summary>The Succ candidates currently pending, so a candidate is enqueued at most once while it waits.</summary>
    private HashSet<(int ContextId, DlTerm Trigger)> PendingSucc { get; } = [];

    /// <summary>The module's symbol table — the root-context machinery reads the individual census from it and the Nom rule mints generated nominals through its bounded channel.</summary>
    private ContextSymbolTable Symbols { get; }

    /// <summary>The reusable map from a Pred completion slot to the target body position it resolves — ground body conjuncts take no slot, so the slot list and the body span no longer align by index.</summary>
    private List<int> ScratchPredSlotPositions { get; } = [];

    /// <summary>The reusable buffer of a predecessor's live broadened ground successor-trigger heads (<c>S(o, o′)</c> / <c>o ≈ o′</c>), collected per Succ candidate beside the materialized templates; the Join bridge dispatch reuses it for a context's ground body literals.</summary>
    private List<DlLiteral> ScratchGroundTriggers { get; } = [];

    /// <summary>The reusable snapshot of Join dispatch candidate ids, so insertions during the resolutions do not perturb the enumeration.</summary>
    private List<int> ScratchJoinDispatch { get; } = [];

    /// <summary>The reusable OUTER snapshot of Join premise-one ids for the bridge dispatch, kept apart from <see cref="ScratchJoinDispatch"/> because the nested abstraction loop refills that buffer.</summary>
    private List<int> ScratchJoinPremises { get; } = [];

    /// <summary>The n-zero r-Pred broadcast conclusions accumulated so far — context-independent <c>σ = {y↦x}</c> images every existing context received at derivation time and every later-created context receives at seeding. An image's POSITION in this list is what a context's containment record and the ordinary Pred arm's containment skip read.</summary>
    private List<DlClause> RootBroadcastClauses { get; } = [];

    /// <summary>The broadcast-list position of each sigma-invariant broadcast image — an image whose body and head literals are all ground, so every Pred substitution leaves it fixed and its Pred conclusion into any predecessor is the image itself. Keyed by clause CONTENT, the key ignoring origin, so a context holding a rebuilt copy is still recognised; a repeated image keeps its FIRST position, which every context receives no later than. Written only where an image is broadcast, read only by the ordinary Pred arm's containment skip.</summary>
    private Dictionary<DlClause, int> BroadcastInvariantImageIndex { get; } = new(DlClauseSpanComparer.Instance);

    /// <summary>The live measurement view of the n-zero r-Pred broadcast images, in derivation order — the reference universe a provenance recognizer builds its set from, caught up by count watermark as the list grows. Measurement-only: no rule consults it, and no caller mutates it.</summary>
    internal IReadOnlyList<DlClause> RootBroadcastImages
    {
        get
        {
            return RootBroadcastClauses;
        }
    }

    /// <summary>The n-zero r-Pred broadcast population accumulated so far — read off the image list rather than carried in a counter, since the list is the truth. One shared image is offered into every ordinary context, so this count is the population axis's own size rather than a share of the offer flood.</summary>
    private int RootBroadcastClauseCount
    {
        get
        {
            return RootBroadcastClauses.Count;
        }
    }

    /// <summary>One entry of the ground-conjunct re-offer index: a swept eligible root clause carried by id, or an n-zero broadcast carried as an index into <see cref="RootBroadcastClauses"/> (a root clause id cannot recover the broadcast image, so broadcast entries reference the stored image directly). Exactly one of the two fields is non-negative.</summary>
    /// <param name="RootClauseId">The eligible root clause's id for a swept entry, or <c>-1</c> for a broadcast entry.</param>
    /// <param name="BroadcastImageIndex">The index into <see cref="RootBroadcastClauses"/> for a broadcast entry, or <c>-1</c> for a swept entry.</param>
    private readonly record struct RootPredReofferEntry(int RootClauseId, int BroadcastImageIndex);

    /// <summary>The re-offer index of the ground-relevance filter: every ground body conjunct of an r-Pred-eligible root clause (swept, by root clause id) or of an n-zero broadcast image (by <see cref="RootBroadcastClauses"/> index) maps to its carriers — the root-level mirror of <see cref="Context.PredEligibleWithBody"/>'s body keying. Maintained only under the filtered mode; consumed by the re-offer triggers.</summary>
    private Dictionary<DlLiteral, List<RootPredReofferEntry>> RootPredEligibleByGroundConjunct { get; } = [];

    /// <summary>The distinct ground-conjunct keys of <see cref="RootPredEligibleByGroundConjunct"/> mentioning each individual in a bare-individual slot — the bridge-premise re-offer's per-constant enumeration when an <c>x ≈ o</c> premise lands (a <c>f(o)</c> slot yields no bridge candidate and is never indexed here). Maintained only under the filtered mode.</summary>
    private Dictionary<int, List<DlLiteral>> RootPredGroundConjunctsByIndividual { get; } = [];

    /// <summary>The ground atoms the downward relevance compensation has flooded from each context — the per-context source set that seeds each atom into the context's ordinary successors exactly once at selection and again over each later-created outgoing edge. Maintained only under the filtered mode.</summary>
    private Dictionary<int, HashSet<DlLiteral>> RelevanceSeededGroundAtoms { get; } = [];

    /// <summary>The new-key gate's dispatched-key sets, per context: a re-offer fires at the selection walk when a processed maximal literal's qualification key is NOT yet in its context's set, and marking the key keeps later same-key landings from re-running the re-offer while the qualification stays live. The mark is keyed to the QUALIFICATION, never to a clause id, so a credited clause tombstoned before its worklist turn loses nothing — the next processed live holder fires instead. A blocked offer RE-ARMS the marks of its failing conjunct's qualification keys (<see cref="ReArmRelevanceKeys"/>), so a key whose holders all died and whose offers blocked in the dead window fires again on its next live landing.</summary>
    private Dictionary<int, HashSet<DlLiteral>> DispatchedRelevanceKeys { get; } = [];

    /// <summary>The r-Pred emission origin whose applications are currently being landed — set at each of the four origins (registration sweep, new root edge, landed premise, broadcast) before its <see cref="ApplyRootPred"/> calls, so the shared landing site can attribute per origin. The engine is single-threaded and no origin's flow nests inside another's.</summary>
    private RootPredOrigin CurrentRootPredOrigin { get; set; }

    /// <summary>The four r-Pred emission origins the per-origin attribution counters partition <see cref="RootPredApplications"/> by.</summary>
    private enum RootPredOrigin
    {
        /// <summary>The eligible clause's own sweep at registration.</summary>
        RegistrationSweep,

        /// <summary>The sweep over a newly added root edge.</summary>
        NewRootEdge,

        /// <summary>A landed-premise re-attempt restricted to one context — the site-2 <c>Sur</c>-image dispatch and the filtered mode's relevance re-offers.</summary>
        Premise,

        /// <summary>The n-zero broadcast path — the live broadcast, the seeding replay, and the filtered mode's re-offered replays.</summary>
        Broadcast,
    }

    /// <summary>The Pred emission driver whose completions are currently being landed — set at each of the three drivers (landed target, landed premise, new edge) before its attempts, so the shared landing site can attribute per driver. The engine is single-threaded and no driver's flow nests inside another's: the offer path only enqueues, and the sole dispatcher is the saturation drain.</summary>
    private PredOrigin CurrentPredOrigin { get; set; }

    /// <summary>The three Pred emission drivers the per-driver attribution counters partition <see cref="PredOffers"/> by.</summary>
    private enum PredOrigin
    {
        /// <summary>A landed Pred-eligible target swept back over the successor's incoming edges.</summary>
        LandedTarget,

        /// <summary>A landed predecessor premise re-attempted over the predecessor's outgoing edges, pinned at the matching target body position.</summary>
        LandedPremise,

        /// <summary>The sweep over a newly added function edge, against the successor's existing Pred-eligible clauses.</summary>
        NewEdge,
    }

    /// <summary>The r-Pred offers the ground-relevance filter blocked, both paths; zero under the unrestricted default.</summary>
    private long RootPredFilteredOffers { get; set; }

    /// <summary>The r-Pred re-offers the relevance triggers fired — swept re-attempts and broadcast replays together; zero under the unrestricted default.</summary>
    private long RootPredReofferedByGroundHead { get; set; }

    /// <summary>The downward relevance tautologies <c>A → A</c> the compensation inserted; zero under the unrestricted default.</summary>
    private long RelevanceTautologiesSeeded { get; set; }

    /// <summary>The r-Pred applications landed from the registration sweep origin.</summary>
    private long RootPredRegistrationSweepLandings { get; set; }

    /// <summary>The r-Pred applications landed from the new-root-edge origin.</summary>
    private long RootPredNewRootEdgeLandings { get; set; }

    /// <summary>The r-Pred applications landed from the landed-premise origin.</summary>
    private long RootPredPremiseLandings { get; set; }

    /// <summary>The r-Pred applications landed from the broadcast origin.</summary>
    private long RootPredBroadcastLandings { get; set; }

    /// <summary>The r-Pred conclusions offered to a context from the registration-sweep origin, landed or not — charged once per conclusion that reached the insertion gate, so the offer-versus-landing pair reads the origin's accept rate rather than only its landings.</summary>
    private long RootPredRegistrationSweepOffers { get; set; }

    /// <summary>The r-Pred conclusions offered to a context from the new-root-edge origin, landed or not.</summary>
    private long RootPredNewRootEdgeOffers { get; set; }

    /// <summary>The r-Pred conclusions offered to a context from the landed-premise origin, landed or not.</summary>
    private long RootPredPremiseOffers { get; set; }

    /// <summary>The r-Pred conclusions offered to a context from the n-zero broadcast origin, landed or not.</summary>
    private long RootPredBroadcastOffers { get; set; }

    /// <summary>The registration-sweep origin's offers the insertion gate absorbed as EXACT DUPLICATES — the origin-keyed share of <see cref="DuplicateContainmentHits"/>; a subsumer absorption charges nothing here.</summary>
    private long RootPredRegistrationSweepDuplicateHits { get; set; }

    /// <summary>The new-root-edge origin's offers the insertion gate absorbed as exact duplicates.</summary>
    private long RootPredNewRootEdgeDuplicateHits { get; set; }

    /// <summary>The landed-premise origin's offers the insertion gate absorbed as exact duplicates.</summary>
    private long RootPredPremiseDuplicateHits { get; set; }

    /// <summary>The broadcast origin's offers the insertion gate absorbed as exact duplicates.</summary>
    private long RootPredBroadcastDuplicateHits { get; set; }

    /// <summary>The registration-sweep origin's offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges the duplicate counter instead.</summary>
    private long RootPredRegistrationSweepSubsumedHits { get; set; }

    /// <summary>The new-root-edge origin's offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges the duplicate counter instead.</summary>
    private long RootPredNewRootEdgeSubsumedHits { get; set; }

    /// <summary>The landed-premise origin's offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges the duplicate counter instead.</summary>
    private long RootPredPremiseSubsumedHits { get; set; }

    /// <summary>The broadcast origin's offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges the duplicate counter instead.</summary>
    private long RootPredBroadcastSubsumedHits { get; set; }

    /// <summary>The join-family conclusions offered to a context, landed or not — charged at the single join conclusion sink, so it stands at or above <see cref="JoinApplications"/> by construction.</summary>
    private long JoinOffers { get; set; }

    /// <summary>The join-family offers the insertion gate absorbed as EXACT DUPLICATES — the origin-keyed share of <see cref="DuplicateContainmentHits"/> charged at the join sink; a subsumer absorption charges nothing here.</summary>
    private long JoinDuplicateHits { get; set; }

    /// <summary>The join-family offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/> charged at the join sink; an exact duplicate charges <see cref="JoinDuplicateHits"/> instead.</summary>
    private long JoinSubsumedHits { get; set; }

    /// <summary>The join-family offering RUNS: one per dispatched (landed clause, dispatch face) that charged at least one offer. The counter is charged LAZILY, on the run's first charged offer, because the join family has no single combination cursor — the four dispatch arms of one run offer independently — so a run that reaches the dispatcher and finds no candidate contributes nothing. This is the ONE semantic difference from <see cref="PredOdometerRuns"/>, which counts cursor-reaching invocations whether or not they offer.</summary>
    private long JoinOfferingRuns { get; set; }

    /// <summary>Whether the join dispatch run in progress has already charged an offer — cleared at each dispatch root and set after every charged offer, so the next duplicate outcome of the same run is attributable to the run itself rather than to an earlier dispatch.</summary>
    private bool JoinRunHasOffered { get; set; }

    /// <summary>The join exact-duplicate absorptions that landed on a run's SECOND or later charged offer — the within-run share of <see cref="JoinDuplicateHits"/>, read as an upper-bound proxy for intra-run convergence, since a later-in-run duplicate may still absorb against a clause that predates the run. Two distinct partners of one enumeration can land the same conclusion when the fixed premise's own spans absorb the partners' distinguishing literals into the offered union, so a within-run duplicate can arise inside a single dispatch arm as well as across positions or arms.</summary>
    private long JoinIntraRunDuplicateHits { get; set; }

    /// <summary>The Core seeds offered to a context, landed or not — charged once per conclusion that reached the insertion gate.</summary>
    private long CoreOffers { get; set; }

    /// <summary>The Core seeds the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long CoreDuplicateHits { get; set; }

    /// <summary>The Core seeds the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="CoreDuplicateHits"/> instead.</summary>
    private long CoreSubsumedHits { get; set; }

    /// <summary>The Hyper conclusions offered to a context, landed or not.</summary>
    private long HyperOffers { get; set; }

    /// <summary>The Hyper offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long HyperDuplicateHits { get; set; }

    /// <summary>The Hyper offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="HyperDuplicateHits"/> instead.</summary>
    private long HyperSubsumedHits { get; set; }

    /// <summary>The Pred conclusions offered to a predecessor, landed or not.</summary>
    private long PredOffers { get; set; }

    /// <summary>The Pred offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long PredDuplicateHits { get; set; }

    /// <summary>The Pred offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="PredDuplicateHits"/> instead.</summary>
    private long PredSubsumedHits { get; set; }

    /// <summary>The Pred conclusions offered to a predecessor from the landed-target driver, landed or not — the driver-keyed share of <see cref="PredOffers"/>.</summary>
    private long PredLandedTargetOffers { get; set; }

    /// <summary>The Pred conclusions offered to a predecessor from the landed-premise driver, landed or not.</summary>
    private long PredLandedPremiseOffers { get; set; }

    /// <summary>The Pred conclusions offered to a predecessor from the new-edge driver, landed or not.</summary>
    private long PredNewEdgeOffers { get; set; }

    /// <summary>The landed-target driver's offers the insertion gate absorbed as EXACT DUPLICATES — the driver-keyed share of <see cref="PredDuplicateHits"/>, itself the origin-keyed share of <see cref="DuplicateContainmentHits"/>; a subsumer absorption charges nothing here.</summary>
    private long PredLandedTargetDuplicateHits { get; set; }

    /// <summary>The landed-premise driver's offers the insertion gate absorbed as exact duplicates.</summary>
    private long PredLandedPremiseDuplicateHits { get; set; }

    /// <summary>The new-edge driver's offers the insertion gate absorbed as exact duplicates.</summary>
    private long PredNewEdgeDuplicateHits { get; set; }

    /// <summary>The landed-target driver's offers the insertion gate absorbed into a strictly more general live clause — the driver-keyed share of <see cref="PredSubsumedHits"/>, itself the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges the duplicate counter instead.</summary>
    private long PredLandedTargetSubsumedHits { get; set; }

    /// <summary>The landed-premise driver's offers the insertion gate absorbed into a strictly more general live clause.</summary>
    private long PredLandedPremiseSubsumedHits { get; set; }

    /// <summary>The new-edge driver's offers the insertion gate absorbed into a strictly more general live clause.</summary>
    private long PredNewEdgeSubsumedHits { get; set; }

    /// <summary>The Pred applications landed from the landed-target driver — the driver-keyed share of <see cref="PredApplications"/>, completing the driver split at the granularity the offer and duplicate counters already carry.</summary>
    private long PredLandedTargetLandings { get; set; }

    /// <summary>The Pred applications landed from the landed-premise driver.</summary>
    private long PredLandedPremiseLandings { get; set; }

    /// <summary>The Pred applications landed from the new-edge driver.</summary>
    private long PredNewEdgeLandings { get; set; }

    /// <summary>The Pred odometer invocations that reached their combination cursor — an attempt refused earlier, because some nonground target body position had no live premise, charges nothing. The zero-slot degenerate run of an empty nonground body reaches the cursor and counts.</summary>
    private long PredOdometerRuns { get; set; }

    /// <summary>Whether the Pred odometer run in progress has already charged an offer — cleared as each run reaches its cursor and set after every charged offer, so the next duplicate outcome of the same run is attributable to the run itself rather than to an earlier invocation.</summary>
    private bool PredRunHasOffered { get; set; }

    /// <summary>The Pred exact-duplicate absorptions that landed on a run's SECOND or later charged offer — the within-run successor-offer duplicate count, read against <see cref="PredDuplicateHits"/> as an upper-bound proxy for intra-odometer convergence, since a later-in-run duplicate may still absorb against a clause that predates the run. A single slot cannot supply one: two live premises of one slot cannot share both body and residual without being one clause, so every within-run duplicate is a cross-slot coincidence.</summary>
    private long PredIntraRunDuplicateHits { get; set; }

    /// <summary>The <see cref="AttemptPred"/> dispatches whose predecessor runs the constant-anchored root machinery — the arm that fans one target out over the anchoring constants. Charged at the arm's entry, and scoped to <see cref="AttemptPred"/> alone: the landed-premise path reads its single anchor off the premise's own terms, so it charges neither arm counter.</summary>
    private long PredAnchoredArmDispatches { get; set; }

    /// <summary>The <see cref="AttemptPred"/> dispatches whose predecessor takes the ordinary arm — every non-root predecessor and every nominal root, which spells its own constant central. Charged at the arm's entry, under the same <see cref="AttemptPred"/> scope as <see cref="PredAnchoredArmDispatches"/>, so the two together count every dispatch.</summary>
    private long PredOrdinaryArmDispatches { get; set; }

    /// <summary>The anchored-arm dispatches whose target is anchor-invariant — every body AND every head literal ground, so the sigma of each anchoring constant leaves both spans alone and every constant completes the same conclusion. Charged on the test's verdict, whether or not the surviving completion then runs.</summary>
    private long PredAnchorInvariantTargetPasses { get; set; }

    /// <summary>The Pred offers the anchor hoist elided: the completions the anchored arm's remaining constants would have charged on an anchor-invariant target, credited after the surviving completion whenever the next constant's own gate would have admitted it. The credit is EXACT on unbounded and population-bounded runs, where it reproduces that gate's decision; under an attempt bound it stands as an upper bound, since the loop it replaces could have latched at any constant.</summary>
    private long PredAnchorPruned { get; set; }

    /// <summary>The Pred offers the ordinary arm elided: the completions a sigma-invariant broadcast image the predecessor already holds would have charged. Every Pred substitution leaves such a target fixed, so the completion IS the target clause and the predecessor's own delivery record proves that content already absorbed there. Credited whenever the elided offer's own gate would have admitted it, so the credit is EXACT on unbounded and population-bounded runs and stands as an upper bound under an attempt bound, exactly as <see cref="PredAnchorPruned"/> does.</summary>
    private long PredBroadcastContainedSkips { get; set; }

    /// <summary>The ordinary-arm dispatches whose target is sigma-invariant — every body AND every head literal ground, so the ordinary sigma leaves both spans alone and the one completion is the target itself. Charged on the test's verdict, whether or not the containment skip then fires; the ordinary arm's counterpart of <see cref="PredAnchorInvariantTargetPasses"/>.</summary>
    private long PredOrdinaryInvariantTargetPasses { get; set; }

    /// <summary>The sigma-invariant ordinary-arm targets that are REGISTERED broadcast images, whether or not the predecessor holds the image. Read against <see cref="PredOrdinaryInvariantTargetPasses"/> it prices the locally-derived all-ground residue; read against <see cref="PredBroadcastContainedSkips"/> it prices the residue no predecessor has been delivered yet.</summary>
    private long PredBroadcastImageTargets { get; set; }

    /// <summary>The origin-merge re-enqueues of a surviving absorber — the one re-entry into the dispatch loop the per-rule offer counters cannot see; zero wherever no clause carries the choice tag, whose push-side twin is counted by <see cref="EqScopeTagJoins"/>.</summary>
    private long OriginClearReenqueues { get; set; }

    /// <summary>The Eq rewrite conclusions offered to a context, landed or not — charged past the constant, scope, and tautology gates, which spend no budget and reach no insertion gate.</summary>
    private long EqOffers { get; set; }

    /// <summary>The Eq offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long EqDuplicateHits { get; set; }

    /// <summary>The Eq offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="EqDuplicateHits"/> instead.</summary>
    private long EqSubsumedHits { get; set; }

    /// <summary>The Eq offering RUNS: one per dispatched (landed clause, maximal literal) that charged at least one offer, both rewrite directions included, and one per redrive firing. The counter is charged LAZILY, on the run's first charged offer, because an Eq dispatch has no single combination cursor — it enumerates snapshots in two directions — so a run that reaches the dispatcher and rewrites nothing contributes nothing. This is the ONE semantic difference from <see cref="PredOdometerRuns"/>, which counts cursor-reaching invocations whether or not they offer.</summary>
    private long EqOfferingRuns { get; set; }

    /// <summary>Whether the Eq dispatch run in progress has already charged an offer — cleared at each dispatch root and at each redrive entry, set after every charged offer, so the next duplicate outcome of the same run is attributable to the run itself rather than to an earlier dispatch.</summary>
    private bool EqRunHasOffered { get; set; }

    /// <summary>The Eq exact-duplicate absorptions that landed on a run's SECOND or later charged offer — the within-run share of <see cref="EqDuplicateHits"/>, read as an upper-bound proxy for intra-run convergence, since a later-in-run duplicate may still absorb against a clause that predates the run. Two firings of one dispatch land the same conclusion only through distinct rewrite targets, or through distinct acting sides, whose rewrites coincide.</summary>
    private long EqIntraRunDuplicateHits { get; set; }

    /// <summary>The equality-factoring conclusions offered to a context, landed or not — charged past the pre-charge tautology gate.</summary>
    private long FactorOffers { get; set; }

    /// <summary>The Factor offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long FactorDuplicateHits { get; set; }

    /// <summary>The Factor offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="FactorDuplicateHits"/> instead.</summary>
    private long FactorSubsumedHits { get; set; }

    /// <summary>The Succ hypothesis and unconditional-K1 seeds offered to a successor, landed or not — the ONE expansion charged by <see cref="SuccApplications"/> offers a whole K2 set and, at a designated ground target, its K1 set as well, so this counter stands ABOVE the expansion count rather than beside it.</summary>
    private long SuccOffers { get; set; }

    /// <summary>The Succ seed offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long SuccDuplicateHits { get; set; }

    /// <summary>The Succ seed offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="SuccDuplicateHits"/> instead.</summary>
    private long SuccSubsumedHits { get; set; }

    /// <summary>The Nom disjunction conclusions offered to a root context, landed or not.</summary>
    private long NomOffers { get; set; }

    /// <summary>The Nom offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long NomDuplicateHits { get; set; }

    /// <summary>The Nom offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="NomDuplicateHits"/> instead.</summary>
    private long NomSubsumedHits { get; set; }

    /// <summary>The push-landing arrivals offered to a root-class context, landed or not — the r-Succ seed landings and the inter-nominal carrier images together, counted at the one physical seam both origins reach the clause set through.</summary>
    private long PushedArrivalOffers { get; set; }

    /// <summary>The push-landing arrivals the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>; the duplicate-image absorption the carrier-cascade convergence argument leans on is counted here.</summary>
    private long PushedArrivalDuplicateHits { get; set; }

    /// <summary>The push-landing arrivals the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="PushedArrivalDuplicateHits"/> instead.</summary>
    private long PushedArrivalSubsumedHits { get; set; }

    /// <summary>The sidecar-driven seeds offered to a context, landed or not — the root data clash, the data clash combinations, the disjunctive data narrowings, and the downward relevance tautologies together; zero on a run driving neither an admitted data restriction nor the ground-filtered relevance mode.</summary>
    private long SidecarSeedOffers { get; set; }

    /// <summary>The sidecar-driven seed offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>.</summary>
    private long SidecarSeedDuplicateHits { get; set; }

    /// <summary>The sidecar-driven seed offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="SidecarSeedDuplicateHits"/> instead.</summary>
    private long SidecarSeedSubsumedHits { get; set; }

    /// <summary>Whether the generated-nominal population outgrew the packed <c>f(o)</c> individual width mid-saturation — the runtime face of the clausification bound check; the reasoner delegates the module named (<c>PackedTermWidthExceeded</c>).</summary>
    public bool HasPackedWidthOverflow { get; private set; }

    /// <summary>The Join-rule applications: intra-context ground resolutions added.</summary>
    private long JoinApplications { get; set; }

    /// <summary>The r-Succ applications: root edges opened with their tautology seeds.</summary>
    private long RootSuccApplications { get; set; }

    /// <summary>The r-Pred applications: root-clause completions landed in ordinary contexts, the n-zero broadcasts included.</summary>
    private long RootPredApplications { get; set; }

    /// <summary>The Nom applications: generated-nominal disjunctions added to the root context.</summary>
    private long NomApplications { get; set; }

    /// <summary>The largest live clause count any root-class context reached — a watermark over the whole root class; zero without nominal jurisdiction.</summary>
    private int RootContextClauses { get; set; }

    /// <summary>Whether a saturation rule derived a clause whose head left the context grammar — the promoted in-saturation guard's latch: the clause is not inserted and the reasoner delegates the module named rather than trust a verdict over an unproven shape. Latching is always sound; the derivability argument admits every shape a rule can legally derive, so a latch marks a defect or a genuinely new shape, never routine traffic.</summary>
    public bool HasOutOfGrammarDerivation { get; private set; }

    /// <summary>The first out-of-grammar derived clause, rendered with its context kind — the latch's diagnostic sample: it names the exact refused shape a delegation was latched on, at zero cost when the latch never fires. <see cref="ContextSaturationStatistics.OutOfGrammarConclusions"/> counts every refusal; this sample identifies the first.</summary>
    public string? OutOfGrammarSample { get; private set; }

    /// <summary>Whether a data-demand marker head landed ON a root context that the per-constant root arm did not decide (<c>D(o)</c> — a demand instantiated at a constant) — the arm's activity statistic. It is set when a unit demand lands while <see cref="RootDataObligationsEnabled"/> is off (a raw-engine census run, so the arm never runs) and when a non-narrowing disjunctive root marker lands, so a census reads whether any root data demand reached a root context. Delegation is governed by <see cref="HasDataObligationUndecidedOnRoot"/>, not by this statistic.</summary>
    public bool RootDataDemandObserved { get; private set; }

    /// <summary>The optional in-saturation progress sampler: when attached, one <see cref="SaturationProgressTraceEvent"/> is emitted at every power-of-two <see cref="InferenceAttempts"/> mark, carrying the population, funnel, and queue-depth columns at that mark; <see langword="null"/> is the zero-cost default (one predicted-not-taken branch per charged attempt).</summary>
    public SaturationProgressSampler? Progress { get; set; }

    /// <summary>The optional per-landing Eq measurement probe: when attached, invoked with the landing context on every landed Eq application — the probe-side attribution seam for the per-<c>v_o</c> Eq-front reads; <see langword="null"/> is the zero-cost default (one null check per landing).</summary>
    public EqLandingProbeDelegate? EqLandingProbe { get; set; }

    /// <summary>The run's record-only recognizer slots — the ONE extension surface an observation point attaches through; every slot is unattached by default and a recognizer observes without ever deciding.</summary>
    internal SaturationRecognizerRegistry Recognizers { get; } = new();

    /// <summary>The root-tier ≈-class surface — the monotone union-find over interned individual ids the equality-head clause landing feeds; <see langword="null"/> until the first root-landed equality, so a nominal-free module allocates none. This is the equality-head <see cref="AddClause"/> LANDING feed, a distinct site from the <see cref="EqLandingProbe"/> re-probe hook, and the two are never collapsed onto one another. Dark this step: no production consumer resolves through it.</summary>
    private RootApproxClasses? RootClasses { get; set; }

    /// <summary>The reusable buffer of an ≈-class's individual-id spellings the dark root-tier co-occurrence reads walk.</summary>
    private List<int> ScratchApproxMembers { get; } = [];

    /// <summary>The per-constant root-tier data-obligation arm's switch: ON in production, so a data demand landing at a constant on a root context is decided PER ≈-CLASS off the pooled read-time union, the re-probe hook re-decides a merged class, and a non-narrowing disjunctive root marker delegates named through <see cref="HasDataObligationUndecidedOnRoot"/>. A raw-engine run leaves it off, so a landed root demand records only the <see cref="RootDataDemandObserved"/> census statistic without the arm running. The root key-join lift's engine-armed sibling stays a separate switch.</summary>
    public bool RootDataObligationsEnabled { get; set; }

    /// <summary>Whether a per-constant root-tier data obligation came back UNDECIDED at some ≈-class: the datatype sidecar could neither realize nor refute the class's pooled demand set, so the reasoner delegates the module named (<c>DataObligationUndecidedOnRoot</c>) — the root-tier per-constant sibling of <see cref="HasUndecidedDataObligation"/>. Set only under <see cref="RootDataObligationsEnabled"/>, which production threads on, so a production module sets it when a landed root demand comes back undecided; a raw-engine run leaves the arm off and it stays <see langword="false"/>.</summary>
    public bool HasDataObligationUndecidedOnRoot { get; private set; }

    /// <summary>The pooled root data-demand signature each ≈-class was last decided on, keyed by (context id, ≈-class representative) — the root-only per-class memo: a NEW structure, never a widening of the ordinary lane's <see cref="LastDataSignature"/> (keyed on bare context id), which stays byte-identical. A class whose pooled demand set is unchanged since its last decision skips the oracle; a merge that grows the pooled set changes the representative-keyed signature and re-fires (the biconditional).</summary>
    private Dictionary<(int ContextId, int Representative), int[]> RootDataSignature { get; } = [];

    /// <summary>The reusable buffer of an ≈-class's sorted pooled data-demand markers collected for the root-tier arm — separate from <see cref="ScratchLiveDemands"/>, which the ordinary lane owns across its own call chain.</summary>
    private List<int> ScratchRootDemands { get; } = [];

    /// <summary>The reusable buffer of the pooled root-class obligations handed to a per-constant sidecar decide, reconstructed as the sidecar's <see cref="AlcConcept"/> shapes.</summary>
    private List<AlcConcept> ScratchRootConcepts { get; } = [];

    /// <summary>The reusable map from a pooled root-class obligation back to its demand marker atom, so a per-constant clash core names the contributing demand property.</summary>
    private Dictionary<AlcConcept, int> ScratchRootConceptToMarker { get; } = [];

    /// <summary>The enumeration-CSP habitat class the census-first recognizer assigned the module at survey time — set by the reasoner after engine creation so every progress mark carries the shape name beside its churn profile; <see cref="EnumerationHabitatClass.None"/> when never set.</summary>
    public EnumerationHabitatClass EnumerationHabitat { get; set; }

    /// <summary>The progress marks emitted so far — the sampler events' consecutive sequence numbers.</summary>
    private long ProgressEmissions { get; set; }

    /// <summary>The reusable buffer of clause ids the backward-Elim sweep collects.</summary>
    private List<int> ScratchSubsumed { get; } = [];

    /// <summary>The reusable buffer accumulating a conclusion's body literals; the offering rule canonicalises it in place through <see cref="DlClause.CanonicaliseInPlace"/> and offers it as a span, or hands it to <see cref="DlClause.Create"/>, which canonicalises a copy.</summary>
    private List<DlLiteral> ScratchBody { get; } = [];

    /// <summary>The reusable buffer accumulating a conclusion's head literals — the substituted rule head plus every premise's carried residual disjuncts; empty for a <c>⊥</c> head. The offering rule canonicalises it in place through <see cref="DlClause.CanonicaliseInPlace"/> and offers it as a span, or hands it to <see cref="DlClause.Create"/>, which canonicalises a copy.</summary>
    private List<DlLiteral> ScratchHead { get; } = [];

    /// <summary>The reusable snapshot of Eq rewrite targets or rewriting-equality ids, so the enumeration is stable while an <see cref="ApplyEq"/> conclusion inserts and tombstones clauses.</summary>
    private List<int> ScratchEqDispatch { get; } = [];

    /// <summary>The reusable buffer of one clause's maximal head indexes for the Eq acting-literal dispatch — the target's set on the given-equality path and the equality premise's set on the given-target path — separate from every other maximal buffer because the dispatch runs while those are in flight.</summary>
    private List<int> ScratchEqActing { get; } = [];

    /// <summary>The reusable buffer of a conclusion's maximal head indexes, filled by <see cref="AddClause"/> for the insert; separate from <see cref="ScratchProcessMaximal"/> because rules dispatched from <see cref="ProcessClause"/> insert conclusions while that buffer is being walked.</summary>
    private List<int> ScratchMaximal { get; } = [];

    /// <summary>The reusable buffer of the landed clause's maximal head indexes <see cref="ProcessClause"/> dispatches over — one rule pass per maximal literal.</summary>
    private List<int> ScratchProcessMaximal { get; } = [];

    /// <summary>The reusable body buffer of the insert-normalization rebuild (orientation swap or Ineq disjunct drop); separate from <see cref="ScratchBody"/> so the rebuild never perturbs a rule's conclusion buffer.</summary>
    private List<DlLiteral> NormalizeBody { get; } = [];

    /// <summary>The reusable head buffer of the insert-normalization pass: every surviving head literal with each equality and inequality oriented maximal-side-first, false self-inequality disjuncts dropped; empty when every disjunct fell (the <c>⊥</c> collapse).</summary>
    private List<DlLiteral> NormalizeHead { get; } = [];

    /// <summary>The reusable head buffer of the SPAN insertion path's normalization rewrite — a buffer of its own, distinct from <see cref="NormalizeHead"/> and from every caller's assembly scratch, so a rewrite never perturbs the spans the caller handed in.</summary>
    private List<DlLiteral> SpanNormalizeHead { get; } = [];

    /// <summary>The reusable body buffer of the SPAN insertion path's entry translation.</summary>
    private List<DlLiteral> SpanTranslateBody { get; } = [];

    /// <summary>The reusable head buffer of the SPAN insertion path's entry translation, distinct from <see cref="SpanNormalizeHead"/> because the normalization reads the translated head.</summary>
    private List<DlLiteral> SpanTranslateHead { get; } = [];

    /// <summary>The per-slot candidate clause-id lists of the join odometer currently running.</summary>
    private List<List<int>> SlotBuffers { get; } = [];

    /// <summary>The neighbour variable each odometer slot binds, or <c>-1</c> for a slot that binds none (a concept premise or an already-bound neighbour).</summary>
    private List<int> SlotNeighbours { get; } = [];

    /// <summary>Whether each binding odometer slot's role body atom carries the central variable first — selects which head argument is the slot's bound term.</summary>
    private List<bool> SlotCentralFirst { get; } = [];

    /// <summary>The exact head atom each non-binding odometer slot looked up (a concept image or a bound-neighbour role image) — the literal the slot's premise FIRED on, whose residual the conclusion carries; <c>default</c> for a free-neighbour slot, whose fired literal is found per premise by shape.</summary>
    private List<DlLiteral> SlotExactAtoms { get; } = [];

    /// <summary>The role symbol of each free-neighbour odometer slot, or <c>-1</c> for a slot resolved by exact atom — with <see cref="SlotCentralFirst"/> it names the shape whose greatest head literal is the premise's fired literal.</summary>
    private List<int> SlotRoleSymbols { get; } = [];

    /// <summary>The pool of slot-candidate lists reused across joins, so a join allocates no per-slot list.</summary>
    private List<List<int>> SlotBufferPool { get; } = [];

    /// <summary>The number of pooled slot buffers handed out to the join currently running.</summary>
    private int SlotBuffersInUse { get; set; }

    /// <summary>The odometer cursor over <see cref="SlotBuffers"/>.</summary>
    private List<int> Cursor { get; } = [];

    /// <summary>The neighbour bindings of the odometer combination currently tested, rebuilt per combination for the consistency check and the head substitution.</summary>
    private Dictionary<int, DlTerm> ScratchBindings { get; } = [];

    /// <summary>The reusable Succ K2 list: the successor triggers whose sigma-image is a live head of the predecessor.</summary>
    private List<DlLiteral> ScratchK2 { get; } = [];

    /// <summary>The reusable Succ K1 set: the successor triggers whose sigma-image is an unconditional head <c>⊤→A'σ</c> of the predecessor.</summary>
    private HashSet<DlLiteral> ScratchK1 { get; } = [];

    /// <summary>The budget bounding the saturation run; unbounded during setup, set by <see cref="Saturate"/>.</summary>
    private ReasoningBudget Budget { get; set; }

    /// <summary>Whether the inference budget has been reached, halting further rule application.</summary>
    private bool BudgetExhausted { get; set; }

    /// <summary>The total rule applications spent (Core, Hyper, Succ, Pred, Elim, Eq, Ineq, Factor, and the data-clash injections together) — the added-conclusion count the statistics report.</summary>
    private long RuleApplications { get; set; }

    /// <summary>The total budget-gated attempts spent — every conclusion offered to a context, oracle invocation, and Succ expansion, whether or not it added a clause — the quantity the budget bounds: a saturation whose joins emit mostly redundant conclusions still spends its ceiling instead of spinning under a frozen added-clause counter.</summary>
    private long InferenceAttempts { get; set; }

    /// <summary>The Core-rule applications: the core seeds and the per-context Top seed, at context creation.</summary>
    private long CoreApplications { get; set; }

    /// <summary>The Hyper-rule applications: clauses Hyper added, the per-context empty-body firings included.</summary>
    private long HyperApplications { get; set; }

    /// <summary>The Succ-rule applications: applicable successor expansions performed.</summary>
    private long SuccApplications { get; set; }

    /// <summary>The Pred-rule applications: clauses Pred added.</summary>
    private long PredApplications { get; set; }

    /// <summary>The Elim-rule applications: clauses removed by backward subsumption.</summary>
    private long ElimApplications { get; set; }

    /// <summary>The Eq-rule applications: equality rewrites of a target head literal Eq added.</summary>
    private long EqApplications { get; set; }

    /// <summary>The Ineq-rule applications: false self-inequality disjuncts dropped from a head (the Horn case's sole disjunct collapses the head to the empty clause).</summary>
    private long IneqApplications { get; set; }

    /// <summary>The Factor-rule applications: equality-factoring conclusions added — a non-selected positive equality disjunct sharing the selected equality's maximal side replaced by the introduced inequality between the minimal sides.</summary>
    private long FactorApplications { get; set; }

    /// <summary>The data-clash applications: <c>⋃Body → Bottom(x)</c> clauses the datatype sidecar injected on a concrete-domain clash.</summary>
    private long DataClashApplications { get; set; }

    /// <summary>The disjunctive data-marker probes: one per sidecar decide the refutation rule ran over a pool-plus-marker obligation set.</summary>
    private long DisjunctiveDataProbes { get; set; }

    /// <summary>The disjunctive data markers recorded refuted — each with its narrowing core, counted once per (context, marker).</summary>
    private long DisjunctiveDataRefutations { get; set; }

    /// <summary>The narrowing clauses the refutation rule emitted — body-conditioned residual heads, one per live contributor combination inserted.</summary>
    private long DisjunctiveDataNarrowings { get; set; }

    /// <summary>The contexts the fixpoint certification decided <c>Consistent</c> over their survivor joint obligation set.</summary>
    private long DisjunctiveDataCertifications { get; set; }

    /// <summary>The contexts the fixpoint certification could NOT certify — a clashing or undecided survivor joint set, each latching the undecided-obligation delegation.</summary>
    private long UncertifiedDisjunctiveDataLatches { get; set; }

    /// <summary>The times <see cref="HasUndecidedDataObligation"/> was latched — the numeric funnel counter across the unit rule's undecided path, an undecided disjunctive probe, and an uncertified fixpoint joint set.</summary>
    private long UndecidedDataObligationCount { get; set; }

    /// <summary>The contexts created, the setup contexts included.</summary>
    private int ContextsCreated { get; set; }

    /// <summary>The contexts reused from the registry rather than created.</summary>
    private int ContextsReused { get; set; }

    /// <summary>The context clauses inserted.</summary>
    private int ClausesDerived { get; set; }

    /// <summary>The context clauses removed by backward subsumption.</summary>
    private int ClausesEliminated { get; set; }

    /// <summary>The conclusions offered to a context that were already contained up to redundancy, so no clause was inserted — the derivation-funnel churn counter the statistics expose.</summary>
    private long RedundantConclusions { get; set; }

    /// <summary>The containment absorptions at the insertion gate whose container was an EXACT DUPLICATE of the offered conclusion — the fast-path half of <see cref="RedundantConclusions"/>. Charged at that gate alone, so the pair partitions the gate's absorptions and says nothing about the containment probes the Succ, hypothesis-cover, and relevance-seed sites run.</summary>
    private long DuplicateContainmentHits { get; set; }

    /// <summary>The containment absorptions at the insertion gate whose container was a strictly more general clause — an index-drawn subsumer or the live empty clause — the scan half of <see cref="RedundantConclusions"/>. Charged at that gate alone, the counterpart of <see cref="DuplicateContainmentHits"/>.</summary>
    private long SubsumedContainmentHits { get; set; }

    /// <summary>The conclusions dropped as tautologies — a reflexive equality disjunct or a complementary equality/inequality pair — at the Eq and Factor pre-charge gate for their materialized conclusions, and at head normalization for every other rule's, ahead of the grammar and redundancy gates. The pre-charge drop spends no budget; the funnel keeps counting it.</summary>
    private long TautologyDrops { get; set; }

    /// <summary>The conclusions refused because a derived head literal left the context-kind grammar — the funnel stage that also latches the named out-of-grammar delegation.</summary>
    private long OutOfGrammarConclusions { get; set; }

    /// <summary>The clause-landed events enqueued on the worklist — every clause the redundancy and grammar gates admitted and <see cref="AddClause"/> inserted, counted once at the single enqueue site across the eager and ordinary queues. The head of the derivation funnel: its excess over <see cref="ClausesDerived"/> is zero (each inserted clause enqueues once), and its ratio against <see cref="InferenceAttempts"/> reads what fraction of attempts reached the worklist rather than being spent on redundant, tautological, or out-of-grammar conclusions.</summary>
    private long WorklistEnqueues { get; set; }

    /// <summary>The largest live clause count any single context reached.</summary>
    private int MaxContextClauses { get; set; }

    /// <summary>The <c>SameIndividual</c> unions the clausifier's pre-merge pass performed — carried through to the statistics.</summary>
    private int PreMergeUnions { get; }

    /// <summary>The ground contexts the setup minted, one per individual representative.</summary>
    private int GroundContextsCreated { get; set; }

    /// <summary>The designated ground-target function edges the Succ rule added, routing an asserted object-property edge to its representative's ground context.</summary>
    private int GroundEdgesSeeded { get; set; }

    /// <summary>The post-saturation Self-ghost re-closure clashes decided — at most one, the ghost pass runs once; the pre-merge and closure clashes are decided before the engine spins up.</summary>
    private int GroundClashes { get; set; }

    /// <summary>The ground context id each designated ground-edge function symbol routes its successor to — the <see cref="ResolveCautiousSuccessor"/> consult that overrides the cautious unique-filler strategy for an asserted edge.</summary>
    private Dictionary<int, int> GroundContextByFunction { get; } = [];

    /// <summary>The ground contexts as (representative, context id) pairs — the Self-ghost pass walks it to read each ground context's unconditional loop-concept heads against its representative's graph node.</summary>
    private List<(Utf8String Representative, int ContextId)> GroundContexts { get; } = [];

    /// <summary>The representative-level asserted-edge closure the Self-ghost pass augments and re-closes; empty for a module carrying no admitted ABox axiom.</summary>
    private GroundAssertionGraph GroundGraph { get; }

    /// <summary>The loop concept atom (<c>Self_p</c>) to forward-base representative-role map the Self-ghost pass reads an unconditional loop-concept head off.</summary>
    private IReadOnlyDictionary<int, RoleRepresentative> GroundSelfLoopConcepts { get; }

    /// <summary>The forward-base role symbol to loop-concept-atom map (dense ids, keyed off packed clause literals) — the inverse of <see cref="GroundSelfLoopConcepts"/> — the root ground-loop bridge reads a derived constant self-edge against; empty when no loop concept was minted.</summary>
    private Dictionary<int, int> RootLoopConceptByBase { get; } = [];

    /// <summary>Whether the post-saturation Self-ghost re-closure derived a clash — a module inconsistency the widened verdict reads alongside the ground-context empty clauses.</summary>
    private bool GroundGhostClashDetected { get; set; }

    /// <summary>Initialises the engine over a clausification result: the ontology indexes, the extended trigger sets, and the cautious-strategy filler map.</summary>
    /// <param name="clausification">The module's clausification.</param>
    /// <param name="registry">The registered-datatype set the datatype sidecar consults where the family classifier abstains; the clausification stays registry-blind.</param>
    /// <param name="paramodulationScope">The scope of the central-variable-versus-individual Eq paramodulation.</param>
    /// <param name="propagationRelevance">The r-Pred ground-relevance mode.</param>
    /// <param name="topology">The root-tier topology.</param>
    private ContextSaturationEngine(ClausificationResult clausification, DatatypeRegistry registry, NominalParamodulationScope paramodulationScope, RootPropagationRelevance propagationRelevance, RootContextTopology topology)
    {
        Registry = registry;
        ParamodulationScope = paramodulationScope;
        PropagationRelevance = propagationRelevance;
        Topology = topology;
        Symbols = clausification.Symbols;
        DataDemandDescriptors = clausification.DataDemandDescriptors;
        DataBox = clausification.DataBox;
        PreMergeUnions = clausification.PreMergeUnions;
        GroundGraph = clausification.GroundGraph;
        GroundSelfLoopConcepts = clausification.GroundSelfLoopConcepts;
        foreach((int selfAtom, RoleRepresentative loopBase) in clausification.GroundSelfLoopConcepts)
        {
            RootLoopConceptByBase[loopBase.Value] = selfAtom;
        }

        foreach(GroundKeyDescriptor descriptor in clausification.KeyDescriptors)
        {
            if(!descriptor.ClassIsThing)
            {
                KeyClassAtoms.Add(descriptor.ClassAtom);
            }
        }

        KeyDescriptors = clausification.KeyDescriptors;
        KeyValueStore = clausification.KeyValueStore;

        //The vr key join arms only for a nominal-jurisdiction module carrying a
        //HasKey descriptor — a pairing the KeyOnNominalModule guard admits only
        //under the construction-time switch, so the arm reads false on every
        //production module and every root-key dispatch stays dark.
        RootKeyJoinArmed = clausification.NominalJurisdiction && clausification.KeyDescriptors.Count > 0;

        Structure = new ContextStructure(new HashSet<int>(clausification.DataDemandDescriptors.Keys));
        Order = clausification.Order;
        PredecessorTriggers = new HashSet<DlLiteral>(clausification.Order.Pr);
        IndexOntology(clausification.Clauses);
        BuildTriggerSets(clausification);
        BuildUniqueFillerMap(clausification.Clauses);
        BuildDistinctFillerAtoms();
        NomSiblingCount = ComputeNomSiblingCount(clausification.Clauses);
    }

    /// <summary>Folds the cautious strategy's filler map down to its DISTINCT filler atoms — the candidate core set whose size is the fill ceiling.</summary>
    private void BuildDistinctFillerAtoms()
    {
        HashSet<int> seen = [];
        foreach(KeyValuePair<int, int> entry in UniqueFillerByFunction)
        {
            if(seen.Add(entry.Value))
            {
                DistinctFillerAtoms.Add(entry.Value);
            }
        }
    }

    /// <summary>Counts the distinct filler atoms the context registry holds a context for — the fill probe, run at surface time over the frozen candidate set.</summary>
    /// <returns>The registered filler-core count.</returns>
    private int CountCautiousCoresRegistered()
    {
        int registered = 0;
        for(int i = 0; i < DistinctFillerAtoms.Count; i++)
        {
            if(Structure.TryGetByCoreAtom(DistinctFillerAtoms[i], out _))
            {
                registered++;
            }
        }

        return registered;
    }

    /// <summary>The structure-wide occurrence-index telemetry of the two survivor-sweep-only indexes: the MAINTAINED side (entries registered and distinct keys held) against the CONSULTED side (sweeps that probed a posting and posting entries walked). The imbalance between the two sides is what the record carries; distinct keys PROBED is out of scope, since counting those needs a set rather than a counter.</summary>
    /// <param name="HeadEntriesRegistered">The head-occurrence entries every context registered.</param>
    /// <param name="BodyEntriesRegistered">The body-occurrence entries every context registered.</param>
    /// <param name="HeadDistinctKeys">The head-occurrence keys every context holds.</param>
    /// <param name="BodyDistinctKeys">The body-occurrence keys every context holds.</param>
    /// <param name="SweepProbes">The backward-subsumption sweeps that reached the posting path.</param>
    /// <param name="SweepPostingEntriesWalked">The posting entries those sweeps walked.</param>
    private readonly record struct OccurrenceTelemetry(long HeadEntriesRegistered, long BodyEntriesRegistered, long HeadDistinctKeys, long BodyDistinctKeys, long SweepProbes, long SweepPostingEntriesWalked);

    /// <summary>Sums the per-context occurrence telemetry over every context in one walk — the surface projection of the six occurrence columns; O(contexts) per read.</summary>
    /// <returns>The summed telemetry.</returns>
    private OccurrenceTelemetry SumOccurrenceTelemetry()
    {
        long headEntries = 0;
        long bodyEntries = 0;
        long headKeys = 0;
        long bodyKeys = 0;
        long probes = 0;
        long walked = 0;
        for(int id = 0; id < Structure.Count; id++)
        {
            Context context = Structure[id];
            headEntries += context.HeadOccurrenceEntriesRegistered;
            bodyEntries += context.BodyOccurrenceEntriesRegistered;
            headKeys += context.HeadOccurrenceDistinctKeys;
            bodyKeys += context.BodyOccurrenceDistinctKeys;
            probes += context.SurvivorSweepProbes;
            walked += context.SurvivorSweepPostingEntriesWalked;
        }

        return new OccurrenceTelemetry(headEntries, bodyEntries, headKeys, bodyKeys, probes, walked);
    }

    /// <summary>Computes the Nom sibling count <c>K</c> from the frozen clause set: <c>K + 1</c> equals the largest neighbour-variable index any DL-clause mentions; zero when no neighbour variable exists (Nom can never fire then).</summary>
    /// <param name="clauses">The clausified ontology.</param>
    /// <returns>The sibling count <c>K</c>.</returns>
    private static int ComputeNomSiblingCount(IReadOnlyList<DlClause> clauses)
    {
        int maxIndex = 0;
        for(int i = 0; i < clauses.Count; i++)
        {
            maxIndex = Math.Max(maxIndex, MaxNeighbourIndex(clauses[i].Body));
            maxIndex = Math.Max(maxIndex, MaxNeighbourIndex(clauses[i].Head));
        }

        return Math.Max(0, maxIndex - 1);
    }

    /// <summary>The largest neighbour-variable index a literal span mentions, or zero.</summary>
    /// <param name="literals">The literal span.</param>
    /// <returns>The largest index.</returns>
    private static int MaxNeighbourIndex(ReadOnlySpan<DlLiteral> literals)
    {
        int maxIndex = 0;
        for(int i = 0; i < literals.Length; i++)
        {
            if(literals[i].First.Kind == DlTermKind.Neighbour)
            {
                maxIndex = Math.Max(maxIndex, literals[i].First.Index);
            }

            if(literals[i].Second.Kind == DlTermKind.Neighbour)
            {
                maxIndex = Math.Max(maxIndex, literals[i].Second.Index);
            }
        }

        return maxIndex;
    }

    /// <summary>Builds an engine for a clausified module: the ontology indexes and the trivial context <c>v_⊤</c> (empty core, so no Core seeds beyond Top, but the empty-body ontology firing runs).</summary>
    /// <param name="clausification">The module's clausification.</param>
    /// <returns>The initialised engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clausification"/> is <see langword="null"/>.</exception>
    public static ContextSaturationEngine Create(ClausificationResult clausification)
    {
        return Create(clausification, DatatypeRegistry.Empty);
    }

    /// <summary>Builds an engine for a clausified module consulting a registered-datatype set at the datatype sidecar — the registry-carrying counterpart of <see cref="Create(ClausificationResult)"/>, with the default query-scoped paramodulation.</summary>
    /// <param name="clausification">The module's clausification.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The initialised engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clausification"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    public static ContextSaturationEngine Create(ClausificationResult clausification, DatatypeRegistry registry)
    {
        return Create(clausification, registry, NominalParamodulationScope.QueryScoped);
    }

    /// <summary>Builds an engine for a clausified module under a selected central-variable-versus-individual paramodulation scope — the measurement counterpart of <see cref="Create(ClausificationResult, DatatypeRegistry)"/> that drives the reference unrestricted mode beside the default query-scoped one, with the default unrestricted root propagation.</summary>
    /// <param name="clausification">The module's clausification.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="paramodulationScope">The scope of the central-variable-versus-individual Eq paramodulation.</param>
    /// <returns>The initialised engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clausification"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    public static ContextSaturationEngine Create(ClausificationResult clausification, DatatypeRegistry registry, NominalParamodulationScope paramodulationScope)
    {
        return Create(clausification, registry, paramodulationScope, RootPropagationRelevance.Unrestricted);
    }

    /// <summary>Builds an engine for a clausified module under a selected paramodulation scope AND a selected r-Pred ground-relevance mode, with the default single-root topology.</summary>
    /// <param name="clausification">The module's clausification.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="paramodulationScope">The scope of the central-variable-versus-individual Eq paramodulation.</param>
    /// <param name="propagationRelevance">The r-Pred ground-relevance mode.</param>
    /// <returns>The initialised engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clausification"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    public static ContextSaturationEngine Create(ClausificationResult clausification, DatatypeRegistry registry, NominalParamodulationScope paramodulationScope, RootPropagationRelevance propagationRelevance)
    {
        return Create(clausification, registry, paramodulationScope, propagationRelevance, RootContextTopology.SingleRoot);
    }

    /// <summary>Builds an engine for a clausified module under a selected paramodulation scope, a selected r-Pred ground-relevance mode, AND a selected root-tier topology — the innermost creation surface that drives the fragmented topology beside the single-root default.</summary>
    /// <param name="clausification">The module's clausification.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="paramodulationScope">The scope of the central-variable-versus-individual Eq paramodulation.</param>
    /// <param name="propagationRelevance">The r-Pred ground-relevance mode.</param>
    /// <param name="topology">The root-tier topology.</param>
    /// <param name="progressSampler">The optional in-saturation progress sampler, attached to <see cref="Progress"/> BEFORE the creation seeding runs, so the seeding's own attempts are observable; <see langword="null"/> is the zero-cost default. An engine handed to a caller unsampled can still be sampled through <see cref="Progress"/>, whose marks then start past the seeding.</param>
    /// <returns>The initialised engine.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clausification"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fragmented topology is combined with the ground-filtered relevance mode: the filter's ground-conjunct index set (<see cref="RootPredEligibleByGroundConjunct"/>, <see cref="RootPredGroundConjunctsByIndividual"/>, <see cref="RelevanceSeededGroundAtoms"/>, <see cref="DispatchedRelevanceKeys"/>) is defined over the single shared root table, so the composition is an invariant violation — the thesis-faithful per-individual relevance filter is a different design.</exception>
    public static ContextSaturationEngine Create(ClausificationResult clausification, DatatypeRegistry registry, NominalParamodulationScope paramodulationScope, RootPropagationRelevance propagationRelevance, RootContextTopology topology, SaturationProgressSampler? progressSampler = null)
    {
        ArgumentNullException.ThrowIfNull(clausification);
        ArgumentNullException.ThrowIfNull(registry);
        if(topology == RootContextTopology.PerIndividualRoots && propagationRelevance == RootPropagationRelevance.GroundFiltered)
        {
            throw new ArgumentException("The fragmented root topology does not compose with the ground-filtered relevance mode: the filter's ground-conjunct indexes are defined over the single shared root table.", nameof(propagationRelevance));
        }

        ContextSaturationEngine engine = new(clausification, registry, paramodulationScope, propagationRelevance, topology);
        engine.Progress = progressSampler;
        Context trivial = engine.Structure.CreateContext([]);
        engine.ContextsCreated++;
        engine.SeedContext(trivial);
        engine.SetupGroundContexts(clausification);
        if(clausification.Symbols.IndividualCount > 0)
        {
            //Nominal jurisdiction: the module interned individual constants, so the
            //root tier exists and the four nominal rules can fire. A nominal-free
            //module never mints it, and every root-aware dispatch stays dark
            //(H-T3-4). The root tier itself is pulled through the resolver, whose
            //topology arm mints the single root or the per-individual contexts.
            engine.SeedRootTier(clausification.RootFacts);
        }

        return engine;
    }

    /// <summary>Seeds the root tier at engine construction: under the single-root topology, the distinguished root context is resolved (minting it on first need) and the clausifier's root facts apply to it flat; under the fragmented topology, every told individual's nominal-root context is resolved eagerly and each root fact routes to its first-slot owner's context through the entry translation, the inter-nominal carrier supplying the foreign-side images when the seeded clause lands.</summary>
    /// <param name="rootFacts">The clausifier's ground root-context clauses.</param>
    private void SeedRootTier(IReadOnlyList<DlClause> rootFacts)
    {
        if(Topology == RootContextTopology.SingleRoot)
        {
            Context root = ResolveSingleRoot();
            for(int i = 0; i < rootFacts.Count; i++)
            {
                ApplyCore(root, rootFacts[i], []);
            }

            return;
        }

        for(int individual = 0; individual < Symbols.IndividualCount; individual++)
        {
            GetOrCreateRootFor(individual);
        }

        for(int i = 0; i < rootFacts.Count; i++)
        {
            RouteRootFact(rootFacts[i]);
        }
    }

    /// <summary>Resolves the root-class context owning an individual — the unifying resolver of the two topology arms: under <see cref="RootContextTopology.SingleRoot"/> the one distinguished root context, minted on first need with its per-constant seeds; under <see cref="RootContextTopology.PerIndividualRoots"/> the individual's nominal-root context, minted on first need with the context clause <c>⊤ → x ≈ o</c> and the entry-translated ordinary seeds. Told individuals resolve at engine construction; a generated nominal or a first-mentioned foreign individual mints lazily here.</summary>
    /// <param name="individual">The individual id.</param>
    /// <returns>The individual's root-class context.</returns>
    private Context GetOrCreateRootFor(int individual)
    {
        if(Topology == RootContextTopology.SingleRoot)
        {
            return ResolveSingleRoot();
        }

        if(Structure.TryGetRootByIndividual(individual, out int existing))
        {
            return Structure[existing];
        }

        Context context = Structure.CreateNominalRootContext(individual);
        ContextsCreated++;
        SeedNominalRootContext(context, individual);

        return context;
    }

    /// <summary>Resolves the distinguished single root context, minting it with its seeds on first need — the <see cref="RootContextTopology.SingleRoot"/> arm of the resolver.</summary>
    /// <returns>The single root context.</returns>
    private Context ResolveSingleRoot()
    {
        if(Structure.RootContextId >= 0)
        {
            return Structure[Structure.RootContextId];
        }

        Context root = Structure.CreateRootContext();
        ContextsCreated++;
        SeedRootContext(root);

        return root;
    }

    /// <summary>Seeds a freshly minted nominal-root context <c>v_o</c>: the Top-semantics seed and the empty-body ontology firing with the own constant respelled central — an own-constant-free clause fires as-is, exactly as in an ordinary context. The context clause <c>⊤ → x ≈ o</c> is realized STRUCTURALLY rather than stored: the entry translation at the single mutation point respells every own-constant mention central (making the stored clause the dropped tautology <c>x ≈ x</c>), and the equality license it grants is exercised by exactly three explicit substitutions — the entry translation itself, the home-grounded r-Pred view, and the carrier's grounding step — each keyed off <see cref="Context.HomeIndividual"/>. NO broadcast replay: a root-class context is never an r-Pred target.</summary>
    /// <param name="context">The nominal-root context.</param>
    /// <param name="individual">The home individual <c>o</c>.</param>
    private void SeedNominalRootContext(Context context, int individual)
    {
        ApplyCore(context, DlClause.Create([], [DlLiteral.Concept(ContextSymbolTable.Top, DlTerm.Central)], DerivedOrigin), []);

        for(int i = 0; i < EmptyBodyOntologyClauses.Count && !BudgetExhausted; i++)
        {
            ApplyHyper(context, TranslateForEntry(EmptyBodyOntologyClauses[i], individual), []);
        }
    }

    /// <summary>Routes one clausifier root fact to its owning nominal-root context under the fragmented topology: the fact's single literal (head-borne for the positive shapes, body-borne for the negative-edge clash form) names its FIRST individual slot as the owner, the entry translation respells the owner's constant central, and the seeded clause's landing fires the inter-nominal carrier for any foreign mention.</summary>
    /// <param name="fact">The root fact.</param>
    private void RouteRootFact(DlClause fact)
    {
        DlLiteral literal = fact.Head.Length > 0 ? fact.Head[0] : fact.Body[0];
        int owner = literal.First.IsIndividual ? literal.First.IndividualId : literal.Second.IndividualId;
        Context target = GetOrCreateRootFor(owner);
        ApplyCore(target, TranslateForEntry(fact, owner), []);
    }

    /// <summary>Applies the D2 entry translation to a clause entering the nominal-root context of an individual: every occurrence of the own constant respells as the central variable — <c>o ↦ x</c> and <c>f(o) ↦ f(x)</c> — while foreign constants and the predecessor variable pass through untouched (stay-<c>y</c> everywhere).</summary>
    /// <param name="clause">The clause as its producer left it.</param>
    /// <param name="individual">The target context's home individual <c>o</c>.</param>
    /// <returns>The entry-translated clause.</returns>
    private DlClause TranslateForEntry(DlClause clause, int individual)
    {
        ScratchEntryBody.Clear();
        ReadOnlySpan<DlLiteral> body = clause.Body;
        for(int i = 0; i < body.Length; i++)
        {
            ScratchEntryBody.Add(TranslateLiteralForEntry(body[i], individual));
        }

        ScratchEntryHead.Clear();
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            ScratchEntryHead.Add(TranslateLiteralForEntry(head[i], individual));
        }

        return DlClause.Create(ScratchEntryBody, ScratchEntryHead, clause.Origin);
    }

    /// <summary>The SPAN face of the D2 entry translation: the same per-literal respelling over body and head spans, leaving the translated form canonical in <see cref="SpanTranslateBody"/> and <see cref="SpanTranslateHead"/> — buffers of their own, so the caller's spans stay untouched. The respelling can collide two literals or unsort a span, so the buffers are canonicalised exactly as the clause face's rebuild canonicalises.</summary>
    /// <param name="body">The body span as its producer left it.</param>
    /// <param name="head">The head span as its producer left it.</param>
    /// <param name="individual">The target context's home individual <c>o</c>.</param>
    private void TranslateSpansForEntry(ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int individual)
    {
        SpanTranslateBody.Clear();
        for(int i = 0; i < body.Length; i++)
        {
            SpanTranslateBody.Add(TranslateLiteralForEntry(body[i], individual));
        }

        SpanTranslateHead.Clear();
        for(int i = 0; i < head.Length; i++)
        {
            SpanTranslateHead.Add(TranslateLiteralForEntry(head[i], individual));
        }

        DlClause.CanonicaliseInPlace(SpanTranslateBody);
        DlClause.CanonicaliseInPlace(SpanTranslateHead);
    }

    /// <summary>Whether a clause mentions an individual's constant or its Skolem images in any body or head slot — the entry-translation gate's cheap pre-test, so an already-central clause is not rebuilt.</summary>
    /// <param name="clause">The clause.</param>
    /// <param name="individual">The individual id.</param>
    /// <returns><see langword="true"/> when a mention occurs.</returns>
    private static bool MentionsOwnConstant(DlClause clause, int individual)
    {
        return SpanMentionsOwnConstant(clause.Body, individual) || SpanMentionsOwnConstant(clause.Head, individual);
    }

    /// <summary>Whether a literal span mentions an individual's constant or its Skolem images.</summary>
    /// <param name="literals">The literal span.</param>
    /// <param name="individual">The individual id.</param>
    /// <returns><see langword="true"/> when a mention occurs.</returns>
    private static bool SpanMentionsOwnConstant(ReadOnlySpan<DlLiteral> literals, int individual)
    {
        for(int i = 0; i < literals.Length; i++)
        {
            if(TermMentionsOwnConstant(literals[i].First, individual) || (literals[i].Kind != DlLiteralKind.Concept && TermMentionsOwnConstant(literals[i].Second, individual)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a term is an individual's constant or one of its Skolem images.</summary>
    /// <param name="term">The term.</param>
    /// <param name="individual">The individual id.</param>
    /// <returns><see langword="true"/> for a mention.</returns>
    private static bool TermMentionsOwnConstant(DlTerm term, int individual)
    {
        return term.Kind is DlTermKind.Individual or DlTermKind.FunctionOfIndividual && term.IndividualId == individual;
    }

    /// <summary>The entry translation of one literal: the own constant maps to the central variable in every slot, its Skolem images demote <c>f(o) ↦ f(x)</c>, everything else is fixed.</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="individual">The home individual <c>o</c>.</param>
    /// <returns>The translated literal.</returns>
    private static DlLiteral TranslateLiteralForEntry(DlLiteral literal, int individual)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(literal.Symbol, TranslateTermForEntry(literal.First, individual)),
            DlLiteralKind.Role => DlLiteral.Role(literal.Symbol, TranslateTermForEntry(literal.First, individual), TranslateTermForEntry(literal.Second, individual)),
            DlLiteralKind.Equality => DlLiteral.Equality(TranslateTermForEntry(literal.First, individual), TranslateTermForEntry(literal.Second, individual)),
            _ => DlLiteral.Inequality(TranslateTermForEntry(literal.First, individual), TranslateTermForEntry(literal.Second, individual)),
        };
    }

    /// <summary>The entry translation of one term: <c>o ↦ x</c>, <c>f(o) ↦ f(x)</c> for the own individual <c>o</c>; every other term — foreign constants, the predecessor variable, ordinary function terms — is fixed.</summary>
    /// <param name="term">The term.</param>
    /// <param name="individual">The home individual <c>o</c>.</param>
    /// <returns>The translated term.</returns>
    private static DlTerm TranslateTermForEntry(DlTerm term, int individual)
    {
        return term.Kind switch
        {
            DlTermKind.Individual when term.IndividualId == individual => DlTerm.Central,
            DlTermKind.FunctionOfIndividual when term.IndividualId == individual => DlTerm.Function(term.FunctionSymbol),
            _ => term,
        };
    }

    /// <summary>Seeds the freshly created root context: the Top-semantics seed and the empty-body ontology firing run PER CONSTANT (<c>σ(x) ∈ Σo</c> — the root Hyper has no central variable to fire on), while ground-headed empty-body clauses (the DL7 facts) fire once.</summary>
    /// <param name="root">The root context.</param>
    private void SeedRootContext(Context root)
    {
        for(int individual = 0; individual < Symbols.IndividualCount; individual++)
        {
            SeedRootConstant(root, individual);
        }

        for(int i = 0; i < EmptyBodyOntologyClauses.Count; i++)
        {
            if(!MentionsCentral(EmptyBodyOntologyClauses[i].Head))
            {
                ApplyHyper(root, EmptyBodyOntologyClauses[i], []);
            }
        }
    }

    /// <summary>Seeds one constant into the root context: the Top seed <c>⊤→Top(o)</c> and the <c>σ(x) = o</c> image of every empty-body ontology clause whose head mentions the central variable. Runs at root creation for the interned constants and again from the mint hook for each generated nominal.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="individual">The constant's individual id.</param>
    private void SeedRootConstant(Context root, int individual)
    {
        DlTerm anchor = DlTerm.Individual(individual);
        ApplyCore(root, DlClause.Create([], [DlLiteral.Concept(ContextSymbolTable.Top, anchor)], DerivedOrigin), []);

        for(int i = 0; i < EmptyBodyOntologyClauses.Count && !BudgetExhausted; i++)
        {
            DlClause clause = EmptyBodyOntologyClauses[i];
            if(!MentionsCentral(clause.Head))
            {
                continue;
            }

            ScratchBody.Clear();
            ScratchHead.Clear();
            ReadOnlySpan<DlLiteral> head = clause.Head;
            for(int j = 0; j < head.Length; j++)
            {
                ScratchHead.Add(ApplyBindings(head[j], anchor));
            }

            ApplyHyper(root, DlClause.Create(ScratchBody, ScratchHead, clause.Origin), []);
        }
    }

    /// <summary>Whether a literal span mentions the central variable in any slot, directly or under a Skolem function.</summary>
    /// <param name="literals">The literal span.</param>
    /// <returns><see langword="true"/> when <c>x</c> or <c>f(x)</c> occurs.</returns>
    private static bool MentionsCentral(ReadOnlySpan<DlLiteral> literals)
    {
        for(int i = 0; i < literals.Length; i++)
        {
            if(MentionsCentralTerm(literals[i].First) || MentionsCentralTerm(literals[i].Second))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a term mentions the central variable — <c>x</c> itself or a function term <c>f(x)</c> over it.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> when the term mentions <c>x</c>.</returns>
    private static bool MentionsCentralTerm(DlTerm term)
    {
        return term.Kind is DlTermKind.Central or DlTermKind.Function;
    }

    /// <summary>
    /// Mints one ground context per individual representative (core the
    /// representative's marker atom <c>O_a</c>, seeded so its class-assertion GCIs
    /// fire by ordinary Hyper) and resolves each ground-edge function symbol to its
    /// target representative's ground context — the designated-successor routing
    /// <see cref="ResolveCautiousSuccessor"/> consults. A setup-time operation
    /// that is a no-op for a module carrying no admitted ABox axiom.
    /// </summary>
    /// <param name="clausification">The module's clausification, carrying the representatives, markers, and the function-to-target map.</param>
    private void SetupGroundContexts(ClausificationResult clausification)
    {
        foreach(Utf8String representative in clausification.GroundRepresentatives)
        {
            int marker = clausification.GroundMarkers[representative];
            EnsureGroundContext(representative, marker);
        }

        foreach((int function, Utf8String representative) in clausification.GroundTargetByFunction)
        {
            int marker = clausification.GroundMarkers[representative];
            if(Structure.TryGetByCoreAtom(marker, out int contextId))
            {
                GroundContextByFunction[function] = contextId;
            }
        }
    }

    /// <summary>Ensures the ground context with core <c>{O_a(x)}</c> exists — reused through the core registry or created with its Core seed and empty-body firing — and records it as a ground context in BOTH conventionally-coupled halves of the registry: the structure's <see cref="ContextStructure.GroundContextIds"/> set (through <see cref="ContextStructure.MarkGround"/>) and the engine's <see cref="GroundContexts"/> representative list. The single writer of both keeps them in lockstep — a debug assertion enforces the coupling so a future edit cannot desync the halves. A setup-time operation, called once per representative before <see cref="Saturate"/>.</summary>
    /// <param name="representative">The individual representative the ground context stands for — the engine list's key half.</param>
    /// <param name="coreAtom">The representative's marker concept atom id — the structure set's key half.</param>
    /// <returns>The ground context id.</returns>
    private int EnsureGroundContext(Utf8String representative, int coreAtom)
    {
        int id;
        if(Structure.TryGetByCoreAtom(coreAtom, out int existing))
        {
            ContextsReused++;
            id = existing;
        }
        else
        {
            Context context = Structure.CreateContext([DlLiteral.Concept(coreAtom, DlTerm.Central)]);
            ContextsCreated++;
            SeedContext(context);
            id = context.Id;
        }

        Structure.MarkGround(id);
        GroundContexts.Add((representative, id));
        GroundContextsCreated++;
        Debug.Assert(GroundContexts.Count == Structure.GroundContextIds.Count, "The engine's ground-context representative list and the structure's ground-context id set are the two conventionally-coupled halves of one ground-context registry; the single writer keeps them in lockstep, one entry per distinct fresh marker.");

        return id;
    }

    /// <summary>Ensures the query context with core <c>{A(x)}</c> exists — reused through the registry or created with its Core seed and empty-body firing. A setup-time operation, called once per signature class before <see cref="Saturate"/>; each signature atom joins the query-atom set the license-scoped atom axis admits as rewrite targets.</summary>
    /// <param name="coreAtom">The concept atom id A whose query context is required.</param>
    /// <returns>The query context id.</returns>
    public int EnsureQueryContext(int coreAtom)
    {
        if(ParamodulationScope == NominalParamodulationScope.LicenseScoped)
        {
            QueryAtomSignatureAtoms ??= [];
            QueryAtomSignatureAtoms.Add(coreAtom);
        }

        if(Structure.TryGetByCoreAtom(coreAtom, out int existing))
        {
            ContextsReused++;
            Structure.MarkQuery(existing);

            return existing;
        }

        Context context = Structure.CreateContext([DlLiteral.Concept(coreAtom, DlTerm.Central)]);
        ContextsCreated++;
        Structure.MarkQuery(context.Id);
        SeedContext(context);

        return context.Id;
    }

    /// <summary>
    /// Saturates the structure to its fixpoint under the five rules. The
    /// eager-Hyper discipline drains the rule queue completely, then processes
    /// one Succ candidate, and repeats until both queues are empty. The budget
    /// is checked before each rule application; the token once per dequeued
    /// item.
    /// </summary>
    /// <param name="budget">The inference budget; zero on the inference axis is unbounded.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns><see cref="SaturationOutcome.Completed"/> at the fixpoint, or <see cref="SaturationOutcome.BudgetExhausted"/> when the budget stopped it.</returns>
    /// <exception cref="OperationCanceledException">The token fired.</exception>
    public SaturationOutcome Saturate(ReasoningBudget budget, CancellationToken cancellationToken)
    {
        Budget = budget;

        while(!BudgetExhausted)
        {
            if(EagerRuleQueue.Count > 0 || RuleQueue.Count > 0)
            {
                (int contextId, int clauseId) = EagerRuleQueue.Count > 0 ? EagerRuleQueue.Dequeue() : RuleQueue.Dequeue();
                cancellationToken.ThrowIfCancellationRequested();
                Context context = Structure[contextId];
                if(context.IsLive(clauseId))
                {
                    ProcessClause(context, clauseId);
                }

                continue;
            }

            if(SuccQueue.Count > 0)
            {
                (int contextId, DlTerm trigger) = SuccQueue.Dequeue();
                PendingSucc.Remove((contextId, trigger));
                cancellationToken.ThrowIfCancellationRequested();
                ProcessSucc(contextId, trigger);

                continue;
            }

            CertifyDisjunctiveData();
            LatchUndecidedKeyObligations();
            LatchUndecidedRootKeyObligations();
            LatchRootEqualityOutsideFold();

            //The fixpoint certification decides through the budget-ticked oracle, so
            //it can exhaust the ceiling mid-scan — the budget latch then owns the
            //outcome exactly as for an in-saturation stop.
            return BudgetExhausted ? SaturationOutcome.BudgetExhausted : SaturationOutcome.Completed;
        }

        return SaturationOutcome.BudgetExhausted;
    }

    /// <summary>
    /// Whether the module is inconsistent: the trivial context holds the empty
    /// clause (the TBox-side criterion — a global <c>⊥</c>), or a ground context
    /// (core a forced marker <c>O_a</c>, non-empty because individual a denotes)
    /// holds it, or the post-saturation Self-ghost re-closure derived an edge-shape
    /// clash. A <c>⊥</c> in a named-class QUERY context is the subsumption
    /// <c>A ⊑ ⊥</c>, not inconsistency — a query context is never in the ground
    /// bucket.
    /// </summary>
    public bool IsInconsistent
    {
        get
        {
            if(Structure.TrivialContextId >= 0 && Structure[Structure.TrivialContextId].HasEmptyClause)
            {
                return true;
            }

            IReadOnlyList<int> rootClass = Structure.RootClassContextIds;
            for(int i = 0; i < rootClass.Count; i++)
            {
                //The N-reduction argument makes a root-class empty clause a sound
                //inconsistency witness regardless of generated-nominal participation:
                //the empty clause mentions no nominal, so no reduction set can satisfy
                //it — restricting the probe to input individuals would be UNSOUND, and
                //the eagerly applied Join rule folds conditional ground clashes into
                //this unconditional form, so the reader stays a simple probe. The scan
                //covers EVERY root-class context: the inter-nominal carrier can land a
                //clash-completing image in a foreign individual's nominal root, so a
                //dropped member would silently lose a verdict.
                if(Structure[rootClass[i]].HasEmptyClause)
                {
                    return true;
                }
            }

            if(GroundGhostClashDetected)
            {
                return true;
            }

            foreach(int groundId in Structure.GroundContextIds)
            {
                if(Structure[groundId].HasEmptyClause)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>White-box probe (battery only): whether the trivial context holds the empty clause — the global-<c>⊥</c> criterion a b-local ground clash does NOT satisfy.</summary>
    internal bool TrivialContextHasEmptyClause
    {
        get
        {
            return Structure.TrivialContextId >= 0 && Structure[Structure.TrivialContextId].HasEmptyClause;
        }
    }

    /// <summary>White-box probe (battery only): whether the ground context cored by a marker atom holds the empty clause — the battery's assertion that a b-local <c>⊥</c> landed in the named target's ground context.</summary>
    /// <param name="markerAtom">The representative's marker concept atom id.</param>
    /// <returns><see langword="true"/> when the marker's ground context holds the empty clause.</returns>
    internal bool GroundContextHasEmptyClause(int markerAtom)
    {
        return Structure.TryGetByCoreAtom(markerAtom, out int contextId) && Structure[contextId].HasEmptyClause;
    }

    /// <summary>
    /// Runs the post-saturation Self-ghost pass: for each ground context, an
    /// unconditionally derived loop concept
    /// <c>Self_p(x)</c> head contributes the loop <c>p(a, a)</c> to the asserted-edge
    /// graph; the pass then re-runs the FULL RBox closure over the ghost-augmented
    /// graph and re-checks every edge-shape clash, latching a module inconsistency on
    /// a clash. Membership-only re-checking is unsound — a ghost loop can be a chain
    /// component recomposing to a non-simple super-role a negative assertion then
    /// condemns. The caller runs it ONLY on a completed saturation; on a budget stop
    /// the partial ghost set is never trusted.
    /// </summary>
    public void RunGroundGhostPass()
    {
        if(GroundContexts.Count == 0 || GroundSelfLoopConcepts.Count == 0)
        {
            return;
        }

        List<(Utf8String Node, RawRoleId Role)> loops = [];
        foreach((Utf8String representative, int contextId) in GroundContexts)
        {
            Context context = Structure[contextId];
            foreach((int selfAtom, RoleRepresentative loopBase) in GroundSelfLoopConcepts)
            {
                if(context.UnconditionalContains(DlLiteral.Concept(selfAtom, DlTerm.Central)))
                {
                    //A representative is itself the minimal member of its class, so the loop
                    //lands as that member's raw edge; the re-closure's mutual and inverse-coupled
                    //arcs lift it onto every spelling of both coupled classes.
                    loops.Add((representative, loopBase.RawMemberId));
                }
            }
        }

        if(loops.Count == 0 || !GroundGraph.AddSelfLoops(loops))
        {
            return;
        }

        GroundGraph.Close();
        if(GroundGraph.DetectClash() is not null)
        {
            GroundGhostClashDetected = true;
            GroundClashes++;
        }
    }

    /// <summary>
    /// Whether <c>A ⊑ B</c> reads off the saturated query context of A
    /// (Definition 4): some live clause has a body that is a subset of
    /// <c>{A(x)}</c> and a head that is a subset of <c>{B(x)}</c> — the
    /// witnesses <c>⊤→B(x)</c>, <c>A(x)→B(x)</c>, <c>⊤→⊥</c>, or <c>A(x)→⊥</c>,
    /// the <c>⊥</c> forms making an unsatisfiable class subsumed by everything.
    /// A missing query context reads as not subsumed.
    /// </summary>
    /// <param name="subClassAtom">The subclass concept atom id A.</param>
    /// <param name="superClassAtom">The superclass concept atom id B.</param>
    /// <returns><see langword="true"/> when the query context witnesses the subsumption.</returns>
    public bool IsSubsumedBy(int subClassAtom, int superClassAtom)
    {
        if(!Structure.TryGetByCoreAtom(subClassAtom, out int queryId))
        {
            return false;
        }

        Context query = Structure[queryId];
        DlLiteral subAtom = DlLiteral.Concept(subClassAtom, DlTerm.Central);
        DlLiteral superAtom = DlLiteral.Concept(superClassAtom, DlTerm.Central);
        for(int id = 0; id < query.ClauseCapacity; id++)
        {
            if(!query.IsLive(id))
            {
                continue;
            }

            DlClause clause = query.At(id);
            if(IsSubsetOfSingle(clause.Body, subAtom) && IsSubsetOfSingle(clause.Head, superAtom))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Appends one row per root-class context in id order — the per-<c>v_o</c> population distribution read for measurement probes: the single root alone under <see cref="RootContextTopology.SingleRoot"/>, one row per resolved nominal root under the fragmented topology. Probes read distributions; the shipped statistics stay scalar aggregates.</summary>
    /// <param name="rowsToAppendTo">The row list appended to.</param>
    public void AppendRootClassPopulation(List<RootClassPopulationRow> rowsToAppendTo)
    {
        for(int id = 0; id < Structure.Count; id++)
        {
            Context context = Structure[id];
            if(!context.IsRoot)
            {
                continue;
            }

            rowsToAppendTo.Add(new RootClassPopulationRow(id, context.HomeIndividual, context.HomeIndividual >= 0 ? Symbols.RenderIndividual(context.HomeIndividual) : "vr", context.LiveCount));
        }

    }

    /// <summary>Whether the root-tier ≈-class surface was allocated — <see langword="true"/> once a root-landed equality has fed it; the zero-touch observation a nominal-free control reads. Dark this step.</summary>
    internal bool HasRootApproxSurface
    {
        get
        {
            return RootClasses is not null;
        }
    }

    /// <summary>The number of root-class contexts carrying a per-constant index — zero on a nominal-free module, whose contexts are never root; the zero-touch observation a control reads. Dark this step.</summary>
    internal int RootConstantIndexContextCount
    {
        get
        {
            IReadOnlyList<int> roots = Structure.RootClassContextIds;
            int count = 0;
            for(int i = 0; i < roots.Count; i++)
            {
                if(Structure[roots[i]].HasRootConstantIndex)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Whether two interned individual ids share a root-tier ≈-class; an id no equality has merged is its own singleton class, so this reads <c>first == second</c> when the surface is unallocated. Dark this step.</summary>
    /// <param name="first">The first individual id.</param>
    /// <param name="second">The second individual id.</param>
    /// <returns><see langword="true"/> when both resolve to the same class.</returns>
    internal bool RootApproxSameClass(int first, int second)
    {
        return RootClasses is null ? first == second : RootClasses.SameClass(first, second);
    }

    /// <summary>The root-tier ≈-class representative of an individual id — the lowest id merged with it, or the id itself when unmerged. Dark this step.</summary>
    /// <param name="individual">The individual id.</param>
    /// <returns>The class representative id.</returns>
    internal int RootApproxRepresentative(int individual)
    {
        return RootClasses is null ? individual : RootClasses.Find(individual);
    }

    /// <summary>Appends the individual ids sharing an id's root-tier ≈-class to a reusable buffer, the queried id included — the class's spellings a read-time union walks. Dark this step.</summary>
    /// <param name="individual">The individual id whose class is enumerated.</param>
    /// <param name="spellingsToAppendTo">The buffer the class's individual ids are appended to.</param>
    internal void AppendRootApproxClassMembers(int individual, List<int> spellingsToAppendTo)
    {
        if(RootClasses is null)
        {
            spellingsToAppendTo.Add(individual);

            return;
        }

        RootClasses.AppendClassMembers(individual, spellingsToAppendTo);
    }

    /// <summary>Appends an individual's concept memberships <c>B(o)</c> summed over every root context that stored a spelling of it — a direct per-constant read WITHOUT ≈-resolution. Dark this step.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="symbolsToAppendTo">The buffer the concept symbols are appended to.</param>
    internal void AppendRootConceptSpelling(int individual, List<int> symbolsToAppendTo)
    {
        IReadOnlyList<int> roots = Structure.RootClassContextIds;
        for(int i = 0; i < roots.Count; i++)
        {
            Structure[roots[i]].AppendRootConceptMemberships(individual, symbolsToAppendTo);
        }
    }

    /// <summary>Appends the concept memberships pooled across an individual's whole ≈-class (the read-time union over the ≈-class surface): every live <c>B(o′)</c> for every spelling <c>o′</c> merged with the individual. Dark this step: the key join is the consumer.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="symbolsToAppendTo">The buffer the pooled concept symbols are appended to.</param>
    internal void AppendPooledRootConcepts(int individual, List<int> symbolsToAppendTo)
    {
        ScratchApproxMembers.Clear();
        AppendRootApproxClassMembers(individual, ScratchApproxMembers);
        for(int i = 0; i < ScratchApproxMembers.Count; i++)
        {
            AppendRootConceptSpelling(ScratchApproxMembers[i], symbolsToAppendTo);
        }
    }

    /// <summary>Appends an individual's outgoing role edges <c>S(o, o′)</c> summed over every root context that stored a spelling of it — a direct per-constant read WITHOUT ≈-resolution. Dark this step.</summary>
    /// <param name="individual">The source individual key.</param>
    /// <param name="edgesToAppendTo">The buffer the role edges are appended to.</param>
    internal void AppendRootRoleTargetSpelling(int individual, List<RootRoleEdge> edgesToAppendTo)
    {
        IReadOnlyList<int> roots = Structure.RootClassContextIds;
        for(int i = 0; i < roots.Count; i++)
        {
            Structure[roots[i]].AppendRootRoleTargets(individual, edgesToAppendTo);
        }
    }

    /// <summary>Appends the outgoing role edges pooled across an individual's whole ≈-class (the read-time union): every live <c>S(o′, ·)</c> for every spelling <c>o′</c> merged with the individual. Dark this step.</summary>
    /// <param name="individual">The source individual key.</param>
    /// <param name="edgesToAppendTo">The buffer the pooled role edges are appended to.</param>
    internal void AppendPooledRootRoleTargets(int individual, List<RootRoleEdge> edgesToAppendTo)
    {
        ScratchApproxMembers.Clear();
        AppendRootApproxClassMembers(individual, ScratchApproxMembers);
        for(int i = 0; i < ScratchApproxMembers.Count; i++)
        {
            AppendRootRoleTargetSpelling(ScratchApproxMembers[i], edgesToAppendTo);
        }
    }

    /// <summary>Whether any spelling in an individual's ≈-class carries a live data-demand marker <c>D(o)</c> (pooled over the ≈-class surface). Dark this step: the per-constant data obligations are the consumer.</summary>
    /// <param name="individual">The individual key.</param>
    /// <param name="marker">The data-demand marker concept atom.</param>
    /// <returns><see langword="true"/> when a merged spelling holds a live demand.</returns>
    internal bool ClassHasLiveRootDataDemand(int individual, int marker)
    {
        ScratchApproxMembers.Clear();
        AppendRootApproxClassMembers(individual, ScratchApproxMembers);
        IReadOnlyList<int> roots = Structure.RootClassContextIds;
        for(int i = 0; i < ScratchApproxMembers.Count; i++)
        {
            for(int r = 0; r < roots.Count; r++)
            {
                if(Structure[roots[r]].RootDataDemandCount(ScratchApproxMembers[i], marker) > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Snapshots the run's counters into the statistics slot.</summary>
    /// <param name="contextDecided">Whether the context engine produced the module's verdict (as opposed to a delegated decision).</param>
    /// <returns>The statistics.</returns>
    public ContextSaturationStatistics BuildStatistics(bool contextDecided)
    {
        OccurrenceTelemetry occurrence = SumOccurrenceTelemetry();

        return new ContextSaturationStatistics(
            contextDecided,
            InferenceAttempts,
            RuleApplications,
            CoreApplications,
            HyperApplications,
            SuccApplications,
            PredApplications,
            ElimApplications,
            EqApplications,
            IneqApplications,
            FactorApplications,
            DataClashApplications,
            JoinApplications,
            RootSuccApplications,
            RootPredApplications,
            NomApplications,
            ContextsCreated,
            ContextsReused,
            ClausesDerived,
            ClausesEliminated,
            MaxContextClauses,
            PreMergeUnions,
            GroundContextsCreated,
            GroundEdgesSeeded,
            GroundClashes,
            Symbols.GeneratedNominalCount,
            Symbols.MaxNominalLabelDepth,
            RootContextClauses,
            Structure.RootEdgeCount)
        {
            RedundantConclusions = RedundantConclusions,
            DuplicateContainmentHits = DuplicateContainmentHits,
            SubsumedContainmentHits = SubsumedContainmentHits,
            TautologyDrops = TautologyDrops,
            OutOfGrammarConclusions = OutOfGrammarConclusions,
            WorklistEnqueues = WorklistEnqueues,
            DisjunctiveDataProbes = DisjunctiveDataProbes,
            DisjunctiveDataRefutations = DisjunctiveDataRefutations,
            DisjunctiveDataNarrowings = DisjunctiveDataNarrowings,
            DisjunctiveDataCertifications = DisjunctiveDataCertifications,
            UncertifiedDisjunctiveDataLatches = UncertifiedDisjunctiveDataLatches,
            UndecidedDataObligationCount = UndecidedDataObligationCount,
            RootEqualityOutsideFoldHeads = RootEqualityOutsideFoldHeads,
            RootEqualityRidesAChoiceHeads = RootEqualityRidesAChoiceHeads,
            RootPredFilteredOffers = RootPredFilteredOffers,
            RootPredReofferedByGroundHead = RootPredReofferedByGroundHead,
            RelevanceTautologiesSeeded = RelevanceTautologiesSeeded,
            RootPredFromRegistrationSweep = RootPredRegistrationSweepLandings,
            RootPredFromNewRootEdge = RootPredNewRootEdgeLandings,
            RootPredFromPremise = RootPredPremiseLandings,
            RootPredFromBroadcast = RootPredBroadcastLandings,
            RootPredRegistrationSweepOffers = RootPredRegistrationSweepOffers,
            RootPredNewRootEdgeOffers = RootPredNewRootEdgeOffers,
            RootPredPremiseOffers = RootPredPremiseOffers,
            RootPredBroadcastOffers = RootPredBroadcastOffers,
            RootPredRegistrationSweepDuplicateHits = RootPredRegistrationSweepDuplicateHits,
            RootPredNewRootEdgeDuplicateHits = RootPredNewRootEdgeDuplicateHits,
            RootPredPremiseDuplicateHits = RootPredPremiseDuplicateHits,
            RootPredBroadcastDuplicateHits = RootPredBroadcastDuplicateHits,
            JoinOffers = JoinOffers,
            JoinDuplicateHits = JoinDuplicateHits,
            CoreOffers = CoreOffers,
            CoreDuplicateHits = CoreDuplicateHits,
            HyperOffers = HyperOffers,
            HyperDuplicateHits = HyperDuplicateHits,
            PredOffers = PredOffers,
            PredDuplicateHits = PredDuplicateHits,
            PredLandedTargetOffers = PredLandedTargetOffers,
            PredLandedPremiseOffers = PredLandedPremiseOffers,
            PredNewEdgeOffers = PredNewEdgeOffers,
            PredLandedTargetDuplicateHits = PredLandedTargetDuplicateHits,
            PredLandedPremiseDuplicateHits = PredLandedPremiseDuplicateHits,
            PredNewEdgeDuplicateHits = PredNewEdgeDuplicateHits,
            PredOdometerRuns = PredOdometerRuns,
            PredIntraRunDuplicateHits = PredIntraRunDuplicateHits,
            OriginClearReenqueues = OriginClearReenqueues,
            EqOffers = EqOffers,
            EqDuplicateHits = EqDuplicateHits,
            FactorOffers = FactorOffers,
            FactorDuplicateHits = FactorDuplicateHits,
            SuccOffers = SuccOffers,
            SuccDuplicateHits = SuccDuplicateHits,
            NomOffers = NomOffers,
            NomDuplicateHits = NomDuplicateHits,
            PushedArrivalOffers = PushedArrivalOffers,
            PushedArrivalDuplicateHits = PushedArrivalDuplicateHits,
            SidecarSeedOffers = SidecarSeedOffers,
            SidecarSeedDuplicateHits = SidecarSeedDuplicateHits,
            NominalRootContexts = Structure.RootClassContextIds.Count,
            InterNominalPropagations = InterNominalPropagations,
            InterNominalRedundant = InterNominalRedundant,
            EqScopeBlockedQueryAtom = EqScopeBlockedQueryAtom,
            EqScopeBlockedRootClass = EqScopeBlockedRootClass,
            EqScopeTagJoins = EqScopeTagJoins,
            RootPredRegistrationSweepSubsumedHits = RootPredRegistrationSweepSubsumedHits,
            RootPredNewRootEdgeSubsumedHits = RootPredNewRootEdgeSubsumedHits,
            RootPredPremiseSubsumedHits = RootPredPremiseSubsumedHits,
            RootPredBroadcastSubsumedHits = RootPredBroadcastSubsumedHits,
            JoinSubsumedHits = JoinSubsumedHits,
            CoreSubsumedHits = CoreSubsumedHits,
            HyperSubsumedHits = HyperSubsumedHits,
            PredSubsumedHits = PredSubsumedHits,
            PredLandedTargetSubsumedHits = PredLandedTargetSubsumedHits,
            PredLandedPremiseSubsumedHits = PredLandedPremiseSubsumedHits,
            PredNewEdgeSubsumedHits = PredNewEdgeSubsumedHits,
            PredLandedTargetLandings = PredLandedTargetLandings,
            PredLandedPremiseLandings = PredLandedPremiseLandings,
            PredNewEdgeLandings = PredNewEdgeLandings,
            EqSubsumedHits = EqSubsumedHits,
            FactorSubsumedHits = FactorSubsumedHits,
            SuccSubsumedHits = SuccSubsumedHits,
            NomSubsumedHits = NomSubsumedHits,
            PushedArrivalSubsumedHits = PushedArrivalSubsumedHits,
            SidecarSeedSubsumedHits = SidecarSeedSubsumedHits,
            JoinOfferingRuns = JoinOfferingRuns,
            JoinIntraRunDuplicateHits = JoinIntraRunDuplicateHits,
            EqOfferingRuns = EqOfferingRuns,
            EqIntraRunDuplicateHits = EqIntraRunDuplicateHits,
            RootBroadcastClauseCount = RootBroadcastClauseCount,
            CautiousCoreCeiling = CautiousCoreCeiling,
            CautiousCoresRegistered = CautiousCoresRegistered,
            HeadOccurrenceEntriesRegistered = occurrence.HeadEntriesRegistered,
            BodyOccurrenceEntriesRegistered = occurrence.BodyEntriesRegistered,
            HeadOccurrenceDistinctKeys = occurrence.HeadDistinctKeys,
            BodyOccurrenceDistinctKeys = occurrence.BodyDistinctKeys,
            SurvivorSweepProbes = occurrence.SweepProbes,
            SurvivorSweepPostingEntriesWalked = occurrence.SweepPostingEntriesWalked,
            PredAnchoredArmDispatches = PredAnchoredArmDispatches,
            PredOrdinaryArmDispatches = PredOrdinaryArmDispatches,
            PredAnchorInvariantTargetPasses = PredAnchorInvariantTargetPasses,
            PredAnchorPruned = PredAnchorPruned,
            PredBroadcastContainedSkips = PredBroadcastContainedSkips,
            PredOrdinaryInvariantTargetPasses = PredOrdinaryInvariantTargetPasses,
            PredBroadcastImageTargets = PredBroadcastImageTargets,
        };
    }

    /// <summary>
    /// Processes one clause-landed event, dispatching the rules ONCE PER MAXIMAL
    /// head literal of the landed clause (the maximal set is a
    /// singleton except on pure-band and pure-Pr heads): Hyper joins each maximal
    /// literal into the ontology body triggers, Eq rewrites in both dispatch
    /// directions (the landed clause as the equality premise and as the rewrite
    /// target), Factor factors a maximal positive equality against its
    /// maximal-side sharers, the Succ candidate enqueues for a maximal f-bearing
    /// literal (Table 2: <c>∆ ⋡ A</c> and <c>A</c> contains <c>f(x)</c> —
    /// residual disjuncts do not restrain the trigger), and Pred runs over the
    /// outgoing edges once per maximal atom (the landed clause as premise).
    /// Per-clause dispatches sit outside the loop: Pred over the incoming edges
    /// (the landed clause as target — the WHOLE head crosses the edge) and the
    /// data-obligation rule for a decided (single-literal) demand-marker head.
    /// </summary>
    /// <param name="context">The context the clause landed in.</param>
    /// <param name="clauseId">The landed clause's id.</param>
    private void ProcessClause(Context context, int clauseId)
    {
        DlClause clause = context.At(clauseId);
        ScratchProcessMaximal.Clear();
        if(clause.Head.Length > 0)
        {
            Order.CollectMaximalHead(clause.Head, ScratchProcessMaximal, GrammarKindOf(context));
        }

        bool rootMachinery = HasRootMachinery;
        for(int m = 0; m < ScratchProcessMaximal.Count && !BudgetExhausted; m++)
        {
            int selectedIndex = ScratchProcessMaximal[m];
            DlLiteral selected = clause.Head[selectedIndex];
            HyperFromGiven(context, clause, clauseId, selectedIndex);

            if(!BudgetExhausted)
            {
                ApplyEqDispatch(context, selected, clauseId, selectedIndex, CollectionsMarshal.AsSpan(ScratchProcessMaximal));
            }

            if(!BudgetExhausted && selected.Kind == DlLiteralKind.Equality)
            {
                ApplyFactor(context, clause, clauseId, selectedIndex);
            }

            if(selected.IsAtom && !BudgetExhausted)
            {
                CurrentPredOrigin = PredOrigin.LandedPremise;
                IReadOnlyList<ContextEdge> outgoing = Structure.Outgoing(context.Id);
                for(int i = 0; i < outgoing.Count && !BudgetExhausted; i++)
                {
                    ContextEdge edge = outgoing[i];
                    PredFromNewPremise(context, edge.Function, Structure[edge.Target], clauseId, selected);
                }
            }

            if(!BudgetExhausted && TryGetSuccTrigger(selected, out DlTerm succTrigger))
            {
                EnqueueSucc(context.Id, succTrigger);
            }

            if(rootMachinery && !BudgetExhausted)
            {
                JoinFromMaximalHead(context, clause, clauseId, selectedIndex);
            }

            if(rootMachinery && !context.IsRoot && !BudgetExhausted)
            {
                TryRootSucc(context, clause, clauseId, selectedIndex);
            }

            if(rootMachinery && !context.IsRoot && !BudgetExhausted)
            {
                RootPredFromPremise(context, selected);
            }

            if(rootMachinery && !context.IsRoot && !BudgetExhausted && PropagationRelevance == RootPropagationRelevance.GroundFiltered)
            {
                RelevanceDispatch(context, selected, clause.BodyLength == 0);
            }

            if(context.IsRoot && !BudgetExhausted)
            {
                NomFromGiven(context, clause, clauseId, selectedIndex);
            }
        }

        if(rootMachinery && !BudgetExhausted)
        {
            JoinFromGroundBody(context, clause, clauseId);
        }

        if(context.IsRoot && !BudgetExhausted)
        {
            RegisterRootPredEligible(context, clauseId, clause, !context.IsDerivedUnderChoice(clauseId));
        }

        if(IsPredEligible(clause, !context.IsDerivedUnderChoice(clauseId), false))
        {
            CurrentPredOrigin = PredOrigin.LandedTarget;
            IReadOnlyList<ContextEdge> incoming = Structure.Incoming(context.Id);
            for(int i = 0; i < incoming.Count && !BudgetExhausted; i++)
            {
                ContextEdge edge = incoming[i];
                AttemptPred(Structure[edge.Source], context, edge.Function, clause, pinnedPosition: -1, pinnedClauseId: -1);
            }
        }

        if(clause.Head.Length == 1 && !BudgetExhausted && !context.IsRoot && IsDataDemandHead(clause.Head[0]))
        {
            //The !IsRoot guard mirrors the disjunctive siblings below: the entry
            //translation respells a home-individual demand marker D(o) as D(x) inside
            //a nominal root, and running the constant-blind oracle lane there could
            //inject a decided verdict where the single-root topology delegates the
            //identical module — the widened root latch below is the sole root-class
            //route for a data demand. Central demand heads cannot land in the
            //single-root grammar, so the guard is behavior-identical there.
            RunDataObligations(context, clauseId);
        }

        if(DataDemandDescriptors.Count > 0 && !context.IsRoot && !BudgetExhausted && clause.Head.Length > 1 && HeadCarriesCentralDataMarker(clause.Head))
        {
            RunDisjunctiveDataObligations(context, clauseId);
        }

        if(context.IsRoot && clause.Head.Length == 1 && IsRootDataDemandHead(clause.Head[0]))
        {
            if(RootDataObligationsEnabled)
            {
                //The per-constant root arm: the demand is decided PER ≈-CLASS off the
                //pooled read-time union through the one root-aware data-obligation
                //method, so the constant-blind latch is bypassed for a unit root demand.
                //Dark by default.
                RunDataObligations(context, clauseId);
            }
            else
            {
                //The per-constant arm is off (a raw-engine census run): record the
                //root-landed data demand in the activity statistic without running the
                //arm — the census read of whether a demand reached a root context.
                RootDataDemandObserved = true;
            }
        }

        if(context.IsRoot && DataDemandDescriptors.Count > 0 && clause.Head.Length > 1 && HeadCarriesRootDataMarker(clause.Head))
        {
            //A disjunctive head carrying a data marker landed on the root context: the
            //per-constant arm decides only unit demands, so a marker that does not narrow
            //to a unit is an undecided per-constant obligation. The activity statistic
            //records the landing, and under the arm the residual routes to the named
            //DataObligationUndecidedOnRoot delegation.
            RootDataDemandObserved = true;
            if(RootDataObligationsEnabled)
            {
                HasDataObligationUndecidedOnRoot = true;
            }
        }

        if(context.IsRoot && !BudgetExhausted)
        {
            BridgeRootGroundLoops(context, clause, clauseId);
        }

        if(context.IsRoot && Topology == RootContextTopology.PerIndividualRoots && !BudgetExhausted)
        {
            FireInterNominalCarrier(context, clause, context.IsDerivedUnderChoice(clauseId));
        }
    }

    /// <summary>
    /// The inter-nominal propagation rule (carrier 1, final form): a derived or
    /// seeded <c>⊤</c>-clause landed in a nominal-root context whose head
    /// literals range over <c>{x, constants, f(x), f(o)}</c> — no occurrence of
    /// the predecessor variable <c>y</c>, which has no equality license — and
    /// mention foreign individuals fires ONCE PER DISTINCT FOREIGN INDIVIDUAL
    /// <c>o_i</c> across the whole head, adding the image
    /// <c>⊤ → (L_1 ∨ … ∨ L_k)[x/src][o_i/x]</c> to <c>o_i</c>'s nominal root.
    /// The <c>[x/src]</c> grounding step maps central occurrences to the source's
    /// home constant — sound there by the context clause <c>⊤ → x ≈ src</c> —
    /// promoting own Skolem terms <c>f(x) ↦ f(src)</c>; the re-centering
    /// <c>[o_i/x]</c> then demotes <c>o_i</c> and its <c>f(o_i)</c> images back
    /// to the central spelling. Whole heads relocate — single- or multi-literal,
    /// central-bearing or ground — which is what assembles the counting bridge's
    /// subject-side premises and lands scattered clash disjunctions where their
    /// denials live; a re-derived image is absorbed by the redundancy discipline,
    /// which bounds the reciprocal cascade.
    /// </summary>
    /// <param name="source">The nominal-root context the clause landed in.</param>
    /// <param name="clause">The landed clause.</param>
    /// <param name="sourceDerived">Whether the landed clause is <c>DerivedUnderChoice</c> (the origin bit): every foreign-root image inherits it, so a choice-riding body-empty clause is never laundered to <c>DecidedUnderNoChoice</c> across the carrier.</param>
    private void FireInterNominalCarrier(Context source, DlClause clause, bool sourceDerived)
    {
        if(clause.BodyLength != 0 || clause.Head.Length == 0)
        {
            return;
        }

        int src = source.HomeIndividual;
        ReadOnlySpan<DlLiteral> head = clause.Head;
        ScratchCarrierForeigns.Clear();
        for(int i = 0; i < head.Length; i++)
        {
            if(MentionsContextVariable(head[i]))
            {
                return;
            }

            CollectForeignIndividuals(head[i], src, ScratchCarrierForeigns);
        }

        for(int i = 0; i < ScratchCarrierForeigns.Count && !BudgetExhausted; i++)
        {
            int foreign = ScratchCarrierForeigns[i];
            Context target = GetOrCreateRootFor(foreign);
            ScratchCarrierHead.Clear();
            for(int j = 0; j < head.Length; j++)
            {
                ScratchCarrierHead.Add(CarrierImageLiteral(head[j], src, foreign));
            }

            ApplyInterNominal(target, DlClause.Create([], ScratchCarrierHead, DerivedOrigin), sourceDerived);
        }
    }

    /// <summary>Whether a literal mentions the predecessor variable <c>y</c> in any slot — the carrier's exclusion: imaging <c>y</c> has no equality license.</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> when a <c>Context</c>-kind term occurs.</returns>
    private static bool MentionsContextVariable(DlLiteral literal)
    {
        return literal.First.Kind == DlTermKind.Context
            || (literal.Kind != DlLiteralKind.Concept && literal.Second.Kind == DlTermKind.Context);
    }

    /// <summary>Collects the distinct foreign individuals a head literal mentions — bare constants and the individuals under <c>f(o)</c> images alike — into the carrier's firing list, first mention first.</summary>
    /// <param name="literal">The head literal.</param>
    /// <param name="src">The source context's home individual.</param>
    /// <param name="foreignsToAppendTo">The distinct-foreign accumulator.</param>
    private static void CollectForeignIndividuals(DlLiteral literal, int src, List<int> foreignsToAppendTo)
    {
        CollectForeignTerm(literal.First, src, foreignsToAppendTo);
        if(literal.Kind != DlLiteralKind.Concept)
        {
            CollectForeignTerm(literal.Second, src, foreignsToAppendTo);
        }
    }

    /// <summary>Collects one term's foreign individual, if any, deduplicated against the accumulator; the list stays tiny, so the linear containment scan is cheap.</summary>
    /// <param name="term">The term.</param>
    /// <param name="src">The source context's home individual.</param>
    /// <param name="foreignsToAppendTo">The distinct-foreign accumulator.</param>
    private static void CollectForeignTerm(DlTerm term, int src, List<int> foreignsToAppendTo)
    {
        if(term.Kind is not (DlTermKind.Individual or DlTermKind.FunctionOfIndividual))
        {
            return;
        }

        int individual = term.IndividualId;
        if(individual == src || foreignsToAppendTo.Contains(individual))
        {
            return;
        }

        foreignsToAppendTo.Add(individual);
    }

    /// <summary>The carrier image of one head literal under <c>[x/src][o_i/x]</c>, term-wise.</summary>
    /// <param name="literal">The head literal.</param>
    /// <param name="src">The source context's home individual.</param>
    /// <param name="foreign">The imaged-toward foreign individual <c>o_i</c>.</param>
    /// <returns>The image literal.</returns>
    private static DlLiteral CarrierImageLiteral(DlLiteral literal, int src, int foreign)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(literal.Symbol, CarrierImageTerm(literal.First, src, foreign)),
            DlLiteralKind.Role => DlLiteral.Role(literal.Symbol, CarrierImageTerm(literal.First, src, foreign), CarrierImageTerm(literal.Second, src, foreign)),
            DlLiteralKind.Equality => DlLiteral.Equality(CarrierImageTerm(literal.First, src, foreign), CarrierImageTerm(literal.Second, src, foreign)),
            _ => DlLiteral.Inequality(CarrierImageTerm(literal.First, src, foreign), CarrierImageTerm(literal.Second, src, foreign)),
        };
    }

    /// <summary>The carrier image of one term: the grounding step <c>x ↦ src</c> / <c>f(x) ↦ f(src)</c> composed with the re-centering <c>o_i ↦ x</c> / <c>f(o_i) ↦ f(x)</c> — the same Function-versus-FunctionOfIndividual kind conversion the root Pred sigma performs at its two arms; foreign constants other than <c>o_i</c> are fixed.</summary>
    /// <param name="term">The term.</param>
    /// <param name="src">The source context's home individual.</param>
    /// <param name="foreign">The imaged-toward foreign individual <c>o_i</c>.</param>
    /// <returns>The image term.</returns>
    private static DlTerm CarrierImageTerm(DlTerm term, int src, int foreign)
    {
        return term.Kind switch
        {
            DlTermKind.Central => DlTerm.Individual(src),
            DlTermKind.Function => DlTerm.FunctionOf(term.Index, src),
            DlTermKind.Individual when term.IndividualId == foreign => DlTerm.Central,
            DlTermKind.FunctionOfIndividual when term.IndividualId == foreign => DlTerm.Function(term.FunctionSymbol),
            _ => term,
        };
    }

    /// <summary>Applies an inter-nominal carrier image under the budget gate, counting a landing per inserted clause and an absorption per image the redundancy discipline consumed — the convergence face of the reciprocal-imaging cascade. The source clause's origin bit rides through so a choice-riding image lands tagged at the foreign root rather than laundered.</summary>
    /// <param name="target">The foreign individual's nominal-root context.</param>
    /// <param name="image">The carrier image.</param>
    /// <param name="sourceDerived">Whether the imaged source clause was <c>DerivedUnderChoice</c>.</param>
    private void ApplyInterNominal(Context target, DlClause image, bool sourceDerived)
    {
        if(!TryApply())
        {
            return;
        }

        long redundantBefore = RedundantConclusions;
        if(AddPushedClause(target, image, sourceDerived))
        {
            InterNominalPropagations++;
            RuleApplications++;

            return;
        }

        if(RedundantConclusions > redundantBefore)
        {
            InterNominalRedundant++;
        }
    }

    /// <summary>
    /// The root ground-loop bridge: a head role literal <c>S(o, o)</c> — both
    /// slots the SAME constant — entails the loop concept <c>Self_p(o)</c> of the
    /// role's forward base, so the clause re-derives with that literal replaced
    /// by the loop concept atom. This is the root-context counterpart of the
    /// ground slice's asserted-loop concept emission and the self-variant pass's
    /// syntactic <c>x</c>-collapse: a derived constant loop (an Eq rewrite
    /// folding <c>S(o, o′)</c> under <c>o ≈ o′</c>, a merged assertion) reaches
    /// every loop consumer — the irreflexivity clash <c>Self_p(x) → ⊥</c>, a
    /// self restriction, the asymmetry clash's collapsed variant — through the
    /// ordinary root Hyper dispatch. Sound literal-wise (<c>S(o, o)</c> entails
    /// <c>Self_p(o)</c> at the same constant, so replacing the disjunct weakens
    /// nothing) and terminating (no new terms; one derivation per bridged
    /// literal; a derived clause's remaining loop literals bridge on their own
    /// landing).
    /// </summary>
    /// <param name="context">The root context the clause landed in.</param>
    /// <param name="clause">The landed clause.</param>
    /// <param name="clauseId">The landed clause's id — the bridged premise the sink's tag inheritance reads.</param>
    private void BridgeRootGroundLoops(Context context, DlClause clause, int clauseId)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length && !BudgetExhausted; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind != DlLiteralKind.Role || !literal.First.IsIndividual || !literal.Second.IsIndividual || literal.First.Index != literal.Second.Index)
            {
                continue;
            }

            if(!RootLoopConceptByBase.TryGetValue(ContextSymbolTable.Forward(literal.Symbol), out int loopAtom))
            {
                continue;
            }

            ScratchBody.Clear();
            AppendSpan(ScratchBody, clause.Body);
            ScratchHead.Clear();
            for(int j = 0; j < head.Length; j++)
            {
                ScratchHead.Add(j == i ? DlLiteral.Concept(loopAtom, head[j].First) : head[j]);
            }

            ApplyCore(context, DlClause.Create(ScratchBody, ScratchHead, DerivedOrigin), [clauseId]);
        }
    }

    /// <summary>Whether a head atom is a data-demand marker in a root-class spelling — <c>D(o)</c> at a constant, or <c>D(x)</c> where the entry translation respelled a home-individual marker central (the call site guards the root-class context) — the shapes whose landing on a root-class context the per-constant root arm decides (or, when the arm is off, records in <see cref="RootDataDemandObserved"/>). Both spellings route the same way because the sidecar is constant-blind whichever way the constant is written; central-spelled markers cannot land in the single root, whose grammar carries no central terms, so the widening is behavior-identical there.</summary>
    /// <param name="atom">The clause's single head atom.</param>
    /// <returns><see langword="true"/> for a root-class demand-marker head.</returns>
    private bool IsRootDataDemandHead(DlLiteral atom)
    {
        return atom.Kind == DlLiteralKind.Concept && (atom.First.IsIndividual || atom.First.IsCentral) && DataDemandDescriptors.ContainsKey(atom.Symbol);
    }

    /// <summary>Whether a head atom is a data-demand marker on the central variable — the <c>marker(x)</c> shape whose landing triggers the data-obligation rule.</summary>
    /// <param name="atom">The clause's single head atom.</param>
    /// <returns><see langword="true"/> when the atom heads a demand-marker clause.</returns>
    private bool IsDataDemandHead(DlLiteral atom)
    {
        return atom.Kind == DlLiteralKind.Concept && atom.First.IsCentral && DataDemandDescriptors.ContainsKey(atom.Symbol);
    }

    /// <summary>
    /// The data-obligation rule (§3.3): when a data-demand marker lands, decide the
    /// context's live-demand set through the shared datatype sidecar
    /// (<see cref="DataRestrictionConsistency"/>) against the module box. A per-context
    /// memo keyed by the sorted live-demand signature skips the ORACLE for a context
    /// whose demand set did not change — but a landing that adds a new contributor
    /// clause for an already-proven conflict set still emits that clause's clash
    /// combinations, so every derivable route to a known clash is covered. Each
    /// sidecar oracle invocation consumes budget through
    /// <see cref="OracleBudgetTick"/> (ceiling-before-wall). A clash caches its
    /// conflict marker set and injects one <c>⋃Body → Bottom(x)</c> clause per
    /// combination of live contributor clauses — the virtual <c>Bottom(x) → ⊥</c>
    /// ontology clause propagates each to the empty clause. An undecided obligation
    /// latches <see cref="HasUndecidedDataObligation"/>, the reasoner's delegation
    /// signal, unless the budget latch already owns the outcome; a consistent one
    /// adds nothing. Every landing also re-triggers the disjunctive lane: the landed
    /// contributor completes any recorded narrowing core it belongs to (both memo
    /// paths), and a changed pool re-probes the context's live disjunctive marker
    /// clauses through the per-context index.
    /// </summary>
    /// <param name="context">The context the demand marker landed in.</param>
    /// <param name="landedClauseId">The id of the demand-marker clause whose landing triggered the rule.</param>
    private void RunDataObligations(Context context, int landedClauseId)
    {
        if(context.IsRoot)
        {
            //The root arm (ONE method, root-aware): on a root-class context the demand
            //is decided PER ≈-CLASS off the pooled read-time union through the root-only
            //per-class memo (the ordinary lane below stays byte-identical — its
            //context-id-keyed memo helpers are untouched). Reached only under the root
            //data-obligation switch.
            RunRootDataObligationsForLanded(context, landedClauseId);

            return;
        }

        ScratchLiveDemands.Clear();
        context.CollectLiveDataDemands(ScratchLiveDemands);
        ScratchLiveDemands.Sort();
        if(SignatureUnchanged(context.Id, ScratchLiveDemands))
        {
            EmitForLandedContributor(context, landedClauseId);
            EmitDisjunctiveNarrowingsForLandedContributor(context, landedClauseId);

            return;
        }

        if(!TryApply())
        {
            return;
        }

        RecordSignature(context.Id, ScratchLiveDemands);

        ScratchDataConcepts.Clear();
        ScratchConceptToMarker.Clear();
        for(int i = 0; i < ScratchLiveDemands.Count; i++)
        {
            int marker = ScratchLiveDemands[i];
            AlcConcept concept = ReconstructDemand(DataDemandDescriptors[marker]);
            ScratchDataConcepts.Add(concept);
            ScratchConceptToMarker[concept] = marker;
        }

        DataConsistencyStatus status = DataRestrictionConsistency.Decide(ScratchDataConcepts, DataBox, OracleBudgetTick, Registry, out IReadOnlyList<AlcConcept> conflict, out bool selfCertified);
        HasSelfCertifiedDataDecision |= selfCertified;
        switch(status)
        {
            case(DataConsistencyStatus.Clash):
            {
                int[] conflictMarkers = ConflictMarkersOf(conflict);
                if(conflictMarkers.Length == conflict.Count)
                {
                    RememberConflictSet(context.Id, conflictMarkers);
                    EmitDataClashCombinations(context, conflictMarkers, requiredClauseId: -1);
                }

                break;
            }

            case(DataConsistencyStatus.Undecided):
            {
                if(!BudgetExhausted)
                {
                    HasUndecidedDataObligation = true;
                    UndecidedDataObligationCount++;
                    RecordUndecidedDataObligationProperties();
                }

                break;
            }

            default:
            {
                break;
            }
        }

        //The pool changed on this path: a landed unit contributor can complete a
        //recorded narrowing core (the semi-naive re-emission) and can turn a
        //not-yet-refutable disjunctive marker refutable (the growth re-probe over
        //the per-context index; the per-clause memo keeps a stale-signature visit
        //from re-running the oracle).
        EmitDisjunctiveNarrowingsForLandedContributor(context, landedClauseId);
        ReprobeDisjunctiveClauses(context);
    }

    /// <summary>
    /// The root arm's demand-landing entry: resolves the landed unit demand head to the
    /// individual it names — the home
    /// slot via <see cref="Context.HomeIndividual"/>, a foreign constant by its term —
    /// and re-decides that individual's whole ≈-class. A head whose keyed slot resolves
    /// to no individual (never the SingleRoot <c>D(o)</c> or the <c>v_o</c> home shape)
    /// files nothing.
    /// </summary>
    /// <param name="context">The root-class context the demand marker landed in.</param>
    /// <param name="landedClauseId">The id of the landed unit demand-marker clause.</param>
    private void RunRootDataObligationsForLanded(Context context, int landedClauseId)
    {
        DlLiteral head = context.At(landedClauseId).Head[0];
        if(RootTermResolution.TryResolveIndividual(head.First, context.HomeIndividual, out int individual))
        {
            DecideRootDataClass(context, individual);
        }
    }

    /// <summary>
    /// Re-decides a root ≈-class's pooled data obligations after an equality rewrite
    /// touched it (the re-probe hook): the merge landed at the equality-head
    /// <see cref="AddClause"/>
    /// feed BEFORE the worklist processed the equality, so <see cref="RootApproxRepresentative"/>
    /// of the replacement already reflects the merged class. The re-decision re-collects
    /// the FULL pooled demand set by read-time union — robust to which literal the
    /// rewrite touched — so a merge that pools a new demand into the class is caught even
    /// when its landed clause is not itself a demand marker; the per-class signature memo
    /// leaves an unchanged pool untouched (the biconditional). A replacement that resolves
    /// to no individual (a function-bearing term) touches no class.
    /// </summary>
    /// <param name="context">The root-class context the rewrite landed in.</param>
    /// <param name="replacement">The equality's replacement term — the side the rewrite rewrote toward.</param>
    private void ReprobeRootDataAfterMerge(Context context, DlTerm replacement)
    {
        if(RootTermResolution.TryResolveIndividual(replacement, context.HomeIndividual, out int individual))
        {
            DecideRootDataClass(context, individual);
        }
    }

    /// <summary>
    /// Decides one root ≈-class's pooled data obligations: the pooled demand set is the
    /// read-time union of every live <c>D(o)</c> over the class's spellings. A per-class
    /// memo keyed
    /// (context, class representative) — a NEW structure, never the ordinary lane's
    /// context-id memo — skips the oracle on an unchanged pool. A pool without a
    /// value-forcing demand certifies without an oracle call (the scale guard). A CLASH
    /// injects the per-class closure <c>⊤ → Bottom(o_rep)</c>, which the virtual
    /// <c>Bottom(o) → ⊥</c> root form (the constant-anchored Hyper against
    /// <c>Bottom(x) → ⊥</c> under <c>σ(x) = o_rep</c>) propagates to the empty clause —
    /// closing ONLY that class, never a sibling class of a different individual (P-GC6).
    /// An UNDECIDED obligation latches <see cref="HasDataObligationUndecidedOnRoot"/>,
    /// the reasoner's named delegation signal, unless the budget latch already owns the
    /// outcome; a consistent pool adds nothing. Each oracle invocation rides the
    /// budget-ticked sidecar.
    /// </summary>
    /// <param name="context">The root-class context the class is decided in.</param>
    /// <param name="individual">Any spelling of the ≈-class to decide.</param>
    private void DecideRootDataClass(Context context, int individual)
    {
        int representative = RootApproxRepresentative(individual);
        ScratchRootDemands.Clear();
        CollectPooledRootDemands(individual, ScratchRootDemands);
        ScratchRootDemands.Sort();
        if(RootDataSignatureUnchanged(context.Id, representative, ScratchRootDemands))
        {
            return;
        }

        if(!HasValueForcingDemand(ScratchRootDemands))
        {
            //No value-forcing demand in the class pool: every obligation is vacuously
            //satisfied by an individual with no filler, so the class certifies without an
            //oracle call — the scale guard, and the recorded signature keeps a re-landing
            //of the same pool from re-deciding.
            RecordRootDataSignature(context.Id, representative, ScratchRootDemands);

            return;
        }

        if(!TryApply())
        {
            return;
        }

        RecordRootDataSignature(context.Id, representative, ScratchRootDemands);

        ScratchRootConcepts.Clear();
        ScratchRootConceptToMarker.Clear();
        for(int i = 0; i < ScratchRootDemands.Count; i++)
        {
            int marker = ScratchRootDemands[i];
            AlcConcept concept = ReconstructDemand(DataDemandDescriptors[marker]);
            ScratchRootConcepts.Add(concept);
            ScratchRootConceptToMarker[concept] = marker;
        }

        DataConsistencyStatus status = DataRestrictionConsistency.Decide(ScratchRootConcepts, DataBox, OracleBudgetTick, Registry, out IReadOnlyList<AlcConcept> conflict, out bool selfCertified);
        HasSelfCertifiedDataDecision |= selfCertified;
        switch(status)
        {
            case(DataConsistencyStatus.Clash):
            {
                EmitRootDataClash(context, representative);

                break;
            }

            case(DataConsistencyStatus.Undecided):
            {
                if(!BudgetExhausted)
                {
                    HasDataObligationUndecidedOnRoot = true;
                    UndecidedDataObligationCount++;
                    RecordUndecidedRootDataProperties();
                }

                break;
            }

            default:
            {
                break;
            }
        }
    }

    /// <summary>Appends the data-demand markers a root ≈-class holds live across its spellings — the pooled demand set the root arm decides, collected by read-time union over the ≈-class surface. The descriptor set is the module's admitted data restrictions, so the scan is bounded by the demand-marker count.</summary>
    /// <param name="individual">Any spelling of the ≈-class.</param>
    /// <param name="markersToAppendTo">The buffer the pooled live demand markers are appended to.</param>
    private void CollectPooledRootDemands(int individual, List<int> markersToAppendTo)
    {
        foreach(int marker in DataDemandDescriptors.Keys)
        {
            if(ClassHasLiveRootDataDemand(individual, marker))
            {
                markersToAppendTo.Add(marker);
            }
        }
    }

    /// <summary>Injects the per-class data clash closure <c>⊤ → Bottom(o_rep)</c> at a root ≈-class's representative: the root demands are unconditional root facts, so the closure body is empty. The constant-anchored root Hyper fires the virtual <c>Bottom(x) → ⊥</c> ontology clause under <c>σ(x) = o_rep</c>, propagating the closure to the empty clause — a whole-module inconsistency attributed to that class alone, never a sibling class. Passes the <see cref="TryApply"/> budget gate.</summary>
    /// <param name="context">The root-class context the clash holds in.</param>
    /// <param name="representative">The ≈-class representative the closure names.</param>
    private void EmitRootDataClash(Context context, int representative)
    {
        if(!TryApply())
        {
            return;
        }

        ScratchHead.Clear();
        ScratchHead.Add(DlLiteral.Concept(ContextSymbolTable.Bottom, DlTerm.Individual(representative)));
        SidecarSeedOffers++;
        ClauseOfferOutcome outcome = AddClauseCore(context, DlClause.Create([], ScratchHead, DerivedOrigin), []);
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            DataClashApplications++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            SidecarSeedDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            SidecarSeedSubsumedHits++;
        }
    }

    /// <summary>Whether a root ≈-class's pooled demand signature equals the one last decided for it, keyed by (context, class representative) — the root-only per-class memo hit that skips re-deciding an unchanged pool.</summary>
    /// <param name="contextId">The root-class context id.</param>
    /// <param name="representative">The ≈-class representative.</param>
    /// <param name="current">The current sorted pooled demand markers.</param>
    /// <returns><see langword="true"/> when the pooled signature is unchanged since the last decision.</returns>
    private bool RootDataSignatureUnchanged(int contextId, int representative, List<int> current)
    {
        if(!RootDataSignature.TryGetValue((contextId, representative), out int[]? last) || last.Length != current.Count)
        {
            return false;
        }

        for(int i = 0; i < current.Count; i++)
        {
            if(last[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Records a root ≈-class's pooled demand signature as the one last decided, keyed by (context, class representative).</summary>
    /// <param name="contextId">The root-class context id.</param>
    /// <param name="representative">The ≈-class representative.</param>
    /// <param name="current">The current sorted pooled demand markers.</param>
    private void RecordRootDataSignature(int contextId, int representative, List<int> current)
    {
        RootDataSignature[(contextId, representative)] = [.. current];
    }

    /// <summary>Records the demand property IRIs of the current pooled root demand set (<see cref="ScratchRootDemands"/>) into <see cref="UndecidedDataObligationPropertiesList"/>, deduplicated — the diagnostic side effect of an undecided per-constant root obligation, naming the <c>DataObligationUndecidedOnRoot</c> delegation.</summary>
    private void RecordUndecidedRootDataProperties()
    {
        for(int i = 0; i < ScratchRootDemands.Count; i++)
        {
            string property = DataDemandDescriptors[ScratchRootDemands[i]].Property.ToString();
            if(!UndecidedDataObligationPropertiesList.Contains(property))
            {
                UndecidedDataObligationPropertiesList.Add(property);
            }
        }
    }

    /// <summary>Whether a multi-literal head carries a data-demand marker on the central variable — the disjunctive probe's trigger shape and the certification's survivor shape.</summary>
    /// <param name="head">The clause head.</param>
    /// <returns><see langword="true"/> when a central data marker rides the head.</returns>
    private bool HeadCarriesCentralDataMarker(ReadOnlySpan<DlLiteral> head)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(IsDataDemandHead(head[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a multi-literal head carries a data-demand marker over the central variable or a constant — the root-latch shape: the root universe instantiates at constants, so both spellings route a root disjunctive data demand to the named delegation.</summary>
    /// <param name="head">The clause head.</param>
    /// <returns><see langword="true"/> when a data marker rides the head in either spelling.</returns>
    private bool HeadCarriesRootDataMarker(ReadOnlySpan<DlLiteral> head)
    {
        for(int i = 0; i < head.Length; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind == DlLiteralKind.Concept && (literal.First.IsCentral || literal.First.IsIndividual) && DataDemandDescriptors.ContainsKey(literal.Symbol))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a head carries the one demand marker, located by predicate scan — never by positional index, since a canonicalised multi-literal head does not sort its marker to a fixed position.</summary>
    /// <param name="head">The clause head.</param>
    /// <param name="marker">The demand-marker atom.</param>
    /// <returns><see langword="true"/> when the marker rides the head on the central variable.</returns>
    private static bool HeadCarriesMarker(ReadOnlySpan<DlLiteral> head, int marker)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(head[i].Kind == DlLiteralKind.Concept && head[i].First.IsCentral && head[i].Symbol == marker)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a sorted marker array holds the marker; narrowing cores are small, so the linear scan stays cheap.</summary>
    /// <param name="markers">The marker array.</param>
    /// <param name="marker">The sought marker.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool ContainsMarker(int[] markers, int marker)
    {
        for(int i = 0; i < markers.Length; i++)
        {
            if(markers[i] == marker)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any marker of the list reconstructs to a value-forcing demand — an Existential or a MinCardinality kind. A set of Universal- and MaxCardinality-kind obligations alone is satisfied vacuously by a node with no filler — neither a range constraint nor an upper bound forces a value — so a probe or joint certification over it never needs the oracle; this is the scale guard that keeps a corpus's per-context dual seeding from flooding the sidecar with trivially consistent decides.</summary>
    /// <param name="markers">The demand markers.</param>
    /// <returns><see langword="true"/> when a value-forcing demand is present.</returns>
    private bool HasValueForcingDemand(List<int> markers)
    {
        for(int i = 0; i < markers.Count; i++)
        {
            if(DataDemandDescriptors[markers[i]].Kind is DataDemandKind.Existential or DataDemandKind.MinCardinality)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a recorded signature equals the current sorted marker list — the per-clause probe memo's hit test and the joint certification memo's.</summary>
    /// <param name="recorded">The recorded sorted signature.</param>
    /// <param name="current">The current sorted markers.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool SameSignature(int[] recorded, List<int> current)
    {
        if(recorded.Length != current.Count)
        {
            return false;
        }

        for(int i = 0; i < recorded.Length; i++)
        {
            if(recorded[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The disjunctive data-refutation rule: probes EVERY central data marker on a
    /// live multi-literal head against the context's unit-forced pool — one
    /// sidecar decide over the pool plus the probed marker's obligation. A clash
    /// whose core contains the probed marker refutes it: the marker is recorded
    /// with its narrowing core (the core minus itself) and every live disjunctive
    /// clause carrying it gets its body-conditioned residual-head narrowings. A
    /// pool-only clash is the unit rule's jurisdiction and fires nothing here. An
    /// undecided probe latches the undecided-obligation delegation unless the
    /// budget latch owns the outcome; a consistent probe leaves the marker a
    /// survivor for the fixpoint certification. A marker already refuted emits
    /// this clause's narrowings off the record instead of re-deciding. The
    /// per-clause signature memo bounds each (clause, signature) pair to one
    /// probe; a budget stop records nothing.
    /// </summary>
    /// <param name="context">The context the disjunctive marker clause lives in.</param>
    /// <param name="clauseId">The disjunctive marker clause's id.</param>
    private void RunDisjunctiveDataObligations(Context context, int clauseId)
    {
        if(!context.IsLive(clauseId))
        {
            return;
        }

        ScratchDisjunctivePool.Clear();
        context.CollectLiveDataDemands(ScratchDisjunctivePool);
        ScratchDisjunctivePool.Sort();
        if(DisjunctiveProbeSignatures.TryGetValue((context.Id, clauseId), out int[]? lastProbed) && SameSignature(lastProbed, ScratchDisjunctivePool))
        {
            return;
        }

        bool poolForcesValue = HasValueForcingDemand(ScratchDisjunctivePool);
        ReadOnlySpan<DlLiteral> head = context.At(clauseId).Head;
        for(int i = 0; i < head.Length && !BudgetExhausted; i++)
        {
            if(!IsDataDemandHead(head[i]))
            {
                continue;
            }

            int marker = head[i].Symbol;
            if(!poolForcesValue && DataDemandDescriptors[marker].Kind is DataDemandKind.Universal or DataDemandKind.MaxCardinality)
            {
                //No value-forcing demand anywhere in the probe set: every obligation is
                //vacuously satisfied by a filler-free node, so the marker survives
                //without an oracle call — a refutation needs a forced value to clash on.
                //An upper bound is as vacuous as a range constraint here.
                continue;
            }

            if(DisjunctiveConflictSets.TryGetValue(context.Id, out Dictionary<int, int[]>? refutedSets) && refutedSets.ContainsKey(marker))
            {
                //Already refuted against this context's pool: a clause that landed
                //after the refutation still needs ITS narrowing combinations — the
                //record-time emission covered only the clauses live then.
                EmitDisjunctiveDataNarrowing(context, clauseId, requiredClauseId: -1);

                continue;
            }

            if(!TryApply())
            {
                return;
            }

            ScratchDisjunctiveConcepts.Clear();
            ScratchDisjunctiveConceptToMarker.Clear();
            bool markerInPool = false;
            for(int p = 0; p < ScratchDisjunctivePool.Count; p++)
            {
                int poolMarker = ScratchDisjunctivePool[p];
                AlcConcept obligation = ReconstructDemand(DataDemandDescriptors[poolMarker]);
                ScratchDisjunctiveConcepts.Add(obligation);
                ScratchDisjunctiveConceptToMarker[obligation] = poolMarker;
                markerInPool |= poolMarker == marker;
            }

            if(!markerInPool)
            {
                AlcConcept probed = ReconstructDemand(DataDemandDescriptors[marker]);
                ScratchDisjunctiveConcepts.Add(probed);
                ScratchDisjunctiveConceptToMarker[probed] = marker;
            }

            DisjunctiveDataProbes++;
            DataConsistencyStatus status = DataRestrictionConsistency.Decide(ScratchDisjunctiveConcepts, DataBox, OracleBudgetTick, Registry, out IReadOnlyList<AlcConcept> conflict, out bool selfCertified);
            HasSelfCertifiedDataDecision |= selfCertified;
            switch(status)
            {
                case(DataConsistencyStatus.Clash):
                {
                    int[] conflictMarkers = DisjunctiveConflictMarkersOf(conflict);
                    if(conflictMarkers.Length == conflict.Count && ContainsMarker(conflictMarkers, marker))
                    {
                        RecordDisjunctiveRefutation(context, marker, conflictMarkers);
                    }

                    break;
                }

                case(DataConsistencyStatus.Undecided):
                {
                    if(!BudgetExhausted)
                    {
                        HasUndecidedDataObligation = true;
                        UndecidedDataObligationCount++;
                        RecordUndecidedDisjunctiveDemandProperty(marker);
                    }

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        if(BudgetExhausted)
        {
            return;
        }

        DisjunctiveProbeSignatures[(context.Id, clauseId)] = [.. ScratchDisjunctivePool];
    }

    /// <summary>Maps a probe's conflict-core obligations back to their marker atoms through the probe's own concept map; a shorter array marks a core the emission cannot attribute, which the caller declines.</summary>
    /// <param name="conflict">The sidecar's conflict-core obligations.</param>
    /// <returns>The marker atoms of the mapped obligations.</returns>
    private int[] DisjunctiveConflictMarkersOf(IReadOnlyList<AlcConcept> conflict)
    {
        List<int> markers = new(conflict.Count);
        for(int i = 0; i < conflict.Count; i++)
        {
            if(ScratchDisjunctiveConceptToMarker.TryGetValue(conflict[i], out int marker))
            {
                markers.Add(marker);
            }
        }

        return [.. markers];
    }

    /// <summary>Records a marker refuted in a context with its narrowing core (the clash core minus the marker itself) and emits the body-conditioned narrowings for every live disjunctive clause of the context carrying it. Idempotent per (context, marker): a repeat clash re-emits nothing here — later contributor routes ride the semi-naive re-emission instead.</summary>
    /// <param name="context">The context the refutation holds in.</param>
    /// <param name="marker">The refuted disjunctive marker.</param>
    /// <param name="conflictMarkers">The full clash core, the refuted marker included.</param>
    private void RecordDisjunctiveRefutation(Context context, int marker, int[] conflictMarkers)
    {
        List<int> coreMarkers = new(conflictMarkers.Length);
        for(int i = 0; i < conflictMarkers.Length; i++)
        {
            if(conflictMarkers[i] != marker)
            {
                coreMarkers.Add(conflictMarkers[i]);
            }
        }

        int[] core = [.. coreMarkers];
        if(!DisjunctiveConflictSets.TryGetValue(context.Id, out Dictionary<int, int[]>? refuted))
        {
            refuted = [];
            DisjunctiveConflictSets[context.Id] = refuted;
        }

        if(!refuted.TryAdd(marker, core))
        {
            return;
        }

        DisjunctiveDataRefutations++;
        if(!LiveDisjunctiveMarkerClauses.TryGetValue(context.Id, out List<int>? disjunctiveIds))
        {
            return;
        }

        for(int i = 0; i < disjunctiveIds.Count && !BudgetExhausted; i++)
        {
            int clauseId = disjunctiveIds[i];
            if(context.IsLive(clauseId) && HeadCarriesMarker(context.At(clauseId).Head, marker))
            {
                EmitDisjunctiveDataNarrowing(context, clauseId, requiredClauseId: -1);
            }
        }
    }

    /// <summary>
    /// Emits the body-conditioned residual-head narrowings of one disjunctive
    /// clause under the context's recorded refutations (the structural sibling of
    /// <see cref="EmitDataClashCombinations"/>): the residual head is the parent's
    /// head minus every data marker refuted in this context — located by predicate
    /// scan, never positional index — and the body joins the parent's own body
    /// with one live single-head contributor clause per marker of the UNION of the
    /// removed markers' narrowing cores, so every removed disjunct is conditioned
    /// on the antecedents that force its refutation (dropping a second refuted
    /// marker on only the first one's core would assert a disjunct-free clause on
    /// nodes where that second refutation is not forced — a wrong verdict). Every
    /// combination of live contributors gets its own conditional narrowing; an
    /// empty residual becomes <c>Bottom(x)</c>, coinciding with the unit clash
    /// emission. A pinned <paramref name="requiredClauseId"/> restricts its
    /// marker's slot to the landed contributor (the semi-naive re-emission); a
    /// vanished contributor aborts the emission rather than inject an
    /// unsoundly-weak body. Each combination passes the budget gate.
    /// </summary>
    /// <param name="context">The context the refutations hold in.</param>
    /// <param name="disjunctiveClauseId">The disjunctive clause to narrow.</param>
    /// <param name="requiredClauseId">A contributor clause id the combinations must include, or -1 for the full cross-product.</param>
    private void EmitDisjunctiveDataNarrowing(Context context, int disjunctiveClauseId, int requiredClauseId)
    {
        if(!DisjunctiveConflictSets.TryGetValue(context.Id, out Dictionary<int, int[]>? refuted) || refuted.Count == 0)
        {
            return;
        }

        DlClause parent = context.At(disjunctiveClauseId);
        ReadOnlySpan<DlLiteral> head = parent.Head;
        ScratchResidualHead.Clear();
        List<int> slotMarkers = [];
        for(int i = 0; i < head.Length; i++)
        {
            DlLiteral literal = head[i];
            if(IsDataDemandHead(literal) && refuted.TryGetValue(literal.Symbol, out int[]? core))
            {
                for(int c = 0; c < core.Length; c++)
                {
                    if(!slotMarkers.Contains(core[c]))
                    {
                        slotMarkers.Add(core[c]);
                    }
                }

                continue;
            }

            ScratchResidualHead.Add(literal);
        }

        if(ScratchResidualHead.Count == head.Length)
        {
            return;
        }

        if(ScratchResidualHead.Count == 0)
        {
            ScratchResidualHead.Add(DlLiteral.Concept(ContextSymbolTable.Bottom, DlTerm.Central));
        }

        List<List<int>> slots = new(slotMarkers.Count);
        int requiredMarker = requiredClauseId >= 0 ? context.At(requiredClauseId).Head[0].Symbol : -1;
        for(int i = 0; i < slotMarkers.Count; i++)
        {
            List<int> slot = [];
            if(slotMarkers[i] == requiredMarker)
            {
                slot.Add(requiredClauseId);
            }
            else
            {
                CollectLiveMarkerClauses(context, slotMarkers[i], slot);
            }

            if(slot.Count == 0)
            {
                return;
            }

            slots.Add(slot);
        }

        Span<int> cursor = stackalloc int[slots.Count];
        cursor.Clear();
        while(!BudgetExhausted)
        {
            if(!TryApply())
            {
                return;
            }

            ScratchBody.Clear();
            AppendSpan(ScratchBody, parent.Body);
            ScratchPremiseIds.Clear();
            ScratchPremiseIds.Add(disjunctiveClauseId);
            for(int i = 0; i < slots.Count; i++)
            {
                ScratchPremiseIds.Add(slots[i][cursor[i]]);
                AppendSpan(ScratchBody, context.At(slots[i][cursor[i]]).Body);
            }

            SidecarSeedOffers++;
            ClauseOfferOutcome narrowingOutcome = AddClauseCore(context, DlClause.Create(ScratchBody, ScratchResidualHead, DerivedOrigin), CollectionsMarshal.AsSpan(ScratchPremiseIds));
            if(narrowingOutcome == ClauseOfferOutcome.Inserted)
            {
                DisjunctiveDataNarrowings++;
                RuleApplications++;
            }
            else if(narrowingOutcome == ClauseOfferOutcome.ExactDuplicate)
            {
                SidecarSeedDuplicateHits++;
            }
            else if(narrowingOutcome == ClauseOfferOutcome.Subsumed)
            {
                SidecarSeedSubsumedHits++;
            }

            int position = slots.Count - 1;
            while(position >= 0)
            {
                cursor[position]++;
                if(cursor[position] < slots[position].Count)
                {
                    break;
                }

                cursor[position] = 0;
                position--;
            }

            if(position < 0)
            {
                return;
            }
        }
    }

    /// <summary>Re-emits the narrowing combinations a newly-landed unit contributor completes (the disjunctive sibling of <see cref="EmitForLandedContributor"/>): for every refuted marker of the context whose narrowing core contains the landed marker, each live disjunctive clause carrying the refuted marker gets its combinations with the landed contributor's slot pinned — so a second derivation route to an already-known refutation still conditions its own narrowing. Runs on both memo paths of the unit rule.</summary>
    /// <param name="context">The context the contributor landed in.</param>
    /// <param name="landedClauseId">The landed unit demand-marker clause's id.</param>
    private void EmitDisjunctiveNarrowingsForLandedContributor(Context context, int landedClauseId)
    {
        if(!DisjunctiveConflictSets.TryGetValue(context.Id, out Dictionary<int, int[]>? refuted) || refuted.Count == 0)
        {
            return;
        }

        if(!LiveDisjunctiveMarkerClauses.TryGetValue(context.Id, out List<int>? disjunctiveIds))
        {
            return;
        }

        int landedMarker = context.At(landedClauseId).Head[0].Symbol;
        foreach(KeyValuePair<int, int[]> entry in refuted)
        {
            if(!ContainsMarker(entry.Value, landedMarker))
            {
                continue;
            }

            for(int i = 0; i < disjunctiveIds.Count && !BudgetExhausted; i++)
            {
                int clauseId = disjunctiveIds[i];
                if(context.IsLive(clauseId) && HeadCarriesMarker(context.At(clauseId).Head, entry.Key))
                {
                    EmitDisjunctiveDataNarrowing(context, clauseId, requiredClauseId: landedClauseId);
                }
            }
        }
    }

    /// <summary>Re-probes the context's live disjunctive marker clauses after the unit pool changed, through the per-context index — a new unit demand can turn a not-yet-refutable disjunct refutable. The per-clause memo keeps an unchanged-signature clause from re-running the oracle, so the sweep costs O(affected).</summary>
    /// <param name="context">The context whose pool changed.</param>
    private void ReprobeDisjunctiveClauses(Context context)
    {
        if(!LiveDisjunctiveMarkerClauses.TryGetValue(context.Id, out List<int>? disjunctiveIds))
        {
            return;
        }

        for(int i = 0; i < disjunctiveIds.Count && !BudgetExhausted; i++)
        {
            RunDisjunctiveDataObligations(context, disjunctiveIds[i]);
        }
    }

    /// <summary>
    /// The fixpoint certification of the disjunctive data lane — the lane's
    /// soundness backstop. Per context, the survivor joint obligation set (the
    /// unit-forced pool plus every central data marker on a live multi-literal
    /// head not recorded refuted) is decided ONCE through the sidecar:
    /// <c>Consistent</c> certifies the context — one concrete value assignment
    /// realizes every obligation the saturation can force there, so the
    /// all-survivors-open model choice is real; a clash or an undecided answer
    /// latches <see cref="HasUndecidedDataObligation"/> — a module consistent
    /// only under a per-disjunction choice delegates rather than being claimed,
    /// never a wrong verdict. A marker the incremental probing missed is still a
    /// survivor here, so a missed re-probe costs only completeness. The joint
    /// set is deliberately conjunctive: two individually-openable markers can be
    /// jointly unsatisfiable. Contexts without a surviving disjunctive marker
    /// are the unit rule's jurisdiction and are skipped. Runs once, only on a
    /// completed saturation — one joint decide per surviving context; the
    /// decides ride the budget-ticked oracle, and a budget stop hands the
    /// outcome to the budget latch.
    /// </summary>
    private void CertifyDisjunctiveData()
    {
        if(DataDemandDescriptors.Count == 0)
        {
            return;
        }

        for(int contextId = 0; contextId < Structure.Count && !BudgetExhausted; contextId++)
        {
            Context context = Structure[contextId];
            ScratchSurvivorMarkers.Clear();
            DisjunctiveConflictSets.TryGetValue(contextId, out Dictionary<int, int[]>? refuted);
            for(int clauseId = 0; clauseId < context.ClauseCount; clauseId++)
            {
                if(!context.IsLive(clauseId))
                {
                    continue;
                }

                ReadOnlySpan<DlLiteral> head = context.At(clauseId).Head;
                if(head.Length < 2)
                {
                    continue;
                }

                for(int i = 0; i < head.Length; i++)
                {
                    if(!IsDataDemandHead(head[i]))
                    {
                        continue;
                    }

                    int marker = head[i].Symbol;
                    if(refuted != null && refuted.ContainsKey(marker))
                    {
                        continue;
                    }

                    if(!ScratchSurvivorMarkers.Contains(marker))
                    {
                        ScratchSurvivorMarkers.Add(marker);
                    }
                }
            }

            if(ScratchSurvivorMarkers.Count == 0)
            {
                continue;
            }

            ScratchDisjunctivePool.Clear();
            context.CollectLiveDataDemands(ScratchDisjunctivePool);
            for(int i = 0; i < ScratchSurvivorMarkers.Count; i++)
            {
                if(!ScratchDisjunctivePool.Contains(ScratchSurvivorMarkers[i]))
                {
                    ScratchDisjunctivePool.Add(ScratchSurvivorMarkers[i]);
                }
            }

            ScratchDisjunctivePool.Sort();
            if(!HasValueForcingDemand(ScratchDisjunctivePool))
            {
                //Every joint obligation is a Universal: a node with no filler realizes
                //them all vacuously, so the context certifies without an oracle call —
                //the scale guard for a corpus whose duals seed every context.
                DisjunctiveDataCertifications++;

                continue;
            }

            if(!TryApply())
            {
                return;
            }

            ScratchDisjunctiveConcepts.Clear();
            for(int i = 0; i < ScratchDisjunctivePool.Count; i++)
            {
                ScratchDisjunctiveConcepts.Add(ReconstructDemand(DataDemandDescriptors[ScratchDisjunctivePool[i]]));
            }

            DataConsistencyStatus status = DataRestrictionConsistency.Decide(ScratchDisjunctiveConcepts, DataBox, OracleBudgetTick, Registry, out IReadOnlyList<AlcConcept> _, out bool selfCertified);
            HasSelfCertifiedDataDecision |= selfCertified;
            if(BudgetExhausted)
            {
                return;
            }

            if(status == DataConsistencyStatus.Consistent)
            {
                DisjunctiveDataCertifications++;
            }
            else
            {
                HasUndecidedDataObligation = true;
                UncertifiedDisjunctiveDataLatches++;
                UndecidedDataObligationCount++;
                for(int i = 0; i < ScratchDisjunctivePool.Count; i++)
                {
                    RecordUndecidedDisjunctiveDemandProperty(ScratchDisjunctivePool[i]);
                }
            }
        }
    }

    /// <summary>Latches <see cref="HasUndecidedKeyObligation"/> for any named key-class atom still riding a MULTI-literal live head of a GROUND context at the completed fixpoint (the key-side sibling of <see cref="CertifyDisjunctiveData"/>): a disjunctive membership can force a clashing merge on one branch, so the key join can neither fire (unsound) nor certify completeness (a wrong CONSISTENT) — the verdict must delegate. Runs only on a completed saturation and only over the ground contexts, whose representatives are the only join participants.</summary>
    private void LatchUndecidedKeyObligations()
    {
        if(KeyClassAtoms.Count == 0)
        {
            return;
        }

        foreach((Utf8String _, int contextId) in GroundContexts)
        {
            Context context = Structure[contextId];
            for(int clauseId = 0; clauseId < context.ClauseCount; clauseId++)
            {
                if(!context.IsLive(clauseId))
                {
                    continue;
                }

                ReadOnlySpan<DlLiteral> head = context.At(clauseId).Head;
                if(head.Length < 2)
                {
                    continue;
                }

                for(int i = 0; i < head.Length; i++)
                {
                    if(head[i].Kind == DlLiteralKind.Concept && head[i].First.IsCentral && KeyClassAtoms.Contains(head[i].Symbol))
                    {
                        HasUndecidedKeyObligation = true;

                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Latches <see cref="HasUndecidedRootKeyObligation"/> for a named key-class atom
    /// still riding a MULTI-literal live head at a ROOT-class constant at the completed
    /// fixpoint (P-GC1) — the root-tier
    /// sibling of <see cref="LatchUndecidedKeyObligations"/>. It is the ground scan's
    /// central-variable pattern EXTENDED five ways for the root shapes: an
    /// <c>IsIndividual</c> arm beside the central one (a single root's memberships are
    /// all constant-spelled <c>B(o)</c>, which the ground pattern alone misses); the
    /// home-slot resolution of a <c>v_o</c> central spelling through
    /// <see cref="Context.HomeIndividual"/>; a deliberate <c>f(o)</c> exclusion (a
    /// function-bearing term never resolves to an individual, so a constant inside one
    /// is no candidate spelling); the ≈-resolution of the scanned constant to its class
    /// representative (the latch is stated over the ≈-class, conservative over every
    /// spelling it carries); and a sweep over ALL root-class context ids, not one
    /// context. Runs only on a completed saturation of an armed engine; a disjunctive
    /// membership that has narrowed to a single certain head by the fixpoint carries no
    /// multi-literal live head and never latches.
    /// </summary>
    private void LatchUndecidedRootKeyObligations()
    {
        if(!RootKeyJoinArmed || KeyClassAtoms.Count == 0)
        {
            return;
        }

        IReadOnlyList<int> roots = Structure.RootClassContextIds;
        for(int r = 0; r < roots.Count; r++)
        {
            Context context = Structure[roots[r]];
            for(int clauseId = 0; clauseId < context.ClauseCount; clauseId++)
            {
                if(!context.IsLive(clauseId))
                {
                    continue;
                }

                ReadOnlySpan<DlLiteral> head = context.At(clauseId).Head;
                if(head.Length < 2)
                {
                    continue;
                }

                for(int i = 0; i < head.Length; i++)
                {
                    if(head[i].Kind == DlLiteralKind.Concept
                        && KeyClassAtoms.Contains(head[i].Symbol)
                        && RootTermResolution.TryResolveIndividual(head[i].First, context.HomeIndividual, out int scanned))
                    {
                        HasUndecidedRootKeyObligation = true;
                        UndecidedRootKeyIndividual = RootApproxRepresentative(scanned);

                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Latches <see cref="HasRootEqualityOutsideFold"/> for a live root-class ground
    /// equality between two key candidates (or demand-bearing constants) whose sides the
    /// ≈-class surface did NOT merge at the completed fixpoint (the backstop). The
    /// surface merges every unconditional single-literal equality that lands on a root
    /// context, so an equality whose sides stay unmerged reached the root off the fold —
    /// a relayed <c>A → A</c> tautology, a carried equality disjunct — and the read-time
    /// union cannot see the identity it names. The backstop checks the invariant directly,
    /// regardless of derivation channel: it scans every equality literal on every live
    /// root-class head, skips the merged sides the fold already covers, latches the
    /// boolean and counts the head into <see cref="RootEqualityOutsideFoldHeads"/> once
    /// per offending head when an unmerged side is a key candidate or a demand-bearing
    /// constant. The count is reset at each scan start, so across the multi-round root key
    /// join's repeated saturations it reports the off-fold head census of the latest
    /// completed fixpoint rather than accumulating over rounds. The scan runs to completion
    /// — the count is the corpus census the boolean latch alone cannot carry — and the
    /// boolean is set identically to a first-hit stop,
    /// so the delegation the reasoner reads is unchanged. Runs only on a completed
    /// saturation of an armed engine.
    /// </summary>
    private void LatchRootEqualityOutsideFold()
    {
        if(!RootKeyJoinArmed)
        {
            return;
        }

        RootEqualityOutsideFoldHeads = 0;
        IReadOnlyList<int> roots = Structure.RootClassContextIds;
        for(int r = 0; r < roots.Count; r++)
        {
            Context context = Structure[roots[r]];
            for(int clauseId = 0; clauseId < context.ClauseCount; clauseId++)
            {
                if(!context.IsLive(clauseId))
                {
                    continue;
                }

                ReadOnlySpan<DlLiteral> head = context.At(clauseId).Head;
                for(int i = 0; i < head.Length; i++)
                {
                    if(head[i].Kind == DlLiteralKind.Equality
                        && RootTermResolution.TryResolveIndividual(head[i].First, context.HomeIndividual, out int first)
                        && RootTermResolution.TryResolveIndividual(head[i].Second, context.HomeIndividual, out int second)
                        && !RootApproxSameClass(first, second)
                        && (IsRootKeyBackstopConstant(first) || IsRootKeyBackstopConstant(second)))
                    {
                        HasRootEqualityOutsideFold = true;
                        RootEqualityOutsideFoldHeads++;

                        break;
                    }
                }
            }
        }
    }

    /// <summary>Arms the general relay latch inline at a guard-site refusal of a <c>DerivedUnderChoice</c> root equality — sets <see cref="HasRootEqualityRidesAChoice"/> and counts the refusal into <see cref="RootEqualityRidesAChoiceHeads"/>. The boolean is monotone and sticky: it is armed during the saturation while-loop and read after <c>Saturate()</c> returns, so it is never reset, unlike the key-join backstop's count-zeroing from-scratch rescan.</summary>
    private void ArmRootEqualityRidesAChoice()
    {
        HasRootEqualityRidesAChoice = true;
        RootEqualityRidesAChoiceHeads++;
    }

    /// <summary>The conditionality-loss lint over an in-grammar derived step: fires when the conclusion's head is strictly narrower in choice-conditions (fewer head disjuncts) than the union of its same-context premises' head disjuncts, with no recorded split. A single-literal head derived from a two-literal premise is the strict narrowing the lint counts, and two single-disjunct premises carrying distinct disjuncts combined into a single-disjunct conclusion is the multi-premise narrowing the union comparison detects that a per-premise widest would miss; an empty head (a complementary resolution / refutation) is excluded so the lint never fires on every inconsistency proof; a premise-free told clause has an empty premise union so it never fires. Head disjuncts are unioned by literal identity — a disjunct shared across premises counts once — at the first-cut choice-condition granularity. Scoped to the visible <c>premiseIds</c> DAG, which is sound for a mechanism detector (unlike the origin-bit guard, this is not a soundness gate).</summary>
    /// <param name="context">The context holding the premises and the conclusion.</param>
    /// <param name="clause">The normalized conclusion clause.</param>
    /// <param name="premiseIds">The same-context premise clause ids the conclusion derives from.</param>
    private void LintConditionalityLoss(Context context, DlClause clause, ReadOnlySpan<int> premiseIds)
    {
        if(clause.Head.Length < 1)
        {
            return;
        }

        int premiseUnionHead = 0;
        for(int i = 0; i < premiseIds.Length; i++)
        {
            ReadOnlySpan<DlLiteral> head = context.At(premiseIds[i]).Head;
            for(int k = 0; k < head.Length; k++)
            {
                if(!PremiseDisjunctSeenEarlier(context, premiseIds, i, head[k]))
                {
                    premiseUnionHead++;
                }
            }
        }

        if(clause.Head.Length < premiseUnionHead)
        {
            ArmConditionalityDropped();
        }
    }

    /// <summary>Whether a premise head disjunct already appears in the head of an earlier premise in the derivation's premise list — the literal-identity de-duplication behind the conditionality-loss lint's union count, so a disjunct shared across premises is counted once rather than summed. Each premise head is itself canonical (sorted, de-duplicated), so only earlier premises are scanned.</summary>
    /// <param name="context">The context holding the premises.</param>
    /// <param name="premiseIds">The same-context premise clause ids of the derivation.</param>
    /// <param name="upTo">The exclusive upper bound on premise indices to scan — the position of the premise the literal is drawn from.</param>
    /// <param name="literal">The premise head disjunct being counted.</param>
    /// <returns><see langword="true"/> when the disjunct already appeared in an earlier premise's head.</returns>
    private static bool PremiseDisjunctSeenEarlier(Context context, ReadOnlySpan<int> premiseIds, int upTo, DlLiteral literal)
    {
        for(int j = 0; j < upTo; j++)
        {
            ReadOnlySpan<DlLiteral> head = context.At(premiseIds[j]).Head;
            for(int k = 0; k < head.Length; k++)
            {
                if(head[k].Equals(literal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Arms the conditionality-loss lint latch at an observed strict head-disjunct narrowing — sets <see cref="HasConditionalityDropped"/> and counts the step into <see cref="ConditionalityDroppedCount"/>. Monotone and sticky, mirroring <see cref="ArmRootEqualityRidesAChoice"/>: armed inline at the lint site during saturation and read after <c>Saturate()</c> returns, never reset.</summary>
    private void ArmConditionalityDropped()
    {
        HasConditionalityDropped = true;
        ConditionalityDroppedCount++;
    }

    /// <summary>Whether an individual is a backstop-relevant constant: a key-join candidate (IRI-denoted, depth zero) or a demand-bearing constant carrying a live root data-demand marker.</summary>
    /// <param name="individual">The individual id.</param>
    /// <returns><see langword="true"/> when an unmerged equality on the individual is worth the backstop delegation.</returns>
    private bool IsRootKeyBackstopConstant(int individual)
    {
        if(Symbols.IsKeyJoinCandidateOrigin(individual))
        {
            return true;
        }

        foreach(int marker in DataDemandDescriptors.Keys)
        {
            if(ClassHasLiveRootDataDemand(individual, marker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The reusable candidate-membership buffer of the vr key join's pooled ≈-class read.</summary>
    private List<int> ScratchRootJoinConcepts { get; } = [];

    /// <summary>The reusable first-side role-edge buffer of the vr key join's object-value agreement.</summary>
    private List<RootRoleEdge> ScratchRootJoinEdgesFirst { get; } = [];

    /// <summary>The reusable second-side role-edge buffer of the vr key join's object-value agreement.</summary>
    private List<RootRoleEdge> ScratchRootJoinEdgesSecond { get; } = [];

    /// <summary>The reusable ≈-class-spelling buffer of the vr key join's pooled data-value read.</summary>
    private List<int> ScratchRootJoinSpellings { get; } = [];

    /// <summary>The reusable first-side value buffer of the vr key join's data-value agreement.</summary>
    private List<Literal> ScratchRootJoinValuesFirst { get; } = [];

    /// <summary>The reusable second-side value buffer of the vr key join's data-value agreement.</summary>
    private List<Literal> ScratchRootJoinValuesSecond { get; } = [];

    /// <summary>
    /// The post-saturation vr key join —
    /// the ground tier's two-part shape translated onto the root tier. Per descriptor,
    /// the candidates are the IRI-denoted depth-zero individuals (the candidacy read)
    /// whose keyed-class membership is derived-certain — <c>B(o)</c> read
    /// off the per-constant index pooled across the individual's ≈-class (told memberships
    /// included: a root fact fires Hyper). Two candidates AGREE when they share an
    /// ≈-class of targets on every object key role (both sides ≈-resolved) and a
    /// value-space-equal literal on every data key property (representatives resolved
    /// through the ≈-surface at read time); an <c>Indeterminate</c> value comparison
    /// abstains and the module delegates named. A fired pair emits <c>⊤ → o ≈ o′</c> as a
    /// root-fact continuation into the engine and the join CONTINUES saturating (option
    /// (a)): the ≈-class feed absorbs the merge, the Ineq rule catches a collision
    /// with a told <c>o ≉ o′</c>, and the join re-runs at the new fixpoint until no pair
    /// fires. Each pass collects the round's pairs against the pre-merge ≈-state, so a
    /// merge that a later pass only reveals fires in its own round (the fixpoint witness);
    /// unions strictly shrink the distinct-class count, so the loop is bounded. Each pass
    /// and each re-saturation charges the standard inference budget.
    /// </summary>
    /// <param name="budget">The inference budget bounding each re-saturation pass.</param>
    /// <param name="cancellationToken">A token that aborts the join.</param>
    /// <param name="candidatesEnumerated">The candidate representatives enumerated across descriptors and passes.</param>
    /// <param name="firedUnions">The union continuations fired.</param>
    /// <returns>The join outcome.</returns>
    internal RootKeyJoinOutcome RunPostSaturationRootKeyJoin(ReasoningBudget budget, CancellationToken cancellationToken, out int candidatesEnumerated, out int firedUnions)
    {
        candidatesEnumerated = 0;
        firedUnions = 0;
        List<int> passCandidates = [];
        HashSet<int> seenRepresentatives = [];
        List<(int First, int Second)> passFired = [];
        while(true)
        {
            passFired.Clear();
            foreach(GroundKeyDescriptor descriptor in KeyDescriptors)
            {
                passCandidates.Clear();
                seenRepresentatives.Clear();
                for(int id = 0; id < Symbols.IndividualCount; id++)
                {
                    if(!Symbols.IsKeyJoinCandidateOrigin(id) || !seenRepresentatives.Add(RootApproxRepresentative(id)))
                    {
                        continue;
                    }

                    if(!descriptor.ClassIsThing)
                    {
                        ScratchRootJoinConcepts.Clear();
                        AppendPooledRootConcepts(id, ScratchRootJoinConcepts);
                        if(!ScratchRootJoinConcepts.Contains(descriptor.ClassAtom))
                        {
                            continue;
                        }
                    }

                    passCandidates.Add(id);
                }

                candidatesEnumerated += passCandidates.Count;
                for(int i = 0; i < passCandidates.Count; i++)
                {
                    for(int j = i + 1; j < passCandidates.Count; j++)
                    {
                        int first = passCandidates[i];
                        int second = passCandidates[j];
                        if(RootApproxSameClass(first, second))
                        {
                            continue;
                        }

                        bool shared = true;
                        foreach(RoleRepresentative role in descriptor.RootObjectRoles)
                        {
                            if(!SharesRootObjectValue(first, second, role))
                            {
                                shared = false;

                                break;
                            }
                        }

                        if(!shared)
                        {
                            continue;
                        }

                        foreach(Utf8String property in descriptor.DataProperties)
                        {
                            DatatypeValueIdentity identity = SharesRootDataValue(first, second, property);
                            if(identity == DatatypeValueIdentity.Indeterminate)
                            {
                                RootKeyIndeterminateProperty = property;

                                return RootKeyJoinOutcome.Indeterminate;
                            }

                            if(identity == DatatypeValueIdentity.Distinct)
                            {
                                shared = false;

                                break;
                            }
                        }

                        if(shared)
                        {
                            passFired.Add((first, second));
                        }
                    }
                }
            }

            if(passFired.Count == 0)
            {
                return RootKeyJoinOutcome.Clean;
            }

            foreach((int first, int second) in passFired)
            {
                if(RootApproxSameClass(first, second))
                {
                    continue;
                }

                Context root = GetOrCreateRootFor(first);
                ApplyCore(root, DlClause.Create([], [DlLiteral.Equality(DlTerm.Individual(first), DlTerm.Individual(second))], DerivedOrigin), []);
                firedUnions++;
            }

            if(Saturate(budget, cancellationToken) == SaturationOutcome.BudgetExhausted)
            {
                return RootKeyJoinOutcome.BudgetExhausted;
            }

            if(IsInconsistent)
            {
                return RootKeyJoinOutcome.Clean;
            }
        }
    }

    /// <summary>Whether two candidates share an ≈-class of targets over an object key role: the pooled outgoing role edges of each, restricted to the role, with both targets resolved through the ≈-class surface. The index projects forward-representative symbols, and every consumed representative is forward-direction because an inverse-direction key representative delegates the module at clausification, so the symbol match is exact.</summary>
    /// <param name="first">The first candidate id.</param>
    /// <param name="second">The second candidate id.</param>
    /// <param name="role">The object key property's representative directioned role.</param>
    /// <returns><see langword="true"/> when a shared ≈-class target exists.</returns>
    private bool SharesRootObjectValue(int first, int second, RoleRepresentative role)
    {
        ScratchRootJoinEdgesFirst.Clear();
        AppendPooledRootRoleTargets(first, ScratchRootJoinEdgesFirst);
        ScratchRootJoinEdgesSecond.Clear();
        AppendPooledRootRoleTargets(second, ScratchRootJoinEdgesSecond);
        foreach(RootRoleEdge edgeFirst in ScratchRootJoinEdgesFirst)
        {
            if(edgeFirst.RoleSymbol != role.Value)
            {
                continue;
            }

            foreach(RootRoleEdge edgeSecond in ScratchRootJoinEdgesSecond)
            {
                if(edgeSecond.RoleSymbol == role.Value && RootApproxSameClass(edgeFirst.Target, edgeSecond.Target))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The three-valued shared-value judgement of two candidates over a data key property: <c>Same</c> on some value-space-equal pair, <c>Indeterminate</c> when no pair is equal but a comparison abstains, <c>Distinct</c> otherwise — including a missing value list, which never fires the key. Values pool across each candidate's ≈-class through the read-time union.</summary>
    /// <param name="first">The first candidate id.</param>
    /// <param name="second">The second candidate id.</param>
    /// <param name="property">The data key property's IRI.</param>
    /// <returns>The shared-value judgement.</returns>
    private DatatypeValueIdentity SharesRootDataValue(int first, int second, Utf8String property)
    {
        ScratchRootJoinValuesFirst.Clear();
        CollectPooledRootDataValues(first, property, ScratchRootJoinValuesFirst);
        if(ScratchRootJoinValuesFirst.Count == 0)
        {
            return DatatypeValueIdentity.Distinct;
        }

        ScratchRootJoinValuesSecond.Clear();
        CollectPooledRootDataValues(second, property, ScratchRootJoinValuesSecond);
        if(ScratchRootJoinValuesSecond.Count == 0)
        {
            return DatatypeValueIdentity.Distinct;
        }

        bool indeterminate = false;
        foreach(Literal firstValue in ScratchRootJoinValuesFirst)
        {
            foreach(Literal secondValue in ScratchRootJoinValuesSecond)
            {
                DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(firstValue, secondValue, Registry);
                if(identity == DatatypeValueIdentity.Same)
                {
                    return DatatypeValueIdentity.Same;
                }

                indeterminate |= identity == DatatypeValueIdentity.Indeterminate;
            }
        }

        return indeterminate ? DatatypeValueIdentity.Indeterminate : DatatypeValueIdentity.Distinct;
    }

    /// <summary>Appends a candidate's told data-key values for a property, pooled across its whole ≈-class through the read-time union over the ≈-class surface: every spelling's stored key-value bucket the class carries; nothing when no spelling carries the property.</summary>
    /// <param name="individual">The candidate id.</param>
    /// <param name="property">The data key property's IRI.</param>
    /// <param name="valuesToAppendTo">The buffer the pooled literal values are appended to.</param>
    private void CollectPooledRootDataValues(int individual, Utf8String property, List<Literal> valuesToAppendTo)
    {
        ScratchRootJoinSpellings.Clear();
        AppendRootApproxClassMembers(individual, ScratchRootJoinSpellings);
        for(int i = 0; i < ScratchRootJoinSpellings.Count; i++)
        {
            if(Symbols.TryIndividualKey(ScratchRootJoinSpellings[i], out Utf8String key)
                && KeyValueStore.TryGetValue(key, out Dictionary<Utf8String, List<Literal>>? properties)
                && properties.TryGetValue(property, out List<Literal>? values))
            {
                valuesToAppendTo.AddRange(values);
            }
        }
    }

    /// <summary>Records the demand property IRI of a disjunctive-lane marker whose obligation stayed undecided — an undecided probe or an uncertified joint set — into <see cref="UndecidedDataObligationPropertiesList"/>, deduplicated.</summary>
    /// <param name="marker">The demand-marker atom.</param>
    private void RecordUndecidedDisjunctiveDemandProperty(int marker)
    {
        string property = DataDemandDescriptors[marker].Property.ToString();
        if(!UndecidedDataObligationPropertiesList.Contains(property))
        {
            UndecidedDataObligationPropertiesList.Add(property);
        }
    }

    /// <summary>Records the demand property IRIs of the current live-demand set (<see cref="ScratchLiveDemands"/>) into <see cref="UndecidedDataObligationPropertiesList"/>, deduplicated — the diagnostic side effect of an undecided data-obligation decision.</summary>
    private void RecordUndecidedDataObligationProperties()
    {
        for(int i = 0; i < ScratchLiveDemands.Count; i++)
        {
            string property = DataDemandDescriptors[ScratchLiveDemands[i]].Property.ToString();
            if(!UndecidedDataObligationPropertiesList.Contains(property))
            {
                UndecidedDataObligationPropertiesList.Add(property);
            }
        }
    }

    /// <summary>The sidecar oracle's budget gate: each checker invocation consumes one rule application from the shared ceiling, so an oracle-heavy module trips the inference budget rather than only the harness wall clock.</summary>
    /// <returns><see langword="true"/> when the invocation may proceed.</returns>
    private bool OracleBudgetTick()
    {
        if(!TryApply())
        {
            return false;
        }

        RuleApplications++;

        return true;
    }

    /// <summary>Maps the sidecar's conflict-core concepts back to their demand marker atoms; a concept without a marker (never minted for this module) yields a shorter array, which the caller treats as a non-emittable core.</summary>
    /// <param name="conflict">The sidecar's conflict-core obligations.</param>
    /// <returns>The marker atoms of the mapped obligations.</returns>
    private int[] ConflictMarkersOf(IReadOnlyList<AlcConcept> conflict)
    {
        List<int> markers = new(conflict.Count);
        for(int i = 0; i < conflict.Count; i++)
        {
            if(ScratchConceptToMarker.TryGetValue(conflict[i], out int marker))
            {
                markers.Add(marker);
            }
        }

        return [.. markers];
    }

    /// <summary>Records a proven-unsatisfiable conflict marker set for a context, deduplicated, so later contributor landings can re-emit without re-deciding.</summary>
    /// <param name="contextId">The context id.</param>
    /// <param name="conflictMarkers">The conflict marker atoms.</param>
    private void RememberConflictSet(int contextId, int[] conflictMarkers)
    {
        if(!DataConflictSets.TryGetValue(contextId, out List<int[]>? sets))
        {
            sets = [];
            DataConflictSets[contextId] = sets;
        }

        for(int i = 0; i < sets.Count; i++)
        {
            if(SameMarkerSet(sets[i], conflictMarkers))
            {
                return;
            }
        }

        sets.Add(conflictMarkers);
    }

    /// <summary>Whether two sorted-insensitive marker arrays hold the same set; conflict cores are small, so the quadratic containment test stays cheap.</summary>
    /// <param name="first">The first marker array.</param>
    /// <param name="second">The second marker array.</param>
    /// <returns><see langword="true"/> when both hold the same markers.</returns>
    private static bool SameMarkerSet(int[] first, int[] second)
    {
        if(first.Length != second.Length)
        {
            return false;
        }

        for(int i = 0; i < first.Length; i++)
        {
            bool found = false;
            for(int j = 0; j < second.Length; j++)
            {
                if(second[j] == first[i])
                {
                    found = true;

                    break;
                }
            }

            if(!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Emits the clash combinations a newly-landed contributor clause completes: when the landed clause's marker belongs to a proven conflict set of its context, every combination pinning that slot to the landed clause is injected — the semi-naive discipline applied to data clashes, so a later-derived route to a known clash still closes.</summary>
    /// <param name="context">The context the clause landed in.</param>
    /// <param name="landedClauseId">The landed demand-marker clause's id.</param>
    private void EmitForLandedContributor(Context context, int landedClauseId)
    {
        if(!DataConflictSets.TryGetValue(context.Id, out List<int[]>? sets))
        {
            return;
        }

        int landedMarker = context.At(landedClauseId).Head[0].Symbol;
        for(int i = 0; i < sets.Count && !BudgetExhausted; i++)
        {
            int[] set = sets[i];
            for(int j = 0; j < set.Length; j++)
            {
                if(set[j] == landedMarker)
                {
                    EmitDataClashCombinations(context, set, requiredClauseId: landedClauseId);

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Injects one <c>⋃Body → Bottom(x)</c> clause per combination of live contributor
    /// clauses over a proven conflict marker set (modelled on the Pred rule's
    /// slot/cursor cross-product): each conflict marker is a slot filled by every live
    /// clause heading it, so incomparable derivation routes to the same clash each get
    /// their own conditional closure. A pinned <paramref name="requiredClauseId"/>
    /// restricts its marker's slot to the landed clause (the incremental re-emission);
    /// a vanished contributor (no live clause for a marker) aborts the whole emission
    /// rather than inject an unsoundly-weak body. Each combination passes the
    /// <see cref="TryApply"/> budget gate.
    /// </summary>
    /// <param name="context">The context the clash holds in.</param>
    /// <param name="conflictMarkers">The proven-unsatisfiable demand marker set.</param>
    /// <param name="requiredClauseId">A clause id the combinations must include, or -1 for the full cross-product.</param>
    private void EmitDataClashCombinations(Context context, int[] conflictMarkers, int requiredClauseId)
    {
        List<List<int>> slots = new(conflictMarkers.Length);
        int requiredMarker = requiredClauseId >= 0 ? context.At(requiredClauseId).Head[0].Symbol : -1;
        for(int i = 0; i < conflictMarkers.Length; i++)
        {
            List<int> slot = [];
            if(conflictMarkers[i] == requiredMarker)
            {
                slot.Add(requiredClauseId);
            }
            else
            {
                CollectLiveMarkerClauses(context, conflictMarkers[i], slot);
            }

            if(slot.Count == 0)
            {
                return;
            }

            slots.Add(slot);
        }

        Span<int> cursor = stackalloc int[slots.Count];
        cursor.Clear();
        while(!BudgetExhausted)
        {
            if(!TryApply())
            {
                return;
            }

            ScratchBody.Clear();
            ScratchPremiseIds.Clear();
            for(int i = 0; i < slots.Count; i++)
            {
                ScratchPremiseIds.Add(slots[i][cursor[i]]);
                AppendSpan(ScratchBody, context.At(slots[i][cursor[i]]).Body);
            }

            ScratchHead.Clear();
            ScratchHead.Add(DlLiteral.Concept(ContextSymbolTable.Bottom, DlTerm.Central));
            SidecarSeedOffers++;
            ClauseOfferOutcome clashOutcome = AddClauseCore(context, DlClause.Create(ScratchBody, ScratchHead, DerivedOrigin), CollectionsMarshal.AsSpan(ScratchPremiseIds));
            if(clashOutcome == ClauseOfferOutcome.Inserted)
            {
                DataClashApplications++;
                RuleApplications++;
            }
            else if(clashOutcome == ClauseOfferOutcome.ExactDuplicate)
            {
                SidecarSeedDuplicateHits++;
            }
            else if(clashOutcome == ClauseOfferOutcome.Subsumed)
            {
                SidecarSeedSubsumedHits++;
            }

            int position = slots.Count - 1;
            while(position >= 0)
            {
                cursor[position]++;
                if(cursor[position] < slots[position].Count)
                {
                    break;
                }

                cursor[position] = 0;
                position--;
            }

            if(position < 0)
            {
                return;
            }
        }
    }

    /// <summary>Collects every live clause whose SINGLE head literal is the demand marker — a marker inside a disjunctive head is not a decided demand and cannot serve as a clash contributor, so multi-literal heads are filtered out even when the marker is their selected literal.</summary>
    /// <param name="context">The context.</param>
    /// <param name="marker">The demand marker concept atom.</param>
    /// <param name="clausesToAppendTo">The live clause ids, appended to.</param>
    private static void CollectLiveMarkerClauses(Context context, int marker, List<int> clausesToAppendTo)
    {
        IReadOnlyList<int> heads = context.SelectedHeadClauses(DlLiteral.Concept(marker, DlTerm.Central));
        for(int i = 0; i < heads.Count; i++)
        {
            if(context.IsLive(heads[i]) && context.At(heads[i]).Head.Length == 1)
            {
                clausesToAppendTo.Add(heads[i]);
            }
        }
    }

    /// <summary>Reconstructs a demand descriptor as the sidecar's <c>AlcConcept</c> obligation — byte-identical to the tableau arms' translation, so a demand decided through the context arm agrees with the same demand decided through a tableau arm. Every kind is named explicitly: an unnamed kind throws rather than aliasing onto another kind's obligation, which the sidecar would bucket as a different constraint entirely.</summary>
    /// <param name="descriptor">The demand descriptor.</param>
    /// <returns>The reconstructed obligation.</returns>
    /// <exception cref="ArgumentException">The descriptor's kind is not a reconstructible demand kind.</exception>
    private static AlcConcept ReconstructDemand(DataDemandDescriptor descriptor)
    {
        return descriptor.Kind switch
        {
            DataDemandKind.Existential => new AlcDataSome(descriptor.Property, descriptor.Range),
            DataDemandKind.Universal => new AlcDataAll(descriptor.Property, descriptor.Range),
            DataDemandKind.MinCardinality => new AlcDataMinCard(descriptor.Count, descriptor.Property, descriptor.Range),
            DataDemandKind.MaxCardinality => new AlcDataMaxCard(descriptor.Count, descriptor.Property, descriptor.Range),
            _ => throw new ArgumentException("The demand descriptor's kind is not a reconstructible demand kind.", nameof(descriptor)),
        };
    }

    /// <summary>Whether a context's sorted live-demand signature equals the one last decided for it — the memo hit that skips re-deciding an unchanged demand set.</summary>
    /// <param name="contextId">The context id.</param>
    /// <param name="current">The current sorted live-demand markers.</param>
    /// <returns><see langword="true"/> when the signature is unchanged since the last decision.</returns>
    private bool SignatureUnchanged(int contextId, List<int> current)
    {
        if(!LastDataSignature.TryGetValue(contextId, out int[]? last) || last.Length != current.Count)
        {
            return false;
        }

        for(int i = 0; i < current.Count; i++)
        {
            if(last[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Records a context's sorted live-demand signature as the one last decided.</summary>
    /// <param name="contextId">The context id.</param>
    /// <param name="current">The current sorted live-demand markers.</param>
    private void RecordSignature(int contextId, List<int> current)
    {
        LastDataSignature[contextId] = [.. current];
    }

    /// <summary>Runs the Hyper rule with the landed clause as the resolved premise: for every ontology body position the clause's SELECTED head literal instantiates — in an ordinary context under <c>σ(x)=x</c>, <c>σ(zᵢ) ∈ {y, f(x), o}</c>; in the root context under <c>σ(x) ∈ Σo</c> (one join per anchoring constant, read off the given literal's individual slots), <c>σ(zᵢ) ∈ {y, f(o), o′}</c> — and completes the remaining positions from the context's indexes (the semi-naive discipline — the landed clause fills at least one position). The given clause's remaining head literals ride into every conclusion as residual disjuncts.</summary>
    /// <param name="context">The context.</param>
    /// <param name="given">The landed clause whose selected head literal drives the joins.</param>
    /// <param name="givenClauseId">The landed clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The index of the given clause's selected literal within its head span.</param>
    private void HyperFromGiven(Context context, DlClause given, int givenClauseId, int givenSelectedIndex)
    {
        DlLiteral head = given.Head[givenSelectedIndex];
        if(!head.IsAtom)
        {
            //An ontology clause body carries only concept and role atoms (KR 2016
            //Section 2), so an equality or inequality head instantiates no ontology
            //body position; the merge information it carries reaches Hyper only
            //through the Eq-rewritten atoms the Eq rule derives.
            return;
        }

        //The constant-anchored odometer is the SingleRoot arm: a nominal-root
        //context's entry translation respells own-constant atoms central, so
        //ordinary central-anchored indexing serves them (the thesis's stated
        //gain) and foreign-constant ground atoms resolve via the inter-nominal
        //carrier's images, never via a per-individual odometer.
        bool constantAnchored = UsesConstantAnchoredRoot(context);
        if(head.Kind == DlLiteralKind.Concept)
        {
            DlTerm conceptAnchor = constantAnchored ? head.First : DlTerm.Central;
            bool conceptMatches = constantAnchored ? head.First.IsIndividual : head.First.IsCentral;
            if(conceptMatches && OntologyConceptBody.TryGetValue(head.Symbol, out List<(int ClauseIndex, int Position)>? conceptSites))
            {
                for(int i = 0; i < conceptSites.Count && !BudgetExhausted; i++)
                {
                    (int clauseIndex, int position) = conceptSites[i];
                    HyperJoin(context, given, givenClauseId, givenSelectedIndex, clauseIndex, position, conceptAnchor, givenNeighbourIndex: -1, givenBinding: default);
                }
            }

            return;
        }

        if(constantAnchored)
        {
            //Each individual slot of the given role literal anchors one sigma(x) = o
            //join family: the other slot is the neighbour binding.
            if(head.First.IsIndividual)
            {
                HyperFromGivenRole(context, given, givenClauseId, givenSelectedIndex, head, head.First, centralFirst: true, head.Second);
            }

            if(!BudgetExhausted && head.Second.IsIndividual)
            {
                HyperFromGivenRole(context, given, givenClauseId, givenSelectedIndex, head, head.Second, centralFirst: false, head.First);
            }

            return;
        }

        if(head.First.IsCentral || head.Second.IsCentral)
        {
            bool centralFirst = head.First.IsCentral;
            HyperFromGivenRole(context, given, givenClauseId, givenSelectedIndex, head, DlTerm.Central, centralFirst, centralFirst ? head.Second : head.First);
        }
    }

    /// <summary>Dispatches the Hyper role-premise joins for one anchoring of the given role literal: the anchor stands in the ontology body atom's <c>x</c> slot and the other argument binds its neighbour variable.</summary>
    /// <param name="context">The context.</param>
    /// <param name="given">The landed clause.</param>
    /// <param name="givenClauseId">The landed clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The index of the given clause's selected literal within its head span.</param>
    /// <param name="head">The given clause's selected role literal.</param>
    /// <param name="anchor">The term standing in the <c>x</c> slot (<c>x</c> itself, or the anchoring constant on the root context).</param>
    /// <param name="centralFirst">Whether the anchor occupies the literal's first argument.</param>
    /// <param name="binding">The neighbour binding read off the other argument.</param>
    private void HyperFromGivenRole(Context context, DlClause given, int givenClauseId, int givenSelectedIndex, DlLiteral head, DlTerm anchor, bool centralFirst, DlTerm binding)
    {
        if(!OntologyRoleBody.TryGetValue((head.Symbol, centralFirst), out List<(int ClauseIndex, int Position)>? roleSites))
        {
            return;
        }

        for(int i = 0; i < roleSites.Count && !BudgetExhausted; i++)
        {
            (int clauseIndex, int position) = roleSites[i];
            DlLiteral bodyAtom = NonEmptyOntologyClauses[clauseIndex].Body[position];
            int neighbour = bodyAtom.First.IsCentral ? bodyAtom.Second.Index : bodyAtom.First.Index;
            HyperJoin(context, given, givenClauseId, givenSelectedIndex, clauseIndex, position, anchor, neighbour, binding);
        }
    }

    /// <summary>
    /// Completes one Hyper join: the given clause is pinned at
    /// <paramref name="givenPosition"/>; every other body position becomes an
    /// odometer slot — a concept atom looks up its exact selected head, a role
    /// atom sharing the given neighbour looks up the bound image, and a role
    /// atom with a free neighbour enumerates the shape index. Each combination
    /// is filtered for consistent neighbour bindings (the same <c>zᵢ</c> must
    /// bind identically across atoms; distinct <c>zᵢ</c> bind independently),
    /// then the conclusion — the union of the premise bodies, every premise's
    /// residual (non-selected) head disjuncts, and the FULL substituted ontology
    /// head (Table 2's <c>∆σ ∨ ⋁∆ᵢ</c>) — is added.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="given">The given clause.</param>
    /// <param name="givenClauseId">The given clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The index of the given clause's selected literal within its head span.</param>
    /// <param name="clauseIndex">The ontology clause index into <see cref="NonEmptyOntologyClauses"/>.</param>
    /// <param name="givenPosition">The ontology body position the given clause resolves.</param>
    /// <param name="anchor">The image of the central variable: <c>x</c> itself in an ordinary context, the anchoring constant <c>σ(x) = o</c> on the root context.</param>
    /// <param name="givenNeighbourIndex">The neighbour variable the given match bound, or <c>-1</c> for a concept match.</param>
    /// <param name="givenBinding">The given match's neighbour binding when one was bound.</param>
    private void HyperJoin(Context context, DlClause given, int givenClauseId, int givenSelectedIndex, int clauseIndex, int givenPosition, DlTerm anchor, int givenNeighbourIndex, DlTerm givenBinding)
    {
        DlClause ontology = NonEmptyOntologyClauses[clauseIndex];
        ReadOnlySpan<DlLiteral> body = ontology.Body;
        BeginJoin();
        for(int position = 0; position < body.Length; position++)
        {
            if(position == givenPosition)
            {
                continue;
            }

            DlLiteral atom = body[position];
            List<int> slot = NextSlotBuffer();
            int slotNeighbour = -1;
            bool slotCentralFirst = false;
            DlLiteral slotExactAtom = default;
            int slotRoleSymbol = -1;
            if(atom.Kind == DlLiteralKind.Concept)
            {
                slotExactAtom = ApplyHyperSigma(atom, anchor, givenNeighbourIndex, givenBinding);
                CollectLiveHeads(context, slotExactAtom, slot);
            }
            else
            {
                bool centralFirst = atom.First.IsCentral;
                int neighbour = centralFirst ? atom.Second.Index : atom.First.Index;
                if(neighbour == givenNeighbourIndex)
                {
                    slotExactAtom = ApplyHyperSigma(atom, anchor, neighbour, givenBinding);
                    CollectLiveHeads(context, slotExactAtom, slot);
                }
                else
                {
                    slotNeighbour = neighbour;
                    slotCentralFirst = centralFirst;
                    slotRoleSymbol = atom.Symbol;
                    if(anchor.IsCentral)
                    {
                        CollectLiveRoleHeads(context, atom.Symbol, centralFirst, slot);
                    }
                    else
                    {
                        CollectLiveIds(context, context.GroundRoleHeads(atom.Symbol, anchor, centralFirst), slot);
                    }
                }
            }

            if(slot.Count == 0)
            {
                return;
            }

            SlotBuffers.Add(slot);
            SlotNeighbours.Add(slotNeighbour);
            SlotCentralFirst.Add(slotCentralFirst);
            SlotExactAtoms.Add(slotExactAtom);
            SlotRoleSymbols.Add(slotRoleSymbol);
        }

        ResetCursor(SlotBuffers.Count);
        while(true)
        {
            if(TryResolveBindings(context, anchor, givenNeighbourIndex, givenBinding))
            {
                ScratchBody.Clear();
                AppendSpan(ScratchBody, given.Body);
                ScratchHead.Clear();
                AppendResidual(ScratchHead, given.Head, givenSelectedIndex);
                ScratchPremiseIds.Clear();
                ScratchPremiseIds.Add(givenClauseId);
                for(int slot = 0; slot < SlotBuffers.Count; slot++)
                {
                    int premiseId = SlotBuffers[slot][Cursor[slot]];
                    ScratchPremiseIds.Add(premiseId);
                    AppendSpan(ScratchBody, context.At(premiseId).Body);

                    //The slot's premise fired on the exact looked-up image (concept or bound-
                    //neighbour slots) or on its greatest head literal of the slot's role shape
                    //(free-neighbour slots) — the residual is the head minus that literal.
                    DlLiteral fired = SlotNeighbours[slot] < 0
                        ? SlotExactAtoms[slot]
                        : FindGreatestSlotRoleLiteral(context, context.At(premiseId), slot, anchor);
                    AppendResidualExcept(ScratchHead, context.At(premiseId).Head, fired);
                }

                ReadOnlySpan<DlLiteral> ontologyHead = ontology.Head;
                for(int i = 0; i < ontologyHead.Length; i++)
                {
                    ScratchHead.Add(ApplyBindings(ontologyHead[i], anchor));
                }

                ApplyHyper(context, DlClause.Create(ScratchBody, ScratchHead, DerivedOrigin), CollectionsMarshal.AsSpan(ScratchPremiseIds));
                if(BudgetExhausted)
                {
                    return;
                }
            }

            if(!Advance())
            {
                return;
            }
        }
    }

    /// <summary>The fired head literal of a free-neighbour slot's premise: the greatest head literal matching the slot's role shape — central-anchored in an ordinary context, constant-anchored on the root context.</summary>
    /// <param name="context">The context the premise lives in.</param>
    /// <param name="premise">The premise clause.</param>
    /// <param name="slot">The odometer slot index.</param>
    /// <param name="anchor">The join's central-variable image.</param>
    /// <returns>The fired head literal.</returns>
    private DlLiteral FindGreatestSlotRoleLiteral(Context context, DlClause premise, int slot, DlTerm anchor)
    {
        return anchor.IsCentral
            ? FindGreatestRoleLiteral(premise, SlotRoleSymbols[slot], SlotCentralFirst[slot], GrammarKindOf(context))
            : FindGreatestAnchoredRoleLiteral(premise, SlotRoleSymbols[slot], anchor, SlotCentralFirst[slot], GrammarKindOf(context));
    }

    /// <summary>The greatest head literal of a clause matching an anchored role shape (symbol and the anchoring constant's position) under the root-class selection order — the literal the anchored role index registered for this clause.</summary>
    /// <param name="clause">The premise clause.</param>
    /// <param name="roleSymbol">The slot's role symbol.</param>
    /// <param name="anchor">The anchoring constant.</param>
    /// <param name="anchorFirst">Whether the anchor is the first argument in the slot's shape.</param>
    /// <param name="kind">The context kind whose selection order applies.</param>
    /// <returns>The greatest matching head literal.</returns>
    private DlLiteral FindGreatestAnchoredRoleLiteral(DlClause clause, int roleSymbol, DlTerm anchor, bool anchorFirst, ContextGrammarKind kind)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        DlLiteral best = default;
        bool found = false;
        for(int i = 0; i < head.Length; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind == DlLiteralKind.Role && literal.Symbol == roleSymbol && (anchorFirst ? literal.First.Equals(anchor) : literal.Second.Equals(anchor))
                && (!found || Order.CompareHeadLiterals(literal, best, kind) > 0))
            {
                best = literal;
                found = true;
            }
        }

        Debug.Assert(found, "A clause returned by the anchored role index carries a head literal of the looked-up shape.");

        return best;
    }

    /// <summary>Copies the live ids of an index lookup into a slot buffer — a snapshot, so insertions during the join do not perturb the enumeration.</summary>
    /// <param name="context">The context.</param>
    /// <param name="ids">The index lookup's id list, possibly with tombstoned entries.</param>
    /// <param name="slotToAppendTo">The slot buffer the live ids are appended to.</param>
    private static void CollectLiveIds(Context context, IReadOnlyList<int> ids, List<int> slotToAppendTo)
    {
        for(int i = 0; i < ids.Count; i++)
        {
            if(context.IsLive(ids[i]))
            {
                slotToAppendTo.Add(ids[i]);
            }
        }
    }

    /// <summary>Rebuilds the neighbour-binding map for the current odometer combination and checks its consistency: every slot binding the same neighbour must agree with the given match and with each other.</summary>
    /// <param name="context">The context the slot clauses live in.</param>
    /// <param name="anchor">The join's central-variable image.</param>
    /// <param name="givenNeighbourIndex">The neighbour the given match bound, or <c>-1</c>.</param>
    /// <param name="givenBinding">The given match's binding.</param>
    /// <returns><see langword="true"/> when the combination's bindings are consistent.</returns>
    private bool TryResolveBindings(Context context, DlTerm anchor, int givenNeighbourIndex, DlTerm givenBinding)
    {
        ScratchBindings.Clear();
        if(givenNeighbourIndex >= 0)
        {
            ScratchBindings[givenNeighbourIndex] = givenBinding;
        }

        for(int slot = 0; slot < SlotBuffers.Count; slot++)
        {
            int neighbour = SlotNeighbours[slot];
            if(neighbour < 0)
            {
                continue;
            }

            DlLiteral head = FindGreatestSlotRoleLiteral(context, context.At(SlotBuffers[slot][Cursor[slot]]), slot, anchor);
            DlTerm bound = SlotCentralFirst[slot] ? head.Second : head.First;
            if(ScratchBindings.TryGetValue(neighbour, out DlTerm existing))
            {
                if(!existing.Equals(bound))
                {
                    return false;
                }
            }
            else
            {
                ScratchBindings[neighbour] = bound;
            }
        }

        return true;
    }

    /// <summary>Substitutes the current combination's bindings into an ontology head literal: neighbours map to their bindings, the central variable to the join's anchor (itself in an ordinary context, the anchoring constant — building <c>f(o)</c> images where the clause head carries <c>f(x)</c> — on the root context); every other term is fixed. An equality/inequality head — the DL4 counting clause's <c>zᵢ approx zⱼ</c> and the DL2 witness-distinctness <c>fᵢ not-approx fⱼ</c> — binds both terms through the factories.</summary>
    /// <param name="atom">The ontology head literal.</param>
    /// <param name="anchor">The join's central-variable image.</param>
    /// <returns>The substituted context literal.</returns>
    private DlLiteral ApplyBindings(DlLiteral atom, DlTerm anchor)
    {
        return atom.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(atom.Symbol, BindTerm(atom.First, anchor)),
            DlLiteralKind.Role => DlLiteral.Role(atom.Symbol, BindTerm(atom.First, anchor), BindTerm(atom.Second, anchor)),
            DlLiteralKind.Equality => DlLiteral.Equality(BindTerm(atom.First, anchor), BindTerm(atom.Second, anchor)),
            _ => DlLiteral.Inequality(BindTerm(atom.First, anchor), BindTerm(atom.Second, anchor)),
        };
    }

    /// <summary>The current combination's image of one ontology head term: a neighbour maps to its binding, the central variable to the anchor (which rewrites <c>f(x)</c> to <c>f(o)</c> on the root context); every other term is fixed.</summary>
    /// <param name="term">The ontology head term.</param>
    /// <param name="anchor">The join's central-variable image.</param>
    /// <returns>The image term.</returns>
    private DlTerm BindTerm(DlTerm term, DlTerm anchor)
    {
        if(term.Kind == DlTermKind.Central)
        {
            return anchor;
        }

        if(term.Kind == DlTermKind.Function && anchor.IsIndividual)
        {
            return DlTerm.FunctionOf(term.Index, anchor.IndividualId);
        }

        if(term.Kind != DlTermKind.Neighbour)
        {
            return term;
        }

        bool bound = ScratchBindings.TryGetValue(term.Index, out DlTerm binding);
        Debug.Assert(bound, "Every neighbour variable in an ontology clause head is bound by a body atom.");

        return binding;
    }

    /// <summary>Substitutes the join anchor and one resolved neighbour binding into an ontology body atom, yielding the exact head literal a premise must carry: the central variable maps to the anchor, the matching neighbour to its binding.</summary>
    /// <param name="atom">The ontology body atom.</param>
    /// <param name="anchor">The join's central-variable image.</param>
    /// <param name="neighbourIndex">The resolved neighbour variable, or <c>-1</c>.</param>
    /// <param name="binding">The neighbour's binding.</param>
    /// <returns>The substituted atom.</returns>
    private static DlLiteral ApplyHyperSigma(DlLiteral atom, DlTerm anchor, int neighbourIndex, DlTerm binding)
    {
        return atom.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(atom.Symbol, HyperImage(atom.First, anchor, neighbourIndex, binding)),
            DlLiteralKind.Role => DlLiteral.Role(atom.Symbol, HyperImage(atom.First, anchor, neighbourIndex, binding), HyperImage(atom.Second, anchor, neighbourIndex, binding)),
            _ => atom,
        };
    }

    /// <summary>The image of one ontology body term under the anchored Hyper substitution: the central variable maps to the anchor, the matching neighbour to its binding; every other term is fixed.</summary>
    /// <param name="term">The body term.</param>
    /// <param name="anchor">The join's central-variable image.</param>
    /// <param name="neighbourIndex">The resolved neighbour variable, or <c>-1</c>.</param>
    /// <param name="binding">The neighbour's binding.</param>
    /// <returns>The image term.</returns>
    private static DlTerm HyperImage(DlTerm term, DlTerm anchor, int neighbourIndex, DlTerm binding)
    {
        if(term.Kind == DlTermKind.Central)
        {
            return anchor;
        }

        return term.Kind == DlTermKind.Neighbour && term.Index == neighbourIndex ? binding : term;
    }

    /// <summary>Runs Pred with the landed predecessor clause as a premise (trigger site 2): inverts the substitution for the edge's function on the premise's fired MAXIMAL literal, looks up the successor's Pred-eligible clauses by the pre-image body atom, and attempts each with the premise pinned at that position. A root predecessor inverts per anchoring constant read off the literal (<c>σ = {y↦o, x↦f(o)}</c>).</summary>
    /// <param name="predecessor">The predecessor context u the clause landed in.</param>
    /// <param name="function">The edge's function symbol f.</param>
    /// <param name="successor">The successor context the edge targets.</param>
    /// <param name="premiseId">The premise's clause id in the predecessor.</param>
    /// <param name="premiseSelected">The maximal head literal the premise participates through (the dispatch loop's literal).</param>
    private void PredFromNewPremise(Context predecessor, int function, Context successor, int premiseId, DlLiteral premiseSelected)
    {
        if(UsesConstantAnchoredRoot(predecessor))
        {
            if(premiseSelected.First.IsIndividual || premiseSelected.First.IsFunctionOfIndividual)
            {
                PredFromNewRootPremise(predecessor, function, successor, premiseId, premiseSelected, AnchorIndividualOf(premiseSelected.First, function));
            }

            if(!BudgetExhausted && premiseSelected.Kind != DlLiteralKind.Concept && (premiseSelected.Second.IsIndividual || premiseSelected.Second.IsFunctionOfIndividual))
            {
                int secondCandidate = AnchorIndividualOf(premiseSelected.Second, function);
                if(secondCandidate != AnchorIndividualOf(premiseSelected.First, function))
                {
                    PredFromNewRootPremise(predecessor, function, successor, premiseId, premiseSelected, secondCandidate);
                }
            }

            return;
        }

        if(InvertPredSigma(premiseSelected, function) is not DlLiteral bodyShape)
        {
            return;
        }

        AttemptPredForBodyShape(predecessor, successor, function, bodyShape, premiseId, anchorIndividual: -1);
    }

    /// <summary>The candidate anchoring constant a root-premise slot names: the individual itself, or the individual under a matching <c>f(o)</c>; <c>-1</c> for a non-matching slot.</summary>
    /// <param name="term">The premise literal's slot term.</param>
    /// <param name="function">The edge's function symbol.</param>
    /// <returns>The candidate individual id, or <c>-1</c>.</returns>
    private static int AnchorIndividualOf(DlTerm term, int function)
    {
        return term.Kind switch
        {
            DlTermKind.Individual => term.IndividualId,
            DlTermKind.FunctionOfIndividual when term.FunctionSymbol == function => term.IndividualId,
            _ => -1,
        };
    }

    /// <summary>Attempts the root-predecessor site-2 dispatch for one candidate anchoring constant: inverts <c>σ = {y↦o, x↦f(o)}</c> term-wise on the landed root premise's selected literal and pins the premise at each matching successor body position.</summary>
    /// <param name="predecessor">The root context.</param>
    /// <param name="function">The edge's function symbol.</param>
    /// <param name="successor">The successor context.</param>
    /// <param name="premiseId">The landed premise's clause id.</param>
    /// <param name="premiseSelected">The landed premise's selected literal.</param>
    /// <param name="anchorIndividual">The candidate anchoring constant, or <c>-1</c> for none.</param>
    private void PredFromNewRootPremise(Context predecessor, int function, Context successor, int premiseId, DlLiteral premiseSelected, int anchorIndividual)
    {
        if(anchorIndividual < 0)
        {
            return;
        }

        if(InvertRootPredSigma(premiseSelected, function, anchorIndividual) is not DlLiteral bodyShape)
        {
            return;
        }

        AttemptPredForBodyShape(predecessor, successor, function, bodyShape, premiseId, anchorIndividual);
    }

    /// <summary>Inverts the root-predecessor substitution <c>σ = {y↦o, x↦f(o)}</c> for one anchoring constant, term-wise: <c>f(o)</c> maps back to <c>x</c>, <c>o</c> to <c>y</c>, other ground terms to themselves; a fully ground pre-image is a carried conjunct, never a premise trigger.</summary>
    /// <param name="head">The root premise's selected literal.</param>
    /// <param name="function">The edge's function symbol.</param>
    /// <param name="anchorIndividual">The anchoring constant <c>o</c>.</param>
    /// <returns>The pre-image body-literal shape, or <see langword="null"/>.</returns>
    private static DlLiteral? InvertRootPredSigma(DlLiteral head, int function, int anchorIndividual)
    {
        if(InvertRootPredTerm(head.First, function, anchorIndividual) is not DlTerm first)
        {
            return null;
        }

        if(head.Kind == DlLiteralKind.Concept)
        {
            return first.IsGround ? null : DlLiteral.Concept(head.Symbol, first);
        }

        if(InvertRootPredTerm(head.Second, function, anchorIndividual) is not DlTerm second)
        {
            return null;
        }

        if(first.IsGround && second.IsGround)
        {
            return null;
        }

        return head.Kind switch
        {
            DlLiteralKind.Role => DlLiteral.Role(head.Symbol, first, second),
            DlLiteralKind.Equality => DlLiteral.Equality(first, second),
            _ => DlLiteral.Inequality(first, second),
        };
    }

    /// <summary>The root-predecessor pre-image of one head term for an anchoring constant: <c>f(o)</c> over the edge's function maps to <c>x</c>, the anchor itself to <c>y</c>, any other ground term to itself; anything else has none.</summary>
    /// <param name="term">The head term.</param>
    /// <param name="function">The edge's function symbol.</param>
    /// <param name="anchorIndividual">The anchoring constant <c>o</c>.</param>
    /// <returns>The pre-image term, or <see langword="null"/>.</returns>
    private static DlTerm? InvertRootPredTerm(DlTerm term, int function, int anchorIndividual)
    {
        return term.Kind switch
        {
            DlTermKind.FunctionOfIndividual when term.FunctionSymbol == function && term.IndividualId == anchorIndividual => DlTerm.Central,
            DlTermKind.Individual when term.IndividualId == anchorIndividual => DlTerm.Context,
            DlTermKind.Individual or DlTermKind.FunctionOfIndividual => term,
            _ => null,
        };
    }

    /// <summary>Pins a landed premise at every successor body position matching its pre-image shape and attempts each Pred completion.</summary>
    /// <param name="predecessor">The predecessor context.</param>
    /// <param name="successor">The successor context.</param>
    /// <param name="function">The edge's function symbol.</param>
    /// <param name="bodyShape">The premise's pre-image body shape.</param>
    /// <param name="premiseId">The landed premise's clause id.</param>
    /// <param name="anchorIndividual">The root anchoring constant, or <c>-1</c> for an ordinary predecessor.</param>
    private void AttemptPredForBodyShape(Context predecessor, Context successor, int function, DlLiteral bodyShape, int premiseId, int anchorIndividual)
    {
        IReadOnlyList<int> targets = successor.PredEligibleWithBody(bodyShape);
        int targetCount = targets.Count;
        for(int i = 0; i < targetCount && !BudgetExhausted; i++)
        {
            int targetId = targets[i];
            if(!successor.IsLive(targetId))
            {
                continue;
            }

            DlClause target = successor.At(targetId);
            ReadOnlySpan<DlLiteral> body = target.Body;
            for(int position = 0; position < body.Length && !BudgetExhausted; position++)
            {
                if(body[position].Equals(bodyShape))
                {
                    if(anchorIndividual >= 0)
                    {
                        AttemptPredWithSigma(predecessor, successor, target, position, premiseId, DlTerm.FunctionOf(function, anchorIndividual), DlTerm.Individual(anchorIndividual));
                    }
                    else
                    {
                        AttemptPredWithSigma(predecessor, successor, target, position, premiseId, DlTerm.Function(function), DlTerm.Central);
                    }
                }
            }
        }
    }

    /// <summary>Whether a Pred target completes to the SAME conclusion under EVERY Pred substitution — the anchored family's and the ordinary one's alike: every body literal and every head literal is ground. A Pred sigma images only the central and context terms, so a ground literal is its own image — the ground body conjuncts cross the edge verbatim and every head literal's sigma-image is the literal itself. Such a target also has no nonground body position, so its completion family is the zero-slot degenerate one: one conclusion, and that conclusion IS the target clause. Both arms read it — the anchored arm's fan-out hoist and the ordinary arm's containment skip.</summary>
    /// <param name="target">The Pred-eligible target clause.</param>
    /// <returns><see langword="true"/> when both spans are ground.</returns>
    private static bool IsAnchorInvariantZeroSlotTarget(DlClause target)
    {
        ReadOnlySpan<DlLiteral> body = target.Body;
        for(int position = 0; position < body.Length; position++)
        {
            if(!Context.IsGroundLiteral(body[position]))
            {
                return false;
            }
        }

        ReadOnlySpan<DlLiteral> head = target.Head;
        for(int position = 0; position < head.Length; position++)
        {
            if(!Context.IsGroundLiteral(head[position]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Attempts the Pred rule for one Pred-eligible target clause over the edge <c>⟨predecessor, successor, function⟩</c>: an ordinary predecessor runs one completion family under <c>σ = {x↦f(x), y↦x}</c>, except on a sigma-invariant broadcast image the predecessor already holds, whose completion is the image itself and is therefore already absorbed there — that family is elided and counted as skipped; the root predecessor runs one family per anchoring constant under <c>σ = {x↦f(o), y↦o}</c> (Table 2 condition 5's two branches), except on an anchor-invariant target, whose families all carry the one conclusion — there the first constant's family runs and the rest are counted as pruned.</summary>
    /// <param name="predecessor">The predecessor context u the conclusion lands in.</param>
    /// <param name="successor">The successor context v the target clause lives in.</param>
    /// <param name="function">The edge's function symbol f.</param>
    /// <param name="target">The Pred-eligible target clause.</param>
    /// <param name="pinnedPosition">The target body position pinned to a fixed premise, or <c>-1</c>.</param>
    /// <param name="pinnedClauseId">The pinned premise's clause id in the predecessor, or <c>-1</c>.</param>
    private void AttemptPred(Context predecessor, Context successor, int function, DlClause target, int pinnedPosition, int pinnedClauseId)
    {
        if(UsesConstantAnchoredRoot(predecessor))
        {
            PredAnchoredArmDispatches++;
            if(IsAnchorInvariantZeroSlotTarget(target))
            {
                PredAnchorInvariantTargetPasses++;
                if(Symbols.IndividualCount > 0 && !BudgetExhausted)
                {
                    AttemptPredWithSigma(predecessor, successor, target, pinnedPosition, pinnedClauseId, DlTerm.FunctionOf(function, 0), DlTerm.Individual(0));

                    //The elided constants are credited exactly when the loop this replaces would
                    //have charged them: the recheck reads the very gate the next constant's own
                    //offer would have met, and only reads it — latching here would stop
                    //state-bearing work the unpruned run performed, and charging an attempt here
                    //would spend budget no offer paid for.
                    if(!BudgetExhausted
                        && !Budget.IsExhaustedByInferences(InferenceAttempts)
                        && !Budget.IsExhaustedByPopulation(ClausesDerived))
                    {
                        PredAnchorPruned += Symbols.IndividualCount - 1;
                    }
                }

                return;
            }

            for(int individual = 0; individual < Symbols.IndividualCount && !BudgetExhausted; individual++)
            {
                AttemptPredWithSigma(predecessor, successor, target, pinnedPosition, pinnedClauseId, DlTerm.FunctionOf(function, individual), DlTerm.Individual(individual));
            }

            return;
        }

        PredOrdinaryArmDispatches++;
        if(IsAnchorInvariantZeroSlotTarget(target))
        {
            PredOrdinaryInvariantTargetPasses++;
            if(BroadcastInvariantImageIndex.TryGetValue(target, out int imageIndex))
            {
                PredBroadcastImageTargets++;
                if(predecessor.HoldsBroadcastImage(imageIndex))
                {
                    //The elided offer is credited exactly where the offer it replaces would have
                    //been charged: the recheck reads the gate that offer would have met, and only
                    //reads it — crediting past an exhausted gate would spend budget no offer paid for.
                    if(!BudgetExhausted
                        && !Budget.IsExhaustedByInferences(InferenceAttempts)
                        && !Budget.IsExhaustedByPopulation(ClausesDerived))
                    {
                        PredBroadcastContainedSkips++;
                    }

                    return;
                }
            }
        }

        //A nominal-root predecessor takes the ordinary arm: its own constant is
        //already central, so the ordinary sigma {x↦f(x), y↦x} is exactly the
        //per-individual image of the root sigma at the home constant.
        AttemptPredWithSigma(predecessor, successor, target, pinnedPosition, pinnedClauseId, DlTerm.Function(function), DlTerm.Central);
    }

    /// <summary>
    /// Attempts the Pred rule for one Pred-eligible target clause under a fixed
    /// substitution: every NONGROUND target body atom needs a predecessor
    /// premise whose SELECTED head literal is exactly its sigma-image (one
    /// position optionally pinned to a fixed premise), while GROUND body
    /// conjuncts <c>Ci</c> are carried into the conclusion body untouched
    /// (Table 2's <c>⋀Ci</c>); each completion adds the conclusion — the union
    /// of the premise bodies and the carried ground conjuncts, every premise's
    /// residual (non-selected) head disjuncts, and the sigma-translation of
    /// EVERY target head literal (Table 2's <c>⋁∆ᵢ ∨ ⋁Lᵢσ</c>) — to the
    /// predecessor. An empty target body degenerates to collapse propagation:
    /// <c>⊤→⊥</c> in the successor puts <c>⊤→⊥</c> into the predecessor.
    /// </summary>
    /// <param name="predecessor">The predecessor context u the conclusion lands in.</param>
    /// <param name="successor">The successor context v the target clause lives in.</param>
    /// <param name="target">The Pred-eligible target clause.</param>
    /// <param name="pinnedPosition">The target body position pinned to a fixed premise, or <c>-1</c>.</param>
    /// <param name="pinnedClauseId">The pinned premise's clause id in the predecessor, or <c>-1</c>.</param>
    /// <param name="centralImage">The image of <c>x</c> — <c>f(x)</c>, or <c>f(o)</c> from the root.</param>
    /// <param name="contextImage">The image of <c>y</c> — <c>x</c>, or <c>o</c> from the root.</param>
    private void AttemptPredWithSigma(Context predecessor, Context successor, DlClause target, int pinnedPosition, int pinnedClauseId, DlTerm centralImage, DlTerm contextImage)
    {
        if(pinnedClauseId >= 0 && !predecessor.IsLive(pinnedClauseId))
        {
            return;
        }

        ReadOnlySpan<DlLiteral> body = target.Body;
        BeginJoin();
        ScratchPredSlotPositions.Clear();
        for(int position = 0; position < body.Length; position++)
        {
            if(Context.IsGroundLiteral(body[position]))
            {
                continue;
            }

            List<int> slot = NextSlotBuffer();
            if(position == pinnedPosition)
            {
                slot.Add(pinnedClauseId);
            }
            else
            {
                CollectLiveHeads(predecessor, ApplyPredSigma(body[position], centralImage, contextImage), slot);
            }

            if(slot.Count == 0)
            {
                return;
            }

            SlotBuffers.Add(slot);
            ScratchPredSlotPositions.Add(position);
        }

        PredOdometerRuns++;
        PredRunHasOffered = false;
        ResetCursor(SlotBuffers.Count);
        while(true)
        {
            ScratchBody.Clear();
            ScratchHead.Clear();
            ReadOnlySpan<DlLiteral> targetHead = target.Head;
            for(int i = 0; i < targetHead.Length; i++)
            {
                ScratchHead.Add(ApplyPredSigma(targetHead[i], centralImage, contextImage));
            }

            ReadOnlySpan<DlLiteral> targetBody = target.Body;
            for(int position = 0; position < targetBody.Length; position++)
            {
                if(Context.IsGroundLiteral(targetBody[position]))
                {
                    ScratchBody.Add(targetBody[position]);
                }
            }

            ScratchPremiseIds.Clear();
            for(int slot = 0; slot < SlotBuffers.Count; slot++)
            {
                int premiseId = SlotBuffers[slot][Cursor[slot]];
                ScratchPremiseIds.Add(premiseId);
                AppendSpan(ScratchBody, predecessor.At(premiseId).Body);

                //The slot's premise fired on exactly the sigma-image of the target body atom at
                //the slot's position (one slot per NONGROUND body position, pinned included), so
                //the carried residual is the premise head minus that literal — by value, since
                //heads are canonical duplicate-free sets.
                AppendResidualExcept(ScratchHead, predecessor.At(premiseId).Head, ApplyPredSigma(targetBody[ScratchPredSlotPositions[slot]], centralImage, contextImage));
            }

            //The odometer's own assembly buffers ARE the offered conclusion, canonicalised
            //where the clause factory would have canonicalised them; the premise-id and
            //slot-position buffers index the slot lists and the target's own stable body,
            //never these, so the in-place sort perturbs no alignment, and the next
            //combination clears both buffers before assembling again.
            DlClause.CanonicaliseInPlace(ScratchBody);
            DlClause.CanonicaliseInPlace(ScratchHead);
            ApplyPredSpans(predecessor, CollectionsMarshal.AsSpan(ScratchBody), CollectionsMarshal.AsSpan(ScratchHead), DerivedOrigin, CollectionsMarshal.AsSpan(ScratchPremiseIds));
            if(BudgetExhausted || !Advance())
            {
                return;
            }
        }
    }

    /// <summary>
    /// Processes a Succ candidate <c>(u, trigger)</c>, recomputing from the
    /// current state: skips a stale trigger (no live trigger-bearing head
    /// remains); computes K1/K2 under the trigger's substitution (<c>σ =
    /// {y↦x, x↦f(x)}</c> ordinarily, <c>σ = {y↦o, x↦f(o)}</c> per anchoring
    /// constant on the root context); skips when an existing edge already
    /// covers every non-core K2 hypothesis up to redundancy (the completeness
    /// proof's applicability reading); otherwise expands — resolves the
    /// cautious successor, adds the deduplicated function edge (a genuinely new
    /// edge runs Pred trigger site 3), and seeds the hypothesis <c>A'→A'</c>
    /// for each <c>A' ∈ K2\core</c>. The strategy can never return the root
    /// context, so Succ never targets <c>vr</c>.
    /// </summary>
    /// <param name="predecessorId">The predecessor context id u.</param>
    /// <param name="trigger">The packed trigger term — <c>f(x)</c>, or <c>f(o)</c> on the root context.</param>
    private void ProcessSucc(int predecessorId, DlTerm trigger)
    {
        Context predecessor = Structure[predecessorId];
        if(!predecessor.HasLiveFunctionHead(trigger))
        {
            return;
        }

        int function = trigger.FunctionSymbol;
        DlTerm contextImage = trigger.IsFunctionOfIndividual ? DlTerm.Individual(trigger.IndividualId) : DlTerm.Central;
        ComputeSuccTriggers(predecessor, trigger, contextImage);
        if(IsSuccCovered(predecessorId, function))
        {
            return;
        }

        if(!TryApply())
        {
            return;
        }

        Context successor = ResolveCautiousSuccessor(function);
        SuccApplications++;
        RuleApplications++;
        bool designated = GroundContextByFunction.ContainsKey(function);

        if(Structure.TryAddEdge(new ContextEdge(predecessorId, function, successor.Id)))
        {
            if(designated)
            {
                GroundEdgesSeeded++;
            }

            PredOverNewEdge(predecessor, successor, function);
            if(PropagationRelevance == RootPropagationRelevance.GroundFiltered && !BudgetExhausted)
            {
                SeedRelevanceOverNewEdge(predecessor, successor);
            }
        }

        for(int i = 0; i < ScratchK2.Count; i++)
        {
            DlLiteral hypothesis = ScratchK2[i];
            if(!successor.CoreContains(hypothesis))
            {
                SuccOffers++;
                ClauseOfferOutcome hypothesisOutcome = AddClauseCore(successor, DlClause.Create([hypothesis], [hypothesis], DerivedOrigin), []);
                if(hypothesisOutcome == ClauseOfferOutcome.ExactDuplicate)
                {
                    SuccDuplicateHits++;
                }
                else if(hypothesisOutcome == ClauseOfferOutcome.Subsumed)
                {
                    SuccSubsumedHits++;
                }
            }
        }

        //At a designated ground target the cautious core promotion channel is severed
        //(the core is pinned to the marker), so a K1 trigger — an unconditionally
        //forced predecessor head — is seeded UNCONDITIONALLY as ⊤→A' into the target,
        //not merely as the conditional A'→A' hypothesis, so cross-predecessor
        //consequences at a shared in-degree-≥2 node conjoin and clash.
        if(designated)
        {
            SeedUnconditionalK1(successor);
        }
    }

    /// <summary>Seeds each current K1 trigger as an unconditional clause <c>⊤→A'</c> into a designated ground target, skipping a trigger already present in the core or as an unconditional head.</summary>
    /// <param name="successor">The designated ground target context.</param>
    private void SeedUnconditionalK1(Context successor)
    {
        foreach(DlLiteral trigger in ScratchK1)
        {
            if(!successor.CoreContains(trigger) && !successor.UnconditionalContains(trigger))
            {
                SuccOffers++;
                ClauseOfferOutcome triggerOutcome = AddClauseCore(successor, DlClause.Create([], [trigger], DerivedOrigin), []);
                if(triggerOutcome == ClauseOfferOutcome.ExactDuplicate)
                {
                    SuccDuplicateHits++;
                }
                else if(triggerOutcome == ClauseOfferOutcome.Subsumed)
                {
                    SuccSubsumedHits++;
                }
            }
        }
    }

    /// <summary>Attempts Pred for every existing Pred-eligible successor clause into the predecessor over a newly added edge (trigger site 3).</summary>
    /// <param name="predecessor">The predecessor context.</param>
    /// <param name="successor">The successor context.</param>
    /// <param name="function">The edge's function symbol.</param>
    private void PredOverNewEdge(Context predecessor, Context successor, int function)
    {
        CurrentPredOrigin = PredOrigin.NewEdge;
        IReadOnlyList<int> eligible = successor.PredEligibleClauses;
        int count = eligible.Count;
        for(int i = 0; i < count && !BudgetExhausted; i++)
        {
            int clauseId = eligible[i];
            if(successor.IsLive(clauseId))
            {
                AttemptPred(predecessor, successor, function, successor.At(clauseId), pinnedPosition: -1, pinnedClauseId: -1);
            }
        }
    }

    /// <summary>Recomputes the Succ trigger sets into the scratch fields: K2 collects the successor triggers whose sigma-image is a live head of the predecessor; K1 the subset whose image is an unconditional head (plain membership). The materialized templates are checked through their sigma-images; the broadened GROUND triggers (<c>S(o, o′)</c> / <c>o ≈ o′</c> selected heads, sigma-fixed) are read off the predecessor's ground trigger-head index directly.</summary>
    /// <param name="predecessor">The predecessor context u.</param>
    /// <param name="centralImage">The image of <c>x</c> under the candidate's substitution.</param>
    /// <param name="contextImage">The image of <c>y</c> under the candidate's substitution.</param>
    private void ComputeSuccTriggers(Context predecessor, DlTerm centralImage, DlTerm contextImage)
    {
        ScratchK2.Clear();
        ScratchK1.Clear();
        for(int i = 0; i < SuccessorTriggers.Count; i++)
        {
            DlLiteral trigger = SuccessorTriggers[i];
            DlLiteral image = ApplyPredSigma(trigger, centralImage, contextImage);
            if(HasLiveHead(predecessor, image))
            {
                ScratchK2.Add(trigger);
                if(predecessor.UnconditionalContains(image))
                {
                    ScratchK1.Add(trigger);
                }
            }
        }

        ScratchGroundTriggers.Clear();
        predecessor.CollectLiveGroundSuccessorTriggerHeads(ScratchGroundTriggers);
        for(int i = 0; i < ScratchGroundTriggers.Count; i++)
        {
            DlLiteral ground = ScratchGroundTriggers[i];
            ScratchK2.Add(ground);
            if(predecessor.UnconditionalContains(ground))
            {
                ScratchK1.Add(ground);
            }
        }
    }

    /// <summary>Whether an existing edge already covers this expansion: some edge <c>⟨u, ·, f⟩</c> whose target contains <c>A'→A'</c> up to redundancy for every <c>A' ∈ K2\core</c> — the completeness proof's quantifier reading of the applicability condition.</summary>
    /// <param name="predecessorId">The predecessor context id.</param>
    /// <param name="function">The function symbol.</param>
    /// <returns><see langword="true"/> when an existing edge makes the expansion redundant.</returns>
    private bool IsSuccCovered(int predecessorId, int function)
    {
        bool designated = GroundContextByFunction.ContainsKey(function);
        IReadOnlyList<ContextEdge> outgoing = Structure.Outgoing(predecessorId);
        for(int i = 0; i < outgoing.Count; i++)
        {
            ContextEdge edge = outgoing[i];
            if(edge.Function != function)
            {
                continue;
            }

            Context target = Structure[edge.Target];
            //Coverage at a designated ground target additionally requires every current
            //K1 trigger present as an unconditional ⊤→A' head, so a K1 member landing
            //after the edge exists re-fires the unconditional seeding rather than being
            //silently skipped by hypothesis-only coverage.
            if(CoversHypotheses(target) && (!designated || CoversK1Unconditionals(target)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a designated ground target already carries every current K1 trigger as an unconditional <c>⊤→A'</c> head (or a core atom).</summary>
    /// <param name="target">The designated ground target context.</param>
    /// <returns><see langword="true"/> when every current K1 trigger is present unconditionally.</returns>
    private bool CoversK1Unconditionals(Context target)
    {
        foreach(DlLiteral trigger in ScratchK1)
        {
            if(!target.CoreContains(trigger) && !target.UnconditionalContains(trigger))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a target context contains <c>A'→A'</c> up to redundancy for every current K2 hypothesis outside the target's core.</summary>
    /// <param name="target">The candidate target context.</param>
    /// <returns><see langword="true"/> when every non-core hypothesis is present up to redundancy.</returns>
    private bool CoversHypotheses(Context target)
    {
        //A one-literal span is canonical by construction, and the probe only reads it, so
        //the same buffer stands as the tautology's body and head; the question is asked
        //without ever building the clause it asks about.
        Span<DlLiteral> probe = stackalloc DlLiteral[1];
        for(int i = 0; i < ScratchK2.Count; i++)
        {
            DlLiteral hypothesis = ScratchK2[i];
            if(target.CoreContains(hypothesis))
            {
                continue;
            }

            probe[0] = hypothesis;
            if(!target.ContainsUpToRedundancy(probe, probe, DerivedOrigin, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Resolves the cautious successor (Definition 6): a designated ground-edge function routes to its representative's ground context FIRST, overriding the cautious strategy so an asserted edge's successor is always the named target's context; otherwise the context with core <c>{B(x)}</c> when f has a unique concept filler B in the ontology heads and <c>B(x) ∈ K1</c>, else the trivial context. Reused through the registry or created with seeding.</summary>
    /// <param name="function">The function symbol f.</param>
    /// <returns>The successor context.</returns>
    private Context ResolveCautiousSuccessor(int function)
    {
        if(GroundContextByFunction.TryGetValue(function, out int groundId))
        {
            ContextsReused++;

            return Structure[groundId];
        }

        if(UniqueFillerByFunction.TryGetValue(function, out int filler))
        {
            DlLiteral fillerAtom = DlLiteral.Concept(filler, DlTerm.Central);
            if(ScratchK1.Contains(fillerAtom))
            {
                return ResolveByCore(fillerAtom);
            }
        }

        return ResolveTrivial();
    }

    /// <summary>Reuses or creates the context with the given single-atom core.</summary>
    /// <param name="coreAtom">The core concept atom.</param>
    /// <returns>The context with exactly that core.</returns>
    private Context ResolveByCore(DlLiteral coreAtom)
    {
        if(Structure.TryGetByCoreAtom(coreAtom.Symbol, out int existing))
        {
            ContextsReused++;

            return Structure[existing];
        }

        Context context = Structure.CreateContext([coreAtom]);
        ContextsCreated++;
        SeedContext(context);

        return context;
    }

    /// <summary>Reuses the trivial (empty-core) context minted at <see cref="Create"/>.</summary>
    /// <returns>The trivial context.</returns>
    private Context ResolveTrivial()
    {
        ContextsReused++;

        return Structure[Structure.TrivialContextId];
    }

    /// <summary>Seeds a freshly created ordinary context: the Core seed <c>⊤→A</c> per core atom, the Top-semantics seed <c>⊤→Top(x)</c>, the empty-body ontology firing (Hyper with no premises; those heads carry no neighbour variable), and the accumulated n-zero root-Pred broadcast conclusions (context-independent images a root clause with no nonground body atoms propagates to every context).</summary>
    /// <param name="context">The context to seed.</param>
    private void SeedContext(Context context)
    {
        IReadOnlyList<DlLiteral> core = context.CoreAtoms;
        for(int i = 0; i < core.Count; i++)
        {
            ApplyCore(context, DlClause.Create([], [core[i]], DerivedOrigin), []);
        }

        ApplyCore(context, DlClause.Create([], [DlLiteral.Concept(ContextSymbolTable.Top, DlTerm.Central)], DerivedOrigin), []);

        for(int i = 0; i < EmptyBodyOntologyClauses.Count; i++)
        {
            ApplyHyper(context, EmptyBodyOntologyClauses[i], []);
        }

        for(int i = 0; i < RootBroadcastClauses.Count && !BudgetExhausted; i++)
        {
            ApplyBroadcastRootPred(context, RootBroadcastClauses[i], i);
        }
    }

    /// <summary>
    /// The Join rule's premise-two dispatch (arXiv:1805.01396 Table 3): the
    /// landed clause's maximal GROUND literal <c>A</c> resolves against every
    /// clause whose body carries <c>A</c> (form a); a landed empty-body clause
    /// whose maximal literal abstracts to <c>A′</c> with <c>A′{x↦o} = A</c>
    /// pairs with an empty-body <c>x ≈ o</c> clause (form b, both maximality
    /// conditions); a landed empty-body maximal <c>x ≈ o</c> pairs the
    /// other way. Join conclusions land on the EAGER queue — Proposition 1
    /// makes eager Join a termination condition.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="clause">The landed clause.</param>
    /// <param name="clauseId">The landed clause's id.</param>
    /// <param name="selectedIndex">The dispatched maximal literal's head index.</param>
    private void JoinFromMaximalHead(Context context, DlClause clause, int clauseId, int selectedIndex)
    {
        JoinRunHasOffered = false;
        DlLiteral selected = clause.Head[selectedIndex];
        if(Context.IsGroundLiteral(selected) && MentionsIndividual(selected))
        {
            ScratchJoinDispatch.Clear();
            CollectLiveIds(context, context.GroundBodyClauses(selected), ScratchJoinDispatch);
            for(int i = 0; i < ScratchJoinDispatch.Count && !BudgetExhausted; i++)
            {
                int premiseId = ScratchJoinDispatch[i];
                if(premiseId != clauseId && context.IsLive(premiseId) && context.IsLive(clauseId))
                {
                    ApplyJoin(context, premiseId, selected, clauseId);
                }
            }
        }

        if(BudgetExhausted || clause.BodyLength != 0)
        {
            return;
        }

        if(IsCentralIndividualEquality(selected))
        {
            JoinBridgeFromEquality(context, clauseId, selected);
        }
        else if(MentionsCentralTerm(selected.First) || MentionsCentralTerm(selected.Second))
        {
            JoinBridgeFromAbstract(context, clauseId, selected);
        }
    }

    /// <summary>Whether a literal is an <c>x ≈ o</c> equality — the Join bridge's second premise shape (canonically stored variable-first).</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> for a central-individual equality.</returns>
    private static bool IsCentralIndividualEquality(DlLiteral literal)
    {
        return literal.Kind == DlLiteralKind.Equality
            && ((literal.First.IsCentral && literal.Second.IsIndividual) || (literal.Second.IsCentral && literal.First.IsIndividual));
    }

    /// <summary>Whether a literal mentions at least one named individual in a slot.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns><see langword="true"/> when an individual occurs.</returns>
    private static bool MentionsIndividual(DlLiteral literal)
    {
        return literal.First.IsIndividual || literal.First.IsFunctionOfIndividual
            || (literal.Kind != DlLiteralKind.Concept && (literal.Second.IsIndividual || literal.Second.IsFunctionOfIndividual));
    }

    /// <summary>The Join premise-one dispatch: a landed clause whose BODY carries ground literals resolves each against the clauses holding that literal maximal (form a), and pairs it with the bridge premises (form b).</summary>
    /// <param name="context">The context.</param>
    /// <param name="clause">The landed clause.</param>
    /// <param name="clauseId">The landed clause's id.</param>
    private void JoinFromGroundBody(Context context, DlClause clause, int clauseId)
    {
        JoinRunHasOffered = false;
        ReadOnlySpan<DlLiteral> body = clause.Body;
        for(int position = 0; position < body.Length && !BudgetExhausted; position++)
        {
            DlLiteral groundLiteral = body[position];
            if(!Context.IsGroundLiteral(groundLiteral) || !MentionsIndividual(groundLiteral))
            {
                continue;
            }

            ScratchJoinDispatch.Clear();
            CollectLiveIds(context, context.SelectedHeadClauses(groundLiteral), ScratchJoinDispatch);
            for(int i = 0; i < ScratchJoinDispatch.Count && !BudgetExhausted; i++)
            {
                int otherId = ScratchJoinDispatch[i];
                if(otherId != clauseId && context.IsLive(otherId) && context.IsLive(clauseId))
                {
                    ApplyJoin(context, clauseId, groundLiteral, otherId);
                }
            }

            if(!BudgetExhausted)
            {
                JoinBridgeForGroundBodyLiteral(context, clauseId, groundLiteral);
            }
        }
    }

    /// <summary>Applies Join form (a) under the budget gate: from <c>A ∧ Γ → ∆</c> (premise one, <c>A</c> ground in the body) and a clause holding <c>A</c> MAXIMAL in its head (premise two — <c>∆′ ∪ ∆″ ⋡ A</c> holds because <c>A</c> is in the maximal set), derive <c>Γ ∧ Γ′ → ∆ ∨ ∆′ ∨ ∆″</c>; the conclusion's landed event is EAGER.</summary>
    /// <param name="context">The context.</param>
    /// <param name="premiseOneId">The ground-body premise's clause id.</param>
    /// <param name="groundLiteral">The resolved ground literal <c>A</c>.</param>
    /// <param name="premiseTwoId">The maximal-head premise's clause id.</param>
    private void ApplyJoin(Context context, int premiseOneId, DlLiteral groundLiteral, int premiseTwoId)
    {
        if(!TryApply())
        {
            return;
        }

        DlClause premiseOne = context.At(premiseOneId);
        DlClause premiseTwo = context.At(premiseTwoId);
        ScratchBody.Clear();
        AppendResidualExcept(ScratchBody, premiseOne.Body, groundLiteral);
        AppendSpan(ScratchBody, premiseTwo.Body);
        ScratchHead.Clear();
        AppendSpan(ScratchHead, premiseOne.Head);
        AppendResidualExcept(ScratchHead, premiseTwo.Head, groundLiteral);

        DlClause.CanonicaliseInPlace(ScratchBody);
        DlClause.CanonicaliseInPlace(ScratchHead);
        AddJoinConclusionSpans(context, CollectionsMarshal.AsSpan(ScratchBody), CollectionsMarshal.AsSpan(ScratchHead), DerivedOrigin, [premiseOneId, premiseTwoId]);
    }

    /// <summary>The bridge dispatch for a landed empty-body <c>x ≈ o</c> premise: pairs it with every empty-body clause whose maximal literal abstracts a ground body literal over <c>o</c>, against every premise-one clause carrying that literal.</summary>
    /// <param name="context">The context.</param>
    /// <param name="equalityId">The landed <c>x ≈ o</c> clause's id.</param>
    /// <param name="equality">The landed <c>x ≈ o</c> literal.</param>
    private void JoinBridgeFromEquality(Context context, int equalityId, DlLiteral equality)
    {
        int individual = equality.First.IsIndividual ? equality.First.IndividualId : equality.Second.IndividualId;
        ScratchGroundTriggers.Clear();
        context.CollectGroundBodyLiteralsMentioning(individual, ScratchGroundTriggers);
        for(int i = 0; i < ScratchGroundTriggers.Count && !BudgetExhausted; i++)
        {
            DlLiteral groundLiteral = ScratchGroundTriggers[i];
            ScratchJoinPremises.Clear();
            CollectLiveIds(context, context.GroundBodyClauses(groundLiteral), ScratchJoinPremises);
            for(int p = 0; p < ScratchJoinPremises.Count && !BudgetExhausted; p++)
            {
                JoinBridgeAbstractions(context, ScratchJoinPremises[p], groundLiteral, individual, equalityId);
            }
        }
    }

    /// <summary>The bridge dispatch for a landed empty-body clause whose maximal literal mentions the central variable: for each constant with a live empty-body <c>x ≈ o</c> clause, grounds the literal at <c>o</c> and resolves against the premise-one clauses carrying the image.</summary>
    /// <param name="context">The context.</param>
    /// <param name="abstractId">The landed clause's id.</param>
    /// <param name="abstractLiteral">The landed clause's maximal literal <c>A′</c>.</param>
    private void JoinBridgeFromAbstract(Context context, int abstractId, DlLiteral abstractLiteral)
    {
        IReadOnlyList<int> registered = context.BridgeIndividuals;
        int lastVisited = -1;
        while(!BudgetExhausted)
        {
            //The cursor is a VALUE, re-located by binary search after every visit: the
            //sweep's own conclusions insert into this context synchronously, so a bridge
            //individual can register mid-sweep and shift every position of the sorted
            //posting. A value cursor visits an individual registering ABOVE it later in
            //the same sweep and never visits one registering below, which is exactly what
            //the per-individual dictionary probe answered before the posting existed; an
            //index cursor would re-read a shifted slot or step over an entry.
            int position = LowerBound(registered, lastVisited + 1);
            if(position >= registered.Count)
            {
                break;
            }

            int individual = registered[position];
            lastVisited = individual;
            DlLiteral bridge = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individual)));
            ScratchJoinDispatch.Clear();
            CollectLiveIds(context, context.SelectedHeadClauses(bridge), ScratchJoinDispatch);
            for(int b = 0; b < ScratchJoinDispatch.Count && !BudgetExhausted; b++)
            {
                int bridgeId = ScratchJoinDispatch[b];
                if(context.At(bridgeId).BodyLength != 0)
                {
                    continue;
                }

                DlLiteral image = GroundAtIndividual(abstractLiteral, individual);
                ScratchEqDispatch.Clear();
                CollectLiveIds(context, context.GroundBodyClauses(image), ScratchEqDispatch);
                for(int p = 0; p < ScratchEqDispatch.Count && !BudgetExhausted; p++)
                {
                    ApplyJoinBridge(context, ScratchEqDispatch[p], image, abstractId, abstractLiteral, bridgeId, bridge);
                }
            }
        }
    }

    /// <summary>The index of the first entry of an ASCENDING deduplicated id list at or above a bound, or the list's count when every entry is below it — the bridge sweep's value-cursor step, run against a list the sweep's own conclusions may insert into between calls.</summary>
    /// <param name="ascendingIds">The ascending, deduplicated id list.</param>
    /// <param name="bound">The inclusive lower bound.</param>
    /// <returns>The index of the first entry at or above the bound.</returns>
    private static int LowerBound(IReadOnlyList<int> ascendingIds, int bound)
    {
        int low = 0;
        int high = ascendingIds.Count;
        while(low < high)
        {
            int middle = low + ((high - low) >> 1);
            if(ascendingIds[middle] < bound)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>The bridge dispatch for a landed premise-one ground body literal: enumerates its x-abstractions per mentioned constant and resolves against the empty-body abstract and <c>x ≈ o</c> premises.</summary>
    /// <param name="context">The context.</param>
    /// <param name="premiseOneId">The premise-one clause id.</param>
    /// <param name="groundLiteral">The ground body literal <c>A</c>.</param>
    private void JoinBridgeForGroundBodyLiteral(Context context, int premiseOneId, DlLiteral groundLiteral)
    {
        if(groundLiteral.First.IsIndividual)
        {
            JoinBridgeAbstractions(context, premiseOneId, groundLiteral, groundLiteral.First.IndividualId, requiredBridgeId: -1);
        }

        if(!BudgetExhausted && groundLiteral.Kind != DlLiteralKind.Concept && groundLiteral.Second.IsIndividual
            && (!groundLiteral.First.IsIndividual || groundLiteral.Second.IndividualId != groundLiteral.First.IndividualId))
        {
            JoinBridgeAbstractions(context, premiseOneId, groundLiteral, groundLiteral.Second.IndividualId, requiredBridgeId: -1);
        }
    }

    /// <summary>Resolves the Join bridge for one premise-one literal and one constant: every non-empty subset of the literal's <c>o</c>-slots abstracts to a candidate <c>A′</c> (<c>A′{x↦o} = A</c>); each live empty-body clause holding <c>A′</c> maximal pairs with each live empty-body clause holding <c>x ≈ o</c> maximal.</summary>
    /// <param name="context">The context.</param>
    /// <param name="premiseOneId">The premise-one clause id.</param>
    /// <param name="groundLiteral">The ground literal <c>A</c>.</param>
    /// <param name="individual">The constant <c>o</c>.</param>
    /// <param name="requiredBridgeId">A specific <c>x ≈ o</c> clause id to pair with, or <c>-1</c> for all.</param>
    private void JoinBridgeAbstractions(Context context, int premiseOneId, DlLiteral groundLiteral, int individual, int requiredBridgeId)
    {
        DlLiteral bridge = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individual)));
        DlTerm constant = DlTerm.Individual(individual);
        bool firstMatches = groundLiteral.First.Equals(constant);
        bool secondMatches = groundLiteral.Kind != DlLiteralKind.Concept && groundLiteral.Second.Equals(constant);
        Span<DlLiteral> candidates = stackalloc DlLiteral[3];
        int candidateCount = 0;
        if(firstMatches)
        {
            candidates[candidateCount++] = ReplaceSlots(groundLiteral, replaceFirst: true, replaceSecond: false);
        }

        if(secondMatches)
        {
            candidates[candidateCount++] = ReplaceSlots(groundLiteral, replaceFirst: false, replaceSecond: true);
        }

        if(firstMatches && secondMatches)
        {
            candidates[candidateCount++] = ReplaceSlots(groundLiteral, replaceFirst: true, replaceSecond: true);
        }

        for(int c = 0; c < candidateCount && !BudgetExhausted; c++)
        {
            DlLiteral abstracted = candidates[c].Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality
                ? ContextTermOrder.OrientEqualityLiteral(candidates[c])
                : candidates[c];
            ScratchJoinDispatch.Clear();
            CollectLiveIds(context, context.SelectedHeadClauses(abstracted), ScratchJoinDispatch);
            for(int a = 0; a < ScratchJoinDispatch.Count && !BudgetExhausted; a++)
            {
                int abstractId = ScratchJoinDispatch[a];
                if(context.At(abstractId).BodyLength != 0)
                {
                    continue;
                }

                if(requiredBridgeId >= 0)
                {
                    if(context.IsLive(requiredBridgeId))
                    {
                        ApplyJoinBridge(context, premiseOneId, groundLiteral, abstractId, abstracted, requiredBridgeId, bridge);
                    }

                    continue;
                }

                ScratchEqDispatch.Clear();
                CollectLiveIds(context, context.SelectedHeadClauses(bridge), ScratchEqDispatch);
                for(int b = 0; b < ScratchEqDispatch.Count && !BudgetExhausted; b++)
                {
                    int bridgeId = ScratchEqDispatch[b];
                    if(context.At(bridgeId).BodyLength == 0)
                    {
                        ApplyJoinBridge(context, premiseOneId, groundLiteral, abstractId, abstracted, bridgeId, bridge);
                    }
                }
            }
        }
    }

    /// <summary>The image of a literal with chosen slots replaced by the central variable — the bridge abstraction <c>A′</c> builder.</summary>
    /// <param name="literal">The ground literal.</param>
    /// <param name="replaceFirst">Whether the first slot becomes <c>x</c>.</param>
    /// <param name="replaceSecond">Whether the second slot becomes <c>x</c>.</param>
    /// <returns>The abstracted literal.</returns>
    private static DlLiteral ReplaceSlots(DlLiteral literal, bool replaceFirst, bool replaceSecond)
    {
        DlTerm first = replaceFirst ? DlTerm.Central : literal.First;
        DlTerm second = replaceSecond ? DlTerm.Central : literal.Second;

        return literal.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(literal.Symbol, first),
            DlLiteralKind.Role => DlLiteral.Role(literal.Symbol, first, second),
            DlLiteralKind.Equality => DlLiteral.Equality(first, second),
            _ => DlLiteral.Inequality(first, second),
        };
    }

    /// <summary>The image of a central-mentioning literal grounded at one constant — the bridge's <c>A = A′{x↦o}</c>.</summary>
    /// <param name="literal">The abstract literal <c>A′</c>.</param>
    /// <param name="individual">The constant's individual id.</param>
    /// <returns>The grounded literal.</returns>
    private static DlLiteral GroundAtIndividual(DlLiteral literal, int individual)
    {
        DlTerm constant = DlTerm.Individual(individual);
        DlTerm first = literal.First.IsCentral ? constant : literal.First;
        DlTerm second = literal.Second.IsCentral ? constant : literal.Second;

        return literal.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(literal.Symbol, first),
            DlLiteralKind.Role => DlLiteral.Role(literal.Symbol, first, second),
            DlLiteralKind.Equality => DlLiteral.Equality(first, second),
            _ => DlLiteral.Inequality(first, second),
        };
    }

    /// <summary>Applies Join form (b) under the budget gate (both maximality conditions hold through the maximal-literal indexes): from <c>A ∧ Γ → ∆</c>, the empty-body <c>⊤ → ∆′ ∨ A′</c> with <c>A′{x↦o} = A</c>, and the empty-body <c>⊤ → ∆″ ∨ x ≈ o</c>, derive <c>Γ → ∆ ∨ ∆′ ∨ ∆″</c>; the conclusion's landed event is EAGER.</summary>
    /// <param name="context">The context.</param>
    /// <param name="premiseOneId">The ground-body premise's clause id.</param>
    /// <param name="groundLiteral">The resolved ground literal <c>A</c>.</param>
    /// <param name="abstractId">The abstract premise's clause id.</param>
    /// <param name="abstractLiteral">The abstract literal <c>A′</c>.</param>
    /// <param name="bridgeId">The <c>x ≈ o</c> premise's clause id.</param>
    /// <param name="bridge">The <c>x ≈ o</c> literal.</param>
    private void ApplyJoinBridge(Context context, int premiseOneId, DlLiteral groundLiteral, int abstractId, DlLiteral abstractLiteral, int bridgeId, DlLiteral bridge)
    {
        if(!context.IsLive(premiseOneId) || !context.IsLive(abstractId) || !context.IsLive(bridgeId))
        {
            return;
        }

        if(!TryApply())
        {
            return;
        }

        DlClause premiseOne = context.At(premiseOneId);
        ScratchBody.Clear();
        AppendResidualExcept(ScratchBody, premiseOne.Body, groundLiteral);
        ScratchHead.Clear();
        AppendSpan(ScratchHead, premiseOne.Head);
        AppendResidualExcept(ScratchHead, context.At(abstractId).Head, abstractLiteral);
        AppendResidualExcept(ScratchHead, context.At(bridgeId).Head, bridge);

        DlClause.CanonicaliseInPlace(ScratchBody);
        DlClause.CanonicaliseInPlace(ScratchHead);
        AddJoinConclusionSpans(context, CollectionsMarshal.AsSpan(ScratchBody), CollectionsMarshal.AsSpan(ScratchHead), DerivedOrigin, [premiseOneId, abstractId, bridgeId]);
    }

    /// <summary>Adds a Join conclusion held as its canonical body and head spans — the family's face, which offers a conclusion without building it — routing its landed event to the eager queue and counting the application. The sole seam every join-family conclusion reaches the clause set through, so the offer count charged here covers the whole family and stands at or above the landed count by construction, and the exact-duplicate and subsumer absorptions of the whole family are attributed here too. The offering run is charged here as well — lazily, on the run's first charged offer — so a dispatch that finds no candidate never counts as a run.</summary>
    /// <param name="context">The context.</param>
    /// <param name="body">The conclusion's canonical body span.</param>
    /// <param name="head">The conclusion's canonical head span.</param>
    /// <param name="origin">The source-axiom index a materialisation stamps.</param>
    /// <param name="premiseIds">The same-context premise ids the sink's tag inheritance reads.</param>
    private void AddJoinConclusionSpans(Context context, ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin, ReadOnlySpan<int> premiseIds)
    {
        EnqueueEagerly = true;
        JoinOffers++;
        ClauseOfferOutcome outcome = AddClauseSpans(context, body, head, origin, premiseIds);
        EnqueueEagerly = false;
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            JoinApplications++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            JoinDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            JoinSubsumedHits++;
        }

        if(!JoinRunHasOffered)
        {
            JoinOfferingRuns++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            JoinIntraRunDuplicateHits++;
        }

        JoinRunHasOffered = true;
    }

    /// <summary>
    /// The r-Succ rule (Table 3): a landed maximal literal in a non-root context
    /// that is the <c>σ = {y↦x}</c> image of a root successor trigger
    /// (<c>B(o)</c>, <c>S(x,o)</c>, <c>S(o,x)</c>, or the broadened
    /// <c>S(o,o′)</c> / <c>o ≈ o′</c>) opens the root exchange for each
    /// constant it mentions: unless the edge and seed already exist (condition
    /// 3), or the premise is BLOCKED (condition 4's (*) — the Definition-11
    /// device the label-depth termination bound leans on), add the root edge
    /// for <c>(u, o)</c> and seed the tautology <c>A → A</c> into the
    /// constant's root-class context; a genuinely new edge runs the r-Pred
    /// sweep for its constant.
    /// </summary>
    /// <param name="context">The non-root context u.</param>
    /// <param name="clause">The landed clause.</param>
    /// <param name="clauseId">The landed clause's id.</param>
    /// <param name="selectedIndex">The dispatched maximal literal's head index.</param>
    private void TryRootSucc(Context context, DlClause clause, int clauseId, int selectedIndex)
    {
        DlLiteral selected = clause.Head[selectedIndex];
        if(!TryGetRootTriggerSeed(selected, out DlLiteral seed))
        {
            return;
        }

        for(int slot = 0; slot < 2 && !BudgetExhausted; slot++)
        {
            DlTerm term = slot == 0 ? seed.First : seed.Second;
            if(!term.IsIndividual || (slot == 1 && (seed.Kind == DlLiteralKind.Concept || seed.First.Equals(seed.Second))))
            {
                continue;
            }

            //The slot's individual selects the target root-class context through the
            //resolver — the one shared root, or the individual's nominal root, whose
            //entry translation respells the seed per target (S(o1,o2) centralizes as
            //S(x,o2) in v_o1 but as S(o1,x) in v_o2) — so the seed image is realised
            //per slot, as its entry-translated literal offered through spans; a
            //clause materialises only where an arm consumes one.
            int individual = term.IndividualId;
            Context target = GetOrCreateRootFor(individual);
            DlLiteral entrySeed = Topology == RootContextTopology.PerIndividualRoots ? TranslateLiteralForEntry(seed, individual) : seed;
            ReadOnlySpan<DlLiteral> seedSpan = [entrySeed];
            if(Structure.HasRootEdge(context.Id, individual) && target.ContainsUpToRedundancy(seedSpan, seedSpan, DerivedOrigin, out _))
            {
                //The pre-check is a second absorption face of the same push
                //channel: the arriving seed is a push by definition, so its tag
                //joins the live absorber exactly as the sink's containment gate
                //would join it — an absorbed arrival must not strand the
                //absorber untagged. The join is a no-op outside its O(1)
                //condition, so the absorbed clause is built exactly where the
                //join can act on it.
                if(PushTagMachineryLive)
                {
                    JoinPushOnAbsorption(target, DlClause.FromCanonicalSpans(seedSpan, seedSpan, DerivedOrigin), true);
                }

                continue;
            }

            if(IsBlockedForRootExchange(context, clause, selected))
            {
                //The blocking device (*) reads the SOURCE context's table and is
                //target-independent (Definition 11), so both slots share blocking
                //fate — the whole-literal early return keeps that shape.
                return;
            }

            if(!TryApply())
            {
                return;
            }

            RootSuccApplications++;
            RuleApplications++;
            bool newEdge = Structure.TryAddRootEdge(context.Id, individual);
            AddPushedClause(target, DlClause.FromCanonicalSpans(seedSpan, seedSpan, DerivedOrigin), false);
            if(newEdge && !BudgetExhausted)
            {
                RootPredOverNewRootEdge(context, individual);
            }
        }
    }

    /// <summary>Maps a landed literal to the underlying root successor trigger <c>A</c> it images under <c>σ = {y↦x}</c>: <c>B(o)</c> and the broadened ground shapes are their own seeds; <c>S(x,o)</c> / <c>S(o,x)</c> seed <c>S(y,o)</c> / <c>S(o,y)</c>; anything else is no trigger.</summary>
    /// <param name="literal">The landed maximal literal.</param>
    /// <param name="seed">The underlying trigger <c>A</c>.</param>
    /// <returns><see langword="true"/> for a root successor trigger image.</returns>
    private static bool TryGetRootTriggerSeed(DlLiteral literal, out DlLiteral seed)
    {
        switch(literal.Kind)
        {
            case(DlLiteralKind.Concept) when literal.First.IsIndividual:
            {
                seed = literal;

                return true;
            }
            case(DlLiteralKind.Role) when literal.First.IsCentral && literal.Second.IsIndividual:
            {
                seed = DlLiteral.Role(literal.Symbol, DlTerm.Context, literal.Second);

                return true;
            }
            case(DlLiteralKind.Role) when literal.First.IsIndividual && literal.Second.IsCentral:
            {
                seed = DlLiteral.Role(literal.Symbol, literal.First, DlTerm.Context);

                return true;
            }
            case(DlLiteralKind.Role) when literal.First.IsIndividual && literal.Second.IsIndividual:
            {
                seed = literal;

                return true;
            }
            case(DlLiteralKind.Equality) when literal.First.IsIndividual && literal.Second.IsIndividual:
            {
                seed = literal;

                return true;
            }
            default:
            {
                seed = default;

                return false;
            }
        }
    }

    /// <summary>
    /// The blocking relation (*) (Table 3 r-Succ condition 4; Definition 11):
    /// the premise <c>Γ → ∆ ∨ Aσ</c> is blocked when some live clause of the
    /// same context has a body contained in <c>Γ</c> and a head whose every
    /// literal either lies in <c>∆</c> (the premise head minus the trigger
    /// occurrence) or is a merge equality of the shapes <c>x ≈ oᵢ</c>,
    /// <c>y ≈ oᵢ</c>, <c>x ≈ y</c> — the sole device the Theorem-4 label-depth
    /// bound leans on, so dropping it is a termination defect, not a soundness
    /// one.
    /// </summary>
    /// <param name="context">The context u.</param>
    /// <param name="premise">The premise clause.</param>
    /// <param name="trigger">The premise's trigger literal <c>Aσ</c>, excluded from <c>∆</c>.</param>
    /// <returns><see langword="true"/> when the premise is blocked.</returns>
    private static bool IsBlockedForRootExchange(Context context, DlClause premise, DlLiteral trigger)
    {
        //Only a clause carrying a merge equality in its head can satisfy the relation,
        //so the merge-head posting is a complete candidate source: the liveness filter,
        //the body-subset gate, and the head walk below are the whole-list scan's own.
        IReadOnlyList<int> candidates = context.PredecessorMergeHeadClauses;
        for(int index = 0; index < candidates.Count; index++)
        {
            int id = candidates[index];
            if(!context.IsLive(id))
            {
                continue;
            }

            DlClause candidate = context.At(id);
            if(!ClauseRedundancy.IsSubset(candidate.Body, premise.Body))
            {
                continue;
            }

            bool blocked = true;
            bool hasMergeLiteral = false;
            ReadOnlySpan<DlLiteral> head = candidate.Head;
            for(int i = 0; i < head.Length; i++)
            {
                if(Context.IsPredecessorTriggerEquality(head[i]))
                {
                    hasMergeLiteral = true;

                    continue;
                }

                if(head[i].Equals(trigger) || !HeadContains(premise.Head, head[i]))
                {
                    blocked = false;

                    break;
                }
            }

            if(blocked && hasMergeLiteral)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a canonical head span contains a literal, by value.</summary>
    /// <param name="head">The canonical head span.</param>
    /// <param name="literal">The literal.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool HeadContains(ReadOnlySpan<DlLiteral> head, DlLiteral literal)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(head[i].Equals(literal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Registers a freshly inserted root-context clause for the r-Pred sweeps when it is r-Pred-ELIGIBLE (Table 3: every head literal a <c>Prr</c> shape — a nonground trigger shape or a function-free ground shape — and every nonground body atom an <c>Sur</c> seed shape): under each constant its nonground body atoms name, and — when NO nonground body atom exists (the n-zero face) — as a context-independent broadcast whose <c>σ = {y↦x}</c> image every context receives.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="clauseId">The inserted clause's id.</param>
    /// <param name="clause">The inserted clause.</param>
    /// <param name="decidedUnderNoChoice">Whether the clause's derivation is choice-free (the origin bit): a <c>DerivedUnderChoice</c> clause carrying an EQUALITY head the shape screen would otherwise relay — in either admission arm, a ground <c>o ≈ o′</c> or a nonground trigger <c>y ≈ o</c> — is refused the r-Pred broadcast/per-constant registration and delegates named, since an equality an unrecorded drop manufactured must not seed the r-Pred sweep's identity relay.</param>
    private void RegisterRootPredEligible(Context root, int clauseId, DlClause clause, bool decidedUnderNoChoice)
    {
        //Every r-Pred read of a nominal-root clause runs on its HOME-GROUNDED image
        //(x ↦ o, f(x) ↦ f(o) at the context's home individual) — the exit
        //counterpart of the entry translation, sound by the context clause
        //⊤ → x ≈ o: eligibility, Sur-constant extraction, and the propagated
        //images are then literally the single-root forms. Under the single-root
        //topology the grounding is the identity and this path is byte-identical.
        clause = HomeGroundedView(root, clause);
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            //Prr carries no f(o) term: a ground head literal is admitted only when it is
            //function-free (B(o), S(o,o′), o ≈ o′), the shapes every ordinary context's
            //literal universe also holds. The f(o)-bearing root shapes (B(f(o)), S(o,f(o)),
            //S(f(o),o), the f(o) (in)equalities) are r-Succ witnesses the ordinary
            //function-edge machinery carries forward, never r-Pred disjuncts — broadcasting
            //one into an ordinary context would land a root-only literal outside that
            //context's grammar.
            if(MentionsFunctionOfIndividual(head[i]) || (!Context.IsGroundLiteral(head[i]) && !IsRootPredecessorTriggerShape(head[i])))
            {
                return;
            }

            //The shape screen has accepted this head literal — a function-free ground
            //literal or a nonground Prr trigger, one that would be broadcast or per-constant
            //registered. Only on such an accepted position is a choice-riding equality
            //refused r-Pred eligibility and the general relay latch armed: a ground o ≈ o′
            //via the IsGroundLiteral arm or a nonground y ≈ o via the trigger-shape arm, so
            //an equality an unrecorded drop manufactured never seeds the r-Pred identity
            //relay. The refusal sits after the screen so an f(o)-bearing equality the screen
            //rejects anyway never forces a delegation.
            if(head[i].Kind == DlLiteralKind.Equality && !decidedUnderNoChoice)
            {
                ArmRootEqualityRidesAChoice();

                return;
            }
        }

        ReadOnlySpan<DlLiteral> body = clause.Body;
        bool hasNongroundBodyAtom = false;
        for(int i = 0; i < body.Length; i++)
        {
            if(Context.IsGroundLiteral(body[i]))
            {
                continue;
            }

            if(!TryGetSurBodyIndividual(body[i], out _))
            {
                return;
            }

            hasNongroundBodyAtom = true;
        }

        if(!hasNongroundBodyAtom)
        {
            BroadcastRootPred(clause);

            return;
        }

        for(int i = 0; i < body.Length; i++)
        {
            if(!Context.IsGroundLiteral(body[i]) && TryGetSurBodyIndividual(body[i], out int individual))
            {
                if(!RootPredEligibleByIndividual.TryGetValue(individual, out List<int>? ids))
                {
                    ids = [];
                    RootPredEligibleByIndividual[individual] = ids;
                }

                if(!ids.Contains(clauseId))
                {
                    ids.Add(clauseId);
                }
            }
        }

        if(PropagationRelevance == RootPropagationRelevance.GroundFiltered)
        {
            RegisterReofferGroundConjuncts(clause.Body, new RootPredReofferEntry(RootClauseId: clauseId, BroadcastImageIndex: -1));
        }

        CurrentRootPredOrigin = RootPredOrigin.RegistrationSweep;
        AttemptRootPred(root, clauseId, restrictSourceId: -1);
    }

    /// <summary>Whether a nonground literal is a <c>Prr</c> shape — <c>B(y)</c>, <c>S(y,o)</c>, <c>S(o,y)</c>, or <c>y ≈ o</c>.</summary>
    /// <param name="literal">The nonground head literal.</param>
    /// <returns><see langword="true"/> for a root predecessor trigger.</returns>
    private static bool IsRootPredecessorTriggerShape(DlLiteral literal)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => literal.First.Kind == DlTermKind.Context,
            DlLiteralKind.Role => (literal.First.Kind == DlTermKind.Context && literal.Second.IsIndividual)
                || (literal.First.IsIndividual && literal.Second.Kind == DlTermKind.Context),
            DlLiteralKind.Equality => (literal.First.Kind == DlTermKind.Context && literal.Second.IsIndividual)
                || (literal.First.IsIndividual && literal.Second.Kind == DlTermKind.Context),
            _ => false,
        };
    }

    /// <summary>Whether a head literal mentions a depth-1 root term <c>f(o)</c> in any slot — the shape no <c>Prr</c> disjunct carries, admitted by the root grammar alone.</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> when a <c>FunctionOfIndividual</c> term occurs.</returns>
    private static bool MentionsFunctionOfIndividual(DlLiteral literal)
    {
        return literal.First.IsFunctionOfIndividual
            || (literal.Kind != DlLiteralKind.Concept && literal.Second.IsFunctionOfIndividual);
    }

    /// <summary>The constant an <c>Sur</c>-shaped nonground body atom names — <c>S(y,o)</c> / <c>S(o,y)</c> name <c>o</c>; every other nonground body literal disqualifies the clause from r-Pred.</summary>
    /// <param name="literal">The nonground body literal.</param>
    /// <param name="individual">The named constant.</param>
    /// <returns><see langword="true"/> for an <c>Sur</c> seed shape.</returns>
    private static bool TryGetSurBodyIndividual(DlLiteral literal, out int individual)
    {
        if(literal.Kind == DlLiteralKind.Role)
        {
            if(literal.First.Kind == DlTermKind.Context && literal.Second.IsIndividual)
            {
                individual = literal.Second.IndividualId;

                return true;
            }

            if(literal.First.IsIndividual && literal.Second.Kind == DlTermKind.Context)
            {
                individual = literal.First.IndividualId;

                return true;
            }
        }

        individual = -1;

        return false;
    }

    /// <summary>Broadcasts the n-zero r-Pred face: a root clause with no nonground body atoms propagates its <c>σ = {y↦x}</c> image into EVERY context — no edge constrains the target, exactly the completeness proof's "r-Pred with n = 0" step; the image joins the broadcast list so later-created contexts receive it at seeding.</summary>
    /// <param name="clause">The eligible root clause.</param>
    private void BroadcastRootPred(DlClause clause)
    {
        ScratchBody.Clear();
        ScratchHead.Clear();
        ReadOnlySpan<DlLiteral> body = clause.Body;
        for(int i = 0; i < body.Length; i++)
        {
            ScratchBody.Add(body[i]);
        }

        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            ScratchHead.Add(ApplyPredSigma(head[i], DlTerm.Central, DlTerm.Central));
        }

        DlClause broadcast = DlClause.Create(ScratchBody, ScratchHead, DerivedOrigin);
        RootBroadcastClauses.Add(broadcast);
        if(IsAnchorInvariantZeroSlotTarget(broadcast))
        {
            _ = BroadcastInvariantImageIndex.TryAdd(broadcast, RootBroadcastClauses.Count - 1);
        }

        if(PropagationRelevance == RootPropagationRelevance.GroundFiltered)
        {
            RegisterReofferGroundConjuncts(broadcast.Body, new RootPredReofferEntry(RootClauseId: -1, BroadcastImageIndex: RootBroadcastClauses.Count - 1));
        }

        for(int contextId = 0; contextId < Structure.Count && !BudgetExhausted; contextId++)
        {
            if(!Structure[contextId].IsRoot)
            {
                ApplyBroadcastRootPred(Structure[contextId], broadcast, RootBroadcastClauses.Count - 1);
            }
        }
    }

    /// <summary>The home-grounded view of a root-class clause: under the fragmented topology, the central variable and its Skolem terms ground at the context's home individual (<c>x ↦ o</c>, <c>f(x) ↦ f(o)</c> — the exit counterpart of the entry translation, sound by the context clause <c>⊤ → x ≈ o</c>), so the r-Pred machinery reads exactly the single-root clause form; under the single-root topology the clause itself, unchanged.</summary>
    /// <param name="rootClass">The root-class context the clause lives in.</param>
    /// <param name="clause">The stored clause.</param>
    /// <returns>The grounded view.</returns>
    private DlClause HomeGroundedView(Context rootClass, DlClause clause)
    {
        if(Topology == RootContextTopology.SingleRoot)
        {
            return clause;
        }

        DlTerm home = DlTerm.Individual(rootClass.HomeIndividual);
        ScratchEntryBody.Clear();
        ReadOnlySpan<DlLiteral> body = clause.Body;
        for(int i = 0; i < body.Length; i++)
        {
            ScratchEntryBody.Add(ApplyBindings(body[i], home));
        }

        ScratchEntryHead.Clear();
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            ScratchEntryHead.Add(ApplyBindings(head[i], home));
        }

        return DlClause.Create(ScratchEntryBody, ScratchEntryHead, clause.Origin);
    }

    /// <summary>Lands one broadcast image in one target context under the ground-relevance guard: a target that cannot discharge every ground body literal of the image refuses the offer at zero budget; a qualifying target lands it through the normal charged path under the broadcast origin. All three broadcast paths — the live broadcast, the seeding replay, and the filtered mode's re-offered replay — route through this site. The context's containment record is written only where the sink RESOLVED the offer as inserted or contained, never on a refusal arm: a refused target does not hold the content, so a record there would license eliding an offer that can still land.</summary>
    /// <param name="context">The target context.</param>
    /// <param name="broadcast">The broadcast image.</param>
    /// <param name="broadcastIndex">The image's position in the broadcast list.</param>
    private void ApplyBroadcastRootPred(Context context, DlClause broadcast, int broadcastIndex)
    {
        if(PropagationRelevance == RootPropagationRelevance.GroundFiltered && TryGetUnqualifiedConjunct(context, broadcast.Body, out DlLiteral unqualified))
        {
            RootPredFilteredOffers++;
            ReArmRelevanceKeys(context, unqualified);

            return;
        }

        CurrentRootPredOrigin = RootPredOrigin.Broadcast;
        ClauseOfferOutcome? outcome = ApplyRootPred(context, broadcast, []);
        if(outcome is ClauseOfferOutcome.Inserted or ClauseOfferOutcome.ExactDuplicate or ClauseOfferOutcome.Subsumed)
        {
            context.RecordBroadcastImageHeld(broadcastIndex);
        }
    }

    /// <summary>The r-Pred site-3 sweep for a newly added root edge <c>⟨u, vr, o⟩</c>: every registered eligible root clause naming <c>o</c> re-attempts its completions restricted to <c>u</c>.</summary>
    /// <param name="source">The new edge's source context u.</param>
    /// <param name="individual">The new edge's constant o.</param>
    private void RootPredOverNewRootEdge(Context source, int individual)
    {
        if(!RootPredEligibleByIndividual.TryGetValue(individual, out List<int>? ids))
        {
            return;
        }

        //The edge's individual selects the holding root-class context: the shared
        //root, or the individual's nominal root — where every registered id keyed
        //by this individual lives, since a nominal-root clause's home-grounded Sur
        //bodies name only its own home constant.
        Context root = GetOrCreateRootFor(individual);
        CurrentRootPredOrigin = RootPredOrigin.NewRootEdge;
        int count = ids.Count;
        for(int i = 0; i < count && !BudgetExhausted; i++)
        {
            if(root.IsLive(ids[i]))
            {
                AttemptRootPred(root, ids[i], source.Id);
            }
        }
    }

    /// <summary>
    /// Attempts the r-Pred rule (Table 3) for one eligible root clause: its
    /// nonground <c>Sur</c> body atoms <c>Aᵢ</c> each name a constant
    /// <c>oᵢ</c>; for every context <c>u</c> holding a root edge for EVERY
    /// <c>oᵢ</c>, each <c>Aᵢ</c> needs an unblocked premise
    /// <c>Γᵢ → ∆ᵢ ∨ Aᵢσ ∈ Su</c> (<c>σ(y) = x</c>, <c>∆ᵢ ⋡ Aᵢσ</c> through
    /// the maximal-literal index, (*) verified per premise); each completion
    /// adds <c>⋀Γᵢ ∧ ⋀Cᵢ → ⋁∆ᵢ ∨ ⋁Lᵢσ</c> to <c>Su</c> — non-local by design:
    /// the sweep runs over root edges regardless of ordinary adjacency.
    /// </summary>
    /// <param name="root">The root context.</param>
    /// <param name="rootClauseId">The eligible root clause's id.</param>
    /// <param name="restrictSourceId">A single candidate source context id, or <c>-1</c> for all edge sources.</param>
    private void AttemptRootPred(Context root, int rootClauseId, int restrictSourceId)
    {
        DlClause clause = HomeGroundedView(root, root.At(rootClauseId));
        ReadOnlySpan<DlLiteral> body = clause.Body;
        int firstIndividual = -1;
        for(int i = 0; i < body.Length && firstIndividual < 0; i++)
        {
            if(!Context.IsGroundLiteral(body[i]) && TryGetSurBodyIndividual(body[i], out int individual))
            {
                firstIndividual = individual;
            }
        }

        if(firstIndividual < 0)
        {
            return;
        }

        if(restrictSourceId >= 0)
        {
            AttemptRootPredAt(root, clause, restrictSourceId);

            return;
        }

        IReadOnlyList<int> sources = Structure.RootEdgeSources(firstIndividual);
        int sourceCount = sources.Count;
        for(int i = 0; i < sourceCount && !BudgetExhausted; i++)
        {
            AttemptRootPredAt(root, clause, sources[i]);
        }
    }

    /// <summary>Attempts every r-Pred completion of one root clause at one candidate source context: the ground-relevance filter refuses an unqualified target before any odometer is built, then verifies the edge for every named constant, builds one unblocked-premise slot per nonground body atom, and lands each odometer combination's conclusion in the source.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="clause">The eligible root clause.</param>
    /// <param name="sourceId">The candidate source context id u.</param>
    private void AttemptRootPredAt(Context root, DlClause clause, int sourceId)
    {
        Context source = Structure[sourceId];
        if(PropagationRelevance == RootPropagationRelevance.GroundFiltered && TryGetUnqualifiedConjunct(source, clause.Body, out DlLiteral unqualified))
        {
            RootPredFilteredOffers++;
            ReArmRelevanceKeys(source, unqualified);

            return;
        }

        ReadOnlySpan<DlLiteral> body = clause.Body;
        BeginJoin();
        ScratchPredSlotPositions.Clear();
        for(int position = 0; position < body.Length; position++)
        {
            if(Context.IsGroundLiteral(body[position]))
            {
                continue;
            }

            if(!TryGetSurBodyIndividual(body[position], out int individual) || !Structure.HasRootEdge(sourceId, individual))
            {
                return;
            }

            DlLiteral image = ApplyPredSigma(body[position], DlTerm.Central, DlTerm.Central);
            List<int> slot = NextSlotBuffer();
            CollectUnblockedLiveHeads(source, image, slot);
            if(slot.Count == 0)
            {
                return;
            }

            SlotBuffers.Add(slot);
            ScratchPredSlotPositions.Add(position);
        }

        if(SlotBuffers.Count == 0)
        {
            return;
        }

        ResetCursor(SlotBuffers.Count);
        while(true)
        {
            ScratchBody.Clear();
            ScratchHead.Clear();
            ReadOnlySpan<DlLiteral> head = clause.Head;
            for(int i = 0; i < head.Length; i++)
            {
                ScratchHead.Add(ApplyPredSigma(head[i], DlTerm.Central, DlTerm.Central));
            }

            ReadOnlySpan<DlLiteral> targetBody = clause.Body;
            for(int position = 0; position < targetBody.Length; position++)
            {
                if(Context.IsGroundLiteral(targetBody[position]))
                {
                    ScratchBody.Add(targetBody[position]);
                }
            }

            ScratchPremiseIds.Clear();
            for(int slot = 0; slot < SlotBuffers.Count; slot++)
            {
                int premiseId = SlotBuffers[slot][Cursor[slot]];
                ScratchPremiseIds.Add(premiseId);
                AppendSpan(ScratchBody, source.At(premiseId).Body);
                AppendResidualExcept(ScratchHead, source.At(premiseId).Head, ApplyPredSigma(targetBody[ScratchPredSlotPositions[slot]], DlTerm.Central, DlTerm.Central));
            }

            //The odometer's own assembly buffers ARE the offered conclusion, canonicalised
            //where the clause factory would have canonicalised them; the premise-id and
            //slot-position buffers index the slot lists and the root clause's own stable
            //body, never these, so the in-place sort perturbs no alignment, and the next
            //combination clears both buffers before assembling again.
            DlClause.CanonicaliseInPlace(ScratchBody);
            DlClause.CanonicaliseInPlace(ScratchHead);
            ApplyRootPredSpans(source, CollectionsMarshal.AsSpan(ScratchBody), CollectionsMarshal.AsSpan(ScratchHead), DerivedOrigin, CollectionsMarshal.AsSpan(ScratchPremiseIds));
            if(BudgetExhausted || !Advance())
            {
                return;
            }
        }
    }

    /// <summary>Copies the live UNBLOCKED clause ids whose SELECTED head literal equals the image into a slot buffer — the r-Pred premise condition pairs the maximal-literal keying with the (*) verification.</summary>
    /// <param name="source">The candidate source context.</param>
    /// <param name="image">The body atom's sigma-image.</param>
    /// <param name="slotToAppendTo">The slot buffer.</param>
    private static void CollectUnblockedLiveHeads(Context source, DlLiteral image, List<int> slotToAppendTo)
    {
        IReadOnlyList<int> heads = source.SelectedHeadClauses(image);
        for(int i = 0; i < heads.Count; i++)
        {
            if(source.IsLive(heads[i]) && !IsBlockedForRootExchange(source, source.At(heads[i]), image))
            {
                slotToAppendTo.Add(heads[i]);
            }
        }
    }

    /// <summary>Applies an r-Pred conclusion under the budget gate, counting one application per added clause and attributing it to the origin whose sweep is landing — the three swept origins and the broadcast path share this single site, so the per-origin split threads through <see cref="CurrentRootPredOrigin"/>. Reports WHICH gate resolved the offer, in the sink's own outcome vocabulary; no answer means the budget refused the offer before the sink saw it.</summary>
    /// <param name="context">The context the conclusion lands in.</param>
    /// <param name="clause">The conclusion.</param>
    /// <param name="premiseIds">The discharging same-context premise ids the sink's tag inheritance reads; empty for the premise-free broadcast image.</param>
    /// <returns>The gate that resolved the offer, or <see langword="null"/> when the budget refused it.</returns>
    private ClauseOfferOutcome? ApplyRootPred(Context context, DlClause clause, ReadOnlySpan<int> premiseIds)
    {
        if(!TryApply())
        {
            return null;
        }

        CountRootPredOffer();
        ClauseOfferOutcome outcome = AddClauseCore(context, clause, premiseIds);
        RecordRootPredOutcome(outcome);

        return outcome;
    }

    /// <summary>Applies an r-Pred conclusion held as its canonical body and head spans — the odometer's face, which offers a completion without building it. Identical to the clause face in every counted quantity: the budget gate fires at the same position, the offer and outcome attribution is the same, and the conclusion reaches the same single mutation point through its span face.</summary>
    /// <param name="context">The context the conclusion lands in.</param>
    /// <param name="body">The conclusion's canonical body span.</param>
    /// <param name="head">The conclusion's canonical head span.</param>
    /// <param name="origin">The source-axiom index a materialisation stamps.</param>
    /// <param name="premiseIds">The discharging same-context premise ids the sink's tag inheritance reads.</param>
    private void ApplyRootPredSpans(Context context, ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin, ReadOnlySpan<int> premiseIds)
    {
        if(!TryApply())
        {
            return;
        }

        CountRootPredOffer();
        RecordRootPredOutcome(AddClauseSpans(context, body, head, origin, premiseIds));
    }

    /// <summary>Charges one r-Pred OFFER to the origin whose sweep is landing — every conclusion that reaches the insertion gate, landed or not, so the offer counters read the flood the accept-keyed landing counters cannot see.</summary>
    private void CountRootPredOffer()
    {
        switch(CurrentRootPredOrigin)
        {
            case(RootPredOrigin.RegistrationSweep):
            {
                RootPredRegistrationSweepOffers++;

                break;
            }
            case(RootPredOrigin.NewRootEdge):
            {
                RootPredNewRootEdgeOffers++;

                break;
            }
            case(RootPredOrigin.Premise):
            {
                RootPredPremiseOffers++;

                break;
            }
            default:
            {
                RootPredBroadcastOffers++;

                break;
            }
        }
    }

    /// <summary>Attributes one offered r-Pred conclusion's outcome to the origin whose sweep is landing: an insertion counts the application and the origin's landing, an exact-duplicate absorption counts the origin's duplicate hit, a subsumer absorption counts the origin's subsumed hit, and every other gate counts none of them.</summary>
    /// <param name="outcome">The gate that resolved the offer.</param>
    private void RecordRootPredOutcome(ClauseOfferOutcome outcome)
    {
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            RootPredApplications++;
            RuleApplications++;
            switch(CurrentRootPredOrigin)
            {
                case(RootPredOrigin.RegistrationSweep):
                {
                    RootPredRegistrationSweepLandings++;

                    break;
                }
                case(RootPredOrigin.NewRootEdge):
                {
                    RootPredNewRootEdgeLandings++;

                    break;
                }
                case(RootPredOrigin.Premise):
                {
                    RootPredPremiseLandings++;

                    break;
                }
                default:
                {
                    RootPredBroadcastLandings++;

                    break;
                }
            }

            return;
        }

        if(outcome == ClauseOfferOutcome.Subsumed)
        {
            switch(CurrentRootPredOrigin)
            {
                case(RootPredOrigin.RegistrationSweep):
                {
                    RootPredRegistrationSweepSubsumedHits++;

                    break;
                }
                case(RootPredOrigin.NewRootEdge):
                {
                    RootPredNewRootEdgeSubsumedHits++;

                    break;
                }
                case(RootPredOrigin.Premise):
                {
                    RootPredPremiseSubsumedHits++;

                    break;
                }
                default:
                {
                    RootPredBroadcastSubsumedHits++;

                    break;
                }
            }

            return;
        }

        if(outcome != ClauseOfferOutcome.ExactDuplicate)
        {
            return;
        }

        switch(CurrentRootPredOrigin)
        {
            case(RootPredOrigin.RegistrationSweep):
            {
                RootPredRegistrationSweepDuplicateHits++;

                break;
            }
            case(RootPredOrigin.NewRootEdge):
            {
                RootPredNewRootEdgeDuplicateHits++;

                break;
            }
            case(RootPredOrigin.Premise):
            {
                RootPredPremiseDuplicateHits++;

                break;
            }
            default:
            {
                RootPredBroadcastDuplicateHits++;

                break;
            }
        }
    }

    /// <summary>The r-Pred site-2 dispatch: a landed non-root maximal literal matching an <c>Sur</c> image (<c>S(x,o)</c> / <c>S(o,x)</c>) whose context holds a root edge for <c>o</c> re-attempts the registered root clauses naming <c>o</c> at this context.</summary>
    /// <param name="context">The non-root context the premise landed in.</param>
    /// <param name="selected">The landed maximal literal.</param>
    private void RootPredFromPremise(Context context, DlLiteral selected)
    {
        if(selected.Kind != DlLiteralKind.Role)
        {
            return;
        }

        int individual;
        if(selected.First.IsCentral && selected.Second.IsIndividual)
        {
            individual = selected.Second.IndividualId;
        }
        else if(selected.First.IsIndividual && selected.Second.IsCentral)
        {
            individual = selected.First.IndividualId;
        }
        else
        {
            return;
        }

        if(!Structure.HasRootEdge(context.Id, individual) || !RootPredEligibleByIndividual.TryGetValue(individual, out List<int>? ids))
        {
            return;
        }

        Context root = GetOrCreateRootFor(individual);
        CurrentRootPredOrigin = RootPredOrigin.Premise;
        int count = ids.Count;
        for(int i = 0; i < count && !BudgetExhausted; i++)
        {
            if(root.IsLive(ids[i]))
            {
                AttemptRootPred(root, ids[i], context.Id);
            }
        }
    }

    /// <summary>Finds the first ground body literal of an r-Pred offer the target context cannot discharge — held maximal by no live clause (no Join form-(a) witness) and covered by no bridge-premise pair (no form-(b) witness). Clauses with no ground body literal always qualify at zero probe cost. Probe keys are canonicalized so equality and inequality conjuncts match the canonically-stored head index whatever orientation their producer left in the body.</summary>
    /// <param name="source">The candidate target context u.</param>
    /// <param name="body">The offered clause's body span.</param>
    /// <param name="unqualified">The first undischargeable ground conjunct, canonical, when one exists.</param>
    /// <returns><see langword="true"/> when the offer is blocked.</returns>
    private static bool TryGetUnqualifiedConjunct(Context source, ReadOnlySpan<DlLiteral> body, out DlLiteral unqualified)
    {
        for(int i = 0; i < body.Length; i++)
        {
            if(Context.IsGroundLiteral(body[i]))
            {
                DlLiteral conjunct = CanonicalRelevanceKey(body[i]);
                if(!GroundConjunctQualifies(source, conjunct))
                {
                    unqualified = conjunct;

                    return true;
                }
            }
        }

        unqualified = default;

        return false;
    }

    /// <summary>The canonical form of a relevance qualification key — a ground conjunct or a bridge-premise candidate: equality and inequality literals re-oriented to the stored canonical orientation, every other kind unchanged, so probe, storage, and re-arm agree on one form.</summary>
    /// <param name="conjunct">The literal as its producer left it.</param>
    /// <returns>The canonical key.</returns>
    private static DlLiteral CanonicalRelevanceKey(DlLiteral conjunct)
    {
        return conjunct.Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality
            ? ContextTermOrder.OrientEqualityLiteral(conjunct)
            : conjunct;
    }

    /// <summary>Re-arms the dispatched re-offer marks of one blocked conjunct's qualification keys — the conjunct itself (the family-(i) key) and its bridge-premise shapes (the family-(ii) keys) — so a qualification that died after an earlier dispatch fires again on its next live landing; without the re-arm a blocked offer in the dead window would never replay.</summary>
    /// <param name="source">The context the offer was blocked at.</param>
    /// <param name="conjunct">The undischargeable ground conjunct, canonical.</param>
    private void ReArmRelevanceKeys(Context source, DlLiteral conjunct)
    {
        if(!DispatchedRelevanceKeys.TryGetValue(source.Id, out HashSet<DlLiteral>? dispatched) || dispatched.Count == 0)
        {
            return;
        }

        dispatched.Remove(conjunct);
        if(conjunct.First.IsIndividual)
        {
            ReArmBridgeKeys(dispatched, conjunct, conjunct.First.IndividualId);
        }

        if(conjunct.Kind != DlLiteralKind.Concept && conjunct.Second.IsIndividual
            && (!conjunct.First.IsIndividual || conjunct.Second.IndividualId != conjunct.First.IndividualId))
        {
            ReArmBridgeKeys(dispatched, conjunct, conjunct.Second.IndividualId);
        }
    }

    /// <summary>Re-arms the bridge-premise keys of one conjunct over one abstracted constant: the oriented <c>x ≈ o</c> bridge and every oriented abstraction candidate.</summary>
    /// <param name="dispatched">The context's dispatched-key set.</param>
    /// <param name="conjunct">The undischargeable ground conjunct.</param>
    /// <param name="individual">The abstracted constant's individual id.</param>
    private static void ReArmBridgeKeys(HashSet<DlLiteral> dispatched, DlLiteral conjunct, int individual)
    {
        dispatched.Remove(ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individual))));
        DlTerm constant = DlTerm.Individual(individual);
        bool firstMatches = conjunct.First.Equals(constant);
        bool secondMatches = conjunct.Kind != DlLiteralKind.Concept && conjunct.Second.Equals(constant);
        if(firstMatches)
        {
            dispatched.Remove(CanonicalRelevanceKey(ReplaceSlots(conjunct, replaceFirst: true, replaceSecond: false)));
        }

        if(secondMatches)
        {
            dispatched.Remove(CanonicalRelevanceKey(ReplaceSlots(conjunct, replaceFirst: false, replaceSecond: true)));
        }

        if(firstMatches && secondMatches)
        {
            dispatched.Remove(CanonicalRelevanceKey(ReplaceSlots(conjunct, replaceFirst: true, replaceSecond: true)));
        }
    }

    /// <summary>Whether one ground conjunct <c>Cᵢ</c> is dischargeable at a target: arm (i) probes <c>Cᵢ</c> itself as a live maximal head atom (form (a) — the premise may be conditional, so no empty-body condition); arm (ii) probes the bridge-premise pair per bare-individual slot, mirroring <see cref="JoinBridgeForGroundBodyLiteral"/>'s slot gating — a <c>f(o)</c> slot yields no bridge candidate and is covered by arm (i) alone.</summary>
    /// <param name="source">The candidate target context.</param>
    /// <param name="conjunct">The ground conjunct <c>Cᵢ</c>.</param>
    /// <returns><see langword="true"/> when a discharge witness is live at the target.</returns>
    private static bool GroundConjunctQualifies(Context source, DlLiteral conjunct)
    {
        if(HasLiveSelectedHead(source, conjunct))
        {
            return true;
        }

        bool firstIndividual = conjunct.First.IsIndividual;
        if(firstIndividual && BridgePairQualifies(source, conjunct, conjunct.First.IndividualId))
        {
            return true;
        }

        return conjunct.Kind != DlLiteralKind.Concept && conjunct.Second.IsIndividual
            && (!firstIndividual || conjunct.Second.IndividualId != conjunct.First.IndividualId)
            && BridgePairQualifies(source, conjunct, conjunct.Second.IndividualId);
    }

    /// <summary>Whether the form-(b) bridge-premise pair for one conjunct and one abstracted constant is live at a target: an empty-body maximal <c>x ≈ o</c> AND an empty-body maximal abstraction candidate — the full <see cref="ReplaceSlots"/> set with equality and inequality candidates re-oriented, mirroring <see cref="JoinBridgeAbstractions"/>'s enumeration and <see cref="ApplyJoinBridge"/>'s premise qualifications exactly.</summary>
    /// <param name="source">The candidate target context.</param>
    /// <param name="conjunct">The ground conjunct <c>Cᵢ</c>.</param>
    /// <param name="individual">The abstracted constant's individual id.</param>
    /// <returns><see langword="true"/> when a candidate-pair is live.</returns>
    private static bool BridgePairQualifies(Context source, DlLiteral conjunct, int individual)
    {
        DlLiteral bridge = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individual)));
        if(!HasLiveEmptyBodySelectedHead(source, bridge))
        {
            return false;
        }

        DlTerm constant = DlTerm.Individual(individual);
        bool firstMatches = conjunct.First.Equals(constant);
        bool secondMatches = conjunct.Kind != DlLiteralKind.Concept && conjunct.Second.Equals(constant);
        Span<DlLiteral> candidates = stackalloc DlLiteral[3];
        int candidateCount = 0;
        if(firstMatches)
        {
            candidates[candidateCount++] = ReplaceSlots(conjunct, replaceFirst: true, replaceSecond: false);
        }

        if(secondMatches)
        {
            candidates[candidateCount++] = ReplaceSlots(conjunct, replaceFirst: false, replaceSecond: true);
        }

        if(firstMatches && secondMatches)
        {
            candidates[candidateCount++] = ReplaceSlots(conjunct, replaceFirst: true, replaceSecond: true);
        }

        for(int c = 0; c < candidateCount; c++)
        {
            DlLiteral abstracted = candidates[c].Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality
                ? ContextTermOrder.OrientEqualityLiteral(candidates[c])
                : candidates[c];
            if(HasLiveEmptyBodySelectedHead(source, abstracted))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a context holds a LIVE clause with the literal as a maximal head atom — the arm (i) probe over <see cref="Context.SelectedHeadClauses"/>.</summary>
    /// <param name="source">The context.</param>
    /// <param name="literal">The probed literal.</param>
    /// <returns><see langword="true"/> when a live entry exists.</returns>
    private static bool HasLiveSelectedHead(Context source, DlLiteral literal)
    {
        IReadOnlyList<int> ids = source.SelectedHeadClauses(literal);
        for(int i = 0; i < ids.Count; i++)
        {
            if(source.IsLive(ids[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a context holds a LIVE EMPTY-BODY clause with the literal as a maximal head atom — the arm (ii) premise probe, matching <see cref="ApplyJoinBridge"/>'s body-length qualification.</summary>
    /// <param name="source">The context.</param>
    /// <param name="literal">The probed literal.</param>
    /// <returns><see langword="true"/> when a live empty-body entry exists.</returns>
    private static bool HasLiveEmptyBodySelectedHead(Context source, DlLiteral literal)
    {
        IReadOnlyList<int> ids = source.SelectedHeadClauses(literal);
        for(int i = 0; i < ids.Count; i++)
        {
            if(source.IsLive(ids[i]) && source.At(ids[i]).BodyLength == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Records the ground-conjunct re-offer entries of one eligible root clause or broadcast image: each ground body literal keys the carrier, and a key's first creation registers its bare-individual slots for the bridge-premise re-offer enumeration.</summary>
    /// <param name="body">The carrier's body span.</param>
    /// <param name="entry">The carrier — a swept root clause id or a broadcast image reference.</param>
    private void RegisterReofferGroundConjuncts(ReadOnlySpan<DlLiteral> body, RootPredReofferEntry entry)
    {
        for(int i = 0; i < body.Length; i++)
        {
            if(!Context.IsGroundLiteral(body[i]))
            {
                continue;
            }

            DlLiteral conjunct = CanonicalRelevanceKey(body[i]);
            if(!RootPredEligibleByGroundConjunct.TryGetValue(conjunct, out List<RootPredReofferEntry>? entries))
            {
                entries = [];
                RootPredEligibleByGroundConjunct[conjunct] = entries;
                RegisterReofferConjunctIndividual(conjunct, conjunct.First);
                if(conjunct.Kind != DlLiteralKind.Concept && !conjunct.Second.Equals(conjunct.First))
                {
                    RegisterReofferConjunctIndividual(conjunct, conjunct.Second);
                }
            }

            if(!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }
    }

    /// <summary>Registers a freshly created ground-conjunct key under one slot's individual when the slot is a bare individual — a <c>f(o)</c> slot yields no bridge candidate and is never indexed for the bridge-premise re-offer.</summary>
    /// <param name="conjunct">The ground-conjunct key.</param>
    /// <param name="slot">The slot term.</param>
    private void RegisterReofferConjunctIndividual(DlLiteral conjunct, DlTerm slot)
    {
        if(!slot.IsIndividual)
        {
            return;
        }

        if(!RootPredGroundConjunctsByIndividual.TryGetValue(slot.IndividualId, out List<DlLiteral>? keys))
        {
            keys = [];
            RootPredGroundConjunctsByIndividual[slot.IndividualId] = keys;
        }

        keys.Add(conjunct);
    }

    /// <summary>The relevance dispatch at the selection walk, one maximal literal of one landed LIVE non-root clause: the downward compensation floods a ground selection into the ordinary successors, and the re-offer triggers replay blocked r-Pred offers whose qualification this landing carries — family (i) on a ground head, family (ii) on an empty-body bridge premise (the <c>x ≈ o</c> equality or a Central-slotted abstraction-candidate shape; a Skolem-term slot never forms a candidate, so it never dispatches). The dispatched-key mark bounds each qualification to one firing per live epoch — the processing clause itself is the live witness, so a credited-then-tombstoned holder costs nothing (the next processed live holder fires instead). Runs only under the filtered mode.</summary>
    /// <param name="context">The non-root context the clause landed in.</param>
    /// <param name="selected">The dispatched maximal head literal.</param>
    /// <param name="emptyBody">Whether the landed clause has an empty body — the family-(ii) premise qualification.</param>
    private void RelevanceDispatch(Context context, DlLiteral selected, bool emptyBody)
    {
        if(Context.IsGroundLiteral(selected) && MentionsIndividual(selected))
        {
            SeedRelevanceTautologies(context, selected);
            if(!BudgetExhausted && TryMarkRelevanceKey(context, selected) && RootPredEligibleByGroundConjunct.TryGetValue(selected, out List<RootPredReofferEntry>? groundEntries))
            {
                ReofferEntries(context, groundEntries);
            }

            return;
        }

        if(BudgetExhausted || !emptyBody)
        {
            return;
        }

        if(IsCentralIndividualEquality(selected))
        {
            if(TryMarkRelevanceKey(context, selected))
            {
                ReofferForBridgeEquality(context, selected);
            }
        }
        else if((selected.First.IsCentral || (selected.Kind != DlLiteralKind.Concept && selected.Second.IsCentral)) && TryMarkRelevanceKey(context, selected))
        {
            ReofferForAbstractCandidate(context, selected);
        }
    }

    /// <summary>Marks a qualification key dispatched in its context, reporting whether this landing is the key's first firing of the current live epoch (<see cref="ReArmRelevanceKeys"/> opens the next epoch when an offer blocks on the dead key).</summary>
    /// <param name="context">The context the qualification landed in.</param>
    /// <param name="key">The qualification key — the canonical maximal head literal.</param>
    /// <returns><see langword="true"/> when the key was not yet dispatched.</returns>
    private bool TryMarkRelevanceKey(Context context, DlLiteral key)
    {
        if(!DispatchedRelevanceKeys.TryGetValue(context.Id, out HashSet<DlLiteral>? dispatched))
        {
            dispatched = [];
            DispatchedRelevanceKeys[context.Id] = dispatched;
        }

        return dispatched.Add(key);
    }

    /// <summary>The downward compensation (R1b): floods a ground selection <c>A</c> as the tautology <c>A → A</c> into every ordinary successor of the selecting context, once per (context, atom) — the seeded tautology's own ground head re-triggers in the successor, so the flood is transitive; a later-created outgoing edge is covered by <see cref="SeedRelevanceOverNewEdge"/> off the same source set. Emission rides the standard <see cref="AddClause"/> path behind the containment pre-check, mirroring r-Succ's tautology seed.</summary>
    /// <param name="context">The context whose selection triggered the flood.</param>
    /// <param name="selected">The ground selected literal <c>A</c>.</param>
    private void SeedRelevanceTautologies(Context context, DlLiteral selected)
    {
        if(!RelevanceSeededGroundAtoms.TryGetValue(context.Id, out HashSet<DlLiteral>? seeded))
        {
            seeded = [];
            RelevanceSeededGroundAtoms[context.Id] = seeded;
        }

        if(!seeded.Add(selected))
        {
            return;
        }

        IReadOnlyList<ContextEdge> outgoing = Structure.Outgoing(context.Id);
        if(outgoing.Count == 0)
        {
            return;
        }

        DlClause seed = DlClause.Create([selected], [selected], DerivedOrigin);
        for(int i = 0; i < outgoing.Count && !BudgetExhausted; i++)
        {
            SeedRelevanceTautology(Structure[outgoing[i].Target], seed);
        }
    }

    /// <summary>Floods every ground atom already selected in the predecessor over one newly created ordinary edge — without this sweep the transitive downward reach of the compensation would depend on edge-creation order.</summary>
    /// <param name="predecessor">The new edge's source context.</param>
    /// <param name="successor">The new edge's target context.</param>
    private void SeedRelevanceOverNewEdge(Context predecessor, Context successor)
    {
        if(!RelevanceSeededGroundAtoms.TryGetValue(predecessor.Id, out HashSet<DlLiteral>? seeded))
        {
            return;
        }

        //Each atom's tautology is offered as spans, so a seed the successor already
        //contains — the common case on a re-flooded edge — costs no clause at all.
        Span<DlLiteral> seed = stackalloc DlLiteral[1];
        foreach(DlLiteral atom in seeded)
        {
            if(BudgetExhausted)
            {
                return;
            }

            seed[0] = atom;
            SeedRelevanceTautology(successor, seed);
        }
    }

    /// <summary>Seeds one relevance tautology into one successor under the containment pre-check and the budget gate, counting the insertion. The clause face, taking the amortised seed a per-(context, atom) flood builds once and reuses across the whole successor fan-out.</summary>
    /// <param name="successor">The successor context.</param>
    /// <param name="seed">The tautology clause <c>A → A</c>.</param>
    private void SeedRelevanceTautology(Context successor, DlClause seed)
    {
        if(successor.ContainsUpToRedundancy(seed))
        {
            return;
        }

        if(!TryApply())
        {
            return;
        }

        SidecarSeedOffers++;
        ClauseOfferOutcome outcome = AddClauseCore(successor, seed, []);
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            RelevanceTautologiesSeeded++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            SidecarSeedDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            SidecarSeedSubsumedHits++;
        }
    }

    /// <summary>Seeds one relevance tautology held as its single ground atom, probing and offering on spans so a seed the successor already contains is answered without building a clause; the counted quantities are the clause face's exactly.</summary>
    /// <param name="successor">The successor context.</param>
    /// <param name="seed">The one-literal canonical span standing as the tautology's body and head.</param>
    private void SeedRelevanceTautology(Context successor, ReadOnlySpan<DlLiteral> seed)
    {
        if(successor.ContainsUpToRedundancy(seed, seed, DerivedOrigin, out _))
        {
            return;
        }

        if(!TryApply())
        {
            return;
        }

        SidecarSeedOffers++;
        ClauseOfferOutcome outcome = AddClauseSpans(successor, seed, seed, DerivedOrigin, []);
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            RelevanceTautologiesSeeded++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            SidecarSeedDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            SidecarSeedSubsumedHits++;
        }
    }

    /// <summary>The family-(ii) re-offer for a newly live empty-body <c>x ≈ o</c> bridge premise: every ground-conjunct key mentioning <c>o</c> in a bare-individual slot is re-offered at this context — the filter at the offer re-checks the completed pair exactly, so a key whose abstraction half is still absent is refused again at zero budget.</summary>
    /// <param name="context">The context the bridge premise landed in.</param>
    /// <param name="equality">The landed <c>x ≈ o</c> literal.</param>
    private void ReofferForBridgeEquality(Context context, DlLiteral equality)
    {
        int individual = equality.First.IsIndividual ? equality.First.IndividualId : equality.Second.IndividualId;
        if(!RootPredGroundConjunctsByIndividual.TryGetValue(individual, out List<DlLiteral>? keys))
        {
            return;
        }

        int count = keys.Count;
        for(int i = 0; i < count && !BudgetExhausted; i++)
        {
            if(RootPredEligibleByGroundConjunct.TryGetValue(keys[i], out List<RootPredReofferEntry>? entries))
            {
                ReofferEntries(context, entries);
            }
        }
    }

    /// <summary>The family-(ii) re-offer for a newly live empty-body abstraction candidate: for each constant with a live empty-body <c>x ≈ o</c> bridge here, the candidate's grounded image keys the re-offered carriers — mirroring <see cref="JoinBridgeFromAbstract"/>'s per-constant enumeration; an image outside the index (a nonground image or a never-registered conjunct) re-offers nothing.</summary>
    /// <param name="context">The context the candidate landed in.</param>
    /// <param name="abstractLiteral">The landed abstraction-candidate literal.</param>
    private void ReofferForAbstractCandidate(Context context, DlLiteral abstractLiteral)
    {
        for(int individual = 0; individual < Symbols.IndividualCount && !BudgetExhausted; individual++)
        {
            DlLiteral bridge = ContextTermOrder.OrientEqualityLiteral(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individual)));
            if(!HasLiveEmptyBodySelectedHead(context, bridge))
            {
                continue;
            }

            DlLiteral image = GroundAtIndividual(abstractLiteral, individual);
            if(image.Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality)
            {
                image = ContextTermOrder.OrientEqualityLiteral(image);
            }

            if(RootPredEligibleByGroundConjunct.TryGetValue(image, out List<RootPredReofferEntry>? entries))
            {
                ReofferEntries(context, entries);
            }
        }
    }

    /// <summary>Replays the carriers of one qualification key at one context: a live swept root clause re-attempts restricted to the context under the landed-premise origin; a broadcast entry replays its stored image through the guarded broadcast site. Each replay counts one re-offer and rides the normal charged attempt path.</summary>
    /// <param name="context">The context whose qualification turned live.</param>
    /// <param name="entries">The key's carrier entries.</param>
    private void ReofferEntries(Context context, List<RootPredReofferEntry> entries)
    {
        //The re-offer machinery runs only under the ground-filtered mode, whose
        //composition with the fragmented topology is rejected at Create, so the
        //scalar fetch here IS the single-root resolver arm.
        Context root = Structure[Structure.RootContextId];
        int count = entries.Count;
        for(int i = 0; i < count && !BudgetExhausted; i++)
        {
            RootPredReofferEntry entry = entries[i];
            if(entry.RootClauseId >= 0)
            {
                if(root.IsLive(entry.RootClauseId))
                {
                    RootPredReofferedByGroundHead++;
                    CurrentRootPredOrigin = RootPredOrigin.Premise;
                    AttemptRootPred(root, entry.RootClauseId, context.Id);
                }
            }
            else
            {
                RootPredReofferedByGroundHead++;
                ApplyBroadcastRootPred(context, RootBroadcastClauses[entry.BroadcastImageIndex], entry.BroadcastImageIndex);
            }
        }
    }

    /// <summary>
    /// The Nom rule's dispatch (Table 3): a landed root maximal literal
    /// matching a body position of a Nom-eligible ontology clause (heads
    /// entirely a-equalities) drives an anchored join at <c>σ(x) = o</c>; each
    /// consistent completion whose head image carries at least one literal of
    /// the form <c>y ≈ y</c> or <c>y ≈ f(o)</c> mints the K generated-nominal
    /// siblings for <c>(o, S)</c> — <c>S</c> the role of the y-binding body
    /// atom — through the memoized bounded channel and concludes
    /// <c>Γ → ∆ ∨ ⋁ y ≈ o_{ρ·S^i}</c>. The rule cannot fire unless the module
    /// carries inverse roles, nominals, and number restrictions together.
    /// </summary>
    /// <param name="root">The root context.</param>
    /// <param name="given">The landed clause.</param>
    /// <param name="givenClauseId">The landed clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The dispatched maximal literal's head index.</param>
    private void NomFromGiven(Context root, DlClause given, int givenClauseId, int givenSelectedIndex)
    {
        if(NomEligibleOntologyClauses.Count == 0)
        {
            return;
        }

        DlLiteral head = given.Head[givenSelectedIndex];
        if(!head.IsAtom)
        {
            return;
        }

        //A nominal-root context under the fragmented topology anchors the Nom join
        //at its own individual through the CENTRAL spelling: the entry translation
        //respells the anchor's premises B1(x)/counting(x, y), so the join anchors
        //on x (which denotes the home constant) beside the constant-anchored
        //dispatch for foreign-ground premises. Central heads cannot land in the
        //single-root grammar, so the added dispatch is dark there.
        bool centralAnchored = Topology == RootContextTopology.PerIndividualRoots;
        if(head.Kind == DlLiteralKind.Concept)
        {
            if((head.First.IsIndividual || (centralAnchored && head.First.IsCentral)) && OntologyConceptBody.TryGetValue(head.Symbol, out List<(int ClauseIndex, int Position)>? conceptSites))
            {
                for(int i = 0; i < conceptSites.Count && !BudgetExhausted; i++)
                {
                    (int clauseIndex, int position) = conceptSites[i];
                    if(NomEligibleByClause[clauseIndex])
                    {
                        NomJoin(root, given, givenClauseId, givenSelectedIndex, clauseIndex, position, head.First, givenNeighbourIndex: -1, givenBinding: default);
                    }
                }
            }

            return;
        }

        if(head.First.IsIndividual)
        {
            NomFromGivenRole(root, given, givenClauseId, givenSelectedIndex, head, head.First, centralFirst: true, head.Second);
        }

        if(!BudgetExhausted && head.Second.IsIndividual)
        {
            NomFromGivenRole(root, given, givenClauseId, givenSelectedIndex, head, head.Second, centralFirst: false, head.First);
        }

        if(centralAnchored && !BudgetExhausted && (head.First.IsCentral || head.Second.IsCentral))
        {
            bool centralFirst = head.First.IsCentral;
            NomFromGivenRole(root, given, givenClauseId, givenSelectedIndex, head, DlTerm.Central, centralFirst, centralFirst ? head.Second : head.First);
        }
    }

    /// <summary>Dispatches the Nom joins for one anchoring of the given role literal over the Nom-eligible ontology clauses.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="given">The landed clause.</param>
    /// <param name="givenClauseId">The landed clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The dispatched literal's head index.</param>
    /// <param name="head">The given role literal.</param>
    /// <param name="anchor">The anchoring constant.</param>
    /// <param name="centralFirst">Whether the anchor occupies the first argument.</param>
    /// <param name="binding">The neighbour binding read off the other argument.</param>
    private void NomFromGivenRole(Context root, DlClause given, int givenClauseId, int givenSelectedIndex, DlLiteral head, DlTerm anchor, bool centralFirst, DlTerm binding)
    {
        if(!OntologyRoleBody.TryGetValue((head.Symbol, centralFirst), out List<(int ClauseIndex, int Position)>? roleSites))
        {
            return;
        }

        for(int i = 0; i < roleSites.Count && !BudgetExhausted; i++)
        {
            (int clauseIndex, int position) = roleSites[i];
            if(!NomEligibleByClause[clauseIndex])
            {
                continue;
            }

            DlLiteral bodyAtom = NonEmptyOntologyClauses[clauseIndex].Body[position];
            int neighbour = bodyAtom.First.IsCentral ? bodyAtom.Second.Index : bodyAtom.First.Index;
            NomJoin(root, given, givenClauseId, givenSelectedIndex, clauseIndex, position, anchor, neighbour, binding);
        }
    }

    /// <summary>Completes one Nom join: the anchored odometer over the eligible clause's remaining body positions, then per consistent combination the head-image partition, the memoized mint, and the conclusion.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="given">The given clause.</param>
    /// <param name="givenClauseId">The given clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The given clause's dispatched literal index.</param>
    /// <param name="clauseIndex">The Nom-eligible ontology clause's index.</param>
    /// <param name="givenPosition">The body position the given clause resolves.</param>
    /// <param name="anchor">The anchoring constant <c>σ(x) = o</c>.</param>
    /// <param name="givenNeighbourIndex">The neighbour the given match bound, or <c>-1</c>.</param>
    /// <param name="givenBinding">The given match's binding.</param>
    private void NomJoin(Context root, DlClause given, int givenClauseId, int givenSelectedIndex, int clauseIndex, int givenPosition, DlTerm anchor, int givenNeighbourIndex, DlTerm givenBinding)
    {
        DlClause ontology = NonEmptyOntologyClauses[clauseIndex];
        ReadOnlySpan<DlLiteral> body = ontology.Body;
        BeginJoin();
        for(int position = 0; position < body.Length; position++)
        {
            if(position == givenPosition)
            {
                continue;
            }

            DlLiteral atom = body[position];
            List<int> slot = NextSlotBuffer();
            int slotNeighbour = -1;
            bool slotCentralFirst = false;
            DlLiteral slotExactAtom = default;
            int slotRoleSymbol = -1;
            if(atom.Kind == DlLiteralKind.Concept)
            {
                slotExactAtom = ApplyHyperSigma(atom, anchor, givenNeighbourIndex, givenBinding);
                CollectLiveHeads(root, slotExactAtom, slot);
            }
            else
            {
                bool centralFirst = atom.First.IsCentral;
                int neighbour = centralFirst ? atom.Second.Index : atom.First.Index;
                if(neighbour == givenNeighbourIndex)
                {
                    slotExactAtom = ApplyHyperSigma(atom, anchor, neighbour, givenBinding);
                    CollectLiveHeads(root, slotExactAtom, slot);
                }
                else
                {
                    slotNeighbour = neighbour;
                    slotCentralFirst = centralFirst;
                    slotRoleSymbol = atom.Symbol;
                    if(anchor.IsCentral)
                    {
                        CollectLiveRoleHeads(root, atom.Symbol, centralFirst, slot);
                    }
                    else
                    {
                        CollectLiveIds(root, root.GroundRoleHeads(atom.Symbol, anchor, centralFirst), slot);
                    }
                }
            }

            if(slot.Count == 0)
            {
                return;
            }

            SlotBuffers.Add(slot);
            SlotNeighbours.Add(slotNeighbour);
            SlotCentralFirst.Add(slotCentralFirst);
            SlotExactAtoms.Add(slotExactAtom);
            SlotRoleSymbols.Add(slotRoleSymbol);
        }

        ResetCursor(SlotBuffers.Count);
        while(true)
        {
            if(TryResolveBindings(root, anchor, givenNeighbourIndex, givenBinding))
            {
                ConcludeNom(root, given, givenClauseId, givenSelectedIndex, ontology, anchor);
                if(BudgetExhausted)
                {
                    return;
                }
            }

            if(!Advance())
            {
                return;
            }
        }
    }

    /// <summary>Concludes one consistent Nom combination: partitions the head images into the kept literals and the <c>y ≈ y</c> / <c>y ≈ f(o)</c> tail, requires a non-empty tail (an empty one is Hyper's job), finds the y-binding role, mints the K siblings through the bounded channel (memoized — a re-fire returns the same block; a fresh block is budget-charged per nominal and seeds each new constant into the root), and adds <c>Γ → ∆ ∨ ⋁ y ≈ o_{ρ·S^i}</c>.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="given">The given clause.</param>
    /// <param name="givenClauseId">The given clause's id — the given premise the sink's tag inheritance reads.</param>
    /// <param name="givenSelectedIndex">The given clause's dispatched literal index.</param>
    /// <param name="ontology">The Nom-eligible ontology clause.</param>
    /// <param name="anchor">The anchoring constant.</param>
    private void ConcludeNom(Context root, DlClause given, int givenClauseId, int givenSelectedIndex, DlClause ontology, DlTerm anchor)
    {
        ReadOnlySpan<DlLiteral> ontologyHead = ontology.Head;
        int specialCount = 0;
        for(int i = 0; i < ontologyHead.Length; i++)
        {
            if(IsNomTailImage(ApplyBindings(ontologyHead[i], anchor)))
            {
                specialCount++;
            }
        }

        if(specialCount == 0)
        {
            return;
        }

        int mintRole = FindYBindingRole(ontology, anchor);
        if(mintRole < 0)
        {
            return;
        }

        if(!TryApply())
        {
            return;
        }

        //The mint anchors at the context's own individual when the join anchored
        //through the central spelling — the fragmented topology's own-constant
        //dispatch — and at the anchoring constant otherwise.
        int anchorIndividual = anchor.IsIndividual ? anchor.IndividualId : root.HomeIndividual;
        bool minted = Symbols.MintGeneratedNominal(anchorIndividual, mintRole, NomSiblingCount, out int firstSibling);
        if(minted)
        {
            if(!DlTerm.FitsFunctionOfIndividual(Symbols.FunctionSymbolCount, Symbols.IndividualCount))
            {
                //The generated-nominal population outgrew the packed individual width; the
                //module delegates named rather than reason over unrepresentable terms.
                HasPackedWidthOverflow = true;

                return;
            }

            for(int k = 0; k < NomSiblingCount && !BudgetExhausted; k++)
            {
                if(TryApply())
                {
                    RuleApplications++;
                }

                //Each sibling seeds into ITS OWN root-class context, resolved per
                //sibling inside the loop: the shared root receives the per-constant
                //seeds; a nominal-root sibling context is minted lazily here, its
                //creation seeding carrying the entry-translated per-constant images.
                if(Topology == RootContextTopology.SingleRoot)
                {
                    SeedRootConstant(root, firstSibling + k);
                }
                else
                {
                    GetOrCreateRootFor(firstSibling + k);
                }
            }

            if(BudgetExhausted)
            {
                return;
            }
        }

        ScratchBody.Clear();
        AppendSpan(ScratchBody, given.Body);
        ScratchHead.Clear();
        AppendResidual(ScratchHead, given.Head, givenSelectedIndex);
        ScratchPremiseIds.Clear();
        ScratchPremiseIds.Add(givenClauseId);
        for(int slot = 0; slot < SlotBuffers.Count; slot++)
        {
            int premiseId = SlotBuffers[slot][Cursor[slot]];
            ScratchPremiseIds.Add(premiseId);
            AppendSpan(ScratchBody, root.At(premiseId).Body);
            DlLiteral fired = SlotNeighbours[slot] < 0
                ? SlotExactAtoms[slot]
                : FindGreatestSlotRoleLiteral(root, root.At(premiseId), slot, anchor);
            AppendResidualExcept(ScratchHead, root.At(premiseId).Head, fired);
        }

        for(int i = 0; i < ontologyHead.Length; i++)
        {
            DlLiteral image = ApplyBindings(ontologyHead[i], anchor);
            if(!IsNomTailImage(image))
            {
                ScratchHead.Add(image);
            }
        }

        for(int k = 0; k < NomSiblingCount; k++)
        {
            ScratchHead.Add(DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(firstSibling + k)));
        }

        NomOffers++;
        ClauseOfferOutcome outcome = AddClauseCore(root, DlClause.Create(ScratchBody, ScratchHead, DerivedOrigin), CollectionsMarshal.AsSpan(ScratchPremiseIds));
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            NomApplications++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            NomDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            NomSubsumedHits++;
        }
    }

    /// <summary>Whether a head image is a Nom tail literal — <c>y ≈ y</c> or <c>y ≈ f(o)</c> in either slot order (Table 3 condition 3). A nominal-root context spells the anchor's Skolem successors centrally, so the <c>y ≈ f(x)</c> image is the same tail in the fragmented spelling; a Function-kind side cannot arise in the single-root context, whose grammar carries no central terms.</summary>
    /// <param name="image">The instantiated head literal.</param>
    /// <returns><see langword="true"/> for a tail literal.</returns>
    private static bool IsNomTailImage(DlLiteral image)
    {
        if(image.Kind != DlLiteralKind.Equality)
        {
            return false;
        }

        bool firstContext = image.First.Kind == DlTermKind.Context;
        bool secondContext = image.Second.Kind == DlTermKind.Context;

        return (firstContext && secondContext)
            || (firstContext && (image.Second.IsFunctionOfIndividual || image.Second.Kind == DlTermKind.Function))
            || (secondContext && (image.First.IsFunctionOfIndividual || image.First.Kind == DlTermKind.Function));
    }

    /// <summary>The role labelling the minted siblings: the first ontology body role atom whose neighbour binds <c>y</c> in the current combination (the soundness proof's <c>S(o_ρ, y) = Aᵢ</c> premise); <c>-1</c> when none binds <c>y</c> (no tail literal can then exist either).</summary>
    /// <param name="ontology">The Nom-eligible ontology clause.</param>
    /// <param name="anchor">The anchoring constant.</param>
    /// <returns>The directioned role id, or <c>-1</c>.</returns>
    private int FindYBindingRole(DlClause ontology, DlTerm anchor)
    {
        ReadOnlySpan<DlLiteral> body = ontology.Body;
        for(int position = 0; position < body.Length; position++)
        {
            DlLiteral atom = body[position];
            if(atom.Kind != DlLiteralKind.Role)
            {
                continue;
            }

            int neighbour = atom.First.IsCentral ? atom.Second.Index : atom.First.Index;
            if(ScratchBindings.TryGetValue(neighbour, out DlTerm binding) && binding.Kind == DlTermKind.Context)
            {
                return atom.Symbol;
            }
        }

        return -1;
    }

    /// <summary>Applies a Core seed under the budget gate, counting one Core application per added clause and attributing the offer and its exact-duplicate absorption to the Core channel.</summary>
    /// <param name="context">The context.</param>
    /// <param name="clause">The seed clause.</param>
    /// <param name="premiseIds">The same-context premise ids the sink's tag inheritance reads; empty for a seed.</param>
    private void ApplyCore(Context context, DlClause clause, ReadOnlySpan<int> premiseIds)
    {
        if(!TryApply())
        {
            return;
        }

        CoreOffers++;
        ClauseOfferOutcome outcome = AddClauseCore(context, clause, premiseIds);
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            CoreApplications++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            CoreDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            CoreSubsumedHits++;
        }
    }

    /// <summary>Applies a Hyper conclusion under the budget gate, counting one Hyper application per added clause and attributing the offer and its exact-duplicate absorption to the Hyper channel.</summary>
    /// <param name="context">The context.</param>
    /// <param name="clause">The conclusion.</param>
    /// <param name="premiseIds">The same-context premise ids the sink's tag inheritance reads; empty for a seeding firing.</param>
    private void ApplyHyper(Context context, DlClause clause, ReadOnlySpan<int> premiseIds)
    {
        if(!TryApply())
        {
            return;
        }

        HyperOffers++;
        ClauseOfferOutcome outcome = AddClauseCore(context, clause, premiseIds);
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            HyperApplications++;
            RuleApplications++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            HyperDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            HyperSubsumedHits++;
        }
    }

    /// <summary>Applies a Pred conclusion held as its canonical body and head spans — the odometer's face, which offers a completion without building it. The budget gate fires ahead of the offer, the offer and its outcome are attributed to the Pred channel and to the driver whose attempts are landing, and the conclusion reaches the same single mutation point through its span face.</summary>
    /// <param name="context">The context the conclusion lands in.</param>
    /// <param name="body">The conclusion's canonical body span.</param>
    /// <param name="head">The conclusion's canonical head span.</param>
    /// <param name="origin">The source-axiom index a materialisation stamps.</param>
    /// <param name="premiseIds">The discharging predecessor-side premise ids — same-context in the landing predecessor — the sink's tag inheritance reads.</param>
    private void ApplyPredSpans(Context context, ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin, ReadOnlySpan<int> premiseIds)
    {
        if(!TryApply())
        {
            return;
        }

        CountPredOffer();
        ClauseOfferOutcome outcome = AddClauseSpans(context, body, head, origin, premiseIds);
        RecordPredOutcome(outcome);
        if(outcome == ClauseOfferOutcome.ExactDuplicate && PredRunHasOffered)
        {
            PredIntraRunDuplicateHits++;
        }

        PredRunHasOffered = true;
        Recognizers.PredOfferProbe?.Invoke(context, premiseIds, outcome);
    }

    /// <summary>Charges one Pred OFFER to the driver whose attempts are landing — every conclusion that reaches the insertion gate, landed or not, so the driver counters partition the channel's own offer flood.</summary>
    private void CountPredOffer()
    {
        PredOffers++;
        switch(CurrentPredOrigin)
        {
            case(PredOrigin.LandedTarget):
            {
                PredLandedTargetOffers++;

                break;
            }
            case(PredOrigin.LandedPremise):
            {
                PredLandedPremiseOffers++;

                break;
            }
            default:
            {
                PredNewEdgeOffers++;

                break;
            }
        }
    }

    /// <summary>Attributes one offered Pred conclusion's outcome to the driver whose attempts are landing: an insertion counts the Pred application and the driver's landing, an exact-duplicate absorption counts the channel's duplicate hit and the driver's share of it, a subsumer absorption counts the channel's subsumed hit and the driver's share of it, and every other gate counts none of them.</summary>
    /// <param name="outcome">The gate that resolved the offer.</param>
    private void RecordPredOutcome(ClauseOfferOutcome outcome)
    {
        if(outcome == ClauseOfferOutcome.Inserted)
        {
            PredApplications++;
            RuleApplications++;
            switch(CurrentPredOrigin)
            {
                case(PredOrigin.LandedTarget):
                {
                    PredLandedTargetLandings++;

                    break;
                }
                case(PredOrigin.LandedPremise):
                {
                    PredLandedPremiseLandings++;

                    break;
                }
                default:
                {
                    PredNewEdgeLandings++;

                    break;
                }
            }

            return;
        }

        if(outcome == ClauseOfferOutcome.Subsumed)
        {
            PredSubsumedHits++;
            switch(CurrentPredOrigin)
            {
                case(PredOrigin.LandedTarget):
                {
                    PredLandedTargetSubsumedHits++;

                    break;
                }
                case(PredOrigin.LandedPremise):
                {
                    PredLandedPremiseSubsumedHits++;

                    break;
                }
                default:
                {
                    PredNewEdgeSubsumedHits++;

                    break;
                }
            }

            return;
        }

        if(outcome != ClauseOfferOutcome.ExactDuplicate)
        {
            return;
        }

        PredDuplicateHits++;
        switch(CurrentPredOrigin)
        {
            case(PredOrigin.LandedTarget):
            {
                PredLandedTargetDuplicateHits++;

                break;
            }
            case(PredOrigin.LandedPremise):
            {
                PredLandedPremiseDuplicateHits++;

                break;
            }
            default:
            {
                PredNewEdgeDuplicateHits++;

                break;
            }
        }
    }

    /// <summary>
    /// The single mutation point of a context's clause set AND the license
    /// scope's premise-carrying conclusion sink: the head normalization
    /// (orientation of every equality and inequality literal, tautology-clause
    /// drop, false-disjunct Ineq), the per-literal grammar
    /// invariant, the forward containment guard (every rule's <c>∉̂</c>
    /// precondition, fronted by the exact-duplicate set), the backward Elim
    /// sweep (each strictly subsumed clause tombstoned and counted), the
    /// selection of the maximal head literal, the indexed insertion, and the
    /// worklist event. The caller counts the deriving rule when the clause was
    /// added. Every rule conclusion hands its same-context premise ids here, and
    /// the sink alone computes the push-provenance tag — pushed iff the arrival
    /// rides a push landing or any premise is pushed — stores it at insertion,
    /// and on a redundancy absorption joins it onto the surviving absorber; no
    /// call site carries tag logic, and the whole computation is dark outside
    /// the license-scoped fragmented cell.
    /// </summary>
    /// <param name="context">The context to mutate.</param>
    /// <param name="clause">The clause to add.</param>
    /// <param name="premiseIds">The same-context premise clause ids the conclusion derives from; empty for a seed, a cross-context arrival, or a conclusion landing where no premise tag can flow.</param>
    /// <returns><see langword="true"/> when the clause was inserted, <see langword="false"/> when it was contained up to redundancy or dropped as a tautology.</returns>
    private bool AddClause(Context context, DlClause clause, ReadOnlySpan<int> premiseIds)
    {
        return AddClauseCore(context, clause, premiseIds) == ClauseOfferOutcome.Inserted;
    }

    /// <summary>The clause-taking face of the single mutation point, reporting WHICH gate resolved the offer — the face a call site reads when it attributes its own offers by outcome. The boolean face is this method's <see cref="ClauseOfferOutcome.Inserted"/> test, so the two faces run one pipeline.</summary>
    /// <param name="context">The context to mutate.</param>
    /// <param name="clause">The clause to add.</param>
    /// <param name="premiseIds">The same-context premise clause ids the conclusion derives from; empty for a seed, a cross-context arrival, or a conclusion landing where no premise tag can flow.</param>
    /// <returns>The gate that resolved the offer.</returns>
    private ClauseOfferOutcome AddClauseCore(Context context, DlClause clause, ReadOnlySpan<int> premiseIds)
    {
        FoldArrivalProvenance(context, premiseIds, out bool pushed, out bool derived);
        bool decidedUnderNoChoice = !derived;

        if(context.HomeIndividual >= 0 && MentionsOwnConstant(clause, context.HomeIndividual))
        {
            //The D2 entry translation at the single mutation point: EVERY clause
            //entering a nominal-root context respells its own constant central —
            //cross-context arrivals and intra-context conclusions alike, since the
            //global ontology clauses mention the constant itself (a HasValue edge,
            //a singleton-class fact) and every rule firing them inside v_o would
            //otherwise reintroduce the own-constant spelling the topology retired.
            //Sound by the context clause ⊤ → x ≈ o; the single-root context has no
            //home individual, so the gate is dark there.
            clause = TranslateForEntry(clause, context.HomeIndividual);
        }

        if(!TryNormalizeHead(clause, out clause))
        {
            TautologyDrops++;

            return ClauseOfferOutcome.TautologyDropped;
        }

        ContextGrammarKind grammarKind = GrammarKindOf(context);
        if(!HeadInContextGrammar(clause.Head, grammarKind))
        {
            //The promoted in-saturation grammar guard: a derived head shape outside the
            //context-kind's literal universe latches a named delegation instead of being
            //trusted into the structure — always sound, and every shape a rule can
            //legally derive is admitted by the grammar, so a latch marks drift.
            OutOfGrammarConclusions++;
            HasOutOfGrammarDerivation = true;
            OutOfGrammarSample ??= RenderOutOfGrammarSample(context, clause, grammarKind);

            return ClauseOfferOutcome.OutOfGrammar;
        }

        if(ConditionalityLintArmed)
        {
            //The conditionality-loss lint observes every in-grammar derived step before the
            //redundancy check, so a step whose head is later absorbed is still counted. Dark
            //and zero-cost when unarmed; a ground-truth-free mechanism census, never a gate.
            LintConditionalityLoss(context, clause, premiseIds);
        }

        if(context.ContainsUpToRedundancy(clause, out bool exactDuplicate))
        {
            RedundantConclusions++;
            if(exactDuplicate)
            {
                DuplicateContainmentHits++;
            }
            else
            {
                SubsumedContainmentHits++;
            }

            JoinPushOnAbsorption(context, clause, pushed);
            JoinOriginOnAbsorption(context, clause, decidedUnderNoChoice);

            return exactDuplicate ? ClauseOfferOutcome.ExactDuplicate : ClauseOfferOutcome.Subsumed;
        }

        InsertSurvivor(context, clause, grammarKind, pushed, derived, decidedUnderNoChoice);

        return ClauseOfferOutcome.Inserted;
    }

    /// <summary>
    /// The SPAN face of the single mutation point: a conclusion offered as its
    /// canonical body and head spans, so a candidate absorbed at any gate is never
    /// built into a clause at all. The stages run in the clause face's order — the
    /// entry-translation gate, head normalization, the head-grammar invariant, the
    /// conditionality lint, then the containment guard — and the shared survivor
    /// tail is the clause face's own, so the two faces are one pipeline. A
    /// <see cref="DlClause"/> materialises on exactly three occasions: on survival,
    /// on the one-time out-of-grammar sample render, and under the two O(1)
    /// absorption-topology guards, whose absorbing clause is content-equal to the
    /// clause face's and therefore reaches the same absorber.
    /// </summary>
    /// <param name="context">The context to mutate.</param>
    /// <param name="body">The conclusion's canonical body span.</param>
    /// <param name="head">The conclusion's canonical head span.</param>
    /// <param name="origin">The source-axiom index a materialisation stamps.</param>
    /// <param name="premiseIds">The same-context premise clause ids the conclusion derives from.</param>
    /// <returns>The gate that resolved the offer.</returns>
    private ClauseOfferOutcome AddClauseSpans(Context context, ReadOnlySpan<DlLiteral> body, ReadOnlySpan<DlLiteral> head, int origin, ReadOnlySpan<int> premiseIds)
    {
        FoldArrivalProvenance(context, premiseIds, out bool pushed, out bool derived);
        bool decidedUnderNoChoice = !derived;

        if(context.HomeIndividual >= 0 && (SpanMentionsOwnConstant(body, context.HomeIndividual) || SpanMentionsOwnConstant(head, context.HomeIndividual)))
        {
            TranslateSpansForEntry(body, head, context.HomeIndividual);
            body = CollectionsMarshal.AsSpan(SpanTranslateBody);
            head = CollectionsMarshal.AsSpan(SpanTranslateHead);
        }

        if(!TryNormalizeHeadSpan(head, out bool rewritten))
        {
            TautologyDrops++;

            return ClauseOfferOutcome.TautologyDropped;
        }

        if(rewritten)
        {
            head = CollectionsMarshal.AsSpan(SpanNormalizeHead);
        }

        ContextGrammarKind grammarKind = GrammarKindOf(context);
        if(!HeadInContextGrammar(head, grammarKind))
        {
            OutOfGrammarConclusions++;
            HasOutOfGrammarDerivation = true;
            OutOfGrammarSample ??= RenderOutOfGrammarSample(context, DlClause.FromCanonicalSpans(body, head, origin), grammarKind);

            return ClauseOfferOutcome.OutOfGrammar;
        }

        if(ConditionalityLintArmed)
        {
            LintConditionalityLoss(context, DlClause.FromCanonicalSpans(body, head, origin), premiseIds);
        }

        if(context.ContainsUpToRedundancy(body, head, origin, out bool exactDuplicate))
        {
            RedundantConclusions++;
            if(exactDuplicate)
            {
                DuplicateContainmentHits++;
            }
            else
            {
                SubsumedContainmentHits++;
            }

            if((pushed && PushTagMachineryLive) || (decidedUnderNoChoice && context.HasDerivedUnderChoiceTags))
            {
                //Both absorption joins are no-ops outside these two O(1) conditions, so the
                //absorbed clause is built exactly where one of them can act on it.
                DlClause absorbed = DlClause.FromCanonicalSpans(body, head, origin);
                JoinPushOnAbsorption(context, absorbed, pushed);
                JoinOriginOnAbsorption(context, absorbed, decidedUnderNoChoice);
            }

            return exactDuplicate ? ClauseOfferOutcome.ExactDuplicate : ClauseOfferOutcome.Subsumed;
        }

        InsertSurvivor(context, DlClause.FromCanonicalSpans(body, head, origin), grammarKind, pushed, derived, decidedUnderNoChoice);

        return ClauseOfferOutcome.Inserted;
    }

    /// <summary>Folds an offered conclusion's push and origin provenance out of the arrival declarations and the same-context premise tags — the one walk both faces of the single mutation point run. The choice tag's sole declaring site is the inter-nominal carrier, whose dispatch is gated on the per-individual-roots topology, so an arrival declaring the tag under the single-root topology is checked here rather than left to prose: a tag that cannot be declared can never be inherited either, since a premise carries it only from an earlier declaring arrival.</summary>
    /// <param name="context">The context the conclusion lands in.</param>
    /// <param name="premiseIds">The same-context premise clause ids.</param>
    /// <param name="pushed">Whether the conclusion carries the push tag.</param>
    /// <param name="derived">Whether the conclusion is <c>DerivedUnderChoice</c>.</param>
    /// <exception cref="InvalidOperationException">An arrival declared the choice tag under the single-root topology, where no site can mint it.</exception>
    private void FoldArrivalProvenance(Context context, ReadOnlySpan<int> premiseIds, out bool pushed, out bool derived)
    {
        if(Topology == RootContextTopology.SingleRoot && ArrivalDerivedUnderChoice)
        {
            throw new InvalidOperationException("A choice-tagged arrival reached the fold under the single-root topology, whose carrier machinery cannot mint the tag.");
        }

        pushed = ArrivalIsPush;
        derived = ArrivalDerivedUnderChoice;
        for(int i = 0; i < premiseIds.Length; i++)
        {
            //The origin taint fold is UNCONDITIONAL — it runs outside the
            //PushTagMachineryLive gate — because that gate (LicenseScoped &&
            //PerIndividualRoots) is dark on every SingleRoot/QueryScoped path, the
            //topology the whole reasoner runs under; nesting the taint in the push block
            //would defeat the guard exactly where it must hold while still passing every
            //gate. The push check stays conditional inside the same span walk, so only one
            //pass over premiseIds is paid. Ontology axioms are never in premiseIds, so a
            //choice-free axiom resolution never taints.
            if(PushTagMachineryLive && !pushed && context.IsPushed(premiseIds[i]))
            {
                pushed = true;
            }

            derived |= context.IsDerivedUnderChoice(premiseIds[i]);
        }
    }

    /// <summary>Renders the out-of-grammar latch's diagnostic sample for one refused clause, naming the context kind the grammar came from.</summary>
    /// <param name="context">The context whose grammar refused the clause.</param>
    /// <param name="clause">The refused clause.</param>
    /// <param name="grammarKind">The context kind whose grammar applies.</param>
    /// <returns>The rendered sample.</returns>
    private string RenderOutOfGrammarSample(Context context, DlClause clause, ContextGrammarKind grammarKind)
    {
        return grammarKind switch
        {
            ContextGrammarKind.Root => "root: " + clause.Render(Symbols),
            ContextGrammarKind.NominalRoot => "nominal root of individual " + context.HomeIndividual + " (context " + context.Id + "): " + clause.Render(Symbols),
            _ => "ordinary: " + clause.Render(Symbols),
        };
    }

    /// <summary>The single mutation point's survivor tail, shared by both of its faces: the backward Elim sweep, the maximal-head selection, the indexed insertion with its provenance tags and latches, the population watermarks, the root-tier ≈-class feed, and the worklist event.</summary>
    /// <param name="context">The context to mutate.</param>
    /// <param name="clause">The post-normalization clause the containment gate admitted.</param>
    /// <param name="grammarKind">The context kind whose selection order applies.</param>
    /// <param name="pushed">Whether the conclusion carries the push tag.</param>
    /// <param name="derived">Whether the conclusion is <c>DerivedUnderChoice</c>.</param>
    /// <param name="decidedUnderNoChoice">Whether the conclusion's derivation is choice-free.</param>
    private void InsertSurvivor(Context context, DlClause clause, ContextGrammarKind grammarKind, bool pushed, bool derived, bool decidedUnderNoChoice)
    {
        ScratchSubsumed.Clear();
        context.CollectStrictlySubsumed(clause, ScratchSubsumed);
        for(int i = 0; i < ScratchSubsumed.Count; i++)
        {
            context.Tombstone(ScratchSubsumed[i]);
            ElimApplications++;
            ClausesEliminated++;
            RuleApplications++;
        }

        ScratchMaximal.Clear();
        if(clause.Head.Length > 0)
        {
            Order.CollectMaximalHead(clause.Head, ScratchMaximal, grammarKind);
        }

        int clauseId = context.Insert(clause, IsPredEligible(clause, decidedUnderNoChoice, true), decidedUnderNoChoice, ScratchMaximal);
        if(pushed)
        {
            context.SetPushed(clauseId);
        }

        if(derived)
        {
            context.SetDerivedUnderChoice(clauseId);
        }

        if(clause.BodyLength == 0 && clause.Head.Length == 1 && derived && clause.Head[0].Kind == DlLiteralKind.Equality)
        {
            //Site D latch arm: Insert withheld this DerivedUnderChoice unconditional
            //equality head from UnconditionalHeads/RootIndex, and the equality flavour of the
            //withhold delegates the module named — the latch lives on the engine, so it is
            //armed here rather than inside Insert. A non-equality withheld head is
            //withhold-only (no arming): declining to record a not-actually-unconditional
            //concept/role head loses no real inference, only a spurious clash.
            ArmRootEqualityRidesAChoice();
        }

        ClausesDerived++;
        if(DataDemandDescriptors.Count > 0 && !context.IsRoot && clause.Head.Length > 1 && HeadCarriesCentralDataMarker(clause.Head))
        {
            //The per-context disjunctive-marker index the pool-growth re-probe and
            //the contributor re-emission consult; entries may go stale on
            //tombstoning and are filtered by liveness at every read.
            if(!LiveDisjunctiveMarkerClauses.TryGetValue(context.Id, out List<int>? disjunctiveIds))
            {
                disjunctiveIds = [];
                LiveDisjunctiveMarkerClauses[context.Id] = disjunctiveIds;
            }

            disjunctiveIds.Add(clauseId);
        }

        if(context.LiveCount > MaxContextClauses)
        {
            MaxContextClauses = context.LiveCount;
        }

        if(context.IsRoot && context.LiveCount > RootContextClauses)
        {
            RootContextClauses = context.LiveCount;
        }

        if(context.IsRoot && RootApproxClasses.TryResolveMerge(clause, context.HomeIndividual, out int mergeFirst, out int mergeSecond))
        {
            //The root-tier ≈-class feed: an unconditional single-literal
            //equality head just landed on a root context — a told or in-root-derived
            //o ≈ o′, or a future key-join continuation clause — so the two constants' classes
            //merge for the read-time union its later consumers resolve through. A bare merge
            //with no rewrite target still registers here; the Eq re-probe seam
            //sits at EqLandingProbe and fires on rewrites BY an equality, never on the
            //equality's own landing — the two sites stay distinct.
            //Site C guard: the ≈-class union runs ONLY for a DecidedUnderNoChoice
            //equality. A DerivedUnderChoice root equality — one whose derivation dropped a
            //non-participating disjunct — is refused the union and delegates named, so the
            //read-time fold never sees an identity an unrecorded drop manufactured. This
            //RootClasses.Union population MUST stay inside the decidedUnderNoChoice branch:
            //empty-premise consumers reading RootClasses inherit the guard transitively, so
            //hoisting the union out of this gate would silently un-guard them.
            if(decidedUnderNoChoice)
            {
                RootClasses ??= new RootApproxClasses();
                RootClasses.Union(mergeFirst, mergeSecond);
            }
            else
            {
                ArmRootEqualityRidesAChoice();
            }
        }

        WorklistEnqueues++;
        if(EnqueueEagerly)
        {
            EagerRuleQueue.Enqueue((context.Id, clauseId));
        }
        else
        {
            RuleQueue.Enqueue((context.Id, clauseId));
        }
    }

    /// <summary>
    /// The license scope's join-on-redundancy: clause identity and subsumption
    /// are content-only, so a pushed derivation absorbed by a content-identical
    /// or subsuming live clause would silently lose its tag — and the absorbed
    /// path dominates the jurisdiction corpus. On an absorption of a pushed
    /// conclusion the tag is OR-ed onto the surviving absorber; a false-to-true
    /// flip charges the tag-join counter and re-enqueues the survivor on the
    /// normal worklist so any previously push-gated rewrite over it is retried.
    /// The re-enqueue is not an insertion, so <see cref="WorklistEnqueues"/>
    /// stays untouched — the tag-join counter carries the event. A no-op on
    /// every unpushed absorption and everywhere the tag machinery is dark.
    /// </summary>
    /// <param name="context">The context whose containment gate absorbed the clause.</param>
    /// <param name="clause">The absorbed clause, in its post-normalization form — the form the containment gate tested.</param>
    /// <param name="pushed">Whether the absorbed conclusion carried the push tag.</param>
    private void JoinPushOnAbsorption(Context context, DlClause clause, bool pushed)
    {
        if(!pushed || !PushTagMachineryLive)
        {
            return;
        }

        if(!context.TryFindLiveAbsorber(clause, out int absorbingId) || context.IsPushed(absorbingId))
        {
            return;
        }

        context.SetPushed(absorbingId);
        EqScopeTagJoins++;
        RuleQueue.Enqueue((context.Id, absorbingId));
    }

    /// <summary>
    /// The origin merge on redundancy, the inverse polarity of the push
    /// join: clause identity and subsumption are content-only, so a choice-free
    /// (<c>DecidedUnderNoChoice</c>) derivation absorbed by a content-identical
    /// clause that first arrived <c>DerivedUnderChoice</c> would leave a decided
    /// fact wearing a choice tag its insert-time guards refused. An O(1) guard on
    /// <see cref="Context.HasDerivedUnderChoiceTags"/> precedes the absorber
    /// subsumption scan, so a run with no tagged clause — every run the acting-literal
    /// discipline keeps well-formed — pays nothing here. On such an absorption the tag
    /// is CLEARED toward <c>DecidedUnderNoChoice</c> on the surviving absorber, the
    /// withheld unconditional head is re-projected, and the survivor is re-enqueued so
    /// its now-un-refused Pred / r-Pred eligibility is re-evaluated. The clear is
    /// one-shot per id (a later duplicate reads the cleared tag and takes no action),
    /// so the non-idempotent data-demand increment inside the re-projection cannot
    /// double-count. The tag only ever moves toward <c>DecidedUnderNoChoice</c> on
    /// absorption — a <c>DerivedUnderChoice</c> arrival never sets the survivor's tag —
    /// so the merge cannot manufacture a choice-riding survivor. Dark on every run where
    /// nothing is tagged <c>DerivedUnderChoice</c>.
    /// </summary>
    /// <param name="context">The context whose containment gate absorbed the clause.</param>
    /// <param name="clause">The absorbed clause, in its post-normalization form — the form the containment gate tested.</param>
    /// <param name="decidedUnderNoChoice">Whether the absorbed conclusion's derivation is choice-free.</param>
    private void JoinOriginOnAbsorption(Context context, DlClause clause, bool decidedUnderNoChoice)
    {
        if(!decidedUnderNoChoice || !context.HasDerivedUnderChoiceTags)
        {
            return;
        }

        if(!context.TryFindLiveAbsorber(clause, out int absorbingId) || !context.IsDerivedUnderChoice(absorbingId))
        {
            return;
        }

        context.ClearDerivedUnderChoice(absorbingId);
        context.ProjectUnconditionalHead(absorbingId);
        OriginClearReenqueues++;
        RuleQueue.Enqueue((context.Id, absorbingId));
    }

    /// <summary>Inserts a push-landing arrival — a carrier image or an r-Succ seed landing in a root-class context, the channels whose clauses the license scope's context axis admits — with the push tag forced on for the sink's computation; the flag stays off wherever the tag machinery is dark. The arrival lands with empty premise ids (a verbatim cross-context relay, no same-context ancestry the taint fold can see), so the origin bit is threaded explicitly from the source through <paramref name="sourceDerived"/> rather than inferred — otherwise a <c>DerivedUnderChoice</c> carrier image would be laundered to <c>DecidedUnderNoChoice</c> at the foreign root. This is the ONE physical seam both push origins reach the clause set through, so the offer and exact-duplicate attribution charged here covers the r-Succ seed landing and the inter-nominal carrier image together rather than either alone.</summary>
    /// <param name="target">The root-class context the arrival lands in.</param>
    /// <param name="arrival">The arriving clause.</param>
    /// <param name="sourceDerived">Whether the source clause the arrival images was <c>DerivedUnderChoice</c>; the image inherits the tag. An r-Succ told/seed push carries no ancestry and passes <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the arrival was inserted.</returns>
    private bool AddPushedClause(Context target, DlClause arrival, bool sourceDerived)
    {
        ArrivalIsPush = PushTagMachineryLive;
        ArrivalDerivedUnderChoice = sourceDerived;
        PushedArrivalOffers++;
        ClauseOfferOutcome outcome = AddClauseCore(target, arrival, []);
        ArrivalDerivedUnderChoice = false;
        ArrivalIsPush = false;
        if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            PushedArrivalDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            PushedArrivalSubsumedHits++;
        }

        return outcome == ClauseOfferOutcome.Inserted;
    }

    /// <summary>
    /// Dispatches the Eq rule (KR 2016 Table 2; arXiv:1805.01396 Eq with the
    /// constant guard) with the landed clause pinned as the newer premise, both
    /// directions (the semi-naive discipline mirrors Hyper's given-pinning): the
    /// landed clause as the equality premise (selected literal a positive
    /// equality), rewriting through each of its legal SOURCE sides — the
    /// oriented maximal side of a comparable equality, the constant side of an
    /// unoriented variable-versus-individual equality — and the landed clause as
    /// a rewrite target (selected literal bearing a non-variable term in a
    /// rewritable position), rewritten by every live selected equality sourcing
    /// that term.
    /// </summary>
    /// <param name="context">The context both Eq premises live in.</param>
    /// <param name="selected">The landed clause's selected head literal.</param>
    /// <param name="clauseId">The landed clause's id.</param>
    /// <param name="selectedIndex">The selected literal's index within the landed clause's head.</param>
    /// <param name="maximalSet">The landed clause's maximal-head indexes, read by value for the given-equality acting-equality witness.</param>
    private void ApplyEqDispatch(Context context, DlLiteral selected, int clauseId, int selectedIndex, ReadOnlySpan<int> maximalSet)
    {
        EqRunHasOffered = false;
        if(selected.Kind == DlLiteralKind.Equality)
        {
            EqFromEquality(context, selected, clauseId, selectedIndex, maximalSet);
        }

        if(!BudgetExhausted)
        {
            EqFromTarget(context, selected, clauseId);
        }
    }

    /// <summary>Runs Eq with the landed clause as the equality premise (<c>Γ1 → ∆1 ∨ s1 ≈ t1</c> with <c>t1 ⋡ s1</c>): each side that qualifies as a rewrite source mints the acting-equality witness against the premise's own maximal set and drives one dispatch over the live targets whose selected literal mentions it.</summary>
    /// <param name="context">The context.</param>
    /// <param name="selected">The equality premise's selected literal.</param>
    /// <param name="equalityId">The equality premise's clause id.</param>
    /// <param name="selectedIndex">The selected literal's index within the equality premise's head.</param>
    /// <param name="maximalSet">The equality premise's maximal-head indexes, the set the witness proves membership against.</param>
    private void EqFromEquality(Context context, DlLiteral selected, int equalityId, int selectedIndex, ReadOnlySpan<int> maximalSet)
    {
        ReadOnlySpan<DlLiteral> head = context.At(equalityId).Head;
        if(ContextTermOrder.IsRewriteSourceSide(selected.First, selected.Second))
        {
            if(ActingEquality.TryFrom(head, maximalSet, selectedIndex, selected.First, selected.Second, out ActingEquality actingEquality))
            {
                RewriteMentionsOfSource(context, equalityId, actingEquality);
            }
            else
            {
                Debug.Assert(false, "The dispatched maximal equality literal sources the rewrite by construction; a failed acting-equality build is an invariant violation.");
            }
        }

        if(!BudgetExhausted && ContextTermOrder.IsRewriteSourceSide(selected.Second, selected.First))
        {
            if(ActingEquality.TryFrom(head, maximalSet, selectedIndex, selected.Second, selected.First, out ActingEquality actingEquality))
            {
                RewriteMentionsOfSource(context, equalityId, actingEquality);
            }
            else
            {
                Debug.Assert(false, "The dispatched maximal equality literal sources the rewrite by construction; a failed acting-equality build is an invariant violation.");
            }
        }
    }

    /// <summary>Rewrites every live target that mentions the source term in a rewrite-eligible slot of a maximal head literal, by the one equality premise, snapshotting the target ids so an inserted or eliminated conclusion does not perturb the enumeration. Each target is dispatched over its maximal head literals, firing the rewrite once per literal that actually mentions the source, so the residual subtraction consumes exactly that literal and every non-acting disjunct of a multi-literal head survives into the conclusion.</summary>
    /// <param name="context">The context.</param>
    /// <param name="equalityId">The equality premise's clause id.</param>
    /// <param name="actingEquality">The acting-equality witness — the dispatched maximal literal <c>s1 ≈ t1</c> and its oriented rewrite source and replacement.</param>
    private void RewriteMentionsOfSource(Context context, int equalityId, ActingEquality actingEquality)
    {
        ScratchEqDispatch.Clear();
        IReadOnlyList<int> targets = context.SelectedHeadMentions(actingEquality.FromTerm);
        for(int i = 0; i < targets.Count; i++)
        {
            int targetId = targets[i];
            if(targetId != equalityId && context.IsLive(targetId))
            {
                ScratchEqDispatch.Add(targetId);
            }
        }

        for(int i = 0; i < ScratchEqDispatch.Count && !BudgetExhausted; i++)
        {
            int targetId = ScratchEqDispatch[i];
            if(!context.IsLive(equalityId) || !context.IsLive(targetId))
            {
                continue;
            }

            DispatchEqOverTargetMaximal(context, equalityId, actingEquality, targetId);
        }
    }

    /// <summary>Fires the Eq rewrite once per maximal literal of the target that mentions the source term in a rewrite-eligible slot — the acting-target dispatch taken by both topologies: the rewritten literal and the subtracted residual are the SAME literal, so a multi-maximal head never loses a non-acting disjunct. Each firing reuses the threaded acting-equality witness and mints the acting-target witness for the source-mentioning literal.</summary>
    /// <param name="context">The context.</param>
    /// <param name="equalityId">The equality premise's clause id.</param>
    /// <param name="actingEquality">The acting-equality witness carrying the dispatched literal and its oriented source and replacement.</param>
    /// <param name="targetId">The rewrite-target clause's id.</param>
    private void DispatchEqOverTargetMaximal(Context context, int equalityId, ActingEquality actingEquality, int targetId)
    {
        DlClause target = context.At(targetId);
        ScratchEqActing.Clear();
        Order.CollectMaximalHead(target.Head, ScratchEqActing, GrammarKindOf(context));
        HeadShape shape = ScratchEqActing.Count == 1 ? HeadShape.SingletonMaximal : HeadShape.MultiMaximal;
        switch(shape)
        {
            case HeadShape.SingletonMaximal:
            case HeadShape.MultiMaximal:
            {
                for(int m = 0; m < ScratchEqActing.Count && !BudgetExhausted; m++)
                {
                    DlLiteral acting = target.Head[ScratchEqActing[m]];
                    if(MentionsInRewritableSlot(acting, actingEquality.FromTerm) && context.IsLive(equalityId) && context.IsLive(targetId))
                    {
                        if(ActingTarget.TryFrom(acting, actingEquality.FromTerm, out ActingTarget actingTarget))
                        {
                            ApplyEq(context, equalityId, targetId, actingEquality, actingTarget);
                        }
                        else
                        {
                            Debug.Assert(false, "A rewrite-slot-mentioning maximal target literal builds an acting target by construction; a failed build is an invariant violation.");
                        }
                    }
                }

                break;
            }
        }
    }

    /// <summary>Whether a head literal mentions the rewrite source in a rewrite-eligible slot — the readback mirror of the term-mention index's per-slot eligibility: any non-variable slot of a concept or role atom, and each (in)equality side not strictly dominated by its other side. The construction-proof <see cref="ActingTarget.TryFrom"/> delegates to this predicate verbatim.</summary>
    /// <param name="literal">The head literal.</param>
    /// <param name="fromTerm">The rewrite source.</param>
    /// <returns><see langword="true"/> for an eligible mention.</returns>
    internal static bool MentionsInRewritableSlot(DlLiteral literal, DlTerm fromTerm)
    {
        bool firstEligible = literal.Kind switch
        {
            DlLiteralKind.Concept or DlLiteralKind.Role => true,
            _ => ContextTermOrder.IsRewritableSide(literal.First, literal.Second),
        };

        if(firstEligible && literal.First.Equals(fromTerm))
        {
            return true;
        }

        bool secondEligible = literal.Kind switch
        {
            DlLiteralKind.Concept => false,
            DlLiteralKind.Role => true,
            _ => ContextTermOrder.IsRewritableSide(literal.Second, literal.First),
        };

        return secondEligible && literal.Second.Equals(fromTerm);
    }

    /// <summary>Whether a literal mentions a term in either occupied slot — the Eq acting-literal validation's V3 belt, confirming the acting target the residual subtracts is the rewritten literal (a literal that mentions neither the source nor the replacement was never a rewrite occurrence).</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="term">The term to look for.</param>
    /// <returns><see langword="true"/> when the term occurs in the literal.</returns>
    private static bool MentionsTerm(DlLiteral literal, DlTerm term)
    {
        return literal.First.Equals(term)
            || (literal.Kind != DlLiteralKind.Concept && literal.Second.Equals(term));
    }

    /// <summary>Whether the acting equality an <see cref="ApplyEq"/> firing consumed genuinely sourced the rewrite — the self-contained structural check the Eq application asserts as an invariant-violation backstop. The residual subtraction consumes <paramref name="actingEquality"/> as a passed parameter, so a literal not sourcing the rewrite would subtract the wrong disjunct. A well-formed acting equality IS an equality (V1) whose two sides are exactly the rewrite terms in either orientation (V2), and the target it subtracts mentions the source term (V3). The acting-literal witnesses establish these facts by construction, so a genuine firing always passes; a failing check is an invariant violation, never an expected condition. Isolated as a structural predicate — no premise, ontology, or engine-state lookup — so it validates the supplied literals directly.</summary>
    /// <param name="actingEquality">The equality literal the firing declares it consumed from the equality premise's head.</param>
    /// <param name="actingTarget">The target literal the firing declares it rewrote.</param>
    /// <param name="fromTerm">The rewrite source term.</param>
    /// <param name="replacement">The rewrite replacement term.</param>
    /// <returns><see langword="true"/> when the declaration passes V1/V2/V3 (a genuine rewrite); <see langword="false"/> when it fails any check (the wrong-acting-literal class).</returns>
    internal static bool IsEqActingLiteralWellFormed(DlLiteral actingEquality, DlLiteral actingTarget, DlTerm fromTerm, DlTerm replacement)
    {
        return actingEquality.Kind == DlLiteralKind.Equality
            && ((actingEquality.First.Equals(fromTerm) && actingEquality.Second.Equals(replacement))
             || (actingEquality.First.Equals(replacement) && actingEquality.Second.Equals(fromTerm)))
            && MentionsTerm(actingTarget, fromTerm);
    }

    /// <summary>
    /// Builds a minimal single-root engine over an empty module — the origin-bit
    /// relay guard's below-gates test redrive seam. The empty clausification yields a
    /// live term order, symbol table, and context structure, so the guard's own
    /// branches (the Eq acting-literal validation with its arrival declaration, the
    /// premise taint fold, the four guard-site refusals with the general latch, and
    /// the absorption-origin merge) can be driven through the real rule logic below
    /// the module-verdict gates rather than only through a whole module verdict. The
    /// seam adds no production path: each entry point forwards to the private rule
    /// method it names.
    /// </summary>
    /// <returns>The minimal single-root engine.</returns>
    internal static ContextSaturationEngine CreateForOriginRedrive()
    {
        return Create(ContextClausifier.Clausify(new ReasoningModule([], Violations: [])));
    }

    /// <summary>Builds a minimal engine over an empty module on the per-individual-roots topology — the redrive seam's fragmented arm, the sole habitat the inter-nominal carrier fires in. The nominal-root contexts are minted on demand by <see cref="RedriveRootForIndividual"/>, so the carrier's source-bit threading is exercisable without a whole topology run.</summary>
    /// <returns>The minimal per-individual-roots engine.</returns>
    internal static ContextSaturationEngine CreateForOriginRedriveFragmented()
    {
        return Create(ContextClausifier.Clausify(new ReasoningModule([], Violations: [])), DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.PerIndividualRoots);
    }

    /// <summary>Drives the Site A predecessor-relay eligibility screen over a hand-built clause with the equality-refusal latch arming live — the redrive seam's entry into <see cref="IsPredEligible"/>, so a choice-riding equality head refused the Pred relay arms the general latch and a shape-rejected head does not.</summary>
    /// <param name="clause">The clause whose head-shape eligibility is tested.</param>
    /// <param name="decidedUnderNoChoice">Whether the clause's derivation is choice-free.</param>
    /// <returns><see langword="true"/> when the clause is Pred-eligible.</returns>
    internal bool RedriveIsPredEligible(DlClause clause, bool decidedUnderNoChoice)
    {
        return IsPredEligible(clause, decidedUnderNoChoice, armLatchOnEqualityRefusal: true);
    }

    /// <summary>Arms the conditionality-loss lint on this engine — the redrive seam's entry into the internal <see cref="ConditionalityLintArmed"/> switch, so a subsequent <see cref="RedriveAddClause"/> runs the dark census check. Forwards to the internal switch and adds no production path.</summary>
    internal void RedriveArmConditionalityLint()
    {
        ConditionalityLintArmed = true;
    }

    /// <summary>Drives the Site B root-predecessor registration over a hand-built clause on a root context — the redrive seam's entry into <see cref="RegisterRootPredEligible"/>, so a choice-riding equality head refused the r-Pred broadcast arms the general latch and a shape-rejected head does not.</summary>
    /// <param name="root">The root context.</param>
    /// <param name="clauseId">The clause's id within the context.</param>
    /// <param name="clause">The clause whose r-Pred eligibility is tested.</param>
    /// <param name="decidedUnderNoChoice">Whether the clause's derivation is choice-free.</param>
    internal void RedriveRegisterRootPredEligible(Context root, int clauseId, DlClause clause, bool decidedUnderNoChoice)
    {
        RegisterRootPredEligible(root, clauseId, clause, decidedUnderNoChoice);
    }

    /// <summary>Drives a conclusion through the single mutation point <see cref="AddClause"/> with a chosen premise-id set — the redrive seam's entry into the taint fold, the Site C root ≈-class fold-feed, the Site D withhold latch, and the absorption-origin merge. Any <c>DerivedUnderChoice</c> premise taints the conclusion; the arrival's own step declares nothing, so a non-Eq conclusion is <c>DerivedUnderChoice</c> only via taint.</summary>
    /// <param name="context">The context the conclusion lands in.</param>
    /// <param name="clause">The conclusion clause.</param>
    /// <param name="premiseIds">The same-context premise ids the conclusion derives from.</param>
    /// <returns>The new clause id when the clause was inserted; <c>-1</c> when it was absorbed or dropped.</returns>
    internal int RedriveAddClause(Context context, DlClause clause, int[] premiseIds)
    {
        int conclusionId = context.ClauseCount;

        return AddClause(context, clause, premiseIds) ? conclusionId : -1;
    }

    /// <summary>Drives a conclusion through the SPAN face of the single mutation point with a chosen premise-id set — the redrive seam's entry into <see cref="AddClauseSpans"/>, so the span face's gates, its absorption joins, and the origin the survivor carries are observable beside the clause face's on the same hand-built population. The seam adds no production path: it forwards to the private face it names.</summary>
    /// <param name="context">The context the conclusion lands in.</param>
    /// <param name="body">The conclusion's canonical body literals.</param>
    /// <param name="head">The conclusion's canonical head literals.</param>
    /// <param name="origin">The source-axiom index a materialisation stamps.</param>
    /// <param name="premiseIds">The same-context premise ids the conclusion derives from.</param>
    /// <returns>The new clause id when the clause was inserted; <c>-1</c> when it was absorbed or dropped.</returns>
    internal int RedriveAddClauseSpans(Context context, DlLiteral[] body, DlLiteral[] head, int origin, int[] premiseIds)
    {
        int conclusionId = context.ClauseCount;

        return AddClauseSpans(context, body, head, origin, premiseIds) == ClauseOfferOutcome.Inserted ? conclusionId : -1;
    }

    /// <summary>Drives an Eq firing through <see cref="ApplyEq"/> with a chosen acting equality by routing the raw literals through the witness factories — the redrive seam's entry into the construction-proven acting-literal layer. The acting equality is wrapped in a synthetic one-literal head so its maximal-set membership is trivially satisfied; a concept acting literal (the wrong-acting-literal class) is refused by <see cref="ActingEquality.TryFrom"/> and lands no conclusion, while a genuine acting equality builds both witnesses and leaves the conclusion <c>DecidedUnderNoChoice</c>.</summary>
    /// <param name="context">The context holding the premises and receiving the conclusion.</param>
    /// <param name="equalityId">The equality premise's id in the context.</param>
    /// <param name="targetId">The rewrite-target premise's id in the context.</param>
    /// <param name="actingEquality">The literal the firing declares as the acting equality.</param>
    /// <param name="actingTarget">The target literal the firing rewrote.</param>
    /// <param name="fromTerm">The rewrite source term.</param>
    /// <param name="replacement">The rewrite replacement term.</param>
    /// <returns>The conclusion's clause id when it was inserted; <c>-1</c> when a factory refused the literals or the conclusion was absorbed or dropped.</returns>
    internal int RedriveApplyEq(Context context, int equalityId, int targetId, DlLiteral actingEquality, DlLiteral actingTarget, DlTerm fromTerm, DlTerm replacement)
    {
        EqRunHasOffered = false;
        Span<DlLiteral> syntheticHead = [actingEquality];
        Span<int> syntheticMaximal = [0];
        if(!ActingEquality.TryFrom(syntheticHead, syntheticMaximal, 0, fromTerm, replacement, out ActingEquality equalityWitness)
            || !ActingTarget.TryFrom(actingTarget, fromTerm, out ActingTarget targetWitness))
        {
            return -1;
        }

        int conclusionId = context.ClauseCount;
        ApplyEq(context, equalityId, targetId, equalityWitness, targetWitness);

        return context.ClauseCount > conclusionId ? conclusionId : -1;
    }

    /// <summary>Drives the Eq acting-literal dispatch over a rewrite target's maximal head literals through <see cref="DispatchEqOverTargetMaximal"/> — the redrive seam's entry into the unified acting-target selection, so a two-maximal target whose source-bearing maximal is not the selected literal rewrites the correct disjunct and keeps the non-acting maximal. The seam adds no production path: it forwards to the private rule method it names.</summary>
    /// <param name="context">The context holding the equality premise and the rewrite target and receiving the conclusion.</param>
    /// <param name="equalityId">The equality premise's id in the context.</param>
    /// <param name="actingEquality">The equality premise's acting literal.</param>
    /// <param name="targetId">The rewrite-target clause's id in the context.</param>
    /// <param name="fromTerm">The rewrite source term.</param>
    /// <param name="replacement">The rewrite replacement term.</param>
    /// <returns>The first landed conclusion's clause id when a firing inserted one; <c>-1</c> when the acting equality was refused or no firing landed a conclusion.</returns>
    internal int RedriveDispatchEqOverTargetMaximal(Context context, int equalityId, DlLiteral actingEquality, int targetId, DlTerm fromTerm, DlTerm replacement)
    {
        Span<DlLiteral> syntheticHead = [actingEquality];
        Span<int> syntheticMaximal = [0];
        if(!ActingEquality.TryFrom(syntheticHead, syntheticMaximal, 0, fromTerm, replacement, out ActingEquality witness))
        {
            return -1;
        }

        int conclusionId = context.ClauseCount;
        DispatchEqOverTargetMaximal(context, equalityId, witness, targetId);

        return context.ClauseCount > conclusionId ? conclusionId : -1;
    }

    /// <summary>Drives the given-equality target-lookup-and-dispatch step over every live rewrite target the source term registers, by one equality premise, through <see cref="RewriteMentionsOfSource"/> — the redrive seam's entry into the step that finds the source-mentioning targets and dispatches the equality over each target's maximal head literals, so a two-maximal target reached through the source-mention index rewrites its source-bearing maximal for every topology. The seam adds no production path: it forwards to the private rule method it names.</summary>
    /// <param name="context">The context holding the equality premise and its rewrite targets and receiving the conclusions.</param>
    /// <param name="equalityId">The equality premise's id in the context.</param>
    /// <param name="actingEquality">The equality premise's acting literal.</param>
    /// <param name="fromTerm">The rewrite source term the targets are looked up by.</param>
    /// <param name="replacement">The premise's other side.</param>
    /// <returns>The first landed conclusion's clause id when a firing inserted one; <c>-1</c> when the acting equality was refused or no firing landed a conclusion.</returns>
    internal int RedriveRewriteMentionsOfSource(Context context, int equalityId, DlLiteral actingEquality, DlTerm fromTerm, DlTerm replacement)
    {
        Span<DlLiteral> syntheticHead = [actingEquality];
        Span<int> syntheticMaximal = [0];
        if(!ActingEquality.TryFrom(syntheticHead, syntheticMaximal, 0, fromTerm, replacement, out ActingEquality witness))
        {
            return -1;
        }

        int conclusionId = context.ClauseCount;
        RewriteMentionsOfSource(context, equalityId, witness);

        return context.ClauseCount > conclusionId ? conclusionId : -1;
    }

    /// <summary>Resolves (minting on first need) the nominal-root context of an individual under the per-individual-roots topology — the redrive seam's handle on the carrier's source and foreign roots.</summary>
    /// <param name="individual">The individual whose nominal root is resolved.</param>
    /// <returns>The individual's nominal-root context.</returns>
    internal Context RedriveRootForIndividual(int individual)
    {
        return GetOrCreateRootFor(individual);
    }

    /// <summary>The number of contexts the structure holds — the redrive seam's bound on the ORDINARY tier, so a row can reach a context that carries no home individual and no registered core.</summary>
    internal int RedriveContextCount
    {
        get
        {
            return Structure.Count;
        }
    }

    /// <summary>Resolves a context by its id — the redrive seam's handle on the ORDINARY tier, the counterpart of <see cref="RedriveRootForIndividual"/> for a context that has no home individual. The seam adds no production path: it forwards to the structure's own indexer.</summary>
    /// <param name="contextId">The context's id in the structure.</param>
    /// <returns>The context.</returns>
    internal Context RedriveContext(int contextId)
    {
        return Structure[contextId];
    }

    /// <summary>Drives the inter-nominal carrier over a body-empty source clause — the redrive seam's entry into <see cref="FireInterNominalCarrier"/>, so the foreign-root image's inheritance of the source origin bit is observable.</summary>
    /// <param name="source">The source nominal-root context.</param>
    /// <param name="clause">The body-empty clause the carrier images.</param>
    /// <param name="sourceDerived">Whether the source clause is <c>DerivedUnderChoice</c>; the image inherits it.</param>
    internal void RedriveFireInterNominalCarrier(Context source, DlClause clause, bool sourceDerived)
    {
        FireInterNominalCarrier(source, clause, sourceDerived);
    }

    /// <summary>Runs Eq with the landed clause as a rewrite target: for each non-variable term its selected literal mentions in a rewritable position — every slot of a concept or role atom, each (in)equality side not strictly dominated by its other side — rewrites the target by every live selected equality sourcing that term.</summary>
    /// <param name="context">The context.</param>
    /// <param name="selected">The rewrite target's selected literal.</param>
    /// <param name="targetId">The rewrite-target clause's id.</param>
    private void EqFromTarget(Context context, DlLiteral selected, int targetId)
    {
        bool firstEligible = selected.Kind switch
        {
            DlLiteralKind.Concept or DlLiteralKind.Role => !selected.First.IsVariable,
            _ => ContextTermOrder.IsRewritableSide(selected.First, selected.Second),
        };

        bool secondEligible = selected.Kind switch
        {
            DlLiteralKind.Concept => false,
            DlLiteralKind.Role => !selected.Second.IsVariable,
            _ => ContextTermOrder.IsRewritableSide(selected.Second, selected.First),
        };

        if(firstEligible)
        {
            RewriteTargetByEqualities(context, targetId, selected, selected.First);
        }

        if(!BudgetExhausted && secondEligible && !selected.Second.Equals(selected.First))
        {
            RewriteTargetByEqualities(context, targetId, selected, selected.Second);
        }
    }

    /// <summary>Rewrites one target by every live selected equality sourcing <paramref name="fromTerm"/>, snapshotting the equality ids so an inserted or eliminated conclusion does not perturb the enumeration. Both topologies dispatch through the equality's maximal head literals and fire the rewrite once per maximal literal that genuinely sources the term (the acting-literal discipline), passing that literal as the acting equality so the residual subtraction consumes exactly it and every non-acting disjunct of a multi-literal equality head survives into the conclusion.</summary>
    /// <param name="context">The context.</param>
    /// <param name="targetId">The rewrite-target clause's id.</param>
    /// <param name="actingTarget">The target's dispatched maximal literal — the acting rewrite position.</param>
    /// <param name="fromTerm">The term the target's acting literal mentions in a rewritable position.</param>
    private void RewriteTargetByEqualities(Context context, int targetId, DlLiteral actingTarget, DlTerm fromTerm)
    {
        ScratchEqDispatch.Clear();
        IReadOnlyList<int> equalities = context.EqualitiesFromSide(fromTerm);
        for(int i = 0; i < equalities.Count; i++)
        {
            int eqId = equalities[i];
            if(eqId != targetId && context.IsLive(eqId))
            {
                ScratchEqDispatch.Add(eqId);
            }
        }

        for(int i = 0; i < ScratchEqDispatch.Count && !BudgetExhausted; i++)
        {
            int eqId = ScratchEqDispatch[i];
            if(!context.IsLive(eqId) || !context.IsLive(targetId))
            {
                continue;
            }

            DispatchEqOverEqualityMaximal(context, eqId, targetId, actingTarget, fromTerm);
        }
    }

    /// <summary>Fires the Eq rewrite once per maximal equality literal of the premise clause that genuinely sources the term — the acting-literal dispatch on the equality side, orientation-gated by <see cref="ContextTermOrder.IsRewriteSourceSide"/> and taken by both topologies: the consumed equality literal and the subtracted residual are the SAME literal.</summary>
    /// <param name="context">The context.</param>
    /// <param name="equalityId">The equality premise's clause id.</param>
    /// <param name="targetId">The rewrite-target clause's id.</param>
    /// <param name="actingTarget">The target's acting literal.</param>
    /// <param name="fromTerm">The rewrite source <c>s1</c>.</param>
    private void DispatchEqOverEqualityMaximal(Context context, int equalityId, int targetId, DlLiteral actingTarget, DlTerm fromTerm)
    {
        DlClause equality = context.At(equalityId);
        ScratchEqActing.Clear();
        Order.CollectMaximalHead(equality.Head, ScratchEqActing, GrammarKindOf(context));
        ReadOnlySpan<int> maximalSet = CollectionsMarshal.AsSpan(ScratchEqActing);
        for(int m = 0; m < ScratchEqActing.Count && !BudgetExhausted; m++)
        {
            int maximalIndex = ScratchEqActing[m];
            DlLiteral acting = equality.Head[maximalIndex];
            if(acting.Kind != DlLiteralKind.Equality)
            {
                continue;
            }

            if(ContextTermOrder.IsRewriteSourceSide(acting.First, acting.Second) && acting.First.Equals(fromTerm) && context.IsLive(equalityId) && context.IsLive(targetId))
            {
                ApplyEqOverEqualityLiteral(context, equalityId, targetId, equality.Head, maximalSet, maximalIndex, acting.First, acting.Second, actingTarget);
            }

            if(!BudgetExhausted && ContextTermOrder.IsRewriteSourceSide(acting.Second, acting.First) && acting.Second.Equals(fromTerm) && context.IsLive(equalityId) && context.IsLive(targetId))
            {
                ApplyEqOverEqualityLiteral(context, equalityId, targetId, equality.Head, maximalSet, maximalIndex, acting.Second, acting.First, actingTarget);
            }
        }
    }

    /// <summary>Mints the acting-equality and acting-target witnesses for one given-target Eq firing and applies the rule — the shared construction point of the two orientation arms. The acting equality proves membership of <paramref name="maximalIndex"/> in the equality premise's own maximal set; the acting target proves the rewrite-slot mention. Both are gate-passing at the call site, so a failed build is an invariant violation, never an expected condition.</summary>
    /// <param name="context">The context.</param>
    /// <param name="equalityId">The equality premise's clause id.</param>
    /// <param name="targetId">The rewrite-target clause's id.</param>
    /// <param name="equalityHead">The equality premise's head span.</param>
    /// <param name="maximalSet">The equality premise's maximal-head indexes, read by value.</param>
    /// <param name="maximalIndex">The attested maximal index of the acting equality literal.</param>
    /// <param name="fromTerm">The rewrite source <c>s1</c>.</param>
    /// <param name="replacement">The rewrite replacement <c>t1</c>.</param>
    /// <param name="actingTargetLiteral">The target's acting literal.</param>
    private void ApplyEqOverEqualityLiteral(Context context, int equalityId, int targetId, ReadOnlySpan<DlLiteral> equalityHead, ReadOnlySpan<int> maximalSet, int maximalIndex, DlTerm fromTerm, DlTerm replacement, DlLiteral actingTargetLiteral)
    {
        if(ActingEquality.TryFrom(equalityHead, maximalSet, maximalIndex, fromTerm, replacement, out ActingEquality actingEquality)
            && ActingTarget.TryFrom(actingTargetLiteral, fromTerm, out ActingTarget actingTarget))
        {
            ApplyEq(context, equalityId, targetId, actingEquality, actingTarget);
        }
        else
        {
            Debug.Assert(false, "A gate-passing acting equality and target build their witnesses by construction; a failed build is an invariant violation.");
        }
    }

    /// <summary>
    /// Applies the Eq rule for one equality premise and one target under the
    /// budget gate: the conclusion is the union of the two premise bodies with
    /// the target's SELECTED literal's eligible occurrences of the source
    /// <c>s1</c> rewritten to <c>t1</c>, and BOTH premises' residual
    /// (non-selected) head disjuncts carried (KR 2016 Table 2,
    /// <c>∆1 ∨ ∆2 ∨ s2[t1]p ⋈ t2</c>). Two structural gates precede the budget
    /// charge, so a rewrite the run will not keep spends none of the ceiling: the
    /// published CONSTANT GUARD — a constant source rewrites only a target literal
    /// free of function-bearing terms (arXiv:1805.01396 Eq — "if <c>s2|p ∈ Σo</c>,
    /// then <c>s2</c> contains no function symbols"), the layered side condition
    /// that keeps a nominal from being paramodulated into a function-bearing
    /// position — and the paramodulation-scope gate, which under the default scope
    /// blocks the central-variable-versus-individual rewrite <c>o ↦ x</c> outside a
    /// read-off context. The conclusion is assembled and, when its head is an
    /// obvious tautology (a reflexive <c>s ≈ s</c> disjunct or a complementary
    /// <c>s ≈ t</c> / <c>s ≉ t</c> pair), dropped and counted before the charge —
    /// so the funnel's dominant tautology producer spends no budget on conclusions
    /// insertion would discard. A non-tautological conclusion charges one attempt
    /// and is offered as its canonical body and head spans; the insert
    /// normalization re-orients it and fires Ineq on it.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="equalityId">The clause id of the equality premise <c>Γ1 → ∆1 ∨ s1 ≈ t1</c>.</param>
    /// <param name="targetId">The clause id of the rewrite target.</param>
    /// <param name="actingEquality">The acting-equality witness — the consumed equality literal <c>s1 ≈ t1</c> with its oriented source <c>s1</c> and replacement <c>t1</c>, so the premise residual subtracts exactly it.</param>
    /// <param name="actingTarget">The acting-target witness — the rewritten target literal, so the target residual subtracts exactly it.</param>
    private void ApplyEq(Context context, int equalityId, int targetId, ActingEquality actingEquality, ActingTarget actingTarget)
    {
        DlClause target = context.At(targetId);
        if(ConstantGuardBlocks(actingTarget.Literal, actingEquality.FromTerm))
        {
            return;
        }

        if(ParamodulationScopeBlocks(context, equalityId, actingTarget.Literal, actingEquality.FromTerm, actingEquality.Replacement))
        {
            return;
        }

        DlClause equality = context.At(equalityId);
        DlLiteral rewritten = RewriteTarget(actingTarget.Literal, actingEquality.FromTerm, actingEquality.Replacement);

        ScratchBody.Clear();
        AppendSpan(ScratchBody, equality.Body);
        AppendSpan(ScratchBody, target.Body);
        ScratchHead.Clear();
        ScratchHead.Add(rewritten);
        AppendResidualExcept(ScratchHead, equality.Head, actingEquality.Literal);
        AppendResidualExcept(ScratchHead, target.Head, actingTarget.Literal);

        if(HeadIsObviousTautology(ScratchHead))
        {
            TautologyDrops++;

            return;
        }

        if(!TryApply())
        {
            return;
        }

        //The acting-equality and acting-target witnesses guarantee a genuine rewrite by
        //construction, so the Eq arrival declares nothing — like every rule whose residual
        //subtracts the matched participant structurally. The predicate is retained as an
        //invariant-violation backstop asserted on every firing.
        Debug.Assert(
            IsEqActingLiteralWellFormed(actingEquality.Literal, actingTarget.Literal, actingEquality.FromTerm, actingEquality.Replacement),
            "The Eq witnesses guarantee a well-formed acting literal by construction; a failed assert is an invariant violation.");
        ArrivalDerivedUnderChoice = false;
        EqOffers++;
        DlClause.CanonicaliseInPlace(ScratchBody);
        DlClause.CanonicaliseInPlace(ScratchHead);
        ClauseOfferOutcome outcome = AddClauseSpans(context, CollectionsMarshal.AsSpan(ScratchBody), CollectionsMarshal.AsSpan(ScratchHead), DerivedOrigin, [equalityId, targetId]);
        ArrivalDerivedUnderChoice = false;
        if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            EqDuplicateHits++;
        }
        else if(outcome == ClauseOfferOutcome.Subsumed)
        {
            EqSubsumedHits++;
        }

        if(outcome == ClauseOfferOutcome.Inserted)
        {
            EqApplications++;
            RuleApplications++;
            EqLandingProbe?.Invoke(context, actingEquality.FromTerm, actingEquality.Replacement);
            if(RootDataObligationsEnabled && context.IsRoot && DataDemandDescriptors.Count > 0)
            {
                //The per-constant root data-obligation re-probe hook: an Eq rewrite BY an
                //equality just landed a non-redundant fact on a root context, so the
                //replacement's ≈-class may have pooled a new demand — re-decide it off the
                //read-time union. This is a SECOND site, distinct from the
                //equality-head landing feed, and it is ROOT-GATED here: a nominal-free
                //module has no root context, so this body never runs (and the switch keeps
                //it dark in production regardless), guaranteeing no new code executes on a
                //nominal-free module.
                ReprobeRootDataAfterMerge(context, actingEquality.Replacement);
            }
        }

        if(!EqRunHasOffered)
        {
            EqOfferingRuns++;
        }
        else if(outcome == ClauseOfferOutcome.ExactDuplicate)
        {
            EqIntraRunDuplicateHits++;
        }

        EqRunHasOffered = true;
    }

    /// <summary>Whether the published constant guard blocks a rewrite: the source term is a named individual and the rewritten p-term <c>s2</c> carries a function symbol. For a concept or role atom the p-term spans every argument slot, so a function-bearing atom never takes a constant rewrite; for an a-equality or inequality the rewritten SIDE is the p-position's own term — a whole-slot constant match is function-free by construction, so the guard never blocks there (and the flat term encoding cannot address a proper position inside <c>f(o)</c>, which is exactly the rewrite the guard forbids).</summary>
    /// <param name="targetSelected">The rewrite target's selected literal.</param>
    /// <param name="fromTerm">The rewrite source.</param>
    /// <returns><see langword="true"/> when the rewrite is blocked.</returns>
    private static bool ConstantGuardBlocks(DlLiteral targetSelected, DlTerm fromTerm)
    {
        if(!fromTerm.IsIndividual)
        {
            return false;
        }

        return targetSelected.Kind switch
        {
            DlLiteralKind.Concept => IsFunctionBearing(targetSelected.First),
            DlLiteralKind.Role => IsFunctionBearing(targetSelected.First) || IsFunctionBearing(targetSelected.Second),
            _ => false,
        };
    }

    /// <summary>Whether a term bears a function symbol — a function term <c>f(x)</c> or a root term <c>f(o)</c>.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> for a function-bearing term.</returns>
    private static bool IsFunctionBearing(DlTerm term)
    {
        return term.Kind is DlTermKind.Function or DlTermKind.FunctionOfIndividual;
    }

    /// <summary>
    /// Whether the selected paramodulation scope blocks this Eq rewrite. Only the
    /// SCOPABLE shape is ever gated: the rewrite carries a named individual down
    /// to the central variable (<c>o ↦ x</c> — the <c>x ≈ o</c> paramodulation,
    /// source a constant and replacement the central variable); the
    /// context-variable rewrite <c>o ↦ y</c> and the ground rewrite
    /// <c>o ↦ o′</c> are never scoped. Under the query-scoped default the
    /// rewrite is blocked outside a read-off context: its central-variable
    /// products never leave the context — no successor, predecessor, or root
    /// trigger (all ground or context-variable shaped) carries a bare-<c>x</c>
    /// atom, and the nominal-driven inconsistency path runs through the
    /// unrestricted Join bridge and the root machinery — so blocking it in a
    /// context no verdict surface inspects for a central-variable consequence
    /// leaves both read-off surfaces complete. The license-scoped widening
    /// narrows further on two axes through <see cref="LicenseScopeBlocks"/>; the
    /// unrestricted reference mode never blocks. The gate sits after the constant
    /// guard inside <see cref="ApplyEq"/> and receives the ACTING literal and the
    /// acting equality clause's id, never a first-maximal proxy — the composition
    /// with the fragmented topology's acting-literal Eq dispatch holds by
    /// construction.
    /// </summary>
    /// <param name="context">The context the Eq conclusion would land in.</param>
    /// <param name="equalityId">The acting equality clause's id in <paramref name="context"/> — the push-provenance read of the license scope's context axis.</param>
    /// <param name="actingTarget">The acted-on target literal — the query-atom test of the license scope's atom axis.</param>
    /// <param name="fromTerm">The rewrite source <c>s1</c>.</param>
    /// <param name="replacement">The rewrite result <c>t1</c>.</param>
    /// <returns><see langword="true"/> when the rewrite is blocked by the selected scope.</returns>
    private bool ParamodulationScopeBlocks(Context context, int equalityId, DlLiteral actingTarget, DlTerm fromTerm, DlTerm replacement)
    {
        if(!fromTerm.IsIndividual || !replacement.IsCentral)
        {
            return false;
        }

        return ParamodulationScope switch
        {
            NominalParamodulationScope.QueryScoped => !IsParamodulationReadOffContext(context),
            NominalParamodulationScope.LicenseScoped => LicenseScopeBlocks(context, equalityId, actingTarget),
            _ => false,
        };
    }

    /// <summary>
    /// The license-scoped two-axis gate on a scopable rewrite (thesis 8.2.3: the
    /// <c>x ≈ o</c> applications are needed only in query-initialized contexts
    /// and only for paramodulation inferences on query atoms). ATOM AXIS — in
    /// every query-initialized context the rewrite fires only when the acting
    /// target is a query atom; a blocked rewrite charges the query-atom counter
    /// and latches the context into the blocked-live certificate's query-surface
    /// arm. CONTEXT AXIS — a root-class context under the fragmented topology
    /// admits the rewrite only when the acting equality clause carries the
    /// transitively inherited push-provenance tag; a blocked rewrite charges the
    /// root-class counter, which carries the certificate's consistency-surface
    /// arm. The single root stays fully exempt (the consistency read-off lives
    /// there and its scopable share arrives ground-shaped), trivial and ground
    /// contexts stay exempt under both topologies, and every ordinary
    /// non-read-off context stays blocked exactly as under the query-scoped
    /// default, uncounted.
    /// </summary>
    /// <param name="context">The context the Eq conclusion would land in.</param>
    /// <param name="equalityId">The acting equality clause's id.</param>
    /// <param name="actingTarget">The acted-on target literal.</param>
    /// <returns><see langword="true"/> when the license scope blocks the rewrite.</returns>
    private bool LicenseScopeBlocks(Context context, int equalityId, DlLiteral actingTarget)
    {
        if(Structure.IsQueryContext(context.Id))
        {
            if(IsQueryAtomTarget(actingTarget))
            {
                return false;
            }

            EqScopeBlockedQueryAtom++;
            EqScopeBlockedQueryContexts ??= [];
            EqScopeBlockedQueryContexts.Add(context.Id);

            return true;
        }

        if(context.IsRoot)
        {
            if(Topology == RootContextTopology.SingleRoot || context.IsPushed(equalityId))
            {
                return false;
            }

            EqScopeBlockedRootClass++;

            return true;
        }

        return context.Id != Structure.TrivialContextId && !Structure.IsGround(context.Id);
    }

    /// <summary>Whether a target literal is a query atom the license-scoped atom axis admits: a concept atom over a subsumption-sweep signature class — the shapes the query read-off consumes as <c>B(x)</c> witnesses — or over the Bottom atom, the read-off-bearing <c>⊥</c> shape. Role, equality, and inequality targets and internal clausification atoms are not query atoms; a clash reachable only through rewriting one is the P3a delta the blocked-live latch guards.</summary>
    /// <param name="actingTarget">The acted-on target literal.</param>
    /// <returns><see langword="true"/> for a query-atom target.</returns>
    private bool IsQueryAtomTarget(DlLiteral actingTarget)
    {
        return actingTarget.Kind == DlLiteralKind.Concept
            && (actingTarget.Symbol == ContextSymbolTable.Bottom || (QueryAtomSignatureAtoms is not null && QueryAtomSignatureAtoms.Contains(actingTarget.Symbol)));
    }

    /// <summary>Whether a context is a read-off surface for the scoped paramodulation — the root context, the trivial consistency context, a ground context, or a query-initialized context — the contexts a verdict surface inspects for a central-variable consequence and where the <c>x ≈ o</c> rewrite therefore stays unrestricted.</summary>
    /// <param name="context">The context.</param>
    /// <returns><see langword="true"/> for a read-off context.</returns>
    private bool IsParamodulationReadOffContext(Context context)
    {
        return context.IsRoot
            || context.Id == Structure.TrivialContextId
            || Structure.IsGround(context.Id)
            || Structure.IsQueryContext(context.Id);
    }

    /// <summary>Whether a freshly built head is an obvious tautology the budget must not be charged for — a reflexive equality disjunct <c>s ≈ s</c>, or a complementary <c>s ≈ t</c> / <c>s ≉ t</c> pair over the same unordered term pair (KR 2016 Definition 4 condition 1). Detected on the unoriented scratch head, so the drop precedes the attempt charge; the check is a superset of the whole-clause tautology drop head normalization performs, so every Eq and Factor tautology is caught here and head normalization stays the authoritative drop for the other rules. A reflexive INEQUALITY <c>s ≉ s</c> is deliberately not flagged — it is a false disjunct the Ineq rule drops with the remaining head kept, not a whole-clause tautology. Heads are short, so the pairwise scan stays cheap.</summary>
    /// <param name="head">The freshly built scratch head.</param>
    /// <returns><see langword="true"/> when the head is an obvious tautology.</returns>
    private static bool HeadIsObviousTautology(List<DlLiteral> head)
    {
        for(int i = 0; i < head.Count; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind == DlLiteralKind.Equality && literal.First.Equals(literal.Second))
            {
                return true;
            }
        }

        for(int i = 0; i < head.Count; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind != DlLiteralKind.Equality)
            {
                continue;
            }

            for(int j = 0; j < head.Count; j++)
            {
                DlLiteral other = head[j];
                if(other.Kind == DlLiteralKind.Inequality && IsSameUnorderedPair(literal, other))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether an equality and an inequality range over the same unordered term pair — the complementary-pair test taken before orientation, so a pair the head-normalization orientation would align is caught regardless of the freshly built disjunct's side order.</summary>
    /// <param name="equality">The equality literal.</param>
    /// <param name="inequality">The inequality literal.</param>
    /// <returns><see langword="true"/> when the two range over the same unordered pair.</returns>
    private static bool IsSameUnorderedPair(DlLiteral equality, DlLiteral inequality)
    {
        return (equality.First.Equals(inequality.First) && equality.Second.Equals(inequality.Second))
            || (equality.First.Equals(inequality.Second) && equality.Second.Equals(inequality.First));
    }

    /// <summary>
    /// Applies the Factor rule (KR 2016 Table 2 — equality factoring)
    /// with the landed clause as the sole premise: the selected
    /// literal is a positive equality, each of its sides <c>s</c> with
    /// <c>t' ⊁ s</c> (the other side <c>t'</c> — the oriented maximal side
    /// always qualifies; the constant side of an unoriented <c>x ≈ o</c>
    /// qualifies through incomparability) factors against every OTHER positive
    /// equality disjunct carrying <c>s</c> on either side, and the conclusion
    /// replaces that disjunct with the introduced inequality <c>t ≉ t'</c>:
    /// <c>Γ → ∆ ∨ s≈t ∨ s≈t'</c> derives <c>Γ → ∆ ∨ t≉t' ∨ s≈t'</c>. The
    /// remaining side condition holds structurally — <c>∆ ∪ {s≈t} ⋡ s≈t'</c>
    /// because the dispatch runs once per MAXIMAL head literal, and a mutually
    /// unordered disjunct satisfies <c>⋡</c> exactly as a smaller one does.
    /// Factor earns its keep at nominals: multi-member enumeration heads
    /// <c>x≈o1 ∨ x≈o2</c> factor on the shared variable side into the
    /// constant-inequality face the enumeration collapse resolves.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="clause">The landed clause whose selected literal is a positive equality.</param>
    /// <param name="clauseId">The landed clause's id — the sole premise the sink's tag inheritance reads.</param>
    /// <param name="selectedIndex">The index of the selected literal within the head span.</param>
    private void ApplyFactor(Context context, DlClause clause, int clauseId, int selectedIndex)
    {
        DlLiteral selected = clause.Head[selectedIndex];
        FactorOnSharedSide(context, clause, clauseId, selectedIndex, selected.First, selected.Second);
        if(!BudgetExhausted && !selected.Second.Equals(selected.First))
        {
            FactorOnSharedSide(context, clause, clauseId, selectedIndex, selected.Second, selected.First);
        }
    }

    /// <summary>Factors the selected equality on one shared-side candidate <c>s</c> (with <c>t'</c> its other side), against every other positive equality disjunct carrying <c>s</c> on either slot. An obvious-tautology conclusion — a reflexive <c>s ≈ s</c> disjunct or a complementary <c>s ≈ t</c> / <c>s ≉ t</c> pair the enumeration-collapse factoring reintroduces — is dropped and counted before the budget charge, so it spends none of the ceiling.</summary>
    /// <param name="context">The context.</param>
    /// <param name="clause">The landed clause.</param>
    /// <param name="clauseId">The landed clause's id — the sole premise the sink's tag inheritance reads.</param>
    /// <param name="selectedIndex">The selected literal's head index.</param>
    /// <param name="sharedSide">The candidate shared side <c>s</c>.</param>
    /// <param name="otherSide">The selected literal's other side <c>t'</c>.</param>
    private void FactorOnSharedSide(Context context, DlClause clause, int clauseId, int selectedIndex, DlTerm sharedSide, DlTerm otherSide)
    {
        if(ContextTermOrder.TryCompareFTerm(otherSide, sharedSide, out int comparison) && comparison > 0)
        {
            return;
        }

        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length && !BudgetExhausted; i++)
        {
            DlLiteral other = head[i];
            if(i == selectedIndex || other.Kind != DlLiteralKind.Equality)
            {
                continue;
            }

            DlTerm introduced;
            if(other.First.Equals(sharedSide))
            {
                introduced = other.Second;
            }
            else if(other.Second.Equals(sharedSide))
            {
                introduced = other.First;
            }
            else
            {
                continue;
            }

            ScratchBody.Clear();
            AppendSpan(ScratchBody, clause.Body);
            ScratchHead.Clear();
            ScratchHead.Add(DlLiteral.Inequality(introduced, otherSide));
            AppendResidual(ScratchHead, head, i);

            if(HeadIsObviousTautology(ScratchHead))
            {
                TautologyDrops++;

                continue;
            }

            if(!TryApply())
            {
                return;
            }

            FactorOffers++;
            ClauseOfferOutcome outcome = AddClauseCore(context, DlClause.Create(ScratchBody, ScratchHead, DerivedOrigin), [clauseId]);
            if(outcome == ClauseOfferOutcome.Inserted)
            {
                FactorApplications++;
                RuleApplications++;
            }
            else if(outcome == ClauseOfferOutcome.ExactDuplicate)
            {
                FactorDuplicateHits++;
            }
            else if(outcome == ClauseOfferOutcome.Subsumed)
            {
                FactorSubsumedHits++;
            }
        }
    }

    /// <summary>Rewrites a target head literal under the equality <c>fromTerm ≈ replacement</c>, kind-keyed over the whole term vocabulary: every slot equal to the source term in a concept or role atom, and each (in)equality SIDE that is a legal rewrite position (<c>t2 ⊁ s2</c> — the oriented minimal side is never rewritten; the constant side of an unoriented <c>x ≈ o</c> is). A-terms are flat, so an occurrence is a whole-slot match.</summary>
    /// <param name="target">The target head literal.</param>
    /// <param name="fromTerm">The equality side acting as the rewrite source <c>s1</c>.</param>
    /// <param name="replacement">The equality's other side <c>t1</c>.</param>
    /// <returns>The rewritten literal.</returns>
    private static DlLiteral RewriteTarget(DlLiteral target, DlTerm fromTerm, DlTerm replacement)
    {
        return target.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(target.Symbol, RewriteSlot(target.First, fromTerm, replacement)),
            DlLiteralKind.Role => DlLiteral.Role(target.Symbol, RewriteSlot(target.First, fromTerm, replacement), RewriteSlot(target.Second, fromTerm, replacement)),
            DlLiteralKind.Equality => DlLiteral.Equality(
                RewriteEligibleSide(target.First, target.Second, fromTerm, replacement),
                RewriteEligibleSide(target.Second, target.First, fromTerm, replacement)),
            _ => DlLiteral.Inequality(
                RewriteEligibleSide(target.First, target.Second, fromTerm, replacement),
                RewriteEligibleSide(target.Second, target.First, fromTerm, replacement)),
        };
    }

    /// <summary>The image of one slot under the equality rewrite: the replacement when the slot is the source term, the slot itself otherwise.</summary>
    /// <param name="slot">The slot term.</param>
    /// <param name="fromTerm">The equality's source term.</param>
    /// <param name="replacement">The equality's other side.</param>
    /// <returns>The rewritten slot.</returns>
    private static DlTerm RewriteSlot(DlTerm slot, DlTerm fromTerm, DlTerm replacement)
    {
        return slot.Equals(fromTerm) ? replacement : slot;
    }

    /// <summary>The image of one (in)equality side under the equality rewrite: rewritten only when the side matches the source AND is a legal rewrite position against its other side (<c>t2 ⊁ s2</c>), so an oriented literal's minimal side stays untouched.</summary>
    /// <param name="side">The side term.</param>
    /// <param name="other">The literal's other side.</param>
    /// <param name="fromTerm">The equality's source term.</param>
    /// <param name="replacement">The equality's other side.</param>
    /// <returns>The rewritten side.</returns>
    private static DlTerm RewriteEligibleSide(DlTerm side, DlTerm other, DlTerm fromTerm, DlTerm replacement)
    {
        return side.Equals(fromTerm) && ContextTermOrder.IsRewritableSide(side, other) ? replacement : side;
    }

    /// <summary>
    /// Normalizes a clause head before insertion (the Ineq rule, tautology
    /// elimination, and orientation of KR 2016 Table 2 and Definition 4, per
    /// head literal): every equality and inequality
    /// literal is oriented so its maximal F-term is first; a clause whose head
    /// carries an <c>s ≈ s</c> disjunct, or both <c>s ≈ t</c> and <c>s ≉ t</c>,
    /// is a tautology and is dropped whole (Definition 4 condition 1); a false
    /// <c>s ≉ s</c> disjunct is dropped with the remaining head KEPT, counting
    /// one Ineq application per drop — the head collapses to <c>Γ → ⊥</c> only
    /// when the false disjunct was the sole literal. A head of atoms only takes
    /// the fast path untouched. Orientation makes duplicate and subsumption
    /// collapse canonical, so the existing redundancy machinery handles the rest.
    /// </summary>
    /// <param name="clause">The clause whose head is normalized.</param>
    /// <param name="normalized">The clause to insert when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the clause should be inserted, <see langword="false"/> when it is a dropped tautology.</returns>
    private bool TryNormalizeHead(DlClause clause, out DlClause normalized)
    {
        if(!HeadHasNonAtom(clause.Head))
        {
            normalized = clause;

            return true;
        }

        if(!TryOrientHead(clause.Head, NormalizeHead, out bool changed))
        {
            normalized = clause;

            return false;
        }

        if(!changed)
        {
            normalized = clause;

            return true;
        }

        NormalizeBody.Clear();
        AppendSpan(NormalizeBody, clause.Body);
        normalized = DlClause.Create(NormalizeBody, NormalizeHead, clause.Origin);

        return true;
    }

    /// <summary>The SPAN face of head normalization: the same orientation, tautology, and Ineq rules over a head span, leaving a rewritten head canonical in <see cref="SpanNormalizeHead"/> — a buffer of its own, so the caller's spans stay untouched. Both faces run one orientation walk, so the two forms cannot drift.</summary>
    /// <param name="head">The head span to normalize.</param>
    /// <param name="rewritten">Whether the normalized head is the one now standing in <see cref="SpanNormalizeHead"/> rather than the caller's own span.</param>
    /// <returns><see langword="true"/> when the conclusion should be offered on, <see langword="false"/> when it is a dropped tautology.</returns>
    private bool TryNormalizeHeadSpan(ReadOnlySpan<DlLiteral> head, out bool rewritten)
    {
        rewritten = false;
        if(!HeadHasNonAtom(head))
        {
            return true;
        }

        if(!TryOrientHead(head, SpanNormalizeHead, out bool changed))
        {
            return false;
        }

        if(!changed)
        {
            return true;
        }

        //The clause face rebuilds through DlClause.Create, which canonicalises; the span
        //face reaches the same canonical form directly, since a dropped or reoriented
        //disjunct can leave the buffer unsorted or duplicated.
        DlClause.CanonicaliseInPlace(SpanNormalizeHead);
        rewritten = true;

        return true;
    }

    /// <summary>Whether a head span carries a literal that is not an atom — the fast-path screen sparing an all-atom head the orientation walk.</summary>
    /// <param name="head">The head span.</param>
    /// <returns><see langword="true"/> when an equality or inequality is present.</returns>
    private static bool HeadHasNonAtom(ReadOnlySpan<DlLiteral> head)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(!head[i].IsAtom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Orients a head span into a reusable buffer: every atom copied, every equality and inequality oriented maximal-side-first, a false self-inequality disjunct dropped with one Ineq application counted, and a whole-clause tautology — a reflexive equality disjunct or a complementary equality/inequality pair — reported by a <see langword="false"/> return. The one orientation walk both normalization faces run.</summary>
    /// <param name="head">The head span to orient.</param>
    /// <param name="orientedToFill">The buffer the oriented head is written to, cleared first.</param>
    /// <param name="changed">Whether any literal was reoriented or dropped, so the caller must take the buffer rather than its own span.</param>
    /// <returns><see langword="true"/> when the head survives, <see langword="false"/> when the clause is a dropped tautology.</returns>
    private bool TryOrientHead(ReadOnlySpan<DlLiteral> head, List<DlLiteral> orientedToFill, out bool changed)
    {
        orientedToFill.Clear();
        changed = false;
        for(int i = 0; i < head.Length; i++)
        {
            DlLiteral literal = head[i];
            if(literal.IsAtom)
            {
                orientedToFill.Add(literal);

                continue;
            }

            DlLiteral oriented = ContextTermOrder.OrientEqualityLiteral(literal);
            if(oriented.First.Equals(oriented.Second))
            {
                if(oriented.Kind == DlLiteralKind.Equality)
                {
                    return false;
                }

                IneqApplications++;
                RuleApplications++;
                changed = true;

                continue;
            }

            changed |= !oriented.Equals(literal);
            orientedToFill.Add(oriented);
        }

        return !HasComplementaryPair(orientedToFill);
    }

    /// <summary>Whether an oriented head holds both <c>s ≈ t</c> and <c>s ≉ t</c> over the same terms — the complementary pair that renders the clause a tautology (KR 2016 Definition 4 condition 1). Heads are short, so the pairwise scan stays cheap.</summary>
    /// <param name="head">The oriented head literals.</param>
    /// <returns><see langword="true"/> when a complementary equality/inequality pair is present.</returns>
    private static bool HasComplementaryPair(List<DlLiteral> head)
    {
        for(int i = 0; i < head.Count; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind != DlLiteralKind.Equality)
            {
                continue;
            }

            for(int j = 0; j < head.Count; j++)
            {
                DlLiteral other = head[j];
                if(other.Kind == DlLiteralKind.Inequality && other.First.Equals(literal.First) && other.Second.Equals(literal.Second))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether every head literal of a clause is in the context-clause grammar of the context kind — the per-literal invariant behind the promoted in-saturation guard (§3.1; arXiv:1805.01396 Definition 1 and Table 4 split the ordinary and root literal universes; the nominal-root grammar is the fragmented topology's per-position pair universe).</summary>
    /// <param name="head">The normalized head span.</param>
    /// <param name="kind">The context kind whose grammar applies.</param>
    /// <returns><see langword="true"/> when every head literal is in-grammar.</returns>
    private static bool HeadInContextGrammar(ReadOnlySpan<DlLiteral> head, ContextGrammarKind kind)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(!IsInContextGrammar(head[i], kind))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether one head literal is in the context-clause grammar of the context
    /// kind. ORDINARY contexts (Definition 1's context p-terms and a-terms,
    /// widened by the certified broadened triggers): concept atoms over
    /// <c>x</c>, <c>y</c>, <c>f(x)</c>, or <c>o</c>; the role atoms
    /// <c>S(x,y)</c>, <c>S(y,x)</c>, <c>S(x,x)</c> (derivable through the
    /// nominal bridge: <c>S(x,f(x))</c> with <c>f(x) ≈ o</c> then <c>x ≈ o</c>),
    /// <c>S(x,f(x))</c>, <c>S(f(x),x)</c>, <c>S(x,o)</c>, <c>S(o,x)</c>,
    /// <c>S(o,o′)</c>, and the nominal-collapse images <c>S(o,y)</c>,
    /// <c>S(y,o)</c>, <c>S(y,y)</c> (a two-individual edge <c>S(o,o′)</c> — a
    /// broadened successor/predecessor trigger a non-root context may hold —
    /// paramodulated by a neighbour-nominal equality <c>y ≈ o</c> that rewrites
    /// a named individual down to the neighbour <c>y</c>; these mirror the root
    /// context's published nominal-collapse role literals); (in)equalities
    /// between the a-terms <c>x</c>, <c>y</c>, <c>f(x)</c>, <c>o</c>. ROOT
    /// context (the <c>x ↦ o′</c> image universe):
    /// concept atoms over <c>y</c>, <c>o</c>, <c>f(o)</c>; the role atoms
    /// <c>S(o,y)</c>, <c>S(y,o)</c>, <c>S(y,y)</c> (the nominal-bridge self-pair,
    /// mirroring the ordinary <c>S(x,x)</c>), <c>S(o,o′)</c>, and the same-individual
    /// images <c>S(o,f(o))</c>, <c>S(f(o),o)</c>; (in)equalities between the
    /// a-terms <c>y</c>, <c>o</c>, <c>f(o)</c>. NOMINAL-ROOT contexts (the
    /// fragmented topology's entry-translated universe) admit the per-position
    /// pair rules of <see cref="IsInNominalRootGrammar"/>. Every (in)equality
    /// must be in its canonical stored form: comparable pairs strictly
    /// maximal-side-first, incomparable pairs variable-first.
    /// </summary>
    /// <param name="literal">The head literal.</param>
    /// <param name="kind">The context kind whose grammar applies.</param>
    /// <returns><see langword="true"/> for an in-grammar literal.</returns>
    private static bool IsInContextGrammar(DlLiteral literal, ContextGrammarKind kind)
    {
        return kind switch
        {
            ContextGrammarKind.Root => IsInRootGrammar(literal),
            ContextGrammarKind.NominalRoot => IsInNominalRootGrammar(literal),
            _ => IsInOrdinaryGrammar(literal),
        };
    }

    /// <summary>Whether one head literal is in the ORDINARY context grammar (see <see cref="IsInContextGrammar"/>).</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> for an in-grammar literal.</returns>
    private static bool IsInOrdinaryGrammar(DlLiteral literal)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => literal.First.Kind is DlTermKind.Central or DlTermKind.Context or DlTermKind.Function or DlTermKind.Individual,
            DlLiteralKind.Role => IsOrdinaryRolePair(literal.First, literal.Second),
            _ => IsOrdinaryEqualityTerm(literal.First) && IsOrdinaryEqualityTerm(literal.Second) && IsCanonicallyStored(literal),
        };
    }

    /// <summary>Whether a role atom's argument pair is one of the ordinary p-term shapes: the central pairs <c>S(x,·)</c> and <c>S(·,x)</c> over Definition 1's a-terms, and the neighbour-and-individual pairs — the two-individual edge <c>S(o,o′)</c> together with the nominal-collapse images <c>S(o,y)</c>, <c>S(y,o)</c>, and <c>S(y,y)</c>. A collapse image arises when a neighbour-nominal equality <c>y ≈ o</c> paramodulates a named individual of <c>S(o,o′)</c> down to the neighbour <c>y</c> — Eq rewrites the individual (a non-variable source) and never the variable, so <c>x</c>/<c>y</c> are never the rewrite source (the "<c>s2|p</c> is not a variable" side condition); a self-edge <c>S(o,o)</c> whose two occurrences both collapse yields <c>S(y,y)</c>. They are the non-root twins of the root context's published nominal-collapse role literals, reachable here because the certified broadened triggers carry <c>S(o,o′)</c> into non-root neighbourhoods.</summary>
    /// <param name="first">The first argument.</param>
    /// <param name="second">The second argument.</param>
    /// <returns><see langword="true"/> for an admitted pair.</returns>
    private static bool IsOrdinaryRolePair(DlTerm first, DlTerm second)
    {
        if(first.IsCentral)
        {
            return second.Kind is DlTermKind.Central or DlTermKind.Context or DlTermKind.Function or DlTermKind.Individual;
        }

        if(second.IsCentral)
        {
            return first.Kind is DlTermKind.Context or DlTermKind.Function or DlTermKind.Individual;
        }

        return first.Kind is DlTermKind.Context or DlTermKind.Individual
            && second.Kind is DlTermKind.Context or DlTermKind.Individual;
    }

    /// <summary>Whether a term may appear in an ordinary-context (in)equality: the a-terms <c>x</c>, <c>y</c>, <c>f(x)</c>, <c>o</c>.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> for an admitted term.</returns>
    private static bool IsOrdinaryEqualityTerm(DlTerm term)
    {
        return term.Kind is DlTermKind.Central or DlTermKind.Context or DlTermKind.Function or DlTermKind.Individual;
    }

    /// <summary>Whether one head literal is in the ROOT context grammar (see <see cref="IsInContextGrammar"/>).</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> for an in-grammar literal.</returns>
    private static bool IsInRootGrammar(DlLiteral literal)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => literal.First.Kind is DlTermKind.Context or DlTermKind.Individual or DlTermKind.FunctionOfIndividual,
            DlLiteralKind.Role => IsRootRolePair(literal.First, literal.Second),
            _ => IsRootEqualityTerm(literal.First) && IsRootEqualityTerm(literal.Second) && IsCanonicallyStored(literal),
        };
    }

    /// <summary>Whether a role atom's argument pair is one of the root p-term shapes — the <c>x ↦ o′</c> images of Definition 1: pairs over <c>y</c> and individuals with at least one individual, the self-pair <c>S(y, y)</c> (derivable through the nominal bridge exactly as the ordinary <c>S(x, x)</c> is: a root self-edge <c>S(o, o)</c> paramodulates under a domain-collapse equality <c>y ≈ o</c>), and the same-individual <c>S(o, f(o))</c> / <c>S(f(o), o)</c> images.</summary>
    /// <param name="first">The first argument.</param>
    /// <param name="second">The second argument.</param>
    /// <returns><see langword="true"/> for an admitted pair.</returns>
    private static bool IsRootRolePair(DlTerm first, DlTerm second)
    {
        if(first.IsIndividual && second.IsIndividual)
        {
            return true;
        }

        if(first.Kind == DlTermKind.Context && second.Kind == DlTermKind.Context)
        {
            return true;
        }

        if(first.Kind == DlTermKind.Context && second.IsIndividual)
        {
            return true;
        }

        if(first.IsIndividual && second.Kind == DlTermKind.Context)
        {
            return true;
        }

        if(first.IsIndividual && second.IsFunctionOfIndividual)
        {
            return second.IndividualId == first.IndividualId;
        }

        if(first.IsFunctionOfIndividual && second.IsIndividual)
        {
            return first.IndividualId == second.IndividualId;
        }

        return false;
    }

    /// <summary>Whether a term may appear in a root-context (in)equality: the root a-terms <c>y</c>, <c>o</c>, <c>f(o)</c>.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> for an admitted term.</returns>
    private static bool IsRootEqualityTerm(DlTerm term)
    {
        return term.Kind is DlTermKind.Context or DlTermKind.Individual or DlTermKind.FunctionOfIndividual;
    }

    /// <summary>Whether one head literal is in the NOMINAL-ROOT grammar — the fragmented topology's entry-translated universe over the kinds {central, context, function, individual, function-of-individual}: concept atoms over <c>x</c>, <c>y</c>, <c>o′</c>, <c>f(x)</c>, <c>f(o′)</c>; role atoms per the frozen per-position pair list of <see cref="IsNominalRootRolePair"/>; (in)equalities over the same term set in canonical stored form. The pair rules are derived from and no broader than the rule-by-rule closure — a kind cross-product would silently weaken the out-of-grammar guard as a soundness backstop for shapes the closure never proves reachable.</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> for an in-grammar literal.</returns>
    private static bool IsInNominalRootGrammar(DlLiteral literal)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => IsNominalRootTerm(literal.First),
            DlLiteralKind.Role => IsNominalRootRolePair(literal.First, literal.Second),
            _ => IsNominalRootTerm(literal.First) && IsNominalRootTerm(literal.Second) && IsCanonicallyStored(literal),
        };
    }

    /// <summary>Whether a role atom's argument pair is one of the nominal-root p-term shapes — the per-position pair list: the entry-translated central pairs <c>(x,o′)</c>, <c>(o′,x)</c>, <c>(x,y)</c>, <c>(y,x)</c>, <c>(x,x)</c> (a reflexive told fact maps both slots central), <c>(x,f(x))</c> and its Pred-direction reverse <c>(f(x),x)</c>; and EVERY published root pair — <c>(o′,o″)</c>, the predecessor-bearing <c>(o′,y)</c> / <c>(y,o′)</c> / <c>(y,y)</c> (the Nom tail's variable-versus-individual equality paramodulates a foreign constant of a ground edge down to <c>y</c>, exactly as it does in the single root, whose grammar carries these as core <c>Prr</c> shapes), and the same-individual-id <c>(o′,f(o′))</c> / <c>(f(o′),o′)</c> images — the id equality mirrors the root grammar's gate, never a bare kind pair (no cross-individual <c>S(o″,f(o′))</c>).</summary>
    /// <param name="first">The first argument.</param>
    /// <param name="second">The second argument.</param>
    /// <returns><see langword="true"/> for an admitted pair.</returns>
    private static bool IsNominalRootRolePair(DlTerm first, DlTerm second)
    {
        if(first.IsCentral)
        {
            return second.IsCentral || second.IsIndividual || second.Kind is DlTermKind.Context or DlTermKind.Function;
        }

        if(second.IsCentral)
        {
            return first.IsIndividual || first.Kind is DlTermKind.Context or DlTermKind.Function;
        }

        return IsRootRolePair(first, second);
    }

    /// <summary>Whether a term may appear in a nominal-root concept atom or (in)equality side: the entry-translated a-terms <c>x</c>, <c>y</c>, <c>f(x)</c>, <c>o′</c>, <c>f(o′)</c>.</summary>
    /// <param name="term">The a-term.</param>
    /// <returns><see langword="true"/> for an admitted term.</returns>
    private static bool IsNominalRootTerm(DlTerm term)
    {
        return term.Kind is DlTermKind.Central or DlTermKind.Context or DlTermKind.Function or DlTermKind.Individual or DlTermKind.FunctionOfIndividual;
    }

    /// <summary>Whether an (in)equality literal is in its canonical stored form: a comparable pair strictly maximal-side-first (a reflexive pair was dropped by normalization), an incomparable pair variable-first.</summary>
    /// <param name="literal">The normalized (in)equality literal.</param>
    /// <returns><see langword="true"/> for the canonical orientation.</returns>
    private static bool IsCanonicallyStored(DlLiteral literal)
    {
        return ContextTermOrder.TryCompareFTerm(literal.First, literal.Second, out int comparison)
            ? comparison > 0
            : literal.First.IsVariable;
    }

    /// <summary>Whether the budget still allows the next rule attempt, spending one unit of <see cref="InferenceAttempts"/> when it does; reaching the inclusive ceiling of either the attempt axis or the clause-population axis latches <see cref="BudgetExhausted"/>. The gate charges the ATTEMPT rather than the added conclusion, so a join emitting redundant conclusions spends the ceiling at the same rate as productive work; the population axis is read here rather than at the insertion site so both axes stop the engine between charged units of work.</summary>
    /// <returns><see langword="true"/> when a rule may be applied.</returns>
    private bool TryApply()
    {
        if(BudgetExhausted)
        {
            return false;
        }

        if(Budget.IsExhaustedByInferences(InferenceAttempts) || Budget.IsExhaustedByPopulation(ClausesDerived))
        {
            BudgetExhausted = true;

            return false;
        }

        InferenceAttempts++;
        if(Progress is not null && (InferenceAttempts & (InferenceAttempts - 1)) == 0)
        {
            EmitProgress();
        }

        return true;
    }

    /// <summary>Builds one progress mark from the run's current counters and emits it to the attached sampler; reached only on a power-of-two attempt count with the sampler attached.</summary>
    private void EmitProgress()
    {
        SaturationProgressSampler sampler = Progress!;
        OccurrenceTelemetry occurrence = SumOccurrenceTelemetry();
        SaturationProgressTraceEvent mark = new(
            ProgressEmissions++,
            sampler.Clock.GetUtcNow().UtcTicks,
            sampler.CorrelationId,
            InferenceAttempts,
            RuleApplications,
            ClausesDerived,
            ClausesEliminated,
            MaxContextClauses,
            RootContextClauses,
            ContextsCreated,
            Structure.RootClassContextIds.Count,
            TautologyDrops,
            RedundantConclusions,
            OutOfGrammarConclusions,
            WorklistEnqueues,
            RuleQueue.Count,
            EagerRuleQueue.Count,
            SuccQueue.Count,
            Symbols.GeneratedNominalCount,
            Symbols.MaxNominalLabelDepth,
            HyperApplications,
            EqApplications,
            FactorApplications,
            PredApplications,
            JoinApplications,
            RootSuccApplications,
            RootPredApplications,
            NomApplications,
            EnumerationHabitat,
            RootPredFilteredOffers,
            RelevanceTautologiesSeeded,
            DuplicateContainmentHits,
            SubsumedContainmentHits,
            RootPredRegistrationSweepOffers,
            RootPredNewRootEdgeOffers,
            RootPredPremiseOffers,
            RootPredBroadcastOffers,
            RootPredRegistrationSweepDuplicateHits,
            RootPredNewRootEdgeDuplicateHits,
            RootPredPremiseDuplicateHits,
            RootPredBroadcastDuplicateHits,
            JoinOffers,
            JoinDuplicateHits,
            CoreOffers,
            CoreDuplicateHits,
            HyperOffers,
            HyperDuplicateHits,
            PredOffers,
            PredDuplicateHits,
            EqOffers,
            EqDuplicateHits,
            FactorOffers,
            FactorDuplicateHits,
            SuccOffers,
            SuccDuplicateHits,
            NomOffers,
            NomDuplicateHits,
            PushedArrivalOffers,
            PushedArrivalDuplicateHits,
            SidecarSeedOffers,
            SidecarSeedDuplicateHits,
            PredLandedTargetOffers,
            PredLandedPremiseOffers,
            PredNewEdgeOffers,
            PredLandedTargetDuplicateHits,
            PredLandedPremiseDuplicateHits,
            PredNewEdgeDuplicateHits,
            PredOdometerRuns,
            PredIntraRunDuplicateHits,
            OriginClearReenqueues,
            RootPredRegistrationSweepSubsumedHits,
            RootPredNewRootEdgeSubsumedHits,
            RootPredPremiseSubsumedHits,
            RootPredBroadcastSubsumedHits,
            JoinSubsumedHits,
            CoreSubsumedHits,
            HyperSubsumedHits,
            PredSubsumedHits,
            PredLandedTargetSubsumedHits,
            PredLandedPremiseSubsumedHits,
            PredNewEdgeSubsumedHits,
            PredLandedTargetLandings,
            PredLandedPremiseLandings,
            PredNewEdgeLandings,
            EqSubsumedHits,
            FactorSubsumedHits,
            SuccSubsumedHits,
            NomSubsumedHits,
            PushedArrivalSubsumedHits,
            SidecarSeedSubsumedHits,
            JoinOfferingRuns,
            JoinIntraRunDuplicateHits,
            EqOfferingRuns,
            EqIntraRunDuplicateHits,
            RootBroadcastClauseCount,
            CautiousCoreCeiling,
            CautiousCoresRegistered,
            occurrence.HeadEntriesRegistered,
            occurrence.BodyEntriesRegistered,
            occurrence.HeadDistinctKeys,
            occurrence.BodyDistinctKeys,
            occurrence.SweepProbes,
            occurrence.SweepPostingEntriesWalked,
            PredAnchoredArmDispatches,
            PredOrdinaryArmDispatches,
            PredAnchorInvariantTargetPasses,
            PredAnchorPruned,
            PredBroadcastContainedSkips,
            PredOrdinaryInvariantTargetPasses,
            PredBroadcastImageTargets);
        sampler.Handler(in mark);
    }

    /// <summary>Enqueues a Succ candidate, deduplicated while it is pending.</summary>
    /// <param name="contextId">The predecessor context id.</param>
    /// <param name="trigger">The packed trigger term.</param>
    private void EnqueueSucc(int contextId, DlTerm trigger)
    {
        if(PendingSucc.Add((contextId, trigger)))
        {
            SuccQueue.Enqueue((contextId, trigger));
        }
    }

    /// <summary>Whether a clause may drive Pred as the successor-side target: an empty head, or every NONGROUND head literal in <c>Pr(O)</c> (Table 2's <c>Lᵢ ∈ Pr</c> for each nonground <c>Lᵢ</c> — ground literals cross the edge sigma-fixed and are unrestricted). A nonground atom checks the materialized trigger set; a nonground equality checks the extended <c>Pr</c> shapes <c>x ≈ y</c>, <c>x ≈ o</c>, <c>y ≈ o</c> (Definition 2 of the nominal calculus); an inequality is never a <c>Pr</c> member.</summary>
    /// <param name="clause">The clause.</param>
    /// <param name="decidedUnderNoChoice">Whether the clause's derivation is choice-free (the origin bit): a <c>DerivedUnderChoice</c> clause carrying an EQUALITY head the shape/trigger screen would otherwise relay — a ground <c>o ≈ o′</c> or a nonground trigger <c>y ≈ o</c>, in either admission arm — is refused Pred eligibility after the screen accepts it and delegates named, mirroring the r-Pred Site B refusal. A crossed predecessor image grounds a nonground trigger equality <c>y ≈ o</c> to a root identity, so relaying either shape an unrecorded disjunct drop manufactured would feed a false merge across the predecessor edge; the target's own origin tag does not ride the predecessor-side premise ids, so this source-eligibility screen is the sole gate for both shapes.</param>
    /// <param name="armLatchOnEqualityRefusal">Whether an equality refusal arms the general <c>RootEqualityRidesAChoice</c> latch: <see langword="true"/> at the single insert-time eligibility computation, <see langword="false"/> at the <see cref="ProcessClause"/> recompute of the same clause, so the census counts each refused clause once (matching Sites B/C/D) rather than twice across the two calls one insertion pays.</param>
    /// <returns><see langword="true"/> for a Pred-eligible clause.</returns>
    private bool IsPredEligible(DlClause clause, bool decidedUnderNoChoice, bool armLatchOnEqualityRefusal)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            if(!Context.IsGroundLiteral(head[i]))
            {
                bool eligible = head[i].IsAtom ? PredecessorTriggers.Contains(head[i]) : Context.IsPredecessorTriggerEquality(head[i]);
                if(!eligible)
                {
                    return false;
                }
            }

            //The shape/trigger screen has already accepted this head literal — a ground
            //atom or equality crossing the edge sigma-fixed, or a nonground Pr trigger — so
            //it is one that would be relayed across the predecessor edge. Only on such an
            //accepted position is a choice-riding equality refused the Pred relay and the
            //general latch armed: a ground OR the nonground trigger equality the predecessor
            //image would ground to a root identity, refused here at the source, the only
            //place the target's origin tag is visible (the crossed conclusion carries
            //predecessor-side premise ids, so it cannot inherit the tag). The refusal sits
            //after the screen so a nonground equality the screen rejects anyway never forces
            //a delegation.
            if(head[i].Kind == DlLiteralKind.Equality && !decidedUnderNoChoice)
            {
                if(armLatchOnEqualityRefusal)
                {
                    ArmRootEqualityRidesAChoice();
                }

                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the context holds a live clause whose SELECTED head literal is the atom — the K2 membership test, exactly Table 2's <c>Γ' → ∆' ∨ A'σ ∈ Sᵤ</c> with <c>∆' ⋡ A'σ</c> pattern.</summary>
    /// <param name="context">The context.</param>
    /// <param name="atom">The selected head atom.</param>
    /// <returns><see langword="true"/> when a live clause has that selected head literal.</returns>
    private static bool HasLiveHead(Context context, DlLiteral atom)
    {
        IReadOnlyList<int> heads = context.SelectedHeadClauses(atom);
        for(int i = 0; i < heads.Count; i++)
        {
            if(context.IsLive(heads[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Copies the live clause ids whose SELECTED head literal equals the atom into a slot buffer — a snapshot, so insertions during the join do not perturb the enumeration; the selected-literal keying enforces the <c>∆ᵢ ⋡ Aᵢσ</c> premise side condition structurally.</summary>
    /// <param name="context">The context.</param>
    /// <param name="atom">The selected head atom.</param>
    /// <param name="slotToAppendTo">The slot buffer the live ids are appended to.</param>
    private static void CollectLiveHeads(Context context, DlLiteral atom, List<int> slotToAppendTo)
    {
        IReadOnlyList<int> heads = context.SelectedHeadClauses(atom);
        for(int i = 0; i < heads.Count; i++)
        {
            if(context.IsLive(heads[i]))
            {
                slotToAppendTo.Add(heads[i]);
            }
        }
    }

    /// <summary>Copies the live clause ids whose SELECTED head literal is a role atom of the given shape (symbol and central position) into a slot buffer — the free-neighbour lookup whose per-candidate binding the consistency filter reads off the selected literal.</summary>
    /// <param name="context">The context.</param>
    /// <param name="roleSymbol">The role symbol.</param>
    /// <param name="centralFirst">Whether the central variable is the first argument.</param>
    /// <param name="slotToAppendTo">The slot buffer the live ids are appended to.</param>
    private static void CollectLiveRoleHeads(Context context, int roleSymbol, bool centralFirst, List<int> slotToAppendTo)
    {
        IReadOnlyList<int> heads = context.RoleHeadClauses(roleSymbol, centralFirst);
        for(int i = 0; i < heads.Count; i++)
        {
            if(context.IsLive(heads[i]))
            {
                slotToAppendTo.Add(heads[i]);
            }
        }
    }

    /// <summary>Resets the join scratch state for a fresh odometer.</summary>
    private void BeginJoin()
    {
        SlotBuffers.Clear();
        SlotNeighbours.Clear();
        SlotCentralFirst.Clear();
        SlotExactAtoms.Clear();
        SlotRoleSymbols.Clear();
        SlotBuffersInUse = 0;
    }

    /// <summary>Hands out a cleared pooled slot buffer, growing the pool on demand.</summary>
    /// <returns>The cleared reusable buffer.</returns>
    private List<int> NextSlotBuffer()
    {
        if(SlotBuffersInUse == SlotBufferPool.Count)
        {
            SlotBufferPool.Add([]);
        }

        List<int> buffer = SlotBufferPool[SlotBuffersInUse];
        SlotBuffersInUse++;
        buffer.Clear();

        return buffer;
    }

    /// <summary>Resets the odometer cursor to the origin for the given slot count.</summary>
    /// <param name="slotCount">The number of odometer slots.</param>
    private void ResetCursor(int slotCount)
    {
        Cursor.Clear();
        for(int i = 0; i < slotCount; i++)
        {
            Cursor.Add(0);
        }
    }

    /// <summary>Advances the odometer to the next combination.</summary>
    /// <returns><see langword="true"/> when a next combination exists, <see langword="false"/> when the enumeration is exhausted.</returns>
    private bool Advance()
    {
        for(int i = Cursor.Count - 1; i >= 0; i--)
        {
            int next = Cursor[i] + 1;
            if(next < SlotBuffers[i].Count)
            {
                Cursor[i] = next;

                return true;
            }

            Cursor[i] = 0;
        }

        return false;
    }

    /// <summary>Applies the Pred/Succ substitution to a context literal, parameterised by the two variable images (Table 2 condition 5): the ordinary <c>σ = {x↦f(x), y↦x}</c> passes <c>(f(x), x)</c>, the root-predecessor <c>σ = {x↦f(o), y↦o}</c> passes <c>(f(o), o)</c>. Equality and inequality literals translate term-wise — the extended <c>Pr</c> equality triggers (<c>x ≈ y</c>, <c>x ≈ o</c>, <c>y ≈ o</c>) cross edges under the same substitution as atoms.</summary>
    /// <param name="literal">The context literal.</param>
    /// <param name="centralImage">The image of the central variable <c>x</c>.</param>
    /// <param name="contextImage">The image of the context variable <c>y</c>.</param>
    /// <returns>The substituted literal.</returns>
    private static DlLiteral ApplyPredSigma(DlLiteral literal, DlTerm centralImage, DlTerm contextImage)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Concept => DlLiteral.Concept(literal.Symbol, PredImage(literal.First, centralImage, contextImage)),
            DlLiteralKind.Role => DlLiteral.Role(literal.Symbol, PredImage(literal.First, centralImage, contextImage), PredImage(literal.Second, centralImage, contextImage)),
            DlLiteralKind.Equality => DlLiteral.Equality(PredImage(literal.First, centralImage, contextImage), PredImage(literal.Second, centralImage, contextImage)),
            _ => DlLiteral.Inequality(PredImage(literal.First, centralImage, contextImage), PredImage(literal.Second, centralImage, contextImage)),
        };
    }

    /// <summary>Whether a head literal is an in-grammar ontology equality or inequality (the DL-clause a-equalities of the published grammar): both terms drawn from the neighbour variables, function terms, the central variable, and named individuals — the DL4 counting <c>zᵢ approx zⱼ</c>, DL2 witness-distinctness <c>fᵢ not-approx fⱼ</c>, DL8 <c>x approx o</c>, and constant-fact <c>o approx o′</c> shapes.</summary>
    /// <param name="literal">The head literal.</param>
    /// <returns><see langword="true"/> for an in-grammar equality or inequality literal.</returns>
    private static bool IsInGrammarEqualityHead(DlLiteral literal)
    {
        return literal.Kind is DlLiteralKind.Equality or DlLiteralKind.Inequality
            && IsOntologyEqualityTerm(literal.First)
            && IsOntologyEqualityTerm(literal.Second);
    }

    /// <summary>Whether a term may appear in an ontology-clause (in)equality head: a neighbour variable, a function term, the central variable, or a named individual.</summary>
    /// <param name="term">The term.</param>
    /// <returns><see langword="true"/> for an admitted term.</returns>
    private static bool IsOntologyEqualityTerm(DlTerm term)
    {
        return term.Kind is DlTermKind.Neighbour or DlTermKind.Function or DlTermKind.Central or DlTermKind.Individual;
    }

    /// <summary>Whether every head literal of an ontology clause is in the §1.4 ontology grammar: a concept or role atom, or an in-grammar (neighbour / function) equality or inequality literal.</summary>
    /// <param name="clause">The ontology clause.</param>
    /// <returns><see langword="true"/> when every head literal is in-grammar.</returns>
    private static bool OntologyHeadInGrammar(DlClause clause)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            if(!head[i].IsAtom && !IsInGrammarEqualityHead(head[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The Pred/Succ image of one term: the central variable maps to the central image, the context variable to the context image; every other term is fixed.</summary>
    /// <param name="term">The term.</param>
    /// <param name="centralImage">The image of <c>x</c>.</param>
    /// <param name="contextImage">The image of <c>y</c>.</param>
    /// <returns>The image term.</returns>
    private static DlTerm PredImage(DlTerm term, DlTerm centralImage, DlTerm contextImage)
    {
        return term.Kind switch
        {
            DlTermKind.Central => centralImage,
            DlTermKind.Context => contextImage,
            _ => term,
        };
    }

    /// <summary>Inverts the ordinary Pred substitution <c>σ = {x↦f(x), y↦x}</c> term-wise: the body-literal shape whose sigma-image is the given predecessor head literal, when one exists — <c>f(x)</c> maps back to <c>x</c>, <c>x</c> to <c>y</c>, ground terms to themselves; a slot holding <c>y</c>, a different function symbol, or a neighbour has no pre-image.</summary>
    /// <param name="head">The predecessor head literal.</param>
    /// <param name="function">The function symbol f.</param>
    /// <returns>The pre-image body-literal shape, or <see langword="null"/> when the head is not a sigma-image for this function.</returns>
    private static DlLiteral? InvertPredSigma(DlLiteral head, int function)
    {
        if(InvertPredTerm(head.First, function) is not DlTerm first)
        {
            return null;
        }

        if(head.Kind == DlLiteralKind.Concept)
        {
            return first.IsGround ? null : DlLiteral.Concept(head.Symbol, first);
        }

        if(InvertPredTerm(head.Second, function) is not DlTerm second)
        {
            return null;
        }

        if(first.IsGround && second.IsGround)
        {
            //A fully ground pre-image is a carried conjunct Ci, never a premise-resolved
            //body atom Ai, so a landed ground head unlocks no Pred instance here.
            return null;
        }

        return head.Kind switch
        {
            DlLiteralKind.Role => DlLiteral.Role(head.Symbol, first, second),
            DlLiteralKind.Equality => DlLiteral.Equality(first, second),
            _ => DlLiteral.Inequality(first, second),
        };
    }

    /// <summary>The ordinary-Pred pre-image of one head term: <c>f(x)</c> (over the edge's function) maps to <c>x</c>, <c>x</c> to <c>y</c>, a ground term to itself; anything else has none.</summary>
    /// <param name="term">The head term.</param>
    /// <param name="function">The edge's function symbol.</param>
    /// <returns>The pre-image term, or <see langword="null"/>.</returns>
    private static DlTerm? InvertPredTerm(DlTerm term, int function)
    {
        return term.Kind switch
        {
            DlTermKind.Function when term.Index == function => DlTerm.Central,
            DlTermKind.Central => DlTerm.Context,
            DlTermKind.Individual or DlTermKind.FunctionOfIndividual => term,
            _ => null,
        };
    }

    /// <summary>
    /// The Succ-trigger term a head literal carries — <c>f(x)</c> in an ordinary
    /// context, <c>f(o)</c> on the root context (Table 2 Succ: "A contains
    /// f(x)", or <c>f(o)</c> for some <c>o</c> on <c>vr</c>). For a concept or
    /// role atom it is the function-bearing term the atom bears; for an oriented
    /// equality or inequality head it is the maximal (first) side's term, so Succ
    /// triggers for the merged successor. The extension reports one term through
    /// the single out-param and does not cover a second function-bearing slot;
    /// that coverage is redundant on every current emission path, since each
    /// witness function is minted in DL2 and immediately emitted in a concept
    /// and role head that enqueue Succ before any inequality or Eq-derived
    /// equality over it exists, and Eq mints no symbol. The single-term reading
    /// is kept for fidelity under a loop-set-ranging equality-bearing clause.
    /// </summary>
    /// <param name="atom">The head literal.</param>
    /// <param name="trigger">The packed trigger term when the literal bears one.</param>
    /// <returns><see langword="true"/> when the literal carries a Succ-trigger term.</returns>
    private static bool TryGetSuccTrigger(DlLiteral atom, out DlTerm trigger)
    {
        if(IsFunctionBearing(atom.First))
        {
            trigger = atom.First;

            return true;
        }

        if(atom.Kind == DlLiteralKind.Role && IsFunctionBearing(atom.Second))
        {
            trigger = atom.Second;

            return true;
        }

        trigger = default;

        return false;
    }

    /// <summary>Whether a canonical literal span is a subset of the singleton set of one atom: empty, or exactly that atom — the Definition-4 read-off shape.</summary>
    /// <param name="span">The canonical span.</param>
    /// <param name="atom">The singleton's atom.</param>
    /// <returns><see langword="true"/> when the span is empty or holds exactly the atom.</returns>
    private static bool IsSubsetOfSingle(ReadOnlySpan<DlLiteral> span, DlLiteral atom)
    {
        return span.Length == 0 || (span.Length == 1 && span[0].Equals(atom));
    }

    /// <summary>Appends a literal span to a conclusion scratch list; the union's duplicates are canonicalised away by <see cref="DlClause.Create"/>.</summary>
    /// <param name="literalsToAppendTo">The scratch list the span is appended to.</param>
    /// <param name="span">The literals to append.</param>
    private static void AppendSpan(List<DlLiteral> literalsToAppendTo, ReadOnlySpan<DlLiteral> span)
    {
        for(int i = 0; i < span.Length; i++)
        {
            literalsToAppendTo.Add(span[i]);
        }
    }

    /// <summary>Appends a premise head's literals EXCEPT the selected one to a conclusion scratch list — the residual disjuncts a premise carries into an ordered-resolution conclusion (KR 2016 Table 2's <c>⋁∆ᵢ</c>); duplicates are canonicalised away by <see cref="DlClause.Create"/>.</summary>
    /// <param name="literalsToAppendTo">The scratch list the residual is appended to.</param>
    /// <param name="head">The premise's head span.</param>
    /// <param name="selectedIndex">The index of the premise's selected literal, skipped.</param>
    private static void AppendResidual(List<DlLiteral> literalsToAppendTo, ReadOnlySpan<DlLiteral> head, int selectedIndex)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(i != selectedIndex)
            {
                literalsToAppendTo.Add(head[i]);
            }
        }
    }

    /// <summary>Appends a premise head's literals EXCEPT the fired one, matched BY VALUE — the residual carry for join sites where the fired literal is known as the lookup image rather than an index; exact because heads are canonical duplicate-free sets.</summary>
    /// <param name="literalsToAppendTo">The scratch list the residual is appended to.</param>
    /// <param name="head">The premise's head span.</param>
    /// <param name="fired">The literal the premise fired on, skipped.</param>
    private static void AppendResidualExcept(List<DlLiteral> literalsToAppendTo, ReadOnlySpan<DlLiteral> head, DlLiteral fired)
    {
        for(int i = 0; i < head.Length; i++)
        {
            if(!head[i].Equals(fired))
            {
                literalsToAppendTo.Add(head[i]);
            }
        }
    }

    /// <summary>The greatest head literal of a clause matching a role shape (symbol and central position) under the selection order — the literal the role-shape index registered for this clause, hence the literal a free-neighbour join slot fired on. Same-shape role literals are always comparable (same symbol tier, argument-lexicographic), so the greatest is the registered maximal.</summary>
    /// <param name="clause">The premise clause.</param>
    /// <param name="roleSymbol">The slot's role symbol.</param>
    /// <param name="centralFirst">Whether the central variable is the first argument in the slot's shape.</param>
    /// <param name="kind">The context kind whose selection order applies.</param>
    /// <returns>The greatest matching head literal.</returns>
    private DlLiteral FindGreatestRoleLiteral(DlClause clause, int roleSymbol, bool centralFirst, ContextGrammarKind kind)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        DlLiteral best = default;
        bool found = false;
        for(int i = 0; i < head.Length; i++)
        {
            DlLiteral literal = head[i];
            if(literal.Kind == DlLiteralKind.Role && literal.Symbol == roleSymbol && literal.First.IsCentral == centralFirst
                && (!found || Order.CompareHeadLiterals(literal, best, kind) > 0))
            {
                best = literal;
                found = true;
            }
        }

        Debug.Assert(found, "A clause returned by the role-shape index carries a head literal of the looked-up shape.");

        return best;
    }

    /// <summary>Indexes the ontology clauses and appends the virtual <c>Bottom(x)→⊥</c> clause that restores the empty-class semantics of the interned Bottom atom.</summary>
    /// <param name="clauses">The clausified ontology.</param>
    private void IndexOntology(IReadOnlyList<DlClause> clauses)
    {
        for(int i = 0; i < clauses.Count; i++)
        {
            RegisterOntologyClause(clauses[i]);
        }

        RegisterOntologyClause(DlClause.Create([DlLiteral.Concept(ContextSymbolTable.Bottom, DlTerm.Central)], [], DerivedOrigin));
    }

    /// <summary>Registers one ontology clause: an empty body joins the per-context firing list; otherwise every body position joins the concept or role trigger index, and a clause whose non-empty head consists entirely of equalities is marked Nom-eligible.</summary>
    /// <param name="clause">The ontology clause.</param>
    private void RegisterOntologyClause(DlClause clause)
    {
        Debug.Assert(OntologyHeadInGrammar(clause), "Every ontology clause head literal is a concept or role atom, or an in-grammar equality or inequality literal (the DL1 union, DL4 counting, DL2 witness-distinctness, DL8, and constant-fact heads of the published grammar).");

        if(clause.BodyLength == 0)
        {
            EmptyBodyOntologyClauses.Add(clause);

            return;
        }

        int clauseIndex = NonEmptyOntologyClauses.Count;
        ReadOnlySpan<DlLiteral> body = clause.Body;
        for(int position = 0; position < body.Length; position++)
        {
            DlLiteral atom = body[position];
            if(atom.Kind == DlLiteralKind.Concept)
            {
                Debug.Assert(atom.First.IsCentral, "An ontology body concept atom is on the central variable.");
                Append(OntologyConceptBody, atom.Symbol, (clauseIndex, position));
            }
            else
            {
                Debug.Assert(atom.Kind == DlLiteralKind.Role, "An ontology body literal is a concept or role atom.");
                Append(OntologyRoleBody, (atom.Symbol, atom.First.IsCentral), (clauseIndex, position));
            }
        }

        NonEmptyOntologyClauses.Add(clause);
        bool nomEligible = clause.Head.Length > 0;
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length && nomEligible; i++)
        {
            nomEligible = head[i].Kind == DlLiteralKind.Equality;
        }

        NomEligibleByClause.Add(nomEligible);
        if(nomEligible)
        {
            NomEligibleOntologyClauses.Add(clauseIndex);
        }
    }

    /// <summary>Builds the extended trigger sets: the successor triggers are <c>Su(O)</c> plus the virtual clause's <c>Bottom(x)</c> body atom; the predecessor triggers gain <c>Bottom(y)</c>, Definition 2 applied to the extended clause set.</summary>
    /// <param name="clausification">The clausification.</param>
    private void BuildTriggerSets(ClausificationResult clausification)
    {
        foreach(DlLiteral trigger in clausification.Order.Su)
        {
            SuccessorTriggers.Add(trigger);
        }

        DlLiteral bottomCentral = DlLiteral.Concept(ContextSymbolTable.Bottom, DlTerm.Central);
        if(!clausification.Order.Su.Contains(bottomCentral))
        {
            SuccessorTriggers.Add(bottomCentral);
        }

        PredecessorTriggers.Add(DlLiteral.Concept(ContextSymbolTable.Bottom, DlTerm.Context));
    }

    /// <summary>Builds the cautious strategy's filler map from the ontology clause heads: a function symbol maps to concept B exactly when the heads carry one distinct concept atom <c>B(f(x))</c> over it.</summary>
    /// <param name="clauses">The clausified ontology.</param>
    private void BuildUniqueFillerMap(IReadOnlyList<DlClause> clauses)
    {
        Dictionary<int, int> firstFiller = [];
        HashSet<int> ambiguous = [];
        for(int i = 0; i < clauses.Count; i++)
        {
            ReadOnlySpan<DlLiteral> head = clauses[i].Head;
            for(int j = 0; j < head.Length; j++)
            {
                DlLiteral atom = head[j];
                if(atom.Kind == DlLiteralKind.Concept && atom.First.IsFunction)
                {
                    RecordFiller(firstFiller, ambiguous, atom.First.Index, atom.Symbol);
                }
            }
        }

        foreach(KeyValuePair<int, int> entry in firstFiller)
        {
            if(!ambiguous.Contains(entry.Key))
            {
                UniqueFillerByFunction[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>Records one concept filler occurrence for a function symbol, marking the function ambiguous when a second distinct filler appears.</summary>
    /// <param name="firstFiller">The first-seen filler per function.</param>
    /// <param name="ambiguous">The functions with more than one distinct filler.</param>
    /// <param name="function">The function symbol.</param>
    /// <param name="filler">The concept filler.</param>
    private static void RecordFiller(Dictionary<int, int> firstFiller, HashSet<int> ambiguous, int function, int filler)
    {
        if(firstFiller.TryGetValue(function, out int existing))
        {
            if(existing != filler)
            {
                ambiguous.Add(function);
            }

            return;
        }

        firstFiller[function] = filler;
    }

    /// <summary>Appends a body-position entry to a trigger index, creating the list on first use.</summary>
    /// <typeparam name="TKey">The index key type.</typeparam>
    /// <param name="index">The trigger index.</param>
    /// <param name="key">The key.</param>
    /// <param name="site">The (clause index, body position) entry.</param>
    private static void Append<TKey>(Dictionary<TKey, List<(int ClauseIndex, int Position)>> index, TKey key, (int ClauseIndex, int Position) site) where TKey : notnull
    {
        if(!index.TryGetValue(key, out List<(int ClauseIndex, int Position)>? sites))
        {
            sites = [];
            index[key] = sites;
        }

        sites.Add(site);
    }
}
