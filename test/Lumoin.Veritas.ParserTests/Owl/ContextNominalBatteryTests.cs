using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The SROIQ nominal increment's module-level ground-truth battery: every
/// semantic cell of the pre-registered ground-truth sheet (26 cells, each
/// derived independently of the engine, from the modules and queries alone,
/// without the expected column) drives
/// <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>
/// at MODULE level through the opened gates - survey, clausifier, second gate,
/// nominal saturation, verdict reader - and checks consistency, the
/// context-decided path, and the EXACT module-local subsumption set.
/// Entailment queries land as their refutation encodings: a
/// <c>SameIndividual</c> conclusion as premise plus <c>DifferentIndividuals</c>
/// (unsatisfiable iff entailed), a class-membership conclusion as premise plus
/// the complemented assertion, and a <c>DifferentIndividuals</c> conclusion as
/// premise plus <c>SameIndividual</c>. A NOT-entailed face lands as premise plus
/// the negated conclusion staying consistent.
/// </summary>
/// <remarks>
/// The honesty flag: the ALC(H) tableau is nominal-blind by design,
/// so NO row here carries a tableau comparand - every decided row rests on the
/// battery and the ground-truth sheet alone unless the EL arm reaches it, and
/// the ground-truth sheet is the oracle. The automated comparand beside the battery is the
/// RL-closure differential over the told-face rows, with its lexical-join
/// carve-out stated on <see cref="NominalRlEngineDifferential"/>: RL derives the
/// told <c>sameAs</c> of a punned pair but is silent on a subclass-of-nominal
/// merge that lies beyond told closure, so it is an oracle only inside that
/// carve-out. Row derivations are stated in 7-bit ASCII: <c>[=</c> subsumption,
/// <c>{o}</c> the enumeration/nominal, <c>~</c> equality, <c>exists</c> /
/// <c>forall</c> the quantifiers, <c>inv</c> the inverse role. Two certified
/// cells - ENUM-1 and NOMR-2, the enumeration-CSP pair - carry measured
/// saturation costs above the production inference ceiling and live as their
/// own measured faces beside the row battery: ENUM-1's certified verdict and
/// exact set pin at a measured backstop, and both pin the honest
/// budget-abstention face at the production default.
/// </remarks>
[TestClass]
internal sealed class ContextNominalBatteryTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier3nominal#";

    /// <summary>
    /// The semantic battery: every decidable ground-truth sheet cell at module level, with
    /// verdict, context-decided path, and exact subsumption set checked. The loop
    /// reports every offender and fails once with the whole table.
    /// </summary>
    [TestMethod]
    public void NominalGroundSemanticBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, string[] ExpectedSubsumptions)[] rows = BatteryRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | true | final | contextDecided | subs | attempts | eq/factor/join/nom | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, string[] expectedSubsumptions) in rows)
        {
            //The production-default budget bounds every row: a certified row must decide
            //at the production ceiling (the calibration floor asserts the 50x margin
            //separately), and a defective row fails fast as a NAMED AbstainedBudget
            //mismatch here instead of hanging the suite unbounded.
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ReasoningConfiguration.Default.Budget, progressSampler: null, TestContext.CancellationToken);
            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            bool contextDecided = totals.ContextDecided;
            string cost = totals.InferenceAttempts + " | " + totals.EqApplications + "/" + totals.FactorApplications + "/" + totals.JoinApplications + "/" + totals.NomApplications;
            if(decision.Verdict is null)
            {
                report.AppendLine(name + " | " + trueConsistent + " | (no verdict: " + decision.Outcome + ") | " + contextDecided + " | - | " + cost + " | MISMATCH");
                mismatches.Add(name + ": no verdict (outcome=" + decision.Outcome + ", attempts=" + totals.InferenceAttempts + ")");

                continue;
            }

            bool finalConsistent = decision.Verdict.IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;

            List<string> expected = [.. expectedSubsumptions];
            expected.Sort(StringComparer.Ordinal);
            List<string> actual = SubsumptionKeys(decision.Verdict);
            bool subsOk = KeysEqual(expected, actual);
            string subsNote = subsOk ? "ok" : DiffKeys(expected, actual);

            bool ok = verdictOk && contextDecided && subsOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + contextDecided + " | " + subsNote + " | " + cost + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " contextDecided=" + contextDecided + " subs=" + subsNote);
            }
        }

        //The cost trace lands in the test log on every run - passing included - so a
        //cost regression names its row without a debugger.
        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The nominal guard rows (JUR-5): two
    /// shapes the lift flip now DECIDES on the context path — a <c>HasKey</c>
    /// axiom beside a nominal, which the root key join decides past the lifted
    /// key-on-nominal guard, and a data demand provably instantiated at a nominal
    /// constant on the root context, which the per-constant root arm decides per
    /// ≈-class — and one shape the anonymous-in-nominal guard still delegates whole to
    /// the fallback oracle over a non-empty unsupported-construct remainder (a blank
    /// node is existential, not a constant). Each row pins its disposition: a decide
    /// row is context-decided and consistent, a delegate row is not context-decided and
    /// names its remainder.
    /// </summary>
    [TestMethod]
    public void NominalGuardRowDispositions()
    {
        (string Name, ReasoningModule Module, string Mechanism, bool Decides)[] rows = DelegationRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | mechanism | decides | contextDecided | remainder | consistent");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, string mechanism, bool decides) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool contextDecided = decision.Statistics.ContextTotals.ContextDecided;
            bool remainderNamed = decision.Verdict!.UnsupportedConstructs.Count > 0;
            report.AppendLine(name + " | " + mechanism + " | " + decides + " | " + contextDecided + " | " + remainderNamed + " | " + decision.Verdict.IsConsistent);
            bool rowOk = decides
                ? contextDecided && decision.Verdict.IsConsistent
                : !contextDecided && remainderNamed;
            if(!rowOk)
            {
                mismatches.Add(name + ": decides=" + decides + " contextDecided=" + contextDecided + " remainderNamed=" + remainderNamed + " consistent=" + decision.Verdict.IsConsistent);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The RL-closure differential with the lexical-join carve-out asserted. The
    /// first row is inside the carve-out: FCT-1's told <c>SameIndividual(o1, o2)</c>
    /// is a told sameAs the RL closure re-derives by eq-sym, so the closure agrees
    /// with the battery on consistency and on the sameAs. The second row pins the
    /// carve-out's factual basis: on MERGE-2's <c>B [= {o}</c>, <c>B(i)</c>,
    /// <c>B(j)</c> the i~j merge is a DL8 subclass-of-nominal derivation BEYOND
    /// told closure, so the RL closure derives NO sameAs and stays consistent while
    /// the battery row certifies the merge is entailed - RL is not an
    /// oracle there, and the battery row (MERGE-2 above) is the sole
    /// oracle for the subclass-of-nominal merge.
    /// </summary>
    [TestMethod]
    public void NominalRlEngineDifferential()
    {
        StringBuilder report = new();
        report.AppendLine("\nrow | sameAs | consistent | verdict");
        List<string> mismatches = [];

        //rl-told-sameas (basis FCT-1): the told sameAs of the punned pair is
        //RL-derivable - eq-sym re-derives the reverse sameAs beyond the base - and
        //the closure stays consistent, agreeing with the battery's FCT-1.
        TermDictionary toldDictionary = new();
        OwlRlTerms toldTerms = new(toldDictionary);
        TermId toldO1 = OwlRlBatteryHelpers.Mint(toldDictionary, "fctO1");
        TermId toldO2 = OwlRlBatteryHelpers.Mint(toldDictionary, "fctO2");
        List<EncodedTriple> toldTriples = [OwlRlBatteryHelpers.Triple(toldO1, toldTerms.SameAs, toldO2)];
        OwlRlResult toldResult = OwlRlClosure.Compute(toldTriples, toldTerms, cancellationToken: TestContext.CancellationToken);
        bool toldSameAs = ContainsSameAs(toldResult, toldTerms, toldO1, toldO2);
        bool toldOk = toldResult.IsConsistent && toldSameAs;
        report.AppendLine("rl-told-sameas | derived=" + toldSameAs + " | consistent=" + toldResult.IsConsistent + " | " + (toldOk ? "OK" : "MISMATCH"));
        if(!toldOk)
        {
            mismatches.Add("rl-told-sameas (FCT-1 told face): sameAs derived=" + toldSameAs + ", consistent=" + toldResult.IsConsistent + " (both must hold)");
        }

        //rl-nominal-silent (basis MERGE-2): the subclass-of-nominal merge lies
        //beyond RL's lexical rule set - no sameAs between the two members is
        //derived and the closure stays consistent, so RL cannot see the entailed
        //merge the battery certifies. The carve-out is the point of the row.
        TermDictionary silentDictionary = new();
        OwlRlTerms silentTerms = new(silentDictionary);
        TermId silentClass = OwlRlBatteryHelpers.Mint(silentDictionary, "merge2B");
        TermId silentI = OwlRlBatteryHelpers.Mint(silentDictionary, "merge2i");
        TermId silentJ = OwlRlBatteryHelpers.Mint(silentDictionary, "merge2j");
        TermId silentNominal = OwlRlBatteryHelpers.Mint(silentDictionary, "merge2o");
        TermId silentEnum = OwlRlBatteryHelpers.Blank(silentDictionary, "merge2enum");
        List<EncodedTriple> silentTriples = [];
        TermId silentList = OwlRlBatteryHelpers.AddList(silentTriples, silentDictionary, silentTerms, [silentNominal], "merge2");
        silentTriples.Add(OwlRlBatteryHelpers.Triple(silentEnum, silentTerms.OneOf, silentList));
        silentTriples.Add(OwlRlBatteryHelpers.Triple(silentClass, silentTerms.SubClassOf, silentEnum));
        silentTriples.Add(OwlRlBatteryHelpers.Triple(silentI, silentTerms.Type, silentClass));
        silentTriples.Add(OwlRlBatteryHelpers.Triple(silentJ, silentTerms.Type, silentClass));
        OwlRlResult silentResult = OwlRlClosure.Compute(silentTriples, silentTerms, cancellationToken: TestContext.CancellationToken);
        bool silentSameAs = ContainsSameAs(silentResult, silentTerms, silentI, silentJ);
        bool silentOk = silentResult.IsConsistent && !silentSameAs;
        report.AppendLine("rl-nominal-silent | derived=" + silentSameAs + " | consistent=" + silentResult.IsConsistent + " | " + (silentOk ? "OK" : "MISMATCH"));
        if(!silentOk)
        {
            mismatches.Add("rl-nominal-silent (MERGE-2 carve-out): the lexical closure must derive NO i~j sameAs (derived=" + silentSameAs + ") and stay consistent (consistent=" + silentResult.IsConsistent + ")");
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The NOMR-1 nominal-habitat statistics witness: the inverse-plus-counting
    /// nominal module whose domain collapses to the singleton fires the Nom rule
    /// (the mint habitat) and its module carries the Nom-trigger co-occurrence
    /// census bit - nominals, object number restrictions, and inverse roles occur
    /// together.
    /// </summary>
    [TestMethod]
    public void Nomr1NominalHabitatFiresNomAndSetsCooccurrence()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Nomr1Module(), TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.IsTrue(totals.ContextDecided, "The NOMR-1 nominal habitat is context-decided.");
        Assert.IsTrue(totals.NominalCountingInverseCooccurrence, "NOMR-1 co-occurs nominals, counting, and inverse roles, so the Nom-trigger census bit is set.");
        Assert.IsGreaterThan(0L, totals.NomApplications, "The domain-collapsing nominal habitat fires the Nom rule (observed Nom=" + totals.NomApplications + ", Generated=" + totals.GeneratedNominals + ", RootSucc=" + totals.RootSuccApplications + ").");
    }

    /// <summary>
    /// The ENUM-1 raw-engine backstop witness on the ENGINE face: driven
    /// through the explicit dark control behind the lit production default,
    /// the raw engine names <see cref="ReasoningDecisionOutcome.AbstainedBudget"/>
    /// at the 600k measured inference backstop on this enumeration-algebra
    /// counting funnel - the inference leg trips (solves and conflicts
    /// unbudgeted), the abstention carries no verdict, and the exhaust's
    /// funnel profile is tautology- and redundancy-dominated over genuine
    /// insertions. The production surface decides this same habitat pre-engine
    /// through the enumeration decider face, pinned by the sibling
    /// <see cref="EnumerationCspRowsDecideAtTheProductionCeiling"/>. The pin is
    /// a two-way regression detector: it fails if the raw engine starts
    /// deciding within the backstop, if it spends other than the whole ceiling,
    /// or if the abstention's funnel identity changes.
    /// </summary>
    [TestMethod]
    public void Enum1RawEngineAbstainsAtMeasuredBackstopWithTheCertifiedSet()
    {
        ReasoningBudget measured = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 600_000);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Enum1Module(), EnumerationDeciderFaces.None, measured, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The raw engine abstains on the counting funnel at the measured backstop (observed attempts=" + totals.InferenceAttempts + ").");
        Assert.IsNull(decision.Verdict, "The backstop abstention carries no verdict - the sound engine never declares this consistent module inconsistent.");
        Assert.AreEqual(600_000L, totals.InferenceAttempts, "The inference leg trips at exactly the measured backstop - a decide-again or a shifted cost fails here and re-opens the measurement.");
        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "The abstention rides the enumeration-algebra habitat classification.");
        Assert.IsGreaterThan(0L, totals.TautologyDrops, "The exhaust drops tautologies at the funnel's first stage.");
        Assert.IsGreaterThan(0L, totals.RedundantConclusions, "The exhaust rejects redundant conclusions at the containment stage.");
        Assert.IsGreaterThan(0L, totals.WorklistEnqueues, "The exhaust still lands genuine insertions at the funnel's head.");
    }

    /// <summary>
    /// The two enumeration-CSP rows at the production default — the decided-row
    /// pins that replaced the honesty pin when the decider faces lit: both are
    /// certified by the ground-truth sheet, and the production surface now decides
    /// each pre-engine at the UNCHANGED inference ceiling — ENUM-1 consistent
    /// with the exact told-equivalence pair off the certifying face, NOMR-2
    /// inconsistent off the clash face — spending zero inference attempts and
    /// constructing no engine. The engine face's honest abstention and funnel
    /// profile stay pinned behind the explicit dark control in the decider
    /// battery.
    /// </summary>
    [TestMethod]
    public void EnumerationCspRowsDecideAtTheProductionCeiling()
    {
        ModuleDecision enumDecision = ContextSaturationModuleReasoner.DecideModule(Enum1Module(), ReasoningConfiguration.Default.Budget, progressSampler: null, TestContext.CancellationToken);
        ContextSaturationStatistics enumTotals = enumDecision.Statistics.ContextTotals;
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, enumDecision.Outcome, "ENUM-1 decides at the production default.");
        Assert.IsTrue(enumDecision.Verdict!.IsConsistent, "ENUM-1 is certified consistent.");
        Assert.AreEqual(0L, enumTotals.InferenceAttempts, "The pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, enumTotals.ContextsCreated, "No engine was constructed.");
        Assert.AreEqual(1, enumTotals.EnumerationDeciderCertifications, "The certifying face's counter reads the decision.");
        List<string> expected = [Sub("C", "D"), Sub("D", "C")];
        expected.Sort(StringComparer.Ordinal);
        List<string> actual = SubsumptionKeys(enumDecision.Verdict);
        Assert.IsTrue(KeysEqual(expected, actual), "ENUM-1's exact certified set is the told equivalence pair: " + DiffKeys(expected, actual));

        ModuleDecision nomrDecision = ContextSaturationModuleReasoner.DecideModule(Nomr2Module(), ReasoningConfiguration.Default.Budget, progressSampler: null, TestContext.CancellationToken);
        ContextSaturationStatistics nomrTotals = nomrDecision.Statistics.ContextTotals;
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, nomrDecision.Outcome, "NOMR-2 decides at the production default.");
        Assert.IsFalse(nomrDecision.Verdict!.IsConsistent, "NOMR-2 is certified inconsistent.");
        Assert.AreEqual(0L, nomrTotals.InferenceAttempts, "The pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(1, nomrTotals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
    }

    /// <summary>The environment variable naming the NOMR-2 deep probe's absolute output path; unset means the probe passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string DeepProbeVariable = "VERITAS_NOMR2_DEEP_PROBE";

    /// <summary>The environment variable naming the ENUM-1 both-modes completion-cost probe's absolute output path; unset means the probe passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string Enum1ModeCostVariable = "VERITAS_ENUM1_MODE_COST";

    /// <summary>
    /// The ENUM-1 both-modes completion-cost probe (the root-churn measurement
    /// program's compensation-cost read): measurement scaffolding that runs only
    /// when <see cref="Enum1ModeCostVariable"/> names an absolute output file,
    /// and otherwise passes without measuring. When it runs, the certified
    /// ENUM-1 cell decides under BOTH root-propagation-relevance modes at its
    /// measured 600k backstop, and each mode's outcome, attempt count, and
    /// r-Pred relevance attribution are written to the named file — the
    /// downward compensation's cost face at the one module whose completion
    /// sits near its backstop.
    /// </summary>
    [TestMethod]
    public void Enum1BothModesCompletionCostWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(Enum1ModeCostVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the ENUM-1 mode-cost probe: set " + Enum1ModeCostVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.AppendLine("ENUM-1 both-modes completion cost (600k backstop)");
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount}");
        foreach(RootPropagationRelevance relevance in (RootPropagationRelevance[])[RootPropagationRelevance.Unrestricted, RootPropagationRelevance.GroundFiltered])
        {
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Enum1Module(), relevance, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 600_000), TestContext.CancellationToken);
            clock.Stop();

            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"mode: {relevance} | outcome={decision.Outcome} | attempts={totals.InferenceAttempts} | wall={clock.Elapsed.TotalSeconds:F2}s | rootPred total={totals.RootPredApplications} sweep={totals.RootPredFromRegistrationSweep} newRootEdge={totals.RootPredFromNewRootEdge} premise={totals.RootPredFromPremise} broadcast={totals.RootPredFromBroadcast} filtered={totals.RootPredFilteredOffers} reoffered={totals.RootPredReofferedByGroundHead} tautologiesSeeded={totals.RelevanceTautologiesSeeded}");
        }

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine("ENUM-1 mode-cost probe written to " + outputPath + ".");
    }

    /// <summary>The environment variable naming the ENUM-1 both-topologies completion-cost probe's absolute output path; unset means the probe passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string Enum1TopologyCostVariable = "VERITAS_ENUM1_TOPOLOGY_COST";

    /// <summary>The escalation ceiling the topology-cost probe re-runs a backstop-abstained topology at, so a completion cost past the measured 600k backstop is located rather than reported as a bare abstention — the deep probe's power-of-two ceiling.</summary>
    private const int Enum1TopologyEscalationAttempts = 4_194_304;

    /// <summary>
    /// The ENUM-1 both-topologies completion-cost probe (the root-fragmentation
    /// measurement program's carrier-cost read): measurement scaffolding that
    /// runs only when <see cref="Enum1TopologyCostVariable"/> names an absolute
    /// output file, and otherwise passes without measuring. When it runs, the
    /// certified ENUM-1 cell decides under BOTH root-tier topologies at its
    /// measured 600k backstop — the inter-nominal carrier adds work on a
    /// multi-individual module, so a completion-cost rise must be measured, not
    /// assumed away — and a topology that abstains at the backstop is re-run
    /// once at the escalation ceiling to locate its completion cost. Each run's
    /// outcome, attempt count, root-class population count, and carrier counters
    /// are written to the named file.
    /// </summary>
    [TestMethod]
    public void Enum1BothTopologiesCompletionCostWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(Enum1TopologyCostVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the ENUM-1 topology-cost probe: set " + Enum1TopologyCostVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.AppendLine("ENUM-1 both-topologies completion cost (600k backstop; a backstop abstention escalates once to the deep-probe ceiling)");
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount}");
        foreach(RootContextTopology topology in (RootContextTopology[])[RootContextTopology.SingleRoot, RootContextTopology.PerIndividualRoots])
        {
            ReasoningDecisionOutcome outcome = AppendEnum1TopologyRun(report, topology, 600_000, "backstop");
            if(outcome == ReasoningDecisionOutcome.AbstainedBudget)
            {
                AppendEnum1TopologyRun(report, topology, Enum1TopologyEscalationAttempts, "escalated");
            }

        }

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine("ENUM-1 topology-cost probe written to " + outputPath + ".");
    }

    /// <summary>Decides the ENUM-1 module once under a root-tier topology and inference ceiling and appends the run's read line.</summary>
    /// <param name="report">The report appended to.</param>
    /// <param name="topology">The root-tier topology.</param>
    /// <param name="ceiling">The inference ceiling.</param>
    /// <param name="label">The run label distinguishing the backstop read from an escalation.</param>
    /// <returns>The decision outcome.</returns>
    private ReasoningDecisionOutcome AppendEnum1TopologyRun(StringBuilder report, RootContextTopology topology, int ceiling, string label)
    {
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Enum1Module(), topology, RootPropagationRelevance.Unrestricted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ceiling), engineProbe: null, TestContext.CancellationToken);
        clock.Stop();

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{label}: topology={topology} | ceiling={ceiling} | outcome={decision.Outcome} | attempts={totals.InferenceAttempts} | wall={clock.Elapsed.TotalSeconds:F2}s | nominalRoots={totals.NominalRootContexts} interNominal={totals.InterNominalPropagations} interNominalRedundant={totals.InterNominalRedundant} | rules hyper={totals.HyperApplications} eq={totals.EqApplications} factor={totals.FactorApplications} pred={totals.PredApplications} join={totals.JoinApplications} rootSucc={totals.RootSuccApplications} rootPred={totals.RootPredApplications} nom={totals.NomApplications}");

        return decision.Outcome;
    }

    /// <summary>The deep probe's attempt backstop — a power of two past the two-million backstop NOMR-2 is measured to exceed, so the final mark aligns with the ceiling and the growth curve carries its full tail.</summary>
    private const int DeepProbeBackstopAttempts = 4_194_304;

    /// <summary>
    /// The NOMR-2 deep probe (the root-tier measurement program's intra-root
    /// attribution instrument): measurement scaffolding that runs only when
    /// <see cref="DeepProbeVariable"/> names an absolute output file, and
    /// otherwise passes without measuring. When it runs, the certified-inconsistent
    /// NOMR-2 module saturates below the gates at the extended backstop with the
    /// sampler attached under every measured (topology, relevance, scope)
    /// combination — the single root under both root-propagation-relevance
    /// modes, the fragmented per-individual topology under the unrestricted
    /// mode, and both topologies again under the license-scoped Eq widening —
    /// and each run's
    /// per-mark growth curve, r-Pred origin and relevance-counter attribution,
    /// and per-landing Eq attribution over the root-class population (which
    /// root-class context each landed Eq application hit, keyed to its home
    /// individual) are written to the named file: the dispatch-versus-population
    /// reads and the per-nominal-root Eq relocation read the fragmentation
    /// decision point consumes.
    /// </summary>
    [TestMethod]
    public void Nomr2DeepProbeWritesTheGrowthCurve()
    {
        string? outputPath = Environment.GetEnvironmentVariable(DeepProbeVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the NOMR-2 deep probe: set " + DeepProbeVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.AppendLine("NOMR-2 deep probe (growth curve at power-of-two attempt marks, every valid topology, relevance, and scope combination)");
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount}");
        foreach((RootContextTopology topology, RootPropagationRelevance relevance, NominalParamodulationScope scope) in ((RootContextTopology Topology, RootPropagationRelevance Relevance, NominalParamodulationScope Scope)[])[
            (RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, NominalParamodulationScope.QueryScoped),
            (RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, NominalParamodulationScope.QueryScoped),
            (RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, NominalParamodulationScope.QueryScoped),
            (RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, NominalParamodulationScope.LicenseScoped),
            (RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, NominalParamodulationScope.LicenseScoped)])
        {
            ClausificationResult clausification = ContextClausifier.Clausify(Nomr2Module());
            ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, scope, relevance, topology);
            List<SaturationProgressTraceEvent> marks = [];
            engine.Progress = new SaturationProgressSampler(new ProgressMarkCollector(marks).Handle, TimeProvider.System, new Guid("3d9b7c14-8a02-4e6f-b5d3-70f4c2a9e815"));
            EqLandingAccumulator eqLandings = new();
            engine.EqLandingProbe = eqLandings.Handle;

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            SaturationOutcome outcome = engine.Saturate(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: DeepProbeBackstopAttempts), TestContext.CancellationToken);
            clock.Stop();

            ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: outcome == SaturationOutcome.Completed);
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"topology: {topology} | mode: {relevance} | scope: {scope} | module: NOMR-2 (the spy-point shape template; INCONSISTENT, decided pre-engine by the clash-only counting face) | backstop={DeepProbeBackstopAttempts} | outcome={outcome} | attempts={totals.InferenceAttempts} | wall={clock.Elapsed.TotalSeconds:F1}s");
            if(outcome == SaturationOutcome.Completed)
            {
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"completed: IsInconsistent={engine.IsInconsistent} (the certified verdict expects true)");
            }

            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"rootPred: total={totals.RootPredApplications} sweep={totals.RootPredFromRegistrationSweep} newRootEdge={totals.RootPredFromNewRootEdge} premise={totals.RootPredFromPremise} broadcast={totals.RootPredFromBroadcast} filtered={totals.RootPredFilteredOffers} reoffered={totals.RootPredReofferedByGroundHead} tautologiesSeeded={totals.RelevanceTautologiesSeeded}");
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"rootClass: nominalRoots={totals.NominalRootContexts} interNominal={totals.InterNominalPropagations} interNominalRedundant={totals.InterNominalRedundant} generatedNominals={totals.GeneratedNominals} labelDepth={totals.MaxNominalLabelDepth}");
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"eqScope: blockedQueryAtom={totals.EqScopeBlockedQueryAtom} blockedRootClass={totals.EqScopeBlockedRootClass} tagJoins={totals.EqScopeTagJoins}");
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"rules: hyper={totals.HyperApplications} eq={totals.EqApplications} ineq={totals.IneqApplications} factor={totals.FactorApplications} pred={totals.PredApplications} join={totals.JoinApplications} rootSucc={totals.RootSuccApplications} rootPred={totals.RootPredApplications} nom={totals.NomApplications}");
            AppendEqAttribution(report, engine, eqLandings, totals.EqApplications);
            report.AppendLine("seq | attempts | live | derived | eliminated | maxCtx | rootCtx | nominalRoots | tautology | redundant | outOfGrammar | enqueues | queue | eager | succ | nominals | depth | hyper | eq | factor | pred | join | rSucc | rPred | nom");
            foreach(SaturationProgressTraceEvent mark in marks)
            {
                report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{mark.SequenceNumber} | {mark.InferenceAttempts} | {mark.ClausesDerived - mark.ClausesEliminated} | {mark.ClausesDerived} | {mark.ClausesEliminated} | {mark.MaxContextClauses} | {mark.RootContextClauses} | {mark.NominalRootContexts} | {mark.TautologyDrops} | {mark.RedundantConclusions} | {mark.OutOfGrammarConclusions} | {mark.WorklistEnqueues} | {mark.QueueDepth} | {mark.EagerQueueDepth} | {mark.SuccQueueDepth} | {mark.GeneratedNominals} | {mark.MaxNominalLabelDepth} | {mark.HyperApplications} | {mark.EqApplications} | {mark.FactorApplications} | {mark.PredApplications} | {mark.JoinApplications} | {mark.RootSuccApplications} | {mark.RootPredApplications} | {mark.NomApplications}");
            }

        }

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine("NOMR-2 deep probe written to " + outputPath + ".");
    }

    /// <summary>
    /// Appends one probe run's per-landing Eq attribution block: the probe total
    /// against the engine's landed-Eq counter (an instrument self-check; the two
    /// count the same single landing site), the root-class versus ordinary split
    /// with the root-class share, and one row per root-class context joining its
    /// home individual and live population with its landed-Eq count, largest
    /// first — the per-nominal-root relocation read.
    /// </summary>
    /// <param name="report">The report appended to.</param>
    /// <param name="engine">The saturated engine whose root-class population is read.</param>
    /// <param name="eqLandings">The run's per-context landed-Eq accumulation.</param>
    /// <param name="landedEqTotal">The engine's landed-Eq counter for the run.</param>
    private static void AppendEqAttribution(StringBuilder report, ContextSaturationEngine engine, EqLandingAccumulator eqLandings, long landedEqTotal)
    {
        long probeTotal = eqLandings.RootClassLanded + eqLandings.OrdinaryLanded;
        double rootShare = probeTotal == 0 ? 0.0 : 100.0 * eqLandings.RootClassLanded / probeTotal;
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"eqAttribution: landedEq={landedEqTotal} probeTotal={probeTotal} rootClassLanded={eqLandings.RootClassLanded} ({rootShare:F1}% of landed) ordinaryLanded={eqLandings.OrdinaryLanded}");
        report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"eqShapes: {eqLandings.ShapeLine()}");

        List<RootClassPopulationRow> population = [];
        engine.AppendRootClassPopulation(population);
        List<(long Landed, RootClassPopulationRow Row)> rows = [];
        foreach(RootClassPopulationRow row in population)
        {
            rows.Add((eqLandings.LandedByContextId.GetValueOrDefault(row.ContextId), row));
        }

        rows.Sort(CompareByLandedDescending);
        report.AppendLine("rootClassRow | ctx | homeIndividual | landedEq | liveClauses");
        foreach((long landed, RootClassPopulationRow row) in rows)
        {
            report.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"rootClassRow | {row.ContextId} | {row.HomeIndividualName} | {landed} | {row.LiveClauses}");
        }

    }

    /// <summary>Orders per-root-class attribution rows largest landed-Eq first, context id ascending on ties.</summary>
    /// <param name="first">The first row.</param>
    /// <param name="second">The second row.</param>
    /// <returns>The comparison result.</returns>
    private static int CompareByLandedDescending((long Landed, RootClassPopulationRow Row) first, (long Landed, RootClassPopulationRow Row) second)
    {
        int byLanded = second.Landed.CompareTo(first.Landed);

        return byLanded != 0 ? byLanded : first.Row.ContextId.CompareTo(second.Row.ContextId);
    }

    /// <summary>Carries the per-context and per-rewrite-shape landed-Eq accumulation behind an <see cref="EqLandingProbeDelegate"/> as explicit state, so the handler closes over no enclosing local. The shape split classifies each landing by its acting rewrite: the scopable individual-to-central form <c>o ↦ x</c> (the only shape any scope value gates), the ground individual-to-individual form <c>o ≈ o′</c>, the never-scoped context-variable replacement form <c>y ≈ o</c>, and the residual function-bearing forms — accumulated separately for root-class and ordinary landing contexts.</summary>
    internal sealed class EqLandingAccumulator
    {
        /// <summary>The landed-Eq count per landing context id.</summary>
        public Dictionary<int, long> LandedByContextId { get; } = [];

        /// <summary>The landed-Eq total across root-class landing contexts.</summary>
        public long RootClassLanded { get; private set; }

        /// <summary>The landed-Eq total across ordinary landing contexts.</summary>
        public long OrdinaryLanded { get; private set; }

        /// <summary>The root-class landings whose rewrite is the scopable individual-to-central form <c>o ↦ x</c>.</summary>
        public long RootScopable { get; private set; }

        /// <summary>The root-class landings whose rewrite is the ground individual-to-individual form <c>o ≈ o′</c>.</summary>
        public long RootGround { get; private set; }

        /// <summary>The root-class landings whose rewrite replaces toward a non-central variable — the never-scoped <c>y ≈ o</c> family.</summary>
        public long RootContextVariable { get; private set; }

        /// <summary>The root-class landings of every residual shape — a function-bearing source or replacement.</summary>
        public long RootFunctional { get; private set; }

        /// <summary>The ordinary-context landings whose rewrite is the scopable individual-to-central form <c>o ↦ x</c>.</summary>
        public long OrdinaryScopable { get; private set; }

        /// <summary>The ordinary-context landings whose rewrite is the ground individual-to-individual form <c>o ≈ o′</c>.</summary>
        public long OrdinaryGround { get; private set; }

        /// <summary>The ordinary-context landings whose rewrite replaces toward a non-central variable — the never-scoped <c>y ≈ o</c> family.</summary>
        public long OrdinaryContextVariable { get; private set; }

        /// <summary>The ordinary-context landings of every residual shape — a function-bearing source or replacement.</summary>
        public long OrdinaryFunctional { get; private set; }

        /// <summary>Accumulates one landed Eq application against its landing context and rewrite shape.</summary>
        /// <param name="context">The landing context.</param>
        /// <param name="fromTerm">The acting rewrite's source term.</param>
        /// <param name="replacement">The acting rewrite's replacement term.</param>
        public void Handle(Context context, DlTerm fromTerm, DlTerm replacement)
        {
            LandedByContextId[context.Id] = LandedByContextId.GetValueOrDefault(context.Id) + 1;
            bool scopable = fromTerm.IsIndividual && replacement.IsCentral;
            bool ground = fromTerm.IsIndividual && replacement.IsIndividual;
            bool contextVariable = fromTerm.IsIndividual && replacement.IsVariable && !replacement.IsCentral;
            if(context.IsRoot)
            {
                RootClassLanded++;
                if(scopable)
                {
                    RootScopable++;
                }
                else if(ground)
                {
                    RootGround++;
                }
                else if(contextVariable)
                {
                    RootContextVariable++;
                }
                else
                {
                    RootFunctional++;
                }

            }
            else
            {
                OrdinaryLanded++;
                if(scopable)
                {
                    OrdinaryScopable++;
                }
                else if(ground)
                {
                    OrdinaryGround++;
                }
                else if(contextVariable)
                {
                    OrdinaryContextVariable++;
                }
                else
                {
                    OrdinaryFunctional++;
                }

            }

        }

        /// <summary>The shape split as one report line fragment: root-class then ordinary, scopable/ground/contextVar/functional each.</summary>
        /// <returns>The line fragment.</returns>
        public string ShapeLine()
        {
            return "root scopable=" + RootScopable + " ground=" + RootGround + " contextVar=" + RootContextVariable + " functional=" + RootFunctional
                + " | ordinary scopable=" + OrdinaryScopable + " ground=" + OrdinaryGround + " contextVar=" + OrdinaryContextVariable + " functional=" + OrdinaryFunctional;
        }

    }

    /// <summary>Carries the mark list behind a progress handler as explicit state, so the handler closes over no enclosing local.</summary>
    /// <param name="marks">The list receiving each emitted mark.</param>
    private sealed class ProgressMarkCollector(List<SaturationProgressTraceEvent> marks)
    {
        /// <summary>The list receiving each emitted mark.</summary>
        private List<SaturationProgressTraceEvent> Marks { get; } = marks;

        /// <summary>Appends one emitted mark.</summary>
        /// <param name="mark">The mark.</param>
        public void Handle(in SaturationProgressTraceEvent mark)
        {
            Marks.Add(mark);
        }
    }

    /// <summary>
    /// The FCT-1 Factor-live statistics witness: the two-member enumeration head
    /// factorizes under the told <c>o1 ~ o2</c> collapse, so Factor fires through
    /// the explicit-dark module decision - the ENGINE face beneath the lit
    /// production decider, the fires-witness companion to the pinned-inert
    /// nominal-free row.
    /// </summary>
    [TestMethod]
    public void Fct1FactorFiresThroughDecideModule()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Fct1Module(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.IsTrue(totals.ContextDecided, "The FCT-1 enumeration module is context-decided.");
        Assert.IsGreaterThan(0L, totals.FactorApplications, "The enumeration head whose disjuncts share the variable side factorizes under the told member collapse (observed Factor=" + totals.FactorApplications + ").");
    }

    /// <summary>
    /// The ROOTX-1 root-exchange statistics witness: the non-local multi-owner
    /// chain opens a root edge over the nominal constant and completes it back into
    /// a different owner's successor context, so both the r-Succ and r-Pred root
    /// rules fire through the module decision.
    /// </summary>
    [TestMethod]
    public void Rootx1RootExchangeAppliesBothDirections()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Rootx1Module(), TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.IsTrue(totals.ContextDecided, "The ROOTX-1 multi-owner chain is context-decided.");
        Assert.IsGreaterThan(0L, totals.RootSuccApplications, "The derived ground role opens the root edge over the nominal constant (observed RootSucc=" + totals.RootSuccApplications + ").");
        Assert.IsGreaterThan(0L, totals.RootPredApplications, "The root clause completes back into the second owner's successor context (observed RootPred=" + totals.RootPredApplications + ").");
        Assert.IsGreaterThan(0L, totals.WorklistEnqueues, "A context-decided exchange lands clauses on the worklist - the funnel's head is non-zero on every decided decision (observed enqueues=" + totals.WorklistEnqueues + ").");
    }

    /// <summary>
    /// The calibration floor scoped to the nominal increment (the Cb1 sibling): no
    /// context-decided row of this battery spends more than a small fraction of the
    /// production default inference ceiling, so no certified nominal decision
    /// abstains at the default. The floor measures the budget-gated
    /// <see cref="ContextSaturationStatistics.InferenceAttempts"/> accumulator the
    /// budget bounds, and its 50x margin is asserted against the maximum observed
    /// over the context-decided rows.
    /// </summary>
    [TestMethod]
    public void NominalCalibrationFloorHoldsAtTheProductionDefault()
    {
        long max = 0;
        string maxRow = "";
        foreach((string name, ReasoningModule module, bool _, string[] _) in BatteryRows())
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            if(!decision.Statistics.ContextTotals.ContextDecided)
            {
                continue;
            }

            long attempts = decision.Statistics.ContextTotals.InferenceAttempts;
            if(attempts > max)
            {
                max = attempts;
                maxRow = name;
            }
        }

        long ceiling = ReasoningConfiguration.Default.Budget.MaxInferences;
        Assert.IsGreaterThanOrEqualTo(50 * max, ceiling, "The production default inference ceiling " + ceiling + " must be at least 50x the maximum observed context InferenceAttempts " + max + " (row " + maxRow + "), so no certified nominal decision abstains at the default.");
    }

    /// <summary>
    /// The decidable battery rows: ground-truth sheet id (refutation-encoded ids carry the
    /// <c>r</c> suffix, a NOT-entailed face the <c>n</c> suffix), module,
    /// ground-truth consistency per the certified ground-truth sheet, and the exact expected
    /// module-local subsumption set. The exact-set check guards against phantom
    /// subsumptions and enforces the certified negatives (an entailed pair absent
    /// from the set would be a silent miss; a pair present that the ground-truth sheet
    /// certifies NOT entailed would be a phantom).
    /// </summary>
    /// <returns>The rows.</returns>
    internal static (string Name, ReasoningModule Module, bool TrueConsistent, string[] ExpectedSubsumptions)[] BatteryRows()
    {
        return
        [
            //ENUM-2: B[={o1,o2}, B(i), i!=o1, i!=o2. i must be one of the two members, both barred, so the
            //enumeration has nowhere to land: inconsistent.
            ("ENUM-2", Module(
                SubClassOf(Class("B"), OneOf("o1", "o2")),
                ClassAssertion(Class("B"), Individual("i")),
                Different("i", "o1"),
                Different("i", "o2")),
                false, []),

            //ENUM-3: C=={a}, C(b). Consistent baseline (b collapses onto a); the singleton enumeration
            //superclass is unnamed, so no named subsumption.
            ("ENUM-3", Module(
                Equivalent(Class("C"), OneOf("a")),
                ClassAssertion(Class("C"), Individual("b"))),
                true, []),

            //ENUM-3r: the refutation of SameIndividual(a,b) - asserting a!=b against the forced b~a clash.
            ("ENUM-3r", Module(
                Equivalent(Class("C"), OneOf("a")),
                ClassAssertion(Class("C"), Individual("b")),
                Different("a", "b")),
                false, []),

            //HVAL-1: Fun(r), A[=r has o1, A[=r has o2, A(i). i's single r-successor is both o1 and o2;
            //consistent baseline (no UNA merges them), no named subsumption.
            ("HVAL-1", Module(
                Functional("r"),
                SubClassOf(Class("A"), HasValue("r", "o1")),
                SubClassOf(Class("A"), HasValue("r", "o2")),
                ClassAssertion(Class("A"), Individual("i"))),
                true, []),

            //HVAL-1r: the refutation of SameIndividual(o1,o2) - the functional merge against told o1!=o2.
            ("HVAL-1r", Module(
                Functional("r"),
                SubClassOf(Class("A"), HasValue("r", "o1")),
                SubClassOf(Class("A"), HasValue("r", "o2")),
                ClassAssertion(Class("A"), Individual("i")),
                Different("o1", "o2")),
                false, []),

            //HVAL-2: Fan==likes has cricket, likes(j,cricket). j's cricket edge lowers the fresh singleton
            //so j is a Fan; consistent baseline, no named subsumption (the equivalent is unnamed).
            ("HVAL-2", Module(
                Equivalent(Class("Fan"), HasValue("likes", "cricket")),
                Edge("likes", "j", "cricket")),
                true, []),

            //HVAL-2r: the refutation of Fan(j) - asserting the complement of Fan on j against the forced
            //membership.
            ("HVAL-2r", Module(
                Equivalent(Class("Fan"), HasValue("likes", "cricket")),
                Edge("likes", "j", "cricket"),
                ClassAssertion(Complement(Class("Fan")), Individual("j"))),
                false, []),

            //MERGE-1: C=={c}, D[=C, D(d). Told D[=C; and in every model d=c is in D, so C={c}[=D - the
            //punned pair is equivalent. Consistent.
            ("MERGE-1", Module(
                Equivalent(Class("C"), OneOf("c")),
                SubClassOf(Class("D"), Class("C")),
                ClassAssertion(Class("D"), Individual("d"))),
                true, [Sub("C", "D"), Sub("D", "C")]),

            //MERGE-1r: the refutation of SameIndividual(d,c) - d!=c against the forced d~c merge.
            ("MERGE-1r", Module(
                Equivalent(Class("C"), OneOf("c")),
                SubClassOf(Class("D"), Class("C")),
                ClassAssertion(Class("D"), Individual("d")),
                Different("d", "c")),
                false, []),

            //MERGE-2: B[={o}, B(i), B(j). Both i and j equal o; consistent baseline, no named subsumption
            //(the enumeration superclass is unnamed).
            ("MERGE-2", Module(
                SubClassOf(Class("B"), OneOf("o")),
                ClassAssertion(Class("B"), Individual("i")),
                ClassAssertion(Class("B"), Individual("j"))),
                true, []),

            //MERGE-2r: the refutation of SameIndividual(i,j) - i!=j against the transitive merge onto o.
            ("MERGE-2r", Module(
                SubClassOf(Class("B"), OneOf("o")),
                ClassAssertion(Class("B"), Individual("i")),
                ClassAssertion(Class("B"), Individual("j")),
                Different("i", "j")),
                false, []),

            //ROOTX-1: the non-local root exchange. a's r-successor is forced = o (in C1); forall r.D puts D
            //on it; D[=E returns E through the root and back into A2's s-successor context. Consistent; the
            //exact set carries A[=F, A2[=H, told D[=E, and the implicit C1[=D, C1[=E (o forced into D hence
            //E in every model, since A(a) is asserted). The certified negatives A[=H and A2[=F are absent.
            ("ROOTX-1", Rootx1Module(),
                true, [Sub("A", "F"), Sub("A2", "H"), Sub("D", "E"), Sub("C1", "D"), Sub("C1", "E")]),

            //PIGN-1: A[=max 2 r, A(a), three r-edges, b1!=b2, P(b1), P(b2). b3 merges with b1 OR b2 (a
            //genuine choice), so consistent; no named subsumption. The inert nominal gives the module
            //nominal jurisdiction so the ground counting routes through the general root path.
            ("PIGN-1", Module(
                InertNominal(),
                SubClassOf(Class("A"), Max("r", 2, null)),
                ClassAssertion(Class("A"), Individual("a")),
                Edge("r", "a", "b1"),
                Edge("r", "a", "b2"),
                Edge("r", "a", "b3"),
                Different("b1", "b2"),
                ClassAssertion(Class("P"), Individual("b1")),
                ClassAssertion(Class("P"), Individual("b2"))),
                true, []),

            //PIGN-1r: the refutation of P(b3) - asserting the complement of P on b3. P holds in BOTH
            //branches of b3's merge (b3=b1 gives P(b1), b3=b2 gives P(b2)), so the complement clashes
            //through disjunction elimination over the merge: inconsistent.
            ("PIGN-1r", Module(
                InertNominal(),
                SubClassOf(Class("A"), Max("r", 2, null)),
                ClassAssertion(Class("A"), Individual("a")),
                Edge("r", "a", "b1"),
                Edge("r", "a", "b2"),
                Edge("r", "a", "b3"),
                Different("b1", "b2"),
                ClassAssertion(Class("P"), Individual("b1")),
                ClassAssertion(Class("P"), Individual("b2")),
                ClassAssertion(Complement(Class("P")), Individual("b3"))),
                false, []),

            //PIGN-1n: the NOT-entailed SameIndividual(b3,b1) - adding b3!=b1 forces b3=b2, which is a
            //model, so the module stays consistent (the merge choice is genuine).
            ("PIGN-1n", Module(
                InertNominal(),
                SubClassOf(Class("A"), Max("r", 2, null)),
                ClassAssertion(Class("A"), Individual("a")),
                Edge("r", "a", "b1"),
                Edge("r", "a", "b2"),
                Edge("r", "a", "b3"),
                Different("b1", "b2"),
                ClassAssertion(Class("P"), Individual("b1")),
                ClassAssertion(Class("P"), Individual("b2")),
                Different("b3", "b1")),
                true, []),

            //PIGN-2: A[=max 2 r, A(a), three r-edges, b1/b2/b3 pairwise distinct. Three distinct successors
            //under a told bound of two: inconsistent through the general path.
            ("PIGN-2", Module(
                InertNominal(),
                SubClassOf(Class("A"), Max("r", 2, null)),
                ClassAssertion(Class("A"), Individual("a")),
                Edge("r", "a", "b1"),
                Edge("r", "a", "b2"),
                Edge("r", "a", "b3"),
                Different("b1", "b2", "b3")),
                false, []),

            //PIGN-3: A[=max 1 r.Q, A(a), r-edges to b1 and b2, Q(b1), Q(b2). Two told Q-successors under a
            //qualified bound of one are forced equal (no choice); consistent baseline, no named subsumption.
            ("PIGN-3", Module(
                InertNominal(),
                SubClassOf(Class("A"), Max("r", 1, Class("Q"))),
                ClassAssertion(Class("A"), Individual("a")),
                Edge("r", "a", "b1"),
                Edge("r", "a", "b2"),
                ClassAssertion(Class("Q"), Individual("b1")),
                ClassAssertion(Class("Q"), Individual("b2"))),
                true, []),

            //PIGN-3r: the refutation of SameIndividual(b1,b2) - adding b1!=b2 against the forced qualified
            //merge: inconsistent.
            ("PIGN-3r", Module(
                InertNominal(),
                SubClassOf(Class("A"), Max("r", 1, Class("Q"))),
                ClassAssertion(Class("A"), Individual("a")),
                Edge("r", "a", "b1"),
                Edge("r", "a", "b2"),
                ClassAssertion(Class("Q"), Individual("b1")),
                ClassAssertion(Class("Q"), Individual("b2")),
                Different("b1", "b2")),
                false, []),

            //INVN-1: C1=={o}, A[=exists r.C1, exists inv(r).A[=B, exists r.B[=F, A(a). a's r-successor is
            //o and o's r-predecessor a is in A, so o is a B: C1={o}[=B. Every A-instance's r-successor is o
            //(a B), so A[=F. C1[=F is NOT entailed (o has no forced r-successor in B). Consistent.
            ("INVN-1", Module(
                Equivalent(Class("C1"), OneOf("o")),
                SubClassOf(Class("A"), Some("r", Class("C1"))),
                SubClassOf(SomeInverse("r", Class("A")), Class("B")),
                SubClassOf(Some("r", Class("B")), Class("F")),
                ClassAssertion(Class("A"), Individual("a"))),
                true, [Sub("A", "F"), Sub("C1", "B")]),

            //INVN-1r: the refutation of B(o) - asserting the complement of B on o against the forced
            //membership o is a B.
            ("INVN-1r", Module(
                Equivalent(Class("C1"), OneOf("o")),
                SubClassOf(Class("A"), Some("r", Class("C1"))),
                SubClassOf(SomeInverse("r", Class("A")), Class("B")),
                SubClassOf(Some("r", Class("B")), Class("F")),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Complement(Class("B")), Individual("o"))),
                false, []),

            //NOMR-1: Top[=exists inv(r).{o} (every element has r-predecessor o), {o}[=max 1 r, P(o),
            //C(i1). o's single r-successor is o (o is its own r-predecessor), and every element is an
            //r-successor of o, so the domain collapses to {o}. Then C=P={o}: both directions. Consistent.
            ("NOMR-1", Nomr1Module(),
                true, [Sub("C", "P"), Sub("P", "C")]),

            //NOMR-1r: the refutation of SameIndividual(i1,o) - i1!=o against the domain collapse forcing
            //i1=o.
            ("NOMR-1r", Extend(Nomr1Module(), Different("i1", "o")),
                false, []),

            //FCT-1: B=={o1,o2}, SameIndividual(o1,o2), B(i). The two-member enumeration collapses to one
            //under o1~o2; consistent baseline, no named subsumption.
            ("FCT-1", Fct1Module(),
                true, []),

            //FCT-1r: the refutation of SameIndividual(i,o1) - i!=o1 against the collapse forcing i onto the
            //single surviving member.
            ("FCT-1r", Extend(Fct1Module(), Different("i", "o1")),
                false, []),

            //EQG-1: A=={o}, A[=exists r.B, Fun(r), r(o,w), B[=C. o has an r-successor in B; functional
            //merges it with the told successor w, so w is in B[=C. Consistent; the only named subsumption
            //is the told B[=C (A's relationships run through unnamed supers).
            ("EQG-1", Module(
                Equivalent(Class("A"), OneOf("o")),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                Functional("r"),
                Edge("r", "o", "w"),
                SubClassOf(Class("B"), Class("C"))),
                true, [Sub("B", "C")]),

            //EQG-1r: the refutation of C(w) - asserting the complement of C on w against the functional
            //merge forcing w into B[=C.
            ("EQG-1r", Module(
                Equivalent(Class("A"), OneOf("o")),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                Functional("r"),
                Edge("r", "o", "w"),
                SubClassOf(Class("B"), Class("C")),
                ClassAssertion(Complement(Class("C")), Individual("w"))),
                false, []),

            //ORD-1: A[={o}, {o}[=B. A is a subset of {o} which is a subset of B, so A[=B reads off through
            //the unoriented central-against-constant equality, even when A is empty. Consistent.
            ("ORD-1", Module(
                SubClassOf(Class("A"), OneOf("o")),
                SubClassOf(OneOf("o"), Class("B"))),
                true, [Sub("A", "B")]),

            //NEUT-1: C=={o1,o2}, D[=C. Told D[=C only; C[=D does NOT hold (D is an arbitrary subset of the
            //two-member enumeration) and the disjunctive equality head yields no named subsumption.
            ("NEUT-1", Module(
                Equivalent(Class("C"), OneOf("o1", "o2")),
                SubClassOf(Class("D"), Class("C"))),
                true, [Sub("D", "C")]),

            //NEUT-2: B[={o}, B(i), i!=o. i must equal o yet is declared different: the ground clash folds
            //before read-off. Inconsistent.
            ("NEUT-2", Module(
                SubClassOf(Class("B"), OneOf("o")),
                ClassAssertion(Class("B"), Individual("i")),
                Different("i", "o")),
                false, []),

            //GRD-CHAIN: Trans(t), C=={o}, A[=exists t.C, C[=exists t.D, exists t.D[=E. The t-chain
            //a -> o -> d composes under transitivity, so A[=E; and o's t-successor in D gives C[=E. No
            //other named pair. Consistent (H-T3-2 decide column).
            ("GRD-CHAIN", Module(
                Transitive("t"),
                Equivalent(Class("C"), OneOf("o")),
                SubClassOf(Class("A"), Some("t", Class("C"))),
                SubClassOf(Class("C"), Some("t", Class("D"))),
                SubClassOf(Some("t", Class("D")), Class("E"))),
                true, [Sub("A", "E"), Sub("C", "E")]),

            //GRD-SELF: A[=Self(e), Self(e)[=B, C=={o}, C[=A. The self-loop makes A[=B; the told C[=A
            //composes to C[=B. Consistent (H-T3-3 decide column).
            ("GRD-SELF", Module(
                SubClassOf(Class("A"), HasSelf("e")),
                SubClassOf(HasSelf("e"), Class("B")),
                Equivalent(Class("C"), OneOf("o")),
                SubClassOf(Class("C"), Class("A"))),
                true, [Sub("A", "B"), Sub("C", "A"), Sub("C", "B")]),
        ];
    }

    /// <summary>
    /// The nominal guard rows: ground-truth sheet id, module, the mechanism the row exercises,
    /// and whether the lift flip decides it on the context path (the key join
    /// and the per-constant data arm decide past their lifted guards) or the
    /// anonymous-in-nominal guard still delegates it (the below-gate mechanics are
    /// pinned in the engine pin battery).
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module, string Mechanism, bool Decides)[] DelegationRows()
    {
        return
        [
            //GRD-KEY: a HasKey axiom beside a nominal construct - the root key join routes the module
            //past the lifted key-on-nominal guard into intake and decides its keyed candidates on the
            //root tier; one keyed candidate joins no pair, so it decides CONSISTENT (JUR-5).
            ("GRD-KEY", Module(
                HasKey(Class("K"), ["feeds"], []),
                Equivalent(Class("C"), OneOf("o")),
                ClassAssertion(Class("K"), Individual("k1"))),
                "root key join (guard lifted)", true),

            //GRD-DATA: a data demand that provably instantiates at the nominal constant o (C=={o} forces
            //C(o) by DL7), landing on the root context, where the per-constant root arm decides its
            //≈-class off the pooled read-time union; a lone integer existential realizes, so it decides
            //CONSISTENT (JUR-5).
            ("GRD-DATA", Module(
                Equivalent(Class("C"), OneOf("o")),
                SubClassOf(Class("C"), DataSome("dp", Integer))),
                "per-constant root data arm", true),

            //GRD-BNODE: an anonymous individual in a has-value filler - a blank node is existential, not a
            //constant, so the survey's anonymous-in-nominal guard delegates the whole module.
            ("GRD-BNODE", Module(
                SubClassOf(Class("A"), HasValueBlank("r", "anon"))),
                "AnonymousIndividualInNominal guard", false),
        ];
    }

    /// <summary>
    /// The ROOTX-1 module: the non-local root exchange with two owners at depth two
    /// - a's r-successor is forced onto the nominal constant, the D-fact transits
    /// through the root context to combine with the told <c>D [= E</c>, and the
    /// result returns into A2's s-successor context, a different owner.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule Rootx1Module()
    {
        return Module(
            Equivalent(Class("C1"), OneOf("o")),
            SubClassOf(Class("A"), Some("r", Class("C1"))),
            SubClassOf(Class("A"), All("r", Class("D"))),
            SubClassOf(Class("D"), Class("E")),
            ClassAssertion(Class("A"), Individual("a")),
            SubClassOf(Class("A2"), Some("s", Class("C1"))),
            SubClassOf(Some("s", Class("E")), Class("H")),
            SubClassOf(Some("r", Class("E")), Class("F")));
    }

    /// <summary>
    /// The NOMR-1 module: every element has the nominal constant as its r-predecessor
    /// and the constant carries at most one r-successor, so the whole domain
    /// collapses to the singleton and the two asserted classes both equal it.
    /// </summary>
    /// <returns>The module.</returns>
    internal static ReasoningModule Nomr1Module()
    {
        return Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, null)),
            ClassAssertion(Class("P"), Individual("o")),
            ClassAssertion(Class("C"), Individual("i1")));
    }

    /// <summary>
    /// The FCT-1 module: a two-member enumeration whose members are told equal, so
    /// the disjunctive enumeration head factorizes under the collapse and the sole
    /// asserted individual lands on the single surviving member.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule Fct1Module()
    {
        return Module(
            Equivalent(Class("B"), OneOf("o1", "o2")),
            Same("o1", "o2"),
            ClassAssertion(Class("B"), Individual("i")));
    }

    /// <summary>
    /// The ENUM-1 module (the oneOf-001 shape): two enumerations told equivalent,
    /// consistent by member collapse under UNA absence. Its subsumption read-off
    /// face is the measured enumeration-CSP row: the query-context equality
    /// saturation completes at roughly 557k inference attempts, an order of
    /// magnitude past the production ceiling.
    /// </summary>
    /// <returns>The module.</returns>
    internal static ReasoningModule Enum1Module()
    {
        return Module(
            Equivalent(Class("C"), OneOf("a", "b", "c")),
            Equivalent(Class("D"), OneOf("y", "z")),
            Equivalent(Class("C"), Class("D")));
    }

    /// <summary>
    /// The NOMR-2 module (the spy-point shape template told through the counting
    /// face's own routes): every element is an r-successor of the nominal
    /// constant, the constant carries at most two, and three pairwise-distinct
    /// individuals are asserted - INCONSISTENT, and decided pre-engine by the
    /// clash-only counting face, whose told-distinct clique of three outruns the
    /// told cap of two. Its saturation cost exceeds a two-million-attempt
    /// backstop (measured), so no locally reachable budget verifies the clash
    /// in-engine and the engine-side deep probe reads the abstention rather than
    /// the verdict.
    /// </summary>
    /// <returns>The module.</returns>
    internal static ReasoningModule Nomr2Module()
    {
        return Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 2, null)),
            Different("i1", "i2", "i3"));
    }

    /// <summary>Whether the closure derived <c>owl:sameAs</c> between the two individuals in either orientation, beyond the base triples.</summary>
    /// <param name="result">The RL closure result.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    /// <returns><see langword="true"/> when a sameAs between the pair is in the derived set.</returns>
    private static bool ContainsSameAs(OwlRlResult result, OwlRlTerms terms, TermId first, TermId second)
    {
        foreach(EncodedTriple triple in result.Derived)
        {
            if(triple.Predicate == terms.SameAs && ((triple.Subject == first && triple.Object == second) || (triple.Subject == second && triple.Object == first)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The verdict's subsumption pairs as sorted comparison keys, one <c>subIri-&gt;superIri</c> string per pair.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The keys, sorted ordinally.</returns>
    private static List<string> SubsumptionKeys(ModuleVerdict verdict)
    {
        List<string> keys = new(verdict.Subsumptions.Count);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add($"{subClass.Iri}->{superClass.Iri}");
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>A sorted subsumption key over two example-namespace local names.</summary>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <returns>The <c>subIri-&gt;superIri</c> key.</returns>
    private static string Sub(string sub, string super)
    {
        return $"{Example}{sub}->{Example}{super}";
    }

    /// <summary>Whether two sorted key lists hold the same keys in the same order.</summary>
    /// <param name="expected">The expected sorted keys.</param>
    /// <param name="actual">The actual sorted keys.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool KeysEqual(List<string> expected, List<string> actual)
    {
        if(expected.Count != actual.Count)
        {
            return false;
        }

        for(int i = 0; i < expected.Count; i++)
        {
            if(!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The missing (expected, absent) and phantom (present, unexpected) keys between an expected and an actual sorted key list, for the offender report.</summary>
    /// <param name="expected">The expected sorted keys.</param>
    /// <param name="actual">The actual sorted keys.</param>
    /// <returns>The rendered difference.</returns>
    private static string DiffKeys(List<string> expected, List<string> actual)
    {
        List<string> missing = [];
        foreach(string key in expected)
        {
            if(!actual.Contains(key))
            {
                missing.Add(key);
            }
        }

        List<string> phantom = [];
        foreach(string key in actual)
        {
            if(!expected.Contains(key))
            {
                phantom.Add(key);
            }
        }

        return "missing=[" + string.Join(",", missing) + "] phantom=[" + string.Join(",", phantom) + "]";
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Extends a module with additional axioms - the refutation encodings append their negated conclusion to the certified premise.</summary>
    /// <param name="baseModule">The premise module.</param>
    /// <param name="extra">The axioms to append.</param>
    /// <returns>The extended module.</returns>
    private static ReasoningModule Extend(ReasoningModule baseModule, params OwlAxiom[] extra)
    {
        return new ReasoningModule([.. baseModule.Axioms, .. extra], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference - the universal class the domain-collapse rows constrain.</summary>
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

    /// <summary>An enumeration of individuals in the example namespace; a single individual is the nominal <c>{a}</c>.</summary>
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

    /// <summary>An individual-value restriction over a forward role and a named individual.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An individual-value restriction over a forward role and an anonymous individual - the anonymous-in-nominal guard face.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="label">The blank-node label.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValueBlank(string property, string label)
    {
        return new OwlObjectHasValue(Property(property), new BlankNode(Utf8Strings.From(label)));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An existential restriction over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(InverseProperty(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A local-reflexivity restriction over a forward role - <c>ObjectHasSelf</c>.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The self restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>An equivalence between two class expressions.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equiv") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An asserted object-property edge between two named individuals.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string property, string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(Utf8Strings.From(Example + property)), Individual(target)) { Origin = Origin("edge") };
    }

    /// <summary>A same-individual axiom pairing two named individuals.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A different-individuals axiom over the named individuals - pairwise distinct.</summary>
    /// <param name="individuals">The pairwise-distinct individuals' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlDifferentIndividualsAxiom(terms) { Origin = Origin("different") };
    }

    /// <summary>A transitivity characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(role)) { Origin = Origin("transitive") };
    }

    /// <summary>A functionality characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(role)) { Origin = Origin("functional") };
    }

    /// <summary>A <c>HasKey</c> axiom over a keyed class, object key properties, and data key properties in the example namespace.</summary>
    /// <param name="keyedClass">The keyed class expression.</param>
    /// <param name="objectProperties">The object key properties' local names.</param>
    /// <param name="dataProperties">The data key properties' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlHasKeyAxiom HasKey(OwlClassExpression keyedClass, string[] objectProperties, string[] dataProperties)
    {
        List<OwlObjectPropertyExpression> objects = [];
        foreach(string local in objectProperties)
        {
            objects.Add(Property(local));
        }

        List<NamedNode> data = [];
        foreach(string local in dataProperties)
        {
            data.Add(DataProperty(local));
        }

        return new OwlHasKeyAxiom(keyedClass, objects, data) { Origin = Origin("haskey") };
    }

    /// <summary>A single-property data existential over a named data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([DataProperty(property)], range);
    }

    /// <summary>The <c>xsd:integer</c> datatype as a data range.</summary>
    private static OwlDatatypeReference Integer { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>An inert nominal mention - a fresh disconnected class subsumed by a singleton enumeration - that gives an otherwise nominal-free ground-counting module its nominal jurisdiction, routing the ABox through the root context onto the general path. Semantically inert: it adds no named subsumption and does not change any verdict.</summary>
    /// <returns>The inert nominal axiom.</returns>
    private static OwlSubClassOfAxiom InertNominal()
    {
        return SubClassOf(Class("NominalAnchor"), OneOf("nominalAnchorPoint"));
    }
}
