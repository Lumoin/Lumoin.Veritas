using System;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The TR battery (TR1-TR7): always-on tests over the
/// public trace surfaces — <see cref="ReasoningDecisionTraceEvent"/> and
/// <see cref="ReasoningInstrumentation"/>, never the ungated soak output. They
/// pin the widened trace event's EL and context-saturation columns against
/// transposition, the single context-saturation phase bracket the reasoner opens
/// per admitted decision, the phase count and its disabled-zero contract, and the
/// budget-exhausted early-return bracket. The canceled-exit rows complete the
/// exit set on both tiers: a decision aborted by its token still closes the
/// context-saturation and EL-saturation brackets it opened. Always-on: the trace
/// widening is the telemetry's correctness surface, not a measurement, so the
/// rows execute under the full-suite gate.
/// </summary>
[TestClass]
internal sealed class ContextTraceWideningTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>TR1: a context-decided module's trace event carries the saturation columns — <c>ContextDecided</c> true, a non-zero budget-checked rule count — and leaves the EL and solver/tableau columns zero, so a transposition that sourced the saturation columns from <c>ElTotals</c> reds.</summary>
    [TestMethod]
    public void Tr1ContextDecisionPopulatesSaturationColumns()
    {
        ReasoningModule module = InverseUniversalModule("Tr1");
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The inverse-universal module is context-decided.");

        ReasoningDecisionTraceEvent trace = TraceOf(decision);

        Assert.IsTrue(trace.ContextDecided, "The trace event's ContextDecided reads the context tier's flag, not the EL tier's.");
        Assert.IsGreaterThan(0L, trace.ContextRuleApplications, "A context decision spends budget-checked rule applications, surfaced on the trace event.");
        Assert.AreEqual(decision.Statistics.ContextTotals.RuleApplications, trace.ContextRuleApplications, "The trace event's ContextRuleApplications is the statistics' budget-checked total verbatim.");
        Assert.IsFalse(trace.ElDecided, "A context decision leaves the EL column unset.");
        Assert.AreEqual(0L, trace.ElRuleApplications, "A context decision leaves the EL rule count zero.");
        Assert.AreEqual(0, trace.SolverDecisions, "A context decision carries empty solver totals.");
        Assert.AreEqual(0, trace.TableauRuns, "A context decision carries empty tableau totals.");
    }

    /// <summary>TR2: an EL-decided module's trace event carries the EL columns — <c>ElDecided</c> true — and leaves the context-saturation columns zero, so the transposition reds from the EL side too.</summary>
    [TestMethod]
    public void Tr2ElDecisionPopulatesElColumns()
    {
        ReasoningModule module = ConceptChainModule("Tr2");
        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);

        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The pure concept-inclusion chain is EL-decided.");

        ReasoningDecisionTraceEvent trace = TraceOf(decision);

        Assert.IsTrue(trace.ElDecided, "The trace event's ElDecided reads the EL tier's flag.");
        Assert.IsFalse(trace.ContextDecided, "An EL decision leaves the context column unset — the transposition would read the EL flag here.");
        Assert.AreEqual(0L, trace.ContextRuleApplications, "An EL decision leaves the context rule count zero.");
        Assert.AreEqual(0, trace.ContextsCreated, "An EL decision leaves the context-count columns zero.");
    }

    /// <summary>TR3: the reasoner opens exactly one context-saturation phase bracket per admitted decision when measurement is enabled, and none when it is disabled — so a bracket retargeted to the EL phase reads a zero context count on a standalone context decision.</summary>
    [TestMethod]
    public void Tr3ContextPhaseCountsOncePerDecisionAndZeroWhenDisabled()
    {
        ReasoningModule module = InverseUniversalModule("Tr3");

        ReasoningInstrumentation.Enable();
        ReasoningInstrumentation.Reset();
        _ = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        long enabledCount = ReasoningInstrumentation.Snapshot().ContextSaturationCount;
        ReasoningInstrumentation.Disable();

        ReasoningInstrumentation.Reset();
        _ = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        long disabledCount = ReasoningInstrumentation.Snapshot().ContextSaturationCount;

        Assert.AreEqual(1L, enabledCount, "A standalone context decision opens exactly one context-saturation phase bracket.");
        Assert.AreEqual(0L, disabledCount, "A disabled measurement counts no phase.");
    }

    /// <summary>TR4: the phase count is widened to six and the report exposes the sixth accessor pair, so a phase enum widened without the count reds here.</summary>
    [TestMethod]
    public void Tr4PhaseCountIsWidenedToSix()
    {
        int phaseMembers = Enum.GetValues<ReasoningPhase>().Length;
        Assert.AreEqual(ReasoningInstrumentation.PhaseCount, phaseMembers, "The phase count matches the ReasoningPhase member count — a phase added without widening the count reds here.");

        ReasoningInstrumentation.Reset();
        ReasoningInstrumentationReport report = ReasoningInstrumentation.Snapshot();

        Assert.AreEqual(0.0, report.ContextSaturationMilliseconds, "The report exposes the context-saturation milliseconds accessor.");
        Assert.AreEqual(0L, report.ContextSaturationCount, "The report exposes the context-saturation count accessor.");
    }

    /// <summary>TR5: a standalone context decision that exhausts the inference budget abstains AND still closes its one phase bracket at the early return, so the budget-exhausted path is measured exactly once.</summary>
    [TestMethod]
    public void Tr5BudgetExhaustedEarlyReturnClosesTheBracket()
    {
        ReasoningModule module = InverseUniversalModule("Tr5");

        ReasoningInstrumentation.Enable();
        ReasoningInstrumentation.Reset();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), progressSampler: null, TestContext.CancellationToken);
        long count = ReasoningInstrumentation.Snapshot().ContextSaturationCount;
        ReasoningInstrumentation.Disable();

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The one-inference budget exhausts before the module decides.");
        Assert.AreEqual(1L, count, "The budget-exhausted early return closes the one context-saturation phase bracket.");
    }

    /// <summary>TR6: a context decision aborted by a canceled token still closes its one context-saturation phase bracket — the exit the finally guards, which TR3 and TR5 leave open. The token is observed only inside the saturation, on the first dequeued worklist item, so a pre-canceled token throws from inside the bracket deterministically.</summary>
    [TestMethod]
    public void Tr6ACanceledContextSaturationStillClosesItsPhaseBracket()
    {
        ReasoningModule module = InverseUniversalModule("Tr6");
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        ReasoningInstrumentation.Enable();
        ReasoningInstrumentation.Reset();
        Assert.Throws<OperationCanceledException>(() => ContextSaturationModuleReasoner.DecideModule(module, canceled.Token), "A pre-canceled token still throws out of the context decision.");
        long count = ReasoningInstrumentation.Snapshot().ContextSaturationCount;
        ReasoningInstrumentation.Disable();

        Assert.AreEqual(1L, count, "The canceled saturation still closes its one context-saturation phase bracket, in the finally.");
    }

    /// <summary>TR7: the EL twin of TR6 — an EL classification aborted by a canceled token still closes its one EL-saturation phase bracket. The EL side seeds its worklist inside the bracketed call, so the throw originates after that preamble but still within the bracket.</summary>
    [TestMethod]
    public void Tr7ACanceledElClassificationStillClosesItsElPhaseBracket()
    {
        ReasoningModule module = ConceptChainModule("Tr7");
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        ReasoningInstrumentation.Enable();
        ReasoningInstrumentation.Reset();
        Assert.Throws<OperationCanceledException>(() => ElCoupledModuleReasoner.DecideModule(module, canceled.Token), "A pre-canceled token still throws out of the EL classification.");
        long count = ReasoningInstrumentation.Snapshot().ElSaturationCount;
        ReasoningInstrumentation.Disable();

        Assert.AreEqual(1L, count, "The canceled classification still closes its one EL-saturation phase bracket, in the finally.");
    }

    /// <summary>Builds the decision's public trace event with fixed sequence, timestamp, correlation, and cost, so the flattened columns are the only variable under test.</summary>
    /// <param name="decision">The decision whose statistics the event flattens.</param>
    /// <returns>The trace event.</returns>
    private static ReasoningDecisionTraceEvent TraceOf(ModuleDecision decision)
    {
        return ReasoningDecisionTraceEvent.From(sequenceNumber: 1, timestampTicks: 0, correlationId: default, decision.Outcome, decision.Statistics, elapsedMilliseconds: 0.0);
    }

    /// <summary>The inverse-universal KC3 shape under a fresh prefix (<c>{prefix}Root ⊑ ∃{prefix}rel.{prefix}Mid</c>, <c>{prefix}Mid ⊑ ∀{prefix}rel⁻.{prefix}Back</c>): EL-declined, context-admitted, certified consistent.</summary>
    /// <param name="prefix">The fresh constant prefix.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule InverseUniversalModule(string prefix)
    {
        NamedNode rel = Iri(prefix + "rel");
        OwlSubClassOfAxiom forward = new(ClassRef(prefix + "Root"), new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(rel), ClassRef(prefix + "Mid"))) { Origin = Origin(prefix + "forward") };
        OwlSubClassOfAxiom inverse = new(ClassRef(prefix + "Mid"), new OwlObjectAllValuesFrom(new OwlInverseObjectProperty(rel), ClassRef(prefix + "Back"))) { Origin = Origin(prefix + "inverse") };

        return new ReasoningModule([forward, inverse], Violations: []);
    }

    /// <summary>The pure concept-inclusion chain KC1 shape under a fresh prefix (<c>{prefix}Alpha ⊑ {prefix}Beta</c>, <c>{prefix}Beta ⊑ {prefix}Gamma</c>): EL-decided, certified consistent.</summary>
    /// <param name="prefix">The fresh constant prefix.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ConceptChainModule(string prefix)
    {
        OwlSubClassOfAxiom first = new(ClassRef(prefix + "Alpha"), ClassRef(prefix + "Beta")) { Origin = Origin(prefix + "first") };
        OwlSubClassOfAxiom second = new(ClassRef(prefix + "Beta"), ClassRef(prefix + "Gamma")) { Origin = Origin(prefix + "second") };

        return new ReasoningModule([first, second], Violations: []);
    }

    /// <summary>A named-class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference ClassRef(string local)
    {
        return new OwlClassReference(Iri(local));
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From("http://example.org/" + local));
    }

    /// <summary>A distinct origin quad for the marker, so each axiom anchors to its own triple.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(Iri(marker), Iri("p"), Iri("o"), Graph: null);
    }
}
