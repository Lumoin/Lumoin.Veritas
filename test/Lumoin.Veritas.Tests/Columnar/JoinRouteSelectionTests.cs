using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The join-route selector seam at unit level: the shipped structural and manual rules over hand-built
/// features, the decision value types and their telemetry identity, and the shape analysis the engine
/// measures a decision on.
/// </summary>
[TestClass]
internal sealed class JoinRouteSelectionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The predicate the fixtures join on.</summary>
    private static TermId Knows { get; } = TermId.FromEncoded(100);

    /// <summary>The fanned chain's first hop predicate.</summary>
    private static TermId Reaches { get; } = TermId.FromEncoded(200);

    /// <summary>The fanned chain's second hop predicate.</summary>
    private static TermId Carries { get; } = TermId.FromEncoded(201);

    /// <summary>The star's first arm predicate.</summary>
    private static TermId FirstArm { get; } = TermId.FromEncoded(300);

    /// <summary>The star's second arm predicate.</summary>
    private static TermId SecondArm { get; } = TermId.FromEncoded(301);

    /// <summary>The star's third arm predicate.</summary>
    private static TermId ThirdArm { get; } = TermId.FromEncoded(302);

    /// <summary>The identity a deployment-supplied selector names itself with in these rows.</summary>
    private static JoinStrategySelectorKind SuppliedKind { get; } = JoinStrategySelectorKind.Create(1002);

    /// <summary>A cyclic core on a six-order view takes the Free Join generic join.</summary>
    [TestMethod]
    public void StructuralSelectorRoutesACyclicShapeToFreeJoin()
    {
        JoinSelectionContext context = ContextFor(FeaturesOf(acyclic: false, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true));

        JoinSelectionDecision decision = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);

        Assert.AreEqual(QueryEngineKind.FreeJoin, decision.Route);
        Assert.AreEqual(JoinSelectionReason.CyclicCore, decision.Reason);
        Assert.AreEqual(JoinStrategySelectorKind.Structural, decision.SelectorKind);
    }

    /// <summary>A disconnected (cartesian) shape on a six-order view takes the Free Join generic join.</summary>
    [TestMethod]
    public void StructuralSelectorRoutesADisconnectedShapeToFreeJoin()
    {
        JoinSelectionContext context = ContextFor(FeaturesOf(acyclic: true, componentCount: 2, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true));

        JoinSelectionDecision decision = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);

        Assert.AreEqual(QueryEngineKind.FreeJoin, decision.Route);
        Assert.AreEqual(JoinSelectionReason.DisconnectedComponents, decision.Reason);
        Assert.AreEqual(JoinStrategySelectorKind.Structural, decision.SelectorKind);
    }

    /// <summary>An acyclic connected shape keeps the batched scan-and-hash route the stand measures as the winner: neither new arm fires.</summary>
    [TestMethod]
    public void StructuralSelectorKeepsAnAcyclicConnectedShapeOnTheBatchedRoute()
    {
        JoinSelectionContext context = ContextFor(FeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true));

        JoinSelectionDecision decision = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);

        Assert.AreEqual(QueryEngineKind.ColumnarBatched, decision.Route);
        Assert.AreEqual(JoinSelectionReason.AcyclicBatched, decision.Reason);
    }

    /// <summary>With the batched route disabled by policy an acyclic connected shape falls to the leapfrog driver — never to Free Join.</summary>
    [TestMethod]
    public void StructuralSelectorFallsToLeapfrogWhenTheBatchedRouteIsDisabled()
    {
        JoinSelectionContext context = ContextFor(FeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: false));

        JoinSelectionDecision decision = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);

        Assert.AreEqual(QueryEngineKind.Columnar, decision.Route);
        Assert.AreEqual(JoinSelectionReason.SoundDefault, decision.Reason);
    }

    /// <summary>A reduced order set keeps every shape on the route it has today: the shape engagements are scoped to a six-order view.</summary>
    [TestMethod]
    public void StructuralSelectorLeavesAReducedOrderSetOnTodaysRoute()
    {
        JoinSelectionContext context = ContextFor(FeaturesOf(acyclic: false, componentCount: 2, ColumnarOrderSetMode.ThreeRotations, batchedRouteEligible: true));

        JoinSelectionDecision decision = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);

        Assert.AreEqual(QueryEngineKind.ColumnarBatched, decision.Route);
    }

    /// <summary>The manual rule is the opt-out: it follows the policy flags with no shape engagement at all.</summary>
    [TestMethod]
    public void ManualSelectorReproducesTheRoutingBeforeTheStructuralRule()
    {
        JoinSelectionContext cyclic = ContextFor(FeaturesOf(acyclic: false, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true));
        JoinSelectionContext disconnected = ContextFor(FeaturesOf(acyclic: true, componentCount: 2, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true));

        JoinSelectionDecision cyclicDecision = JoinStrategySelectors.Manual(in cyclic, TestContext.CancellationToken);
        JoinSelectionDecision disconnectedDecision = JoinStrategySelectors.Manual(in disconnected, TestContext.CancellationToken);

        Assert.AreEqual(QueryEngineKind.ColumnarBatched, cyclicDecision.Route);
        Assert.AreEqual(JoinStrategySelectorKind.Manual, cyclicDecision.SelectorKind);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, disconnectedDecision.Route);
        Assert.AreEqual(JoinStrategySelectorKind.Manual, disconnectedDecision.SelectorKind);
    }

    /// <summary>The unconsulted-seam discriminator sits at code zero, so a decision no selector took reads as one.</summary>
    [TestMethod]
    public void SelectorKindNoneIsTheDefaultValue()
    {
        Assert.AreEqual(0, default(JoinStrategySelectorKind).Code);
        Assert.AreEqual(JoinStrategySelectorKind.None, default(JoinStrategySelectorKind));
        Assert.AreEqual(JoinStrategySelectorKind.None, default(JoinSelectionDecision).SelectorKind);
    }

    /// <summary>A deployment names its own selector, and the registry both keeps it and keeps the built-ins.</summary>
    [TestMethod]
    public void SelectorKindCreateRegistersANewIdentity()
    {
        JoinStrategySelectorKind created = JoinStrategySelectorKind.Create(1000);

        Assert.AreNotEqual(JoinStrategySelectorKind.None, created);
        Assert.AreNotEqual(JoinStrategySelectorKind.Forced, created);
        Assert.AreNotEqual(JoinStrategySelectorKind.Structural, created);
        Assert.AreNotEqual(JoinStrategySelectorKind.Manual, created);

        IReadOnlyList<JoinStrategySelectorKind> all = JoinStrategySelectorKind.All;

        Assert.IsTrue(Holds(all, created), "The created identity is registered, and the registry snapshot's copy equals it.");
        Assert.IsTrue(Holds(all, JoinStrategySelectorKind.None), "The registry still holds the built-in none.");
        Assert.IsTrue(Holds(all, JoinStrategySelectorKind.Forced), "The registry still holds the built-in forced.");
        Assert.IsTrue(Holds(all, JoinStrategySelectorKind.Structural), "The registry still holds the built-in structural.");
        Assert.IsTrue(Holds(all, JoinStrategySelectorKind.Manual), "The registry still holds the built-in manual.");
    }

    /// <summary>Two selectors may not share one telemetry identity.</summary>
    [TestMethod]
    public void SelectorKindCreateRejectsADuplicateCode()
    {
        Assert.ThrowsExactly<ArgumentException>(static () => JoinStrategySelectorKind.Create(JoinStrategySelectorKind.Structural.Code));
    }

    /// <summary>The names are the metric tags: a drift silently renames a metric dimension.</summary>
    [TestMethod]
    public void SelectorKindNamesAreTheTelemetryTags()
    {
        Assert.AreEqual("none", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.None));
        Assert.AreEqual("forced", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.Forced));
        Assert.AreEqual("structural", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.Structural));
        Assert.AreEqual("manual", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.Manual));
        Assert.AreEqual("custom_1001", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.Create(1001)));
    }

    /// <summary>Every factory stamps who decided and why, so telemetry can attribute a route to a rule.</summary>
    [TestMethod]
    public void DecisionFactoriesStampTheirKindAndReason()
    {
        JoinSelectionDecision structural = JoinSelectionDecision.Structural(QueryEngineKind.FreeJoin, JoinSelectionReason.CyclicCore);
        JoinSelectionDecision manual = JoinSelectionDecision.Manual(QueryEngineKind.ColumnarBatched, JoinSelectionReason.AcyclicBatched);
        JoinSelectionDecision supplied = JoinSelectionDecision.Supplied(QueryEngineKind.Columnar, SuppliedKind);
        JoinSelectionDecision forced = JoinSelectionDecision.Forced(QueryEngineKind.FreeJoin);

        Assert.AreEqual(QueryEngineKind.FreeJoin, structural.Route);
        Assert.AreEqual(JoinStrategySelectorKind.Structural, structural.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.CyclicCore, structural.Reason);

        Assert.AreEqual(QueryEngineKind.ColumnarBatched, manual.Route);
        Assert.AreEqual(JoinStrategySelectorKind.Manual, manual.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.AcyclicBatched, manual.Reason);

        Assert.AreEqual(QueryEngineKind.Columnar, supplied.Route);
        Assert.AreEqual(SuppliedKind, supplied.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.Unspecified, supplied.Reason);

        Assert.AreEqual(QueryEngineKind.FreeJoin, forced.Route);
        Assert.AreEqual(JoinStrategySelectorKind.Forced, forced.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.PolicyForced, forced.Reason);
    }

    /// <summary>The arc's headline shape reads as one cyclic component.</summary>
    [TestMethod]
    public void ShapeAnalysisSeesTheCyclicTriangleAsOneComponent()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(TinyView(), Triangle(new VariableRegistry()), QueryEnginePolicy.Default);

        Assert.IsFalse(features.Acyclic);
        Assert.AreEqual(1, features.ComponentCount);
    }

    /// <summary>Independent components are counted apart, which is what makes the disconnected rule fire at all.</summary>
    [TestMethod]
    public void ShapeAnalysisCountsIndependentComponents()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(TinyView(), TwoDisjointChains(new VariableRegistry()), QueryEnginePolicy.Default);

        Assert.IsTrue(features.Acyclic);
        Assert.AreEqual(2, features.ComponentCount);
    }

    /// <summary>A fully bound pattern is a membership constraint, not a component of its own.</summary>
    [TestMethod]
    public void ShapeAnalysisIgnoresFullyBoundPatterns()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(TinyView(), ConstrainedTwoPatternJoin(new VariableRegistry()), QueryEnginePolicy.Default);

        Assert.AreEqual(1, features.ComponentCount);
        Assert.AreEqual(3, features.PatternCount);
    }

    /// <summary>The skew signal is read off the JOIN keys: a private tail's heavier fan is not the query's join skew.</summary>
    [TestMethod]
    public void FeaturesCarryTheHeaviestJoinKeyFanOut()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(FannedChainView(), FannedChain(new VariableRegistry()), QueryEnginePolicy.Default);

        //The hub draws six first-hop subjects and carries four second-hop objects, so the heaviest fan of
        //the one join variable is six; the first hop's own subject 7 carries eleven objects, and that
        //variable joins nothing.
        Assert.AreEqual(6, features.MaximumKeyFanOut);
    }

    /// <summary>A query no pattern of which exposes the statistic reports the unreadable value, which sits outside every real reading's range.</summary>
    [TestMethod]
    public void FeaturesReportTheUnreadableSentinelWhenNoStatisticIsExposed()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(TinyView(), TwoUnboundPatterns(new VariableRegistry()), QueryEnginePolicy.Default);

        Assert.AreEqual(JoinSelectionFeatures.UnreadableKeyFanOut, features.MaximumKeyFanOut);
        Assert.IsLessThan(0, features.MaximumKeyFanOut, "A located key group reads zero or more, so the unreadable value must sit outside that range.");
        Assert.AreEqual(2, features.PatternCount, "The record is populated, so the unreadable value is a reading and not a default record's zero.");
    }

    /// <summary>The structural companion counts the JOIN-COVER plan's tails, so a shape whose every relation would extend still reports the tails it carries.</summary>
    [TestMethod]
    public void FeaturesCountTheTailsOfTheJoinCoverPlan()
    {
        JoinSelectionFeatures star = JoinShapeAnalysis.Describe(HighFanStarView(), HighFanStar(new VariableRegistry()), QueryEnginePolicy.Default);
        JoinSelectionFeatures triangle = JoinShapeAnalysis.Describe(TinyView(), Triangle(new VariableRegistry()), QueryEnginePolicy.Default);

        //Every arm of the star concentrates the engagement fan on its hub, so a plan taken under the engaged
        //rule would extend all three and leave nothing to count; the join-cover plan the seam reads carries
        //three.
        Assert.AreEqual(3, star.TailBearingRelationCount);
        Assert.AreEqual(0, triangle.TailBearingRelationCount, "A cyclic core's cover depth is already full depth, so zero is the shape's own reading.");
    }

    /// <summary>Every axis the decision grew defaults to the value that means "the engine's standing behaviour", so a selector that knows none of them stays sound.</summary>
    [TestMethod]
    public void TheNewDecisionAxesDefaultToUnspecified()
    {
        JoinSelectionDecision decision = default;

        Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, decision.Depth);
        Assert.AreEqual(FreeJoinTrieBuildPreference.Unspecified, decision.Build);
        Assert.AreEqual(FactorizationEngagement.Unspecified, decision.Factorization);
        Assert.AreEqual(JoinSelectionHintedAxes.None, decision.HintedAxes);

        JoinQueryHints hints = default;

        Assert.AreEqual(JoinRouteHintKind.None, hints.Route);
        Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, hints.Depth);
        Assert.AreEqual(FreeJoinTrieBuildPreference.Unspecified, hints.Build);
        Assert.AreEqual(FactorizationEngagement.Unspecified, hints.Factorization);
    }

    /// <summary>A factory invents no axis: what the caller does not state stays unspecified, and what it states is carried verbatim.</summary>
    [TestMethod]
    public void DecisionFactoriesCarryUnspecifiedAxesUnchanged()
    {
        JoinSelectionDecision structural = JoinSelectionDecision.Structural(QueryEngineKind.FreeJoin, JoinSelectionReason.CyclicCore);
        JoinSelectionDecision manual = JoinSelectionDecision.Manual(QueryEngineKind.ColumnarBatched, JoinSelectionReason.AcyclicBatched);
        JoinSelectionDecision forced = JoinSelectionDecision.Forced(QueryEngineKind.FreeJoin);
        JoinSelectionDecision supplied = JoinSelectionDecision.Supplied(QueryEngineKind.Columnar, SuppliedKind);
        JoinSelectionDecision calibrated = JoinSelectionDecision.Calibrated(QueryEngineKind.ColumnarBatched, JoinSelectionReason.AcyclicBatched);

        foreach(JoinSelectionDecision unstated in (JoinSelectionDecision[])[structural, manual, forced, supplied, calibrated])
        {
            Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, unstated.Depth);
            Assert.AreEqual(FreeJoinTrieBuildPreference.Unspecified, unstated.Build);
            Assert.AreEqual(FactorizationEngagement.Unspecified, unstated.Factorization);
            Assert.AreEqual(JoinSelectionHintedAxes.None, unstated.HintedAxes);
        }

        JoinSelectionDecision stated = JoinSelectionDecision.Calibrated(QueryEngineKind.ColumnarBatched, JoinSelectionReason.AcyclicBatched, FactorizationEngagement.Star);

        Assert.AreEqual(FactorizationEngagement.Star, stated.Factorization);
        Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, stated.Depth);
        Assert.AreEqual(JoinStrategySelectorKind.Calibrated, stated.SelectorKind);
    }

    /// <summary>The two identities this rung adds are registered, distinct, and named — the names are the metric tags.</summary>
    [TestMethod]
    public void SelectorKindCalibratedAndHintedAreRegistered()
    {
        Assert.AreEqual(400, JoinStrategySelectorKind.Calibrated.Code);
        Assert.AreEqual(500, JoinStrategySelectorKind.Hinted.Code);
        Assert.AreNotEqual(JoinStrategySelectorKind.Calibrated, JoinStrategySelectorKind.Hinted);

        IReadOnlyList<JoinStrategySelectorKind> all = JoinStrategySelectorKind.All;

        Assert.IsTrue(Holds(all, JoinStrategySelectorKind.Calibrated), "The calibrated identity is registered.");
        Assert.IsTrue(Holds(all, JoinStrategySelectorKind.Hinted), "The hinted identity is registered.");
        Assert.AreEqual("calibrated", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.Calibrated));
        Assert.AreEqual("hinted", JoinStrategySelectorKindNames.GetName(JoinStrategySelectorKind.Hinted));
    }

    /// <summary>The hinted-route rationale is its own value, read back off a decision the hinted factory stamped.</summary>
    [TestMethod]
    public void SelectionReasonNamesTheHintedRoute()
    {
        JoinSelectionDecision decision = JoinSelectionDecision.Hinted(QueryEngineKind.FreeJoin);

        Assert.AreEqual(6, (int)decision.Reason);
        Assert.AreEqual(JoinStrategySelectorKind.Hinted, decision.SelectorKind);

        foreach(JoinSelectionReason existing in (JoinSelectionReason[])[JoinSelectionReason.Unspecified, JoinSelectionReason.CyclicCore, JoinSelectionReason.DisconnectedComponents, JoinSelectionReason.AcyclicBatched, JoinSelectionReason.SoundDefault, JoinSelectionReason.PolicyForced])
        {
            Assert.AreNotEqual(existing, decision.Reason);
        }
    }

    /// <summary>With no statistic to read, the calibrated rule is the structural rule: it decides nothing the data did not justify.</summary>
    [TestMethod]
    public void CalibratedEqualsStructuralWhenStatisticsAreUnreadable()
    {
        foreach(JoinSelectionContext context in UnreadableShapeMatrix())
        {
            JoinSelectionDecision structural = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);
            JoinSelectionDecision calibrated = JoinStrategySelectors.Calibrated(in context, TestContext.CancellationToken);

            //Everything but the deciding identity is the structural rule's own decision.
            Assert.AreEqual(structural with { SelectorKind = JoinStrategySelectorKind.Calibrated }, calibrated);
        }
    }

    /// <summary>The calibrated rule's route axis is the structural rule's: no arm routes an acyclic shape away from the batched default.</summary>
    [TestMethod]
    public void CalibratedRoutesByTheStructuralRule()
    {
        foreach(JoinSelectionContext context in ReadableShapeMatrix())
        {
            JoinSelectionDecision structural = JoinStrategySelectors.Structural(in context, TestContext.CancellationToken);
            JoinSelectionDecision calibrated = JoinStrategySelectors.Calibrated(in context, TestContext.CancellationToken);

            Assert.AreEqual(structural.Route, calibrated.Route);
            Assert.AreEqual(structural.Reason, calibrated.Reason);
            Assert.AreEqual(JoinStrategySelectorKind.Calibrated, calibrated.SelectorKind);
        }
    }

    /// <summary>Depth stays the engine's per-relation decision and build stays the policy's: the calibrated rule states neither.</summary>
    [TestMethod]
    public void CalibratedLeavesDepthAndBuildUnspecified()
    {
        JoinSelectionContext context = ContextFor(
            ReadableFeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true),
            HighFanStar(new VariableRegistry()),
            HighFanStarView());

        JoinSelectionDecision decision = JoinStrategySelectors.Calibrated(in context, TestContext.CancellationToken);

        Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, decision.Depth);
        Assert.AreEqual(FreeJoinTrieBuildPreference.Unspecified, decision.Build);
    }

    /// <summary>The weighted mean rides the feature record beside the maximum, aggregated the same way and distinct from it.</summary>
    [TestMethod]
    public void FeaturesCarryTheDegreeWeightedMeanFanOut()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(FannedChainView(), FannedChain(new VariableRegistry()), QueryEnginePolicy.Default);

        //Keyed on the hub the first hop reads degrees six and eleven ones — a weighted mean of 47 / 17 —
        //while the second hop's single hub key carries four, a weighted mean of 16 / 4 = 4.0. The feature is
        //the maximum over the readable readings, so it is the second hop's four, distinct from the maximum
        //fan of six the same query reports.
        Assert.AreEqual(4.0, features.DegreeWeightedMeanFanOut, 0.0001);
        Assert.AreEqual(6, features.MaximumKeyFanOut);
    }

    /// <summary>A query no pattern of which exposes the statistic reports the unreadable weighted fan, which sits outside every real reading's range.</summary>
    [TestMethod]
    public void FeaturesReportTheUnreadableWeightedFanSentinel()
    {
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(TinyView(), TwoUnboundPatterns(new VariableRegistry()), QueryEnginePolicy.Default);

        Assert.AreEqual(JoinSelectionFeatures.UnreadableWeightedFanOut, features.DegreeWeightedMeanFanOut, 0.0001);
        Assert.IsLessThan(0.0, features.DegreeWeightedMeanFanOut, "A located key group reads zero or more, so the unreadable value must sit outside that range.");
        Assert.AreEqual(2, features.PatternCount, "The record is populated, so the unreadable value is a reading and not a default record's zero.");
    }

    /// <summary>The four shape classes the route rules separate, carried on features whose data statistics are unreadable.</summary>
    /// <returns>The contexts, one per shape class.</returns>
    private static JoinSelectionContext[] UnreadableShapeMatrix()
    {
        return
        [
            ContextFor(FeaturesOf(acyclic: false, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true), Triangle(new VariableRegistry()), TinyView()),
            ContextFor(FeaturesOf(acyclic: true, componentCount: 2, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true), TwoDisjointChains(new VariableRegistry()), TinyView()),
            ContextFor(FeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true), TwoPatternJoin(new VariableRegistry()), TinyView()),
            ContextFor(FeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: false), TwoPatternJoin(new VariableRegistry()), TinyView())
        ];
    }

    /// <summary>The same four shape classes carried on features whose data statistics read.</summary>
    /// <returns>The contexts, one per shape class.</returns>
    private static JoinSelectionContext[] ReadableShapeMatrix()
    {
        return
        [
            ContextFor(ReadableFeaturesOf(acyclic: false, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true), Triangle(new VariableRegistry()), TinyView()),
            ContextFor(ReadableFeaturesOf(acyclic: true, componentCount: 2, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true), TwoDisjointChains(new VariableRegistry()), TinyView()),
            ContextFor(ReadableFeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: true), HighFanStar(new VariableRegistry()), HighFanStarView()),
            ContextFor(ReadableFeaturesOf(acyclic: true, componentCount: 1, ColumnarOrderSetMode.AllSixOrders, batchedRouteEligible: false), HighFanStar(new VariableRegistry()), HighFanStarView())
        ];
    }

    /// <summary>Whether the registry snapshot holds a kind equal to the given one.</summary>
    /// <param name="kinds">The registry snapshot.</param>
    /// <param name="kind">The kind sought.</param>
    /// <returns><see langword="true"/> when the snapshot holds it.</returns>
    private static bool Holds(IReadOnlyList<JoinStrategySelectorKind> kinds, JoinStrategySelectorKind kind)
    {
        for(int i = 0; i < kinds.Count; i++)
        {
            if(kinds[i].Equals(kind))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Hand-built features addressing one arm of the rule directly.</summary>
    /// <param name="acyclic">Whether the GYO reduction clears the shape.</param>
    /// <param name="componentCount">The connected-component count over shared variables.</param>
    /// <param name="orderSetMode">The permutation set the view materialises.</param>
    /// <param name="batchedRouteEligible">Whether policy enables the batched route.</param>
    /// <returns>The features.</returns>
    private static JoinSelectionFeatures FeaturesOf(bool acyclic, int componentCount, ColumnarOrderSetMode orderSetMode, bool batchedRouteEligible)
    {
        return new JoinSelectionFeatures(
            PatternCount: 3,
            ViewTripleCount: 4,
            Acyclic: acyclic,
            ComponentCount: componentCount,
            OrderSetMode: orderSetMode,
            BatchedRouteEligible: batchedRouteEligible,
            MaximumKeyFanOut: JoinSelectionFeatures.UnreadableKeyFanOut,
            TailBearingRelationCount: JoinSelectionFeatures.UnplannedTailBearingRelationCount,
            DegreeWeightedMeanFanOut: JoinSelectionFeatures.UnreadableWeightedFanOut);
    }

    /// <summary>Hand-built features whose data statistics read, so a rule that consults them is not looking at sentinels.</summary>
    /// <param name="acyclic">Whether the GYO reduction clears the shape.</param>
    /// <param name="componentCount">The connected-component count over shared variables.</param>
    /// <param name="orderSetMode">The permutation set the view materialises.</param>
    /// <param name="batchedRouteEligible">Whether policy enables the batched route.</param>
    /// <returns>The features.</returns>
    private static JoinSelectionFeatures ReadableFeaturesOf(bool acyclic, int componentCount, ColumnarOrderSetMode orderSetMode, bool batchedRouteEligible)
    {
        return new JoinSelectionFeatures(
            PatternCount: 3,
            ViewTripleCount: 72,
            Acyclic: acyclic,
            ComponentCount: componentCount,
            OrderSetMode: orderSetMode,
            BatchedRouteEligible: batchedRouteEligible,
            MaximumKeyFanOut: 8,
            TailBearingRelationCount: 3,
            DegreeWeightedMeanFanOut: 8.0);
    }

    /// <summary>A consultation context carrying the given features over a real query and a real view.</summary>
    /// <param name="features">The features the arm under test reads.</param>
    /// <returns>The context.</returns>
    private static JoinSelectionContext ContextFor(JoinSelectionFeatures features)
    {
        return ContextFor(features, TwoPatternJoin(new VariableRegistry()), TinyView());
    }

    /// <summary>A consultation context carrying the given features over the given query and view, hinting nothing.</summary>
    /// <param name="features">The features the arm under test reads.</param>
    /// <param name="query">The query the rule may read statistics for.</param>
    /// <param name="view">The view the rule may read statistics off.</param>
    /// <returns>The context.</returns>
    private static JoinSelectionContext ContextFor(JoinSelectionFeatures features, BasicGraphPattern query, ColumnarTripleIndex view)
    {
        return new JoinSelectionContext(query, view, features, default);
    }

    /// <summary>The four-triple knows view the unit rows consult over.</summary>
    /// <returns>The view.</returns>
    private static ColumnarTripleIndex TinyView()
    {
        List<EncodedTriple> triples =
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3)
        ];

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>(?a knows ?b) . (?b knows ?c): the acyclic connected two-pattern join.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern TwoPatternJoin(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(Knows), PatternPosition.OfVariable(c))
            ],
            registry);
    }

    /// <summary>(?x knows ?y) . (?y knows ?z) . (?z knows ?x): the cyclic core.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern Triangle(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(Knows), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(Knows), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(Knows), PatternPosition.OfVariable(x))
            ],
            registry);
    }

    /// <summary>Two independent two-pattern chains sharing no variable: the disconnected shape.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern TwoDisjointChains(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");
        Variable e = registry.GetOrAdd("e");
        Variable f = registry.GetOrAdd("f");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(Knows), PatternPosition.OfVariable(c)),
                new TriplePattern(PatternPosition.OfVariable(d), PatternPosition.Bound(Knows), PatternPosition.OfVariable(e)),
                new TriplePattern(PatternPosition.OfVariable(e), PatternPosition.Bound(Knows), PatternPosition.OfVariable(f))
            ],
            registry);
    }

    /// <summary>A fully bound membership pattern beside the connected two-pattern join.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern ConstrainedTwoPatternJoin(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(Knows), PatternPosition.OfVariable(c)),
                new TriplePattern(PatternPosition.Bound(TermId.FromEncoded(1)), PatternPosition.Bound(Knows), PatternPosition.Bound(TermId.FromEncoded(2)))
            ],
            registry);
    }

    /// <summary>
    /// The fanned two-hop view: hub 500 draws six first-hop subjects and carries four second-hop
    /// objects, while the non-joining subject 7 carries eleven first-hop objects.
    /// </summary>
    /// <returns>The view.</returns>
    private static ColumnarTripleIndex FannedChainView()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 6; subject++)
        {
            triples.Add(EncodedTriple.FromEncoded(subject, Reaches.Encoded, 500));
        }

        for(uint offset = 0; offset < 11; offset++)
        {
            triples.Add(EncodedTriple.FromEncoded(7, Reaches.Encoded, 600 + offset));
        }

        for(uint offset = 0; offset < 4; offset++)
        {
            triples.Add(EncodedTriple.FromEncoded(500, Carries.Encoded, 700 + offset));
        }

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>(?a reaches ?b) . (?b carries ?c): the two-hop chain whose only join variable is the hub.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern FannedChain(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Reaches), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(Carries), PatternPosition.OfVariable(c))
            ],
            registry);
    }

    /// <summary>
    /// The three-arm star view: each of three hub subjects carries eight objects on each of the three arm
    /// predicates, so every arm's own cover key concentrates the engagement fan.
    /// </summary>
    /// <returns>The view.</returns>
    private static ColumnarTripleIndex HighFanStarView()
    {
        List<EncodedTriple> triples = [];
        for(uint hub = 1; hub <= 3; hub++)
        {
            for(uint offset = 0; offset < 8; offset++)
            {
                triples.Add(EncodedTriple.FromEncoded(hub, FirstArm.Encoded, 10_000 + (hub * 100) + offset));
                triples.Add(EncodedTriple.FromEncoded(hub, SecondArm.Encoded, 20_000 + (hub * 100) + offset));
                triples.Add(EncodedTriple.FromEncoded(hub, ThirdArm.Encoded, 30_000 + (hub * 100) + offset));
            }
        }

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>(?k firstArm ?b) . (?k secondArm ?c) . (?k thirdArm ?d): the three-arm star on one hub key.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern HighFanStar(VariableRegistry registry)
    {
        Variable k = registry.GetOrAdd("k");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(FirstArm), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(SecondArm), PatternPosition.OfVariable(c)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(ThirdArm), PatternPosition.OfVariable(d))
            ],
            registry);
    }

    /// <summary>(?a ?b ?c) . (?c ?d ?e): two three-variable patterns, neither of which exposes a key group.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern TwoUnboundPatterns(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");
        Variable e = registry.GetOrAdd("e");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.OfVariable(b), PatternPosition.OfVariable(c)),
                new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.OfVariable(d), PatternPosition.OfVariable(e))
            ],
            registry);
    }
}
