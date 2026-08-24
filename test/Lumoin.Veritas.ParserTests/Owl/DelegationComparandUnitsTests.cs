using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The KC battery (KC1-KC6): always-on unit
/// tests over the delegation-rate harness's extracted pure comparand units —
/// producer detection, decide/delegate bucketing, the comparand-selection rule,
/// the whole-verdict admission gate, and the battery-only census — asserted with
/// hand-built decisions, plus the column-distinctness pin driving a certified
/// context-decided module through both composition columns. Always-on: the units
/// are the KPI arm's correctness surface, not a measurement, so they execute under
/// the full-suite gate rather than behind the harness's environment gate.
/// </summary>
[TestClass]
internal sealed class DelegationComparandUnitsTests
{
    /// <summary>KC1: an EL-decided verdict names the EL tier, buckets DECIDED, and is cross-checked against standalone SatBacked alone.</summary>
    [TestMethod]
    public void Kc1ElDecidedSelectsStandaloneSatBacked()
    {
        ModuleDecision decision = WholeConsistent(ElDecidedStatistics());

        Assert.AreEqual(DelegationRateHarness.VerdictProducer.ElSaturation, DelegationRateHarness.DetectProducer(decision.Statistics), "An EL-decided decision's producer is the EL saturation tier.");
        Assert.AreEqual(DelegationRateHarness.KpiBucket.Decided, DelegationRateHarness.BucketDecision(decision), "An EL-decided decision buckets DECIDED.");

        DelegationRateHarness.ComparandCandidates candidates = DelegationRateHarness.SelectComparands(DelegationRateHarness.VerdictProducer.ElSaturation);
        Assert.IsTrue(candidates.Sat, "An EL-decided verdict is cross-checked against standalone SatBacked.");
        Assert.IsFalse(candidates.Snapshot, "An EL-decided verdict is not cross-checked against standalone Snapshot.");
    }

    /// <summary>KC2: a context-decided verdict within ALC(H) names the context tier, buckets DECIDED, folds BOTH standalone comparands, and — with a whole comparand present — is not battery-only.</summary>
    [TestMethod]
    public void Kc2ContextDecidedFoldsBothStandaloneComparands()
    {
        ModuleDecision decision = WholeConsistent(ContextDecidedStatistics());

        Assert.AreEqual(DelegationRateHarness.VerdictProducer.ContextSaturation, DelegationRateHarness.DetectProducer(decision.Statistics), "A context-decided decision's producer is the context-saturation tier.");
        Assert.AreEqual(DelegationRateHarness.KpiBucket.Decided, DelegationRateHarness.BucketDecision(decision), "A context-decided decision buckets DECIDED.");

        DelegationRateHarness.ComparandCandidates candidates = DelegationRateHarness.SelectComparands(DelegationRateHarness.VerdictProducer.ContextSaturation);
        Assert.IsTrue(candidates.Sat && candidates.Snapshot, "A context-decided verdict is cross-checked against both standalone comparands.");
        Assert.IsFalse(DelegationRateHarness.IsBatteryOnly(DelegationRateHarness.VerdictProducer.ContextSaturation, anyComparandAdmitted: true), "A context-decided module with a whole automated comparand is not battery-only.");
    }

    /// <summary>KC3: a context-decided verdict beyond ALC(H) whose standalone comparands are all fragment-relative has no valid automated comparand and is battery-only.</summary>
    [TestMethod]
    public void Kc3ContextDecidedBeyondAlcIsBatteryOnly()
    {
        ModuleDecision decision = WholeConsistent(ContextDecidedStatistics());
        ModuleDecision satFragmentRelative = FragmentRelativeConsistent();
        ModuleDecision snapshotFragmentRelative = FragmentRelativeConsistent();

        Assert.AreEqual(DelegationRateHarness.VerdictProducer.ContextSaturation, DelegationRateHarness.DetectProducer(decision.Statistics), "The KPI decision is context-decided.");
        Assert.IsFalse(DelegationRateHarness.IsAdmissibleComparand(satFragmentRelative), "A fragment-relative SatBacked comparand does not clear the whole-verdict admission gate.");
        Assert.IsFalse(DelegationRateHarness.IsAdmissibleComparand(snapshotFragmentRelative), "A fragment-relative Snapshot comparand does not clear the whole-verdict admission gate.");

        bool anyAdmitted = DelegationRateHarness.IsAdmissibleComparand(satFragmentRelative) || DelegationRateHarness.IsAdmissibleComparand(snapshotFragmentRelative);
        Assert.IsTrue(DelegationRateHarness.IsBatteryOnly(DelegationRateHarness.VerdictProducer.ContextSaturation, anyAdmitted), "A context-decided module whose comparands are all fragment-relative is battery-only.");
    }

