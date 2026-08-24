using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Owl.Contexts;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The subsumption-index exercisers over a bare <see cref="Context"/>: the
/// containment test and the backward-subsumption sweep answer off literal-keyed
/// postings rather than a scan of the live clause list, and each row drives one
/// arm of that machinery in isolation. The containment rows cover the
/// selected-literal key a non-empty-head subsumer is reachable through, the
/// first-body-literal key a head-empty subsumer is reachable through, the live
/// empty clause that keys no posting at all, and the liveness filter that hides a
/// tombstoned subsumer whose posting entry survives. The sweep rows cover the
/// head-keyed and body-keyed probes, the same liveness filter in the collecting
/// direction, and the live-list order the collected ids are restored to. The
/// merge-head rows cover the predecessor-trigger-equality posting the root-exchange
/// blocking relation draws its candidates from — the whole-head registration walk,
/// its negative space, the stale ids a reader filters, and the shape predicate
/// itself. The bridge-individual rows cover the posting the Join bridge sweep
/// enumerates instead of the whole individual census — the empty-body maximal
/// registration, its two negative spaces (a non-empty body and a shape that is not a
/// maximal central-individual equality), the sorted insert that keeps the posting
/// ascending under out-of-order arrival, the deduplication that keeps one entry per
/// individual, and the stale-tolerated entry a tombstone leaves behind. The absorber
/// row pins the absorber scan, which stays a plain live-list
/// walk, against an accidental reroute through a posting. The span rows carry the
/// unbuilt-conclusion face: the span key hashes and compares as the clause it
/// stands for, the alternate lookup agrees with the set and hands back the stored
/// reference, a content-identical clause of another origin is recognised at the
/// exact-duplicate fast path rather than falling to the subsumption walk, the
/// in-place canonical form is the clause factory's literal-for-literal, the
/// canonical-span factory stamps the origin it is handed, and both clause faces of
/// the containment test answer through the span core on every arm. The clause
/// equality-operator rows carry the value-semantics contract the operators publish:
/// distinct instances of identical content, differing content, provenance-only
/// difference, and null on either side. The occurrence-telemetry rows carry the
/// maintained-versus-consulted record of the two survivor-sweep indexes: entries
/// registered per LITERAL, distinct keys held, sweeps that reached the posting path,
/// and the posting entries they walked, with the empty-clause arm's zero charge
/// pinned beside them. Every population is hand-built and hand-verified against the
/// documented append and swap-remove bookkeeping, so no row depends on a module
/// verdict.
/// </summary>
[TestClass]
internal sealed class ContextRedundancyIndexTests
{
    /// <summary>The clause origin marker the fixtures stamp; the origin value is inert for the redundancy relation under test.</summary>
    private const int DerivedOrigin = -1;

    /// <summary>The first distinguishing source-axiom index the cross-origin and factory rows stamp, so a swapped or defaulted origin argument cannot read as the other.</summary>
    private const int FirstOrigin = 11;

    /// <summary>The second distinguishing source-axiom index, different from <see cref="FirstOrigin"/> and from <see cref="DerivedOrigin"/>.</summary>
    private const int SecondOrigin = 22;

    /// <summary>The concept-atom id that sorts ahead of every other fixture atom, so a body span built with it puts the other atoms mid-span.</summary>
    private const int LeadingAtom = 3;

    /// <summary>The first ordinary concept-atom id the fixtures build heads and bodies from.</summary>
    private const int FirstAtom = 5;

    /// <summary>The second ordinary concept-atom id.</summary>
    private const int SecondAtom = 7;

    /// <summary>The third ordinary concept-atom id.</summary>
    private const int ThirdAtom = 9;

    /// <summary>The fourth ordinary concept-atom id.</summary>
    private const int FourthAtom = 11;

    /// <summary>The concept-atom id the filler clauses head, which no probe clause carries.</summary>
    private const int FillerAtom = 13;

    /// <summary>The concept-atom id the second filler clause heads, which no probe clause carries.</summary>
    private const int OtherFillerAtom = 15;

    /// <summary>The interned individual id the merge-shape fixtures build their equalities against.</summary>
    private const int FirstIndividual = 1;

    /// <summary>The second interned individual id, so a ground-ground equality between two named individuals is expressible.</summary>
    private const int SecondIndividual = 2;

    /// <summary>The third interned individual id, above both others, so a registration sequence arriving out of individual-id order is expressible.</summary>
    private const int ThirdIndividual = 4;

    /// <summary>The directioned role id the merge-free role head is built over.</summary>
    private const int RoleSymbol = 4;

