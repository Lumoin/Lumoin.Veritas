namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The work the consequence-based context-saturation engine spent on one module
/// decision: whether the context engine itself produced the verdict, the
/// budget-gated attempts the run spent, the rule applications each of the eight
/// live rules ran, the contexts it created and reused, the clauses it derived
/// and eliminated, and the largest single context's clause count. It is the
/// context arm's counterpart to <see cref="ElSaturationStatistics"/> and
/// <see cref="Lumoin.Veritas.Core.Sat.SatSolveStatistics"/>: a decision the
/// context engine did not decide leaves this empty and carries the fallback's
/// own totals instead.
/// </summary>
/// <param name="ContextDecided">Whether the context-saturation engine produced the module's verdict — the path signal analogous to <see cref="ElSaturationStatistics.ElDecided"/>. <see langword="false"/> in three cases: the module was delegated to the fallback without the context engine running (every counter zero); an admitted module's saturation abstained on the inference budget through a standalone decision surface (counters non-zero, the decision's outcome is the abstention); or, behind the seam, an admitted module's saturation exhausted the budget and the seam delegated to the fallback (counters non-zero ALONGSIDE the fallback's own totals; the outcome is the fallback's — decided in the common case, an abstention when the fallback exhausts its own budget too). Only a <see langword="true"/> value marks a context verdict.</param>
/// <param name="InferenceAttempts">The budget-gated attempts the saturation spent — every conclusion offered to a context, every datatype-sidecar oracle invocation, and every Succ expansion, productive or redundant — the single accumulator the inference budget bounds. A budget-exhausted run holds exactly the ceiling; zero when delegated.</param>
/// <param name="RuleApplications">The added-conclusion total the saturation spent: the Core, Hyper, Succ, Pred, Elim, Eq, Ineq, Factor, and data-clash applications together with each datatype-sidecar oracle invocation. It exceeds the sum of the per-rule fields below on a data-bearing module, whose oracle ticks carry no paired per-rule counter; zero when delegated.</param>
/// <param name="CoreApplications">The Core-rule applications the saturation ran; zero when delegated.</param>
/// <param name="HyperApplications">The Hyper-rule applications the saturation ran; zero when delegated.</param>
/// <param name="SuccApplications">The Succ-rule applications the saturation ran; zero when delegated.</param>
/// <param name="PredApplications">The Pred-rule applications the saturation ran; zero when delegated.</param>
/// <param name="ElimApplications">The Elim-rule applications the saturation ran; zero when delegated.</param>
/// <param name="EqApplications">The Eq-rule applications the saturation ran — the equality rewrites of a target head literal under a derived context equality; zero when delegated.</param>
/// <param name="IneqApplications">The Ineq-rule applications the saturation ran — the false <c>t not-approx t</c> head disjuncts dropped (the Horn case's sole disjunct collapses the head to the empty clause); zero when delegated.</param>
/// <param name="FactorApplications">The Factor-rule applications the saturation ran — the equality-factoring conclusions added when a non-selected positive equality disjunct shares the selected equality's maximal side; zero when delegated and on every Horn module (a single-literal head has no second disjunct to factor).</param>
/// <param name="DataClashApplications">The data-clash applications the saturation ran — the <c>⋃Body → Bottom(x)</c> clauses the datatype sidecar injected on a concrete-domain clash; zero when delegated or when no admitted data demand clashed.</param>
/// <param name="JoinApplications">The Join-rule applications the saturation ran — the intra-context ground resolutions of the nominal calculus; zero when delegated or without nominal jurisdiction.</param>
/// <param name="RootSuccApplications">The r-Succ applications the saturation ran — root edges opened with their tautology seeds; zero when delegated or without nominal jurisdiction.</param>
/// <param name="RootPredApplications">The r-Pred applications the saturation ran — root-clause completions landed in ordinary contexts, the n-zero broadcasts included; zero when delegated or without nominal jurisdiction.</param>
/// <param name="NomApplications">The Nom-rule applications the saturation ran — generated-nominal disjunctions added to the root context; zero when delegated, without nominal jurisdiction, or when the module lacks any of inverse roles, nominals, and number restrictions (the rule's co-occurrence trigger).</param>
/// <param name="ContextsCreated">The contexts the saturation created; zero when delegated.</param>
/// <param name="ContextsReused">The contexts the saturation reused from the registry rather than creating; zero when delegated.</param>
/// <param name="ClausesDerived">The context clauses the saturation added; zero when delegated.</param>
/// <param name="ClausesEliminated">The context clauses the saturation removed by backward subsumption; zero when delegated.</param>
/// <param name="MaxContextClauses">The largest clause count any single context held; zero when delegated.</param>
/// <param name="PreMergeUnions">The <c>SameIndividual</c> unions the pre-merge pass performed (distinct individual representatives merged) before clausification; zero when delegated or when the module carries no admitted ABox merge.</param>
/// <param name="GroundContextsCreated">The ground contexts the setup minted, one per individual representative mentioned in an admitted ABox axiom; zero when delegated or when the module carries no admitted ABox axiom.</param>
/// <param name="GroundEdgesSeeded">The designated ground-target function edges the Succ rule added, routing an asserted object-property edge to its representative's ground context; zero when delegated or when no asserted edge fired.</param>
/// <param name="GroundClashes">The ground clashes decided: a pre-merge representative collision, a closure clash over the asserted-edge graph, or a post-saturation Self-ghost re-closure clash — one counter across the three sites; zero when no ground clash fired.</param>
/// <param name="GeneratedNominals">The generated nominals the Nom rule minted through the bounded in-saturation channel; zero when the rule never fired.</param>
/// <param name="MaxNominalLabelDepth">The deepest generated-nominal label minted — the label-depth statistic the termination wedge observes; zero without minting.</param>
/// <param name="RootContextClauses">The largest live clause count any root-class context reached — a watermark over the whole root class (the one shared root, or the per-individual nominal roots under the fragmented topology); zero without nominal jurisdiction.</param>
/// <param name="RootEdges">The nominal-labelled root edges the r-Succ rule added, a module total across every root-class target; zero without nominal jurisdiction.</param>
public readonly record struct ContextSaturationStatistics(
    bool ContextDecided,
    long InferenceAttempts,
    long RuleApplications,
    long CoreApplications,
    long HyperApplications,
    long SuccApplications,
    long PredApplications,
    long ElimApplications,
    long EqApplications,
    long IneqApplications,
    long FactorApplications,
    long DataClashApplications,
    long JoinApplications,
    long RootSuccApplications,
    long RootPredApplications,
    long NomApplications,
    int ContextsCreated,
    int ContextsReused,
    int ClausesDerived,
    int ClausesEliminated,
    int MaxContextClauses,
    int PreMergeUnions,
    int GroundContextsCreated,
    int GroundEdgesSeeded,
    int GroundClashes,
    int GeneratedNominals,
    int MaxNominalLabelDepth,
    int RootContextClauses,
    int RootEdges)
{
    /// <summary>The empty statistics: no context saturation decided the module.</summary>
    public static ContextSaturationStatistics Empty => default;

    /// <summary>The derived-merge fixpoint rounds the reasoner ran for the decision: zero when no key machinery engaged, one for a single saturation whose post-saturation join found nothing, and one more per seeded re-clausification a key-forced union demanded. Assembled by the reasoner, not the engine.</summary>
    public int MergeRounds { get; init; }

    /// <summary>The candidate representatives the key joins enumerated across descriptors and rounds; zero when the module carries no key descriptor. Assembled by the reasoner.</summary>
    public int KeyJoinCandidates { get; init; }

    /// <summary>The key-forced unions the joins performed across rounds — the derived ground merges of the decision; zero when no key fired. Assembled by the reasoner.</summary>
    public int KeyForcedUnions { get; init; }

    /// <summary>The told ground-counting pigeonhole clashes the rider decided; zero with the rider dark or when no constraint clashed. Assembled by the reasoner off the clash reason.</summary>
    public int GroundCountingClashes { get; init; }

    /// <summary>Whether nominals, object number restrictions, and inverse roles co-occur in the decided module — the Nom rule's trigger census (the rule cannot fire without all three), recorded by the survey and assembled by the reasoner; <see langword="false"/> when the module was delegated without a survey pass or lacks any of the three legs.</summary>
    public bool NominalCountingInverseCooccurrence { get; init; }

    /// <summary>The conclusions the saturation offered to a context that were already contained up to redundancy, so no clause was inserted — the derivation-funnel churn signal. Its ratio against <see cref="InferenceAttempts"/> separates a productive saturation from one re-deriving clauses the subset-subsumption redundancy relation keeps rejecting; zero when delegated. Assembled by the engine.</summary>
    public long RedundantConclusions { get; init; }

    /// <summary>The conclusions the saturation dropped as tautologies — a reflexive <c>s ≈ s</c> disjunct or a complementary <c>s ≈ t</c> / <c>s ≉ t</c> pair — the first funnel stage, ahead of the grammar and redundancy gates; zero when delegated. Dropped at the Eq and Factor pre-charge gate for their materialized conclusions (spending no budget) and at head normalization for every other rule's; the counter spans both sites. Assembled by the engine.</summary>
    public long TautologyDrops { get; init; }

    /// <summary>The containment absorptions at the saturation's single insertion gate whose container was an EXACT DUPLICATE of the offered conclusion — the fast-path half of <see cref="RedundantConclusions"/>, which the pair sums to. Measures that gate alone: the containment probes the successor, hypothesis-cover, and relevance-seed sites run answer before a conclusion is offered and charge neither counter. Assembled by the engine.</summary>
    public long DuplicateContainmentHits { get; init; }

    /// <summary>The containment absorptions at the saturation's single insertion gate whose container was a strictly more general clause — an index-drawn subsumer or the live empty clause — the scan half of <see cref="RedundantConclusions"/>. Measures that gate alone, the counterpart of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long SubsumedContainmentHits { get; init; }

    /// <summary>The conclusions the saturation refused because a derived head literal left the context-kind grammar (the promoted in-saturation guard that also latches the named out-of-grammar delegation) — the funnel stage between the tautology drop and the redundancy gate; zero when delegated. Assembled by the engine.</summary>
    public long OutOfGrammarConclusions { get; init; }

    /// <summary>The clause-landed events the saturation enqueued on the worklist — the conclusions that survived the tautology, grammar, and redundancy gates and were inserted, counted once at the single enqueue site. The head of the derivation funnel: its ratio against <see cref="InferenceAttempts"/> reads what fraction of budget-gated attempts reached the worklist rather than being spent on discarded conclusions; zero when delegated. Assembled by the engine.</summary>
    public long WorklistEnqueues { get; init; }

    /// <summary>The disjunctive data-marker probes the refutation rule ran — one per sidecar decide over a pool-plus-marker obligation set; zero when delegated or without a disjunctive data marker. Assembled by the engine.</summary>
    public long DisjunctiveDataProbes { get; init; }

    /// <summary>The disjunctive data markers recorded refuted against their context's unit pool, counted once per (context, marker); zero when no probe clashed. Assembled by the engine.</summary>
    public long DisjunctiveDataRefutations { get; init; }

    /// <summary>The body-conditioned residual-head narrowing clauses the refutation rule inserted; it can exceed the refutation count when several disjunctive clauses or contributor combinations ride one refuted marker. Assembled by the engine.</summary>
    public long DisjunctiveDataNarrowings { get; init; }

    /// <summary>The contexts the fixpoint certification decided <c>Consistent</c> over their survivor joint obligation set — each certified context's disjunctive markers are jointly realizable, so the claimed whole verdict carries them; zero without a surviving disjunctive marker. Assembled by the engine.</summary>
    public long DisjunctiveDataCertifications { get; init; }

    /// <summary>The contexts the fixpoint certification could not certify — a clashing or undecided survivor joint set, each latching the undecided-obligation delegation; zero on a module the lane decides whole. Assembled by the engine.</summary>
    public long UncertifiedDisjunctiveDataLatches { get; init; }

    /// <summary>The times the undecided-data-obligation delegation latched, across the unit rule's undecided path, an undecided disjunctive probe, and an uncertified fixpoint joint set — the numeric funnel counter behind the boolean latch. Assembled by the engine.</summary>
    public long UndecidedDataObligationCount { get; init; }

    /// <summary>The live root-class heads carrying an off-fold equality between key-candidate or demand-bearing constants at the completed fixpoint — the numeric count behind the <c>HasRootEqualityOutsideFold</c> boolean backstop latch. A nonzero value is the measured corpus demand for the root-tier equality-relay completeness upgrade; zero when the fold covered every root equality, the module was delegated for another reason before the backstop scan, or the engine is unarmed. Assembled by the engine.</summary>
    public long RootEqualityOutsideFoldHeads { get; init; }

    /// <summary>The guard-site refusals of a <c>DerivedUnderChoice</c> root equality across the saturation — the numeric count behind the <c>HasRootEqualityRidesAChoice</c> boolean general relay latch. A nonzero value is a decision-time observation that a choice-riding root equality was refused the ≈-class fold, the Pred relay, the r-Pred broadcast, or the unconditional-head projection, and the module was delegated named; zero when no choice-riding equality was refused. Assembled by the engine.</summary>
    public long RootEqualityRidesAChoiceHeads { get; init; }

    /// <summary>The negative-polarity data dual disjuncts the clausifier minted — one per subclass-position data existential or has-value lowered to its universal-marker NNF dual; the attribution counter separating this widening's delegation-rate movement from the fixed batteries. Assembled by the reasoner off the clausification.</summary>
    public long NegativePolarityDataMarkersMinted { get; init; }

    /// <summary>The r-Pred offers the ground-relevance filter blocked — swept completions refused at the odometer and broadcast images refused at a target context together — each spending no budget; zero under the unrestricted default mode. Assembled by the engine.</summary>
    public long RootPredFilteredOffers { get; init; }

    /// <summary>The r-Pred re-offers the relevance triggers fired — a blocked root clause re-attempted or a blocked broadcast replayed into one context when a qualifying ground head or bridge premise first landed there; zero under the unrestricted default mode. Assembled by the engine.</summary>
    public long RootPredReofferedByGroundHead { get; init; }

    /// <summary>The downward relevance tautologies <c>A → A</c> the ground-relevance compensation inserted into ordinary successors of a context selecting a ground atom; zero under the unrestricted default mode. Assembled by the engine.</summary>
    public long RelevanceTautologiesSeeded { get; init; }

    /// <summary>The r-Pred applications whose sweep entered at root-clause registration — the eligible clause's own first sweep over the existing root edges. The four origin counters partition <see cref="RootPredApplications"/>. Assembled by the engine.</summary>
    public long RootPredFromRegistrationSweep { get; init; }

    /// <summary>The r-Pred applications whose sweep entered at a newly added root edge — the registered clauses naming the edge's constant re-attempted at the edge's source. Assembled by the engine.</summary>
    public long RootPredFromNewRootEdge { get; init; }

    /// <summary>The r-Pred applications whose sweep entered at a landed premise in one context — the site-2 <c>Sur</c>-image dispatch, and under the filtered mode the relevance re-offers, both being landed-premise-driven re-attempts restricted to that context. Assembled by the engine.</summary>
    public long RootPredFromPremise { get; init; }

    /// <summary>The r-Pred applications landed by the n-zero broadcast path — the live broadcast over the existing contexts, the seeding replay into each new context, and under the filtered mode the re-offered broadcast replays. Assembled by the engine.</summary>
    public long RootPredFromBroadcast { get; init; }

    /// <summary>The r-Pred conclusions OFFERED to a context from the registration-sweep origin — one per conclusion that reached the insertion gate, landed or not, so the pair with <see cref="RootPredFromRegistrationSweep"/> reads the origin's accept rate. Never below its landing counterpart. Assembled by the engine.</summary>
    public long RootPredRegistrationSweepOffers { get; init; }

    /// <summary>The r-Pred conclusions offered to a context from the new-root-edge origin, landed or not; never below <see cref="RootPredFromNewRootEdge"/>. Assembled by the engine.</summary>
    public long RootPredNewRootEdgeOffers { get; init; }

    /// <summary>The r-Pred conclusions offered to a context from the landed-premise origin, landed or not; never below <see cref="RootPredFromPremise"/>. Assembled by the engine.</summary>
    public long RootPredPremiseOffers { get; init; }

    /// <summary>The r-Pred conclusions offered to a context from the n-zero broadcast origin, landed or not; never below <see cref="RootPredFromBroadcast"/>. Assembled by the engine.</summary>
    public long RootPredBroadcastOffers { get; init; }

    /// <summary>The registration-sweep origin's offers the insertion gate absorbed as EXACT DUPLICATES — the origin-keyed share of <see cref="DuplicateContainmentHits"/>, so the offer flood's concentration is read per rule-invocation site. Charged at that gate alone; a subsumer absorption charges nothing here. Assembled by the engine.</summary>
    public long RootPredRegistrationSweepDuplicateHits { get; init; }

    /// <summary>The new-root-edge origin's offers the insertion gate absorbed as exact duplicates. Assembled by the engine.</summary>
    public long RootPredNewRootEdgeDuplicateHits { get; init; }

    /// <summary>The landed-premise origin's offers the insertion gate absorbed as exact duplicates. Assembled by the engine.</summary>
    public long RootPredPremiseDuplicateHits { get; init; }

    /// <summary>The broadcast origin's offers the insertion gate absorbed as exact duplicates. Assembled by the engine.</summary>
    public long RootPredBroadcastDuplicateHits { get; init; }

    /// <summary>The join-family conclusions OFFERED to a context — every Join form-(a) and form-(b) conclusion that reached the insertion gate, landed or not, counted at the single join conclusion sink. Never below <see cref="JoinApplications"/>, which counts the landed share. Assembled by the engine.</summary>
    public long JoinOffers { get; init; }

    /// <summary>The join-family offers the insertion gate absorbed as EXACT DUPLICATES — the origin-keyed share of <see cref="DuplicateContainmentHits"/>, so the offer flood's concentration is read per rule-invocation site. Charged at that gate alone; a subsumer absorption charges nothing here. Assembled by the engine.</summary>
    public long JoinDuplicateHits { get; init; }

    /// <summary>The Core seeds OFFERED to a context — one per seed that reached the insertion gate, landed or not, so the pair with <see cref="CoreApplications"/> reads the channel's accept rate. Never below its landing counterpart. Assembled by the engine.</summary>
    public long CoreOffers { get; init; }

    /// <summary>The Core seeds the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long CoreDuplicateHits { get; init; }

    /// <summary>The Hyper conclusions OFFERED to a context, landed or not; never below <see cref="HyperApplications"/>. Assembled by the engine.</summary>
    public long HyperOffers { get; init; }

    /// <summary>The Hyper offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long HyperDuplicateHits { get; init; }

    /// <summary>The Pred conclusions OFFERED to a predecessor, landed or not; never below <see cref="PredApplications"/>. Assembled by the engine.</summary>
    public long PredOffers { get; init; }

    /// <summary>The Pred offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long PredDuplicateHits { get; init; }

    /// <summary>The Pred conclusions OFFERED to a predecessor from the landed-target driver, landed or not — the driver-keyed share of <see cref="PredOffers"/>, which the three driver columns sum to. Assembled by the engine.</summary>
    public long PredLandedTargetOffers { get; init; }

    /// <summary>The Pred conclusions offered to a predecessor from the landed-premise driver, landed or not. Assembled by the engine.</summary>
    public long PredLandedPremiseOffers { get; init; }

    /// <summary>The Pred conclusions offered to a predecessor from the new-edge driver, landed or not. Assembled by the engine.</summary>
    public long PredNewEdgeOffers { get; init; }

    /// <summary>The landed-target driver's offers the insertion gate absorbed as EXACT DUPLICATES — the driver-keyed share of <see cref="PredDuplicateHits"/>, which the three driver duplicate columns sum to; a subsumer absorption charges nothing here. Assembled by the engine.</summary>
    public long PredLandedTargetDuplicateHits { get; init; }

    /// <summary>The landed-premise driver's offers the insertion gate absorbed as exact duplicates. Assembled by the engine.</summary>
    public long PredLandedPremiseDuplicateHits { get; init; }

    /// <summary>The new-edge driver's offers the insertion gate absorbed as exact duplicates. Assembled by the engine.</summary>
    public long PredNewEdgeDuplicateHits { get; init; }

    /// <summary>The Pred odometer invocations that reached their combination cursor — an attempt refused earlier, because some nonground target body position had no live premise, charges nothing, so the pair with <see cref="PredOffers"/> reads the offers each entered run produced. Assembled by the engine.</summary>
    public long PredOdometerRuns { get; init; }

    /// <summary>The Pred exact-duplicate absorptions that landed on an odometer run's SECOND or later charged offer — the within-run successor-offer duplicate count, an upper-bound proxy for intra-odometer convergence read against <see cref="PredDuplicateHits"/>, since a later-in-run duplicate may still absorb against a clause that predates the run. Assembled by the engine.</summary>
    public long PredIntraRunDuplicateHits { get; init; }

    /// <summary>The origin-merge re-enqueues of a surviving absorber — the one re-entry into the dispatch loop the per-rule offer counters cannot see; zero wherever no clause carries the choice tag, whose push-side twin is <see cref="EqScopeTagJoins"/>. Assembled by the engine.</summary>
    public long OriginClearReenqueues { get; init; }

    /// <summary>The Eq rewrite conclusions OFFERED to a context, landed or not — the conclusions past the constant, paramodulation-scope, and pre-charge tautology gates, which reach no insertion gate at all; never below <see cref="EqApplications"/>. Assembled by the engine.</summary>
    public long EqOffers { get; init; }

    /// <summary>The Eq offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long EqDuplicateHits { get; init; }

    /// <summary>The equality-factoring conclusions OFFERED to a context, landed or not; never below <see cref="FactorApplications"/>. Assembled by the engine.</summary>
    public long FactorOffers { get; init; }

    /// <summary>The Factor offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long FactorDuplicateHits { get; init; }

    /// <summary>The Succ hypothesis and unconditional-K1 seeds OFFERED to a successor, landed or not. This channel's attempt-to-offer relation is one-to-many, unlike every per-offer-gated channel: <see cref="SuccApplications"/> counts EXPANSIONS, each of which offers a whole K2 hypothesis set and, at a designated ground target, its K1 set as well, so this counter stands above the expansion count rather than beside it. Assembled by the engine.</summary>
    public long SuccOffers { get; init; }

    /// <summary>The Succ seed offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long SuccDuplicateHits { get; init; }

    /// <summary>The Nom disjunction conclusions OFFERED to a root context, landed or not; never below <see cref="NomApplications"/>. Assembled by the engine.</summary>
    public long NomOffers { get; init; }

    /// <summary>The Nom offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long NomDuplicateHits { get; init; }

    /// <summary>The push-landing arrivals OFFERED to a root-class context, landed or not — the r-Succ seed landings and the inter-nominal carrier images together, counted at the one physical seam both origins reach the clause set through. Assembled by the engine.</summary>
    public long PushedArrivalOffers { get; init; }

    /// <summary>The push-landing arrivals the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>; the duplicate-image absorption bounding the reciprocal-imaging cascade is counted here. Assembled by the engine.</summary>
    public long PushedArrivalDuplicateHits { get; init; }

    /// <summary>The sidecar-driven seeds OFFERED to a context, landed or not — the root data clash, the data clash combinations, the disjunctive data narrowings, and the downward relevance tautologies together; zero on a run driving neither an admitted data restriction nor the ground-filtered relevance mode. Assembled by the engine.</summary>
    public long SidecarSeedOffers { get; init; }

    /// <summary>The sidecar-driven seed offers the insertion gate absorbed as exact duplicates — the origin-keyed share of <see cref="DuplicateContainmentHits"/>. Assembled by the engine.</summary>
    public long SidecarSeedDuplicateHits { get; init; }

    /// <summary>The root-class context population: one under the single-root topology with nominal jurisdiction, the resolved per-individual nominal roots under the fragmented topology, zero without nominal jurisdiction. Assembled by the engine.</summary>
    public int NominalRootContexts { get; init; }

    /// <summary>The inter-nominal carrier landings — whole-head <c>[x/src][o_i/x]</c> images inserted into a foreign individual's nominal-root context; zero under the single-root default. Assembled by the engine.</summary>
    public long InterNominalPropagations { get; init; }

    /// <summary>The inter-nominal carrier offers the redundancy discipline absorbed — the duplicate-image absorption that bounds the reciprocal-imaging cascade; zero under the single-root default. Assembled by the engine.</summary>
    public long InterNominalRedundant { get; init; }

    /// <summary>The scopable Eq rewrites the license-scoped atom axis blocked in query-initialized contexts because the acted-on target was not a query atom; zero under every other paramodulation scope. Assembled by the engine.</summary>
    public long EqScopeBlockedQueryAtom { get; init; }

    /// <summary>The scopable Eq rewrites the license-scoped context axis blocked in root-class contexts under the fragmented topology because the acting equality clause carried no push provenance; zero under every other scope and under the single-root topology. Assembled by the engine.</summary>
    public long EqScopeBlockedRootClass { get; init; }

    /// <summary>The push-provenance tag joins the redundancy discipline performed under the license scope — an absorbed pushed derivation OR-ed onto an untagged surviving absorber, which is re-enqueued; zero under every other scope. Assembled by the engine.</summary>
    public long EqScopeTagJoins { get; init; }

    /// <summary>The registration-sweep origin's r-Pred offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="RootPredRegistrationSweepDuplicateHits"/> instead. Assembled by the engine.</summary>
    public long RootPredRegistrationSweepSubsumedHits { get; init; }

    /// <summary>The new-root-edge origin's r-Pred offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long RootPredNewRootEdgeSubsumedHits { get; init; }

    /// <summary>The landed-premise origin's r-Pred offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long RootPredPremiseSubsumedHits { get; init; }

    /// <summary>The broadcast origin's r-Pred offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long RootPredBroadcastSubsumedHits { get; init; }

    /// <summary>The join-family offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>; an exact duplicate charges <see cref="JoinDuplicateHits"/> instead. Assembled by the engine.</summary>
    public long JoinSubsumedHits { get; init; }

    /// <summary>The Core seeds the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long CoreSubsumedHits { get; init; }

    /// <summary>The Hyper offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long HyperSubsumedHits { get; init; }

    /// <summary>The Pred offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long PredSubsumedHits { get; init; }

    /// <summary>The landed-target driver's Pred offers absorbed into a strictly more general live clause — the driver-keyed share of <see cref="PredSubsumedHits"/>. Assembled by the engine.</summary>
    public long PredLandedTargetSubsumedHits { get; init; }

    /// <summary>The landed-premise driver's Pred offers absorbed into a strictly more general live clause — the driver-keyed share of <see cref="PredSubsumedHits"/>. Assembled by the engine.</summary>
    public long PredLandedPremiseSubsumedHits { get; init; }

    /// <summary>The new-edge driver's Pred offers absorbed into a strictly more general live clause — the driver-keyed share of <see cref="PredSubsumedHits"/>. Assembled by the engine.</summary>
    public long PredNewEdgeSubsumedHits { get; init; }

    /// <summary>The Pred applications landed from the landed-target driver — the driver-keyed share of <see cref="PredApplications"/>. Assembled by the engine.</summary>
    public long PredLandedTargetLandings { get; init; }

    /// <summary>The Pred applications landed from the landed-premise driver — the driver-keyed share of <see cref="PredApplications"/>. Assembled by the engine.</summary>
    public long PredLandedPremiseLandings { get; init; }

    /// <summary>The Pred applications landed from the new-edge driver — the driver-keyed share of <see cref="PredApplications"/>. Assembled by the engine.</summary>
    public long PredNewEdgeLandings { get; init; }

    /// <summary>The Eq offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long EqSubsumedHits { get; init; }

    /// <summary>The Factor offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long FactorSubsumedHits { get; init; }

    /// <summary>The Succ seed offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long SuccSubsumedHits { get; init; }

    /// <summary>The Nom offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long NomSubsumedHits { get; init; }

    /// <summary>The push-landing arrivals the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long PushedArrivalSubsumedHits { get; init; }

    /// <summary>The sidecar-driven seed offers the insertion gate absorbed into a strictly more general live clause — the origin-keyed share of <see cref="SubsumedContainmentHits"/>. Assembled by the engine.</summary>
    public long SidecarSeedSubsumedHits { get; init; }

    /// <summary>The join-family offering RUNS: one per dispatched (landed clause, dispatch face) that charged at least one offer. Charged lazily on a run's first charged offer, because the join family has no single combination cursor — so a dispatch finding no candidate counts no run. This is the ONE semantic difference from <see cref="PredOdometerRuns"/>, which counts cursor-reaching invocations whether or not they offer. Assembled by the engine.</summary>
    public long JoinOfferingRuns { get; init; }

    /// <summary>The join exact-duplicate absorptions landing on a run's SECOND or later charged offer — the within-run share of <see cref="JoinDuplicateHits"/>, an upper-bound proxy for intra-run convergence since a later-in-run duplicate may still absorb against a clause predating the run. Assembled by the engine.</summary>
    public long JoinIntraRunDuplicateHits { get; init; }

    /// <summary>The Eq offering RUNS: one per dispatched (landed clause, maximal literal) that charged at least one offer, both rewrite directions included, plus one per redrive firing. Charged lazily on a run's first charged offer, because an Eq dispatch has no single combination cursor. Assembled by the engine.</summary>
    public long EqOfferingRuns { get; init; }

    /// <summary>The Eq exact-duplicate absorptions landing on a run's SECOND or later charged offer — the within-run share of <see cref="EqDuplicateHits"/>, an upper-bound proxy for intra-run convergence. Assembled by the engine.</summary>
    public long EqIntraRunDuplicateHits { get; init; }

    /// <summary>The n-zero r-Pred broadcast population: the context-independent images accumulated over the run, one of which is offered into every ordinary context. Assembled by the engine.</summary>
    public int RootBroadcastClauseCount { get; init; }

    /// <summary>The signature-bounded ceiling of the cautious registry's single-atom-core fill — the distinct concept filler atoms the frozen signature admits as candidate cores. Assembled by the engine.</summary>
    public int CautiousCoreCeiling { get; init; }

    /// <summary>The filler cores the context registry holds a context for, never above <see cref="CautiousCoreCeiling"/> — the fill numerator. Registry state is counted, not provenance: a filler core a ground or query tier registered counts as filled. Assembled by the engine.</summary>
    public int CautiousCoresRegistered { get; init; }

    /// <summary>The head-occurrence index entries every context registered — one per head literal of every inserted clause; the MAINTAINED cost of the backward-subsumption sweep's head index. Assembled by the engine.</summary>
    public long HeadOccurrenceEntriesRegistered { get; init; }

    /// <summary>The body-occurrence index entries every context registered — one per body literal of every inserted clause. Assembled by the engine.</summary>
    public long BodyOccurrenceEntriesRegistered { get; init; }

    /// <summary>The distinct head-occurrence keys every context holds — the maintained key breadth beside the entry count. Assembled by the engine.</summary>
    public long HeadOccurrenceDistinctKeys { get; init; }

    /// <summary>The distinct body-occurrence keys every context holds. Assembled by the engine.</summary>
    public long BodyOccurrenceDistinctKeys { get; init; }

    /// <summary>The backward-subsumption sweeps that reached the posting path — the CONSULTED side's invocation count; the empty clause's own sweep probes no occurrence index and charges nothing. Assembled by the engine.</summary>
    public long SurvivorSweepProbes { get; init; }

    /// <summary>The posting entries the backward-subsumption sweeps walked — the CONSULTED cost read against the registered entries and keys. Assembled by the engine.</summary>
    public long SurvivorSweepPostingEntriesWalked { get; init; }

    /// <summary>The Pred dispatches whose predecessor ran the constant-anchored root machinery — the arm that fans one target out over the anchoring constants; the landed-premise path reads its single anchor off the premise's own terms and charges neither arm column. Assembled by the engine.</summary>
    public long PredAnchoredArmDispatches { get; init; }

    /// <summary>The Pred dispatches whose predecessor took the ordinary arm — every non-root predecessor and every nominal root, which spells its own constant central; the two arm columns together count every dispatch of that path. Assembled by the engine.</summary>
    public long PredOrdinaryArmDispatches { get; init; }

    /// <summary>The anchored-arm dispatches whose target is anchor-invariant — every body AND every head literal ground, so each anchoring constant completes the same conclusion; charged on the test's verdict, whether or not the surviving completion then runs. Assembled by the engine.</summary>
    public long PredAnchorInvariantTargetPasses { get; init; }

    /// <summary>The Pred offers the anchor hoist elided: the completions the anchored arm's remaining constants would have charged on an anchor-invariant target, credited whenever the next constant's own gate would have admitted them. EXACT on unbounded and population-bounded runs, an upper bound under an attempt bound. Assembled by the engine.</summary>
    public long PredAnchorPruned { get; init; }

    /// <summary>The Pred offers the ordinary arm elided: the completions a sigma-invariant broadcast image the predecessor already holds would have charged, credited whenever the elided offer's own gate would have admitted it. EXACT on unbounded and population-bounded runs, an upper bound under an attempt bound. Assembled by the engine.</summary>
    public long PredBroadcastContainedSkips { get; init; }

    /// <summary>The ordinary-arm dispatches whose target is sigma-invariant — every body AND every head literal ground, so the ordinary sigma leaves both spans alone and the completion is the target itself; charged on the test's verdict, whether or not the containment skip then fires. Assembled by the engine.</summary>
    public long PredOrdinaryInvariantTargetPasses { get; init; }

    /// <summary>The sigma-invariant ordinary-arm targets that are registered broadcast images, whether or not the predecessor holds the image — the two residues the skip does not reach are read off this column and its two neighbours. Assembled by the engine.</summary>
    public long PredBroadcastImageTargets { get; init; }

    /// <summary>The enumeration-CSP habitat class the census-first recognizer assigned the module at survey time — a census label on every context-arm decision and abstention, never a verdict. Assembled by the reasoner off the survey.</summary>
    public EnumerationHabitatClass EnumerationHabitat { get; init; }

    /// <summary>The clash-only (face-one) enumeration-decider decisions: one when the nominal-funnel counting face decided the told clash pre-engine — a forced-merge collapse of a told-distinct pair or a counted told-distinct clique exceeding its cap; zero with the face dark or silent. Assembled by the reasoner off the clash reason.</summary>
    public int EnumerationDeciderClashes { get; init; }

    /// <summary>The enumeration-algebra consistency decisions: one when the certifying face (face two) certified the module consistent with its exact subsumption set pre-engine, or the pair-certify face (face eight) did so past the member-universe window; zero with the deciding face dark or silent. <see cref="EnumerationPairVectorCount"/> names which mechanism decided. Assembled by the reasoner.</summary>
    public int EnumerationDeciderCertifications { get; init; }

    /// <summary>The enumeration-algebra refutations: one when the certifying face (face two) decided the module inconsistent by exhausting every equality partition pre-engine, or the pair-clash face (face seven) did so by exhausting every assignment vector past the member-universe window; zero with the deciding face dark or silent. <see cref="EnumerationPairVectorCount"/> names which mechanism decided. Assembled by the reasoner.</summary>
    public int EnumerationDeciderRefutations { get; init; }

    /// <summary>The face-one silences charged to the funnel-chain hop bound: the told subclass chain from <c>owl:Thing</c> exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextNominalCountingDecider.FunnelChainHopBound"/> before reaching the funnel shape, so the face stayed silent on that funnel. Assembled by the reasoner off the clausification.</summary>
    public int EnumerationWindowExceededChainHops { get; init; }

    /// <summary>The face-one silences charged to the counted-population bound: a cap anchor's counted successor population exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextNominalCountingDecider.CountedPopulationBound"/>, so the face stayed silent on that cap. Assembled by the reasoner off the clausification.</summary>
    public int EnumerationWindowExceededPopulation { get; init; }

    /// <summary>The face-two silences charged to the member-universe bound: the deduplicated named-individual universe exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextEnumerationAlgebraDecider.MemberUniverseBound"/>, so the certifying face stayed silent on the module. Assembled by the reasoner.</summary>
    public int EnumerationWindowExceededMembers { get; init; }

    /// <summary>The face-two silences charged to the signature-class bound: the module's named-class count exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextEnumerationAlgebraDecider.SignatureClassBound"/>, so the certifying face stayed silent on the module. Assembled by the reasoner.</summary>
    public int EnumerationWindowExceededClasses { get; init; }

    /// <summary>The largest counted successor population any cap axiom's window measurement read — sources enumerated, told-Same deduplicated, qualified-filler filtered — before any boundary comparison ran; zero when no cap was measured. Assembled by the reasoner off the clausification.</summary>
    public int EnumerationCountedPopulation { get; init; }

    /// <summary>The largest pairwise told-distinct clique the window measurement found inside a counted population; zero when no cap was measured. Assembled by the reasoner off the clausification.</summary>
    public int EnumerationDistinctCliqueSize { get; init; }

    /// <summary>The cap bound <c>k</c> paired with <see cref="EnumerationCountedPopulation"/> — the bound of the cap axiom whose counted population is reported; zero when no cap was measured. Assembled by the reasoner off the clausification.</summary>
    public int EnumerationCapBound { get; init; }

    /// <summary>The deduplicated named-individual member universe the enumeration-algebra measurement read over the whole module; zero when the module is not enumeration-algebra shaped. Assembled by the reasoner.</summary>
    public int EnumerationMemberUniverse { get; init; }

    /// <summary>The distinct pairwise-disjoint anchors <c>m</c> the partition template's existential fillers name, deduplicated by class identity and landed before any boundary check; zero when the partition jurisdiction rejected the module. Assembled by the reasoner.</summary>
    public int PartitionAnchorCount { get; init; }

    /// <summary>The existential conjunct count <c>n</c> of the partition template's intersection, landed before any boundary check; zero when the partition jurisdiction rejected the module. Assembled by the reasoner.</summary>
    public int PartitionRestrictionCount { get; init; }

    /// <summary>The unqualified max-cardinality bound <c>k</c> the partition template's single counting conjunct carries, landed before any boundary check; zero when the partition jurisdiction rejected the module. Assembled by the reasoner.</summary>
    public int PartitionCapBound { get; init; }

    /// <summary>The partition-clash (face-three) decisions: one when the distinct anchors outnumbered the told cap and the closed-form pigeonhole refuted the module pre-engine; zero with the face dark or silent. Assembled by the reasoner.</summary>
    public int PartitionDeciderClashes { get; init; }

    /// <summary>The partition-certify (face-four) decisions: one when the distinct anchors fitted inside the told cap and the closed-form witness model certified the module consistent pre-engine; zero with the face dark or silent. Assembled by the reasoner.</summary>
    public int PartitionDeciderCertifications { get; init; }

    /// <summary>The partition silences charged to the anchor bound: the distinct anchors exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextPartitionCountingDecider.PartitionAnchorBound"/>, so the faces stayed silent on the module. Assembled by the reasoner.</summary>
    public int PartitionWindowExceededAnchors { get; init; }

    /// <summary>The partition silences charged to the existential-conjunct bound: the template's existential restrictions exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextPartitionCountingDecider.PartitionRestrictionBound"/>, so the faces stayed silent on the module. Assembled by the reasoner.</summary>
    public int PartitionWindowExceededRestrictions { get; init; }

    /// <summary>The gadget-property atoms the boolean-cardinality-gadget compilation minted — one per property carrying a bare boolean cardinality gadget, landed before any boundary check; zero when the gadget jurisdiction rejected the module. Assembled by the reasoner.</summary>
    public int GadgetPropertyAtomCount { get; init; }

    /// <summary>The free-class atoms the boolean-cardinality-gadget compilation minted — one per named class no told equivalence determines, landed before any boundary check; zero when the gadget jurisdiction rejected the module. Assembled by the reasoner.</summary>
    public int GadgetFreeClassAtomCount { get; init; }

    /// <summary>The assignments the gadget faces' bounded walk evaluated: the first passing assignment's index plus one on a certification, the whole <c>2^F</c> free space on a refutation — <c>F</c> the atoms surviving defined-atom elimination — and zero on every dark, silent, and census-only pass. Assembled by the reasoner.</summary>
    public int GadgetEvaluatedVectorCount { get; init; }

    /// <summary>The gadget-clash (face-five) decisions: one when every assignment failed and the exhaustion refutation decided the module inconsistent pre-engine; zero with the face dark or silent. Assembled by the reasoner.</summary>
    public int GadgetDeciderClashes { get; init; }

    /// <summary>The gadget-certify (face-six) decisions: one when an assignment passed and its induced witness model certified the module consistent pre-engine; zero with the face dark or silent. Assembled by the reasoner.</summary>
    public int GadgetDeciderCertifications { get; init; }

    /// <summary>The gadget silences charged to the atom bound: the atoms surviving defined-atom elimination exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextBooleanGadgetDecider.GadgetAtomBound"/>, so the faces stayed silent on the module; the two raw atom counts still land on the record. Assembled by the reasoner.</summary>
    public int GadgetWindowExceededAtoms { get; init; }

    /// <summary>The pair count <c>k</c> of the anchor-and-pair composition the enumeration-algebra measurement read past the member-universe window, landed before any boundary comparison — a structural reading computed on every pass, lit and dark alike; zero when the composition did not resolve and on every module inside the member window. Assembled by the reasoner.</summary>
    public int EnumerationPairCount { get; init; }

    /// <summary>The assignment vectors the pair-composition sweep evaluated: the witness vector's index plus one on a certification stopped at its witness, the whole <c>2^k</c> space on a refutation and on an exhaustive read-off, and zero on every dark, silent, and census-only pass — the sweep-ran marker beside the always-computed structural fields. Assembled by the reasoner.</summary>
    public int EnumerationPairVectorCount { get; init; }

    /// <summary>The pair-composition silences charged to the pair bound: the pair count exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextEnumerationAlgebraDecider.PairAssignmentBound"/>, so the faces stayed silent on the module — computed on every pass, lit and dark alike, beside <see cref="EnumerationPairCount"/>. Assembled by the reasoner.</summary>
    public int EnumerationWindowExceededPairs { get; init; }

    /// <summary>The distinct named one-of members <c>n</c> the largest recognized spy-point funnel drives the domain into, deduplicated by individual identity and landed before any boundary check; zero when no spy-point funnel was recognized. Assembled by the reasoner.</summary>
    public int SpyPointMemberCount { get; init; }

    /// <summary>The tightest told domain bound <c>k</c> the spy-point measurement summed — the funnel members' inverse-linked caps added in long arithmetic; zero when no funnel and cap role paired. Assembled by the reasoner.</summary>
    public long SpyPointCapBound { get; init; }

    /// <summary>The effective demand the spy-point measurement compared against <see cref="SpyPointCapBound"/> — the largest told minimum-cardinality demand, never below the nonempty domain's own demand of one; zero when no funnel and cap role paired. Assembled by the reasoner.</summary>
    public long SpyPointDemandBound { get; init; }

    /// <summary>The spy-point clash (face-nine) decisions: one when the told demand outran the told domain bound and the closed-form pigeonhole refuted the module pre-engine; zero with the face dark or silent. The face has no certify counterpart. Assembled by the reasoner off the clash reason.</summary>
    public int SpyPointDeciderClashes { get; init; }

    /// <summary>The spy-point silences charged to the member bound: a funnel's distinct members exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextSpyPointDecider.SpyPointMemberBound"/>, so the face skipped that funnel. Assembled by the reasoner.</summary>
    public int SpyPointWindowExceededMembers { get; init; }

    /// <summary>The size variables the bijection-chain measurement recognized — the distinct named classes carrying a told constraint source, deduplicated by class identity and landed before any boundary check; zero when no source was recognized. Assembled by the reasoner.</summary>
    public int BijectionChainClassCount { get; init; }

    /// <summary>The constraint sources the bijection-chain measurement collected — the told constants, equalities, sums, products, bounds, and the outright asserted-conjunct clash, landed before any boundary check; zero when no source was recognized. Assembled by the reasoner.</summary>
    public int BijectionChainConstraintCount { get; init; }

    /// <summary>The bijection-chain clash (face-ten) decisions: one when the told size propagation reached an impossible state and refuted the module pre-engine; zero with the face dark or silent. Assembled by the reasoner off the outcome.</summary>
    public int BijectionChainDeciderClashes { get; init; }

    /// <summary>The bijection-chain certify (face-eleven) decisions: one when exactly one certificate route — the all-empty vacuity model or the canonical grounded-tower fiber model — validated the whole module and certified it consistent pre-engine; zero with the face dark or silent. Assembled by the reasoner off the outcome.</summary>
    public int BijectionChainDeciderCertifications { get; init; }

    /// <summary>The bijection-chain silences charged to the class bound: the recognized size variables exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextBijectionChainDecider.BijectionChainClassBound"/>, so both faces stayed silent on the module. Assembled by the reasoner.</summary>
    public int BijectionChainWindowExceededClasses { get; init; }

    /// <summary>The carriers the told-ground-witness measurement harvested — the domain size of the described model, one carrier per distinct told individual term and one fresh carrier where the module told none, landed before any boundary check. Assembled by the reasoner.</summary>
    public int ToldGroundWitnessCarrierCount { get; init; }

    /// <summary>The ground role edges the told-ground-witness completion holds — the told object-property assertions closed under told inverse mirroring, or the told edges alone where a window silence stopped the completion. Assembled by the reasoner.</summary>
    public int ToldGroundWitnessEdgeCount { get; init; }

    /// <summary>The told-ground-witness clash (face-twelve) decisions: one when the ground memberships derived a class membership beside its own denial, a told disjoint partner, an asserted empty class, or a denied edge and refuted the module pre-engine; zero with the face dark or silent. Assembled by the reasoner off the outcome.</summary>
    public int ToldGroundWitnessDeciderClashes { get; init; }

    /// <summary>The told-ground-witness certify (face-thirteen) decisions: one when the described model satisfied every axiom on re-check and certified the module consistent pre-engine; zero with the face dark or silent. Assembled by the reasoner off the outcome.</summary>
    public int ToldGroundWitnessDeciderCertifications { get; init; }

    /// <summary>The told-ground-witness silences charged to the ground bounds: the carriers, the named classes, or the roles exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextToldGroundWitnessDecider.ToldGroundWitnessCarrierBound"/>, <see cref="Lumoin.Veritas.Owl.Contexts.ContextToldGroundWitnessDecider.ToldGroundWitnessClassBound"/>, or <see cref="Lumoin.Veritas.Owl.Contexts.ContextToldGroundWitnessDecider.ToldGroundWitnessRoleBound"/>, so both faces stayed silent on the module. Assembled by the reasoner.</summary>
    public int ToldGroundWitnessWindowExceededCarriers { get; init; }

    /// <summary>The carriers the repairing measurement read — the domain size of the proposed model, one carrier per distinct told individual term after the told-sameness quotient plus one per minted witness, and the told term count where no construction ran. Assembled by the reasoner.</summary>
    public int RepairingCarrierCount { get; init; }

    /// <summary>The edges the repairing construction's committed relation holds at the last leaf — the told edges, the deterministic and choice repairs, and the minted witnesses' edges, all under the re-applied closure operator; the told edges alone where a window silence stopped the construction. Assembled by the reasoner.</summary>
    public int RepairingCommittedEdgeCount { get; init; }

    /// <summary>The repairing-ground clash (face-fourteen) decisions: one when the told ground memberships derived a class membership beside its own denial, a told disjoint partner, an asserted empty class, or a denied edge and refuted the module pre-engine; zero with the face dark or silent. Assembled by the reasoner off the outcome.</summary>
    public int RepairingDeciderClashes { get; init; }

    /// <summary>The repairing certify (face-fifteen) decisions: one when a repaired candidate model satisfied every admitted axiom on re-check and certified the module consistent pre-engine; zero with the face dark or silent. Assembled by the reasoner off the outcome.</summary>
    public int RepairingDeciderCertifications { get; init; }

    /// <summary>The repairing silences charged to the ground bounds: the carriers, the named classes, or the roles exceeded <see cref="Lumoin.Veritas.Owl.Contexts.ContextRepairingCertifyDecider.RepairCarrierBound"/>, <see cref="Lumoin.Veritas.Owl.Contexts.ContextRepairingCertifyDecider.RepairClassBound"/>, or <see cref="Lumoin.Veritas.Owl.Contexts.ContextRepairingCertifyDecider.RepairRoleBound"/>, so both faces stayed silent on the module. Assembled by the reasoner.</summary>
    public int RepairingWindowExceededCarriers { get; init; }

    /// <summary>The fresh skolem successors the modal expansion allocated at its stopping point — spawned nodes only, the told level-0 frontier excluded, though <see cref="Lumoin.Veritas.Owl.Contexts.ContextModalRoleExpansionDecider.ModalExpansionNodeBound"/> bounds the two together. Assembled by the reasoner.</summary>
    public int ModalExpansionNodesSpawned { get; init; }

    /// <summary>The deepest spawn level the modal expansion reached, measured from the told frontier at level zero. Assembled by the reasoner.</summary>
    public int ModalExpansionMaxDepthReached { get; init; }

    /// <summary>The largest per-node counted fact set the modal expansion reached — <c>owl:Thing</c> and the intersection concept itself excluded, conjuncts counted individually. Assembled by the reasoner.</summary>
    public int ModalExpansionPeakLabelSize { get; init; }

    /// <summary>The directed edges the modal expansion's structure holds — told, spawn-forward and materialised-inverse counted separately, and never an edge derived from transitivity, since no rule derives one. Assembled by the reasoner.</summary>
    public int ModalExpansionEdgesMaterialised { get; init; }

    /// <summary>The rule firings the modal expansion charged to its stopping point: one per derived fact, an existential spawn charging ONE application for its edge fact and its membership fact together. Assembled by the reasoner.</summary>
    public int ModalExpansionRuleApplications { get; init; }

    /// <summary>The modal-expansion clash (face-sixteen) decisions: one when the bounded expansion reached a node-local numeric contradiction or an asserted empty class and refuted the module pre-engine; zero with the face dark or silent. The face has no certify counterpart. Assembled by the reasoner off the outcome.</summary>
    public int ModalExpansionDeciderClashes { get; init; }

    /// <summary>The modal-expansion silences charged to the five expansion bounds: the node arena, the spawn depth, the per-node label set, the directed edges, or the rule applications reached <see cref="Lumoin.Veritas.Owl.Contexts.ContextModalRoleExpansionDecider.ModalExpansionNodeBound"/>, <see cref="Lumoin.Veritas.Owl.Contexts.ContextModalRoleExpansionDecider.ModalExpansionDepthBound"/>, <see cref="Lumoin.Veritas.Owl.Contexts.ContextModalRoleExpansionDecider.ModalExpansionLabelBound"/>, <see cref="Lumoin.Veritas.Owl.Contexts.ContextModalRoleExpansionDecider.ModalExpansionEdgeBound"/>, or <see cref="Lumoin.Veritas.Owl.Contexts.ContextModalRoleExpansionDecider.ModalExpansionStepBound"/>, so the face stayed silent on the module. The tripping quantity stands at its own ceiling and the other four below theirs. Assembled by the reasoner.</summary>
    public int ModalExpansionWindowSilences { get; init; }

    /// <summary>The free gadget atoms the modal-gadget construction carried into its vector walk — the gadget properties that survived defined-atom elimination, never the module's raw gadget-atom count. The value is the deciding vector's, or the last vector tried on a silence. Assembled by the reasoner, off the outcome.</summary>
    public int ModalGadgetFreeAtomCount { get; init; }

    /// <summary>The deduped successor demands the modal-gadget construction may spawn — a STATIC measurement over the module's existential occurrences taken once at admission, independent of which vector is tried, so it is a property of the module rather than of a walk. Assembled by the reasoner, off the outcome.</summary>
    public int ModalGadgetSignatureCount { get; init; }

    /// <summary>The arena nodes the modal-gadget construction holds — told individuals and spawned skolem successors together, which is the population the node bound covers. The value is the deciding vector's, or the last vector tried on a silence. Assembled by the reasoner, off the outcome.</summary>
    public int ModalGadgetNodesBuilt { get; init; }

    /// <summary>The modal-gadget clash (face-seventeen) decisions: one when the monotone composition closure derived a class membership beside its own told complement, or a told bottom membership, and refuted the module pre-engine; zero with the face dark or silent. Assembled by the reasoner, off the outcome.</summary>
    public int ModalGadgetDeciderClashes { get; init; }

    /// <summary>The modal-gadget certify (face-eighteen) decisions: one when the minted skolem tree satisfied every admitted axiom on re-check against its raw relations and certified the module consistent pre-engine; zero with the face dark or silent. Assembled by the reasoner, off the outcome.</summary>
    public int ModalGadgetDeciderCertifications { get; init; }

    /// <summary>The modal-gadget silences charged to any of the eleven modal-gadget bounds, SUMMED over both faces: the clash face charges its own step bound and nothing else, the certify face charges the other ten, and a field neither face fills is left at its other face's value rather than clobbered with a default. Assembled by the reasoner, off the outcomes.</summary>
    public int ModalGadgetWindowSilences { get; init; }

    /// <summary>The nominal-pinned-role measurement's largest resolved range membership — the deduplicated named one-of members behind the reported resolution, landed before any boundary check. Assembled by the reasoner.</summary>
    public int NominalPinnedRoleMemberCount { get; init; }

    /// <summary>The told self-loops the nominal-pinned-role measurement's reported resolution consumed — the clashing resolution's full member cover, or the largest recognized resolution's covered members on a silence. Assembled by the reasoner.</summary>
    public int NominalPinnedRolePinnedEdgeCount { get; init; }

    /// <summary>The told concept-form edge denials the nominal-pinned-role measurement recognized module-wide — top-level complements of a has-value over a plain role with named carrier and value. Assembled by the reasoner.</summary>
    public int NominalPinnedRoleDeniedEdgeCount { get; init; }

    /// <summary>The nominal-pinned-role clash (face-nineteen) decisions: one when told inverse-functionality and total told self-loops pinned the role's extension into the identity diagonal and a reverse-denied told edge refuted the module pre-engine; zero with the face dark or silent. The face has no certify counterpart. Assembled by the reasoner off the clash reason.</summary>
    public int NominalPinnedRoleDeciderClashes { get; init; }

    /// <summary>The nominal-pinned-role silences charged to the member bound: a range resolution carried more than <see cref="Lumoin.Veritas.Owl.Contexts.ContextNominalPinnedRoleDecider.NominalPinnedRoleMemberBound"/> distinct members, so the face stayed silent on that resolution with the measured count on the record. Assembled by the reasoner.</summary>
    public int NominalPinnedRoleWindowExceededMembers { get; init; }
}
