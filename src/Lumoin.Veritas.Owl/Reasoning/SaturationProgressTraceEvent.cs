using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// One sampled in-saturation progress mark: emitted at every power-of-two
/// <see cref="InferenceAttempts"/> count while a
/// <see cref="SaturationProgressSampler"/> is attached to the saturation
/// engine, carrying the clause population, the derivation funnel, and the
/// worklist depths at that mark. The growth-curve instrument that answers
/// dispatch-versus-population from telemetry alone: a productive saturation
/// grows <see cref="WorklistEnqueues"/> in step with its attempts, while a
/// population-bound one grows <see cref="TautologyDrops"/> and
/// <see cref="RedundantConclusions"/> against a slowly draining queue. The
/// per-decision companion is <see cref="ReasoningDecisionTraceEvent"/>, which
/// carries the same funnel columns once at the decision's end.
/// </summary>
/// <param name="SequenceNumber">The mark's ordinal within its saturation run, consecutive from zero.</param>
/// <param name="TimestampTicks">The emission time in UTC ticks, stamped by the sampler's clock.</param>
/// <param name="CorrelationId">The correlation id linking the run's marks to the decision they belong to.</param>
/// <param name="InferenceAttempts">The budget-gated attempts spent at this mark — a power of two by the sampling rule.</param>
/// <param name="RuleApplications">The added-conclusion total at this mark.</param>
/// <param name="ClausesDerived">The context clauses added at this mark; the live population reads as this minus <paramref name="ClausesEliminated"/>.</param>
/// <param name="ClausesEliminated">The context clauses removed by backward subsumption at this mark.</param>
/// <param name="MaxContextClauses">The largest clause count any single context has held by this mark.</param>
/// <param name="RootContextClauses">The largest live clause count any root-class context has reached by this mark — a watermark over the whole root class; zero without nominal jurisdiction.</param>
/// <param name="ContextsCreated">The contexts created by this mark.</param>
/// <param name="NominalRootContexts">The root-class context population at this mark: one under the single-root topology with nominal jurisdiction, the resolved per-individual nominal roots under the fragmented topology.</param>
/// <param name="TautologyDrops">The conclusions dropped as tautologies by this mark — the funnel's first stage, the pre-charge gates included.</param>
/// <param name="RedundantConclusions">The conclusions rejected as contained up to redundancy by this mark — the funnel's containment stage.</param>
/// <param name="OutOfGrammarConclusions">The conclusions refused by the in-saturation grammar guard by this mark.</param>
/// <param name="WorklistEnqueues">The conclusions that survived every gate and were inserted by this mark — the funnel's head.</param>
/// <param name="QueueDepth">The pending clause-landed events on the ordinary worklist at this mark.</param>
/// <param name="EagerQueueDepth">The pending clause-landed events on the eager (front) worklist at this mark.</param>
/// <param name="SuccQueueDepth">The pending successor-expansion candidates at this mark.</param>
/// <param name="GeneratedNominals">The generated nominals minted through the bounded channel by this mark; zero when the Nom rule has not fired.</param>
/// <param name="MaxNominalLabelDepth">The deepest generated-nominal label minted by this mark; zero without minting.</param>
/// <param name="HyperApplications">The Hyper-rule applications by this mark — the per-rule attribution column set reads which rule the curve's growth rides.</param>
/// <param name="EqApplications">The Eq-rule applications by this mark.</param>
/// <param name="FactorApplications">The Factor-rule applications by this mark.</param>
/// <param name="PredApplications">The Pred-rule applications by this mark.</param>
/// <param name="JoinApplications">The Join-rule applications by this mark.</param>
/// <param name="RootSuccApplications">The r-Succ applications by this mark.</param>
/// <param name="RootPredApplications">The r-Pred applications by this mark.</param>
/// <param name="NomApplications">The Nom-rule applications by this mark.</param>
/// <param name="EnumerationHabitat">The enumeration-CSP habitat class the census-first recognizer assigned the module at survey time — the shape name beside the churn profile the mark's funnel columns carry. Marks exist only on engine-processed runs by construction: a pre-engine decision constructs no engine, so no sampler and no mark.</param>
/// <param name="RootPredFilteredOffers">The r-Pred offers the ground-relevance filter blocked by this mark, swept completions and broadcast images together; zero under the unrestricted default mode.</param>
/// <param name="RelevanceTautologiesSeeded">The downward relevance tautologies the ground-relevance compensation inserted by this mark; zero under the unrestricted default mode.</param>
/// <param name="DuplicateContainmentHits">The insertion-gate absorptions by this mark whose container was an exact duplicate — the fast-path half of <paramref name="RedundantConclusions"/>.</param>
/// <param name="SubsumedContainmentHits">The insertion-gate absorptions by this mark whose container was a strictly more general clause — the scan half of <paramref name="RedundantConclusions"/>; the two halves sum to it.</param>
/// <param name="RootPredRegistrationSweepOffers">The r-Pred conclusions offered from the registration-sweep origin by this mark, landed or not — the offer-side column beside <paramref name="RootPredApplications"/>, which counts only landings.</param>
/// <param name="RootPredNewRootEdgeOffers">The r-Pred conclusions offered from the new-root-edge origin by this mark, landed or not.</param>
/// <param name="RootPredPremiseOffers">The r-Pred conclusions offered from the landed-premise origin by this mark, landed or not.</param>
/// <param name="RootPredBroadcastOffers">The r-Pred conclusions offered from the n-zero broadcast origin by this mark, landed or not.</param>
/// <param name="RootPredRegistrationSweepDuplicateHits">The registration-sweep origin's offers absorbed as exact duplicates by this mark — the origin-keyed share of <paramref name="DuplicateContainmentHits"/>.</param>
/// <param name="RootPredNewRootEdgeDuplicateHits">The new-root-edge origin's offers absorbed as exact duplicates by this mark.</param>
/// <param name="RootPredPremiseDuplicateHits">The landed-premise origin's offers absorbed as exact duplicates by this mark.</param>
/// <param name="RootPredBroadcastDuplicateHits">The broadcast origin's offers absorbed as exact duplicates by this mark.</param>
/// <param name="JoinOffers">The join-family conclusions offered to a context by this mark, landed or not; never below <paramref name="JoinApplications"/>.</param>
/// <param name="JoinDuplicateHits">The join-family offers absorbed as exact duplicates by this mark — the origin-keyed share of <paramref name="DuplicateContainmentHits"/>.</param>
/// <param name="CoreOffers">The Core seeds offered to a context by this mark, landed or not.</param>
/// <param name="CoreDuplicateHits">The Core seeds absorbed as exact duplicates by this mark.</param>
/// <param name="HyperOffers">The Hyper conclusions offered to a context by this mark, landed or not.</param>
/// <param name="HyperDuplicateHits">The Hyper offers absorbed as exact duplicates by this mark.</param>
/// <param name="PredOffers">The Pred conclusions offered to a predecessor by this mark, landed or not.</param>
/// <param name="PredDuplicateHits">The Pred offers absorbed as exact duplicates by this mark.</param>
/// <param name="EqOffers">The Eq rewrite conclusions offered to a context by this mark, landed or not.</param>
/// <param name="EqDuplicateHits">The Eq offers absorbed as exact duplicates by this mark.</param>
/// <param name="FactorOffers">The equality-factoring conclusions offered to a context by this mark, landed or not.</param>
/// <param name="FactorDuplicateHits">The Factor offers absorbed as exact duplicates by this mark.</param>
/// <param name="SuccOffers">The Succ hypothesis and unconditional-K1 seeds offered to a successor by this mark, landed or not; one expansion offers a whole trigger set, so this column stands above the expansion count.</param>
/// <param name="SuccDuplicateHits">The Succ seed offers absorbed as exact duplicates by this mark.</param>
/// <param name="NomOffers">The Nom disjunction conclusions offered to a root context by this mark, landed or not.</param>
/// <param name="NomDuplicateHits">The Nom offers absorbed as exact duplicates by this mark.</param>
/// <param name="PushedArrivalOffers">The push-landing arrivals offered to a root-class context by this mark, landed or not — the r-Succ seed landings and the inter-nominal carrier images together.</param>
/// <param name="PushedArrivalDuplicateHits">The push-landing arrivals absorbed as exact duplicates by this mark.</param>
/// <param name="SidecarSeedOffers">The sidecar-driven seeds offered to a context by this mark, landed or not — the data clashes, the disjunctive narrowings, and the relevance tautologies together.</param>
/// <param name="SidecarSeedDuplicateHits">The sidecar-driven seed offers absorbed as exact duplicates by this mark.</param>
/// <param name="PredLandedTargetOffers">The Pred conclusions offered from the landed-target driver by this mark, landed or not — the driver-keyed share of <paramref name="PredOffers"/>.</param>
/// <param name="PredLandedPremiseOffers">The Pred conclusions offered from the landed-premise driver by this mark, landed or not.</param>
/// <param name="PredNewEdgeOffers">The Pred conclusions offered from the new-edge driver by this mark, landed or not.</param>
/// <param name="PredLandedTargetDuplicateHits">The landed-target driver's offers absorbed as exact duplicates by this mark — the driver-keyed share of <paramref name="PredDuplicateHits"/>.</param>
/// <param name="PredLandedPremiseDuplicateHits">The landed-premise driver's offers absorbed as exact duplicates by this mark.</param>
/// <param name="PredNewEdgeDuplicateHits">The new-edge driver's offers absorbed as exact duplicates by this mark.</param>
/// <param name="PredOdometerRuns">The Pred odometer invocations that reached their combination cursor by this mark; an attempt refused for want of a live premise at some slot charges nothing.</param>
/// <param name="PredIntraRunDuplicateHits">The Pred exact-duplicate absorptions landing on an odometer run's second or later charged offer by this mark — the within-run share of <paramref name="PredDuplicateHits"/>.</param>
/// <param name="OriginClearReenqueues">The origin-merge re-enqueues of a surviving absorber by this mark — the dispatch-loop re-entry the per-rule offer columns cannot see.</param>
/// <param name="RootPredRegistrationSweepSubsumedHits">The registration-sweep origin's r-Pred offers absorbed into a strictly more general live clause by this mark — the origin-keyed share of <paramref name="SubsumedContainmentHits"/>.</param>
/// <param name="RootPredNewRootEdgeSubsumedHits">The new-root-edge origin's r-Pred offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="RootPredPremiseSubsumedHits">The landed-premise origin's r-Pred offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="RootPredBroadcastSubsumedHits">The broadcast origin's r-Pred offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="JoinSubsumedHits">The join-family offers absorbed into a strictly more general live clause by this mark — the origin-keyed share of <paramref name="SubsumedContainmentHits"/>.</param>
/// <param name="CoreSubsumedHits">The Core seeds absorbed into a strictly more general live clause by this mark.</param>
/// <param name="HyperSubsumedHits">The Hyper offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="PredSubsumedHits">The Pred offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="PredLandedTargetSubsumedHits">The landed-target driver's Pred offers absorbed into a strictly more general live clause by this mark — the driver-keyed share of <paramref name="PredSubsumedHits"/>.</param>
/// <param name="PredLandedPremiseSubsumedHits">The landed-premise driver's Pred offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="PredNewEdgeSubsumedHits">The new-edge driver's Pred offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="PredLandedTargetLandings">The Pred applications landed from the landed-target driver by this mark — the driver-keyed share of <paramref name="PredApplications"/>.</param>
/// <param name="PredLandedPremiseLandings">The Pred applications landed from the landed-premise driver by this mark.</param>
/// <param name="PredNewEdgeLandings">The Pred applications landed from the new-edge driver by this mark.</param>
/// <param name="EqSubsumedHits">The Eq offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="FactorSubsumedHits">The Factor offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="SuccSubsumedHits">The Succ seed offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="NomSubsumedHits">The Nom offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="PushedArrivalSubsumedHits">The push-landing arrivals absorbed into a strictly more general live clause by this mark.</param>
/// <param name="SidecarSeedSubsumedHits">The sidecar-driven seed offers absorbed into a strictly more general live clause by this mark.</param>
/// <param name="JoinOfferingRuns">The join-family dispatch runs that charged at least one offer by this mark — charged lazily on a run's first charged offer, so a dispatch finding no candidate counts no run; the one semantic difference from <paramref name="PredOdometerRuns"/>, which counts cursor-reaching invocations whether or not they offer.</param>
/// <param name="JoinIntraRunDuplicateHits">The join exact-duplicate absorptions landing on a run's second or later charged offer by this mark — the within-run share of <paramref name="JoinDuplicateHits"/>.</param>
/// <param name="EqOfferingRuns">The Eq dispatch runs that charged at least one offer by this mark, both rewrite directions and each redrive firing included — charged lazily on a run's first charged offer.</param>
/// <param name="EqIntraRunDuplicateHits">The Eq exact-duplicate absorptions landing on a run's second or later charged offer by this mark — the within-run share of <paramref name="EqDuplicateHits"/>.</param>
/// <param name="RootBroadcastClauseCount">The n-zero r-Pred broadcast population at this mark — the context-independent images accumulated so far.</param>
/// <param name="CautiousCoreCeiling">The signature-bounded ceiling of the cautious registry's single-atom-core fill — fixed for the run.</param>
/// <param name="CautiousCoresRegistered">The filler cores the context registry holds a context for at this mark, never above <paramref name="CautiousCoreCeiling"/>.</param>
/// <param name="HeadOccurrenceEntriesRegistered">The head-occurrence index entries every context had registered by this mark — the maintained cost of the backward-subsumption sweep's head index.</param>
/// <param name="BodyOccurrenceEntriesRegistered">The body-occurrence index entries every context had registered by this mark.</param>
/// <param name="HeadOccurrenceDistinctKeys">The distinct head-occurrence keys every context held at this mark.</param>
/// <param name="BodyOccurrenceDistinctKeys">The distinct body-occurrence keys every context held at this mark.</param>
/// <param name="SurvivorSweepProbes">The backward-subsumption sweeps that reached the posting path by this mark — the consulted side's invocation count.</param>
/// <param name="SurvivorSweepPostingEntriesWalked">The posting entries those sweeps walked by this mark — the consulted cost read against the registered entries and keys.</param>
/// <param name="PredAnchoredArmDispatches">The Pred dispatches taking the constant-anchored root arm by this mark — the arm that fans one target out over the anchoring constants.</param>
/// <param name="PredOrdinaryArmDispatches">The Pred dispatches taking the ordinary arm by this mark — every non-root predecessor and every nominal root; the two arm columns together count every dispatch of that path.</param>
/// <param name="PredAnchorInvariantTargetPasses">The anchored-arm dispatches by this mark whose target is anchor-invariant — every body and every head literal ground, so each constant completes the same conclusion.</param>
/// <param name="PredAnchorPruned">The Pred offers the anchor hoist elided by this mark — the completions the remaining constants would have charged on an anchor-invariant target; exact on unbounded and population-bounded runs.</param>
/// <param name="PredBroadcastContainedSkips">The Pred offers the ordinary arm elided by this mark — the completions a sigma-invariant broadcast image the predecessor already holds would have charged; exact on unbounded and population-bounded runs.</param>
/// <param name="PredOrdinaryInvariantTargetPasses">The ordinary-arm dispatches by this mark whose target is sigma-invariant — every body and every head literal ground, so the completion is the target itself.</param>
/// <param name="PredBroadcastImageTargets">The sigma-invariant ordinary-arm targets by this mark that are registered broadcast images, whether or not the predecessor holds the image.</param>
public readonly record struct SaturationProgressTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    long InferenceAttempts,
    long RuleApplications,
    int ClausesDerived,
    int ClausesEliminated,
    int MaxContextClauses,
    int RootContextClauses,
    int ContextsCreated,
    int NominalRootContexts,
    long TautologyDrops,
    long RedundantConclusions,
    long OutOfGrammarConclusions,
    long WorklistEnqueues,
    int QueueDepth,
    int EagerQueueDepth,
    int SuccQueueDepth,
    int GeneratedNominals,
    int MaxNominalLabelDepth,
    long HyperApplications,
    long EqApplications,
    long FactorApplications,
    long PredApplications,
    long JoinApplications,
    long RootSuccApplications,
    long RootPredApplications,
    long NomApplications,
    EnumerationHabitatClass EnumerationHabitat,
    long RootPredFilteredOffers,
    long RelevanceTautologiesSeeded,
    long DuplicateContainmentHits,
    long SubsumedContainmentHits,
    long RootPredRegistrationSweepOffers,
    long RootPredNewRootEdgeOffers,
    long RootPredPremiseOffers,
    long RootPredBroadcastOffers,
    long RootPredRegistrationSweepDuplicateHits,
    long RootPredNewRootEdgeDuplicateHits,
    long RootPredPremiseDuplicateHits,
    long RootPredBroadcastDuplicateHits,
    long JoinOffers,
    long JoinDuplicateHits,
    long CoreOffers,
    long CoreDuplicateHits,
    long HyperOffers,
    long HyperDuplicateHits,
    long PredOffers,
    long PredDuplicateHits,
    long EqOffers,
    long EqDuplicateHits,
    long FactorOffers,
    long FactorDuplicateHits,
    long SuccOffers,
    long SuccDuplicateHits,
    long NomOffers,
    long NomDuplicateHits,
    long PushedArrivalOffers,
    long PushedArrivalDuplicateHits,
    long SidecarSeedOffers,
    long SidecarSeedDuplicateHits,
    long PredLandedTargetOffers,
    long PredLandedPremiseOffers,
    long PredNewEdgeOffers,
    long PredLandedTargetDuplicateHits,
    long PredLandedPremiseDuplicateHits,
    long PredNewEdgeDuplicateHits,
    long PredOdometerRuns,
    long PredIntraRunDuplicateHits,
    long OriginClearReenqueues,
    long RootPredRegistrationSweepSubsumedHits,
    long RootPredNewRootEdgeSubsumedHits,
    long RootPredPremiseSubsumedHits,
    long RootPredBroadcastSubsumedHits,
    long JoinSubsumedHits,
    long CoreSubsumedHits,
    long HyperSubsumedHits,
    long PredSubsumedHits,
    long PredLandedTargetSubsumedHits,
    long PredLandedPremiseSubsumedHits,
    long PredNewEdgeSubsumedHits,
    long PredLandedTargetLandings,
    long PredLandedPremiseLandings,
    long PredNewEdgeLandings,
    long EqSubsumedHits,
    long FactorSubsumedHits,
    long SuccSubsumedHits,
    long NomSubsumedHits,
    long PushedArrivalSubsumedHits,
    long SidecarSeedSubsumedHits,
    long JoinOfferingRuns,
    long JoinIntraRunDuplicateHits,
    long EqOfferingRuns,
    long EqIntraRunDuplicateHits,
    int RootBroadcastClauseCount,
    int CautiousCoreCeiling,
    int CautiousCoresRegistered,
    long HeadOccurrenceEntriesRegistered,
    long BodyOccurrenceEntriesRegistered,
    long HeadOccurrenceDistinctKeys,
    long BodyOccurrenceDistinctKeys,
    long SurvivorSweepProbes,
    long SurvivorSweepPostingEntriesWalked,
    long PredAnchoredArmDispatches,
    long PredOrdinaryArmDispatches,
    long PredAnchorInvariantTargetPasses,
    long PredAnchorPruned,
    long PredBroadcastContainedSkips,
    long PredOrdinaryInvariantTargetPasses,
    long PredBroadcastImageTargets): ITraceEvent;
