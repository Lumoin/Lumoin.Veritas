using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Epistemics;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Tests the reasoning outcome surface of the <see cref="VeritasEngine"/> facade across both lanes. On the
/// IMMUTABLE lane an open carries its single reasoning result onto <see cref="VeritasEngine.ReasoningProvenance"/>,
/// the options-wired trace seams observe the same run, and the refusal knob turns a derived inconsistency into a
/// loud failure. On the reasoned MUTABLE lane a configured open materialises the RL closure into the served
/// default graph and MAINTAINS it per commit: queries answer the entailments, each commit refreshes the
/// provenance (a falsity withdraws the overlay and names the rule; removing the falsity restores it), the refusal
/// knob vetoes an inconsistent commit pre-append while the property stays at the last landed generation, a
/// beyond-RL verdict decays to fragment-relative past its decided generation, a reasoned reopen serves the
/// closure from the first query, and a cancelled commit leaves the base untouched. A mutable open with reasoning
/// UNWIRED serves the asserted graph with no maintenance and a <see langword="null"/> provenance, byte-identical
/// to before.
/// </summary>
[TestClass]
internal sealed class VeritasEngineReasoningSurfaceTests
{
    /// <summary>The example-namespace prefix the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdfs:subClassOf</c> IRI.</summary>
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The <c>owl:equivalentClass</c> IRI.</summary>
    private const string OwlEquivalentClass = "http://www.w3.org/2002/07/owl#equivalentClass";

    /// <summary>The <c>owl:disjointWith</c> IRI.</summary>
    private const string OwlDisjointWith = "http://www.w3.org/2002/07/owl#disjointWith";

    /// <summary>The <c>owl:Class</c> IRI.</summary>
    private const string OwlClass = "http://www.w3.org/2002/07/owl#Class";

    /// <summary>The <c>owl:ObjectProperty</c> IRI.</summary>
    private const string OwlObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty";

    /// <summary>The <c>owl:Restriction</c> IRI.</summary>
    private const string OwlRestriction = "http://www.w3.org/2002/07/owl#Restriction";

    /// <summary>The <c>owl:onProperty</c> IRI.</summary>
    private const string OwlOnProperty = "http://www.w3.org/2002/07/owl#onProperty";

    /// <summary>The <c>owl:inverseOf</c> IRI.</summary>
    private const string OwlInverseOf = "http://www.w3.org/2002/07/owl#inverseOf";

    /// <summary>The <c>owl:someValuesFrom</c> IRI.</summary>
    private const string OwlSomeValuesFrom = "http://www.w3.org/2002/07/owl#someValuesFrom";

    /// <summary>The <c>owl:unionOf</c> IRI.</summary>
    private const string OwlUnionOf = "http://www.w3.org/2002/07/owl#unionOf";

    /// <summary>The <c>owl:oneOf</c> IRI.</summary>
    private const string OwlOneOf = "http://www.w3.org/2002/07/owl#oneOf";

    /// <summary>The <c>rdf:first</c> IRI.</summary>
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";

    /// <summary>The <c>rdf:rest</c> IRI.</summary>
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";

