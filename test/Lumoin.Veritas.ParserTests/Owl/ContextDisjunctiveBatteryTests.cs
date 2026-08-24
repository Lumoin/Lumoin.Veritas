using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The disjunctive-fragment battery: every
/// semantic row of the pre-registered ground-truth sheet
/// (42 of 42 independently confirmed) drives
/// <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>
/// at MODULE level through the opened gates — survey, clausifier, second gate,
/// saturation, verdict reader — and checks consistency, the context-decided
/// path, and the EXACT module-local subsumption set. Rows whose constructs the
/// ALC(H) tableau translates additionally assert the shared-fragment
/// differential (verdict and subsumption set) against
/// <see cref="AlcModuleReasoner.Decide(ReasoningModule, System.Threading.CancellationToken)"/>;
/// cardinality-bearing rows have NO automated comparand
/// (<c>AlcModuleReasoner.TryTranslate</c> carries no object-cardinality arm) and
/// are decided by the battery and the ground-truth sheet alone — the per-row
/// oracle-infeasibility flag is stated, not hidden. Ground-truth
/// entailment queries land as subsumption-set membership (an unsatisfiable
/// class is subsumed by every signature class); individual-level entailment
/// queries land as their refutation encoding (premise plus complemented
/// assertion answers inconsistent). Every consistent row carries its
/// hand-built model and every inconsistent row its derivation, in 7-bit ASCII
/// ([= subsumption, exists/forall, bot the empty class, ~ equality).
/// </summary>
[TestClass]
internal sealed class ContextDisjunctiveBatteryTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier2stageC#";

    /// <summary>
    /// The semantic battery: every ground-truth sheet row decided at module level, with
    /// verdict, context-decided path, exact subsumption set, and — where the
    /// row's constructs lie in the shared ALC(H) fragment — the tableau
    /// differential all checked. The loop reports every offender and fails once
    /// with the whole table.
    /// </summary>
    [TestMethod]
    public void Tier2DisjunctionSemanticBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, string[] ExpectedSubsumptions, bool AlcComparand)[] rows = BatteryRows();

        StringBuilder report = new();
        report.AppendLine("\nrow | true | final | contextDecided | subs | alc | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, string[] expectedSubsumptions, bool alcComparand) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool contextDecided = decision.Statistics.ContextTotals.ContextDecided;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;

            List<string> expected = [.. expectedSubsumptions];
            expected.Sort(StringComparer.Ordinal);
            List<string> actual = SubsumptionKeys(decision.Verdict);
            bool subsOk = KeysEqual(expected, actual);
            string subsNote = subsOk ? "ok" : DiffKeys(expected, actual);

            bool alcOk = true;
            string alcNote = "n/a";
            if(alcComparand)
            {
                ModuleVerdict tableau = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
                bool alcVerdictOk = tableau.IsConsistent == trueConsistent;
                bool alcSubsOk = KeysEqual(expected, SubsumptionKeys(tableau));
                alcOk = alcVerdictOk && alcSubsOk;
                alcNote = alcOk ? "agrees" : ("DIVERGES verdict=" + tableau.IsConsistent + " " + DiffKeys(expected, SubsumptionKeys(tableau)));
            }

            bool ok = verdictOk && contextDecided && subsOk && alcOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + contextDecided + " | " + subsNote + " | " + alcNote + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " contextDecided=" + contextDecided + " subs=" + subsNote + " alc=" + alcNote);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The nominal boundary rows (ground-truth sheet NOM-1..5, the certified DECL-FLIP):
    /// the nominal tier decides the plain nominal shapes whole — a <c>oneOf</c>
    /// enumeration, a <c>hasValue</c> restriction, a nominal buried inside an
    /// otherwise-admitted union, and a nominal operand inside a
    /// <c>DisjointUnion</c> all CONTEXT-DECIDE consistent, with the ground-truth sheet's
    /// exact named-subsumption sets (empty everywhere except NOM-5's
    /// <c>Birch ⊑ Ash</c> covering face). NOM-3 — a key over a NON-ATOMIC keyed
    /// class — still DELEGATES whole: its blocker is the ground key rung's S8
    /// admission boundary, untouched by nominal admission, and the fallback's
    /// verdict stays fragment-relative over a non-empty unsupported-construct
    /// remainder.
    /// </summary>
    [TestMethod]
    public void Tier2NominalBoundaryDeclineRows()
    {
        (string Name, ReasoningModule Module, bool Decides, string[] ExpectedSubsumptions)[] rows =
        [
            //NOM-1: a positive oneOf superclass; consistent; the enumeration superclass is unnamed.
            ("NOM-1", Module(SubClassOf(Class("Ash"), OneOf("ida", "idb"))), true, []),
            //NOM-2: a positive hasValue superclass; consistent; no named subsumption.
            ("NOM-2", Module(SubClassOf(Class("Ash"), HasValue("feeds", "ida"))), true, []),
            //NOM-3: a key over a non-atomic keyed class — the S8 admission grammar declines the
            //axiom whole (no membership readout under the atom-only ground join); consistent.
            ("NOM-3", Module(new OwlHasKeyAxiom(Union(Class("Ash"), Class("Birch")), [Property("feeds")], []) { Origin = Origin("haskey") }), false, []),
            //NOM-4: a nominal buried inside an otherwise-admitted positive union; consistent.
            ("NOM-4", Module(SubClassOf(Class("Ash"), Union(Class("Birch"), OneOf("ida")))), true, []),
            //NOM-5: a nominal operand inside a DisjointUnion — the covering inclusion carries the
            //one named subsumption; the disjointness face carries none.
            ("NOM-5", Module(DisjointUnion("Ash", Class("Birch"), OneOf("ida"))), true, [Sub("Birch", "Ash")]),
        ];

        StringBuilder report = new();
        report.AppendLine("\nrow | contextDecided | remainder | consistent | subs");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool decides, string[] expectedSubsumptions) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool contextDecided = decision.Statistics.ContextTotals.ContextDecided;
            bool remainderNamed = decision.Verdict!.UnsupportedConstructs.Count > 0;
            if(decides)
            {
                List<string> expected = [.. expectedSubsumptions];
                List<string> actual = SubsumptionKeys(decision.Verdict);
                bool subsOk = KeysEqual(expected, actual);
                report.AppendLine(name + " | " + contextDecided + " | " + remainderNamed + " | " + decision.Verdict.IsConsistent + " | " + (subsOk ? "ok" : DiffKeys(expected, actual)));
                if(!contextDecided || !decision.Verdict.IsConsistent || !subsOk)
                {
                    mismatches.Add(name + ": contextDecided=" + contextDecided + " consistent=" + decision.Verdict.IsConsistent + " subsOk=" + subsOk);
                }
            }
            else
            {
                report.AppendLine(name + " | " + contextDecided + " | " + remainderNamed + " | " + decision.Verdict.IsConsistent + " | n/a");
                if(contextDecided || !remainderNamed)
                {
                    mismatches.Add(name + ": contextDecided=" + contextDecided + " remainderNamed=" + remainderNamed);
                }
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The battery rows: ground-truth sheet id, module, ground-truth consistency, the
    /// exact expected module-local subsumption set, and whether the ALC(H)
    /// tableau offers a comparand (false on every cardinality-bearing row,
    /// which the tableau cannot decide).
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, string[] ExpectedSubsumptions, bool AlcComparand)[] BatteryRows()
    {
        return
        [
            //HORN-1 (control): a told chain. Model: Ash=Birch=Cedar={e}. Pairs = the chain closure.
            ("HORN-1", Module(
                SubClassOf(Class("Ash"), Class("Birch")),
                SubClassOf(Class("Birch"), Class("Cedar"))),
                true, [Sub("Ash", "Birch"), Sub("Ash", "Cedar"), Sub("Birch", "Cedar")], true),

            //HORN-2 (control): disjointness clash on one individual. ida in Ash and Birch, Ash n Birch [= bot.
            ("HORN-2", Module(
                Disjoint(Class("Ash"), Class("Birch")),
                ClassAssertion(Class("Ash"), Individual("ida")),
                ClassAssertion(Class("Birch"), Individual("ida"))),
                false, [], true),

            //COV-1: case analysis over the covering. Both branches reach Oak, so Ash [= Oak; neither disjunct
            //is entailed. Model: x in Ash,Birch,Oak.
            ("COV-1", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Oak")),
                SubClassOf(Class("Cedar"), Class("Oak"))),
                true, [Sub("Ash", "Oak"), Sub("Birch", "Oak"), Sub("Cedar", "Oak")], true),

            //COV-2: the Cedar branch stays open, so Ash [= Oak is NOT entailed. Countermodel: x in Ash,Cedar, not Oak.
            ("COV-2", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Oak"))),
                true, [Sub("Birch", "Oak")], true),

            //COV-3: the pairwise-disjointness half of a disjoint union clashes one individual asserted into
            //both members: idb in Birch and Cedar, Birch n Cedar [= bot.
            ("COV-3", Module(
                DisjointUnion("Ash", Class("Birch"), Class("Cedar")),
                ClassAssertion(Class("Birch"), Individual("idb")),
                ClassAssertion(Class("Cedar"), Individual("idb"))),
                false, [], true),

            //COV-4: covering with a dead disjunct. Birch [= bot kills the Birch branch, so Ash [= Cedar; the
            //members give Birch [= Ash, Cedar [= Ash; unsatisfiable Birch is subsumed by everything. Model:
            //ida in Ash,Cedar. The ground-truth sheet's individual-level query (ida a Cedar) is COV-4r below.
            ("COV-4", Module(
                DisjointUnion("Ash", Class("Birch"), Class("Cedar")),
                SubClassOf(Class("Birch"), NothingReference),
                ClassAssertion(Class("Ash"), Individual("ida"))),
                true, [Sub("Ash", "Cedar"), Sub("Birch", "Ash"), Sub("Birch", "Cedar"), Sub("Cedar", "Ash")], true),

            //COV-4r: the refutation encoding of the ground-truth sheet's ida-a-Cedar entailment — asserting ida into
            //the complement of Cedar contradicts the forced Cedar membership, so the module is inconsistent.
            ("COV-4r", Module(
                DisjointUnion("Ash", Class("Birch"), Class("Cedar")),
                SubClassOf(Class("Birch"), NothingReference),
                ClassAssertion(Class("Ash"), Individual("ida")),
                ClassAssertion(Complement(Class("Cedar")), Individual("ida"))),
                false, [], true),

            //COV-5: the member-inclusion direction of the equivalence (Horn face). Model: x in Birch,Ash.
            ("COV-5", Module(
                Equivalent(Class("Ash"), Union(Class("Birch"), Class("Cedar")))),
                true, [Sub("Birch", "Ash"), Sub("Cedar", "Ash")], true),

            //COV-6: the covering direction of the equivalence — case analysis gives Ash [= Fir on top of the
            //member inclusions. Model: x in Birch,Ash,Fir.
            ("COV-6", Module(
                Equivalent(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Fir")),
                SubClassOf(Class("Cedar"), Class("Fir"))),
                true, [Sub("Ash", "Fir"), Sub("Birch", "Ash"), Sub("Birch", "Fir"), Sub("Cedar", "Ash"), Sub("Cedar", "Fir")], true),

            //UNI-1: two-step case analysis — the covering resolves to Elm, which chains to Fir.
            //Model: x in Ash,Birch,Elm,Fir.
            ("UNI-1", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Elm")),
                SubClassOf(Class("Cedar"), Class("Elm")),
                SubClassOf(Class("Elm"), Class("Fir"))),
                true, [Sub("Ash", "Elm"), Sub("Ash", "Fir"), Sub("Birch", "Elm"), Sub("Birch", "Fir"), Sub("Cedar", "Elm"), Sub("Cedar", "Fir"), Sub("Elm", "Fir")], true),

            //UNI-2: disjunct absorption — Birch [= Cedar folds the Birch branch into Cedar, so Ash [= Cedar;
            //the absorbed disjunct itself is not entailed. Model: x in Ash,Cedar.
            ("UNI-2", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Cedar"))),
                true, [Sub("Ash", "Cedar"), Sub("Birch", "Cedar")], true),

            //UNI-3: nested-union flattening — all three leaves reach Oak. Model: x in Ash,Birch,Oak.
            ("UNI-3", Module(
                SubClassOf(Class("Ash"), Union(Union(Class("Birch"), Class("Cedar")), Class("Elm"))),
                SubClassOf(Class("Birch"), Class("Oak")),
                SubClassOf(Class("Cedar"), Class("Oak")),
                SubClassOf(Class("Elm"), Class("Oak"))),
                true, [Sub("Ash", "Oak"), Sub("Birch", "Oak"), Sub("Cedar", "Oak"), Sub("Elm", "Oak")], true),

            //UNI-4: a non-atomic disjunct — the existential branch is abstracted, and both branches reach
            //Oak. Model: x in Ash,Birch,Oak.
            ("UNI-4", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Some("feeds", Class("Cedar")))),
                SubClassOf(Class("Birch"), Class("Oak")),
                SubClassOf(Some("feeds", Class("Cedar")), Class("Oak"))),
                true, [Sub("Ash", "Oak"), Sub("Birch", "Oak")], true),

            //UNI-5: a disjunct eliminated by disjointness — Ash n Birch [= bot closes the Birch branch on
            //Ash itself, forcing Ash [= Cedar. Model: x in Ash,Cedar.
            ("UNI-5", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                Disjoint(Class("Ash"), Class("Birch"))),
                true, [Sub("Ash", "Cedar")], true),

            //NEG-1: the negative complement totalizes — not-Birch [= Cedar is Top [= Birch | Cedar, so EVERY
            //class reaches Oak through the covering, including the upward-unconstrained Fir and Yew (the
            //Yew [= Fir bystander only mentions them into the sweep signature; the ground-truth sheet states the row
            //over an axiom-free Fir). Model: x in Birch,Oak (and Yew,Fir,Birch,Oak for the bystanders).
            ("NEG-1", Module(
                SubClassOf(Complement(Class("Birch")), Class("Cedar")),
                SubClassOf(Class("Birch"), Class("Oak")),
                SubClassOf(Class("Cedar"), Class("Oak")),
                SubClassOf(Class("Yew"), Class("Fir"))),
                true, [Sub("Birch", "Oak"), Sub("Cedar", "Oak"), Sub("Fir", "Oak"), Sub("Yew", "Fir"), Sub("Yew", "Oak")], true),

            //NEG-2 (control): the positive complement — ida in Ash [= not-Birch and in Birch clashes.
            ("NEG-2", Module(
                SubClassOf(Class("Ash"), Complement(Class("Birch"))),
                ClassAssertion(Class("Ash"), Individual("ida")),
                ClassAssertion(Class("Birch"), Individual("ida"))),
                false, [], true),

            //NEG-3: the double complement collapses under NNF — Ash [= Birch. Model: x in Ash,Birch.
            ("NEG-3", Module(
                SubClassOf(Class("Ash"), Complement(Complement(Class("Birch"))))),
                true, [Sub("Ash", "Birch")], true),

            //DL4-1: three forced pairwise-distinct successors under an unqualified max-2 refute by the
            //pigeonhole, so Ash is unsatisfiable — subsumed by every signature class. The module itself is
            //consistent (Ash empty; B1,B2,B3 pairwise-disjoint singletons). No tableau comparand.
            ("DL4-1", Module(
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3"))),
                true, [Sub("Ash", "B1"), Sub("Ash", "B2"), Sub("Ash", "B3")], false),

            //DL4-2 (control): only two forced successors satisfy max-2. Model: a feeds b1 in B1, a feeds b2
            //in B2 (B3 empty). No entailed pairs.
            ("DL4-2", Module(
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3"))),
                true, [], false),

            //DL4-5: the exact-2 upper half carries the same pigeonhole as DL4-1.
            ("DL4-5", Module(
                SubClassOf(Class("Ash"), Exact("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3"))),
                true, [Sub("Ash", "B1"), Sub("Ash", "B2"), Sub("Ash", "B3")], false),

            //DL4-6 (control): two distinct successors meet both exact-2 bounds. Model: a feeds b1 in B1,
            //a feeds b2 in B2.
            ("DL4-6", Module(
                SubClassOf(Class("Ash"), Exact("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                Disjoint(Class("B1"), Class("B2"))),
                true, [], false),

            //FACT-1: partial distinctness — only B1/B2 are disjoint, so the B3 witness merges with either
            //(model: a feeds b1 in B1,B3 and b2 in B2). Ash stays satisfiable; a false s != s disjunct
            //arising mid-merge must drop alone for this row to hold.
            ("FACT-1", Module(
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2"))),
                true, [], false),

            //FACT-2: two disjoint pairs still leave the B2/B3 merge open. Model: a feeds b1 in B1 and
            //b23 in B2,B3.
            ("FACT-2", Module(
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3"))),
                true, [], false),

            //CTRL-1 (control): the shipped max-1 merge clash — two disjoint forced successors under
            //max-1 make Ash unsatisfiable.
            ("CTRL-1", Module(
                SubClassOf(Class("Ash"), Max("feeds", 1, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("Birch"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("Cedar"))),
                Disjoint(Class("Birch"), Class("Cedar"))),
                true, [Sub("Ash", "Birch"), Sub("Ash", "Cedar")], false),

            //DUAL-1: an asserted min-2 against a max-1 TBox bound — the refutation-encoding shape. The
            //asserted witnesses are pairwise distinct, the merge forces them equal: clash.
            ("DUAL-1", Module(
                SubClassOf(Class("Ash"), Max("feeds", 1, null)),
                ClassAssertion(Class("Ash"), Individual("ida")),
                ClassAssertion(Min("feeds", 2, null), Individual("ida"))),
                false, [], false),

            //DUAL-2: the negative min dualizes — not(>=2) = <=1 — and the two asserted existential
            //successors in disjoint classes clash under the merge.
            ("DUAL-2", Module(
                SubClassOf(Class("Ash"), Complement(Min("feeds", 2, null))),
                Disjoint(Class("Birch"), Class("Cedar")),
                ClassAssertion(Class("Ash"), Individual("ida")),
                ClassAssertion(Some("feeds", Class("Birch")), Individual("ida")),
                ClassAssertion(Some("feeds", Class("Cedar")), Individual("ida"))),
                false, [], false),

            //DUAL-3: the n=1 edge — not(>=1) = <=0 against one forced successor.
            ("DUAL-3", Module(
                SubClassOf(Class("Ash"), Complement(Min("feeds", 1, null))),
                ClassAssertion(Class("Ash"), Individual("ida")),
                ClassAssertion(Some("feeds", Class("Birch")), Individual("ida"))),
                false, [], false),

            //DUAL-4: not(<=1) = >=2 forces two successors, the feeds-range (the ground-truth sheet's Top [= forall
            //feeds.Fir in its range-axiom spelling) puts them in Fir [= bot: clash.
            ("DUAL-4", Module(
                SubClassOf(Class("Ash"), Complement(Max("feeds", 1, null))),
                Range("feeds", Class("Fir")),
                SubClassOf(Class("Fir"), NothingReference),
                ClassAssertion(Class("Ash"), Individual("ida"))),
                false, [], false),

            //ZERO-1: max-0 against a forced successor — the no-successor Horn bottom clause. Ash
            //unsatisfiable; module consistent with Ash empty.
            ("ZERO-1", Module(
                SubClassOf(Class("Ash"), Max("feeds", 0, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("Birch")))),
                true, [Sub("Ash", "Birch")], false),

            //ZERO-2: exact-0 alone forces nothing — ida inhabits Ash with no feeds successors.
            ("ZERO-2", Module(
                SubClassOf(Class("Ash"), Exact("feeds", 0, null)),
                ClassAssertion(Class("Ash"), Individual("ida"))),
                true, [], false),

            //ZERO-3: exact-0 against a forced successor refutes through its max half.
            ("ZERO-3", Module(
                SubClassOf(Class("Ash"), Exact("feeds", 0, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("Birch")))),
                true, [Sub("Ash", "Birch")], false),

            //NEU-1: answer neutrality — a genuine open covering entails NEITHER disjunct; the exact-empty
            //set is the strongest module-level form of the ground-truth sheet's NEU-1a/NEU-1b pair. Countermodels
            //pick either branch.
            ("NEU-1", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar")))),
                true, [], true),

            //NEU-2: the disjuncts coincide — Pine == Birch collapses the covering legitimately, so
            //Ash [= Birch IS entailed (with the equivalence pairs). Model: x in Ash,Birch,Pine.
            ("NEU-2", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Pine"))),
                Equivalent(Class("Pine"), Class("Birch"))),
                true, [Sub("Ash", "Birch"), Sub("Ash", "Pine"), Sub("Birch", "Pine"), Sub("Pine", "Birch")], true),

            //NEU-3 (control for NEU-2): without the equivalence the covering stays open.
            ("NEU-3", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Pine")))),
                true, [], true),

            //SUCC-1: the existential disjunct dies via successor reasoning — Birch [= bot condemns the
            //exists-feeds-Birch branch, the head narrows to Cedar, and Cedar [= Oak chains. Unsatisfiable
            //Birch is subsumed by everything. Model: x in Ash,Cedar,Oak.
            ("SUCC-1", Module(
                SubClassOf(Class("Ash"), Union(Class("Cedar"), Some("feeds", Class("Birch")))),
                SubClassOf(Class("Birch"), NothingReference),
                SubClassOf(Class("Cedar"), Class("Oak"))),
                true, [Sub("Ash", "Cedar"), Sub("Ash", "Oak"), Sub("Birch", "Ash"), Sub("Birch", "Cedar"), Sub("Birch", "Oak"), Sub("Cedar", "Oak")], true),

            //SUCC-2: the universal kills the existential disjunct — every feeds-successor of Ash is
            //not-Birch, so the exists-feeds-Birch branch is impossible on Ash. Model: x in Ash,Cedar.
            ("SUCC-2", Module(
                SubClassOf(Class("Ash"), Union(Class("Cedar"), Some("feeds", Class("Birch")))),
                SubClassOf(Class("Ash"), All("feeds", Complement(Class("Birch"))))),
                true, [Sub("Ash", "Cedar")], true),

            //SUCC-3/SUCC-4 (one module, both queries): the feeds-range (the ground-truth sheet's Top [= forall
            //feeds.Elm) clashes the existential branch's successor with Birch n Elm [= bot — the successor
            //clash condemns only that DISJUNCT, never Ash itself (the premature-clash guard), so Ash stays
            //satisfiable AND the surviving Cedar branch is forced. Model: x in Ash,Cedar; Birch={b} with no
            //incoming feeds edge.
            ("SUCC-3", Module(
                SubClassOf(Class("Ash"), Union(Class("Cedar"), Some("feeds", Class("Birch")))),
                Range("feeds", Class("Elm")),
                Disjoint(Class("Birch"), Class("Elm"))),
                true, [Sub("Ash", "Cedar")], true),

            //CARRY-1/CARRY-2 (one module, both queries): the whole pigeonhole apparatus rides under a
            //carried covering disjunct — Rowan is unsatisfiable by the DL4-1 pigeonhole (subsumed by
            //everything), the dead branch peels away leaving Ash [= Cedar, and Ash keeps its Cedar model
            //(consistent). No tableau comparand.
            ("CARRY-1", Module(
                SubClassOf(Class("Ash"), Union(Class("Cedar"), Class("Rowan"))),
                SubClassOf(Class("Rowan"), Max("feeds", 2, null)),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3"))),
                true, [Sub("Ash", "Cedar"), Sub("Rowan", "Ash"), Sub("Rowan", "B1"), Sub("Rowan", "B2"), Sub("Rowan", "B3"), Sub("Rowan", "Cedar")], false),

            //TAX-1/TAX-2 (one module, both queries): inherited case analysis — Pine [= Ash inherits the
            //covering's resolved Elm; the exact set also certifies TAX-2's NO (Elm [= Ash absent).
            //Model: x in Pine,Ash,Birch,Elm.
            ("TAX-1", Module(
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Elm")),
                SubClassOf(Class("Cedar"), Class("Elm")),
                SubClassOf(Class("Pine"), Class("Ash"))),
                true, [Sub("Ash", "Elm"), Sub("Birch", "Elm"), Sub("Cedar", "Elm"), Sub("Pine", "Ash"), Sub("Pine", "Elm")], true),

            //SWEEP-1: the module-sweep-signature regression witness — the disjoint union's named member
            //rides beside an out-of-ALC cardinality operand (ordered first, so the failing ALC translation
            //never reaches Birch), and only the sweep walk's DisjointUnion arm puts Birch into the
            //signature; without it the true member inclusion Birch [= Cedar is silently unswept. Model:
            //all classes empty. No tableau comparand.
            ("SWEEP-1", Module(
                DisjointUnion("Cedar", Min("feeds", 1, Class("Oak")), Class("Birch"))),
                true, [Sub("Birch", "Cedar")], false),
        ];
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

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
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

    /// <summary>The reserved <c>owl:Nothing</c> reference.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
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

    /// <summary>A qualified or unqualified minimum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
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

    /// <summary>A qualified or unqualified exact-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Exact(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), filler);
    }

    /// <summary>An individual-value restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An enumeration of individuals in the example namespace.</summary>
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

    /// <summary>A pairwise disjointness axiom.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = Origin("disjoint") };
    }

    /// <summary>A disjoint-union axiom defining a class as the disjoint union of its operands.</summary>
    /// <param name="definedClass">The defined class's local name.</param>
    /// <param name="operands">The member expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointUnionAxiom DisjointUnion(string definedClass, params OwlClassExpression[] operands)
    {
        return new OwlDisjointUnionAxiom(new NamedNode(Utf8Strings.From(Example + definedClass)), operands) { Origin = Origin("disjointunion") };
    }

    /// <summary>A role range axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="range">The range class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(property), range) { Origin = Origin("range") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

}
