using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// Adversarial pins for the engine-boundary reasoning surface: every shape of a non-whole consistency claim
/// on <see cref="VeritasEngine.ReasoningProvenance"/> — a fragment-relative delegated verdict, a budget
/// abstention, and their decided controls — reads honestly through <see cref="ReasoningProvenance.IsDecisive"/>,
/// the refusal knob refuses only decided inconsistencies, and the record retains no store-sized state. The
/// battery exists because the abstention shape surfaces a consistent verdict with an EMPTY
/// <see cref="ReasoningProvenance.UndecidedConstructs"/>, so remainder inspection alone cannot distinguish it
/// from a whole-module decision — only <see cref="ReasoningProvenance.IsDecisive"/> can.
/// </summary>
[TestClass]
internal sealed class EngineBoundaryReviewProbes
{
    private const string Ex = "http://example.org/";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";
    private const string OwlEquivalentClass = "http://www.w3.org/2002/07/owl#equivalentClass";
    private const string OwlDisjointWith = "http://www.w3.org/2002/07/owl#disjointWith";
    private const string OwlClass = "http://www.w3.org/2002/07/owl#Class";
    private const string OwlObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty";
    private const string OwlRestriction = "http://www.w3.org/2002/07/owl#Restriction";
    private const string OwlOnProperty = "http://www.w3.org/2002/07/owl#onProperty";
    private const string OwlInverseOf = "http://www.w3.org/2002/07/owl#inverseOf";
    private const string OwlSomeValuesFrom = "http://www.w3.org/2002/07/owl#someValuesFrom";
    private const string OwlUnionOf = "http://www.w3.org/2002/07/owl#unionOf";
    private const string OwlOneOf = "http://www.w3.org/2002/07/owl#oneOf";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The two spellings of an inverse existential are BOTH decided whole by the EL-coupled engine's
    /// generator-role reduction: the superclass form <c>A ⊑ ∃r⁻.B</c> through the inclusion it already is, and
    /// the class-assertion form <c>x : ∃r⁻.C</c> through the asserted individual's own node. Pins that the two
    /// spellings share one whole-decision face on the same tier.
    /// </summary>
    [TestMethod]
    public async Task Deviation1SuperclassVersusAssertionInverseExistential()
    {
        ReasoningProvenance superclass = await OpenAndReadAsync(SuperclassInverseExistentialGraph()).ConfigureAwait(false);
        ReasoningProvenance assertion = await OpenAndReadAsync(AssertionInverseExistentialGraph()).ConfigureAwait(false);

        Assert.AreNotEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, superclass.DecisionOutcome, "The superclass inverse existential is EL-decided whole, never fragment-relative.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, assertion.DecisionOutcome, "The assertion-position inverse existential is EL-decided whole, past the inverse-blind fallback.");
        Assert.IsEmpty(assertion.UndecidedConstructs, "The whole EL decision names no undecided construct.");
        Assert.IsTrue(assertion.IsDecisive, "The whole EL decision covers the content whole.");
    }

    /// <summary>
    /// A union superclass is a WHOLE context decision: IsConsistent with an EMPTY remainder and decisive; the
    /// refusal knob opens it normally — a whole consistency is not a decided inconsistency. Pins that face
    /// against a laundering of the consistency claim.
    /// </summary>
    [TestMethod]
    public async Task LaunderCheckWholeContextDecisionIsConsistentAndNotRefused()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(UnionSuperclassGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            ReasoningProvenance? p = database.ReasoningProvenance;
            Assert.IsNotNull(p, "The context-decided open surfaces a provenance.");
            Assert.AreEqual(ReasoningDecisionOutcome.Decided, p.DecisionOutcome, "The union superclass is context-decided whole.");
            Assert.IsTrue(p.IsConsistent, "The context decision is consistent.");
            Assert.IsEmpty(p.UndecidedConstructs, "A whole context decision names no remainder.");
            Assert.IsTrue(p.IsDecisive, "A whole context decision reads as covering the content whole.");
        }

