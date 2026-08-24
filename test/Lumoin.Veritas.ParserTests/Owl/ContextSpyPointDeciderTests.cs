using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The spy-point domain-bound habitat decider's battery: the closed-form
/// refuting rows across every told route the jurisdiction admits — the corpus
/// template's plain funnel with a told inverse, the inline-inverse funnel, both
/// cap routes, a two-member cap sum, and the implicit demand a zero cap clashes
/// with — the under-demand silence that hands the module back to saturation, the
/// explicit dark control and its census ride, the nine near-miss silences one
/// per named hazard, the member-window silence, the long-arithmetic overflow pin,
/// the window derivation, and the verdict-identity sweep. Every row drives the
/// production seams — the faces-carrying reasoner overload or the decider's own
/// measurement surface — and every counter the battery reads is consumed by an
/// assert.
/// </summary>
[TestClass]
internal sealed class ContextSpyPointDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/spypointcsp#";

    /// <summary>The spy-point clash face lit — the selection the refuting rows drive. The face has no certify counterpart.</summary>
    private const EnumerationDeciderFaces SpyPointFaces = EnumerationDeciderFaces.SpyPointClash;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the verdict-identity sweep runs against the explicit dark control.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a spy-point module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// The corpus template told exactly as the premise spells it: the whole
    /// domain funnelled along a plain role into a singleton one-of, the funnel
    /// role linked to the cap role by a told inverse axiom, the spy member capped
    /// at two by an ABox-typed anonymous restriction (cap route a), and a demand
    /// of three reached one subclass hop from the class an ANONYMOUS individual is
    /// asserted into (demand route a). Three elements demanded, two admitted: the
    /// closed form refutes with zero inference attempts and no engine. The
    /// habitat-label assert doubles as the positive reachability pin — a nominal
    /// module whose funnel and cap the rider's probes both decline must still
    /// reach the spy-point probe — and every one of the five spy-point statistics
    /// fields is read.
    /// </summary>
    [TestMethod]
    public void Sp1CorpusTemplateRefutesByDomainPigeonhole()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusTemplateModule(), SpyPointFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Sp1 CorpusTemplate: the clash face decides the template at the production ceiling.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Sp1 CorpusTemplate: three demanded elements cannot fit a domain of two, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Sp1 CorpusTemplate: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Sp1 CorpusTemplate: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, totals.EnumerationHabitat, "Sp1 CorpusTemplate: a nominal module the rider's funnel and cap probes both decline still reaches the spy-point probe and is labelled Shape S.");
        Assert.AreEqual(1, totals.SpyPointDeciderClashes, "Sp1 CorpusTemplate: the clash face's counter reads the decision.");
        Assert.AreEqual(1, totals.SpyPointMemberCount, "Sp1 CorpusTemplate: the singleton funnel member is measured.");
        Assert.AreEqual(2L, totals.SpyPointCapBound, "Sp1 CorpusTemplate: the told cap of two is the whole domain bound.");
        Assert.AreEqual(3L, totals.SpyPointDemandBound, "Sp1 CorpusTemplate: the told demand of three is measured beside the bound.");
        Assert.AreEqual(0, totals.SpyPointWindowExceededMembers, "Sp1 CorpusTemplate: no member-window silence at one member.");
        Assert.AreEqual("SpyPointDomainPigeonhole(" + Example + "p)", ContextSpyPointDecider.Run(CorpusTemplateModule()).ClashReason, "Sp1 CorpusTemplate: the clash reason names the funnel role.");
    }

    /// <summary>
    /// The inline-inverse funnel: the domain is driven along the INVERSE of a
    /// role, so the cap role is that role itself and no told inverse axiom is
    /// needed. The cap is the ABox-typed route (a) — the told-subclass cap route
    /// is what the rider's own probe claims, so it would label the module Shape N
    /// and hand it to face one instead — and the demand is typed DIRECTLY onto an
    /// individual (route b). Two demanded, one admitted: the closed form refutes.
    /// </summary>
    [TestMethod]
    public void Sp2InlineInverseFunnelRefutesWithDirectDemand()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(InlineInverseModule(2), SpyPointFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Sp2 InlineInverseFunnel: the clash face decides the inline-inverse funnel.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Sp2 InlineInverseFunnel: two demanded elements cannot fit a domain of one.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Sp2 InlineInverseFunnel: the refutation is pre-engine.");
        Assert.AreEqual(0, totals.ContextsCreated, "Sp2 InlineInverseFunnel: no engine was constructed.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, totals.EnumerationHabitat, "Sp2 InlineInverseFunnel: the ABox-typed cap leaves the rider's cap probe declining, so the label is Shape S.");
        Assert.AreEqual(1, totals.SpyPointDeciderClashes, "Sp2 InlineInverseFunnel: the clash face's counter reads the decision.");
        Assert.AreEqual(1, totals.SpyPointMemberCount, "Sp2 InlineInverseFunnel: the singleton funnel member is measured.");
        Assert.AreEqual(1L, totals.SpyPointCapBound, "Sp2 InlineInverseFunnel: the inverted role's own cap of one bounds the domain.");
        Assert.AreEqual(2L, totals.SpyPointDemandBound, "Sp2 InlineInverseFunnel: the directly typed demand of two is measured.");
    }

    /// <summary>
    /// The demand inside the bound: the same inline-inverse module with a demand
    /// of one against a cap of one. The face is SILENT — it has no certify
    /// direction at all, because a demand the domain bound admits says nothing
    /// about the rest of the module — so ordinary saturation owns the verdict and
    /// no clash counter moves. The measured numbers still ride the record.
    /// </summary>
    [TestMethod]
    public void Sp3UnderDemandStaysSilentAndSaturationOwnsTheModule()
    {
        ReasoningModule module = InlineInverseModule(1);
        SpyPointOutcome measured = ContextSpyPointDecider.Measure(module);
        SpyPointOutcome ran = ContextSpyPointDecider.Run(module);

        Assert.IsNull(measured.Consistent, "Sp3 UnderDemand: the measurement surface never forms a verdict.");
        Assert.IsNull(ran.Consistent, "Sp3 UnderDemand: a demand inside the bound leaves the face silent — there is no certify direction.");
        Assert.IsNull(ran.ClashReason, "Sp3 UnderDemand: a silent face names no clash reason.");
        Assert.AreEqual(1L, ran.Window.CapBound, "Sp3 UnderDemand: the domain bound of one is measured.");
        Assert.AreEqual(1L, ran.Window.DemandBound, "Sp3 UnderDemand: the effective demand of one is measured beside it.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, SpyPointFaces, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.SpyPointDeciderClashes, "Sp3 UnderDemand: no clash decision when the demand fits the bound.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, totals.EnumerationHabitat, "Sp3 UnderDemand: the census label rides a silent face.");
        Assert.AreEqual(1L, totals.SpyPointCapBound, "Sp3 UnderDemand: the measured bound rides the statistics record.");
        Assert.IsGreaterThan(0L, totals.InferenceAttempts, "Sp3 UnderDemand: ordinary saturation owns the module the face declined.");
    }

    /// <summary>The cap told through route (b) instead: the corpus template's plain funnel with the spy member capped by a told subclass axiom from the one-of to the max-cardinality restriction. The same domain bound of two is summed, and the same demand of three refutes.</summary>
    [TestMethod]
    public void Sp4OneOfSubclassCapRouteRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(SubclassCapRouteModule(), SpyPointFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Sp4 OneOfSubclassCapRoute: the clash face decides the told-subclass cap route.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Sp4 OneOfSubclassCapRoute: the domain bound of two cannot host three demanded elements.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Sp4 OneOfSubclassCapRoute: the refutation is pre-engine.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, totals.EnumerationHabitat, "Sp4 OneOfSubclassCapRoute: the plain funnel leaves the rider's funnel probe declining, so the label is Shape S.");
        Assert.AreEqual(1, totals.SpyPointDeciderClashes, "Sp4 OneOfSubclassCapRoute: the clash face's counter reads the decision.");
        Assert.AreEqual(2L, totals.SpyPointCapBound, "Sp4 OneOfSubclassCapRoute: route (b) records the same cap as route (a).");
        Assert.AreEqual(3L, totals.SpyPointDemandBound, "Sp4 OneOfSubclassCapRoute: the told demand of three is measured.");
    }

    /// <summary>The two-member funnel: the domain bound is the SUM of the per-member caps, one told through each route, so two members capped at one each admit two elements — and the demand of three still refutes.</summary>
    [TestMethod]
    public void Sp5TwoSpySumBoundRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(TwoMemberModule(), SpyPointFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Sp5 TwoSpySumBound: the clash face decides the two-member funnel.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Sp5 TwoSpySumBound: two capped seats cannot host three demanded elements.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Sp5 TwoSpySumBound: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.SpyPointDeciderClashes, "Sp5 TwoSpySumBound: the clash face's counter reads the decision.");
        Assert.AreEqual(2, totals.SpyPointMemberCount, "Sp5 TwoSpySumBound: both funnel members are measured.");
        Assert.AreEqual(2L, totals.SpyPointCapBound, "Sp5 TwoSpySumBound: the bound is the sum of the two told caps, mixed routes.");
        Assert.AreEqual(3L, totals.SpyPointDemandBound, "Sp5 TwoSpySumBound: the told demand of three is measured.");
    }

    /// <summary>
    /// The near-miss sub-checks: nine perturbations of the corpus template, each
    /// of which must leave the face SILENT — a data-side demand that bounds
    /// literals rather than domain elements, a cap on a role no told inverse links
    /// to the funnel, a cap carried by a non-member, a qualified cap that bounds
    /// only its filler's successors, a funnel under a named class rather than
    /// <c>owl:Thing</c>, an anonymous one-of member that drops the funnel whole, a
    /// second funnel member left uncapped, a demand two subclass hops from its
    /// asserted class, and the funnel wrapped in an intersection instead of
    /// sitting top-level. Every row is built so that a leak in the named guard
    /// would REFUTE it, so silence is the whole assertion. The face is read
    /// directly and through the reasoner: the clash counter may not move on any
    /// row.
    /// </summary>
    [TestMethod]
    public void Sp6NearMissRowsStaySilent()
    {
        foreach((string name, ReasoningModule module) in NearMissRows())
        {
            SpyPointOutcome outcome = ContextSpyPointDecider.Run(module);

            Assert.IsNull(outcome.Consistent, "Sp6 " + name + ": the face must stay silent on the near miss.");
            Assert.IsNull(outcome.ClashReason, "Sp6 " + name + ": a silent face names no clash reason.");

            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, SpyPointFaces, ProbeBudget, TestContext.CancellationToken);

            Assert.AreEqual(0, decision.Statistics.ContextTotals.SpyPointDeciderClashes, "Sp6 " + name + ": no clash decision on the near miss.");
        }
    }

    /// <summary>
    /// The zero cap against the implicit demand: a funnel whose single member is
    /// capped at zero admits an EMPTY domain, and every OWL 2 interpretation has a
    /// nonempty one. The module tells no minimum-cardinality demand at all, so the
    /// nonempty domain's own demand of one is the entire clash source — the
    /// effective demand never drops below one.
    /// </summary>
    [TestMethod]
    public void Sp7ZeroCapClashesOnTheImplicitDemand()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(ZeroCapModule(), SpyPointFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Sp7 ZeroCap: the clash face decides the zero-cap module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Sp7 ZeroCap: an empty domain is no model, so the module is inconsistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Sp7 ZeroCap: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.SpyPointDeciderClashes, "Sp7 ZeroCap: the clash face's counter reads the decision.");
        Assert.AreEqual(0L, totals.SpyPointCapBound, "Sp7 ZeroCap: the told cap of zero bounds the domain at zero.");
        Assert.AreEqual(1L, totals.SpyPointDemandBound, "Sp7 ZeroCap: the effective demand is the nonempty domain's own one, told by no axiom.");
    }

    /// <summary>
    /// The cap sum runs in long arithmetic: two members each capped at
    /// <see cref="int.MaxValue"/> sum past the int range, and the window records
    /// the exact long total rather than a wrapped negative that would refute every
    /// demand. The row is read on the decider's own measurement surface, where the
    /// summed bound is visible without an engine-side cardinality of two billion.
    /// </summary>
    [TestMethod]
    public void Sp8CapSumUsesLongArithmetic()
    {
        SpyPointOutcome outcome = ContextSpyPointDecider.Run(OverflowCapModule());

        Assert.IsNull(outcome.Consistent, "Sp8 CapSum: a demand of three sits far inside the summed bound, so the face is silent.");
        Assert.IsNull(outcome.ClashReason, "Sp8 CapSum: no clash, no reason.");
        Assert.AreEqual(2L * int.MaxValue, outcome.Window.CapBound, "Sp8 CapSum: the exact long sum is recorded — no int wraparound.");
        Assert.IsGreaterThan(0L, outcome.Window.CapBound, "Sp8 CapSum: the summed bound stayed positive past the int range.");
        Assert.AreEqual(2, outcome.Window.MemberCount, "Sp8 CapSum: both maximally capped members are measured.");
        Assert.AreEqual(3L, outcome.Window.DemandBound, "Sp8 CapSum: the told demand rides the silent measurement.");
        Assert.AreEqual(0, outcome.Window.MemberSilences, "Sp8 CapSum: two members are well inside the member window.");
    }

    /// <summary>
    /// The member-window silence charges its named counter, with the measured
    /// numbers landing BEFORE the boundary comparison: a funnel one member past
    /// the bound is SKIPPED, so its cap sum is never taken and no pairing survives
    /// to compare. Every member is capped at zero, so a funnel summed past the
    /// window would refute on the implicit demand alone — silence here is the
    /// window doing its work, not a coincidence of the numbers.
    /// </summary>
    [TestMethod]
    public void Sp9SeventeenMembersChargeTheMemberWindowSilence()
    {
        int overflow = ContextSpyPointDecider.SpyPointMemberBound + 1;
        ReasoningModule module = MemberWindowModule(overflow, cap: 0);
        SpyPointOutcome outcome = ContextSpyPointDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Sp9 MemberWindow: the face is silent past the member bound.");
        Assert.AreEqual(overflow, outcome.Window.MemberCount, "Sp9 MemberWindow: the measured member count is reported past the bound.");
        Assert.AreEqual(1, outcome.Window.MemberSilences, "Sp9 MemberWindow: the silence is charged to the member counter.");
        Assert.AreEqual(0L, outcome.Window.CapBound, "Sp9 MemberWindow: a skipped funnel contributes no domain bound.");
        Assert.AreEqual(0L, outcome.Window.DemandBound, "Sp9 MemberWindow: with no pairing there is no demand to report beside a bound.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, SpyPointFaces, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(1, totals.SpyPointWindowExceededMembers, "Sp9 MemberWindow: the window silence rides the statistics record.");
        Assert.AreEqual(overflow, totals.SpyPointMemberCount, "Sp9 MemberWindow: the measured members ride the statistics record.");
        Assert.AreEqual(0, totals.SpyPointDeciderClashes, "Sp9 MemberWindow: no clash past the member bound.");
    }

    /// <summary>
    /// The verdict-identity sweep: every nominal-battery row and every certified
    /// partition-battery row decided under the explicit dark control and under
    /// every lit face, across both paramodulation scopes and both root-tier
    /// topologies, must be identical in outcome, verdict, subsumption set, and
    /// attempt count. No spy-point face may claim a row of either neighbouring
    /// habitat, and — the census guard — no such row may take the Shape S label
    /// either: the new probe answers LAST on the nominal path, so an existing
    /// classification moving is a probe-placement leak. The refuting spy-point
    /// rows ride the same matrix: the lit run decides each one pre-engine with
    /// zero attempts in every cell.
    /// </summary>
    [TestMethod]
    public void Sp10LitFaceMovesNoCertifiedVerdictAcrossTheMatrix()
    {
        (NominalParamodulationScope Scope, RootContextTopology Topology)[] cells =
        [
            (NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.PerIndividualRoots),
        ];
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
                ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
                if(litTotals.SpyPointDeciderClashes > 0)
                {
                    mismatches.Add(cell + ": a nominal-battery row was claimed by the spy-point face.");
                    continue;
                }

                if(litTotals.EnumerationHabitat == EnumerationHabitatClass.SpyPointDomainBound)
                {
                    mismatches.Add(cell + ": a nominal-battery row's census label moved to Shape S.");
                    continue;
                }

                if(litTotals.EnumerationHabitat != dark.Statistics.ContextTotals.EnumerationHabitat)
                {
                    mismatches.Add(cell + ": the census label moved between the dark and lit runs.");
                    continue;
                }

                if(lit.Outcome != dark.Outcome)
                {
                    mismatches.Add(cell + ": outcome moved " + dark.Outcome + " -> " + lit.Outcome + ".");
                    continue;
                }

                if(lit.Verdict is null != dark.Verdict is null || (lit.Verdict is not null && lit.Verdict.IsConsistent != dark.Verdict!.IsConsistent))
                {
                    mismatches.Add(cell + ": the verdict moved under the lit faces.");
                    continue;
                }

                if(!KeySetsEqual(SubsumptionKeySet(dark.Verdict), SubsumptionKeySet(lit.Verdict)))
                {
                    mismatches.Add(cell + ": the exact subsumption set moved under the lit faces.");
                    continue;
                }

                bool preEngine = litTotals.EnumerationDeciderClashes + litTotals.EnumerationDeciderCertifications + litTotals.EnumerationDeciderRefutations > 0;
                if(!preEngine && litTotals.InferenceAttempts != dark.Statistics.ContextTotals.InferenceAttempts)
                {
                    mismatches.Add(cell + ": a silent-face run moved the attempt count (" + dark.Statistics.ContextTotals.InferenceAttempts + " -> " + litTotals.InferenceAttempts + ").");
                }
            }
        }

        int partitionDecided = 0;
        foreach((string name, ReasoningModule module, bool consistent) in ContextPartitionDeciderTests.PartitionRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ProbeBudget, TestContext.CancellationToken);
                ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
                if(litTotals.SpyPointDeciderClashes > 0)
                {
                    mismatches.Add(cell + ": a partition-battery row was claimed by the spy-point face.");
                    continue;
                }

                if(litTotals.EnumerationHabitat == EnumerationHabitatClass.SpyPointDomainBound)
                {
                    mismatches.Add(cell + ": a partition-battery row's census label moved to Shape S.");
                    continue;
                }

                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the partition row lost its certified verdict under the spy-point-lit faces.");
                    continue;
                }

                partitionDecided++;
            }
        }

        int spyPointDecided = 0;
        foreach((string name, ReasoningModule module) in SpyPointRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ProbeBudget, TestContext.CancellationToken);
                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent)
                {
                    mismatches.Add(cell + ": the lit spy-point face did not refute the row.");
                    continue;
                }

                spyPointDecided++;
                if(lit.Statistics.ContextTotals.InferenceAttempts != 0L)
                {
                    mismatches.Add(cell + ": a spy-point-decided run spent engine attempts (" + lit.Statistics.ContextTotals.InferenceAttempts + ").");
                }
            }
        }

        TestContext.WriteLine("Sp10 verdict-identity sweep: " + spyPointDecided + " spy-point cells refuted pre-engine, " + partitionDecided + " partition cells unmoved, zero certified movement.");
        Assert.IsGreaterThan(0, spyPointDecided, "Sp10: the lit face refutes at least one spy-point cell pre-engine — the sweep instruments a lit decider.");
        Assert.IsGreaterThan(0, partitionDecided, "Sp10: the neighbouring partition habitat still decides under the spy-point-lit selection.");
        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection the corpus template
    /// keeps the honest engine-face budget abstention — the abstained outcome, no
    /// verdict, the inclusive ceiling spent, a genuine saturation behind it — and
    /// the census still ships: the habitat label and all three measured numbers
    /// are on the record while both the walk and the decision counter stayed at
    /// zero.
    /// </summary>
    [TestMethod]
    public void Sp11DarkFaceKeepsTheAbstentionByteIdenticalAndCensusRides()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusTemplateModule(), EnumerationDeciderFaces.None, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Sp11 DarkFace: the template abstains honestly with the face dark.");
        Assert.IsNull(decision.Verdict, "Sp11 DarkFace: the dark abstention carries no verdict.");
        Assert.AreEqual((long)ProbeBudget.MaxInferences, totals.InferenceAttempts, "Sp11 DarkFace: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Sp11 DarkFace: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, totals.EnumerationHabitat, "Sp11 DarkFace: the habitat label rides the dark abstention record.");
        Assert.AreEqual(1, totals.SpyPointMemberCount, "Sp11 DarkFace: the funnel member is measured dark.");
        Assert.AreEqual(2L, totals.SpyPointCapBound, "Sp11 DarkFace: the domain bound is measured dark.");
        Assert.AreEqual(3L, totals.SpyPointDemandBound, "Sp11 DarkFace: the demand is measured dark.");
        Assert.AreEqual(0, totals.SpyPointWindowExceededMembers, "Sp11 DarkFace: no window silence dark at one member.");
        Assert.AreEqual(0, totals.SpyPointDeciderClashes, "Sp11 DarkFace: no clash decision with the face dark.");
    }

    /// <summary>
    /// The window-constant derivation pin: the member ceiling sits on the counting
    /// faces' shared sixteen boundary discipline — equal by value to the
    /// counted-population, ground-clique, partition-anchor, gadget-atom, and
    /// pair-assignment ceilings — and a funnel sitting exactly AT the bound still
    /// decides, so the boundary is inclusive and the silence begins one member
    /// later.
    /// </summary>
    [TestMethod]
    public void Sp12WindowConstantDerivation()
    {
        SpyPointOutcome atBound = ContextSpyPointDecider.Run(MemberWindowModule(ContextSpyPointDecider.SpyPointMemberBound, cap: 0));

        Assert.IsFalse(atBound.Consistent, "Sp12 WindowConstant: the clash face decides AT the member bound — sixteen zero-capped seats admit no element at all.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, atBound.Window.MemberCount, "Sp12 WindowConstant: the measured member ceiling shares the counted-population bound — one boundary discipline across the pre-engine faces.");
        Assert.AreEqual(ContextClausifier.GroundCountingCliqueBound, atBound.Window.MemberCount, "Sp12 WindowConstant: the measured member ceiling shares the ground rider's clique bound.");
        Assert.AreEqual(ContextPartitionCountingDecider.PartitionAnchorBound, atBound.Window.MemberCount, "Sp12 WindowConstant: the measured member ceiling shares the partition faces' anchor bound.");
        Assert.AreEqual(ContextBooleanGadgetDecider.GadgetAtomBound, atBound.Window.MemberCount, "Sp12 WindowConstant: the measured member ceiling shares the gadget faces' atom bound.");
        Assert.AreEqual(ContextEnumerationAlgebraDecider.PairAssignmentBound, atBound.Window.MemberCount, "Sp12 WindowConstant: the measured member ceiling shares the pair-composition bound.");
        Assert.AreEqual(0, atBound.Window.MemberSilences, "Sp12 WindowConstant: no window silence exactly at the bound.");
        Assert.AreEqual(0L, atBound.Window.CapBound, "Sp12 WindowConstant: sixteen zero caps sum to zero.");
        Assert.AreEqual(3L, atBound.Window.DemandBound, "Sp12 WindowConstant: the told demand rides the deciding measurement.");
    }

    /// <summary>The refuting spy-point rows the sweep drives — every one of them inconsistent by the closed-form domain-bound pigeonhole. Internal: the sibling batteries' cross-family sweeps consume the rows.</summary>
    /// <returns>The rows.</returns>
    internal static (string Name, ReasoningModule Module)[] SpyPointRows()
    {
        return
        [
            ("Sp1", CorpusTemplateModule()),
            ("Sp2", InlineInverseModule(2)),
            ("Sp4", SubclassCapRouteModule()),
            ("Sp5", TwoMemberModule()),
            ("Sp7", ZeroCapModule()),
        ];
    }

    /// <summary>
    /// The nine near-miss modules, one per named hazard: the data-side demand
    /// lookalike, the cap on a role the funnel has no told inverse to, the cap
    /// carried by a stranger, the qualified cap filler, the named-class funnel
    /// subject, the anonymous one-of member, the uncapped second member, the
    /// two-hop demand chain, and the funnel under an intersection. Every one is
    /// tuned so that a leak in its guard would refute the module.
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module)[] NearMissRows()
    {
        return
        [
            ("DataDemandLookalike", Module(
                SubClassOf(Thing, Some("p", OneOf("spy"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 2, null), Individual("spy")),
                SubClassOf(Class("U"), MinData("d", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("CapOnUnlinkedRole", Module(
                SubClassOf(Thing, Some("p", OneOf("spy"))),
                ClassAssertion(Max("p", 2, null), Individual("spy")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("CapOnStranger", Module(
                SubClassOf(Thing, Some("p", OneOf("spy"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 2, null), Individual("stranger")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("QualifiedCapFiller", Module(
                SubClassOf(Thing, Some("p", OneOf("spy"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 2, Class("F")), Individual("spy")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("NamedClassFunnel", Module(
                SubClassOf(Class("C"), Some("p", OneOf("spy"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 0, null), Individual("spy")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("AnonymousSpyMember", Module(
                SubClassOf(Thing, Some("p", MixedOneOf("spy", "hidden"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 0, null), Individual("spy")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("UncappedSecondMember", Module(
                SubClassOf(Thing, Some("p", OneOf("s1", "s2"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 0, null), Individual("s1")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("TwoHopDemandChain", Module(
                SubClassOf(Thing, Some("p", OneOf("spy"))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 2, null), Individual("spy")),
                SubClassOf(Class("U"), Class("V")),
                SubClassOf(Class("V"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),

            ("FunnelUnderIntersection", Module(
                SubClassOf(Thing, Intersection(Some("p", OneOf("spy")))),
                InverseProperties("p", "invP"),
                ClassAssertion(Max("invP", 0, null), Individual("spy")),
                SubClassOf(Class("U"), MinObject("r", 3)),
                ClassAssertion(Class("U"), Anonymous("x")))),
        ];
    }

    /// <summary>The corpus template: a plain funnel into a singleton one-of, a told inverse linking the funnel and cap roles, the spy capped at two through the ABox-typed route, and a demand of three one subclass hop from the class an anonymous individual is asserted into.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusTemplateModule()
    {
        return Module(
            SubClassOf(Thing, Some("p", OneOf("spy"))),
            InverseProperties("p", "invP"),
            ClassAssertion(Max("invP", 2, null), Individual("spy")),
            SubClassOf(Class("U"), MinObject("r", 3)),
            ClassAssertion(Class("U"), Anonymous("x")));
    }

    /// <summary>The inline-inverse funnel: the domain driven along the inverse of a role whose own cap of one bounds it, with the demand typed directly onto an individual.</summary>
    /// <param name="demand">The told minimum-cardinality demand.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule InlineInverseModule(int demand)
    {
        return Module(
            SubClassOf(Thing, SomeInverse("q", OneOf("o"))),
            ClassAssertion(Max("q", 1, null), Individual("o")),
            ClassAssertion(MinObject("s", demand), Individual("i")));
    }

    /// <summary>The corpus template with the cap told through route (b): a subclass axiom from the one-of to the max-cardinality restriction.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SubclassCapRouteModule()
    {
        return Module(
            SubClassOf(Thing, Some("p", OneOf("spy"))),
            InverseProperties("p", "invP"),
            SubClassOf(OneOf("spy"), Max("invP", 2, null)),
            SubClassOf(Class("U"), MinObject("r", 3)),
            ClassAssertion(Class("U"), Anonymous("x")));
    }

    /// <summary>The two-member funnel with one cap told through each route, summing to a domain bound of two against a demand of three.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule TwoMemberModule()
    {
        return Module(
            SubClassOf(Thing, Some("p", OneOf("s1", "s2"))),
            InverseProperties("p", "invP"),
            ClassAssertion(Max("invP", 1, null), Individual("s1")),
            SubClassOf(OneOf("s2"), Max("invP", 1, null)),
            SubClassOf(Class("U"), MinObject("r", 3)),
            ClassAssertion(Class("U"), Anonymous("x")));
    }

    /// <summary>The zero-cap module carrying no told demand at all: the domain bound is zero and the nonempty domain's own demand of one is the whole clash source.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ZeroCapModule()
    {
        return Module(
            SubClassOf(Thing, Some("p", OneOf("spy"))),
            InverseProperties("p", "invP"),
            ClassAssertion(Max("invP", 0, null), Individual("spy")));
    }

    /// <summary>The two-member funnel whose caps are both the maximal int, so the summed domain bound leaves the int range.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule OverflowCapModule()
    {
        return Module(
            SubClassOf(Thing, Some("p", OneOf("s1", "s2"))),
            InverseProperties("p", "invP"),
            ClassAssertion(Max("invP", int.MaxValue, null), Individual("s1")),
            ClassAssertion(Max("invP", int.MaxValue, null), Individual("s2")),
            SubClassOf(Class("U"), MinObject("r", 3)),
            ClassAssertion(Class("U"), Anonymous("x")));
    }

    /// <summary>The member-window template: one funnel over the requested number of distinct named members, every one of them capped at the requested bound through the ABox-typed route, beside a told demand of three.</summary>
    /// <param name="members">The distinct funnel members.</param>
    /// <param name="cap">The per-member told cap.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule MemberWindowModule(int members, int cap)
    {
        string[] names = new string[members];
        for(int index = 0; index < members; index++)
        {
            names[index] = "s" + index;
        }

        List<OwlAxiom> axioms =
        [
            SubClassOf(Thing, Some("p", OneOf(names))),
            InverseProperties("p", "invP"),
        ];
        for(int index = 0; index < members; index++)
        {
            axioms.Add(ClassAssertion(Max("invP", cap, null), Individual(names[index])));
        }

        axioms.Add(SubClassOf(Class("U"), MinObject("r", 3)));
        axioms.Add(ClassAssertion(Class("U"), Anonymous("x")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The verdict's sorted subsumption key set, empty for an absent verdict.</summary>
    /// <param name="verdict">The verdict, or <see langword="null"/>.</param>
    /// <returns>The sorted keys.</returns>
    private static List<string> SubsumptionKeySet(ModuleVerdict? verdict)
    {
        List<string> keys = [];
        if(verdict is null)
        {
            return keys;
        }

        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add(subClass.Iri.ToString() + "->" + superClass.Iri.ToString());
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>Whether two sorted key lists are element-wise equal.</summary>
    /// <param name="first">The first sorted list.</param>
    /// <param name="second">The second sorted list.</param>
    /// <returns><see langword="true"/> on equality.</returns>
    private static bool KeySetsEqual(List<string> first, List<string> second)
    {
        if(first.Count != second.Count)
        {
            return false;
        }

        for(int index = 0; index < first.Count; index++)
        {
            if(!string.Equals(first[index], second[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference — the funnel's subject.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>The inverse of a named object property in the example namespace.</summary>
    /// <param name="local">The forward role's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named data property node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An anonymous individual — the demand carrier the corpus template asserts.</summary>
    /// <param name="label">The blank node's label.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Anonymous(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>An enumeration of named individuals in the example namespace.</summary>
    /// <param name="individuals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>An enumeration of one named and one ANONYMOUS individual — the one-of shape that drops a funnel whole, since an anonymous member can carry no told cap.</summary>
    /// <param name="named">The named member's local name.</param>
    /// <param name="label">The anonymous member's blank-node label.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf MixedOneOf(string named, string label)
    {
        return new OwlObjectOneOf([Individual(named), Anonymous(label)]);
    }

    /// <summary>An intersection of class expressions — the combinator the funnel must NOT hide under.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf([.. operands]);
    }

    /// <summary>An existential restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An existential restriction over the inverse of a named forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(InverseProperty(property), filler);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound <c>k</c>.</param>
    /// <param name="filler">The qualifying filler, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named forward role — the demand shape.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The demand <c>m</c>.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MinObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named DATA property — the demand lookalike that bounds literals rather than domain elements.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MinData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>A told inverse between two named object properties — the axiom linking a plain funnel role to its cap role.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InverseProperties(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }
}
