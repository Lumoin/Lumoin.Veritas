using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The guarded co-occurrence lift battery's origin-primitive pins: the
/// named/blank capture at root intake. Each pin drives the
/// context clausifier's individual-mint sites, or the symbol table's mint
/// channel directly, and reads the recorded <see cref="IndividualOrigin"/> and
/// the key-join candidacy predicate. The candidacy read is dark — no production
/// consumer reads it yet — so these pins are its sole exercise until the key
/// join lands. The two candidacy-exclusion pins are duals: a generated nominal
/// fails on the depth conjunct with the origin conjunct passing, and a
/// blank-node subject fails on the origin conjunct with the depth conjunct
/// passing.
/// </summary>
[TestClass]
internal sealed class ContextCooccurrenceLiftTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/gcstagea#";

    /// <summary>(a) A named-individual ABox subject of a nominal-jurisdiction module interns through the root-intake site with an IRI-denoted origin and reads as a key-join candidate.</summary>
    [TestMethod]
    public void NamedIndividualAboxSubjectInternsIriDenoted()
    {
        ClausificationResult clausification = Clausify(
            SubClassOf(Class("A"), HasValue("r", "o")),
            ClassAssertion(Class("A"), Individual("i")));

        Assert.IsTrue(clausification.NominalJurisdiction, "The ObjectHasValue superclass puts the module under nominal jurisdiction, routing the class assertion through the root-intake mint site.");
        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From(Example + "i"), out int subject), "The named ABox subject interned at the root-intake site.");
        Assert.AreEqual(IndividualOrigin.IriDenoted, clausification.Symbols.OriginOf(subject), "A NamedNode subject records the IRI-denoted origin.");
        Assert.IsTrue(clausification.Symbols.IsKeyJoinCandidateOrigin(subject), "An IRI-denoted, depth-zero individual reads as a key-join candidate.");
    }

    /// <summary>(b) A blank-node ABox subject of a nominal-jurisdiction module interns through the root-intake site with a blank-node origin — a blank node is barred only from nominal positions, not from the ABox subject slot.</summary>
    [TestMethod]
    public void BlankNodeAboxSubjectInternsBlankNode()
    {
        ClausificationResult clausification = Clausify(
            SubClassOf(Class("A"), HasValue("r", "o")),
            ClassAssertion(Class("A"), Blank("b0")));

        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From("b0"), out int subject), "The blank-node ABox subject interned at the root-intake site under its label.");
        Assert.AreEqual(IndividualOrigin.BlankNode, clausification.Symbols.OriginOf(subject), "A blank-node subject records the blank-node origin.");
    }

    /// <summary>(c) An individual punned with a class of the same IRI interns with an IRI-denoted origin: the concept-atom and individual id spaces are disjoint, so the pun neither collides nor demotes the individual's candidacy.</summary>
    [TestMethod]
    public void PunnedIndividualInternsIriDenoted()
    {
        ClausificationResult clausification = Clausify(
            SubClassOf(Class("shared"), HasValue("r", "o")),
            ClassAssertion(Class("B"), Individual("shared")));

        Assert.IsGreaterThan(ContextSymbolTable.Bottom, clausification.Symbols.AtomOf(Utf8Strings.From(Example + "shared")), "The shared IRI also occupies the concept-atom space as a named class atom — the pun.");
        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From(Example + "shared"), out int individual), "The shared IRI interns as an individual through the root-intake site.");
        Assert.AreEqual(IndividualOrigin.IriDenoted, clausification.Symbols.OriginOf(individual), "The punned individual records the IRI-denoted origin.");
        Assert.IsTrue(clausification.Symbols.IsKeyJoinCandidateOrigin(individual), "The punned individual reads as a key-join candidate.");
    }

    /// <summary>(d) The named filler of an ObjectHasValue restriction interns with an IRI-denoted origin through the fresh-singleton mint site and reads as a key-join candidate.</summary>
    [TestMethod]
    public void ObjectHasValueFillerInternsIriDenoted()
    {
        ClausificationResult clausification = Clausify(SubClassOf(Class("A"), HasValue("r", "f")));

        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From(Example + "f"), out int filler), "The ObjectHasValue filler interned at the fresh-singleton mint site.");
        Assert.AreEqual(IndividualOrigin.IriDenoted, clausification.Symbols.OriginOf(filler), "A NamedNode filler records the IRI-denoted origin.");
        Assert.IsTrue(clausification.Symbols.IsKeyJoinCandidateOrigin(filler), "The filler reads as a key-join candidate.");
    }

    /// <summary>(e) A generated nominal fails the key-join candidacy read on the depth conjunct: minted from an IRI-denoted, depth-zero prefix that is itself a candidate, the sibling carries the candidate origin yet interns at depth one, so the depth conjunct alone excludes it.</summary>
    [TestMethod]
    public void GeneratedNominalFailsCandidacyByDepthConjunct()
    {
        ContextSymbolTable symbols = new();
        int prefix = symbols.InternIndividual(Utf8Strings.From(Example + "o"), IndividualOrigin.IriDenoted);
        Assert.IsTrue(symbols.IsKeyJoinCandidateOrigin(prefix), "The IRI-denoted, depth-zero prefix is itself a key-join candidate.");

        bool minted = symbols.MintGeneratedNominal(prefix, roleId: 0, count: 1, out int sibling);
        Assert.IsTrue(minted, "The first mint for the (prefix, role) pair mints a fresh sibling.");
        Assert.IsGreaterThan(0, symbols.IndividualDepth(sibling), "The generated sibling interns at depth one or greater.");
        Assert.AreEqual(IndividualOrigin.IriDenoted, symbols.OriginOf(sibling), "The generated sibling carries the candidate origin, so the origin conjunct passes and only the depth conjunct can exclude it.");
        Assert.IsFalse(symbols.IsKeyJoinCandidateOrigin(sibling), "The depth conjunct excludes the generated nominal from key-join candidacy despite its candidate origin bit.");
    }

    /// <summary>(f) A blank-node ABox subject fails the key-join candidacy read on the origin conjunct: it interns at depth zero, so the depth conjunct passes, and the blank-node origin alone excludes it.</summary>
    [TestMethod]
    public void BlankNodeSubjectFailsCandidacyByOriginBit()
    {
        ClausificationResult clausification = Clausify(
            SubClassOf(Class("A"), HasValue("r", "o")),
            ClassAssertion(Class("A"), Blank("b0")));

        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From("b0"), out int subject), "The blank-node ABox subject interned under its label.");
        Assert.AreEqual(0, clausification.Symbols.IndividualDepth(subject), "The blank-node subject interns at depth zero, so the depth conjunct passes.");
        Assert.AreEqual(IndividualOrigin.BlankNode, clausification.Symbols.OriginOf(subject), "The subject records the blank-node origin.");
        Assert.IsFalse(clausification.Symbols.IsKeyJoinCandidateOrigin(subject), "The origin conjunct excludes the depth-zero blank-node subject from key-join candidacy.");
    }

    /// <summary>The re-intern origin-collision residual: re-interning an already-known key with a disagreeing origin returns the existing id, never overwrites the recorded origin, and records the un-namespaced blank-label/IRI key-collision marker naming the colliding key — the residual the jurisdiction machinery reads to delegate named.</summary>
    [TestMethod]
    public void ReInternWithDisagreeingOriginRecordsCollisionResidual()
    {
        ContextSymbolTable symbols = new();
        Utf8String key = Utf8Strings.From(Example + "x");
        int first = symbols.InternIndividual(key, IndividualOrigin.IriDenoted);
        Assert.IsFalse(symbols.HasIndividualOriginConflict, "A first intern records no collision.");

        int matching = symbols.InternIndividual(key, IndividualOrigin.IriDenoted);
        Assert.AreEqual(first, matching, "A re-intern with the recorded origin returns the existing id.");
        Assert.IsFalse(symbols.HasIndividualOriginConflict, "An agreeing re-intern records no collision.");

        int disagreeing = symbols.InternIndividual(key, IndividualOrigin.BlankNode);
        Assert.AreEqual(first, disagreeing, "A disagreeing re-intern still returns the existing id, never a new one.");
        Assert.AreEqual(IndividualOrigin.IriDenoted, symbols.OriginOf(first), "The recorded origin is never overwritten by the disagreeing re-intern.");
        Assert.IsTrue(symbols.HasIndividualOriginConflict, "The disagreeing re-intern records the key-collision residual.");
        Assert.IsTrue(symbols.ConflictingIndividualKey is Utf8String recorded && recorded.Equals(key), "The residual names the colliding key.");
    }

    /// <summary>Feed row: a told <c>SameIndividual(a, b)</c> lands on the root context as the unconditional single-literal equality <c>⊤ → a ≈ b</c> and merges the two ids' ≈-classes at the AddClause landing. The surface allocates on the first feed, and union by id order keeps the lower id as the class representative.</summary>
    [TestMethod]
    public void ToldSameIndividualMergesTheTwoClasses()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                Same("a", "b"),
            ],
            out ClausificationResult clausification);

        int idA = IndividualId(clausification, "a");
        int idB = IndividualId(clausification, "b");
        Assert.AreNotEqual(idA, idB, "The two named individuals interned as distinct ids — the pre-merge pass does not collapse the interned id space.");
        Assert.IsTrue(engine.HasRootApproxSurface, "The told equality landing allocated the ≈-class surface.");
        Assert.IsTrue(engine.RootApproxSameClass(idA, idB), "The told SameIndividual merged the two ids' classes at the equality-head landing.");
        Assert.AreEqual(Math.Min(idA, idB), engine.RootApproxRepresentative(idA), "Union by id order keeps the lower id as the class representative.");
    }

    /// <summary>Feed row: a told chain <c>SameIndividual(a, b)</c>, <c>SameIndividual(b, c)</c> lands two equality heads that transitively put a and c in one ≈-class (a monotone merge).</summary>
    [TestMethod]
    public void ToldEqualityChainPutsEndpointsInOneClass()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                Same("a", "b"),
                Same("b", "c"),
            ],
            out ClausificationResult clausification);

        int idA = IndividualId(clausification, "a");
        int idC = IndividualId(clausification, "c");
        Assert.IsTrue(engine.RootApproxSameClass(idA, idC), "The told a≈b, b≈c chain transitively merged a and c into one ≈-class.");
    }

    /// <summary>Feed row: a DISJUNCTIVE equality head (a multi-literal head carrying an equality) is not a decided merge, so the feed decision rejects it — the certainty discipline.</summary>
    [TestMethod]
    public void DisjunctiveEqualityHeadDoesNotFeed()
    {
        DlClause disjunctive = DlClause.Create(
            [],
            [DlLiteral.Equality(DlTerm.Individual(1), DlTerm.Individual(2)), DlLiteral.Equality(DlTerm.Individual(1), DlTerm.Individual(3))],
            0);

        Assert.IsGreaterThan(1, disjunctive.Head.Length, "The fixture is a multi-literal disjunctive head.");
        Assert.IsFalse(RootApproxClasses.TryResolveMerge(disjunctive, homeIndividual: -1, out _, out _), "A disjunctive equality head is not a decided merge and does not feed the ≈-class surface.");
    }

    /// <summary>Feed row: a CONDITIONAL equality (a nonempty body) is not a decided merge, so the feed decision rejects it — the certainty discipline.</summary>
    [TestMethod]
    public void ConditionalEqualityDoesNotFeed()
    {
        DlClause conditional = DlClause.Create(
            [DlLiteral.Concept(1, DlTerm.Central)],
            [DlLiteral.Equality(DlTerm.Individual(2), DlTerm.Individual(3))],
            0);

        Assert.AreEqual(1, conditional.BodyLength, "The fixture carries a nonempty body.");
        Assert.IsFalse(RootApproxClasses.TryResolveMerge(conditional, homeIndividual: -1, out _, out _), "A conditional equality is not a decided merge and does not feed the ≈-class surface.");
    }

    /// <summary>Home-slot feed row (the entry-translation shape): a Central-spelled equality <c>⊤ → x ≈ o′</c> on a nominal-root context <c>v_o</c> resolves its central side to the home individual and feeds the merge (o, o′) — the home slot keys by <see cref="Context.HomeIndividual"/>, not the head literal's term.</summary>
    [TestMethod]
    public void CentralSpelledEqualityFeedsViaHomeIndividual()
    {
        DlClause homeEquality = DlClause.Create(
            [],
            [DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(7))],
            0);

        Assert.IsTrue(RootApproxClasses.TryResolveMerge(homeEquality, homeIndividual: 3, out int first, out int second), "A Central-spelled equality on a v_o context feeds via the home individual.");
        Assert.AreEqual(3, first, "The central side resolves to the context's home individual, not to a term id.");
        Assert.AreEqual(7, second, "The foreign side resolves by its term id.");
    }

    /// <summary>Index row: a told class membership lands as the root fact <c>⊤ → A(a)</c> and is readable per-constant off the root-tier index (the B(o) family).</summary>
    [TestMethod]
    public void ToldClassMembershipIsReadablePerConstant()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                ClassAssertion(Class("A"), Individual("a")),
            ],
            out ClausificationResult clausification);

        int idA = IndividualId(clausification, "a");
        int atomA = clausification.Symbols.AtomOf(Utf8Strings.From(Example + "A"));
        List<int> memberships = [];
        engine.AppendRootConceptSpelling(idA, memberships);
        Assert.Contains(atomA, memberships, "The told membership A(a) reads off the per-constant index under a's key.");
    }

    /// <summary>Index row: a told role edge lands as the root fact <c>⊤ → r(a, b)</c> and is readable by source off the root-tier index (the S(o, o′) family).</summary>
    [TestMethod]
    public void ToldRoleEdgeIsReadableBySource()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                Edge("a", "rel", "b"),
            ],
            out ClausificationResult clausification);

        int idA = IndividualId(clausification, "a");
        int idB = IndividualId(clausification, "b");
        List<RootRoleEdge> edges = [];
        engine.AppendRootRoleTargetSpelling(idA, edges);
        bool reachesB = false;
        foreach(RootRoleEdge edge in edges)
        {
            if(edge.Target == idB)
            {
                reachesB = true;
            }
        }

        Assert.IsTrue(reachesB, "The told edge r(a, b) reads off the per-constant index as a role edge from a to b.");
    }

    /// <summary>Index row: a constant-spelled data-demand marker D(o) counts live on projection and DECREMENTS to dead on tombstone (the D(o) live-count family, clean-on-tombstone). Driven at the index directly, mirroring the same-cycle Insert/Tombstone maintenance the engine runs.</summary>
    [TestMethod]
    public void DataDemandMarkerCountsLiveAndDecrementsOnTombstone()
    {
        RootConstantIndex index = new();
        HashSet<int> markers = [99];
        DlLiteral demand = DlLiteral.Concept(99, DlTerm.Individual(4));

        index.Project(demand, homeIndividual: -1, markers);
        Assert.AreEqual(1, index.DataDemandCount(4, 99), "The projected constant-spelled demand marker counts one live.");

        index.Retract(demand, homeIndividual: -1, markers);
        Assert.AreEqual(0, index.DataDemandCount(4, 99), "The tombstoned demand marker decrements to dead.");
    }

    /// <summary>Index liveness row: a tombstoned concept membership is NOT readable (the liveness discipline) — a retracted spelling leaves no readable trace to ghost through a read-time union.</summary>
    [TestMethod]
    public void TombstonedMembershipIsNotReadable()
    {
        RootConstantIndex index = new();
        HashSet<int> markers = [];
        DlLiteral membership = DlLiteral.Concept(5, DlTerm.Individual(4));

        index.Project(membership, homeIndividual: -1, markers);
        List<int> live = [];
        index.AppendConceptMemberships(4, live);
        Assert.Contains(5, live, "The projected membership reads live.");

        index.Retract(membership, homeIndividual: -1, markers);
        List<int> afterTombstone = [];
        index.AppendConceptMemberships(4, afterTombstone);
        Assert.DoesNotContain(5, afterTombstone, "The tombstoned membership is no longer readable.");
    }

    /// <summary>Index home-slot row: a Central-spelled concept head <c>⊤ → A(x)</c> on <c>v_o</c> projects under the home individual (the home slot keys by HomeIndividual).</summary>
    [TestMethod]
    public void HomeSlotConceptProjectsUnderHomeIndividual()
    {
        RootConstantIndex index = new();
        HashSet<int> markers = [];
        DlLiteral homeMembership = DlLiteral.Concept(8, DlTerm.Central);

        index.Project(homeMembership, homeIndividual: 6, markers);
        List<int> memberships = [];
        index.AppendConceptMemberships(6, memberships);
        Assert.Contains(8, memberships, "The Central-spelled membership projects under the context's home individual, not a term id.");
    }

    /// <summary>KVR-12 precursor at surface level: membership told under one spelling and under a second, told <c>SameIndividual(a, b)</c>; the ≈-union read pools BOTH memberships across the two merged spellings, while a direct single-spelling read of the higher-id constant misses the lower-id constant's told fact (the read-time union over the ≈-class surface).</summary>
    [TestMethod]
    public void ApproxUnionReadPoolsFactsAcrossTwoMergedSpellings()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("C"), Individual("b")),
                Same("a", "b"),
            ],
            out ClausificationResult clausification);

        int idA = IndividualId(clausification, "a");
        int idB = IndividualId(clausification, "b");
        int atomA = clausification.Symbols.AtomOf(Utf8Strings.From(Example + "A"));
        int atomC = clausification.Symbols.AtomOf(Utf8Strings.From(Example + "C"));
        Assert.IsTrue(engine.RootApproxSameClass(idA, idB), "The told SameIndividual merged the two spellings.");

        List<int> pooledFromA = [];
        engine.AppendPooledRootConcepts(idA, pooledFromA);
        Assert.Contains(atomA, pooledFromA, "The pooled read surfaces a's own told membership.");
        Assert.Contains(atomC, pooledFromA, "The pooled read surfaces b's told membership through the ≈-union.");

        int higher = Math.Max(idA, idB);
        int lowerTold = higher == idA ? atomC : atomA;
        List<int> higherDirect = [];
        engine.AppendRootConceptSpelling(higher, higherDirect);
        Assert.DoesNotContain(lowerTold, higherDirect, "The higher-id spelling's direct read misses the lower-id spelling's told fact — paramodulation rewrites only toward the lower id.");
        List<int> higherPooled = [];
        engine.AppendPooledRootConcepts(higher, higherPooled);
        Assert.Contains(lowerTold, higherPooled, "The read-time union surfaces the merged spelling's fact the direct read misses — the join happens ONLY through the union.");
    }

    /// <summary>Zero-touch row: a nominal-free module mints no root context, so neither the ≈-class surface nor any per-constant index is allocated (the memory discipline). The dark spine costs a control run nothing.</summary>
    [TestMethod]
    public void NominalFreeModuleAllocatesNoRootTierSurface()
    {
        ContextSaturationEngine engine = Saturate(
            [
                SubClassOf(Class("A"), Class("B")),
            ],
            out _);

        Assert.IsFalse(engine.HasRootApproxSurface, "A nominal-free module never feeds the ≈-class surface, so it stays unallocated.");
        Assert.AreEqual(0, engine.RootConstantIndexContextCount, "A nominal-free module mints no root context, so no per-constant index is allocated.");
    }

    /// <summary>
    /// JUR-1 (the value-landing + decide half): with the
    /// key-join switch lit, a HasKey + nominal module carrying a told data-key
    /// assertion routes PAST the <c>KeyOnNominalModule</c> guard into intake (the
    /// P-GC9 witness). The guard no longer names the remainder, the module is
    /// admitted at the second gate, the told data-key value lands in the key-value
    /// store, and the nominal jurisdiction's suppress fork still mints no ground
    /// representative. The decide over that landed value is pinned by
    /// <see cref="Jur1HasKeyNominalDataKeyModuleDecidesUnderRootKeyJoin"/>.
    /// </summary>
    [TestMethod]
    public void Jur1HasKeyNominalDataKeyValueLandsPastTheLiftedGuard()
    {
        ClausificationResult clausification = ContextClausifier.Clausify(
            new ReasoningModule(
                [
                    HasKey(Class("K"), [], ["dk"]),
                    SubClassOf(Class("A"), HasValue("r", "o")),
                    ClassAssertion(Class("K"), Individual("k1")),
                    DataAssertion("k1", "dk", "V-1"),
                ],
                Violations: []),
            EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false, rootKeyJoinEnabled: true);

        Assert.IsTrue(clausification.NominalJurisdiction, "The ObjectHasValue superclass sets the nominal jurisdiction bit.");
        Assert.DoesNotContain(ContextRemainderNames.KeyOnNominalModule, clausification.Remainder, "The lit key-join switch routes the HasKey+nominal module past the key-on-nominal guard, so the guard no longer names the remainder.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DelegatesOnSecondGate(clausification), "With the guard lifted the module is admitted at the second gate.");
        Assert.IsEmpty(clausification.GroundRepresentatives, "The nominal jurisdiction's suppress fork mints no ground representative even as intake runs (P-GC9).");
        Assert.IsNotEmpty(clausification.KeyValueStore, "Intake now runs past the lifted guard, so the told data-key value lands in the key-value store.");
    }

    /// <summary>
    /// JUR-2: the suppress repair's zero-movement witness — a nominal module WITHOUT
    /// HasKey carrying a told data assertion. Under the P-GC9 fork
    /// the told value lands in the jurisdiction-independent key-value store, yet the
    /// subject takes no ground side effect, so the setup mints no parallel ground
    /// context. The verdict is unchanged-sound: consistent, decided. The fork can
    /// only FREE budget (an abstain→decide flip), never move a wrong verdict — a
    /// one-directional budget mover.
    /// </summary>
    [TestMethod]
    public void Jur2NominalWithoutHasKeyDataAssertionSuppressesGroundContextConsistent()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                ClassAssertion(Class("A"), Individual("s")),
                DataAssertion("s", "dp", "V-2"),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(clausification.NominalJurisdiction, "The trigger's ObjectHasValue superclass puts the module under nominal jurisdiction.");
        Assert.IsTrue(
            clausification.KeyValueStore.TryGetValue(Utf8Strings.From(Example + "s"), out Dictionary<Utf8String, List<Literal>>? properties) && properties.ContainsKey(Utf8Strings.From(Example + "dp")),
            "The told value lands in the jurisdiction-independent key-value store under the subject and property, unchanged by the fork.");
        Assert.IsEmpty(clausification.GroundRepresentatives, "The nominal data fork takes no ground-representative side effect, so no representative registers.");
        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.AreEqual(0, statistics.GroundContextsCreated, "With no ground representative the setup mints no parallel ground context for the told-data subject — the P-GC9 suppress.");
        Assert.IsFalse(engine.IsInconsistent, "The module is consistent — the suppress moves no verdict, only frees setup budget (the one-directional mover).");
    }

    /// <summary>
    /// JUR-3 (the P-GC10 structural-exclusion pin): a
    /// HasKey-carrying, otherwise enumeration-algebra-shaped (Σ_E) module is NOT
    /// classified as the enumeration algebra. The recognizer's closed-world Σ_E kind
    /// gate rejects the HasKey kind, so the certifying face stays Silent and
    /// unreachable on such a module — no new gate lands; the exclusion is already
    /// structural. The baseline (the same module without the HasKey) IS the
    /// enumeration algebra, isolating the HasKey kind as the sole excluder.
    /// </summary>
    [TestMethod]
    public void Jur3HasKeyExcludesTheEnumerationAlgebraCertifyingFace()
    {
        ReasoningModule baseline = new([Equivalent(Class("C"), OneOf("a", "b"))], Violations: []);
        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, ContextHabitatRecognizer.Classify(baseline, mentionsNominals: true, mentionsCounting: false), "Without the HasKey the module is enumeration-algebra shaped — the Σ_E baseline the certifying face reads.");

        ReasoningModule withHasKey = new([Equivalent(Class("C"), OneOf("a", "b")), HasKey(Class("K"), ["r"], [])], Violations: []);
        Assert.AreNotEqual(EnumerationHabitatClass.EnumerationAlgebra, ContextHabitatRecognizer.Classify(withHasKey, mentionsNominals: true, mentionsCounting: false), "The closed-world Σ_E kind gate rejects the HasKey kind, so the module is not the enumeration algebra — the certifying face stays Silent and unreachable.");
    }

    /// <summary>
    /// JUR-6: an ObjectHasSelf + told-data nominal module. The clausifier mints a
    /// self-loop concept (<c>GroundSelfLoopConcepts</c> non-empty), yet under the
    /// P-GC9 suppress no ground context exists, so the Self-ghost pass moves nothing
    /// and the verdict is sound and unmoved (the ghost-pass
    /// reason). The operative inertness reason is that the GroundGraph is empty on the
    /// nominal path — the self-loop machinery has no asserted-edge graph to re-close —
    /// so even the ground representation the suppress removes could contribute no
    /// clash.
    /// </summary>
    [TestMethod]
    public void Jur6ObjectHasSelfWithToldDataMovesNothingDespiteSelfLoopConcept()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Trigger(),
                SubClassOf(Class("A"), HasSelf("e")),
                ClassAssertion(Class("A"), Individual("s")),
                DataAssertion("s", "dp", "V-6"),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(clausification.NominalJurisdiction, "The trigger puts the module under nominal jurisdiction.");
        Assert.IsGreaterThan(0, clausification.GroundSelfLoopConcepts.Count, "The ObjectHasSelf axiom mints a self-loop concept — the ghost pass's GroundSelfLoopConcepts is non-empty.");
        Assert.IsEmpty(clausification.GroundRepresentatives, "The nominal class and data forks take no ground-representative side effect.");
        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.AreEqual(0, statistics.GroundContextsCreated, "No ground context exists on the nominal path — the P-GC9 suppress, despite the minted self-loop concept.");
        Assert.IsFalse(engine.IsInconsistent, "The module is sound and unmoved: the GroundGraph is empty on the nominal path, so the Self-ghost machinery contributes no clash.");
    }

    /// <summary>
    /// KVR-1: two named individuals sharing all data-key values join — the vr key
    /// join fires the merge <c>⊤ → o1 ≈ o2</c> as a root-fact continuation
    /// (SameAs entailed) and the module is consistent, decided by the root arm.
    /// The switch routes the
    /// HasKey+nominal module past the guard; the fired-union count and the
    /// consistent context decision are the join's two faces.
    /// </summary>
    [TestMethod]
    public void Kvr1TwoNamedIndividualsSharingDataKeyValuesJoinConsistent()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("o1")),
            ClassAssertion(Class("K"), Individual("o2")),
            DataAssertion("o1", "dk", "V"),
            DataAssertion("o2", "dk", "V"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the HasKey+nominal module whole under the switch.");
        Assert.IsTrue(decision.Verdict is ModuleVerdict, "A decided module carries a verdict.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The shared data-key value joins the pair (SameAs entailed) with no told distinctness, so the module is consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "The vr key join fired exactly one union continuation for the joined pair.");
    }

    /// <summary>
    /// KVR-2: the KVR-1 join against told <c>DifferentIndividuals(o1, o2)</c> —
    /// the join-forced merge collides with the told distinctness and the module is
    /// INCONSISTENT, decided by the root arm (guarded co-occurrence lifts spec
    /// section 4). The Ineq rule inherits the collision from the option-(a)
    /// continuation.
    /// </summary>
    [TestMethod]
    public void Kvr2JoinAgainstToldDifferentIndividualsInconsistent()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("o1")),
            ClassAssertion(Class("K"), Individual("o2")),
            DataAssertion("o1", "dk", "V"),
            DataAssertion("o2", "dk", "V"),
            Different("o1", "o2"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsTrue(decision.Verdict is ModuleVerdict, "A decided module carries a verdict.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The join-forced merge collides with the told DifferentIndividuals, so the module is inconsistent.");
    }

    /// <summary>
    /// KVR-5: an <c>Indeterminate</c> data-key value comparison delegates named:
    /// two candidates carry an
    /// unregistered datatype with differing lexical forms, so the value comparison
    /// abstains, the root join returns <see cref="RootKeyJoinOutcome.Indeterminate"/>,
    /// and the property is the existing <c>KeyValueComparisonIndeterminate</c> name.
    /// </summary>
    [TestMethod]
    public void Kvr5IndeterminateComparisonDelegatesNamed()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoin(
            [
                HasKey(Class("K"), [], ["dk"]),
                Trigger(),
                ClassAssertion(Class("K"), Individual("o1")),
                ClassAssertion(Class("K"), Individual("o2")),
                CustomDataAssertion("o1", "dk", "a", "custom"),
                CustomDataAssertion("o2", "dk", "b", "custom"),
            ],
            out _);

        RootKeyJoinOutcome outcome = engine.RunPostSaturationRootKeyJoin(ReasoningBudget.Unbounded, TestContext.CancellationToken, out _, out int fired);

        Assert.AreEqual(RootKeyJoinOutcome.Indeterminate, outcome, "The unregistered-datatype comparison abstains, so the join is indeterminate.");
        Assert.AreEqual(0, fired, "An indeterminate comparison never fires a merge.");
        Assert.IsTrue(engine.RootKeyIndeterminateProperty.Equals(Utf8Strings.From(Example + "dk")), "The indeterminate property is the data key.");
        Assert.AreEqual(ContextRemainderNames.KeyValueComparisonIndeterminate(Utf8Strings.From(Example + "dk")), ContextRemainderNames.KeyValueComparisonIndeterminate(engine.RootKeyIndeterminateProperty), "The delegation carries the existing KeyValueComparisonIndeterminate name for the property.");
    }

    /// <summary>
    /// KVR-7: a disjunctive key-class membership at a root constant delegates via
    /// the P-GC1 latch. A
    /// <c>C ⊑ K ⊔ D</c> told at <c>C(o)</c> derives the multi-literal head
    /// <c>⊤ → K(o) ∨ D(o)</c> on the root, and K is the key class, so the
    /// IsIndividual arm of the root latch fires <c>HasUndecidedRootKeyObligation</c>
    /// and the module delegates named (<c>KeyMembershipUndecidedOnRoot</c>).
    /// </summary>
    [TestMethod]
    public void Kvr7DisjunctiveKeyMembershipAtConstantLatchesRootObligation()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoin(
            [
                HasKey(Class("K"), [], ["dk"]),
                Trigger(),
                SubClassOf(Class("C"), Union(Class("K"), Class("D"))),
                ClassAssertion(Class("C"), Individual("o")),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.HasUndecidedRootKeyObligation, "The disjunctive K-membership at the root constant latches the root key obligation via the IsIndividual arm.");
        Assert.AreEqual(IndividualId(clausification, "o"), engine.UndecidedRootKeyIndividual, "The latch's ≈-resolved diagnostic names the constant carrying the uncertain membership.");
    }

    /// <summary>
    /// KVR-10: a join-fired merge enables a SECOND join, decided at the second
    /// fixpoint. Over the object key
    /// <c>r</c>, <c>a</c> and <c>b</c> share the target <c>x</c> and merge first;
    /// the merge makes <c>c</c>'s target <c>a</c> and <c>d</c>'s target <c>b</c>
    /// ≈-equal, so <c>c ≈ d</c> fires only in the second pass. The option-(a) loop
    /// re-runs the join at each new fixpoint until no pair fires.
    /// </summary>
    [TestMethod]
    public void Kvr10JoinFiredMergeEnablesSecondJoinAtFixpoint()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), ["r"], []),
            Trigger(),
            ClassAssertion(Class("K"), Individual("a")),
            ClassAssertion(Class("K"), Individual("b")),
            ClassAssertion(Class("K"), Individual("c")),
            ClassAssertion(Class("K"), Individual("d")),
            Edge("a", "r", "x"),
            Edge("b", "r", "x"),
            Edge("c", "r", "a"),
            Edge("d", "r", "b"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "No told distinctness, so both merges leave the module consistent.");
        Assert.AreEqual(2, decision.Statistics.ContextTotals.KeyForcedUnions, "The first merge (a≈b) enables the second (c≈d) only at the next fixpoint — two union continuations across two passes.");
    }

    /// <summary>
    /// KVR-11: a join-fired merge collides with a told <c>o ≉ o′</c> and the Ineq
    /// rule catches it — the option-(a) inheritance witness. Read at the engine,
    /// the join fires the merge and the
    /// re-saturation derives the empty clause, so the module reads inconsistent
    /// through the inherited collision rather than a pre-checked distinctness.
    /// </summary>
    [TestMethod]
    public void Kvr11JoinFiredMergeCollidesWithToldInequalityViaIneq()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoin(
            [
                HasKey(Class("K"), [], ["dk"]),
                Trigger(),
                ClassAssertion(Class("K"), Individual("o1")),
                ClassAssertion(Class("K"), Individual("o2")),
                DataAssertion("o1", "dk", "V"),
                DataAssertion("o2", "dk", "V"),
                Different("o1", "o2"),
            ],
            out _);

        RootKeyJoinOutcome outcome = engine.RunPostSaturationRootKeyJoin(ReasoningBudget.Unbounded, TestContext.CancellationToken, out _, out int fired);

        Assert.AreEqual(RootKeyJoinOutcome.Clean, outcome, "Every comparison was decisive; the collision reads off the inconsistency probe, not the join outcome.");
        Assert.AreEqual(1, fired, "The join fired the merge continuation before the collision was inherited.");
        Assert.IsTrue(engine.IsInconsistent, "The option-(a) continuation ⊤ → o1 ≈ o2 collides with the told o1 ≉ o2 and the Ineq rule derives the empty clause.");
    }

    /// <summary>
    /// KVR-12: the ≈-split spelling row — membership told under <c>o</c>, key value
    /// told under <c>o′</c>, and told <c>o ≈ o′</c> — joins a third candidate ONLY
    /// through the read-time union (the V-4 killer). The merged <c>{o, o′}</c>
    /// class pools <c>o</c>'s K
    /// membership and <c>o′</c>'s key value, so a third candidate <c>p</c> matching
    /// both joins the class; a direct single-spelling read would miss one half and
    /// never fire.
    /// </summary>
    [TestMethod]
    public void Kvr12SplitSpellingJoinsThroughReadTimeUnion()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("o")),
            DataAssertion("oPrime", "dk", "V"),
            Same("o", "oPrime"),
            ClassAssertion(Class("K"), Individual("p")),
            DataAssertion("p", "dk", "V"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "No told distinctness, so the join leaves the module consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "p joins the merged {o, oPrime} class ONLY because the read-time union pools o's membership with oPrime's key value — one fired union.");
    }

    /// <summary>
    /// KVR-15: an off-fold root equality between two key candidates that the
    /// ≈-class surface did not merge latches the backstop. A <c>B ⊑ {o1, o2}</c> told at
    /// <c>B(o)</c> derives the disjunctive equality <c>⊤ → o ≈ o1 ∨ o ≈ o2</c> on
    /// the root, whose sides the fold cannot merge, so <c>HasRootEqualityOutsideFold</c>
    /// latches and the module delegates named (<c>RootEqualityOutsideFold</c>) —
    /// conservative regardless of derivation channel.
    /// </summary>
    [TestMethod]
    public void Kvr15OffFoldRootEqualityBetweenCandidatesLatchesBackstop()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Class("K"), [], ["dk"]),
            SubClassOf(Class("B"), OneOf("o1", "o2")),
            ClassAssertion(Class("B"), Individual("o")),
        ];

        ContextSaturationEngine engine = SaturateWithRootKeyJoin(axioms, out _);
        Assert.IsTrue(engine.HasRootEqualityOutsideFold, "The disjunctive root equality o ≈ o1 ∨ o ≈ o2 leaves candidate sides the fold did not merge, so the backstop latches.");

        ModuleDecision decision = DecideWithRootKeyJoin(axioms);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The backstop delegates the module — the reasoner does not claim a context verdict.");
    }

    /// <summary>
    /// KVR-16: an OFF-ROOT-derived equality between two key candidates that reaches the
    /// root off the ≈-class fold latches the backstop (the
    /// off-root channel the KVR-15 in-root disjunctive witness does not itself exercise).
    /// The equality is derived in a genuine off-root context: the existential
    /// <c>A ⊑ ∃rf.F</c> mints a successor (filler) context whose core is F, and there the
    /// qualified counting bound <c>F ⊑ ≤1 s.G</c> would merge F's two named s-successors
    /// <c>o1</c> and <c>o2</c> — but only when both are G, and each is told merely
    /// <c>{oi} ⊑ G ⊔ H</c>, so the merge is UNDECIDED. The off-root context therefore
    /// derives the off-fold disjunctive equality <c>⊤ → H(o1) ∨ H(o2) ∨ o1 ≈ o2</c>, whose
    /// equality disjunct is a candidate pair the fold cannot fold: a landed equality feeds
    /// the ≈-surface only when it is unconditional AND single-literal, and this
    /// head is multi-literal, so the surface is never fed at all — it stays UNALLOCATED and
    /// merges no pair. The equality never landed as an unconditional root fact, yet the
    /// backstop scan over live root-class heads catches the equality literal between the two
    /// unmerged key candidates and latches <c>HasRootEqualityOutsideFold</c>, so the module
    /// delegates named. The unallocated ≈-surface is the live-run witness that the equality
    /// did not land as a root fact and the pair was never folded.
    /// </summary>
    [TestMethod]
    public void Kvr16OffRootEqualityOffTheFoldLatchesBackstop()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Class("K"), [], ["dk"]),
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), HasValue("s", "o1")),
            SubClassOf(Class("F"), HasValue("s", "o2")),
            SubClassOf(Class("F"), Max("s", 1, Class("G"))),
            SubClassOf(OneOf("o1"), Union(Class("G"), Class("H"))),
            SubClassOf(OneOf("o2"), Union(Class("G"), Class("H"))),
            ClassAssertion(Class("A"), Individual("a")),
        ];

        ContextSaturationEngine engine = SaturateWithRootKeyJoin(axioms, out ClausificationResult clausification);

        int idO1 = IndividualId(clausification, "o1");
        int idO2 = IndividualId(clausification, "o2");
        Assert.IsFalse(engine.HasRootApproxSurface, "The off-root equality reaches the root only inside a multi-literal disjunctive head, and the ≈-surface feed fires only for an unconditional single-literal equality — so no root-landed equality ever fed the ≈-class surface: it stays unallocated (the live-run witness that the equality did not land as an unconditional root fact).");
        Assert.IsFalse(engine.RootApproxSameClass(idO1, idO2), "The ≈-surface merged no pair, so o1 and o2 stay in distinct classes — the off-fold identity the read-time union cannot see.");
        Assert.IsTrue(engine.HasRootEqualityOutsideFold, "The backstop scan over live root-class heads catches the off-root-derived equality literal between the two unmerged key candidates and latches — the off-root channel, caught on a live run.");

        ModuleDecision decision = DecideWithRootKeyJoin(axioms);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The backstop delegates the module — the reasoner does not claim a context verdict on the off-root off-fold equality.");
    }

    /// <summary>
    /// KVR-17: the PURE body-nonempty <c>A → A</c> tautology-relay channel — the
    /// channel the KVR-16 qualified-bound
    /// variant does NOT itself exercise. Off-root equalities between key candidates that stay
    /// UNDECIDED reach the root as body-NONEMPTY tautologies whose heads carry the equality, and
    /// the backstop — not the ≈-class fold — is the mechanism that delegates. The existential
    /// <c>A ⊑ ∃rf.F</c> mints a genuine off-root successor (filler) context whose core is F. There
    /// the three named value-successors <c>∃s.{o1}</c>, <c>∃s.{o2}</c>, <c>∃s.{o3}</c> collide
    /// under the unqualified counting bound <c>F ⊑ ≤2 s</c>: at most two distinct s-fillers may
    /// survive among three named ones, so by pigeonhole SOME pair must merge — but WHICH pair is a
    /// genuine choice, so the counting decides no pair unconditionally and the merge obligation
    /// stays DISJUNCTIVE. No single-literal unconditional equality ever lands on the root, so the
    /// ≈-surface (fed only by a body-length-zero AND head-length-one equality) is never
    /// fed: it stays UNALLOCATED and merges no pair, and the read-time union sees no relayed
    /// identity. The candidate equalities are shipped by the counting relay (<c>TryRootSucc</c>,
    /// <c>AddPushedClause(target, DlClause.Create([seed],[seed],…))</c>) into the root
    /// class as body-nonempty tautology clauses <c>oi = oj → oi = oj</c>: the head carries the
    /// equality, the body repeats it, so each clause is conditional at the root, never a decided
    /// unconditional fact. The backstop scan over live root-class heads catches the body-nonempty
    /// tautology equality literal between two unmerged key candidates and latches
    /// <c>HasRootEqualityOutsideFold</c>, so the module delegates named
    /// (<c>RootEqualityOutsideFold</c>) and is NOT decided inconsistent. The unallocated ≈-surface
    /// with every candidate still distinct is the live-run witness that no equality landed as an
    /// unconditional root fact and the fold merged nothing, so the delegation is attributable to
    /// the backstop over the relayed tautologies alone: the body-nonempty <c>A → A</c> channel is
    /// what fires. KVR-16's qualified-bound disjunctive head reaches the same unallocated-surface,
    /// backstop-latched end state through a different derivation channel; this row pins the pure
    /// tautology-relay channel.
    /// </summary>
    [TestMethod]
    public void Kvr17PureAToATautologyRelayLatchesBackstop()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Class("K"), [], ["dk"]),
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), HasValue("s", "o1")),
            SubClassOf(Class("F"), HasValue("s", "o2")),
            SubClassOf(Class("F"), HasValue("s", "o3")),
            SubClassOf(Class("F"), Max("s", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
        ];

        ContextSaturationEngine engine = SaturateWithRootKeyJoin(axioms, out ClausificationResult clausification);

        int idO1 = IndividualId(clausification, "o1");
        int idO2 = IndividualId(clausification, "o2");
        int idO3 = IndividualId(clausification, "o3");

        Assert.IsTrue(clausification.Symbols.IsKeyJoinCandidateOrigin(idO3), "The relayed successor o3 interns IRI-denoted and depth-zero through the value-restriction filler site, so it is a key-join candidate — the backstop precondition the relayed equality's side satisfies.");
        Assert.IsFalse(engine.HasRootApproxSurface, "Which pair the ≤2-over-three pigeonhole merges is a genuine choice, so the counting decides no pair unconditionally and the merge stays disjunctive; the ≈-class surface fires only for an unconditional single-literal equality, so no root-landed equality ever fed it — it stays unallocated, the live-run witness that no equality landed as an unconditional root fact.");

        int mergedPairs = (engine.RootApproxSameClass(idO1, idO2) ? 1 : 0)
            + (engine.RootApproxSameClass(idO1, idO3) ? 1 : 0)
            + (engine.RootApproxSameClass(idO2, idO3) ? 1 : 0);
        Assert.AreEqual(0, mergedPairs, "The disjunctive merge obligation folds no candidate pair, so all three successors stay in distinct root-tier ≈-classes — the off-fold identities the read-time union cannot see.");

        Assert.IsTrue(engine.HasRootEqualityOutsideFold, "The backstop scan over live root-class heads catches the body-nonempty A → A equality literals between unmerged key candidates and latches — the pure tautology-relay channel, caught on a live run.");
        Assert.IsFalse(engine.IsInconsistent, "No told distinctness, so the relayed off-root equalities force no Ineq clash — the saturation is consistent and the module is not decided inconsistent.");

        ModuleDecision decision = DecideWithRootKeyJoin(axioms);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The backstop over the relayed tautologies delegates the module named on the RootEqualityOutsideFold remainder; no pair folds, so the delegation is attributable to the backstop alone — the reasoner claims no context verdict and does not decide the module inconsistent.");
    }

    /// <summary>
    /// KVR-18: the off-fold equality backstop's latch surfaces through the module
    /// decision's statistics — the
    /// corpus census the boolean latch alone cannot carry, since the backstop delegates
    /// and the fallback's decision otherwise leaves the context totals empty. The KVR-15
    /// witness (a disjunctive root equality between two key candidates the fold cannot
    /// merge) delegates on the backstop, and the delegated decision now carries the
    /// latched off-fold-equality head count on its context totals; a nominal-free module
    /// the context engine decides carries zero, never arming the backstop; and an armed
    /// HasKey+nominal module the root arm decides carries zero with a minted root context
    /// — the folded-clean zero, distinct from the nominal-free unarmed zero — so the count
    /// reads the backstop demand per module and discriminates armed-but-folded from unarmed.
    /// </summary>
    [TestMethod]
    public void Kvr18BackstopLatchHeadCountSurfacesOnDecisionStatistics()
    {
        OwlAxiom[] latching =
        [
            HasKey(Class("K"), [], ["dk"]),
            SubClassOf(Class("B"), OneOf("o1", "o2")),
            ClassAssertion(Class("B"), Individual("o")),
        ];

        ModuleDecision latchingDecision = DecideWithRootKeyJoin(latching);
        Assert.IsFalse(latchingDecision.Statistics.ContextTotals.ContextDecided, "The backstop delegates the module, so the reasoner claims no context verdict.");
        Assert.IsGreaterThan(0L, latchingDecision.Statistics.ContextTotals.RootEqualityOutsideFoldHeads, "The backstop's latched off-fold-equality head count surfaces on the delegated decision's context totals — the corpus census surface the plain delegation could not carry.");

        ModuleDecision nominalFreeDecision = DecideWithRootKeyJoin(
            SubClassOf(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("a")));
        Assert.IsTrue(nominalFreeDecision.Statistics.ContextTotals.ContextDecided, "A nominal-free taxonomy is decided by context saturation.");
        Assert.AreEqual(0L, nominalFreeDecision.Statistics.ContextTotals.RootEqualityOutsideFoldHeads, "A nominal-free module never arms the backstop, so its off-fold-equality head count reads zero.");

        ModuleDecision armedFoldedDecision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("k1")),
            DataAssertion("k1", "dk", "V-1"));
        Assert.IsTrue(armedFoldedDecision.Statistics.ContextTotals.ContextDecided, "The HasKey+nominal module decides via the root arm, so the context engine carries the verdict.");
        Assert.IsGreaterThan(0, armedFoldedDecision.Statistics.ContextTotals.NominalRootContexts, "The HasKey+nominal module mints a root-class context, so the backstop is armed — unlike the nominal-free row that mints none.");
        Assert.AreEqual(0L, armedFoldedDecision.Statistics.ContextTotals.RootEqualityOutsideFoldHeads, "An armed module whose fold covers every root equality reads zero off-fold-equality heads — the folded-clean zero, distinct from the nominal-free unarmed zero.");
    }

    /// <summary>
    /// JUR-1 (the decide half): with the switch ON, the HasKey+nominal
    /// module's told data-key value lands in the key-value store and the module
    /// DECIDES via the root arm with zero ground/root cross-talk (the P-GC9
    /// witness). A single keyed candidate joins no
    /// pair, so the module is consistent, decided, and the setup mints no ground
    /// context — the guard-lifted counterpart of the reachable half.
    /// </summary>
    [TestMethod]
    public void Jur1HasKeyNominalDataKeyModuleDecidesUnderRootKeyJoin()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("k1")),
            DataAssertion("k1", "dk", "V-1"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The switch routes the HasKey+nominal module past the guard and the root arm decides it.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A single keyed candidate joins no pair, so the module is consistent.");
        Assert.AreEqual(0, decision.Statistics.ContextTotals.GroundContextsCreated, "The nominal path mints no ground context — the module decides without ground/root cross-talk.");
    }

    /// <summary>
    /// DVR-1: the cross-contamination killer. Two constants each carry
    /// individually-satisfiable demands —
    /// <c>o1</c> a value above five, <c>o2</c> a value below three under an all-values
    /// universal — whose whole-context union would clash (above five cannot be below
    /// three). The constants are NOT ≈-merged, so the per-CLASS pooling decides each
    /// class in isolation and the module stays CONSISTENT; a naive one-context pool
    /// would wrongly clash. Under the switch the per-constant arm decides each class, so
    /// the <c>RootDataDemandObserved</c> statistic stays clear (no arm-off landing).
    /// </summary>
    [TestMethod]
    public void Dvr1UnmergedCrossUnsatisfiableDemandsStayConsistent()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligations(
            [
                SubClassOf(OneOf("o1"), DataSome("dp", IntegerAbove(5))),
                SubClassOf(OneOf("o2"), DataSome("dp", IntegerBelow(3))),
                SubClassOf(OneOf("o2"), DataAll("dp", IntegerBelow(3))),
            ],
            out _);

        Assert.IsFalse(engine.IsInconsistent, "o1's demand (a value above 5) and o2's demands (a value below 3, all values below 3) are each individually satisfiable; the two constants are not ≈-merged, so the per-class pooling never forms the cross-constant clash a naive whole-context pool would.");
        Assert.IsFalse(engine.RootDataDemandObserved, "The per-constant arm decides each unit class under the switch, so the arm-off statistic never records.");
        Assert.IsFalse(engine.HasDataObligationUndecidedOnRoot, "Both classes decide, so no undecided delegation latches.");
    }

    /// <summary>
    /// DVR-2: one constant's jointly-unsatisfiable demands decide INCONSISTENT via the
    /// per-class closure <c>⊤ → Bottom(o)</c>. <c>o</c> pools an existential above five
    /// and a universal below
    /// three; the sidecar refutes the conjunction and the closure propagates through the
    /// virtual <c>Bottom(o) → ⊥</c> root form. Driven through the reasoner's root
    /// data-obligation entry, so the threaded switch and the decide-not-delegate flip
    /// are both exercised.
    /// </summary>
    [TestMethod]
    public void Dvr2SingleConstantJointlyUnsatisfiableDemandsInconsistent()
    {
        ModuleDecision decision = DecideWithRootDataObligations(
            SubClassOf(OneOf("o"), DataSome("dp", IntegerAbove(5))),
            SubClassOf(OneOf("o"), DataAll("dp", IntegerBelow(3))));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The per-constant root arm decides the module whole under the switch — the delegate-to-decide flip the dark latch would otherwise block.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "o's pooled demand set — an existential above 5 and a universal below 3 — is jointly unsatisfiable, so the per-class closure ⊤ → Bottom(o) collapses the module.");
    }

    /// <summary>
    /// DVR-3: merged constants' split demands clash only pooled (the
    /// re-probe-on-merge witness). <c>o</c>
    /// carries the existential above five and <c>oPrime</c> the universal below three —
    /// each satisfiable alone. The told <c>SameIndividual(o, oPrime)</c> merges the two
    /// ≈-classes, so the pooled demand set of the merged class clashes and the module is
    /// INCONSISTENT — the clash appears only through the read-time union over the merge.
    /// </summary>
    [TestMethod]
    public void Dvr3MergedConstantsSplitDemandsClashOnlyPooled()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligations(
            [
                SubClassOf(OneOf("o"), DataSome("dp", IntegerAbove(5))),
                SubClassOf(OneOf("oPrime"), DataAll("dp", IntegerBelow(3))),
                Same("o", "oPrime"),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "o's existential above 5 and oPrime's universal below 3 are each individually satisfiable, but the told o ≈ oPrime merges the classes so the pooled demand set clashes — the clash exists only pooled over the merge.");
    }

    /// <summary>
    /// DVR-4: an undecided datatype delegates named per-constant. A lone
    /// <c>xsd:string</c> existential is an obligation the
    /// value-space checker cannot size, so the per-constant arm latches
    /// <c>HasDataObligationUndecidedOnRoot</c> and records the demand property — the
    /// reasoner then delegates named (<c>DataObligationUndecidedOnRoot(property)</c>),
    /// never a wrong verdict.
    /// </summary>
    [TestMethod]
    public void Dvr4UndecidedDatatypeDelegatesNamedOnRoot()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligations(
            [
                SubClassOf(OneOf("o"), DataSome("dp", StringType)),
            ],
            out _);

        Assert.IsTrue(engine.HasDataObligationUndecidedOnRoot, "A lone xsd:string existential is an undecided obligation the checker cannot size, so the per-constant arm latches the named root delegation.");
        Assert.IsFalse(engine.IsInconsistent, "An undecided obligation is never a clash.");
        Assert.Contains(Example + "dp", engine.UndecidedDataObligationProperties, "The undecided delegation records the demand property IRI.");
        Assert.AreEqual($"DataObligationUndecidedOnRoot({Example}dp)", ContextRemainderNames.DataObligationUndecidedOnRoot(Utf8Strings.From(Example + "dp")), "The delegation carries the DataObligationUndecidedOnRoot name for the demand property.");
    }

    /// <summary>
    /// DVR-6: a late-landing equality re-probes to INCONSISTENT (the
    /// pure-membership-merge witness). Both demands
    /// land FIRST — <c>o</c>'s existential above five and <c>oPrime</c>'s universal below
    /// three, each class decided consistent — then the two-hop told chain
    /// <c>B1 ⊑ B2 ⊑ {o}</c> with <c>B1(oPrime)</c> derives the merge <c>oPrime ≈ o</c>
    /// LATE in saturation. The equality rewrites a demand marker onto the merged
    /// representative and the re-probe hook re-decides the class off the read-time union,
    /// pooling both demands into the clash a marker-only signature memo would miss.
    /// </summary>
    [TestMethod]
    public void Dvr6LateLandingEqualityReprobesToInconsistent()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligations(
            [
                SubClassOf(OneOf("o"), DataSome("dp", IntegerAbove(5))),
                SubClassOf(OneOf("oPrime"), DataAll("dp", IntegerBelow(3))),
                SubClassOf(Class("B1"), Class("B2")),
                SubClassOf(Class("B2"), OneOf("o")),
                ClassAssertion(Class("B1"), Individual("oPrime")),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "Both demands land first (each class decided consistent); the two-hop chain B1(oPrime) → B2(oPrime) → oPrime ≈ o derives the merge late, and the re-probe hook re-decides the merged class off the read-time union so the pooled demands clash.");
    }

    /// <summary>
    /// DVR-7: a true negative after a merge stays CONSISTENT (the DVR-3 dual,
    /// catching a spurious double-count). The told
    /// <c>SameIndividual(o, oPrime)</c> fires the merge, but the pooled demand set — an
    /// existential above five and an existential above three — stays jointly satisfiable
    /// (a value above five realizes both), so the merge adds no clash.
    /// </summary>
    [TestMethod]
    public void Dvr7TrueNegativeAfterMergeStaysConsistent()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligations(
            [
                SubClassOf(OneOf("o"), DataSome("dp", IntegerAbove(5))),
                SubClassOf(OneOf("oPrime"), DataSome("dp", IntegerAbove(3))),
                Same("o", "oPrime"),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsInconsistent, "The told o ≈ oPrime merges the classes and the pooled demand set stays jointly satisfiable (a value above 5 realizes both existentials), so the merge fires no spurious double-count clash.");
        Assert.IsTrue(engine.RootApproxSameClass(IndividualId(clausification, "o"), IndividualId(clausification, "oPrime")), "The told SameIndividual merged the two classes — the merge fired but pooled satisfiably.");
    }

    /// <summary>
    /// COF-1: the co-fire row with BOTH switches ON. Over the data key <c>dk</c>,
    /// <c>o1</c> and <c>o2</c> share
    /// the value V, so the vr key join fires the merge <c>⊤ → o1 ≈ o2</c> as a root-fact
    /// continuation on the SAME root-tier substrate. <c>o1</c> demands a value
    /// above five and <c>o2</c> constrains all values below three — individually
    /// satisfiable pre-merge. The fired union triggers the lift-2 re-probe on the merged
    /// class, whose pooled demands are jointly unsatisfiable, so the module decides
    /// INCONSISTENT at the post-merge fixpoint.
    /// </summary>
    [TestMethod]
    public void Cof1KeyJoinMergeTriggersLiftTwoReprobeInconsistent()
    {
        ModuleDecision decision = DecideWithRootLifts(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("o1")),
            ClassAssertion(Class("K"), Individual("o2")),
            DataAssertion("o1", "dk", "V"),
            DataAssertion("o2", "dk", "V"),
            SubClassOf(OneOf("o1"), DataSome("dp", IntegerAbove(5))),
            SubClassOf(OneOf("o2"), DataAll("dp", IntegerBelow(3))));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "Both lifts run on the shared substrate and the module decides at the post-merge fixpoint.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The key join fires ⊤ → o1 ≈ o2 on the shared data key value, the lift-2 re-probe re-decides the merged class, and the pooled demands — an existential above 5 and a universal below 3, individually satisfiable pre-merge — clash post-merge.");
    }

    /// <summary>
    /// KVR-3: object-key agreement through a DERIVED role edge. The keyed pair's
    /// shared role edge is
    /// not a told <c>ObjectPropertyAssertion</c> but derived from the told
    /// subsumptions <c>{a} ⊑ ∃r.{x}</c> and <c>{b} ⊑ ∃r.{x}</c>, so <c>r(a, x)</c>
    /// and <c>r(b, x)</c> land as derived root facts. Over the object key <c>r</c>
    /// the two candidates share the ≈-class of the target <c>x</c>, so the vr key
    /// join fires the merge (SameAs entailed) and the module is consistent, decided
    /// by the root arm — the derived edge feeds the per-constant index exactly as a
    /// told one would.
    /// </summary>
    [TestMethod]
    public void Kvr3ObjectKeyAgreementViaDerivedRoleEdgeJoins()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), ["r"], []),
            Trigger(),
            ClassAssertion(Class("K"), Individual("a")),
            ClassAssertion(Class("K"), Individual("b")),
            SubClassOf(OneOf("a"), HasValue("r", "x")),
            SubClassOf(OneOf("b"), HasValue("r", "x")));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the HasKey+nominal module whole under the switch.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The derived shared r-edge joins the pair (SameAs entailed) with no told distinctness, so the module is consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "a and b share the DERIVED object-key target x, so the vr key join fires exactly one union continuation.");
    }

    /// <summary>
    /// KVR-4: typed data-key variants join across differing lexical spellings.
    /// The two candidates carry the
    /// data-key value under differing <c>xsd:integer</c> lexical forms — <c>1</c>
    /// and <c>01</c> — which are byte-distinct yet value-space-equal, so the
    /// datatype checker answers <see cref="DatatypeValueIdentity.Same"/> and the vr
    /// key join fires the merge (SameAs entailed) on value-space equality, not
    /// lexical identity. The module is consistent, decided by the root arm.
    /// </summary>
    [TestMethod]
    public void Kvr4TypedDataKeyVariantsJoinAcrossLexicalSpellings()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("K"), Individual("o1")),
            ClassAssertion(Class("K"), Individual("o2")),
            IntegerDataAssertion("o1", "dk", "1"),
            IntegerDataAssertion("o2", "dk", "01"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The byte-distinct but value-space-equal integers 1 and 01 join the pair (SameAs entailed), so the module is consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "The value-space comparison fires exactly one union across the two lexical spellings — the join reads values, not lexical forms.");
    }

    /// <summary>
    /// KVR-6: keyed-class membership arriving DERIVED through a told chain joins.
    /// Neither candidate is a told
    /// K member; both are told <c>B1</c> members and <c>B1 ⊑ K</c> derives the
    /// certain root fact <c>K(o1)</c>, <c>K(o2)</c> that the candidate read pools
    /// through the per-constant index. The two derived K members share the
    /// data-key value V, so the vr key join fires (SameAs entailed) and the module
    /// is consistent — told membership is not required, derived-certain suffices.
    /// </summary>
    [TestMethod]
    public void Kvr6DerivedKeyedClassMembershipThroughToldChainJoins()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            SubClassOf(Class("B1"), Class("K")),
            ClassAssertion(Class("B1"), Individual("o1")),
            ClassAssertion(Class("B1"), Individual("o2")),
            DataAssertion("o1", "dk", "V"),
            DataAssertion("o2", "dk", "V"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "o1 and o2 derive K-membership through the told B1 ⊑ K chain and share the data-key value V, so the join fires and the module is consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "The derived-certain K membership qualifies both as candidates, so the shared value fires exactly one union.");
    }

    /// <summary>
    /// KVR-8: a Nom-minted generated sibling NEVER becomes a key-join candidate
    /// (the P-GC5 depth conjunct). The inverse-counting
    /// Nom habitat mints generated-nominal siblings mid-saturation; each interns at
    /// label depth one, so the depth conjunct of the candidacy filter excludes it
    /// even though it carries the candidate origin bit. The named input
    /// individuals stay candidates; the join enumerates only them and fires no
    /// union — the generated sibling never joins.
    /// </summary>
    [TestMethod]
    public void Kvr8GeneratedNominalSiblingNeverBecomesAKeyJoinCandidate()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoin(
            [
                Inverse("r", "rInv"),
                SubClassOf(Class("B"), Some("s", Class("A"))),
                SubClassOf(Class("A"), HasValue("r", "o")),
                SubClassOf(OneOf("o"), MaxInverse("r", 1, null)),
                ClassAssertion(Class("B"), Individual("w")),
                HasKey(Class("A"), [], ["dk"]),
            ],
            out ClausificationResult clausification);

        ContextSaturationStatistics totals = engine.BuildStatistics(contextDecided: false);
        Assert.IsGreaterThan(0, totals.GeneratedNominals, "The inverse-counting Nom habitat minted at least one generated nominal sibling.");

        int generatedCount = 0;
        for(int id = 0; id < clausification.Symbols.IndividualCount; id++)
        {
            if(clausification.Symbols.IndividualDepth(id) > 0)
            {
                generatedCount++;
                Assert.IsFalse(clausification.Symbols.IsKeyJoinCandidateOrigin(id), "A generated nominal sibling is excluded from key-join candidacy by the depth conjunct despite carrying the candidate origin bit.");
            }
        }

        Assert.IsGreaterThan(0, generatedCount, "At least one generated nominal sibling exists to exercise the exclusion.");
        RootKeyJoinOutcome outcome = engine.RunPostSaturationRootKeyJoin(ReasoningBudget.Unbounded, TestContext.CancellationToken, out _, out int fired);
        Assert.AreEqual(RootKeyJoinOutcome.Clean, outcome, "The join completes cleanly over the named candidates.");
        Assert.AreEqual(0, fired, "No generated nominal sibling ever enters candidacy, so the join fires no union — the Nom-minted sibling never joins.");
    }

    /// <summary>
    /// KVR-9: a told-anonymous (blank-node) individual sharing ALL key values
    /// NEVER joins (the P-GC5 origin-bit bookkeeping, distinct from KVR-8's
    /// depth conjunct). The blank
    /// node is a told K member carrying the same data-key value V as the named
    /// candidate, yet the ORIGIN conjunct — blank-node origin — excludes it while
    /// its depth stays zero. The named K member is a candidate; the blank node is
    /// not, so the pair never forms and the join fires no union.
    /// </summary>
    [TestMethod]
    public void Kvr9ToldAnonymousIndividualSharingKeyValuesNeverJoins()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoin(
            [
                HasKey(Class("K"), [], ["dk"]),
                Trigger(),
                ClassAssertion(Class("K"), Individual("o1")),
                ClassAssertion(Class("K"), Blank("b0")),
                DataAssertion("o1", "dk", "V"),
                BlankDataAssertion("b0", "dk", "V"),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From("b0"), out int blank), "The blank-node K member interned under its label.");
        Assert.AreEqual(0, clausification.Symbols.IndividualDepth(blank), "The blank node interns at depth zero, so the depth conjunct passes — only the origin bit can exclude it.");
        Assert.AreEqual(IndividualOrigin.BlankNode, clausification.Symbols.OriginOf(blank), "The told-anonymous subject records the blank-node origin.");
        Assert.IsFalse(clausification.Symbols.IsKeyJoinCandidateOrigin(blank), "The origin conjunct excludes the blank node from key-join candidacy.");
        Assert.IsTrue(clausification.Symbols.IsKeyJoinCandidateOrigin(IndividualId(clausification, "o1")), "The named K member IS a candidate — the exclusion is the blank node's origin bit, not the shared key shape.");

        RootKeyJoinOutcome outcome = engine.RunPostSaturationRootKeyJoin(ReasoningBudget.Unbounded, TestContext.CancellationToken, out _, out int fired);
        Assert.AreEqual(RootKeyJoinOutcome.Clean, outcome, "The join completes cleanly.");
        Assert.AreEqual(0, fired, "The blank node shares o1's K-membership and dk value V yet is excluded by its origin bit, so the pair never forms and the join fires no union.");
    }

    /// <summary>
    /// KVR-13: a disjunctive key-class membership that NARROWS to certain BEFORE
    /// the fixpoint DECIDES (joins), not delegates — the over-eager-latch
    /// killer. A
    /// <c>C ⊑ K ⊔ D</c> told at <c>C(o)</c> derives the multi-literal head
    /// <c>⊤ → K(o) ∨ D(o)</c>, but <c>D ⊑ ⊥</c> closes the D disjunct so the head
    /// narrows to the certain <c>⊤ → K(o)</c> and the disjunctive clause is
    /// subsumed away before the latch scan. With no live multi-literal head the
    /// root key latch must NOT fire; the narrowed-certain o joins the told K member
    /// o2 on the shared value and the module decides consistent. If the latch fires
    /// here — the dead disjunctive clause still seen — that is a defect in the
    /// latch scan.
    /// </summary>
    [TestMethod]
    public void Kvr13DisjunctiveKeyMembershipNarrowedBeforeFixpointDecidesWithoutLatch()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            SubClassOf(Class("C"), Union(Class("K"), Class("D"))),
            ClassAssertion(Class("C"), Individual("o")),
            SubClassOf(Class("D"), Nothing),
            DataAssertion("o", "dk", "V"),
            ClassAssertion(Class("K"), Individual("o2")),
            DataAssertion("o2", "dk", "V"),
        ];

        ContextSaturationEngine engine = SaturateWithRootKeyJoin(axioms, out _);
        Assert.IsFalse(engine.HasUndecidedRootKeyObligation, "The disjunctive K-membership narrowed to the certain K(o) before the fixpoint — the closed D disjunct left no live multi-literal head, so the over-eager latch must NOT fire.");

        ModuleDecision decision = DecideWithRootKeyJoin(axioms);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "With no latch the root arm decides the module whole — the narrowed membership joins, it does not delegate.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "o (narrowed to a certain K member) and o2 (told K) share the data-key value V, so the join fires and the module is consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "The narrowed-certain o joins o2 on the shared value — exactly one union.");
    }

    /// <summary>
    /// KVR-14: an object-key TARGET-side ≈-split joins through the read-time union
    /// (the P-GC3 target leg). The keyed pair's role edges spell the target differently —
    /// <c>r(a, t)</c> and <c>r(b, tPrime)</c> — and a told <c>t ≈ tPrime</c> merges
    /// the target-side ≈-classes, so a and b share the object-key target ONLY
    /// through the union. The vr key join fires the merge (SameAs entailed) and the
    /// module is consistent — the KVR-12 discipline carried onto the target leg.
    /// </summary>
    [TestMethod]
    public void Kvr14ObjectKeyTargetSideSplitJoinsThroughReadTimeUnion()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), ["r"], []),
            Trigger(),
            ClassAssertion(Class("K"), Individual("a")),
            ClassAssertion(Class("K"), Individual("b")),
            Edge("a", "r", "t"),
            Edge("b", "r", "tPrime"),
            Same("t", "tPrime"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The told t ≈ tPrime merges the target-side ≈-classes, so a and b share the object-key target and join (SameAs entailed) — consistent, the target-leg verdict.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.KeyForcedUnions, "a and b share the object-key target ONLY through the target-side read-time union — one fired union.");
    }

    /// <summary>
    /// NM-1: an ≈-class spanning a generated nominal pools the generated spelling's
    /// facts WITHOUT extending candidacy. A named prefix individual and its
    /// generated-nominal sibling merge into
    /// one ≈-class; a fact stored under the generated spelling pools through the
    /// class into the named individual's read-time union, so the named-only filter
    /// reads through the class — yet the generated id (label depth one) is never a
    /// key-join candidate. Driven at the shared root-tier surface directly,
    /// mirroring the same-cycle projection the engine runs, so the pooling and the
    /// depth-conjunct exclusion are both witnessed on one merged class.
    /// </summary>
    [TestMethod]
    public void Nm1ApproxClassSpanningGeneratedNominalPoolsFactsWithoutExtendingCandidacy()
    {
        ContextSymbolTable symbols = new();
        int named = symbols.InternIndividual(Utf8Strings.From(Example + "o"), IndividualOrigin.IriDenoted);
        Assert.IsTrue(symbols.MintGeneratedNominal(named, roleId: 0, count: 1, out int generated), "The first mint for the (prefix, role) pair mints a fresh generated sibling.");
        Assert.IsTrue(symbols.IsKeyJoinCandidateOrigin(named), "The named prefix individual is a key-join candidate.");
        Assert.IsFalse(symbols.IsKeyJoinCandidateOrigin(generated), "Candidacy does NOT extend to the generated nominal — the depth conjunct excludes it even inside the merged class.");

        RootConstantIndex index = new();
        HashSet<int> markers = [];
        const int generatedSpellingConcept = 7;
        index.Project(DlLiteral.Concept(generatedSpellingConcept, DlTerm.Individual(generated)), homeIndividual: -1, markers);

        RootApproxClasses classes = new();
        classes.Union(named, generated);
        Assert.IsTrue(classes.SameClass(named, generated), "The ≈-class spans the named individual and the generated nominal.");

        List<int> spellings = [];
        classes.AppendClassMembers(named, spellings);
        List<int> pooled = [];
        foreach(int spelling in spellings)
        {
            index.AppendConceptMemberships(spelling, pooled);
        }

        Assert.Contains(generatedSpellingConcept, pooled, "The generated spelling's fact pools through the merged class into the named individual's read-time union — the facts leak through the merged spelling.");
        List<int> namedDirect = [];
        index.AppendConceptMemberships(named, namedDirect);
        Assert.DoesNotContain(generatedSpellingConcept, namedDirect, "A direct read of the named spelling misses the generated spelling's fact — the pooling happens ONLY through the ≈-union, and candidacy still does not extend to the generated id.");
    }

    /// <summary>
    /// NM-2: a keyed class with ZERO root members decides with no latch, no join,
    /// and no delegation from the key machinery (guarded co-occurrence lifts spec
    /// section 4). The HasKey names a class K that no individual joins; the join's
    /// candidate enumeration over K is empty, so it fires nothing and the root arm
    /// decides the module consistent. No key-class membership is uncertain, so the
    /// root key latch never fires.
    /// </summary>
    [TestMethod]
    public void Nm2KeyedClassWithZeroRootMembersDecidesWithoutLatchOrJoin()
    {
        OwlAxiom[] axioms =
        [
            HasKey(Class("K"), [], ["dk"]),
            Trigger(),
            ClassAssertion(Class("A"), Individual("o1")),
            DataAssertion("o1", "dk", "V"),
        ];

        ContextSaturationEngine engine = SaturateWithRootKeyJoin(axioms, out _);
        Assert.IsFalse(engine.HasUndecidedRootKeyObligation, "No individual is a K member, so no key-class membership is uncertain and the root key latch never fires.");

        ModuleDecision decision = DecideWithRootKeyJoin(axioms);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The keyed class has zero root members, so the join enumerates empty and the root arm decides the module whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "An empty keyed class forces no merge, so the module is consistent.");
        Assert.AreEqual(0, decision.Statistics.ContextTotals.KeyForcedUnions, "Zero root members join no pair — no union fires and the key machinery delegates nothing.");
    }

    /// <summary>
    /// NM-3: a transitive fired-union collision decides INCONSISTENT via Ineq at
    /// the SECOND fixpoint. Over the
    /// object key <c>r</c>, a and b share the target x and merge first (a ≈ b);
    /// that merge makes c's target a and d's target b ≈-equal, so the second pass
    /// fires c ≈ d. The told <c>c ≉ d</c> collides with that second-round fired
    /// union and the Ineq rule derives the empty clause — the collision surfaces
    /// only after the first round's merge composes transitively.
    /// </summary>
    [TestMethod]
    public void Nm3TransitiveFiredUnionCollisionInconsistentAtSecondFixpoint()
    {
        ModuleDecision decision = DecideWithRootKeyJoin(
            HasKey(Class("K"), ["r"], []),
            Trigger(),
            ClassAssertion(Class("K"), Individual("a")),
            ClassAssertion(Class("K"), Individual("b")),
            ClassAssertion(Class("K"), Individual("c")),
            ClassAssertion(Class("K"), Individual("d")),
            Edge("a", "r", "x"),
            Edge("b", "r", "x"),
            Edge("c", "r", "a"),
            Edge("d", "r", "b"),
            Different("c", "d"));

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The root arm decides the module whole under the switch.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The first-round merge a ≈ b makes c's and d's targets ≈-equal, firing c ≈ d at the second fixpoint; that fired union collides with the told c ≉ d and the Ineq rule derives the empty clause.");
    }

    /// <summary>
    /// DVR-5: a disjunctive root data demand that NARROWS to a unit before the
    /// fixpoint refutes through the normal demand-landing fork (the scope
    /// boundary — the
    /// measured disposition). <c>{o} ⊑ (∃dp.&gt;5) ⊔ E</c> lands the disjunctive
    /// head <c>⊤ → marker(o) ∨ E(o)</c> on the root, but <c>E ⊑ ⊥</c> closes the
    /// E disjunct so it narrows to the unit demand <c>∃dp.&gt;5(o)</c>; that unit
    /// lands through the per-constant root arm and pools with the told universal
    /// <c>∀dp.&lt;3(o)</c>, which is jointly unsatisfiable, so the module decides
    /// INCONSISTENT — the disjunctive-marker delegation does NOT govern the
    /// narrowed unit (<c>RootDataDemandObserved</c> stays clear because the multi-literal
    /// marker head does not survive as a live root head once narrowed). This pins
    /// the actual behavior: the narrowed unit decides, it does not delegate.
    /// </summary>
    [TestMethod]
    public void Dvr5DisjunctiveRootDataDemandNarrowsAndRefutesThroughTheNormalFork()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligations(
            [
                SubClassOf(OneOf("o"), DataAll("dp", IntegerBelow(3))),
                SubClassOf(OneOf("o"), Union(DataSome("dp", IntegerAbove(5)), Class("E"))),
                SubClassOf(Class("E"), Nothing),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "The disjunctive demand narrows to the unit ∃dp.>5, which pools with the told universal ∀dp.<3 through the per-constant root arm and refutes — the narrowed unit decides INCONSISTENT through the normal demand-landing fork.");
        Assert.IsFalse(engine.RootDataDemandObserved, "The narrowed unit demand routes through the per-constant arm, not the disjunctive-marker delegation — the multi-literal marker head does not survive as a live root head once narrowed, so the arm-off statistic stays clear.");
    }

    /// <summary>
    /// KVR-12 (T): the ≈-split spelling row on the fragmented per-individual-roots
    /// topology (the home-slot-sensitive twin). Membership told under <c>o</c>,
    /// key value under <c>oPrime</c>, told
    /// <c>o ≈ oPrime</c>, and a third candidate <c>p</c> matching both: p joins the
    /// merged class ONLY through the read-time union pooled across the per-individual
    /// roots. The ≈-surface is topology-uniform, so the single-root KVR-12 verdict
    /// holds — one fired union, consistent — with each individual's facts home-slot
    /// resolved in its own <c>v_o</c> context.
    /// </summary>
    [TestMethod]
    public void Kvr12SplitSpellingJoinsThroughReadTimeUnionFragmented()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoinFragmented(
            [
                HasKey(Class("K"), [], ["dk"]),
                Trigger(),
                ClassAssertion(Class("K"), Individual("o")),
                DataAssertion("oPrime", "dk", "V"),
                Same("o", "oPrime"),
                ClassAssertion(Class("K"), Individual("p")),
                DataAssertion("p", "dk", "V"),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.RootApproxSameClass(IndividualId(clausification, "o"), IndividualId(clausification, "oPrime")), "The told SameIndividual merged the split spellings across the per-individual roots.");
        RootKeyJoinOutcome outcome = engine.RunPostSaturationRootKeyJoin(ReasoningBudget.Unbounded, TestContext.CancellationToken, out _, out int fired);
        Assert.AreEqual(RootKeyJoinOutcome.Clean, outcome, "The join completes cleanly under the fragmented topology.");
        Assert.AreEqual(1, fired, "p joins the merged {o, oPrime} class through the read-time union pooled across the per-individual roots — the single-root KVR-12 verdict holds under the fragmented topology.");
        Assert.IsFalse(engine.IsInconsistent, "No told distinctness, so the fragmented join leaves the module consistent.");
    }

    /// <summary>
    /// KVR-3 (T): object-key agreement through a DERIVED role edge on the
    /// fragmented per-individual-roots topology (guarded co-occurrence lifts spec
    /// section 4, the home-slot-sensitive twin). The derived edges <c>r(a, x)</c>
    /// and <c>r(b, x)</c> land in each individual's own <c>v_o</c> context,
    /// home-slot resolved through the entry translation, and pool over the
    /// per-individual roots so a and b share the object-key target x. The join
    /// fires one union and the module is consistent — the single-root KVR-3 verdict
    /// holds under the fragmented topology.
    /// </summary>
    [TestMethod]
    public void Kvr3ObjectKeyAgreementViaDerivedRoleEdgeJoinsFragmented()
    {
        ContextSaturationEngine engine = SaturateWithRootKeyJoinFragmented(
            [
                HasKey(Class("K"), ["r"], []),
                Trigger(),
                ClassAssertion(Class("K"), Individual("a")),
                ClassAssertion(Class("K"), Individual("b")),
                SubClassOf(OneOf("a"), HasValue("r", "x")),
                SubClassOf(OneOf("b"), HasValue("r", "x")),
            ],
            out _);

        RootKeyJoinOutcome outcome = engine.RunPostSaturationRootKeyJoin(ReasoningBudget.Unbounded, TestContext.CancellationToken, out _, out int fired);
        Assert.AreEqual(RootKeyJoinOutcome.Clean, outcome, "The join completes cleanly under the fragmented topology.");
        Assert.AreEqual(1, fired, "The derived object-key edge feeds each per-individual root index, home-slot resolved, and a joins b — the single-root KVR-3 verdict holds under the fragmented topology.");
        Assert.IsFalse(engine.IsInconsistent, "No told distinctness, so the fragmented join leaves the module consistent.");
    }

    /// <summary>
    /// DVR-3 (T): merged constants' split demands clash only pooled on the
    /// fragmented per-individual-roots topology (guarded co-occurrence lifts spec
    /// section 4, the home-slot-sensitive twin). <c>o</c> carries the existential
    /// above five in its own <c>v_o</c> and <c>oPrime</c> the universal below three
    /// in its own <c>v_oPrime</c> — each satisfiable alone — and the told
    /// <c>o ≈ oPrime</c> merges the classes so the pooled demand set clashes across
    /// the per-individual root boundary. The module is INCONSISTENT — the
    /// single-root DVR-3 verdict holds under the fragmented topology, the ≈-surface
    /// spanning individuals uniformly.
    /// </summary>
    [TestMethod]
    public void Dvr3MergedConstantsSplitDemandsClashOnlyPooledFragmented()
    {
        ContextSaturationEngine engine = SaturateWithRootDataObligationsFragmented(
            [
                SubClassOf(OneOf("o"), DataSome("dp", IntegerAbove(5))),
                SubClassOf(OneOf("oPrime"), DataAll("dp", IntegerBelow(3))),
                Same("o", "oPrime"),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "The told o ≈ oPrime merges the per-individual roots' demand classes and the pooled demand set clashes — the single-root DVR-3 verdict holds under the fragmented topology, the ≈-surface is topology-uniform.");
    }

    /// <summary>Clausifies the axioms with the vr key join armed, builds the engine below the gates, saturates to the fixpoint, and runs the ground ghost pass — the engine the root-tier key-join pins drive.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for symbol lookups.</param>
    /// <returns>The saturated engine.</returns>
    private ContextSaturationEngine SaturateWithRootKeyJoin(OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(new ReasoningModule([.. axioms], Violations: []), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false, rootKeyJoinEnabled: true);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
    }

    /// <summary>Clausifies the axioms with the vr key join armed and builds the engine on the fragmented per-individual-roots topology below the gates, then saturates and runs the ground ghost pass — the engine the (T) key-join twins drive. The topology is reached at the engine <c>Create</c> surface, exactly as <see cref="ContextRootFragmentationTests"/> drives it; no production reasoner signature changes.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for symbol lookups.</param>
    /// <returns>The saturated fragmented-topology engine.</returns>
    private ContextSaturationEngine SaturateWithRootKeyJoinFragmented(OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(new ReasoningModule([.. axioms], Violations: []), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false, rootKeyJoinEnabled: true);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.PerIndividualRoots);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded fragmented saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
    }

    /// <summary>Decides a module through the reasoner with the vr key join armed — the lift battery's lit reasoner entry.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module decision.</returns>
    private ModuleDecision DecideWithRootKeyJoin(params OwlAxiom[] axioms)
    {
        return ContextSaturationModuleReasoner.DecideModuleWithRootKeyJoin(new ReasoningModule([.. axioms], Violations: []), TestContext.CancellationToken);
    }

    /// <summary>Clausifies the axioms, builds the engine below the gates, arms the per-constant root data-obligation lift, saturates to the fixpoint, and runs the ground ghost pass — the engine the DVR pins read.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for symbol lookups.</param>
    /// <returns>The saturated engine.</returns>
    private ContextSaturationEngine SaturateWithRootDataObligations(OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(new ReasoningModule([.. axioms], Violations: []));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.RootDataObligationsEnabled = true;
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
    }

    /// <summary>Clausifies the axioms and builds the engine on the fragmented per-individual-roots topology below the gates, arms the per-constant root data-obligation lift, then saturates and runs the ground ghost pass — the engine the (T) data twin drives. The topology is reached at the engine <c>Create</c> surface, exactly as <see cref="ContextRootFragmentationTests"/> drives it; no production reasoner signature changes.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for symbol lookups.</param>
    /// <returns>The saturated fragmented-topology engine.</returns>
    private ContextSaturationEngine SaturateWithRootDataObligationsFragmented(OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(new ReasoningModule([.. axioms], Violations: []));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.PerIndividualRoots);
        engine.RootDataObligationsEnabled = true;
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded fragmented saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
    }

    /// <summary>Decides a module through the reasoner with the per-constant root data-obligation lift armed — the lift battery's lit reasoner entry for the data arm.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module decision.</returns>
    private ModuleDecision DecideWithRootDataObligations(params OwlAxiom[] axioms)
    {
        return ContextSaturationModuleReasoner.DecideModuleWithRootDataObligations(new ReasoningModule([.. axioms], Violations: []), TestContext.CancellationToken);
    }

    /// <summary>Decides a module through the reasoner with BOTH root-tier lifts armed — the co-fire reasoner entry the COF-1 row reads.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module decision.</returns>
    private ModuleDecision DecideWithRootLifts(params OwlAxiom[] axioms)
    {
        return ContextSaturationModuleReasoner.DecideModuleWithRootLifts(new ReasoningModule([.. axioms], Violations: []), TestContext.CancellationToken);
    }

    /// <summary>Clausifies the axioms, builds the engine below the gates, saturates to the fixpoint, and runs the ground ghost pass — the engine whose dark root-tier surface and per-constant index the pins read.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for symbol lookups.</param>
    /// <returns>The saturated engine.</returns>
    private ContextSaturationEngine Saturate(OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(new ReasoningModule([.. axioms], Violations: []));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
    }

    /// <summary>The interned id of a named individual in the example namespace.</summary>
    /// <param name="clausification">The clausification whose symbol table is consulted.</param>
    /// <param name="local">The individual's local name.</param>
    /// <returns>The interned individual id.</returns>
    private static int IndividualId(ClausificationResult clausification, string local)
    {
        Assert.IsTrue(clausification.Symbols.TryIndividualId(Utf8Strings.From(Example + local), out int id), "The individual interned at clausification.");

        return id;
    }

    /// <summary>The nominal-jurisdiction trigger — an <c>ObjectHasValue</c> superclass that routes the ABox through the root-intake site and mints the root context.</summary>
    /// <returns>The trigger axiom.</returns>
    private static OwlSubClassOfAxiom Trigger()
    {
        return SubClassOf(Class("Trigger"), HasValue("r", "anchor"));
    }

    /// <summary>A same-individual axiom pairing two named individuals.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>An asserted role edge between two individuals.</summary>
    /// <param name="from">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="to">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string from, string role, string to)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(from), Individual(role), Individual(to)) { Origin = Origin("edge") };
    }

    /// <summary>Clausifies a module over the axioms with no violations attached, below the module survey and the reasoner gates.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The clausification, whose symbol table the pins read.</returns>
    private static ClausificationResult Clausify(params OwlAxiom[] axioms)
    {
        return ContextClausifier.Clausify(new ReasoningModule([.. axioms], Violations: []));
    }

    /// <summary>A provenance quad naming an axiom's origin.</summary>
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

    /// <summary>A named object property reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
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

    /// <summary>A blank-node individual under the given label — an anonymous ABox subject.</summary>
    /// <param name="label">The blank-node label, also the interning key.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>An individual-value restriction over a forward role — <c>∃r.{a}</c> in its ObjectHasValue spelling — the nominal construct that puts a module under nominal jurisdiction.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A class assertion typing an individual — a named node or a blank-node subject.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual term.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An equivalent-classes axiom pairing two class expressions.</summary>
    /// <param name="first">The first class expression.</param>
    /// <param name="second">The second class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>An enumeration of named individuals in the example namespace — the <c>ObjectOneOf</c> nominal construct.</summary>
    /// <param name="individuals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration expression.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>A local-reflexivity restriction over a forward role — <c>ObjectHasSelf</c>, the construct that mints a self-loop concept.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The self restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>A <c>HasKey</c> axiom over a keyed class, its object key properties, and its data key properties by local name.</summary>
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

    /// <summary>A named data property in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The data property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A single-property data existential (<c>DataSomeValuesFrom</c>) — the value-forcing demand shape whose marker lands at a constant on the root context.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([DataProperty(property)], range);
    }

    /// <summary>A single-property data universal (<c>DataAllValuesFrom</c>) — the constraining demand that pools with a same-property existential at a constant.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The constraining range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataAllValuesFrom DataAll(string property, OwlDataRange range)
    {
        return new OwlDataAllValuesFrom([DataProperty(property)], range);
    }

    /// <summary>The named <c>xsd:string</c> data range — a value space the sidecar's family checker abstains on, so a lone string demand is undecided.</summary>
    private static OwlDatatypeReference StringType { get; } = new(new NamedNode(Vocabulary.Xsd.String));

    /// <summary>An integer range bounded below exclusively — a value strictly above the bound.</summary>
    /// <param name="bound">The exclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAbove(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinExclusive, bound));
    }

    /// <summary>An integer range bounded above exclusively — a value strictly below the bound.</summary>
    /// <param name="bound">The exclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBelow(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MaxExclusive, bound));
    }

    /// <summary>An integer datatype restriction over the given facet bounds.</summary>
    /// <param name="bounds">The facet-bound pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerRestriction(params (Utf8String Facet, int Bound)[] bounds)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((Utf8String facet, int bound) in bounds)
        {
            facets.Add(new OwlFacetRestriction(new NamedNode(facet), new Literal(Utf8Strings.From(bound.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer))));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }

    /// <summary>A data-property assertion over a named subject and an <c>xsd:string</c> literal value.</summary>
    /// <param name="subject">The subject individual's local name.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The literal's lexical form.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom DataAssertion(string subject, string property, string value)
    {
        return new OwlDataPropertyAssertionAxiom(Individual(subject), DataProperty(property), StringLiteral(value)) { Origin = Origin("data") };
    }

    /// <summary>An <c>xsd:string</c> literal with the given lexical form.</summary>
    /// <param name="value">The literal's lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StringLiteral(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));
    }

    /// <summary>A data-property assertion over a named subject and a literal of an unregistered example-namespace datatype — the shape whose value comparison the datatype checker answers <c>Indeterminate</c>.</summary>
    /// <param name="subject">The subject individual's local name.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The literal's lexical form.</param>
    /// <param name="datatype">The custom datatype's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom CustomDataAssertion(string subject, string property, string value, string datatype)
    {
        Literal literal = new(Utf8Strings.From(value), new NamedNode(Utf8Strings.From(Example + datatype)));

        return new OwlDataPropertyAssertionAxiom(Individual(subject), DataProperty(property), literal) { Origin = Origin("data") };
    }

    /// <summary>A different-individuals axiom asserting two named individuals distinct.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(string first, string second)
    {
        return new OwlDifferentIndividualsAxiom([Individual(first), Individual(second)]) { Origin = Origin("different") };
    }

    /// <summary>An object union of two class expressions — the <c>ObjectUnionOf</c> disjunction a disjunctive membership rides.</summary>
    /// <param name="first">The first class expression.</param>
    /// <param name="second">The second class expression.</param>
    /// <returns>The union expression.</returns>
    private static OwlObjectUnionOf Union(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlObjectUnionOf([first, second]);
    }

    /// <summary>The <c>owl:Nothing</c> reference — the empty class a told <c>D ⊑ ⊥</c> closure condemns a disjunct into so a disjunctive membership narrows to its surviving disjunct.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>An existential restriction over a forward role — the successor-forcing shape that seeds the inverse-counting Nom mint habitat.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse — the inverse declaration the Nom mint habitat rides.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a forward role — the counting bound that merges two role successors into one, deriving their equality in the restriction-bearing context.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The forward maximum-cardinality restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over the inverse of a forward role — the inverse counting bound that forces the Nom rule to mint generated-nominal siblings.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The inverse maximum-cardinality restriction.</returns>
    private static OwlObjectCardinality MaxInverse(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), filler);
    }

    /// <summary>A data-property assertion over a named subject and an <c>xsd:integer</c> literal of the given lexical form — the typed data-key value whose lexical spelling the value-space comparison sees through.</summary>
    /// <param name="subject">The subject individual's local name.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="lexical">The integer literal's lexical form.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom IntegerDataAssertion(string subject, string property, string lexical)
    {
        return new OwlDataPropertyAssertionAxiom(Individual(subject), DataProperty(property), new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.Integer))) { Origin = Origin("data") };
    }

    /// <summary>A data-property assertion over a BLANK-NODE subject and an <c>xsd:string</c> literal value — the told-anonymous key value whose subject the origin conjunct bars from key-join candidacy.</summary>
    /// <param name="label">The blank-node subject's label.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The literal's lexical form.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom BlankDataAssertion(string label, string property, string value)
    {
        return new OwlDataPropertyAssertionAxiom(Blank(label), DataProperty(property), StringLiteral(value)) { Origin = Origin("data") };
    }
}