        VeritasEngineOptions refusing = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with { RefuseInconsistent = true },
        };
        VeritasEngine refused = await VeritasEngine
            .OpenAsync(UnionSuperclassGraph(), refusing, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(refused.ConfigureAwait(false))
        {
            Assert.IsNotNull(refused.ReasoningProvenance, "RefuseInconsistent does not refuse a whole consistent context decision.");
        }
    }

    /// <summary>
    /// A budget abstention surfaces the exact data state remainder inspection cannot distinguish from a
    /// whole-module decision — IsConsistent stays at the in-engine value with UndecidedConstructs EMPTY — and
    /// <see cref="ReasoningProvenance.IsDecisive"/> is what marks it undecisive: the delegated module went
    /// wholly undecided, so the consistency claim covers only the in-engine pass.
    /// </summary>
    [TestMethod]
    public async Task BudgetAbstentionSurfacesAnUndecisiveConsistency()
    {
        ReasoningProvenance p = await OpenStarvedUnionAsync().ConfigureAwait(false);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, p.DecisionOutcome, "The starved union module abstains on its budget.");
        Assert.IsTrue(p.IsConsistent, "Abstention keeps the in-engine consistency.");
        Assert.IsEmpty(p.UndecidedConstructs, "Abstention names no remainder — the module went wholly undecided, not partially decided.");
        Assert.IsFalse(p.IsDecisive, "An abstained decision never reads as a whole-content consistency claim.");
        Assert.IsGreaterThan(0, p.ModuleAxiomCount, "The undecided beyond-ceiling module is visible through its axiom count.");
    }

    /// <summary>
    /// RefuseInconsistent does not refuse a budget abstention — abstention is not a decided inconsistency —
    /// and the served provenance reads undecisive, so the host is confronted with the abstention rather than
    /// a laundered whole-truth consistency.
    /// </summary>
    [TestMethod]
    public async Task RefuseInconsistentDoesNotRefuseABudgetAbstention()
    {
        VeritasEngineOptions refusingStarved = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with
            {
                RefuseInconsistent = true,
                Budget = new ReasoningBudget(MaxSolves: 1, MaxConflicts: 0, MaxInferences: 1),
            },
        };

        VeritasEngine database = await VeritasEngine
            .OpenAsync(UnionSuperclassGraph(), refusingStarved, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            ReasoningProvenance? p = database.ReasoningProvenance;
            Assert.IsNotNull(p, "RefuseInconsistent plus abstention opens without a throw.");
            Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, p.DecisionOutcome, "The starved decision abstained.");
            Assert.IsTrue(p.IsConsistent, "The refusal gate reads the folded consistency, which abstention leaves at the in-engine value.");
            Assert.IsFalse(p.IsDecisive, "The undecided module is surfaced as undecisive, never as whole-truth.");
        }
    }

    /// <summary>
    /// The two non-whole consistency shapes carry OPPOSITE remainder counts — fragment-relative names its
    /// exclusions, abstention names none — yet both read undecisive: the decisive reading is a function of the
    /// outcome, not of remainder inspection.
    /// </summary>
    [TestMethod]
    public async Task EveryNonWholeConsistencyShapeReadsUndecisive()
    {
        //Post-flip the assertion-position inverse existential decides WHOLE, so the fragment-relative arm is
        //sourced from the beyond-both module (inverse existential PLUS a nominal enumeration) that both
        //saturation tiers decline and the inverse-blind SAT oracle reads fragment-relative.
        ReasoningProvenance fragmentRelative = await OpenAndReadAsync(AssertionInverseForcedBeyondBothGraph()).ConfigureAwait(false);
        ReasoningProvenance abstention = await OpenStarvedUnionAsync().ConfigureAwait(false);

        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, fragmentRelative.DecisionOutcome, "The beyond-both module decides fragment-relative.");
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstention.DecisionOutcome, "The starved union module abstains.");
        Assert.IsNotEmpty(fragmentRelative.UndecidedConstructs, "Fragment-relative names its excluded remainder.");
        Assert.IsEmpty(abstention.UndecidedConstructs, "Abstention names no remainder.");
        Assert.IsFalse(fragmentRelative.IsDecisive, "A named remainder scopes the consistency claim.");
        Assert.IsFalse(abstention.IsDecisive, "A wholly undecided module scopes the consistency claim just the same.");
    }

    /// <summary>
    /// Decided control: a delegated or in-engine condemnation carries no remainder and is legitimately
    /// decisive — condemnation is monotone, so a falsity in any decided fragment condemns the whole.
    /// </summary>
    [TestMethod]
    public async Task DisjointRlCondemnationIsWholeTruthWithEmptyRemainder()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(DisjointInconsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            ReasoningProvenance p = database.ReasoningProvenance!;
            Assert.IsFalse(p.IsConsistent, "The disjoint membership is a decided inconsistency.");
            Assert.IsEmpty(p.UndecidedConstructs, "A condemnation carries no remainder.");
            Assert.IsTrue(p.IsDecisive, "A derived inconsistency covers the content whole.");
        }
    }

    /// <summary>
    /// The disjoint-membership graph fires exactly the class-disjointness falsity rule and still serves the
    /// asserted closure — the served answers and the surfaced verdict travel together.
    /// </summary>
    [TestMethod]
    public async Task DisjointGraphFiresCaxDwAndServesPartialClosure()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(DisjointInconsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            bool served = await database
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}x> <{RdfType}> <{Ex}A> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            ReasoningProvenance p = database.ReasoningProvenance!;
            Assert.IsTrue(served, "The partial closure still answers the asserted membership.");
            Assert.IsFalse(p.IsConsistent, "The outcome is inconsistent.");
            Assert.AreEqual("cax-dw", p.InconsistencyRule, "The class-disjointness falsity rule is named.");
        }
    }

    /// <summary>
    /// Decided control: a within-RL consistent graph selects the RL closure, derives, and reads decisive —
    /// nothing lay beyond the in-engine ceiling.
    /// </summary>
    [TestMethod]
    public async Task WithinRlConsistentSelectsRlSufficient()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            ReasoningProvenance p = database.ReasoningProvenance!;
            Assert.AreEqual(ReasoningStrategy.Rl, p.Strategy, "The equivalent-class axiom selects the RL closure.");
            Assert.AreEqual(ReasoningSelectionReason.RlSufficient, p.Reason, "The content is within the RL profile.");
            Assert.IsGreaterThan(0, p.DerivedCount, "The RL closure derived at least one triple.");
            Assert.IsTrue(p.IsDecisive, "An in-engine decision over within-ceiling content covers it whole.");
        }
    }

    /// <summary>
    /// Default (untraced) open surfaces exactly one provenance and the served answers hold — the facade
    /// surface adds nothing observable to the default path beyond the property itself.
    /// </summary>
    [TestMethod]
    public async Task DefaultPathNoTraceEventsOneProvenanceServedAnswersUnchanged()
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(WithinRlConsistentGraph(), cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            bool served = await database
                .AskAsync(Utf8Strings.From($"ASK {{ <{Ex}whiskers> <{RdfType}> <{Ex}Cat> }}"), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false);
            Assert.IsTrue(served, "The default served closure answers the asserted membership.");
            Assert.IsNotNull(database.ReasoningProvenance, "Exactly one provenance is surfaced on the default open.");
        }

        Assert.IsNull(VeritasEngineOptions.Default.ReasoningTrace, "Default options wire no reasoning trace handler.");
        Assert.IsNull(VeritasEngineOptions.Default.ReasoningDecisionTrace, "Default options wire no reasoning decision trace handler.");
    }

    /// <summary>
    /// The provenance record declares no member typed as a store, dictionary, module, or engine, so holding it
    /// for the database's lifetime pins no store-sized state.
    /// </summary>
    [TestMethod]
    public void ProvenanceRetainsNoStoreOrModuleTypedMembers()
    {
        Type provenanceType = typeof(ReasoningProvenance);
        string[] forbidden = ["HypertrieGraphStore", "TermDictionary", "ReasoningModule", "ReasoningResult", "ModuleVerdict", "VeritasEngine", "SparqlQueryEngine"];

        IEnumerable<Type> memberTypes = provenanceType
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .Concat(provenanceType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType));

        foreach(Type t in memberTypes)
        {
            Assert.IsFalse(forbidden.Contains(t.Name), $"ReasoningProvenance must not retain a {t.Name}.");
        }
    }

    /// <summary>Opens the triples with default options and returns the surfaced provenance.</summary>
    /// <param name="triples">The default-graph triples to open.</param>
    /// <returns>The surfaced reasoning provenance.</returns>
    private async Task<ReasoningProvenance> OpenAndReadAsync(IReadOnlyList<DataTriple> triples)
    {
        VeritasEngine database = await VeritasEngine
            .OpenAsync(triples, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            return database.ReasoningProvenance!;
        }
    }

    /// <summary>
    /// Opens the beyond-RL union graph under a starved budget on BOTH saturation and solve axes — the
    /// disjunctive context tier admits the union module, so one inference attempt starves it into the
    /// delegation, and one world solve starves the SAT fallback — so the delegated decision abstains, and
    /// returns the surfaced provenance.
    /// </summary>
    /// <returns>The surfaced reasoning provenance of the abstained open.</returns>
    private async Task<ReasoningProvenance> OpenStarvedUnionAsync()
    {
        VeritasEngineOptions starved = VeritasEngineOptions.Default with
        {
            Reasoning = ReasoningConfiguration.Default with
            {
                Budget = new ReasoningBudget(MaxSolves: 1, MaxConflicts: 0, MaxInferences: 1),
            },
        };

        VeritasEngine database = await VeritasEngine
            .OpenAsync(UnionSuperclassGraph(), starved, cancellationToken: TestContext.CancellationToken)
            .ConfigureAwait(false);
        await using(database.ConfigureAwait(false))
        {
            return database.ReasoningProvenance!;
        }
    }

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

    //x : exists r-inverse.C — inverse existential in class-assertion position.
    private static IReadOnlyList<DataTriple> AssertionInverseExistentialGraph()
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

    //x : exists r-inverse.C PLUS Base subClassOf {left, right, _:anon} — the inverse existential the
    //context tier would decide, forced beyond both saturation tiers by the ANONYMOUS enumeration member
    //(a blank node in a nominal position is existential, not a constant, so the AnonymousIndividualInNominal
    //guard delegates the module named — a permanent guard, never a banked lift; the multi-member enumeration
    //keeps EL out), so the inverse-blind SAT oracle reads it fragment-relative.
    private static IReadOnlyList<DataTriple> AssertionInverseForcedBeyondBothGraph()
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

    //A subClassOf exists r-inverse.B — inverse existential on the superclass side.
    private static IReadOnlyList<DataTriple> SuperclassInverseExistentialGraph()
    {
        BlankNode restriction = new(Utf8Strings.From("restr"));
        BlankNode inverse = new(Utf8Strings.From("inv"));

        return
        [
            new DataTriple(Iri(Ex + "A"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "B"), Iri(RdfType), Iri(OwlClass)),
            new DataTriple(Iri(Ex + "r"), Iri(RdfType), Iri(OwlObjectProperty)),
            new DataTriple(Iri(Ex + "A"), Iri(RdfsSubClassOf), restriction),
            new DataTriple(restriction, Iri(RdfType), Iri(OwlRestriction)),
            new DataTriple(restriction, Iri(OwlOnProperty), inverse),
            new DataTriple(inverse, Iri(OwlInverseOf), Iri(Ex + "r")),
            new DataTriple(restriction, Iri(OwlSomeValuesFrom), Iri(Ex + "B")),
        ];
    }

    //c1 subClassOf (a union b) — a union on the superclass side, beyond RL and beyond EL.
    private static IReadOnlyList<DataTriple> UnionSuperclassGraph()
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
        ];
    }

    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }
}