    /// <summary>The containment test reaches a subsumer whose head literal is its registered maximal one through that literal's selected-literal posting: the probe clause's own head literal is the key, since a subsumer's head is a subset of it. Dropping the head-key walk leaves the exact-duplicate fast path and the body-key walk, neither of which sees this subsumer, so the answer flips.</summary>
    [TestMethod]
    public void ContainmentFindsASubsumerThroughItsSelectedLiteralKey()
    {
        Context context = NewContext();
        context.Insert(Clause([], [Concept(FirstAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        DlClause candidate = Clause([], [Concept(FirstAtom), Concept(SecondAtom)]);

        Assert.IsTrue(context.ContainsUpToRedundancy(candidate), "The live single-head clause subsumes the two-literal-head candidate and is reachable through its own head literal's selected-literal posting.");
    }

    /// <summary>The containment test reaches a HEAD-EMPTY subsumer through the posting keyed by that subsumer's FIRST body literal, walking every one of the probe clause's body literals rather than only its first: the fixture places the subsumer's first body literal mid-span in the probe's body, so a walk narrowed to the probe's own first literal misses it. Dropping the body-key walk leaves nothing that sees a head-empty subsumer, so the answer flips.</summary>
    [TestMethod]
    public void ContainmentFindsAHeadEmptySubsumerThroughItsFirstBodyLiteral()
    {
        Context context = NewContext();
        context.Insert(Clause([Concept(FirstAtom), Concept(SecondAtom)], []), isPredEligible: false, decidedUnderNoChoice: true, NoMaximal());

        DlClause candidate = Clause([Concept(LeadingAtom), Concept(FirstAtom), Concept(SecondAtom)], [Concept(ThirdAtom)]);

        Assert.IsTrue(context.ContainsUpToRedundancy(candidate), "The head-empty subsumer's first body literal sits mid-span in the candidate's body, and the walk over every candidate body literal still reaches its posting.");
    }

    /// <summary>Once the empty clause is live it subsumes every clause, and it keys no posting at all — its head and body are both empty — so the containment test answers off the live-empty-clause guard rather than any index walk. Dropping the guard leaves both walks empty-handed and the answer flips.</summary>
    [TestMethod]
    public void ContainmentAnswersTrueForEveryClauseOnceTheEmptyClauseIsLive()
    {
        Context context = NewContext();
        context.Insert(Clause([], []), isPredEligible: false, decidedUnderNoChoice: true, NoMaximal());

        DlClause unrelated = Clause([Concept(FirstAtom)], [Concept(SecondAtom)]);

        Assert.IsTrue(context.HasEmptyClause, "Inserting the body-empty head-empty clause marks the context's empty-clause flag.");
        Assert.IsTrue(context.ContainsUpToRedundancy(unrelated), "The live empty clause subsumes every clause, including one sharing no literal with it.");
    }

    /// <summary>A tombstone leaves the removed id in the postings for readers to filter, so the containment test must check liveness before every subsumption merge-walk. Dropping that filter finds the stale id, whose clause bytes still subsume the candidate, and the answer flips.</summary>
    [TestMethod]
    public void ContainmentIgnoresATombstonedSubsumer()
    {
        Context context = NewContext();
        int subsumerId = context.Insert(Clause([], [Concept(FirstAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Tombstone(subsumerId);

        DlClause candidate = Clause([], [Concept(FirstAtom), Concept(SecondAtom)]);

        Assert.IsFalse(context.ContainsUpToRedundancy(candidate), "The only subsumer is tombstoned, so the candidate is not contained even though the posting still names it.");
    }

    /// <summary>
    /// The backward sweep returns its ids in LIVE-LIST order, not in the order the
    /// probed posting registered them. The population is hand-verified against the
    /// documented bookkeeping: four inserts append ids 0..3 to the live list, and
    /// tombstoning id 0 swap-removes it by moving the last live id into its slot,
    /// leaving the live list as id 3, id 1, id 2 — while the posting still holds
    /// id 1, id 2, id 3 in registration order. The sweep must return the former.
    /// </summary>
    [TestMethod]
    public void BackwardSweepRestoresLiveIdOrderAfterATombstoneReshuffle()
    {
        Context context = NewContext();
        int fillerId = context.Insert(Clause([], [Concept(FillerAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        int firstId = context.Insert(Clause([], [Concept(FirstAtom), Concept(SecondAtom)]), isPredEligible: false, decidedUnderNoChoice: true, BothMaximal());
        int secondId = context.Insert(Clause([], [Concept(FirstAtom), Concept(ThirdAtom)]), isPredEligible: false, decidedUnderNoChoice: true, BothMaximal());
        int thirdId = context.Insert(Clause([], [Concept(FirstAtom), Concept(FourthAtom)]), isPredEligible: false, decidedUnderNoChoice: true, BothMaximal());
        context.Tombstone(fillerId);

        List<int> subsumed = [];
        context.CollectStrictlySubsumed(Clause([], [Concept(FirstAtom)]), subsumed);

        Assert.AreSequenceEqual(new[] { thirdId, firstId, secondId }, subsumed, "The sweep returns the subsumed ids in live-list order, which the tombstone's swap-remove made differ from both insertion order and ascending id order.");
    }

    /// <summary>A HEAD-EMPTY probe clause draws its candidates from the body-occurrence postings, since a clause it subsumes carries every literal of its body. Dropping that probe arm leaves the head-keyed arm with nothing to iterate and the collected set empties.</summary>
    [TestMethod]
    public void BackwardSweepFindsSubsumedClausesThroughAHeadEmptyProbe()
    {
        Context context = NewContext();
        int subsumedId = context.Insert(Clause([Concept(LeadingAtom), Concept(FirstAtom), Concept(SecondAtom)], [Concept(ThirdAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        List<int> subsumed = [];
        context.CollectStrictlySubsumed(Clause([Concept(FirstAtom), Concept(SecondAtom)], []), subsumed);

        Assert.AreSequenceEqual(new[] { subsumedId }, subsumed, "The head-empty probe reaches the live clause whose body carries both of the probe's body literals.");
    }

    /// <summary>
    /// A head-non-empty probe draws its candidates from the head-occurrence
    /// postings, which key EVERY head literal — not only the maximal ones the
    /// selected-literal index keys. The subsumed clause here is inserted with a
    /// single maximal index, so its second head literal reaches the postings only
    /// through the whole-head registration, and that literal is the probe's sole
    /// head. Dropping the whole-head registration leaves the probe's key absent and
    /// the collected set empties.
    /// </summary>
    [TestMethod]
    public void BackwardSweepFindsSubsumedClausesThroughAHeadNonEmptyProbe()
    {
        Context context = NewContext();
        int subsumedId = context.Insert(Clause([], [Concept(FirstAtom), Concept(SecondAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        List<int> subsumed = [];
        context.CollectStrictlySubsumed(Clause([], [Concept(SecondAtom)]), subsumed);

        Assert.AreSequenceEqual(new[] { subsumedId }, subsumed, "The probe's sole head literal is the subsumed clause's NON-maximal head literal, reachable only because every head literal is registered.");
    }

    /// <summary>The backward sweep filters liveness before every subsumption merge-walk, since a tombstone leaves the removed id in the postings. Dropping that filter collects the stale id and the sweep would tombstone an already-removed clause.</summary>
    [TestMethod]
    public void BackwardSweepIgnoresATombstonedTarget()
    {
        Context context = NewContext();
        int targetId = context.Insert(Clause([], [Concept(FirstAtom), Concept(SecondAtom)]), isPredEligible: false, decidedUnderNoChoice: true, BothMaximal());
        context.Tombstone(targetId);

        List<int> subsumed = [];
        context.CollectStrictlySubsumed(Clause([], [Concept(FirstAtom)]), subsumed);

        Assert.IsEmpty(subsumed, "The only subsumed clause is tombstoned, so the sweep collects nothing even though the posting still names it.");
    }

    /// <summary>
    /// The absorber scan stays a plain walk of the live list and returns the
    /// FIRST live subsumer in that order. The population makes every other
    /// candidate answer wrong: four inserts append ids 0..3, tombstoning id 0
    /// swap-removes it by moving the last live id into its slot, so the live list
    /// reads id 3, id 1, id 2 and the scan-first absorber is id 3 — while both
    /// ascending id order and insertion order would answer id 1. A reroute of the
    /// scan through a posting index, or an ascending-id iteration, therefore reds
    /// here.
    /// </summary>
    [TestMethod]
    public void AbsorberChoiceMatchesTheLiveOrderScanAfterAnEliminationReshuffle()
    {
        Context context = NewContext();
        int fillerId = context.Insert(Clause([], [Concept(FillerAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        int earlyAbsorberId = context.Insert(Clause([], [Concept(FirstAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [Concept(OtherFillerAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        int lateAbsorberId = context.Insert(Clause([], [Concept(SecondAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Tombstone(fillerId);

        DlClause absorbed = Clause([], [Concept(FirstAtom), Concept(SecondAtom), Concept(ThirdAtom)]);

        Assert.IsTrue(context.TryFindLiveAbsorber(absorbed, out int absorbingId), "Two live clauses subsume the absorbed clause, so the scan finds one.");
        Assert.AreEqual(lateAbsorberId, absorbingId, "The absorber is the scan-first live clause after the swap-remove, not the smaller id or the earlier insert.");
        Assert.AreNotEqual(earlyAbsorberId, absorbingId, "The earlier-inserted, smaller-id subsumer is the answer both a posting reroute and an ascending-id scan would give.");
    }

    /// <summary>The merge-equality head posting registers exactly the clauses whose head carries a predecessor-trigger equality, in insertion (id) order, and leaves every merge-free head out. Dropping the registration empties the posting and the blocking relation loses every candidate it may answer from.</summary>
    [TestMethod]
    public void MergeEqualityHeadPostingRegistersOnInsert()
    {
        Context context = NewContext();
        context.Insert(Clause([], [Concept(FirstAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        int centralContextId = context.Insert(Clause([], [CentralContextEquality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [Concept(SecondAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        int centralIndividualId = context.Insert(Clause([], [CentralIndividualEquality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.AreSequenceEqual(new[] { centralContextId, centralIndividualId }, context.PredecessorMergeHeadClauses, "The posting holds exactly the two merge-equality-headed ids, in the order their inserts appended them.");
    }

    /// <summary>The registration walks the WHOLE head span, not the maximal subset: a clause whose merge equality is not its selected literal still registers, because the blocking relation reads every head literal of a candidate. A registration narrowed to the maximal indexes drops this id and the blocking relation goes blind to it.</summary>
    [TestMethod]
    public void MergeEqualityHeadPostingRegistersANonMaximalMergeLiteral()
    {
        Context context = NewContext();
        int mixedId = context.Insert(Clause([], [Concept(FirstAtom), CentralIndividualEquality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.AreSequenceEqual(new[] { mixedId }, context.PredecessorMergeHeadClauses, "The head's concept literal is the selected one and its merge equality sits at index one, so only a whole-head walk registers the id.");
    }

    /// <summary>A tombstone leaves the removed id in the merge-head posting for readers to filter, exactly as it does in every other posting, so a reader pairs the posting with the liveness flag. Compacting the posting on tombstone, or reading it without the liveness filter, changes what the blocking relation sees.</summary>
    [TestMethod]
    public void MergeEqualityHeadPostingIgnoresATombstonedCandidateAtRead()
    {
        Context context = NewContext();
        int mergeId = context.Insert(Clause([], [CentralIndividualEquality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Tombstone(mergeId);

        Assert.AreSequenceEqual(new[] { mergeId }, context.PredecessorMergeHeadClauses, "The posting keeps the removed id: it is stale-tolerated, never compacted.");
        Assert.IsFalse(context.IsLive(mergeId), "The tombstoned id reads not live, so a posting reader filtering by liveness skips it.");
    }

    /// <summary>The negative space of the merge shape: concept heads, role heads, inequalities, and ground-ground equalities between two named individuals never register, since none of them is a predecessor-trigger equality. A predicate widened to any equality, or to any literal, registers these and the blocking relation gains candidates it must not have.</summary>
    [TestMethod]
    public void MergeEqualityHeadPostingSkipsMergeFreeHeads()
    {
        Context context = NewContext();
        context.Insert(Clause([], [Concept(FirstAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [RoleHead()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [CentralIndividualInequality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [GroundEquality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.IsEmpty(context.PredecessorMergeHeadClauses, "No merge-free head registers: a ground-ground equality carries no variable side, so it is not a merge shape either.");
    }

    /// <summary>An EMPTY-BODY clause whose MAXIMAL head literal is the central-individual equality <c>x approx o</c> posts its individual: the clause is a Join bridge premise, and the sweep enumerates the posting instead of the whole individual census. Dropping the registration empties the posting and the sweep visits nothing at all.</summary>
    [TestMethod]
    public void BridgeIndividualPostingRegistersAnEmptyBodyMaximalBridgeHead()
    {
        Context context = NewContext();
        context.Insert(Clause([], [CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.AreSequenceEqual(new[] { SecondIndividual }, context.BridgeIndividuals, "The empty-body maximal bridge head posts exactly its own individual.");
    }

    /// <summary>A NON-EMPTY-BODY clause holding the same bridge equality maximal does NOT post its individual: the sweep's inner walk skips every non-empty-body entry it draws before any image probe or conclusion, so such an individual contributes nothing and is never enumerated. Dropping the body condition posts it and the sweep gains an individual whose entries it can only skip.</summary>
    [TestMethod]
    public void BridgeIndividualPostingSkipsANonEmptyBodyBridgeHead()
    {
        Context context = NewContext();
        context.Insert(Clause([Concept(FirstAtom)], [CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.IsEmpty(context.BridgeIndividuals, "A bridge equality under a non-empty body is no bridge premise, so its individual is not posted.");
    }

    /// <summary>The MAXIMAL central-individual equality is the only registering shape: a ground <c>o approx o'</c> maximal head carries no central side, and a central-individual equality sitting at a NON-maximal head position is not the clause's bridge literal. A registration widened to the whole head span — the merge-head posting's own walk — posts the non-maximal one and reds here.</summary>
    [TestMethod]
    public void BridgeIndividualPostingRegistersOnlyTheMaximalCentralIndividualEquality()
    {
        Context context = NewContext();
        context.Insert(Clause([], [GroundEquality()]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [Concept(FirstAtom), CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.IsEmpty(context.BridgeIndividuals, "Neither the ground-ground equality nor the non-maximal bridge equality posts an individual.");
    }

    /// <summary>Registrations arriving out of individual-id order enumerate ASCENDING: clauses insert in derivation order, so the posting is held sorted by a sorted insert rather than by construction, and the sweep's conclusion and enqueue order downstream depends on the ascending walk. A plain append leaves the arrival order and reds here.</summary>
    [TestMethod]
    public void BridgeIndividualPostingEnumeratesAscendingAfterOutOfOrderArrival()
    {
        Context context = NewContext();
        context.Insert(Clause([], [CentralEquality(ThirdIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [CentralEquality(FirstIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.AreSequenceEqual(new[] { FirstIndividual, SecondIndividual, ThirdIndividual }, context.BridgeIndividuals, "The posting reads ascending although the three registrations arrived in descending order.");
    }

    /// <summary>A second qualifying clause for an already-posted individual leaves the posting at ONE entry: the row pins the COUNT rather than mere presence, since a duplicate entry would satisfy a presence check while making the sweep visit the individual twice. Dropping the sorted insert's presence check lands the duplicate and reds here.</summary>
    [TestMethod]
    public void BridgeIndividualPostingDeduplicatesASecondQualifyingClause()
    {
        Context context = NewContext();
        context.Insert(Clause([], [CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([], [Concept(FirstAtom), CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, BothMaximal());

        Assert.HasCount(1, context.BridgeIndividuals, "The individual is posted once however many qualifying clauses register it.");
        Assert.AreSequenceEqual(new[] { SecondIndividual }, context.BridgeIndividuals, "The single entry is the registering individual.");
    }

    /// <summary>Tombstoning EVERY clause of a posted individual leaves it posted: the bridge posting is append-only and stale-tolerated exactly like every sibling index, and the sweep re-probes the selected-literal postings and filters by liveness, so a stale individual yields zero work rather than a wrong answer. Compacting the posting on tombstone reds here.</summary>
    [TestMethod]
    public void BridgeIndividualPostingKeepsAStaleIndividualAfterTombstone()
    {
        Context context = NewContext();
        int bridgeId = context.Insert(Clause([], [CentralEquality(SecondIndividual)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Tombstone(bridgeId);

        Assert.AreSequenceEqual(new[] { SecondIndividual }, context.BridgeIndividuals, "The posting keeps the individual: it is stale-tolerated, never compacted.");
        Assert.IsFalse(context.IsLive(bridgeId), "The individual's only bridge clause reads not live, so the sweep draws an empty candidate list for it.");
    }

    /// <summary>The merge-shape predicate admits the three extended <c>Pr</c> equality shapes in either storage orientation and refuses everything else — the ground-ground equality, the inequality of the same terms, and the atoms. A predicate that lost an orientation, or that accepted a ground-ground equality, flips one of these answers.</summary>
    [TestMethod]
    public void PredecessorTriggerEqualityRecognisesTheThreeShapesInBothOrientations()
    {
        Assert.IsTrue(Context.IsPredecessorTriggerEquality(DlLiteral.Equality(DlTerm.Central, DlTerm.Context)), "x = y is a merge shape.");
        Assert.IsTrue(Context.IsPredecessorTriggerEquality(DlLiteral.Equality(DlTerm.Context, DlTerm.Central)), "y = x is the same shape stored the other way round.");
        Assert.IsTrue(Context.IsPredecessorTriggerEquality(DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(FirstIndividual))), "x = o is a merge shape.");
        Assert.IsTrue(Context.IsPredecessorTriggerEquality(DlLiteral.Equality(DlTerm.Individual(FirstIndividual), DlTerm.Central)), "o = x is the same shape stored the other way round.");
        Assert.IsTrue(Context.IsPredecessorTriggerEquality(DlLiteral.Equality(DlTerm.Context, DlTerm.Individual(FirstIndividual))), "y = o is a merge shape.");
        Assert.IsTrue(Context.IsPredecessorTriggerEquality(DlLiteral.Equality(DlTerm.Individual(FirstIndividual), DlTerm.Context)), "o = y is the same shape stored the other way round.");

        Assert.IsFalse(Context.IsPredecessorTriggerEquality(GroundEquality()), "An equality between two named individuals has no variable side, so it is not a merge shape.");
        Assert.IsFalse(Context.IsPredecessorTriggerEquality(CentralIndividualInequality()), "An inequality never qualifies, whatever its terms.");
        Assert.IsFalse(Context.IsPredecessorTriggerEquality(Concept(FirstAtom)), "A concept atom is not an equality.");
        Assert.IsFalse(Context.IsPredecessorTriggerEquality(RoleHead()), "A role atom is not an equality.");
    }

    /// <summary>
    /// The span key hashes and compares exactly as the clause it stands for over a
    /// pinned population of clause shapes — both spans empty, head-only, body-only,
    /// and a both-non-empty clause built from unsorted, duplicated input. The
    /// agreement is what lets the live set answer an exact-duplicate question about
    /// a conclusion that has not been built: a hash that dropped the body-length
    /// prefix, or that walked head before body, lands the key in another bucket and
    /// every arm below flips.
    /// </summary>
    [TestMethod]
    public void TheSpanKeyHashesAndComparesAsTheClauseItStandsFor()
    {
        AssertKeyAgreesWithClause(Clause([], []), "The empty clause: the hash is the body-length prefix alone.");
        AssertKeyAgreesWithClause(Clause([], [Concept(FirstAtom)]), "An empty body with a single head literal.");
        AssertKeyAgreesWithClause(Clause([Concept(FirstAtom), Concept(SecondAtom)], []), "A two-literal body with an empty head.");
        AssertKeyAgreesWithClause(Clause([Concept(SecondAtom), Concept(FirstAtom)], [Concept(ThirdAtom), Concept(FirstAtom), Concept(ThirdAtom)]), "Unsorted, duplicated input in both spans, whose body and head sequences differ.");
    }

    /// <summary>
    /// The alternate lookup answers a span key exactly as the set answers the clause:
    /// membership agrees, the key retrieves the STORED reference rather than an equal
    /// copy, and a key over content the set does not hold misses. A key materialising
    /// through the comparer carries the key's own origin.
    /// </summary>
    [TestMethod]
    public void TheAlternateLookupAgreesWithTheSetAndReturnsTheStoredReference()
    {
        DlClause stored = Clause([Concept(FirstAtom)], [Concept(SecondAtom)]);
        DlClause absent = Clause([Concept(FirstAtom)], [Concept(ThirdAtom)]);
        HashSet<DlClause> set = new(DlClauseSpanComparer.Instance)
        {
            stored,
        };

        HashSet<DlClause>.AlternateLookup<DlClauseSpanKey> lookup = set.GetAlternateLookup<DlClauseSpanKey>();

        Assert.Contains(stored, set, "The set holds the clause under the comparer's object face.");
        Assert.IsTrue(lookup.Contains(new DlClauseSpanKey(stored.Body, stored.Head, stored.Origin)), "The span key finds the same clause the object face finds.");
        Assert.IsTrue(lookup.TryGetValue(new DlClauseSpanKey(stored.Body, stored.Head, stored.Origin), out DlClause? found), "The span key retrieves the stored clause.");
        Assert.IsTrue(ReferenceEquals(stored, found), "The lookup hands back the STORED reference, not an equal copy.");
        Assert.DoesNotContain(absent, set, "The set does not hold the differing clause.");
        Assert.IsFalse(lookup.Contains(new DlClauseSpanKey(absent.Body, absent.Head, absent.Origin)), "A span key over content the set does not hold misses, exactly as the object face does.");
    }

    /// <summary>
    /// Two clauses of identical content but DIFFERENT origins are one clause to the
    /// live set: a key carrying either origin matches the stored clause, and the
    /// containment answer is EXACT DUPLICATE rather than a subsumer hit. Origin is
    /// provenance; an origin-sensitive alternate hash or equality would let the key
    /// miss the fast path and let the subsumption walk answer instead, silently
    /// moving the event from the duplicate half of the funnel to the subsumed half.
    /// </summary>
    [TestMethod]
    public void ACrossOriginDuplicateIsRecognisedAtTheFastPath()
    {
        Context context = NewContext();
        DlClause stored = DlClause.Create([], [Concept(FirstAtom)], FirstOrigin);
        context.Insert(stored, isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        DlClause foreign = DlClause.Create([], [Concept(FirstAtom)], SecondOrigin);

        Assert.AreNotEqual(stored.Origin, foreign.Origin, "The two clauses differ in origin, which is the point of the row.");
        Assert.IsTrue(stored.Equals(foreign), "Origin is provenance, so the two clauses are equal.");
        Assert.IsTrue(context.ContainsUpToRedundancy(foreign, out bool clauseFaceDuplicate), "The cross-origin clause is contained.");
        Assert.IsTrue(clauseFaceDuplicate, "The clause face answers EXACT DUPLICATE, not a subsumer hit.");
        Assert.IsTrue(context.ContainsUpToRedundancy(foreign.Body, foreign.Head, SecondOrigin, out bool spanFaceDuplicate), "The span face answers contained for a key carrying the foreign origin.");
        Assert.IsTrue(spanFaceDuplicate, "The span face answers EXACT DUPLICATE for a key carrying the foreign origin.");
        Assert.IsTrue(context.ContainsUpToRedundancy(foreign.Body, foreign.Head, FirstOrigin, out bool storedOriginDuplicate), "The span face answers contained for a key carrying the stored origin.");
        Assert.IsTrue(storedOriginDuplicate, "The span face answers EXACT DUPLICATE for a key carrying the stored origin: the two keys are one.");

        HashSet<DlClause> set = new(DlClauseSpanComparer.Instance)
        {
            stored,
        };

        HashSet<DlClause>.AlternateLookup<DlClauseSpanKey> lookup = set.GetAlternateLookup<DlClauseSpanKey>();
        Assert.IsTrue(lookup.TryGetValue(new DlClauseSpanKey(foreign.Body, foreign.Head, SecondOrigin), out DlClause? found), "The foreign-origin key retrieves the stored clause.");
        Assert.IsTrue(ReferenceEquals(stored, found), "The retrieved clause is the stored reference, whatever origin the key carried.");
    }

    /// <summary>
    /// The in-place canonicalisation leaves a buffer literal-for-literal in the form
    /// the clause factory builds, over unsorted and duplicated input and over the
    /// empty-body and empty-head shapes. The two forms are ONE implementation, so a
    /// sort or a de-duplication lost on either side shows here rather than only in a
    /// downstream containment answer.
    /// </summary>
    [TestMethod]
    public void TheInPlaceCanonicalFormMatchesTheClauseFactoryForm()
    {
        AssertCanonicalFormsAgree([Concept(SecondAtom), Concept(FirstAtom), Concept(SecondAtom)], [Concept(FourthAtom), Concept(ThirdAtom), Concept(FourthAtom)], "Unsorted, duplicated input in both spans.");
        AssertCanonicalFormsAgree([], [Concept(SecondAtom), Concept(FirstAtom)], "An empty body with an unsorted head.");
        AssertCanonicalFormsAgree([Concept(SecondAtom), Concept(FirstAtom)], [], "An unsorted body with an empty head.");
        AssertCanonicalFormsAgree([], [], "Both spans empty.");
    }

    /// <summary>
    /// The two canonical faces agree on the literal multisets a predecessor
    /// completion assembles: the body is the concatenation of already-sorted premise
    /// bodies, so a shared conjunct arrives BOTH out of order and duplicated, and
    /// the head is the target head's images followed by each premise's residual, so
    /// a disjunct two premises share arrives duplicated as well. These are the exact
    /// shapes the odometer's append steps leave in its buffers, and both spans are
    /// asserted literal-for-literal against the clause factory's form.
    /// </summary>
    [TestMethod]
    public void TheCanonicalFormAgreesWithTheClauseFactoryOnPredShapedInput()
    {
        AssertCanonicalFormsAgree(
            [Concept(FirstAtom), Concept(ThirdAtom), Concept(FirstAtom), Concept(SecondAtom)],
            [Concept(FourthAtom), Concept(FourthAtom)],
            "Two sorted premise bodies concatenated over a shared conjunct, with a residual disjunct both premises carry.");
        AssertCanonicalFormsAgree(
            [Concept(SecondAtom), Concept(LeadingAtom), Concept(SecondAtom), Concept(LeadingAtom)],
            [Concept(ThirdAtom), Concept(FirstAtom), Concept(ThirdAtom)],
            "Two premises contributing the same body pair, with a target head image ordering above one residual and below the other.");
        AssertCanonicalFormsAgree(
            [Concept(FirstAtom), Concept(SecondAtom)],
            [],
            "An empty target head with empty residuals — the collapse-propagating completion.");
    }

    /// <summary>
    /// The canonical-span factory stamps the origin it is HANDED and copies both
    /// spans faithfully: the clause it builds is content-equal to the factory-built
    /// clause the spans came from while carrying a distinguishing origin of its own.
    /// A defaulted or swapped origin argument is visible nowhere else — the
    /// containment relation, the subsumption sweep, and every gate ignore origin by
    /// design — so this row is the origin's only structural witness.
    /// </summary>
    [TestMethod]
    public void TheCanonicalSpanFactoryStampsTheOriginItIsHanded()
    {
        DlClause built = DlClause.Create([Concept(SecondAtom), Concept(FirstAtom)], [Concept(ThirdAtom)], FirstOrigin);

        DlClause fromSpans = DlClause.FromCanonicalSpans(built.Body, built.Head, SecondOrigin);

        Assert.AreEqual(SecondOrigin, fromSpans.Origin, "The factory stamps the origin it was handed.");
        Assert.AreNotEqual(built.Origin, fromSpans.Origin, "The two origins differ, so a swapped or defaulted argument cannot pass unseen.");
        Assert.AreEqual(built.BodyLength, fromSpans.BodyLength, "The body split rides the copy.");
        Assert.AreSequenceEqual(built.Body.ToArray(), fromSpans.Body.ToArray(), "The body span is copied literal-for-literal.");
        Assert.AreSequenceEqual(built.Head.ToArray(), fromSpans.Head.ToArray(), "The head span is copied literal-for-literal.");
        Assert.IsTrue(built.Equals(fromSpans), "The two clauses are content-equal: origin is provenance, not identity.");
    }

    /// <summary>
    /// Both clause faces of the containment test answer THROUGH the span core, on
    /// every arm the relation has: the exact-duplicate fast path, the index-drawn
    /// subsumer, the head-empty subsumer reached by a body key, the live empty
    /// clause, and a clause nothing contains. Each arm is asserted three ways — the
    /// plain clause face, the arm-reporting clause face, and the span core itself —
    /// so a defect confined to the core cannot answer one way for spans and another
    /// for clauses.
    /// </summary>
    [TestMethod]
    public void TheClauseContainmentFacesAnswerThroughTheSpanCore()
    {
        Context context = NewContext();
        context.Insert(Clause([], [Concept(FirstAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());
        context.Insert(Clause([Concept(SecondAtom)], []), isPredEligible: false, decidedUnderNoChoice: true, NoMaximal());

        AssertFacesAgree(context, Clause([], [Concept(FirstAtom)]), contained: true, exactDuplicate: true, "The stored single-head clause is its own exact duplicate.");
        AssertFacesAgree(context, Clause([], [Concept(FirstAtom), Concept(SecondAtom)]), contained: true, exactDuplicate: false, "The two-literal head is reached through the selected-literal posting of its first head literal.");
        AssertFacesAgree(context, Clause([Concept(LeadingAtom), Concept(SecondAtom)], [Concept(ThirdAtom)]), contained: true, exactDuplicate: false, "The head-empty subsumer is reached through a body key sitting mid-span.");
        AssertFacesAgree(context, Clause([Concept(LeadingAtom)], [Concept(ThirdAtom)]), contained: false, exactDuplicate: false, "Nothing live subsumes a clause sharing no literal with either stored clause.");

        Context withEmptyClause = NewContext();
        withEmptyClause.Insert(Clause([], []), isPredEligible: false, decidedUnderNoChoice: true, NoMaximal());
        AssertFacesAgree(withEmptyClause, Clause([Concept(FirstAtom)], [Concept(SecondAtom)]), contained: true, exactDuplicate: false, "The live empty clause subsumes every clause and is never the exact-duplicate arm.");
    }

    /// <summary>
    /// The clause equality operators carry VALUE semantics, not reference
    /// semantics: two DISTINCT instances built from identical content compare
    /// equal through <c>==</c> while <see cref="object.ReferenceEquals"/> reports
    /// them apart — the sharing scenario in which a reference-implemented operator
    /// would silently answer the opposite. Differing content compares unequal, a
    /// provenance-only difference compares equal (origin is not identity), and null
    /// on either side and on both sides answers as the contract states. Every
    /// <c>==</c> assertion is paired with its <c>!=</c> negation, so an operator
    /// that is not the other's negation reds here rather than passing on one half.
    /// </summary>
    [TestMethod]
    public void TheClauseEqualityOperatorsCompareByValueRatherThanByReference()
    {
        DlClause clause = Clause([Concept(SecondAtom), Concept(FirstAtom)], [Concept(ThirdAtom)]);
        DlClause twin = Clause([Concept(SecondAtom), Concept(FirstAtom)], [Concept(ThirdAtom)]);
        DlClause other = Clause([Concept(SecondAtom), Concept(FirstAtom)], [Concept(FourthAtom)]);
        DlClause firstOrigin = DlClause.Create([Concept(FirstAtom)], [Concept(ThirdAtom)], FirstOrigin);
        DlClause secondOrigin = DlClause.Create([Concept(FirstAtom)], [Concept(ThirdAtom)], SecondOrigin);
        DlClause? nothing = null;

        //The both-null pair is read out of an array rather than held in two locals, so the
        //comparison below is a genuine operator call rather than a constant the compiler folds.
        DlClause?[] absent = [null, null];

        Assert.IsFalse(ReferenceEquals(clause, twin), "The two instances are genuinely distinct objects, so a reference-implemented operator would answer the opposite below.");
        Assert.IsTrue(clause == twin, "Distinct instances of identical content are equal by value.");
        Assert.IsFalse(clause != twin, "The inequality operator is the equality operator's negation on the equal pair.");

        Assert.IsFalse(clause == other, "A differing head literal makes the two clauses unequal.");
        Assert.IsTrue(clause != other, "The inequality operator is the equality operator's negation on the unequal pair.");

        Assert.AreNotEqual(firstOrigin.Origin, secondOrigin.Origin, "The two clauses carry different provenance, so the comparison below is a genuine provenance-only difference.");
        Assert.IsTrue(firstOrigin == secondOrigin, "Origin is provenance rather than logical identity, so a provenance-only difference compares equal.");
        Assert.IsFalse(firstOrigin != secondOrigin, "The inequality operator agrees with the provenance exclusion.");

        Assert.IsFalse(clause == nothing, "A clause is not equal to null on the right.");
        Assert.IsTrue(clause != nothing, "The inequality operator answers the right-null case as the negation.");
        Assert.IsFalse(nothing == clause, "A clause is not equal to null on the left.");
        Assert.IsTrue(nothing != clause, "The inequality operator answers the left-null case as the negation.");
        Assert.IsTrue(absent[0] == absent[1], "Two null references are equal.");
        Assert.IsFalse(absent[0] != absent[1], "The inequality operator answers the both-null case as the negation.");
    }

    /// <summary>
    /// The occurrence-index telemetry records the MAINTAINED side per LITERAL and
    /// the CONSULTED side per sweep: two inserts of known head and body lengths pin
    /// the registered-entry counters at the literal totals rather than the clause
    /// count, and the distinct-key counters at the shared-key totals, so a charge
    /// moved to a per-clause site reads three where the literals say four. One
    /// backward-subsumption sweep over a non-empty head pins the probe count at one
    /// and the walked entries at the SHORTEST posting's length, and the empty
    /// clause's own sweep — which walks the live list and probes no index at all —
    /// leaves both consulted counters exactly where they were, so a charge
    /// mistakenly placed in that arm reds here.
    /// </summary>
    [TestMethod]
    public void TheOccurrenceTelemetryRecordsRegisteredEntriesAndSweepConsultation()
    {
        Context context = NewContext();
        context.Insert(Clause([Concept(FirstAtom), Concept(SecondAtom)], [Concept(ThirdAtom), Concept(FourthAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.AreEqual(2L, context.HeadOccurrenceEntriesRegistered, "The first clause registers one head-occurrence entry per head literal.");
        Assert.AreEqual(2L, context.BodyOccurrenceEntriesRegistered, "The first clause registers one body-occurrence entry per body literal.");
        Assert.AreEqual(2, context.HeadOccurrenceDistinctKeys, "The first clause's two head literals key two distinct postings.");
        Assert.AreEqual(2, context.BodyOccurrenceDistinctKeys, "The first clause's two body literals key two distinct postings.");

        context.Insert(Clause([Concept(FirstAtom)], [Concept(ThirdAtom)]), isPredEligible: false, decidedUnderNoChoice: true, SingleMaximal());

        Assert.AreEqual(3L, context.HeadOccurrenceEntriesRegistered, "The second clause adds one further head entry, so the counter reads per literal rather than per clause.");
        Assert.AreEqual(3L, context.BodyOccurrenceEntriesRegistered, "The second clause adds one further body entry.");
        Assert.AreEqual(2, context.HeadOccurrenceDistinctKeys, "The second clause's head literal reuses an existing key, so the key breadth does not grow with the entry count.");
        Assert.AreEqual(2, context.BodyOccurrenceDistinctKeys, "The second clause's body literal reuses an existing key.");
        Assert.AreEqual(0L, context.SurvivorSweepProbes, "No backward-subsumption sweep has run yet, so the consulted side is untouched by the registrations.");
        Assert.AreEqual(0L, context.SurvivorSweepPostingEntriesWalked, "No posting has been walked yet.");

        List<int> subsumed = [];
        context.CollectStrictlySubsumed(Clause([Concept(FirstAtom)], [Concept(ThirdAtom), Concept(FourthAtom)]), subsumed);

        Assert.AreEqual(1L, context.SurvivorSweepProbes, "One sweep of a non-empty-head clause reaches the posting path exactly once.");
        Assert.AreEqual(1L, context.SurvivorSweepPostingEntriesWalked, "The sweep walks the SHORTEST of its head keys' postings, which holds the single clause carrying that literal.");

        context.CollectStrictlySubsumed(Clause([], []), subsumed);

        Assert.AreEqual(1L, context.SurvivorSweepProbes, "The empty clause's own sweep walks the live list and probes no occurrence index, so it charges no probe.");
        Assert.AreEqual(1L, context.SurvivorSweepPostingEntriesWalked, "The empty-clause arm reaches no posting, so it walks no posting entry.");
    }

    /// <summary>Asserts a span key over a clause's own spans hashes and compares as that clause does, and that the alternate lookup agrees with the object face on a set holding it.</summary>
    /// <param name="clause">The clause the key stands for.</param>
    /// <param name="shape">The shape's description, reported on a failure.</param>
    private static void AssertKeyAgreesWithClause(DlClause clause, string shape)
    {
        DlClauseSpanKey key = new(clause.Body, clause.Head, clause.Origin);

        Assert.AreEqual(clause.GetHashCode(), DlClauseSpanComparer.Instance.GetHashCode(key), shape);
        Assert.IsTrue(DlClauseSpanComparer.Instance.Equals(key, clause), shape);
        Assert.IsTrue(DlClauseSpanComparer.Instance.Equals(clause, clause), shape);
        Assert.AreEqual(clause.GetHashCode(), DlClauseSpanComparer.Instance.GetHashCode(clause), shape);

        HashSet<DlClause> set = new(DlClauseSpanComparer.Instance)
        {
            clause,
        };

        Assert.IsTrue(set.GetAlternateLookup<DlClauseSpanKey>().Contains(key), shape);
    }

    /// <summary>Asserts the in-place canonicalisation of raw body and head buffers matches the clause factory's canonical form literal-for-literal, and that a clause rebuilt from the in-place buffers is the factory's clause.</summary>
    /// <param name="rawBody">The body literals as a producer would leave them.</param>
    /// <param name="rawHead">The head literals as a producer would leave them.</param>
    /// <param name="shape">The shape's description, reported on a failure.</param>
    private static void AssertCanonicalFormsAgree(DlLiteral[] rawBody, DlLiteral[] rawHead, string shape)
    {
        DlClause built = DlClause.Create(rawBody, rawHead, DerivedOrigin);

        List<DlLiteral> body = [.. rawBody];
        List<DlLiteral> head = [.. rawHead];
        DlClause.CanonicaliseInPlace(body);
        DlClause.CanonicaliseInPlace(head);

        Assert.AreSequenceEqual(built.Body.ToArray(), body, shape);
        Assert.AreSequenceEqual(built.Head.ToArray(), head, shape);
        Assert.IsTrue(built.Equals(DlClause.FromCanonicalSpans(CollectionsMarshal.AsSpan(body), CollectionsMarshal.AsSpan(head), DerivedOrigin)), shape);
    }

    /// <summary>Asserts the plain clause face, the arm-reporting clause face, and the span core all answer one containment question the same way.</summary>
    /// <param name="context">The context probed.</param>
    /// <param name="candidate">The candidate clause.</param>
    /// <param name="contained">The expected containment answer.</param>
    /// <param name="exactDuplicate">The expected arm: the exact-duplicate fast path or anything else.</param>
    /// <param name="arm">The arm's description, reported on a failure.</param>
    private static void AssertFacesAgree(Context context, DlClause candidate, bool contained, bool exactDuplicate, string arm)
    {
        Assert.AreEqual(contained, context.ContainsUpToRedundancy(candidate), arm);
        Assert.AreEqual(contained, context.ContainsUpToRedundancy(candidate, out bool clauseFaceDuplicate), arm);
        Assert.AreEqual(exactDuplicate, clauseFaceDuplicate, arm);
        Assert.AreEqual(contained, context.ContainsUpToRedundancy(candidate.Body, candidate.Head, candidate.Origin, out bool spanFaceDuplicate), arm);
        Assert.AreEqual(exactDuplicate, spanFaceDuplicate, arm);
    }

    /// <summary>Builds an empty ordinary context the fixtures insert into.</summary>
    /// <returns>The fresh context.</returns>
    private static Context NewContext()
    {
        return new Context(0, Array.Empty<DlLiteral>(), isRoot: false, -1, new HashSet<int>());
    }

    /// <summary>A concept atom over the central variable, so the fixtures' literal order follows the atom ids.</summary>
    /// <param name="atom">The concept-atom id.</param>
    /// <returns>The concept literal.</returns>
    private static DlLiteral Concept(int atom)
    {
        return DlLiteral.Concept(atom, DlTerm.Central);
    }

    /// <summary>The merge equality <c>x approx y</c> between the central and context variables.</summary>
    /// <returns>The equality literal.</returns>
    private static DlLiteral CentralContextEquality()
    {
        return DlLiteral.Equality(DlTerm.Central, DlTerm.Context);
    }

    /// <summary>The merge equality <c>x approx o</c> between the central variable and a named individual.</summary>
    /// <returns>The equality literal.</returns>
    private static DlLiteral CentralIndividualEquality()
    {
        return CentralEquality(FirstIndividual);
    }

    /// <summary>The merge equality <c>x approx o</c> against a chosen individual, in the variable-first form the order stores an unoriented variable-versus-individual pair in.</summary>
    /// <param name="individual">The individual id.</param>
    /// <returns>The equality literal.</returns>
    private static DlLiteral CentralEquality(int individual)
    {
        return DlLiteral.Equality(DlTerm.Central, DlTerm.Individual(individual));
    }

    /// <summary>The inequality <c>x not-approx o</c> over the same terms a merge equality would carry, so only the literal kind separates it from a merge shape.</summary>
    /// <returns>The inequality literal.</returns>
    private static DlLiteral CentralIndividualInequality()
    {
        return DlLiteral.Inequality(DlTerm.Central, DlTerm.Individual(FirstIndividual));
    }

    /// <summary>The equality <c>o approx o'</c> between two named individuals — an equality with no variable side, so no merge shape.</summary>
    /// <returns>The equality literal.</returns>
    private static DlLiteral GroundEquality()
    {
        return DlLiteral.Equality(DlTerm.Individual(FirstIndividual), DlTerm.Individual(SecondIndividual));
    }

    /// <summary>A role atom over the central variable and its first neighbour — a merge-free head literal.</summary>
    /// <returns>The role literal.</returns>
    private static DlLiteral RoleHead()
    {
        return DlLiteral.Role(RoleSymbol, DlTerm.Central, DlTerm.Neighbour(0));
    }

    /// <summary>A clause over the fixture origin, canonicalised by the clause factory.</summary>
    /// <param name="body">The body atoms.</param>
    /// <param name="head">The head literals.</param>
    /// <returns>The canonical clause.</returns>
    private static DlClause Clause(DlLiteral[] body, DlLiteral[] head)
    {
        return DlClause.Create(body, head, DerivedOrigin);
    }

    /// <summary>The maximal-index list for an empty head.</summary>
    /// <returns>The empty maximal-index list.</returns>
    private static List<int> NoMaximal()
    {
        return [];
    }

    /// <summary>The maximal-index list naming the head's first literal alone — the sole maximal of a single-literal head, and the deliberate single-maximal selection of the whole-head registration exerciser.</summary>
    /// <returns>The maximal-index list.</returns>
    private static List<int> SingleMaximal()
    {
        return [0];
    }

    /// <summary>The maximal-index list for a two-literal head whose literals are both maximal, so the premise indexes register each.</summary>
    /// <returns>The maximal-index list.</returns>
    private static List<int> BothMaximal()
    {
        return [0, 1];
    }
}
