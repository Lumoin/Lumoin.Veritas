using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="ReasoningRendezvous"/>: the expressiveness-floor
/// detection, the strategy rungs and their announcements on the trace bus,
/// the journal-committed materializations, and the description-logic seam's
/// report-or-delegate behaviour for beyond-ceiling modules.
/// </summary>
[TestClass]
internal sealed class ReasoningRendezvousTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    /// <summary>An RDFS-shaped TBox takes the streaming pass and derives the subclass typing.</summary>
    [TestMethod]
    public async Task RdfsShapedTakesTheStreamingPass()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId car = Mint(dictionary, Example + "Car");
        TermId vehicle = Mint(dictionary, Example + "Vehicle");
        TermId myCar = Mint(dictionary, Example + "myCar");

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(car, type, owlClass),
                Triple(vehicle, type, owlClass),
                Triple(car, subClassOf, vehicle),
                Triple(myCar, type, car),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.Rdfs, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.RdfsSufficient, result.Reason);
        Assert.IsTrue(result.IsConsistent);
        Assert.IsGreaterThan(0, result.DerivedCount, "The streaming pass derives the subclass typing.");
        Assert.IsTrue(ContainsTriple(result.Store, Triple(myCar, type, vehicle)), "myCar types as Vehicle in the post-commit store.");
        Assert.HasCount(1, events);
        Assert.AreEqual(ReasoningStrategy.Rdfs, events[0].Strategy);
    }

    /// <summary>A TBox using an RL-but-not-RDFS construct takes the RL closure.</summary>
    [TestMethod]
    public async Task TransitivePropertyTakesTheRlClosure()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId transitive = Mint(dictionary, "http://www.w3.org/2002/07/owl#TransitiveProperty");
        TermId objectProperty = Mint(dictionary, "http://www.w3.org/2002/07/owl#ObjectProperty");
        TermId partOf = Mint(dictionary, Example + "partOf");
        TermId wheel = Mint(dictionary, Example + "wheel");
        TermId car = Mint(dictionary, Example + "car");
        TermId fleet = Mint(dictionary, Example + "fleet");

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(partOf, type, objectProperty),
                Triple(partOf, type, transitive),
                Triple(wheel, partOf, car),
                Triple(car, partOf, fleet),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.Rl, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.RlSufficient, result.Reason);
        Assert.IsTrue(ContainsTriple(result.Store, Triple(wheel, partOf, fleet)), "Transitivity closes wheel partOf fleet.");
        Assert.IsTrue(result.DetectedProfiles.HasFlag(OwlProfiles.Rl));
    }

    /// <summary>A TBox beyond RL is reported when no description-logic delegate is wired — never silently dropped.</summary>
    [TestMethod]
    public async Task BeyondRlIsReportedWithoutADelegate()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.Rl, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlReported, result.Reason);
        Assert.IsNotNull(result.Module);
        Assert.IsNotEmpty(result.Module.Violations, "The module carries the grammar findings.");
        Assert.IsNull(result.ModuleVerdict);
        Assert.IsFalse(result.DetectedProfiles.HasFlag(OwlProfiles.Rl));
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlReported, events[0].Reason);
    }

    /// <summary>A wired delegate receives the beyond-ceiling module and its verdict folds into the result.</summary>
    [TestMethod]
    public async Task BeyondRlDelegatesWhenWired()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningModule? received = null;
        ReasoningRendezvous rendezvous = new(
            ReasoningPolicy.Default,
            (module, _) =>
            {
                received = module;

                return ValueTask.FromResult(ModuleDecision.Decided(
                    new ModuleVerdict(IsConsistent: true, Subsumptions: []),
                    ReasoningDecisionStatistics.Empty));
            });

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.DescriptionLogicDelegate, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, result.Reason);
        Assert.IsNotNull(received);
        Assert.IsNotNull(result.ModuleVerdict);
        Assert.IsTrue(result.IsConsistent);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, result.DecisionOutcome);
        Assert.IsNotNull(result.DecisionStatistics);
    }

    /// <summary>Strategy E: the first classification request pays the build, the second reuses the same generation's result, and the closure's inverted index answers expansion.</summary>
    [TestMethod]
    public async Task ClassificationBuildsThenReuses()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId car = Mint(dictionary, Example + "Car");
        TermId vehicle = Mint(dictionary, Example + "Vehicle");

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(car, type, owlClass),
                Triple(vehicle, type, owlClass),
                Triple(car, subClassOf, vehicle),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        Lumoin.Veritas.Owl.El.ElClassification first = rendezvous.Classify(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);
        Lumoin.Veritas.Owl.El.ElClassification second = rendezvous.Classify(
            store, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        Assert.AreSame(first, second, "The same store generation reuses the classification.");
        Assert.HasCount(2, events);
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationBuilt, events[0].Reason);
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationReused, events[1].Reason);
        Assert.Contains(Utf8Strings.From(Example + "Car"), first.SubsumeesOf(Utf8Strings.From(Example + "Vehicle")), "The inverted index answers type-pattern expansion.");
    }

    /// <summary>The in-library ALC default decides the beyond-ceiling module in-engine: the seam no longer requires an external reasoner for the ALC(H) fragment.</summary>
    [TestMethod]
    public async Task AlcDefaultDelegateDecidesTheBeyondRlModule()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default, AlcModuleReasoner.CreateDelegate());

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.DescriptionLogicDelegate, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, result.Reason);
        Assert.IsNotNull(result.ModuleVerdict);
        Assert.IsTrue(result.IsConsistent, "c1 ⊑ a ∪ b is satisfiable.");
        Assert.IsEmpty(result.ModuleVerdict.UnsupportedConstructs, "The union module sits inside ALC(H).");
    }

    /// <summary>
    /// The SAT-backed sibling is selectable behind the same seam by wiring
    /// <see cref="SatTableauModuleReasoner.CreateDelegate"/> in place of the
    /// snapshot delegate: the beyond-RL module routes through the
    /// description-logic strategy and the SAT engine decides it, naming no
    /// beyond-fragment remainder, exactly as the snapshot default does.
    /// </summary>
    [TestMethod]
    public async Task SatBackedDelegateDecidesTheBeyondRlModule()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default, SatTableauModuleReasoner.CreateDelegate());

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningStrategy.DescriptionLogicDelegate, result.Strategy);
        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, result.Reason);
        Assert.IsNotNull(result.ModuleVerdict);
        Assert.IsTrue(result.IsConsistent, "c1 ⊑ a ∪ b is satisfiable.");
        Assert.IsEmpty(result.ModuleVerdict.UnsupportedConstructs, "The union module sits inside ALC(H).");

        //The SAT engine reports the work it spent, threaded through to the result.
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, result.DecisionOutcome);
        Assert.IsNotNull(result.DecisionStatistics);
        ReasoningDecisionStatistics statistics = result.DecisionStatistics.Value;
        Assert.IsGreaterThan(0, statistics.SolveCount, "The SAT engine ran at least one world solve.");
        Assert.HasCount(statistics.ModuleAxiomCount, result.Module!.Axioms);
    }

    /// <summary>
    /// The seam is the selection point: the snapshot default and the
    /// SAT-backed sibling, wired through the same delegate parameter, route
    /// identically and reach the same verdict over the same beyond-RL module
    /// — the engine choice is search strategy, not answer.
    /// </summary>
    [TestMethod]
    public async Task SnapshotAndSatBackedDelegatesAgreeThroughTheSeam()
    {
        TermDictionary snapshotDictionary = new();
        (HypertrieGraphStore snapshotStore, _) = await BuildBeyondRlStoreAsync(snapshotDictionary).ConfigureAwait(false);
        ReasoningRendezvous snapshotRendezvous = new(ReasoningPolicy.Default, AlcModuleReasoner.CreateDelegate());
        ReasoningResult snapshotResult = await snapshotRendezvous.MaterializeAsync(
            snapshotStore, snapshotDictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        TermDictionary satDictionary = new();
        (HypertrieGraphStore satStore, _) = await BuildBeyondRlStoreAsync(satDictionary).ConfigureAwait(false);
        ReasoningRendezvous satRendezvous = new(ReasoningPolicy.Default, SatTableauModuleReasoner.CreateDelegate());
        ReasoningResult satResult = await satRendezvous.MaterializeAsync(
            satStore, satDictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(snapshotResult.Strategy, satResult.Strategy, "Both engines route through the same strategy.");
        Assert.AreEqual(snapshotResult.Reason, satResult.Reason, "Both engines route through the same reason.");
        Assert.IsNotNull(snapshotResult.ModuleVerdict);
        Assert.IsNotNull(satResult.ModuleVerdict);
        Assert.AreEqual(snapshotResult.ModuleVerdict.IsConsistent, satResult.ModuleVerdict.IsConsistent, "Both engines agree on consistency.");
    }

    /// <summary>A delegated decision emits one <see cref="ReasoningDecisionTraceEvent"/> carrying the outcome and the solver work it spent.</summary>
    [TestMethod]
    public async Task DelegatedDecisionEmitsADecisionTraceEvent()
    {
        TermDictionary dictionary = new();
        (HypertrieGraphStore store, _) = await BuildBeyondRlStoreAsync(dictionary).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default, SatTableauModuleReasoner.CreateDelegate());
        List<ReasoningDecisionTraceEvent> decisionEvents = [];

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store,
            dictionary,
            timeProvider: TimeProvider.System,
            decisionTraceHandler: CollectDecisions(decisionEvents),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, decisionEvents);
        ReasoningDecisionTraceEvent emitted = decisionEvents[0];
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, emitted.Outcome);
        Assert.IsGreaterThan(0, emitted.SolveCount, "The SAT engine ran at least one world solve.");
        Assert.AreEqual(result.Module!.Axioms.Count, emitted.ModuleAxiomCount);
    }

    /// <summary>Floor detection runs once per store generation and is visible through <see cref="ReasoningRendezvous.FloorFor"/>.</summary>
    [TestMethod]
    public async Task FloorDetectsOncePerGeneration()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId car = Mint(dictionary, Example + "Car");
        TermId vehicle = Mint(dictionary, Example + "Vehicle");

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(car, type, owlClass),
                Triple(vehicle, type, owlClass),
                Triple(car, subClassOf, vehicle),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);

        Assert.IsNull(rendezvous.FloorFor(store), "No detection has run yet.");

        await rendezvous.MaterializeAsync(store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningFloor? floor = rendezvous.FloorFor(store);
        Assert.IsNotNull(floor);
        Assert.IsTrue(floor.IsRdfsShaped);
        Assert.IsTrue(floor.IsWithinRl);
        Assert.IsNull(floor.Module);
    }

    /// <summary>An assertion-only commit carries the floor and the classification to the new generation; the statistics never carry.</summary>
    [TestMethod]
    public async Task AssertionOnlyAdvanceCarriesFloorAndClassification()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId car = Mint(dictionary, Example + "Car");
        TermId vehicle = Mint(dictionary, Example + "Vehicle");
        TermId myCar = Mint(dictionary, Example + "myCar");

        EncodedTriple[] schema =
        [
            Triple(car, type, owlClass),
            Triple(vehicle, type, owlClass),
            Triple(car, subClassOf, vehicle),
        ];

        HypertrieGraphStore first = await HypertrieGraphStore.BuildAsync(schema, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        HypertrieGraphStore second = await HypertrieGraphStore.BuildAsync(
            [.. schema, Triple(myCar, type, car)],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        await rendezvous.MaterializeAsync(first, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        ReasoningFloor? detected = rendezvous.FloorFor(first);
        Lumoin.Veritas.Owl.El.ElClassification classification = rendezvous.Classify(
            first, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        rendezvous.Advance(second, [Triple(myCar, type, car)], [], dictionary);

        Assert.AreSame(detected, rendezvous.FloorFor(second), "The floor carries to the new generation.");
        Assert.IsNull(rendezvous.FloorFor(first), "The cache describes one generation at a time.");
        Assert.AreSame(classification, rendezvous.Classify(
            second, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken),
            "The TBox-only classification carries with it.");
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationBuilt, events[0].Reason);
        Assert.AreEqual(ReasoningSelectionReason.ElClassificationReused, events[1].Reason, "The carried classification answers the new generation without a rebuild.");
    }

    /// <summary>A commit touching schema vocabulary invalidates the floor and the classification for re-detection.</summary>
    [TestMethod]
    public async Task SchemaTouchingAdvanceInvalidates()
    {
        TermDictionary dictionary = new();
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId car = Mint(dictionary, Example + "Car");
        TermId vehicle = Mint(dictionary, Example + "Vehicle");
        TermId machine = Mint(dictionary, Example + "Machine");

        EncodedTriple[] schema =
        [
            Triple(car, type, owlClass),
            Triple(vehicle, type, owlClass),
            Triple(car, subClassOf, vehicle),
        ];

        HypertrieGraphStore first = await HypertrieGraphStore.BuildAsync(schema, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        HypertrieGraphStore second = await HypertrieGraphStore.BuildAsync(
            [.. schema, Triple(machine, type, owlClass), Triple(vehicle, subClassOf, machine)],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);
        List<ReasoningTraceEvent> events = [];

        await rendezvous.MaterializeAsync(first, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        rendezvous.Classify(first, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        rendezvous.Advance(second, [Triple(machine, type, owlClass), Triple(vehicle, subClassOf, machine)], [], dictionary);

        Assert.IsNull(rendezvous.FloorFor(second), "Schema vocabulary in the delta invalidates the floor.");
        Assert.IsNull(rendezvous.FloorFor(first));

        rendezvous.Classify(second, dictionary, Collect(events), TimeProvider.System, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(ReasoningSelectionReason.ElClassificationBuilt, events[1].Reason, "The new generation classifies afresh.");
    }

    /// <summary>
    /// A beyond-RL module whose delegated verdict is fragment-relative surfaces
    /// as <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/> with the
    /// excluded remainder carried onto the result. The inverse existential on the
    /// superclass side (<c>A ⊑ ∃r⁻.B</c>) is beyond RL, so it seeds the module,
    /// and beyond ALC(H), so the Alc delegate excludes-and-names it; the fold
    /// keeps the in-engine consistency and the result carries the named remainder.
    /// </summary>
    [TestMethod]
    public async Task FragmentRelativeDelegatedVerdictSurfacesTheRemainder()
    {
        TermDictionary dictionary = new();
        HypertrieGraphStore store = await BuildStoreFromQuadsAsync(dictionary, InverseExistentialSuperclassQuads()).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default, AlcModuleReasoner.CreateDelegate());

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningSelectionReason.BeyondRlDelegated, result.Reason, "The inverse existential on the superclass side is beyond RL, so the module delegates.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, result.DecisionOutcome);
        Assert.IsNotNull(result.ModuleVerdict);
        Assert.IsTrue(result.ModuleVerdict.IsConsistent, "The Alc delegate drops the inverse existential and finds no clash.");
        Assert.IsFalse(result.ModuleVerdict.IsDecisive, "A consistent verdict with a named remainder is scoped to the supported fragment.");
        Assert.IsNotEmpty(result.ModuleVerdict.UnsupportedConstructs);
        Assert.Contains(nameof(OwlSubClassOfAxiom), result.ModuleVerdict.UnsupportedConstructs, "The excluded inverse-existential inclusion is named by its axiom type.");
        Assert.AreSequenceEqual(
            new List<string>(result.ModuleVerdict.UnsupportedConstructs),
            new List<string>(result.UndecidedConstructs),
            "The result carries the verdict's remainder verbatim.");
        Assert.IsTrue(result.IsConsistent, "The fold is unchanged: a consistent fragment-relative verdict leaves the in-engine value.");
    }

    /// <summary>
    /// The recorded mapper-honesty residual. A structure the mapper cannot read
    /// — a malformed <c>owl:Restriction</c> carrying an <c>owl:onProperty</c>
    /// with no constraining facet — maps to no axiom; only the discarded
    /// document's diagnostics record what was wrong. With the readable remainder
    /// RDFS-shaped, the streaming pass carries the reasoning and the dropped
    /// structure is invisible on the result: no beyond-ceiling module, no
    /// verdict, and no <see cref="ReasoningResult.UndecidedConstructs"/> naming
    /// it. The pin flips when a future increment threads mapper diagnostics onto
    /// the reasoning surface.
    /// </summary>
    [TestMethod]
    public async Task MapperDroppedStructureIsInvisibleOnTheResult()
    {
        List<Quad> quads = MalformedRestrictionQuads();

        //The mapper records the malformed restriction as a diagnostic and maps
        //it to no axiom — the dropped structure this pin tracks.
        OwlOntologyDocument document = OwlRdfMapper.Map(quads);
        Assert.IsTrue(document.Diagnostics.HasErrors, "The malformed restriction produces a mapper diagnostic.");

        TermDictionary dictionary = new();
        HypertrieGraphStore store = await BuildStoreFromQuadsAsync(dictionary, quads).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningSelectionReason.RdfsSufficient, result.Reason, "The readable remainder is RDFS-shaped, so the streaming pass carries the reasoning.");
        Assert.IsNull(result.Module, "The dropped structure seeds no beyond-ceiling module.");
        Assert.IsNull(result.ModuleVerdict);
        Assert.IsNull(result.DecisionOutcome);
        Assert.IsEmpty(result.UndecidedConstructs, "The mapper-dropped structure names no undecided construct on the result — its specific diagnostic lives only on the discarded document.");
        Assert.IsTrue(result.IsConsistent);
    }

    /// <summary>
    /// The second shape of the recorded mapper-honesty residual. When the readable remainder is not
    /// RDFS-shaped, a mapper diagnostic collapses the profile check to no memberships with a single
    /// origin-less violation, so the beyond-ceiling module extracted from it seeds no axioms: the result
    /// reports a beyond-ceiling module that exists yet carries nothing of the dropped structure — only the
    /// module's structural violation names anything at all, and
    /// <see cref="ReasoningResult.UndecidedConstructs"/> stays empty. The pin flips when a future increment
    /// threads mapper diagnostics onto the reasoning surface.
    /// </summary>
    [TestMethod]
    public async Task MapperDroppedStructureYieldsAnEmptyBeyondCeilingModule()
    {
        List<Quad> quads = MalformedRestrictionQuads();
        quads.Add(new Quad(Named(Example + "p"), RdfType, Named(OwlSymmetricProperty), Graph: null));

        //The mapper records the malformed restriction as a diagnostic and maps
        //it to no axiom; the symmetric characteristic keeps the readable
        //remainder off the RDFS streaming pass.
        OwlOntologyDocument document = OwlRdfMapper.Map(quads);
        Assert.IsTrue(document.Diagnostics.HasErrors, "The malformed restriction produces a mapper diagnostic.");

        TermDictionary dictionary = new();
        HypertrieGraphStore store = await BuildStoreFromQuadsAsync(dictionary, quads).ConfigureAwait(false);

        ReasoningRendezvous rendezvous = new(ReasoningPolicy.Default);

        ReasoningResult result = await rendezvous.MaterializeAsync(
            store, dictionary, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningSelectionReason.BeyondRlReported, result.Reason, "The diagnostics-bearing document holds no profile membership, so the content reads as beyond RL.");
        Assert.IsNotNull(result.Module, "A beyond-ceiling module is reported.");
        Assert.IsEmpty(result.Module.Axioms, "The origin-less structural violation seeds no axiom — the module names nothing of the dropped structure.");
        Assert.IsNotEmpty(result.Module.Violations, "The structural violation is the only surviving signal of the unreadable content.");
        Assert.IsNull(result.ModuleVerdict);
        Assert.IsNull(result.DecisionOutcome);
        Assert.IsEmpty(result.UndecidedConstructs, "The mapper-dropped structure names no undecided construct on the result.");
        Assert.IsTrue(result.IsConsistent);
    }

    //A TBox with a union on the superclass side — outside the RL grammar:
    //c1 ⊑ (a ∪ b).
    private async Task<(HypertrieGraphStore Store, TermId C1)> BuildBeyondRlStoreAsync(TermDictionary dictionary)
    {
        TermId type = Mint(dictionary, Vocabulary.Rdf.Type.ToString());
        TermId subClassOf = Mint(dictionary, RdfVocabulary.Rdfs.SubClassOf.ToString());
        TermId owlClass = Mint(dictionary, "http://www.w3.org/2002/07/owl#Class");
        TermId unionOf = Mint(dictionary, "http://www.w3.org/2002/07/owl#unionOf");
        TermId first = Mint(dictionary, RdfVocabulary.Rdf.First.ToString());
        TermId rest = Mint(dictionary, RdfVocabulary.Rdf.Rest.ToString());
        TermId nil = Mint(dictionary, RdfVocabulary.Rdf.Nil.ToString());
        TermId c1 = Mint(dictionary, Example + "c1");
        TermId a = Mint(dictionary, Example + "a");
        TermId b = Mint(dictionary, Example + "b");
        TermId union = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("union")));
        TermId list1 = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("list1")));
        TermId list2 = dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("list2")));

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [
                Triple(c1, type, owlClass),
                Triple(a, type, owlClass),
                Triple(b, type, owlClass),
                Triple(c1, subClassOf, union),
                Triple(union, unionOf, list1),
                Triple(list1, first, a),
                Triple(list1, rest, list2),
                Triple(list2, first, b),
                Triple(list2, rest, nil),
            ],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);

        return (store, c1);
    }

    private static TermId Mint(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    private static bool ContainsTriple(HypertrieGraphStore store, EncodedTriple triple)
    {
        foreach(EncodedTriple _ in store.Match(triple.Subject, triple.Predicate, triple.Object))
        {
            return true;
        }

        return false;
    }

    private static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }

    private static TraceHandler<ReasoningTraceEvent> Collect(List<ReasoningTraceEvent> sink)
    {
        return (in ReasoningTraceEvent evt) => sink.Add(evt);
    }

    /// <summary>A trace handler that appends every decision event to a sink, for assertions.</summary>
    /// <param name="sink">The list the handler appends to.</param>
    /// <returns>The handler.</returns>
    private static TraceHandler<ReasoningDecisionTraceEvent> CollectDecisions(List<ReasoningDecisionTraceEvent> sink)
    {
        return (in ReasoningDecisionTraceEvent evt) => sink.Add(evt);
    }

    /// <summary>Builds a store from the graph's quads, minting each term through the dictionary.</summary>
    /// <param name="dictionary">The term dictionary the store's triples encode with.</param>
    /// <param name="quads">The graph's quads.</param>
    /// <returns>The built store.</returns>
    private async Task<HypertrieGraphStore> BuildStoreFromQuadsAsync(TermDictionary dictionary, List<Quad> quads)
    {
        List<EncodedTriple> triples = new(quads.Count);
        foreach(Quad quad in quads)
        {
            triples.Add(Triple(dictionary.GetOrAdd(quad.Subject), dictionary.GetOrAdd(quad.Predicate), dictionary.GetOrAdd(quad.Object)));
        }

        return await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>The graph of <c>A ⊑ ∃r⁻.B</c> — an inverse existential on the superclass side, beyond both the RL grammar and the ALC(H) calculus.</summary>
    /// <returns>The graph's quads.</returns>
    private static List<Quad> InverseExistentialSuperclassQuads()
    {
        BlankNode restriction = new(Utf8Strings.From("restr"));
        BlankNode inverse = new(Utf8Strings.From("inv"));

        return
        [
            new Quad(Named(Example + "A"), RdfType, Named(OwlClass), Graph: null),
            new Quad(Named(Example + "B"), RdfType, Named(OwlClass), Graph: null),
            new Quad(Named(Example + "r"), RdfType, Named(OwlObjectProperty), Graph: null),
            new Quad(Named(Example + "A"), RdfsSubClassOf, restriction, Graph: null),
            new Quad(restriction, RdfType, Named(OwlRestriction), Graph: null),
            new Quad(restriction, Named(OwlOnProperty), inverse, Graph: null),
            new Quad(inverse, Named(OwlInverseOf), Named(Example + "r"), Graph: null),
            new Quad(restriction, Named(OwlSomeValuesFrom), Named(Example + "B"), Graph: null),
        ];
    }

    /// <summary>The graph of a malformed <c>owl:Restriction</c> — an <c>owl:onProperty</c> with no constraining facet — that the mapper reads only as a diagnostic.</summary>
    /// <returns>The graph's quads.</returns>
    private static List<Quad> MalformedRestrictionQuads()
    {
        BlankNode restriction = new(Utf8Strings.From("restr"));

        return
        [
            new Quad(Named(Example + "p"), RdfType, Named(OwlObjectProperty), Graph: null),
            new Quad(Named(Example + "A"), RdfType, Named(OwlClass), Graph: null),
            new Quad(Named(Example + "A"), RdfsSubClassOf, restriction, Graph: null),
            new Quad(restriction, RdfType, Named(OwlRestriction), Graph: null),
            new Quad(restriction, Named(OwlOnProperty), Named(Example + "p"), Graph: null),
        ];
    }

    /// <summary>A named node for the IRI.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>The <c>rdf:type</c> predicate node.</summary>
    private static NamedNode RdfType { get; } = new(Utf8Strings.From(Vocabulary.Rdf.Type.ToString()));

    /// <summary>The <c>rdfs:subClassOf</c> predicate node.</summary>
    private static NamedNode RdfsSubClassOf { get; } = new(Utf8Strings.From(RdfVocabulary.Rdfs.SubClassOf.ToString()));

    private const string OwlClass = "http://www.w3.org/2002/07/owl#Class";
    private const string OwlObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty";
    private const string OwlRestriction = "http://www.w3.org/2002/07/owl#Restriction";
    private const string OwlOnProperty = "http://www.w3.org/2002/07/owl#onProperty";
    private const string OwlInverseOf = "http://www.w3.org/2002/07/owl#inverseOf";
    private const string OwlSomeValuesFrom = "http://www.w3.org/2002/07/owl#someValuesFrom";
    private const string OwlSymmetricProperty = "http://www.w3.org/2002/07/owl#SymmetricProperty";
}
