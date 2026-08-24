using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The SROIQ datatype-sidecar battery through the CONTEXT arm
/// (<see cref="ContextSaturationModuleReasoner"/>, the consequence-based
/// saturation engine). The F4 datatype-half rows (R30–R37, R43–R45) are decided
/// by the context engine itself — a superclass-position data restriction lowers to
/// a demand marker the saturation accumulates and hands the shared datatype
/// sidecar, injecting the <c>⋃Body → Bottom(x)</c> clash clause on an
/// unsatisfiable conjunction and delegating on an undecided obligation; R45 pins
/// that a two-carrier body union condemns only the shared carrier, never a
/// carrier that supplies just part of the clash (the MU9 observable). The
/// F1/F2/F3 parity rows (R01, R03–R28) run the TBox-only ground-truth modules
/// through the context arm and must match the certified verdicts the tableau arms
/// reach; the ABox-carrying rows (R02, R07) are decided INCONSISTENT by the
/// ground context — their datatype content is sidecar-decided and the
/// class assertion forces the carrier's clash onto the asserted individual, a
/// module inconsistency read off the ground context. Class satisfiability is read off the
/// subsumption sweep: an unsatisfiable class is subsumed by a bystander class
/// (<c>A ⊑ ⊥ ⊑ everything</c>), a satisfiable one is not. The certified ground
/// truths are the independently derived battery table.
/// </summary>
[TestClass]
internal sealed class ContextDatatypeSidecarTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, data properties, and literals are drawn from.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The bystander class name whose entailment <c>A ⊑ Bystander</c> witnesses that <c>A</c> is unsatisfiable.</summary>
    private const string Bystander = "Bystander";

    //F4 — datatype restrictions, decided by the context engine itself.

    /// <summary>R30: an intersection of disjoint integer intervals empties the demand, so the carrier is unsatisfiable.</summary>
    [TestMethod]
    public void R30IntersectionOfDisjointIntervalsUnsatisfiable()
    {
        AssertUnsatisfiable(SubClassOf(Reference("A"), DataSome("d", IntersectionOf(IntegerAbove(5), IntegerBelow(3)))));
    }

    /// <summary>R31: an existential above a bound and a same-property universal below a lower bound cannot share a value.</summary>
    [TestMethod]
    public void R31ExistentialAndDisjointUniversalUnsatisfiable()
    {
        AssertUnsatisfiable(
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataAll("d", IntegerBelow(3))));
    }

    /// <summary>R32: an existential and a same-property universal whose ranges overlap admit a shared value.</summary>
    [TestMethod]
    public void R32ExistentialUnderOverlappingUniversalSatisfiable()
    {
        AssertSatisfiable(
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataAll("d", IntegerBelow(10))));
    }

    /// <summary>R33: xsd:boolean carries exactly two values, so a minimum cardinality of three is unsatisfiable.</summary>
    [TestMethod]
    public void R33BooleanMinCardinalityThreeUnsatisfiable()
    {
        AssertUnsatisfiable(SubClassOf(Reference("A"), DataMinCard(3, "d", BooleanType)));
    }

    /// <summary>R34: a dateTime existential over an empty interval (lower bound after the upper) is unsatisfiable.</summary>
    [TestMethod]
    public void R34EmptyDateTimeIntervalUnsatisfiable()
    {
        AssertUnsatisfiable(SubClassOf(Reference("A"), DataSome("d", DateTimeRestriction(
            (Vocabulary.XsdFacets.MinInclusive, "2020-01-01T00:00:00Z"),
            (Vocabulary.XsdFacets.MaxInclusive, "2019-01-01T00:00:00Z")))));
    }

    /// <summary>
    /// R35: an enumeration existential constrained by a universal excluding one
    /// member. The shared value-space checker models <c>xsd:string</c> value
    /// identity decisively — the string lexical-to-value mapping is the
    /// identity function, so "b" is provably not the excluded "a" — and the
    /// surviving "b" witness satisfies both restrictions, so the module is
    /// context-decided with A satisfiable (the ground-truth SAT verdict).
    /// </summary>
    [TestMethod]
    public void R35StringEnumerationExclusionSatisfiable()
    {
        AssertSatisfiable(
            SubClassOf(Reference("A"), DataSome("d", OneOf(StringLiteral("a"), StringLiteral("b")))),
            SubClassOf(Reference("A"), DataAll("d", ComplementOf(OneOf(StringLiteral("a"))))));
    }

    /// <summary>R36: an empty-interval demand makes the carrier unsatisfiable, so it is subsumed by an unrelated class (<c>A ⊑ ⊥ ⊑ C</c>).</summary>
    [TestMethod]
    public void R36UnsatisfiableCarrierSubsumedByUnrelatedClass()
    {
        AssertUnsatisfiable(SubClassOf(Reference("A"), DataSome("d", IntegerRestriction(
            (Vocabulary.XsdFacets.MinExclusive, 5), (Vocabulary.XsdFacets.MaxExclusive, 3)))));
    }

    /// <summary>
    /// R37: a data range whose disjunctive-normal-form product exceeds the checker's
    /// DNF cap is an undecided obligation, so the module delegates. Realised as an
    /// intersection of two nine-way unions (an 81-disjunct product past the 64 cap);
    /// the checker caps the intersection product, so a bare wide union does not trip
    /// it, but an intersection of unions does — the DNF-cap-delegate obligation the
    /// row pins.
    /// </summary>
    [TestMethod]
    public void R37DnfCapDelegates()
    {
        AssertDelegates(SubClassOf(Reference("A"), DataSome("d", IntersectionOf(
            IntegerSingletonUnion(1, 9),
            IntegerSingletonUnion(10, 18)))));
    }

    /// <summary>R43: a counting demand of seven distinct values over a five-value integer interval is unsatisfiable.</summary>
    [TestMethod]
    public void R43CountingAboveIntervalSizeUnsatisfiable()
    {
        AssertUnsatisfiable(SubClassOf(Reference("A"), DataMinCard(7, "d", IntegerRestriction(
            (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 5)))));
    }

    /// <summary>R44: a counting demand of five distinct values over a five-value integer interval is satisfiable.</summary>
    [TestMethod]
    public void R44CountingAtIntervalSizeSatisfiable()
    {
        AssertSatisfiable(SubClassOf(Reference("A"), DataMinCard(5, "d", IntegerRestriction(
            (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 5)))));
    }

    /// <summary>
    /// A superclass-position data maximum cardinality admits: it lowers to the
    /// non-value-forcing maximum marker the sidecar buckets into the property's max
    /// slot. A range-less bound of one is the single slot every value of the
    /// property must share, so a carrier whose class also forces two distinct values
    /// on it is unsatisfiable, while the bound alone forces nothing and A stays
    /// satisfiable.
    /// </summary>
    [TestMethod]
    public void DataMaxCardinalityDecidesOnTheRangeLessSlot()
    {
        AssertSatisfiable(SubClassOf(Reference("A"), DataMaxCard(1, "d")));

        AssertUnsatisfiable(
            SubClassOf(Reference("A"), DataMaxCard(1, "d")),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(7))));
    }

    /// <summary>
    /// A QUALIFIED maximum of one against two told values that are provably
    /// distinct AND provably in the qualifying range is violated in every model:
    /// each point is realised by every model, so two range-typed fillers exist
    /// where the bound admits one. The carrier is unsatisfiable through the max
    /// slot's points-only overflow rule.
    /// </summary>
    [TestMethod]
    public void QualifiedMaxSlotDistinctInRangePointsDecidesInconsistent()
    {
        AssertUnsatisfiable(
            SubClassOf(Reference("A"), DataMaxCard(1, "d", StringType)),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("alpha"))),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("beta"))));
    }

    /// <summary>
    /// The qualification guard: a qualified maximum bounds only the fillers its
    /// range types, so two told values OUTSIDE that range count against nothing
    /// and the carrier stays consistent. Neither point passes the per-maximum
    /// membership proof, the overflow rule raises nothing, and the slot's
    /// certificate cannot place two points under a bound of one, so the module
    /// delegates rather than claiming a verdict.
    /// </summary>
    [TestMethod]
    public void QualifiedMaxSlotOutOfRangePointsKeepsDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataMaxCard(1, "d", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(50))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(60))));
    }

    /// <summary>
    /// The witness-count guard: a counting demand beside a point leaves the pool's
    /// realised values open — the counted witnesses need not differ from the point —
    /// so the overflow rule does not engage, and the mixed pool's certificate cannot
    /// carry the slot either, because one told point is one witness short of the
    /// demanded two. Independently, the qualified <c>xsd:string</c> minimum side is a
    /// value space the checker does not size, so the counting obligation itself stays
    /// undecided. The module delegates.
    /// </summary>
    [TestMethod]
    public void QualifiedMaxSlotMixedPointAndCountingPoolKeepsDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataMaxCard(2, "d", StringType)),
            SubClassOf(Reference("A"), DataMinCard(2, "d", StringType)),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("alpha"))));
    }

    /// <summary>
    /// The identity guard: two told values whose comparison is indeterminate — a
    /// comment-bearing XML literal beside a plain one, which the byte scanner
    /// surfaces no comment events for — fold to no proven distinctness, so the
    /// overflow rule abstains rather than counting them as two.
    /// </summary>
    [TestMethod]
    public void QualifiedMaxSlotIndeterminatePairKeepsDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataMaxCard(1, "d", XmlLiteralType)),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<b><!-- note -->text</b>"))),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<b>text</b>"))));
    }

    //The mixed pool: told points beside exactly one counting demand.

    /// <summary>
    /// M1: the flagship shape at the sidecar surface. A range-less exact
    /// cardinality of two decomposes into a counting demand and a bound of two,
    /// which pool beside the two told points; the points are provably distinct, they
    /// fit the bound, and each of them inhabits the counting demand's own literal-top
    /// range, so the pool IS the model and the carrier stays satisfiable.
    /// </summary>
    [TestMethod]
    public void MixedPoolRangeLessExactTwoTwoDistinctPointsDecidesConsistent()
    {
        AssertSatisfiable(
            SubClassOf(Reference("A"), DataExactCard(2, "d")),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("alpha"))),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("beta"))));
    }

    /// <summary>
    /// M2: the QUALIFIED mixed pool, carried by genuine membership proofs rather
    /// than by the literal top. Both told integers lie inside the exact
    /// cardinality's own qualifying interval, so both witness its minimum half and
    /// both count against its maximum half — two values under a bound of two.
    /// </summary>
    [TestMethod]
    public void MixedPoolQualifiedIntegerExactTwoInRangePointsDecidesConsistent()
    {
        AssertSatisfiable(
            SubClassOf(Reference("A"), DataExactCard(2, "d", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(3))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(7))));
    }

    /// <summary>
    /// M3: a told value OUTSIDE the counting demand's range is a filler that counts
    /// toward the bound but witnesses nothing, and the pool still certifies when the
    /// remaining values carry the minimum on their own. The string point cannot
    /// inhabit the integer interval, the integer point can, and one witness meets a
    /// demand for one.
    /// </summary>
    [TestMethod]
    public void MixedPoolPointOutsideCountingRangeStillCertifiesWhenOthersWitness()
    {
        AssertSatisfiable(
            SubClassOf(Reference("A"), DataMaxCard(2, "d")),
            SubClassOf(Reference("A"), DataMinCard(1, "d", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(7))),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("alpha"))));
    }

    /// <summary>
    /// M4: the carrier-direction guard. A value told of the SLOT property is no
    /// filler of a SUB-property, so it cannot witness a counting demand carried
    /// there — witnessing runs down the sub-or-self relation, never up it. The pool
    /// finds no witness for the demand of one and the module delegates; a transposed
    /// direction test would wrongly certify it.
    /// </summary>
    [TestMethod]
    public void MixedPoolSuperPropertyPointDoesNotWitnessSubCounting()
    {
        AssertDelegates(
            SubDataProperty("q", "d"),
            SubClassOf(Reference("A"), DataMaxCard(2, "d")),
            SubClassOf(Reference("A"), DataMinCard(1, "q", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))));
    }

    /// <summary>
    /// M5: a counting demand above the witness count keeps its abstention. One told
    /// point witnesses the range-less exact cardinality of two once, and the
    /// certificate never invents the second witness the demand still needs, so the
    /// module delegates rather than claiming a model it cannot exhibit.
    /// </summary>
    [TestMethod]
    public void MixedPoolCountingAboveWitnessCountKeepsDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataExactCard(2, "d")),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("alpha"))));
    }

    /// <summary>
    /// M6: three provably-distinct points under a bound of two are more values than
    /// the slot admits, but the mixed branch raises no clash of its own — the
    /// points-only overflow rule declines every pool carrying a counting demand, so
    /// the surplus is an abstention and the module delegates.
    /// </summary>
    [TestMethod]
    public void MixedPoolDistinctPointsAboveBoundKeepsDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataMaxCard(2, "d")),
            SubClassOf(Reference("A"), DataMinCard(1, "d")),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(1))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(2))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(3))));
    }

    /// <summary>
    /// M7: two counting demands may ride disjoint witness sets, so the told points
    /// do not settle the pool's filler count and the certificate declines the pool
    /// outright — the mixed branch admits exactly one counting demand.
    /// </summary>
    [TestMethod]
    public void MixedPoolTwoCountingDemandsKeepDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataMaxCard(2, "d")),
            SubClassOf(Reference("A"), DataMinCard(1, "d", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)))),
            SubClassOf(Reference("A"), DataMinCard(1, "d", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 20), (Vocabulary.XsdFacets.MaxInclusive, 30)))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))));
    }

    /// <summary>
    /// M11: the mixed pool's OWN identity guard. Two told XML literals whose
    /// comparison is indeterminate — a comment-bearing fragment beside a plain one —
    /// fold to no proven distinctness, so the certificate aborts in its own fold
    /// before any witness question is asked. With a decidable pair the same shape
    /// certifies, so the row pins the fold rather than the witness count.
    /// </summary>
    [TestMethod]
    public void MixedPoolIndeterminatePairKeepsDelegating()
    {
        AssertDelegates(
            SubClassOf(Reference("A"), DataMaxCard(2, "d")),
            SubClassOf(Reference("A"), DataMinCard(1, "d")),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<b><!-- note -->text</b>"))),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<b>text</b>"))));
    }

    /// <summary>
    /// M12: one value forced under TWO sub-properties is a filler of both, and the
    /// fold keeps every carrier of it. Only the <c>q1</c> carrier lies below the
    /// counting demand's property, so the witness proof must range over the whole
    /// carrier set and run against that qualifying carrier — a fold keeping only the
    /// first carrier it met would find the non-qualifying <c>q2</c> and wrongly
    /// abstain.
    /// </summary>
    [TestMethod]
    public void MixedPoolSharedValueWitnessesThroughQualifyingCarrier()
    {
        AssertSatisfiable(
            SubDataProperty("q2", "d"),
            SubDataProperty("q1", "c"),
            SubDataProperty("c", "d"),
            SubClassOf(Reference("A"), DataMaxCard(2, "d")),
            SubClassOf(Reference("A"), DataMinCard(1, "c", IntegerRestriction(
                (Vocabulary.XsdFacets.MinInclusive, 1), (Vocabulary.XsdFacets.MaxInclusive, 10)))),
            SubClassOf(Reference("A"), DataHasValue("q2", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("q1", IntegerLiteral(5))));
    }

    /// <summary>
    /// R45: two named classes C ⊑ A and C ⊑ B share a carrier where A demands an
    /// existential above five and B a universal below three on the same property.
    /// The context arm's saturation accumulates the shared carrier's body
    /// <c>{A(x), B(x)}</c> and derives the clash there, condemning only C — the MU9
    /// observable pins that a weakened single-body clash must not derive a spurious
    /// A ⊑ ⊥, which would wrongly entail A ⊑ Bystander.
    /// </summary>
    [TestMethod]
    public void R45TwoCarrierDataClashCondemnsOnlyTheCarrierIntersection()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("C"), Reference("A")),
            SubClassOf(Reference("C"), Reference("B")),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("B"), DataAll("d", IntegerBelow(3))),
        ]);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "R45 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "C", Bystander), "The shared carrier C clashes on the accumulated body, so C is subsumed by the bystander.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "A", Bystander), "A alone carries only the existential, so A is not subsumed by the bystander (the MU9 observable).");
    }

    //R48/R49/R50 — the mutation-survivor killer rows (object-successor clash routing).

    /// <summary>
    /// R48: Q's r-successor is forced to be both the existential witness B and the
    /// universal filler A1, so the shared successor carries B's existential demand
    /// and A1's disjoint universal and clashes there. The clash body must invert
    /// through the Pred rule into the predecessor Q, condemning Q — B considered on
    /// its own carries only the existential and stays satisfiable. The MU9
    /// discriminator: a clash-body union truncated to its first contributor would
    /// condemn the shared successor unconditionally, wrongly entailing B ⊑ Bystander.
    /// </summary>
    [TestMethod]
    public void R48ClashBodyInversionIntoThePredecessorLeavesTheFillerSatisfiable()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("Q"), ObjectSome("r", Reference("B"))),
            SubClassOf(Reference("Q"), ObjectAll("r", Reference("A1"))),
            SubClassOf(Reference("B"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("A1"), DataAll("d", IntegerBelow(3))),
        ]);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "R48 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "Q", Bystander), "Q's r-successor inherits both B's existential and A1's universal, so Q clashes.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "B", Bystander), "B alone carries only the existential, so B is not subsumed by the bystander (the MU9 discriminator).");
    }

    /// <summary>
    /// R49: P's own existential lands first and decides the context consistent,
    /// then a three-hop subclass chain (P ⊑ X ⊑ Y) delivers Y's disjoint universal
    /// in a later saturation round. The demand-set memo must recognise the changed
    /// signature and re-decide, or the staggered universal never derives the clash
    /// (the MU14 discriminator: a memo keyed only on context presence would skip
    /// the re-decision).
    /// </summary>
    [TestMethod]
    public void R49StaggeredThreeHopUniversalForcesRedecision()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("P"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("P"), Reference("X")),
            SubClassOf(Reference("X"), Reference("Y")),
            SubClassOf(Reference("Y"), DataAll("d", IntegerBelow(3))),
        ]);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "R49 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "P", Bystander), "The staggered universal arriving through the three-hop chain must still be found and clash with P's own existential.");
    }

    /// <summary>
    /// R50: both predecessors reach the SAME cautious successor context through
    /// the single existential occurrence E ⊑ ∃r.C (successor contexts are keyed
    /// by function symbol, so one occurrence means one shared successor), and
    /// both fillers reach the SAME demand marker through the single occurrence
    /// W ⊑ ∃d.integer[≥5] (demand markers memoize per canonical descriptor).
    /// The X-side contributor lands first and the decision records the conflict
    /// set; the Y-side contributor is delayed three subclass hops and lands with
    /// the live-demand signature UNCHANGED — the identical marker — so its clash
    /// clause, the one whose Pred inversion condemns P_Y, can only come from the
    /// pinned re-emission path (the MU15 discriminator: a no-op re-emission
    /// leaves the delayed predecessor wrongly satisfiable).
    /// </summary>
    [TestMethod]
    public void R50SignatureUnchangedSecondContributorRoutesThroughReemission()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("E"), ObjectSome("r", Reference("C"))),
            SubClassOf(Reference("C"), DataAll("d", IntegerBelow(3))),
            SubClassOf(Reference("W"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("P_X"), Reference("E")),
            SubClassOf(Reference("P_X"), ObjectAll("r", Reference("X"))),
            SubClassOf(Reference("X"), Reference("W")),
            SubClassOf(Reference("P_Y"), Reference("Y1")),
            SubClassOf(Reference("Y1"), Reference("Y2")),
            SubClassOf(Reference("Y2"), Reference("E")),
            SubClassOf(Reference("P_Y"), ObjectAll("r", Reference("Y"))),
            SubClassOf(Reference("Y"), Reference("W")),
        ]);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "R50 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "P_X", Bystander), "P_X's r-successor carries C's universal and W's existential via X, so P_X clashes.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "P_Y", Bystander), "P_Y reaches the SAME successor three hops later carrying the SAME demand marker via Y; its clash clause must come from the pinned re-emission, so P_Y clashes too.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "E", Bystander), "E alone gives the successor only C's universal — no existential demand — so E stays satisfiable.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "X", Bystander), "X carries only W's existential, so X is not subsumed by the bystander.");
    }

    /// <summary>
    /// R51 (the disjunctive-lane sibling of R50): the dual of
    /// <c>∃d.integer[≥4] ⊑ B</c> seeds every context as
    /// <c>⊤ → {∀d.¬[≥4](x), B(x)}</c>, and both predecessors reach the SAME
    /// cautious successor (core C, disjoint with B) through the single
    /// occurrence <c>E ⊑ ∃r.C</c>. The X-side filler forces
    /// <c>∃d.integer[≥5]</c> CONDITIONALLY (the contributor clause's body is
    /// <c>X(x)</c> inside the shared successor), the probe refutes the dual's
    /// universal (<c>[≥5] ⊆ [≥4]</c>), and the narrowing must condition its
    /// residual <c>B</c> on that contributor body: <c>X(x) → B(x)</c> clashes
    /// with the disjoint core only where X holds, condemning P_X while E — whose
    /// filler carries no data demand and keeps the open marker branch — stays
    /// satisfiable. The refuting pools of this row are unit-forced, so its
    /// narrowings are unconditional in their own contexts; the
    /// conditional-contributor face — where an unconditional emission would
    /// wrongly condemn a sibling owner — is R52's jurisdiction. The P_Y route
    /// lands the IDENTICAL
    /// marker three subclass hops later with the live-demand signature
    /// unchanged, so its narrowing combination can only come from the
    /// disjunctive semi-naive re-emission — the second-contributor-route
    /// completeness face. The narrowing count strictly exceeds the refutation
    /// count (one refuted marker, one emission per contributor route),
    /// separating the emitted-clause counter from the probe and refutation
    /// counters.
    /// </summary>
    [TestMethod]
    public void R51DisjunctiveNarrowingConditionsOnContributorBodiesAcrossBothRoutes()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("E"), ObjectSome("r", Reference("C"))),
            SubClassOf(DataSome("d", IntegerAtLeast(4)), Reference("B")),
            DisjointClasses("B", "C"),
            SubClassOf(Reference("W"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("P_X"), Reference("E")),
            SubClassOf(Reference("P_X"), ObjectAll("r", Reference("X"))),
            SubClassOf(Reference("X"), Reference("W")),
            SubClassOf(Reference("P_Y"), Reference("Y1")),
            SubClassOf(Reference("Y1"), Reference("Y2")),
            SubClassOf(Reference("Y2"), Reference("E")),
            SubClassOf(Reference("P_Y"), ObjectAll("r", Reference("Y"))),
            SubClassOf(Reference("Y"), Reference("W")),
        ]);

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        Assert.IsTrue(totals.ContextDecided, "R51 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "P_X", Bystander), "P_X's filler forces the existential, the dual's universal refutes, and the body-conditioned narrowing derives B against the disjoint core — P_X clashes.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "P_Y", Bystander), "P_Y reaches the SAME successor with the IDENTICAL marker later; its narrowing combination comes from the disjunctive semi-naive re-emission, so P_Y clashes too.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "E", Bystander), "E's filler carries no data demand: the dual's marker branch stays open on its successor face and B is never forced there.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "X", Bystander), "X forces the existential but is not the successor core C, so the derived B never meets its disjoint partner on an X node alone.");
        Assert.IsGreaterThan(0L, totals.DisjunctiveDataRefutations, "The dual's universal refutes against the forced existential.");
        Assert.IsGreaterThan(1L, totals.DisjunctiveDataNarrowings, "Both contributor routes emit their own body-conditioned narrowing (the P_X and P_Y condemnations each require theirs), so the emitted-clause counter reads the per-route emissions the refutation counter does not.");
    }

    /// <summary>
    /// R52 (the shared-successor conditional-contributor row): both owners reach
    /// the SAME cautious successor through the single occurrence
    /// <c>OwnerBase ⊑ ∃r.K</c>. Q3 pushes X3 into it and X3 forces
    /// <c>∃d.integer[≥5]</c>, so inside the successor the existential contributor
    /// clause is CONDITIONAL on the pushed X3; Owner2 pushes NotB, disjoint with
    /// the dual's residual B. The probe refutes the dual's universal against the
    /// conditionally-carried existential, and the narrowing must condition its
    /// residual on that contributor's body: <c>X3(x) → B(x)</c> meets the
    /// disjoint NotB only where both pushes hold, condemning Both while Owner2 —
    /// whose successor carries NotB but never X3 — stays satisfiable. An
    /// unconditional narrowing (<c>⊤ → B(x)</c>) would derive
    /// <c>NotB(x) → ⊥</c> and wrongly condemn Owner2, so the Owner2 assertion is
    /// the body-conditioning mutation's killer. X3's own unit-forced pool
    /// refutes the same dual, so the entailed <c>X3 ⊑ B</c>
    /// (<c>[≥5] ⊆ [≥4]</c>) is the row's positive subsumption face.
    /// </summary>
    [TestMethod]
    public void R52SharedSuccessorNarrowingCondemnsOnlyTheForcingOwner()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("OwnerBase"), ObjectSome("r", Reference("K"))),
            SubClassOf(Reference("Q3"), Reference("OwnerBase")),
            SubClassOf(Reference("Q3"), ObjectAll("r", Reference("X3"))),
            SubClassOf(Reference("X3"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(DataSome("d", IntegerAtLeast(4)), Reference("B")),
            SubClassOf(Reference("Owner2"), Reference("OwnerBase")),
            SubClassOf(Reference("Owner2"), ObjectAll("r", Reference("NotB"))),
            DisjointClasses("B", "NotB"),
            SubClassOf(Reference("Both"), Reference("Q3")),
            SubClassOf(Reference("Both"), Reference("Owner2")),
        ]);

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        Assert.IsTrue(totals.ContextDecided, "R52 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "Both", Bystander), "Both pushes X3 and NotB into the shared successor, so the conditioned narrowing meets its disjoint partner there and Both clashes.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "Owner2", Bystander), "Owner2's successor carries NotB but never X3, so the narrowed residual must stay conditional on the pushed contributor — an unconditional emission would wrongly condemn Owner2.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "Q3", Bystander), "Q3's successor derives B under X3 with no disjoint partner pushed, so Q3 stays satisfiable.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "OwnerBase", Bystander), "OwnerBase pushes nothing, so its successor keeps the open marker branch alone.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "X3", "B"), "X3's unit-forced pool refutes the dual's universal, so the narrowing derives the entailed X3 under B.");
        Assert.IsGreaterThan(0L, totals.DisjunctiveDataRefutations, "The dual's universal refutes against the forced existential.");
        Assert.IsGreaterThan(0L, totals.DisjunctiveDataNarrowings, "The refutations emit their body-conditioned narrowings.");
    }

    /// <summary>
    /// R53 (the pool-growth re-cover row): two owners push INDEPENDENT filler
    /// classes into the SAME cautious successor through the single occurrence
    /// <c>OwnerBase ⊑ ∃r.K</c>, and both fillers force the IDENTICAL demand
    /// marker (<c>∃d.integer[≥5]</c>, one canonical marker by interning) through
    /// routes that share no body atom. The second-landed contributor arrives
    /// with the live-demand SIGNATURE unchanged and takes the memo path — but
    /// the residual B mints its own universal marker
    /// (<c>B ⊑ ∀d.integer[&lt;3]</c>), the pool GROWS when that marker's
    /// carrier derives, and the growth reprobe's already-refuted re-emission
    /// re-collects EVERY live contributor — so both owners condemn through
    /// their own route's narrowing whatever the landing order. OwnerBase pushes
    /// nothing and stays satisfiable. The quiet-pool face — where no later
    /// growth exists — is R54's jurisdiction.
    /// </summary>
    [TestMethod]
    public void R53PoolGrowthReprobeRecoversTheSecondContributorRoute()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("OwnerBase"), ObjectSome("r", Reference("K"))),
            SubClassOf(Reference("Q3"), Reference("OwnerBase")),
            SubClassOf(Reference("Q3"), ObjectAll("r", Reference("X3"))),
            SubClassOf(Reference("X3"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("Owner4"), Reference("OwnerBase")),
            SubClassOf(Reference("Owner4"), ObjectAll("r", Reference("Y3"))),
            SubClassOf(Reference("Y3"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(DataSome("d", IntegerAtLeast(4)), Reference("B")),
            SubClassOf(Reference("B"), DataAll("d", IntegerBelow(3))),
        ]);

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        Assert.IsTrue(totals.ContextDecided, "R53 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "Q3", Bystander), "Q3's pushed X3 forces the existential in the shared successor, the dual derives B there, and B's universal clashes with the forced value — Q3 clashes through its own route's narrowing.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "Owner4", Bystander), "Owner4's pushed Y3 reaches the IDENTICAL marker through an independent route; the pool-growth reprobe re-collects every live contributor, so Owner4 clashes too.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "OwnerBase", Bystander), "OwnerBase pushes nothing, so its successor keeps the open marker branch and stays satisfiable.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "X3", Bystander), "X3 forces a value at least 5 and rides the dual into B, whose universal forbids it — X3 is unsatisfiable.");
        Assert.IsGreaterThan(0L, totals.DisjunctiveDataRefutations, "The dual's universal refutes against the forced existential.");
    }

    /// <summary>
    /// R54 (the quiet-pool second-contributor row, the disjunctive lane's
    /// semi-naive exerciser): the R52 shape — a shared cautious successor
    /// whose dual keeps a data-free residual B with an object-level disjoint
    /// partner pushed by a sibling owner — plus a SECOND independent route to
    /// the IDENTICAL existential marker, delayed three subclass hops
    /// (<c>Y3 ⊑ Y2b ⊑ Y1b ⊑ W5 ⊑ ∃d.integer[≥5]</c>). The delayed contributor
    /// lands with the successor's live-demand signature UNCHANGED (the marker is
    /// already pooled), so the landing runs the memo path and exercises the
    /// disjunctive semi-naive re-emission. The verdicts do not depend on that
    /// re-emission: the shared marker is minted by the told intermediate axiom
    /// <c>W5 ⊑ ∃d.integer[≥5]</c>, whose contributor clause <c>W5(x) → marker</c>
    /// is live from clausification, so the refutation-time emission already
    /// conditions a narrowing on W5, and Both2 — who pushes Y3 beside the
    /// disjoint NotB — completes through the told chain in its successor. The
    /// row pins the quiet-pool verdict set and witnesses the re-emission's
    /// redundancy on this shape. Both rides the first-landed route's
    /// refutation-time narrowing; the single-route owners and OwnerBase stay
    /// satisfiable.
    /// </summary>
    [TestMethod]
    public void R54QuietPoolSecondContributorExercisesTheSemiNaiveReemission()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("OwnerBase"), ObjectSome("r", Reference("K"))),
            SubClassOf(Reference("Q3"), Reference("OwnerBase")),
            SubClassOf(Reference("Q3"), ObjectAll("r", Reference("X3"))),
            SubClassOf(Reference("X3"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(DataSome("d", IntegerAtLeast(4)), Reference("B")),
            SubClassOf(Reference("Owner2"), Reference("OwnerBase")),
            SubClassOf(Reference("Owner2"), ObjectAll("r", Reference("NotB"))),
            DisjointClasses("B", "NotB"),
            SubClassOf(Reference("Both"), Reference("Q3")),
            SubClassOf(Reference("Both"), Reference("Owner2")),
            SubClassOf(Reference("Owner4"), Reference("OwnerBase")),
            SubClassOf(Reference("Owner4"), ObjectAll("r", Reference("Y3"))),
            SubClassOf(Reference("Y3"), Reference("Y2b")),
            SubClassOf(Reference("Y2b"), Reference("Y1b")),
            SubClassOf(Reference("Y1b"), Reference("W5")),
            SubClassOf(Reference("W5"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("Both2"), Reference("Owner4")),
            SubClassOf(Reference("Both2"), Reference("Owner2")),
        ]);

        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        Assert.IsTrue(totals.ContextDecided, "R54 must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "Both", Bystander), "Both pushes X3 and NotB into the shared successor, so the refutation-time narrowing meets its disjoint partner and Both clashes.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "Both2", Bystander), "Both2 pushes the DELAYED Y3 route beside NotB; the late contributor lands on the memo path with a quiet pool and exercises the semi-naive re-emission, while the condemnation completes through the record-time W5-conditioned narrowing and the told subclass chain.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "Owner2", Bystander), "Owner2 pushes NotB alone, so no route forces the existential on its successor face.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "Q3", Bystander), "Q3 pushes X3 alone: B derives without a disjoint partner, so Q3 stays satisfiable.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "Owner4", Bystander), "Owner4 pushes Y3 alone: B derives without a disjoint partner, so Owner4 stays satisfiable.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "OwnerBase", Bystander), "OwnerBase pushes nothing, so its successor keeps the open marker branch alone.");
        Assert.IsGreaterThan(0L, totals.DisjunctiveDataRefutations, "The dual's universal refutes against the conditionally-forced existential.");
    }

    /// <summary>
    /// MRK-02 (context engine): two syntactic occurrences of the identical demand
    /// range on two predecessors — A ⊑ ∃d.integer[≥5] and B ⊑ ∃d.integer[≥5], each
    /// under the disjoint universal ∀d.integer[&lt;3] — share ONE canonical demand
    /// marker through the mint (the shape the former reference-equality marker hole
    /// made unreachable), and the context arm still decides both carriers
    /// inconsistent.
    /// </summary>
    [TestMethod]
    public void MRK02TwoOccurrencesOfOneRangeShareMarkerAndDecide()
    {
        ModuleDecision decision = DecideWithBystander(
        [
            SubClassOf(Reference("A"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("A"), DataAll("d", IntegerBelow(3))),
            SubClassOf(Reference("B"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("B"), DataAll("d", IntegerBelow(3))),
        ]);

        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The two-occurrence module must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "A", Bystander), "A's demand for a value at least 5 clashes with its universal below 3.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "B", Bystander), "B carries the SAME demand range at a second occurrence and clashes through the shared canonical marker.");
    }

    //F1 — domain typing (TBox-only rows the context survey admits).

    /// <summary>R01: a data demand fires the property domain, so the carrier is entailed to be the domain class.</summary>
    [TestMethod]
    public void R01DomainFiresOnDemand()
    {
        AssertEntailed("A", "C",
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataSome("d", Integer)));
    }

    /// <summary>R03: a sub-property demand fires a super-property domain through the HasValueOf closure.</summary>
    [TestMethod]
    public void R03DomainFiresThroughSubPropertyClosure()
    {
        AssertEntailed("A", "C",
            DataDomain("e", Reference("C")),
            SubDataProperty("d", "e"),
            SubClassOf(Reference("A"), DataSome("d", Integer)));
    }

    /// <summary>R04: a demand on an unrelated property does not fire the domain.</summary>
    [TestMethod]
    public void R04UnrelatedPropertyDoesNotFireDomain()
    {
        AssertNotEntailed("A", "C",
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataSome("e", Integer)));
    }

    /// <summary>R05: a has-value demand fires the property domain.</summary>
    [TestMethod]
    public void R05DomainFiresOnHasValue()
    {
        AssertEntailed("A", "C",
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))));
    }

    /// <summary>R06: a positive counting demand fires the property domain.</summary>
    [TestMethod]
    public void R06DomainFiresOnMinCardinality()
    {
        AssertEntailed("A", "C",
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataMinCard(1, "d", Integer)));
    }

    /// <summary>
    /// R08: domain typing to C plus B ⊑ C over a bare xsd:string demand. The shared
    /// checker abstains on a lone xsd:string value space (it sizes only the numeric,
    /// boolean, and temporal families), so the string demand is an undecided
    /// obligation and the module delegates through the context arm — parity with the
    /// fragment-relative tableau arms, which reach the same non-entailment of A ⊑ B.
    /// </summary>
    [TestMethod]
    public void R08BareStringDemandDelegates()
    {
        AssertDelegates(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("B"), Reference("C")),
            SubClassOf(Reference("A"), DataSome("d", StringType)));
    }

    /// <summary>R02: the class assertion x:A pins the carrier A onto ctx_x; the data demand fires the domain to C, and C ⊑ ⊥ collapses ctx_x, so the module is inconsistent through the ground context.</summary>
    [TestMethod]
    public void R02AboxCarryingDomainModuleInconsistent()
    {
        AssertInconsistent(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("C"), Nothing),
            SubClassOf(Reference("A"), DataSome("d", Integer)),
            ClassAssertion("A", "x"));
    }

    /// <summary>R07: the class assertion x:A pins the carrier A onto ctx_x; A demands an empty integer interval, an unsatisfiable data obligation that clashes ctx_x, so the module is inconsistent through the ground context.</summary>
    [TestMethod]
    public void R07AboxCarryingEmptyIntervalModuleInconsistent()
    {
        AssertInconsistent(
            SubClassOf(Reference("A"), DataSome("d", IntegerRestriction(
                (Vocabulary.XsdFacets.MinExclusive, 5), (Vocabulary.XsdFacets.MaxExclusive, 3)))),
            ClassAssertion("A", "x"));
    }

    //F2 — functional data properties.

    /// <summary>R09: overlapping ranges on a functional property share a value.</summary>
    [TestMethod]
    public void R09FunctionalOverlappingRangesSatisfiable()
    {
        AssertSatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerAtMost(10))));
    }

    /// <summary>R10: disjoint ranges on a functional property cannot share a value.</summary>
    [TestMethod]
    public void R10FunctionalDisjointRangesUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(3))));
    }

    /// <summary>R11: without functionality the two demands take different values.</summary>
    [TestMethod]
    public void R11NoFunctionalityTwoDemandsSatisfiable()
    {
        AssertSatisfiable(
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(3))));
    }

    /// <summary>R12: a functional property cannot carry two distinct values.</summary>
    [TestMethod]
    public void R12FunctionalMinCardinalityTwoUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataMinCard(2, "d", Integer)));
    }

    /// <summary>R13: functionality pools a demand on a property and one on its functional super-property across disjoint ranges.</summary>
    [TestMethod]
    public void R13FunctionalPoolingViaSubPropertyUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("f"),
            SubDataProperty("d", "f"),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataSome("f", IntegerBelow(3))));
    }

    /// <summary>R14: two has-value demands whose literals denote the same integer (5 and 05) agree on a functional property.</summary>
    [TestMethod]
    public void R14FunctionalSameValuedHasValuesSatisfiable()
    {
        AssertSatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("d", new Literal(Utf8Strings.From("05"), new NamedNode(Vocabulary.Xsd.Integer)))));
    }

    /// <summary>R15: two distinct has-value demands cannot both hold of a functional property's single value.</summary>
    [TestMethod]
    public void R15FunctionalDistinctHasValuesUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(7))));
    }

    /// <summary>R16: a functional existential constrained by a same-property universal still admits a value above the bound.</summary>
    [TestMethod]
    public void R16FunctionalExistentialUnderUniversalSatisfiable()
    {
        AssertSatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", Integer)),
            SubClassOf(Reference("A"), DataAll("d", IntegerAbove(10))));
    }

    /// <summary>R17: an existential whose range is disjoint from the same-property universal clashes without any functionality.</summary>
    [TestMethod]
    public void R17ExistentialDisjointFromUniversalUnsatisfiable()
    {
        AssertUnsatisfiable(
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))),
            SubClassOf(Reference("A"), DataAll("d", IntegerAbove(10))));
    }

    /// <summary>R18: a functional property pooling a string demand and an integer demand crosses disjoint families.</summary>
    [TestMethod]
    public void R18FunctionalAcrossDisjointFamiliesUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", StringType)),
            SubClassOf(Reference("A"), DataSome("d", Integer)));
    }

    /// <summary>R19: a vacuous minimum cardinality of zero never joins a functional pool.</summary>
    [TestMethod]
    public void R19FunctionalPoolExcludesVacuousMinCardinalityZero()
    {
        AssertSatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", Integer)),
            SubClassOf(Reference("A"), DataMinCard(0, "d", StringType)));
    }

    //F3 — disjoint / sub / equivalent data properties.

    /// <summary>R20: two has-value demands forcing the same value into a disjoint property pair clash.</summary>
    [TestMethod]
    public void R20DisjointPairSamePointValueUnsatisfiable()
    {
        AssertUnsatisfiable(
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataHasValue("a", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("b", IntegerLiteral(5))));
    }

    /// <summary>R21: two distinct has-value demands across a disjoint pair co-exist.</summary>
    [TestMethod]
    public void R21DisjointPairDistinctPointValuesSatisfiable()
    {
        AssertSatisfiable(
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataHasValue("a", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("b", IntegerLiteral(7))));
    }

    /// <summary>R22: a single property below both members of a disjoint pair forces one value into both (the common-subproperty rule).</summary>
    [TestMethod]
    public void R22CommonSubPropertyOfDisjointPairUnsatisfiable()
    {
        AssertUnsatisfiable(
            Disjoint("a", "b"),
            SubDataProperty("d", "a"),
            SubDataProperty("d", "b"),
            SubClassOf(Reference("A"), DataSome("d", Integer)));
    }

    /// <summary>R23: equivalent properties that are also disjoint reduce to a self-disjoint property.</summary>
    [TestMethod]
    public void R23EquivalentAndDisjointUnsatisfiable()
    {
        AssertUnsatisfiable(
            Disjoint("a", "b"),
            EquivalentDataProperties("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", Integer)));
    }

    /// <summary>R24: two unconstrained existentials across a disjoint pair take different integer values.</summary>
    [TestMethod]
    public void R24DisjointPairFreeValueChoiceSatisfiable()
    {
        AssertSatisfiable(
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", Integer)),
            SubClassOf(Reference("A"), DataSome("b", Integer)));
    }

    /// <summary>R25: a functional super-property pools demands from both members of a disjoint pair into one shared value.</summary>
    [TestMethod]
    public void R25FunctionalForcedSharedValueAcrossDisjointPairUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("f"),
            SubDataProperty("a", "f"),
            SubDataProperty("b", "f"),
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", Integer)),
            SubClassOf(Reference("A"), DataSome("b", Integer)));
    }

    /// <summary>R26: a super-property's asserted range constrains a sub-property's demand into an empty conjunction.</summary>
    [TestMethod]
    public void R26SubPropertyDemandUnderSuperRangeUnsatisfiable()
    {
        AssertUnsatisfiable(
            SubDataProperty("d", "e"),
            DataPropertyRange("e", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>R27: an asserted range on an unrelated property does not constrain the demand.</summary>
    [TestMethod]
    public void R27RangeOnUnrelatedPropertyDoesNotConstrain()
    {
        AssertSatisfiable(
            DataPropertyRange("e", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>R28: an equivalent property's asserted range flows across the equivalence and empties the demand.</summary>
    [TestMethod]
    public void R28EquivalentPropertyRangeConstrainsUnsatisfiable()
    {
        AssertUnsatisfiable(
            EquivalentDataProperties("d", "e"),
            DataPropertyRange("e", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>R29: a disjoint pair forced to a degenerate single-point interval canonicalizes both demands to the same point enumeration, so the context arm decides the disjoint clash decisively — the module is inconsistent.</summary>
    [TestMethod]
    public void R29DisjointPairForcedToDegeneratePointUnsatisfiable()
    {
        AssertUnsatisfiable(
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)))),
            SubClassOf(Reference("A"), DataSome("b", IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)))));
    }

    //R46/R47 — addendum-certified UNSAT rows (the F1 exact-core fix; the MU1 two-hop sub-closure catcher).

    /// <summary>
    /// R46: a functional property pools two existentials that each individually
    /// survive the node universal, but the pooled conjunction — both existentials
    /// AND the universal together — is empty, so the carrier is unsatisfiable
    /// through the context arm too (the functional-pool clash driven by the
    /// universal, the exact-core fix's parity pin).
    /// </summary>
    [TestMethod]
    public void R46FunctionalPoolClashDrivenByUniversalUnsatisfiable()
    {
        AssertUnsatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(2))),
            SubClassOf(Reference("A"), DataAll("d", OneOf(IntegerLiteral(1), IntegerLiteral(6)))));
    }

    /// <summary>
    /// R47: a two-hop sub-property closure (d ⊑ e ⊑ f) carries a range asserted only
    /// on the top of the chain, f, down to a demand on d through the context arm's
    /// <c>HasValueOf</c> closure — the MU1 catcher every prior sub-closure row
    /// reached in a single hop.
    /// </summary>
    [TestMethod]
    public void R47TwoHopSubPropertyRangeClosureUnsatisfiable()
    {
        AssertUnsatisfiable(
            SubDataProperty("d", "e"),
            SubDataProperty("e", "f"),
            DataPropertyRange("f", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>
    /// The bare rdf:XMLLiteral range through the context arm: a functional property
    /// pooling two demands that name the datatype but force no point hands the
    /// sidecar a named range whose inhabitants the checker does not enumerate, an
    /// undecided obligation, so the module delegates rather than decide. Value
    /// identity settles pooled POINTS; a range with none is out of its reach.
    /// </summary>
    [TestMethod]
    public void XmlLiteralFunctionalPoolDelegates()
    {
        AssertDelegates(
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", XmlLiteralType)),
            SubClassOf(Reference("A"), DataSome("d", XmlLiteralType)));
    }

    /// <summary>
    /// The consistent XMLLiteral pool: a functional property forced to two XML
    /// literals that canonicalize alike — they differ by attribute order and
    /// empty-element form only — holds one value, so the carrier stays satisfiable
    /// and the arm decides it (the sidecar mirror of WebOnt-miscellaneous-202).
    /// </summary>
    [TestMethod]
    public void XmlLiteralFunctionalEqualPointsStaysConsistent()
    {
        AssertSatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<br /><img src=\"vn.png\" alt=\"Venn diagram\"></img>"))),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<br ></br><img alt=\"Venn diagram\" src=\"vn.png\" />"))));
    }

    /// <summary>
    /// The clashing XMLLiteral pool: a functional property forced to two XML
    /// literals whose canonical forms differ cannot hold both, so the carrier is
    /// unsatisfiable through the context arm (the sidecar mirror of
    /// WebOnt-miscellaneous-203/204).
    /// </summary>
    [TestMethod]
    public void XmlLiteralFunctionalDistinctPointsDecidesInconsistent()
    {
        AssertUnsatisfiable(
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<span><b>Good!</b></span>"))),
            SubClassOf(Reference("A"), DataHasValue("d", XmlLiteralValue("<span><b>Bad!</b></span>"))));
    }

    //Budget pin, retraction bookkeeping.

    /// <summary>
    /// The budget ceiling abstains, never hangs or misdecides: an oracle-bearing
    /// admitted module bounded below its fixpoint requirement exhausts the budget
    /// and abstains with no verdict, while the unbounded decision reaches the
    /// unsatisfiability.
    /// </summary>
    /// <remarks>
    /// Sizing: every <see cref="DataRestrictionConsistency"/> oracle invocation now
    /// spends one application through the engine's internal <c>OracleBudgetTick</c>,
    /// on top of the ordinary Core and Hyper work, so a ceiling meant to land inside
    /// the oracle-gated tail must be measured, not guessed. A calibration scan
    /// (<c>MaxInferences</c> 1 through 40 against this exact module, reading
    /// <see cref="ContextSaturationStatistics"/> off each trial) finds 11 the
    /// smallest budget under which the decision completes and 10 the largest that
    /// still abstains. At 10 the run has already spent its five Core seeds (the
    /// trivial, A, and Bystander query contexts) and landed three of the four
    /// demand-marker Hyper firings — well past plain seeding — and is mid-decision
    /// on the oracle-gated conjunction check, one application short of completing
    /// it: the ceiling pins the oracle gate itself, not merely a budget too small to
    /// ever reach the data obligation.
    /// </remarks>
    [TestMethod]
    public void BudgetCeilingAbstainsOnDataObligation()
    {
        OwlAxiom[] tbox =
        [
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataAll("d", IntegerBelow(3))),
            SubClassOf(Reference(Bystander), Thing),
        ];

        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideModule(Module(tbox), new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 10), progressSampler: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "A budget one application short of the oracle-gated decision abstains.");
        Assert.IsNull(abstained.Verdict, "A budget abstention carries no verdict.");

        ModuleDecision decided = ContextSaturationModuleReasoner.DecideModule(Module(tbox), TestContext.CancellationToken);
        Assert.IsTrue(decided.Statistics.ContextTotals.ContextDecided, "The unbounded decision is context-decided.");
        Assert.IsTrue(HasSubsumption(decided.Verdict!, "A", Bystander), "The unbounded decision proves A unsatisfiable.");
    }

    /// <summary>
    /// The retraction bookkeeping (white-box on the internal <see cref="Context"/>
    /// surface): a data-demand count survives while any live clause still carries the
    /// descriptor and drops to zero when none does. Inserting a body-weaker
    /// same-descriptor clause keeps the count at the live total; tombstoning one live
    /// clause decrements by its own head; tombstoning the last drops the demand. A
    /// skipped Tombstone decrement would leave the count stuck above zero.
    /// </summary>
    [TestMethod]
    public void DataDemandCountSurvivesLiveClauseAndDropsWhenNoneRemains()
    {
        const int marker = 7;
        Context context = new(0, [], isRoot: false, homeIndividual: -1, new HashSet<int> { marker });
        DlClause first = DlClause.Create([DlLiteral.Concept(2, DlTerm.Central)], [DlLiteral.Concept(marker, DlTerm.Central)], 0);
        DlClause second = DlClause.Create([DlLiteral.Concept(3, DlTerm.Central)], [DlLiteral.Concept(marker, DlTerm.Central)], 0);

        int firstId = context.Insert(first, isPredEligible: false, decidedUnderNoChoice: true, maximalIndexes: [0]);
        Assert.AreEqual(1, context.DataDemandCount(marker), "The first demand clause raises the count to one.");

        int secondId = context.Insert(second, isPredEligible: false, decidedUnderNoChoice: true, maximalIndexes: [0]);
        Assert.AreEqual(2, context.DataDemandCount(marker), "The second same-descriptor demand clause raises the count to two.");

        context.Tombstone(firstId);
        Assert.AreEqual(1, context.DataDemandCount(marker), "The demand survives while a live clause still carries the descriptor.");

        List<int> live = [];
        context.CollectLiveDataDemands(live);
        Assert.Contains(marker, live, "The marker is a live demand while one clause carries it.");

        context.Tombstone(secondId);
        Assert.AreEqual(0, context.DataDemandCount(marker), "The demand drops when no live clause carries the descriptor.");

        live.Clear();
        context.CollectLiveDataDemands(live);
        Assert.DoesNotContain(marker, live, "The marker is no longer a live demand once all its clauses are tombstoned.");
    }

    /// <summary>Asserts the class A is satisfiable through the context arm: the module is context-decided and A is not subsumed by the bystander.</summary>
    /// <param name="tbox">The TBox and RBox axioms.</param>
    private void AssertSatisfiable(params OwlAxiom[] tbox)
    {
        ModuleDecision decision = DecideWithBystander(tbox);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "A satisfiability row must be context-decided, not delegated.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, "A", Bystander), "A satisfiable class is not subsumed by the unrelated bystander.");
    }

    /// <summary>Asserts the class A is unsatisfiable through the context arm: the module is context-decided and A is subsumed by the bystander (<c>A ⊑ ⊥ ⊑ everything</c>).</summary>
    /// <param name="tbox">The TBox and RBox axioms.</param>
    private void AssertUnsatisfiable(params OwlAxiom[] tbox)
    {
        ModuleDecision decision = DecideWithBystander(tbox);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "An unsatisfiability row must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, "A", Bystander), "An unsatisfiable class is subsumed by every class, the bystander included.");
    }

    /// <summary>Asserts the entailment <c>sub ⊑ super</c> is context-decided and present on the subsumption sweep.</summary>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <param name="tbox">The TBox and RBox axioms.</param>
    private void AssertEntailed(string sub, string super, params OwlAxiom[] tbox)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(tbox), TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "An entailment row must be context-decided, not delegated.");
        Assert.IsTrue(HasSubsumption(decision.Verdict!, sub, super), $"{sub} ⊑ {super} must be entailed.");
    }

    /// <summary>Asserts the entailment <c>sub ⊑ super</c> is context-decided and does NOT surface on the subsumption sweep.</summary>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <param name="tbox">The TBox and RBox axioms.</param>
    private void AssertNotEntailed(string sub, string super, params OwlAxiom[] tbox)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(tbox), TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "A control row must be context-decided, not delegated.");
        Assert.IsFalse(HasSubsumption(decision.Verdict!, sub, super), $"{sub} ⊑ {super} must NOT be entailed.");
    }

    /// <summary>Asserts the module falls outside the context slice and delegates to the fallback (context path not taken).</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertDelegates(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The module must delegate: the context arm must not claim a fragment-relative verdict.");
    }

    /// <summary>Asserts the module is context-decided and inconsistent — the module collapses a ground context, so the verdict is decisively inconsistent.</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertInconsistent(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "An inconsistency row must be context-decided, not delegated.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The module collapses a ground context, so it is decisively inconsistent.");
    }

    /// <summary>Decides a module through the context arm with the bystander class appended so an unsatisfiable A surfaces as <c>A ⊑ Bystander</c>.</summary>
    /// <param name="tbox">The TBox and RBox axioms.</param>
    /// <returns>The context decision.</returns>
    private ModuleDecision DecideWithBystander(OwlAxiom[] tbox)
    {
        List<OwlAxiom> axioms = [.. tbox, SubClassOf(Reference(Bystander), Thing)];

        return ContextSaturationModuleReasoner.DecideModule(Module([.. axioms]), TestContext.CancellationToken);
    }

    /// <summary>Whether the verdict's subsumption list carries the named pair, by an explicit scan.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <returns><see langword="true"/> when the pair is present.</returns>
    private static bool HasSubsumption(ModuleVerdict verdict, string sub, string super)
    {
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            if(Local(subClass) == sub && Local(superClass) == super)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The full IRI of an example-namespace local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string local)
    {
        return Utf8Strings.From(Example + local);
    }

    /// <summary>The local name of an example-namespace node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The local name.</returns>
    private static string Local(NamedNode node)
    {
        string iri = node.Iri.ToString();

        return iri.StartsWith(Example, System.StringComparison.Ordinal) ? iri[Example.Length..] : iri;
    }

    /// <summary>A named-class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Reference(string local)
    {
        return new OwlClassReference(new NamedNode(Iri(local)));
    }

    /// <summary>The <c>owl:Thing</c> reference.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The <c>owl:Nothing</c> reference.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>The named <c>xsd:integer</c> data range.</summary>
    private static OwlDatatypeReference Integer { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>The named <c>xsd:string</c> data range.</summary>
    private static OwlDatatypeReference StringType { get; } = new(new NamedNode(Vocabulary.Xsd.String));

    /// <summary>The named <c>xsd:boolean</c> data range.</summary>
    private static OwlDatatypeReference BooleanType { get; } = new(new NamedNode(Vocabulary.Xsd.Boolean));

    /// <summary>The named <c>rdf:XMLLiteral</c> data range, a datatype the value-space checker does not model.</summary>
    private static OwlDatatypeReference XmlLiteralType { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#XMLLiteral")));

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Iri(marker)), new NamedNode(Iri("p")), new NamedNode(Iri("o")), Graph: null);
    }

    /// <summary>A <c>SubClassOf</c> axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A <c>ClassAssertion</c> axiom.</summary>
    /// <param name="local">The asserted class local name.</param>
    /// <param name="individual">The individual local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(string local, string individual)
    {
        return new OwlClassAssertionAxiom(Reference(local), new NamedNode(Iri(individual))) { Origin = Origin("assert") };
    }

    /// <summary>A <c>DataPropertyDomain</c> axiom.</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyDomainAxiom DataDomain(string property, OwlClassExpression domain)
    {
        return new OwlDataPropertyDomainAxiom(new NamedNode(Iri(property)), domain) { Origin = Origin("domain") };
    }

    /// <summary>A <c>DataPropertyRange</c> axiom.</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The asserted range.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyRangeAxiom DataPropertyRange(string property, OwlDataRange range)
    {
        return new OwlDataPropertyRangeAxiom(new NamedNode(Iri(property)), range) { Origin = Origin("range") };
    }

    /// <summary>A <c>SubDataPropertyOf</c> axiom.</summary>
    /// <param name="sub">The sub-property local name.</param>
    /// <param name="super">The super-property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubDataPropertyOfAxiom SubDataProperty(string sub, string super)
    {
        return new OwlSubDataPropertyOfAxiom(new NamedNode(Iri(sub)), new NamedNode(Iri(super))) { Origin = Origin("subdata") };
    }

    /// <summary>An <c>EquivalentDataProperties</c> axiom over a pair.</summary>
    /// <param name="first">The first property local name.</param>
    /// <param name="second">The second property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentDataPropertiesAxiom EquivalentDataProperties(string first, string second)
    {
        return new OwlEquivalentDataPropertiesAxiom(new NamedNode(Iri(first)), new NamedNode(Iri(second))) { Origin = Origin("equivdata") };
    }

    /// <summary>A <c>FunctionalDataProperty</c> axiom.</summary>
    /// <param name="property">The data property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlFunctionalDataPropertyAxiom Functional(string property)
    {
        return new OwlFunctionalDataPropertyAxiom(new NamedNode(Iri(property))) { Origin = Origin("functional") };
    }

    /// <summary>A <c>DisjointDataProperties</c> axiom over a pair.</summary>
    /// <param name="first">The first property local name.</param>
    /// <param name="second">The second property local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointDataPropertiesAxiom Disjoint(string first, string second)
    {
        return new OwlDisjointDataPropertiesAxiom([new NamedNode(Iri(first)), new NamedNode(Iri(second))]) { Origin = Origin("disjoint") };
    }

    /// <summary>A <c>DisjointClasses</c> axiom over two named classes.</summary>
    /// <param name="first">The first class local name.</param>
    /// <param name="second">The second class local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom DisjointClasses(string first, string second)
    {
        return new OwlDisjointClassesAxiom([Reference(first), Reference(second)]) { Origin = Origin("disjointclasses") };
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference ObjectProperty(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Iri(local)));
    }

    /// <summary>A single-property object existential (<c>ObjectSomeValuesFrom</c>).</summary>
    /// <param name="property">The object property local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The class expression.</returns>
    private static OwlObjectSomeValuesFrom ObjectSome(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(ObjectProperty(property), filler);
    }

    /// <summary>A single-property object universal (<c>ObjectAllValuesFrom</c>).</summary>
    /// <param name="property">The object property local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The class expression.</returns>
    private static OwlObjectAllValuesFrom ObjectAll(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(ObjectProperty(property), filler);
    }

    /// <summary>A single-property data existential (<c>DataSomeValuesFrom</c>).</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Iri(property))], range);
    }

    /// <summary>A single-property data universal (<c>DataAllValuesFrom</c>).</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The constraining range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataAllValuesFrom DataAll(string property, OwlDataRange range)
    {
        return new OwlDataAllValuesFrom([new NamedNode(Iri(property))], range);
    }

    /// <summary>A literal-value data restriction (<c>DataHasValue</c>).</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="value">The required literal value.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataHasValue DataHasValue(string property, Literal value)
    {
        return new OwlDataHasValue(new NamedNode(Iri(property)), value);
    }

    /// <summary>A positive data minimum-cardinality restriction (<c>DataMinCardinality</c>).</summary>
    /// <param name="count">The minimum count.</param>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The qualifying range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataMinCard(int count, string property, OwlDataRange range)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, count, new NamedNode(Iri(property)), range);
    }

    /// <summary>An unqualified (range-less) positive data minimum-cardinality restriction (<c>DataMinCardinality</c>) — a counting demand over the literal top.</summary>
    /// <param name="count">The minimum count.</param>
    /// <param name="property">The data property local name.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataMinCard(int count, string property)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, count, new NamedNode(Iri(property)), null);
    }

    /// <summary>An unqualified (range-less) data exact-cardinality restriction (<c>DataExactCardinality</c>) — a counting demand and a bound of the same size over the literal top.</summary>
    /// <param name="count">The exact count.</param>
    /// <param name="property">The data property local name.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataExactCard(int count, string property)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Exact, count, new NamedNode(Iri(property)), null);
    }

    /// <summary>A QUALIFIED data exact-cardinality restriction (<c>DataExactCardinality</c>) — a counting demand and a bound of the same size, both over the qualifying range.</summary>
    /// <param name="count">The exact count.</param>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The qualifying range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataExactCard(int count, string property, OwlDataRange range)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Exact, count, new NamedNode(Iri(property)), range);
    }

    /// <summary>An unqualified (range-less) data maximum-cardinality restriction (<c>DataMaxCardinality</c>) — the sidecar's single max slot at bound one.</summary>
    /// <param name="count">The maximum count.</param>
    /// <param name="property">The data property local name.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataMaxCard(int count, string property)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Max, count, new NamedNode(Iri(property)), null);
    }

    /// <summary>A QUALIFIED data maximum-cardinality restriction (<c>DataMaxCardinality</c>) — a bound that counts only the fillers its range types.</summary>
    /// <param name="count">The maximum count.</param>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The qualifying range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataCardinality DataMaxCard(int count, string property, OwlDataRange range)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Max, count, new NamedNode(Iri(property)), range);
    }

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>An <c>xsd:string</c> typed literal.</summary>
    /// <param name="value">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal StringLiteral(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Vocabulary.Xsd.String));
    }

    /// <summary>An <c>rdf:XMLLiteral</c> typed literal.</summary>
    /// <param name="value">The XML fragment lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal XmlLiteralValue(string value)
    {
        return new Literal(Utf8Strings.From(value), new NamedNode(Vocabulary.Rdf.XmlLiteral));
    }

    /// <summary>A data enumeration (<c>DataOneOf</c>).</summary>
    /// <param name="literals">The enumerated literals.</param>
    /// <returns>The data range.</returns>
    private static OwlDataOneOf OneOf(params Literal[] literals)
    {
        return new OwlDataOneOf(literals);
    }

    /// <summary>A data complement (<c>DataComplementOf</c>).</summary>
    /// <param name="range">The complemented range.</param>
    /// <returns>The data range.</returns>
    private static OwlDataComplementOf ComplementOf(OwlDataRange range)
    {
        return new OwlDataComplementOf(range);
    }

    /// <summary>A data intersection (<c>DataIntersectionOf</c>).</summary>
    /// <param name="ranges">The intersected ranges.</param>
    /// <returns>The data range.</returns>
    private static OwlDataIntersectionOf IntersectionOf(params OwlDataRange[] ranges)
    {
        return new OwlDataIntersectionOf(ranges);
    }

    /// <summary>A union of singleton integer enumerations over the inclusive integer range — a wide disjunction feeding the DNF-cap probe.</summary>
    /// <param name="from">The first integer value.</param>
    /// <param name="to">The last integer value.</param>
    /// <returns>The data range.</returns>
    private static OwlDataUnionOf IntegerSingletonUnion(int from, int to)
    {
        List<OwlDataRange> singletons = [];
        for(int value = from; value <= to; value++)
        {
            singletons.Add(OneOf(IntegerLiteral(value)));
        }

        return new OwlDataUnionOf(singletons);
    }

    /// <summary>An integer range bounded below inclusively.</summary>
    /// <param name="bound">The inclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtLeast(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, bound));
    }

    /// <summary>An integer range bounded above inclusively.</summary>
    /// <param name="bound">The inclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtMost(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MaxInclusive, bound));
    }

    /// <summary>An integer range bounded below exclusively.</summary>
    /// <param name="bound">The exclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAbove(int bound)
    {
        return IntegerRestriction((Vocabulary.XsdFacets.MinExclusive, bound));
    }

    /// <summary>An integer range bounded above exclusively.</summary>
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
            facets.Add(new OwlFacetRestriction(new NamedNode(facet), IntegerLiteral(bound)));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }

    /// <summary>A dateTime datatype restriction over the given facet bounds.</summary>
    /// <param name="bounds">The facet-lexical pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction DateTimeRestriction(params (Utf8String Facet, string Lexical)[] bounds)
    {
        List<OwlFacetRestriction> facets = [];
        foreach((Utf8String facet, string lexical) in bounds)
        {
            facets.Add(new OwlFacetRestriction(new NamedNode(facet), new Literal(Utf8Strings.From(lexical), new NamedNode(Vocabulary.Xsd.DateTime))));
        }

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.DateTime), facets);
    }
}
