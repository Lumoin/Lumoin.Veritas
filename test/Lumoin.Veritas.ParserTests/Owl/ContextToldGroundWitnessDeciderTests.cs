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
/// The told-ground-witness habitat decider's battery: the corpus premise
/// certified by its own described model and its harness refutation probe
/// refuted by the derived membership, the four named soundness attacks each
/// pinned at the guard that stops it — the domain-forced non-member, the
/// pinned universal filler, the repeated IRI, and the derived-only inverse
/// edge — the no-skolemization pin that keeps a consistent module consistent,
/// the disagreeing equivalences and the sameness axiom that each silence the
/// certificate, the empty-class assertion, the shared blank label, the general
/// left-hand side, the inverse chain and the shared inverse partners, the
/// ontology import that passes through the admission as a non-logical marker,
/// the jurisdiction handover to the bijection-chain habitat, the carrier-window
/// silence with its inclusive boundary, the whole-module admission against the
/// monotone clash, and the explicit dark control with its census ride. Every
/// row drives the production seams — the faces-carrying reasoner overload or
/// the decider's own measurement surface — and every counter the battery reads
/// is consumed by an assert.
/// </summary>
[TestClass]
internal sealed class ContextToldGroundWitnessDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/toldgroundwitnesscsp#";

    /// <summary>Both told-ground-witness faces lit — the selection the deciding rows drive.</summary>
    private const EnumerationDeciderFaces ToldGroundWitnessFaces = EnumerationDeciderFaces.ToldGroundWitnessClash | EnumerationDeciderFaces.ToldGroundWitnessCertify;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the jurisdiction rows drive.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The clash reason family's complemented-membership leading identifier.</summary>
    private const string ComplementedMembership = "ToldGroundWitnessComplementedMembership(";

    /// <summary>The clash reason family's disjointness leading identifier.</summary>
    private const string DisjointMembership = "ToldGroundWitnessDisjointMembership(";

    /// <summary>The clash reason family's empty-class leading identifier.</summary>
    private const string AssertedNothingMembership = "ToldGroundWitnessAssertedNothingMembership(";

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a told-ground module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// The corpus premise told exactly as the manifest spells it: six
    /// declarations, the union class equated to its six-member enumeration, the
    /// six member typings, the domain axiom on the forward role, the told
    /// inverse pair, the delegate class equated to an existential over the
    /// inverse role into <c>owl:Thing</c>, the one person typing, and the single
    /// told edge. The described model — seven carriers, the told edge closed to
    /// two, and the four class extensions the least fixpoint fills — satisfies
    /// every axiom on re-check, so the certify face decides the premise
    /// consistent with zero inference attempts and no engine. The habitat assert
    /// doubles as the positive reachability pin: a nominal module every earlier
    /// probe declines must still reach the told-ground-witness probe.
    /// </summary>
    [TestMethod]
    public void Tw1DescribedModelCertifiesTheCorpusPremise()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusPremiseModule(), ToldGroundWitnessFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Tw1 DescribedModel: the certify face decides the corpus premise at the production ceiling.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Tw1 DescribedModel: the constructed model satisfies every told axiom.");
        Assert.IsEmpty(decision.Verdict.Subsumptions, "Tw1 DescribedModel: the described-model certificate claims no subsumption set.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Tw1 DescribedModel: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Tw1 DescribedModel: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, totals.EnumerationHabitat, "Tw1 DescribedModel: a nominal module every earlier probe declines still reaches the told-ground-witness probe and is labelled Shape W.");
        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderCertifications, "Tw1 DescribedModel: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw1 DescribedModel: a certified module takes no clash.");
        Assert.AreEqual(7, totals.ToldGroundWitnessCarrierCount, "Tw1 DescribedModel: the six enumerated members and the one person are the seven carriers.");
        Assert.AreEqual(2, totals.ToldGroundWitnessEdgeCount, "Tw1 DescribedModel: the one told edge closes to two under the told inverse pair.");
        Assert.AreEqual(0, totals.ToldGroundWitnessWindowExceededCarriers, "Tw1 DescribedModel: seven carriers sit well inside the carrier window.");

        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(CorpusPremiseModule());

        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw1 DescribedModel: the described-model route names the certificate.");
        Assert.IsNull(outcome.ClashReason, "Tw1 DescribedModel: a certificate names no clash reason.");
    }

    /// <summary>
    /// The strict arm's refutation-probe shape decided pre-engine: the corpus
    /// premise plus the EXACT axiom the shared refutation builders emit for a
    /// <c>ClassAssertion</c> conclusion on a named individual — a bare
    /// complement of the conclusion's class on the conclusion's own individual,
    /// with no intersection wrapper and no <c>owl:Nothing</c>. The told edge
    /// mirrors to the inverse role, the existential definition reads that
    /// derived edge into the delegate class, and the probe's complement denies
    /// the same membership: the clash face refutes, which is exactly what the
    /// entailment arm needs. The probe shape is the shared refutation switch's
    /// named-individual class-assertion arm in W3cOwl2DirectTests.cs, replicated
    /// here so a harness change cannot silently diverge from this battery.
    /// </summary>
    [TestMethod]
    public void Tw2ComplementProbeClashesOnTheDerivedMembership()
    {
        ReasoningModule module = CorpusPremiseModule(ClassAssertion(Complement(Class("EuroMP")), Individual("Kinnock")));
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsFalse(outcome.Consistent, "Tw2 ComplementProbe: a term derived into a class and denied of the same class refutes the probe module.");
        Assert.StartsWith(ComplementedMembership, outcome.ClashReason, StringComparison.Ordinal, "Tw2 ComplementProbe: the clash reason names the complemented membership.");
        Assert.IsNull(outcome.CertificateRoute, "Tw2 ComplementProbe: the complement keeps the certify face out of the module.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Tw2 ComplementProbe: the clash face decides the probe module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Tw2 ComplementProbe: the probe module is inconsistent, so the conclusion axiom is entailed.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Tw2 ComplementProbe: the probe decides with zero inference attempts.");
        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderClashes, "Tw2 ComplementProbe: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw2 ComplementProbe: no certificate on a refuted probe.");
        Assert.AreEqual(7, totals.ToldGroundWitnessCarrierCount, "Tw2 ComplementProbe: the probe names no new term, so the carrier count is the premise's own.");
        Assert.AreEqual(2, totals.ToldGroundWitnessEdgeCount, "Tw2 ComplementProbe: the closed edge count is the premise's own.");
    }

    /// <summary>
    /// The domain-forced non-member: a told domain axiom drives an individual
    /// the enumeration does not list into an enumeration-defined class. The
    /// class variable takes both the enumerated members and the forced element,
    /// so the equivalence's exactness check fails and the certify face is
    /// SILENT — that check is the SOLE guard here, the domain check itself
    /// passing tautologically because the same seeding put the forced element
    /// in. Nothing connects the shape to a ground clash either, so the clash
    /// face is silent too and ordinary saturation owns the module.
    /// </summary>
    [TestMethod]
    public void Tw3DomainForcedNonMemberStaysSilent()
    {
        ReasoningModule module = DomainForcedNonMemberModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Tw3 DomainForcedNonMember: the exactness check silences the certificate rather than repairing the class.");
        Assert.IsNull(outcome.CertificateRoute, "Tw3 DomainForcedNonMember: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Tw3 DomainForcedNonMember: the certify face never refutes, and no ground rule reaches a clash.");
        Assert.AreEqual(4, outcome.Window.CarrierCount, "Tw3 DomainForcedNonMember: the two enumerated members, the forced outsider, and the edge target are the four carriers.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, totals.EnumerationHabitat, "Tw3 DomainForcedNonMember: the recognized-but-silent module still carries the Shape W census label.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw3 DomainForcedNonMember: no certificate on the attack module.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw3 DomainForcedNonMember: no clash either.");
    }

    /// <summary>
    /// The universal filler reads the edge relation: the delegate class is
    /// defined by an existential over the inverse role into <c>owl:Thing</c>,
    /// and <c>owl:Thing</c> is PINNED to the whole domain rather than left as a
    /// fixpoint variable. A variable starting empty would evaluate the
    /// existential to the empty set and silently empty the delegate class, so
    /// the derived membership — and with it the disjointness clash against the
    /// person typing — would vanish. The clash firing is the pin; the certify
    /// face takes no certificate on the same module.
    /// </summary>
    [TestMethod]
    public void Tw4ThingFillerReadsTheEdgeDomain()
    {
        ReasoningModule module = ThingFillerDisjointModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsFalse(outcome.Consistent, "Tw4 ThingFiller: the pinned universal filler reads the mirrored edge, so the delegate class is nonempty and clashes with the disjoint typing.");
        Assert.StartsWith(DisjointMembership, outcome.ClashReason, StringComparison.Ordinal, "Tw4 ThingFiller: the clash reason names the disjointness.");
        Assert.IsNull(outcome.CertificateRoute, "Tw4 ThingFiller: a refutation names no certificate route.");

        ContextSaturationStatistics certifyOnly = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ToldGroundWitnessCertify, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(0, certifyOnly.ToldGroundWitnessDeciderCertifications, "Tw4 ThingFiller: with the clash face dark the certify face still never certifies the unsatisfiable module.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderClashes, "Tw4 ThingFiller: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// The repeated IRI is ONE carrier: two typings and a self-loop edge name
    /// the same individual through separately constructed terms, and the
    /// content-equal keying keeps the domain at a single element. Object
    /// identity or ordinal-reference keying would split it into several
    /// carriers and turn the told disjointness into a satisfiable module — a
    /// wrong consistency. The measured domain size is the pin; the clash face
    /// refutes on the disjointness.
    /// </summary>
    [TestMethod]
    public void Tw5RepeatedIriYieldsOneCarrier()
    {
        ReasoningModule module = RepeatedIriModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.AreEqual(1, outcome.Window.CarrierCount, "Tw5 RepeatedIri: every mention of the one IRI keys to the same carrier, so the domain holds exactly one element.");
        Assert.IsFalse(outcome.Consistent, "Tw5 RepeatedIri: one element in two told-disjoint classes refutes the module.");
        Assert.StartsWith(DisjointMembership, outcome.ClashReason, StringComparison.Ordinal, "Tw5 RepeatedIri: the clash reason names the disjointness.");
        Assert.IsNull(outcome.CertificateRoute, "Tw5 RepeatedIri: a refutation names no certificate route.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessCarrierCount, "Tw5 RepeatedIri: the measured domain size rides the statistics record.");
        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderClashes, "Tw5 RepeatedIri: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// The derived-only inverse edge carries the domain constraint: the module
    /// tells one forward edge and a told inverse pair, and the domain axiom sits
    /// on the INVERSE role, whose only edge is the mirrored one. Reading the
    /// told edge list rather than the completed relation would miss the forced
    /// membership and leave the disjointness unrefuted. The measured edge count
    /// pins the completion; the clash refutes.
    /// </summary>
    [TestMethod]
    public void Tw6DerivedInverseEdgeForcesTheDomainMembership()
    {
        ReasoningModule module = DerivedInverseDomainModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.AreEqual(2, outcome.Window.EdgeCount, "Tw6 DerivedInverseEdge: the one told edge closes to two, the second being the only edge the constrained role holds.");
        Assert.IsFalse(outcome.Consistent, "Tw6 DerivedInverseEdge: the derived edge forces the domain membership, which clashes with the told-disjoint typing.");
        Assert.StartsWith(DisjointMembership, outcome.ClashReason, StringComparison.Ordinal, "Tw6 DerivedInverseEdge: the clash reason names the disjointness.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderClashes, "Tw6 DerivedInverseEdge: the clash face's counter reads the decision.");
        Assert.AreEqual(2, totals.ToldGroundWitnessEdgeCount, "Tw6 DerivedInverseEdge: the completed edge count rides the statistics record.");
    }

    /// <summary>
    /// The general left-hand side seeds the variable: a subclass axiom whose
    /// SUBJECT is <c>owl:Thing</c> and an equivalence whose defining side is
    /// <c>owl:Thing</c> each hand the whole domain to their named target. A rule
    /// restricted to named left-hand sides would seed nothing, and both
    /// exactness checks would then fail on a trivially consistent module. The
    /// certificate landing is the pin.
    /// </summary>
    [TestMethod]
    public void Tw7ThingOnTheLeftSeedsTheVariable()
    {
        ReasoningModule module = ThingOnTheLeftModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsTrue(outcome.Consistent, "Tw7 ThingOnTheLeft: the universal left-hand side seeds the whole domain, so both checks pass.");
        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw7 ThingOnTheLeft: the described-model route names the certificate.");
        Assert.IsNull(outcome.ClashReason, "Tw7 ThingOnTheLeft: a certificate names no clash reason.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.IsTrue(decision.Verdict!.IsConsistent, "Tw7 ThingOnTheLeft: the reasoner carries the same certificate.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.ToldGroundWitnessDeciderCertifications, "Tw7 ThingOnTheLeft: the certify face's counter reads the decision.");
    }

    /// <summary>
    /// The existential direction is never skolemized: the module types an
    /// individual into a class an existential defines, so every model owes that
    /// individual a successor — but the witness may be an element no told term
    /// denotes. A rule instantiating the successor with a told term would put
    /// the module's one person into the told range class and refute a module
    /// that has a model. The clash face's SILENCE is the whole assertion; the
    /// certify face is silent too, the told complement lying outside the
    /// evaluable grammar.
    /// </summary>
    [TestMethod]
    public void Tw8ExistentialDirectionNeverSkolemizes()
    {
        ReasoningModule module = NoSkolemizationModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Tw8 ExistentialDirection: a consistent module must never be refuted, and the complement keeps the certificate out.");
        Assert.IsNull(outcome.ClashReason, "Tw8 ExistentialDirection: no told term is ever picked as the existential's witness, so no clash reason exists.");
        Assert.IsNull(outcome.CertificateRoute, "Tw8 ExistentialDirection: a silent certify face names no route.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, totals.EnumerationHabitat, "Tw8 ExistentialDirection: the recognized-but-silent module still carries the Shape W census label.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw8 ExistentialDirection: no clash decision on a module that has a model.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw8 ExistentialDirection: no certificate either.");
    }

    /// <summary>
    /// Two equivalences on one class disagree: the fixpoint unions both
    /// defining sides into the variable, so the wider equivalence's check passes
    /// and the narrower one's fails. Every equivalence is checked
    /// independently — none overriding another — so the disagreement lands as a
    /// SILENCE rather than as a certificate over whichever axiom happened to be
    /// read last.
    /// </summary>
    [TestMethod]
    public void Tw9DisagreeingEquivalencesStaySilent()
    {
        ReasoningModule module = DisagreeingEquivalencesModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Tw9 DisagreeingEquivalences: the second equivalence's check fails, so no certificate is taken.");
        Assert.IsNull(outcome.CertificateRoute, "Tw9 DisagreeingEquivalences: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Tw9 DisagreeingEquivalences: the certify face never refutes.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, totals.EnumerationHabitat, "Tw9 DisagreeingEquivalences: the recognized-but-silent module still carries the Shape W census label.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw9 DisagreeingEquivalences: no certificate on disagreeing definitions.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw9 DisagreeingEquivalences: no clash either.");
    }

    /// <summary>
    /// The certify jurisdiction is WHOLE-MODULE: the corpus premise certifies,
    /// and the same premise plus ONE told sameness axiom does not — a sameness
    /// axiom would collapse two carriers the construction keeps apart, so it is
    /// outside the admission and the whole certificate falls. The clash face,
    /// monotone, is unaffected by the unrecognized axiom and stays silent
    /// exactly as it was.
    /// </summary>
    [TestMethod]
    public void Tw10SameIndividualBlocksTheCertificate()
    {
        Assert.AreEqual("DescribedModel", ContextToldGroundWitnessDecider.Run(CorpusPremiseModule()).CertificateRoute, "Tw10 SameIndividual: the unperturbed premise certifies, so the row's silence is the sameness axiom's doing.");

        ReasoningModule module = CorpusPremiseModule(Same("UK", "BE"));
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Tw10 SameIndividual: one unadmitted axiom leaves the certify face silent.");
        Assert.IsNull(outcome.CertificateRoute, "Tw10 SameIndividual: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Tw10 SameIndividual: the monotone clash face ignores the unrecognized axiom and derives no clash.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, totals.EnumerationHabitat, "Tw10 SameIndividual: the recognized-but-silent module still carries the Shape W census label.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw10 SameIndividual: no certificate past the whole-module admission.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw10 SameIndividual: no clash either.");
    }

    /// <summary>
    /// The jurisdiction handover, told as a minimal pair: the corpus premise
    /// carries the told object-property assertion, the told inverse pair, and the
    /// plain-role existential the Shape W probe reads. A told functional
    /// characteristic on the inverse pair's OTHER role leaves the Shape B signal
    /// incomplete — that probe binds its three ingredients to a single role — so
    /// the premise keeps the Shape W label and its own faces still own it. The
    /// same characteristic on the existential's OWN role completes the signal,
    /// and the bijection-chain probe is consulted first at both fallthroughs, so
    /// that module takes the Shape B label and no told-ground-witness face may
    /// claim it. The pair is the probe-order guarantee that keeps the two habitats
    /// from contending for one module.
    /// </summary>
    [TestMethod]
    public void Tw11CharacteristicOnTheExistentialRoleRoutesToBijectionChain()
    {
        ContextSaturationStatistics plain = ContextSaturationModuleReasoner.DecideModule(CorpusPremiseModule(), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, plain.EnumerationHabitat, "Tw11 Characteristic: without a characteristic the premise is Shape W.");

        ContextSaturationStatistics offRole = ContextSaturationModuleReasoner.DecideModule(CorpusPremiseModule(Functional("hasEuroMP")), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, offRole.EnumerationHabitat, "Tw11 Characteristic: a characteristic away from the existential's role completes no Shape B signal, so the premise keeps the Shape W label.");
        Assert.AreEqual(0, offRole.ToldGroundWitnessDeciderCertifications, "Tw11 Characteristic: a told characteristic is outside the certify face's whole-module admission, so the retained label takes no certificate.");
        Assert.AreEqual(0, offRole.ToldGroundWitnessDeciderClashes, "Tw11 Characteristic: no clash on the off-role perturbation either.");

        ContextSaturationStatistics onRole = ContextSaturationModuleReasoner.DecideModule(CorpusPremiseModule(Functional("isEuroMPFrom")), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, onRole.EnumerationHabitat, "Tw11 Characteristic: the existential's own role told functional beside its told inverse completes the Shape B signal, which is consulted first.");
        Assert.AreEqual(0, onRole.ToldGroundWitnessDeciderClashes, "Tw11 Characteristic: no told-ground-witness clash on a module the bijection-chain habitat claims.");
        Assert.AreEqual(0, onRole.ToldGroundWitnessDeciderCertifications, "Tw11 Characteristic: no told-ground-witness certificate either.");
    }

    /// <summary>
    /// The empty-class assertion refutes outright and never certifies: a told
    /// typing with <c>owl:Nothing</c> demands a member of an extension empty in
    /// every interpretation, so the clash face decides before any propagation
    /// runs. With the clash face dark the certify face reaches the same module
    /// and its own check fails — <c>owl:Nothing</c> is pinned to the empty set,
    /// so the assertion cannot be satisfied — leaving a silence, never a
    /// consistency.
    /// </summary>
    [TestMethod]
    public void Tw12NothingAssertionRefutesAndNeverCertifies()
    {
        ReasoningModule module = NothingAssertionModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsFalse(outcome.Consistent, "Tw12 NothingAssertion: an asserted empty class refutes the module outright.");
        Assert.StartsWith(AssertedNothingMembership, outcome.ClashReason, StringComparison.Ordinal, "Tw12 NothingAssertion: the clash reason names the empty-class assertion.");
        Assert.IsNull(outcome.CertificateRoute, "Tw12 NothingAssertion: a refutation names no certificate route.");

        ContextSaturationStatistics certifyOnly = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ToldGroundWitnessCertify, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(0, certifyOnly.ToldGroundWitnessDeciderCertifications, "Tw12 NothingAssertion: with the clash face dark the certify face is silent, never consistent.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderClashes, "Tw12 NothingAssertion: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// A shared blank-node label is ONE carrier across every axiom that names
    /// it: three separately constructed anonymous terms with the same label key
    /// to a single element beside the one named anchor, so the domain holds two
    /// carriers rather than four. The certificate landing on the module pins
    /// that anonymous identity carries through the construction and every
    /// re-check.
    /// </summary>
    [TestMethod]
    public void Tw13SharedBlankLabelIsOneCarrier()
    {
        ReasoningModule module = SharedBlankLabelModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.AreEqual(2, outcome.Window.CarrierCount, "Tw13 SharedBlankLabel: the three mentions of one label are one carrier beside the named anchor.");
        Assert.IsTrue(outcome.Consistent, "Tw13 SharedBlankLabel: the described model satisfies every told axiom.");
        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw13 SharedBlankLabel: the described-model route names the certificate.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(2, totals.ToldGroundWitnessCarrierCount, "Tw13 SharedBlankLabel: the measured domain size rides the statistics record.");
        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderCertifications, "Tw13 SharedBlankLabel: the certify face's counter reads the decision.");
    }

    /// <summary>
    /// The carrier-window silence charges its named counter, with the measured
    /// numbers landing BEFORE the boundary comparison: one carrier past the
    /// bound leaves both faces silent even though the module's own told
    /// disjointness clashes outright, so silence here is the window doing its
    /// work and not a coincidence of the shapes. The same template sitting
    /// exactly AT the bound still decides, so the boundary is inclusive and the
    /// silence begins one carrier later.
    /// </summary>
    [TestMethod]
    public void Tw14CarrierWindowSilencesBothFaces()
    {
        int overflow = ContextToldGroundWitnessDecider.ToldGroundWitnessCarrierBound + 1;
        ReasoningModule module = CarrierWindowModule(overflow);
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Tw14 CarrierWindow: both faces are silent past the carrier bound.");
        Assert.AreEqual(overflow, outcome.Window.CarrierCount, "Tw14 CarrierWindow: the measured domain size is reported past the bound.");
        Assert.AreEqual(1, outcome.Window.WindowSilences, "Tw14 CarrierWindow: the silence is charged to the ground-window counter.");
        Assert.IsNull(outcome.ClashReason, "Tw14 CarrierWindow: no reason past the bound.");
        Assert.IsNull(outcome.CertificateRoute, "Tw14 CarrierWindow: no route past the bound.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessWindowExceededCarriers, "Tw14 CarrierWindow: the window silence rides the statistics record.");
        Assert.AreEqual(overflow, totals.ToldGroundWitnessCarrierCount, "Tw14 CarrierWindow: the measured carriers ride the statistics record.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw14 CarrierWindow: no clash past the carrier bound.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw14 CarrierWindow: no certificate past the carrier bound.");

        ToldGroundWitnessOutcome atBound = ContextToldGroundWitnessDecider.Run(CarrierWindowModule(ContextToldGroundWitnessDecider.ToldGroundWitnessCarrierBound));

        Assert.IsFalse(atBound.Consistent, "Tw14 CarrierWindow: the clash face decides AT the carrier bound — the boundary is inclusive.");
        Assert.AreEqual(0, atBound.Window.WindowSilences, "Tw14 CarrierWindow: no window silence exactly at the bound.");
        Assert.AreEqual(ContextToldGroundWitnessDecider.ToldGroundWitnessCarrierBound, atBound.Window.CarrierCount, "Tw14 CarrierWindow: the measured carrier ceiling is the face's own bound.");
        Assert.AreEqual(ContextToldGroundWitnessDecider.ToldGroundWitnessClassBound, atBound.Window.CarrierCount, "Tw14 CarrierWindow: the class ceiling shares the measured carrier bound — one boundary discipline across the ground window.");
        Assert.AreEqual(ContextToldGroundWitnessDecider.ToldGroundWitnessRoleBound, atBound.Window.CarrierCount, "Tw14 CarrierWindow: the role ceiling shares the measured carrier bound.");
        Assert.AreEqual(ContextBijectionChainDecider.BijectionChainClassBound, atBound.Window.CarrierCount, "Tw14 CarrierWindow: the ground window shares the counting faces' measured sixteen ceiling.");
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection the corpus premise
    /// keeps the engine-face budget abstention — the abstained outcome, no
    /// verdict, the inclusive ceiling spent, a genuine saturation behind it —
    /// and the census still ships: the habitat label and both measured numbers
    /// are on the record while neither decision counter moved.
    /// </summary>
    [TestMethod]
    public void Tw15DarkFacesDecideNothing()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusPremiseModule(), EnumerationDeciderFaces.None, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Tw15 DarkFaces: the premise abstains with both faces dark.");
        Assert.IsNull(decision.Verdict, "Tw15 DarkFaces: the dark abstention carries no verdict.");
        Assert.AreEqual((long)ProbeBudget.MaxInferences, totals.InferenceAttempts, "Tw15 DarkFaces: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Tw15 DarkFaces: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, totals.EnumerationHabitat, "Tw15 DarkFaces: the habitat label rides the dark abstention record.");
        Assert.AreEqual(7, totals.ToldGroundWitnessCarrierCount, "Tw15 DarkFaces: the carriers are measured dark.");
        Assert.AreEqual(2, totals.ToldGroundWitnessEdgeCount, "Tw15 DarkFaces: the completed edges are measured dark.");
        Assert.AreEqual(0, totals.ToldGroundWitnessWindowExceededCarriers, "Tw15 DarkFaces: no window silence dark at seven carriers.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw15 DarkFaces: no clash decision with the faces dark.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw15 DarkFaces: no certificate with the faces dark.");
    }

    /// <summary>
    /// The two jurisdictions are asymmetric by design: one told
    /// minimum-cardinality axiom beside a told disjointness clash leaves the
    /// certify face silent, because consistency is not preserved under axiom
    /// addition and a whole module the admission does not cover carries no
    /// certificate — while the clash face, whose refutation over a told subset
    /// condemns every superset, still decides the module inconsistent.
    /// </summary>
    [TestMethod]
    public void Tw16UnadmittedAxiomSilencesCertifyOnly()
    {
        ReasoningModule module = UnadmittedBesideClashModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsFalse(outcome.Consistent, "Tw16 UnadmittedAxiom: the monotone clash face decides the module despite the unadmitted axiom.");
        Assert.StartsWith(DisjointMembership, outcome.ClashReason, StringComparison.Ordinal, "Tw16 UnadmittedAxiom: the clash reason names the disjointness.");
        Assert.IsNull(outcome.CertificateRoute, "Tw16 UnadmittedAxiom: no certificate route on a module the whole-module admission rejects.");

        ContextSaturationStatistics certifyOnly = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ToldGroundWitnessCertify, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(0, certifyOnly.ToldGroundWitnessDeciderCertifications, "Tw16 UnadmittedAxiom: the certify face never fires on a module carrying an unadmitted axiom.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderClashes, "Tw16 UnadmittedAxiom: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderCertifications, "Tw16 UnadmittedAxiom: no certificate on the same module.");
    }

    /// <summary>
    /// Told distinctness is satisfied for free: the corpus premise plus a told
    /// distinctness over its six enumerated members still certifies, because the
    /// construction gives each distinct term its own carrier and the pairwise
    /// check reads off that choice. Injectivity is a free choice of
    /// interpretation rather than a unique-name assumption — it can only cost
    /// completeness, never soundness.
    /// </summary>
    [TestMethod]
    public void Tw17DifferentIndividualsSatisfiedByDistinctCarriers()
    {
        ReasoningModule module = CorpusPremiseModule(Different("UK", "BE", "ES", "FR", "NL", "PT"));
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsTrue(outcome.Consistent, "Tw17 DifferentIndividuals: the pairwise-distinct carriers satisfy the told distinctness.");
        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw17 DifferentIndividuals: the described-model route names the certificate.");
        Assert.AreEqual(7, outcome.Window.CarrierCount, "Tw17 DifferentIndividuals: the distinctness axiom names no new term.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderCertifications, "Tw17 DifferentIndividuals: the certify face's counter reads the decision.");
    }

    /// <summary>
    /// The inverse completion closes a CHAIN: two told inverse pairs sharing a
    /// middle role turn one told edge into three, and both told axioms are
    /// re-checked independently against the finished relation. A completion that
    /// mirrored told edges only would leave the second pair's converse check
    /// failing on a module that has a model.
    /// </summary>
    [TestMethod]
    public void Tw18InverseChainClosesTransitively()
    {
        ReasoningModule module = InverseChainModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.AreEqual(3, outcome.Window.EdgeCount, "Tw18 InverseChain: the one told edge closes to three across the chained pairs.");
        Assert.IsTrue(outcome.Consistent, "Tw18 InverseChain: both converse checks pass against the completed relation.");
        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw18 InverseChain: the described-model route names the certificate.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderCertifications, "Tw18 InverseChain: the certify face's counter reads the decision.");
        Assert.AreEqual(3, totals.ToldGroundWitnessEdgeCount, "Tw18 InverseChain: the completed edge count rides the statistics record.");
    }

    /// <summary>
    /// Two told inverse pairs share their FIRST role: the completion derives one
    /// converse into each partner, so both partners hold the same reversed edge
    /// and each pair's own re-check passes independently. Nothing in the
    /// jurisdiction ever needs the two partners to differ, so the fan-out
    /// certifies rather than silencing.
    /// </summary>
    [TestMethod]
    public void Tw19SharedRoleInversePartnersConverge()
    {
        ReasoningModule module = SharedInversePartnerModule();
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.AreEqual(3, outcome.Window.EdgeCount, "Tw19 SharedRoleInversePartners: the one told edge mirrors into both partners.");
        Assert.IsTrue(outcome.Consistent, "Tw19 SharedRoleInversePartners: both converse checks pass against the completed relation.");
        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw19 SharedRoleInversePartners: the described-model route names the certificate.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderCertifications, "Tw19 SharedRoleInversePartners: the certify face's counter reads the decision.");
    }

    /// <summary>
    /// An <c>owl:imports</c> row is, by the <see cref="ReasoningModule"/> caller
    /// contract, a resolved-closure marker admitted as non-logical, and the
    /// certificate is bit-identical with and without it: the corpus premise
    /// carrying the import certifies through the same described-model route over
    /// the same seven carriers, since an import names no individual. The row
    /// pins the passthrough admission.
    /// </summary>
    [TestMethod]
    public void Tw20ImportMarkerPassesThroughTheCertificate()
    {
        ReasoningModule module = CorpusPremiseModule(Import("http://example.org/imported"));
        ToldGroundWitnessOutcome outcome = ContextToldGroundWitnessDecider.Run(module);

        Assert.IsTrue(outcome.Consistent, "Tw20 ImportMarker: the non-logical import marker leaves the described model certifying.");
        Assert.AreEqual("DescribedModel", outcome.CertificateRoute, "Tw20 ImportMarker: the described-model route names the certificate.");
        Assert.AreEqual(7, outcome.Window.CarrierCount, "Tw20 ImportMarker: an import names no individual, so the premise's own seven carriers stand.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ToldGroundWitnessFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Tw20 ImportMarker: the certify face decides the import-bearing premise at the production ceiling.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Tw20 ImportMarker: the verdict reads consistent off the certificate.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Tw20 ImportMarker: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(1, totals.ToldGroundWitnessDeciderCertifications, "Tw20 ImportMarker: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.ToldGroundWitnessDeciderClashes, "Tw20 ImportMarker: a certified module takes no clash.");
    }

    /// <summary>The corpus premise told exactly as the manifest spells it, with the row's own perturbation appended last.</summary>
    /// <param name="extra">The axioms the row appends.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusPremiseModule(params OwlAxiom[] extra)
    {
        List<OwlAxiom> axioms =
        [
            Declaration(OwlEntityKind.Class, "EuropeanCountry"),
            Declaration(OwlEntityKind.Class, "Person"),
            Declaration(OwlEntityKind.Class, "EUCountry"),
            Declaration(OwlEntityKind.Class, "EuroMP"),
            Declaration(OwlEntityKind.ObjectProperty, "hasEuroMP"),
            Declaration(OwlEntityKind.ObjectProperty, "isEuroMPFrom"),
            Equivalent(Class("EUCountry"), OneOf("UK", "BE", "ES", "FR", "NL", "PT")),
            ClassAssertion(Class("EuropeanCountry"), Individual("UK")),
            ClassAssertion(Class("EuropeanCountry"), Individual("BE")),
            ClassAssertion(Class("EuropeanCountry"), Individual("ES")),
            ClassAssertion(Class("EuropeanCountry"), Individual("FR")),
            ClassAssertion(Class("EuropeanCountry"), Individual("NL")),
            ClassAssertion(Class("EuropeanCountry"), Individual("PT")),
            Domain("hasEuroMP", Class("EUCountry")),
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)),
            ClassAssertion(Class("Person"), Individual("Kinnock")),
            Edge("hasEuroMP", Individual("UK"), Individual("Kinnock")),
        ];
        for(int index = 0; index < extra.Length; index++)
        {
            axioms.Add(extra[index]);
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The domain-forced non-member module: a two-member enumeration defines a class the told domain axiom additionally forces a third, unenumerated element into.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DomainForcedNonMemberModule()
    {
        return Module(
            Equivalent(Class("EUCountry"), OneOf("uk", "be")),
            Domain("hasEuroMP", Class("EUCountry")),
            Edge("hasEuroMP", Individual("outsider"), Individual("kinnock")),
            Different("uk", "be", "outsider"),
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)));
    }

    /// <summary>The pinned-filler module: a class defined by an existential over the inverse role into <c>owl:Thing</c>, told disjoint from a class the edge target is typed with.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ThingFillerDisjointModule()
    {
        return Module(
            Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)),
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Edge("hasEuroMP", Individual("uk"), Individual("kinnock")),
            ClassAssertion(Class("Person"), Individual("kinnock")),
            Disjoint(Class("EuroMP"), Class("Person")),
            Equivalent(Class("Anchor"), OneOf("uk")));
    }

    /// <summary>The repeated-IRI module: two told-disjoint classes typing the same IRI through separately constructed terms, with a self-loop edge carrying the habitat signal so the domain stays a single element.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RepeatedIriModule()
    {
        return Module(
            Disjoint(Class("C"), Class("D")),
            ClassAssertion(Class("C"), Individual("a")),
            ClassAssertion(Class("D"), Individual("a")),
            Edge("p", Individual("a"), Individual("a")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("a")));
    }

    /// <summary>The derived-inverse-domain module: one told forward edge, a told inverse pair, and a domain axiom on the inverse role whose only edge is the mirrored one, clashing with a told-disjoint typing.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DerivedInverseDomainModule()
    {
        return Module(
            Edge("hasEuroMP", Individual("uk"), Individual("kinnock")),
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Domain("isEuroMPFrom", Class("Member")),
            ClassAssertion(Class("Person"), Individual("kinnock")),
            Disjoint(Class("Member"), Class("Person")),
            Equivalent(Class("E"), Some("isEuroMPFrom", Thing)),
            Equivalent(Class("Anchor"), OneOf("uk")));
    }

    /// <summary>The general-left-hand-side module: a subclass axiom from <c>owl:Thing</c> and an equivalence whose defining side is <c>owl:Thing</c>, each demanding the whole domain of its named target.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ThingOnTheLeftModule()
    {
        return Module(
            SubClassOf(Thing, Class("A")),
            Equivalent(Class("B"), Thing),
            Edge("p", Individual("x"), Individual("y")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("x")));
    }

    /// <summary>
    /// The no-skolemization module: a term typed into an existential-defined
    /// class whose successor a model may take fresh, beside a told range
    /// constraint and a disjointness that a witness picked from the told terms
    /// would violate. The module has a model — the witness is a fresh element —
    /// so both faces must stay silent.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NoSkolemizationModule()
    {
        return Module(
            Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)),
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Range("isEuroMPFrom", Class("Country")),
            ClassAssertion(Class("EuroMP"), Individual("kinnock")),
            ClassAssertion(Class("Person"), Individual("blair")),
            Disjoint(Class("Country"), Class("Person")),
            Edge("hasEuroMP", Individual("uk"), Individual("arthur")),
            ClassAssertion(Complement(Class("Retired")), Individual("blair")),
            Equivalent(Class("Anchor"), OneOf("uk")));
    }

    /// <summary>The disagreeing-equivalences module: one class defined twice over, by a two-member and by a one-member enumeration.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DisagreeingEquivalencesModule()
    {
        return Module(
            Equivalent(Class("A"), OneOf("x", "y")),
            Equivalent(Class("A"), OneOf("x")),
            Edge("p", Individual("x"), Individual("y")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)));
    }

    /// <summary>The empty-class assertion module: a told typing with <c>owl:Nothing</c> beside the habitat signal.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NothingAssertionModule()
    {
        return Module(
            ClassAssertion(Nothing, Individual("a")),
            Edge("p", Individual("a"), Individual("a")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("a")));
    }

    /// <summary>The shared-blank-label module: three separately constructed anonymous terms carrying one label, beside a named anchor supplying the nominal mention.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SharedBlankLabelModule()
    {
        return Module(
            ClassAssertion(Class("C"), Anonymous("b")),
            ClassAssertion(Class("D"), Anonymous("b")),
            Edge("p", Anonymous("b"), Anonymous("b")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("anchor")));
    }

    /// <summary>The carrier-window template: the requested number of distinct individuals, the first of them typed into two told-disjoint classes so the module's own arithmetic clashes wherever the window admits it.</summary>
    /// <param name="carriers">The distinct individuals the module names.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule CarrierWindowModule(int carriers)
    {
        List<OwlAxiom> axioms =
        [
            Disjoint(Class("C"), Class("D")),
            ClassAssertion(Class("C"), Individual("m0")),
            ClassAssertion(Class("D"), Individual("m0")),
            Edge("p", Individual("m0"), Individual("m0")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("m0")),
        ];
        for(int index = 1; index < carriers; index++)
        {
            axioms.Add(ClassAssertion(Class("Filler"), Individual("m" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The unadmitted-beside-clash module: a told minimum-cardinality axiom the certify admission rejects, beside a told disjointness the monotone clash face still decides.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UnadmittedBesideClashModule()
    {
        return Module(
            Disjoint(Class("C"), Class("D")),
            ClassAssertion(Class("C"), Individual("a")),
            ClassAssertion(Class("D"), Individual("a")),
            SubClassOf(Class("C"), Min("p", 1)),
            Edge("p", Individual("a"), Individual("a")),
            InverseProperties("p", "q"),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("a")));
    }

    /// <summary>The inverse-chain module: two told inverse pairs sharing a middle role, so one told edge closes to three.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule InverseChainModule()
    {
        return Module(
            InverseProperties("f", "g"),
            InverseProperties("g", "h"),
            Edge("f", Individual("x"), Individual("y")),
            Equivalent(Class("E"), Some("f", Thing)),
            Equivalent(Class("Anchor"), OneOf("x")));
    }

    /// <summary>The shared-partner module: two told inverse pairs sharing their first role, so one told edge mirrors into both partners.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SharedInversePartnerModule()
    {
        return Module(
            InverseProperties("p", "q"),
            InverseProperties("p", "r"),
            Edge("p", Individual("x"), Individual("y")),
            Equivalent(Class("E"), Some("p", Thing)),
            Equivalent(Class("Anchor"), OneOf("x")));
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

    /// <summary>The <c>owl:Thing</c> reference — the universal class the construction pins to the whole domain.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The <c>owl:Nothing</c> reference — the empty class the construction pins to the empty set.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

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
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An anonymous individual — the carrier whose identity is its label rather than an IRI.</summary>
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

    /// <summary>A complement of a class expression — the shape the strict arm's refutation probes spell and the evaluable grammar excludes.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>An existential restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named forward role — the shape the certify admission rejects.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), Filler: null);
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

    /// <summary>A disjoint-classes axiom over the operands.</summary>
    /// <param name="operands">The pairwise-disjoint class expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom([.. operands]) { Origin = Origin("disjoint") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>A told object-property assertion between two individual terms.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="source">The source individual.</param>
    /// <param name="target">The target individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string role, RdfTerm source, RdfTerm target)
    {
        return new OwlObjectPropertyAssertionAxiom(source, new NamedNode(Utf8Strings.From(Example + role)), target) { Origin = Origin("edge") };
    }

    /// <summary>A different-individuals axiom over the named individuals — pairwise distinct.</summary>
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

    /// <summary>A told sameness axiom over two named individuals — the shape the certify admission rejects, since a collapse would merge two carriers the construction keeps apart.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A functionality characteristic over a named role — the ingredient that completes the bijection-chain probe's signal.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(role)) { Origin = Origin("functional") };
    }

    /// <summary>A told inverse between two named object properties.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InverseProperties(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A told object-property domain axiom.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom Domain(string role, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(Property(role), domain) { Origin = Origin("domain") };
    }

    /// <summary>A told object-property range axiom.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="range">The range class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string role, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(role), range) { Origin = Origin("range") };
    }

    /// <summary>An entity declaration — the non-logical passthrough the corpus premise opens with.</summary>
    /// <param name="kind">The declared entity kind.</param>
    /// <param name="local">The entity's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDeclarationAxiom Declaration(OwlEntityKind kind, string local)
    {
        return new OwlDeclarationAxiom(kind, new NamedNode(Utf8Strings.From(Example + local))) { Origin = Origin("declare") };
    }

    /// <summary>An ontology import — the non-logical marker a caller-resolved import closure leaves in the module.</summary>
    /// <param name="ontology">The imported ontology's IRI, told whole rather than in the example namespace.</param>
    /// <returns>The axiom.</returns>
    private static OwlImportAxiom Import(string ontology)
    {
        return new OwlImportAxiom(new NamedNode(Utf8Strings.From(ontology))) { Origin = Origin("import") };
    }
}