    /// <summary>KC4: a verdict neither fast-path tier produced names the SAT oracle, buckets DELEGATED, and is cross-checked against standalone Snapshot — never the SatBacked oracle it came from.</summary>
    [TestMethod]
    public void Kc4SatDecidedSelectsStandaloneSnapshot()
    {
        ModuleDecision decision = WholeConsistent(ReasoningDecisionStatistics.Empty);

        Assert.AreEqual(DelegationRateHarness.VerdictProducer.SatOracle, DelegationRateHarness.DetectProducer(decision.Statistics), "A decision neither fast-path tier decided has the SAT oracle as producer.");
        Assert.AreEqual(DelegationRateHarness.KpiBucket.Delegated, DelegationRateHarness.BucketDecision(decision), "A SAT-decided decision buckets DELEGATED.");

        DelegationRateHarness.ComparandCandidates candidates = DelegationRateHarness.SelectComparands(DelegationRateHarness.VerdictProducer.SatOracle);
        Assert.IsFalse(candidates.Sat, "A SAT-decided verdict is not cross-checked against the SatBacked oracle it came from.");
        Assert.IsTrue(candidates.Snapshot, "A SAT-decided verdict is cross-checked against standalone Snapshot.");
    }

    /// <summary>KC5: the whole-verdict admission gate drops a fragment-relative comparand at the fold, so a fragment-relative "consistent" scores no disagreement against a whole "inconsistent"; the gate is load-bearing, since the two conflicting consistency bits would otherwise score.</summary>
    [TestMethod]
    public void Kc5FragmentRelativeComparandNeverScoresAgainstAWholeVerdict()
    {
        ModuleDecision wholeInconsistent = WholeInconsistent(ReasoningDecisionStatistics.Empty);
        ModuleDecision fragmentRelativeConsistent = FragmentRelativeConsistent();
        ModuleDecision wholeConsistent = WholeConsistent(ReasoningDecisionStatistics.Empty);

        Assert.IsTrue(DelegationRateHarness.Disagrees(wholeInconsistent, fragmentRelativeConsistent), "A fragment-relative 'consistent' and a whole 'inconsistent' carry conflicting consistency bits — the gate is what suppresses the score.");
        Assert.IsFalse(DelegationRateHarness.IsAdmissibleComparand(fragmentRelativeConsistent), "A fragment-relative comparand does not clear the whole-verdict admission gate.");
        Assert.IsFalse(DelegationRateHarness.ScoresDisagreement(wholeInconsistent, fragmentRelativeConsistent), "The admission gate drops the fragment-relative comparand, so it scores no disagreement against the whole verdict.");
        Assert.IsTrue(DelegationRateHarness.ScoresDisagreement(wholeInconsistent, wholeConsistent), "Two whole verdicts that conflict on consistency score a disagreement.");
    }

