using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The branching modal-gadget habitat decider's battery: the measured
/// instance's module through the builders with all six statistics fields riding
/// the certificate, the nine refutation-probe rows the conformance arm's own
/// builder shape produces, the builder-built fence row for the probe forms this
/// module does NOT produce, the premise-direction pin, the twelve attack rows
/// one per named guard — the cardinality-side goal, the two-model membership
/// read, the silent construction failures, the whole-module admission, the two
/// jurisdiction postures, the bound read by value, the polarity read from the
/// bound, the modal role's characteristics, the label under the probe's added
/// complement, the partial-conjunct composition, and the verification pass as
/// two independent rows for its two independent claims — the three rule and
/// phase rows, the eleven window rows with their discriminants, the near-miss
/// bound against the widened walk, the explicit dark control, and the habitat
/// ordering. Every completeness limit is asserted as a SILENCE carrying its
/// measurement, never as a verdict, and every guard row carries the
/// discrimination control whose correct reading DOES reach the decision.
/// </summary>
[TestClass]
internal sealed class ContextModalGadgetTreeDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, properties and individuals are drawn from.</summary>
    private const string Example = "http://example.org/modalgadgettree#";

    /// <summary>The second namespace the opaque-name row draws its renamed twins from.</summary>
    private const string Alternate = "http://example.org/modalgadgetalt#";

    /// <summary>The <c>owl:Thing</c> IRI — the built-in whose extension the semantics fixes rather than the module.</summary>
    private const string OwlThing = "http://www.w3.org/2002/07/owl#Thing";

    /// <summary>The <c>owl:Nothing</c> IRI — the built-in empty class.</summary>
    private const string OwlNothing = "http://www.w3.org/2002/07/owl#Nothing";

    /// <summary>A datatype IRI qualifying a data cardinality restriction, taking it outside the admission grammar.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>Both modal-gadget faces lit — the selection the deciding rows drive.</summary>
    private const EnumerationDeciderFaces ModalGadgetFaces = EnumerationDeciderFaces.ModalGadgetClash | EnumerationDeciderFaces.ModalGadgetCertify;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the jurisdiction and ordering rows drive against the explicit dark control.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>Every face lit EXCEPT the two modal-gadget ones — the selection the dark control compares the explicit dark run against.</summary>
    private static EnumerationDeciderFaces AllFacesButModalGadget { get; } = AllFaces & ~ModalGadgetFaces;

    /// <summary>The measured instance's surviving free gadget atoms after defined-atom elimination.</summary>
    private const int CorpusFreeAtoms = 5;

    /// <summary>The measured instance's deduped successor demands — six propositional signatures beside one non-propositional filler.</summary>
    private const int CorpusSignatures = 7;

    /// <summary>The measured instance's raw modal atoms at the told individual — thirteen existential occurrences beside three told universals, both quantifier kinds counted.</summary>
    private const int CorpusRawModalAtoms = 16;

    /// <summary>The measured instance's raw gadget atoms before elimination — five free, thirty intersection-defined and four modal-defined.</summary>
    private const int CorpusRawGadgetAtoms = 39;

    /// <summary>The measured instance's intersection-defined gadget properties.</summary>
    private const int CorpusIntersectionDefined = 30;

    /// <summary>The measured instance's modal-defined gadget properties.</summary>
    private const int CorpusModalDefined = 4;

    /// <summary>The measured instance's plain existential classes, which stand beside the modal-defined ones inside its thirteen existential occurrences.</summary>
    private const int CorpusPlainExistentials = 9;

    /// <summary>The measured instance's told class assertions carrying a goal atom.</summary>
    private const int CorpusToldAtoms = 6;

    /// <summary>The measured instance's entailment goals — five one step from told facts, two two steps, one three steps.</summary>
    private const int CorpusGoals = 8;

    /// <summary>The bait composition axioms the measured instance's shape carries outside its goal cone.</summary>
    private const int CorpusBaitCompositions = 8;

    /// <summary>The composition-layer padding the ordering-independent fixtures carry so the recognizer's threshold clause admits them; the census signal reads MORE than the threshold, so the padding stands one axiom above it.</summary>
    private const int CompositionPadding = 40;

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on an admitted module, far below what a saturation of one would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>The ceiling the dark control drives: one inference attempt, so an admitted module the faces are dark on exhausts the engine budget and the census rides an abstention record rather than a decision.</summary>
    private static ReasoningBudget DarkBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1);

    /// <summary>
    /// The habitat's measured-instance premise through the builders: a
    /// propositional layer of thirty-nine unqualified cardinality gadgets whose
    /// defined-atom elimination leaves five free bits, a modal layer of thirteen
    /// existential occurrences over ONE characteristic-free role deduping to
    /// seven successor demands beside three told universals, a composition layer
    /// of forty-nine binary intersections over named classes, and a told ABox of
    /// one individual with no property assertion anywhere. The certify face
    /// mints its tree, verifies every admitted axiom against the finished
    /// structure's raw relations and certifies the premise consistent
    /// pre-engine, all six statistics fields riding the decision by name. The
    /// tree is the told frontier ALONE — the all-false modal vector is
    /// compatible, so nothing is spawned and the three told universals are
    /// vacuous because there is no edge for them to range over, which is exactly
    /// what a one-element model of this habitat relies on and exactly what
    /// materialising a box would destroy. The ABox closes with the premise's
    /// trailing <c>owl:Thing</c> typing, admitted CARRIER-ONLY and verified
    /// against the whole domain — every statistic below is pinned with that
    /// assertion carried, so the built-in admission contributes no atom, no
    /// signature, no node and no silence.
    /// </summary>
    [TestMethod]
    public void Mg1CertifyDecidesTheCorpusPremiseConsistent()
    {
        ReasoningModule module = CorpusShapedModule();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ModalGadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Mg1 CorpusPremise: the certify face decides the measured instance at the production ceiling.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Mg1 CorpusPremise: the minted tree satisfies every admitted axiom on re-check, so the premise has a model.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Mg1 CorpusPremise: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Mg1 CorpusPremise: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, totals.EnumerationHabitat, "Mg1 CorpusPremise: the module carries the Shape K census label.");
        Assert.AreEqual(CorpusFreeAtoms, totals.ModalGadgetFreeAtomCount, "Mg1 CorpusPremise: thirty-nine raw gadget atoms reduce to five free bits.");
        Assert.AreEqual(CorpusSignatures, totals.ModalGadgetSignatureCount, "Mg1 CorpusPremise: thirteen existential occurrences dedupe to seven successor demands.");
        Assert.AreEqual(1, totals.ModalGadgetNodesBuilt, "Mg1 CorpusPremise: the arena holds the told individual alone — ZERO successors spawned.");
        Assert.AreEqual(0, totals.ModalGadgetDeciderClashes, "Mg1 CorpusPremise: the clash face reaches no clash on the premise.");
        Assert.AreEqual(1, totals.ModalGadgetDeciderCertifications, "Mg1 CorpusPremise: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.ModalGadgetWindowSilences, "Mg1 CorpusPremise: the module sits inside every window ceiling.");

        ModalGadgetCertifyOutcome outcome = ContextModalGadgetTreeDecider.RunCertify(module);

        Assert.IsTrue(outcome.Consistent, "Mg1 CorpusPremise: the face itself certifies the module.");
        Assert.AreEqual(1, outcome.Window.NodesBuilt, "Mg1 CorpusPremise: a universal NEVER spawns, so the three told boxes range over an EMPTY role extension and are vacuously satisfied.");
    }

    /// <summary>The first goal probe: the composition closure derives the goal at the told individual, where the probe's own complement denies it.</summary>
    [TestMethod]
    public void Mg2FirstGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(1, "Mg2 FirstGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The second goal probe.</summary>
    [TestMethod]
    public void Mg3SecondGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(2, "Mg3 SecondGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The third goal probe.</summary>
    [TestMethod]
    public void Mg4ThirdGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(3, "Mg4 ThirdGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The fourth goal probe.</summary>
    [TestMethod]
    public void Mg5FourthGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(4, "Mg5 FourthGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The fifth goal probe — the last of the five goals one composition step from the told facts.</summary>
    [TestMethod]
    public void Mg6FifthGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(5, "Mg6 FifthGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The sixth goal probe — two composition steps from the told facts.</summary>
    [TestMethod]
    public void Mg7SixthGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(6, "Mg7 SixthGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The seventh goal probe — two composition steps from the told facts.</summary>
    [TestMethod]
    public void Mg8SeventhGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(7, "Mg8 SeventhGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>The eighth goal probe — the deepest, three composition steps from the told facts.</summary>
    [TestMethod]
    public void Mg9EighthGoalProbeDecidesInconsistent()
    {
        AssertGoalProbeDecides(8, "Mg9 EighthGoalProbe", TestContext.CancellationToken);
    }

    /// <summary>
    /// The ninth refutation probe: the one the conclusion's <c>owl:Thing</c>
    /// wrappers produce, which the harness's vacuity predicate does not filter. A
    /// told complement of the universal class is the told bottom, and the clash
    /// face reads it directly rather than deriving anything at all.
    /// </summary>
    [TestMethod]
    public void Mg10ThingWrapperProbeDecidesInconsistent()
    {
        ReasoningModule module = ThingWrapperProbeModule();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ModalGadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Mg10 ThingWrapperProbe: the clash face decides the ninth probe module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Mg10 ThingWrapperProbe: the probe module is inconsistent, so its conclusion follows from the premise.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Mg10 ThingWrapperProbe: the probe decides with zero inference attempts.");
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, totals.EnumerationHabitat, "Mg10 ThingWrapperProbe: the habitat label is UNCHANGED from the premise module's.");
        Assert.AreEqual(1, totals.ModalGadgetDeciderClashes, "Mg10 ThingWrapperProbe: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.ModalGadgetDeciderCertifications, "Mg10 ThingWrapperProbe: the certify face never decides a probe module.");
        Assert.AreEqual(ModalGadgetClashReasons.AssertedBottomMembership, ContextModalGadgetTreeDecider.RunClash(module).Reason, "Mg10 ThingWrapperProbe: the clash reason is the asserted bottom, not a complemented membership.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg10 ThingWrapperProbe: the certify face is structurally silent on the probe module.");
    }

    /// <summary>
    /// The probe fence is B-J1's <c>ClassAssertion</c> arm — <c>named</c> or
    /// <c>ALL namedRole.named</c> only — and NOT the complement ban beside it:
    /// "every probe module carries a complement" is true of the ONE arm this
    /// module's nine probes use and false of the seven complement-free arms the
    /// harness's own builder carries. Two silence legs replicate probe forms this
    /// module does NOT produce — the anonymous-individual
    /// <c>SubClassOf(C, owl:Nothing)</c> arm and one data-cardinality De Morgan
    /// dual, both complement-free — so the fence is CHECKED against the builder's
    /// shapes rather than asserted of them. The re-check leg discharges the
    /// widening protocol for the carrier-only <c>owl:Thing</c> arm: the one probe
    /// form whose class references the built-in wraps it in a COMPLEMENT, which is
    /// not the span-exact class reference the arm reads, so no probe form the
    /// harness builds can reach the widened admission.
    /// </summary>
    [TestMethod]
    public void Mg11ProbeFenceIsTheClassAssertionArmNotTheComplement()
    {
        ReasoningModule anonymousProbe = Module([.. Append(CorpusShapedAxioms(), ReplicatedAnonymousIndividualRefutation(Class("Goal1")))]);
        ReasoningModule dualProbe = Module([.. Append(CorpusShapedAxioms(), ReplicatedDataCardinalityDual(Individual("v"), "f1", 1))]);

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(anonymousProbe).Consistent, "Mg11 ProbeFence: the anonymous-individual probe carries a SubClassOf, which B-J1's axiom-kind allow-list does not admit, so the certify face silences the module whole.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(dualProbe).Consistent, "Mg11 ProbeFence: the data-cardinality dual asserts a cardinality class, which B-J1's ClassAssertion arm does not admit — and it carries NO complement at all.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(ThingWrapperProbeModule()).Consistent, "Mg11 ProbeFence: the ninth probe's complement of owl:Thing is a complement EXPRESSION and not the span-exact built-in the carrier-only arm reads, so the widened admission stays unreachable from every probe form the harness builds.");
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule()).Consistent, "Mg11 ProbeFence: the same premise without a probe axiom certifies — its trailing owl:Thing typing carried — so each silence is the ClassAssertion arm's doing.");
    }

    /// <summary>
    /// The premise direction pinned inside the face battery: the corpus premise
    /// certifies CONSISTENT and the clash face is SILENT on the SAME module, so
    /// no face-level regression can turn a consistency premise into a
    /// refutation. Battery hygiene rather than a hole guard — the conformance
    /// arm pins the direction independently, walking every declared kind off the
    /// manifest and asserting the premise consistent whenever the consistency
    /// kind stands.
    /// </summary>
    [TestMethod]
    public void Mg12PremiseDirectionPinnedInsideTheFaceBattery()
    {
        ReasoningModule module = CorpusShapedModule();

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg12 PremiseDirection: the premise's own direction is CONSISTENT and the certify face reaches it.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(module).Consistent, "Mg12 PremiseDirection: the clash face is silent on the same module — a wrong refutation here would fail the conformance arm loudly.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(module).Reason, "Mg12 PremiseDirection: a silent face names no clash reason.");
    }

    /// <summary>
    /// The clash face NEVER reads a cardinality bound: a goal reachable only
    /// through the cardinality side — a second class equivalent to the same
    /// unqualified minimum the told type carries — is not derived, so its probe
    /// module stays silent. The design rule is the guard, not a property of the
    /// instance: a monotone face that read a bound in a module with zero
    /// property assertions would have to read a value out of a constructed
    /// model, which is the membership read the family forbids at the type level.
    /// The discrimination control puts the same goal one composition step from
    /// the told facts on the same module shape, and the face DOES decide it.
    /// </summary>
    [TestMethod]
    public void Mg13ClashFaceNeverReadsACardinalityBound()
    {
        ModalGadgetClashOutcome throughTheBound = ContextModalGadgetTreeDecider.RunClash(CardinalitySideGoalModule());

        Assert.IsNull(throughTheBound.Consistent, "Mg13 CardinalityBound: a goal reachable only through the cardinality side is never derived, so the probe module is not refuted.");
        Assert.IsNull(throughTheBound.Reason, "Mg13 CardinalityBound: a silent face names no clash reason.");

        ModalGadgetClashOutcome throughComposition = ContextModalGadgetTreeDecider.RunClash(CompositionSideGoalModule());

        Assert.IsFalse(throughComposition.Consistent, "Mg13 CardinalityBound: the same shape with the goal one composition step away DOES decide, so the silence is the missing bound rule and not a blanket refusal.");
        Assert.AreEqual(ModalGadgetClashReasons.ComplementedMembership(Utf8Strings.From(Example + "Reached")), throughComposition.Reason, "Mg13 CardinalityBound: the discrimination control's clash is the complemented membership.");
    }

    /// <summary>
    /// The certify model witnesses CONSISTENCY and nothing else. The fixture has
    /// TWO models that disagree about the told individual: one puts it in the
    /// zero-bound class, the other in the minimum-bound one. The certify face
    /// picks one, and NEITHER membership is emitted as a verdict by either face
    /// — both complement probes stay silent, so the choice is unreadable. The
    /// type level carries the same claim by construction: the certify outcome
    /// deconstructs into exactly a verdict and a window, so there is nowhere for
    /// a class, an individual, a pair or a model handle to be read from. The
    /// clash face's own reading is unchanged by whether the certify face ran.
    /// </summary>
    [TestMethod]
    public void Mg14CertifyEmitsNoMembershipAndTheTwoModelsAgreeOnNothingElse()
    {
        ReasoningModule module = TwoModelModule();
        ModalGadgetClashOutcome beforeCertify = ContextModalGadgetTreeDecider.RunClash(module);
        ModalGadgetCertifyOutcome certify = ContextModalGadgetTreeDecider.RunCertify(module);
        ModalGadgetClashOutcome afterCertify = ContextModalGadgetTreeDecider.RunClash(module);

        Assert.IsTrue(certify.Consistent, "Mg14 NoMembership: the module has a model and the certify face mints one.");
        Assert.AreEqual(beforeCertify, afterCertify, "Mg14 NoMembership: the clash face's derived reading is IDENTICAL whether or not the certify face ran — the two faces share no derivation structure.");

        (bool? consistent, ModalGadgetWindow window) = certify;

        Assert.IsTrue(consistent, "Mg14 NoMembership: the certify outcome's whole payload is a verdict and a window — this deconstruction would not compile against a type carrying a membership member.");
        Assert.AreEqual(certify.Window, window, "Mg14 NoMembership: the window is the second and last component of that payload.");

        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(TwoModelProbeModule(positive: false)).Consistent, "Mg14 NoMembership: the class the certify model satisfies at the told individual is NOT entailed — its probe stays silent.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(TwoModelProbeModule(positive: true)).Consistent, "Mg14 NoMembership: nor is its polarity partner, which the SECOND model satisfies instead — a membership read off either model would have been a coin toss.");
    }

    /// <summary>
    /// Every construction failure is SILENCE and never a verdict: the told
    /// unit-propagation contradiction, the exhausted vector sweep, and the failed
    /// verification pass each leave the certify face silent with no window
    /// silence charged, which is what separates a construction failure from a
    /// bound trip. REFUTATION BY EXHAUSTION is a NAMED BACKLOG item and not a
    /// gap in the guard set: reading the exhausted sweep as "no model of premise
    /// plus not-G exists, therefore G is entailed" needs a completeness lemma
    /// that must also argue a bounded tree suffices, and this rung ships without
    /// it deliberately.
    /// </summary>
    [TestMethod]
    public void Mg15ConstructionFailuresAreAlwaysSilent()
    {
        ModalGadgetCertifyOutcome contradiction = ContextModalGadgetTreeDecider.RunCertify(ToldContradictionModule());

        Assert.IsNull(contradiction.Consistent, "Mg15 SilentFailures: a told unit-propagation contradiction is silence — this face is certify-only and has no path to a refutation.");
        Assert.AreEqual(0, contradiction.Window.WindowSilences, "Mg15 SilentFailures: the told contradiction charges no window counter, so it is a construction failure and not a bound trip.");

        ModalGadgetCertifyOutcome exhausted = ContextModalGadgetTreeDecider.RunCertify(ExhaustedSweepModule());

        Assert.IsNull(exhausted.Consistent, "Mg15 SilentFailures: an exhausted sweep is silence and is NEVER read as a refutation — refutation by exhaustion is a named backlog item.");
        Assert.AreEqual(0, exhausted.Window.WindowSilences, "Mg15 SilentFailures: the sweep ran to its end inside every ceiling, so the silence is exhaustion rather than a charged bound.");
        Assert.IsGreaterThan(0, exhausted.Window.NodesBuilt, "Mg15 SilentFailures: the sweep did build structures — it is an exhausted search, not an unentered one.");

        ModalGadgetCertifyOutcome failedPass = ContextModalGadgetTreeDecider.RunCertify(FailedVerificationModule());

        Assert.IsNull(failedPass.Consistent, "Mg15 SilentFailures: a failed verification pass is silence, the candidate discarded rather than the module refuted.");
        Assert.AreEqual(0, failedPass.Window.WindowSilences, "Mg15 SilentFailures: no ceiling was reached, so the pass itself is what declined.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(FailedVerificationModule()).Consistent, "Mg15 SilentFailures: the clash face is indifferent to a failed construction it never observes.");
    }

    /// <summary>
    /// Anything outside the certify face's allow-list silences the module WHOLE,
    /// because a certify face that ignores an axiom certifies a structure that
    /// axiom may falsify. One silence leg per forbidden kind, the
    /// definition-graph legs, the unresolved import, and the BUILT-IN CLASS legs
    /// — the last carrying the face-A discrimination control, since a module
    /// equating <c>owl:Nothing</c> with an intersection whose operands are both
    /// told has NO MODEL and the clash face decides it correctly through the
    /// composition rule and the bottom clash while the certify face must not
    /// read either built-in as an ordinary atom. The ASSERTION position is the
    /// one place a built-in is admitted, and only half of it: the span-exact
    /// <c>owl:Thing</c> assertion admits CARRIER-ONLY and certifies under a label
    /// ceiling pinched to the chassis's own class count — the built-in interns no
    /// class-table row — while a lookalike named class in another namespace
    /// interns one and trips the same pinched ceiling, and an asserted
    /// <c>owl:Nothing</c> stays silenced with the clash face refuting the same
    /// module through its told-bottom read.
    /// </summary>
    [TestMethod]
    public void Mg16AdmissionSilencesTheModuleWhole()
    {
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(AdmittedChassisModule()).Consistent, "Mg16 Admission: the unperturbed chassis certifies, so every leg below measures its own perturbation.");

        AssertChassisSilences(ClassAssertion(All("pa", Class("Pa")), Individual("v")), "a universal on a gadget property");
        AssertChassisSilences(EquivalentClasses(Class("Alien"), Some("pa", Class("Pa"))), "an existential on a gadget property");
        AssertChassisSilences(new OwlObjectPropertyDomainAxiom(Property("r"), Class("Pa")) { Origin = Origin("domain") }, "a domain axiom");
        AssertChassisSilences(new OwlObjectPropertyRangeAxiom(Property("r"), Class("Pa")) { Origin = Origin("range") }, "a range axiom");
        AssertChassisSilences(new OwlDisjointObjectPropertiesAxiom([Property("r"), Property("pa")]) { Origin = Origin("disjointroles") }, "a disjoint-properties axiom");
        AssertChassisSilences(new OwlHasKeyAxiom(Class("Pa"), [Property("r")], []) { Origin = Origin("key") }, "a key");
        AssertChassisSilences(new OwlSubObjectPropertyOfAxiom(Property("sub"), Property("r")) { Origin = Origin("subrole") }, "a sub-property into the modal role");
        AssertChassisSilences(EquivalentClasses(Class("Alien"), new OwlDataCardinality(OwlCardinalityKind.Min, 1, DataProperty("pc"), new OwlDatatypeReference(new NamedNode(Utf8Strings.From(XsdString))))), "a data range");
        AssertChassisSilences(PropertyAssertion(Individual("v"), "r", Individual("w")), "a told property assertion");
        AssertChassisSilences(new OwlImportAxiom(new NamedNode(Utf8Strings.From(Alternate + "imported"))) { Origin = Origin("import") }, "an unresolved import");

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(DefinitionCycleModule()).Consistent, "Mg16 Admission: a definition cycle has no evaluation order, so the module silences.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(SecondDefinerModule()).Consistent, "Mg16 Admission: a gadget property whose polarity pair carries TWO further definitions leaves the construction choosing between two functional dependencies, and choosing is guessing.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(UnpairedPolarityModule()).Consistent, "Mg16 Admission: a gadget property whose polarity pair cannot be identified is not free by default — it silences.");

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(NaryIntersectionModule(satisfied: true)).Consistent, "Mg16 Admission: an intersection's OWN arity is admitted n-ary with every operand named, and every operand is evaluated.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(NaryIntersectionModule(satisfied: false)).Consistent, "Mg16 Admission: dropping ONE operand's told support takes the whole axiom out of satisfaction, so no operand is matched away — the axiom-level arity is binary by construction, the abstract syntax's n-ary spelling never reaching this face as one axiom.");

        ReasoningModule bottomEquated = BottomEquatedModule();

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(bottomEquated).Consistent, "Mg16 Admission: owl:Nothing is neither defined nor free — its extension is fixed by the semantics and not by the module — so a module treating it as an ordinary atomic class silences the certify face.");
        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(bottomEquated).Consistent, "Mg16 Admission: the DISCRIMINATION control — face A knows what owl:Nothing means and decides the same module inconsistent through the composition rule and the bottom clash, so the certify silence costs no verdict.");
        Assert.AreEqual(ModalGadgetClashReasons.AssertedBottomMembership, ContextModalGadgetTreeDecider.RunClash(bottomEquated).Reason, "Mg16 Admission: the clash reason names the bottom membership.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(BottomFillerBoxModule()).Consistent, "Mg16 Admission: the same hole is reachable through the admitted universal arm with owl:Nothing as the filler, and it silences there too.");

        ReasoningModule topAsserted = Module([.. Append(AdmittedChassisAxioms(), ClassAssertion(Thing, Individual("v")))]);
        ReasoningModule bottomAsserted = Module([.. Append(AdmittedChassisAxioms(), ClassAssertion(Nothing, Individual("v")))]);
        ReasoningModule lookalikeAsserted = Module([.. Append(AdmittedChassisAxioms(), ClassAssertion(Class("Thing"), Individual("v")))]);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(topAsserted).Consistent, "Mg16 Admission: the span-exact owl:Thing assertion admits CARRIER-ONLY and the verification pass evaluates it against the built-in's whole-domain extension, so the chassis still certifies.");
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(topAsserted, Widened(label: 6)).Consistent, "Mg16 Admission: the built-in interns NO class-table row — a label ceiling pinched to the chassis's own six classes still admits the module.");
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(lookalikeAsserted).Consistent, "Mg16 Admission: a lookalike class NAMED Thing in another namespace is an ordinary atomic class and certifies as one.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(lookalikeAsserted, Widened(label: 6)).Consistent, "Mg16 Admission: the lookalike DOES intern a class-table row and trips the same pinched ceiling — the admission arm reads the span-exact IRI, never a local name.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(bottomAsserted).Consistent, "Mg16 Admission: owl:Nothing at the assertion position stays SILENCED — the built-in evaluation admits the top assertion alone, and no element can witness the empty extension.");
        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(bottomAsserted).Consistent, "Mg16 Admission: the control's clash-face expectation — face A reads the asserted bottom and refutes the module, so the certify silence costs no verdict.");
        Assert.AreEqual(ModalGadgetClashReasons.AssertedBottomMembership, ContextModalGadgetTreeDecider.RunClash(bottomAsserted).Reason, "Mg16 Admission: the asserted bottom names its reason.");
    }

    /// <summary>
    /// The two jurisdiction postures stay separate: the clash face IGNORES an
    /// axiom it does not recognize, licensed by the exhaustive "no rule
    /// consults" exclusion list, and still decides its clash; the certify face is
    /// whole-module ALL-OR-NOTHING and silences on the same module. The dispatch
    /// leg carries the resolution the entry order alone does not fix: on a module
    /// the clash face decides, the reported verdict is INCONSISTENT and the
    /// certify face is NOT ENTERED, so the two faces' verdicts are never both
    /// read on one module.
    /// </summary>
    [TestMethod]
    public void Mg17JurisdictionPosturesStaySeparate()
    {
        ReasoningModule module = AlienAxiomClashModule();

        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(module).Consistent, "Mg17 Postures: the clash face ignores the alien axiom and still reaches its clash — adding axioms can never break a clash.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg17 Postures: the certify face silences the SAME module whole — adding axioms can always break a model.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusProbeModule(1), ModalGadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.IsFalse(decision.Verdict!.IsConsistent, "Mg17 Postures: a decided clash is what the dispatch reports.");
        Assert.AreEqual(1, totals.ModalGadgetDeciderClashes, "Mg17 Postures: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.ModalGadgetDeciderCertifications, "Mg17 Postures: a DECIDED CLASH SUPPRESSES the certify entry entirely, so no write order can decide which face is reported.");
    }

    /// <summary>
    /// A cardinality bound is read from the PARSED NUMERIC VALUE and never from
    /// a datatype IRI. The structural surface the decider reads carries the
    /// value alone — the lexical form and its datatype are discharged upstream
    /// in the RDF mapper — so no datatype-IRI gate is reachable here at all, and
    /// the two spellings of each informative bound read IDENTICALLY: an exact
    /// one reads as the minimum side and an exact zero as the zero side. A bound
    /// that constrains nothing leaves the polarity pair unidentifiable and
    /// silences, and the one position where a datatype IRI COULD stand — a
    /// qualified data cardinality's range — takes the module outside the
    /// admission grammar rather than into a datatype comparison.
    /// </summary>
    [TestMethod]
    public void Mg18CardinalityBoundsReadByValueNeverByDatatypeIri()
    {
        ModalGadgetCertifyOutcome plain = ContextModalGadgetTreeDecider.RunCertify(BoundSpellingModule(exactMinimum: false, exactZero: false));
        ModalGadgetCertifyOutcome exactMinimum = ContextModalGadgetTreeDecider.RunCertify(BoundSpellingModule(exactMinimum: true, exactZero: false));
        ModalGadgetCertifyOutcome exactZero = ContextModalGadgetTreeDecider.RunCertify(BoundSpellingModule(exactMinimum: false, exactZero: true));
        ModalGadgetCertifyOutcome both = ContextModalGadgetTreeDecider.RunCertify(BoundSpellingModule(exactMinimum: true, exactZero: true));

        Assert.IsTrue(plain.Consistent, "Mg18 BoundsByValue: the minimum-and-maximum spelling certifies.");
        Assert.AreEqual(plain.Window, exactMinimum.Window, "Mg18 BoundsByValue: an exact one reads as the minimum side, measurement for measurement.");
        Assert.AreEqual(plain.Window, exactZero.Window, "Mg18 BoundsByValue: an exact zero reads as the zero side.");
        Assert.AreEqual(plain.Window, both.Window, "Mg18 BoundsByValue: both spellings together read identically — the VALUE decides and the flavour is read beside it.");

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(UninformativeBoundModule()).Consistent, "Mg18 BoundsByValue: a bound whose value is neither zero nor one constrains nothing and leaves the polarity pair unidentifiable, so the module silences.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(QualifiedRangeBoundModule()).Consistent, "Mg18 BoundsByValue: the one position a datatype IRI could stand in — a qualified data cardinality's range — is outside the admission grammar, so no datatype comparison is reachable from this face at all.");
    }

    /// <summary>
    /// Polarity is READ FROM THE BOUND and never inferred from a class's local
    /// name: the fixture names its minimum-side class after the zero side and
    /// its zero-side class after the minimum, so a name-driven reader swaps the
    /// two verdicts. The bound-driven reader finds the told types contradictory
    /// and silences; the discrimination control keeps the same inverted names
    /// and the same shape with the told types consistent, and certifies. The
    /// opaque-name leg replaces every local name with one carrying no polarity
    /// hint at all and asserts that no decision and no measurement moves.
    /// </summary>
    [TestMethod]
    public void Mg19PolarityReadFromTheBoundNeverFromTheName()
    {
        ModalGadgetCertifyOutcome contradictory = ContextModalGadgetTreeDecider.RunCertify(InvertedPolarityModule(coupleToMinimumSide: true, opaqueNames: false));

        Assert.IsNull(contradictory.Consistent, "Mg19 PolarityByBound: the told types pin the property TRUE through the class NAMED for the zero side and FALSE through the class named for the minimum side, so the bound-driven read finds a contradiction — a name-driven read would have certified.");

        ModalGadgetCertifyOutcome consistent = ContextModalGadgetTreeDecider.RunCertify(InvertedPolarityModule(coupleToMinimumSide: false, opaqueNames: false));

        Assert.IsTrue(consistent.Consistent, "Mg19 PolarityByBound: the DISCRIMINATION control couples the same names to the other polarity and certifies — a name-driven read would have silenced instead, so the pair swaps under the wrong reading.");

        Assert.AreEqual(contradictory, ContextModalGadgetTreeDecider.RunCertify(InvertedPolarityModule(coupleToMinimumSide: true, opaqueNames: true)), "Mg19 PolarityByBound: replacing every class local name with an opaque one moves no decision and no measurement.");
        Assert.AreEqual(consistent, ContextModalGadgetTreeDecider.RunCertify(InvertedPolarityModule(coupleToMinimumSide: false, opaqueNames: true)), "Mg19 PolarityByBound: nor on the control — no rule in either face consults a local name for any purpose.");
    }

    /// <summary>
    /// A characteristic on the modal role is REJECTED and never ignored: the
    /// finite-tree-model result that licenses the certificate is a K-logic
    /// result and does not survive transitivity, symmetry, functionality,
    /// inverse-functionality, an inverse pairing, or a domain or range on that
    /// role. The clash-only sibling family TOLERATES exactly those, because
    /// clash-only soundness needs no model bound at all — which is the cleanest
    /// one-sentence statement of why these are two shapes.
    /// </summary>
    [TestMethod]
    public void Mg20ModalRoleCharacteristicsAreRejectedNotIgnored()
    {
        AssertChassisSilences(Characteristic(OwlPropertyCharacteristic.Transitive, "r"), "a transitive modal role, whose transitivity VOIDS the depth-bounded tree the certificate stands on");
        AssertChassisSilences(Characteristic(OwlPropertyCharacteristic.Symmetric, "r"), "a symmetric modal role");
        AssertChassisSilences(Characteristic(OwlPropertyCharacteristic.Functional, "r"), "a functional modal role");
        AssertChassisSilences(Characteristic(OwlPropertyCharacteristic.InverseFunctional, "r"), "an inverse-functional modal role");
        AssertChassisSilences(new OwlInverseObjectPropertiesAxiom(Property("r"), Property("invR")) { Origin = Origin("inverse") }, "an inverse pairing on the modal role, which the tree bound does not survive either");
        AssertChassisSilences(new OwlObjectPropertyDomainAxiom(Property("r"), Class("Pa")) { Origin = Origin("domain") }, "a domain on the modal role");
        AssertChassisSilences(new OwlObjectPropertyRangeAxiom(Property("r"), Class("Pa")) { Origin = Origin("range") }, "a range on the modal role");
    }

    /// <summary>
    /// The habitat label SURVIVES the probe's added complement. The conformance
    /// arm's refutation builder adds ONE complement class assertion to an
    /// EXISTING individual and changes no population, so the label read on
    /// premise plus probe must EQUAL the label read on the premise alone for
    /// every one of the nine probe modules — otherwise the dispatch block never
    /// runs and all nine probes gap the case. The negative leg pins the
    /// builder's own output shape: the emitted axiom is a class assertion whose
    /// class is the complement of the conclusion's class and whose individual is
    /// the SAME told term, never a fresh witness, so a change to that shape
    /// fails this row rather than passing through a hand-written approximation.
    /// </summary>
    [TestMethod]
    public void Mg21HabitatLabelSurvivesTheProbesAddedComplement()
    {
        ReasoningModule premise = CorpusShapedModule();
        EnumerationHabitatClass premiseLabel = ContextHabitatRecognizer.Classify(premise, mentionsNominals: false, mentionsCounting: true);

        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, premiseLabel, "Mg21 LabelStability: the premise module carries the Shape K label.");

        List<string> mismatches = [];
        for(int goal = 1; goal <= CorpusGoals; goal++)
        {
            ReasoningModule probe = CorpusProbeModule(goal);
            if(ContextHabitatRecognizer.Classify(probe, mentionsNominals: false, mentionsCounting: true) != premiseLabel)
            {
                mismatches.Add("Mg21 LabelStability: probe " + goal + " moved the habitat label off the premise's.");
            }

            if(ContextModalGadgetTreeDecider.RunClash(probe).Consistent is not false)
            {
                mismatches.Add("Mg21 LabelStability: probe " + goal + " was not decided by the clash face.");
            }
        }

        ReasoningModule wrapperProbe = ThingWrapperProbeModule();
        if(ContextHabitatRecognizer.Classify(wrapperProbe, mentionsNominals: false, mentionsCounting: true) != premiseLabel)
        {
            mismatches.Add("Mg21 LabelStability: the owl:Thing wrapper probe moved the habitat label.");
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));

        OwlClassAssertionAxiom emitted = ReplicatedNamedIndividualRefutation(ClassAssertion(Class("Goal1"), Individual("v")));

        Assert.IsInstanceOfType<OwlObjectComplementOf>(emitted.Class, "Mg21 LabelStability: the builder's named-individual arm emits a COMPLEMENT of the conclusion's class.");
        Assert.AreEqual(Class("Goal1"), ((OwlObjectComplementOf)emitted.Class).Operand, "Mg21 LabelStability: the complement's operand is the conclusion's own class expression.");
        Assert.AreEqual(Individual("v"), emitted.Individual, "Mg21 LabelStability: the complement lands on the told individual ITSELF, so the probe module carries the SAME population as the premise and no clause of the predicate may be an individual-count test.");
    }

    /// <summary>
    /// Composition uses an axiom WHOLE or not at all. The mixed-conjunct fixture
    /// is the family's sharpest wrong-verdict route: a partial-conjunct backward
    /// composition would derive the goal from the single named conjunct, pair it
    /// with the told complement and report the conclusion ENTAILED on a green
    /// run — and it is not entailed, since a model with the bounded property
    /// empty puts the individual outside the defined class. The argument-order
    /// legs pin that the name side is chosen by CONSTRUCT: the intersection
    /// written first composes identically, two class-IRI operands engage no
    /// composition at all, and an equivalence with no class-IRI operand drops
    /// whole. The axiom-kind leg pins that the subsumption spelling is not in the
    /// composition rule's axiom set, on the vector a rule engine reused from the
    /// sibling family arrives pre-loaded with.
    /// </summary>
    [TestMethod]
    public void Mg22CompositionUsesAnAxiomWholeOrNotAtAll()
    {
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(MixedConjunctModule(probe: false)).Consistent, "Mg22 WholeAxiom: a mixed-operand equivalence is unusable in WHOLE and never in part, so the goal is not derived.");

        ReasoningModule probeModule = MixedConjunctModule(probe: true);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(probeModule, ModalGadgetFaces, ProbeBudget, TestContext.CancellationToken);

        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(probeModule).Consistent, "Mg22 WholeAxiom: the probe module for that goal is NOT refuted — the WRONG ENTAILED does not go green.");
        Assert.AreEqual(0, decision.Statistics.ContextTotals.ModalGadgetDeciderClashes, "Mg22 WholeAxiom: no clash decision is taken on the probe module.");

        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(ArgumentOrderModule(intersectionFirst: false)).Consistent, "Mg22 WholeAxiom: the equivalence written name-first composes and the clash is reached.");
        Assert.AreEqual(
            ContextModalGadgetTreeDecider.RunClash(ArgumentOrderModule(intersectionFirst: false)).Reason,
            ContextModalGadgetTreeDecider.RunClash(ArgumentOrderModule(intersectionFirst: true)).Reason,
            "Mg22 WholeAxiom: the same equivalence written intersection-FIRST composes identically — the name side is chosen by construct and never by argument position.");

        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(BothNamedEquivalenceModule()).Consistent, "Mg22 WholeAxiom: two class-IRI operands leave no conjunct to drop and no intersection to compose, so the rule does not engage.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(NoNamedOperandModule()).Consistent, "Mg22 WholeAxiom: an equivalence with NO class-IRI operand drops whole.");

        ReasoningModule subsumption = AxiomKindModule(equivalence: false, probe: false);

        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(subsumption).Consistent, "Mg22 WholeAxiom: a subsumption is not in the composition rule's axiom set, so nothing is derived through it.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(AxiomKindModule(equivalence: false, probe: true)).Consistent, "Mg22 WholeAxiom: its probe module is not refuted either.");
        Assert.AreEqual(0, ContextSaturationModuleReasoner.DecideModule(AxiomKindModule(equivalence: false, probe: true), ModalGadgetFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals.ModalGadgetDeciderClashes, "Mg22 WholeAxiom: and no clash decision is taken on it.");
        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(AxiomKindModule(equivalence: true, probe: true)).Consistent, "Mg22 WholeAxiom: the DISCRIMINATION control on the equivalence spelling DOES decide, so the axiom-kind restriction is implemented and not merely stated.");
    }

    /// <summary>
    /// The verification pass reads the RAW STRUCTURE and never the
    /// construction's own bit table. The fixture's table and its structure
    /// disagree on exactly ONE existential atom: the told type pins the atom
    /// TRUE, the spawn mints a successor for its non-propositional filler, and
    /// no free vector of that successor can put it inside an atomic filler the
    /// module never defines — so in the REAL structure the existential is FALSE
    /// where the table says TRUE, and the pass declines. A table-driven verifier
    /// would certify, and the discrimination control proves it: the same shape
    /// with a propositional filler the successor DOES satisfy certifies. The
    /// two-vector leg pins the arena RESET: a module whose first vectors fail
    /// verification and whose later one certifies carries the LATER vector's
    /// node count alone, never the sum of the vectors tried.
    /// </summary>
    [TestMethod]
    public void Mg23VerificationReadsTheRawStructureNotTheBitTable()
    {
        ReasoningModule opaque = RawStructureModule(propositionalFiller: false);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(PaddedModule(RawStructureAxioms(propositionalFiller: false)), ModalGadgetFaces, ProbeBudget, TestContext.CancellationToken);

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(opaque).Consistent, "Mg23 RawStructure: the constructed successor does not satisfy the filler, so the existential is FALSE in the structure where the table says TRUE and the pass declines.");
        Assert.AreEqual(0, decision.Statistics.ContextTotals.ModalGadgetDeciderCertifications, "Mg23 RawStructure: no certificate is issued on the module a table-driven verifier would have certified.");
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(RawStructureModule(propositionalFiller: true)).Consistent, "Mg23 RawStructure: the same shape whose successor DOES satisfy the filler certifies, so the silence is the raw-structure reading and not a blanket refusal.");

        ModalGadgetCertifyOutcome twoVector = ContextModalGadgetTreeDecider.RunCertify(TwoVectorModule());

        Assert.IsTrue(twoVector.Consistent, "Mg23 RawStructure: the module certifies on a later vector.");
        Assert.AreEqual(3, twoVector.Window.NodesBuilt, "Mg23 RawStructure: the arena carries the CERTIFYING vector's structure alone — one told node and two spawned successors — never the sum of the vectors tried, so it is CLEARED at the head of every vector.");
    }

    /// <summary>
    /// An equivalence verifies as SET EQUALITY in BOTH inclusions at every
    /// element. Both legs drive the construction seam so the construction
    /// proposes a structure that satisfies ONE inclusion and not the other, and
    /// pin that the pass DECLINES it: with defined-atom elimination suppressed
    /// the gadget bit is enumerated independently of the class its bound
    /// defines, and with the vector ceiling driven to a single candidate the
    /// decline is visible as a charged silence. The REVERSE leg is the one a
    /// one-way verifier would accept — a zero-bound class FALSE at an element
    /// whose property extension is empty — and it bites hardest on the
    /// zero-bound classes, each a maximum-of-zero equivalence whose reverse
    /// inclusion reads "the property is empty here, therefore the element is in
    /// the class". The role-index leg pins that a gadget self-loop can never
    /// satisfy a modal existential.
    /// </summary>
    [TestMethod]
    public void Mg24EquivalencesVerifyAsSetEqualityInBothInclusions()
    {
        ModalGadgetConstructionOptions singleCandidate = new(ModalGadgetEntry.Decide, new ModalGadgetBounds(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0), new ModalGadgetConstruction(SuppressDefinedAtomElimination: true, SuppressMinimalModalFirst: false));
        ModalGadgetConstructionOptions production = new(ModalGadgetEntry.Decide, new ModalGadgetBounds(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0), default);

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(InclusionModule(zeroSide: true), singleCandidate).Consistent, "Mg24 BothInclusions: the candidate leaves the property extension empty while the zero-bound class is FALSE at that element, so the REVERSE inclusion fails and the pass declines it.");
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(InclusionModule(zeroSide: true), production).Consistent, "Mg24 BothInclusions: the production construction proposes a structure whose two inclusions agree and the SAME single candidate certifies — a one-way verifier could not tell the two apart.");

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(InclusionModule(zeroSide: false), singleCandidate).Consistent, "Mg24 BothInclusions: the FORWARD inclusion is checked on the same evidence — a minimum-bound class TRUE at an element with an empty extension is declined.");
        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(InclusionModule(zeroSide: false), production).Consistent, "Mg24 BothInclusions: and the production candidate for the same module certifies, so the pass is not a no-op on either side.");

        ModalGadgetCertifyOutcome roleIndexed = ContextModalGadgetTreeDecider.RunCertify(RoleIndexModule());

        Assert.IsTrue(roleIndexed.Consistent, "Mg24 BothInclusions: the module certifies with the modal atom FALSE.");
        Assert.AreEqual(1, roleIndexed.Window.NodesBuilt, "Mg24 BothInclusions: the told node's own object-gadget SELF-LOOP does not satisfy the modal existential over the same node, so no successor was needed — the edge relation is role-indexed and not flat.");
    }

    /// <summary>
    /// The composition closure derives the goal cone and nothing outside it. All
    /// eight goals derive — five one step from the told facts, two two steps and
    /// one three — each pinned by its own probe module reaching the
    /// complemented-membership clash, and a class outside the cone is NOT
    /// derived, so the closure is a least fixpoint rather than a saturation of
    /// everything named. The depth leg removes ONE intermediate composition and
    /// the deepest goal stops deriving while the shallower ones keep deriving,
    /// which is what makes the three-step chain a chain.
    /// </summary>
    [TestMethod]
    public void Mg25CompositionClosureDerivesTheGoalCone()
    {
        List<string> mismatches = [];
        for(int goal = 1; goal <= CorpusGoals; goal++)
        {
            if(ContextModalGadgetTreeDecider.RunClash(CorpusProbeModule(goal)).Consistent is not false)
            {
                mismatches.Add("Mg25 GoalCone: goal " + goal + " is not derived by the composition closure.");
            }
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));

        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(BaitProbeModule()).Consistent, "Mg25 GoalCone: a class outside the goal cone is NOT derived — the closure is a least fixpoint over the told seeds, never a saturation of everything the module names.");

        ReasoningModule brokenChain = BrokenChainProbeModule();

        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(brokenChain).Consistent, "Mg25 GoalCone: removing ONE intermediate composition stops the deepest goal deriving, so the three-step chain is a chain and not a coincidence.");
        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(CorpusProbeModule(CorpusGoals)).Consistent, "Mg25 GoalCone: with that composition present the same goal derives.");
    }

    /// <summary>
    /// Defined-atom elimination shrinks the free vector and carries NO soundness
    /// weight. On the measured instance the elimination leaves five free bits
    /// and the module certifies; with elimination suppressed through the
    /// construction seam the SAME module records its THIRTY-NINE raw gadget
    /// atoms and is SILENT under production bounds, because the raw count
    /// exceeds the free-atom ceiling — the phase is a construction heuristic and
    /// the windows are charged against what it produces. The small pair carries
    /// the verdict claim at a size a widened walk can finish: with elimination
    /// on and off the module certifies IDENTICALLY, recording the eliminated
    /// count and the raw count respectively, and narrowing the free-atom ceiling
    /// below the raw count silences the suppressed leg alone.
    /// </summary>
    [TestMethod]
    public void Mg26DefinedAtomEliminationShrinksTheFreeVector()
    {
        ModalGadgetConstructionOptions suppressed = new(ModalGadgetEntry.Decide, default, new ModalGadgetConstruction(SuppressDefinedAtomElimination: true, SuppressMinimalModalFirst: false));
        ModalGadgetCertifyOutcome eliminated = ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule());
        ModalGadgetCertifyOutcome raw = ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule(), suppressed);

        Assert.IsTrue(eliminated.Consistent, "Mg26 Elimination: with the elimination on, the measured instance certifies.");
        Assert.AreEqual(CorpusFreeAtoms, eliminated.Window.FreeAtomCount, "Mg26 Elimination: and records five free bits.");
        Assert.IsNull(raw.Consistent, "Mg26 Elimination: with the elimination suppressed the same module is SILENT under production bounds — thirty-nine raw atoms exceed the free-atom ceiling.");
        Assert.AreEqual(CorpusRawGadgetAtoms, raw.Window.FreeAtomCount, "Mg26 Elimination: the suppressed leg records the RAW gadget-atom count, which is the quantity the ceiling was NOT charged against on admission.");
        Assert.AreEqual(1, raw.Window.WindowSilences, "Mg26 Elimination: the silence is a charged window trip.");

        ModalGadgetCertifyOutcome smallEliminated = ContextModalGadgetTreeDecider.RunCertify(EliminationPairModule());
        ModalGadgetCertifyOutcome smallRaw = ContextModalGadgetTreeDecider.RunCertify(EliminationPairModule(), suppressed);

        Assert.IsTrue(smallEliminated.Consistent, "Mg26 Elimination: the small pair certifies with the elimination on.");
        Assert.IsTrue(smallRaw.Consistent, "Mg26 Elimination: and certifies with it suppressed — the SAME verdict, so the elimination changes no verdict and carries no soundness weight.");
        Assert.IsGreaterThan(smallEliminated.Window.FreeAtomCount, smallRaw.Window.FreeAtomCount, "Mg26 Elimination: the suppressed leg enumerates the raw atoms the eliminated one computed.");

        ModalGadgetConstructionOptions narrowed = new(
            ModalGadgetEntry.Decide,
            new ModalGadgetBounds(smallEliminated.Window.FreeAtomCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            new ModalGadgetConstruction(SuppressDefinedAtomElimination: true, SuppressMinimalModalFirst: false));

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(EliminationPairModule(), narrowed).Consistent, "Mg26 Elimination: with the free-atom ceiling driven to the eliminated count the suppressed leg silences, which is the ceiling being charged against the surviving atoms and never against a raw count.");
    }

    /// <summary>
    /// The spawn dedupes by COMPUTED filler signature and the measurement is
    /// STATIC. The measured instance's thirteen existential occurrences —
    /// sixteen raw modal atoms once its three told universals are counted, both
    /// quantifier kinds — collapse to seven successor demands; two fillers that
    /// are syntactically distinct but propositionally identical dedupe to ONE
    /// demand, so the count is a property of the module and not of its
    /// serialisation; and a non-propositional filler gets its OWN demand,
    /// because no successor free vector can be solved for it and the
    /// verification pass decides. The static leg reads the SAME count on a
    /// module whose all-false vector succeeds and spawns nothing and on the same
    /// module forced to spawn, which is what the exponent ceilings and the
    /// admission split both need. The dedupe carries no soundness weight: the
    /// pass re-verifies whatever it produces.
    /// </summary>
    [TestMethod]
    public void Mg27SpawnDedupesByComputedFillerSignature()
    {
        Assert.AreEqual(CorpusSignatures, ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule()).Window.SignatureCount, "Mg27 SignatureDedupe: thirteen existential occurrences collapse to seven successor demands.");
        Assert.AreEqual(1, ContextModalGadgetTreeDecider.Measure(SignatureShapeModule(distinctSpellings: true)).SignatureCount, "Mg27 SignatureDedupe: two syntactically distinct fillers with identical propositional content are ONE demand.");
        Assert.AreEqual(2, ContextModalGadgetTreeDecider.Measure(SignatureShapeModule(distinctSpellings: false)).SignatureCount, "Mg27 SignatureDedupe: a non-propositional filler beside a propositional one gets its OWN demand.");

        ModalGadgetWindow quiet = ContextModalGadgetTreeDecider.RunCertify(StaticSignatureModule(forcedToSpawn: false)).Window;
        ModalGadgetWindow spawning = ContextModalGadgetTreeDecider.RunCertify(StaticSignatureModule(forcedToSpawn: true)).Window;

        Assert.AreEqual(quiet.SignatureCount, spawning.SignatureCount, "Mg27 SignatureDedupe: the count is IDENTICAL on a module that spawns nothing and on the same module forced to spawn — it is a static admission-time measurement over the module's existential occurrences, never a construction-time count that would read zero on every all-false success.");
        Assert.AreEqual(1, quiet.NodesBuilt, "Mg27 SignatureDedupe: the quiet module's all-false vector succeeds and spawns nothing.");
        Assert.IsGreaterThan(1, spawning.NodesBuilt, "Mg27 SignatureDedupe: the forced module does spawn, so the two legs really differ in what the construction did.");
    }

    /// <summary>
    /// A bound costs COMPLETENESS only and never correctness: the module's
    /// certificate sits one vector past a narrowed vector ceiling supplied
    /// through the bounds seam, where zero means production per member, so the
    /// face is SILENT with the counter charged; the SAME module under the
    /// production ceiling certifies.
    /// </summary>
    [TestMethod]
    public void Mg28NearMissBoundSilencesWhatAWiderWalkWouldDecide()
    {
        ReasoningModule module = TwoVectorModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, new ModalGadgetConstructionOptions(ModalGadgetEntry.Decide, new ModalGadgetBounds(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0), default));

        Assert.IsNull(narrow.Consistent, "Mg28 NearMissBound: the certificate sits past the narrowed vector ceiling, so the face abstains.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg28 NearMissBound: the abstention is a charged window silence, never a verdict over an unfinished structure.");

        ModalGadgetCertifyOutcome wide = ContextModalGadgetTreeDecider.RunCertify(module);

        Assert.IsTrue(wide.Consistent, "Mg28 NearMissBound: the SAME module under the production ceiling reaches the certificate the narrow walk could not.");
        Assert.AreEqual(0, wide.Window.WindowSilences, "Mg28 NearMissBound: the wider walk trips nothing.");
    }

    /// <summary>
    /// The dark control: with both face bits clear the module keeps the
    /// engine-budget abstention byte for byte — the abstained outcome, no
    /// verdict, the ceiling spent — while the census still ships, the habitat
    /// label and the static window riding the abstention record with both
    /// decision counters at zero. The measurement path compares window ceilings
    /// and neither composes nor constructs anything, so it forms no verdict on
    /// any input. The one-face legs light each face alone and each decides only
    /// its OWN direction.
    /// </summary>
    [TestMethod]
    public void Mg29DarkFacesDecideNothingAndTheCensusRides()
    {
        ReasoningModule module = CorpusShapedModule();
        ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, DarkBudget, TestContext.CancellationToken);
        ModuleDecision litElsewhere = ContextSaturationModuleReasoner.DecideModule(module, AllFacesButModalGadget, DarkBudget, TestContext.CancellationToken);
        ContextSaturationStatistics darkTotals = dark.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, dark.Outcome, "Mg29 DarkFaces: the module abstains on the budget with every face dark.");
        Assert.IsNull(dark.Verdict, "Mg29 DarkFaces: the dark abstention carries no verdict.");
        Assert.IsGreaterThan(0L, darkTotals.InferenceAttempts, "Mg29 DarkFaces: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, darkTotals.EnumerationHabitat, "Mg29 DarkFaces: the habitat label rides the dark abstention record.");
        Assert.AreEqual(CorpusSignatures, darkTotals.ModalGadgetSignatureCount, "Mg29 DarkFaces: and so does the static window measurement, which the measurement path takes whether the faces are lit or dark.");
        Assert.AreEqual(0, darkTotals.ModalGadgetDeciderClashes, "Mg29 DarkFaces: no clash decision with the faces dark.");
        Assert.AreEqual(0, darkTotals.ModalGadgetDeciderCertifications, "Mg29 DarkFaces: no certificate either.");
        Assert.AreEqual(0, darkTotals.ModalGadgetWindowSilences, "Mg29 DarkFaces: the measurement charges no silence on a module inside its ceilings.");

        ContextSaturationStatistics litTotals = litElsewhere.Statistics.ContextTotals;

        Assert.AreEqual(dark.Outcome, litElsewhere.Outcome, "Mg29 DarkFaces: lighting every OTHER face leaves the abstention record identical.");
        Assert.AreEqual(darkTotals.InferenceAttempts, litTotals.InferenceAttempts, "Mg29 DarkFaces: and spends the same attempts.");
        Assert.AreEqual(darkTotals.EnumerationHabitat, litTotals.EnumerationHabitat, "Mg29 DarkFaces: and carries the same census label.");
        Assert.AreEqual(0, litTotals.ModalGadgetDeciderCertifications, "Mg29 DarkFaces: no sibling face claims the module either.");

        ContextSaturationStatistics certifyOnly = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ModalGadgetCertify, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics clashOnlyOnPremise = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ModalGadgetClash, DarkBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, certifyOnly.ModalGadgetDeciderCertifications, "Mg29 DarkFaces: with the certify face alone lit the premise still certifies.");
        Assert.AreEqual(0, clashOnlyOnPremise.ModalGadgetDeciderClashes, "Mg29 DarkFaces: with the clash face alone lit the premise takes no decision — each face decides only its own direction.");

        ContextSaturationStatistics clashOnlyOnProbe = ContextSaturationModuleReasoner.DecideModule(CorpusProbeModule(1), EnumerationDeciderFaces.ModalGadgetClash, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics certifyOnlyOnProbe = ContextSaturationModuleReasoner.DecideModule(CorpusProbeModule(1), EnumerationDeciderFaces.ModalGadgetCertify, DarkBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, clashOnlyOnProbe.ModalGadgetDeciderClashes, "Mg29 DarkFaces: with the clash face alone lit the probe module is refuted.");
        Assert.AreEqual(0, certifyOnlyOnProbe.ModalGadgetDeciderCertifications, "Mg29 DarkFaces: with the certify face alone lit the probe module takes no decision.");

        Assert.AreEqual(0, ContextModalGadgetTreeDecider.Measure(module).WindowSilences, "Mg29 DarkFaces: the measurement surface charges nothing on a module inside its ceilings.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(module, new ModalGadgetConstructionOptions(ModalGadgetEntry.MeasureOnly, default, default)).Consistent, "Mg29 DarkFaces: the measurement entry forms no verdict on any input.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(module, new ModalGadgetConstructionOptions(ModalGadgetEntry.MeasureOnly, default, default)).Consistent, "Mg29 DarkFaces: on either face.");
    }

    /// <summary>
    /// The probe answers FIRST on both recognizer paths and steals only what its
    /// own clauses claim. Every sibling shape still reads its OWN label; the
    /// branching modal-gadget shape reads Shape K on BOTH paths; a module whose
    /// gadget layer is carried ENTIRELY by data cardinality restrictions still
    /// reaches the probe, which is what placing it AHEAD of the counting gate is
    /// for, since the census's counting mention covers object number
    /// restrictions and the functional characteristics only; a module a sibling
    /// gadget face legitimately DECIDES whose modal restriction sits inside its
    /// prelude keeps its own label, because the composition threshold and not
    /// the modal-restriction clause is what separates the two there; and a
    /// module with no modal restriction at all is NOT taken, which is what the
    /// ordering-safety clause actually delivers. No sibling battery row moves
    /// its label, takes a modal-gadget decision, or loses its verdict.
    /// </summary>
    [TestMethod]
    public void Mg30HabitatOrderingStealsOnlyTheEnumeratedSet()
    {
        ReasoningModule target = CorpusShapedModule();

        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, ContextHabitatRecognizer.Classify(target, mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the target shape is reached on the nominal-free path.");
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, ContextHabitatRecognizer.Classify(target, mentionsNominals: true, mentionsCounting: true), "Mg30 Ordering: and on the nominal path, where it opens the fallback chain.");
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, ContextHabitatRecognizer.Classify(DataOnlyGadgetLayerModule(), mentionsNominals: false, mentionsCounting: false), "Mg30 Ordering: a gadget layer carried ENTIRELY by data cardinality restrictions sets no counting mention and STILL reaches the probe — the placement ahead of the counting gate is what the wild shape needs.");

        Assert.AreEqual(EnumerationHabitatClass.BooleanCardinalityGadget, ContextHabitatRecognizer.Classify(GadgetShapedModule(preludeExistential: false), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the gadget module keeps Shape G.");
        Assert.AreEqual(EnumerationHabitatClass.BooleanCardinalityGadget, ContextHabitatRecognizer.Classify(GadgetShapedModule(preludeExistential: true), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: a Shape-G-DECIDABLE module carrying a PRELUDE existential keeps Shape G too — its composition layer sits below the threshold, so the habitat-identity clause and not the modal-restriction clause is what leaves it alone.");
        Assert.AreEqual(EnumerationHabitatClass.PartitionCounting, ContextHabitatRecognizer.Classify(PartitionShapedModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the partition module keeps Shape P.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, ContextHabitatRecognizer.Classify(BijectionShapedModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the role-linked module keeps Shape B.");
        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, ContextHabitatRecognizer.Classify(ToldGroundShapedModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the told-ground module keeps Shape W.");
        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, ContextHabitatRecognizer.Classify(RestrictionRichShapedModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the restriction-rich module keeps Shape R.");
        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, ContextHabitatRecognizer.Classify(ModalExpansionShapedModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: the skolem-expansion module keeps Shape M, which is the tail behind every probe on that path.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, ContextHabitatRecognizer.Classify(SpyPointShapedModule(), mentionsNominals: true, mentionsCounting: true), "Mg30 Ordering: the spy-point module keeps Shape S on the nominal path, where the new probe opens the fallback chain ahead of it.");

        Assert.AreNotEqual(EnumerationHabitatClass.ModalGadgetTree, ContextHabitatRecognizer.Classify(NoModalRestrictionModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: a composition layer above the threshold with NO modal restriction anywhere is not taken — that is what the ordering-safety clause delivers.");
        Assert.AreNotEqual(EnumerationHabitatClass.ModalGadgetTree, ContextHabitatRecognizer.Classify(BelowThresholdModule(), mentionsNominals: false, mentionsCounting: true), "Mg30 Ordering: a branching module whose composition layer sits below the threshold is face-decidable and probe-unreachable, which is a recorded REACH cost and never a wrong verdict.");

        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            AppendSiblingRowMismatch(name, module, expectedConsistent: null, mismatchesToAppendTo: mismatches, token: TestContext.CancellationToken);
        }

        foreach((string name, ReasoningModule module, bool consistent) in ContextPartitionDeciderTests.PartitionRows())
        {
            AppendSiblingRowMismatch(name, module, expectedConsistent: consistent, mismatchesToAppendTo: mismatches, token: TestContext.CancellationToken);
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The free-atom ceiling records the measured instance's five surviving
    /// atoms and silences a module whose surviving residue passes it, the
    /// overflow derived from the shared constant. The ADMISSION leg is the one
    /// the loose reading would have failed: the measured instance carries
    /// THIRTY-NINE raw gadget atoms against a ceiling of sixteen and is
    /// ADMITTED, because the ceiling is charged against the atoms that SURVIVE
    /// defined-atom elimination and never against a raw count on admission.
    /// </summary>
    [TestMethod]
    public void Mg31FreeAtomBoundRecordsAndSilences()
    {
        ModalGadgetCertifyOutcome measured = ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule());

        Assert.AreEqual(CorpusFreeAtoms, measured.Window.FreeAtomCount, "Mg31 FreeAtomBound: the measured instance's surviving free atoms ride the deciding window.");
        Assert.IsTrue(measured.Consistent, "Mg31 FreeAtomBound: and the module carrying thirty-nine RAW gadget atoms against a ceiling of sixteen is ADMITTED, the ceiling being charged against the surviving residue alone.");
        Assert.IsGreaterThan(ContextModalGadgetTreeDecider.ModalGadgetFreeAtomBound, CorpusRawGadgetAtoms, "Mg31 FreeAtomBound: the raw count really does exceed the ceiling, so the admission leg measures something.");

        ModalGadgetCertifyOutcome overflow = ContextModalGadgetTreeDecider.RunCertify(FreeAtomOverflowModule());

        Assert.IsNull(overflow.Consistent, "Mg31 FreeAtomBound: a module whose SURVIVING residue passes the ceiling is silent — a bound trip is never a verdict.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Mg31 FreeAtomBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalGadgetTreeDecider.ModalGadgetFreeAtomBound + 1, overflow.Window.FreeAtomCount, "Mg31 FreeAtomBound: the surviving free atoms stand exactly one past the ceiling.");
        AssertOtherFieldsBelowTheirCeilings(overflow.Window, "Mg31 FreeAtomBound", freeAtom: false, signature: true, node: true);
    }

    /// <summary>
    /// The signature ceiling records the measured instance's seven deduped
    /// successor demands and silences a module whose demands pass it, with the
    /// other two measured quantities strictly below their ceilings.
    /// </summary>
    [TestMethod]
    public void Mg32SignatureBoundRecordsAndSilences()
    {
        Assert.AreEqual(CorpusSignatures, ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule()).Window.SignatureCount, "Mg32 SignatureBound: the measured instance's seven demands ride the deciding window.");

        ModalGadgetCertifyOutcome overflow = ContextModalGadgetTreeDecider.RunCertify(SignatureOverflowModule());

        Assert.IsNull(overflow.Consistent, "Mg32 SignatureBound: a module past the signature ceiling is silent.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Mg32 SignatureBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalGadgetTreeDecider.ModalGadgetSignatureBound + 1, overflow.Window.SignatureCount, "Mg32 SignatureBound: the deduped demands stand exactly one past the ceiling.");
        AssertOtherFieldsBelowTheirCeilings(overflow.Window, "Mg32 SignatureBound", freeAtom: true, signature: false, node: true);
    }

    /// <summary>
    /// The node ceiling bounds the ARENA — told individuals and spawned
    /// successors together — rather than the spawn count. The measured instance
    /// records its single told node; a spawning module past the arena is silent;
    /// and the TOLD-ARENA leg, whose told individuals alone pass the ceiling
    /// with nothing spawned anywhere, is silent with the same counter charged
    /// and never a second allocation.
    /// </summary>
    [TestMethod]
    public void Mg33NodeBoundRecordsAndSilences()
    {
        Assert.AreEqual(1, ContextModalGadgetTreeDecider.RunCertify(CorpusShapedModule()).Window.NodesBuilt, "Mg33 NodeBound: the measured instance's arena is its told individual alone.");

        ModalGadgetCertifyOutcome spawned = ContextModalGadgetTreeDecider.RunCertify(NodeOverflowModule());

        Assert.IsNull(spawned.Consistent, "Mg33 NodeBound: a spawning module past the arena is silent.");
        Assert.AreEqual(1, spawned.Window.WindowSilences, "Mg33 NodeBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalGadgetTreeDecider.ModalGadgetNodeBound, spawned.Window.NodesBuilt, "Mg33 NodeBound: the arena stands exactly AT its ceiling when the next successor is refused.");

        ModalGadgetCertifyOutcome toldArena = ContextModalGadgetTreeDecider.RunCertify(ToldArenaOverflowModule());

        Assert.IsNull(toldArena.Consistent, "Mg33 NodeBound: a told population past the arena is silent too.");
        Assert.AreEqual(1, toldArena.Window.WindowSilences, "Mg33 NodeBound: told individuals are nodes of the constructed structure, so the arena bound covers them and the told-only overflow charges the same counter.");
        Assert.AreEqual(0, toldArena.Window.NodesBuilt, "Mg33 NodeBound: nothing was built — the bound is the arena's and not the spawn count's.");
        AssertOtherFieldsBelowTheirCeilings(toldArena.Window, "Mg33 NodeBound", freeAtom: true, signature: true, node: false);
    }

    /// <summary>
    /// The raw-modal-atom ceiling silences a module carrying more raw modal
    /// atoms at one node than it admits, with the three fielded quantities
    /// strictly below their ceilings and the paired widening proving the bound
    /// is CHECKED rather than merely declared. The VALUE-PIN legs pin the
    /// measured instance's raw count at EXACTLY sixteen without a statistics
    /// field: driven to sixteen through the bounds seam the premise still
    /// certifies, and driven to fifteen it silences — both quantifier kinds
    /// counted, which is what raising the production ceiling to twice that was
    /// for.
    /// </summary>
    [TestMethod]
    public void Mg34ModalAtomBoundSilences()
    {
        ModalGadgetCertifyOutcome overflow = ContextModalGadgetTreeDecider.RunCertify(ModalAtomOverflowModule());

        Assert.IsNull(overflow.Consistent, "Mg34 ModalAtomBound: a node carrying more raw modal atoms than the ceiling admits is silent.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Mg34 ModalAtomBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(overflow.Window, "Mg34 ModalAtomBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(ModalAtomOverflowModule(), Widened(modalAtom: CorpusRawModalAtoms * 4)).Consistent, "Mg34 ModalAtomBound: the SAME module under a widened ceiling DECIDES, so the narrow silence was charged by the very check the row claims tripped — a declared, documented, never-checked bound cannot pass this pair.");

        ReasoningModule premise = CorpusShapedModule();

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(premise, Widened(modalAtom: CorpusRawModalAtoms)).Consistent, "Mg34 ModalAtomBound: the measured instance still certifies with the ceiling driven down to sixteen.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(premise, Widened(modalAtom: CorpusRawModalAtoms - 1)).Consistent, "Mg34 ModalAtomBound: and silences at fifteen, which pins its raw modal-atom count at EXACTLY sixteen — thirteen existential occurrences beside three told universals — without any statistics field.");
    }

    /// <summary>
    /// The vector ceiling silences a module whose certificate sits past the
    /// vectors it admits, with the three fielded quantities strictly below their
    /// ceilings and the paired widening proving the bound is checked.
    /// </summary>
    [TestMethod]
    public void Mg35VectorBoundSilences()
    {
        ReasoningModule module = TwoVectorModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, Widened(vector: 1));

        Assert.IsNull(narrow.Consistent, "Mg35 VectorBound: a module whose certificate sits past the vector ceiling is silent.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg35 VectorBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(narrow.Window, "Mg35 VectorBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg35 VectorBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the vector check itself.");
    }

    /// <summary>
    /// The spawn-depth ceiling silences a module demanding a level below it. The
    /// shipped construction mints one level below the told frontier, so the
    /// production ceiling of eight cannot be reached from above and the narrow
    /// leg drives the ceiling BELOW that single level through the same
    /// zero-is-production seam — the mirror of the paired widening, whose
    /// widened half is the production ceiling itself.
    /// </summary>
    [TestMethod]
    public void Mg36DepthBoundSilences()
    {
        ReasoningModule module = SpawningModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, Widened(depth: -ContextModalGadgetTreeDecider.ModalGadgetDepthBound));

        Assert.IsNull(narrow.Consistent, "Mg36 DepthBound: a module demanding a spawn level the ceiling refuses is silent.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg36 DepthBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(narrow.Window, "Mg36 DepthBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg36 DepthBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the depth check itself.");
    }

    /// <summary>
    /// The per-node label ceiling silences a module carrying more named classes
    /// than one node's label row spans, with the three fielded quantities
    /// strictly below their ceilings and the paired widening proving the bound
    /// is checked.
    /// </summary>
    [TestMethod]
    public void Mg37LabelBoundSilences()
    {
        ReasoningModule module = AdmittedChassisModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, Widened(label: 1));

        Assert.IsNull(narrow.Consistent, "Mg37 LabelBound: a module whose named classes pass the label ceiling is silent.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg37 LabelBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(narrow.Window, "Mg37 LabelBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg37 LabelBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the label check itself.");
    }

    /// <summary>
    /// The directed-edge ceiling silences a module whose structure carries more
    /// edges than it admits — a gadget self-loop counting as an edge and a
    /// data-property literal counting as none, since a literal is no ordered
    /// pair of domain elements — with the three fielded quantities strictly
    /// below their ceilings and the paired widening proving the bound is
    /// checked.
    /// </summary>
    [TestMethod]
    public void Mg38EdgeBoundSilences()
    {
        ReasoningModule module = EdgeDenseModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, Widened(edge: 1));

        Assert.IsNull(narrow.Consistent, "Mg38 EdgeBound: a structure past the directed-edge ceiling is silent.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg38 EdgeBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(narrow.Window, "Mg38 EdgeBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg38 EdgeBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the edge check itself.");
    }

    /// <summary>
    /// The module-admission ceiling silences a module carrying more LOGICAL
    /// axioms than it admits, non-logical declarations and annotations being
    /// uncharged, with the three fielded quantities strictly below their
    /// ceilings and the paired widening proving the bound is checked.
    /// </summary>
    [TestMethod]
    public void Mg39AxiomBoundSilences()
    {
        ReasoningModule module = AdmittedChassisModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, Widened(axiom: 1));

        Assert.IsNull(narrow.Consistent, "Mg39 AxiomBound: a module past the admission ceiling is silent.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg39 AxiomBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(narrow.Window, "Mg39 AxiomBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg39 AxiomBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the axiom check itself.");
    }

    /// <summary>
    /// The whole-module verification ceiling silences a decision that would
    /// spend more complete passes than it admits — a partial pass abandoned on
    /// the first failure still counting as one — with the three fielded
    /// quantities strictly below their ceilings and the paired widening proving
    /// the bound is checked.
    /// </summary>
    [TestMethod]
    public void Mg40VerifyPassBoundSilences()
    {
        ReasoningModule module = TwoVectorModule();
        ModalGadgetCertifyOutcome narrow = ContextModalGadgetTreeDecider.RunCertify(module, Widened(verifyPass: 1));

        Assert.IsNull(narrow.Consistent, "Mg40 VerifyPassBound: a decision needing more whole-module passes than the ceiling admits is silent.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg40 VerifyPassBound: the silence is charged to the window counter.");
        AssertOtherFieldsBelowTheirCeilings(narrow.Window, "Mg40 VerifyPassBound", freeAtom: true, signature: true, node: true);

        Assert.IsTrue(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg40 VerifyPassBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the pass check itself.");
    }

    /// <summary>
    /// The rule-application ceiling is charged on the CLASH face alone and
    /// silences a closure that would fire past it. The window assembly is a
    /// MERGE and never last-writer-wins: the clash face contributes its own
    /// silence charge and NOTHING else, so the three certify-face fields stand
    /// at ZERO beside a non-zero counter — the discriminant that separates a
    /// face-A step trip from every certify-face silence.
    /// </summary>
    [TestMethod]
    public void Mg41StepBoundSilences()
    {
        ReasoningModule module = ArgumentOrderModule(intersectionFirst: false);
        ModalGadgetClashOutcome narrow = ContextModalGadgetTreeDecider.RunClash(module, Widened(step: 1));

        Assert.IsNull(narrow.Consistent, "Mg41 StepBound: a closure past the rule-application ceiling is silent — a bound trip is never a verdict.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Mg41 StepBound: the silence is charged to the window counter.");
        Assert.AreEqual(0, narrow.Window.FreeAtomCount, "Mg41 StepBound: the clash face charges NO free-atom measurement — a field a face does not fill is left alone rather than clobbered.");
        Assert.AreEqual(0, narrow.Window.SignatureCount, "Mg41 StepBound: nor a signature measurement.");
        Assert.AreEqual(0, narrow.Window.NodesBuilt, "Mg41 StepBound: nor a node count — the clash face creates no nodes at all.");

        Assert.IsFalse(ContextModalGadgetTreeDecider.RunClash(module).Consistent, "Mg41 StepBound: the SAME module under the production ceiling DECIDES, so the narrow silence was charged by the step check itself.");
    }

    /// <summary>Asserts that the fielded window quantities other than the one under test sit STRICTLY BELOW their production ceilings, which is the partial discriminant a family with three fields and eleven bounds can carry.</summary>
    /// <param name="window">The measured window of the silent run.</param>
    /// <param name="row">The row prefix the messages open with.</param>
    /// <param name="freeAtom">Whether the surviving free atoms are one of the quantities to check.</param>
    /// <param name="signature">Whether the deduped demands are one of the quantities to check.</param>
    /// <param name="node">Whether the arena is one of the quantities to check.</param>
    private static void AssertOtherFieldsBelowTheirCeilings(ModalGadgetWindow window, string row, bool freeAtom, bool signature, bool node)
    {
        if(freeAtom)
        {
            Assert.IsGreaterThan(window.FreeAtomCount, ContextModalGadgetTreeDecider.ModalGadgetFreeAtomBound, row + ": the surviving free atoms stayed strictly below their ceiling, so the silence was charged elsewhere. Eight of the eleven bounds carry no statistics field, so this discriminant is partial and the paired widening beside it is what proves the bound under test is live.");
        }

        if(signature)
        {
            Assert.IsGreaterThan(window.SignatureCount, ContextModalGadgetTreeDecider.ModalGadgetSignatureBound, row + ": the deduped successor demands stayed strictly below their ceiling.");
        }

        if(node)
        {
            Assert.IsGreaterThan(window.NodesBuilt, ContextModalGadgetTreeDecider.ModalGadgetNodeBound, row + ": the arena stayed strictly below its ceiling.");
        }
    }

    /// <summary>Builds a bounds seam value overriding ONE ceiling and leaving every other member at zero, which is production per member.</summary>
    /// <param name="freeAtom">The surviving-free-atom override.</param>
    /// <param name="signature">The deduped-demand override.</param>
    /// <param name="modalAtom">The raw-modal-atom override.</param>
    /// <param name="vector">The evaluated-vector override.</param>
    /// <param name="node">The arena override.</param>
    /// <param name="depth">The spawn-depth override.</param>
    /// <param name="label">The classes-per-node override.</param>
    /// <param name="edge">The directed-edge override.</param>
    /// <param name="axiom">The module-admission override.</param>
    /// <param name="verifyPass">The whole-module-verification override.</param>
    /// <param name="step">The rule-application override.</param>
    /// <returns>The options value.</returns>
    private static ModalGadgetConstructionOptions Widened(
        int freeAtom = 0,
        int signature = 0,
        int modalAtom = 0,
        int vector = 0,
        int node = 0,
        int depth = 0,
        int label = 0,
        int edge = 0,
        int axiom = 0,
        int verifyPass = 0,
        int step = 0)
    {
        return new ModalGadgetConstructionOptions(
            ModalGadgetEntry.Decide,
            new ModalGadgetBounds(freeAtom, signature, modalAtom, vector, node, depth, label, edge, axiom, verifyPass, step),
            default);
    }

    /// <summary>Asserts that one probe module of the measured instance's goal cone is decided inconsistent by the clash face, pre-engine, with the habitat label unchanged and the complemented-membership reason naming the goal class.</summary>
    /// <param name="goal">The goal's one-based index.</param>
    /// <param name="row">The row prefix the messages open with.</param>
    /// <param name="token">The cancellation token.</param>
    private static void AssertGoalProbeDecides(int goal, string row, CancellationToken token)
    {
        ReasoningModule module = CorpusProbeModule(goal);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ModalGadgetFaces, ReasoningConfiguration.Default.Budget, token);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, row + ": the clash face decides the probe module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, row + ": the probe module is inconsistent, so its conclusion follows from the premise.");
        Assert.AreEqual(0L, totals.InferenceAttempts, row + ": the probe decides with zero inference attempts.");
        Assert.AreEqual(EnumerationHabitatClass.ModalGadgetTree, totals.EnumerationHabitat, row + ": the habitat label is UNCHANGED from the premise module's.");
        Assert.AreEqual(1, totals.ModalGadgetDeciderClashes, row + ": the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.ModalGadgetDeciderCertifications, row + ": the certify face is structurally silent on every probe module the arm builds.");
        Assert.AreEqual(ModalGadgetClashReasons.ComplementedMembership(Utf8Strings.From(Example + "Goal" + goal)), ContextModalGadgetTreeDecider.RunClash(module).Reason, row + ": the clash reason names the goal class the closure derived and the probe denied.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, row + ": the certify face's own reading of the probe module is silence.");
    }

    /// <summary>Asserts that adding one axiom to the admitted chassis silences the certify face whole while the clash face stays indifferent.</summary>
    /// <param name="perturbation">The added axiom.</param>
    /// <param name="what">The perturbation's short name, folded into the assertion message.</param>
    private static void AssertChassisSilences(OwlAxiom perturbation, string what)
    {
        ReasoningModule module = Module([.. Append(AdmittedChassisAxioms(), perturbation)]);

        Assert.IsNull(ContextModalGadgetTreeDecider.RunCertify(module).Consistent, "Mg16 Admission: " + what + " is outside the allow-list, and anything outside it silences the module WHOLE.");
        Assert.IsNull(ContextModalGadgetTreeDecider.RunClash(module).Consistent, "Mg16 Admission: the clash face is indifferent to " + what + " — it ignores what it does not recognize and finds no clash template here either way.");
    }

    /// <summary>Appends one sibling-battery row's ordering mismatches: the census label may never move to Shape K, the modal-gadget faces may take no decision, and a row with a known verdict must keep it.</summary>
    /// <param name="name">The row's name.</param>
    /// <param name="module">The row's module.</param>
    /// <param name="expectedConsistent">The row's certified verdict, or <see langword="null"/> when the row carries none.</param>
    /// <param name="mismatchesToAppendTo">The mismatch list.</param>
    /// <param name="token">The cancellation token.</param>
    private static void AppendSiblingRowMismatch(string name, ReasoningModule module, bool? expectedConsistent, List<string> mismatchesToAppendTo, CancellationToken token)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, ProbeBudget, token);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        if(totals.EnumerationHabitat == EnumerationHabitatClass.ModalGadgetTree)
        {
            mismatchesToAppendTo.Add(name + ": a sibling-battery row's census label moved to Shape K.");

            return;
        }

        if(totals.ModalGadgetDeciderClashes > 0 || totals.ModalGadgetDeciderCertifications > 0)
        {
            mismatchesToAppendTo.Add(name + ": a sibling-battery row was claimed by a modal-gadget face.");

            return;
        }

        if(expectedConsistent is bool consistent && (decision.Outcome != ReasoningDecisionOutcome.Decided || decision.Verdict is null || decision.Verdict.IsConsistent != consistent))
        {
            mismatchesToAppendTo.Add(name + ": the sibling row lost its certified verdict under the modal-gadget-lit faces.");
        }
    }

    /// <summary>
    /// The habitat's measured-instance premise told through the builders:
    /// thirty-nine gadget properties of which five survive elimination, thirteen
    /// existential occurrences over ONE role deduping to seven demands, three
    /// told universals on the single told individual, a forty-nine-axiom
    /// composition layer carrying an eight-goal cone and its bait, the TRAILING
    /// <c>owl:Thing</c> typing the measured premise closes its ABox with — the
    /// carrier-only admission the built-in evaluation clause verifies against
    /// the whole domain — and no property assertion of any kind.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusShapedModule()
    {
        return Module([.. CorpusShapedAxioms()]);
    }

    /// <summary>The measured instance's axioms, so a probe row can append the conformance arm's own refutation axiom without restating the premise.</summary>
    /// <returns>The axioms.</returns>
    private static List<OwlAxiom> CorpusShapedAxioms()
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("F1"), MinData("f1", 1)),
            EquivalentClasses(Class("Z1"), MaxData("f1", 0)),
            EquivalentClasses(Class("F2"), MinData("f2", 1)),
            EquivalentClasses(Class("Z2"), MaxData("f2", 0)),
            EquivalentClasses(Class("F3"), MinObject("f3", 1)),
            EquivalentClasses(Class("Z3"), MaxObject("f3", 0)),
            EquivalentClasses(Class("F4"), MinObject("f4", 1)),
            EquivalentClasses(Class("Z4"), MaxObject("f4", 0)),
            EquivalentClasses(Class("F5"), MinObject("f5", 1)),
            EquivalentClasses(Class("Z5"), MaxObject("f5", 0)),
            EquivalentClasses(Class("C12"), Intersection(Class("F1"), Class("F2"))),
            EquivalentClasses(Class("C13"), Intersection(Class("F1"), Class("Z3"))),
            EquivalentClasses(Class("C45"), Intersection(Class("F4"), Class("F5"))),
        ];

        for(int index = 0; index < CorpusIntersectionDefined; index++)
        {
            axioms.Add(EquivalentClasses(Class("Di" + index), MinData("di" + index, 1)));
            axioms.Add(EquivalentClasses(Class("Yi" + index), MaxData("di" + index, 0)));
            axioms.Add(EquivalentClasses(Class("Di" + index), Intersection(Class("F1"), Class("F2"))));
        }

        string[] modalDefinedFillers = ["C45", "C45", "C13", "Op"];
        for(int index = 0; index < CorpusModalDefined; index++)
        {
            axioms.Add(EquivalentClasses(Class("M" + index), MinObject("dm" + index, 1)));
            axioms.Add(EquivalentClasses(Class("YM" + index), MaxObject("dm" + index, 0)));
            axioms.Add(EquivalentClasses(Class("M" + index), Some("r", Class(modalDefinedFillers[index]))));
        }

        string[] plainFillers = ["F1", "F1", "F2", "F2", "Z3", "Z3", "C12", "C12", "C13"];
        for(int index = 0; index < CorpusPlainExistentials; index++)
        {
            axioms.Add(EquivalentClasses(Class("E" + index), Some("r", Class(plainFillers[index]))));
        }

        axioms.Add(EquivalentClasses(Class("Goal1"), Intersection(Class("T1"), Class("T2"))));
        axioms.Add(EquivalentClasses(Class("Goal2"), Intersection(Class("T2"), Class("T3"))));
        axioms.Add(EquivalentClasses(Class("Goal3"), Intersection(Class("T3"), Class("T4"))));
        axioms.Add(EquivalentClasses(Class("Goal4"), Intersection(Class("T4"), Class("T5"))));
        axioms.Add(EquivalentClasses(Class("Goal5"), Intersection(Class("T5"), Class("T6"))));
        axioms.Add(EquivalentClasses(Class("Goal6"), Intersection(Class("Goal1"), Class("Goal2"))));
        axioms.Add(EquivalentClasses(Class("Goal7"), Intersection(Class("Goal3"), Class("Goal4"))));
        axioms.Add(EquivalentClasses(Class("Goal8"), Intersection(Class("Goal6"), Class("Goal7"))));

        for(int index = 0; index < CorpusBaitCompositions; index++)
        {
            axioms.Add(EquivalentClasses(Class("Bait" + index), Intersection(Class("Bx" + index), Class("By" + index))));
        }

        axioms.Add(ClassAssertion(Class("F1"), Individual("v")));
        axioms.Add(ClassAssertion(Class("F2"), Individual("v")));
        axioms.Add(ClassAssertion(Class("Z3"), Individual("v")));
        axioms.Add(ClassAssertion(Class("Z4"), Individual("v")));
        axioms.Add(ClassAssertion(Class("Z5"), Individual("v")));
        for(int index = 1; index <= CorpusToldAtoms; index++)
        {
            axioms.Add(ClassAssertion(Class("T" + index), Individual("v")));
        }

        axioms.Add(ClassAssertion(All("r", Class("F1")), Individual("v")));
        axioms.Add(ClassAssertion(All("r", Class("F2")), Individual("v")));
        axioms.Add(ClassAssertion(All("r", Class("Op")), Individual("v")));
        axioms.Add(ClassAssertion(Thing, Individual("v")));
        axioms.Add(Declare(OwlEntityKind.ObjectProperty, Example + "r"));

        return axioms;
    }

    /// <summary>The measured instance's premise beside the conformance arm's own refutation axiom for one goal conclusion.</summary>
    /// <param name="goal">The goal's one-based index.</param>
    /// <returns>The probe module.</returns>
    private static ReasoningModule CorpusProbeModule(int goal)
    {
        return Module([.. Append(CorpusShapedAxioms(), ReplicatedNamedIndividualRefutation(ClassAssertion(Class("Goal" + goal), Individual("v"))))]);
    }

    /// <summary>The measured instance's premise beside the NINTH refutation axiom, the one the conclusion's <c>owl:Thing</c> wrappers produce.</summary>
    /// <returns>The probe module.</returns>
    private static ReasoningModule ThingWrapperProbeModule()
    {
        return Module([.. Append(CorpusShapedAxioms(), ReplicatedNamedIndividualRefutation(ClassAssertion(Thing, Individual("v"))))]);
    }

    /// <summary>The measured instance's premise beside the refutation axiom for a BAIT class outside the goal cone.</summary>
    /// <returns>The probe module.</returns>
    private static ReasoningModule BaitProbeModule()
    {
        return Module([.. Append(CorpusShapedAxioms(), ReplicatedNamedIndividualRefutation(ClassAssertion(Class("Bait0"), Individual("v"))))]);
    }

    /// <summary>The deepest goal's probe module with ONE intermediate composition removed, so the three-step chain no longer reaches it.</summary>
    /// <returns>The probe module.</returns>
    private static ReasoningModule BrokenChainProbeModule()
    {
        List<OwlAxiom> axioms = [];
        foreach(OwlAxiom axiom in CorpusShapedAxioms())
        {
            if(axiom is OwlEquivalentClassesAxiom { First: OwlClassReference name } && name.Class.Iri.Equals(Utf8Strings.From(Example + "Goal6")))
            {
                continue;
            }

            axioms.Add(axiom);
        }

        axioms.Add(ReplicatedNamedIndividualRefutation(ClassAssertion(Class("Goal8"), Individual("v"))));

        return Module([.. axioms]);
    }

    /// <summary>
    /// The conformance arm's named-individual refutation arm, REPLICATED from
    /// the shared builder's own switch so a harness change cannot silently
    /// diverge from this battery: a conclusion class assertion on a NAMED
    /// individual becomes one class assertion of the complement of the
    /// conclusion's class on that SAME individual, never on a fresh witness
    /// term. This is the arm all nine of the measured instance's probes take.
    /// </summary>
    /// <param name="conclusion">The conclusion axiom.</param>
    /// <returns>The refutation axiom the arm emits.</returns>
    private static OwlClassAssertionAxiom ReplicatedNamedIndividualRefutation(OwlClassAssertionAxiom conclusion)
    {
        return ClassAssertion(new OwlObjectComplementOf(conclusion.Class), conclusion.Individual);
    }

    /// <summary>The conformance arm's anonymous-individual refutation arm, REPLICATED: an anonymous-individual class assertion reads existentially, so forcing the asserted class empty is its exact negation — a probe form carrying NO complement at all.</summary>
    /// <param name="asserted">The conclusion's asserted class.</param>
    /// <returns>The refutation axiom the arm emits.</returns>
    private static OwlSubClassOfAxiom ReplicatedAnonymousIndividualRefutation(OwlClassExpression asserted)
    {
        return SubClassOf(asserted, Nothing);
    }

    /// <summary>The conformance arm's data-cardinality De Morgan dual, REPLICATED: a conclusion maximum of n becomes a POSITIVE minimum of n plus one on the same individual — the second probe form carrying no complement.</summary>
    /// <param name="individual">The conclusion's individual.</param>
    /// <param name="property">The bounded data property's local name.</param>
    /// <param name="bound">The conclusion's maximum.</param>
    /// <returns>The refutation axiom the arm emits.</returns>
    private static OwlClassAssertionAxiom ReplicatedDataCardinalityDual(RdfTerm individual, string property, int bound)
    {
        return ClassAssertion(MinData(property, bound + 1), individual);
    }

    /// <summary>
    /// The battery's canonical admitted chassis: two gadget properties, one
    /// object and one data, each with its polarity pair; one composition; one
    /// existential over the single modal role; and a told individual carrying
    /// two types and one universal. The all-false modal vector is compatible, so
    /// the chassis certifies with nothing spawned.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule AdmittedChassisModule()
    {
        return Module([.. AdmittedChassisAxioms()]);
    }

    /// <summary>The admitted chassis's axioms, so a perturbation row can add or replace one without restating the rest.</summary>
    /// <returns>The axioms.</returns>
    private static List<OwlAxiom> AdmittedChassisAxioms()
    {
        return
        [
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Pb"), MinData("pb", 1)),
            EquivalentClasses(Class("Zb"), MaxData("pb", 0)),
            EquivalentClasses(Class("Cab"), Intersection(Class("Pa"), Class("Pb"))),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")),
            ClassAssertion(Class("Zb"), Individual("v")),
            ClassAssertion(All("r", Class("Pa")), Individual("v")),
        ];
    }

    /// <summary>The chassis with a definition CYCLE across two intersection-defined classes, which has no evaluation order.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DefinitionCycleModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(EquivalentClasses(Class("Ca"), Intersection(Class("Cb"), Class("Pa"))));
        axioms.Add(EquivalentClasses(Class("Cb"), Intersection(Class("Ca"), Class("Pa"))));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with BOTH of one gadget property's polarity classes carrying a further definition — two functional dependencies for one bit.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SecondDefinerModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(EquivalentClasses(Class("Pa"), Intersection(Class("Pb"), Class("Cab"))));
        axioms.Add(EquivalentClasses(Class("Za"), Intersection(Class("Pb"), Class("Ex"))));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with a gadget property carrying its minimum side ALONE, so its polarity pair cannot be identified.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UnpairedPolarityModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(EquivalentClasses(Class("Pc"), MinObject("pc", 1)));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with an intersection of THREE named operands, told satisfied or with one operand's support withheld — the arity leg's pair.</summary>
    /// <param name="satisfied">Whether the third operand's told support is present.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule NaryIntersectionModule(bool satisfied)
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(EquivalentClasses(Class("Triple"), Intersection(Class("Ta"), Class("Tb"), Class("Tc"))));
        axioms.Add(ClassAssertion(Class("Triple"), Individual("v")));
        axioms.Add(ClassAssertion(Class("Ta"), Individual("v")));
        axioms.Add(ClassAssertion(Class("Tb"), Individual("v")));
        if(satisfied)
        {
            axioms.Add(ClassAssertion(Class("Tc"), Individual("v")));
        }
        else
        {
            axioms.Add(ClassAssertion(Class("Za"), Individual("w")));
            axioms.Add(EquivalentClasses(Class("Tc"), Intersection(Class("Pa"), Class("Za"))));
        }

        return Module([.. axioms]);
    }

    /// <summary>The module equating <c>owl:Nothing</c> with an intersection whose two operands are both told at the individual — a module with NO MODEL, whose bottom the clash face knows and the certify face must not read as an ordinary atom.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BottomEquatedModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(EquivalentClasses(Nothing, Intersection(Class("Na"), Class("Nb"))));
        axioms.Add(ClassAssertion(Class("Na"), Individual("v")));
        axioms.Add(ClassAssertion(Class("Nb"), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with a told universal whose filler is <c>owl:Nothing</c> beside a told existential — the second route to the same hole, through the admitted universal arm.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BottomFillerBoxModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(ClassAssertion(All("r", Nothing), Individual("v")));
        axioms.Add(ClassAssertion(Class("Ex"), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with an ALIEN axiom beside a clash template the composition closure reaches: face A ignores the alien axiom and decides, face B silences the module whole.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule AlienAxiomClashModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(ClassAssertion(Class("Pb"), Individual("v")));
        axioms.Add(SubClassOf(Class("Pa"), Class("Pb")));
        axioms.Add(ClassAssertion(new OwlObjectComplementOf(Class("Cab")), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>The module whose goal is reachable ONLY through the cardinality side: a second class equivalent to the same unqualified minimum the told type carries, denied at the individual.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CardinalitySideGoalModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Reached"), MinObject("pa", 1)),
            ClassAssertion(Class("Pa"), Individual("v")),
            ClassAssertion(new OwlObjectComplementOf(Class("Reached")), Individual("v")));
    }

    /// <summary>The same shape with the goal one COMPOSITION step from the told facts — the discrimination control the cardinality-side row measures against.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CompositionSideGoalModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Reached"), Intersection(Class("Pa"), Class("Ta"))),
            ClassAssertion(Class("Pa"), Individual("v")),
            ClassAssertion(Class("Ta"), Individual("v")),
            ClassAssertion(new OwlObjectComplementOf(Class("Reached")), Individual("v")));
    }

    /// <summary>The two-model module: one free gadget property no told type pins, so the individual sits in the zero-bound class in the model the construction mints and in the minimum-bound class in a second model of the same module.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule TwoModelModule()
    {
        return Module([.. TwoModelAxioms()]);
    }

    /// <summary>The two-model module's axioms.</summary>
    /// <returns>The axioms.</returns>
    private static List<OwlAxiom> TwoModelAxioms()
    {
        return
        [
            EquivalentClasses(Class("Pg"), MinObject("g", 1)),
            EquivalentClasses(Class("Zg"), MaxObject("g", 0)),
            EquivalentClasses(Class("Cg"), Intersection(Class("Pg"), Class("Tg"))),
            EquivalentClasses(Class("Eg"), Some("r", Class("Pg"))),
            ClassAssertion(Class("Tg"), Individual("v")),
        ];
    }

    /// <summary>The two-model module beside a refutation axiom denying one of the two polarity classes at the told individual.</summary>
    /// <param name="positive">Whether the denied class is the minimum-bound one rather than the zero-bound one.</param>
    /// <returns>The probe module.</returns>
    private static ReasoningModule TwoModelProbeModule(bool positive)
    {
        List<OwlAxiom> axioms = TwoModelAxioms();
        axioms.Add(ReplicatedNamedIndividualRefutation(ClassAssertion(positive ? Class("Pg") : Class("Zg"), Individual("v"))));

        return Module([.. axioms]);
    }

    /// <summary>The module whose told types pin one free bit BOTH ways — the told unit-propagation contradiction, which is silence and never a verdict.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldContradictionModule()
    {
        List<OwlAxiom> axioms = AdmittedChassisAxioms();
        axioms.Add(ClassAssertion(Class("Za"), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>
    /// The module whose sweep runs to its end without a model: a told type pins
    /// an existential atom TRUE, its filler is an atomic class the module never
    /// defines, and no spawned successor can be put inside an atomic extension
    /// the construction fixes only at told individuals.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ExhaustedSweepModule()
    {
        return Module([.. RawStructureAxioms(propositionalFiller: false)]);
    }

    /// <summary>The module whose single candidate structure fails its verification pass: every free bit is pinned, so exactly one vector is built and the told universal's filler is one no spawned successor carries.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule FailedVerificationModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")),
            ClassAssertion(Class("Ex"), Individual("v")),
            ClassAssertion(All("r", Class("Za")), Individual("v")));
    }

    /// <summary>
    /// The raw-structure module: a told type pins one existential atom TRUE and
    /// the spawn mints a successor for its filler's signature. With a
    /// NON-PROPOSITIONAL filler no successor free vector can be solved for it,
    /// so the existential is FALSE in the finished structure where the
    /// construction's table says TRUE and the pass declines; with a
    /// propositional one the successor satisfies it and the module certifies.
    /// </summary>
    /// <param name="propositionalFiller">Whether the existential's filler is propositional.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule RawStructureModule(bool propositionalFiller)
    {
        return Module([.. RawStructureAxioms(propositionalFiller)]);
    }

    /// <summary>The raw-structure module's axioms.</summary>
    /// <param name="propositionalFiller">Whether the existential's filler is propositional.</param>
    /// <returns>The axioms.</returns>
    private static List<OwlAxiom> RawStructureAxioms(bool propositionalFiller)
    {
        return
        [
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", propositionalFiller ? Class("Pa") : Class("Op"))),
            ClassAssertion(Class("Ex"), Individual("v")),
        ];
    }

    /// <summary>
    /// The two-vector module: one existential atom is pinned TRUE by a told type
    /// and a second existential's filler is satisfied by the FIRST one's
    /// successor, so the early vectors carry a table that says false where the
    /// structure says true and fail, and a later vector spawns both demands and
    /// certifies. The arena of the certifying vector holds one told node and TWO
    /// successors — never the sum of the vectors tried.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule TwoVectorModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Pb"), MinObject("pb", 1)),
            EquivalentClasses(Class("Zb"), MaxObject("pb", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            EquivalentClasses(Class("Ey"), Some("r", Class("Zb"))),
            ClassAssertion(Class("Ex"), Individual("v")));
    }

    /// <summary>
    /// The inclusion module: one gadget property whose definer class is fixed by
    /// an intersection the told types settle, so the class's own label and the
    /// property's materialised extension are computed by two different routes
    /// once defined-atom elimination is suppressed. On the zero side the
    /// candidate leaves the extension empty while the class is FALSE, which only
    /// the REVERSE inclusion catches; on the minimum side the class is TRUE
    /// where the extension is empty, which the forward one catches.
    /// </summary>
    /// <param name="zeroSide">Whether the definer stands on the property's zero side.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule InclusionModule(bool zeroSide)
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("Pq"), MinObject("q", 1)),
            EquivalentClasses(Class("Zq"), MaxObject("q", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pq"))),
            ClassAssertion(Class("Ta"), Individual("v")),
        ];
        if(zeroSide)
        {
            axioms.Add(EquivalentClasses(Class("Zq"), Intersection(Class("Ta"), Class("Tb"))));
        }
        else
        {
            axioms.Add(EquivalentClasses(Class("Pq"), Intersection(Class("Ta"), Class("Tb"))));
            axioms.Add(ClassAssertion(Class("Tb"), Individual("v")));
        }

        return Module([.. axioms]);
    }

    /// <summary>The role-index module: the told individual carries a TRUE object gadget property, hence a self-loop, beside an existential over the modal role into that same class. A flat edge relation would read the self-loop as a modal successor; the role-indexed one does not, so the all-false modal vector certifies with nothing spawned.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RoleIndexModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")));
    }

    /// <summary>The mixed-conjunct module: a class defined by an intersection carrying ONE named conjunct beside a cardinality restriction, with the named conjunct told at the individual. A partial-conjunct backward composition would derive the class and, with the probe axiom present, report the conclusion entailed on a green run.</summary>
    /// <param name="probe">Whether the conformance arm's refutation axiom for that goal is present.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule MixedConjunctModule(bool probe)
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("Cx"), Intersection(Class("Ta"), MinObject("pa", 1))),
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Ta"), Individual("v")),
        ];
        if(probe)
        {
            axioms.Add(ReplicatedNamedIndividualRefutation(ClassAssertion(Class("Cx"), Individual("v"))));
        }

        return PaddedModule(axioms);
    }

    /// <summary>The composition clash module written name-first or intersection-first — the argument-order pair proving the name side is chosen by construct. The clash sits THREE compositions down the chain, so a rule-application ceiling narrowed below that depth stops the closure before it is reached.</summary>
    /// <param name="intersectionFirst">Whether the intersection stands as the equivalence's first operand.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ArgumentOrderModule(bool intersectionFirst)
    {
        return Module(
            intersectionFirst
                ? EquivalentClasses(Intersection(Class("Ta"), Class("Tb")), Class("Reached"))
                : EquivalentClasses(Class("Reached"), Intersection(Class("Ta"), Class("Tb"))),
            EquivalentClasses(Class("Onward"), Intersection(Class("Reached"), Class("Ta"))),
            EquivalentClasses(Class("Final"), Intersection(Class("Onward"), Class("Tb"))),
            ClassAssertion(Class("Ta"), Individual("v")),
            ClassAssertion(Class("Tb"), Individual("v")),
            ClassAssertion(new OwlObjectComplementOf(Class("Final")), Individual("v")));
    }

    /// <summary>The module whose clash route needs an equivalence with TWO class-IRI operands, which leaves no conjunct to drop and no intersection to compose.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BothNamedEquivalenceModule()
    {
        return Module(
            EquivalentClasses(Class("Reached"), Class("Ta")),
            ClassAssertion(Class("Ta"), Individual("v")),
            ClassAssertion(new OwlObjectComplementOf(Class("Reached")), Individual("v")));
    }

    /// <summary>The module whose clash route runs through an equivalence with NO class-IRI operand, which drops whole.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NoNamedOperandModule()
    {
        return Module(
            EquivalentClasses(Intersection(Class("Ta"), Class("Tb")), Intersection(Class("Reached"), Class("Ta"))),
            ClassAssertion(Class("Ta"), Individual("v")),
            ClassAssertion(Class("Tb"), Individual("v")),
            ClassAssertion(new OwlObjectComplementOf(Class("Reached")), Individual("v")));
    }

    /// <summary>The axiom-kind module: the same composition told as a subsumption or as an equivalence, with or without the conformance arm's refutation axiom for the composed goal.</summary>
    /// <param name="equivalence">Whether the composition is told as an equivalence.</param>
    /// <param name="probe">Whether the refutation axiom is present.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule AxiomKindModule(bool equivalence, bool probe)
    {
        List<OwlAxiom> axioms =
        [
            equivalence
                ? EquivalentClasses(Class("Reached"), Intersection(Class("Ta"), Class("Tb")))
                : SubClassOf(Class("Reached"), Intersection(Class("Ta"), Class("Tb"))),
            ClassAssertion(Class("Ta"), Individual("v")),
            ClassAssertion(Class("Tb"), Individual("v")),
        ];
        if(probe)
        {
            axioms.Add(ReplicatedNamedIndividualRefutation(ClassAssertion(Class("Reached"), Individual("v"))));
        }

        return PaddedModule(axioms);
    }

    /// <summary>The bound-spelling module: the same two gadget bounds spelled as a minimum and a maximum or as their exact equivalents, which read identically because the VALUE decides.</summary>
    /// <param name="exactMinimum">Whether the minimum side is spelled as an exact one.</param>
    /// <param name="exactZero">Whether the zero side is spelled as an exact zero.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule BoundSpellingModule(bool exactMinimum, bool exactZero)
    {
        return Module(
            EquivalentClasses(Class("Pa"), exactMinimum ? ExactObject("pa", 1) : MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), exactZero ? ExactObject("pa", 0) : MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")));
    }

    /// <summary>The chassis whose gadget bound constrains nothing, leaving the polarity pair unidentifiable.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UninformativeBoundModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 2)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")));
    }

    /// <summary>The chassis whose data cardinality carries a QUALIFYING range — the one position a datatype IRI could stand in, and it is outside the admission grammar.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule QualifiedRangeBoundModule()
    {
        return Module(
            EquivalentClasses(Class("Pb"), new OwlDataCardinality(OwlCardinalityKind.Min, 1, DataProperty("pb"), new OwlDatatypeReference(new NamedNode(Utf8Strings.From(XsdString))))),
            EquivalentClasses(Class("Zb"), MaxData("pb", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pb"))),
            ClassAssertion(Class("Pb"), Individual("v")));
    }

    /// <summary>
    /// The inverted-polarity module: the class NAMED for the zero side carries
    /// the unqualified MINIMUM and the class named for the minimum side carries
    /// the maximum of zero. Coupled to the minimum side the told types
    /// contradict under the bound-driven read and agree under a name-driven one;
    /// coupled to the zero side they do the opposite.
    /// </summary>
    /// <param name="coupleToMinimumSide">Whether the told composition couples to the class carrying the minimum bound.</param>
    /// <param name="opaqueNames">Whether every class local name is replaced by one carrying no polarity hint.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule InvertedPolarityModule(bool coupleToMinimumSide, bool opaqueNames)
    {
        string zeroLooking = opaqueNames ? "Xa" : "ZeroLooking";
        string minimumLooking = opaqueNames ? "Xb" : "MinimumLooking";
        string coupled = opaqueNames ? "Xc" : "Coupled";
        string atom = opaqueNames ? "Xd" : "Atom";
        string existential = opaqueNames ? "Xe" : "Spawner";

        return Module(
            EquivalentClasses(Class(zeroLooking), MinObject("pa", 1)),
            EquivalentClasses(Class(minimumLooking), MaxObject("pa", 0)),
            EquivalentClasses(Class(coupled), Intersection(Class(coupleToMinimumSide ? zeroLooking : minimumLooking), Class(atom))),
            EquivalentClasses(Class(existential), Some("r", Class(zeroLooking))),
            ClassAssertion(Class(minimumLooking), Individual("v")),
            ClassAssertion(Class(coupled), Individual("v")),
            ClassAssertion(Class(atom), Individual("v")));
    }

    /// <summary>The small elimination pair: two free gadget properties beside two whose polarity classes carry a further definition, so the elimination halves the enumerated vector while the raw count stays inside every ceiling.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule EliminationPairModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Pb"), MinData("pb", 1)),
            EquivalentClasses(Class("Zb"), MaxData("pb", 0)),
            EquivalentClasses(Class("Pc"), MinData("pc", 1)),
            EquivalentClasses(Class("Zc"), MaxData("pc", 0)),
            EquivalentClasses(Class("Pc"), Intersection(Class("Pa"), Class("Pb"))),
            EquivalentClasses(Class("Pd"), MinData("pd", 1)),
            EquivalentClasses(Class("Zd"), MaxData("pd", 0)),
            EquivalentClasses(Class("Pd"), Intersection(Class("Pa"), Class("Pc"))),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")),
            ClassAssertion(Class("Pb"), Individual("v")));
    }

    /// <summary>The signature-shape module: two existentials whose fillers are syntactically distinct spellings of one propositional content, or one propositional filler beside a non-propositional one.</summary>
    /// <param name="distinctSpellings">Whether the two fillers spell the same propositional content differently.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule SignatureShapeModule(bool distinctSpellings)
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Pb"), MinData("pb", 1)),
            EquivalentClasses(Class("Zb"), MaxData("pb", 0)),
            EquivalentClasses(Class("Sa"), Intersection(Class("Pa"), Class("Pb"))),
            EquivalentClasses(Class("Sb"), Intersection(Class("Pb"), Class("Pa"))),
            EquivalentClasses(Class("Ex"), Some("r", Class("Sa"))),
            EquivalentClasses(Class("Ey"), Some("r", distinctSpellings ? Class("Sb") : Class("Op"))),
            ClassAssertion(Class("Pa"), Individual("v")));
    }

    /// <summary>The static-signature module: the same existential occurrences with and without a told type forcing the spawn, so the measurement can be compared across a construction that spawns nothing and one that does.</summary>
    /// <param name="forcedToSpawn">Whether a told type pins the existential atom true.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule StaticSignatureModule(bool forcedToSpawn)
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Ta"), Individual("v")),
        ];
        if(forcedToSpawn)
        {
            axioms.Add(ClassAssertion(Class("Ex"), Individual("v")));
        }

        return Module([.. axioms]);
    }

    /// <summary>The module whose SURVIVING free gadget atoms stand one past the free-atom ceiling, the overflow derived from the shared constant.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule FreeAtomOverflowModule()
    {
        List<OwlAxiom> axioms = [];
        for(int index = 0; index <= ContextModalGadgetTreeDecider.ModalGadgetFreeAtomBound; index++)
        {
            axioms.Add(EquivalentClasses(Class("P" + index), MinData("p" + index, 1)));
            axioms.Add(EquivalentClasses(Class("Z" + index), MaxData("p" + index, 0)));
        }

        axioms.Add(EquivalentClasses(Class("Ex"), Some("r", Class("P0"))));
        axioms.Add(ClassAssertion(Class("Ta"), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>The module whose deduped successor demands stand one past the signature ceiling while its surviving free atoms stay well inside theirs.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SignatureOverflowModule()
    {
        List<OwlAxiom> axioms = [];
        for(int index = 0; index < SignatureOverflowProperties; index++)
        {
            axioms.Add(EquivalentClasses(Class("P" + index), MinData("p" + index, 1)));
            axioms.Add(EquivalentClasses(Class("Z" + index), MaxData("p" + index, 0)));
        }

        int demands = ContextModalGadgetTreeDecider.ModalGadgetSignatureBound + 1;
        for(int index = 0; index < demands; index++)
        {
            axioms.Add(EquivalentClasses(Class("S" + index), Intersection(Class("P" + (index % SignatureOverflowProperties)), Class(index < SignatureOverflowProperties ? "Z" + ((index + 1) % SignatureOverflowProperties) : "P" + ((index + 1) % SignatureOverflowProperties)))));
            axioms.Add(EquivalentClasses(Class("E" + index), Some("r", Class("S" + index))));
        }

        axioms.Add(ClassAssertion(Class("Ta"), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>The free gadget properties the signature-overflow module draws its distinct demands from, chosen so the demand count passes its ceiling while the free-atom count stays far below its own.</summary>
    private const int SignatureOverflowProperties = 10;

    /// <summary>The module whose spawned successors take the arena past its ceiling: several told individuals, each carrying every existential class as a told type, so each demands one successor per distinct signature.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NodeOverflowModule()
    {
        List<OwlAxiom> axioms = [];
        int demands = ContextModalGadgetTreeDecider.ModalGadgetSignatureBound;
        for(int index = 0; index < demands; index++)
        {
            axioms.Add(EquivalentClasses(Class("P" + index), MinData("p" + index, 1)));
            axioms.Add(EquivalentClasses(Class("Z" + index), MaxData("p" + index, 0)));
            axioms.Add(EquivalentClasses(Class("E" + index), Some("r", Class("P" + index))));
        }

        int carriers = (ContextModalGadgetTreeDecider.ModalGadgetNodeBound / demands) + 2;
        for(int carrier = 0; carrier < carriers; carrier++)
        {
            for(int index = 0; index < demands; index++)
            {
                axioms.Add(ClassAssertion(Class("E" + index), Individual("v" + carrier)));
            }
        }

        return Module([.. axioms]);
    }

    /// <summary>The module whose TOLD individuals alone pass the arena ceiling, with no existential anywhere to spawn from.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldArenaOverflowModule()
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
        ];
        for(int index = 0; index <= ContextModalGadgetTreeDecider.ModalGadgetNodeBound; index++)
        {
            axioms.Add(ClassAssertion(Class("Ta"), Individual("v" + index)));
        }

        return Module([.. axioms]);
    }

    /// <summary>The module whose ONE told individual carries more raw modal atoms — existential occurrences and told universals together — than the raw-modal-atom ceiling admits, its demands deduping to one so no other ceiling can trip.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ModalAtomOverflowModule()
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
        ];
        for(int index = 0; index <= ContextModalGadgetTreeDecider.ModalGadgetModalAtomBound; index++)
        {
            axioms.Add(EquivalentClasses(Class("E" + index), Some("r", Class("Pa"))));
        }

        axioms.Add(ClassAssertion(Class("Ta"), Individual("v")));

        return Module([.. axioms]);
    }

    /// <summary>The module whose told type forces one spawn, so a spawn-depth ceiling below that single level refuses it.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SpawningModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Ex"), Individual("v")));
    }

    /// <summary>The module whose structure carries several directed edges — object gadget self-loops beside the spawned modal edge — so a narrowed edge ceiling refuses it.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule EdgeDenseModule()
    {
        return Module(
            EquivalentClasses(Class("Pa"), MinObject("pa", 1)),
            EquivalentClasses(Class("Za"), MaxObject("pa", 0)),
            EquivalentClasses(Class("Pb"), MinObject("pb", 1)),
            EquivalentClasses(Class("Zb"), MaxObject("pb", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pa"))),
            ClassAssertion(Class("Pa"), Individual("v")),
            ClassAssertion(Class("Pb"), Individual("v")),
            ClassAssertion(Class("Ex"), Individual("v")));
    }

    /// <summary>A module the boolean-cardinality-gadget probe claims, optionally carrying an existential inside its prelude — a Shape-G-DECIDABLE module with a modal restriction.</summary>
    /// <param name="preludeExistential">Whether the module carries an existential inside its prelude.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule GadgetShapedModule(bool preludeExistential)
    {
        List<OwlAxiom> axioms =
        [
            EquivalentClasses(Class("Gadget"), MaxObject("g", 1)),
            EquivalentClasses(Class("Compound"), Intersection(Class("Ga"), Class("Gb"))),
            ClassAssertion(Class("Compound"), Individual("gadget")),
        ];
        if(preludeExistential)
        {
            axioms.Add(SubClassOf(Class("Ga"), Some("gr", Class("Gb"))));
        }

        return Module([.. axioms]);
    }

    /// <summary>A module the partition-counting probe claims.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule PartitionShapedModule()
    {
        return Module(
            EquivalentClasses(Class("Part"), Intersection(Some("pr", Class("Pa")), Some("pr", Class("Pb")), MaxObject("pr", 1))),
            ClassAssertion(Class("Part"), Individual("part")));
    }

    /// <summary>A module the bijection-chain probe claims.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BijectionShapedModule()
    {
        return Module(
            Characteristic(OwlPropertyCharacteristic.Functional, "bq"),
            new OwlInverseObjectPropertiesAxiom(Property("bq"), Property("invBq")) { Origin = Origin("inverse") },
            SubClassOf(Class("Ba"), Some("bq", Class("Bb"))),
            ClassAssertion(Class("Ba"), Individual("bijection")));
    }

    /// <summary>A module the told-ground-witness probe claims.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldGroundShapedModule()
    {
        return Module(
            PropertyAssertion(Individual("w1"), "wr", Individual("w2")),
            new OwlInverseObjectPropertiesAxiom(Property("wr"), Property("invWr")) { Origin = Origin("inverse") },
            SubClassOf(Class("Wa"), Some("wr", Class("Wb"))),
            SubClassOf(Class("Wc"), MaxObject("wc", 1)),
            ClassAssertion(Class("Wa"), Individual("w1")));
    }

    /// <summary>A module the restriction-rich-ground probe claims.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RestrictionRichShapedModule()
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Class("Ra"), MaxObject("rr", 1)),
            SubClassOf(Class("Rb"), All("rr", Class("Rc"))),
        ];
        for(int index = 0; index < RestrictionRichTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("Ra"), Individual("rich" + index)));
        }

        return Module([.. axioms]);
    }

    /// <summary>The told individual population the restriction-rich module carries — comfortably above the floor its own probe reads.</summary>
    private const int RestrictionRichTerms = 20;

    /// <summary>A module the skolem-expansion modal probe claims, the tail behind every probe on the nominal-free path.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ModalExpansionShapedModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinData("d", 1)),
            EquivalentClasses(Class("Down"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            new OwlInverseObjectPropertiesAxiom(Property("invS"), Property("s")) { Origin = Origin("inverse") },
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>A module the spy-point probe claims on the nominal path.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SpyPointShapedModule()
    {
        return Module(
            SubClassOf(Thing, Some("sp", OneOf("spy"))),
            new OwlInverseObjectPropertiesAxiom(Property("sp"), Property("invSp")) { Origin = Origin("inverse") },
            ClassAssertion(MaxObject("invSp", 2), Individual("spy")),
            SubClassOf(Class("Su"), MinObject("sr", 3)),
            ClassAssertion(Class("Su"), Anonymous("spyroot")));
    }

    /// <summary>The branching module whose gadget layer is carried ENTIRELY by data cardinality restrictions, so the census sets no counting mention and only a probe ahead of the counting gate can reach it.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DataOnlyGadgetLayerModule()
    {
        return PaddedModule(
        [
            EquivalentClasses(Class("Pb"), MinData("pb", 1)),
            EquivalentClasses(Class("Zb"), MaxData("pb", 0)),
            EquivalentClasses(Class("Ex"), Some("r", Class("Pb"))),
            ClassAssertion(Class("Pb"), Individual("v")),
        ]);
    }

    /// <summary>The module carrying a composition layer above the threshold with NO modal restriction anywhere — the shape the ordering-safety clause declines.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NoModalRestrictionModule()
    {
        return PaddedModule([ClassAssertion(Class("Bx0"), Individual("v"))]);
    }

    /// <summary>The branching module whose composition layer sits BELOW the threshold — face-decidable and probe-unreachable, the recorded reach cost.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BelowThresholdModule()
    {
        return AdmittedChassisModule();
    }

    /// <summary>Wraps a module's axioms with a composition-layer padding that clears the recognizer's threshold, so an ordering-independent fixture can still be reached through the census probe.</summary>
    /// <param name="axioms">The fixture's own axioms.</param>
    /// <returns>The padded module.</returns>
    private static ReasoningModule PaddedModule(List<OwlAxiom> axioms)
    {
        List<OwlAxiom> padded = [.. axioms];
        for(int index = 0; index < CompositionPadding; index++)
        {
            padded.Add(EquivalentClasses(Class("Bait" + index), Intersection(Class("Bx" + index), Class("By" + index))));
        }

        return Module([.. padded]);
    }

    /// <summary>Appends one axiom to a copied axiom list, so a probe row leaves the premise builder untouched.</summary>
    /// <param name="axioms">The premise's axioms.</param>
    /// <param name="appended">The axiom to append.</param>
    /// <returns>The extended list.</returns>
    private static List<OwlAxiom> Append(List<OwlAxiom> axioms, OwlAxiom appended)
    {
        List<OwlAxiom> extended = [.. axioms, appended];

        return extended;
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
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "origin")), new NamedNode(Utf8Strings.From(Example + "root")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference — a built-in whose extension the semantics fixes.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From(OwlThing)));

    /// <summary>The <c>owl:Nothing</c> reference — the other built-in.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From(OwlNothing)));

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

    /// <summary>An anonymous individual.</summary>
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

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf([.. operands]);
    }

    /// <summary>An existential restriction over a named role in the example namespace.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction over a named role in the example namespace.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MinObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a named role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified exact-cardinality restriction over a named role — read as its minimum and maximum halves together.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality ExactObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a data property.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MinData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a data property.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MaxData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Max, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A told equivalence axiom.</summary>
    /// <param name="first">The first operand.</param>
    /// <param name="second">The second operand.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom EquivalentClasses(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>A told object-property assertion.</summary>
    /// <param name="source">The edge's source individual.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="target">The edge's target individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom PropertyAssertion(RdfTerm source, string property, RdfTerm target)
    {
        return new OwlObjectPropertyAssertionAxiom(source, new NamedNode(Utf8Strings.From(Example + property)), target) { Origin = Origin("edge") };
    }

    /// <summary>A told property characteristic.</summary>
    /// <param name="characteristic">The characteristic.</param>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Characteristic(OwlPropertyCharacteristic characteristic, string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(characteristic, Property(property)) { Origin = Origin("characteristic") };
    }

    /// <summary>An entity declaration — non-logical passthrough the admission never charges.</summary>
    /// <param name="kind">The declared entity kind.</param>
    /// <param name="iri">The declared entity's IRI.</param>
    /// <returns>The axiom.</returns>
    private static OwlDeclarationAxiom Declare(OwlEntityKind kind, string iri)
    {
        return new OwlDeclarationAxiom(kind, new NamedNode(Utf8Strings.From(iri))) { Origin = Origin("declare") };
    }
}
