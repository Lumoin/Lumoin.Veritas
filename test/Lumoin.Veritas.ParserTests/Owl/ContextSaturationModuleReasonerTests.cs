using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="ContextSaturationModuleReasoner"/>: the consequence-based
/// context-saturation engine decides admitted Horn-ALCHI modules by saturation
/// alone and delegates the rest whole to the fallback oracle. The soundness
/// battery drives every ground-truth SROIQ module of the battery table and
/// checks verdict, decision path,
/// and the exact module-local subsumption set; the
/// ELH-degeneracy differential pins the context arm equal to the EL arm on every
/// module both admit, over the fixture population and a deterministic random
/// sweep; the remaining pins cover budget abstention, the abstention surfacing
/// through a composed engine, consistency-only parity, the cautious-reuse context
/// bound, and the second-gate drift tripwire. Every consistent context-decided
/// row carries its hand-built model and every inconsistent row its unsat
/// derivation, stated in 7-bit ASCII ([= subsumption,
/// exists/forall, r- inverse, bot the empty class).
/// </summary>
[TestClass]
internal sealed class ContextSaturationModuleReasonerTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The decision path a battery row expects: the context engine decided it, or it was delegated whole to the fallback oracle.</summary>
    private enum ContextPath
    {
        /// <summary>The context-saturation engine decided the module.</summary>
        ContextDecided,

        /// <summary>The module fell outside the admitted slice and was delegated to the fallback.</summary>
        Delegated,
    }

    /// <summary>
    /// The context-saturation soundness battery: every ground-truth module of the
    /// battery table is decided by
    /// <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>,
    /// and its verdict, decision path, and (for a context-decided row with a stated
    /// set) exact module-local subsumption set are checked against the
    /// independently derived ground truth. The SAT-backed fragment-relative column is
    /// reported, never asserted. The loop reports every offender and fails once with
    /// the whole table.
    /// </summary>
    [TestMethod]
    public void ContextSaturationSoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ContextPath ExpectedPath, string[]? ExpectedSubsumptions)[] rows = BatteryRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | true | final | expectedPath | actualPath | tableau | subs | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ContextPath expectedPath, string[]? expectedSubsumptions) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ContextPath actualPath = decision.Statistics.ContextTotals.ContextDecided ? ContextPath.ContextDecided : ContextPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            //The fragment-relative comparand column is informational, never asserted. The SAT-backed
            //arm reports it: the snapshot tableau's internalized-GCI branching is exponential in the
            //axiom count and wedges on the larger rows, while the SAT arm decides the same ALC(H)
            //fragment in milliseconds.
            bool tableauConsistent = SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool subsOk = true;
            string subsNote = "n/a";
            if(expectedSubsumptions is not null)
            {
                List<string> expected = [.. expectedSubsumptions];
                expected.Sort(StringComparer.Ordinal);
                List<string> actual = SubsumptionKeys(decision.Verdict);
                subsOk = KeysEqual(expected, actual);
                subsNote = subsOk ? "ok" : DiffKeys(expected, actual);
            }

            bool ok = verdictOk && pathOk && subsOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + subsNote + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath + " subs=" + subsNote);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The V-node correctness face: the FULL soundness battery
    /// decided under <see cref="EqualityLowering.SuccessorSharing"/> must reach the
    /// SAME verdict, decision path, and exact module-local subsumption set as the
    /// independently derived ground truth the default general clause decides — a
    /// mismatch on any row is a ship blocker for the sharing mode, which would then
    /// land selectable-but-flagged. Successor sharing merges a same-owner functional
    /// successor by construction instead of through the DL4 counting clause and the
    /// Eq rule, so it changes the derivation SHAPE of the merge rows (E1/E3/E5)
    /// but never the answer. The loop reports every offender and fails once
    /// with the whole table.
    /// </summary>
    [TestMethod]
    public void SuccessorSharingBatteryMatchesGeneralClause()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ContextPath ExpectedPath, string[]? ExpectedSubsumptions)[] rows = BatteryRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | true | final | expectedPath | actualPath | subs | verdict (SuccessorSharing)");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ContextPath expectedPath, string[]? expectedSubsumptions) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EqualityLowering.SuccessorSharing, TestContext.CancellationToken);
            ContextPath actualPath = decision.Statistics.ContextTotals.ContextDecided ? ContextPath.ContextDecided : ContextPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool subsOk = true;
            string subsNote = "n/a";
            if(expectedSubsumptions is not null)
            {
                List<string> expected = [.. expectedSubsumptions];
                expected.Sort(StringComparer.Ordinal);
                List<string> actual = SubsumptionKeys(decision.Verdict);
                subsOk = KeysEqual(expected, actual);
                subsNote = subsOk ? "ok" : DiffKeys(expected, actual);
            }

            bool ok = verdictOk && pathOk && subsOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + subsNote + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath + " subs=" + subsNote);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The directioned-key witness pin: on
    /// <c>{Func(r), A [= exists r.B1, A [= exists r-.B2, B1 and B2 [= bot}</c> the
    /// forward successor (key <c>r</c>) and the inverse successor (key <c>r-</c>)
    /// must NOT share a function symbol. <c>Func(r)</c> makes only the FORWARD role
    /// functional, so the general clause merges no forward-with-inverse successor and
    /// A is satisfiable. A forward-BASE key (the rejected alternative that folds
    /// <c>r</c> and <c>r-</c> to one base) would hand both successors one shared
    /// symbol, force <c>B1(f) and B2(f) [= bot</c>, and derive the spurious
    /// <c>A [= bot</c>. The directioned key keeps the two successors distinct, so
    /// the module stays CONSISTENT with an EMPTY subsumption set (no <c>A [= B1</c> /
    /// <c>A [= B2</c> unsatisfiability marker) under BOTH lowerings.
    /// </summary>
    [TestMethod]
    public void SuccessorSharingDirectionedKeyKeepsInverseSuccessorDistinct()
    {
        ReasoningModule module = Module(
            Functional("r"),
            SubClassOf(Class("A"), Some("r", Class("B1"))),
            SubClassOf(Class("A"), SomeInverse("r", Class("B2"))),
            SubClassOf(Intersection(Class("B1"), Class("B2")), NothingReference));

        ModuleDecision general = ContextSaturationModuleReasoner.DecideModule(module, EqualityLowering.GeneralClause, TestContext.CancellationToken);
        ModuleDecision sharing = ContextSaturationModuleReasoner.DecideModule(module, EqualityLowering.SuccessorSharing, TestContext.CancellationToken);

        Assert.IsTrue(general.Statistics.ContextTotals.ContextDecided, "The C3 counterexample must be context-decided under the general clause.");
        Assert.IsTrue(sharing.Statistics.ContextTotals.ContextDecided, "The C3 counterexample must be context-decided under successor sharing.");
        Assert.IsTrue(general.Verdict!.IsConsistent, "The C3 counterexample is CONSISTENT under the general clause (Func(r) merges no forward-with-inverse successor).");
        Assert.IsTrue(sharing.Verdict!.IsConsistent, "The C3 counterexample must stay CONSISTENT under successor sharing — the directioned key keeps the inverse successor distinct.");

        List<string> generalKeys = SubsumptionKeys(general.Verdict);
        List<string> sharingKeys = SubsumptionKeys(sharing.Verdict);
        Assert.IsTrue(KeysEqual(generalKeys, sharingKeys), "The two lowerings must agree on the subsumption set: " + DiffKeys(generalKeys, sharingKeys));
        Assert.IsEmpty(sharingKeys, "Under a directioned key A is satisfiable, so the subsumption set is empty; the spurious A [= B1 / A [= B2 a forward-base key would derive must NOT appear. Got: " + string.Join(", ", sharingKeys));
    }

    /// <summary>
    /// The reserved-role hardening witness battery. Two
    /// populations split at the reserved-vocabulary constant fold: the six
    /// pointwise-constant rows (RR9, RR10, RR15, RR16, RR17, RR18) each hold a
    /// reserved <c>owl:topObjectProperty</c> / <c>owl:bottomObjectProperty</c>
    /// restriction whose whole meaning is <c>owl:Thing</c> or <c>owl:Nothing</c>,
    /// so the front-door fold turns it into that constant and the module decides
    /// INCONSISTENT WHOLE (outcome <see cref="ReasoningDecisionOutcome.Decided"/>,
    /// consistency false) — measured, and the semantic ground truth for every one
    /// (each carries a top-forced or reserved-empty axiom that empties the
    /// non-empty domain). The eleven global-shape rows (RR1-RR8, RR13, RR14, RR19)
    /// mention their reserved role in a position the fold does not touch — a role
    /// hierarchy, chain, characteristic, domain/range property slot, or an
    /// operand the pointwise fold does not cover — so the module-level scan still
    /// rejects them, the context arm DELEGATES (ContextDecided false), and the
    /// honest stack answers NON-decisively (outcome
    /// <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/> over a
    /// non-empty <see cref="ModuleVerdict.UnsupportedConstructs"/> remainder). The
    /// delegated rows' consistency boolean is NOT asserted: the honest stack is
    /// fragment-relative on these, and their semantic ground truth (INCONSISTENT
    /// except RR13 and RR14, CONSISTENT — RR14 with A unsatisfiable) is recorded
    /// per row. Each loop reports every offender and fails once with the whole
    /// table.
    /// </summary>
    [TestMethod]
    public void ReservedRoleHardeningBattery()
    {
        (string Name, ReasoningModule Module)[] decidedRows =
        [
            //RR9 {top [= exists bottom.top} INCONSISTENT (exists bottom.top folds to owl:Nothing (row 1), so
            //top [= Nothing empties the non-empty domain; the TBox twin of the W3C New-Feature-BottomObjectProperty
            //shape).
            ("RR9", Module(SubClassOf(ThingReference, BottomSome(ThingReference)))),

            //RR10 {exists top.Self [= B, B [= bot, D [= E} INCONSISTENT (exists top.Self folds to owl:Thing
            //(row 12), so Thing [= B [= Nothing empties the non-empty domain).
            ("RR10", Module(SubClassOf(TopHasSelf(), Class("B")), SubClassOf(Class("B"), NothingReference), SubClassOf(Class("D"), Class("E")))),

            //RR15 {DisjointClasses(exists top.Self, A), top [= A} INCONSISTENT (exists top.Self folds to owl:Thing
            //(row 12), so Disjoint(Thing, A) forces A empty against top [= A -- the DisjointClasses-operand
            //surface).
            ("RR15", Module(Disjoint(TopHasSelf(), Class("A")), SubClassOf(ThingReference, Class("A")))),

            //RR16 {Range(r, exists bottom.top), top [= exists r.top} INCONSISTENT (the range filler folds to
            //owl:Nothing (row 1), so Range(r, Nothing) forces r empty against forced r-successors -- the
            //range-FILLER surface).
            ("RR16", Module(Range("r", BottomSome(ThingReference)), SubClassOf(ThingReference, Some("r", ThingReference)))),

            //RR17 {Domain(r, exists bottom.top), top [= exists r.top} INCONSISTENT (the domain filler folds to
            //owl:Nothing (row 1), so Domain(r, Nothing) forces r empty against forced r-successors -- the
            //domain-FILLER surface).
            ("RR17", Module(Domain("r", BottomSome(ThingReference)), SubClassOf(ThingReference, Some("r", ThingReference)))),

            //RR18 {EquivalentClasses(A, exists bottom.top), top [= A} INCONSISTENT (the equivalent filler folds
            //to owl:Nothing (row 1), so A == Nothing yet top [= A over a non-empty domain -- the
            //EquivalentClasses-operand surface).
            ("RR18", Module(Equivalent(Class("A"), BottomSome(ThingReference)), SubClassOf(ThingReference, Class("A")))),
        ];

        (string Name, ReasoningModule Module)[] delegatedRows =
        [
            //RR1 {top [= forall top.B, B [= bot} INCONSISTENT (top universal => every element a top-successor
            //=> Delta subset of B subset of empty). forall top.B with a non-Thing filler is a global inclusion the
            //pointwise fold keeps.
            ("RR1", Module(SubClassOf(ThingReference, TopAll(Class("B"))), SubClassOf(Class("B"), NothingReference))),

            //RR2 {top [= exists s.top, s [= bottom} INCONSISTENT (s forced empty yet every element needs an
            //s-successor). The reserved role sits in a sub-property axiom's role slot, not a class expression.
            ("RR2", Module(SubClassOf(ThingReference, Some("s", ThingReference)), new OwlSubObjectPropertyOfAxiom(Property("s"), BottomObjectPropertyRef()) { Origin = Origin("rr2sub") })),

            //RR3 {Irr(top), A [= exists r.B} INCONSISTENT ((x,x) in top for all x contradicts irreflexivity).
            //The reserved role sits in a characteristic axiom's role slot.
            ("RR3", Module(new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, TopObjectPropertyRef()) { Origin = Origin("rr3irr") }, SubClassOf(Class("A"), Some("r", Class("B"))))),

            //RR4 {Ref(bottom), A [= exists r.B} INCONSISTENT (reflexivity needs (x,x) in the empty bottom).
            //The reserved role sits in a characteristic axiom's role slot.
            ("RR4", Module(new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, BottomObjectPropertyRef()) { Origin = Origin("rr4ref") }, SubClassOf(Class("A"), Some("r", Class("B"))))),

            //RR5 {Domain(top,A), A [= bot, D [= E} INCONSISTENT (diagonal top-pairs put every element in
            //A [= bot). The reserved role sits in the domain axiom's PROPERTY slot, not its class filler.
            ("RR5", Module(new OwlObjectPropertyDomainAxiom(TopObjectPropertyRef(), Class("A")) { Origin = Origin("rr5domain") }, SubClassOf(Class("A"), NothingReference), SubClassOf(Class("D"), Class("E")))),

            //RR6 {EquivProps(r,top), Dis(s,r), top [= exists s.top} INCONSISTENT (r == universal => s forced
            //empty). Quotient laundering: the raw-operand guard passed it; the mention-level scan rejects the
            //EquivProps(r,top) mention.
            ("RR6", Module(new OwlEquivalentObjectPropertiesAxiom(Property("r"), TopObjectPropertyRef()) { Origin = Origin("rr6equiv") }, DisjointProperties("s", "r"), SubClassOf(ThingReference, Some("s", ThingReference)))),

            //RR7 {top [= forall Inv(top).B, B [= bot} INCONSISTENT (Inv(top) unwraps to the universal top again).
            //forall top.B with a non-Thing filler is a global inclusion the pointwise fold keeps.
            ("RR7", Module(SubClassOf(ThingReference, new OwlObjectAllValuesFrom(new OwlInverseObjectProperty(new NamedNode(OwlVocabulary.TopObjectProperty)), Class("B"))), SubClassOf(Class("B"), NothingReference))),

            //RR8 {Chain(r,s) [= bottom, top [= exists r.exists s.top} INCONSISTENT (the forced r.s path lands in
            //the empty super role). The reserved role sits in a property-chain axiom's role slot.
            ("RR8", Module(new OwlPropertyChainAxiom([Property("r"), Property("s")], BottomObjectPropertyRef()) { Origin = Origin("rr8chain") }, SubClassOf(ThingReference, Some("r", Some("s", ThingReference))))),

            //RR13 {r [= top, A [= exists r.B} CONSISTENT (tautologous top mention); rides this method PATH-ONLY
            //-- a deliberate over-delegation on a scanned tautology. The non-decisive assert
            //holds because the fallback records the reserved r [= top mention.
            ("RR13", Module(new OwlSubObjectPropertyOfAxiom(Property("r"), TopObjectPropertyRef()) { Origin = Origin("rr13sub") }, SubClassOf(Class("A"), Some("r", Class("B"))))),

            //RR14 {A [= exists s.B, s [= bottom} CONSISTENT with A unsatisfiable (the fallback is the Alc
            //arm, so the consistent-module-with-unsat-named-class shape delegates non-decisively).
            ("RR14", Module(SubClassOf(Class("A"), Some("s", Class("B"))), new OwlSubObjectPropertyOfAxiom(Property("s"), BottomObjectPropertyRef()) { Origin = Origin("rr14sub") })),

            //RR19 {top [= exists r.(forall top.B), B [= bot} INCONSISTENT (the nested forall top.B is a global
            //top inclusion the pointwise fold keeps, so its reserved role sits inside a non-reserved existential
            //filler the scan still rejects -- the nested-depth surface).
            ("RR19", Module(SubClassOf(ThingReference, Some("r", TopAll(Class("B")))), SubClassOf(Class("B"), NothingReference))),
        ];

        StringBuilder report = new();
        report.AppendLine("\nrow | path | outcome | consistent | unsupported");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module) in decidedRows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            int unsupported = decision.Verdict is null ? -1 : decision.Verdict.UnsupportedConstructs.Count;
            string consistent = decision.Verdict is null ? "none" : decision.Verdict.IsConsistent.ToString();
            bool ok = decision.Outcome == ReasoningDecisionOutcome.Decided && decision.Verdict is not null && !decision.Verdict.IsConsistent;
            report.AppendLine(name + " | Decided | " + decision.Outcome + " | " + consistent + " | " + unsupported);
            if(!ok)
            {
                mismatches.Add(name + ": outcome=" + decision.Outcome + " consistent=" + consistent + " unsupported=" + unsupported + " (expected Decided/inconsistent)");
            }
        }

        foreach((string name, ReasoningModule module) in delegatedRows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool delegated = !decision.Statistics.ContextTotals.ContextDecided;
            int unsupported = decision.Verdict is null ? -1 : decision.Verdict.UnsupportedConstructs.Count;
            bool nonDecisive = decision.Outcome == ReasoningDecisionOutcome.DecidedFragmentRelative && unsupported > 0;
            bool ok = delegated && nonDecisive;
            report.AppendLine(name + " | " + (delegated ? "Delegated" : "ContextDecided") + " | " + decision.Outcome + " | none | " + unsupported);
            if(!ok)
            {
                mismatches.Add(name + ": delegated=" + delegated + " outcome=" + decision.Outcome + " unsupported=" + unsupported + " (expected Delegated/fragment-relative)");
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The loops-times-counting guard battery (GL rows R19-R24): every row is
    /// survey-ADMITTED (all axioms in the admitted slice) yet clausifier-DELEGATED
    /// because a counting-constrained role can carry a loop (GL1-GL4, the loop guard's
    /// named remainder <c>LoopCapableRoleInNumberRestriction</c>) or is non-simple
    /// under a number restriction (GL5, GL5b, the simplicity guard's
    /// <c>NonSimpleRoleInNumberRestriction</c> — the two-guard completeness split's
    /// max and min faces). The owner-successor diagonal a functional merge would force
    /// (<c>f(x) approx x</c>) is inexpressible in the context grammar, so the calculus
    /// delegates the whole module rather than answer a subsumption MISSING; the honest
    /// stack then answers NON-decisively (outcome
    /// <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/> over a non-empty
    /// <see cref="ModuleVerdict.UnsupportedConstructs"/> remainder). This method takes
    /// the reserved-role hardening assertion mode verbatim: <c>delegated</c> is
    /// asserted true and the outcome non-decisive; the consistency boolean is NOT
    /// asserted. The semantic ground truth is recorded per row,
    /// not an assertion — and for GL1-GL3 the entailment <c>A [= B</c> the stack does
    /// NOT decide is answered BY DELEGATION,
    /// never claimed decided. The loop reports every offender and fails once with the
    /// whole table.
    /// </summary>
    [TestMethod]
    public void LoopCountingGuardBattery()
    {
        (string Name, ReasoningModule Module)[] rows =
        [
            //GL1 {A [= exists r.Self, A [= exists r.B, Func(r)} DELEGATED. A [= exists r.Self registers base(r) in
            //the loop set L; Func(r)'s DL4 records Forward(Rep(r)) as a counting target; the post-CloseLoopSet
            //check finds it in L => remainder LoopCapableRoleInNumberRestriction(r). SEMANTIC: consistent, A [= B
            //ENTAILED (the r-loop at the A-element and the minted r-witness are both r-successors, Func merges
            //them, so the owner is in B) -- answered BY DELEGATION, the stack does NOT decide it. MU6 catcher.
            ("GL1", Module(SubClassOf(Class("A"), HasSelf("r")), SubClassOf(Class("A"), Some("r", Class("B"))), Functional("r"))),

            //GL2 {Ref(r), Func(r), A [= exists r.B} DELEGATED. Ref(r) seeds Forward(Rep(r)) into L (reflexive
            //seed); Func(r) records the counting target; the check names r. SEMANTIC: consistent, A [= B ENTAILED
            //(Ref forces an r-loop at every element, Func merges it with the r-witness in B) -- answered BY
            //DELEGATION. MU6 catcher.
            ("GL2", Module(Reflexive("r"), Functional("r"), SubClassOf(Class("A"), Some("r", Class("B"))))),

            //GL3 {s [= r, A [= exists s.Self, Func(r), A [= exists r.B} DELEGATED (upward-closure face). The
            //s-loop registers Forward(Rep(s)) in L; s [= r puts (s,r) in RepArcs; CloseLoopSet promotes
            //Forward(r) into L; Func(r) records r as a counting target; the check finds r (reached ONLY via the
            //upward closure, never the seed). SEMANTIC: consistent, A [= B ENTAILED (the s-loop promotes to an
            //r-loop, merged by Func) -- answered BY DELEGATION. MU7 catcher (a seed-only guard read misses r).
            ("GL3", Module(SubProperty("s", "r"), SubClassOf(Class("A"), HasSelf("s")), Functional("r"), SubClassOf(Class("A"), Some("r", Class("B"))))),

            //GL4 {Irr(r), Func(r), A [= exists r.B} DELEGATED (over-approximation face). Irr(r) seeds
            //Forward(Rep(r)) into L (the guard seeds from IrreflexiveRoles too, deliberately over-approximate);
            //Func(r) records the counting target; the check names r. SEMANTIC: consistent, NO entailment -- Irr(r)
            //FORBIDS an r-loop, so there is no owner-successor merge and A [= B is NOT entailed. The stack COULD
            //decide this module; the guard delegates it conservatively
            //-- a deliberate over-delegation. MU6 catcher.
            ("GL4", Module(Irreflexive("r"), Functional("r"), SubClassOf(Class("A"), Some("r", Class("B"))))),

            //GL5 {Trans(r), A [= <=1 r.B} DELEGATED (simplicity guard, max side). Trans(r) makes r non-simple, so
            //StepMaxSuper's simplicity guard names NonSimpleRoleInNumberRestriction(r) BEFORE any loop-capability
            //question -- the two-guard completeness split. SEMANTIC: consistent, no entailment. Clausifier pin:
            //MaxOverTransitive (ContextClausifierTests.cs).
            ("GL5", Module(Transitive("r"), SubClassOf(Class("A"), Max("r", 1, Class("B"))))),

            //GL5b {Trans(r), A [= >=2 r.B} DELEGATED (simplicity guard, min side, spec correction C-1). Trans(r)
            //makes r non-simple; the count >= 2 StepMinSuper simplicity guard (which min-1/existential lacks)
            //names NonSimpleRoleInNumberRestriction(r). SEMANTIC: consistent, no entailment. MU11 catcher;
            //clausifier pin: MinTwoOverTransitive (ContextClausifierTests.cs).
            ("GL5b", Module(Transitive("r"), SubClassOf(Class("A"), Min("r", 2, Class("B"))))),
        ];

        StringBuilder report = new();
        report.AppendLine("\nrow | path | outcome | unsupported");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool delegated = !decision.Statistics.ContextTotals.ContextDecided;
            int unsupported = decision.Verdict is null ? -1 : decision.Verdict.UnsupportedConstructs.Count;
            bool nonDecisive = decision.Outcome == ReasoningDecisionOutcome.DecidedFragmentRelative && unsupported > 0;
            bool ok = delegated && nonDecisive;
            report.AppendLine(name + " | " + (delegated ? "Delegated" : "ContextDecided") + " | " + decision.Outcome + " | " + unsupported);
            if(!ok)
            {
                mismatches.Add(name + ": delegated=" + delegated + " outcome=" + decision.Outcome + " unsupported=" + unsupported);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The ELH-degeneracy differential: over the fixture population — every
    /// context-decided battery row plus the delegation-rate harness's synthetic
    /// ladder — plus a deterministic 300-round xorshift sweep, any module both the
    /// EL survey and the context survey admit AND both arms actually decide agree on
    /// consistency and the module-local subsumption set. A module either arm delegates
    /// is not compared -- the EL reasoner may delegate a module its coarse survey
    /// admits (CE5's inverse-in-subclass chain, S7's Irr-condemned Self producer).
    /// The delegated battery rows
    /// are excluded from the population: they are survey-admitted-but-correctly-
    /// delegated (a non-simple Self/Irr role, an irregular RBox, a budget blow-up),
    /// so the context arm honestly delegates them and they are not both-decided.
    /// The seven ABox-carrying EL soundness battery families
    /// (<see cref="ElCoupledModuleReasonerTests.AboxSoundnessBatteryModules"/>) join the
    /// population directly; the compared count is the post-double-filter
    /// population (both surveys admit AND both arms decide), and each source's
    /// admitted-versus-filtered split is recorded in the report. Before comparison BOTH
    /// arms' key sets are projected
    /// onto the shared ALC Translate signature: the EL arm
    /// sweeps the un-widened ALC signature while the context arm sweeps the widened
    /// W, so a raw comparison of a both-admit Self module (S1: elKeys=[] vs
    /// contextKeys=[(A,B)]) would read spuriously red; the projection drops any pair
    /// mentioning a class outside the ALC signature, leaving the Self pair-oracle to
    /// the S1/S2/S5/S6 battery rows. Non-vacuity floors guard against slice drift
    /// hollowing the differential: at least twenty modules compared, at least one
    /// with a non-empty projected subsumption set, and at least one carrying a Self
    /// construct (ObjectHasSelf, Reflexive, or Irreflexive), and the cross-check
    /// rows A1 and A3 pinned both-decided (the EL arm's forced-empty and
    /// told-reflexivity tiers against the context arm's disjoint-role clash clause).
    /// Reports every offender.
    /// </summary>
    [TestMethod]
    public void ElhDegeneracyDifferential()
    {
        List<(string Source, string Name, ReasoningModule Module)> population = [];
        foreach((string name, ReasoningModule module, bool _, ContextPath expectedPath, string[]? _) in BatteryRows())
        {
            //Delegated rows are survey-admitted-but-correctly-delegated (non-simple Self/Irr, irregular RBox,
            //budget blow-up); the context arm honestly delegates them, so they are not both-decided and are
            //not part of the both-arms-decide differential.
            if(expectedPath == ContextPath.Delegated)
            {
                continue;
            }

            population.Add(("battery", name, module));
        }

        foreach((string name, ReasoningModule module) in DelegationRateHarness.SyntheticSuiteModules())
        {
            population.Add(("synthetic", $"synthetic:{name}", module));
        }

        //The seven ABox-carrying EL soundness battery families join the population; each family's
        //admitted-versus-filtered split under the double filter is recorded in the report below.
        foreach((string family, string name, ReasoningModule module) in ElCoupledModuleReasonerTests.AboxSoundnessBatteryModules())
        {
            population.Add((family, family + ":" + name, module));
        }

        ulong state = 0xB5297A4D3F84D5B5UL;
        for(int round = 0; round < 300; round++)
        {
            population.Add(("sweep", $"sweep{round}", GenerateModule(ref state)));
        }

        StringBuilder report = new();
        report.AppendLine("\nmodule | elDecided | contextDecided | consistent | subs");
        List<string> mismatches = [];
        HashSet<string> bothDecided = [];
        Dictionary<string, int> sourceAdmitted = [];
        Dictionary<string, int> sourceFiltered = [];
        int compared = 0;
        int subsumptionBearing = 0;
        int selfCompared = 0;
        foreach((string source, string name, ReasoningModule module) in population)
        {
            //The EL pre-filter mirrors the EL tier's own front-door admission predicate, which folds the
            //reserved-vocabulary constant shapes before surveying, so the population is filtered on the
            //same view the production reasoner admits on.
            if(!ElModuleSurvey.IsElDecidable(ReservedVocabularyFold.Apply(module).Axioms) || !ContextModuleSurvey.Survey(module).Admitted)
            {
                sourceFiltered[source] = sourceFiltered.TryGetValue(source, out int surveyFiltered) ? surveyFiltered + 1 : 1;

                continue;
            }

            ModuleDecision el = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ModuleDecision context = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool elDecided = el.Statistics.ElTotals.ElDecided;
            bool contextDecided = context.Statistics.ContextTotals.ContextDecided;
            //The EL reasoner may delegate a module its coarse EL survey admits (CE5's inverse-in-subclass chain,
            //S7's Irr-condemned Self producer), so the two arms are compared only where BOTH actually decide;
            //the ContextSaturationSoundnessBattery pins each context-decided row's own verdict directly.
            if(!elDecided || !contextDecided)
            {
                sourceFiltered[source] = sourceFiltered.TryGetValue(source, out int armFiltered) ? armFiltered + 1 : 1;
                report.AppendLine(name + " | " + elDecided + " | " + contextDecided + " | (one arm delegates) | -");

                continue;
            }

            sourceAdmitted[source] = sourceAdmitted.TryGetValue(source, out int admittedSoFar) ? admittedSoFar + 1 : 1;
            compared++;
            bothDecided.Add(name);
            if(CarriesSelfConstruct(module))
            {
                selfCompared++;
            }

            bool consistentAgrees = el.Verdict!.IsConsistent == context.Verdict!.IsConsistent;
            //The EL arm sweeps the un-widened ALC signature and the context arm the widened W (section 2.1);
            //project both key sets onto the shared ALC Translate signature before comparing, dropping any pair
            //that mentions a class outside it, so a widened Self pair (e.g. S1's (A,B)) is not a spurious diff.
            HashSet<string> alcSignature = AlcSignatureIris(module);
            List<string> elKeys = ProjectOntoSignature(SubsumptionKeys(el.Verdict!), alcSignature);
            List<string> contextKeys = ProjectOntoSignature(SubsumptionKeys(context.Verdict!), alcSignature);
            bool subsAgrees = KeysEqual(elKeys, contextKeys);
            if(contextKeys.Count > 0)
            {
                subsumptionBearing++;
            }

            bool ok = consistentAgrees && subsAgrees;
            string subsNote = subsAgrees ? "ok" : DiffKeys(elKeys, contextKeys);
            report.AppendLine(name + " | " + elDecided + " | " + contextDecided + " | " + (consistentAgrees ? "agree" : "DISAGREE") + " | " + subsNote);
            if(!ok)
            {
                mismatches.Add(name + ": consistentAgrees=" + consistentAgrees + " subs=" + subsNote);
            }
        }

        report.AppendLine("compared=" + compared + " subsumptionBearing=" + subsumptionBearing + " selfCompared=" + selfCompared);
        SortedSet<string> sources = [.. sourceAdmitted.Keys, .. sourceFiltered.Keys];
        report.AppendLine("per-source admitted vs filtered (post-double-filter):");
        foreach(string source in sources)
        {
            int admittedForSource = sourceAdmitted.TryGetValue(source, out int sourceAdmit) ? sourceAdmit : 0;
            int filteredForSource = sourceFiltered.TryGetValue(source, out int sourceFilter) ? sourceFilter : 0;
            report.AppendLine(source + " | admitted=" + admittedForSource + " | filtered=" + filteredForSource);
        }

        Assert.IsEmpty(mismatches, report.ToString());
        Assert.IsGreaterThan(19, compared, "The differential compares at least twenty modules; a lower count is slice drift. " + report);
        Assert.IsGreaterThan(0, subsumptionBearing, "The differential compares at least one subsumption-bearing module. " + report);
        Assert.IsGreaterThan(0, selfCompared, "The differential compares at least one module carrying a Self construct; a lower count is slice drift in the Self direction. " + report);
        //The global floors survive on other rows if either arm quietly demotes A1 or A3, so the two
        //cross-check rows are pinned by name: a missing row is EL-side (forced-empty or
        //told-reflexivity tier) or context-side (disjoint-role clash) slice drift, not an acceptable drop.
        Assert.IsTrue(bothDecided.Contains("A1") && bothDecided.Contains("A3"), "The cross-check rows A1 and A3 are compared both-decided. " + report);
    }

    /// <summary>
    /// Budget abstention on the O1 family: a saturation bounded to a single
    /// inference exhausts the budget and abstains with no verdict; the unbounded
    /// decision reaches the verdict with the O1 subsumption set. The abstention is
    /// the answer, not a delegation.
    /// </summary>
    [TestMethod]
    public void BudgetAbstention()
    {
        ReasoningModule module = O1Module();

        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideModule(module, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), progressSampler: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "A one-inference budget abstains on O1.");
        Assert.IsNull(abstained.Verdict, "A budget abstention carries no verdict.");

        ModuleDecision decided = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decided.Outcome, "The unbounded decision reaches a verdict.");
        List<string> expected = [Sub("B0", "C0"), Sub("B1", "C1"), Sub("B2", "C2"), Sub("B3", "C3")];
        expected.Sort(StringComparer.Ordinal);
        Assert.AreSequenceEqual(expected, SubsumptionKeys(decided.Verdict!), "O1 entails exactly Bi [= Ci.");
    }

    /// <summary>
    /// PF2 (MU11/MU12): behind the seam a context
    /// budget exhaustion is composed into a DELEGATION, not surfaced as an abstention.
    /// An EL-coupled engine over a budgeted context-saturation engine over a SAT-backed
    /// fallback, driven with the EL-rejected but context-admitted P1 module under a
    /// one-inference ceiling, exhausts the context tier and delegates to the SAT oracle:
    /// the decision is the SAT oracle's decided verdict, carrying the exhausted
    /// saturation's <see cref="ContextSaturationStatistics"/> (ContextDecided
    /// <see langword="false"/>, non-zero counters) beside the SAT oracle's own solver
    /// totals. Kills MU11 (exhaustion returns AbstainedBudget through the seam) and MU12
    /// (the exhausted ContextTotals dropped from the delegated decision).
    /// </summary>
    [TestMethod]
    public async Task ComposedChainDelegatesContextExhaustionToTheSatOracle()
    {
        DescriptionLogicDelegate composed = ReasoningEngines.ElCoupled(
            ReasoningEngines.ContextSaturation(
                new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1),
                ReasoningEngines.SatBacked(ReasoningBudget.Unbounded)));

        ModuleDecision decision = await composed(P1Module(), TestContext.CancellationToken).ConfigureAwait(false);

        //The SAT oracle is inverse-blind, so its verdict on P1's inverse-universal is
        //fragment-relative; the point is that the exhaustion DELEGATED (a verdict, not an
        //abstention) rather than surfacing AbstainedBudget through the seam.
        Assert.AreNotEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The exhausted context tier delegates to the SAT oracle rather than surfacing the abstention through the seam.");
        Assert.IsNotNull(decision.Verdict, "The delegated SAT decision carries a verdict.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "P1 is consistent, as the SAT oracle decides.");
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The exhausted saturation did not decide the module, so its ContextDecided flag stays false.");
        Assert.IsGreaterThan(0L, decision.Statistics.ContextTotals.RuleApplications, "The exhausted saturation's spent totals are reattached to the delegated decision.");
        Assert.IsGreaterThan(0, decision.Statistics.SolveCount, "The SAT oracle's own solver totals are present beside the exhausted context totals.");
    }

    /// <summary>
    /// The seam default fallback is bounded: the fallback-omitted
    /// <see cref="ContextSaturationModuleReasoner.CreateDelegate(DatatypeRegistry, ReasoningBudget, DescriptionLogicDelegate?)"/>
    /// binds the snapshot oracle under the seam's own budget, so a non-admitted
    /// module the default oracle decides abstains under a starved budget and
    /// decides under an unbounded one — so the fallback-omitted composition
    /// inherits a bounded oracle rather than an unbounded one.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task SeamDefaultFallbackIsBoundedByTheSeamBudget()
    {
        ReasoningModule nonAdmitted = NonAdmittedTableauModule();

        DescriptionLogicDelegate starved = ContextSaturationModuleReasoner.CreateDelegate(DatatypeRegistry.Empty, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), fallback: null);
        ModuleDecision starvedDecision = await starved(nonAdmitted, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, starvedDecision.Outcome, "The default snapshot oracle is bounded by the seam's budget, so the non-admitted module abstains under a starved budget.");
        Assert.IsNull(starvedDecision.Verdict, "The bounded default fallback abstention carries no verdict.");

        DescriptionLogicDelegate unbounded = ContextSaturationModuleReasoner.CreateDelegate(DatatypeRegistry.Empty, ReasoningBudget.Unbounded, fallback: null);
        ModuleDecision unboundedDecision = await unbounded(nonAdmitted, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreNotEqual(ReasoningDecisionOutcome.AbstainedBudget, unboundedDecision.Outcome, "An unbounded seam budget lets the default oracle decide the non-admitted module.");
        Assert.IsNotNull(unboundedDecision.Verdict, "The unbounded default fallback decides a whole verdict.");
    }

    /// <summary>
    /// Consistency-only parity: over every context-decided battery row the
    /// consistency-only decision (no query contexts) agrees with the full decision's
    /// consistency verdict; the query contexts the full decision creates do not
    /// perturb the trivial context's consistency read. Reports every offender.
    /// </summary>
    [TestMethod]
    public void DecideConsistencyMatchesDecideModule()
    {
        StringBuilder report = new();
        report.AppendLine("\nrow | consistencyOnly | fullVerdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, ContextPath expectedPath, string[]? _) in BatteryRows())
        {
            if(expectedPath != ContextPath.ContextDecided)
            {
                continue;
            }

            bool consistencyOnly = ContextSaturationModuleReasoner.DecideConsistency(module, TestContext.CancellationToken).IsConsistent;
            bool fullVerdict = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!.IsConsistent;
            bool ok = consistencyOnly == fullVerdict;
            report.AppendLine(name + " | " + consistencyOnly + " | " + fullVerdict + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": consistencyOnly=" + consistencyOnly + " fullVerdict=" + fullVerdict);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The cautious-reuse context bound on CE4 (<c>A [= exists r.A</c>, Symmetric):
    /// the cautious strategy reuses the query context q_A as its own f-successor, so
    /// exactly two contexts are created (the trivial context and q_A) with at least
    /// one reuse, and the module decides consistent with no subsumptions.
    /// </summary>
    [TestMethod]
    public void CautiousReuseBoundsContexts()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Ce4Module(), TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "CE4 is decided by the context engine.");
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "CE4 rides the context path.");
        Assert.AreEqual(2, decision.Statistics.ContextTotals.ContextsCreated, "CE4 creates exactly the trivial context and q_A.");
        Assert.IsGreaterThan(0, decision.Statistics.ContextTotals.ContextsReused, "The self-edge reuses q_A as its own successor.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "CE4 is consistent.");
        Assert.IsEmpty(decision.Verdict!.Subsumptions, "CE4 entails no subsumption.");
    }

    /// <summary>
    /// The second-gate contract over the per-literal head grammar: handed a
    /// synthetic clausification directly, the internal gate ADMITS a
    /// central-concept disjunctive head (the DL1 shape), a pairwise-equality
    /// disjunctive head (the DL4 shape), a mixed central-concept-and-equality
    /// disjunctive head, a single-literal equality head whose terms are
    /// neighbour / function (matched by a fresh-role count that equals the
    /// counting-role count), the single-literal neighbour- and function-term
    /// concept heads (the DL3 and DL2 shapes), the one-central role heads, and a
    /// clean Horn clausification; and DELEGATES a central-term equality head (out
    /// of the neighbour / function grammar), a fresh role with no matching
    /// counting role (the drift tripwire), a non-empty remainder, and an exceeded
    /// automaton budget. No admitted module can reach the delegated shapes; the
    /// probe exercises the guard against survey/clausifier drift regardless.
    /// </summary>
    [TestMethod]
    public void SecondGateAdmitsPerLiteralGrammarHeads()
    {
        DlClause conceptDisjunction = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Central), DlLiteral.Concept(2, DlTerm.Central)], 0);
        DlClause equalityDisjunction = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Equality(DlTerm.Neighbour(1), DlTerm.Neighbour(2)), DlLiteral.Equality(DlTerm.Neighbour(1), DlTerm.Neighbour(3))], 0);
        DlClause mixedDisjunction = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Central), DlLiteral.Equality(DlTerm.Neighbour(1), DlTerm.Neighbour(2))], 0);
        DlClause inGrammarEquality = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Equality(DlTerm.Neighbour(1), DlTerm.Function(0))], 0);
        DlClause neighbourConcept = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central), DlLiteral.Role(0, DlTerm.Central, DlTerm.Neighbour(1))], [DlLiteral.Concept(1, DlTerm.Neighbour(1))], 0);
        DlClause functionConcept = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Function(0))], 0);
        DlClause successorRole = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Role(0, DlTerm.Central, DlTerm.Function(0))], 0);
        DlClause propagationRole = DlClause.Create([DlLiteral.Role(0, DlTerm.Neighbour(1), DlTerm.Central)], [DlLiteral.Role(1, DlTerm.Neighbour(1), DlTerm.Central)], 0);
        DlClause centralEquality = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Equality(DlTerm.Central, DlTerm.Function(0))], 0);
        DlClause[] clean = [DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Central)], 0)];

        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([conceptDisjunction])), "A central-concept disjunctive head (the DL1 shape) passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([equalityDisjunction])), "A pairwise-equality disjunctive head (the DL4 shape) passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([mixedDisjunction])), "A mixed central-concept and equality disjunctive head passes the per-literal gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([inGrammarEquality], freshRoles: 1, countingRoles: 1)), "A single-literal neighbour / function equality head with a matched counting-role count passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([neighbourConcept])), "A single-literal neighbour-term concept head (the DL3 shape) passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([functionConcept])), "A single-literal function-term concept head (the DL2 shape) passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([successorRole])), "A central-to-function successor role head passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([propagationRole])), "A neighbour-to-central propagation role head passes the gate.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult(clean)), "A clean Horn clausification passes.");

        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([centralEquality])), "A central-term equality head is out of the neighbour / function grammar and delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult(clean, freshRoles: 1)), "A fresh role with no matching counting role delegates (the drift tripwire).");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult(clean, remainder: ["Chain(beyond-slice)"])), "A non-empty remainder delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult(clean, automatonBudgetExceeded: true)), "An exceeded automaton budget delegates.");
    }

    /// <summary>
    /// An out-of-grammar concept or role literal in a DISJUNCTIVE head delegates:
    /// a neighbour-term or function-term concept disjunct (only central-concept
    /// disjuncts are clausifier-reachable, via DL1), a role atom sharing a
    /// disjunctive head (no emission path produces one), a role head without
    /// exactly one central argument, and a context-term concept head are the
    /// structural backstops the per-literal grammar enforces.
    /// </summary>
    [TestMethod]
    public void SecondGateDelegatesOutOfGrammarDisjunctiveHead()
    {
        DlClause neighbourConceptDisjunct = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Neighbour(1)), DlLiteral.Concept(2, DlTerm.Central)], 0);
        DlClause functionConceptDisjunct = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Function(0)), DlLiteral.Concept(2, DlTerm.Central)], 0);
        DlClause roleDisjunct = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Role(0, DlTerm.Central, DlTerm.Neighbour(1)), DlLiteral.Concept(1, DlTerm.Central)], 0);
        DlClause neighbourPairRole = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Role(0, DlTerm.Neighbour(1), DlTerm.Neighbour(2))], 0);
        DlClause contextConcept = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Context)], 0);

        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([neighbourConceptDisjunct])), "A neighbour-term concept disjunct is out of the disjunctive-head grammar and delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([functionConceptDisjunct])), "A function-term concept disjunct is out of the disjunctive-head grammar and delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([roleDisjunct])), "A role atom sharing a disjunctive head delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([neighbourPairRole])), "A role head without exactly one central argument delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([contextConcept])), "A context-term concept head delegates.");
    }

    /// <summary>
    /// The F3.3 counting-marker drift belt: a <c>MaxCardinality</c> demand marker
    /// sharing a DISJUNCTIVE head is out of the per-literal head grammar and
    /// delegates — only the non-value-forcing <c>Universal</c> marker the
    /// subclass-position NNF dual emits is admitted in an uncommitted disjunct. No
    /// clausifier path emits a counting marker there (a superclass-position bound
    /// always lands in a single-literal head, and a union disjunct abstracts to a
    /// fresh name before the bound is lowered), so the shape is synthetic; the same
    /// marker ALONE in a single-literal head is in grammar and admits.
    /// </summary>
    [TestMethod]
    public void DataQcrMaxMarkerInDisjunctiveHeadKeepsDelegating()
    {
        OwlDataRange literalTop = new OwlDatatypeReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2000/01/rdf-schema#Literal")));
        Dictionary<int, DataDemandDescriptor> demands = new()
        {
            [1] = new DataDemandDescriptor(Utf8Strings.From("http://example.org/qcr#maxProperty"), DataDemandKind.MaxCardinality, 2, literalTop),
        };
        DlClause disjunctiveMarkerHead = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Central), DlLiteral.Concept(2, DlTerm.Central)], 0);
        DlClause unitMarkerHead = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Concept(1, DlTerm.Central)], 0);

        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([disjunctiveMarkerHead], dataDemands: demands)), "A MaxCardinality marker sharing a disjunctive head is out of the disjunctive-head grammar and delegates.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([unitMarkerHead], dataDemands: demands)), "The same MaxCardinality marker alone in a single-literal head is in grammar and admits.");
    }

    /// <summary>
    /// The MU11 internal-seam probe: a synthetic clausification carrying a Role
    /// literal with both arguments the central variable <c>x</c> — the
    /// <c>S(x,x)</c> self-loop shape no emission path produces (the self-variant
    /// pass rewrites every would-be loop atom to a <c>Self_p</c> concept) —
    /// delegates at the second gate, whether the literal sits in the clause body or
    /// its head. Mirrors <see cref="SecondGateAdmitsPerLiteralGrammarHeads"/> for
    /// the two-central tripwire.
    /// </summary>
    [TestMethod]
    public void SecondGateDelegatesTwoCentralRoleClause()
    {
        DlClause twoCentralBody = DlClause.Create([DlLiteral.Role(0, DlTerm.Central, DlTerm.Central)], [DlLiteral.Concept(0, DlTerm.Central)], 0);
        DlClause twoCentralHead = DlClause.Create([DlLiteral.Concept(0, DlTerm.Central)], [DlLiteral.Role(0, DlTerm.Central, DlTerm.Central)], 0);

        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([twoCentralBody])), "A two-central role body literal delegates.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DelegatesOnSecondGate(GateResult([twoCentralHead])), "A two-central role head literal delegates.");
    }

    /// <summary>
    /// The §3.2 insert-normalization Ineq arm, driven at saturation level: a
    /// synthetic empty-body ontology clause with a self-inequality head
    /// <c>f0(x) not-approx f0(x)</c> fires into the trivial context at seeding, and
    /// the normalization collapses it to the empty clause (the Ineq rule), so the
    /// structure reads inconsistent and one Ineq application is counted. Mirrors the
    /// SecondGate synthetic probes: a directly-built clausification the engine
    /// consumes, exercising the guard regardless of any admitted emission path.
    /// </summary>
    [TestMethod]
    public void NormalizationCollapsesSelfInequalityToEmptyClause()
    {
        DlClause selfInequality = DlClause.Create([], [DlLiteral.Inequality(DlTerm.Function(0), DlTerm.Function(0))], 0);

        ContextSaturationEngine engine = SaturateSynthetic([selfInequality], TestContext.CancellationToken);

        Assert.IsTrue(engine.IsInconsistent, "A self-inequality head normalises to the empty clause in the trivial context.");
        Assert.IsGreaterThan(0L, engine.BuildStatistics(contextDecided: true).IneqApplications, "The Ineq rule fires once on the self-inequality collapse.");
    }

    /// <summary>
    /// The §3.2 insert-normalization tautology arm: a synthetic empty-body clause
    /// with a self-equality head <c>f0(x) approx f0(x)</c> is a tautology (KR 2016
    /// Definition 4) and is DROPPED, so the trivial context stays consistent and the
    /// derived-clause count is identical to the empty-ontology baseline. The
    /// non-inert MU5 face at saturation level: the drop adds no clause.
    /// </summary>
    [TestMethod]
    public void NormalizationDropsSelfEqualityTautology()
    {
        DlClause selfEquality = DlClause.Create([], [DlLiteral.Equality(DlTerm.Function(0), DlTerm.Function(0))], 0);

        ContextSaturationEngine withTautology = SaturateSynthetic([selfEquality], TestContext.CancellationToken);
        ContextSaturationEngine baseline = SaturateSynthetic([], TestContext.CancellationToken);

        Assert.IsFalse(withTautology.IsInconsistent, "A self-equality head is a tautology, not a contradiction.");
        Assert.AreEqual(baseline.BuildStatistics(contextDecided: true).ClausesDerived, withTautology.BuildStatistics(contextDecided: true).ClausesDerived, "The dropped tautology adds no derived clause.");
    }

    /// <summary>
    /// The §3.2 insert-normalization orientation arm: the two orientations of one
    /// equality (<c>f0(x) approx f1(x)</c> and <c>f1(x) approx f0(x)</c>), both
    /// seeded as synthetic empty-body clauses, orient to the SAME maximal-side-first
    /// literal and canonicalise to a single derived clause, so the two-clause
    /// clausification derives exactly what the one-clause clausification does. Pins
    /// that orientation makes duplicate collapse canonical.
    /// </summary>
    [TestMethod]
    public void NormalizationOrientsEqualityCanonically()
    {
        DlClause forward = DlClause.Create([], [DlLiteral.Equality(DlTerm.Function(0), DlTerm.Function(1))], 0);
        DlClause reversed = DlClause.Create([], [DlLiteral.Equality(DlTerm.Function(1), DlTerm.Function(0))], 0);

        ContextSaturationEngine one = SaturateSynthetic([forward], TestContext.CancellationToken);
        ContextSaturationEngine both = SaturateSynthetic([forward, reversed], TestContext.CancellationToken);

        Assert.AreEqual(one.BuildStatistics(contextDecided: true).ClausesDerived, both.BuildStatistics(contextDecided: true).ClausesDerived, "The two orientations of one equality canonicalise to a single derived clause.");
    }

    /// <summary>
    /// TIE-1 (the engine-pin family): the DL4 all-equality merge head selects a
    /// DETERMINISTIC SINGLETON under the term-order tie-break. The
    /// pairwise head over three successor terms holds two equalities sharing the
    /// oriented maximal side <c>f2</c>; their primary comparison ties on every
    /// axis (same max argument, both <c>NoSymbol</c>, non-Role arguments), so
    /// only the tie-break — <c>CompareFTerm</c> on the minimal side, then the
    /// total <c>DlLiteral.CompareTo</c> fallback — orders them. Dropping that
    /// fallback (the mutation) makes the pair co-maximal and the maximal
    /// set two-element, which this pin fails on directly. An OWL-level VERDICT
    /// cannot witness the mutation: maximal-set firing over the coarsened order
    /// is the reference's known-complete mode and the residual carry recovers
    /// the un-selected co-maximal one step later, so the deterministic-singleton
    /// assert here is the sharpest observable.
    /// </summary>
    [TestMethod]
    public void Tie1AllEqualityMergeHeadSelectsDeterministicSingletonMaximal()
    {
        DlClause merge = DlClause.Create(
            [],
            [
                DlLiteral.Equality(DlTerm.Function(1), DlTerm.Function(0)),
                DlLiteral.Equality(DlTerm.Function(2), DlTerm.Function(0)),
                DlLiteral.Equality(DlTerm.Function(2), DlTerm.Function(1)),
            ],
            0);
        ContextTermOrder order = ContextTermOrder.ForModule([merge]);

        List<int> maximal = [];
        order.CollectMaximalHead(merge.Head, maximal, ContextGrammarKind.Ordinary);

        Assert.HasCount(1, maximal, "The DL4 all-equality head selects a singleton maximal set: the tie-break totally orders the two equalities sharing the maximal side f2.");
        Assert.AreEqual(DlLiteral.Equality(DlTerm.Function(2), DlTerm.Function(1)), merge.Head[maximal[0]], "The selected literal is the term-order-greatest equality f2 ~ f1 (shared maximal side f2, greater minimal side f1).");
    }

    /// <summary>
    /// The REAL-FACTOR-COLLAPSE synthetic witness (the equality-factoring rule's
    /// own certificate): a single clause carrying two positive equalities that
    /// share the oriented maximal side <c>f2</c> is exactly the published
    /// equality-factoring premise, and saturating it fires Factor — the
    /// conclusion introduces the <c>f0 ≉ f1</c> inequality between the two
    /// minimal sides while keeping the selected equality. <c>Canonicalise</c>
    /// (sort + dedup) cannot manufacture that literal, so a non-zero
    /// <c>FactorApplications</c> certifies the factoring conclusion path
    /// executed; the factored clause set stays consistent (no spurious bottom).
    /// </summary>
    [TestMethod]
    public void FactorFiresOnSharedMaxSideEqualityHead()
    {
        DlClause merge = DlClause.Create(
            [],
            [
                DlLiteral.Equality(DlTerm.Function(2), DlTerm.Function(0)),
                DlLiteral.Equality(DlTerm.Function(2), DlTerm.Function(1)),
            ],
            0);

        ContextSaturationEngine engine = SaturateSynthetic([merge], TestContext.CancellationToken);

        Assert.IsGreaterThan(0L, engine.BuildStatistics(contextDecided: true).FactorApplications, "Factor fires on a clause whose two positive equalities share the oriented maximal side.");
        Assert.IsFalse(engine.IsInconsistent, "Equality factoring introduces the minimal-side inequality; it derives no bottom from a satisfiable head.");
    }

    /// <summary>
    /// The public bounded reasoner surface across the admission × budget matrix
    /// (census-located arms). On an ADMITTED disjunctive module the unbounded
    /// <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>
    /// decides the module-local subsumption set, and a one-inference ceiling on the
    /// same admitted module abstains through
    /// <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, ReasoningBudget, SaturationProgressSampler, System.Threading.CancellationToken)"/> —
    /// the abstention is the answer, not a delegation. On a NON-ADMITTED module the
    /// bounded entry reaches the snapshot tableau bounded by the same budget: a
    /// generous budget decides a whole verdict through the tableau leg and a
    /// one-inference ceiling abstains, so the fallback leg is bounded rather than
    /// searching without end.
    /// </summary>
    [TestMethod]
    public void PublicBoundedSurfacesAbstainOnExhaustionAndBoundTheFallback()
    {
        ReasoningModule union = Module(
            SubClassOf(Class("A"), Union(Class("B"), Class("C"))),
            SubClassOf(Class("B"), Class("D")),
            SubClassOf(Class("C"), Class("D")));

        ModuleDecision decided = ContextSaturationModuleReasoner.DecideModule(union, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decided.Outcome, "The admitted covering module decides unbounded on the context tier.");
        Assert.IsTrue(decided.Verdict!.IsConsistent, "The covering module is consistent.");
        Assert.Contains(Sub("A", "D"), SubsumptionKeys(decided.Verdict!), "The unbounded decision reads the module-local subsumption A [= D.");

        ModuleDecision admittedStarved = ContextSaturationModuleReasoner.DecideModule(union, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), progressSampler: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, admittedStarved.Outcome, "A one-inference ceiling on the admitted module abstains — the abstention is the answer, not a delegation.");
        Assert.IsNull(admittedStarved.Verdict, "The admitted budget abstention carries no verdict.");

        ReasoningModule nonAdmitted = NonAdmittedTableauModule();

        ModuleDecision delegatedDecided = ContextSaturationModuleReasoner.DecideModule(nonAdmitted, ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken);
        Assert.AreNotEqual(ReasoningDecisionOutcome.AbstainedBudget, delegatedDecided.Outcome, "A non-admitted module reaches the snapshot tableau and decides under a generous budget.");
        Assert.IsNotNull(delegatedDecided.Verdict, "The bounded tableau leg returns a verdict on the non-admitted module.");
        Assert.IsGreaterThan(0, delegatedDecided.Statistics.TableauTotals.RuleApplications, "The non-admitted module was decided by the snapshot tableau, not the context tier.");

        ModuleDecision delegatedStarved = ContextSaturationModuleReasoner.DecideModule(nonAdmitted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), progressSampler: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, delegatedStarved.Outcome, "A one-inference ceiling bounds the snapshot-tableau fallback on the non-admitted module, so it abstains.");
        Assert.IsNull(delegatedStarved.Verdict, "The bounded fallback abstention carries no verdict.");
    }

    /// <summary>
    /// The bounded consistency-only surface
    /// <see cref="ContextSaturationModuleReasoner.DecideConsistencyModule(ReasoningModule, ReasoningBudget, System.Threading.CancellationToken)"/>
    /// across the admission × budget matrix. On an ADMITTED module a one-inference
    /// ceiling abstains carrying the spent context totals (the abstention is the
    /// answer), and a generous budget decides consistent with no subsumption (no
    /// query context is created). On a NON-ADMITTED module the surface delegates to
    /// the snapshot tableau bounded by the same budget: a generous budget decides
    /// through the tableau leg (non-zero tableau rule applications prove it ran),
    /// and a one-inference ceiling abstains as the tableau leg exhausts its own
    /// bound (a per-leg budget).
    /// </summary>
    [TestMethod]
    public void DecideConsistencyModuleAbstainsOnExhaustionAcrossAdmissionAndBudget()
    {
        //A top-forced existential chain drives the consistency-only saturation of the
        //trivial context through several successor inferences (the union covering
        //module, whose disjunction fires only on a node labelled A, decides its
        //trivial context in one), so the one-inference ceiling exhausts it.
        ReasoningModule admitted = Module(
            SubClassOf(ThingReference, Some("r", Class("A"))),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Some("r", Class("C"))));

        ModuleDecision admittedStarved = ContextSaturationModuleReasoner.DecideConsistencyModule(admitted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, admittedStarved.Outcome, "A one-inference ceiling on the admitted module abstains on the context tier.");
        Assert.IsNull(admittedStarved.Verdict, "The admitted budget abstention carries no verdict.");
        Assert.IsGreaterThan(0L, admittedStarved.Statistics.ContextTotals.RuleApplications, "The abstention carries the spent context saturation's totals.");

        ModuleDecision admittedDecided = ContextSaturationModuleReasoner.DecideConsistencyModule(admitted, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, admittedDecided.Outcome, "A generous budget decides the admitted module consistent.");
        Assert.IsTrue(admittedDecided.Verdict!.IsConsistent, "The top-forced existential chain is consistent.");
        Assert.IsEmpty(admittedDecided.Verdict!.Subsumptions, "The consistency-only surface creates no query context, so it reads no subsumption.");

        ReasoningModule nonAdmitted = NonAdmittedTableauModule();

        ModuleDecision delegatedDecided = ContextSaturationModuleReasoner.DecideConsistencyModule(nonAdmitted, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        Assert.AreNotEqual(ReasoningDecisionOutcome.AbstainedBudget, delegatedDecided.Outcome, "A non-admitted module reaches the snapshot tableau and decides under a generous budget.");
        Assert.IsNotNull(delegatedDecided.Verdict, "The bounded tableau leg returns a verdict.");
        Assert.IsGreaterThan(0, delegatedDecided.Statistics.TableauTotals.RuleApplications, "The non-admitted module was decided by the snapshot tableau leg, proving the delegation ran.");
        Assert.IsEmpty(delegatedDecided.Verdict!.Subsumptions, "The consistency-only delegation runs no subsumption sweep in the tableau leg, so it reads no subsumption.");

        ModuleDecision delegatedStarved = ContextSaturationModuleReasoner.DecideConsistencyModule(nonAdmitted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, delegatedStarved.Outcome, "A one-inference ceiling bounds the snapshot-tableau leg on the non-admitted module, so it abstains.");
        Assert.IsNull(delegatedStarved.Verdict, "The bounded fallback abstention carries no verdict.");
    }

    /// <summary>
    /// The unbounded verdict surface
    /// <see cref="ContextSaturationModuleReasoner.Decide(ReasoningModule, System.Threading.CancellationToken)"/>:
    /// an ADMITTED covering module decides on the context tier and reads its
    /// module-local subsumption A [= D, and a NON-ADMITTED module is delegated whole
    /// to the snapshot tableau and decides consistent (fragment-relative on the
    /// chain remainder).
    /// </summary>
    [TestMethod]
    public void UnboundedDecideDecidesTheAdmittedSubsumptionAndDelegatesTheNonAdmitted()
    {
        ReasoningModule union = Module(
            SubClassOf(Class("A"), Union(Class("B"), Class("C"))),
            SubClassOf(Class("B"), Class("D")),
            SubClassOf(Class("C"), Class("D")));

        ModuleVerdict admitted = ContextSaturationModuleReasoner.Decide(union, TestContext.CancellationToken);
        Assert.IsTrue(admitted.IsConsistent, "The covering module is consistent.");
        Assert.Contains(Sub("A", "D"), SubsumptionKeys(admitted), "The unbounded verdict surface reads the module-local subsumption A [= D on the context tier.");

        ModuleVerdict delegated = ContextSaturationModuleReasoner.Decide(NonAdmittedTableauModule(), TestContext.CancellationToken);
        Assert.IsTrue(delegated.IsConsistent, "The non-admitted module is delegated to the snapshot tableau and decides consistent.");
    }

    /// <summary>
    /// The registry- and budget-carrying consistency-only surface
    /// <see cref="ContextSaturationModuleReasoner.DecideConsistencyModule(ReasoningModule, DatatypeRegistry, ReasoningBudget, System.Threading.CancellationToken)"/>
    /// on a non-empty registry: a generous budget decides an admitted covering
    /// module consistent with no subsumption (the consistency-only surface creates
    /// no query context), and a one-inference ceiling on a top-forced existential
    /// chain abstains with a reason.
    /// </summary>
    [TestMethod]
    public void DecideConsistencyModuleWithRegistryAndBudgetDecidesGenerouslyAndAbstainsStarved()
    {
        DatatypeRegistry registry = NonEmptyRegistry();

        ReasoningModule union = Module(
            SubClassOf(Class("A"), Union(Class("B"), Class("C"))),
            SubClassOf(Class("B"), Class("D")),
            SubClassOf(Class("C"), Class("D")));

        ModuleDecision decided = ContextSaturationModuleReasoner.DecideConsistencyModule(union, registry, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1000), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decided.Outcome, "A generous budget decides the admitted covering module consistent through the registry-carrying entry.");
        Assert.IsTrue(decided.Verdict!.IsConsistent, "The covering module is consistent.");
        Assert.IsEmpty(decided.Verdict!.Subsumptions, "The consistency-only surface creates no query context, so it reads no subsumption.");

        ReasoningModule chain = Module(
            SubClassOf(ThingReference, Some("r", Class("A"))),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Some("r", Class("C"))));

        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideConsistencyModule(chain, registry, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "A one-inference ceiling on the admitted chain abstains through the registry-carrying entry.");
        Assert.IsNull(abstained.Verdict, "The starved abstention carries no verdict.");
    }

    /// <summary>
    /// The per-constant root-data-obligation arm's reasoner wiring: a
    /// nominal-jurisdiction module whose
    /// data demand instantiates at a constant (<c>{o} ⊑ ∃dp.integer</c> — the
    /// demand marker lands ON the root context) is decided per ≈-class by the
    /// per-constant root arm, and the reasoner reads a completed saturation and
    /// DECIDES the module whole rather than delegating over the obligation. A lone
    /// integer existential is satisfiable, so the module decides CONSISTENT. The
    /// engine-level arm pins live in the below-gate battery; this row is the
    /// production wiring's witness.
    /// </summary>
    [TestMethod]
    public void RootDataDemandDecidesPerConstant()
    {
        ReasoningModule module = Module(
            SubClassOf(OneOf("o"), DataSome("dp", Integer)),
            SubClassOf(Class("Z"), OneOf("zed")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The per-constant root arm decides the root data demand's ≈-class, so the module decides whole on the context path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A single guarded data demand is satisfiable, so the module decides CONSISTENT.");
    }

    /// <summary>
    /// The census-located ApplyFactor budget arm: on a single clause carrying THREE
    /// positive equalities sharing the oriented maximal side, the selected equality
    /// factors against two others in one loop, and a ceiling calibrated between the
    /// first and second factoring stops the loop midway — exactly one factoring
    /// lands and the saturation reports budget exhaustion. The pin self-calibrates
    /// by scanning ceilings upward, so it never encodes attempt totals.
    /// </summary>
    [TestMethod]
    public void FactorBudgetTripStopsTheFactorLoopMidway()
    {
        long unbounded = FactorApplicationsUnder(ReasoningBudget.Unbounded, out SaturationOutcome unboundedOutcome);
        Assert.AreEqual(SaturationOutcome.Completed, unboundedOutcome, "The unbounded saturation completes.");
        Assert.IsGreaterThanOrEqualTo(2L, unbounded, "The selected equality factors against both maximal-side sharers when unbounded.");

        for(int ceiling = 1; ceiling <= 512; ceiling++)
        {
            long factored = FactorApplicationsUnder(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ceiling), out SaturationOutcome outcome);
            if(factored == 1)
            {
                Assert.AreEqual(SaturationOutcome.BudgetExhausted, outcome, "A ceiling separating the first factoring from the second stops inside the Factor loop as an exhaustion, never a silent completion.");

                return;
            }
        }

        Assert.Fail("No ceiling in 1..512 separated the first factoring from the second; the calibration scan must find the mid-loop trip.");
    }

    /// <summary>
    /// The census-located marker face, DECIDED by the disjunctive data lane: the
    /// union's abstracted existential resolves into the derived disjunctive head
    /// <c>Ash(x) → Cedar(x) ∨ m∃integer(x)</c>, the refutation probe clashes the
    /// marker against the unit pool's string universal (disjoint value spaces),
    /// and the body-conditioned narrowing derives the entailed
    /// <c>Ash ⊑ Cedar</c> INSIDE the engine — the demand is never silently
    /// dropped: refute-or-certify-or-latch answers it instead of a blanket
    /// delegation, and a decide WITHOUT the entailment is the wrong-verdict
    /// shape the assertions target.
    /// </summary>
    [TestMethod]
    public void DataDemandUnderCarriedDisjunctDecidesThroughTheDisjunctiveLane()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Ash"), Union(Class("Cedar"), DataSome("age", new OwlDatatypeReference(new NamedNode(Vocabulary.Xsd.Integer))))),
            SubClassOf(Class("Ash"), new OwlDataAllValuesFrom([new NamedNode(Utf8Strings.From(Example + "age"))], new OwlDatatypeReference(new NamedNode(Vocabulary.Xsd.String)))));

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The disjunctive data lane decides the module whole: the derived marker disjunct refutes against the pool instead of latching the blanket delegation.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Ash keeps the Cedar model.");
        Assert.IsGreaterThan(0L, decision.Statistics.ContextTotals.DisjunctiveDataRefutations, "The refutation rule refutes the derived existential disjunct against the string universal.");
        List<string> expected = [Sub("Ash", "Cedar")];
        List<string> actual = SubsumptionKeys(decision.Verdict);
        Assert.IsTrue(KeysEqual(expected, actual), "The engine reads the entailed subsumption: the string universal kills the integer branch through the body-conditioned narrowing. " + DiffKeys(expected, actual));
    }

    /// <summary>Saturates the three-equality shared-maximal-side merge clause under the given budget and reads the factoring count.</summary>
    /// <param name="budget">The budget bounding the saturation.</param>
    /// <param name="outcome">The saturation outcome.</param>
    /// <returns>The Factor applications spent.</returns>
    private long FactorApplicationsUnder(ReasoningBudget budget, out SaturationOutcome outcome)
    {
        DlClause merge = DlClause.Create(
            [],
            [
                DlLiteral.Equality(DlTerm.Function(3), DlTerm.Function(0)),
                DlLiteral.Equality(DlTerm.Function(3), DlTerm.Function(1)),
                DlLiteral.Equality(DlTerm.Function(3), DlTerm.Function(2)),
            ],
            0);
        ContextSymbolTable symbols = new();
        ClausificationResult clausification = new([merge], [], symbols, ContextTermOrder.ForModule([merge]), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [], NominalJurisdiction: false, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        outcome = engine.Saturate(budget, TestContext.CancellationToken);

        return engine.BuildStatistics(contextDecided: outcome == SaturationOutcome.Completed).FactorApplications;
    }

    /// <summary>
    /// The sampled in-saturation progress trace: with a
    /// <see cref="SaturationProgressSampler"/> attached at
    /// the engine, one mark is emitted at each power-of-two attempt count —
    /// sequence-numbered consecutively from zero, stamped by the caller's fixed
    /// clock, carrying the attached correlation id, attempt marks strictly
    /// increasing powers of two, and coherent population and funnel columns —
    /// the growth-curve instrument the deep probes read. An engine without a
    /// sampler emits nothing, the zero-cost default every other test in this
    /// class exercises.
    /// </summary>
    [TestMethod]
    public void SaturationProgressSamplerEmitsPowerOfTwoMarks()
    {
        ClausificationResult clausification = ContextClausifier.Clausify(NomWedgeTowerModule(1));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        List<SaturationProgressTraceEvent> marks = [];
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
        Guid correlation = new("8f0e3a52-6f1c-4b7e-9a44-2d5c11e0b301");
        engine.Progress = new SaturationProgressSampler(new ProgressMarkCollector(marks).Handle, clock, correlation);
        engine.EnsureQueryContext(clausification.Symbols.AtomOf(Utf8Strings.From(Example + "NwAnchor0")));

        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The smallest wedge rung saturates to its fixpoint.");
        Assert.IsNotEmpty(marks, "A saturation spending hundreds of attempts crosses power-of-two marks, so the attached sampler received events.");
        for(int i = 0; i < marks.Count; i++)
        {
            SaturationProgressTraceEvent mark = marks[i];
            Assert.AreEqual((long)i, mark.SequenceNumber, "Marks are sequence-numbered consecutively from zero.");
            Assert.AreEqual(correlation, mark.CorrelationId, "Every mark carries the attached correlation id.");
            Assert.AreEqual(clock.GetUtcNow().UtcTicks, mark.TimestampTicks, "Every mark is stamped by the caller's clock - the fixed test instant.");
            Assert.AreEqual(0L, mark.InferenceAttempts & (mark.InferenceAttempts - 1), "Every mark lands on a power-of-two attempt count (observed " + mark.InferenceAttempts + ").");
            Assert.IsGreaterThanOrEqualTo(mark.ClausesEliminated, mark.ClausesDerived, "The live population never reads negative: derived stays at or above eliminated.");
            if(i > 0)
            {
                Assert.IsGreaterThan(marks[i - 1].InferenceAttempts, mark.InferenceAttempts, "Attempt marks strictly increase down the sequence.");
            }
        }

        SaturationProgressTraceEvent last = marks[^1];
        Assert.IsGreaterThan(0L, last.WorklistEnqueues, "A completing saturation landed clauses by its final mark.");
        Assert.IsGreaterThan(0, last.ClausesDerived, "A completing saturation derived clauses by its final mark.");
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

    /// <summary>The clause origin the redrive fixtures stamp; the origin value is inert for the containment relation the split counts.</summary>
    private const int RedriveOrigin = -1;

    /// <summary>The concept-atom id the redrive fixtures build their live clause and its duplicate from.</summary>
    private const int RedriveFirstAtom = 5;

    /// <summary>The concept-atom id the first weaker arrival carries beside the live clause's head literal.</summary>
    private const int RedriveSecondAtom = 6;

    /// <summary>The concept-atom id the second weaker arrival carries beside the live clause's head literal.</summary>
    private const int RedriveThirdAtom = 7;

    /// <summary>Builds an empty ordinary context the redrive fixtures insert into and drive conclusions at.</summary>
    /// <returns>The fresh context.</returns>
    private static Context NewRedriveContext()
    {
        return new Context(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());
    }

    /// <summary>A body-empty single-literal concept-head clause <c>T -&gt; A(x)</c> over the central variable.</summary>
    /// <param name="atom">The concept atom.</param>
    /// <returns>The clause.</returns>
    private static DlClause UnconditionalConcept(int atom)
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(atom, DlTerm.Central) }, RedriveOrigin);
    }

    /// <summary>A body-empty two-literal concept-head clause <c>T -&gt; A(x), B(x)</c> — strictly weaker than the single-literal clause over its first atom, so a live one subsumes it through the selected-literal posting.</summary>
    /// <param name="first">The first concept atom.</param>
    /// <param name="second">The second concept atom.</param>
    /// <returns>The clause.</returns>
    private static DlClause UnconditionalDisjunction(int first, int second)
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(first, DlTerm.Central), DlLiteral.Concept(second, DlTerm.Central) }, RedriveOrigin);
    }

    /// <summary>The empty clause — body and head both empty — the local collapse witness that subsumes every clause and keys no posting.</summary>
    /// <returns>The clause.</returns>
    private static DlClause EmptyClause()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), Array.Empty<DlLiteral>(), RedriveOrigin);
    }

    /// <summary>
    /// The public sampler seam: the budget-bearing public decision surface carries
    /// a caller-supplied <see cref="SaturationProgressSampler"/> into every round's
    /// engine, attached before the creation seeding runs. The row asserts the marks
    /// arrive at all, that each carries the caller's correlation id, and that the
    /// FIRST mark lands on inference attempt one -- the attachment-point
    /// discriminator, since an engine sampled only after creation cannot observe
    /// the attempts the seeding spends.
    /// </summary>
    [TestMethod]
    public void TheSamplerParameterEmitsMarksThroughTheBudgetDecideModule()
    {
        ReasoningModule module = NomWedgeTowerModule(1);
        List<SaturationProgressTraceEvent> marks = [];
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
        Guid correlation = new("2b6d4c19-77a0-4f2e-8c31-9e5410ab7d22");
        SaturationProgressSampler sampler = new(new ProgressMarkCollector(marks).Handle, clock, correlation);

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, sampler, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The nominal wedge rung decides under an unbounded budget, so the sampled run reached a verdict.");
        Assert.IsNotEmpty(marks, "The sampler handed to the public budget surface received the run's power-of-two marks.");
        Assert.AreEqual(1L, marks[0].InferenceAttempts, "The first mark lands on attempt one: the sampler is attached before the creation seeding, whose attempts a post-creation attachment could never observe.");
        for(int i = 0; i < marks.Count; i++)
        {
            Assert.AreEqual(correlation, marks[i].CorrelationId, "Every mark carries the correlation id the caller's sampler was built with.");
        }
    }

    /// <summary>
    /// The set-level tripwire over the redundancy machinery: the KR2016 chain
    /// module's derivation, elimination, redundancy, enqueue, and peak-population
    /// totals are pinned to literals, so a containment or backward-subsumption
    /// answer that moves -- a missed subsumer, a wrong probe key, a stale id
    /// collected -- shifts at least one of the five and reds here, complementing
    /// the unit rows that drive each index arm in isolation. The three absorbed
    /// conclusions pin the forward containment arm; the zero eliminations pin the
    /// backward sweep against over-collection, since every extra id it returned
    /// would be tombstoned and counted.
    /// </summary>
    [TestMethod]
    public void EliminationCountsAreUnmovedByTheIndexedSweep()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(O1Module(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.IsTrue(totals.ContextDecided, "The chain module is context-decided, so the totals are the saturation's own.");
        Assert.AreEqual(45, totals.ClausesDerived, "The saturation derives exactly its 45 clauses.");
        Assert.AreEqual(0, totals.ClausesEliminated, "The backward sweep strictly subsumes nothing on this module, so an over-collecting probe reds here.");
        Assert.AreEqual(3L, totals.RedundantConclusions, "The forward containment gate absorbs exactly three conclusions, so a missed subsumer reds here.");
        Assert.AreEqual(45L, totals.WorklistEnqueues, "The run enqueues exactly its 45 landed clauses.");
        Assert.AreEqual(11, totals.MaxContextClauses, "The largest context peaks at 11 live clauses.");
    }

    /// <summary>
    /// The population axis converts an unbounded clause flood into a reasoned
    /// abstention: a decision bounded on total inserted clauses stops at its
    /// ceiling with no verdict and the spent population on its statistics, while
    /// the same module decides whole with 45 clauses unbounded. The pinned
    /// population is exact rather than a bound comparison, since a charged unit of
    /// work may insert a bounded burst past the ceiling before the next check.
    /// </summary>
    [TestMethod]
    public void PopulationBoundedDecideModuleAbstainsOnBudget()
    {
        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideModule(O1Module(), new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: 5), progressSampler: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "A five-clause population ceiling abstains on O1, whose unbounded run inserts 45.");
        Assert.IsNull(abstained.Verdict, "A budget abstention carries no verdict.");
        Assert.AreEqual(17, abstained.Statistics.ContextTotals.ClausesDerived, "The abstention carries the exact population the run had inserted when the ceiling latched: the charged unit that crossed the ceiling seeded its whole burst first, so the recorded population sits above the bound.");
    }

    /// <summary>The population axis ships dark: the unbounded budget carries a zero population bound, and an explicitly zero bound decides the same module with the same population as the unbounded default — a zero bound is unbounded on the axis, never a one-clause ceiling.</summary>
    [TestMethod]
    public void UnboundedBudgetDefaultKeepsThePopulationAxisDark()
    {
        Assert.AreEqual(0, ReasoningBudget.Unbounded.MaxDerivedClauses, "The unbounded budget bounds nothing on the population axis.");

        ModuleDecision unbounded = ContextSaturationModuleReasoner.DecideModule(O1Module(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken);
        ModuleDecision explicitZero = ContextSaturationModuleReasoner.DecideModule(O1Module(), new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: 0), progressSampler: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unbounded.Outcome, "The unbounded decision reaches a verdict.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, explicitZero.Outcome, "An explicit zero population bound reaches the same verdict.");
        Assert.AreEqual(45, unbounded.Statistics.ContextTotals.ClausesDerived, "The unbounded run inserts its 45 clauses.");
        Assert.AreEqual(45, explicitZero.Statistics.ContextTotals.ClausesDerived, "The zero-bounded run inserts exactly the same 45 clauses, so the axis moved nothing.");
    }

    /// <summary>
    /// Behind the seam a population exhaustion composes into a DELEGATION exactly
    /// as an inference exhaustion does: a context-admitted module under a
    /// one-clause population ceiling exhausts the context tier, and the seam
    /// returns the fallback oracle's decided verdict carrying the exhausted
    /// saturation's context totals rather than surfacing the abstention.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task TheSeamComposesAPopulationAbstentionIntoADelegation()
    {
        DescriptionLogicDelegate seam = ReasoningEngines.ContextSaturation(
            new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: 1),
            ReasoningEngines.SatBacked(ReasoningBudget.Unbounded));

        ModuleDecision decision = await seam(P1Module(), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreNotEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The population-exhausted context tier delegates to the fallback rather than surfacing the abstention through the seam.");
        Assert.IsNotNull(decision.Verdict, "The delegated decision carries the fallback's verdict.");
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The exhausted saturation did not decide the module, so its ContextDecided flag stays false.");
        Assert.IsGreaterThan(0, decision.Statistics.ContextTotals.ClausesDerived, "The exhausted saturation's spent population is reattached to the delegated decision.");
    }

    /// <summary>
    /// The mark carries the relevance funnel pair and the containment split pair:
    /// driven through the public sampler seam under the unrestricted default, both
    /// relevance columns read zero at every mark (the filtered mode is the only
    /// thing that moves them) and the two containment columns partition the mark's
    /// own redundancy total. The final mark's split is pinned to its exact values,
    /// so a transposition of the two columns at the emission site reds here.
    /// </summary>
    [TestMethod]
    public void TheMarkCarriesTheRelevanceAndContainmentSplitColumns()
    {
        List<SaturationProgressTraceEvent> marks = [];
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
        SaturationProgressSampler sampler = new(new ProgressMarkCollector(marks).Handle, clock, new Guid("6c1f9d38-0a24-4a55-b0d7-3f8e21c4a915"));

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, sampler, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The nominal wedge rung decides, so the sampled run reached a verdict.");
        Assert.IsNotEmpty(marks, "The sampled run crossed power-of-two attempt marks.");
        for(int i = 0; i < marks.Count; i++)
        {
            SaturationProgressTraceEvent mark = marks[i];
            Assert.AreEqual(0L, mark.RootPredFilteredOffers, "The unrestricted default blocks no r-Pred offer, so the column reads zero at every mark.");
            Assert.AreEqual(0L, mark.RelevanceTautologiesSeeded, "The unrestricted default seeds no compensation tautology, so the column reads zero at every mark.");
            Assert.AreEqual(mark.RedundantConclusions, mark.DuplicateContainmentHits + mark.SubsumedContainmentHits, "The two containment columns partition the mark's own redundancy total.");
        }

        SaturationProgressTraceEvent last = marks[^1];
        TestContext.WriteLine("final mark: duplicate=" + last.DuplicateContainmentHits + " subsumed=" + last.SubsumedContainmentHits + " redundant=" + last.RedundantConclusions);
        Assert.AreEqual(216L, last.DuplicateContainmentHits, "The final mark's exact-duplicate absorptions.");
        Assert.AreEqual(90L, last.SubsumedContainmentHits, "The final mark's subsumer absorptions - distinct from the duplicate half, so a transposition of the two columns at the emission site reds here.");
    }

    /// <summary>
    /// The containment split attributes the exact-duplicate fast-path hit and the
    /// index-drawn subsumer hit to different counters: one duplicate arrival and
    /// two strictly weaker arrivals over the same live clause charge one and two.
    /// A split that reported every hit as a duplicate, or that transposed the two,
    /// reds on the distinct values.
    /// </summary>
    [TestMethod]
    public void TheContainmentSplitCountsDuplicatesAndSubsumedSeparately()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewRedriveContext();
        context.Insert(UnconditionalConcept(RedriveFirstAtom), isPredEligible: false, decidedUnderNoChoice: true, [0]);

        engine.RedriveAddClause(context, UnconditionalConcept(RedriveFirstAtom), []);
        engine.RedriveAddClause(context, UnconditionalDisjunction(RedriveFirstAtom, RedriveSecondAtom), []);
        engine.RedriveAddClause(context, UnconditionalDisjunction(RedriveFirstAtom, RedriveThirdAtom), []);

        ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: false);
        Assert.AreEqual(3L, totals.RedundantConclusions, "All three arrivals are contained up to redundancy.");
        Assert.AreEqual(1L, totals.DuplicateContainmentHits, "The identical arrival is an exact-duplicate hit.");
        Assert.AreEqual(2L, totals.SubsumedContainmentHits, "The two strictly weaker arrivals are index-drawn subsumer hits.");
    }

    /// <summary>The live empty clause subsumes every arrival and keys no posting, so an absorption it drives is a subsumer hit, never an exact duplicate — the third containment arm's attribution, pinned apart from the fast path's.</summary>
    [TestMethod]
    public void TheEmptyClauseAbsorptionCountsAsSubsumedNotDuplicate()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewRedriveContext();
        context.Insert(EmptyClause(), isPredEligible: true, decidedUnderNoChoice: true, []);

        engine.RedriveAddClause(context, UnconditionalConcept(RedriveFirstAtom), []);

        ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: false);
        Assert.AreEqual(1L, totals.RedundantConclusions, "The live empty clause absorbs the arrival.");
        Assert.AreEqual(0L, totals.DuplicateContainmentHits, "The arrival is not a duplicate of the empty clause.");
        Assert.AreEqual(1L, totals.SubsumedContainmentHits, "The empty-clause absorption charges the subsumer counter.");
    }

    /// <summary>The structural invariant of the split on a real saturation: every containment hit the single insertion gate absorbs lands on exactly one of the two counters, so the pair sums to the redundancy total. A counter charged anywhere but that gate breaks the sum.</summary>
    [TestMethod]
    public void TheContainmentSplitSumsToRedundantConclusions()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        TestContext.WriteLine("split: duplicate=" + totals.DuplicateContainmentHits + " subsumed=" + totals.SubsumedContainmentHits + " redundant=" + totals.RedundantConclusions);
        Assert.IsGreaterThan(0L, totals.RedundantConclusions, "The nominal wedge rung absorbs conclusions, so the invariant has something to hold over.");
        Assert.AreEqual(totals.RedundantConclusions, totals.DuplicateContainmentHits + totals.SubsumedContainmentHits, "The split partitions the redundancy total exactly.");
    }

    /// <summary>The per-decision face of the split: the statistics record a caller reads off a decision carries both containment counters, summing to the redundancy total the same record reports.</summary>
    [TestMethod]
    public void TheStatisticsCarryTheContainmentSplit()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(O1Module(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        TestContext.WriteLine("O1 split: duplicate=" + totals.DuplicateContainmentHits + " subsumed=" + totals.SubsumedContainmentHits);
        Assert.AreEqual(3L, totals.RedundantConclusions, "The O1 chain absorbs exactly three conclusions.");
        Assert.AreEqual(3L, totals.DuplicateContainmentHits + totals.SubsumedContainmentHits, "The decision's statistics expose both halves of that absorption total.");
    }

    /// <summary>The distinguishing source-axiom index the span-face survivor row offers under, so a defaulted or swapped origin argument cannot read as the derived marker every other fixture stamps.</summary>
    private const int SpanSurvivorOrigin = 37;

    /// <summary>
    /// The span face of the single mutation point lands its survivor carrying the
    /// origin it was OFFERED, not a default: the stored clause read back by id
    /// reports the distinguishing origin, over the body and head spans it was
    /// handed. Nothing else can see this — the containment relation, the
    /// subsumption sweep, and every gate ignore origin by design — so a swapped or
    /// defaulted origin argument on the span path is visible here alone.
    /// </summary>
    [TestMethod]
    public void TheSpanFaceLandsTheSurvivorCarryingTheOfferedOrigin()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewRedriveContext();
        DlClause offered = DlClause.Create([DlLiteral.Concept(RedriveSecondAtom, DlTerm.Central)], [DlLiteral.Concept(RedriveFirstAtom, DlTerm.Central)], SpanSurvivorOrigin);

        int landed = engine.RedriveAddClauseSpans(context, offered.Body.ToArray(), offered.Head.ToArray(), SpanSurvivorOrigin, []);

        Assert.AreEqual(0, landed, "The first offer into an empty context survives every gate and takes clause id zero.");
        Assert.AreEqual(SpanSurvivorOrigin, context.At(landed).Origin, "The stored survivor carries the origin the span face was handed.");
        Assert.AreNotEqual(RedriveOrigin, context.At(landed).Origin, "The offered origin differs from the derived marker, so a defaulted argument cannot pass unseen.");
        Assert.AreSequenceEqual(offered.Body.ToArray(), context.At(landed).Body.ToArray(), "The survivor's body is the offered span.");
        Assert.AreSequenceEqual(offered.Head.ToArray(), context.At(landed).Head.ToArray(), "The survivor's head is the offered span.");
    }

    /// <summary>
    /// The span face materialises the absorbed conclusion under the absorption
    /// guard, so the origin merge reaches the surviving absorber exactly as the
    /// clause face's does: a choice-tagged live clause absorbing a content-identical
    /// choice-free offer is CLEARED toward the decided origin. Dropping the lazy
    /// materialisation leaves the join with nothing to act on and the tag stands.
    /// The clause face is driven over the same population, so the two faces are
    /// pinned to the same observable rather than argued equal.
    /// </summary>
    [TestMethod]
    public void TheSpanFaceAbsorptionJoinClearsTheChoiceTagLikeTheClauseFace()
    {
        ContextSaturationEngine spanEngine = ContextSaturationEngine.CreateForOriginRedrive();
        Context spanContext = NewRedriveContext();
        int spanAbsorberId = spanContext.Insert(UnconditionalConcept(RedriveFirstAtom), isPredEligible: false, decidedUnderNoChoice: false, [0]);
        spanContext.SetDerivedUnderChoice(spanAbsorberId);

        Assert.IsTrue(spanContext.IsDerivedUnderChoice(spanAbsorberId), "The population starts with the absorber wearing the choice tag.");
        Assert.IsTrue(spanContext.HasDerivedUnderChoiceTags, "The context reports a live choice tag, so the absorption guard is armed.");

        DlClause duplicate = UnconditionalConcept(RedriveFirstAtom);
        int spanLanded = spanEngine.RedriveAddClauseSpans(spanContext, duplicate.Body.ToArray(), duplicate.Head.ToArray(), RedriveOrigin, []);

        ContextSaturationStatistics spanTotals = spanEngine.BuildStatistics(contextDecided: false);
        Assert.AreEqual(-1, spanLanded, "The content-identical offer is absorbed, so nothing lands.");
        Assert.AreEqual(1L, spanTotals.DuplicateContainmentHits, "The span face's exact-duplicate fast path answered the absorption.");
        Assert.AreEqual(0L, spanTotals.SubsumedContainmentHits, "No subsumer walk ran, so the fast path is what answered.");
        Assert.IsFalse(spanContext.IsDerivedUnderChoice(spanAbsorberId), "The span face built the absorbed clause under the absorption guard, so the origin merge cleared the absorber's choice tag.");

        ContextSaturationEngine clauseEngine = ContextSaturationEngine.CreateForOriginRedrive();
        Context clauseContext = NewRedriveContext();
        int clauseAbsorberId = clauseContext.Insert(UnconditionalConcept(RedriveFirstAtom), isPredEligible: false, decidedUnderNoChoice: false, [0]);
        clauseContext.SetDerivedUnderChoice(clauseAbsorberId);

        int clauseLanded = clauseEngine.RedriveAddClause(clauseContext, UnconditionalConcept(RedriveFirstAtom), []);

        ContextSaturationStatistics clauseTotals = clauseEngine.BuildStatistics(contextDecided: false);
        Assert.AreEqual(spanLanded, clauseLanded, "Both faces absorb the offer.");
        Assert.AreEqual(spanTotals.DuplicateContainmentHits, clauseTotals.DuplicateContainmentHits, "Both faces charge the same containment half.");
        Assert.IsFalse(clauseContext.IsDerivedUnderChoice(clauseAbsorberId), "The clause face clears the same tag, so the two faces agree on the absorption observable.");
    }

    /// <summary>
    /// Both faces of the single mutation point charge the derivation funnel
    /// identically over one population that reaches EVERY gate: a tautology head,
    /// an out-of-grammar head, a surviving clause, its exact duplicate, and two
    /// strictly weaker arrivals. Driving the same offers through the clause face and
    /// through the span face and comparing every funnel counter is the plumbing pin
    /// — a defect confined to either face moves one column and not the other.
    /// </summary>
    [TestMethod]
    public void TheSpanAndClauseFacesChargeTheSameFunnelCounters()
    {
        DlClause[] population =
        [
            DlClause.Create([], [DlLiteral.Equality(DlTerm.Function(0), DlTerm.Function(0))], RedriveOrigin),
            DlClause.Create([], [DlLiteral.Concept(ContextSymbolTable.Top, DlTerm.FunctionOf(0, 0))], RedriveOrigin),
            UnconditionalConcept(RedriveFirstAtom),
            UnconditionalDisjunction(RedriveFirstAtom, RedriveSecondAtom),
            UnconditionalDisjunction(RedriveFirstAtom, RedriveThirdAtom),
        ];

        ContextSaturationEngine clauseEngine = ContextSaturationEngine.CreateForOriginRedrive();
        Context clauseContext = NewRedriveContext();
        clauseContext.Insert(UnconditionalConcept(RedriveFirstAtom), isPredEligible: false, decidedUnderNoChoice: true, [0]);
        ContextSaturationEngine spanEngine = ContextSaturationEngine.CreateForOriginRedrive();
        Context spanContext = NewRedriveContext();
        spanContext.Insert(UnconditionalConcept(RedriveFirstAtom), isPredEligible: false, decidedUnderNoChoice: true, [0]);
        for(int i = 0; i < population.Length; i++)
        {
            clauseEngine.RedriveAddClause(clauseContext, population[i], []);
            spanEngine.RedriveAddClauseSpans(spanContext, population[i].Body.ToArray(), population[i].Head.ToArray(), population[i].Origin, []);
        }

        ContextSaturationStatistics clauseTotals = clauseEngine.BuildStatistics(contextDecided: false);
        ContextSaturationStatistics spanTotals = spanEngine.BuildStatistics(contextDecided: false);

        TestContext.WriteLine("clause face: taut=" + clauseTotals.TautologyDrops + " oog=" + clauseTotals.OutOfGrammarConclusions + " derived=" + clauseTotals.ClausesDerived + " redundant=" + clauseTotals.RedundantConclusions + " dup=" + clauseTotals.DuplicateContainmentHits + " sub=" + clauseTotals.SubsumedContainmentHits + " enqueued=" + clauseTotals.WorklistEnqueues + " ineq=" + clauseTotals.IneqApplications);
        Assert.AreEqual(1L, clauseTotals.TautologyDrops, "The self-equality head is dropped as a tautology.");
        Assert.AreEqual(1L, clauseTotals.OutOfGrammarConclusions, "The root-term concept head is refused by the ordinary grammar.");
        Assert.AreEqual(1, clauseTotals.ClausesDerived, "No offer survives — the live clause absorbs its duplicate and both weaker arrivals — so the only derived clause is the Top seed the engine's own creation lands in its trivial context.");
        Assert.AreEqual(1L, clauseTotals.WorklistEnqueues, "The creation seed is the run's only landed event; no offer of this population reached the worklist.");
        Assert.AreEqual(3L, clauseTotals.RedundantConclusions, "Three arrivals are contained up to redundancy.");
        Assert.AreEqual(1L, clauseTotals.DuplicateContainmentHits, "One arrival is an exact duplicate.");
        Assert.AreEqual(2L, clauseTotals.SubsumedContainmentHits, "Two arrivals are index-drawn subsumer hits.");

        Assert.AreEqual(clauseTotals.TautologyDrops, spanTotals.TautologyDrops, "The two faces drop the same tautologies.");
        Assert.AreEqual(clauseTotals.OutOfGrammarConclusions, spanTotals.OutOfGrammarConclusions, "The two faces refuse the same out-of-grammar heads.");
        Assert.AreEqual(clauseTotals.ClausesDerived, spanTotals.ClausesDerived, "The two faces insert the same clauses.");
        Assert.AreEqual(clauseTotals.RedundantConclusions, spanTotals.RedundantConclusions, "The two faces absorb the same conclusions.");
        Assert.AreEqual(clauseTotals.DuplicateContainmentHits, spanTotals.DuplicateContainmentHits, "The two faces charge the same duplicate half.");
        Assert.AreEqual(clauseTotals.SubsumedContainmentHits, spanTotals.SubsumedContainmentHits, "The two faces charge the same subsumed half.");
        Assert.AreEqual(clauseTotals.WorklistEnqueues, spanTotals.WorklistEnqueues, "The two faces enqueue the same landed events.");
        Assert.IsNotNull(clauseEngine.OutOfGrammarSample, "The clause face records the refused shape.");
        Assert.AreEqual(clauseEngine.OutOfGrammarSample, spanEngine.OutOfGrammarSample, "The two faces render the same out-of-grammar sample.");
    }

    /// <summary>
    /// The progress mark's appended tail is read by NAME, not by position: a mark
    /// constructed positionally with seventy-seven DISTINCT sentinel values in the
    /// appended columns returns each sentinel from its own named property. A
    /// transposition at the emission site or in the record's component order reds
    /// here whatever traffic a driven fixture happens to produce.
    /// </summary>
    [TestMethod]
    public void TheProgressMarkTailReadsEachAppendedColumnByName()
    {
        SaturationProgressTraceEvent mark = new(
            0L, 0L, Guid.Empty, 0L, 0L, 0, 0, 0, 0, 0, 0, 0L, 0L, 0L, 0L, 0, 0, 0, 0, 0,
            0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, EnumerationHabitatClass.None, 0L, 0L, 0L, 0L,
            9001L, 9002L, 9003L, 9004L, 9005L, 9006L, 9007L, 9008L, 9009L,
            9010L, 9011L, 9012L, 9013L, 9014L, 9015L, 9016L, 9017L, 9018L, 9019L,
            9020L, 9021L, 9022L, 9023L, 9024L, 9025L, 9026L, 9027L, 9028L,
            9029L, 9030L, 9031L, 9032L, 9033L, 9034L, 9035L, 9036L, 9037L,
            9038L, 9039L, 9040L, 9041L, 9042L, 9043L, 9044L, 9045L, 9046L,
            9047L, 9048L, 9049L, 9050L, 9051L, 9052L, 9053L, 9054L, 9055L,
            9056L, 9057L, 9058L, 9059L, 9060L, 9061L, 9062, 9063, 9064,
            9065L, 9066L, 9067L, 9068L, 9069L, 9070L,
            9071L, 9072L, 9073L, 9074L,
            9075L, 9076L, 9077L);

        Assert.AreEqual(9001L, mark.RootPredRegistrationSweepOffers, "The first appended column is the registration-sweep offer count.");
        Assert.AreEqual(9002L, mark.RootPredNewRootEdgeOffers, "The second appended column is the new-root-edge offer count.");
        Assert.AreEqual(9003L, mark.RootPredPremiseOffers, "The third appended column is the landed-premise offer count.");
        Assert.AreEqual(9004L, mark.RootPredBroadcastOffers, "The fourth appended column is the broadcast offer count.");
        Assert.AreEqual(9005L, mark.RootPredRegistrationSweepDuplicateHits, "The fifth appended column is the registration-sweep duplicate count.");
        Assert.AreEqual(9006L, mark.RootPredNewRootEdgeDuplicateHits, "The sixth appended column is the new-root-edge duplicate count.");
        Assert.AreEqual(9007L, mark.RootPredPremiseDuplicateHits, "The seventh appended column is the landed-premise duplicate count.");
        Assert.AreEqual(9008L, mark.RootPredBroadcastDuplicateHits, "The eighth appended column is the broadcast duplicate count.");
        Assert.AreEqual(9009L, mark.JoinOffers, "The ninth appended column is the join offer count.");
        Assert.AreEqual(9010L, mark.JoinDuplicateHits, "The tenth appended column is the join duplicate count.");
        Assert.AreEqual(9011L, mark.CoreOffers, "The eleventh appended column is the Core offer count.");
        Assert.AreEqual(9012L, mark.CoreDuplicateHits, "The twelfth appended column is the Core duplicate count.");
        Assert.AreEqual(9013L, mark.HyperOffers, "The thirteenth appended column is the Hyper offer count.");
        Assert.AreEqual(9014L, mark.HyperDuplicateHits, "The fourteenth appended column is the Hyper duplicate count.");
        Assert.AreEqual(9015L, mark.PredOffers, "The fifteenth appended column is the Pred offer count.");
        Assert.AreEqual(9016L, mark.PredDuplicateHits, "The sixteenth appended column is the Pred duplicate count.");
        Assert.AreEqual(9017L, mark.EqOffers, "The seventeenth appended column is the Eq offer count.");
        Assert.AreEqual(9018L, mark.EqDuplicateHits, "The eighteenth appended column is the Eq duplicate count.");
        Assert.AreEqual(9019L, mark.FactorOffers, "The nineteenth appended column is the Factor offer count.");
        Assert.AreEqual(9020L, mark.FactorDuplicateHits, "The twentieth appended column is the Factor duplicate count.");
        Assert.AreEqual(9021L, mark.SuccOffers, "The twenty-first appended column is the Succ offer count.");
        Assert.AreEqual(9022L, mark.SuccDuplicateHits, "The twenty-second appended column is the Succ duplicate count.");
        Assert.AreEqual(9023L, mark.NomOffers, "The twenty-third appended column is the Nom offer count.");
        Assert.AreEqual(9024L, mark.NomDuplicateHits, "The twenty-fourth appended column is the Nom duplicate count.");
        Assert.AreEqual(9025L, mark.PushedArrivalOffers, "The twenty-fifth appended column is the push-arrival offer count.");
        Assert.AreEqual(9026L, mark.PushedArrivalDuplicateHits, "The twenty-sixth appended column is the push-arrival duplicate count.");
        Assert.AreEqual(9027L, mark.SidecarSeedOffers, "The twenty-seventh appended column is the sidecar-seed offer count.");
        Assert.AreEqual(9028L, mark.SidecarSeedDuplicateHits, "The twenty-eighth appended column is the sidecar-seed duplicate count.");
        Assert.AreEqual(9029L, mark.PredLandedTargetOffers, "The twenty-ninth appended column is the landed-target driver's Pred offer count.");
        Assert.AreEqual(9030L, mark.PredLandedPremiseOffers, "The thirtieth appended column is the landed-premise driver's Pred offer count.");
        Assert.AreEqual(9031L, mark.PredNewEdgeOffers, "The thirty-first appended column is the new-edge driver's Pred offer count.");
        Assert.AreEqual(9032L, mark.PredLandedTargetDuplicateHits, "The thirty-second appended column is the landed-target driver's Pred duplicate count.");
        Assert.AreEqual(9033L, mark.PredLandedPremiseDuplicateHits, "The thirty-third appended column is the landed-premise driver's Pred duplicate count.");
        Assert.AreEqual(9034L, mark.PredNewEdgeDuplicateHits, "The thirty-fourth appended column is the new-edge driver's Pred duplicate count.");
        Assert.AreEqual(9035L, mark.PredOdometerRuns, "The thirty-fifth appended column is the Pred odometer run count.");
        Assert.AreEqual(9036L, mark.PredIntraRunDuplicateHits, "The thirty-sixth appended column is the within-run Pred duplicate count.");
        Assert.AreEqual(9037L, mark.OriginClearReenqueues, "The thirty-seventh appended column is the origin-merge re-enqueue count.");
        Assert.AreEqual(9038L, mark.RootPredRegistrationSweepSubsumedHits, "The thirty-eighth appended column is the registration-sweep subsumed count.");
        Assert.AreEqual(9039L, mark.RootPredNewRootEdgeSubsumedHits, "The thirty-ninth appended column is the new-root-edge subsumed count.");
        Assert.AreEqual(9040L, mark.RootPredPremiseSubsumedHits, "The fortieth appended column is the landed-premise origin's subsumed count.");
        Assert.AreEqual(9041L, mark.RootPredBroadcastSubsumedHits, "The forty-first appended column is the broadcast origin's subsumed count.");
        Assert.AreEqual(9042L, mark.JoinSubsumedHits, "The forty-second appended column is the join subsumed count.");
        Assert.AreEqual(9043L, mark.CoreSubsumedHits, "The forty-third appended column is the Core subsumed count.");
        Assert.AreEqual(9044L, mark.HyperSubsumedHits, "The forty-fourth appended column is the Hyper subsumed count.");
        Assert.AreEqual(9045L, mark.PredSubsumedHits, "The forty-fifth appended column is the Pred subsumed count.");
        Assert.AreEqual(9046L, mark.PredLandedTargetSubsumedHits, "The forty-sixth appended column is the landed-target driver's subsumed count.");
        Assert.AreEqual(9047L, mark.PredLandedPremiseSubsumedHits, "The forty-seventh appended column is the landed-premise driver's subsumed count.");
        Assert.AreEqual(9048L, mark.PredNewEdgeSubsumedHits, "The forty-eighth appended column is the new-edge driver's subsumed count.");
        Assert.AreEqual(9049L, mark.PredLandedTargetLandings, "The forty-ninth appended column is the landed-target driver's landing count.");
        Assert.AreEqual(9050L, mark.PredLandedPremiseLandings, "The fiftieth appended column is the landed-premise driver's landing count.");
        Assert.AreEqual(9051L, mark.PredNewEdgeLandings, "The fifty-first appended column is the new-edge driver's landing count.");
        Assert.AreEqual(9052L, mark.EqSubsumedHits, "The fifty-second appended column is the Eq subsumed count.");
        Assert.AreEqual(9053L, mark.FactorSubsumedHits, "The fifty-third appended column is the Factor subsumed count.");
        Assert.AreEqual(9054L, mark.SuccSubsumedHits, "The fifty-fourth appended column is the Succ subsumed count.");
        Assert.AreEqual(9055L, mark.NomSubsumedHits, "The fifty-fifth appended column is the Nom subsumed count.");
        Assert.AreEqual(9056L, mark.PushedArrivalSubsumedHits, "The fifty-sixth appended column is the push-arrival subsumed count.");
        Assert.AreEqual(9057L, mark.SidecarSeedSubsumedHits, "The fifty-seventh appended column is the sidecar-seed subsumed count.");
        Assert.AreEqual(9058L, mark.JoinOfferingRuns, "The fifty-eighth appended column is the join offering-run count.");
        Assert.AreEqual(9059L, mark.JoinIntraRunDuplicateHits, "The fifty-ninth appended column is the within-run join duplicate count.");
        Assert.AreEqual(9060L, mark.EqOfferingRuns, "The sixtieth appended column is the Eq offering-run count.");
        Assert.AreEqual(9061L, mark.EqIntraRunDuplicateHits, "The sixty-first appended column is the within-run Eq duplicate count.");
        Assert.AreEqual(9062, mark.RootBroadcastClauseCount, "The sixty-second appended column is the broadcast population count.");
        Assert.AreEqual(9063, mark.CautiousCoreCeiling, "The sixty-third appended column is the cautious core ceiling.");
        Assert.AreEqual(9064, mark.CautiousCoresRegistered, "The sixty-fourth appended column is the registered cautious core count.");
        Assert.AreEqual(9065L, mark.HeadOccurrenceEntriesRegistered, "The sixty-fifth appended column is the head-occurrence registered-entry count.");
        Assert.AreEqual(9066L, mark.BodyOccurrenceEntriesRegistered, "The sixty-sixth appended column is the body-occurrence registered-entry count.");
        Assert.AreEqual(9067L, mark.HeadOccurrenceDistinctKeys, "The sixty-seventh appended column is the head-occurrence distinct-key count.");
        Assert.AreEqual(9068L, mark.BodyOccurrenceDistinctKeys, "The sixty-eighth appended column is the body-occurrence distinct-key count.");
        Assert.AreEqual(9069L, mark.SurvivorSweepProbes, "The sixty-ninth appended column is the survivor-sweep probe count.");
        Assert.AreEqual(9070L, mark.SurvivorSweepPostingEntriesWalked, "The seventieth appended column is the survivor-sweep walked-entry count.");
        Assert.AreEqual(9071L, mark.PredAnchoredArmDispatches, "The seventy-first appended column is the anchored-arm dispatch count.");
        Assert.AreEqual(9072L, mark.PredOrdinaryArmDispatches, "The seventy-second appended column is the ordinary-arm dispatch count.");
        Assert.AreEqual(9073L, mark.PredAnchorInvariantTargetPasses, "The seventy-third appended column is the anchor-invariant target-pass count.");
        Assert.AreEqual(9074L, mark.PredAnchorPruned, "The seventy-fourth appended column is the pruned anchored-offer count.");
        Assert.AreEqual(9075L, mark.PredBroadcastContainedSkips, "The seventy-fifth appended column is the skipped contained-image offer count.");
        Assert.AreEqual(9076L, mark.PredOrdinaryInvariantTargetPasses, "The seventy-sixth appended column is the ordinary-arm invariant target-pass count.");
        Assert.AreEqual(9077L, mark.PredBroadcastImageTargets, "The seventy-seventh appended column is the registered-broadcast-image target count.");
        Assert.AreEqual(0L, mark.SubsumedContainmentHits, "The column ahead of the appended tail keeps its own value, so the tail was appended rather than inserted.");
    }

    /// <summary>
    /// The nine appended columns ride EVERY mark of a driven nominal run and hold
    /// their structural relations there: each origin's offers stand at or above its
    /// landings would allow, the four duplicate columns together never exceed the
    /// mark's own exact-duplicate total, and the join offers stand at or above the
    /// join applications. The final mark's columns are pinned exactly, so a column
    /// that stopped being fed reds rather than reading a plausible zero.
    /// </summary>
    [TestMethod]
    public void TheProgressMarkCarriesTheOfferAndDuplicateColumns()
    {
        List<SaturationProgressTraceEvent> marks = [];
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
        SaturationProgressSampler sampler = new(new ProgressMarkCollector(marks).Handle, clock, new Guid("2d41f7a8-9b30-4c62-8e15-5a7c93d0b641"));

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, sampler, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The nominal wedge rung decides, so the sampled run reached a verdict.");
        Assert.IsNotEmpty(marks, "The sampled run crossed power-of-two attempt marks.");
        for(int i = 0; i < marks.Count; i++)
        {
            SaturationProgressTraceEvent mark = marks[i];
            long offers = mark.RootPredRegistrationSweepOffers + mark.RootPredNewRootEdgeOffers + mark.RootPredPremiseOffers + mark.RootPredBroadcastOffers;
            long duplicates = mark.RootPredRegistrationSweepDuplicateHits + mark.RootPredNewRootEdgeDuplicateHits + mark.RootPredPremiseDuplicateHits + mark.RootPredBroadcastDuplicateHits;
            Assert.IsGreaterThanOrEqualTo(mark.RootPredApplications, offers, "Every landed r-Pred conclusion was offered first, so the offer columns cover the applications.");
            Assert.IsGreaterThanOrEqualTo(duplicates, mark.DuplicateContainmentHits, "The r-Pred duplicate columns are a share of the mark's own exact-duplicate total.");
            Assert.IsGreaterThanOrEqualTo(mark.JoinApplications, mark.JoinOffers, "Every landed join conclusion was offered first.");
            Assert.IsGreaterThanOrEqualTo(duplicates, offers, "An origin's duplicates are a share of its offers.");
        }

        SaturationProgressTraceEvent last = marks[^1];
        TestContext.WriteLine("final mark offers: sweep=" + last.RootPredRegistrationSweepOffers + " edge=" + last.RootPredNewRootEdgeOffers + " premise=" + last.RootPredPremiseOffers + " broadcast=" + last.RootPredBroadcastOffers
            + " dup: " + last.RootPredRegistrationSweepDuplicateHits + "/" + last.RootPredNewRootEdgeDuplicateHits + "/" + last.RootPredPremiseDuplicateHits + "/" + last.RootPredBroadcastDuplicateHits
            + " joinOffers=" + last.JoinOffers + " joinApplications=" + last.JoinApplications);
        Assert.AreEqual(5L, last.RootPredRegistrationSweepOffers, "The final mark's registration-sweep offers.");
        Assert.AreEqual(0L, last.RootPredNewRootEdgeOffers, "The final mark's new-root-edge offers: the rung opens no root edge whose sweep re-attempts, so the origin's column reads zero rather than absorbing another origin's traffic.");
        Assert.AreEqual(0L, last.RootPredPremiseOffers, "The final mark's landed-premise offers: the origin carries no traffic on this rung, so its column reads zero.");
        Assert.AreEqual(65L, last.RootPredBroadcastOffers, "The final mark's broadcast offers — distinct from every other origin's, so a misattributed increment reds here.");
        Assert.AreEqual(4L, last.RootPredRegistrationSweepDuplicateHits, "The final mark's registration-sweep duplicates.");
        Assert.AreEqual(0L, last.RootPredNewRootEdgeDuplicateHits, "The final mark's new-root-edge duplicates.");
        Assert.AreEqual(0L, last.RootPredPremiseDuplicateHits, "The final mark's landed-premise duplicates.");
        Assert.AreEqual(10L, last.RootPredBroadcastDuplicateHits, "The final mark's broadcast duplicates: the broadcast path reads its offers' outcomes, so a broadcast face left on the plain boolean sink could never populate this column.");
        Assert.AreEqual(160L, last.JoinOffers, "The final mark's join offers, far above the seven landed join applications — the offer-versus-landing gap the column exists to read.");
    }

    /// <summary>
    /// The in-saturation grammar guard's live face — the runtime killer for a
    /// mutation that drops the guard: a
    /// synthetic ontology clause whose Hyper conclusion carries a concept atom
    /// over the root term <c>f(o)</c> into an ORDINARY (query) context — a shape
    /// outside the ordinary context grammar that no clausifier-produced module
    /// derives after the closure audit — is REFUSED at the single mutation
    /// point: the refusal counts on the funnel's
    /// <see cref="ContextSaturationStatistics.OutOfGrammarConclusions"/> stage,
    /// latches <see cref="ContextSaturationEngine.HasOutOfGrammarDerivation"/>
    /// (the named delegation the production reasoner reads), and records the
    /// rendered first shape on
    /// <see cref="ContextSaturationEngine.OutOfGrammarSample"/>. The saturation
    /// itself completes — the guard is a sound latch, never a wedge.
    /// </summary>
    [TestMethod]
    public void OutOfGrammarConclusionCountsLatchesAndSamples()
    {
        ContextSymbolTable symbols = new();
        int anchor = symbols.InternIndividual(Utf8Strings.From(Example + "o"), IndividualOrigin.IriDenoted);
        int bodyAtom = symbols.AtomOf(Utf8Strings.From(Example + "A"));
        int headAtom = symbols.AtomOf(Utf8Strings.From(Example + "B"));
        DlClause ontology = DlClause.Create(
            [DlLiteral.Concept(bodyAtom, DlTerm.Central)],
            [DlLiteral.Concept(headAtom, DlTerm.FunctionOf(0, anchor))],
            0);
        ClausificationResult clausification = new([ontology], [], symbols, ContextTermOrder.ForModule([ontology]), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [], NominalJurisdiction: true, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.EnsureQueryContext(bodyAtom);

        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The refused conclusion never wedges the saturation.");
        Assert.IsTrue(engine.HasOutOfGrammarDerivation, "The out-of-grammar conclusion latches the named delegation the production reasoner consumes.");
        Assert.IsNotNull(engine.OutOfGrammarSample, "The first refused shape is recorded on the permanent latch diagnostic.");
        Assert.IsGreaterThan(0L, engine.BuildStatistics(contextDecided: false).OutOfGrammarConclusions, "The refusal counts on the derivation funnel's grammar stage.");
    }

    /// <summary>
    /// The root-clash reader face, end to end (the strengthen-first probe for
    /// the vr-probe mutation): a synthetic clausification whose sole content is an
    /// empty-clause RootFact must read inconsistent. The battery MEASURED the
    /// duplication mechanism this face rides: an empty clause is trivially n-zero
    /// r-Pred eligible, so its image is broadcast INLINE at insertion into every
    /// context (the trivial context included) — which is why the reader's dedicated
    /// root probe is defense-in-depth on every completed saturation (it can be the
    /// sole witness only on a budget-exhausted partial structure, whose verdict is
    /// never read). This pin certifies the reader consumes a root-seeded clash
    /// through whichever arm carries it.
    /// </summary>
    [TestMethod]
    public void RootSeededEmptyClauseReadsInconsistent()
    {
        ContextSymbolTable symbols = new();
        symbols.InternIndividual(Utf8Strings.From(Example + "o"), IndividualOrigin.IriDenoted);
        DlClause emptyClause = DlClause.Create([], [], 0);
        ClausificationResult clausification = new([], [], symbols, ContextTermOrder.ForModule([]), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [emptyClause], NominalJurisdiction: true, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);

        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The seeded structure saturates trivially.");
        Assert.IsTrue(engine.IsInconsistent, "A root-context empty clause is a sound inconsistency witness the reader must consume off the root probe itself.");
    }

    /// <summary>
    /// THE BRIDGE SWEEP'S CURSOR IS A VALUE, NOT AN INDEX. The synthetic module
    /// below drives a bridge individual to register MID-SWEEP, numerically BEHIND
    /// the cursor: three constants are interned in ascending id order (the residual
    /// disjunct's, the mid-sweep registrant's, and the swept one's), and three root
    /// facts broadcast their <c>y ↦ x</c> images into the trivial context IN THAT
    /// ORDER — the abstract premise <c>⊤ → B(x) ∨ x ≈ o_res</c>, the disjunctive
    /// bridge <c>⊤ → x ≈ o_reg ∨ x ≈ o_swept</c> whose MAXIMAL disjunct is the
    /// swept constant's, and the ground premise <c>B(o_swept) → ⊥</c>. The abstract
    /// premise is therefore the first of the three PROCESSED with the other two
    /// already inserted, so its own sweep is what derives the bridge conclusion
    /// <c>⊤ → x ≈ o_res ∨ x ≈ o_reg</c> — an empty-body clause whose maximal
    /// literal is the registrant's equality, which posts that constant while the
    /// cursor stands at the higher swept one. A value cursor re-locates past the
    /// visited VALUE and neither revisits it nor reaches the constant that
    /// registered below it, reproducing exactly what the per-individual dictionary
    /// probe answered. An index cursor re-reads the shifted slot and offers the
    /// same conclusion a second time, so every counter pinned here reads EXACTLY
    /// ONE higher — an integer, timing-free divergence.
    /// </summary>
    [TestMethod]
    public void TheBridgeSweepCursorSurvivesMidSweepRegistrationBelowIt()
    {
        ContextSaturationEngine engine = MidSweepRegistrationEngine();

        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The synthetic module saturates to its fixpoint.");

        ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: true);
        TestContext.WriteLine(ChannelCounterLine("midSweep", totals));
        Assert.AreEqual(2L, totals.JoinApplications, "The bridge conclusion lands once and the two later re-derivations are absorbed, whatever the cursor does.");
        Assert.AreEqual(5L, totals.JoinOffers, "The join offers: an index cursor re-reads the shifted slot and offers a sixth.");
        Assert.AreEqual(3L, totals.JoinDuplicateHits, "The join offers the gate absorbed as exact duplicates: an index cursor's re-offer absorbs as a fourth.");
        Assert.AreEqual(39L, totals.InferenceAttempts, "The budget-gated attempts: the re-offer of an index cursor charges a fortieth.");
        Assert.AreEqual(7L, totals.DuplicateContainmentHits, "The run's exact-duplicate absorptions: an index cursor's re-offer absorbs as an eighth.");
        Assert.AreEqual(18L, totals.RedundantConclusions, "The run's absorbed conclusions.");
        Assert.AreEqual(21, totals.ClausesDerived, "The inserted population is cursor-independent: the re-offer of an index cursor lands nothing new.");
    }

    /// <summary>
    /// THE CLOSURE IDENTITY of the per-channel duplicate attribution: on a whole
    /// module decision the per-channel exact-duplicate counters SUM EXACTLY to
    /// <see cref="ContextSaturationStatistics.DuplicateContainmentHits"/>, the
    /// run's own count of that gate's fast-path absorptions. Every production seam
    /// that offers a conclusion to the single mutation point carries a channel, so
    /// a seam left uncounted, an increment charged twice, or a duplicate attributed
    /// to a channel the offer did not come from breaks the sum. The fixtures drive
    /// whole-module decisions rather than the redrive seams, which are test-only
    /// drivers with no channel and no production path. Each fixture's per-channel
    /// reading rides the log, so a channel that stops being fed is visible as a
    /// zero beside its peers.
    /// </summary>
    [TestMethod]
    public void ThePerChannelDuplicateCountersCloseOnTheDuplicateTotal()
    {
        (string Name, ReasoningModule Module)[] fixtures =
        [
            ("nominalBridgeChain", NominalBridgeChainModule()),
            ("nominalWedge", NomWedgeTowerModule(1)),
            ("chain", O1Module()),
            ("equalityMerge", EqualityMergeModule()),
            ("dataNarrowing", DataNarrowingModule()),
        ];

        StringBuilder report = new();
        report.AppendLine();
        List<string> mismatches = [];
        long witnessed = 0;
        foreach((string name, ReasoningModule module) in fixtures)
        {
            ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
            report.AppendLine(ChannelCounterLine(name, totals));
            witnessed += totals.DuplicateContainmentHits;
            if(ChannelDuplicateSum(totals) != totals.DuplicateContainmentHits)
            {
                mismatches.Add(name + ": the channel duplicate counters sum to " + ChannelDuplicateSum(totals) + " but the run absorbed " + totals.DuplicateContainmentHits + " exact duplicates.");
            }

            if(totals.SubsumedContainmentHits > 0 && ChannelDuplicateSum(totals) > totals.DuplicateContainmentHits)
            {
                mismatches.Add(name + ": a subsumer absorption reached a duplicate channel counter.");
            }
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
        Assert.IsGreaterThan(280L, witnessed, "The fixture set absorbs hundreds of exact duplicates, so the closure is asserted over real traffic rather than over zeros. " + report);
    }

    /// <summary>
    /// The anchored Pred arm runs ONE completion on an anchor-invariant target and
    /// credits the constants it did not run. The fixture drives a zero-slot target
    /// whose head is a single ground literal, so every anchoring constant would
    /// complete the same conclusion: the arm dispatches four times, the invariance
    /// test passes on all four, one offer is charged per dispatch, and the pruned
    /// credit is the remaining two constants per dispatch. The conclusion still
    /// lands in the predecessor, so the surviving completion is the derivation
    /// rather than a skipped one.
    /// </summary>
    [TestMethod]
    public void TheAnchorHoistRunsOneCompletionAndCreditsTheRemainingConstants()
    {
        ContextSaturationStatistics totals = SaturatedTotals(AnchorInvariantTargetEngine(), TestContext.CancellationToken);

        Assert.AreEqual(4L, totals.PredAnchoredArmDispatches, "The fixture drives four Pred dispatches over the constant-anchored root arm.");
        Assert.AreEqual(0L, totals.PredOrdinaryArmDispatches, "Every dispatch of this fixture has the root as its predecessor, so the ordinary arm stays dark.");
        Assert.AreEqual(4L, totals.PredAnchorInvariantTargetPasses, "Every one of those dispatches carries an anchor-invariant target.");
        Assert.AreEqual(8L, totals.PredAnchorPruned, "Three told individuals leave two credited constants per hoisted dispatch.");
        Assert.AreEqual(4L, totals.PredOffers, "One offer is charged per hoisted dispatch — the surviving completion, and no other Pred traffic exists here.");
        Assert.AreEqual(4L, totals.PredOdometerRuns, "Each surviving completion reaches its combination cursor exactly once.");
        Assert.AreEqual(4L, totals.PredNewEdgeOffers, "Every offer of this fixture rides the new-edge driver.");
        Assert.AreEqual(0L, totals.PredLandedTargetOffers, "No offer of this fixture rides the landed-target driver.");
        Assert.AreEqual(1L, totals.PredApplications, "The surviving completion's conclusion lands in the predecessor, so the pruned dispatches lose no derivation.");
    }

    /// <summary>
    /// The anchored arm DECLINES a zero-slot target whose head carries the central
    /// variable and runs every anchoring constant instead: the head conjunct of the
    /// anchor-invariance test refuses it, so its dispatch charges no pass and no
    /// pruned credit, its offers stay at one per constant, and three distinct
    /// per-anchor conclusions land. The fixture's own remaining dispatches are
    /// anchor-invariant, so the pass count sits strictly below the dispatch count
    /// rather than at it.
    /// </summary>
    [TestMethod]
    public void TheAnchorHoistDeclinesANongroundHeadTargetAndRunsEveryConstant()
    {
        ContextSaturationStatistics totals = SaturatedTotals(AnchorDependentHeadTargetEngine(), TestContext.CancellationToken);

        Assert.AreEqual(6L, totals.PredAnchoredArmDispatches, "The fixture drives six Pred dispatches over the constant-anchored root arm.");
        Assert.AreEqual(0L, totals.PredOrdinaryArmDispatches, "Every dispatch of this fixture has the root as its predecessor.");
        Assert.AreEqual(5L, totals.PredAnchorInvariantTargetPasses, "The nonground-head target is the one dispatch the invariance test refuses.");
        Assert.IsLessThan(totals.PredAnchoredArmDispatches, totals.PredAnchorInvariantTargetPasses, "A test that accepted every target would pass as often as it dispatched.");
        Assert.AreEqual(10L, totals.PredAnchorPruned, "Only the accepted dispatches credit constants, two apiece.");
        Assert.AreEqual(8L, totals.PredOffers, "The refused dispatch charges one offer per anchoring constant while the accepted ones charge one apiece.");
        Assert.AreEqual(8L, totals.PredOdometerRuns, "Every charged offer of this fixture comes from its own cursor-reaching run.");
        Assert.AreEqual(3L, totals.PredApplications, "The refused target lands a DISTINCT conclusion for each of the three constants, which one hoisted completion could not produce.");
    }

    /// <summary>
    /// The anchored arm DECLINES a target with a nonground body position even
    /// though its head is a single ground literal: the body conjunct of the
    /// anchor-invariance test refuses it, so that dispatch charges no pass and no
    /// pruned credit and its completion stays slot-driven — one offer, from the one
    /// anchoring constant whose sigma-image the predecessor can discharge.
    /// </summary>
    [TestMethod]
    public void TheAnchorHoistDeclinesANongroundBodyTargetAndRunsEveryConstant()
    {
        ContextSaturationStatistics totals = SaturatedTotals(AnchorDependentBodyTargetEngine(), TestContext.CancellationToken);

        Assert.AreEqual(5L, totals.PredAnchoredArmDispatches, "The fixture drives five Pred dispatches over the constant-anchored root arm.");
        Assert.AreEqual(0L, totals.PredOrdinaryArmDispatches, "Every dispatch of this fixture has the root as its predecessor.");
        Assert.AreEqual(4L, totals.PredAnchorInvariantTargetPasses, "The nonground-body target is the one dispatch the invariance test refuses.");
        Assert.IsLessThan(totals.PredAnchoredArmDispatches, totals.PredAnchorInvariantTargetPasses, "A test blind to the body conjunct would pass as often as it dispatched.");
        Assert.AreEqual(8L, totals.PredAnchorPruned, "Only the accepted dispatches credit constants, two apiece.");
        Assert.AreEqual(5L, totals.PredOffers, "The refused dispatch adds the one slot-driven offer its single dischargeable constant supports.");
        Assert.AreEqual(5L, totals.PredOdometerRuns, "A constant whose slot finds no live premise is refused before the cursor and charges no run.");
        Assert.AreEqual(1L, totals.PredApplications, "One conclusion of this fixture lands.");
    }

    /// <summary>
    /// The two arm columns SEPARATE the Pred dispatch population: the anchored
    /// column counts only dispatches whose predecessor runs the constant-anchored
    /// root machinery, the ordinary column counts every other, and the pruned
    /// credit is an anchored-arm charge alone. Four fixtures read the split from
    /// both sides — two ordinary-only modules (one of them nominal, so a credit
    /// misplaced on the ordinary arm would be visible rather than degenerate), the
    /// anchored-only fixture, and that same fixture under the fragmented topology,
    /// where the anchored arm is out of reach by the topology's own definition.
    /// </summary>
    [TestMethod]
    public void ThePredArmCountersSeparateTheAnchoredArmFromTheOrdinaryArm()
    {
        ContextSaturationStatistics chain = ContextSaturationModuleReasoner.DecideModule(S10Module(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics nominalChain = ContextSaturationModuleReasoner.DecideModule(NominalOrdinaryChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics anchored = SaturatedTotals(AnchorInvariantTargetEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics fragmented = SaturatedTotals(AnchorInvariantTargetEngine(RootContextTopology.PerIndividualRoots), TestContext.CancellationToken);

        Assert.AreEqual(1L, chain.PredOrdinaryArmDispatches, "The self-chain module's one Pred dispatch takes the ordinary arm.");
        Assert.AreEqual(0L, chain.PredAnchoredArmDispatches, "The self-chain module has no root tier, so no dispatch takes the anchored arm.");
        Assert.AreEqual(0L, chain.PredAnchorInvariantTargetPasses, "A dispatch that never reaches the anchored arm never runs the invariance test.");
        Assert.AreEqual(0L, chain.PredAnchorPruned, "No constant is credited where no anchored fan-out ran.");

        Assert.AreEqual(2L, nominalChain.PredOrdinaryArmDispatches, "The nominal ordinary-chain module's Pred dispatches run between ordinary contexts.");
        Assert.AreEqual(0L, nominalChain.PredAnchoredArmDispatches, "None of them has the root as its predecessor.");
        Assert.AreEqual(0L, nominalChain.PredAnchorInvariantTargetPasses, "The invariance test is not consulted on the ordinary arm.");
        Assert.AreEqual(0L, nominalChain.PredAnchorPruned, "A credit charged on the ordinary arm would show here, where told individuals exist.");

        Assert.AreEqual(4L, anchored.PredAnchoredArmDispatches, "The anchored fixture's dispatches all take the anchored arm.");
        Assert.AreEqual(0L, anchored.PredOrdinaryArmDispatches, "None of them takes the ordinary arm, so the two columns cannot be transposed unnoticed.");

        Assert.AreEqual(0L, fragmented.PredAnchoredArmDispatches, "Under the fragmented topology no context runs the constant-anchored machinery, so the anchored arm is unreachable.");
        Assert.AreEqual(4L, fragmented.PredOrdinaryArmDispatches, "The same dispatches take the ordinary arm instead.");
        Assert.AreEqual(0L, fragmented.PredAnchorInvariantTargetPasses, "No invariance test runs where the anchored arm is unreachable.");
        Assert.AreEqual(0L, fragmented.PredAnchorPruned, "No constant is credited where the anchored arm is unreachable.");
    }

    /// <summary>
    /// The four appended columns read ZERO on modules that drive no Pred dispatch
    /// at all: an edge-free module, whose one subsumption creates no function edge,
    /// and the nominal-free Horn control, whose chain has no root tier. A phantom
    /// charge on any of the four columns shows here, where no positive row's own
    /// traffic can hide it.
    /// </summary>
    [TestMethod]
    public void TheNewAnchorColumnsReadZeroWhereNoPredDispatchRuns()
    {
        ContextSaturationStatistics edgeFree = ContextSaturationModuleReasoner.DecideModule(Module(SubClassOf(Class("A"), Class("B"))), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics nominalFree = ContextSaturationModuleReasoner.DecideModule(NominalFreeHornModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(0L, edgeFree.PredOffers, "The edge-free module offers no Pred conclusion, so its four appended columns must all read zero.");
        Assert.AreEqual(0L, edgeFree.PredAnchoredArmDispatches, "The edge-free module dispatches Pred over no anchored arm.");
        Assert.AreEqual(0L, edgeFree.PredOrdinaryArmDispatches, "The edge-free module dispatches Pred over no ordinary arm.");
        Assert.AreEqual(0L, edgeFree.PredAnchorInvariantTargetPasses, "The edge-free module runs no invariance test.");
        Assert.AreEqual(0L, edgeFree.PredAnchorPruned, "The edge-free module credits no constant.");

        Assert.AreEqual(0L, nominalFree.PredOffers, "The nominal-free control offers no Pred conclusion.");
        Assert.AreEqual(0L, nominalFree.PredAnchoredArmDispatches, "The nominal-free control dispatches Pred over no anchored arm.");
        Assert.AreEqual(0L, nominalFree.PredOrdinaryArmDispatches, "The nominal-free control dispatches Pred over no ordinary arm.");
        Assert.AreEqual(0L, nominalFree.PredAnchorInvariantTargetPasses, "The nominal-free control runs no invariance test.");
        Assert.AreEqual(0L, nominalFree.PredAnchorPruned, "The nominal-free control credits no constant.");
    }

    /// <summary>
    /// The pruned credit is SUPPRESSED for the hoisted completion whose own insert
    /// reaches the population bound: the credit is charged only where the next
    /// constant's own gate would have admitted its offer, and that gate refuses
    /// once the insert has reached the ceiling. Under a bound placed exactly at the
    /// landing insert the run's credit falls by one dispatch's worth while the
    /// conclusion still lands and the surviving completion still runs, so a credit
    /// charged unconditionally after the completion reds here.
    /// </summary>
    [TestMethod]
    public void ThePopulationBoundSuppressesTheAnchorPrunedCreditOnTheCrossingOffer()
    {
        ContextSaturationEngine engine = AnchorInvariantTargetEngine();
        engine.Saturate(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: 13), TestContext.CancellationToken);
        ContextSaturationStatistics bounded = engine.BuildStatistics(contextDecided: true);

        Assert.AreEqual(13, bounded.ClausesDerived, "The bound is placed exactly at the insert the hoisted completion lands.");
        Assert.AreEqual(4L, bounded.PredAnchoredArmDispatches, "The bounded run reaches the same anchored dispatches as the unbounded one.");
        Assert.AreEqual(4L, bounded.PredAnchorInvariantTargetPasses, "Every one of them carries an anchor-invariant target.");
        Assert.AreEqual(4L, bounded.PredOffers, "Every surviving completion still runs and charges its offer.");
        Assert.AreEqual(4L, bounded.PredOdometerRuns, "Every surviving completion still reaches its cursor.");
        Assert.AreEqual(1L, bounded.PredApplications, "The crossing conclusion still lands.");
        Assert.AreEqual(6L, bounded.PredAnchorPruned, "The crossing completion credits nothing, so the run's credit is one dispatch's worth below the unbounded eight.");
    }

    /// <summary>
    /// The four anchored-arm columns ride EVERY mark of a driven nominal run and
    /// hold their structural relations there: the invariance test never passes more
    /// often than the anchored arm dispatches, and the credited constants never fall
    /// back between marks. The final mark's four columns are pinned exactly on that
    /// run and again on a synthetic run whose three told individuals make all four
    /// columns read DIFFERENT values, so a pair transposed at the emission site —
    /// where the record's own component order is sound — reds here rather than
    /// reading a plausible value. The ordinary arm's own three columns ride the same
    /// mark and are pinned beside them, so a transposition among THEM at the emission
    /// site reds here too rather than only where the record is built positionally.
    /// </summary>
    [TestMethod]
    public void TheProgressMarkCarriesTheAnchoredArmColumns()
    {
        List<SaturationProgressTraceEvent> marks = [];
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
        SaturationProgressSampler sampler = new(new ProgressMarkCollector(marks).Handle, clock, new Guid("8f2a6c14-77d5-4be9-a03c-1d6b5e94f2a7"));

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, sampler, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The nominal wedge rung decides, so the sampled run reached a verdict.");
        Assert.IsNotEmpty(marks, "The sampled run crossed power-of-two attempt marks.");
        long previousPruned = 0L;
        for(int i = 0; i < marks.Count; i++)
        {
            SaturationProgressTraceEvent mark = marks[i];
            Assert.IsGreaterThanOrEqualTo(mark.PredAnchorInvariantTargetPasses, mark.PredAnchoredArmDispatches, "The invariance test runs once per anchored dispatch, so it never passes more often than the arm dispatched.");
            Assert.IsGreaterThanOrEqualTo(previousPruned, mark.PredAnchorPruned, "The credited constants only accumulate, so the column never falls back between marks.");
            previousPruned = mark.PredAnchorPruned;
        }

        SaturationProgressTraceEvent last = marks[^1];
        Assert.AreEqual(39L, last.PredAnchoredArmDispatches, "The final mark's anchored-arm dispatches.");
        Assert.AreEqual(55L, last.PredOrdinaryArmDispatches, "The final mark's ordinary-arm dispatches — a distinct reading from the anchored arm's, so a transposition of the two columns at the emission site reds here.");
        Assert.AreEqual(23L, last.PredAnchorInvariantTargetPasses, "The final mark's anchor-invariant target passes, below the anchored dispatches that ran the test.");
        Assert.AreEqual(22L, last.PredAnchorPruned, "The final mark's credited constants: this module's two told individuals leave one constant per hoisted dispatch that met its gate.");
        Assert.AreEqual(37L, last.PredBroadcastContainedSkips, "The final mark's elided ordinary-arm offers.");
        Assert.AreEqual(41L, last.PredOrdinaryInvariantTargetPasses, "The final mark's sigma-invariant ordinary targets — a reading distinct from the elided offers', so a pair transposed at the emission site reds here.");
        Assert.AreEqual(37L, last.PredBroadcastImageTargets, "The final mark's registered-image ordinary targets, below the invariant passes that ran the test.");

        List<SaturationProgressTraceEvent> syntheticMarks = [];
        SaturationProgressSampler syntheticSampler = new(new ProgressMarkCollector(syntheticMarks).Handle, clock, new Guid("4b90d3e6-2c81-45fa-9a72-6e0c8b31d54f"));
        ContextSaturationEngine engine = AnchorDependentHeadTargetEngine(syntheticSampler);
        engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken);

        Assert.IsNotEmpty(syntheticMarks, "The sampled synthetic run crossed power-of-two attempt marks.");
        SaturationProgressTraceEvent syntheticLast = syntheticMarks[^1];
        Assert.AreEqual(5L, syntheticLast.PredAnchoredArmDispatches, "The synthetic run's anchored dispatches at its final mark.");
        Assert.AreEqual(0L, syntheticLast.PredOrdinaryArmDispatches, "The synthetic run's predecessor is always the root, so its ordinary column reads zero.");
        Assert.AreEqual(4L, syntheticLast.PredAnchorInvariantTargetPasses, "The synthetic run's invariance passes at its final mark.");
        Assert.AreEqual(6L, syntheticLast.PredAnchorPruned, "The synthetic run's credited constants: three told individuals leave two per hoisted dispatch, so the four columns read four DIFFERENT values and no pair of them can be transposed unnoticed.");
    }

    /// <summary>
    /// The credit CLOSES the fixture's offer and run totals against the fan-out it
    /// replaced: on a fixture whose only Pred traffic is the anchored arm's, the
    /// charged offers plus the credited constants equal one offer per anchoring
    /// constant per hoisted dispatch, and the cursor-reaching runs close the same
    /// way. An off-by-one credit or a credit charged where the gate would have
    /// refused breaks both identities.
    /// </summary>
    [TestMethod]
    public void TheAnchorPrunedCreditClosesTheFixtureScaleOfferAndRunTotals()
    {
        ContextSaturationStatistics totals = SaturatedTotals(AnchorInvariantTargetEngine(), TestContext.CancellationToken);

        Assert.AreEqual(12L, totals.PredOffers + totals.PredAnchorPruned, "Three told individuals over four hoisted dispatches are twelve completions, and this fixture carries no other Pred offer.");
        Assert.AreEqual(12L, totals.PredOdometerRuns + totals.PredAnchorPruned, "Each of those completions would have reached its own cursor.");
        Assert.AreEqual(2L * totals.PredAnchorInvariantTargetPasses, totals.PredAnchorPruned, "Every accepted dispatch credits exactly the constants it did not run.");
    }

    /// <summary>
    /// The Core, Hyper, and Pred channels pin their offers and their
    /// exact-duplicate absorptions at EXACT values on two constructed modules with
    /// distinct readings, so a channel transposition — a Hyper offer charged to
    /// the Pred counter — moves both columns and reds here on both rows rather
    /// than cancelling out. Every channel's offers stand at or above its landed
    /// applications, the relation the offer column exists to read.
    /// </summary>
    [TestMethod]
    public void TheCoreHyperAndPredChannelsPinTheirOffersAndDuplicates()
    {
        ContextSaturationStatistics merge = ContextSaturationModuleReasoner.DecideModule(EqualityMergeModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics chain = ContextSaturationModuleReasoner.DecideModule(S10Module(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(ChannelCounterLine("equalityMerge", merge));
        TestContext.WriteLine(ChannelCounterLine("selfChain", chain));

        Assert.AreEqual(7L, merge.CoreOffers, "The equality-merge run's Core seed offers.");
        Assert.AreEqual(0L, merge.CoreDuplicateHits, "The equality-merge run's Core seeds are never exact duplicates.");
        Assert.AreEqual(13L, merge.HyperOffers, "The equality-merge run's Hyper offers.");
        Assert.AreEqual(1L, merge.HyperDuplicateHits, "The equality-merge run's Hyper duplicate absorptions.");
        Assert.AreEqual(3L, merge.PredOffers, "The equality-merge run's Pred offers — distinct from its Hyper offers, so a transposition of the two columns reds here.");
        Assert.AreEqual(0L, merge.PredDuplicateHits, "The equality-merge run's Pred duplicate absorptions.");

        Assert.AreEqual(7L, chain.CoreOffers, "The self-chain run's Core seed offers.");
        Assert.AreEqual(10L, chain.HyperOffers, "The self-chain run's Hyper offers — different from the equality-merge run's, so a fixture-blind misattribution cannot satisfy both rows.");
        Assert.AreEqual(2L, chain.HyperDuplicateHits, "The self-chain run's Hyper duplicate absorptions.");
        Assert.AreEqual(1L, chain.PredOffers, "The self-chain run's Pred offers.");
        Assert.AreEqual(0L, chain.PredDuplicateHits, "The self-chain run's Pred duplicate absorptions.");

        Assert.IsGreaterThanOrEqualTo(merge.CoreApplications, merge.CoreOffers, "Every landed Core seed was offered first.");
        Assert.IsGreaterThanOrEqualTo(merge.HyperApplications, merge.HyperOffers, "Every landed Hyper conclusion was offered first.");
        Assert.IsGreaterThanOrEqualTo(merge.PredApplications, merge.PredOffers, "Every landed Pred conclusion was offered first.");
    }

    /// <summary>
    /// The Eq and Factor channels pin their offers and duplicates at EXACT values.
    /// The Eq offer is charged PAST the constant, paramodulation-scope, and
    /// pre-charge tautology gates, so it counts the conclusions the insertion gate
    /// actually saw rather than the rewrites the dispatch enumerated; the Factor
    /// reading comes from the synthetic module, whose factoring both offers and
    /// absorbs.
    /// </summary>
    [TestMethod]
    public void TheEqAndFactorChannelsPinTheirOffersAndDuplicates()
    {
        ContextSaturationStatistics merge = ContextSaturationModuleReasoner.DecideModule(EqualityMergeModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics bridgeChain = ContextSaturationModuleReasoner.DecideModule(NominalBridgeChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationEngine synthetic = MidSweepRegistrationEngine();
        synthetic.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ContextSaturationStatistics factored = synthetic.BuildStatistics(contextDecided: true);

        TestContext.WriteLine(ChannelCounterLine("equalityMerge", merge));
        TestContext.WriteLine(ChannelCounterLine("nominalBridgeChain", bridgeChain));
        TestContext.WriteLine(ChannelCounterLine("midSweep", factored));

        Assert.AreEqual(4L, merge.EqOffers, "The equality-merge run's Eq offers.");
        Assert.AreEqual(3L, merge.EqDuplicateHits, "The equality-merge run's Eq duplicate absorptions.");
        Assert.AreEqual(11L, bridgeChain.EqOffers, "The bridge-chain run's Eq offers — different from the equality-merge run's, so a fixture-blind misattribution cannot satisfy both rows.");
        Assert.AreEqual(6L, bridgeChain.EqDuplicateHits, "The bridge-chain run's Eq duplicate absorptions.");
        Assert.IsGreaterThanOrEqualTo(merge.EqApplications, merge.EqOffers, "Every landed Eq conclusion was offered first.");
        Assert.IsGreaterThan(merge.EqApplications, merge.EqOffers, "The equality-merge run offers strictly more Eq conclusions than it lands, so a counter keyed on accept could not produce this total.");

        Assert.AreEqual(3L, factored.FactorOffers, "The synthetic run's Factor offers.");
        Assert.AreEqual(1L, factored.FactorDuplicateHits, "The synthetic run's Factor duplicate absorptions.");
        Assert.IsGreaterThanOrEqualTo(factored.FactorApplications, factored.FactorOffers, "Every landed Factor conclusion was offered first.");
    }

    /// <summary>
    /// The Succ channel counts OFFERS, not expansions: one budget-charged
    /// expansion seeds a whole K2 hypothesis set — and, at a designated ground
    /// target, its K1 set as well — through the two seams that share that ONE
    /// upstream charge, so the offer column stands STRICTLY ABOVE
    /// <see cref="ContextSaturationStatistics.SuccApplications"/> on a fixture with
    /// a genuinely multi-trigger expansion. An increment moved beside the shared
    /// upstream charge would count one offer per expansion and collapse the two
    /// columns onto each other, which the strict inequality below refuses.
    /// </summary>
    [TestMethod]
    public void TheSuccChannelCountsPerSeedRatherThanPerExpansion()
    {
        ContextSaturationStatistics wedge = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics merge = ContextSaturationModuleReasoner.DecideModule(EqualityMergeModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(ChannelCounterLine("nominalWedge", wedge));
        TestContext.WriteLine(ChannelCounterLine("equalityMerge", merge));

        Assert.AreEqual(10L, wedge.SuccOffers, "The nominal wedge's Succ seed offers.");
        Assert.AreEqual(4L, wedge.SuccDuplicateHits, "The nominal wedge's Succ seed duplicate absorptions.");
        Assert.AreEqual(5L, wedge.SuccApplications, "The nominal wedge's Succ expansions — half its seed offers, so the two columns are read apart.");
        Assert.IsGreaterThan(wedge.SuccApplications, wedge.SuccOffers, "One expansion seeds several hypotheses, so the offer column stands strictly above the expansion count.");

        Assert.AreEqual(4L, merge.SuccOffers, "The equality-merge run's Succ seed offers.");
        Assert.AreEqual(1L, merge.SuccDuplicateHits, "The equality-merge run's Succ seed duplicate absorptions.");
        Assert.AreEqual(3L, merge.SuccApplications, "The equality-merge run's Succ expansions.");
        Assert.IsGreaterThan(merge.SuccApplications, merge.SuccOffers, "The second fixture carries the same strict multiplicity at different values.");
    }

    /// <summary>
    /// The Join, Nom, push-arrival, and sidecar-seed channels pin their offers and
    /// duplicates at EXACT values, and a nominal-free Horn module leaves every one
    /// of the nominal channels dark. The push-arrival column is the ONE physical
    /// seam the r-Succ seed landing and the inter-nominal carrier image share, so
    /// its reading covers both origins; the sidecar column is fed only by a module
    /// carrying an admitted data restriction. The nominal-free control's zeros run
    /// past the offer columns to the SUBSUMED column of every dark channel and to
    /// both join and Eq offering-run pairs, so a counter charged unconditionally
    /// beside its correct arm — a phantom charge, which a sum identity can be made
    /// to hide — moves a zero here.
    /// </summary>
    [TestMethod]
    public void TheJoinNomPushAndSidecarChannelsPinTheirOffersAndDuplicates()
    {
        ContextSaturationStatistics bridgeChain = ContextSaturationModuleReasoner.DecideModule(NominalBridgeChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics wedge = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics data = ContextSaturationModuleReasoner.DecideModule(DataNarrowingModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics nominalFree = ContextSaturationModuleReasoner.DecideModule(NominalFreeHornModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(ChannelCounterLine("nominalBridgeChain", bridgeChain));
        TestContext.WriteLine(ChannelCounterLine("nominalWedge", wedge));
        TestContext.WriteLine(ChannelCounterLine("dataNarrowing", data));
        TestContext.WriteLine(ChannelCounterLine("nominalFree", nominalFree));

        Assert.AreEqual(34L, bridgeChain.JoinOffers, "The bridge-chain run's join offers.");
        Assert.AreEqual(32L, bridgeChain.JoinDuplicateHits, "The bridge-chain run's join offers absorbed as exact duplicates — the flood the join column exists to attribute.");
        Assert.AreEqual(2L, bridgeChain.JoinApplications, "The bridge-chain run's landed join conclusions.");
        Assert.AreEqual(11L, bridgeChain.PushedArrivalOffers, "The bridge-chain run's push-landing arrivals.");
        Assert.AreEqual(0L, bridgeChain.PushedArrivalDuplicateHits, "The bridge-chain run's push landings are refused by the r-Succ pre-check before the gate rather than absorbed at it.");

        Assert.AreEqual(162L, wedge.JoinOffers, "The nominal wedge's join offers.");
        Assert.AreEqual(147L, wedge.JoinDuplicateHits, "The nominal wedge's join duplicate absorptions — a different reading from the bridge chain's, so a fixture-blind misattribution cannot satisfy both.");
        Assert.AreEqual(2L, wedge.NomOffers, "The nominal wedge's Nom disjunction offers.");
        Assert.AreEqual(1L, wedge.NomDuplicateHits, "The nominal wedge's Nom duplicate absorption.");
        Assert.AreEqual(15L, wedge.PushedArrivalOffers, "The nominal wedge's push-landing arrivals.");

        Assert.AreEqual(1L, data.SidecarSeedOffers, "The data-narrowing run's sidecar seed offer.");
        Assert.AreEqual(0L, data.SidecarSeedDuplicateHits, "The data-narrowing run's sidecar seed lands rather than absorbing.");
        Assert.AreEqual(0L, bridgeChain.SidecarSeedOffers, "A module carrying no admitted data restriction and running the unrestricted relevance mode offers no sidecar seed.");

        Assert.AreEqual(0L, nominalFree.JoinOffers, "A nominal-free module cannot fire the join rule, so its join column is dark.");
        Assert.AreEqual(0L, nominalFree.JoinDuplicateHits, "A nominal-free module absorbs no join duplicate.");
        Assert.AreEqual(0L, nominalFree.NomOffers, "A nominal-free module cannot fire the Nom rule.");
        Assert.AreEqual(0L, nominalFree.PushedArrivalOffers, "A nominal-free module has no root tier, so nothing is pushed into one.");
        Assert.AreEqual(0L, nominalFree.SidecarSeedOffers, "A nominal-free Horn module drives no sidecar seed.");

        Assert.AreEqual(0L, nominalFree.JoinSubsumedHits, "A dark join channel absorbs no subsumer, so a charge made unconditionally beside the correct arm moves this zero.");
        Assert.AreEqual(0L, nominalFree.NomSubsumedHits, "A dark Nom channel absorbs no subsumer.");
        Assert.AreEqual(0L, nominalFree.PushedArrivalSubsumedHits, "A dark push-arrival channel absorbs no subsumer.");
        Assert.AreEqual(0L, nominalFree.SidecarSeedSubsumedHits, "A dark sidecar channel absorbs no subsumer.");
        Assert.AreEqual(0L, nominalFree.RootPredRegistrationSweepSubsumedHits, "A nominal-free module runs no registration sweep.");
        Assert.AreEqual(0L, nominalFree.RootPredNewRootEdgeSubsumedHits, "A nominal-free module opens no root edge.");
        Assert.AreEqual(0L, nominalFree.RootPredPremiseSubsumedHits, "A nominal-free module dispatches no landed root premise.");
        Assert.AreEqual(0L, nominalFree.RootPredBroadcastSubsumedHits, "A nominal-free module broadcasts no root image.");
        Assert.AreEqual(0L, nominalFree.JoinOfferingRuns, "A dark join channel charges no offering run, so a run counter charged at dispatcher entry moves this zero.");
        Assert.AreEqual(0L, nominalFree.JoinIntraRunDuplicateHits, "A dark join channel absorbs no within-run duplicate.");
        Assert.AreEqual(0L, nominalFree.EqOffers, "A nominal-free Horn module carries no equality, so the Eq channel is dark too.");
        Assert.AreEqual(0L, nominalFree.EqSubsumedHits, "A dark Eq channel absorbs no subsumer.");
        Assert.AreEqual(0L, nominalFree.EqOfferingRuns, "A dark Eq channel charges no offering run.");
        Assert.AreEqual(0L, nominalFree.EqIntraRunDuplicateHits, "A dark Eq channel absorbs no within-run duplicate.");
    }

    /// <summary>One log line of the per-channel offer and duplicate counters for a named row — the per-channel witness reading the closure row records beside its sum.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string ChannelCounterLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": attempts=" + totals.InferenceAttempts + " derived=" + totals.ClausesDerived + " redundant=" + totals.RedundantConclusions
            + " dup=" + totals.DuplicateContainmentHits + " sub=" + totals.SubsumedContainmentHits
            + " core=" + totals.CoreOffers + "/" + totals.CoreDuplicateHits
            + " hyper=" + totals.HyperOffers + "/" + totals.HyperDuplicateHits
            + " pred=" + totals.PredOffers + "/" + totals.PredDuplicateHits
            + " eq=" + totals.EqOffers + "/" + totals.EqDuplicateHits
            + " factor=" + totals.FactorOffers + "/" + totals.FactorDuplicateHits
            + " succ=" + totals.SuccOffers + "/" + totals.SuccDuplicateHits
            + " nom=" + totals.NomOffers + "/" + totals.NomDuplicateHits
            + " join=" + totals.JoinOffers + "/" + totals.JoinDuplicateHits
            + " push=" + totals.PushedArrivalOffers + "/" + totals.PushedArrivalDuplicateHits
            + " sidecar=" + totals.SidecarSeedOffers + "/" + totals.SidecarSeedDuplicateHits
            + " rootPredDup=" + (totals.RootPredRegistrationSweepDuplicateHits + totals.RootPredNewRootEdgeDuplicateHits + totals.RootPredPremiseDuplicateHits + totals.RootPredBroadcastDuplicateHits)
            + " channelSum=" + ChannelDuplicateSum(totals);
    }

    /// <summary>The sum of every channel's exact-duplicate counter, the four frozen r-Pred origin counters included — the quantity the closure identity pins against the run's own exact-duplicate total.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The summed duplicate attribution.</returns>
    private static long ChannelDuplicateSum(ContextSaturationStatistics totals)
    {
        return totals.CoreDuplicateHits + totals.HyperDuplicateHits + totals.PredDuplicateHits + totals.EqDuplicateHits
            + totals.FactorDuplicateHits + totals.SuccDuplicateHits + totals.NomDuplicateHits + totals.JoinDuplicateHits
            + totals.PushedArrivalDuplicateHits + totals.SidecarSeedDuplicateHits
            + totals.RootPredRegistrationSweepDuplicateHits + totals.RootPredNewRootEdgeDuplicateHits
            + totals.RootPredPremiseDuplicateHits + totals.RootPredBroadcastDuplicateHits;
    }

    /// <summary>One log line of the per-driver Pred counters and the odometer pair for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string PredDriverLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": pred=" + totals.PredOffers + "/" + totals.PredDuplicateHits
            + " target=" + totals.PredLandedTargetOffers + "/" + totals.PredLandedTargetDuplicateHits
            + " premise=" + totals.PredLandedPremiseOffers + "/" + totals.PredLandedPremiseDuplicateHits
            + " edge=" + totals.PredNewEdgeOffers + "/" + totals.PredNewEdgeDuplicateHits
            + " runs=" + totals.PredOdometerRuns + " intra=" + totals.PredIntraRunDuplicateHits
            + " reenq=" + totals.OriginClearReenqueues
            + " derived=" + totals.ClausesDerived + " attempts=" + totals.InferenceAttempts;
    }

    /// <summary>The sum of the three per-driver Pred offer counters — the quantity the driver partition pins against the channel's own offer total.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The summed driver offers.</returns>
    private static long PredDriverOfferSum(ContextSaturationStatistics totals)
    {
        return totals.PredLandedTargetOffers + totals.PredLandedPremiseOffers + totals.PredNewEdgeOffers;
    }

    /// <summary>The sum of the three per-driver Pred duplicate counters — the quantity the driver partition pins against the channel's own duplicate total.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The summed driver duplicates.</returns>
    private static long PredDriverDuplicateSum(ContextSaturationStatistics totals)
    {
        return totals.PredLandedTargetDuplicateHits + totals.PredLandedPremiseDuplicateHits + totals.PredNewEdgeDuplicateHits;
    }

    /// <summary>One log line of the per-channel SUBSUMED counters for a named row — the subsumed witness the closure row records beside its sum.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string ChannelSubsumedLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": sub=" + totals.SubsumedContainmentHits
            + " core=" + totals.CoreSubsumedHits
            + " hyper=" + totals.HyperSubsumedHits
            + " pred=" + totals.PredSubsumedHits
            + " predTarget=" + totals.PredLandedTargetSubsumedHits
            + " predPremise=" + totals.PredLandedPremiseSubsumedHits
            + " predEdge=" + totals.PredNewEdgeSubsumedHits
            + " eq=" + totals.EqSubsumedHits
            + " factor=" + totals.FactorSubsumedHits
            + " succ=" + totals.SuccSubsumedHits
            + " nom=" + totals.NomSubsumedHits
            + " join=" + totals.JoinSubsumedHits
            + " push=" + totals.PushedArrivalSubsumedHits
            + " sidecar=" + totals.SidecarSeedSubsumedHits
            + " rootPred=" + totals.RootPredRegistrationSweepSubsumedHits + "/" + totals.RootPredNewRootEdgeSubsumedHits
            + "/" + totals.RootPredPremiseSubsumedHits + "/" + totals.RootPredBroadcastSubsumedHits
            + " channelSum=" + ChannelSubsumedSum(totals)
            + " predLandings=" + totals.PredLandedTargetLandings + "/" + totals.PredLandedPremiseLandings + "/" + totals.PredNewEdgeLandings
            + " predApplied=" + totals.PredApplications;
    }

    /// <summary>The sum of every channel-level SUBSUMED counter, the four r-Pred origin counters included — the quantity the subsumed closure identity pins against the run's own subsumer-absorption total.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The summed subsumed attribution.</returns>
    private static long ChannelSubsumedSum(ContextSaturationStatistics totals)
    {
        return totals.CoreSubsumedHits + totals.HyperSubsumedHits + totals.PredSubsumedHits + totals.EqSubsumedHits
            + totals.FactorSubsumedHits + totals.SuccSubsumedHits + totals.NomSubsumedHits + totals.JoinSubsumedHits
            + totals.PushedArrivalSubsumedHits + totals.SidecarSeedSubsumedHits
            + totals.RootPredRegistrationSweepSubsumedHits + totals.RootPredNewRootEdgeSubsumedHits
            + totals.RootPredPremiseSubsumedHits + totals.RootPredBroadcastSubsumedHits;
    }

    /// <summary>The sum of the three per-driver Pred subsumed counters — the quantity the driver partition pins against the channel's own subsumed total.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The summed driver subsumed absorptions.</returns>
    private static long PredDriverSubsumedSum(ContextSaturationStatistics totals)
    {
        return totals.PredLandedTargetSubsumedHits + totals.PredLandedPremiseSubsumedHits + totals.PredNewEdgeSubsumedHits;
    }

    /// <summary>The sum of the three per-driver Pred LANDING counters — the quantity the driver partition pins against the channel's own application total.</summary>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The summed driver landings.</returns>
    private static long PredDriverLandingSum(ContextSaturationStatistics totals)
    {
        return totals.PredLandedTargetLandings + totals.PredLandedPremiseLandings + totals.PredNewEdgeLandings;
    }

    /// <summary>One log line of the Join and Eq offering-run pairs for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string OfferingRunLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": joinRuns=" + totals.JoinOfferingRuns + " joinIntra=" + totals.JoinIntraRunDuplicateHits
            + " joinOffers=" + totals.JoinOffers + " joinDup=" + totals.JoinDuplicateHits
            + " eqRuns=" + totals.EqOfferingRuns + " eqIntra=" + totals.EqIntraRunDuplicateHits
            + " eqOffers=" + totals.EqOffers + " eqDup=" + totals.EqDuplicateHits;
    }

    /// <summary>One log line of a broadcast-provenance recognizer's records for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="recognizer">The armed recognizer.</param>
    /// <returns>The line.</returns>
    private static string RecognizerLine(string name, RootBroadcastProvenanceRecognizer recognizer)
    {
        return name + ": allBroadcast=" + recognizer.AllBroadcastOffers + "/" + recognizer.AllBroadcastDuplicateHits
            + " mixed=" + recognizer.MixedOffers + "/" + recognizer.MixedDuplicateHits
            + " nonBroadcast=" + recognizer.NonBroadcastOffers + "/" + recognizer.NonBroadcastDuplicateHits
            + " emptyPremise=" + recognizer.EmptyPremiseOffers + "/" + recognizer.EmptyPremiseDuplicateHits
            + " probed=" + recognizer.PremisesProbed + " matched=" + recognizer.PremisesMatched;
    }

    /// <summary>Arms a fresh broadcast-provenance recognizer on an unsaturated engine's Pred offer slot and hands it back for the post-run read.</summary>
    /// <param name="engine">The unsaturated engine.</param>
    /// <returns>The armed recognizer.</returns>
    private static RootBroadcastProvenanceRecognizer ArmBroadcastRecognizer(ContextSaturationEngine engine)
    {
        RootBroadcastProvenanceRecognizer recognizer = new(engine.RootBroadcastImages);
        engine.Recognizers.PredOfferProbe = recognizer.Observe;

        return recognizer;
    }

    /// <summary>The engine-probe arming side of a module-driven recognizer read: it arms one fresh recognizer per constructed engine and holds every one, so the decision's own round sequence is observable and the last round's recognizer is the one whose statistics the decision carries. A named instance method rather than a captured lambda, so the arming carries no closure.</summary>
    private sealed class BroadcastRecognizerArmer
    {
        /// <summary>The recognizers armed so far, in engine-construction order; the last is the deciding round's.</summary>
        public List<RootBroadcastProvenanceRecognizer> Armed { get; } = [];

        /// <summary>Arms a fresh recognizer on one constructed engine's Pred offer slot.</summary>
        /// <param name="engine">The engine handed over before seeding.</param>
        public void Arm(ContextSaturationEngine engine)
        {
            RootBroadcastProvenanceRecognizer recognizer = new(engine.RootBroadcastImages);
            engine.Recognizers.PredOfferProbe = recognizer.Observe;
            Armed.Add(recognizer);
        }
    }

    /// <summary>Saturates a synthetic engine to its fixpoint under an unbounded budget and hands back its counters.</summary>
    /// <param name="engine">The unsaturated engine.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saturated engine's totals.</returns>
    private static ContextSaturationStatistics SaturatedTotals(ContextSaturationEngine engine, System.Threading.CancellationToken cancellationToken)
    {
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, cancellationToken), "The synthetic module saturates to its fixpoint under an unbounded budget.");

        return engine.BuildStatistics(contextDecided: true);
    }

    /// <summary>
    /// The odometer canonicalises its assembled body and head BEFORE offering them:
    /// two root premises sharing a body atom and a residual disjunct assemble a
    /// completion whose raw literal sequence is unsorted and carries one duplicate
    /// in each span, and whose canonical form is exactly a pre-seeded root clause.
    /// The offer therefore reads as an EXACT DUPLICATE and lands nothing. A dropped
    /// or late canonicalisation hands the gate the raw duplicate-bearing sequence,
    /// which no longer hashes as the stored clause, so the exact-duplicate share and
    /// the derived population both move here — the unit-level agreement between the
    /// two canonical faces cannot see that, because it never asks whether the
    /// odometer calls the shared routine at all.
    /// </summary>
    [TestMethod]
    public void TheOdometerCanonicalisesItsCompletionBeforeOfferingIt()
    {
        ContextSaturationStatistics totals = SaturatedTotals(PreSeededCompletionEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("preSeededCompletion", totals));
        TestContext.WriteLine(PredDriverLine("preSeededCompletion", totals));
        Assert.AreEqual(4L, totals.PredOffers, "The run's Pred offers.");
        Assert.AreEqual(4L, totals.PredDuplicateHits, "Every Pred offer of this population canonicalises onto a clause the root already holds, so all four are absorbed at the exact-duplicate fast path.");
        Assert.AreEqual(0L, totals.PredApplications, "No Pred offer lands: a completion whose canonical form is the pre-seeded clause inserts nothing.");
        Assert.AreEqual(12, totals.ClausesDerived, "The inserted population carries no Pred conclusion.");
        Assert.AreEqual(7L, totals.DuplicateContainmentHits, "The run's exact-duplicate absorptions.");
        Assert.AreEqual(1L, totals.SubsumedContainmentHits, "The run's single subsumer absorption, which is not a Pred offer's.");
    }

    /// <summary>
    /// The Pred channel splits its offers by DRIVER, and the landed-target and
    /// landed-premise drivers are read apart on a population where both fire at
    /// DIFFERENT counts: a third filler's own clash target completes first and
    /// lands a root conclusion whose head fires the ontology lift, so a second
    /// candidate for the two-slot target's first body position arrives in the
    /// predecessor after that target and its edge already exist and the landed-
    /// premise dispatch pins it. A dropped assignment leaves the previous driver's
    /// value standing and moves both columns; a transposition moves them the other
    /// way, and neither reading satisfies the second fixture as well.
    /// </summary>
    [TestMethod]
    public void ThePredChannelSplitsItsOffersByLandedTargetAndLandedPremise()
    {
        ContextSaturationStatistics arrival = SaturatedTotals(PremiseArrivalEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics swap = SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(PredDriverLine("premiseArrival", arrival));
        TestContext.WriteLine(PredDriverLine("crossSlotSwap", swap));
        Assert.AreEqual(7L, arrival.PredLandedTargetOffers, "The premise-arrival run's landed-target offers.");
        Assert.AreEqual(5L, arrival.PredLandedTargetDuplicateHits, "The premise-arrival run's landed-target duplicate absorptions.");
        Assert.AreEqual(2L, arrival.PredLandedPremiseOffers, "The premise-arrival run's landed-premise offers — the late candidate pinned at the first body position, a count distinct from every other driver's on this fixture.");
        Assert.AreEqual(0L, arrival.PredLandedPremiseDuplicateHits, "The premise-arrival run's landed-premise offers both land rather than absorbing.");

        Assert.AreEqual(8L, swap.PredLandedTargetOffers, "The cross-slot-swap run's landed-target offers — a different reading from the premise-arrival run's, so a fixture-blind misattribution cannot satisfy both.");
        Assert.AreEqual(5L, swap.PredLandedTargetDuplicateHits, "The cross-slot-swap run's landed-target duplicate absorptions.");
        Assert.AreEqual(0L, swap.PredLandedPremiseOffers, "The cross-slot-swap population lands no premise after its edge exists, so the landed-premise column reads zero rather than absorbing another driver's traffic.");
    }

    /// <summary>
    /// The new-edge driver counts the sweep a freshly added function edge runs over
    /// the successor's already-registered Pred-eligible clauses — traffic neither
    /// landing-driven column may absorb. The two fixtures carry it at different
    /// counts, one of them the module-level nominal chain whose new-edge sweep
    /// survives the containment elision at a single offer, so a column swapped with
    /// either landing driver reds on both.
    /// </summary>
    [TestMethod]
    public void ThePredNewEdgeDriverCountsTheSweepOverAFreshFunctionEdge()
    {
        ContextSaturationStatistics bridgeChain = ContextSaturationModuleReasoner.DecideModule(NominalBridgeChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics arrival = SaturatedTotals(PremiseArrivalEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(PredDriverLine("nominalBridgeChain", bridgeChain));
        TestContext.WriteLine(PredDriverLine("premiseArrival", arrival));
        Assert.AreEqual(1L, bridgeChain.PredNewEdgeOffers, "The bridge-chain run's new-edge offers.");
        Assert.AreEqual(0L, bridgeChain.PredNewEdgeDuplicateHits, "The bridge-chain run's new-edge driver absorbs no duplicate, so a driver transposition moves this zero.");
        Assert.AreEqual(3L, bridgeChain.PredLandedTargetOffers, "The bridge-chain run's landed-target offers, distinct from its new-edge offers.");

        Assert.AreEqual(1L, arrival.PredNewEdgeOffers, "The premise-arrival run's new-edge offers.");
        Assert.AreEqual(1L, arrival.PredNewEdgeDuplicateHits, "The premise-arrival run's new-edge offer is absorbed as an exact duplicate.");
    }

    /// <summary>
    /// THE DRIVER PARTITION: on every fixture the three per-driver Pred offer
    /// counters SUM EXACTLY to <see cref="ContextSaturationStatistics.PredOffers"/>
    /// and the three duplicate counters to
    /// <see cref="ContextSaturationStatistics.PredDuplicateHits"/>, so a fourth
    /// dispatch path left unattributed, an increment charged twice, or a duplicate
    /// keyed off an outcome the channel total does not key off breaks the sum. Each
    /// driver is witnessed nonzero somewhere in the set — the purpose-built
    /// fixtures guarantee the floor rather than hoping the module set covers it —
    /// and at least one duplicate column is nonzero, so the identity is never
    /// satisfied by zeros. The origin-merge re-enqueue column is pinned ZERO across
    /// the whole set: the choice tag has no source under the single-root topology,
    /// so the merge that charges it is dark. The channel closure the whole
    /// attribution rides is re-asserted beside it.
    /// </summary>
    [TestMethod]
    public void ThePerDriverPredCountersCloseOnTheChannelTotals()
    {
        (string Name, ReasoningModule Module)[] modules =
        [
            ("nominalBridgeChain", NominalBridgeChainModule()),
            ("nominalWedge", NomWedgeTowerModule(1)),
            ("chain", O1Module()),
            ("equalityMerge", EqualityMergeModule()),
            ("dataNarrowing", DataNarrowingModule()),
        ];

        StringBuilder report = new();
        report.AppendLine();
        List<string> mismatches = [];
        List<ContextSaturationStatistics> readings = [];
        foreach((string name, ReasoningModule module) in modules)
        {
            readings.Add(ContextSaturationModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals);
            report.AppendLine(PredDriverLine(name, readings[^1]));
        }

        readings.Add(SaturatedTotals(PremiseArrivalEngine(), TestContext.CancellationToken));
        report.AppendLine(PredDriverLine("premiseArrival", readings[^1]));
        readings.Add(SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken));
        report.AppendLine(PredDriverLine("crossSlotSwap", readings[^1]));
        readings.Add(SaturatedTotals(PreSeededCompletionEngine(), TestContext.CancellationToken));
        report.AppendLine(PredDriverLine("preSeededCompletion", readings[^1]));

        long targetOffers = 0;
        long premiseOffers = 0;
        long edgeOffers = 0;
        long driverDuplicates = 0;
        foreach(ContextSaturationStatistics totals in readings)
        {
            if(PredDriverOfferSum(totals) != totals.PredOffers)
            {
                mismatches.Add("the driver offer counters sum to " + PredDriverOfferSum(totals) + " but the channel offered " + totals.PredOffers + ".");
            }

            if(PredDriverDuplicateSum(totals) != totals.PredDuplicateHits)
            {
                mismatches.Add("the driver duplicate counters sum to " + PredDriverDuplicateSum(totals) + " but the channel absorbed " + totals.PredDuplicateHits + " exact duplicates.");
            }

            if(ChannelDuplicateSum(totals) != totals.DuplicateContainmentHits)
            {
                mismatches.Add("the channel duplicate counters sum to " + ChannelDuplicateSum(totals) + " but the run absorbed " + totals.DuplicateContainmentHits + " exact duplicates.");
            }

            if(totals.OriginClearReenqueues != 0)
            {
                mismatches.Add("an origin-merge re-enqueue was charged on a single-root fixture, where the choice tag has no source.");
            }

            targetOffers += totals.PredLandedTargetOffers;
            premiseOffers += totals.PredLandedPremiseOffers;
            edgeOffers += totals.PredNewEdgeOffers;
            driverDuplicates += PredDriverDuplicateSum(totals);
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
        Assert.IsGreaterThan(0L, targetOffers, "The landed-target driver is witnessed nonzero, so its column is covered by the partition rather than assumed. " + report);
        Assert.IsGreaterThan(0L, premiseOffers, "The landed-premise driver is witnessed nonzero on the purpose-built fixture. " + report);
        Assert.IsGreaterThan(0L, edgeOffers, "The new-edge driver is witnessed nonzero. " + report);
        Assert.IsGreaterThan(50L, driverDuplicates, "The fixture set absorbs dozens of Pred duplicates across the drivers, so the duplicate half of the partition is asserted over real traffic. " + report);
    }

    /// <summary>
    /// The odometer-run counter counts the invocations that REACH their combination
    /// cursor, never the attempts refused earlier for want of a live premise at some
    /// slot. The cross-slot-swap population runs strictly fewer odometers than it
    /// makes offers — one of its runs walks four combinations — while the module
    /// fixtures run exactly one offer per entered run despite the per-constant
    /// fan-out attempting far more invocations than that. An increment moved to the
    /// method's entry counts every refused attempt and breaks both readings.
    /// </summary>
    [TestMethod]
    public void ThePredOdometerRunsCountOnlyTheInvocationsThatReachTheirCursor()
    {
        ContextSaturationStatistics swap = SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics arrival = SaturatedTotals(PremiseArrivalEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics bridgeChain = ContextSaturationModuleReasoner.DecideModule(NominalBridgeChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics wedge = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(PredDriverLine("crossSlotSwap", swap));
        TestContext.WriteLine(PredDriverLine("premiseArrival", arrival));
        TestContext.WriteLine(PredDriverLine("nominalBridgeChain", bridgeChain));
        TestContext.WriteLine(PredDriverLine("nominalWedge", wedge));
        Assert.AreEqual(6L, swap.PredOdometerRuns, "The cross-slot-swap population's odometer runs.");
        Assert.AreEqual(9L, swap.PredOffers, "The cross-slot-swap population's offers stand above its runs, so at least one run walked several combinations.");
        Assert.IsGreaterThan(swap.PredOdometerRuns, swap.PredOffers, "A multi-combination run offers strictly more than once, which a per-invocation counter could not produce.");

        Assert.AreEqual(10L, arrival.PredOdometerRuns, "The premise-arrival population's odometer runs.");
        Assert.AreEqual(4L, bridgeChain.PredOdometerRuns, "The bridge-chain run's odometer runs — one per offer, with the per-constant fan-out's refused attempts charging nothing.");
        Assert.AreEqual(46L, wedge.PredOdometerRuns, "The nominal wedge's odometer runs, a different reading from every other fixture's.");
    }

    /// <summary>
    /// The within-run duplicate counter catches the CROSS-SLOT coincidence, the only
    /// way one odometer run can offer the same conclusion twice: two candidates of
    /// one slot can never share both body and residual without being one clause, so
    /// a single slot supplies no within-run duplicate. The population stages four
    /// candidates across two slots such that the fourth combination reassembles the
    /// second bit for bit while the two between them stay incomparable to it, so the
    /// run's last offer is an exact duplicate of a clause the same run inserted. The
    /// counter reads strictly below the channel's duplicate total, so a counter that
    /// simply mirrored that total would red here.
    /// </summary>
    [TestMethod]
    public void TheWithinRunPredDuplicateCountsTheCrossSlotSwapCoincidence()
    {
        ContextSaturationStatistics swap = SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(PredDriverLine("crossSlotSwap", swap));
        Assert.AreEqual(1L, swap.PredIntraRunDuplicateHits, "The four-combination run's last offer reassembles its second, so exactly one duplicate lands on a run that had already offered.");
        Assert.AreEqual(6L, swap.PredDuplicateHits, "The population's Pred duplicate absorptions.");
        Assert.IsGreaterThan(swap.PredIntraRunDuplicateHits, swap.PredDuplicateHits, "The within-run share stands strictly below the channel's duplicate total, so the counter is not a mirror of it.");
    }

    /// <summary>
    /// The within-run duplicate counter is CLEARED at every run's cursor: on
    /// populations whose duplicates are each the first charged offer of their own
    /// odometer run, the column reads zero while the channel absorbs dozens of
    /// duplicates. A flag left standing across runs would attribute every one of
    /// those cross-invocation absorptions to a run that never offered before them.
    /// </summary>
    [TestMethod]
    public void TheWithinRunPredDuplicateStaysZeroAcrossSingleOfferRuns()
    {
        ContextSaturationStatistics bridgeChain = ContextSaturationModuleReasoner.DecideModule(NominalBridgeChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics wedge = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics preSeeded = SaturatedTotals(PreSeededCompletionEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(PredDriverLine("nominalBridgeChain", bridgeChain));
        TestContext.WriteLine(PredDriverLine("nominalWedge", wedge));
        TestContext.WriteLine(PredDriverLine("preSeededCompletion", preSeeded));
        Assert.AreEqual(0L, bridgeChain.PredIntraRunDuplicateHits, "Every bridge-chain duplicate is the first charged offer of its own run.");
        Assert.AreEqual(1L, bridgeChain.PredDuplicateHits, "The bridge-chain run absorbs one Pred duplicate, and it is not a within-run one.");
        Assert.AreEqual(0L, wedge.PredIntraRunDuplicateHits, "Every nominal-wedge duplicate is the first charged offer of its own run.");
        Assert.AreEqual(32L, wedge.PredDuplicateHits, "The nominal wedge absorbs thirty-two Pred duplicates, none of them within a run.");
        Assert.AreEqual(0L, preSeeded.PredIntraRunDuplicateHits, "The pre-seeded population's single-combination runs cannot duplicate within themselves.");
        Assert.AreEqual(4L, preSeeded.PredDuplicateHits, "The pre-seeded population absorbs four Pred duplicates, none of them within a run.");
    }

    /// <summary>
    /// THE CLOSURE IDENTITY of the per-channel SUBSUMED attribution: on every
    /// fixture the fourteen channel-level subsumed counters SUM EXACTLY to
    /// <see cref="ContextSaturationStatistics.SubsumedContainmentHits"/>, the run's
    /// own count of the containment gate's subsumer absorptions. The Pred driver
    /// trio closes on the channel's own subsumed total, and the three driver
    /// LANDING counters close on
    /// <see cref="ContextSaturationStatistics.PredApplications"/>. A seam left
    /// uncounted, an increment charged twice, or a subsumer absorption attributed to
    /// a channel the offer did not come from breaks the first sum; a subsumed
    /// absorption charged onto a duplicate counter breaks the shipped duplicate
    /// closure asserted alongside it. The fixture set is witnessed absorbing
    /// subsumers, so the identity is never satisfied by zeros.
    /// </summary>
    [TestMethod]
    public void ThePerChannelSubsumedCountersCloseOnTheSubsumedTotal()
    {
        (string Name, ReasoningModule Module)[] modules =
        [
            ("nominalBridgeChain", NominalBridgeChainModule()),
            ("nominalWedge", NomWedgeTowerModule(1)),
            ("chain", O1Module()),
            ("equalityMerge", EqualityMergeModule()),
            ("dataNarrowing", DataNarrowingModule()),
            ("nominalFree", NominalFreeHornModule()),
        ];

        StringBuilder report = new();
        report.AppendLine();
        List<string> mismatches = [];
        List<ContextSaturationStatistics> readings = [];
        foreach((string name, ReasoningModule module) in modules)
        {
            readings.Add(ContextSaturationModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals);
            report.AppendLine(ChannelSubsumedLine(name, readings[^1]));
        }

        readings.Add(SaturatedTotals(PremiseArrivalEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("premiseArrival", readings[^1]));
        readings.Add(SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("crossSlotSwap", readings[^1]));
        readings.Add(SaturatedTotals(PreSeededCompletionEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("preSeededCompletion", readings[^1]));
        readings.Add(SaturatedTotals(MidSweepRegistrationEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("midSweep", readings[^1]));
        readings.Add(SaturatedTotals(JoinCrossResidualEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("joinCrossResidual", readings[^1]));
        readings.Add(SaturatedTotals(EqTwoTargetEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("eqTwoTarget", readings[^1]));
        readings.Add(SaturatedTotals(SuccSubsumptionEngine(), TestContext.CancellationToken));
        report.AppendLine(ChannelSubsumedLine("succSubsumption", readings[^1]));

        long witnessed = 0;
        foreach(ContextSaturationStatistics totals in readings)
        {
            if(ChannelSubsumedSum(totals) != totals.SubsumedContainmentHits)
            {
                mismatches.Add("the channel subsumed counters sum to " + ChannelSubsumedSum(totals) + " but the run absorbed " + totals.SubsumedContainmentHits + " subsumers.");
            }

            if(PredDriverSubsumedSum(totals) != totals.PredSubsumedHits)
            {
                mismatches.Add("the driver subsumed counters sum to " + PredDriverSubsumedSum(totals) + " but the channel absorbed " + totals.PredSubsumedHits + " subsumers.");
            }

            if(PredDriverLandingSum(totals) != totals.PredApplications)
            {
                mismatches.Add("the driver landing counters sum to " + PredDriverLandingSum(totals) + " but the channel landed " + totals.PredApplications + " conclusions.");
            }

            if(ChannelDuplicateSum(totals) != totals.DuplicateContainmentHits)
            {
                mismatches.Add("a subsumer absorption reached a duplicate counter: the channel duplicate counters sum to " + ChannelDuplicateSum(totals) + " against " + totals.DuplicateContainmentHits + " exact duplicates.");
            }

            witnessed += totals.SubsumedContainmentHits;
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
        Assert.IsGreaterThan(0L, witnessed, "The fixture set absorbs subsumers, so the closure is asserted over real traffic rather than over zeros. " + report);
    }

    /// <summary>
    /// The per-channel SUBSUMED counters pin their constructed absorptions at EXACT
    /// values, so a dropped arm reads low and a transposition with the channel's own
    /// duplicate arm moves both columns. Each pinned channel is read on a fixture
    /// whose traffic genuinely absorbs a strictly more general live clause through
    /// that channel; every other counter rides the closure row. The Core channel is
    /// deliberately absent from the nonzero floor: a Core seed is architecturally the
    /// FIRST insertion into a freshly created context, so nothing can predate it there
    /// to subsume it, and the floor's insert-channel slot is carried by Hyper instead.
    /// </summary>
    [TestMethod]
    public void ThePerChannelSubsumedCountersPinTheirConstructedAbsorptions()
    {
        ContextSaturationStatistics bridgeChain = ContextSaturationModuleReasoner.DecideModule(NominalBridgeChainModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics wedge = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics merge = ContextSaturationModuleReasoner.DecideModule(EqualityMergeModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics preSeeded = SaturatedTotals(PreSeededCompletionEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics swap = SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics joinCross = SaturatedTotals(JoinCrossResidualEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics eqTwoTarget = SaturatedTotals(EqTwoTargetEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics midSweepSubsumed = SaturatedTotals(MidSweepRegistrationEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics succSubsumption = SaturatedTotals(SuccSubsumptionEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelSubsumedLine("succSubsumption", succSubsumption));
        TestContext.WriteLine(ChannelSubsumedLine("midSweep", midSweepSubsumed));
        TestContext.WriteLine(ChannelSubsumedLine("nominalBridgeChain", bridgeChain));
        TestContext.WriteLine(ChannelSubsumedLine("nominalWedge", wedge));
        TestContext.WriteLine(ChannelSubsumedLine("equalityMerge", merge));
        TestContext.WriteLine(ChannelSubsumedLine("preSeededCompletion", preSeeded));
        TestContext.WriteLine(ChannelSubsumedLine("crossSlotSwap", swap));
        TestContext.WriteLine(ChannelSubsumedLine("joinCrossResidual", joinCross));
        TestContext.WriteLine(ChannelSubsumedLine("eqTwoTarget", eqTwoTarget));

        Assert.AreEqual(90L, wedge.SubsumedContainmentHits, "The nominal wedge's subsumer absorptions — the total the channel columns partition.");
        Assert.AreEqual(6L, wedge.HyperSubsumedHits, "The nominal wedge's Hyper subsumer absorptions — the insert-channel slot of the coverage floor.");
        Assert.AreEqual(4L, wedge.PredSubsumedHits, "The nominal wedge's Pred subsumer absorptions.");
        Assert.AreEqual(4L, wedge.PredLandedTargetSubsumedHits, "The nominal wedge's landed-target driver carries almost all of the channel's subsumer absorptions.");
        Assert.AreEqual(0L, wedge.PredLandedPremiseSubsumedHits, "The nominal wedge's landed-premise driver absorbs no subsumer, so a driver transposition moves this zero.");
        Assert.AreEqual(0L, wedge.PredNewEdgeSubsumedHits, "The nominal wedge's new-edge driver absorbs no subsumer — a value distinct from the landed-target driver's, so a transposition reds on some driver.");
        Assert.AreEqual(54L, wedge.EqSubsumedHits, "The nominal wedge's Eq subsumer absorptions.");
        Assert.AreEqual(8L, wedge.JoinSubsumedHits, "The nominal wedge's join subsumer absorptions.");
        Assert.AreEqual(7L, wedge.PushedArrivalSubsumedHits, "The nominal wedge's push-arrival subsumer absorptions.");
        Assert.AreEqual(11L, wedge.RootPredBroadcastSubsumedHits, "The nominal wedge's broadcast-origin subsumer absorptions — the r-Pred origin slot of the coverage floor.");
        Assert.AreEqual(0L, wedge.RootPredRegistrationSweepSubsumedHits, "The nominal wedge's registration-sweep origin absorbs no subsumer, so an origin transposition moves this zero.");
        Assert.AreEqual(0L, wedge.CoreSubsumedHits, "A Core seed is the first insertion into a freshly created context, so nothing can predate it there to subsume it.");
        Assert.AreEqual(5L, wedge.PredLandedTargetLandings, "The nominal wedge's landed-target driver's landings.");
        Assert.AreEqual(0L, wedge.PredLandedPremiseLandings, "The nominal wedge's landed-premise driver lands nothing.");
        Assert.AreEqual(2L, wedge.PredNewEdgeLandings, "The nominal wedge's new-edge driver's landings — a third distinct value, so a landing-switch transposition reds.");

        Assert.AreEqual(11L, bridgeChain.SubsumedContainmentHits, "The bridge-chain run's subsumer absorptions.");
        Assert.AreEqual(0L, bridgeChain.PredSubsumedHits, "The bridge-chain run's Pred subsumer absorptions — a different reading from the wedge's, so a fixture-blind misattribution cannot satisfy both.");
        Assert.AreEqual(0L, bridgeChain.PredLandedTargetSubsumedHits, "The bridge-chain run's Pred channel absorbs no subsumer at all, so its landed-target driver reads zero beside the wedge's four.");
        Assert.AreEqual(1L, bridgeChain.EqSubsumedHits, "The bridge-chain run's Eq subsumer absorption.");
        Assert.AreEqual(7L, bridgeChain.PushedArrivalSubsumedHits, "The bridge-chain run's push-arrival subsumer absorptions.");
        Assert.AreEqual(3L, bridgeChain.RootPredBroadcastSubsumedHits, "The bridge-chain run's broadcast-origin subsumer absorptions.");
        Assert.AreEqual(0L, bridgeChain.JoinSubsumedHits, "The bridge-chain run's join offers are absorbed as exact duplicates rather than as subsumers, so a transposed arm moves this zero and the shipped join duplicate pin together.");

        Assert.AreEqual(0L, merge.SubsumedContainmentHits, "The functional-merge run absorbs no subsumer at all, so every one of its channel subsumed columns must read zero.");
        Assert.AreEqual(0L, ChannelSubsumedSum(merge), "A channel counter charged unconditionally beside its correct arm would move this zero.");
        Assert.AreEqual(3L, merge.PredLandedTargetLandings, "The functional-merge run's Pred landings are all landed-target, so the landing switch is exercised on a run with no subsumed traffic at all.");

        Assert.AreEqual(1L, preSeeded.SubsumedContainmentHits, "The pre-seeded population's single subsumer absorption.");
        Assert.AreEqual(1L, preSeeded.PushedArrivalSubsumedHits, "The pre-seeded population's one subsumer absorption is a push-landing arrival's, not a Pred offer's.");
        Assert.AreEqual(0L, preSeeded.PredSubsumedHits, "The pre-seeded population's Pred offers are all absorbed as exact duplicates rather than as subsumers.");

        Assert.AreEqual(1L, swap.SubsumedContainmentHits, "The cross-slot-swap population's single subsumer absorption.");
        Assert.AreEqual(1L, swap.PushedArrivalSubsumedHits, "The cross-slot-swap population's one subsumer absorption is a push-landing arrival's.");
        Assert.AreEqual(3L, swap.PredLandedTargetLandings, "The cross-slot-swap population's Pred landings are all landed-target.");

        Assert.AreEqual(11L, midSweepSubsumed.SubsumedContainmentHits, "The mid-sweep run's subsumer absorptions.");
        Assert.AreEqual(9L, midSweepSubsumed.EqSubsumedHits, "The mid-sweep run's Eq subsumer absorptions — a third distinct Eq reading.");
        Assert.AreEqual(2L, midSweepSubsumed.RootPredBroadcastSubsumedHits, "The mid-sweep run's broadcast-origin subsumer absorptions.");

        Assert.AreEqual(3L, joinCross.SubsumedContainmentHits, "The cross-residual join population's subsumer absorptions.");
        Assert.AreEqual(2L, eqTwoTarget.SubsumedContainmentHits, "The two-target Eq population's subsumer absorptions.");
        Assert.AreEqual(0L, joinCross.JoinSubsumedHits, "The cross-residual join population's offers each reassemble a live clause exactly, so they are absorbed as duplicates rather than as subsumers.");

        Assert.AreEqual(1L, succSubsumption.SubsumedContainmentHits, "The Succ-subsumption population absorbs exactly one subsumer.");
        Assert.AreEqual(1L, succSubsumption.SuccSubsumedHits, "That one absorption is the hypothesis seed's, absorbed by the unconditional head the successor already carried — the Succ slot of the coverage floor, which no other fixture in the battery reaches.");
        Assert.AreEqual(0L, succSubsumption.SuccDuplicateHits, "The hypothesis seed is absorbed by a strictly more general clause rather than by an exact duplicate, so a transposed Succ arm moves both columns.");
    }

    /// <summary>
    /// The join OFFERING-RUN counter charges on a run's FIRST charged offer, never
    /// at the dispatcher's entry, and the within-run duplicate counter catches the
    /// cross-position coincidence one run can produce: the landed premise's two
    /// ground body positions each resolve against a partner whose residual gives
    /// back the landed premise itself, so a single ground-body dispatch offers the
    /// same clause twice and the second reads as an exact duplicate. The same
    /// population carries a genuinely OFFER-LESS dispatch — the landed premise's own
    /// head literal keys no ground body posting — so a run counter moved to the
    /// dispatcher's entry would read strictly higher here. The cross-run zero pin
    /// rides a population whose join duplicates each open their own run, where the
    /// within-run column must read zero while the channel absorbs several.
    /// </summary>
    [TestMethod]
    public void TheJoinOfferingRunsChargeOnTheirFirstOfferAndCountWithinRunDuplicates()
    {
        ContextSaturationStatistics joinCross = SaturatedTotals(JoinCrossResidualEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics midSweep = SaturatedTotals(MidSweepRegistrationEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(OfferingRunLine("joinCrossResidual", joinCross));
        TestContext.WriteLine(OfferingRunLine("midSweep", midSweep));

        Assert.AreEqual(10L, joinCross.JoinOffers, "The cross-residual population's join offers.");
        Assert.AreEqual(10L, joinCross.JoinDuplicateHits, "Every one of those offers reassembles a live clause exactly, so the whole set is absorbed at the exact-duplicate fast path.");
        Assert.AreEqual(5L, joinCross.JoinOfferingRuns, "Five dispatch runs charged an offer. The population enters the dispatcher far more often than that — the landed premise's own head literal keys no ground body posting — so a run charged at dispatcher entry reads strictly higher.");
        Assert.AreEqual(5L, joinCross.JoinIntraRunDuplicateHits, "Each offering run walks the landed premise's two body positions and its second offer duplicates its first, so every run contributes exactly one within-run duplicate.");
        Assert.AreEqual(joinCross.JoinOffers - joinCross.JoinOfferingRuns, joinCross.JoinIntraRunDuplicateHits, "Each run charges one offer before the flag is set and every later offer of the run is a duplicate, so the within-run count is exactly the offers past the first of each run.");

        Assert.AreEqual(5L, midSweep.JoinOffers, "The mid-sweep run's join offers.");
        Assert.AreEqual(3L, midSweep.JoinDuplicateHits, "The mid-sweep run absorbs three join duplicates.");
        Assert.AreEqual(4L, midSweep.JoinOfferingRuns, "The mid-sweep run's offering runs — a different reading from the cross-residual population's.");
        Assert.AreEqual(0L, midSweep.JoinIntraRunDuplicateHits, "Every mid-sweep join duplicate is the first charged offer of its own run, so a flag left standing across dispatches would attribute those cross-run absorptions to a run that never offered before them.");
    }

    /// <summary>
    /// The join sink canonicalises its assembled body and head BEFORE offering
    /// them: on the cross-residual population every offer of form (a) reassembles
    /// the landed premise itself out of a raw sequence that is unsorted in the body
    /// on one of the two governing dispatches and carries a repeated head disjunct
    /// on both, so the whole set is absorbed at the exact-duplicate fast path and
    /// nothing lands. A dropped BODY canonicalisation hands the gate a descending
    /// pair, whose merge-walk breaks at its first step, so the offer misses every
    /// container and lands as a spurious clause; a dropped HEAD canonicalisation
    /// hands it a repeated disjunct the stored single-literal head subsumes, so the
    /// absorption moves to the subsumer column. The four-way pin below reads both.
    /// </summary>
    [TestMethod]
    public void TheJoinSinkCanonicalisesItsConclusionBeforeOfferingIt()
    {
        ContextSaturationStatistics totals = SaturatedTotals(JoinCrossResidualEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("joinCrossResidual", totals));
        TestContext.WriteLine(ChannelSubsumedLine("joinCrossResidual", totals));
        Assert.AreEqual(10L, totals.JoinOffers, "The cross-residual population's join offers.");
        Assert.AreEqual(totals.JoinOffers, totals.JoinDuplicateHits, "Every offer canonicalises onto the landed premise itself, so the whole set is absorbed at the exact-duplicate fast path.");
        Assert.AreEqual(0L, totals.JoinSubsumedHits, "No join offer reaches the subsumer walk: a repeated head disjunct would be absorbed by the stored single-literal head instead, moving the absorption here.");
        Assert.AreEqual(0L, totals.JoinApplications, "No join offer lands: a descending body would miss every container and insert a spurious clause instead.");
        Assert.AreEqual(15, totals.ClausesDerived, "The inserted population carries no join conclusion.");
        Assert.AreEqual(10L, totals.DuplicateContainmentHits, "The run's exact-duplicate absorptions are exactly the ten join offers.");
    }

    /// <summary>
    /// The join BRIDGE sink canonicalises its assembled head before offering it:
    /// the abstract premise and the disjunctive bridge premise carry the SAME
    /// residual disjunct, so every form (b) offer assembles that literal twice and
    /// its canonical form is a pre-seeded live clause. A dropped canonicalisation
    /// hands the gate the repeated disjunct, which no longer hashes as the stored
    /// clause, and the pre-seed absorbs it as a strictly more general clause
    /// instead — the duplicate column empties into the subsumer column.
    /// </summary>
    [TestMethod]
    public void TheJoinBridgeSinkCanonicalisesItsConclusionBeforeOfferingIt()
    {
        ContextSaturationStatistics totals = SaturatedTotals(JoinBridgeDuplicateResidualEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("joinBridgeDuplicateResidual", totals));
        TestContext.WriteLine(ChannelSubsumedLine("joinBridgeDuplicateResidual", totals));
        Assert.AreEqual(5L, totals.JoinOffers, "The bridge population's join offers.");
        Assert.AreEqual(3L, totals.JoinDuplicateHits, "Three bridge offers canonicalise onto the pre-seeded conclusion exactly, so the fast path absorbs them.");
        Assert.AreEqual(2L, totals.JoinSubsumedHits, "The remaining two are absorbed by a strictly more general clause, a reading distinct from the duplicate column's.");
        Assert.AreEqual(0L, totals.JoinApplications, "No bridge offer lands.");
        Assert.AreEqual(19, totals.ClausesDerived, "The inserted population carries no bridge conclusion.");
        Assert.AreEqual(8L, totals.DuplicateContainmentHits, "The run's exact-duplicate absorptions, of which the three join duplicates are a share.");
    }

    /// <summary>
    /// THE EAGER LANDING: a join conclusion's landed event is routed to the eager
    /// queue, which drains ahead of the ordinary one, so the conclusion's own
    /// downstream lift runs BEFORE a bystander landed event that was already
    /// waiting. The two lifts share a head, and the join side's carries a body the
    /// bystander's does not, so the drain order decides which of them lands first:
    /// eager first inserts the conditional lift and lets the later unconditional one
    /// eliminate it; ordinary first inserts the unconditional lift and absorbs the
    /// conditional one as a subsumed arrival. The derived, eliminated, and subsumed
    /// columns read the order directly, with no dependence on timing.
    /// </summary>
    [TestMethod]
    public void TheJoinConclusionLandsOnTheEagerQueue()
    {
        ContextSaturationStatistics totals = SaturatedTotals(JoinEagerLandingEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("joinEagerLanding", totals));
        TestContext.WriteLine(ChannelSubsumedLine("joinEagerLanding", totals));
        Assert.AreEqual(1L, totals.JoinApplications, "The join conclusion genuinely lands, so its landed event is routed at all.");
        Assert.AreEqual(2L, totals.HyperApplications, "Both lifts land when the join conclusion is processed first; the ordinary order absorbs one of them instead.");
        Assert.AreEqual(1L, totals.HyperSubsumedHits, "Exactly one lift arrives after a strictly more general clause already stands.");
        Assert.AreEqual(2L, totals.SubsumedContainmentHits, "The run's subsumer absorptions, of which the single lift absorption is a share.");
        Assert.AreEqual(15, totals.ClausesDerived, "The inserted population under the eager order.");
        Assert.AreEqual(2, totals.ClausesEliminated, "The clauses backward subsumption removes under the eager order — the ordinary order removes one fewer and absorbs one more.");
    }

    /// <summary>
    /// The join sink folds its premise ids into the conclusion's origin tag: a
    /// choice-tagged premise drives the REAL join dispatch through a saturation of
    /// a nominal-jurisdiction population, and the landed conclusion carries the tag.
    /// A sink that offered an empty premise set would land the same conclusion
    /// choice-free, which is exactly the laundering the origin guard forbids.
    /// </summary>
    [TestMethod]
    public void TheJoinSinkInheritsATaintedPremisesChoiceTag()
    {
        ContextSymbolTable symbols = new();
        int ground = symbols.InternIndividual(Utf8Strings.From(Example + "ground"), IndividualOrigin.IriDenoted);
        int resolved = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int carried = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        DlTerm home = DlTerm.Individual(ground);
        ContextSaturationEngine engine = SyntheticRootEngine(symbols, [], []);
        Context root = engine.RedriveRootForIndividual(ground);
        int taggedId = engine.RedriveAddClause(root, DlClause.Create([], [DlLiteral.Concept(resolved, home)], RedriveOrigin), []);
        root.SetDerivedUnderChoice(taggedId);
        int groundBodyId = engine.RedriveAddClause(root, DlClause.Create([DlLiteral.Concept(resolved, home)], [DlLiteral.Concept(carried, home)], RedriveOrigin), []);
        int conclusionId = root.ClauseCount;

        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The two-premise population saturates to its fixpoint.");

        ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: false);
        Assert.AreNotEqual(-1, groundBodyId, "The ground-body premise lands, so the join dispatch has both premises.");
        Assert.AreEqual(1L, totals.JoinApplications, "The real join dispatch lands exactly one conclusion.");
        Assert.AreEqual(conclusionId + 1, root.ClauseCount, "That conclusion takes the next clause id in the root context.");
        Assert.AreEqual(0, root.At(conclusionId).BodyLength, "The conclusion is the body-empty resolvent of the two premises.");
        Assert.IsTrue(root.IsDerivedUnderChoice(conclusionId), "The join conclusion inherits the tagged premise's DerivedUnderChoice bit through the sink's premise-id fold.");
    }

    /// <summary>
    /// The Eq OFFERING-RUN counter charges on a run's FIRST charged offer and the
    /// within-run duplicate counter catches the coincidence one dispatch can
    /// produce: one acting equality rewrites TWO distinct role targets — a slot pair
    /// and a slot repeat — onto the identical conclusion, so the second offer of
    /// that dispatch reads as an exact duplicate. The cross-run zero pin rides the
    /// functional-merge module, whose Eq duplicates each open their own run.
    /// </summary>
    [TestMethod]
    public void TheEqOfferingRunsChargeOnTheirFirstOfferAndCountWithinRunDuplicates()
    {
        ContextSaturationStatistics eqTwoTarget = SaturatedTotals(EqTwoTargetEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics midSweep = SaturatedTotals(MidSweepRegistrationEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics merge = ContextSaturationModuleReasoner.DecideModule(EqualityMergeModule(), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(OfferingRunLine("eqTwoTarget", eqTwoTarget));
        TestContext.WriteLine(OfferingRunLine("midSweep", midSweep));
        TestContext.WriteLine(OfferingRunLine("equalityMerge", merge));

        Assert.AreEqual(8L, eqTwoTarget.EqOffers, "The two-target population's Eq offers.");
        Assert.AreEqual(8L, eqTwoTarget.EqDuplicateHits, "Every one of those offers is absorbed at the exact-duplicate fast path.");
        Assert.AreEqual(6L, eqTwoTarget.EqOfferingRuns, "Six Eq dispatch runs charged an offer; the population enters the dispatcher more often than that, so a run charged at dispatcher entry reads strictly higher.");
        Assert.AreEqual(2L, eqTwoTarget.EqIntraRunDuplicateHits, "Two runs rewrote both targets onto the identical conclusion, so each contributes one within-run duplicate.");

        Assert.AreEqual(14L, midSweep.EqOffers, "The mid-sweep run's Eq offers.");
        Assert.AreEqual(2L, midSweep.EqDuplicateHits, "The mid-sweep run absorbs two Eq duplicates.");
        Assert.AreEqual(13L, midSweep.EqOfferingRuns, "The mid-sweep run's Eq offering runs.");
        Assert.AreEqual(0L, midSweep.EqIntraRunDuplicateHits, "Every mid-sweep Eq duplicate is the first charged offer of its own run, so a flag left standing across dispatches would attribute those cross-run absorptions to a run that never offered before them.");

        Assert.AreEqual(4L, merge.EqOffers, "The functional-merge run's Eq offers.");
        Assert.AreEqual(2L, merge.EqOfferingRuns, "The functional-merge run's Eq offering runs — a third distinct reading.");
        Assert.AreEqual(1L, merge.EqIntraRunDuplicateHits, "One functional-merge run rewrites twice onto one conclusion.");
        Assert.AreEqual(0L, merge.JoinOfferingRuns, "The functional-merge module fires no join, so its join run column stays dark beside a live Eq one.");
    }

    /// <summary>
    /// The Eq sink canonicalises its assembled body and head BEFORE offering them:
    /// the equality premise and the rewrite target carry overlapping but mutually
    /// incomparable bodies, so the union is DESCENDING at one step and repeats a
    /// guard, and the target's rewritten disjunct equals the residual it carries, so
    /// the head repeats too. The canonical form is a live clause, so the offer is
    /// absorbed at the exact-duplicate fast path and lands nothing. A dropped BODY
    /// canonicalisation hands the gate a descending sequence whose merge-walk breaks,
    /// so the offer misses every container and lands; a dropped HEAD canonicalisation
    /// hands it a repeated disjunct the live clause subsumes, moving the absorption to
    /// the subsumer column. Spans captured before the in-place canonicalisation read a
    /// stale, still duplicate-bearing tail in BOTH buffers and read the same subsumer.
    /// </summary>
    [TestMethod]
    public void TheEqSinkCanonicalisesItsConclusionBeforeOfferingIt()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewRedriveContext();
        DlLiteral actingEquality = DlLiteral.Equality(EqUnionSource, EqUnionReplacement);
        DlLiteral actingTarget = DlLiteral.Concept(EqUnionRewriteAtom, EqUnionSource);
        DlLiteral carriedResidual = DlLiteral.Concept(EqUnionRewriteAtom, EqUnionReplacement);
        int equalityId = context.Insert(DlClause.Create([EqUnionGuard(RedriveSecondAtom), EqUnionGuard(RedriveThirdAtom)], [actingEquality], RedriveOrigin), isPredEligible: false, decidedUnderNoChoice: true, [0]);
        int targetId = context.Insert(DlClause.Create([EqUnionGuard(RedriveFirstAtom), EqUnionGuard(RedriveSecondAtom)], [carriedResidual, actingTarget], RedriveOrigin), isPredEligible: false, decidedUnderNoChoice: true, [1]);
        context.Insert(DlClause.Create([EqUnionGuard(RedriveFirstAtom), EqUnionGuard(RedriveSecondAtom), EqUnionGuard(RedriveThirdAtom)], [carriedResidual], RedriveOrigin), isPredEligible: false, decidedUnderNoChoice: true, [0]);

        int landed = engine.RedriveApplyEq(context, equalityId, targetId, actingEquality, actingTarget, EqUnionSource, EqUnionReplacement);

        ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: false);
        TestContext.WriteLine(ChannelCounterLine("eqDuplicateUnion", totals));
        TestContext.WriteLine(ChannelSubsumedLine("eqDuplicateUnion", totals));
        Assert.AreEqual(-1, landed, "The offer is absorbed, so the population keeps the three clauses it started with.");
        Assert.AreEqual(3, context.ClauseCount, "Nothing was inserted beside the three seeded premises.");
        Assert.AreEqual(1L, totals.EqOffers, "The firing charges exactly one Eq offer.");
        Assert.AreEqual(totals.EqOffers, totals.EqDuplicateHits, "The offer canonicalises onto the seeded conclusion exactly, so the fast path absorbs it.");
        Assert.AreEqual(0L, totals.EqSubsumedHits, "The offer never reaches the subsumer walk: a repeated body or head literal would be absorbed by the seed as a strictly more general clause instead.");
        Assert.AreEqual(0L, totals.EqApplications, "No Eq offer lands: a descending body would miss every container and insert a spurious clause instead.");
        Assert.AreEqual(1, totals.ClausesDerived, "The only derived clause is the trivial context's own creation seed.");
        Assert.AreEqual(1L, totals.DuplicateContainmentHits, "The run's single exact-duplicate absorption is the Eq offer's.");
        Assert.AreEqual(0L, totals.SubsumedContainmentHits, "No subsumer absorption ran at all.");
        Assert.AreEqual(0L, totals.JoinOffers, "The redriven firing offers through the Eq sink alone, so the join channel stays dark beside it.");
        Assert.AreEqual(0L, totals.FactorOffers, "The Factor channel stays dark.");
        Assert.AreEqual(0L, totals.PredOffers, "The Pred channel stays dark.");
        Assert.AreEqual(0L, totals.SuccOffers, "The Succ channel stays dark.");
    }

    /// <summary>
    /// The broadcast-provenance recognizer classifies EVERY charged Pred offer it
    /// observes into exactly one of its four classes, so the class counts partition
    /// the run's own Pred offer total on every fixture — a hook that stopped firing
    /// reads zero against a nonzero offer count. The recognizer is record-only: the
    /// armed run's production counters are identical to the unarmed run's, which the
    /// row asserts column by column. The broadcast population column is pinned on the
    /// same fixtures, so a population axis that stopped growing is visible beside the
    /// classification.
    /// </summary>
    /// <remarks>
    /// Across every fixture reachable here the match count is ZERO, and that is a
    /// structural reading rather than an accident: a population carrying a broadcast
    /// stratum runs Pred from the ROOT context, whose clauses are the root facts
    /// themselves and never the broadcast images built from them, while a population
    /// whose Pred runs from an ordinary context carries no nominal jurisdiction and
    /// therefore no broadcast stratum at all. The recognizer's positive classes are
    /// pinned at the seam instead, on hand-built images and a real context
    /// resolution, by the catch-up row.
    /// </remarks>
    [TestMethod]
    public void TheBroadcastProvenanceRecognizerClassifiesEveryPredOfferItObserves()
    {
        ContextSaturationEngine armedSwap = CrossSlotSwapEngine();
        RootBroadcastProvenanceRecognizer swapRecognizer = ArmBroadcastRecognizer(armedSwap);
        ContextSaturationStatistics armedSwapTotals = SaturatedTotals(armedSwap, TestContext.CancellationToken);
        ContextSaturationStatistics darkSwapTotals = SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken);

        ContextSaturationEngine armedArrival = PremiseArrivalEngine();
        RootBroadcastProvenanceRecognizer arrivalRecognizer = ArmBroadcastRecognizer(armedArrival);
        ContextSaturationStatistics armedArrivalTotals = SaturatedTotals(armedArrival, TestContext.CancellationToken);

        ContextSaturationEngine armedMidSweep = MidSweepRegistrationEngine();
        RootBroadcastProvenanceRecognizer midSweepRecognizer = ArmBroadcastRecognizer(armedMidSweep);
        ContextSaturationStatistics armedMidSweepTotals = SaturatedTotals(armedMidSweep, TestContext.CancellationToken);

        (string Name, ReasoningModule Module)[] modules =
        [
            ("nominalBridgeChain", NominalBridgeChainModule()),
            ("nominalWedge", NomWedgeTowerModule(1)),
            ("chain", O1Module()),
            ("selfChain", S10Module()),
            ("equalityMerge", EqualityMergeModule()),
            ("dataNarrowing", DataNarrowingModule()),
            ("nominalFree", NominalFreeHornModule()),
            ("nominalOrdinaryChain", NominalOrdinaryChainModule()),
        ];

        StringBuilder report = new();
        report.AppendLine();
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module) in modules)
        {
            BroadcastRecognizerArmer armer = new();
            ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningBudget.Unbounded, armer.Arm, TestContext.CancellationToken).Statistics.ContextTotals;
            RootBroadcastProvenanceRecognizer deciding = armer.Armed[^1];
            report.AppendLine(RecognizerLine(name, deciding) + " rounds=" + armer.Armed.Count + " broadcast=" + totals.RootBroadcastClauseCount + " predOffers=" + totals.PredOffers);
            long classified = deciding.AllBroadcastOffers + deciding.MixedOffers + deciding.NonBroadcastOffers + deciding.EmptyPremiseOffers;
            if(armer.Armed.Count == 1 && classified != totals.PredOffers)
            {
                mismatches.Add(name + ": the recognizer classified " + classified + " offers against the run's " + totals.PredOffers + ".");
            }

            if(deciding.PremisesMatched > deciding.PremisesProbed)
            {
                mismatches.Add(name + ": more premises matched than were probed.");
            }

            if(name == "nominalWedge")
            {
                Assert.AreEqual(14L, deciding.NonBroadcastOffers, "The nominal wedge's premise-bearing Pred offers.");
                Assert.AreEqual(2L, deciding.NonBroadcastDuplicateHits, "Two of the wedge's premise-bearing offers are absorbed as exact duplicates.");
                Assert.AreEqual(32L, deciding.EmptyPremiseOffers, "Most of the wedge's Pred offers are the zero-slot degenerate run's collapse propagation.");
                Assert.AreEqual(30L, deciding.EmptyPremiseDuplicateHits, "The wedge's empty-premise duplicate cross-tab.");
                Assert.AreEqual(23L, deciding.PremisesProbed, "The wedge's probed premise slots.");
                Assert.AreEqual(13, totals.RootBroadcastClauseCount, "The wedge's broadcast population — nonzero beside a zero match count, so the reading is a genuine provenance answer rather than an empty reference set.");
            }
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());

        TestContext.WriteLine(RecognizerLine("crossSlotSwap", swapRecognizer));
        TestContext.WriteLine(RecognizerLine("premiseArrival", arrivalRecognizer));
        TestContext.WriteLine(RecognizerLine("midSweep", midSweepRecognizer));
        TestContext.WriteLine(ChannelCounterLine("crossSlotSwapArmed", armedSwapTotals));
        TestContext.WriteLine("broadcastCounts: crossSlotSwap=" + armedSwapTotals.RootBroadcastClauseCount + " premiseArrival=" + armedArrivalTotals.RootBroadcastClauseCount + " midSweep=" + armedMidSweepTotals.RootBroadcastClauseCount);

        long swapClassified = swapRecognizer.AllBroadcastOffers + swapRecognizer.MixedOffers + swapRecognizer.NonBroadcastOffers + swapRecognizer.EmptyPremiseOffers;
        long arrivalClassified = arrivalRecognizer.AllBroadcastOffers + arrivalRecognizer.MixedOffers + arrivalRecognizer.NonBroadcastOffers + arrivalRecognizer.EmptyPremiseOffers;
        long midSweepClassified = midSweepRecognizer.AllBroadcastOffers + midSweepRecognizer.MixedOffers + midSweepRecognizer.NonBroadcastOffers + midSweepRecognizer.EmptyPremiseOffers;

        Assert.AreEqual(armedSwapTotals.PredOffers, swapClassified, "Every charged Pred offer of the cross-slot-swap run reaches the hook and lands in exactly one class.");
        Assert.AreEqual(armedArrivalTotals.PredOffers, arrivalClassified, "Every charged Pred offer of the premise-arrival run reaches the hook and lands in exactly one class.");
        Assert.AreEqual(armedMidSweepTotals.PredOffers, midSweepClassified, "Every charged Pred offer of the mid-sweep run reaches the hook and lands in exactly one class.");
        Assert.IsGreaterThan(0L, swapClassified, "The cross-slot-swap run offers Pred conclusions, so the hook is exercised rather than silently dark.");
        Assert.IsGreaterThanOrEqualTo(swapRecognizer.PremisesMatched, swapRecognizer.PremisesProbed, "Every matched premise was probed first.");

        Assert.AreEqual(darkSwapTotals.PredOffers, armedSwapTotals.PredOffers, "Arming the recognizer moves no production counter: the Pred offers are identical.");
        Assert.AreEqual(darkSwapTotals.PredDuplicateHits, armedSwapTotals.PredDuplicateHits, "Arming the recognizer moves no production counter: the Pred duplicates are identical.");
        Assert.AreEqual(darkSwapTotals.InferenceAttempts, armedSwapTotals.InferenceAttempts, "Arming the recognizer spends no budget: the attempt counts are identical.");
        Assert.AreEqual(darkSwapTotals.ClausesDerived, armedSwapTotals.ClausesDerived, "Arming the recognizer derives nothing: the inserted populations are identical.");
        Assert.AreEqual(darkSwapTotals.SubsumedContainmentHits, armedSwapTotals.SubsumedContainmentHits, "Arming the recognizer changes no gate outcome.");

        Assert.AreEqual(4L, swapRecognizer.NonBroadcastOffers, "The cross-slot-swap run's premise-bearing Pred offers draw every premise from the root context, whose clauses are the root facts themselves rather than broadcast images.");
        Assert.AreEqual(1L, swapRecognizer.NonBroadcastDuplicateHits, "One of those offers is absorbed as an exact duplicate, so the class's duplicate cross-tab is exercised.");
        Assert.AreEqual(5L, swapRecognizer.EmptyPremiseOffers, "The remaining offers carry no premise — the zero-slot degenerate run's collapse propagation.");
        Assert.AreEqual(5L, swapRecognizer.EmptyPremiseDuplicateHits, "Every one of those empty-premise offers is absorbed as an exact duplicate.");
        Assert.AreEqual(8L, swapRecognizer.PremisesProbed, "The four premise-bearing offers carry two premises each.");
        Assert.AreEqual(0L, swapRecognizer.PremisesMatched, "No premise of this population is a broadcast image, so the classification is the negative one throughout.");
        Assert.AreEqual(0L, swapRecognizer.AllBroadcastOffers, "A classification transposed with the non-broadcast class would move this zero.");
        Assert.AreEqual(0L, swapRecognizer.MixedOffers, "No combination mixes the two provenances here.");

        Assert.AreEqual(4L, arrivalRecognizer.NonBroadcastOffers, "The premise-arrival run's premise-bearing Pred offers.");
        Assert.AreEqual(6L, arrivalRecognizer.EmptyPremiseOffers, "The premise-arrival run's empty-premise offers — a different split from the cross-slot-swap run's.");
        Assert.AreEqual(7L, arrivalRecognizer.PremisesProbed, "The premise-arrival run's probed premise slots.");
        Assert.AreEqual(0L, midSweepRecognizer.PremisesProbed, "The mid-sweep run offers no Pred conclusion at all, so its recognizer stays silent rather than reading another run's traffic.");

        Assert.AreEqual(5, armedSwapTotals.RootBroadcastClauseCount, "The cross-slot-swap population's broadcast image count.");
        Assert.AreEqual(6, armedArrivalTotals.RootBroadcastClauseCount, "The premise-arrival population's broadcast image count.");
        Assert.AreEqual(10, armedMidSweepTotals.RootBroadcastClauseCount, "The mid-sweep population's broadcast image count — a third distinct reading, so a column fed from another population's list reds here.");
    }

    /// <summary>
    /// The recognizer's reference set CATCHES UP to the broadcast image list at
    /// every observation, so an image appended AFTER an earlier observation is
    /// recognised on the next one. The view is driven by hand across two
    /// observations of the same two-premise combination: the first runs while only
    /// one of the two premises is a broadcast image and must read MIXED, the second
    /// runs after the other premise joined the list and must read ALL-BROADCAST. A
    /// recognizer that folded the list once at construction — where the list is
    /// empty — would read both observations as NON-BROADCAST with nothing matched,
    /// and a transposed classification would swap the two class columns.
    /// </summary>
    [TestMethod]
    public void TheBroadcastProvenanceRecognizerCatchesUpToImagesAddedAfterItsLastObservation()
    {
        List<DlClause> images = [];
        RootBroadcastProvenanceRecognizer recognizer = new(images);
        Context context = new(0, [], isRoot: false, -1, new HashSet<int>());
        DlClause early = DlClause.Create([], [DlLiteral.Concept(5, DlTerm.Central)], -1);
        DlClause late = DlClause.Create([], [DlLiteral.Concept(7, DlTerm.Central)], -1);
        int earlyId = context.Insert(early, isPredEligible: false, decidedUnderNoChoice: true, [0]);
        int lateId = context.Insert(late, isPredEligible: false, decidedUnderNoChoice: true, [0]);

        images.Add(early);
        recognizer.Observe(context, [earlyId, lateId], ClauseOfferOutcome.Inserted);

        Assert.AreEqual(1L, recognizer.MixedOffers, "One of the two premises is a broadcast image at the first observation, so the combination reads MIXED.");
        Assert.AreEqual(0L, recognizer.AllBroadcastOffers, "The second premise is not yet an image, so the first observation is not all-broadcast.");
        Assert.AreEqual(0L, recognizer.NonBroadcastOffers, "A recognizer that never folded the growing list would read this observation as non-broadcast.");
        Assert.AreEqual(2L, recognizer.PremisesProbed, "Both premises of the combination were probed.");
        Assert.AreEqual(1L, recognizer.PremisesMatched, "Exactly the image already in the list matched.");

        images.Add(late);
        recognizer.Observe(context, [earlyId, lateId], ClauseOfferOutcome.ExactDuplicate);

        TestContext.WriteLine(RecognizerLine("grownList", recognizer));
        Assert.AreEqual(1L, recognizer.AllBroadcastOffers, "The late image joined the list between the two observations, so the catch-up makes the second combination all-broadcast.");
        Assert.AreEqual(1L, recognizer.AllBroadcastDuplicateHits, "The second observation's exact-duplicate outcome is charged to its own class.");
        Assert.AreEqual(1L, recognizer.MixedOffers, "The first observation's class is not re-charged by the second.");
        Assert.AreEqual(0L, recognizer.NonBroadcastOffers, "Neither observation is non-broadcast.");
        Assert.AreEqual(0L, recognizer.EmptyPremiseOffers, "Both observations carry premises.");
        Assert.AreEqual(4L, recognizer.PremisesProbed, "The second observation probes both premises again.");
        Assert.AreEqual(3L, recognizer.PremisesMatched, "One premise matched on the first observation and both on the second.");
    }

    /// <summary>
    /// The cautious-registry fill fraction reads the REGISTRY against a ceiling the
    /// frozen signature fixes: the candidate-core set is the DISTINCT filler atoms of
    /// the unique-filler map, so a module giving three function symbols a unique
    /// filler of which two are the SAME concept has a ceiling of two, not three, and
    /// an ambiguous function contributes none. On a module whose saturation reaches no
    /// successor expansion the registered count stands strictly below the ceiling, so
    /// a probe counting registry MISSES as hits reads at the ceiling instead.
    /// </summary>
    [TestMethod]
    public void TheCautiousCoreFillReadsTheRegistryAgainstTheSignatureCeiling()
    {
        ContextSaturationStatistics census = SaturatedTotals(FillerCensusEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics arrival = SaturatedTotals(PremiseArrivalEngine(), TestContext.CancellationToken);
        ContextSaturationStatistics swap = SaturatedTotals(CrossSlotSwapEngine(), TestContext.CancellationToken);

        TestContext.WriteLine("fillerCensus: ceiling=" + census.CautiousCoreCeiling + " registered=" + census.CautiousCoresRegistered + " contexts=" + census.ContextsCreated);
        TestContext.WriteLine("premiseArrival: ceiling=" + arrival.CautiousCoreCeiling + " registered=" + arrival.CautiousCoresRegistered + " contexts=" + arrival.ContextsCreated);
        TestContext.WriteLine("crossSlotSwap: ceiling=" + swap.CautiousCoreCeiling + " registered=" + swap.CautiousCoresRegistered + " contexts=" + swap.ContextsCreated);

        Assert.AreEqual(2, census.CautiousCoreCeiling, "Three functions carry a unique filler and two of those fillers are the SAME concept, so the deduplicated candidate-core set holds two atoms; the ambiguous function contributes none, and a ceiling counted without deduplication reads three.");
        Assert.AreEqual(0, census.CautiousCoresRegistered, "The census module's saturation reaches no successor expansion, so neither candidate core is registered and a probe counting registry MISSES as hits reads the ceiling instead.");
        Assert.IsLessThan(census.CautiousCoreCeiling, census.CautiousCoresRegistered, "The registered count stands strictly below the ceiling on this module.");

        Assert.AreEqual(1, arrival.CautiousCoreCeiling, "The premise-arrival module gives its one function a unique filler, so its candidate-core set holds a single atom.");
        Assert.AreEqual(0, arrival.CautiousCoresRegistered, "The premise-arrival saturation resolves its successor without the by-core strategy, so the filler core stays unregistered at a different ceiling from the census module's.");
        Assert.AreEqual(0, swap.CautiousCoreCeiling, "The cross-slot-swap module gives no function a concept filler at all, so its ceiling is zero and its registered count can only be zero with it.");
        Assert.AreEqual(0, swap.CautiousCoresRegistered, "A registered count above a zero ceiling would be an impossible reading.");
    }

    /// <summary>
    /// Builds the engine of the cross-slot swap module WITHOUT saturating it: one
    /// constant, one Skolem function, and four root facts whose selected heads fill
    /// the two successor-side body positions of the clash target two candidates
    /// each, so the single odometer run over that target assembles four
    /// combinations of which the LAST repeats the second bit for bit.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine CrossSlotSwapEngine()
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int firstFiller = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int secondFiller = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int firstCondition = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int secondCondition = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));
        int residual = symbols.AtomOf(Utf8Strings.From(Example + "Hazel"));
        DlTerm successorTerm = DlTerm.FunctionOf(function, owner);
        DlTerm home = DlTerm.Individual(owner);
        DlClause clash = DlClause.Create([DlLiteral.Concept(firstFiller, DlTerm.Central), DlLiteral.Concept(secondFiller, DlTerm.Central)], [], 0);
        DlClause firstNarrow = DlClause.Create([DlLiteral.Concept(firstCondition, home)], [DlLiteral.Concept(firstFiller, successorTerm)], 0);
        DlClause firstWide = DlClause.Create([], [DlLiteral.Concept(firstFiller, successorTerm), DlLiteral.Concept(residual, home)], 0);
        DlClause secondNarrow = DlClause.Create([DlLiteral.Concept(secondCondition, home)], [DlLiteral.Concept(secondFiller, successorTerm)], 0);
        DlClause secondWide = DlClause.Create([DlLiteral.Concept(firstCondition, home)], [DlLiteral.Concept(secondFiller, successorTerm), DlLiteral.Concept(residual, home)], 0);

        return SyntheticRootEngine(symbols, [clash], [firstNarrow, firstWide, secondNarrow, secondWide]);
    }

    /// <summary>
    /// Builds the engine of the pre-seeded-completion module WITHOUT saturating it:
    /// two root premises sharing a body atom AND a residual disjunct fill the two
    /// successor-side body positions of the clash target one candidate each, and a
    /// third root fact IS the canonical form of the completion they assemble, so
    /// the odometer's single offer must read as an exact duplicate.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine PreSeededCompletionEngine()
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int firstFiller = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int secondFiller = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int shared = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int firstOnly = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));
        int secondOnly = symbols.AtomOf(Utf8Strings.From(Example + "Fir"));
        int residual = symbols.AtomOf(Utf8Strings.From(Example + "Hazel"));
        DlTerm successorTerm = DlTerm.FunctionOf(function, owner);
        DlTerm home = DlTerm.Individual(owner);
        DlClause clash = DlClause.Create([DlLiteral.Concept(firstFiller, DlTerm.Central), DlLiteral.Concept(secondFiller, DlTerm.Central)], [], 0);
        DlClause firstPremise = DlClause.Create([DlLiteral.Concept(shared, home), DlLiteral.Concept(firstOnly, home)], [DlLiteral.Concept(firstFiller, successorTerm), DlLiteral.Concept(residual, home)], 0);
        DlClause secondPremise = DlClause.Create([DlLiteral.Concept(shared, home), DlLiteral.Concept(secondOnly, home)], [DlLiteral.Concept(secondFiller, successorTerm), DlLiteral.Concept(residual, home)], 0);
        DlClause completion = DlClause.Create([DlLiteral.Concept(shared, home), DlLiteral.Concept(firstOnly, home), DlLiteral.Concept(secondOnly, home)], [DlLiteral.Concept(residual, home)], 0);

        return SyntheticRootEngine(symbols, [clash], [firstPremise, secondPremise, completion]);
    }

    /// <summary>
    /// Builds the engine of the premise-arrival module WITHOUT saturating it: a
    /// third filler's own clash target completes first and lands a conclusion in
    /// the root whose head fires the ontology lift, so a SECOND candidate for the
    /// two-slot target's first body position arrives in the predecessor after that
    /// target and the edge already exist — the landed-premise driver's staging.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine PremiseArrivalEngine()
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int firstFiller = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int secondFiller = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int thirdFiller = symbols.AtomOf(Utf8Strings.From(Example + "Deodar"));
        int lifted = symbols.AtomOf(Utf8Strings.From(Example + "Hazel"));
        int firstCondition = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int secondCondition = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));
        int thirdCondition = symbols.AtomOf(Utf8Strings.From(Example + "Fir"));
        DlTerm successorTerm = DlTerm.FunctionOf(function, owner);
        DlTerm home = DlTerm.Individual(owner);
        DlClause clash = DlClause.Create([DlLiteral.Concept(firstFiller, DlTerm.Central), DlLiteral.Concept(secondFiller, DlTerm.Central)], [], 0);
        DlClause thirdClash = DlClause.Create([DlLiteral.Concept(thirdFiller, DlTerm.Central)], [], 0);
        DlClause lift = DlClause.Create([DlLiteral.Concept(lifted, DlTerm.Central)], [DlLiteral.Concept(firstFiller, DlTerm.Function(function))], 0);
        DlClause firstPremise = DlClause.Create([DlLiteral.Concept(firstCondition, home)], [DlLiteral.Concept(firstFiller, successorTerm)], 0);
        DlClause secondPremise = DlClause.Create([DlLiteral.Concept(secondCondition, home)], [DlLiteral.Concept(secondFiller, successorTerm)], 0);
        DlClause thirdPremise = DlClause.Create([DlLiteral.Concept(thirdCondition, home)], [DlLiteral.Concept(thirdFiller, successorTerm), DlLiteral.Concept(lifted, home)], 0);

        return SyntheticRootEngine(symbols, [clash, thirdClash, lift], [firstPremise, secondPremise, thirdPremise]);
    }

    /// <summary>
    /// Builds the engine of the cross-residual join module WITHOUT saturating it:
    /// one landed premise carries TWO ground body literals and a ground head, and
    /// two partner clauses each hold one of those ground literals maximal in a head
    /// that repeats the landed premise's own head disjunct. One
    /// <c>JoinFromGroundBody</c> run walks both body positions and both offers
    /// reassemble the landed premise itself, so the second is an exact duplicate
    /// inside a single run. The landed premise's own head literal keys no ground
    /// body posting anywhere in the population, so its maximal-head dispatch is a
    /// genuinely OFFER-LESS run — the shape a run counter charged at dispatcher
    /// entry would count and a lazily charged one must not.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine JoinCrossResidualEngine()
    {
        ContextSymbolTable symbols = new();
        int firstOwner = symbols.InternIndividual(Utf8Strings.From(Example + "first"), IndividualOrigin.IriDenoted);
        int secondOwner = symbols.InternIndividual(Utf8Strings.From(Example + "second"), IndividualOrigin.IriDenoted);
        int carrier = symbols.InternIndividual(Utf8Strings.From(Example + "carrier"), IndividualOrigin.IriDenoted);
        int shared = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int firstGround = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int secondGround = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        DlLiteral firstLiteral = DlLiteral.Concept(firstGround, DlTerm.Individual(firstOwner));
        DlLiteral secondLiteral = DlLiteral.Concept(secondGround, DlTerm.Individual(secondOwner));
        DlLiteral sharedHead = DlLiteral.Concept(shared, DlTerm.Individual(carrier));
        DlClause landed = DlClause.Create([firstLiteral, secondLiteral], [sharedHead], 0);
        DlClause firstPartner = DlClause.Create([firstLiteral], [firstLiteral, sharedHead], 0);
        DlClause secondPartner = DlClause.Create([secondLiteral], [secondLiteral, sharedHead], 0);

        return SyntheticRootEngine(symbols, [], [landed, firstPartner, secondPartner]);
    }

    /// <summary>
    /// Builds the engine of the two-target Eq module WITHOUT saturating it: one
    /// acting equality over two individuals and TWO rewrite targets whose role
    /// atoms rewrite to the identical conclusion under it — a role slot pair and a
    /// role slot repeat — so one dispatch over the equality's targets offers the
    /// same conclusion twice and the second is an exact duplicate inside a single
    /// run.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine EqTwoTargetEngine()
    {
        ContextSymbolTable symbols = new();
        int source = symbols.InternIndividual(Utf8Strings.From(Example + "source"), IndividualOrigin.IriDenoted);
        int replacement = symbols.InternIndividual(Utf8Strings.From(Example + "replacement"), IndividualOrigin.IriDenoted);
        int role = symbols.RoleOf(Utf8Strings.From(Example + "r")).Value;
        DlTerm sourceTerm = DlTerm.Individual(source);
        DlTerm replacementTerm = DlTerm.Individual(replacement);
        DlClause equality = DlClause.Create([], [DlLiteral.Equality(sourceTerm, replacementTerm)], 0);
        DlClause mixedTarget = DlClause.Create([], [DlLiteral.Role(role, sourceTerm, replacementTerm)], 0);
        DlClause repeatedTarget = DlClause.Create([], [DlLiteral.Role(role, sourceTerm, sourceTerm)], 0);

        return SyntheticRootEngine(symbols, [], [equality, mixedTarget, repeatedTarget]);
    }

    /// <summary>
    /// Builds the engine of the duplicate-residual join bridge module WITHOUT
    /// saturating it: the abstract premise and the disjunctive bridge premise carry
    /// the SAME residual equality disjunct, so the bridge conclusion's raw head
    /// holds that literal twice, and the ground premise carries a second body
    /// literal so the conclusion keeps a body of its own and subsumes neither
    /// empty-body premise. A fourth root fact seeds exactly the conclusion's
    /// canonical form, so a canonicalised offer reads as an exact duplicate while a
    /// duplicate-bearing one reads the seed as a subsumer.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine JoinBridgeDuplicateResidualEngine()
    {
        ContextSymbolTable symbols = new();
        int registrant = symbols.InternIndividual(Utf8Strings.From(Example + "reg"), IndividualOrigin.IriDenoted);
        int swept = symbols.InternIndividual(Utf8Strings.From(Example + "swept"), IndividualOrigin.IriDenoted);
        int marker = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int carried = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        DlLiteral residual = DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(registrant));
        DlLiteral carriedBody = DlLiteral.Concept(carried, DlTerm.Individual(swept));
        DlClause preSeed = DlClause.Create([carriedBody], [residual], 0);
        DlClause abstractPremise = DlClause.Create([], [DlLiteral.Concept(marker, DlTerm.Context), residual], 0);
        DlClause bridgePremise = DlClause.Create([], [residual, DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(swept))], 0);
        DlClause groundPremise = DlClause.Create([DlLiteral.Concept(marker, DlTerm.Individual(swept)), carriedBody], [], 0);

        return SyntheticRootEngine(symbols, [], [preSeed, abstractPremise, bridgePremise, groundPremise]);
    }

    /// <summary>The concept-atom id the duplicate-union Eq fixture's rewrite target and its carried residual share.</summary>
    private const int EqUnionRewriteAtom = 8;

    /// <summary>The rewrite source term of the duplicate-union Eq fixture — the side the term order admits as the rewrite source.</summary>
    private static DlTerm EqUnionSource { get; } = DlTerm.Individual(2);

    /// <summary>The rewrite replacement term of the duplicate-union Eq fixture — the side the rewrite result carries.</summary>
    private static DlTerm EqUnionReplacement { get; } = DlTerm.Individual(1);

    /// <summary>The individual the duplicate-union Eq fixture's body guards are asserted over.</summary>
    private static DlTerm EqUnionGuardHome { get; } = DlTerm.Individual(3);

    /// <summary>One ground body guard of the duplicate-union Eq fixture, over a chosen concept atom.</summary>
    /// <param name="atom">The concept atom.</param>
    /// <returns>The guard literal.</returns>
    private static DlLiteral EqUnionGuard(int atom)
    {
        return DlLiteral.Concept(atom, EqUnionGuardHome);
    }

    /// <summary>
    /// Builds the engine of the eager-landing join module WITHOUT saturating it: a
    /// ground trigger fact and a two-literal-body premise holding that trigger
    /// derive a join conclusion that genuinely lands, and a bystander root fact
    /// seeded BEHIND them waits on the ordinary queue while the conclusion's own
    /// landed event is routed. Two ontology lifts turn each of the two landed
    /// events into a further clause, and the lift the join conclusion feeds is
    /// STRICTLY WEAKER than the bystander's, so the order the two events drain in
    /// decides whether the weaker clause is absorbed on arrival or inserted and
    /// then eliminated.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine JoinEagerLandingEngine()
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int trigger = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int guard = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int lifted = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int bystanderHead = symbols.AtomOf(Utf8Strings.From(Example + "Deodar"));
        int shared = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));
        DlTerm home = DlTerm.Individual(owner);
        DlClause liftedRule = DlClause.Create([DlLiteral.Concept(lifted, DlTerm.Central)], [DlLiteral.Concept(shared, DlTerm.Central)], 0);
        DlClause bystanderRule = DlClause.Create([DlLiteral.Concept(bystanderHead, DlTerm.Central)], [DlLiteral.Concept(shared, DlTerm.Central)], 0);
        DlClause premiseTwo = DlClause.Create([], [DlLiteral.Concept(trigger, home)], 0);
        DlClause premiseOne = DlClause.Create([DlLiteral.Concept(trigger, home), DlLiteral.Concept(guard, home)], [DlLiteral.Concept(lifted, home)], 0);
        DlClause bystander = DlClause.Create([], [DlLiteral.Concept(bystanderHead, home)], 0);

        return SyntheticRootEngine(symbols, [liftedRule, bystanderRule], [premiseTwo, premiseOne, bystander]);
    }

    /// <summary>
    /// Builds the engine of the Succ-subsumption module WITHOUT saturating it: an
    /// empty-body ontology clause seeds EVERY context the unconditional head
    /// <c>⊤ → A(x)</c>, a second seeds <c>⊤ → A(f(x))</c> so the successor trigger
    /// <c>A</c> enters the expansion's hypothesis set, a third gives the same
    /// function a second distinct filler so the cautious strategy resolves the
    /// trivial context rather than minting a core-<c>{A}</c> one, and a fourth puts
    /// <c>A</c> in a clause body so it is a successor trigger at all. The expansion
    /// therefore offers the hypothesis <c>A(x) → A(x)</c> into a context that
    /// already holds the strictly more general <c>⊤ → A(x)</c>, and the offer is
    /// absorbed as a SUBSUMER rather than as a duplicate — the one shape in which a
    /// Succ seed can be subsumed at all, since a Succ seed is otherwise the first
    /// thing its successor sees.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine SuccSubsumptionEngine()
    {
        ContextSymbolTable symbols = new();
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int trigger = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int ambiguity = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int consumer = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        DlClause liftTrigger = DlClause.Create([], [DlLiteral.Concept(trigger, DlTerm.Function(function))], 0);
        DlClause liftAmbiguity = DlClause.Create([], [DlLiteral.Concept(ambiguity, DlTerm.Function(function))], 0);
        DlClause consume = DlClause.Create([DlLiteral.Concept(trigger, DlTerm.Central)], [DlLiteral.Concept(consumer, DlTerm.Central)], 0);
        DlClause seed = DlClause.Create([], [DlLiteral.Concept(trigger, DlTerm.Central)], 0);

        return SyntheticRootEngine(symbols, [liftTrigger, liftAmbiguity, consume, seed], []);
    }

    /// <summary>
    /// Builds the engine of the filler-census module WITHOUT saturating it: five
    /// ontology clauses give THREE function symbols a unique concept filler and
    /// leave a fourth ambiguous, and two of the three unique fillers are the SAME
    /// concept — so the cautious strategy's candidate-core set holds two DISTINCT
    /// filler atoms, not three and not four. The saturation reaches no successor
    /// expansion, so the registry holds a core for neither candidate.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine FillerCensusEngine()
    {
        ContextSymbolTable symbols = new();
        int firstFunction = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int secondFunction = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int thirdFunction = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int ambiguousFunction = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int sharedFiller = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int otherFiller = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int firstAmbiguousFiller = symbols.AtomOf(Utf8Strings.From(Example + "Deodar"));
        int secondAmbiguousFiller = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));
        int firstTrigger = symbols.AtomOf(Utf8Strings.From(Example + "Fir"));
        int secondTrigger = symbols.AtomOf(Utf8Strings.From(Example + "Gean"));
        int thirdTrigger = symbols.AtomOf(Utf8Strings.From(Example + "Hazel"));
        int fourthTrigger = symbols.AtomOf(Utf8Strings.From(Example + "Ilex"));
        int fifthTrigger = symbols.AtomOf(Utf8Strings.From(Example + "Juniper"));
        DlClause[] clauses =
        [
            DlClause.Create([DlLiteral.Concept(firstTrigger, DlTerm.Central)], [DlLiteral.Concept(sharedFiller, DlTerm.Function(firstFunction))], 0),
            DlClause.Create([DlLiteral.Concept(secondTrigger, DlTerm.Central)], [DlLiteral.Concept(otherFiller, DlTerm.Function(secondFunction))], 0),
            DlClause.Create([DlLiteral.Concept(thirdTrigger, DlTerm.Central)], [DlLiteral.Concept(sharedFiller, DlTerm.Function(thirdFunction))], 0),
            DlClause.Create([DlLiteral.Concept(fourthTrigger, DlTerm.Central)], [DlLiteral.Concept(firstAmbiguousFiller, DlTerm.Function(ambiguousFunction))], 0),
            DlClause.Create([DlLiteral.Concept(fifthTrigger, DlTerm.Central)], [DlLiteral.Concept(secondAmbiguousFiller, DlTerm.Function(ambiguousFunction))], 0)
        ];

        return SyntheticRootEngine(symbols, clauses, []);
    }

    /// <summary>Builds an engine over a directly-constructed nominal clausification of the given ontology clauses and root facts — the synthetic driver the Pred odometer rows read.</summary>
    /// <param name="symbols">The interning table the clauses were built over.</param>
    /// <param name="clauses">The ontology clauses.</param>
    /// <param name="rootFacts">The root-context facts.</param>
    /// <param name="topology">The root-tier topology, the published single root by default.</param>
    /// <param name="progressSampler">The in-saturation progress sampler, unattached by default.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine SyntheticRootEngine(ContextSymbolTable symbols, IReadOnlyList<DlClause> clauses, IReadOnlyList<DlClause> rootFacts, RootContextTopology topology = RootContextTopology.SingleRoot, SaturationProgressSampler? progressSampler = null)
    {
        ClausificationResult clausification = new(clauses, [], symbols, ContextTermOrder.ForModule(clauses), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: rootFacts, NominalJurisdiction: true, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);

        return ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, topology, progressSampler);
    }

    /// <summary>
    /// Builds the engine of the anchor-invariant target module WITHOUT saturating
    /// it: three told individuals, a root fact whose <c>f(o)</c> head drives one
    /// root function edge, an ontology clause making that edge's filler a
    /// successor trigger, and a second root fact — empty body, one GROUND head
    /// literal — whose n-zero broadcast image lands in the successor as a
    /// zero-slot target with an all-ground head. That target is the anchored
    /// arm's anchor-invariant shape: every anchoring constant completes the same
    /// conclusion back into the root.
    /// </summary>
    /// <param name="topology">The root-tier topology, the published single root by default.</param>
    /// <param name="progressSampler">The in-saturation progress sampler, unattached by default.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine AnchorInvariantTargetEngine(RootContextTopology topology = RootContextTopology.SingleRoot, SaturationProgressSampler? progressSampler = null)
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int carrier = symbols.InternIndividual(Utf8Strings.From(Example + "carrier"), IndividualOrigin.IriDenoted);
        _ = symbols.InternIndividual(Utf8Strings.From(Example + "bystander"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int filler = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int carried = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        DlClause lift = DlClause.Create([DlLiteral.Concept(filler, DlTerm.Central)], [DlLiteral.Concept(carried, DlTerm.Individual(carrier))], 0);
        DlClause rootEdge = DlClause.Create([], [DlLiteral.Concept(filler, DlTerm.FunctionOf(function, owner))], 0);
        DlClause trigger = DlClause.Create([], [DlLiteral.Concept(filler, DlTerm.Context)], 0);

        return SyntheticRootEngine(symbols, [lift], [rootEdge, trigger], topology, progressSampler);
    }

    /// <summary>
    /// Builds the engine of the anchor-DEPENDENT head module WITHOUT saturating
    /// it: the anchor-invariant module's ground broadcast source replaced by a
    /// nonground trigger head <c>y ≈ o</c>, whose broadcast image is the
    /// successor-side <c>x ≈ o</c>. The target is still zero-slot, but its head
    /// literal carries the central variable, so each anchoring constant completes
    /// a DIFFERENT conclusion and the anchored arm must run every constant.
    /// </summary>
    /// <param name="progressSampler">The in-saturation progress sampler, unattached by default.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine AnchorDependentHeadTargetEngine(SaturationProgressSampler? progressSampler = null)
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int carrier = symbols.InternIndividual(Utf8Strings.From(Example + "carrier"), IndividualOrigin.IriDenoted);
        _ = symbols.InternIndividual(Utf8Strings.From(Example + "bystander"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int filler = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int consumer = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        DlClause consume = DlClause.Create([DlLiteral.Concept(filler, DlTerm.Central)], [DlLiteral.Concept(consumer, DlTerm.Central)], 0);
        DlClause rootEdge = DlClause.Create([], [DlLiteral.Concept(filler, DlTerm.FunctionOf(function, owner))], 0);
        DlClause trigger = DlClause.Create([], [DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(carrier))], 0);

        return SyntheticRootEngine(symbols, [consume], [rootEdge, trigger], RootContextTopology.SingleRoot, progressSampler);
    }

    /// <summary>
    /// Builds the engine of the anchor-DEPENDENT body module WITHOUT saturating
    /// it: the root edge of the anchor-invariant module beside an ontology clause
    /// whose nonground body atom is the successor's own core trigger and whose
    /// head is a single GROUND literal. The successor derives a target with a
    /// nonground body position and an all-ground head — the shape the body
    /// conjunct of the anchor-invariance test alone declines.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine AnchorDependentBodyTargetEngine()
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int carrier = symbols.InternIndividual(Utf8Strings.From(Example + "carrier"), IndividualOrigin.IriDenoted);
        _ = symbols.InternIndividual(Utf8Strings.From(Example + "bystander"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int filler = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int carried = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        DlClause lift = DlClause.Create([DlLiteral.Concept(filler, DlTerm.Central)], [DlLiteral.Concept(carried, DlTerm.Individual(carrier))], 0);
        DlClause rootEdge = DlClause.Create([], [DlLiteral.Concept(filler, DlTerm.FunctionOf(function, owner))], 0);

        return SyntheticRootEngine(symbols, [lift], [rootEdge]);
    }

    /// <summary>
    /// Builds the engine of the mid-sweep-registration module WITHOUT saturating
    /// it: three constants interned in ascending id order and three root facts
    /// whose broadcast images reach the trivial context in the order the sweep row
    /// depends on — the abstract premise first, then the disjunctive bridge, then
    /// the ground premise — so the abstract premise's own sweep is the first
    /// derivation of the bridge conclusion.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine MidSweepRegistrationEngine()
    {
        ContextSymbolTable symbols = new();
        int residual = symbols.InternIndividual(Utf8Strings.From(Example + "resid"), IndividualOrigin.IriDenoted);
        int registrant = symbols.InternIndividual(Utf8Strings.From(Example + "reg"), IndividualOrigin.IriDenoted);
        int swept = symbols.InternIndividual(Utf8Strings.From(Example + "swept"), IndividualOrigin.IriDenoted);
        int marker = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        DlClause abstractPremise = DlClause.Create([], [DlLiteral.Concept(marker, DlTerm.Context), DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(residual))], 0);
        DlClause bridgePremise = DlClause.Create([], [DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(swept)), DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(registrant))], 0);
        DlClause groundPremise = DlClause.Create([DlLiteral.Concept(marker, DlTerm.Individual(swept))], [], 0);
        ClausificationResult clausification = new([], [], symbols, ContextTermOrder.ForModule([]), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [abstractPremise, bridgePremise, groundPremise], NominalJurisdiction: true, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);

        return ContextSaturationEngine.Create(clausification);
    }

    /// <summary>The nominal bridge-chain fixture: a consumer deriving the ground typing of an enumerated individual, an ordinary edge down to a successor holding the root edge for that individual without the local typing, and the ontology existential lifting to the ground-conjunct root clause — the join-bearing, push-bearing module the channel rows read.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NominalBridgeChainModule()
    {
        return Module(
            SubClassOf(Class("W"), Some("s", Class("U"))),
            SubClassOf(Class("U"), HasValue("r", "o")),
            SubClassOf(Class("W"), Some("q", Class("X"))),
            SubClassOf(Class("X"), OneOf("o")),
            SubClassOf(Class("X"), Class("B")),
            SubClassOf(Some("r", Class("B")), Class("D")),
            SubClassOf(Class("Spruce"), Class("Willow")));
    }

    /// <summary>The data-narrowing fixture: a carried data existential disjunct the refutation rule clashes against a string universal, so the body-conditioned narrowing seeds through the sidecar channel.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DataNarrowingModule()
    {
        return Module(
            SubClassOf(Class("Ash"), Union(Class("Cedar"), DataSome("age", new OwlDatatypeReference(new NamedNode(Vocabulary.Xsd.Integer))))),
            SubClassOf(Class("Ash"), new OwlDataAllValuesFrom([new NamedNode(Utf8Strings.From(Example + "age"))], new OwlDatatypeReference(new NamedNode(Vocabulary.Xsd.String)))));
    }

    /// <summary>The functional-merge fixture: two existential witnesses a functional role merges, whose disjoint fillers empty the owner — the Eq-, Succ-, and Hyper-bearing module the channel rows read.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule EqualityMergeModule()
    {
        return Module(
            SubClassOf(Class("A"), Some("r", Class("B1"))),
            SubClassOf(Class("A"), Some("r", Class("B2"))),
            SubClassOf(Intersection(Class("B1"), Class("B2")), NothingReference),
            Functional("r"));
    }

    /// <summary>The nominal-plus-ordinary-chain fixture: an enumerated individual gives the module nominal jurisdiction, so the root tier exists and the n-zero broadcast stratum is populated, while an unrelated existential chain drives Succ and Pred from ORDINARY contexts — the one shape in which a broadcast image could stand as a Pred premise.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NominalOrdinaryChainModule()
    {
        return Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Class("D")),
            SubClassOf(Class("D"), Class("E")),
            SubClassOf(Class("X"), OneOf("o")),
            SubClassOf(Class("X"), Class("A")),
            SubClassOf(Class("X"), Class("B")));
    }

    /// <summary>The nominal-free control: a plain Horn module carrying no individual, so the root tier never exists and every nominal channel must read zero.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NominalFreeHornModule()
    {
        return Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Class("D")));
    }

    /// <summary>Builds an engine over a directly-constructed clausification of the given clauses (empty remainder, no automaton budget, no fresh roles) and saturates it to its fixpoint under an unbounded budget — the synthetic driver the normalization probes read.</summary>
    /// <param name="clauses">The synthetic clauses.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saturated engine.</returns>
    private static ContextSaturationEngine SaturateSynthetic(IReadOnlyList<DlClause> clauses, System.Threading.CancellationToken cancellationToken)
    {
        ContextSymbolTable symbols = new();
        ClausificationResult clausification = new(clauses, [], symbols, ContextTermOrder.ForModule(clauses), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [], NominalJurisdiction: false, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.Saturate(ReasoningBudget.Unbounded, cancellationToken);

        return engine;
    }

    /// <summary>
    /// The no-two-central-literal emission invariant (section 6.3): every admitted
    /// battery module clausifies to clauses none of which carries a Role literal
    /// with both arguments the central variable <c>x</c> — the emission-side face of
    /// the second-gate tripwire, since the self-variant pass rewrites every would-be
    /// loop atom to a <c>Self_p</c> concept. Reports every offender.
    /// </summary>
    [TestMethod]
    public void NoTwoCentralRoleLiteralInAdmittedBatteryModules()
    {
        List<string> offenders = [];
        foreach((string name, ReasoningModule module, bool _, ContextPath _, string[]? _) in BatteryRows())
        {
            if(!ContextModuleSurvey.Survey(module).Admitted)
            {
                continue;
            }

            ClausificationResult clausification = ContextClausifier.Clausify(module);
            foreach(DlClause clause in clausification.Clauses)
            {
                if(CarriesTwoCentralRole(clause))
                {
                    offenders.Add(name + ": " + clause.Render(clausification.Symbols));
                }
            }
        }

        Assert.IsEmpty(offenders, "No admitted battery module may emit a two-central role literal.\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The loop-atom-sharing pin (section 6.3): the S5 module
    /// <c>{A [= exists r-.Self, exists r.Self [= B}</c> folds the inverse spelling
    /// onto the SAME base atom <c>Self_{base(r)}</c>, so clausification mints exactly
    /// one fresh Self atom AND the two Self clauses render over one shared Self
    /// symbol — the producer <c>A(x) -> Self_r(x)</c> and the consumer
    /// <c>Self_r(x) -> B(x)</c>. The signature-independent backup catcher for a
    /// direction-sensitive atom (MU7): the counter half and the rendered half.
    /// </summary>
    [TestMethod]
    public void S5ClausificationMintsExactlyOneSelfAtom()
    {
        ReasoningModule module = Module(SubClassOf(Class("A"), HasSelfInverse("r")), SubClassOf(HasSelf("r"), Class("B")));

        ClausificationResult clausification = ContextClausifier.Clausify(module);

        Assert.AreEqual(1, clausification.FreshAtoms, "S5 folds exists r-.Self and exists r.Self onto one Self atom.");

        //The rendered half: the folded Self atom is the single concept that stands as a clause head in the
        //producer and a clause body in the consumer; the intersection of the head-concept and body-concept
        //symbol ids is exactly that atom, so the inverse spelling minted no direction-sensitive second symbol.
        HashSet<int> headConcepts = [];
        HashSet<int> bodyConcepts = [];
        foreach(DlClause clause in clausification.Clauses)
        {
            foreach(DlLiteral literal in clause.Head)
            {
                if(literal.Kind == DlLiteralKind.Concept)
                {
                    headConcepts.Add(literal.Symbol);
                }
            }

            foreach(DlLiteral literal in clause.Body)
            {
                if(literal.Kind == DlLiteralKind.Concept)
                {
                    bodyConcepts.Add(literal.Symbol);
                }
            }
        }

        headConcepts.IntersectWith(bodyConcepts);
        Assert.HasCount(1, headConcepts, "Exactly one Self atom is shared as a producer head and a consumer body.");

        int selfSymbol = -1;
        foreach(int symbol in headConcepts)
        {
            selfSymbol = symbol;
        }

        //Rendering both carrying clauses confirms the fold survives to the emitted form: the same Self symbol
        //(a fresh atom, rendered _a{id}) reads in the producer's head and the consumer's body.
        string selfName = clausification.Symbols.RenderAtom(selfSymbol);
        string? producer = null;
        string? consumer = null;
        foreach(DlClause clause in clausification.Clauses)
        {
            foreach(DlLiteral literal in clause.Head)
            {
                if(literal.Kind == DlLiteralKind.Concept && literal.Symbol == selfSymbol)
                {
                    producer = clause.Render(clausification.Symbols);
                }
            }

            foreach(DlLiteral literal in clause.Body)
            {
                if(literal.Kind == DlLiteralKind.Concept && literal.Symbol == selfSymbol)
                {
                    consumer = clause.Render(clausification.Symbols);
                }
            }
        }

        Assert.IsNotNull(producer, "S5 emits a producer clause carrying the Self atom in its head.");
        Assert.IsNotNull(consumer, "S5 emits a consumer clause carrying the Self atom in its body.");
        Assert.IsTrue(producer!.Contains(selfName, StringComparison.Ordinal), "The producer clause renders the folded Self atom. " + producer);
        Assert.IsTrue(consumer!.Contains(selfName, StringComparison.Ordinal), "The consumer clause renders the folded Self atom. " + consumer);
    }

    /// <summary>
    /// The §3.3 undecided-data-obligation diagnostics (white-box on the internal
    /// <see cref="ContextSaturationEngine"/> surface): the R40-shaped pattern-facet
    /// demand (a data existential over a range the value-space checker cannot
    /// decide) sets <see cref="ContextSaturationEngine.HasUndecidedDataObligation"/>
    /// and records the demanding property's IRI on
    /// <see cref="ContextSaturationEngine.UndecidedDataObligationProperties"/>, once,
    /// even though the query context lands the demand marker twice (once per rule
    /// firing on the seeded core, once again were it re-triggered) — the
    /// deduplication pin.
    /// </summary>
    [TestMethod]
    public void UndecidedDataObligationRecordsTheDemandingPropertyName()
    {
        ReasoningModule module = Module(SubClassOf(Class("A"), DataSome("d", StringPattern("[0-9]+"))));

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.EnsureQueryContext(clausification.Symbols.AtomOf(Utf8Strings.From(Example + "A")));
        engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken);

        Assert.IsTrue(engine.HasUndecidedDataObligation, "The pattern facet is outside the checker's decided fragment, so the obligation is undecided.");
        Assert.Contains(Example + "d", engine.UndecidedDataObligationProperties, "The undecided obligation records the demanding property d's IRI.");
        Assert.HasCount(1, engine.UndecidedDataObligationProperties, "One property name is recorded, deduplicated across every landing that leaves it undecided.");
    }

    /// <summary>
    /// The cautious-reuse context bound extended to a chain module (section 6.3):
    /// the S10 family
    /// <c>{A [= exists r.Self, A [= exists s.B, r.s [= t, A [= forall t.C, B AND C [= bot}</c>
    /// is decided on the context path, and its saturation stays bounded — the
    /// cautious strategy keeps context creation finite and small even with the
    /// self-loop transition variant walking the chain letter in place.
    /// </summary>
    [TestMethod]
    public void CautiousReuseBoundsContextsOnChainModule()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(S10Module(), TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "S10 is decided by the context engine.");
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "S10 rides the context path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "S10 is consistent (A is simply unsatisfiable).");
        Assert.IsLessThan(32, decision.Statistics.ContextTotals.ContextsCreated, "The cautious strategy bounds context creation on the chain module. " + decision.Statistics.ContextTotals.ContextsCreated);
    }

    /// <summary>The wedge-family ladder rungs (family sizes) the Stage E growth measurement and admission pins sweep.</summary>
    private static int[] WedgeLadder { get; } = [2, 4, 6, 8, 10, 12, 16, 20, 24, 28, 32];

    /// <summary>
    /// The wedge family is admitted at every ladder rung: at each size the survey
    /// admits the module and the clausification
    /// passes the second gate, so the saturation-cost family is genuinely decided by
    /// the context engine (not delegated) at every size the growth ladder and the
    /// budget-ceiling pin use. Horn-ALCHI, empty remainder, no role automaton.
    /// Reports every offender.
    /// </summary>
    [TestMethod]
    public void WedgeFamilyAdmittedAcrossLadder()
    {
        List<string> offenders = [];
        foreach(int n in WedgeLadder)
        {
            ReasoningModule module = WedgeTowerModule(n);
            if(!ContextModuleSurvey.Survey(module).Admitted)
            {
                offenders.Add($"n={n}: survey rejects");

                continue;
            }

            if(ContextSaturationModuleReasoner.DelegatesOnSecondGate(ContextClausifier.Clausify(module)))
            {
                offenders.Add($"n={n}: second gate delegates");
            }
        }

        Assert.IsEmpty(offenders, "The wedge family must be context-admitted at every ladder rung.\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Direction (b) of the budget-honesty wedge (MU12's
    /// catcher): a large-but-finite wedge instance under a finite
    /// <see cref="ReasoningBudget.MaxInferences"/> ceiling returns
    /// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> with no verdict — the
    /// budget-honesty statement is work-based: the abstention outcome and the
    /// ceiling's accounting pin that the ceiling, not any other bound, ends the
    /// run. The same instance under an unbounded budget both decides
    /// (proving the ceiling is the cause, not the module's own limit) and spends far
    /// more than the ceiling's rule applications (proving the ceiling genuinely bites,
    /// so a mutation that ignored <c>MaxInferences</c> would run to the decided
    /// verdict and fail this pin). The calibration constants are recorded in the test.
    /// </summary>
    [TestMethod]
    public void WedgeBudgetCeilingAbstains()
    {
        //Calibration (measured on the wedge ladder): at WedgeCeilingSize (32) the
        //unbounded saturation spends far more than WedgeCeiling (50) rule
        //applications, so the finite WedgeCeiling ceiling trips first and abstains.
        ReasoningModule module = WedgeTowerModule(WedgeCeilingSize);

        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideModule(module, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: WedgeCeiling), progressSampler: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "The finite inference ceiling abstains on the large wedge instance.");
        Assert.IsNull(abstained.Verdict, "A budget abstention carries no verdict.");

        ModuleDecision decided = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decided.Outcome, "The same instance decides under an unbounded budget, so the ceiling is the abstention's cause.");
        Assert.IsGreaterThan((long)WedgeCeiling, TotalRuleApplications(decided.Statistics.ContextTotals), "The unbounded saturation spends more than the ceiling, so the ceiling genuinely bites.");
    }

    /// <summary>
    /// Direction (a) of the budget-honesty wedge: the unbounded saturation
    /// rule applications are measured across the wedge ladder and emitted for the
    /// captured run log. This wedge family
    /// saturates in polynomial work at the measured sizes — the cautious context
    /// reuse keeps the context count linear in n and the marker back-propagation
    /// keeps the rule applications sub-cubic (measured empirically near n^1.7) — so
    /// every ladder size the measurement reaches saturates in polynomial
    /// work: direction (a) is recorded VACUOUS-FOR-THIS-SLICE with this
    /// family's measured growth as the honest capability note (this family, at these
    /// sizes, saturates fast), exactly the spec's pre-authorized outcome, and
    /// direction (b) plus MU12 carry the wedge obligation (the ceiling pin above).
    /// The ladder crosses the 16-class subsumption cap at n=8 (classes 2n+2): the
    /// small rungs create per-class query contexts, the larger rungs saturate the
    /// trivial consistency context alone, so growth is compared only within the pure
    /// worklist (cap-off) regime — there it is strictly increasing and its log-log
    /// slope stays well under the cubic bound, the quantitative evidence that this
    /// family shows no super-polynomial explosion across the measured sizes.
    /// </summary>
    [TestMethod]
    public void WedgeSaturationGrowthLadder()
    {
        StringBuilder report = new();
        report.AppendLine("\nsroiq2 Stage E wedge saturation ladder (direction (a) measurement)");
        report.AppendLine("n | axioms | classes | outcome | ruleApplications | contextsCreated | maxContextClauses | elapsedMs");

        List<(int N, long Applications)> capOff = [];
        foreach(int n in WedgeLadder)
        {
            ReasoningModule module = WedgeTowerModule(n);
            int classes = AlcModuleReasoner.Translate(module).SignatureClasses.Count;

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            clock.Stop();

            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            long applications = TotalRuleApplications(totals);
            report.AppendLine(n + " | " + module.Axioms.Count + " | " + classes + " | " + decision.Outcome + " | " + applications + " | " + totals.ContextsCreated + " | " + totals.MaxContextClauses + " | " + clock.ElapsedMilliseconds);

            if(classes > AlcModuleReasoner.SubsumptionSignatureCap)
            {
                capOff.Add((n, applications));
            }

        }

        TestContext.WriteLine(report.ToString());

        Assert.IsGreaterThanOrEqualTo(2, capOff.Count, "The ladder spans at least two pure worklist (cap-off) rungs to measure growth." + report);

        for(int i = 1; i < capOff.Count; i++)
        {
            Assert.IsGreaterThan(capOff[i - 1].Applications, capOff[i].Applications, "Saturation work strictly increases with n across the pure worklist regime." + report);
        }

        (int N, long Applications) low = capOff[0];
        (int N, long Applications) high = capOff[^1];
        double slope = Math.Log((double)high.Applications / low.Applications) / Math.Log((double)high.N / low.N);
        Assert.IsLessThan(3.0, slope, "The measured log-log growth slope stays sub-cubic (VACUOUS-FOR-THIS-SLICE: polynomial, not super-polynomial). slope=" + slope + report);

        Assert.IsGreaterThan(0L, high.Applications, "The top ladder rung does real saturation work." + report);
    }

    /// <summary>
    /// CB1 — the production budget calibration FLOOR:
    /// the production default inference ceiling
    /// (<see cref="ReasoningConfiguration.Default"/>) is at least 50× the maximum
    /// budget-gated <see cref="ContextSaturationStatistics.InferenceAttempts"/> any
    /// certified context-decided module spends, so no certified context decision
    /// abstains at the default. The floor measures the ATTEMPT accumulator the budget
    /// actually bounds — redundant conclusions spend attempts without adding clauses,
    /// so the added-conclusion count is not a safe proxy — and its 50× margin is
    /// calibrated against that accumulator: the certified maximum is 686 attempts
    /// (BC6-SymSelfLoop), 72.9× inside the 50,000 default, and the ceiling itself
    /// stays untouched because the practical-reach census population is calibrated
    /// at it. Measured over the context-decided battery rows and the vendored
    /// OWL2Bench QL corpus TBox — the certification capability set read through the
    /// statistics exposure of the budget-gated accumulator. Supports MU13's converse
    /// (a zero default runs the context tier unbounded).
    /// </summary>
    [TestMethod]
    public void Cb1CalibrationFloorHoldsAtTheProductionDefault()
    {
        long max = 0;
        string maxRow = "";
        foreach((string name, ReasoningModule module, bool _, ContextPath expectedPath, string[]? _) in BatteryRows())
        {
            if(expectedPath != ContextPath.ContextDecided)
            {
                continue;
            }

            long attempts = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken).Statistics.ContextTotals.InferenceAttempts;
            if(attempts > max)
            {
                max = attempts;
                maxRow = name;
            }
        }

        ModuleDecision ql = ContextSaturationModuleReasoner.DecideModule(LoadOwl2BenchQlModule(), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, ql.Outcome, "The vendored OWL2Bench QL TBox is context-decided, so its saturation cost is a floor input.");
        if(ql.Statistics.ContextTotals.InferenceAttempts > max)
        {
            max = ql.Statistics.ContextTotals.InferenceAttempts;
            maxRow = "corpus:OWL2Bench-QL";
        }

        long ceiling = ReasoningConfiguration.Default.Budget.MaxInferences;
        Assert.IsGreaterThanOrEqualTo(50 * max, ceiling, "The production default inference ceiling " + ceiling + " must be at least 50x the maximum observed context InferenceAttempts " + max + " (row " + maxRow + "), so no certified context decision abstains at the default.");
    }

    /// <summary>
    /// CB2 — the production budget calibration CEILING:
    /// a wedge-tower module whose full saturation exceeds the production default
    /// inference ceiling exhausts the context tier under that default and DELEGATES to
    /// the SAT-backed oracle through the composed production chain.
    /// The delegated decision carries the spent saturation's
    /// <see cref="ContextSaturationStatistics"/> (<see cref="ContextSaturationStatistics.ContextDecided"/>
    /// <see langword="false"/>; <see cref="ContextSaturationStatistics.InferenceAttempts"/> EXACTLY the
    /// ceiling — the gate checks before spending and latches at the inclusive bound;
    /// <see cref="ContextSaturationStatistics.RuleApplications"/> the added conclusions, positive and
    /// never above the ceiling, since every added conclusion spends an attempt) beside the SAT oracle's
    /// own totals, and reads a whole outcome from the oracle — never fragment-relative. Kills MU13 (a
    /// zero default runs the context tier unbounded, deciding the wedge whole rather than exhausting
    /// and delegating).
    /// </summary>
    [TestMethod]
    public async Task Cb2WedgeExhaustsAndDelegatesAtTheProductionDefault()
    {
        ReasoningBudget budget = ReasoningConfiguration.Default.Budget;
        DescriptionLogicDelegate chain = ReasoningEngines.ElCoupled(
            ReasoningEngines.ContextSaturation(budget, ReasoningEngines.SatBacked(budget, ReasoningConfiguration.Default.SearchMode)));

        ReasoningModule module = WedgeTowerModule(Cb2CalibrationSize);

        ModuleDecision decision = await chain(module, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The context tier exhausted its budget and delegated, so it did not decide the wedge.");
        Assert.IsGreaterThan(0, decision.Statistics.ContextTotals.RuleApplications, "The delegated decision carries the exhausted saturation's spent totals.");
        Assert.AreEqual((long)budget.MaxInferences, decision.Statistics.ContextTotals.InferenceAttempts, "A budget-exhausted saturation spends exactly the attempts ceiling: the gate checks before spending and latches at the inclusive bound.");
        Assert.IsGreaterThanOrEqualTo(decision.Statistics.ContextTotals.RuleApplications, (long)budget.MaxInferences, "The added conclusions never exceed the attempts ceiling, since every added conclusion spends one attempt.");
        Assert.IsGreaterThan(0, decision.Statistics.SolveCount, "The exhaustion delegated to the SAT oracle, which ran its own solves rather than the seam surfacing the abstention.");
    }

    /// <summary>Loads the vendored OWL2Bench QL TBox as one reasoning module — the corpus floor input for CB1.</summary>
    /// <returns>The QL TBox as a reasoning module.</returns>
    private static ReasoningModule LoadOwl2BenchQlModule()
    {
        string file = Path.Combine(W3cCorpusPath.LibraryDirectory("Benchmark"), "OWL2Bench", "UNIV-BENCH-OWL2QL.owl");
        byte[] bytes = File.ReadAllBytes(file);
        DiagnosticBag diagnostics = new();
        string baseIri = new Uri(Path.GetFullPath(file)).AbsoluteUri;
        OwlOntologyDocument document = OwlRdfMapper.Map(RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(baseIri)));

        return new ReasoningModule([.. document.Axioms], Violations: []);
    }

    /// <summary>The wedge family size CB2 drives through the composed chain: large enough that the unbounded saturation exceeds the production default inference ceiling, so the context tier exhausts and delegates.</summary>
    private const int Cb2CalibrationSize = 176;

    /// <summary>The wedge family size the direction-(b) budget-ceiling pin drives — large enough that the unbounded saturation spends far more than the finite ceiling.</summary>
    internal const int WedgeCeilingSize = 32;

    /// <summary>The finite <see cref="ReasoningBudget.MaxInferences"/> ceiling the direction-(b) pin sets — below the unbounded rule-application count at <see cref="WedgeCeilingSize"/>, so the ceiling trips and the decision abstains.</summary>
    internal const int WedgeCeiling = 50;

    /// <summary>The nominal-wedge ladder rungs (tower depths) the admission pin sweeps -- every size the growth, ceiling, and exhaust pins use appears here.</summary>
    private static int[] NomWedgeLadder { get; } = [1, 2, 3, 4, 5, 6];

    /// <summary>The rungs the growth pin measures. MEASURED dilution bound: under any fixed backstop a deeper tower spreads the budget across more habitats and starves the depth chain (a six-rung sweep at 300k left every rung past the second at label depth one), so the label-ladder growth face lives on the first two rungs -- the second rung is where the depth-two mint lands -- and the deep rungs serve the admission and exhaust faces instead.</summary>
    private static int[] NomWedgeGrowthRungs { get; } = [1, 2];

    /// <summary>The fixed measurement budget the growth pin runs each growth rung at -- large enough that the first rung decides and the second rung's depth-two mint lands (MEASURED: the mint misses at 100k and lands by 300k), small enough that the second rung's honest exhaustion stays a bounded spend.</summary>
    private const int NomWedgeMeasurementBackstop = 200_000;

    /// <summary>
    /// The nominal wedge family is admitted at every ladder rung: at each size
    /// the survey admits the module in nominal
    /// jurisdiction, records the nominal census bit and the Nom-trigger
    /// co-occurrence (nominals, object number restrictions, and inverse roles
    /// together), and the clausification passes the second gate, so the
    /// generated-nominal saturation cost is genuinely decided by the context
    /// engine (not delegated) at every size the growth and budget pins use. No
    /// HasKey axiom, no anonymous individual in a nominal position, no data
    /// range. Reports every offender.
    /// </summary>
    [TestMethod]
    public void NomWedgeFamilyAdmittedAcrossLadder()
    {
        List<string> offenders = [];
        foreach(int n in NomWedgeLadder)
        {
            ReasoningModule module = NomWedgeTowerModule(n);
            ContextModuleSurveyResult survey = ContextModuleSurvey.Survey(module);
            if(!survey.Admitted)
            {
                offenders.Add($"n={n}: survey rejects");

                continue;
            }

            if(!survey.MentionsNominals)
            {
                offenders.Add($"n={n}: survey misses the nominal census bit");
            }

            if(!survey.NominalCountingInverseCooccurrence)
            {
                offenders.Add($"n={n}: survey misses the Nom-trigger co-occurrence");
            }

            if(ContextSaturationModuleReasoner.DelegatesOnSecondGate(ContextClausifier.Clausify(module)))
            {
                offenders.Add($"n={n}: second gate delegates");
            }
        }

        Assert.IsEmpty(offenders, "The nominal wedge family must be nominal-jurisdiction admitted at every ladder rung.\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Direction (b) of the nominal budget-honesty wedge: a nominal wedge
    /// instance under a finite
    /// <see cref="ReasoningBudget.MaxInferences"/> ceiling returns
    /// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> with no verdict, and
    /// the same instance under an unbounded budget decides on the context path
    /// while spending far more than the ceiling's rule applications, so the
    /// ceiling -- charged per rule application including per minted nominal -- is
    /// the abstention's cause and genuinely bites. The unbounded decision
    /// exercised the Nom rule, so the ceiling bit the generated-nominal
    /// machinery. The calibration constants are recorded in the test.
    /// </summary>
    [TestMethod]
    public void NomWedgeBudgetCeilingAbstains()
    {
        ReasoningModule module = NomWedgeTowerModule(NomWedgeCeilingSize);

        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideModule(module, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: NomWedgeCeiling), progressSampler: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "The finite inference ceiling abstains on the nominal wedge instance.");
        Assert.IsNull(abstained.Verdict, "A budget abstention carries no verdict.");

        ModuleDecision decided = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decided.Outcome, "The same nominal wedge decides under an unbounded budget, so the ceiling is the abstention's cause.");
        Assert.IsGreaterThan((long)NomWedgeCeiling, decided.Statistics.ContextTotals.RuleApplications, "The unbounded saturation spends more than the ceiling, so the ceiling genuinely bites.");
        Assert.IsGreaterThan(0L, decided.Statistics.ContextTotals.NomApplications, "The unbounded decision exercised the Nom rule, so the ceiling bit the generated-nominal machinery.");
    }

    /// <summary>
    /// Direction (a) of the nominal budget-honesty wedge: the label ladder
    /// demonstrably grows. Every rung runs at
    /// the fixed <see cref="NomWedgeMeasurementBackstop"/> -- the deep rungs'
    /// full saturation provably exceeds any practical ceiling (that is the
    /// tower's point), so a rung may decide or honestly exhaust; the growth
    /// observable is the nominal counters, never unbounded completion. Asserted:
    /// the smallest rung decides on the context path; every rung mints at least
    /// one generated nominal; Nom applications are monotone non-decreasing and
    /// the top rung fires strictly more than the bottom; the maximum
    /// generated-nominal label depth is monotone non-decreasing and reaches at
    /// least two somewhere on the ladder -- a generated nominal named onto the
    /// counted-nominal habitat of an earlier generated nominal, the label
    /// ladder's second rung.
    /// </summary>
    [TestMethod]
    public void NomWedgeGrowthLadder()
    {
        StringBuilder report = new();
        report.AppendLine("\nNominal wedge saturation ladder (direction (a): the label ladder grows)");
        report.AppendLine("n | axioms | outcome | contextDecided | nomApplications | generatedNominals | maxLabelDepth | attempts | redundant | tautology | elapsedMs");

        ReasoningBudget backstop = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: NomWedgeMeasurementBackstop);
        List<(int N, long NomApplications, int Generated, int Depth)> rungs = [];
        foreach(int n in NomWedgeGrowthRungs)
        {
            ReasoningModule module = NomWedgeTowerModule(n);

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, backstop, progressSampler: null, TestContext.CancellationToken);
            clock.Stop();

            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            report.AppendLine(n + " | " + module.Axioms.Count + " | " + decision.Outcome + " | " + totals.ContextDecided + " | " + totals.NomApplications + " | " + totals.GeneratedNominals + " | " + totals.MaxNominalLabelDepth + " | " + totals.InferenceAttempts + " | " + totals.RedundantConclusions + " | " + totals.TautologyDrops + " | " + clock.ElapsedMilliseconds);

            Assert.IsTrue(decision.Outcome is ReasoningDecisionOutcome.Decided or ReasoningDecisionOutcome.AbstainedBudget, "A ladder rung decides or honestly exhausts the measurement backstop, never anything else." + report);
            if(n == NomWedgeGrowthRungs[0])
            {
                Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The smallest rung decides, so the ladder's growth is anchored on a completed saturation." + report);
                Assert.IsTrue(totals.ContextDecided, "The smallest rung rides the context path in nominal jurisdiction, not a delegation." + report);
            }

            Assert.IsGreaterThan(0, totals.GeneratedNominals, "The Nom rule mints at least one generated nominal at every rung." + report);

            rungs.Add((n, totals.NomApplications, totals.GeneratedNominals, totals.MaxNominalLabelDepth));
        }

        TestContext.WriteLine(report.ToString());

        for(int i = 1; i < rungs.Count; i++)
        {
            Assert.IsGreaterThanOrEqualTo(rungs[i - 1].NomApplications, rungs[i].NomApplications, "Nom applications are monotone non-decreasing across the ladder." + report);
            Assert.IsGreaterThanOrEqualTo(rungs[i - 1].Depth, rungs[i].Depth, "The maximum nominal label depth is monotone non-decreasing across the ladder." + report);
        }

        Assert.IsGreaterThan(rungs[0].NomApplications, rungs[^1].NomApplications, "The top rung fires the Nom rule strictly more than the bottom rung, so the ladder genuinely grows." + report);

        int deepestLabel = 0;
        for(int i = 0; i < rungs.Count; i++)
        {
            if(rungs[i].Depth > deepestLabel)
            {
                deepestLabel = rungs[i].Depth;
            }

        }

        Assert.IsGreaterThanOrEqualTo(2, deepestLabel, "Some ladder rung mints a generated nominal at label depth at least two, so the label ladder grows past its first rung." + report);
    }

    /// <summary>
    /// The production budget ceiling companion:
    /// a nominal wedge tower whose full generated-nominal saturation exceeds the
    /// production default inference ceiling exhausts the context tier under that
    /// default and DELEGATES through the composed production chain. The
    /// delegated decision carries the spent saturation's
    /// <see cref="ContextSaturationStatistics"/>
    /// (<see cref="ContextSaturationStatistics.ContextDecided"/> <see langword="false"/>;
    /// <see cref="ContextSaturationStatistics.InferenceAttempts"/> EXACTLY the ceiling --
    /// the gate checks before spending and latches at the inclusive bound;
    /// <see cref="ContextSaturationStatistics.RuleApplications"/> the added conclusions,
    /// positive and never above the ceiling) beside the oracle's own totals, and reads a
    /// whole outcome from the oracle.
    /// </summary>
    [TestMethod]
    public async Task NomWedgeExhaustsAndDelegatesAtTheProductionDefault()
    {
        ReasoningBudget budget = ReasoningConfiguration.Default.Budget;
        DescriptionLogicDelegate chain = ReasoningEngines.ElCoupled(
            ReasoningEngines.ContextSaturation(budget, ReasoningEngines.SatBacked(budget, ReasoningConfiguration.Default.SearchMode)));

        ReasoningModule module = NomWedgeTowerModule(NomWedgeExhaustSize);

        ModuleDecision decision = await chain(module, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The context tier exhausted its budget on the nominal wedge and delegated, so it did not decide.");
        Assert.IsGreaterThan(0, decision.Statistics.ContextTotals.RuleApplications, "The delegated decision carries the exhausted saturation's spent totals.");
        Assert.AreEqual((long)budget.MaxInferences, decision.Statistics.ContextTotals.InferenceAttempts, "A budget-exhausted saturation spends exactly the attempts ceiling: the gate checks before spending and latches at the inclusive bound.");
        Assert.IsGreaterThanOrEqualTo(decision.Statistics.ContextTotals.RuleApplications, (long)budget.MaxInferences, "The added conclusions never exceed the attempts ceiling, since every added conclusion spends one attempt.");
        Assert.IsGreaterThan(0, decision.Statistics.SolveCount, "The exhaustion delegated to the SAT oracle, which ran its own solves rather than the seam surfacing the abstention.");
    }

    /// <summary>The nominal wedge tower depth the direction-(b) budget-ceiling pin drives -- the smallest rung, whose unbounded generated-nominal saturation completes in milliseconds yet spends far more than the finite ceiling.</summary>
    internal const int NomWedgeCeilingSize = 1;

    /// <summary>The finite <see cref="ReasoningBudget.MaxInferences"/> ceiling the direction-(b) nominal pin sets -- below the unbounded rule-application count at <see cref="NomWedgeCeilingSize"/>, so the ceiling trips and the decision abstains.</summary>
    internal const int NomWedgeCeiling = 50;

    /// <summary>The nominal wedge tower depth the exhaust-and-delegate pin drives through the composed chain -- a measured deep rung whose saturation exceeds the production default inference ceiling by orders of magnitude (a 300k sweep exhausts at this size), while its named-class census stays UNDER the subsumption-sweep signature cap: an over-cap tower skips the sweep's query contexts -- where the enumeration cost lives -- and decides its cheap consistency-only face instead (MEASURED at depth eight, seventeen named classes).</summary>
    private const int NomWedgeExhaustSize = 6;

    /// <summary>
    /// The nominal budget-honesty wedge family:
    /// a descending anonymous-witness tower sized by <paramref name="n"/> whose
    /// Nom-rule work scales with the size. Each level i seeds an anonymous witness
    /// (<c>NwAnchor_i [= exists s.NwL_i</c>) that reaches a per-level counted
    /// nominal (<c>NwL_i [= exists r.{nwo_i}</c>, <c>{nwo_i} [= &lt;=1 r-</c>), so the
    /// Nom rule mints a depth-one generated nominal naming that anonymous
    /// predecessor -- n independent firings, one per level. The levels thread
    /// through a shared inverse-counting recursion (<c>NwL_i [= exists r-.NwL_{i+1}</c>,
    /// <c>NwL_i [= &lt;=1 r-</c>): a depth-one name carries its type
    /// <c>NwL_i</c>, which supplies an anonymous r-predecessor and a counting
    /// bound, so the Nom rule fires again anchored at that generated nominal and
    /// mints at label depth two, and the mint chain descends the distinct
    /// <c>NwL_0 .. NwL_n</c> type ladder so the label depth grows with the size.
    /// The module carries nominals, object number restrictions, and inverse roles
    /// together (the Nom co-occurrence trigger), holds no HasKey axiom, no
    /// anonymous individual in a nominal position, and no data range, so it is
    /// survey-admitted in nominal jurisdiction and second-gate clean; every fold
    /// is an unconstrained merge, so the module is consistent and the growth is
    /// pure saturation work.
    /// </summary>
    /// <param name="n">The family size: the tower depth and the count of independent per-level counted-nominal habitats together.</param>
    /// <returns>The nominal wedge module.</returns>
    internal static ReasoningModule NomWedgeTowerModule(int n)
    {
        List<OwlAxiom> axioms = [Inverse("r", "rInv")];

        for(int level = 0; level < n; level++)
        {
            axioms.Add(SubClassOf(Class($"NwAnchor{level}"), Some("s", Class($"NwL{level}"))));
            axioms.Add(SubClassOf(Class($"NwL{level}"), HasValue("r", $"nwo{level}")));
            axioms.Add(SubClassOf(OneOf($"nwo{level}"), MaxInverse("r", 1, ThingReference)));
            axioms.Add(SubClassOf(Class($"NwL{level}"), SomeInverse("r", Class($"NwL{level + 1}"))));
            axioms.Add(SubClassOf(Class($"NwL{level}"), MaxInverse("r", 1, ThingReference)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Whether a clause carries a role literal with both arguments the central variable <c>x</c>, in its body or head.</summary>
    /// <param name="clause">The clause.</param>
    /// <returns><see langword="true"/> when a two-central role literal occurs.</returns>
    private static bool CarriesTwoCentralRole(DlClause clause)
    {
        foreach(DlLiteral literal in clause.Body)
        {
            if(literal.Kind == DlLiteralKind.Role && literal.First.IsCentral && literal.Second.IsCentral)
            {
                return true;
            }
        }

        foreach(DlLiteral literal in clause.Head)
        {
            if(literal.Kind == DlLiteralKind.Role && literal.First.IsCentral && literal.Second.IsCentral)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Wraps clauses in a synthetic clausification result the second gate reads, with an empty remainder and no automaton budget or fresh roles by default.</summary>
    /// <param name="clauses">The synthetic clauses.</param>
    /// <param name="remainder">The named remainder; empty by default.</param>
    /// <param name="automatonBudgetExceeded">Whether a role automaton exceeded its budget; <see langword="false"/> by default.</param>
    /// <param name="freshRoles">The minted fresh-role count; zero by default.</param>
    /// <param name="countingRoles">The minted counting-role count; zero by default. The gate delegates when it diverges from <paramref name="freshRoles"/>.</param>
    /// <param name="nominalJurisdiction">The jurisdiction bit under which the gate admits constant-bearing head literals; <see langword="false"/> by default.</param>
    /// <param name="dataDemands">The demand-marker descriptors keyed by marker atom, against which the gate reads a data-marker head literal's kind; empty by default.</param>
    /// <returns>The synthetic clausification result.</returns>
    private static ClausificationResult GateResult(IReadOnlyList<DlClause> clauses, IReadOnlyList<string>? remainder = null, bool automatonBudgetExceeded = false, int freshRoles = 0, int countingRoles = 0, bool nominalJurisdiction = false, IReadOnlyDictionary<int, DataDemandDescriptor>? dataDemands = null)
    {
        ContextSymbolTable symbols = new();

        return new ClausificationResult(clauses, remainder ?? [], symbols, ContextTermOrder.ForModule(clauses), 0, automatonBudgetExceeded, 0, freshRoles, countingRoles, NegativePolarityDataMarkers: 0, dataDemands ?? new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: [], nominalJurisdiction, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);
    }

    /// <summary>
    /// The battery's ground-truth rows, each with its module, true consistency,
    /// expected decision path, and exact expected subsumption set (a set of sorted
    /// <c>subIri-&gt;superIri</c> keys, empty for a context-decided row entailing
    /// nothing, and <see langword="null"/> for a delegated row whose set is not
    /// asserted). Every entry states an independently derived ground truth.
    /// </summary>
    /// <returns>The battery rows.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ContextPath ExpectedPath, string[]? ExpectedSubsumptions)[] BatteryRows()
    {
        return
        [
            //D1 {A [= exists r.B, B [= bot} CONSISTENT. Model: domain {d}, every class empty, r empty; both
            //axioms vacuous. A unsat (needs an r-successor in B, and B [= bot), B unsat (direct). Each
            //unsatisfiable class is [= the other.
            ("D1", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("B", "A")]),

            //D2 {r [= s} CONSISTENT. Any structure models it; no named class is swept, so no subsumption.
            ("D2", Module(SubProperty("r", "s")), true, ContextPath.ContextDecided, []),

            //D3 {A [= exists r.B, B [= forall r-.C, C [= bot} CONSISTENT (all-empty model). A unsat: its
            //witness b in B, and forall r-.C at b puts every r-predecessor (a) into C [= bot. C unsat (direct).
            //B sat (isolated b, no incoming r). Signature [A,B,C].
            ("D3", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), AllInverse("r", Class("C"))), SubClassOf(Class("C"), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C"), Sub("C", "A"), Sub("C", "B")]),

            //D3b D3 with B [= C added: A, B, C ALL unsat (B [= C [= bot; A needs a B-witness). All six ordered
            //pairs over {A,B,C}.
            ("D3b", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), AllInverse("r", Class("C"))), SubClassOf(Class("C"), NothingReference), SubClassOf(Class("B"), Class("C"))), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C"), Sub("B", "A"), Sub("B", "C"), Sub("C", "A"), Sub("C", "B")]),

            //D4 {top [= A, A [= bot} INCONSISTENT: a domain is non-empty, so the element is forced into
            //A [= bot. The empty-body clause fires in v_top; Hyper with A(x)->bot yields the empty clause.
            ("D4", Module(SubClassOf(ThingReference, Class("A")), SubClassOf(Class("A"), NothingReference)), false, ContextPath.ContextDecided, []),

            //T1 {top [= exists r.B, B [= bot} INCONSISTENT: every element needs an r-successor in B [= bot,
            //and a domain is non-empty. The per-context top->Top(x) seed carries the top-level witness chain.
            ("T1", Module(SubClassOf(ThingReference, Some("r", Class("B"))), SubClassOf(Class("B"), NothingReference)), false, ContextPath.ContextDecided, []),

            //T2 {A [= exists r.bot, D [= E} CONSISTENT. Model: A empty, D = E = {d}. A unsat (its witness would
            //inhabit bot; the virtual Bottom(x)->bot clause condemns A through its successor). D, E sat, D [= E.
            //Signature [A,D,E].
            ("T2", Module(SubClassOf(Class("A"), Some("r", NothingReference)), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("A", "D"), Sub("A", "E"), Sub("D", "E")]),

            //P0 {A [= exists r.B, B [= forall r-.C} CONSISTENT. Model {a,b}, A={a}, B={b}, r={(a,b)}, C={a}.
            //A [= C holds semantically (the witness's forall r- reaches back over the Skolem edge -- the Pred
            //rule). C occurs only under forall r-, so it enters the swept W SOLELY via the widened
            //BuildSweepSignature (inverse-universal filler, absent from the base ALC signature). W=[A,B,C],
            //pairs {A->C}.
            ("P0", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), AllInverse("r", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //P1 {A [= exists r.B, B [= forall r-.C, C [= M} CONSISTENT. Model {a,b}, A={a}, B={b}, r={(a,b)},
            //C={a}, M={a}. A [= C (the witness's forall r- reaches back over the Skolem edge -- the Pred rule),
            //C [= M (direct), A [= M (composition). C enters the signature via C [= M. Signature [A,B,C,M].
            ("P1", P1Module(), true, ContextPath.ContextDecided,
                [Sub("A", "C"), Sub("A", "M"), Sub("C", "M")]),

            //P2 {InverseObjectProperties(r,s), A [= exists r.B, B [= forall s.C} CONSISTENT via the named
            //inverse: r(a,b) iff s(b,a), so b's s-successors = a and a in C. Signature [A,B,C].
            ("P2", Module(Inverse("r", "s"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), All("s", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //P3 {r [= s- (SubPropertyInverse), A [= exists r.B, B [= forall s.C} CONSISTENT: r(a,b) => s(b,a)
            //=> a in C. Signature [A,B,C].
            ("P3", Module(SubPropertyInverse("r", "s"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), All("s", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //P3n {s [= r- (the OTHER direction), A [= exists r.B, B [= forall s.C} CONSISTENT, NOTHING entailed:
            //an r-edge forces no s-edge (counter-model r={(a,b)}, s empty, C empty). The direction-confusion pin.
            ("P3n", Module(SubPropertyInverse("s", "r"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), All("s", Class("C")))), true, ContextPath.ContextDecided, []),

            //CE0 {A1 [= exists r.B, A2 [= exists r.B, A1 [= bot, Symmetric(r)} CONSISTENT. Model {a2,b}, A2={a2},
            //B={b}, r={(a2,b),(b,a2)}. A1 unsat (direct). A2, B sat -- A2 stays clean (no shared-filler fold
            //leak). Signature [A1,B,A2].
            ("CE0", Module(SubClassOf(Class("A1"), Some("r", Class("B"))), SubClassOf(Class("A2"), Some("r", Class("B"))), SubClassOf(Class("A1"), NothingReference), Symmetric("r")), true, ContextPath.ContextDecided,
                [Sub("A1", "B"), Sub("A1", "A2")]),

            //CE1 {A1,A2,A3 [= exists r.B, A2 [= bot, InverseSubProperty(r,r) i.e. r- [= r} CONSISTENT; only A2
            //unsat. Signature [A1,B,A2,A3].
            ("CE1", Module(SubClassOf(Class("A1"), Some("r", Class("B"))), SubClassOf(Class("A2"), Some("r", Class("B"))), SubClassOf(Class("A3"), Some("r", Class("B"))), SubClassOf(Class("A2"), NothingReference), InverseSubProperty("r", "r")), true, ContextPath.ContextDecided,
                [Sub("A2", "A1"), Sub("A2", "B"), Sub("A2", "A3")]),

            //CE2 {A1 [= exists r.B, A2 [= exists r.B, A1 [= bot, Range(r,D)} CONSISTENT. Model {a2,b}, A2={a2},
            //B={b}, D={b}, r={(a2,b)}. A1 unsat. B [= D is NOT entailed (a B-element with no incoming r need
            //not be D). Signature [A1,B,A2,D]; the pin is B->D ABSENT.
            ("CE2", Module(SubClassOf(Class("A1"), Some("r", Class("B"))), SubClassOf(Class("A2"), Some("r", Class("B"))), SubClassOf(Class("A1"), NothingReference), Range("r", Class("D"))), true, ContextPath.ContextDecided,
                [Sub("A1", "B"), Sub("A1", "A2"), Sub("A1", "D")]),

            //CE3 {A1 [= exists r.B, A2 [= exists r.B, exists r-.A1 [= C, C [= bot, Symmetric(r)} CONSISTENT.
            //Model {a2,b2}, A2={a2}, B={b2}, r={(a2,b2),(b2,a2)}, A1=C empty. A1 unsat: its witness has an
            //r-predecessor a1 in A1, so it is in C [= bot. C unsat (direct). A2, B sat. Signature [A1,B,A2,C].
            ("CE3", Module(SubClassOf(Class("A1"), Some("r", Class("B"))), SubClassOf(Class("A2"), Some("r", Class("B"))), SubClassOf(SomeInverse("r", Class("A1")), Class("C")), SubClassOf(Class("C"), NothingReference), Symmetric("r")), true, ContextPath.ContextDecided,
                [Sub("A1", "B"), Sub("A1", "A2"), Sub("A1", "C"), Sub("C", "A1"), Sub("C", "B"), Sub("C", "A2")]),

            //CE4 {A [= exists r.A, Symmetric(r)} CONSISTENT. Model {a}, A={a}, r={(a,a)}. Terminates: the
            //cautious strategy reuses q_A as its own f-successor. Signature [A]; nothing entailed.
            ("CE4", Ce4Module(), true, ContextPath.ContextDecided, []),

            //CE5 {A1,A2 [= exists r.B, B [= exists s.E, r.s [= t, exists t-.A1 [= bot, Symmetric(r)} CONSISTENT
            //(A1 empty in every model). ContextDecided: chains are admitted. Model
            //witnessing A2/B/E sat: {a2,b,e}, A2={a2}, B={b}, E={e}, r={(a2,b),(b,a2)} (symmetric), s={(b,e)},
            //t from r.s = {(a2,e)}, A1 empty. A1 UNSAT via Pred through the chain: any a1 in A1 has r->b in B,
            //b has s->e in E, so r.s composes a1 -t-> e; then e has a t-predecessor a1 in A1 [= exists t-.A1 [=
            //bot => bot. A2, B, E sat. ALC={A1,B,A2,E} (roles are not classes; the inverse restriction walks no
            //new class); HasSelf={}. W=[A1,B,A2,E] (4 classes). Pairs {A1->B, A1->A2, A1->E} (A1 unsat [= the
            //three sat classes; no subsumption among sat classes).
            ("CE5", Module(SubClassOf(Class("A1"), Some("r", Class("B"))), SubClassOf(Class("A2"), Some("r", Class("B"))), SubClassOf(Class("B"), Some("s", Class("E"))), Chain("t", "r", "s"), SubClassOf(SomeInverse("t", Class("A1")), NothingReference), Symmetric("r")), true, ContextPath.ContextDecided,
                [Sub("A1", "B"), Sub("A1", "A2"), Sub("A1", "E")]),

            //B6 {A1 [= exists r-.B, A2 [= exists r-.B, B [= exists r-.K, exists r.A1 [= Q1, exists r.Q1 [= P,
            //P [= bot} CONSISTENT. Model {a2,b2,k2}, A2={a2}, B={b2}, K={k2}, r={(b2,a2),(k2,b2)}, rest empty.
            //A1 unsat: its witness b (b->r->a1) is in exists r.A1 => Q1; b's own witness k (k->r->b) is in
            //exists r.Q1 => P [= bot. P unsat (direct). A2, B, K, Q1 sat. K occurs only as the filler of
            //exists r-.K and enters W SOLELY via the signature widening (inverse-existential filler). Signature
            //[A1,A2,B,Q1,P,K]; the two unsat classes A1 and P each subsume the other five (K included).
            ("B6", Module(SubClassOf(Class("A1"), SomeInverse("r", Class("B"))), SubClassOf(Class("A2"), SomeInverse("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("K"))), SubClassOf(Some("r", Class("A1")), Class("Q1")), SubClassOf(Some("r", Class("Q1")), Class("P")), SubClassOf(Class("P"), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A1", "A2"), Sub("A1", "B"), Sub("A1", "Q1"), Sub("A1", "P"), Sub("A1", "K"), Sub("P", "A1"), Sub("P", "A2"), Sub("P", "B"), Sub("P", "Q1"), Sub("P", "K")]),

            //CM = B6 plus {Q1 [= exists s.G, G [= bot} CONSISTENT (model: {a2,b2,k2}, A2={a2}, B={b2},
            //K={k2}, r={(b2,a2),(k2,b2)}, s empty, all else empty). The additions flip Q1 to unsat (its
            //witness would inhabit G [= bot), which also shortens A1's condemnation; A2, B, K stay sat.
            //K enters W SOLELY via the signature widening (inverse-existential filler); signature [A1,A2,B,Q1,P,G,K].
            //THE CONDITIONAL-CORE PIN: in the shared successor v_B the g-existential fires only through the
            //CONDITIONAL Q1(x)->Q1(x) hypothesis, so healthy cautious targets the trivial context (G certain
            //nowhere); a K1:=K2 regression wrongly cores {G(x)}, collapses that context unconditionally, and
            //falsely condemns A2 over the shared edge (mutation M2's catcher). The four unsat classes
            //A1,Q1,P,G each subsume K.
            ("CM", Module(SubClassOf(Class("A1"), SomeInverse("r", Class("B"))), SubClassOf(Class("A2"), SomeInverse("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("K"))), SubClassOf(Some("r", Class("A1")), Class("Q1")), SubClassOf(Some("r", Class("Q1")), Class("P")), SubClassOf(Class("P"), NothingReference), SubClassOf(Class("Q1"), Some("s", Class("G"))), SubClassOf(Class("G"), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A1", "A2"), Sub("A1", "B"), Sub("A1", "Q1"), Sub("A1", "P"), Sub("A1", "G"), Sub("A1", "K"), Sub("Q1", "A1"), Sub("Q1", "A2"), Sub("Q1", "B"), Sub("Q1", "P"), Sub("Q1", "G"), Sub("Q1", "K"), Sub("P", "A1"), Sub("P", "A2"), Sub("P", "B"), Sub("P", "Q1"), Sub("P", "G"), Sub("P", "K"), Sub("G", "A1"), Sub("G", "A2"), Sub("G", "B"), Sub("G", "Q1"), Sub("G", "P"), Sub("G", "K")]),

            //CM2 = CM without the P-route ({exists r.Q1 [= P, P [= bot} dropped): {A1,A2 [= exists r-.B,
            //B [= exists r-.K, exists r.A1 [= Q1, Q1 [= exists s.G, G [= bot} CONSISTENT (same model as CM
            //with P gone). A1 unsat via the G-route ONLY: its witness b is in Q1 (b has the r-successor a1),
            //and Q1's s-witness would inhabit G [= bot. Q1, G unsat; A2, B sat; K unswept. Signature
            //[A1,A2,B,Q1,G]. THE M2 CATCHER PROPER: with no competing condemnation route, the conditional
            //g-existential in the shared successor MUST expand (in CM the P-route condemns Q1 first and
            //backward Elim retires the g-trigger before it processes -- a correct race this row removes);
            //healthy cautious targets the trivial context (G(x) certain nowhere), and a K1:=K2 regression
            //wrongly reuses the collapsed q_G as the successor core and falsely condemns A2. K enters W SOLELY
            //via the signature widening (inverse-existential filler); signature [A1,A2,B,Q1,G,K]. The three unsat
            //classes A1,Q1,G each subsume K.
            ("CM2", Module(SubClassOf(Class("A1"), SomeInverse("r", Class("B"))), SubClassOf(Class("A2"), SomeInverse("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("K"))), SubClassOf(Some("r", Class("A1")), Class("Q1")), SubClassOf(Class("Q1"), Some("s", Class("G"))), SubClassOf(Class("G"), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A1", "A2"), Sub("A1", "B"), Sub("A1", "Q1"), Sub("A1", "G"), Sub("A1", "K"), Sub("Q1", "A1"), Sub("Q1", "A2"), Sub("Q1", "B"), Sub("Q1", "G"), Sub("Q1", "K"), Sub("G", "A1"), Sub("G", "A2"), Sub("G", "B"), Sub("G", "Q1"), Sub("G", "K")]),

            //O1 (n=3) the KR2016 Figure-1 chain: Bi [= exists Sj.B(i+1) (j in 1,2), B3 [= C3,
            //exists Sj.C(i+1) [= Ci. CONSISTENT (the looping-chain model, all classes sat). Entails exactly
            //Bi [= Ci for all i. Signature 8 classes.
            ("O1", O1Module(), true, ContextPath.ContextDecided,
                [Sub("B0", "C0"), Sub("B1", "C1"), Sub("B2", "C2"), Sub("B3", "C3")]),

            //MS1 diamond {A[=B, A[=C, B[=D, C[=D, D[=E, F[=exists r.A, exists r.B[=G, exists r.D[=H} CONSISTENT,
            //all sat. F's witness is in A [= B and A [= D, so F in exists r.B [= G and exists r.D [= H.
            //Signature [A,B,C,D,E,F,G,H].
            ("MS1", Module(SubClassOf(Class("A"), Class("B")), SubClassOf(Class("A"), Class("C")), SubClassOf(Class("B"), Class("D")), SubClassOf(Class("C"), Class("D")), SubClassOf(Class("D"), Class("E")), SubClassOf(Class("F"), Some("r", Class("A"))), SubClassOf(Some("r", Class("B")), Class("G")), SubClassOf(Some("r", Class("D")), Class("H"))), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C"), Sub("A", "D"), Sub("A", "E"), Sub("B", "D"), Sub("B", "E"), Sub("C", "D"), Sub("C", "E"), Sub("D", "E"), Sub("F", "G"), Sub("F", "H")]),

            //MS2 equivalence {A == B and C, B [= D} CONSISTENT, all sat. Signature [A,B,C,D].
            ("MS2", Module(Equivalent(Class("A"), Intersection(Class("B"), Class("C"))), SubClassOf(Class("B"), Class("D"))), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C"), Sub("A", "D"), Sub("B", "D")]),

            //MS3 union-subclass {B or C [= D, A [= B, E [= C} CONSISTENT, all sat -- the Horn union split
            //carries it. Signature [B,C,D,A,E].
            ("MS3", Module(SubClassOf(Union(Class("B"), Class("C")), Class("D")), SubClassOf(Class("A"), Class("B")), SubClassOf(Class("E"), Class("C"))), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "D"), Sub("B", "D"), Sub("C", "D"), Sub("E", "C"), Sub("E", "D")]),

            //H1 {r [= s, Domain(s,C), A [= exists r.B} CONSISTENT. A [= C: the r-witness edge is an s-edge by
            //DL5, so a is in s's domain. B sat. Signature [C,A,B].
            ("H1", Module(SubProperty("r", "s"), Domain("s", Class("C")), SubClassOf(Class("A"), Some("r", Class("B")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //EQP {EquivalentObjectProperties(r,s), A [= exists r.B, Domain(s,C)} CONSISTENT; A [= C (r == s).
            //Signature [A,B,C].
            ("EQP", Module(EquivalentProperties("r", "s"), SubClassOf(Class("A"), Some("r", Class("B"))), Domain("s", Class("C"))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //DJ {DisjointClasses(A,B), C [= A, C [= B} CONSISTENT (A={a}, B={b}, C empty). C unsat
            //(C [= A and B [= bot). Signature [A,B,C]. Exercises the two-premise Hyper on A(x) and B(x)->bot.
            ("DJ", Module(Disjoint(Class("A"), Class("B")), SubClassOf(Class("C"), Class("A")), SubClassOf(Class("C"), Class("B"))), true, ContextPath.ContextDecided,
                [Sub("C", "A"), Sub("C", "B")]),

            //CS {A [= not B, C [= A, C [= B} CONSISTENT (A={a}, B={b}, C empty). C unsat (C [= A [= not B and
            //C [= B). Signature [A,B,C]. Pins the superclass-complement Horn lowering (A and B [= bot).
            ("CS", Module(SubClassOf(Class("A"), Complement(Class("B"))), SubClassOf(Class("C"), Class("A")), SubClassOf(Class("C"), Class("B"))), true, ContextPath.ContextDecided,
                [Sub("C", "A"), Sub("C", "B")]),

            //M1R {A [= >=1 r.B, exists r.B [= C} CONSISTENT; A [= C (>=1 == exists). Min-1 in superclass
            //(positive) position is admitted. Signature [A,B,C].
            ("M1R", Module(SubClassOf(Class("A"), Min("r", 1, Class("B"))), SubClassOf(Some("r", Class("B")), Class("C"))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //DE1 {A [= exists r.B, B [= forall r-.C, C [= forall r.E, E [= bot} CONSISTENT (model: domain {y},
            //B={y}, everything else empty; y has no r-predecessor). A unsat: its witness edge (a,b) first
            //back-propagates C onto a (forall r-), then forward-propagates E onto b (forall r.E over the SAME
            //edge), and E [= bot. E unsat (direct); B sat (predecessor-free), C sat (successor-free). The
            //double-expansion pin: E(f(x)) lands in q_A only AFTER the first Succ expansion's Pred round-trip,
            //so the SAME (u,f) candidate must re-fire and add the late E(x)->E(x) hypothesis seed to the
            //existing successor -- a coverage-check regression that skips on any existing f-edge loses A's
            //condemnation entirely.
            ("DE1", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), AllInverse("r", Class("C"))), SubClassOf(Class("C"), All("r", Class("E"))), SubClassOf(Class("E"), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C"), Sub("A", "E"), Sub("E", "A"), Sub("E", "B"), Sub("E", "C")]),

            //The content-bearing families over Transitive, Chain, HasSelf, Reflexive and Irreflexive: the Self
            //soundness rows (S1-S12), the chain/transitivity content rows (TE1, LR3), the non-entailment and
            //guard rows (X1-X4, RefBeyond), and the regularity/budget delegated rows. Each carries its
            //independently derived model or unsat derivation condensed; the swept signature W is stated per row.

            //S1 producer/consumer {A [= exists r.Self, exists r.Self [= B} CONSISTENT. Model {a}, A={a},
            //r={(a,a)}, Self_r={a}, B={a}. Every A-element has r(x,x) (producer), every r-loop is B (consumer)
            //=> A [= B. ALC={A} (ax2 short-circuits), HasSelf={A,B}; W=[A,B]. Headline observability pin
            //(section 2.1-dependent).
            ("S1", Module(SubClassOf(Class("A"), HasSelf("r")), SubClassOf(HasSelf("r"), Class("B"))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //S2 hierarchy {A [= exists r.Self, r [= s, exists s.Self [= B} CONSISTENT. Model {a}, A={a},
            //r=s={(a,a)}, B={a}. A => r(x,x) => (r[=s) s(x,x) => (consumer on s) B; the s-loop arises via the
            //DL5 self-variant Self_r(x)->Self_s(x). ALC={A}, HasSelf={A,B}; W=[A,B]. MU3 catcher (post-2.1).
            ("S2", Module(SubClassOf(Class("A"), HasSelf("r")), SubProperty("r", "s"), SubClassOf(HasSelf("s"), Class("B"))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //S3 loop-as-witness {A [= exists r.Self, A [= C, exists r.C [= D} CONSISTENT. Model {a}, A={a},
            //C={a}, r={(a,a)}, D={a}. A has r(x,x) and x in C, so x in exists r.C => D. A [= C (asserted) and
            //A [= D (the loop is x's own r-witness into C). ALC={A,C,D} (ax2/ax3 clean), HasSelf={A}; W=[A,C,D].
            //The asserted (A,C) IS swept and MUST be listed. MU1 catcher (DL3-second variant).
            ("S3", Module(SubClassOf(Class("A"), HasSelf("r")), SubClassOf(Class("A"), Class("C")), SubClassOf(Some("r", Class("C")), Class("D"))), true, ContextPath.ContextDecided,
                [Sub("A", "C"), Sub("A", "D")]),

            //S4 loop-under-universal {A [= exists r.Self, A [= forall r.C} CONSISTENT. Model {a}, A={a},
            //r={(a,a)}, C={a}. x in A has r(x,x) and forall r.C forces its only r-successor (itself) into C
            //=> A [= C. ALC={A,C} (forward-forall pushes C), HasSelf={A}; W=[A,C]. MU2 catcher (DL3-first).
            ("S4", Module(SubClassOf(Class("A"), HasSelf("r")), SubClassOf(Class("A"), All("r", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //S5 inverse spelling {A [= exists r-.Self, exists r.Self [= B} CONSISTENT. Model {a}, A={a},
            //r={(a,a)}, B={a} (r(a,a) iff r-(a,a)). exists r-.Self and exists r.Self both lower to
            //Self_{base(r)}, so A has r(x,x) and the r-consumer fires => A [= B. ONE Self atom (folded).
            //ALC={A}, HasSelf={A,B}; W=[A,B]. MU7 catcher (backup = the loop-atom-sharing parity pin).
            ("S5", Module(SubClassOf(Class("A"), HasSelfInverse("r")), SubClassOf(HasSelf("r"), Class("B"))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //S6 reflexive collector {Ref(r), exists r.Self [= B, D [= E} CONSISTENT. Model {d}, r={(d,d)},
            //B={d}, D={d}, E={d}. Ref(r) forces r(x,x) for EVERY x (top->Self_r), so the consumer makes every
            //element B => top [= B => every swept class [= B; plus D [= E. ALC={D,E}, HasSelf={B}; W=[D,E,B].
            //MU5 catcher (drop Ref producer).
            ("S6", Module(Reflexive("r"), SubClassOf(HasSelf("r"), Class("B")), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("D", "E"), Sub("D", "B"), Sub("E", "B")]),

            //S7 irreflexive kills producer {Irr(r), A [= exists r.Self, D [= E} CONSISTENT (module), A UNSAT.
            //Model {d}, A empty, r empty, D={d}, E={d}. A(x)->Self_r(x) but Irr is Self_r(x)->bot, so A(x)->bot
            //=> A empty everywhere (C1: unsat class, consistent module). Spectators make A observable.
            //ALC={A,D,E}, HasSelf={A}; W=[A,D,E]. Pairs {A->D,A->E,D->E}. MU6 catcher.
            ("S7", Module(Irreflexive("r"), SubClassOf(Class("A"), HasSelf("r")), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("A", "D"), Sub("A", "E"), Sub("D", "E")]),

            //S8 reflexive AND irreflexive {Ref(r), Irr(r)} INCONSISTENT. Ref is empty-body top->Self_r(x) fired
            //in v_top off the top->Top(x) seed; Irr is Self_r(x)->bot. Hyper yields the empty clause in v_top
            //(a domain is non-empty). No class swept; W=[]. MU5 catcher (false-consistent on S8).
            ("S8", Module(Reflexive("r"), Irreflexive("r")), false, ContextPath.ContextDecided, []),

            //S9 propagation x guard {Ref(r), r [= s, Irr(s)} INCONSISTENT. Ref(r) top->Self_r; the DL5
            //self-variant of r[=s (Self_r(x)->Self_s(x)) lifts the loop; Irr(s) Self_s(x)->bot. Chain
            //top -> Self_r -> Self_s -> bot. Both r,s in L0 here, so a DUD for MU10. W=[]. MU3 catcher.
            ("S9", Module(Reflexive("r"), SubProperty("r", "s"), Irreflexive("s")), false, ContextPath.ContextDecided, []),

            //S10 Self x chain letter {A [= exists r.Self, A [= exists s.B, r.s [= t, A [= forall t.C, B AND C [= bot}
            //CONSISTENT (module), A UNSAT. x in A has r(x,x) (Self, r simple) and s->y in B; r.s with the r-loop
            //at x (z:=x) composes t(x,y); forall t.C => y in C; but y in B and B AND C [= bot => bot. A empty
            //everywhere. Walks the r-letter in place via the transition variant q_i(x) AND Self_r(x)->q_j(x).
            //ALC={A,B,C}, HasSelf={A}; W=[A,B,C]. r,s simple, t non-simple. MU4 catcher (transition variants).
            ("S10", S10Module(), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C")]),

            //S11 Ref on non-simple {Trans(t), Ref(t), A [= forall t.C} CONSISTENT. Model {a}, A={a}, t={(a,a)},
            //C={a}. Ref(t) forces t(x,x); t NON-SIMPLE (transitive) -- Ref is LEGAL on non-simple (KR2006, no
            //guard). forall t.C at x in A with the t-loop => x in C => A [= C. Rides the t-automaton primary arc
            //via the transition variant. ALC={A,C}, HasSelf={}; W=[A,C]. MU4 catcher.
            ("S11", Module(Transitive("t"), Reflexive("t"), SubClassOf(Class("A"), All("t", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //S12 closure-only propagation {Ref(r), r [= s, A [= forall s.C} CONSISTENT. Model {a}, A={a},
            //r=s={(a,a)}, C={a}. top->Self_r; DL5 variant Self_r->Self_s; DL3-first variant A AND Self_s -> C.
            //s bears NO Self/Ref/Irr axiom -- s in L ONLY via the upward closure of L0={r} under r[=s. The
            //closure's sole discriminating witness. ALC={A,C}, HasSelf={}; W=[A,C]. MU10 catcher.
            ("S12", Module(Reflexive("r"), SubProperty("r", "s"), SubClassOf(Class("A"), All("s", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //X1 chain-head loop mints nothing {A [= exists r.B, B [= exists s.A, r.s [= t, A [= forall t.C}
            //CONSISTENT; C NOT entailed at A (the pin). Countermodel (the 2-cycle does NOT close): {a0,b0,a1,b1},
            //A={a0,a1}, B={b0,b1}, C={a1}, r={(a0,b0),(a1,b1)}, s={(b0,a1),(b1,a1)}, t=r.s={(a0,a1),(a1,a1)}.
            //a0 in A but a0 not in C => A[=C refuted; the chain-head loop t(a1,a1) forces only a1 in C, never
            //the predecessor a0. Nothing else entailed. ALC={A,B,C}, HasSelf={}; W=[A,B,C]. Pairs {}.
            ("X1", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), Some("s", Class("A"))), Chain("t", "r", "s"), SubClassOf(Class("A"), All("t", Class("C")))), true, ContextPath.ContextDecided, []),

            //X2 HasSelf on non-simple {Trans(r), A [= exists r.Self} DELEGATED. exists r.Self requires r simple
            //(KR2006 Def 5); Trans(r) makes r non-simple => remainder NonSimpleRoleInSelfRestriction(r) => the
            //second gate delegates. TRUE verdict CONSISTENT ({a},A={a},r={(a,a)}). HasSelf replacement delegated
            //pin AND a Transitive-still-delegates pin. MU8 catcher (drop simple guard => path flips).
            ("X2", Module(Transitive("r"), SubClassOf(Class("A"), HasSelf("r"))), true, ContextPath.Delegated, null),

            //X3 Irr on non-simple {Trans(r), Irr(r)} DELEGATED. Irr(r) requires r simple (KR2006); Trans(r)
            //makes r non-simple => remainder NonSimpleRoleInIrreflexivity(r) => delegate. TRUE verdict CONSISTENT
            //(a strict transitive order {a,b}, r={(a,b)}). Irreflexive replacement delegated pin AND a
            //Transitive-still-delegates pin.
            ("X3", Module(Transitive("r"), Irreflexive("r")), true, ContextPath.Delegated, null),

            //X4 loops x counting guard {A [= exists r.Self, Functional(r)} DELEGATED. The survey
            //ADMITS both A [= exists r.Self and Functional(r), so the module survey-admits; the loops x
            //counting guard then trips: A [= exists r.Self registers base(r) in the loop set L, and Func(r)'s DL4
            //counting target Forward(Rep(r)) lands in L => remainder LoopCapableRoleInNumberRestriction(r) => the
            //second gate delegates. TRUE verdict CONSISTENT ({a},A={a},r={(a,a)}). (X4 = GL1 minus the exists r.B
            //witness.) It delegates via the loops x counting guard, not a whole-module survey reject.
            ("X4", Module(SubClassOf(Class("A"), HasSelf("r")), Functional("r")), true, ContextPath.Delegated, null),

            //RefBeyond loops x counting guard {Ref(r), A [= <=1 r.B} DELEGATED. The survey ADMITS
            //both Ref(r) and the max-1, so the module survey-admits; the loops x counting guard then trips:
            //Ref(r) seeds Forward(Rep(r)) into the loop set L, and the <=1 r.B DL4 counting target lands in L =>
            //remainder LoopCapableRoleInNumberRestriction(r) => delegate. TRUE verdict CONSISTENT
            //({a},A={a},r={(a,a)},B={a}). (RefBeyond = GL2 minus the exists r.B witness.) Reflexive has no
            //simplicity guard (KR2006); it delegates here via the loops x counting guard.
            ("RefBeyond", Module(Reflexive("r"), SubClassOf(Class("A"), Max("r", 1, Class("B")))), true, ContextPath.Delegated, null),

            //TE1 transitivity entailment {Trans(r), A [= exists r.B, B [= exists r.C, exists r.C [= D} CONSISTENT.
            //Model {a,b,c}, A={a}, B={b}, C={c}, r={(a,b),(b,c),(a,c)} (transitive closure), D={a,b}. exists r.C
            //[= D with r NON-SIMPLE goes through the mirror conversion C [= forall r-.D. b-r->c => B [= D (without
            //Trans); a-r->c via the transitivity automaton => A [= D (WITHOUT Trans, a-r->c not forced, A[=D
            //fails). C[=D not entailed. ALC={A,B,C,D}, HasSelf={}; W=[A,B,C,D]. Transitive replacement content
            //row (r-automaton + non-simple-exists mirror).
            ("TE1", Module(Transitive("r"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), Some("r", Class("C"))), SubClassOf(Some("r", Class("C")), Class("D"))), true, ContextPath.ContextDecided,
                [Sub("A", "D"), Sub("B", "D")]),

            //TE1-noTrans {A [= exists r.B, B [= exists r.C, exists r.C [= D} CONSISTENT -- TE1 minus Trans(r).
            //With r SIMPLE the exists r.C [= D consumer is the direct DL3 r(z1,x), C(x) -> D(z1): b in B has an
            //r-successor in C, so B [= D. A's r-successor lies in B, never C, so A [= D never forms -- the
            //discriminating half A->D requires Trans, and its absence from the EXACT set is what pins that.
            //ALC={A,B,C,D}, HasSelf={}; W=[A,B,C,D]. Pairs {B->D} only.
            ("TE1-noTrans", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), Some("r", Class("C"))), SubClassOf(Some("r", Class("C")), Class("D"))), true, ContextPath.ContextDecided,
                [Sub("B", "D")]),

            //LR3 length-3 regular ladder {r1.r2.r3 [= t, A [= exists r1.B, B [= exists r2.C, C [= exists r3.E,
            //exists t.E [= G} CONSISTENT. Model {a,b,c,e}, A={a}, B={b}, C={c}, E={e}, G={a}, r1={(a,b)},
            //r2={(b,c)}, r3={(c,e)}, t={(a,e)}. a gets the length-3 t-edge to e in E => a in exists t.E => A [= G.
            //Regular (r1<r2<r3<t). Nothing else entailed. ALC={A,B,C,E,G}, HasSelf={}; W=[A,B,C,E,G]. Chain
            //replacement content row (length-3 automaton + <-ordering).
            ("LR3", Module(Chain("t", "r1", "r2", "r3"), SubClassOf(Class("A"), Some("r1", Class("B"))), SubClassOf(Class("B"), Some("r2", Class("C"))), SubClassOf(Class("C"), Some("r3", Class("E"))), SubClassOf(Some("t", Class("E")), Class("G"))), true, ContextPath.ContextDecided,
                [Sub("A", "G")]),

            //RR-cyclic4 {p.q [= q, q.r [= r, r.s [= s, s.p [= p} DELEGATED (RboxIrregular). Each RIA forces the
            //leading letter < head: p<q<r<s<p -- a cycle admitting no strict partial order => not regular =>
            //IsRegular rejects the whole module. Semantic verdict CONSISTENT (RBox-only, empty model). Chain
            //replacement delegated pin. MU9 catcher (regularity guard bypassed => irregular module decided).
            ("RR-cyclic4", Module(Chain("q", "p", "q"), Chain("r", "q", "r"), Chain("s", "r", "s"), Chain("p", "s", "p")), true, ContextPath.Delegated, null),

            //RRinv {R.R- [= R} DELEGATED (RboxIrregular, the KR2006 p.2 remark). R.R- matches no Def-2 form: not
            //R.R (transitivity), not R-, and any si<R reading needs R<R (false under strict irreflexive <, using
            //s<r iff s-<r). Not regular => delegate. CONSISTENT (trivial). Second chain-irregular pin.
            ("RRinv", RRinvModule(), true, ContextPath.Delegated, null),

            //Automaton-budget {s0 transitive; s_i = super of s_{i-1} o s_{i-1} (doubling tower); A [= forall
            //s13.B} DELEGATED (RboxAutomaton state-budget-exceeded). REGULAR + legal (a transitive-tower is
            //admissible-transitivity), but the product automaton for the top role exceeds the 4096-state budget
            //=> the whole module delegates BEFORE Saturate. CONSISTENT. Chain replacement delegated pin
            //(state-budget-driven, not a legality finding).
            ("AutomatonBudget", AutomatonBudgetModule(), true, ContextPath.Delegated, null),

            //Delegation slice: one row per rejected family; path Delegated, verdict = the fallback's (all
            //consistent), no subsumption set asserted.

            //PositiveUnionDelegates {A [= B or C} is ContextDecided (the row keeps its
            //...Delegates name as its identity). The positive union is admitted and clausifies to the DL1
            //disjunctive head A(x) -> B(x) | C(x); consistent (model: all empty), and the disjunctive head is
            //not a decided subsumption for any pair, so the swept signature [A,B,C] yields no pairs.
            ("PositiveUnionDelegates", Module(SubClassOf(Class("A"), Union(Class("B"), Class("C")))), true, ContextPath.ContextDecided, []),

            //DisjointUnionDelegates {DisjointUnion(A; B, C)} is ContextDecided (the row keeps its
            //...Delegates name as its identity). Lowers to the covering A [= B or C, the member
            //inclusions B [= A and C [= A, and B and C [= bot; consistent (model: all empty), and the member
            //inclusions are the two decided pairs over the swept signature [A,B,C].
            ("DisjointUnionDelegates", Module(DisjointUnion("A", Class("B"), Class("C"))), true, ContextPath.ContextDecided,
                [Sub("B", "A"), Sub("C", "A")]),

            //MaxCardinalityDelegates {A [= <=1 r.B} is ContextDecided (the row keeps its
            //...Delegates name as its identity). Max-1 is admitted; A is satisfiable with 0 or 1 r-B
            //successor, so no merge and no clash. B occurs only as the max filler and enters W SOLELY via the
            //signature widening. W=[A,B], pairs {}. FreshRoles=1=CountingRoles; r simple, not loop-capable => ContextDecided.
            ("MaxCardinalityDelegates", Module(SubClassOf(Class("A"), Max("r", 1, Class("B")))), true, ContextPath.ContextDecided, []),

            //MinCardinalityTwoDelegates {A [= >=2 r.B} is ContextDecided under S1 (= MC2/R15; the row keeps its
            //...Delegates name as its identity). The two >=2 witnesses are distinct-by-inequality and never merge
            //without a Func; A satisfiable. B via the signature widening. W=[A,B], pairs {}. FreshRoles=0; single-inequality
            //Horn head in-grammar => ContextDecided.
            ("MinCardinalityTwoDelegates", Module(SubClassOf(Class("A"), Min("r", 2, Class("B")))), true, ContextPath.ContextDecided, []),

            //MinOneSubclassDelegates {>=1 r.B [= C} is ContextDecided (the row keeps
            //its ...Delegates name as its identity). The subclass-position minimum lowers through the coded
            //contrapositive dual top [= C or <=0 r.B into a positive union over the DL4 counting machinery;
            //consistent (model: all empty, no r-edges), and nothing subsumes over the swept signature [B,C].
            ("MinOneSubclassDelegates", Module(SubClassOf(Min("r", 1, Class("B")), Class("C"))), true, ContextPath.ContextDecided, []),

            //FunctionalDelegates {Func(r)} is ContextDecided (the row keeps its ...Delegates
            //name as its identity). Func(r) lowers to Top [= <=1 r (DL4); no existential, no witness, no clash,
            //so no named class is swept. W=[], pairs []. FreshRoles=1=CountingRoles; r not loop-capable =>
            //ContextDecided.
            ("FunctionalDelegates", Module(Functional("r")), true, ContextPath.ContextDecided, []),

            //HasValue: A [= exists r.{i} -- ContextDecided under the SROIQ Tier 3 nominal jurisdiction (the
            //row keeps its ...Delegates name as its identity). The value restriction lowers through the fresh
            //singleton N_i; A's r-successor is the constant i, which entails no named subsumption. W=[], pairs [].
            ("HasValueDelegates", Module(SubClassOf(Class("A"), HasValue("r", "i"))), true, ContextPath.ContextDecided, []),

            //OneOf: A [= {i} -- ContextDecided under SROIQ Tier 3 (the row keeps its ...Delegates name as its
            //identity). The singleton enumeration lowers to the DL8 head A(x) -> x ~ i, an unnamed superclass
            //entailing no named subsumption. W=[], pairs [].
            ("OneOfDelegates", Module(SubClassOf(Class("A"), OneOf("i"))), true, ContextPath.ContextDecided, []),

            //ABox: A [= B, a:A -- ContextDecided over the SROIQ ground slice (the row keeps its
            //...Delegates name as its identity). The class assertion lowers to the marker GCI O_a [= A, and
            //A [= B is entailed and swept (A and B stay named-class query contexts; the marker O_a is excluded
            //from the signature classes). The module is consistent and entails A [= B.
            ("AboxDelegates", Module(SubClassOf(Class("A"), Class("B")), ClassAssertion(Class("A"), Individual("a"))), true, ContextPath.ContextDecided, [Sub("A", "B")]),

            //data restriction: A [= exists d.xsd:integer -- ContextDecided through the SROIQ
            //datatype sidecar (the row keeps its ...Delegates name as its identity). A superclass-position data
            //existential over a satisfiable datatype lowers to a demand marker the sidecar decides
            //consistent, so A is satisfiable with an empty subsumption set.
            ("DataRestrictionDelegates", Module(SubClassOf(Class("A"), DataSome("d", Integer))), true, ContextPath.ContextDecided, []),

            //NegativeComplementDelegates {not A [= B} is ContextDecided (the row keeps
            //its ...Delegates name as its identity). The negative-position complement lowers to the covering
            //top [= A or B, a DL1 disjunctive head; consistent (model: one element in A), and neither named
            //class subsumes the other over the swept signature [A,B].
            ("NegativeComplementDelegates", Module(SubClassOf(Complement(Class("A")), Class("B"))), true, ContextPath.ContextDecided, []),

            //NegativeUniversalDelegates {forall r.C [= D} is ContextDecided (the row keeps
            //its ...Delegates name as its identity). The subclass-position universal lowers through the
            //faithful rewrite top [= exists r.not C or D into a positive union over fresh names; consistent
            //(model: one element in D with no r-edges — vacuously in the universal, hence rightly in D), and
            //nothing subsumes over the swept signature [C,D].
            ("NegativeUniversalDelegates", Module(SubClassOf(All("r", Class("C")), Class("D"))), true, ContextPath.ContextDecided, []),

            //Reference-study additions (BC1-BC6) -- six ground truths derived independently from the axioms
            //alone. Each carries its model or derivation condensed and states the swept signature W.

            //BC1 chain-inverse quotient irregular {R o Q [= P, Inv(P,Q)} DELEGATED (RboxIrregular). Inv(P,Q) makes
            //Q == P-, so the interior chain letter Q shares P's base after the told-cycle quotient and the regularity
            //interior check (base(letter) == base(super)) refuses the whole module. Quotient-MEDIATED companion to
            //the SYNTACTIC R o R- [= R of RRinv. Semantic verdict CONSISTENT (RBox-only, empty model). No pair set.
            ("BC1-ChainInvIrregular", Module(Chain("P", "R", "Q"), Inverse("P", "Q")), true, ContextPath.Delegated, null),

            //BC2 inverse-collapse accept {R o Q- [= P, Inv(P,Q), A [= exists R.X, X [= exists P.Y, exists P.Y [= Z}
            //CONSISTENT. Inv(P,Q) makes Q- == P, so the chain is the tail-recursive R o P [= P (KR2006 form v, R < P)
            //and stays regular -- the deliberate ACCEPT-through-inverse-collapse oracle choice (cross-reasoner
            //contentious). X [= exists P.Y and exists P.Y [= Z give X [= Z; A [= exists R.X plus the collapsed chain
            //give a a P-successor y in Y, so a in exists P.Y [= Z => A [= Z. ALC={A,X,Y,Z} (chain/Inv name nothing),
            //HasSelf={}; W=[A,X,Y,Z]. Pins RBox-divergence 2 as a deliberate accept.
            ("BC2-InvChainAccept", Bc2Module(), true, ContextPath.ContextDecided,
                [Sub("A", "Z"), Sub("X", "Z")]),

            //BC3 reflexive-domain collapse {R [= S, Ref(R), Domain(S,A), D [= E} CONSISTENT. Ref(R) forces R(x,x);
            //R [= S lifts it to S(x,x), so every x has an S-successor and Domain(S,A) (exists S.Top [= A) puts every
            //x in A => A == Top. Hence every OTHER swept class [= A: D [= A, E [= A; plus D [= E asserted. A is SWEPT
            //because Domain(S,A) translates its domain class into the ALC signature, so (D,A)/(E,A) are observable
            //with NO spectator adjustment. ALC={A,D,E} (A from Domain, D/E from the D [= E inclusion), HasSelf={};
            //W=[A,D,E]. Pins closure x domain-GCI x self-variant.
            ("BC3-RefDomainCollapse", Module(SubProperty("R", "S"), Reflexive("R"), Domain("S", Class("A")), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("D", "E"), Sub("D", "A"), Sub("E", "A")]),

            //BC4 reflexive interior-chain close {Ref(R), H o R [= T, A [= exists H.C, B == exists T.C} CONSISTENT.
            //A [= exists H.C gives a an H-successor c in C; Ref(R) forces R(c,c); the chain H o R [= T composes
            //H(a,c) o R(c,c) => T(a,c), so a in exists T.C == B => A [= B. Ref on the chain sub-letter R closes the
            //chain -- a trigger S10/S11 do not hit. (B,A) refuted by an extra-T-edge countermodel. ALC={A,C,B}
            //(chain/Ref name nothing; A/C from ax3, B from the equivalence), HasSelf={}; W=[A,C,B].
            ("BC4-RefInteriorClose", Module(Reflexive("R"), Chain("T", "H", "R"), SubClassOf(Class("A"), Some("H", Class("C"))), Equivalent(Class("B"), Some("T", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //BC5 equivalent-properties reasoning-through {EquivProps(R,S), A [= exists S.B, exists R.B [= C}
            //CONSISTENT. R == S (mutual told sub-roles), so a's S-successor in B is an R-successor in B: A [= exists
            //S.B and exists R.B [= C give A [= C through the quotient. (C,A) refuted. EL-decidable differential twin.
            //ALC={A,B,C} (EquivProps names nothing; A/B from ax2, C from ax3), HasSelf={}; W=[A,B,C].
            ("BC5-EquivPropThrough", Module(EquivalentProperties("R", "S"), SubClassOf(Class("A"), Some("S", Class("B"))), SubClassOf(Some("R", Class("B")), Class("C"))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //BC6 symmetric self-loop {Sym(R), R o R- [= R, A [= exists R.B, B [= exists R.C, exists R.(exists R.C) [= D}
            //CONSISTENT. Sym(R) makes R- == R, so R o R- [= R collapses to the transitivity form R o R [= R (regular).
            //A [= exists R.B and B [= exists R.C give the 2-step path a-R->b-R->c in C, so a in exists R.(exists R.C)
            //[= D => A [= D (this HOLDS in PLAIN ALC, no transitivity). (B,D) is the sym+trans-discriminating pair:
            //symmetry plus transitivity mint the self-loop b-R->b (R(b,c) o R(c,b) => R(b,b)), so b in
            //exists R.(exists R.C) => B [= D. C is NOT captured (isolated-C countermodel). ALC={A,B,C,D}, HasSelf={};
            //W=[A,B,C,D].
            ("BC6-SymSelfLoop", Bc6Module(), true, ContextPath.ContextDecided,
                [Sub("A", "D"), Sub("B", "D")]),

            //The Asymmetric and DisjointObjectProperties rows. Asymmetric records the pair (r, r-) and
            //DisjointObjectProperties the pairwise operand pairs; EmitRoleDisjointness emits one clash clause
            //per pair under the simplicity and reserved-role guards. The disjointness rows carry a descriptive
            //suffix on their row id (D1..D7) so they do not collide with the D-rows above; the two
            //...Delegates rows keep their names as their identity.

            //Asy {Asy(r)} ContextDecided: Asymmetric is admitted. r simple, so
            //EmitRoleDisjointness emits the mixed clash r(z1,x) AND r(x,z1) -> bot; CONSISTENT (model {d}, r empty:
            //asymmetry vacuous, no class forces an r-edge). No named class => W=[], subs [].
            ("AsymmetricDelegates", Module(Asymmetric("r")), true, ContextPath.ContextDecided, []),

            //Dis {Dis(r,s)} ContextDecided: DisjointObjectProperties is admitted.
            //Both operands simple => clash r(z1,x) AND s(z1,x) -> bot; CONSISTENT ({d}, r=s empty). W=[], subs [].
            ("DisjointObjectPropertiesDelegates", Module(DisjointProperties("r", "s")), true, ContextPath.ContextDecided, []),

            //A1 {Sym(r), Asy(r), A [= exists r.B, D [= E} CONSISTENT (module), A UNSAT (class). Sym(r) makes r
            //self-inverse (Rep(r-)=Rep(r)), so the Asy pair's two atoms share the rep and DlClause.Create dedupes
            //to r(z1,x) -> bot (KR2006 Sym(r)+Asy(r) <=> r empty). A [= exists r.B then empties A. Spectator D [= E
            //and filler B make A's unsatisfiability observable. ALC={A,B,D,E}, HasSelf={}; W=[A,B,D,E]. EL-diff.
            ("A1", Module(Symmetric("r"), Asymmetric("r"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "D"), Sub("A", "E"), Sub("D", "E")]),

            //A2 {Asy(r), A [= exists r.Self, D [= E} CONSISTENT (module), A UNSAT (class). A [= exists r.Self seeds
            //base(r) in L; the Asy diagonal collapses to Self_r(x) -> bot (derived irreflexivity, KR2006
            //Asy(R)=>Irr(R)). The producer A(x)->Self_r(x) then empties A. Spectator D [= E observes it
            //(without it W={A} and the emptiness emits no pair). ALC={A,D,E}, HasSelf={A}; W=[A,D,E]. EL DELEGATES.
            ("A2", Module(Asymmetric("r"), SubClassOf(Class("A"), HasSelf("r")), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("A", "D"), Sub("A", "E"), Sub("D", "E")]),

            //A3 {Asy(r), Ref(r)} INCONSISTENT. Ref(r) emits top -> Self_r(x) and seeds base(r) in L0; the Asy
            //diagonal collapses to Self_r(x) -> bot. The chain top -> Self_r -> bot fires the empty clause in
            //v_top (domain non-empty; KR2006 Asy(R)=>Irr(R) meeting Ref(R)). r simple => ContextDecided. No class
            //swept => W=[]. Verdict (false, []). EL-diff (EL's told-reflexivity tier decides it).
            ("A3", Module(Asymmetric("r"), Reflexive("r")), false, ContextPath.ContextDecided, []),

            //A4 {A [= exists t.B, t [= r, Inv(t,u), u [= r, Asy(r)} CONSISTENT (module), A UNSAT (class). Asy(r)
            //emits the mixed clash r(z1,x) AND r(x,z1) -> bot. An A-element's witness t-edge t(a,b) promotes both
            //ways: t [= r gives r(a,b); Inv(t,u) makes u == t-, and u [= r gives r(b,a). The shared reverse pair
            //fires the clash => A [= bot. r,t,u simple. ALC={A,B}, HasSelf={}; W=[A,B]; filler B observes A->B.
            ("A4", Module(SubClassOf(Class("A"), Some("t", Class("B"))), SubProperty("t", "r"), Inverse("t", "u"), SubProperty("u", "r"), Asymmetric("r")), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //N1 {Asy(r), A [= exists r.B, B [= exists r.A} CONSISTENT, NOTHING unsat (the pin). Asy(r) emits the
            //mixed clash, but the exists-witnesses form a chain of FRESH successors (a-r->f0 in B, f0-r->f1 in A,
            //...): no node's r-successor is also its r-predecessor, so no shared reverse pair ever forms. Kills the
            //both-edges-anywhere misreading. ALC={A,B}, HasSelf={}; W=[A,B]. Pairs {}. MU2/MU5 catcher.
            ("N1", Module(Asymmetric("r"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), Some("r", Class("A")))), true, ContextPath.ContextDecided, []),

            //N2 {Dis(r, owl:bottomObjectProperty), A [= exists r.B} CONSISTENT, NOTHING unsat, ContextDecided.
            //bottom is NOT guarded (bottom^I empty => Dis(R,bottom) is a tautology), so the module ADMITS with NO
            //remainder and the ordinary clash r(z1,x) AND bottom(z1,x) -> bot is emitted; it fires only on a
            //bottom-edge, and none is derived, so it is inert. ALC={A,B}, HasSelf={}; W=[A,B]. Pairs {}. The
            //bottom-not-over-delegated completeness face. The second operand is the bottom reference.
            ("N2", Module(DisjointProperties(Property("r"), BottomObjectPropertyRef()), SubClassOf(Class("A"), Some("r", Class("B")))), true, ContextPath.ContextDecided, []),

            //RR11 {Asy(bottom), A [= exists r.B} CONSISTENT, NOTHING unsat, ContextDecided. The bottom Asymmetric
            //property is the bottom carve-out's asymmetric side -- the module-level scan skips it, so the module
            //admits and EmitRoleDisjointness emits the ordinary mixed clash bottom(z1,x) AND bottom(x,z1) -> bot,
            //inert because no bottom-edge is derived. A [= exists r.B mints a fresh r-successor, nothing entailed.
            //ALC={A,B}, W=[A,B]. Pairs {}. The Asy-side complement of N2.
            ("RR11", Module(new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, BottomObjectPropertyRef()) { Origin = Origin("rr11asy") }, SubClassOf(Class("A"), Some("r", Class("B")))), true, ContextPath.ContextDecided, []),

            //RR12 {Declaration(ObjectProperty(top)), A [= exists r.B, B [= C} CONSISTENT, NOTHING unsat,
            //ContextDecided. A declaration is not a role position, so the module-level scan does not fire on it
            //(the MU7 over-fire kill), the survey admits the declaration, and the module decides. The only
            //entailed named pair is B [= C; A [= exists r.B mints a fresh successor and a complex superclass is
            //no named signature widening for A. ALC={A,B,C}, W=[A,B,C]. Pairs {(B,C)}.
            ("RR12", Module(new OwlDeclarationAxiom(OwlEntityKind.ObjectProperty, new NamedNode(OwlVocabulary.TopObjectProperty)) { Origin = Origin("rr12decl") }, SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), Class("C"))), true, ContextPath.ContextDecided,
                [Sub("B", "C")]),

            //D1-HierarchySharedPair {Dis(r,s), t [= r, t [= s, A [= exists t.B} CONSISTENT (module), A UNSAT. THE
            //plain two-role-literal clash on distinct reps (the shape section 1.2's no-engine-change claim rests
            //on). Dis(r,s) emits r(z1,x) AND s(z1,x) -> bot; a witness t-edge t(a,b) promotes via t [= r and t [= s
            //to the SAME pair (a,b) in r AND s => clash => A [= bot. Named filler B observes A->B. W=[A,B].
            ("D1-HierarchySharedPair", Module(DisjointProperties("r", "s"), SubProperty("t", "r"), SubProperty("t", "s"), SubClassOf(Class("A"), Some("t", Class("B")))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //D2-DistinctSuccessors {Dis(r,s), A [= exists r.B, A [= exists s.B} CONSISTENT, NOTHING unsat (the
            //pin). The two existentials mint DISTINCT fresh successors r(a,f0) and s(a,f1) -- two pairs, no shared
            //(u,v) in r AND s. Disjointness is pair-level, not co-occurrence-level. ALC={A,B}, HasSelf={}; W=[A,B].
            //Pairs {} (A has a B-successor but is not itself B).
            ("D2-DistinctSuccessors", Module(DisjointProperties("r", "s"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("A"), Some("s", Class("B")))), true, ContextPath.ContextDecided, []),

            //D3-EquivalentCollapse {Dis(r,s), r == s (EquivalentObjectProperties), A [= exists r.B} CONSISTENT
            //(module), A UNSAT. EquivProps(r,s) makes Rep(r)=Rep(s), so Dis(r,s) dedupes to r(z1,x) -> bot (role
            //emptiness, KR2006 Dis(r,r) <=> r empty). A [= exists r.B empties A. ALC={A,B}, HasSelf={}; W=[A,B].
            ("D3-EquivalentCollapse", Module(DisjointProperties("r", "s"), EquivalentProperties("r", "s"), SubClassOf(Class("A"), Some("r", Class("B")))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //D4-InverseSpelling {Dis(r, s-), r [= s-, A [= exists r.B} CONSISTENT (module), A UNSAT. Dis(r, s-)
            //normalizes the inverse operand by argument flip => clash r(z1,x) AND s(x,z1) -> bot. A witness r(a,f0)
            //plus r [= s- (DL6, r(z1,x)->s(x,z1)) gives s(f0,a); the clash matches at x=f0, z1=a => A [= bot. r,s
            //simple. Named filler B. ALC={A,B}, HasSelf={}; W=[A,B]. The second Dis operand is the inverse of s.
            ("D4-InverseSpelling", Module(DisjointProperties(Property("r"), InverseProperty("s")), SubPropertyInverse("r", "s"), SubClassOf(Class("A"), Some("r", Class("B")))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //D5-TwoBaseDiagonal {Dis(r,s), A [= exists r.Self, A [= exists s.Self, D [= E} CONSISTENT (module),
            //A UNSAT. Both Self axioms seed base(r) AND base(s) into L; the Dis diagonal Self_r(x) AND Self_s(x) ->
            //bot is emitted (two distinct bases, no collapse). Producers A(x)->Self_r(x) and A(x)->Self_s(x) empty
            //A. Spectator D [= E observes it (the section 4.3 variant pin covers only the single-base A2 shape).
            //ALC={A,D,E}, HasSelf={A}; W=[A,D,E]. MU4 catcher with A2.
            ("D5-TwoBaseDiagonal", Module(DisjointProperties("r", "s"), SubClassOf(Class("A"), HasSelf("r")), SubClassOf(Class("A"), HasSelf("s")), SubClassOf(Class("D"), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("A", "D"), Sub("A", "E"), Sub("D", "E")]),

            //D6-ReflexivePairClash {Dis(r,s), Ref(r), Ref(s)} INCONSISTENT. Ref(r) emits top->Self_r(x) (base(r)
            //in L0), Ref(s) emits top->Self_s(x) (base(s) in L0); the Dis diagonal Self_r(x) AND Self_s(x) -> bot
            //is emitted. Every element carries both loops => top->Self_r->...->bot in v_top. r,s simple. No class
            //swept => W=[]. Verdict (false, []).
            ("D6-ReflexivePairClash", Module(DisjointProperties("r", "s"), Reflexive("r"), Reflexive("s")), false, ContextPath.ContextDecided, []),

            //D7-NonAdjacentPair {Dis(r,s,t), u [= r, u [= t, A [= exists u.B} CONSISTENT (module), A UNSAT. The
            //n-ary intake makes pairwise pairs (r,s),(r,t),(s,t); the clash rides the NON-ADJACENT (r,t): a u-edge
            //u(a,b) promotes via u [= r and u [= t to r(a,b) and t(a,b), the shared pair in r AND t => the (r,t)
            //clash fires => A [= bot. Named filler B. r,s,t,u simple. ALC={A,B}, HasSelf={}; W=[A,B]. MU1 catcher.
            ("D7-NonAdjacentPair", Module(DisjointProperties("r", "s", "t"), SubProperty("u", "r"), SubProperty("u", "t"), SubClassOf(Class("A"), Some("u", Class("B")))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //G1 {Trans(r), Asy(r)} DELEGATED. Trans(r) makes r non-simple; the asymmetric guard trips on r =>
            //remainder NonSimpleRoleInAsymmetry(r) => delegate. TRUE verdict CONSISTENT (a strict transitive order
            //is transitive and asymmetric; or r empty). Fallback drops both => CONSISTENT. MU3 catcher.
            ("G1", Module(Transitive("r"), Asymmetric("r")), true, ContextPath.Delegated, null),

            //G2 {Trans(s), Dis(r,s)} DELEGATED. Trans(s) makes s non-simple; the disjointness guard tests BOTH
            //operands and trips on the SECOND, s => remainder NonSimpleRoleInRoleDisjointness(s) => delegate.
            //CONSISTENT (empty model). Fallback CONSISTENT. MU8 (and MU3) catcher -- the second-position operand.
            ("G2", Module(Transitive("s"), DisjointProperties("r", "s")), true, ContextPath.Delegated, null),

            //G3 {r o s [= t, Dis(t,u)} DELEGATED. The regular chain r o s [= t makes t non-simple; Dis(t,u) trips
            //on t => remainder NonSimpleRoleInRoleDisjointness(t) => delegate. CONSISTENT (RBox-only, empty model).
            //Fallback drops the chain and Dis => CONSISTENT.
            ("G3", Module(Chain("t", "r", "s"), DisjointProperties("t", "u")), true, ContextPath.Delegated, null),

            //G4 {Dis(r, owl:topObjectProperty)} DELEGATED, SAFE as a row (SI-2). The top operand trips the
            //soundness-forced reserved guard => remainder ReservedRoleInRoleDisjointness(top IRI) => delegate.
            //TRUE verdict CONSISTENT (Dis(r,top) <=> r empty; empty r is a model). Fallback drops Dis => CONSISTENT.
            //The second Dis operand is the top reference. {Asy(top)} is instead a clausifier pin (SI-1), not a row.
            ("G4", Module(DisjointProperties(Property("r"), TopObjectPropertyRef())), true, ContextPath.Delegated, null),

            //The equality tier (Eq/Ineq over minted successors). Functional/InverseFunctional lower to
            //unqualified max-1 (DL4); superclass max-1/exact-1 and min-n (S1) admit; the Eq rule rewrites
            //merged-successor atoms and the Ineq rule collapses a self-inequality to bot. Every
            //clash/entailment row carries a NAMED filler or spectator so the unsatisfiable class is
            //observable, and B/C/Q fillers enter the swept W via the widened BuildSweepSignature. The
            //equality-tier N rows carry a -Eq suffix so they do not collide with the N1/N2 row ids above.

            //E1 (R1) {A [= exists r.B1, A [= exists r.B2, B1 AND B2 [= bot, Func(r)} CONSISTENT (module), A UNSAT.
            //Func(r) DL4 merges the two r-witnesses (f2 = f1, oriented by mint order); Eq rewrites B2(f2)->B2(f1),
            //so f1 carries B1 AND B2 and the disjointness empties A. B1,B2 named fillers. W=[A,B1,B2].
            ("E1", Module(SubClassOf(Class("A"), Some("r", Class("B1"))), SubClassOf(Class("A"), Some("r", Class("B2"))), SubClassOf(Intersection(Class("B1"), Class("B2")), NothingReference), Functional("r")), true, ContextPath.ContextDecided,
                [Sub("A", "B1"), Sub("A", "B2")]),

            //E2 (R2) E1 minus Func: the two witnesses stay DISTINCT and never merge; disjointness satisfied off the
            //diagonal. The differential twin that separates merging from mere co-existence. W=[A,B1,B2], pairs {}.
            ("E2", Module(SubClassOf(Class("A"), Some("r", Class("B1"))), SubClassOf(Class("A"), Some("r", Class("B2"))), SubClassOf(Intersection(Class("B1"), Class("B2")), NothingReference)), true, ContextPath.ContextDecided, []),

            //E3 (R3) {A [= exists r.B, A [= exists r.C, Func(r), B AND C [= D, exists r.D [= E} CONSISTENT, A [= E.
            //Func merges the r-witnesses; the merged successor carries B AND C, so it is in D; the DL3 consumer
            //exists r.D [= E fires E on the A-element. W=[A,B,C,D,E], pairs {A->E}.
            ("E3", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("A"), Some("r", Class("C"))), Functional("r"), SubClassOf(Intersection(Class("B"), Class("C")), Class("D")), SubClassOf(Some("r", Class("D")), Class("E"))), true, ContextPath.ContextDecided,
                [Sub("A", "E")]),

            //E4 (R4) E3 minus Func: distinct witnesses, neither in B AND C, no D forms, exists r.D [= E vacuous. The
            //E3 differential twin. W=[A,B,C,D,E], pairs {}.
            ("E4", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("A"), Some("r", Class("C"))), SubClassOf(Intersection(Class("B"), Class("C")), Class("D")), SubClassOf(Some("r", Class("D")), Class("E"))), true, ContextPath.ContextDecided, []),

            //E5 (R5) depth-2 double-Func cascade {A [= exists r.B, A [= exists r.C, Func(r), B [= exists s.D,
            //C [= exists s.E, Func(s), D AND E [= bot} CONSISTENT (module), A UNSAT. Func(r) merges the r-witnesses
            //onto one node carrying B AND C, which spawns BOTH s-witnesses (Succ re-fires on the merged node);
            //Func(s) merges those, and D AND E [= bot empties them, condemning A. W=[A,B,C,D,E], pairs
            //{A->B, A->C, A->D, A->E}. Exercises both Eq dispatch directions at depth 2.
            ("E5", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("A"), Some("r", Class("C"))), Functional("r"), SubClassOf(Class("B"), Some("s", Class("D"))), SubClassOf(Class("C"), Some("s", Class("E"))), Functional("s"), SubClassOf(Intersection(Class("D"), Class("E")), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A", "B"), Sub("A", "C"), Sub("A", "D"), Sub("A", "E")]),

            //EQ1 (R6) {A [= exists r.B1, A [= exists r.B2, B1 [= Q, B2 [= Q, A [= <=1 r.Q, B1 AND B2 [= bot}
            //CONSISTENT, A UNSAT. Both witnesses are in Q, so the qualified <=1 r.Q counts and merges them; the
            //disjointness empties A. W=[A,B1,B2,Q], pairs {A->B1, A->B2, A->Q, B1->Q, B2->Q}.
            ("EQ1", Module(SubClassOf(Class("A"), Some("r", Class("B1"))), SubClassOf(Class("A"), Some("r", Class("B2"))), SubClassOf(Class("B1"), Class("Q")), SubClassOf(Class("B2"), Class("Q")), SubClassOf(Class("A"), Max("r", 1, Class("Q"))), SubClassOf(Intersection(Class("B1"), Class("B2")), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("A", "B1"), Sub("A", "B2"), Sub("A", "Q"), Sub("B1", "Q"), Sub("B2", "Q")]),

            //EQ2 (R7) {A [= exists r.B1, A [= exists r.B2, A [= <=1 r.Q, B1 [= Q, B1 AND B2 [= bot} CONSISTENT,
            //NOTHING unsat. Only B1 is provably in Q, so the qualified <=1 r.Q counts ONE Q-successor and forces
            //no merge; B1,B2 disjoint off the diagonal. Pins that qualified <=1 does not over-merge. W=[A,B1,B2,Q],
            //pairs {B1->Q}.
            ("EQ2", Module(SubClassOf(Class("A"), Some("r", Class("B1"))), SubClassOf(Class("A"), Some("r", Class("B2"))), SubClassOf(Class("A"), Max("r", 1, Class("Q"))), SubClassOf(Class("B1"), Class("Q")), SubClassOf(Intersection(Class("B1"), Class("B2")), NothingReference)), true, ContextPath.ContextDecided,
                [Sub("B1", "Q")]),

            //N1-Eq (R8) {A [= exists r.B, B [= exists r-.C, InvFunc(r)} CONSISTENT, A [= C. The by-name f(x) ~ y
            //merge: f's two r-predecessors (the owner a as neighbour y, and the minted g in C) merge under <=1 r-;
            //Eq rewrites C(g(x))->C(y) and Pred carries C onto a => A [= C. C enters W ONLY via the signature widening
            //(inverse existential filler). W=[A,B,C], pairs {A->C}. Orientation-sensitive (MU1 catcher).
            ("N1-Eq", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("C"))), InverseFunctional("r")), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //N2-Eq (R9) N1 minus InvFunc: f's r-predecessors need not merge; a stays outside C. The differential
            //twin. C via the signature widening. W=[A,B,C], pairs {}.
            ("N2-Eq", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("C")))), true, ContextPath.ContextDecided, []),

            //N3-Eq (R10) {A [= exists r.B, B [= exists r-.C, C [= A, B [= <=1 r-.A} CONSISTENT, A [= C (qualified
            //InvFunc). f has two r-predecessors both in A (owner a native, minted g via C [= A); <=1 r-.A merges
            //them; Eq rewrites C(g(x))->C(y), Pred carries C onto a => A [= C, and C [= A is asserted, so A == C.
            //C via ALC (C [= A). W=[A,B,C], pairs {A->C, C->A}. Orientation-sensitive (MU1 co-catcher).
            ("N3-Eq", Module(SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("C"))), SubClassOf(Class("C"), Class("A")), SubClassOf(Class("B"), MaxInverse("r", 1, Class("A")))), true, ContextPath.ContextDecided,
                [Sub("A", "C"), Sub("C", "A")]),

            //IF1 (R11) {InvFunc(r)} CONSISTENT, no existential, no witness, no clash. Pins survey/gate admission of
            //InverseFunctional (Top [= <=1 r-, DL4, single-equality Horn head). W=[], pairs [].
            ("IF1", Module(InverseFunctional("r")), true, ContextPath.ContextDecided, []),

            //IF2 (R12) {Func(r), InvFunc(r), A [= exists r.B} CONSISTENT, nothing unsat. A's single witness f is a's
            //only r-successor (Func ok) and a is f's only r-predecessor (InvFunc ok); no merge, no clash. Pins the
            //forward and inverse counting clauses coexisting. W=[A,B], pairs {}.
            ("IF2", Module(Functional("r"), InverseFunctional("r"), SubClassOf(Class("A"), Some("r", Class("B")))), true, ContextPath.ContextDecided, []),

            //IF3 (R13) {Func(r-), A [= exists r.B, B [= exists r-.C} CONSISTENT, A [= C. Func(r-) lowers to the SAME
            //Top [= <=1 r- as InvFunc(r), so IF3 is N1's axiom set spelled through the inverse fold -- same
            //derivation, same A [= C. Pins the Func(r-) == InvFunc(r) fold. C via the signature widening. W=[A,B,C],
            //pairs {A->C}.
            ("IF3", Module(FunctionalInverse("r"), SubClassOf(Class("A"), Some("r", Class("B"))), SubClassOf(Class("B"), SomeInverse("r", Class("C")))), true, ContextPath.ContextDecided,
                [Sub("A", "C")]),

            //MC1 (R14) {A [= >=2 r.B, Func(r)} CONSISTENT (module), A UNSAT. >=2 r.B mints f1,f2 with the emitted
            //inequality f1 != f2 (DL2, count>=2); Func merges f1 = f2; the Ineq rule rewrites f1 != f2 -> f1 != f1
            //-> bot => A [= bot. B occurs only as the min filler and enters W SOLELY via the signature widening. W=[A,B],
            //pairs {A->B}. MU4 (Ineq dropped) and MU12 (widening dropped) catcher.
            ("MC1", Module(SubClassOf(Class("A"), Min("r", 2, Class("B"))), Functional("r")), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //MC3 (R16) {A [= >=2 r.B, A [= <=1 r.B} CONSISTENT (module), A UNSAT (qualified variant). >=2 r.B mints
            //f1,f2 with f1 != f2; qualified <=1 r.B merges the two r-successors-in-B; the Ineq clash empties A. B via
            //the signature widening. W=[A,B], pairs {A->B}. MU12's primary named catcher; MU4 catcher.
            ("MC3", Module(SubClassOf(Class("A"), Min("r", 2, Class("B"))), SubClassOf(Class("A"), Max("r", 1, Class("B")))), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //MC4 (R17) {A [= >=3 r.B, Func(r)} CONSISTENT (module), A UNSAT (multiple pairwise inequalities). >=3 r.B
            //mints f1,f2,f3 with THREE inequalities; Func merges all three; each merge rewrites a distinct inequality
            //to fi != fi => three Ineq applications => A [= bot. B via the signature widening. W=[A,B], pairs {A->B}. MU4
            //catcher (the multi-inequality face).
            ("MC4", Module(SubClassOf(Class("A"), Min("r", 3, Class("B"))), Functional("r")), true, ContextPath.ContextDecided,
                [Sub("A", "B")]),

            //MC5 (R18) {A [= >=2 r.B, A [= <=1 s.B} CONSISTENT, NOTHING unsat (role mismatch). The min is over r, the
            //max over a DIFFERENT role s; <=1 s.B counts s-successors (none forced), so no merge, no clash. Pins the
            //per-(role,filler) DL4 aux: r and s never share a counting role. W=[A,B], pairs {}.
            ("MC5", Module(SubClassOf(Class("A"), Min("r", 2, Class("B"))), SubClassOf(Class("A"), Max("s", 1, Class("B")))), true, ContextPath.ContextDecided, []),
        ];
    }

    /// <summary>The O1 module (KR2016 Figure-1 chain, n = 3): the successor chain, the leaf typing, and the predecessor subsumption inclusions over roles S1 and S2.</summary>
    /// <returns>The O1 module.</returns>
    private static ReasoningModule O1Module()
    {
        List<OwlAxiom> axioms = [];
        for(int i = 0; i < 3; i++)
        {
            for(int j = 1; j <= 2; j++)
            {
                axioms.Add(SubClassOf(Class($"B{i}"), Some($"S{j}", Class($"B{i + 1}"))));
            }
        }

        axioms.Add(SubClassOf(Class("B3"), Class("C3")));

        for(int i = 0; i < 3; i++)
        {
            for(int j = 1; j <= 2; j++)
            {
                axioms.Add(SubClassOf(Some($"S{j}", Class($"C{i + 1}")), Class($"C{i}")));
            }
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The P1 module {A [= exists r.B, B [= forall r-.C, C [= M}: the Pred-rule row that carries a subsumption back over the Skolem edge and is EL-rejected but context-admitted.</summary>
    /// <returns>The P1 module.</returns>
    private static ReasoningModule P1Module()
    {
        return Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), AllInverse("r", Class("C"))),
            SubClassOf(Class("C"), Class("M")));
    }

    /// <summary>The CE4 module {A [= exists r.A, Symmetric(r)}: the cautious-reuse termination witness whose self-edge reuses q_A as its own successor.</summary>
    /// <returns>The CE4 module.</returns>
    private static ReasoningModule Ce4Module()
    {
        return Module(SubClassOf(Class("A"), Some("r", Class("A"))), Symmetric("r"));
    }

    /// <summary>The S10 module {A [= exists r.Self, A [= exists s.B, r.s [= t, A [= forall t.C, B AND C [= bot}: the Self-x-chain-letter row whose r-loop walks the transition variant in place, condemning A; also the chain module for the cautious-reuse context bound.</summary>
    /// <returns>The S10 module.</returns>
    private static ReasoningModule S10Module()
    {
        return Module(
            SubClassOf(Class("A"), HasSelf("r")),
            SubClassOf(Class("A"), Some("s", Class("B"))),
            Chain("t", "r", "s"),
            SubClassOf(Class("A"), All("t", Class("C"))),
            SubClassOf(Intersection(Class("B"), Class("C")), NothingReference));
    }

    /// <summary>The RRinv module {R o R- [= R}: the KR2006 p.2 irregular chain with an inverse tail letter, which the regularity guard rejects whole.</summary>
    /// <returns>The RRinv module.</returns>
    private static ReasoningModule RRinvModule()
    {
        OwlObjectPropertyExpression[] links = [Property("R"), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "R")))];
        OwlPropertyChainAxiom chain = new(links, Property("R")) { Origin = Origin("chaininv") };

        return new ReasoningModule([chain], Violations: []);
    }

    /// <summary>The non-admitted tableau module {R o R- [= R, A [= B, B [= C}: the KR2006 irregular chain the regularity guard rejects whole, so the context tier declines the module and delegates it to the snapshot tableau, which internalises the two class inclusions into per-node disjunctions and spends several rule applications deciding it consistent (fragment-relative on the chain remainder). A generous budget decides through the tableau leg; a one-inference ceiling abstains.</summary>
    /// <returns>The non-admitted tableau module.</returns>
    private static ReasoningModule NonAdmittedTableauModule()
    {
        OwlObjectPropertyExpression[] links = [Property("R"), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "R")))];
        OwlPropertyChainAxiom irregularChain = new(links, Property("R")) { Origin = Origin("nonadmitchain") };

        return Module(
            irregularChain,
            SubClassOf(Class("A"), Class("B")),
            SubClassOf(Class("B"), Class("C")));
    }

    /// <summary>The automaton-budget module: a doubling tower of chain RIAs (s0 transitive; s_i the super of s_{i-1} o s_{i-1}) plus A [= forall s13.B, whose product automaton for the top role exceeds the 4096-state budget so the whole module delegates before saturation.</summary>
    /// <returns>The automaton-budget module.</returns>
    private static ReasoningModule AutomatonBudgetModule()
    {
        List<OwlAxiom> axioms = [Transitive("s0")];
        for(int level = 1; level <= 13; level++)
        {
            axioms.Add(Chain($"s{level}", $"s{level - 1}", $"s{level - 1}"));
        }

        axioms.Add(SubClassOf(Class("A"), All("s13", Class("B"))));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The BC2 module {R o Q- [= P, Inv(P,Q), A [= exists R.X, X [= exists P.Y, exists P.Y [= Z}: the inverse-collapse accept row whose chain becomes the tail-recursive R o P [= P once the told-cycle quotient identifies the inverse tail Q- with P.</summary>
    /// <returns>The BC2 module.</returns>
    private static ReasoningModule Bc2Module()
    {
        OwlObjectPropertyExpression[] links = [Property("R"), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "Q")))];
        OwlPropertyChainAxiom chain = new(links, Property("P")) { Origin = Origin("bc2chain") };

        return Module(
            chain,
            Inverse("P", "Q"),
            SubClassOf(Class("A"), Some("R", Class("X"))),
            SubClassOf(Class("X"), Some("P", Class("Y"))),
            SubClassOf(Some("P", Class("Y")), Class("Z")));
    }

    /// <summary>The BC6 module {Sym(R), R o R- [= R, A [= exists R.B, B [= exists R.C, exists R.(exists R.C) [= D}: the symmetric self-loop row whose R o R- [= R collapses to the transitivity form R o R [= R once symmetry identifies R- with R.</summary>
    /// <returns>The BC6 module.</returns>
    private static ReasoningModule Bc6Module()
    {
        OwlObjectPropertyExpression[] links = [Property("R"), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "R")))];
        OwlPropertyChainAxiom chain = new(links, Property("R")) { Origin = Origin("bc6chain") };

        return Module(
            Symmetric("R"),
            chain,
            SubClassOf(Class("A"), Some("R", Class("B"))),
            SubClassOf(Class("B"), Some("R", Class("C"))),
            SubClassOf(Some("R", Some("R", Class("C"))), Class("D")));
    }

    /// <summary>
    /// The budget-honesty wedge family: a saturation-cost module sized by
    /// <paramref name="n"/> whose
    /// blowup lives in the worklist, not the clausifier or a role automaton. A
    /// descending existential tower <c>A_i [= exists r.A_{i+1}</c> (i in 0..n-1),
    /// globally seeded by <c>Thing [= A0</c> so the whole spine fires in the trivial
    /// consistency context, mints one cautiously-reused anonymous successor per
    /// level; <paramref name="n"/> markers seeded at the deepest level each relay up
    /// the spine over the inverse role via an inverse universal
    /// <c>M_j [= forall r-.M_j</c>, so the Pred rule pushes every marker from each
    /// successor context back into all its ancestors and ancestor contexts
    /// accumulate the growing marker set; a width-<paramref name="n"/> conjunctive
    /// re-entry <c>M_0 AND ... AND M_{n-1} [= Saturated</c> forces a Hyper firing
    /// whose body is the full marker conjunction. The module is Horn-ALCHI (simple
    /// role r, no chain, no transitivity, so no role automaton), empty-remainder,
    /// second-gate clean. The cost is the Pred back-propagation and the conjunctive
    /// Hyper bodies the saturation worklist carries, not any clausifier or automaton
    /// object.
    /// </summary>
    /// <param name="n">The family size: the tower depth and the marker width together.</param>
    /// <returns>The wedge module.</returns>
    internal static ReasoningModule WedgeTowerModule(int n)
    {
        List<OwlAxiom> axioms = [SubClassOf(ThingReference, Class("A0"))];

        for(int level = 0; level < n; level++)
        {
            axioms.Add(SubClassOf(Class($"A{level}"), Some("r", Class($"A{level + 1}"))));
        }

        for(int marker = 0; marker < n; marker++)
        {
            axioms.Add(SubClassOf(Class($"A{n}"), Class($"M{marker}")));
            axioms.Add(SubClassOf(Class($"M{marker}"), AllInverse("r", Class($"M{marker}"))));
        }

        OwlClassExpression[] markers = new OwlClassExpression[n];
        for(int marker = 0; marker < n; marker++)
        {
            markers[marker] = Class($"M{marker}");
        }

        axioms.Add(SubClassOf(Intersection(markers), Class("Saturated")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The total saturation rule applications a context decision spent — the Core, Hyper, Succ, Pred, Elim, Eq, Ineq, Factor, and data-clash applications together, the quantity the inference budget bounds.</summary>
    /// <param name="statistics">The context-saturation telemetry of the decision.</param>
    /// <returns>The total rule applications.</returns>
    private static long TotalRuleApplications(ContextSaturationStatistics statistics)
    {
        return statistics.CoreApplications
            + statistics.HyperApplications
            + statistics.SuccApplications
            + statistics.PredApplications
            + statistics.ElimApplications
            + statistics.EqApplications
            + statistics.IneqApplications
            + statistics.FactorApplications
            + statistics.DataClashApplications;
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

    /// <summary>Builds a minimal non-empty registry carrying one bounded datatype, so the registry-carrying decision entries exercise a populated registry.</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry NonEmptyRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new BoundedDatatype(
            Utf8Strings.From(Example + "Percent"),
            Vocabulary.Xsd.Integer,
            [
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), new Literal(Utf8Strings.From("0"), new NamedNode(Vocabulary.Xsd.Integer))),
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), new Literal(Utf8Strings.From("100"), new NamedNode(Vocabulary.Xsd.Integer))),
            ]));

        return builder.Build();
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

    /// <summary>The named classes of the module's shared ALC translation as IRI strings — the signature the EL arm sweeps and the differential projects both arms onto.</summary>
    /// <param name="module">The module to translate.</param>
    /// <returns>The ALC signature IRIs.</returns>
    private static HashSet<string> AlcSignatureIris(ReasoningModule module)
    {
        HashSet<string> iris = new(StringComparer.Ordinal);
        foreach(Utf8String signatureClass in AlcModuleReasoner.Translate(module).SignatureClasses)
        {
            iris.Add(signatureClass.ToString());
        }

        return iris;
    }

    /// <summary>Projects a sorted subsumption-key list onto the ALC signature, keeping only the pairs whose sub and super IRI both lie in it; the order-preserving filter leaves the list sorted.</summary>
    /// <param name="keys">The sorted <c>subIri-&gt;superIri</c> keys.</param>
    /// <param name="signatureIris">The ALC signature IRIs to keep pairs within.</param>
    /// <returns>The projected sorted keys.</returns>
    private static List<string> ProjectOntoSignature(List<string> keys, HashSet<string> signatureIris)
    {
        List<string> projected = [];
        foreach(string key in keys)
        {
            int arrow = key.IndexOf("->", StringComparison.Ordinal);
            string sub = key[..arrow];
            string super = key[(arrow + 2)..];
            if(signatureIris.Contains(sub) && signatureIris.Contains(super))
            {
                projected.Add(key);
            }
        }

        return projected;
    }

    /// <summary>Whether the module carries a Self construct: an <c>ObjectHasSelf</c> in any axiom's class expressions, or a Reflexive or Irreflexive role characteristic.</summary>
    /// <param name="module">The module to inspect.</param>
    /// <returns><see langword="true"/> when a Self construct occurs.</returns>
    private static bool CarriesSelfConstruct(ReasoningModule module)
    {
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Reflexive or OwlPropertyCharacteristic.Irreflexive } || AxiomBearsHasSelf(axiom))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an axiom's class expressions contain an <c>ObjectHasSelf</c>, walked with an explicit stack (no recursion).</summary>
    /// <param name="axiom">The axiom to walk.</param>
    /// <returns><see langword="true"/> when a self restriction occurs.</returns>
    private static bool AxiomBearsHasSelf(OwlAxiom axiom)
    {
        Stack<OwlClassExpression> work = new();
        foreach(OwlClassExpression root in SelfWalkRoots(axiom))
        {
            work.Push(root);
        }

        while(work.Count > 0)
        {
            OwlClassExpression expression = work.Pop();
            if(expression is OwlObjectHasSelf)
            {
                return true;
            }

            foreach(OwlClassExpression child in SelfWalkChildren(expression))
            {
                work.Push(child);
            }
        }

        return false;
    }

    /// <summary>The class-expression roots the Self walk descends from on a class-bearing axiom; an axiom carrying none yields none.</summary>
    /// <param name="axiom">The axiom.</param>
    /// <returns>The root class expressions.</returns>
    private static IReadOnlyList<OwlClassExpression> SelfWalkRoots(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom subClass => [subClass.SubClass, subClass.SuperClass],
            OwlEquivalentClassesAxiom equivalent => [equivalent.First, equivalent.Second],
            OwlDisjointClassesAxiom disjoint => disjoint.Operands,
            _ => [],
        };
    }

    /// <summary>The immediate class-expression subexpressions of a composite — operands, complemented operand, or restriction filler; a leaf or filler-free restriction yields none.</summary>
    /// <param name="expression">The class expression.</param>
    /// <returns>The subexpressions to descend into.</returns>
    private static IReadOnlyList<OwlClassExpression> SelfWalkChildren(OwlClassExpression expression)
    {
        return expression switch
        {
            OwlObjectIntersectionOf intersection => intersection.Operands,
            OwlObjectUnionOf union => union.Operands,
            OwlObjectComplementOf complement => [complement.Operand],
            OwlObjectSomeValuesFrom some => [some.Filler],
            OwlObjectAllValuesFrom all => [all.Filler],
            OwlObjectCardinality { Filler: not null } cardinality => [cardinality.Filler],
            _ => [],
        };
    }

    //Construction helpers; the EL test DSL copied privately, with the additions
    //(All/AllInverse/Complement/Union/Intersection/Min/Max/DisjointUnion/DisjointProperties).

    /// <summary>The IRI prefix the test classes, roles, and individuals live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The fixed-top class reference, <c>owl:Thing</c>.</summary>
    private static OwlClassReference ThingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The fixed-bottom class reference, <c>owl:Nothing</c>.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>The <c>xsd:integer</c> datatype as a data range.</summary>
    private static OwlDatatypeReference Integer { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

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

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An inverse existential restriction <c>exists r-.C</c> over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The inverse existential restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), filler);
    }

    /// <summary>A universal restriction <c>forall r.C</c> over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The universal restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction <c>forall r-.C</c> over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The inverse universal restriction.</returns>
    private static OwlObjectAllValuesFrom AllInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), filler);
    }

    /// <summary>A complement <c>not C</c> of a class expression.</summary>
    /// <param name="operand">The complemented expression.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf(operands);
    }

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf(operands);
    }

    /// <summary>A qualified minimum-cardinality restriction <c>&gt;=n r.C</c> over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The minimum count.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The minimum-cardinality restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
    }

    /// <summary>A qualified maximum-cardinality restriction <c>&lt;=n r.C</c> over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The maximum count.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The maximum-cardinality restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A qualified maximum-cardinality restriction <c>&lt;=n r-.C</c> over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="cardinality">The maximum count.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The inverse maximum-cardinality restriction.</returns>
    private static OwlObjectCardinality MaxInverse(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, InverseProperty(property), filler);
    }

    /// <summary>A self-restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>A universal restriction <c>forall top.C</c> over the reserved <c>owl:topObjectProperty</c>.</summary>
    /// <param name="filler">The filler.</param>
    /// <returns>The universal restriction.</returns>
    private static OwlObjectAllValuesFrom TopAll(OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(TopObjectPropertyRef(), filler);
    }

    /// <summary>An existential restriction <c>exists bottom.C</c> over the reserved <c>owl:bottomObjectProperty</c>.</summary>
    /// <param name="filler">The filler.</param>
    /// <returns>The existential restriction.</returns>
    private static OwlObjectSomeValuesFrom BottomSome(OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(BottomObjectPropertyRef(), filler);
    }

    /// <summary>A self-restriction <c>exists top.Self</c> over the reserved <c>owl:topObjectProperty</c>.</summary>
    /// <returns>The self restriction.</returns>
    private static OwlObjectHasSelf TopHasSelf()
    {
        return new OwlObjectHasSelf(TopObjectPropertyRef());
    }

    /// <summary>A self-restriction over the inverse of a forward role -- <c>exists r-.Self</c>, which the loop lowering folds onto the same <c>Self_{base(r)}</c> atom as the forward spelling.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The inverse self-restriction.</returns>
    private static OwlObjectHasSelf HasSelfInverse(string property)
    {
        return new OwlObjectHasSelf(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))));
    }

    /// <summary>An individual-value restriction over a forward role -- <c>exists r.{a}</c> in its <c>ObjectHasValue</c> spelling.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
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

    /// <summary>A single-property data existential over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Utf8Strings.From(Example + property))], range);
    }

    /// <summary>An <c>xsd:normalizedString</c> range restricted by a pattern facet the checker leaves undecided — the built-in automaton route models pattern facets over <c>xsd:string</c> only, so a text sibling is deferred.</summary>
    /// <param name="pattern">The pattern lexical form.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction StringPattern(string pattern)
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.NormalizedString),
            [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.Pattern), new Literal(Utf8Strings.From(pattern), new NamedNode(Vocabulary.Xsd.String)))]);
    }

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>An equivalence of two class expressions.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>A pairwise disjointness axiom.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = Origin("disjoint") };
    }

    /// <summary>A disjoint-union axiom defining a class as the disjoint union of its operands.</summary>
    /// <param name="definedClass">The defined class's local name.</param>
    /// <param name="operands">The disjoint member expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointUnionAxiom DisjointUnion(string definedClass, params OwlClassExpression[] operands)
    {
        return new OwlDisjointUnionAxiom(new NamedNode(Utf8Strings.From(Example + definedClass)), operands) { Origin = Origin("disjointunion") };
    }

    /// <summary>A subrole inclusion <c>sub [= super</c>.</summary>
    /// <param name="sub">The subrole's local name.</param>
    /// <param name="super">The superrole's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = Origin("subrole") };
    }

    /// <summary>An inverse subrole inclusion <c>ObjectInverseOf(sub) [= super</c>, that is <c>sub- [= super</c>.</summary>
    /// <param name="sub">The inverted subproperty's local name.</param>
    /// <param name="super">The superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom InverseSubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + sub))), Property(super)) { Origin = Origin("inversesubrole") };
    }

    /// <summary>A sub-property-of-inverse inclusion <c>sub [= ObjectInverseOf(super)</c>, that is <c>sub [= super-</c>.</summary>
    /// <param name="sub">The subproperty's local name.</param>
    /// <param name="super">The inverted superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubPropertyInverse(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + super)))) { Origin = Origin("subroleinverse") };
    }

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>An equivalence of two object properties -- bidirectional sub-role inclusion.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentObjectPropertiesAxiom EquivalentProperties(string first, string second)
    {
        return new OwlEquivalentObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("equivalentrole") };
    }

    /// <summary>A pairwise disjoint-object-properties axiom.</summary>
    /// <param name="roles">The mutually disjoint roles' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointObjectPropertiesAxiom DisjointProperties(params string[] roles)
    {
        OwlObjectPropertyExpression[] operands = new OwlObjectPropertyExpression[roles.Length];
        for(int index = 0; index < roles.Length; index++)
        {
            operands[index] = Property(roles[index]);
        }

        return new OwlDisjointObjectPropertiesAxiom(operands) { Origin = Origin("disjointroles") };
    }

    /// <summary>A pairwise disjoint-object-properties axiom over explicit operand expressions -- the inverse-, top-, and bottom-operand rows the bare-local-name overload cannot spell.</summary>
    /// <param name="operands">The mutually disjoint operand expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointObjectPropertiesAxiom DisjointProperties(params OwlObjectPropertyExpression[] operands)
    {
        return new OwlDisjointObjectPropertiesAxiom(operands) { Origin = Origin("disjointroles") };
    }

    /// <summary>The inverse of a named object property in the example namespace, spelled as an <c>ObjectInverseOf</c>.</summary>
    /// <param name="local">The forward role's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A property reference to the reserved <c>owl:topObjectProperty</c> over its vocabulary IRI, not the example-namespace helper.</summary>
    /// <returns>The reference.</returns>
    private static OwlObjectPropertyReference TopObjectPropertyRef()
    {
        return new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty));
    }

    /// <summary>A property reference to the reserved <c>owl:bottomObjectProperty</c> over its vocabulary IRI, not the example-namespace helper.</summary>
    /// <returns>The reference.</returns>
    private static OwlObjectPropertyReference BottomObjectPropertyRef()
    {
        return new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty));
    }

    /// <summary>A range axiom typing every target of the role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="range">The range class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(property), range) { Origin = Origin("range") };
    }

    /// <summary>A domain axiom typing every source of the role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="domain">The domain class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom Domain(string property, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(Property(property), domain) { Origin = Origin("domain") };
    }

    /// <summary>A property-chain sub-role inclusion -- a single link is a plain sub-role, several compose.</summary>
    /// <param name="superProperty">The superproperty's local name.</param>
    /// <param name="links">The chain links' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlPropertyChainAxiom Chain(string superProperty, params string[] links)
    {
        OwlObjectPropertyExpression[] chain = new OwlObjectPropertyExpression[links.Length];
        for(int index = 0; index < links.Length; index++)
        {
            chain[index] = Property(links[index]);
        }

        return new OwlPropertyChainAxiom(chain, Property(superProperty)) { Origin = Origin("chain") };
    }

    /// <summary>A transitive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = Origin("transitive") };
    }

    /// <summary>A symmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(property)) { Origin = Origin("symmetric") };
    }

    /// <summary>A functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(property)) { Origin = Origin("functional") };
    }

    /// <summary>An inverse-functional-role characteristic axiom -- <c>InverseFunctionalObjectProperty(r)</c>, the clausifier's unqualified max-1 over <c>r-</c>.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, Property(property)) { Origin = Origin("inversefunctional") };
    }

    /// <summary>A functional characteristic over the inverse spelling -- <c>FunctionalObjectProperty(ObjectInverseOf(r))</c> = <c>Func(r-)</c>, which lowers to the same unqualified max-1 over <c>r-</c> as <see cref="InverseFunctional"/>.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom FunctionalInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, InverseProperty(property)) { Origin = Origin("functionalinverse") };
    }

    /// <summary>An asymmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, Property(property)) { Origin = Origin("asymmetric") };
    }

    /// <summary>A reflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, Property(property)) { Origin = Origin("reflexive") };
    }

    /// <summary>An irreflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, Property(property)) { Origin = Origin("irreflexive") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    //The random-sweep generator, a fresh copy of SatTableauModuleReasonerTests' xorshift generator
    //(same shapes, fixed seed). Its class-expression builder is an explicit-stack (breadth-first)
    //assembler rather than the source's recursion -- the no-recursion house rule -- producing the same
    //shape kinds and depth budget deterministically.

    /// <summary>The named classes the differential sweep draws from.</summary>
    private static string[] SweepClasses { get; } = ["A0", "A1", "A2", "A3"];

    /// <summary>The roles the differential sweep draws from.</summary>
    private static string[] SweepRoles { get; } = ["r0", "r1", "s"];

    /// <summary>Generates one in-scope module: one to three TBox axioms biased toward disjunction-heavy inclusions, with occasional global inclusions, cycles, disjointness, and a role-hierarchy pair, plus zero to two asserted individuals and zero to three asserted role edges.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule GenerateModule(ref ulong state)
    {
        List<OwlAxiom> axioms = [];
        int axiomCount = 1 + (int)(Next(ref state) % 3);
        for(int i = 0; i < axiomCount; i++)
        {
            switch((int)(Next(ref state) % 8))
            {
                case(0 or 1 or 2):
                {
                    axioms.Add(new OwlSubClassOfAxiom(Class(NextClass(ref state)), GenerateExpression(ref state, depth: 2)) { Origin = Origin($"sub{i}") });
                    break;
                }
                case(3):
                {
                    axioms.Add(new OwlSubClassOfAxiom(ThingReference, GenerateExpression(ref state, depth: 2)) { Origin = Origin($"global{i}") });
                    break;
                }
                case(4):
                {
                    string cyclic = NextClass(ref state);
                    axioms.Add(new OwlSubClassOfAxiom(Class(cyclic), new OwlObjectSomeValuesFrom(Property(NextRole(ref state)), Class(cyclic))) { Origin = Origin($"cycle{i}") });
                    break;
                }
                case(5):
                {
                    string first = NextClass(ref state);
                    string second = NextClass(ref state);
                    if(first == second)
                    {
                        second = SweepClasses[(Array.IndexOf(SweepClasses, first) + 1) % SweepClasses.Length];
                    }

                    axioms.Add(new OwlDisjointClassesAxiom([Class(first), Class(second)]) { Origin = Origin($"disjoint{i}") });
                    break;
                }
                default:
                {
                    axioms.Add(new OwlSubObjectPropertyOfAxiom(Property("s"), Property("r0")) { Origin = Origin($"hierarchy{i}") });
                    break;
                }
            }
        }

        int individualCount = (int)(Next(ref state) % 3);
        for(int individual = 0; individual < individualCount; individual++)
        {
            int assertionCount = 1 + (int)(Next(ref state) % 3);
            for(int assertion = 0; assertion < assertionCount; assertion++)
            {
                axioms.Add(new OwlClassAssertionAxiom(GenerateExpression(ref state, depth: 2), Individual($"i{individual}")) { Origin = Origin($"assert{individual}_{assertion}") });
            }
        }

        int edgeCount = (int)(Next(ref state) % 4);
        for(int edge = 0; edge < edgeCount; edge++)
        {
            string from = $"i{Next(ref state) % 3}";
            string to = $"i{Next(ref state) % 3}";
            axioms.Add(new OwlObjectPropertyAssertionAxiom(Individual(from), Individual(NextRole(ref state)), Individual(to)) { Origin = Origin($"edge{edge}") });
        }

        return new ReasoningModule(axioms, Violations: []);
    }

    /// <summary>Generates a deterministic class expression with a disjunction-heavy bias -- unions and intersections of two operands, complements, and existential and universal restrictions down to the depth budget -- built breadth-first through an explicit node worklist, then assembled bottom-up.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <param name="depth">The depth budget.</param>
    /// <returns>The expression.</returns>
    private static OwlClassExpression GenerateExpression(ref ulong state, int depth)
    {
        List<GenNode> nodes = [new GenNode { Depth = depth }];
        Queue<int> frontier = [];
        frontier.Enqueue(0);
        while(frontier.Count > 0)
        {
            int index = frontier.Dequeue();
            GenNode node = nodes[index];
            if(node.Depth == 0)
            {
                node.Kind = GenKind.Leaf;
                node.Leaf = GenerateLeaf(ref state);

                continue;
            }

            switch((int)(Next(ref state) % 10))
            {
                case(0 or 1):
                {
                    node.Kind = GenKind.Leaf;
                    node.Leaf = GenerateLeaf(ref state);
                    break;
                }
                case(2 or 3 or 4):
                {
                    node.Kind = GenKind.Union;
                    EnqueueTwoChildren(nodes, frontier, node);
                    break;
                }
                case(5):
                {
                    node.Kind = GenKind.Intersection;
                    EnqueueTwoChildren(nodes, frontier, node);
                    break;
                }
                case(6):
                {
                    node.Kind = GenKind.Complement;
                    node.First = EnqueueChild(nodes, frontier, node.Depth - 1);
                    break;
                }
                case(7 or 8):
                {
                    node.Kind = GenKind.Some;
                    node.Role = NextRole(ref state);
                    node.First = EnqueueChild(nodes, frontier, node.Depth - 1);
                    break;
                }
                default:
                {
                    node.Kind = GenKind.All;
                    node.Role = NextRole(ref state);
                    node.First = EnqueueChild(nodes, frontier, node.Depth - 1);
                    break;
                }
            }
        }

        OwlClassExpression[] built = new OwlClassExpression[nodes.Count];
        for(int index = nodes.Count - 1; index >= 0; index--)
        {
            GenNode node = nodes[index];
            built[index] = node.Kind switch
            {
                GenKind.Union => new OwlObjectUnionOf([built[node.First], built[node.Second]]),
                GenKind.Intersection => new OwlObjectIntersectionOf([built[node.First], built[node.Second]]),
                GenKind.Complement => new OwlObjectComplementOf(built[node.First]),
                GenKind.Some => new OwlObjectSomeValuesFrom(Property(node.Role!), built[node.First]),
                GenKind.All => new OwlObjectAllValuesFrom(Property(node.Role!), built[node.First]),
                _ => node.Leaf!,
            };
        }

        return built[0];
    }

    /// <summary>Appends a child node at one less depth to the node list and the frontier, returning its index.</summary>
    /// <param name="nodes">The node list.</param>
    /// <param name="frontier">The expansion frontier.</param>
    /// <param name="depth">The child's depth budget.</param>
    /// <returns>The child's index.</returns>
    private static int EnqueueChild(List<GenNode> nodes, Queue<int> frontier, int depth)
    {
        nodes.Add(new GenNode { Depth = depth });
        int index = nodes.Count - 1;
        frontier.Enqueue(index);

        return index;
    }

    /// <summary>Appends two child nodes at one less depth to a binary node and enqueues both.</summary>
    /// <param name="nodes">The node list.</param>
    /// <param name="frontier">The expansion frontier.</param>
    /// <param name="node">The binary node to attach the children to.</param>
    private static void EnqueueTwoChildren(List<GenNode> nodes, Queue<int> frontier, GenNode node)
    {
        node.First = EnqueueChild(nodes, frontier, node.Depth - 1);
        node.Second = EnqueueChild(nodes, frontier, node.Depth - 1);
    }

    /// <summary>Generates a leaf: a named class or its complement.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The leaf expression.</returns>
    private static OwlClassExpression GenerateLeaf(ref ulong state)
    {
        OwlClassReference reference = Class(NextClass(ref state));

        return Next(ref state) % 2 == 0 ? reference : new OwlObjectComplementOf(reference);
    }

    /// <summary>The next class name of the sweep signature.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The class name.</returns>
    private static string NextClass(ref ulong state)
    {
        return SweepClasses[(int)(Next(ref state) % (uint)SweepClasses.Length)];
    }

    /// <summary>The next role name of the sweep signature.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The role name.</returns>
    private static string NextRole(ref ulong state)
    {
        return SweepRoles[(int)(Next(ref state) % (uint)SweepRoles.Length)];
    }

    /// <summary>The next value of the deterministic xorshift sequence.</summary>
    /// <param name="state">The generator state.</param>
    /// <returns>The next value.</returns>
    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;

        return state;
    }

    /// <summary>The individual id of the told-pair fixtures' single constant — the first interned individual of their symbol table.</summary>
    private const int ToldPairIndividual = 0;

    /// <summary>The concept-atom id of the told-pair fixtures' FIRST told membership, interned after the seeded top and bottom atoms.</summary>
    private const int ToldPairFirstAtom = 2;

    /// <summary>The concept-atom id of the told-pair fixtures' SECOND told membership, interned after the first.</summary>
    private const int ToldPairSecondAtom = 3;

    /// <summary>The individual id of the told-edge fixture's edge SOURCE, the first interned individual of its symbol table.</summary>
    private const int ToldEdgeSource = 0;

    /// <summary>The individual id of the told-edge fixture's edge TARGET, interned after the source.</summary>
    private const int ToldEdgeTarget = 1;

    /// <summary>The directioned role id of the told-edge and told-seed fixtures' single role, interned in its forward direction as the first role of its symbol table.</summary>
    private const int ToldRole = 0;

    /// <summary>The individual id of the told-seed fixture's standing edge target, interned after the successor owner.</summary>
    private const int ToldSeedStanding = 1;

    /// <summary>The clause origin marker the engine stamps on a derived clause; the r-Succ seed landing carries it.</summary>
    private const int RootSeedDerivedOrigin = -1;

    /// <summary>The individual id of the anchor-invariant module's carrier constant, interned after its successor owner.</summary>
    private const int AnchorCarrierIndividual = 1;

    /// <summary>The concept-atom id of the anchor-invariant module's carried membership, interned after the seeded top and bottom atoms and the successor filler.</summary>
    private const int AnchorCarriedAtom = 3;

    /// <summary>
    /// Builds the told-pair root facts over a fresh symbol table: one individual and
    /// two concept memberships asserted over it, interned in the order the ids above
    /// record. The two facts broadcast into the trivial context in told order, so the
    /// FIRST landing opens the root edge for the individual and the second meets it.
    /// </summary>
    /// <param name="symbols">The interning table the facts are built over.</param>
    /// <returns>The two told root facts, first membership first.</returns>
    private static DlClause[] ToldPairRootFacts(ContextSymbolTable symbols)
    {
        int individual = symbols.InternIndividual(Utf8Strings.From(Example + "held"), IndividualOrigin.IriDenoted);
        int firstAtom = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int secondAtom = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));

        return
        [
            DlClause.Create([], [DlLiteral.Concept(firstAtom, DlTerm.Individual(individual))], 0),
            DlClause.Create([], [DlLiteral.Concept(secondAtom, DlTerm.Individual(individual))], 0)
        ];
    }

    /// <summary>Builds the told-pair engine on the per-individual-roots topology WITHOUT saturating it — the cell the pre-check's translated-image probe is read on.</summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine ToldPairFragmentedEngine()
    {
        ContextSymbolTable symbols = new();

        return SyntheticRootEngine(symbols, [], ToldPairRootFacts(symbols), RootContextTopology.PerIndividualRoots);
    }

    /// <summary>Builds the told-pair engine on the license-scoped, per-individual-roots cell WITHOUT saturating it — the one habitat in which the push-tag machinery is live, so both absorption arms join their tag.</summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine ToldPairLicenseScopedEngine()
    {
        ContextSymbolTable symbols = new();

        return LicenseScopedFragmentedEngine(symbols, [], ToldPairRootFacts(symbols));
    }

    /// <summary>Builds an engine over a directly-constructed nominal clausification on the LICENSE-SCOPED paramodulation scope and the per-individual-roots topology — the synthetic driver of the cell whose push-tag machinery is live.</summary>
    /// <param name="symbols">The interning table the clauses were built over.</param>
    /// <param name="clauses">The ontology clauses.</param>
    /// <param name="rootFacts">The root-context facts.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine LicenseScopedFragmentedEngine(ContextSymbolTable symbols, IReadOnlyList<DlClause> clauses, IReadOnlyList<DlClause> rootFacts)
    {
        ClausificationResult clausification = new(clauses, [], symbols, ContextTermOrder.ForModule(clauses), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: rootFacts, NominalJurisdiction: true, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);

        return ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.LicenseScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.PerIndividualRoots, progressSampler: null);
    }

    /// <summary>
    /// Builds the told-seed successor engine WITHOUT saturating it: a root fact whose
    /// <c>f(o)</c> head drives one root function edge, an ontology clause turning that
    /// edge's filler into a role atom naming a standing individual, and a THIRD root
    /// fact that is the successor trigger's own seed tautology spelled over the
    /// predecessor variable. The successor's landing therefore pushes a seed the root
    /// already holds, so the push is absorbed as an exact duplicate.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine ToldSeedSuccessorEngine()
    {
        ContextSymbolTable symbols = new();
        int owner = symbols.InternIndividual(Utf8Strings.From(Example + "owner"), IndividualOrigin.IriDenoted);
        int standing = symbols.InternIndividual(Utf8Strings.From(Example + "standing"), IndividualOrigin.IriDenoted);
        int function = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int filler = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int role = symbols.RoleOf(Utf8Strings.From(Example + "edge")).Value;
        DlLiteral seedLiteral = DlLiteral.Role(role, DlTerm.Context, DlTerm.Individual(standing));
        DlClause lift = DlClause.Create([DlLiteral.Concept(filler, DlTerm.Central)], [DlLiteral.Role(role, DlTerm.Central, DlTerm.Individual(standing))], 0);
        DlClause rootEdge = DlClause.Create([], [DlLiteral.Concept(filler, DlTerm.FunctionOf(function, owner))], 0);
        DlClause toldSeed = DlClause.Create([seedLiteral], [seedLiteral], 0);

        return SyntheticRootEngine(symbols, [lift], [rootEdge, toldSeed]);
    }

    /// <summary>
    /// Builds the told-edge engine on the per-individual-roots topology WITHOUT
    /// saturating it: a single told ground role edge between two individuals, whose
    /// broadcast image is a TWO-SLOT successor trigger, so the r-Succ seed reaches
    /// both nominal roots and each receives its own respelling.
    /// </summary>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine ToldEdgeFragmentedEngine()
    {
        ContextSymbolTable symbols = new();
        int source = symbols.InternIndividual(Utf8Strings.From(Example + "source"), IndividualOrigin.IriDenoted);
        int target = symbols.InternIndividual(Utf8Strings.From(Example + "target"), IndividualOrigin.IriDenoted);
        int role = symbols.RoleOf(Utf8Strings.From(Example + "edge")).Value;
        DlClause toldEdge = DlClause.Create([], [DlLiteral.Role(role, DlTerm.Individual(source), DlTerm.Individual(target))], 0);

        return SyntheticRootEngine(symbols, [], [toldEdge], RootContextTopology.PerIndividualRoots);
    }

    /// <summary>The id of the first LIVE clause of a context whose head is exactly the given single literal, or <c>-1</c> when the context holds none — the stored-content scan the root-seed rows read their landings back through.</summary>
    /// <param name="context">The context scanned.</param>
    /// <param name="head">The single head literal looked for.</param>
    /// <returns>The clause id, or <c>-1</c>.</returns>
    private static int LiveClauseWithHead(Context context, DlLiteral head)
    {
        for(int id = 0; id < context.ClauseCount; id++)
        {
            if(context.IsLive(id) && context.At(id).Head.Length == 1 && context.At(id).Head[0].Equals(head))
            {
                return id;
            }
        }

        return -1;
    }

    /// <summary>The id of the first clause of a context — LIVE OR TOMBSTONED — whose body and head are both exactly the given single literal, or <c>-1</c> when the context holds none. The r-Succ seed is that tautology shape, and a stronger clause can backward-subsume it after it lands, so the scan reads the whole id space rather than the live list alone.</summary>
    /// <param name="context">The context scanned.</param>
    /// <param name="literal">The literal the seed tautology is built over.</param>
    /// <returns>The clause id, or <c>-1</c>.</returns>
    private static int SeedTautologyClauseId(Context context, DlLiteral literal)
    {
        for(int id = 0; id < context.ClauseCount; id++)
        {
            DlClause clause = context.At(id);
            if(clause.Body.Length == 1 && clause.Head.Length == 1 && clause.Body[0].Equals(literal) && clause.Head[0].Equals(literal))
            {
                return id;
            }
        }

        return -1;
    }

    /// <summary>
    /// The r-Succ pre-check probes the seed image the target root-class context
    /// STORES, not the raw trigger: two told memberships of one individual broadcast
    /// into an ordinary context in told order, so the first landing finds no root edge
    /// and pushes its entry-translated seed, and every later landing meets that edge
    /// and is absorbed by the told membership the individual's nominal root holds in
    /// its central-variable spelling. A probe reading the RAW seed misses that stored
    /// image, so each later landing charges an application and a push the sink then
    /// absorbs — the offer, subsumption, and application columns all move.
    /// </summary>
    [TestMethod]
    public void TheRootSuccPreCheckProbesTheTranslatedSeedImage()
    {
        ContextSaturationStatistics totals = SaturatedTotals(ToldPairFragmentedEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("toldPairFragmented", totals));
        Assert.AreEqual(1L, totals.PushedArrivalOffers, "Exactly one of the three broadcast trigger landings reaches the push sink: the first opens the root edge, and the pre-check absorbs the rest.");
        Assert.AreEqual(1L, totals.PushedArrivalSubsumedHits, "That one push is absorbed by the told membership already standing in the nominal root, so it lands nothing.");
        Assert.AreEqual(0L, totals.PushedArrivalDuplicateHits, "No push reaches the gate as an exact duplicate: a raw-seed probe would push the later landings too, and the sink would absorb their translated images.");
        Assert.AreEqual(1L, totals.RootSuccApplications, "Exactly one r-Succ application is charged, because the pre-check refuses the later landings before the budget gate.");
        Assert.AreEqual(7, totals.ClausesDerived, "The inserted population is the nominal root's three clauses and the ordinary context's four, and the push adds none.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "Every seed image offered is in its target context's grammar.");
        Assert.AreEqual(0L, totals.TautologyDrops, "No seed image normalises away.");
    }

    /// <summary>
    /// The r-Succ seed is the trigger the landed literal IMAGES, not the landed
    /// literal itself: a successor context derives the role atom over the central
    /// variable, whose underlying trigger carries the predecessor variable instead,
    /// and the single root is told exactly that trigger's seed tautology. The
    /// pre-check therefore finds the seed already standing and absorbs the landing
    /// without charging, so the run's applications and pushes come from the broadcast
    /// landings alone. Seeding from the landed literal instead builds a
    /// central-variable role pair, which the root grammar does not admit: the probe
    /// misses, the application and push are charged, and the offer is refused
    /// out of grammar.
    /// </summary>
    [TestMethod]
    public void TheRootSuccSeedProbesAndPushesTheTriggerImageNotTheLandedLiteral()
    {
        ContextSaturationEngine engine = ToldSeedSuccessorEngine();
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context root = engine.RedriveRootForIndividual(ToldSeedStanding);
        DlLiteral seedLiteral = DlLiteral.Role(ToldRole, DlTerm.Context, DlTerm.Individual(ToldSeedStanding));
        DlLiteral landedLiteral = DlLiteral.Role(ToldRole, DlTerm.Central, DlTerm.Individual(ToldSeedStanding));
        int toldSeedId = SeedTautologyClauseId(root, seedLiteral);

        TestContext.WriteLine(ChannelCounterLine("toldSeedSuccessor", totals));
        Assert.AreEqual(2L, totals.PushedArrivalOffers, "Only the two broadcast landings push: the successor's own trigger landing is absorbed at the pre-check against the told seed.");
        Assert.AreEqual(2L, totals.PushedArrivalSubsumedHits, "Both pushes are absorbed by the root memberships already standing.");
        Assert.AreEqual(0L, totals.PushedArrivalDuplicateHits, "No push reaches the gate at all from the successor's trigger landing.");
        Assert.AreEqual(2L, totals.RootSuccApplications, "Two r-Succ applications are charged, one per broadcast landing; the trigger landing charges none.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "A seed spelled over the predecessor variable is an admitted root role pair, so nothing is refused out of grammar.");
        Assert.AreEqual(9, totals.ClausesDerived, "The inserted population is the root's four clauses and the successor's five.");
        Assert.AreNotEqual(-1, toldSeedId, "The root holds the told seed tautology the pre-check probes against.");
        Assert.AreEqual(0, root.At(toldSeedId).Origin, "That seed is the TOLD root fact, so the pre-check absorbed the landing rather than letting a derived copy insert beside it.");
        Assert.AreEqual(-1, SeedTautologyClauseId(root, landedLiteral), "No clause of the root carries the landed literal's central-variable role pair, which the root grammar does not admit.");
    }

    /// <summary>
    /// A two-slot trigger seeds each of its individuals' nominal roots with ITS OWN
    /// respelling: a told ground role edge broadcasts into an ordinary context, and
    /// the r-Succ slots push the seed into the source's root as the central-variable
    /// source pair and into the target's root as the central-variable target pair.
    /// Reusing the first slot's image for the second hands the target root a pair
    /// whose entry translation centralises BOTH sides, an admitted nominal-root pair
    /// that therefore inserts: the self-pair appears in the target root and the
    /// derived population grows.
    /// </summary>
    [TestMethod]
    public void TheRootSuccSeedRespellsPerSlotIntoEachNominalRoot()
    {
        ContextSaturationEngine engine = ToldEdgeFragmentedEngine();
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context sourceRoot = engine.RedriveRootForIndividual(ToldEdgeSource);
        Context targetRoot = engine.RedriveRootForIndividual(ToldEdgeTarget);
        DlLiteral selfPair = DlLiteral.Role(ToldRole, DlTerm.Central, DlTerm.Central);

        TestContext.WriteLine(ChannelCounterLine("toldEdgeFragmented", totals));
        Assert.AreEqual(2L, totals.RootSuccApplications, "Both slots of the one two-slot trigger apply, one per individual the edge names.");
        Assert.AreEqual(4L, totals.PushedArrivalOffers, "The two slot seeds and the two inter-nominal carrier images together reach the push sink.");
        Assert.AreEqual(2L, totals.PushedArrivalSubsumedHits, "Each slot seed is absorbed by the edge image its own root already carries.");
        Assert.AreEqual(1L, totals.PushedArrivalDuplicateHits, "One carrier image arrives as an exact duplicate.");
        Assert.AreEqual(8, totals.ClausesDerived, "The inserted population is the two roots' two clauses each and the ordinary context's four.");
        Assert.AreEqual(0L, totals.TautologyDrops, "No slot image normalises away.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "Both slot images are admitted nominal-root role pairs.");
        Assert.AreNotEqual(-1, LiveClauseWithHead(sourceRoot, DlLiteral.Role(ToldRole, DlTerm.Central, DlTerm.Individual(ToldEdgeTarget))), "The source's root carries the edge spelled with ITS constant central.");
        Assert.AreNotEqual(-1, LiveClauseWithHead(targetRoot, DlLiteral.Role(ToldRole, DlTerm.Individual(ToldEdgeSource), DlTerm.Central)), "The target's root carries the edge spelled with ITS constant central.");
        Assert.AreEqual(-1, SeedTautologyClauseId(targetRoot, selfPair), "The target's root holds no self-pair seed, which is what the first slot's image would centralise into on entry there.");
        Assert.AreEqual(-1, SeedTautologyClauseId(sourceRoot, selfPair), "The source's root holds no self-pair seed either.");
    }

    /// <summary>
    /// The r-Succ seed that genuinely lands carries the DERIVED origin: the
    /// anchor-invariant module's successor derives a ground concept trigger whose
    /// seed the single root does not yet hold, so the push inserts, and the stored
    /// clause is the one-literal-body tautology stamped with the derived marker. A
    /// later stronger clause backward-subsumes it, so the stored form is read across
    /// the whole id space. Nothing else can see the stamp — containment, subsumption,
    /// and every gate ignore origin by design.
    /// </summary>
    [TestMethod]
    public void TheRootSuccSeedLandsCarryingTheDerivedOrigin()
    {
        ContextSaturationEngine engine = AnchorInvariantTargetEngine();
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context root = engine.RedriveRootForIndividual(AnchorCarrierIndividual);
        DlLiteral carried = DlLiteral.Concept(AnchorCarriedAtom, DlTerm.Individual(AnchorCarrierIndividual));
        int seedId = SeedTautologyClauseId(root, carried);

        TestContext.WriteLine(ChannelCounterLine("anchorInvariantTarget", totals));
        Assert.AreEqual(4L, totals.RootSuccApplications, "The module's four trigger landings each charge an r-Succ application.");
        Assert.AreEqual(4L, totals.PushedArrivalOffers, "Each application offers its seed to the root.");
        Assert.AreNotEqual(-1, seedId, "The successor's carried trigger seeds a tautology the root did not hold, so the push landed it.");
        Assert.AreEqual(1, root.At(seedId).BodyLength, "The landed seed is the one-literal-body tautology over the trigger.");
        Assert.AreEqual(RootSeedDerivedOrigin, root.At(seedId).Origin, "The landed seed carries the derived origin marker the r-Succ arm stamps, so a shifted or defaulted origin argument is visible here.");
    }

    /// <summary>
    /// The pre-check's absorbed arm joins the push tag onto the live absorber, so an
    /// absorbed r-Succ arrival never strands its absorber untagged: under the
    /// license-scoped, per-individual-roots cell the same told-pair population runs
    /// with the push-tag machinery live, its first landing is absorbed at the SINK and
    /// its later landings at the PRE-CHECK, and every clause of the individual's
    /// nominal root ends up tagged. Dropping the pre-check's guarded build-and-join
    /// leaves the sink's single join standing alone and the later absorbers untagged.
    /// </summary>
    [TestMethod]
    public void TheRootSuccPreCheckJoinsThePushTagOnTheLiveAbsorber()
    {
        ContextSaturationEngine engine = ToldPairLicenseScopedEngine();
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context nominalRoot = engine.RedriveRootForIndividual(ToldPairIndividual);
        int topId = LiveClauseWithHead(nominalRoot, DlLiteral.Concept(ContextSymbolTable.Top, DlTerm.Central));
        int firstId = LiveClauseWithHead(nominalRoot, DlLiteral.Concept(ToldPairFirstAtom, DlTerm.Central));
        int secondId = LiveClauseWithHead(nominalRoot, DlLiteral.Concept(ToldPairSecondAtom, DlTerm.Central));

        TestContext.WriteLine(ChannelCounterLine("toldPairLicenseScoped", totals));
        Assert.AreEqual(3L, totals.EqScopeTagJoins, "Three absorptions join a tag: the sink's one on the first landing's push, and the pre-check's one per later landing.");
        Assert.AreEqual(0L, totals.InterNominalPropagations, "The population names one individual, so the inter-nominal carrier is dark and the tag-join column reads the two r-Succ arms alone.");
        Assert.AreEqual(1L, totals.PushedArrivalOffers, "The license scope leaves the push channel exactly where the query scope does.");
        Assert.AreEqual(1L, totals.RootSuccApplications, "The license scope leaves the r-Succ application count exactly where the query scope does.");
        Assert.AreEqual(7, totals.ClausesDerived, "The license scope leaves the inserted population exactly where the query scope does.");
        Assert.AreNotEqual(-1, topId, "The nominal root holds its top seed.");
        Assert.AreNotEqual(-1, firstId, "The nominal root holds the first told membership.");
        Assert.AreNotEqual(-1, secondId, "The nominal root holds the second told membership.");
        Assert.IsTrue(nominalRoot.IsPushed(topId), "The absorber of the top trigger's seed carries the joined push tag.");
        Assert.IsTrue(nominalRoot.IsPushed(firstId), "The absorber of the first membership's seed carries the joined push tag.");
        Assert.IsTrue(nominalRoot.IsPushed(secondId), "The absorber of the second membership's seed carries the joined push tag.");
    }

    /// <summary>The individual id of the ordinary-edge image fixtures' single constant, the first interned individual of their symbol table.</summary>
    private const int OrdinaryEdgeIndividual = 0;

    /// <summary>The concept-atom id of the ordinary-edge image fixtures' root-edge filler, interned after the seeded top and bottom atoms.</summary>
    private const int OrdinaryEdgeFillerAtom = 2;

    /// <summary>The concept-atom id of the ordinary-edge image fixtures' ground qualification atom, the conditional image's body conjunct.</summary>
    private const int OrdinaryEdgeGateAtom = 4;

    /// <summary>The concept-atom id of the ordinary-edge image fixtures' conditional-image head atom.</summary>
    private const int OrdinaryEdgeToldAtom = 5;

    /// <summary>The broadcast-list position of the ordinary-edge image fixtures' unconditional gate image.</summary>
    private const int OrdinaryEdgeGateImagePosition = 4;

    /// <summary>The clause id the ordinary context assigns that image when it lands there — deliberately unequal to the image's broadcast position.</summary>
    private const int OrdinaryEdgeGateImageClauseId = 12;

    /// <summary>The derived-clause bound whose latch falls between the ordinary-edge cell's skippable dispatches.</summary>
    private const int OrdinaryEdgePopulationBound = 16;

    /// <summary>
    /// Builds the ordinary-edge broadcast-image engine WITHOUT saturating it: a root
    /// fact whose <c>f(o)</c> head opens one root function edge into a filler-cored
    /// ordinary context, an ontology clause turning that filler into a second
    /// function head so an ORDINARY-to-ordinary edge exists, a qualifying clause
    /// giving the successor a ground discharge witness, and a CONDITIONAL root fact
    /// whose body and head are both ground. Every one of the resulting broadcast
    /// images is ground in body and head, so each is sigma-invariant and the ordinary
    /// arm's containment skip is exercised over the whole image population.
    /// </summary>
    /// <param name="topology">The root-tier topology, the published single root by default.</param>
    /// <param name="relevance">The r-Pred ground-relevance mode, the published unrestricted mode by default.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine OrdinaryEdgeGroundImageEngine(RootContextTopology topology = RootContextTopology.SingleRoot, RootPropagationRelevance relevance = RootPropagationRelevance.Unrestricted)
    {
        ContextSymbolTable symbols = new();
        int held = symbols.InternIndividual(Utf8Strings.From(Example + "held"), IndividualOrigin.IriDenoted);
        int rootFunction = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int edgeFunction = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        int filler = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        int carried = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        int gate = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        int told = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));
        DlClause lift = DlClause.Create([DlLiteral.Concept(filler, DlTerm.Central)], [DlLiteral.Concept(carried, DlTerm.Function(edgeFunction))], 0);
        DlClause qualifier = DlClause.Create([DlLiteral.Concept(carried, DlTerm.Central)], [DlLiteral.Concept(gate, DlTerm.Individual(held))], 0);
        DlClause rootEdge = DlClause.Create([], [DlLiteral.Concept(filler, DlTerm.FunctionOf(rootFunction, held))], 0);
        DlClause conditional = DlClause.Create([DlLiteral.Concept(gate, DlTerm.Individual(held))], [DlLiteral.Concept(told, DlTerm.Individual(held))], 0);

        return RelevanceScopedRootEngine(symbols, [lift, qualifier], [rootEdge, conditional], topology, relevance);
    }

    /// <summary>Builds an engine over a directly-constructed nominal clausification under a chosen topology and r-Pred ground-relevance mode — the synthetic driver of the cells the containment-skip rows read, a second creation call over the same clausification shape and no production path of its own.</summary>
    /// <param name="symbols">The interning table the clauses were built over.</param>
    /// <param name="clauses">The ontology clauses.</param>
    /// <param name="rootFacts">The root-context facts.</param>
    /// <param name="topology">The root-tier topology.</param>
    /// <param name="relevance">The r-Pred ground-relevance mode.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine RelevanceScopedRootEngine(ContextSymbolTable symbols, IReadOnlyList<DlClause> clauses, IReadOnlyList<DlClause> rootFacts, RootContextTopology topology, RootPropagationRelevance relevance)
    {
        ClausificationResult clausification = new(clauses, [], symbols, ContextTermOrder.ForModule(clauses), 0, false, 0, 0, 0, NegativePolarityDataMarkers: 0, new Dictionary<int, DataDemandDescriptor>(), DataPropertyBox.Empty, GroundClash: false, GroundClashReason: null, PreMergeUnions: 0, [], new Dictionary<Utf8String, int>(), new Dictionary<int, Utf8String>(), GroundAssertionGraph.Empty(symbols), new Dictionary<int, RoleRepresentative>(), KeyForcedUnions: 0, [], new Dictionary<Utf8String, Dictionary<Utf8String, List<Literal>>>(), new Dictionary<Utf8String, HashSet<int>>(), new HashSet<Utf8String>(), [], RootFacts: rootFacts, NominalJurisdiction: true, NominalClash: false, NominalClashReason: null, NominalCountingWindow.Empty);

        return ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, relevance, topology, progressSampler: null);
    }

    /// <summary>The instrument chain a run's ordinary Pred arm must always satisfy: every skip stands on a registered image target, every image target on a sigma-invariant pass, and every pass on a dispatch.</summary>
    /// <param name="name">The cell label carried into the failure text.</param>
    /// <param name="totals">The run's totals.</param>
    private static void AssertOrdinaryArmChain(string name, ContextSaturationStatistics totals)
    {
        Assert.IsLessThanOrEqualTo(totals.PredBroadcastImageTargets, totals.PredBroadcastContainedSkips, name + ": every elided offer's target is a registered broadcast image.");
        Assert.IsLessThanOrEqualTo(totals.PredOrdinaryInvariantTargetPasses, totals.PredBroadcastImageTargets, name + ": every registered image target passed the sigma-invariance test first.");
        Assert.IsLessThanOrEqualTo(totals.PredOrdinaryArmDispatches, totals.PredOrdinaryInvariantTargetPasses, name + ": every invariance test ran on an ordinary-arm dispatch.");
    }

    /// <summary>The rendered clause sequence of a context, tombstoned ids included, in insertion order.</summary>
    /// <param name="context">The context whose clauses are read.</param>
    /// <param name="symbols">The symbol table naming the atoms.</param>
    /// <returns>The newline-joined rendering.</returns>
    private static string ClauseSequence(Context context, ContextSymbolTable symbols)
    {
        StringBuilder builder = new();
        for(int id = 0; id < context.ClauseCount; id++)
        {
            if(id > 0)
            {
                builder.Append('\n');
            }

            builder.Append(context.At(id).Render(symbols));
        }

        return builder.ToString();
    }

    /// <summary>The symbol table of the ordinary-edge image fixtures, rebuilt in the same interning order so a row can render the cell's stored clauses.</summary>
    /// <returns>The table.</returns>
    private static ContextSymbolTable OrdinaryEdgeSymbols()
    {
        ContextSymbolTable symbols = new();
        _ = symbols.InternIndividual(Utf8Strings.From(Example + "held"), IndividualOrigin.IriDenoted);
        _ = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        _ = symbols.MintFunctionSymbol(ContextSymbolTable.Top);
        _ = symbols.AtomOf(Utf8Strings.From(Example + "Birch"));
        _ = symbols.AtomOf(Utf8Strings.From(Example + "Cedar"));
        _ = symbols.AtomOf(Utf8Strings.From(Example + "Alder"));
        _ = symbols.AtomOf(Utf8Strings.From(Example + "Elm"));

        return symbols;
    }

    /// <summary>Builds the ordinary-edge ground-image engine with the root-edge filler's own query context ensured, so the Pred predecessor and its successor are two DISTINCT ordinary contexts.</summary>
    /// <param name="relevance">The r-Pred ground-relevance mode.</param>
    /// <returns>The unsaturated engine.</returns>
    private static ContextSaturationEngine QualifiedEdgeGroundImageEngine(RootPropagationRelevance relevance)
    {
        ContextSaturationEngine engine = OrdinaryEdgeGroundImageEngine(RootContextTopology.SingleRoot, relevance);
        _ = engine.EnsureQueryContext(OrdinaryEdgeFillerAtom);

        return engine;
    }

    /// <summary>
    /// The ordinary Pred arm ELIDES the offer whose target is a sigma-invariant
    /// broadcast image the predecessor already holds: the conclusion of such a
    /// completion is the target clause itself, and the broadcast's own delivery
    /// record proves that content is already contained in the predecessor. On the
    /// ordinary-edge cell every one of the six broadcast images is ground in body and
    /// head, so all six image-target dispatches are elided and the charged offers are
    /// exactly the four non-image completions plus the six anchored ones. The
    /// invariant-pass column stands strictly below the dispatch column, so a counter
    /// charged before the invariance test rather than on its verdict shows here.
    /// </summary>
    [TestMethod]
    public void TheOrdinaryPredArmSkipsTheBroadcastImageThePredecessorHolds()
    {
        ContextSaturationStatistics totals = SaturatedTotals(OrdinaryEdgeGroundImageEngine(), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeGroundImage", totals));
        AssertOrdinaryArmChain("ordinaryEdgeGroundImage", totals);
        Assert.AreEqual(6, totals.RootBroadcastClauseCount, "The cell broadcasts six images: the root's per-constant top seed, the conditional image and its two relevance tautologies, and the two unconditional ground heads.");
        Assert.AreEqual(6L, totals.PredOrdinaryInvariantTargetPasses, "Six ordinary-arm dispatches carry a target whose body and head literals are all ground.");
        Assert.AreEqual(6L, totals.PredBroadcastImageTargets, "Every one of those sigma-invariant targets is a REGISTERED broadcast image, so the locally-derived all-ground residue is empty here.");
        Assert.AreEqual(6L, totals.PredBroadcastContainedSkips, "The predecessor holds every one of them, so all six offers are elided.");
        Assert.AreEqual(10L, totals.PredOffers, "The offers that remain are the four surviving ordinary completions and the six anchored ones.");
        Assert.AreEqual(totals.PredOffers, totals.PredOdometerRuns, "Every odometer run that reaches its cursor charges exactly one offer here, so the elision removes runs and offers together.");
        Assert.AreEqual(4L, totals.PredApplications, "Four Pred conclusions land; an elided offer lands nothing by construction.");
        Assert.AreEqual(21, totals.ClausesDerived, "The inserted population is unchanged by the elision: every elided conclusion was already contained.");
        Assert.AreEqual(0L, totals.TautologyDrops, "No elided or surviving conclusion normalises away.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "Every offered conclusion is in its target context's grammar.");
    }

    /// <summary>
    /// THE FILTER ROW: an image whose spans are NOT all ground is still offered,
    /// because its Pred conclusion is a genuinely new clause rather than the image
    /// itself. The nominal wedge drives fifty-five ordinary dispatches of which
    /// fourteen carry a non-invariant target and four more carry a ground target that
    /// no broadcast registered, so both residues are live rather than nominal, and an
    /// implementation that dropped the sigma-invariance test would admit those
    /// fourteen to the lookup: the invariant-pass column rises to the dispatch census
    /// and reds here. The derived population and the Pred application count are pinned
    /// beside it, so an elision that DID take a conclusion would red on those instead.
    /// </summary>
    [TestMethod]
    public void TheOrdinaryPredArmOffersTheAnchorDependentImage()
    {
        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(NomWedgeTowerModule(1), ReasoningBudget.Unbounded, progressSampler: null, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(ChannelCounterLine("nominalWedgeOrdinaryArm", totals));
        AssertOrdinaryArmChain("nominalWedgeOrdinaryArm", totals);
        Assert.AreEqual(55L, totals.PredOrdinaryArmDispatches, "The wedge's ordinary-arm dispatch census.");
        Assert.AreEqual(41L, totals.PredOrdinaryInvariantTargetPasses, "Fourteen of those dispatches carry a target that is NOT sigma-invariant, so the invariance filter has real work to refuse.");
        Assert.AreEqual(37L, totals.PredBroadcastImageTargets, "Four of the sigma-invariant targets were derived locally rather than broadcast, so the registration filter is likewise not vacuous.");
        Assert.AreEqual(37L, totals.PredBroadcastContainedSkips, "The predecessor holds every registered image it is offered here.");
        Assert.AreEqual(46L, totals.PredOffers, "The offers the two arms charge after the elision.");
        Assert.AreEqual(7L, totals.PredApplications, "The Pred conclusions that land — the count an elided non-invariant offer would take away.");
        Assert.AreEqual(168, totals.ClausesDerived, "The wedge's inserted population, which the elision leaves exactly where the unelided run put it.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "No conclusion of the wedge is refused out of grammar.");
    }

    /// <summary>
    /// The skip counter closes the ordinary arm's dispatch ledger under BOTH budget
    /// regimes: the dispatch census is charged before the guard, so it is byte-identical
    /// to the unelided run's, and the instrument chain holds at the end of an unbounded
    /// run and of a population-bounded one that latches mid-flood. A counter moved
    /// behind the guard reads the surviving dispatches alone and reds on the census.
    /// </summary>
    [TestMethod]
    public void TheSkipCounterClosesTheOrdinaryArmDispatchLedger()
    {
        ContextSaturationStatistics unbounded = SaturatedTotals(OrdinaryEdgeGroundImageEngine(), TestContext.CancellationToken);
        ContextSaturationEngine boundedEngine = OrdinaryEdgeGroundImageEngine();
        SaturationOutcome boundedOutcome = boundedEngine.Saturate(new ReasoningBudget(0, 0, 0, MaxDerivedClauses: OrdinaryEdgePopulationBound), TestContext.CancellationToken);
        ContextSaturationStatistics bounded = boundedEngine.BuildStatistics(contextDecided: true);

        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeUnbounded", unbounded));
        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeBounded", bounded));
        AssertOrdinaryArmChain("ordinaryEdgeUnbounded", unbounded);
        AssertOrdinaryArmChain("ordinaryEdgeBounded", bounded);
        Assert.AreEqual(10L, unbounded.PredOrdinaryArmDispatches, "The unbounded run dispatches ten ordinary completions, six of which the guard then elides.");
        Assert.AreEqual(SaturationOutcome.BudgetExhausted, boundedOutcome, "The population bound latches before the fixpoint, so the bounded arm reads a genuinely truncated run.");
        Assert.AreEqual(OrdinaryEdgePopulationBound, bounded.ClausesDerived, "The bounded run stops exactly at its derived-clause bound.");
        Assert.AreEqual(5L, bounded.PredOrdinaryArmDispatches, "The bounded run reaches five ordinary dispatches before the latch.");
        Assert.AreEqual(3L, bounded.PredBroadcastContainedSkips, "Three of them are elided, so skippable dispatches fall on BOTH sides of the latch and the bounded reading is not the unbounded one.");
        Assert.IsLessThan(unbounded.PredBroadcastContainedSkips, bounded.PredBroadcastContainedSkips, "The bounded run credits strictly fewer elisions than the unbounded one.");
    }

    /// <summary>
    /// The containment record is written only where the sink RESOLVED the offer: under
    /// the ground-relevance mode a context that cannot discharge an image's ground body
    /// conjunct refuses the offer at zero budget and holds none of that content, so its
    /// later Pred offer of the same image is CHARGED rather than elided. On the cell
    /// whose predecessor and successor are two distinct ordinary contexts the filtered
    /// arm refuses five offers, elides one image target FEWER than it registers, and
    /// charges one Pred offer MORE than the unrestricted control over the same
    /// clausification. Recording on the refusal arm would close that gap and take the
    /// charged offer with it.
    /// </summary>
    [TestMethod]
    public void TheBroadcastContainmentIsRecordedOnlyWhereTheOfferLanded()
    {
        ContextSaturationStatistics filtered = SaturatedTotals(QualifiedEdgeGroundImageEngine(RootPropagationRelevance.GroundFiltered), TestContext.CancellationToken);
        ContextSaturationStatistics unrestricted = SaturatedTotals(QualifiedEdgeGroundImageEngine(RootPropagationRelevance.Unrestricted), TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("qualifiedEdgeFiltered", filtered));
        TestContext.WriteLine(ChannelCounterLine("qualifiedEdgeUnrestricted", unrestricted));
        AssertOrdinaryArmChain("qualifiedEdgeFiltered", filtered);
        AssertOrdinaryArmChain("qualifiedEdgeUnrestricted", unrestricted);
        Assert.AreEqual(5L, filtered.RootPredFilteredOffers, "The ground-relevance guard refuses five broadcast offers on this cell, so the refusal arm is genuinely exercised rather than assumed.");
        Assert.AreEqual(0L, unrestricted.RootPredFilteredOffers, "The control runs the same clausification with the guard off, so the two readings differ by the refusals alone.");
        Assert.AreEqual(9L, filtered.PredBroadcastImageTargets, "Nine ordinary-arm targets are registered images in the filtered arm.");
        Assert.AreEqual(8L, filtered.PredBroadcastContainedSkips, "Only eight of them are elided: the refused context holds no record for the ninth, so that offer stands.");
        Assert.IsLessThan(filtered.PredBroadcastImageTargets, filtered.PredBroadcastContainedSkips, "A registered image target that the predecessor does NOT hold is exactly what the refusal leaves behind.");
        Assert.AreEqual(13L, filtered.PredOffers, "The refused image's later Pred offer is CHARGED, so the filtered arm charges one offer MORE than the unrestricted control.");
        Assert.AreEqual(12L, unrestricted.PredOffers, "The control's charged offers.");
        Assert.AreEqual(11L, unrestricted.PredBroadcastImageTargets, "With the guard off every registered image reaches every ordinary context.");
        Assert.AreEqual(11L, unrestricted.PredBroadcastContainedSkips, "And every one of them is elided, so the control's chain closes with no residue.");
        Assert.AreEqual(6L, filtered.PredApplications, "The filtered arm's Pred landings.");
        Assert.AreEqual(34, filtered.ClausesDerived, "The filtered arm's inserted population.");
        Assert.AreEqual(0L, filtered.TautologyDrops, "No conclusion of the filtered arm normalises away.");
        Assert.AreEqual(0L, filtered.OutOfGrammarConclusions, "No conclusion of the filtered arm is refused out of grammar.");
    }

    /// <summary>
    /// A ROOT-CLASS predecessor can never elide: the broadcast loop skips root-class
    /// contexts and the seeding replay never runs for them, so a nominal root holds no
    /// containment record and every image offered from its successor is charged. On the
    /// per-individual-roots cell exactly half of the twelve image-target dispatches are
    /// elided — the ordinary predecessor's — while the nominal root's are all offered,
    /// and the images it receives through those offers stand in its own central-variable
    /// spelling. Reading the SUCCESSOR's record instead would elide the nominal root's
    /// offers too and strip those clauses out of it.
    /// </summary>
    [TestMethod]
    public void TheRootClassPredecessorNeverSkipsTheBroadcastImage()
    {
        ContextSaturationEngine engine = OrdinaryEdgeGroundImageEngine(RootContextTopology.PerIndividualRoots);
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context nominalRoot = engine.RedriveRootForIndividual(OrdinaryEdgeIndividual);

        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeFragmented", totals));
        AssertOrdinaryArmChain("ordinaryEdgeFragmented", totals);
        Assert.AreEqual(20L, totals.PredOrdinaryArmDispatches, "Under the fragmented topology the nominal root takes the ordinary arm too, so the dispatch census doubles.");
        Assert.AreEqual(12L, totals.PredBroadcastImageTargets, "Twelve of those dispatches carry a registered image.");
        Assert.AreEqual(6L, totals.PredBroadcastContainedSkips, "Only the ORDINARY predecessor's six are elided; the nominal root holds no record and its six are charged.");
        Assert.AreEqual(10L, totals.PredOffers, "The charged offers include the nominal root's six image offers.");
        Assert.AreEqual(4L, totals.PredApplications, "The Pred conclusions that land under the fragmented topology.");
        Assert.AreEqual(21, totals.ClausesDerived, "The fragmented cell's inserted population.");
        Assert.AreNotEqual(-1, LiveClauseWithHead(nominalRoot, DlLiteral.Concept(OrdinaryEdgeGateAtom, DlTerm.Central)), "The nominal root carries the gate image in its own central-variable spelling.");
        Assert.AreNotEqual(-1, LiveClauseWithHead(nominalRoot, DlLiteral.Concept(OrdinaryEdgeToldAtom, DlTerm.Central)), "The nominal root carries the conditional image's head in its own central-variable spelling.");
    }

    /// <summary>
    /// The containment record and the image map are MONOTONE facts about state that
    /// only grows, so they are never cleared and a second saturation of an engine at
    /// its fixpoint elides nothing further and charges nothing further: every counter
    /// reads identically across the two observations. Clearing either at the head of
    /// the driver would re-offer and re-charge what the first run already elided.
    /// </summary>
    [TestMethod]
    public void TheSecondSaturateCallElidesNothingFurther()
    {
        ContextSaturationEngine engine = OrdinaryEdgeGroundImageEngine();
        ContextSaturationStatistics first = SaturatedTotals(engine, TestContext.CancellationToken);
        ContextSaturationStatistics second = SaturatedTotals(engine, TestContext.CancellationToken);

        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeFirstPass", first));
        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeSecondPass", second));
        Assert.AreEqual(first.PredBroadcastContainedSkips, second.PredBroadcastContainedSkips, "The second saturation credits no further elision, so the containment record survived it.");
        Assert.AreEqual(first.PredBroadcastImageTargets, second.PredBroadcastImageTargets, "The second saturation finds no further registered-image target, so the image map survived it.");
        Assert.AreEqual(first.PredOrdinaryInvariantTargetPasses, second.PredOrdinaryInvariantTargetPasses, "No further invariance test runs.");
        Assert.AreEqual(first.PredOrdinaryArmDispatches, second.PredOrdinaryArmDispatches, "No further ordinary dispatch runs.");
        Assert.AreEqual(first.PredOffers, second.PredOffers, "No further Pred offer is charged.");
        Assert.AreEqual(first.InferenceAttempts, second.InferenceAttempts, "No further attempt is spent.");
        Assert.AreEqual(first.ClausesDerived, second.ClausesDerived, "No further clause is inserted.");
        Assert.AreEqual(6L, second.PredBroadcastContainedSkips, "Both readings stand at the cell's own elision count, so the row is not comparing two zeros.");
    }

    /// <summary>
    /// The containment record is indexed by the image's BROADCAST POSITION, not by the
    /// clause id the sink assigns it: the unconditional gate image sits at position four
    /// of the broadcast list and lands in the ordinary context as clause twelve, so a
    /// record written at the landed id would be read back at the wrong slot and every
    /// later dispatch of that image would miss the skip. The two indices are pinned
    /// unequal here, which is what makes the confusion observable at all.
    /// </summary>
    [TestMethod]
    public void TheImageBroadcastAfterTheEdgeIsOfferedOnceThenSkipped()
    {
        ContextSaturationEngine engine = OrdinaryEdgeGroundImageEngine();
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context ordinary = engine.RedriveContext(0);
        DlClause image = engine.RootBroadcastImages[OrdinaryEdgeGateImagePosition];

        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeImagePositions", totals));
        Assert.IsFalse(ordinary.IsRoot, "The context the images land in is the ordinary one, which is where the containment record lives.");
        Assert.HasCount(6, engine.RootBroadcastImages, "The cell carries six broadcast images, so a position is not trivially the only one.");
        Assert.AreEqual(DlClause.Create([], [DlLiteral.Concept(OrdinaryEdgeGateAtom, DlTerm.Individual(OrdinaryEdgeIndividual))], -1), image, "The image at the pinned position is the unconditional ground gate head.");
        Assert.AreEqual(image, ordinary.At(OrdinaryEdgeGateImageClauseId), "That image landed in the ordinary context at the pinned clause id.");
        Assert.AreEqual(6L, totals.PredBroadcastContainedSkips, "Every image the predecessor holds is elided, which a record written at the landed id would not achieve.");
    }

    /// <summary>
    /// The elision happens IN PLACE at the single dispatch point, so the surviving
    /// offers keep the order the sweep enumerated them in and the predecessor's clause
    /// sequence is exactly the unelided run's. The whole insertion sequence of the
    /// ordinary context is pinned as content, tombstoned ids included: an
    /// implementation that hoisted, partitioned or deferred the skipped entries would
    /// perturb it. This pins ORDER PRESERVATION, not absorber choice under convergence.
    /// </summary>
    [TestMethod]
    public void TheOrdinaryPredArmKeepsTheSurvivingOfferOrder()
    {
        ContextSaturationEngine engine = OrdinaryEdgeGroundImageEngine();
        ContextSaturationStatistics totals = SaturatedTotals(engine, TestContext.CancellationToken);
        Context ordinary = engine.RedriveContext(0);
        string expected = string.Join(
            "\n",
            " -> Top(x)",
            " -> Top(o0)",
            Example + "Alder(o0) -> " + Example + "Elm(o0)",
            Example + "Elm(o0) -> " + Example + "Elm(o0)",
            Example + "Birch(x) -> " + Example + "Birch(x)",
            Example + "Birch(x) -> " + Example + "Cedar(f1(x))",
            Example + "Cedar(x) -> " + Example + "Cedar(x)",
            Example + "Cedar(x) -> " + Example + "Alder(o0)",
            Example + "Cedar(x) -> " + Example + "Elm(o0)",
            Example + "Birch(x) -> " + Example + "Alder(o0)",
            Example + "Birch(x) -> " + Example + "Elm(o0)",
            Example + "Alder(o0) -> " + Example + "Alder(o0)",
            " -> " + Example + "Alder(o0)",
            " -> " + Example + "Elm(o0)");

        TestContext.WriteLine(ChannelCounterLine("ordinaryEdgeOfferOrder", totals));
        Assert.AreEqual(14, ordinary.ClauseCount, "The ordinary context's insertion count.");
        Assert.AreEqual(expected, ClauseSequence(ordinary, OrdinaryEdgeSymbols()), "The predecessor's insertion sequence is the unelided run's, position by position.");
        Assert.AreEqual(6L, totals.PredBroadcastContainedSkips, "The sweep that produced that sequence did elide offers, so the order is asserted over a genuinely mixed enumeration.");
    }

    /// <summary>The kind of a generator node in the explicit-stack class-expression builder.</summary>
    private enum GenKind
    {
        /// <summary>A leaf: a named class or its complement.</summary>
        Leaf,

        /// <summary>A binary union.</summary>
        Union,

        /// <summary>A binary intersection.</summary>
        Intersection,

        /// <summary>A unary complement.</summary>
        Complement,

        /// <summary>An existential restriction.</summary>
        Some,

        /// <summary>A universal restriction.</summary>
        All,
    }

    /// <summary>A partially built generator node: its drawn kind, remaining depth, role and leaf payloads, and its child indices in the node list.</summary>
    private sealed class GenNode
    {
        /// <summary>The node's drawn kind; a leaf until the frontier expands it.</summary>
        public GenKind Kind { get; set; } = GenKind.Leaf;

        /// <summary>The node's remaining depth budget.</summary>
        public int Depth { get; set; }

        /// <summary>The role's local name for an existential or universal node; otherwise <see langword="null"/>.</summary>
        public string? Role { get; set; }

        /// <summary>The leaf expression for a leaf node; otherwise <see langword="null"/>.</summary>
        public OwlClassExpression? Leaf { get; set; }

        /// <summary>The first (or only) child's index in the node list, or <c>-1</c> for a leaf.</summary>
        public int First { get; set; } = -1;

        /// <summary>The second child's index in the node list for a binary node, or <c>-1</c>.</summary>
        public int Second { get; set; } = -1;
    }
}