    /// <summary>KC6: the column-distinctness pin — the certified context-decided module (R6 shape) is context-decided through the KPI composition and never context-decided through the retained ElSat composition, so the two columns are distinct on the context-decided signal.</summary>
    [TestMethod]
    public async Task Kc6ColumnDistinctnessContextDecidedThroughBothCompositions()
    {
        ReasoningModule module = Kc6Module();

        ModuleDecision kpi = await DelegationRateHarness.ElCtxSatComposition(module, CancellationToken.None).ConfigureAwait(false);
        ModuleDecision elSat = await DelegationRateHarness.ElSatComposition(module, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(kpi.Statistics.ContextTotals.ContextDecided, "The KPI composition (ElCtxSat) admits the beyond-EL module through the context tier and decides it.");
        Assert.IsFalse(elSat.Statistics.ContextTotals.ContextDecided, "The retained ElSat composition has no context tier, so its decision is never context-decided.");
        Assert.AreNotEqual(kpi.Statistics.ContextTotals.ContextDecided, elSat.Statistics.ContextTotals.ContextDecided, "The two composition columns must be distinct on the context-decided signal, or the ElSat column is wired to the new chain.");
    }

    /// <summary>Statistics carrying only the EL-decided fast-path flag — an EL-produced verdict.</summary>
    /// <returns>The EL-decided statistics.</returns>
    private static ReasoningDecisionStatistics ElDecidedStatistics()
    {
        return ReasoningDecisionStatistics.Empty with { ElTotals = ElSaturationStatistics.Empty with { ElDecided = true } };
    }

    /// <summary>Statistics carrying only the context-decided fast-path flag — a context-produced verdict.</summary>
    /// <returns>The context-decided statistics.</returns>
    private static ReasoningDecisionStatistics ContextDecidedStatistics()
    {
        return ReasoningDecisionStatistics.Empty with { ContextTotals = ContextSaturationStatistics.Empty with { ContextDecided = true } };
    }

    /// <summary>A whole-consistent decision carrying the given statistics.</summary>
    /// <param name="statistics">The decision's statistics.</param>
    /// <returns>The decision.</returns>
    private static ModuleDecision WholeConsistent(ReasoningDecisionStatistics statistics)
    {
        return ModuleDecision.Decided(new ModuleVerdict(true, []), statistics);
    }

    /// <summary>A whole-inconsistent decision carrying the given statistics — an inconsistency covers the module whole regardless of any remainder.</summary>
    /// <param name="statistics">The decision's statistics.</param>
    /// <returns>The decision.</returns>
    private static ModuleDecision WholeInconsistent(ReasoningDecisionStatistics statistics)
    {
        return ModuleDecision.Decided(new ModuleVerdict(false, []), statistics);
    }

    /// <summary>A fragment-relative consistent decision: a consistency claim scoped to a sub-fragment by a named unsupported construct, which the whole-verdict admission gate rejects.</summary>
    /// <returns>The decision.</returns>
    private static ModuleDecision FragmentRelativeConsistent()
    {
        return ModuleDecision.Decided(new ModuleVerdict(true, []) { UnsupportedConstructs = ["inverse-role"] }, ReasoningDecisionStatistics.Empty);
    }

    /// <summary>The KC6 module (R6 shape, fresh names): <c>Kc6Root ⊑ ∃kc6rel.Kc6Mid</c> and <c>Kc6Mid ⊑ ∀kc6rel⁻.Kc6Back</c> — EL-declined, context-admitted, certified consistent.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule Kc6Module()
    {
        NamedNode role = new(Utf8Strings.From("http://example.org/kc6rel"));
        OwlClassReference root = new(new NamedNode(Utf8Strings.From("http://example.org/Kc6Root")));
        OwlClassReference mid = new(new NamedNode(Utf8Strings.From("http://example.org/Kc6Mid")));
        OwlClassReference back = new(new NamedNode(Utf8Strings.From("http://example.org/Kc6Back")));
        OwlSubClassOfAxiom forward = new(root, new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(role), mid)) { Origin = Kc6Origin("forward") };
        OwlSubClassOfAxiom inverse = new(mid, new OwlObjectAllValuesFrom(new OwlInverseObjectProperty(role), back)) { Origin = Kc6Origin("inverse") };

        return new ReasoningModule([forward, inverse], Violations: []);
    }

    /// <summary>A distinct origin quad for a KC6 axiom, so each axiom carries the origin the reporting path names.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Kc6Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From("http://example.org/" + marker)), new NamedNode(Utf8Strings.From("http://example.org/p")), new NamedNode(Utf8Strings.From("http://example.org/o")), Graph: null);
    }
}