    /// <summary>The <c>rdf:nil</c> IRI.</summary>
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>The RL falsity rule a shared instance of two disjoint classes fires.</summary>
    private const string CaxDwRule = "cax-dw";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ConsistentRlOpenSurfacesTheReasoningProvenance()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "An immutable reasoned open surfaces the reasoning outcome on the facade.");
        Assert.IsTrue(provenance.IsConsistent, "The within-RL graph derives no contradiction, so the outcome is consistent.");
        Assert.IsGreaterThan(0, provenance.DerivedCount, "The RL closure derived at least one entailed triple.");
        Assert.AreEqual(ReasoningStrategy.Rl, provenance.Strategy, "An equivalent-class axiom takes the content off the RDFS streaming pass onto the RL closure.");
        Assert.AreEqual(ReasoningSelectionReason.RlSufficient, provenance.Reason, "The content is within the RL profile, so the RL closure answers completely.");
        Assert.IsEmpty(provenance.UndecidedConstructs, "A within-profile outcome excludes no construct.");
        Assert.IsTrue(provenance.IsDecisive, "An in-engine decision over within-profile content covers it whole.");
    }

    [TestMethod]
    public async Task RlInconsistentOpenServesThePartialClosureAndSurfacesTheInconsistency()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(DisjointInconsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The default serves the partial closure: the asserted membership is still queryable.
        bool servesAsserted = await database
            .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}x> <{RdfType}> <{Ex}A> }}"), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(servesAsserted, "An inconsistent open still serves the asserted partial closure by default.");

        //And the inconsistency is visible on the facade rather than dying at the engine boundary.
        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "The inconsistent open surfaces its outcome on the facade.");
        Assert.IsFalse(provenance.IsConsistent, "A shared instance of two disjoint classes is inconsistent.");
        Assert.AreEqual(CaxDwRule, provenance.InconsistencyRule, "The class-disjointness falsity rule is named on the outcome.");
        Assert.IsTrue(provenance.IsDecisive, "A derived inconsistency condemns the content whole, so it reads decisive.");
    }

    [TestMethod]
    public async Task BeyondRlContextDecidedWholeOutcomeReachesTheFacade()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(UnionSuperclassAssertionGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //A union superclass is beyond RL and outside the EL fragment, so the context tier of
        //ElCoupled(ContextSaturation(SatBacked)) is the arm that decides it, and its whole decision surfaces on
        //the facade — the decision outcome, the empty remainder, the tier's own totals and the module's size.
        AssertContextDecidedWholeFace(database.ReasoningProvenance);
        Assert.IsGreaterThan(0, database.ReasoningProvenance!.ModuleAxiomCount, "The context-decided module carries axioms, whose count reaches the facade.");
    }

    [TestMethod]
    public async Task BeyondRlElDecidedWholeOutcomeReachesTheFacade()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(InverseExistentialAssertionGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //The route counterpart of the context-decided face: an inverse existential in class-assertion position is
        //beyond RL and inside the EL survey's admitted set, so the EL fast-path is the arm that decides it. The
        //row pins the TIER as well as the verdict — a shape that moved between saturation arms would keep a
        //whole-decision face while changing which engine produced it, and that change is what this asserts.
        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "The EL-decided open surfaces its outcome on the facade.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, provenance.DecisionOutcome, "The EL fast-path decides the assertion-position inverse existential whole.");
        Assert.IsTrue(provenance.IsConsistent, "x gains its forced r-predecessor in C, so the module is consistent.");
        Assert.IsEmpty(provenance.UndecidedConstructs, "A whole EL decision names no undecided construct.");
        Assert.IsTrue(provenance.IsDecisive, "A whole EL decision reads as covering the content whole.");
        Assert.IsNotNull(provenance.DecisionStatistics, "A delegated EL decision carries its work statistics to the facade.");
        Assert.IsTrue(provenance.DecisionStatistics.Value.ElTotals.ElDecided, "The EL saturation fast-path produced the verdict.");
        Assert.IsFalse(provenance.DecisionStatistics.Value.ContextTotals.ContextDecided, "The EL arm answers ahead of the context tier, which never runs.");
        Assert.AreEqual(0, provenance.DecisionStatistics.Value.SolveCount, "An EL decision runs no SAT solves, so the solver totals are empty.");
    }

    [TestMethod]
    public async Task ContextDecidedWholeReachesBothLanesWithEmptySolverTotals()
    {
        //The three-tier delegate is wired at both VeritasEngine call sites — the immutable-open lane and the
        //reasoned mutable-open lane. Driving one context-decided module through BOTH asserts each lane carries
        //the same face: whole context decision, ContextDecided true, empty solver totals. A lane wired to a
        //different delegate, or to none, reads a different face here.
        VeritasEngine immutable = await VeritasEngine
            .OpenAsync(UnionSuperclassAssertionGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(immutable.ConfigureAwait(false))
        {
            AssertContextDecidedWholeFace(immutable.ReasoningProvenance);
        }

        VeritasEngine mutable = await VeritasEngine
            .OpenMutableAsync(UnionSuperclassAssertionGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(mutable.ConfigureAwait(false))
        {
            AssertContextDecidedWholeFace(mutable.ReasoningProvenance);
        }
    }

    [TestMethod]
    public async Task UnwiredReasoningLeavesTheReasoningProvenanceNull()
    {
        VeritasEngineOptions noReasoning = VeritasEngineOptions.Default with { Reasoning = null };
        VeritasEngine database = await VeritasEngine
            .OpenAsync(WithinRlConsistentGraph(), noReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNull(database.ReasoningProvenance, "An open with reasoning unwired ran no reasoning, so there is no outcome to surface.");
    }

    [TestMethod]
    public async Task UnwiredMutableOpenServesAssertedOnlyWithNullProvenance()
    {
        //Reasoning unwired: the mutable engine is byte-identical to before this increment — no maintenance seam,
        //no served overlay, a null provenance.
        VeritasEngineOptions noReasoning = VeritasEngineOptions.Default with { Reasoning = null };
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(WithinRlConsistentGraph(), noReasoning, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNull(database.ReasoningProvenance, "An unwired mutable open runs no reasoning, so no outcome is surfaced.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The unwired engine serves the asserted graph.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The unwired engine materialises no entailment, so the subclass entailment is not served.");
    }

    [TestMethod]
    public async Task WiredReasoningTraceReceivesTheSelectedStrategy()
    {
        List<ReasoningTraceEvent> events = [];
        VeritasEngineOptions traced = VeritasEngineOptions.Default with { ReasoningTrace = Collect(events) };

        VeritasEngine database = await VeritasEngine
            .OpenAsync(WithinRlConsistentGraph(), traced, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNotEmpty(events, "A wired reasoning trace receives the materialisation's strategy-selection event.");
        Assert.AreEqual(database.ReasoningProvenance!.Strategy, events[0].Strategy, "The traced strategy is the one the facade surfaced.");
        Assert.AreNotEqual(System.Guid.Empty, events[0].CorrelationId, "A traced run mints a fresh correlation id.");
    }

    [TestMethod]
    public async Task WiredReasoningDecisionTraceReceivesTheDelegatedDecision()
    {
        List<ReasoningDecisionTraceEvent> events = [];
        VeritasEngineOptions traced = VeritasEngineOptions.Default with { ReasoningDecisionTrace = CollectDecisions(events) };

        VeritasEngine database = await VeritasEngine
            .OpenAsync(InverseExistentialAssertionGraph(), traced, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.HasCount(1, events, "Exactly one beyond-RL module is delegated, so exactly one decision event is emitted.");
        Assert.AreEqual(database.ReasoningProvenance!.DecisionOutcome, events[0].Outcome, "The traced decision outcome matches the one the facade surfaced.");
    }

    [TestMethod]
    public void DefaultOptionsWireNoReasoningTraceHandlers()
    {
        Assert.IsNull(VeritasEngineOptions.Default.ReasoningTrace, "The default options wire no reasoning trace handler, so tracing costs nothing.");
        Assert.IsNull(VeritasEngineOptions.Default.ReasoningDecisionTrace, "The default options wire no reasoning decision trace handler.");
    }

    [TestMethod]
    public void DefaultOptionsCarryTheEmptyEpistemicReasonRegistry()
    {
        Assert.AreSame(EpistemicReasonRegistry.Empty, VeritasEngineOptions.Default.EpistemicReasons, "The epistemic-surface seam is dark by default: the default options carry the shared empty registry, zero per-query overhead.");
    }

    [TestMethod]
    public async Task RefuseInconsistentThrowsOnADerivedInconsistency()
    {
        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };

        ReasoningInconsistencyException refusal = await Assert
            .ThrowsExactlyAsync<ReasoningInconsistencyException>(async () => await VeritasEngine
                .OpenAsync(DisjointInconsistentGraph(), refusing, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);

        Assert.IsNotNull(refusal.Provenance, "The refusal carries the reasoning outcome the served database would otherwise have exposed.");
        Assert.IsFalse(refusal.Provenance.IsConsistent, "The refused open's outcome is inconsistent.");
        Assert.AreEqual(CaxDwRule, refusal.Provenance.InconsistencyRule, "The refusal names the falsity rule that fired.");
    }

    [TestMethod]
    public async Task RefuseInconsistentOpensNormallyOnAConsistentGraph()
    {
        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };

        VeritasEngine database = await VeritasEngine
            .OpenAsync(WithinRlConsistentGraph(), refusing, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNotNull(database.ReasoningProvenance, "A consistent open under the refusal knob opens normally and surfaces its outcome.");
        Assert.IsTrue(database.ReasoningProvenance.IsConsistent, "The consistent open's outcome is consistent.");
    }

    [TestMethod]
    public async Task RefuseInconsistentDoesNotThrowOnAWholeConsistentContextDecision()
    {
        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };

        //A union superclass is a WHOLE consistent context decision, not a decided inconsistency, so the refusal
        //knob leaves it alone and the open surfaces the whole-module face.
        VeritasEngine database = await VeritasEngine
            .OpenAsync(UnionSuperclassAssertionGraph(), refusing, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsNotNull(database.ReasoningProvenance, "The whole-consistent context-decided open opens normally under the refusal knob.");
        Assert.IsTrue(database.ReasoningProvenance.IsConsistent, "The context decision is consistent.");
        Assert.IsEmpty(database.ReasoningProvenance.UndecidedConstructs, "A whole context decision names no remainder.");
        Assert.IsTrue(database.ReasoningProvenance.IsDecisive, "A whole context decision reads as covering the content whole.");
    }

    [TestMethod]
    public void RefuseInconsistentDefaultsFalse()
    {
        Assert.IsFalse(ReasoningConfiguration.Default.RefuseInconsistent, "The default reasoning configuration serves the partial closure rather than refusing.");
    }

    [TestMethod]
    public async Task ReasonedMutableOpenServesEntailmentsImmediately()
    {
        //Reasoning is wired by the default options, so a reasoned mutable open materialises the RL closure into the
        //served default graph AT OPEN — the first query answers the entailments, not just the asserted graph.
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The asserted membership is served.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The subclass entailment is served from the first query.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "The equivalent-class entailment is served from the first query.");

        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "A reasoned mutable open surfaces the reasoning outcome on the facade.");
        Assert.IsTrue(provenance.IsConsistent, "The within-RL graph derives no contradiction.");
        Assert.IsGreaterThan(0, provenance.DerivedCount, "The served closure reports its derived-set size.");
    }

    [TestMethod]
    public async Task PerCommitProvenanceWithdrawsThenRestoresTheOverlay()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(ConsistentDisjointWithEntailmentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The entailment is served on the consistent open.");
        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The open is consistent.");

        //Introduce a falsity: x becomes a shared instance of two disjoint classes, so the maintained commit turns
        //inconsistent, withdraws the overlay, and serves the asserted graph.
        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}x> <{RdfType}> <{Ex}B> }}").ConfigureAwait(false);

        ReasoningProvenance? inconsistent = database.ReasoningProvenance;
        Assert.IsNotNull(inconsistent, "The commit refreshed the provenance.");
        Assert.IsFalse(inconsistent.IsConsistent, "The disjoint membership is a decided inconsistency.");
        Assert.AreEqual(CaxDwRule, inconsistent.InconsistencyRule, "The class-disjointness falsity rule is named on the refreshed outcome.");
        Assert.AreEqual(0, inconsistent.DerivedCount, "An overlay-withdrawn generation reports zero derived triples, matching what it serves.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The overlay withdrew, so the derived entailment is no longer served.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The asserted graph is still served under the withdrawn overlay.");

        //Remove the falsity premise: the maintained rebuild restores consistency and the overlay returns.
        await UpdateAsync(database, $"DELETE DATA {{ <{Ex}x> <{RdfType}> <{Ex}B> }}").ConfigureAwait(false);

        ReasoningProvenance? restored = database.ReasoningProvenance;
        Assert.IsNotNull(restored, "The restoring commit refreshed the provenance.");
        Assert.IsTrue(restored.IsConsistent, "Removing the falsity premise restores consistency.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The overlay returned, so the entailment is served again.");
    }

    [TestMethod]
    public async Task WithdrawalFlipKeepsTheAssertedFormerlyDerivedFactAnswerable()
    {
        //A withdrawal flip that ALSO asserts a previously-derived fact keeps it answerable — the
        //priorOverlay ∩ baseAdded overlap shape, observed through query answers. The base derives
        //rex a Animal (rex a Dog, Dog ⊑ Animal) and whiskers a Feline (whiskers a Cat, Cat ≡ Feline),
        //with Animal disjointWith Reptile standing dormant.
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(AssertedFormerlyDerivedFlipGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "rex a Animal is a served derivation on the consistent open.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "whiskers a Feline is a served derivation on the consistent open.");
        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The open is consistent.");

        //One update BOTH asserts the previously-derived rex a Animal AND introduces the falsity
        //rex a Reptile (Reptile disjoint with Animal): cax-dw fires, the overlay withdraws. rex a Animal
        //is ASSERTED now, so it stays answerable through the withdrawal; the purely-derived
        //whiskers a Feline stops answering.
        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> . <{Ex}rex> <{RdfType}> <{Ex}Reptile> }}").ConfigureAwait(false);

        Assert.IsFalse(database.ReasoningProvenance!.IsConsistent, "The shared disjoint membership withdraws the overlay.");
        Assert.AreEqual(CaxDwRule, database.ReasoningProvenance!.InconsistencyRule, "The class-disjointness falsity rule is named on the withdrawal.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The asserted-was-derived fact stays answerable through the withdrawal — it is asserted now.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "The purely-derived fact stops answering under the withdrawn overlay.");

        //Removing the falsity restores consistency: the overlay returns and the purely-derived fact answers again.
        await UpdateAsync(database, $"DELETE DATA {{ <{Ex}rex> <{RdfType}> <{Ex}Reptile> }}").ConfigureAwait(false);

        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "Removing the falsity restores consistency.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "The overlay returned, so the purely-derived fact answers again.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "rex a Animal remains answerable (asserted, and derivable again under the returned overlay).");
    }

    [TestMethod]
    public async Task RefuseInconsistentVetoesTheCommitAndKeepsTheLastLandedGeneration()
    {
        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(ConsistentDisjointWithEntailmentGraph(), refusing, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The consistent open lands under the refusal knob.");

        //The refusal veto: the maintained commit turns inconsistent, so the delegate throws pre-append and the
        //commit never linearises. The exception carries the POST-OP provenance; the property stays at the last
        //landed (consistent) generation — the published property and the refusal's carried provenance are distinct
        //objects.
        ReasoningInconsistencyException refusal = await Assert
            .ThrowsExactlyAsync<ReasoningInconsistencyException>(async () => await database
                .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}x> <{RdfType}> <{Ex}B> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false))
            .ConfigureAwait(false);

        Assert.IsNotNull(refusal.Provenance, "The refusal carries the post-op provenance.");
        Assert.IsFalse(refusal.Provenance.IsConsistent, "The refused commit's outcome is inconsistent.");
        Assert.AreEqual(CaxDwRule, refusal.Provenance.InconsistencyRule, "The refusal names the fired falsity rule.");

        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The property stays at the last landed (consistent) generation, distinct from the refusal's post-op provenance.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}x> <{RdfType}> <{Ex}B> }}").ConfigureAwait(false), "The refused commit never linearised, so the base is unchanged.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The last landed overlay still serves the entailment.");

        //The next commit rebuilds from the committed base (the discarded instance is rebuilt) and lands fine.
        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}pluto> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false);

        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The rebuild after the refusal is consistent and lands.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}pluto> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The rebuilt closure serves the new instance's entailment.");
    }

    [TestMethod]
    public async Task RefuseInconsistentFalseInconsistentCommitLandsAndServesAsserted()
    {
        //The default RefuseInconsistent=false: an inconsistent commit LANDS, withdraws the overlay, and serves the
        //asserted graph — mirroring the immutable facade's serve-the-partial-closure behaviour.
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(ConsistentDisjointWithEntailmentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}x> <{RdfType}> <{Ex}B> }}").ConfigureAwait(false);

        Assert.IsFalse(database.ReasoningProvenance!.IsConsistent, "The inconsistent commit landed and surfaced its inconsistency.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}x> <{RdfType}> <{Ex}B> }}").ConfigureAwait(false), "The inconsistent commit landed, so its asserted triple is served.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}x> <{RdfType}> <{Ex}A> }}").ConfigureAwait(false), "The prior asserted membership is still served.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The overlay withdrew, so no derived triple is served.");
    }

    [TestMethod]
    public async Task WholeConsistentContextDecisionDoesNotRefuseOnTheMutableLane()
    {
        //A union superclass is a WHOLE consistent context decision, so the per-commit refusal veto leaves the
        //reasoned mutable open alone — the same fold the immutable lane applies, through the wired three-tier
        //delegate.
        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(UnionSuperclassAssertionGraph(), refusing, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "The whole-consistent context-decided open lands under the refusal knob.");
        Assert.IsTrue(provenance.IsConsistent, "The context decision is consistent.");
        Assert.IsEmpty(provenance.UndecidedConstructs, "A whole context decision names no remainder.");
        Assert.IsTrue(provenance.IsDecisive, "A whole context decision reads as covering the content whole.");
    }

    [TestMethod]
    public async Task BeyondRlDecayInheritsTheContextDecisionAndReadsWhole()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(UnionSuperclassAssertionGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ReasoningProvenance? opened = database.ReasoningProvenance;
        Assert.IsNotNull(opened, "The beyond-RL open surfaces a context decision.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, opened.DecisionOutcome, "The open decides the union superclass whole through the context tier.");
        Assert.IsEmpty(opened.UndecidedConstructs, "A whole context decision names no remainder.");
        Assert.IsTrue(opened.IsDecisive, "The open reads as covering the content whole.");

        //An assertion-only commit does not re-decide the standing beyond-RL module: the provenance INHERITS the
        //last-landed whole decision outcome unchanged.
        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}foo> <{Ex}bar> <{Ex}baz> }}").ConfigureAwait(false);

        ReasoningProvenance? decayed = database.ReasoningProvenance;
        Assert.IsNotNull(decayed, "The assertion-only commit refreshed the provenance.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decayed.DecisionOutcome, "The decayed generation inherits the last-landed whole decision outcome unchanged.");
        Assert.IsEmpty(decayed.UndecidedConstructs, "The decayed generation carries no remainder.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}foo> <{Ex}bar> <{Ex}baz> }}").ConfigureAwait(false), "The asserted triple committed.");
    }

    [TestMethod]
    public async Task RefuseInconsistentDoesNotRefuseABeyondBothFragmentRelativeConsistency()
    {
        //PF10 — the beyond-BOTH-tiers module (inverse existential in assertion position PLUS a nominal
        //enumeration superclass): the multi-individual one-of is not context-admitted and not EL, so both
        //saturation tiers decline and the inverse-blind SAT oracle returns a fragment-relative consistent
        //verdict with the excluded constructs named. It keeps the fragment-relative refuse-knob coverage
        //the flipped inverse-existential and positive-union pins lose.
        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };
        VeritasEngine database = await VeritasEngine
            .OpenAsync(InverseForcedBeyondBothGraph(), refusing, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, "A fragment-relative-consistent open opens normally under the refusal knob.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, provenance.DecisionOutcome, "The beyond-both module is decided fragment-relative by the inverse-blind SAT oracle.");
        Assert.IsTrue(provenance.IsConsistent, "The fragment-relative verdict is consistent.");
        Assert.IsNotEmpty(provenance.UndecidedConstructs, "The excluded inverse-existential inclusion is named as the remainder.");
        Assert.IsFalse(provenance.IsDecisive, "A fragment-relative consistency claim never reads as covering the content whole.");
    }

    [TestMethod]
    public async Task BeyondBothDecayInheritsFragmentRelative()
    {
        //PF10 — the fragment-relative decay coverage on the beyond-both module, kept alive past the flip.
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(InverseForcedBeyondBothGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        ReasoningProvenance? opened = database.ReasoningProvenance;
        Assert.IsNotNull(opened, "The beyond-both open surfaces a delegated decision.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, opened.DecisionOutcome, "The open decides the beyond-both module fragment-relative.");
        Assert.IsNotEmpty(opened.UndecidedConstructs, "The excluded inverse-existential inclusion is named.");

        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}foo> <{Ex}bar> <{Ex}baz> }}").ConfigureAwait(false);

        ReasoningProvenance? decayed = database.ReasoningProvenance;
        Assert.IsNotNull(decayed, "The assertion-only commit refreshed the provenance.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decayed.DecisionOutcome, "The decayed generation inherits the last-landed fragment-relative outcome unchanged.");
        Assert.IsNotEmpty(decayed.UndecidedConstructs, "The decayed generation inherits the named remainder.");
        Assert.IsFalse(decayed.IsDecisive, "A decayed beyond-RL verdict reads fragment-relative, never a phantom whole-module claim.");
    }

    [TestMethod]
    public async Task NamedGraphOnlyUpdateKeepsEntailmentsServed()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The default-graph entailment is served.");
        int derivedBefore = database.ReasoningProvenance!.DerivedCount;

        //A named-graph-only commit carries no default-graph delta, so maintenance is skipped and the served default
        //store and reasoning payload carry forward by reference — the entailments stay served.
        await UpdateAsync(database, $"INSERT DATA {{ GRAPH <{Ex}g> {{ <{Ex}a> <{Ex}b> <{Ex}c> }} }}").ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The default-graph entailment is still served after the named-graph commit.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ GRAPH <{Ex}g> {{ <{Ex}a> <{Ex}b> <{Ex}c> }} }}").ConfigureAwait(false), "The named-graph triple committed.");
        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The provenance stays consistent across the named-graph commit.");
        Assert.AreEqual(derivedBefore, database.ReasoningProvenance!.DerivedCount, "The named-graph commit carries the same served generation, so the derived count is unchanged.");
    }

    [TestMethod]
    public async Task DeleteAssertedButDerivableTripleStaysAnswerable()
    {
        //An asserted triple that is ALSO derivable: deleting it removes it from the asserted store while the closure
        //rederives it, so the served store keeps the fact and queries still answer it (D-UPDATE-READS accounting).
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(AssertedAndDerivableGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The asserted-and-derivable membership is served.");

        await UpdateAsync(database, $"DELETE DATA {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false);
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The closure rederives the fact from the still-standing subclass support, so the query still answers it.");

        //Remove the derivation support: only now — with the asserted copy already gone AND the derivation gone — does
        //the fact leave the served store, which proves the DELETE DATA removed the asserted copy.
        await UpdateAsync(database, $"DELETE DATA {{ <{Ex}Dog> <{RdfsSubClassOf}> <{Ex}Animal> }}").ConfigureAwait(false);
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "With the asserted copy deleted and the derivation support gone, the fact is no longer served.");
    }

    [TestMethod]
    public async Task WholesaleReplaceRebuildsAndServesTheNewClosure()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "The opened closure serves the equivalent-class entailment.");

        //CLEAR of the whole default graph is a wholesale replacement (the net retract covers the entire asserted
        //default graph), which rebuilds the closure from the tentative base rather than feeding a degenerate apply.
        await UpdateAsync(database, "CLEAR DEFAULT").ConfigureAwait(false);
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "The wholesale replace cleared the default graph, so the old entailment is gone.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The asserted graph was cleared too.");

        //A subsequent maintained commit serves the new closure correctly.
        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}Cat> <{OwlEquivalentClass}> <{Ex}Feline> . <{Ex}tom> <{RdfType}> <{Ex}Cat> }}").ConfigureAwait(false);
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}tom> <{RdfType}> <{Ex}Feline> }}").ConfigureAwait(false), "The new equivalent-class closure is served after the rebuild.");
        Assert.IsTrue(database.ReasoningProvenance!.IsConsistent, "The rebuilt closure is consistent.");
    }

    [TestMethod]
    public async Task ReasonedReopenServesEntailmentsFromTheFirstQuery()
    {
        string storeDirectory = Directory.CreateTempSubdirectory("veritas-reasoned-reopen-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            //Persist a reasoned mutable engine's committed generation (the ASSERTED graph — entailments are not
            //persisted), then dispose it.
            {
                VeritasEngine mutable = await VeritasEngine
                    .OpenMutableAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
                    .ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                mutable.Persist(store);
            }

            //Reopen the store with reasoning configured: the closure is rebuilt at open over the recovered asserted
            //base, so entailments are served from the FIRST query, not from the first maintained commit.
            VeritasEngine reopened = await VeritasEngine
                .OpenMutableAsync(store, cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);

            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The recovered asserted membership is served.");
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The reopened engine serves the entailment from the first query.");
            Assert.IsNotNull(reopened.ReasoningProvenance, "The reasoned reopen surfaces its provenance.");
            Assert.IsTrue(reopened.ReasoningProvenance.IsConsistent, "The recovered base is consistent.");
        }
        finally
        {
            Directory.Delete(storeDirectory, true);
        }
    }

    [TestMethod]
    public async Task CancelledCommitLeavesTheBaseAndTheNextCommitServesCorrectly()
    {
        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //A cancelled token fails the update pre-append (D-CANCEL — cancellation is observed before the linearising
        //append), so the base is untouched.
        bool cancelled = false;
        using(CancellationTokenSource cts = new())
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await database
                    .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}pluto> <{RdfType}> <{Ex}Dog> }}"), cancellationToken: cts.Token)
                    .ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
                cancelled = true;
            }
        }

        Assert.IsTrue(cancelled, "The cancelled update fails.");
        Assert.IsFalse(await AskAsync(database, $"ASK {{ <{Ex}pluto> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The cancelled update never linearised, so the base is unchanged.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}rex> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The served closure is intact after the cancelled update.");

        //The next update succeeds and serves the closure correctly (a rebuild if the cancelled commit had
        //invalidated the maintenance; an incremental otherwise — either way the served store is correct).
        await UpdateAsync(database, $"INSERT DATA {{ <{Ex}pluto> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false);
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}pluto> <{RdfType}> <{Ex}Dog> }}").ConfigureAwait(false), "The next update commits its triple.");
        Assert.IsTrue(await AskAsync(database, $"ASK {{ <{Ex}pluto> <{RdfType}> <{Ex}Animal> }}").ConfigureAwait(false), "The next commit serves the new instance's entailment correctly.");
    }

    /// <summary>Asks a boolean query over the database with the test's cancellation token.</summary>
    /// <param name="database">The database to query.</param>
    /// <param name="ask">The ASK query text.</param>
    /// <returns>The boolean answer.</returns>
    private async Task<bool> AskAsync(VeritasEngine database, string ask)
    {
        return await database.AskAsync(Utf8Strings.From(ask), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes a SPARQL update over the database with the test's cancellation token.</summary>
    /// <param name="database">The database to update.</param>
    /// <param name="update">The update text.</param>
    /// <returns>The asynchronous update.</returns>
    private async Task UpdateAsync(VeritasEngine database, string update)
    {
        await database.UpdateAsync(Utf8Strings.From(update), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asserts the whole-module context-decision face on a facade provenance: a whole <see cref="ReasoningDecisionOutcome.Decided"/> outcome, no named remainder, decisive, produced by the context saturation tier with empty solver totals.</summary>
    /// <param name="provenance">The facade provenance to check.</param>
    private static void AssertContextDecidedWholeFace(ReasoningProvenance? provenance)
    {
        Assert.IsNotNull(provenance, "The context-decided open surfaces its outcome on the facade.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, provenance.DecisionOutcome, "The context tier decides the module whole, past the inverse-blind ALC fallback.");
        Assert.IsEmpty(provenance.UndecidedConstructs, "A whole context decision names no undecided construct.");
        Assert.IsTrue(provenance.IsConsistent, "The context decision is consistent.");
        Assert.IsTrue(provenance.IsDecisive, "A whole context decision reads as covering the content whole.");
        Assert.IsNotNull(provenance.DecisionStatistics, "A delegated context decision carries its work statistics to the facade.");
        Assert.IsTrue(provenance.DecisionStatistics.Value.ContextTotals.ContextDecided, "The context saturation tier produced the verdict.");
        Assert.AreEqual(0, provenance.DecisionStatistics.Value.SolveCount, "A context decision runs no SAT solves, so the solver totals are empty.");
    }

    /// <summary>A directory durability barrier that does nothing, so the persistence store side does not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>A trace handler that appends every strategy-selection event to a sink, for assertions.</summary>
    /// <param name="sink">The list the handler appends to.</param>
    /// <returns>The handler.</returns>
    private static TraceHandler<ReasoningTraceEvent> Collect(List<ReasoningTraceEvent> sink)
    {
        return (in ReasoningTraceEvent evt) => sink.Add(evt);
    }

    /// <summary>A trace handler that appends every delegated-decision event to a sink, for assertions.</summary>
    /// <param name="sink">The list the handler appends to.</param>
    /// <returns>The handler.</returns>
    private static TraceHandler<ReasoningDecisionTraceEvent> CollectDecisions(List<ReasoningDecisionTraceEvent> sink)
    {
        return (in ReasoningDecisionTraceEvent evt) => sink.Add(evt);
    }

    /// <summary>
    /// A consistent graph within the OWL 2 RL profile but off the RDFS streaming pass: a subclass hierarchy with
    /// an asserted instance (an RDFS entailment) plus an equivalent-class axiom with its own instance (an RL
    /// entailment), so the RL closure is selected and derives at least one triple.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> WithinRlConsistentGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfsSubClassOf), Iri(Ex + "Animal")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Dog")),
            new DataTriple(Iri(Ex + "Cat"), Iri(OwlEquivalentClass), Iri(Ex + "Feline")),
            new DataTriple(Iri(Ex + "whiskers"), Iri(RdfType), Iri(Ex + "Cat")),
        ];
    }

    /// <summary>
    /// An inconsistent graph within the OWL 2 RL profile: an individual asserted as an instance of two classes
    /// declared <c>owl:disjointWith</c>, which fires the class-disjointness falsity rule.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> DisjointInconsistentGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "A"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "B"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "A"), Iri(OwlDisjointWith), Iri(Ex + "B")),
            new DataTriple(Iri(Ex + "x"), Iri(RdfType), Iri(Ex + "A")),
            new DataTriple(Iri(Ex + "x"), Iri(RdfType), Iri(Ex + "B")),
        ];
    }

    /// <summary>
    /// The graph of <c>x : ∃r⁻.C</c> — an inverse existential in class-assertion position. It is beyond the RL
    /// grammar and inside the EL survey's admitted set, so the EL fast-path decides it whole: the assertion
    /// reduces to a forward existential over the synthetic per-<c>r</c> generator role and mints <c>x</c>'s
    /// <c>r</c>-predecessor from the individual's own node. Consistent, with no remainder — a decision the
    /// inverse-blind fallback cannot witness.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> InverseExistentialAssertionGraph()
    {
        BlankNode restriction = new(Utf8Strings.From("restr"));
        BlankNode inverse = new(Utf8Strings.From("inv"));

        return
        [
            new DataTriple(Iri(Ex + "C"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "r"), Iri(RdfType), Iri(OwlObjectProperty)),
            new DataTriple(Iri(Ex + "x"), Iri(RdfType), restriction),
            new DataTriple(restriction, Iri(RdfType), Iri(OwlRestriction)),
            new DataTriple(restriction, Iri(OwlOnProperty), inverse),
            new DataTriple(inverse, Iri(OwlInverseOf), Iri(Ex + "r")),
            new DataTriple(restriction, Iri(OwlSomeValuesFrom), Iri(Ex + "C")),
        ];
    }

    /// <summary>
    /// The graph of <c>c1 ⊑ (a ⊔ b)</c> with an asserted <c>x : c1</c> — a class disjunction on the superclass
    /// side. A union superclass is outside the RL grammar and outside the EL fragment, which carries no
    /// disjunction, so both the RL closure and the EL survey decline it; the context saturation tier decides it
    /// whole by ordered resolution over the disjunctive head, running no world solve, so the facade reads a whole
    /// consistent decision with empty solver totals.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> UnionSuperclassAssertionGraph()
    {
        BlankNode union = new(Utf8Strings.From("union"));
        BlankNode list1 = new(Utf8Strings.From("list1"));
        BlankNode list2 = new(Utf8Strings.From("list2"));

        return
        [
            new DataTriple(Iri(Ex + "c1"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "a"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "b"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "c1"), Iri(RdfsSubClassOf), union),
            new DataTriple(union, Iri(OwlUnionOf), list1),
            new DataTriple(list1, Iri(RdfFirst), Iri(Ex + "a")),
            new DataTriple(list1, Iri(RdfRest), list2),
            new DataTriple(list2, Iri(RdfFirst), Iri(Ex + "b")),
            new DataTriple(list2, Iri(RdfRest), Iri(RdfNil)),
            new DataTriple(Iri(Ex + "x"), Iri(RdfType), Iri(Ex + "c1")),
        ];
    }

    /// <summary>
    /// The <c>x : ∃r⁻.C</c> inverse existential of <see cref="InverseExistentialAssertionGraph"/> plus a nominal
    /// enumeration superclass <c>Base ⊑ {left, right, _:anon}</c> whose third member is a blank node. An
    /// anonymous individual in a nominal position is existential, not a constant, so the context tier's
    /// <c>AnonymousIndividualInNominal</c> guard delegates the module named — a permanent guard, never a banked
    /// lift — and the multi-individual one-of sits outside EL, so BOTH saturation tiers decline and the module
    /// delegates to the inverse-blind SAT oracle, which decides it CONSISTENT relative to its supported fragment
    /// with the excluded constructs named — the module that keeps the fragment-relative decay and refuse-knob
    /// coverage alive.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> InverseForcedBeyondBothGraph()
    {
        BlankNode restriction = new(Utf8Strings.From("restr"));
        BlankNode inverse = new(Utf8Strings.From("inv"));
        BlankNode enumeration = new(Utf8Strings.From("enumeration"));
        BlankNode list1 = new(Utf8Strings.From("list1"));
        BlankNode list2 = new(Utf8Strings.From("list2"));
        BlankNode list3 = new(Utf8Strings.From("list3"));
        BlankNode anonymousMember = new(Utf8Strings.From("anonmember"));

        return
        [
            new DataTriple(Iri(Ex + "C"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "r"), Iri(RdfType), Iri(OwlObjectProperty)),
            new DataTriple(Iri(Ex + "x"), Iri(RdfType), restriction),
            new DataTriple(restriction, Iri(RdfType), Iri(OwlRestriction)),
            new DataTriple(restriction, Iri(OwlOnProperty), inverse),
            new DataTriple(inverse, Iri(OwlInverseOf), Iri(Ex + "r")),
            new DataTriple(restriction, Iri(OwlSomeValuesFrom), Iri(Ex + "C")),
            new DataTriple(Iri(Ex + "Base"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "Base"), Iri(RdfsSubClassOf), enumeration),
            new DataTriple(enumeration, Iri(RdfType), Iri(OwlClass)),
            new DataTriple(enumeration, Iri(OwlOneOf), list1),
            new DataTriple(list1, Iri(RdfFirst), Iri(Ex + "left")),
            new DataTriple(list1, Iri(RdfRest), list2),
            new DataTriple(list2, Iri(RdfFirst), Iri(Ex + "right")),
            new DataTriple(list2, Iri(RdfRest), list3),
            new DataTriple(list3, Iri(RdfFirst), anonymousMember),
            new DataTriple(list3, Iri(RdfRest), Iri(RdfNil)),
        ];
    }

    /// <summary>
    /// A CONSISTENT graph carrying both a class-disjointness axiom whose instance is not yet shared and a within-RL
    /// subclass entailment: <c>x</c> is an instance of <c>A</c> only (consistent), while <c>rex</c> is a <c>Dog</c>
    /// and <c>Dog</c> is a subclass of <c>Animal</c> (so <c>rex a Animal</c> is derived). Asserting <c>x a B</c>
    /// later shares the disjoint pair and fires the class-disjointness falsity rule.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> ConsistentDisjointWithEntailmentGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "A"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "B"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "A"), Iri(OwlDisjointWith), Iri(Ex + "B")),
            new DataTriple(Iri(Ex + "x"), Iri(RdfType), Iri(Ex + "A")),
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfsSubClassOf), Iri(Ex + "Animal")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Dog")),
        ];
    }

    /// <summary>
    /// A consistent graph carrying two derivations and a dormant disjointness: <c>rex a Animal</c> is derived
    /// (<c>rex a Dog</c>, <c>Dog subClassOf Animal</c>) and <c>whiskers a Feline</c> is derived
    /// (<c>whiskers a Cat</c>, <c>Cat equivalentClass Feline</c>), while <c>Animal disjointWith Reptile</c> stands
    /// unshared. Asserting <c>rex a Animal</c> together with <c>rex a Reptile</c> both re-asserts a derivation and
    /// fires the class-disjointness falsity, so the overlay withdraws while the re-asserted fact stays served.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> AssertedFormerlyDerivedFlipGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfsSubClassOf), Iri(Ex + "Animal")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Dog")),
            new DataTriple(Iri(Ex + "Cat"), Iri(OwlEquivalentClass), Iri(Ex + "Feline")),
            new DataTriple(Iri(Ex + "whiskers"), Iri(RdfType), Iri(Ex + "Cat")),
            new DataTriple(Iri(Ex + "Animal"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "Reptile"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "Animal"), Iri(OwlDisjointWith), Iri(Ex + "Reptile")),
        ];
    }

    /// <summary>
    /// A graph whose <c>rex a Animal</c> membership is BOTH asserted and derivable (from <c>rex a Dog</c> and
    /// <c>Dog subClassOf Animal</c>), so deleting the asserted copy leaves the closure rederiving it while the
    /// derivation support stands.
    /// </summary>
    /// <returns>The graph triples.</returns>
    private static IReadOnlyList<DataTriple> AssertedAndDerivableGraph()
    {
        return
        [
            new DataTriple(Iri(Ex + "Dog"), Iri(RdfsSubClassOf), Iri(Ex + "Animal")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Dog")),
            new DataTriple(Iri(Ex + "rex"), Iri(RdfType), Iri(Ex + "Animal")),
        ];
    }

    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The IRI term.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }
}
