using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="ElCoupledModuleReasoner"/>: the EL pay-as-you-go
/// fast-path decides EL⊥ modules by saturation with a verdict matching the
/// snapshot tableau on their shared fragment (consistency including the
/// non-empty-domain requirement, ABox clashes, <c>SameIndividual</c> merges,
/// existentials into ⊥, transitive roles with ranges, and module-local
/// classification), decides property chains by role composition where the
/// chain-blind tableau cannot (verified against the known correct answer), and
/// delegates every module outside the fragment to the tableau oracle unchanged.
/// </summary>
[TestClass]
internal sealed class ElCoupledModuleReasonerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A satisfiable EL TBox with an ABox individual is decided consistent by the fast-path, matching the snapshot.</summary>
    [TestMethod]
    public void ElDecidesConsistentModule()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Car"), Class("Vehicle")),
            ClassAssertion(Class("Car"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A TBox forcing owl:Thing empty is inconsistent — OWL 2 DL requires a non-empty domain — and the EL fast-path matches the snapshot's anonymous-root verdict.</summary>
    [TestMethod]
    public void NonEmptyDomainForcesInconsistency()
    {
        ReasoningModule module = Module(SubClassOf(ThingReference, NothingReference));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>Two disjoint types on one individual clash; dropping one assertion is consistent — both decided by the fast-path.</summary>
    [TestMethod]
    public void AboxClashIsInconsistent()
    {
        ReasoningModule clashing = Module(
            Disjoint(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("B"), Individual("x")));
        AssertElDecidesLike(clashing, expectConsistent: false);

        ReasoningModule consistent = Module(
            Disjoint(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")));
        AssertElDecidesLike(consistent, expectConsistent: true);
    }

    /// <summary><c>SameIndividual</c> merges two individuals onto one node, so disjoint types asserted across them clash — matching the snapshot's pre-merge.</summary>
    [TestMethod]
    public void SameIndividualMergesTypes()
    {
        ReasoningModule merged = Module(
            Disjoint(Class("A"), Class("B")),
            SameIndividual(Individual("x"), Individual("y")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("B"), Individual("y")));
        AssertElDecidesLike(merged, expectConsistent: false);

        ReasoningModule distinct = Module(
            Disjoint(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("B"), Individual("y")));
        AssertElDecidesLike(distinct, expectConsistent: true);
    }

    /// <summary>An individual forced to have an existential successor in an empty class is itself empty, hence inconsistent — EL bottom-propagation through the generated edge, matching the snapshot.</summary>
    [TestMethod]
    public void ExistentialIntoBottomIsInconsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("C"), Some("r", Class("D"))),
            SubClassOf(Class("D"), NothingReference),
            ClassAssertion(Class("C"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>A transitive role composes a chain of existentials; the fast-path admits transitive roles and matches the snapshot.</summary>
    [TestMethod]
    public void TransitiveRoleCompositionMatchesSnapshot()
    {
        ReasoningModule module = Module(
            Transitive("r"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Some("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// A property range is decided by the EL fast-path with a sound per-edge rule:
    /// the range types each existential's anonymous successor, not the named filler
    /// class. Here a range disjoint from a role's filler makes that role's owner
    /// (<c>A</c>) unsatisfiable, but the filler <c>B</c> used under a different,
    /// range-free role (<c>q</c>) stays satisfiable, so the asserted <c>D</c>
    /// individual keeps the module consistent — matching the tableau, where the old
    /// shared-filler over-approximation would have contaminated <c>B</c> and wrongly
    /// flipped the module inconsistent. Regression for that soundness hazard.
    /// </summary>
    [TestMethod]
    public void RangePropertyIsDecidedSoundly()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            Range("r", Class("C")),
            Disjoint(Class("B"), Class("C")),
            SubClassOf(Class("D"), Some("q", Class("B"))),
            ClassAssertion(Class("D"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);

        //The named filler B is not contaminated: it is satisfiable on its own (it is
        //C only as r's successor, never globally), so it is not subsumed by the
        //disjoint range C.
        ModuleVerdict elVerdict = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!;
        Assert.DoesNotContain("B→C", SubsumptionKeys(elVerdict), "The range must not contaminate the named filler class B.");
    }

    /// <summary>
    /// A range types an existential's successor, which then satisfies a
    /// left-existential and subsumes the owner: with <c>A ⊑ ∃r.B</c> and
    /// <c>range(r) = C</c>, A's r-successor is a C, so <c>∃r.C ⊑ G</c> fires and
    /// <c>A ⊑ G</c> — agreeing with the tableau's per-node universal reading of the
    /// range.
    /// </summary>
    [TestMethod]
    public void RangeTypesTheSuccessorAndSubsumesTheOwner()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            Range("r", Class("C")),
            SubClassOf(Some("r", Class("C")), Class("G")));

        AssertElDecidesLike(module, expectConsistent: true);
        Assert.Contains($"{Example}A→{Example}G", SubsumptionKeys(ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!), "The range types A's r-successor as C, firing the left-existential to subsume A under G.");
    }

    /// <summary>
    /// A range on a superrole reaches a subrole's successors: with <c>r ⊑ s</c> and
    /// <c>range(s) = C</c>, an <c>r</c>-edge is promoted to <c>s</c> and so its target
    /// gains C, making <c>A ⊑ ∃r.B</c> have a C-successor and subsume under
    /// <c>∃s.C ⊑ G</c> — agreeing with the tableau.
    /// </summary>
    [TestMethod]
    public void RangePropagatesThroughTheRoleHierarchy()
    {
        ReasoningModule module = Module(
            SubProperty("r", "s"),
            Range("s", Class("C")),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Some("s", Class("C")), Class("G")));

        AssertElDecidesLike(module, expectConsistent: true);
        Assert.Contains($"{Example}A→{Example}G", SubsumptionKeys(ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!), "The superrole's range types A's promoted s-successor as C, firing the left-existential to subsume A under G.");
    }

    /// <summary>A pure-EL TBox classifies to the same module-local subsumption set as the snapshot, and the set is non-empty.</summary>
    [TestMethod]
    public void ClassificationMatchesSnapshot()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Car"), Class("Vehicle")),
            SubClassOf(Class("Vehicle"), Class("Artifact")));

        ModuleVerdict reference = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
        Assert.IsNotEmpty(reference.Subsumptions);
        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A disjunction on the superclass side leaves the EL fragment; the module is delegated to the tableau and the verdict is unchanged.</summary>
    [TestMethod]
    public void DisjunctionDelegatesToTableau()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), new OwlObjectUnionOf([Class("B"), Class("C")])),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A universal restriction leaves the EL fragment; the module is delegated to the tableau and the verdict is unchanged.</summary>
    [TestMethod]
    public void UniversalDelegatesToTableau()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), new OwlObjectAllValuesFrom(Property("r"), Class("B"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>
    /// A property chain is decided by the EL fast-path through role composition — the
    /// one place the coupled engine decides strictly more than the tableau. With
    /// <c>r∘r⊑s</c>, the asserted r-chain <c>a→b→c</c> composes into an s-edge
    /// <c>a→c</c>; <c>c:E</c> and <c>∃s.E⊑⊥</c> then force <c>a</c> unsatisfiable, so
    /// the module is INCONSISTENT. The snapshot tableau misses this — it drops
    /// <c>ObjectPropertyChain</c> as beyond its ALC(H)+S fragment and so calls the
    /// module consistent — so the EL verdict is checked against the known correct
    /// answer, not the chain-blind tableau.
    /// </summary>
    [TestMethod]
    public void PropertyChainIsDecidedByComposition()
    {
        ReasoningModule module = Module(
            new OwlPropertyChainAxiom([Property("r"), Property("r")], Property("s")) { Origin = Origin("chain") },
            SubClassOf(Some("s", Class("E")), NothingReference),
            Edge("a", "r", "b"),
            Edge("b", "r", "c"),
            ClassAssertion(Class("E"), Individual("c")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The chain module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "a→b→c composes to a→c via r∘r⊑s; c:E and ∃s.E⊑⊥ force a unsatisfiable.");

        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The tableau drops the chain and so misses the inconsistency.");
    }

    /// <summary>A superclass-side data existential over a satisfiable range is a value demand the EL fast-path decides consistent, matching the snapshot.</summary>
    [TestMethod]
    public void DataExistentialWithSatisfiableRangeIsConsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataSome("age", Integer)),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>An empty data range (an inclusive lower bound above its upper bound) makes the carrier unsatisfiable, so a forced individual makes the module inconsistent — decided by the fast-path with the same value-space checker the tableau uses.</summary>
    [TestMethod]
    public void DataExistentialWithEmptyRangeForcesInconsistency()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataSome("age", IntegerBetween("5", "2"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary><c>DataHasValue</c> is the singleton-enumeration data existential; over a well-typed literal it is consistent and the fast-path decides it.</summary>
    [TestMethod]
    public void DataHasValueIsConsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataValue("age", Lit("7", Vocabulary.Xsd.Integer))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A <c>DataHasValue</c> over an ill-typed literal (a negative <c>xsd:nonNegativeInteger</c>) denotes no value, so its carrier is unsatisfiable and a forced individual makes the module inconsistent.</summary>
    [TestMethod]
    public void DataHasValueWithIllTypedLiteralForcesInconsistency()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataValue("age", Lit("-1", Vocabulary.Xsd.NonNegativeInteger))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>An empty data demand on an existential's named filler empties the filler, and the unsatisfiability propagates back over the role edge to the owner — decided by the fast-path, matching the snapshot and never contaminating unrelated classes.</summary>
    [TestMethod]
    public void EmptyDataDemandOnFillerFlowsBackToOwner()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), DataSome("age", IntegerBetween("5", "2"))),
            SubClassOf(Class("C"), DataSome("age", Integer)),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>A data existential nested directly in a superclass-side object-existential filler attaches its demand to the fresh successor; an empty range there empties the successor and the owner over the edge — decided by the fast-path.</summary>
    [TestMethod]
    public void NestedSuperclassDataExistentialIsDecided()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", DataSome("age", IntegerBetween("5", "2")))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>A data existential asserted directly on an individual attaches the demand to that individual; an empty range forces the individual empty and the module inconsistent — exercising the class-assertion path.</summary>
    [TestMethod]
    public void DataExistentialAssertedOnIndividualForcesInconsistency()
    {
        ReasoningModule module = Module(
            ClassAssertion(DataSome("age", IntegerBetween("5", "2")), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>
    /// A data existential on the SUBCLASS side names the concept of everything carrying such a value,
    /// and the fast-path recognizes it on each class whose own value demand entails it: <c>B</c> demands
    /// an age in <c>[1,5]</c>, which lies inside <c>xsd:integer</c>, so <c>∃age.integer ⊑ Y</c> gives
    /// <c>B ⊑ Y</c> — the subsumption the snapshot derives too.
    /// </summary>
    [TestMethod]
    public void SubclassSideDataExistentialDecides()
    {
        ReasoningModule module = Module(
            SubClassOf(DataSome("age", Integer), Class("Y")),
            SubClassOf(Class("B"), DataSome("age", IntegerBetween("1", "5"))),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleVerdict verdict = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!;
        Assert.IsTrue(Subsumes(verdict, "B", "Y"), "B's demanded age lies inside the recognized range, so B falls under the left-position concept's conclusion.");
    }

    /// <summary>A subclass-side EMPTY data range must NOT be read as a demand on its carrier: it is the concept nothing falls under, so the module stays consistent and the fast-path decides it, matching the tableau — the regression guard against reading the subclass occurrence as a value demand.</summary>
    [TestMethod]
    public void SubclassSideEmptyDataRangeDecides()
    {
        ReasoningModule module = Module(
            SubClassOf(DataSome("age", IntegerBetween("5", "2")), Class("Y")),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// A data existential in an equivalence is read in BOTH directions: the superclass direction puts a
    /// value demand on <c>A</c>'s members, and the subclass direction names the concept the fast-path
    /// recognizes, so a class demanding a narrower range on the same property is derived to be an
    /// <c>A</c>. Both directions are decided and the verdict matches the snapshot.
    /// </summary>
    [TestMethod]
    public void EquivalenceWithDataExistentialDecidesBothDirections()
    {
        ReasoningModule module = Module(
            Equivalent(Class("A"), DataSome("age", Integer)),
            SubClassOf(Class("B"), DataSome("age", IntegerBetween("1", "5"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleVerdict verdict = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!;
        Assert.IsTrue(Subsumes(verdict, "B", "A"), "The recognition direction of the equivalence: B's demand entails the defining data existential, so B ⊑ A.");
    }

    /// <summary>
    /// A data existential in a disjointness operand is recognized on the class whose own demand entails
    /// it, and the disjointness then empties that class: every <c>A</c> carries an age in <c>[1,5]</c>,
    /// which the operand <c>∃age.integer</c> covers, so <c>A</c> meets a class it is disjoint from and
    /// the asserted individual condemns the module — decided by the fast-path, matching the tableau.
    /// </summary>
    [TestMethod]
    public void DisjointnessWithDataExistentialDecides()
    {
        ReasoningModule module = Module(
            Disjoint(Class("A"), DataSome("age", Integer)),
            SubClassOf(Class("A"), DataSome("age", IntegerBetween("1", "5"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: false);
    }

    /// <summary>
    /// The recognition-completion battery: a left-position data existential <c>∃d.R ⊑ Y</c> is
    /// recognized on a demand carrier exactly when the carrier's own demands force a <c>d</c>-value
    /// inside <c>R</c>. Each row names the containment direction it exercises, and every row also pins
    /// the EL verdict — consistency and the whole subsumption set — against the snapshot, so a
    /// recognition the tableau does not draw fails the row just as a missing one does.
    /// </summary>
    [TestMethod]
    public void DataRecognitionCompletionBattery()
    {
        (string Name, ReasoningModule Module, bool ExpectRecognition)[] cases =
        [
            ("a closed interval inside a wider closed one", Module(
                SubClassOf(DataSome("age", IntegerBetween("39", "59")), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("40", "55"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("a closed interval not inside a narrower closed one", Module(
                SubClassOf(DataSome("age", IntegerBetween("40", "55")), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("39", "59"))),
                ClassAssertion(Class("Y"), Individual("x"))), false),
            ("an exclusive lower bound inside an inclusive one", Module(
                SubClassOf(DataSome("age", IntegerAtLeast("1")), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerAbove("3"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("an exclusive upper bound inside the inclusive one", Module(
                SubClassOf(DataSome("age", IntegerAtMost("5")), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBelow("5"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("an inclusive upper bound not inside the exclusive one", Module(
                SubClassOf(DataSome("age", IntegerBelow("5")), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerAtMost("5"))),
                ClassAssertion(Class("Y"), Individual("x"))), false),
            ("a demanded value inside the recognized range", Module(
                SubClassOf(DataSome("age", IntegerBetween("40", "55")), Class("Y")),
                SubClassOf(Class("B"), DataValue("age", Lit("42", Vocabulary.Xsd.Integer))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("a demanded value outside the recognized range", Module(
                SubClassOf(DataSome("age", IntegerBetween("40", "55")), Class("Y")),
                SubClassOf(Class("B"), DataValue("age", Lit("7", Vocabulary.Xsd.Integer))),
                ClassAssertion(Class("Y"), Individual("x"))), false),
            ("a recognized value restriction met by a point demand", Module(
                SubClassOf(DataValue("age", Lit("42", Vocabulary.Xsd.Integer)), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("42", "42"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("a recognized value restriction unmet by a wider demand", Module(
                SubClassOf(DataValue("age", Lit("42", Vocabulary.Xsd.Integer)), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("40", "55"))),
                ClassAssertion(Class("Y"), Individual("x"))), false),
            ("a sub-property demand under the recognized super-property", Module(
                SubDataProperty("age", "measure"),
                SubClassOf(DataSome("measure", Integer), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("1", "5"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("a super-property demand does not meet the recognized sub-property", Module(
                SubDataProperty("age", "measure"),
                SubClassOf(DataSome("age", Integer), Class("Y")),
                SubClassOf(Class("B"), DataSome("measure", IntegerBetween("1", "5"))),
                ClassAssertion(Class("Y"), Individual("x"))), false),
            ("an equivalent data property closes the recognition", Module(
                EquivalentDataProperties("age", "years"),
                SubClassOf(DataSome("years", Integer), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("1", "5"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("an integer demand inside the rational value space", Module(
                SubClassOf(DataSome("age", RationalRange), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("1", "5"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("an integer demand inside the decimal value space", Module(
                SubClassOf(DataSome("age", DecimalRange), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("1", "5"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("a fractional decimal demand not inside the integer value space", Module(
                SubClassOf(DataSome("age", Integer), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", DecimalBetween("1.5", "2.5"))),
                ClassAssertion(Class("Y"), Individual("x"))), false),
            ("a carrier already emptied by its own demand still gains the recognition", Module(
                SubClassOf(DataSome("age", Integer), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", IntegerBetween("5", "2"))),
                ClassAssertion(Class("Y"), Individual("x"))), true),
            ("an empty recognized range recognizes nothing", Module(
                SubClassOf(DataSome("age", IntegerBetween("5", "2")), Class("Y")),
                SubClassOf(Class("B"), DataSome("age", Integer)),
                ClassAssertion(Class("Y"), Individual("x"))), false)
        ];

        foreach((string name, ReasoningModule module, bool expectRecognition) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, name + ": the module stays inside the EL fragment and is decided by the fast-path.");

            ModuleVerdict reference = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
            Assert.AreEqual(reference.IsConsistent, decision.Verdict!.IsConsistent, name + ": the EL consistency verdict agrees with the snapshot.");
            Assert.AreSequenceEqual(SubsumptionKeys(reference), SubsumptionKeys(decision.Verdict), name + ": the EL subsumption set agrees with the snapshot.");
            Assert.AreEqual(expectRecognition, Subsumes(decision.Verdict, "B", "Y"), name + ": the recognition fires exactly when the demand entails the recognized range.");
        }
    }

    /// <summary>
    /// A data existential nested under an object existential on the subclass side is named on the same
    /// left spine: <c>∃r.(∃age.integer) ⊑ Y</c> becomes a left existential over <c>r</c> whose filler is
    /// the recognized data concept, so a class whose <c>r</c>-successor demands an age in <c>[1,5]</c>
    /// gains <c>Y</c> — decided by the fast-path and agreeing with the snapshot.
    /// </summary>
    [TestMethod]
    public void NestedDataExistentialUnderAnObjectExistentialDecides()
    {
        ReasoningModule module = Module(
            SubClassOf(Some("r", DataSome("age", Integer)), Class("Y")),
            SubClassOf(Class("C"), DataSome("age", IntegerBetween("1", "5"))),
            SubClassOf(Class("B"), Some("r", Class("C"))),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleVerdict verdict = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!;
        Assert.IsTrue(Subsumes(verdict, "B", "Y"), "B's r-successor is a C, whose demanded age lies inside the nested recognized range, so the nested left existential fires on B.");
    }

    /// <summary>
    /// Two classes defined by nested value intervals stand in the subsumption the intervals state:
    /// everything with an age of at least 21 has an age of at least 18, so the narrower definition is
    /// subsumed by the wider one and not the other way round. Both directions are read off the EL
    /// projection and the whole verdict agrees with the snapshot.
    /// </summary>
    [TestMethod]
    public void NestedIntervalDefinitionsDeriveTheRecognitionSubsumption()
    {
        ReasoningModule module = Module(
            Equivalent(Class("A"), DataSome("age", IntegerAtLeast("18"))),
            Equivalent(Class("B"), DataSome("age", IntegerAtLeast("21"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleVerdict verdict = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!;
        Assert.IsTrue(Subsumes(verdict, "B", "A"), "The narrower interval definition is subsumed by the wider one.");
        Assert.IsFalse(Subsumes(verdict, "A", "B"), "The wider interval definition is not subsumed by the narrower one.");
    }

    /// <summary>
    /// A functional data property whose value demands are carried by two distinct classes is outside the
    /// EL classifier's per-carrier decision — a common subsumee inherits both demands and functionality
    /// forces them onto one value — so the module is delegated whole and the tableau, which reads the
    /// demands jointly, decides it inconsistent. A left-position data existential in the same module
    /// does not buy the decision back.
    /// </summary>
    [TestMethod]
    public void FunctionalDataPropertyPoolingAcrossClassesDelegates()
    {
        ReasoningModule module = Module(
            FunctionalData("age"),
            SubClassOf(DataSome("age", Integer), Class("Z")),
            SubClassOf(Class("Y1"), DataSome("age", IntegerBetween("0", "4"))),
            SubClassOf(Class("Y2"), DataSome("age", IntegerBetween("6", "9"))),
            SubClassOf(Class("X"), Class("Y1")),
            SubClassOf(Class("X"), Class("Y2")),
            ClassAssertion(Class("X"), Individual("a")));

        AssertDelegatesLike(module, expectConsistent: false);
    }

    /// <summary>
    /// A containment the value-space checker cannot decide is never guessed: the recognition test comes
    /// back undecided, the classifier names the undecided marker, and the coupled reasoner delegates the
    /// module rather than answer from a recognition it could not test.
    /// </summary>
    [TestMethod]
    public void UndecidableRecognitionContainmentDelegates()
    {
        ReasoningModule module = Module(
            SubClassOf(DataSome("name", StringPattern("a.*")), Class("Y")),
            SubClassOf(Class("B"), DataSome("name", PlainStringPattern("a.*"))),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>
    /// A recognition seeded on a class flows onto the witnesses minted for that class, in both witness
    /// regimes: the recognized subsumer empties the witness's core, the emptiness travels back over the
    /// minting edge, and the owner's asserted individual condemns the module. The inverse-blind tableau
    /// never forces the predecessor, so it answers consistent — the capability the fast-path adds, here
    /// carried by a recognition rather than a told subsumption.
    /// </summary>
    [TestMethod]
    public void DataRecognitionSeedsFlowOntoWitnesses()
    {
        (string Name, ReasoningModule Module)[] cases =
        [
            ("the shared-witness regime", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(Class("C"), DataSome("age", IntegerBetween("1", "5"))),
                SubClassOf(DataSome("age", Integer), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a")))),
            ("the per-owner witness regime", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(Class("C"), DataSome("age", IntegerBetween("1", "5"))),
                SubClassOf(DataSome("age", Integer), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))))
        ];

        foreach((string name, ReasoningModule module) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, name + ": the module is decided by the EL fast-path.");
            Assert.IsFalse(decision.Verdict!.IsConsistent, name + ": C's demand is recognized as Y, Y is empty, so a's minted r-predecessor cored C is empty and a is condemned.");
            Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, name + ": the inverse-blind tableau drops the inverse existential, never forces a's predecessor, and answers consistent.");
        }
    }

    /// <summary>A data existential over several data properties is a value tuple with no single-property reading, so a left-position occurrence of one stays delegated — the negative control on the arm's single-property guard.</summary>
    [TestMethod]
    public void NAryDataExistentialLeftPositionStaysDelegated()
    {
        ReasoningModule module = Module(
            SubClassOf(DataSomeAcross("age", "height", Integer), Class("Y")),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A reserved data property has a fixed extension the EL calculus does not interpret, so a left-position data existential over one stays delegated — the negative control on the arm's reserved-property guard.</summary>
    [TestMethod]
    public void ReservedDataPropertyLeftOccurrenceStaysDelegated()
    {
        ReasoningModule module = Module(
            SubClassOf(ReservedDataSome(Integer), Class("Y")),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>
    /// Value demands that meet only through a subsumption between two distinct carriers are fenced when a
    /// functional data property would pool them: the fast-path delegates and the tableau's joint reading
    /// gives the true verdict, both where an asserted individual makes the clash a module inconsistency
    /// and where the class is merely uninhabited. Two controls bound the fence: without functionality the
    /// two carriers are decided, and a single carrier's own pooled demands are decided too.
    /// </summary>
    [TestMethod]
    public void FunctionalPoolingAcrossToldSubsumersDelegates()
    {
        ReasoningModule condemned = Module(
            FunctionalData("age"),
            SubClassOf(Class("Y1"), DataSome("age", IntegerBetween("0", "4"))),
            SubClassOf(Class("Y2"), DataSome("age", IntegerBetween("6", "9"))),
            SubClassOf(Class("X"), Class("Y1")),
            SubClassOf(Class("X"), Class("Y2")),
            ClassAssertion(Class("X"), Individual("a")));
        AssertDelegatesLike(condemned, expectConsistent: false);

        ReasoningModule uninhabited = Module(
            FunctionalData("age"),
            SubClassOf(Class("Y1"), DataSome("age", IntegerBetween("0", "4"))),
            SubClassOf(Class("Y2"), DataSome("age", IntegerBetween("6", "9"))),
            SubClassOf(Class("X"), Class("Y1")),
            SubClassOf(Class("X"), Class("Y2")));
        AssertDelegatesLike(uninhabited, expectConsistent: true);

        ReasoningModule withoutFunctionality = Module(
            SubClassOf(Class("Y1"), DataSome("age", IntegerBetween("0", "4"))),
            SubClassOf(Class("Y2"), DataSome("age", IntegerBetween("6", "9"))),
            SubClassOf(Class("X"), Class("Y1")),
            SubClassOf(Class("X"), Class("Y2")),
            ClassAssertion(Class("X"), Individual("a")));
        AssertElDecidesLike(withoutFunctionality, expectConsistent: true);

        ReasoningModule singleCarrier = Module(
            FunctionalData("age"),
            SubClassOf(Class("Y1"), DataSome("age", IntegerBetween("0", "4"))),
            SubClassOf(Class("Y1"), DataSome("age", IntegerBetween("6", "9"))),
            ClassAssertion(Class("Y1"), Individual("a")));
        AssertElDecidesLike(singleCarrier, expectConsistent: false);
    }

    /// <summary>A data universal (<c>DataAllValuesFrom</c>) is outside the EL fragment and delegated to the tableau.</summary>
    [TestMethod]
    public void DataUniversalIsDelegated()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataAll("age", Integer)),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A data range the value-space checker cannot decide (a regex pattern) records the undecided marker, so the coupled reasoner delegates the module rather than trust a fragment-relative verdict — matching the tableau, which also abstains.</summary>
    [TestMethod]
    public void UndecidableDataRangeIsDelegated()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataSome("name", StringPattern("a.*"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>
    /// An undecided concrete-domain obligation scopes a consistent verdict to the modelled fragment, and the
    /// decision surface says so: the fallback names the undecided marker on the remainder, so the verdict is
    /// not decisive and the outcome is <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/> — the
    /// datatype counterpart of the axiom-typed remainder, carried through the same abstention contract.
    /// </summary>
    [TestMethod]
    public void UndecidableDataRangeSurfacesAsFragmentRelative()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), DataSome("name", StringPattern("a.*"))),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ElTotals.ElDecided, "The undecided data range delegates to the fallback decider.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, "A consistent verdict carrying the undecided-datatype marker is scoped, not whole-module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The modelled fragment is clash-free; only the datatype obligation stays open.");
        Assert.IsFalse(decision.Verdict.IsDecisive, "The named remainder scopes the consistency claim to the modelled fragment.");
        Assert.Contains(DataRestrictionConsistency.UndecidedMarker, decision.Verdict.UnsupportedConstructs, "The undecided obligation is named on the remainder, never folded silently into the verdict.");
    }

    /// <summary>A self-restriction forces a self-edge whose elimination clashes with the carrier's own type; the EL fast-path decides the inconsistency the self-blind tableau drops.</summary>
    [TestMethod]
    public void SelfForcedInconsistencyIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasSelf("r")),
            SubClassOf(HasSelf("r"), Class("B")),
            Disjoint(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The self-restriction module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x:A forces a self r-edge, which makes x a B; A and B are disjoint, so x is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The self-blind tableau drops ObjectHasSelf and misses the inconsistency.");
    }

    /// <summary>A reflexive role gives every node a self-edge; an elimination of that self-edge into the bottom concept makes the module inconsistent — decided by the fast-path where the tableau drops the characteristic.</summary>
    [TestMethod]
    public void ReflexiveRoleForcesSelfEdgeInconsistency()
    {
        ReasoningModule module = Module(
            Reflexive("r"),
            SubClassOf(HasSelf("r"), NothingReference),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The reflexive-role module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Every node has a self r-edge, which the elimination sends to the bottom concept, so no node is satisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The tableau drops the reflexive characteristic and misses the inconsistency.");
    }

    /// <summary>A reflexive role's range types every node through its self-edge; a range disjoint from a node's asserted type clashes — a verdict the self-blind tableau cannot reach.</summary>
    [TestMethod]
    public void ReflexiveRoleRangeTypesNodeAndCanClash()
    {
        ReasoningModule module = Module(
            Reflexive("r"),
            Range("r", Class("C")),
            Disjoint(Class("A"), Class("C")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The reflexive-range module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The self r-edge makes x a C via r's range; A and C are disjoint, so x is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The tableau drops the reflexive characteristic, so the range never types x and no clash is found.");
    }

    /// <summary>A consistent module carrying a self-restriction is decided consistent by the fast-path (known-answer, not the differential: EL keeps the construct the tableau drops, so their subsumption sets need not coincide).</summary>
    [TestMethod]
    public void SelfRestrictionConsistentModuleIsDecided()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasSelf("r")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The self-restriction module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A self r-edge on x is satisfiable; nothing forces a clash.");
    }

    /// <summary>A self-edge is its own reverse, so <c>∃r⁻.Self</c> holds of exactly the elements <c>∃r.Self</c> holds of and the inverse spelling registers its demand on the forward role — where the self-elimination, the mint guard and the constrained-role gate already read it. The module is decided consistent by the fast-path (known-answer, not the differential: the self-blind tableau drops the construct).</summary>
    [TestMethod]
    public void InverseSelfRestrictionIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasSelfInverse("r")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse self-restriction module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A self r-edge on x satisfies the demand in either spelling; nothing forces a clash.");
    }

    /// <summary>
    /// A TBox-only Self producer/consumer pair — <c>Sw1Owner ⊑ ∃sw1rel.Self</c> and
    /// <c>∃sw1rel.Self ⊑ Sw1Reflexive</c> — is EL-decided consistent, and the widened
    /// Self sweep surfaces the subsumption <c>Sw1Owner ⊑ Sw1Reflexive</c>. The consumer
    /// class <c>Sw1Reflexive</c> occurs only on a HasSelf occurrence the ALC translation
    /// never reaches, so the un-widened ALC signature omits it and the pair goes unswept;
    /// the HasSelf-gated widening restores it.
    /// </summary>
    [TestMethod]
    public void SelfConsumerPairIsSurfacedByTheWidenedSweep()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Sw1Owner"), HasSelf("sw1rel")),
            SubClassOf(HasSelf("sw1rel"), Class("Sw1Reflexive")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The TBox-only Self pair is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "An sw1rel self-loop witnesses the owner; nothing forces a clash.");
        Assert.IsTrue(Subsumes(decision.Verdict, "Sw1Owner", "Sw1Reflexive"), "The widened Self sweep surfaces the consumer/producer subsumption the ALC signature omits.");
    }

    /// <summary>
    /// A Self-free control — <c>Sw2Root ⊑ ∃sw2rel.Sw2Mid</c>, <c>Sw2Mid ⊑ Sw2Tail</c> —
    /// is EL-decided consistent with a sweep byte-identical to the un-widened ALC
    /// signature: with no HasSelf the gate holds the un-widened path, so the certified
    /// Self-free subsumption face matches the snapshot exactly. Consistency-only: the flat
    /// <c>Sw2Root ⊑ Sw2Tail</c> does not hold — only <c>Sw2Root ⊑ ∃sw2rel.Sw2Tail</c> —
    /// so no flat subsumption is asserted.
    /// </summary>
    [TestMethod]
    public void SelfFreeControlKeepsTheUnwidenedSweep()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Sw2Root"), Some("sw2rel", Class("Sw2Mid"))),
            SubClassOf(Class("Sw2Mid"), Class("Sw2Tail")));

        Assert.IsFalse(ModuleSweepSignature.CarriesHasSelf(module), "The control carries no HasSelf, so the EL sweep keeps the un-widened ALC signature.");
        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// The ABox-dressed twin of the Self pair — <c>Sw3Owner ⊑ ∃sw3rel.Self</c>,
    /// <c>∃sw3rel.Self ⊑ Sw3Reflexive</c>, <c>sw3i : Sw3Owner</c> — is EL-decided
    /// consistent and surfaces <c>Sw3Owner ⊑ Sw3Reflexive</c> through the widened Self
    /// sweep, the consumer class still occurring only on the HasSelf occurrence.
    /// </summary>
    [TestMethod]
    public void AboxDressedSelfPairSurfacesTheSubsumption()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Sw3Owner"), HasSelf("sw3rel")),
            SubClassOf(HasSelf("sw3rel"), Class("Sw3Reflexive")),
            ClassAssertion(Class("Sw3Owner"), Individual("sw3i")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The ABox-dressed Self module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The sw3rel self-loop on sw3i is satisfiable; nothing forces a clash.");
        Assert.IsTrue(Subsumes(decision.Verdict, "Sw3Owner", "Sw3Reflexive"), "The Self consumer pair surfaces with the ABox individual present.");
    }

    /// <summary>
    /// A Self-free EL-admitted module with an ALC-blind inverse-existential filler —
    /// <c>Sw4Root ⊑ ∃sw4rel.Sw4Mid</c>, <c>Sw4Mid ⊑ ∃sw4rel⁻.Sw4Back</c> — keeps the
    /// un-widened ALC sweep: with no HasSelf the gate holds the ALC signature, which
    /// drops the inverse-existential filler <c>Sw4Back</c> the full walk would capture.
    /// The HasSelf gate must not sweep <c>Sw4Back</c> for this Self-free module even
    /// though the full widened walk reaches it.
    /// </summary>
    [TestMethod]
    public void SelfFreeAlcBlindFillerStaysOutOfTheSweep()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("Sw4Root"), Some("sw4rel", Class("Sw4Mid"))),
            SubClassOf(Class("Sw4Mid"), SomeInverse("sw4rel", Class("Sw4Back"))));

        Assert.IsFalse(ModuleSweepSignature.CarriesHasSelf(module), "The module is Self-free, so the EL sweep keeps the un-widened ALC signature.");
        AssertElDecidesLike(module, expectConsistent: true);

        Utf8String back = Utf8Strings.From(Example + "Sw4Back");
        Assert.DoesNotContain(back, AlcModuleReasoner.Translate(module).SignatureClasses, "The un-widened ALC signature the Self-free gate keeps drops the inverse-existential filler Sw4Back.");
        Assert.Contains(back, ModuleSweepSignature.Build(module), "The full widened walk would capture Sw4Back — the sweep the HasSelf gate suppresses for a Self-free module.");
    }

    /// <summary>
    /// A Self-free module at the sweep-cap boundary: a three-class named chain
    /// (<c>Sw6A ⊑ Sw6B ⊑ Sw6C</c>) beside fourteen inverse-existential axioms whose
    /// distinct fillers push the FULL axiom-walk signature to seventeen classes —
    /// past the sweep cap — while the un-widened ALC signature stays at three. The
    /// HasSelf gate keeps the three-class sweep, so the transitive chain pair is
    /// emitted; a widened signature would exceed the cap and empty the subsumption
    /// set, so the emitted pair pins the gate through the public verdict face.
    /// </summary>
    [TestMethod]
    public void SelfFreeCapBoundaryKeepsTheSweepPopulated()
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Class("Sw6A"), Class("Sw6B")),
            SubClassOf(Class("Sw6B"), Class("Sw6C")),
        ];
        for(int filler = 1; filler <= 14; filler++)
        {
            axioms.Add(SubClassOf(Class("Sw6A"), SomeInverse("sw6q", Class("Sw6H" + filler))));
        }

        ReasoningModule module = new([.. axioms], Violations: []);
        Assert.IsFalse(ModuleSweepSignature.CarriesHasSelf(module), "The cap-boundary module carries no HasSelf, so the gate keeps the un-widened ALC signature.");
        Assert.IsGreaterThan(AlcModuleReasoner.SubsumptionSignatureCap, ModuleSweepSignature.Build(module).Count, "The full axiom walk exceeds the sweep cap; only the gate keeps the sweep populated.");

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The Self-free module with inverse-existential superclasses is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing forces a clash; every filler is freely satisfiable.");
        Assert.IsTrue(Subsumes(decision.Verdict, "Sw6A", "Sw6C"), "The un-widened three-class sweep emits the transitive chain pair; a signature past the cap would empty the set.");
        Assert.IsFalse(Subsumes(decision.Verdict, "Sw6C", "Sw6A"), "The chain runs one direction only.");
    }

    /// <summary>
    /// The differential-projection identity re-verified against the widened
    /// Self sweep: for each Self-family module, the EL arm's and the snapshot tableau's
    /// subsumption key sets agree once both are projected onto the shared ALC signature.
    /// The EL arm now sweeps the widened signature on Self-bearing modules, so its Self
    /// pairs mention consumer classes outside the ALC signature; the projection drops
    /// them, leaving the two arms identical on the shared fragment.
    /// </summary>
    [TestMethod]
    public void WidenedSelfSweepPreservesTheProjectionIdentity()
    {
        ReasoningModule sw1 = Module(
            SubClassOf(Class("Sw1Owner"), HasSelf("sw1rel")),
            SubClassOf(HasSelf("sw1rel"), Class("Sw1Reflexive")));
        ReasoningModule sw2 = Module(
            SubClassOf(Class("Sw2Root"), Some("sw2rel", Class("Sw2Mid"))),
            SubClassOf(Class("Sw2Mid"), Class("Sw2Tail")));
        ReasoningModule sw3 = Module(
            SubClassOf(Class("Sw3Owner"), HasSelf("sw3rel")),
            SubClassOf(HasSelf("sw3rel"), Class("Sw3Reflexive")),
            ClassAssertion(Class("Sw3Owner"), Individual("sw3i")));

        AssertProjectionIdentity(sw1);
        AssertProjectionIdentity(sw2);
        AssertProjectionIdentity(sw3);
    }

    /// <summary>Asserts the EL arm and the snapshot tableau produce the same subsumption key set once both are projected onto the module's shared ALC signature — the differential-projection identity.</summary>
    /// <param name="module">The module to check.</param>
    private void AssertProjectionIdentity(ReasoningModule module)
    {
        HashSet<Utf8String> shared = [.. AlcModuleReasoner.Translate(module).SignatureClasses];
        ModuleVerdict elVerdict = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken).Verdict!;
        ModuleVerdict snapshot = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
        List<string> elProjected = ProjectOntoSignature(elVerdict, shared);
        List<string> snapshotProjected = ProjectOntoSignature(snapshot, shared);
        Assert.AreSequenceEqual(snapshotProjected, elProjected, "The EL and snapshot subsumption sets agree on the shared ALC signature after projection.");
    }

    /// <summary>The verdict's subsumption keys whose subclass and superclass both lie in the shared signature, ordinally sorted — the projection onto the shared fragment.</summary>
    /// <param name="verdict">The verdict whose subsumptions to project.</param>
    /// <param name="shared">The shared signature IRIs.</param>
    /// <returns>The projected keys, ordinally sorted.</returns>
    private static List<string> ProjectOntoSignature(ModuleVerdict verdict, HashSet<Utf8String> shared)
    {
        List<string> keys = [];
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            if(shared.Contains(subClass.Iri) && shared.Contains(superClass.Iri))
            {
                keys.Add($"{subClass.Iri}→{superClass.Iri}");
            }
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>An irreflexive characteristic over a role bearing no self-edge is decided consistent by the EL fast-path — the ground-graph characteristic tier admits it, and with no asserted self-edge nothing clashes. Pins that the characteristic is now interpreted, not delegated as an unadmitted sibling of <c>Reflexive</c>.</summary>
    [TestMethod]
    public void IrreflexiveCharacteristicWithNoSelfEdgeIsDecidedConsistent()
    {
        ReasoningModule module = Module(
            Irreflexive("r"),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>An asymmetric role bearing an asserted edge and its reverse is decided inconsistent by the EL fast-path — <c>r(a, b)</c> and <c>r(b, a)</c> cannot both hold when <c>r</c> is asymmetric. The characteristic-blind tableau drops <c>Asymmetric(r)</c> and answers consistent; this tier closes exactly that blindness.</summary>
    [TestMethod]
    public void AsymmetricPairClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Asymmetric("r"),
            Edge("a", "r", "b"),
            Edge("b", "r", "a"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The asymmetric-pair module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "r(a, b) and r(b, a) over an asymmetric r is a first-order contradiction, so the module is inconsistent.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The characteristic-blind tableau drops Asymmetric(r) and misses the reverse-pair clash.");
    }

    /// <summary>An irreflexive role bearing an asserted self-edge is decided inconsistent by the EL fast-path — <c>r(a, a)</c> cannot hold when <c>r</c> is irreflexive. The characteristic-blind tableau drops <c>Irreflexive(r)</c> and answers consistent.</summary>
    [TestMethod]
    public void IrreflexiveSelfEdgeClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Irreflexive("r"),
            Edge("a", "r", "a"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The irreflexive-self-edge module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "r(a, a) over an irreflexive r is a first-order contradiction, so the module is inconsistent.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The characteristic-blind tableau drops Irreflexive(r) and misses the self-edge clash.");
    }

    /// <summary>A delegated asymmetric module surfaces its abstention honestly: the constrained role bears a positive-position existential (so the tier gate delegates), and the fallback verdict names the excluded <c>OwlObjectPropertyCharacteristicAxiom</c> in its remainder, is not decisive, and records the fragment-relative outcome — never a blind consistent.</summary>
    [TestMethod]
    public void DelegatedAsymmetricModuleSurfacesTheRemainder()
    {
        ReasoningModule module = Module(
            Asymmetric("r"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            Edge("a", "r", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ElTotals.ElDecided, "The asymmetric role bears an existential, so the tier gate delegates the module to the fallback decider.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The single asserted edge a -> b has no reverse, so the module is consistent.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, "The delegated asymmetric verdict surfaces as a named fragment-relative outcome.");
        Assert.IsFalse(decision.Verdict.IsDecisive, "A consistent verdict naming an excluded characteristic is scoped to the supported fragment.");
        Assert.Contains(nameof(OwlObjectPropertyCharacteristicAxiom), decision.Verdict.UnsupportedConstructs, "The excluded asymmetric characteristic is named by its axiom type.");
    }

    /// <summary>An asserted <c>ObjectHasValue(r, a)</c> on the named individual <c>x</c> seeds an r-edge to the shared individual node a; the role's range types a and clashes with a's asserted type, so a is unsatisfiable — an inconsistency the nominal-blind tableau, which drops the assertion, cannot reach. The edge is forced from a genuine individual, so the verdict is sound.</summary>
    [TestMethod]
    public void AssertedObjectHasValueRangeClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(HasValue("r", "a"), Individual("x")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The asserted-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x asserts an r-edge to a; r's range makes a a K; a is also L and K, L are disjoint, so a is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the asserted ObjectHasValue, so the range never types a and the clash is missed.");
    }

    /// <summary>An asserted singleton <c>ObjectSomeValuesFrom(r, ObjectOneOf(a))</c> seeds the same ∃r.{a} edge as ObjectHasValue, so the same range clash on a is decided. Exercises the singleton-enumeration class-assertion arm.</summary>
    [TestMethod]
    public void AssertedSingletonObjectOneOfRangeClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(Some("r", OneOf("a")), Individual("x")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The singleton-enumeration nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "∃r.{a} routes the asserted edge to a exactly as ObjectHasValue does, so the range clash on a holds.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The tableau drops the enumeration filler and misses the clash.");
    }

    /// <summary>An asserted <c>ObjectHasValue(r, a)</c> edge feeds a left-existential elimination — <c>∃r.B ⊑ ⊥</c> with the individual a typed B — driving the asserting individual x to the bottom concept; the tableau drops the edge and misses it.</summary>
    [TestMethod]
    public void AssertedObjectHasValueLeftExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(HasValue("r", "a"), Individual("x")),
            ClassAssertion(Class("B"), Individual("a")),
            SubClassOf(Some("r", Class("B")), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The asserted-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x asserts an r-edge to a; a is a B, so x has an r-successor in B, which the elimination sends to the bottom concept.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The tableau drops the asserted nominal edge, so x has no known r-successor and the elimination never fires.");
    }

    /// <summary>Two co-referent individuals collapse to one node through the <c>SameIndividual</c> merge, so a range typing reached via an asserted <c>{a}</c> edge and a disjoint type asserted on <c>{b}</c> clash on the shared node. Pins the <c>FindKey</c> co-reference resolution; without it the two halves of the clash land on separate nodes.</summary>
    [TestMethod]
    public void SameIndividualCollapsesAssertedNominal()
    {
        ReasoningModule module = Module(
            SameIndividual(Individual("a"), Individual("b")),
            ClassAssertion(HasValue("r", "a"), Individual("x")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("b")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The co-referent-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "a and b are the same node; the range makes it K via the asserted {a} edge while {b} asserts L, and K, L are disjoint.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The tableau drops the asserted nominal edge, so the range never types the node and the clash is missed.");
    }

    /// <summary>A consistent module carrying an asserted nominal is decided consistent by the fast-path (known-answer: EL keeps a construct the tableau drops, so their subsumption sets need not coincide).</summary>
    [TestMethod]
    public void AssertedNominalConsistentModuleIsDecided()
    {
        ReasoningModule module = Module(
            ClassAssertion(HasValue("r", "a"), Individual("x")),
            ClassAssertion(Class("Person"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The asserted-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "An asserted r-edge to a, with a a Person, is satisfiable; nothing forces a clash.");
    }

    /// <summary>An asserted nominal never leaks into the named-class subsumption output: the individual atom lives in a space disjoint from the named classes, so an unrelated <c>P ⊑ Q</c> is decided and no subsumption key mentions an individual.</summary>
    [TestMethod]
    public void NominalDoesNotContaminateNamedClassProjection()
    {
        ReasoningModule module = Module(
            ClassAssertion(HasValue("r", "a"), Individual("x")),
            SubClassOf(Class("P"), Class("Q")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The asserted-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing forces a clash, so the module is consistent.");

        List<string> keys = SubsumptionKeys(decision.Verdict);
        Assert.Contains($"{Example}P→{Example}Q", keys, "The unrelated P ⊑ Q subsumption is decided over the named-class signature.");
        foreach(string key in keys)
        {
            Assert.IsFalse(key.Contains($"{Example}a", StringComparison.Ordinal), "No nominal individual leaks into the named-class subsumption output as a subject.");
            Assert.IsFalse(key.Contains($"{Example}x", StringComparison.Ordinal), "No asserting individual leaks into the named-class subsumption output.");
        }
    }

    /// <summary>A consistent superclass nominal (<c>A ⊑ ∃r.{a}</c>) with an inhabited carrier is decided consistent by the fast-path: x's forced r-edge to a is satisfiable when nothing clashes.</summary>
    [TestMethod]
    public void SuperclassNominalConsistentModuleIsDecided()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasValue("r", "a")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("Person"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The superclass-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x is an A, so it has an r-edge to a, with a a Person; nothing forces a clash.");
    }

    /// <summary>
    /// The documented uninhabited-carrier case, now DECIDED CONSISTENT (the regression guard for the
    /// false-inconsistent bug). With no instance of the carrier A, <c>A ⊑ ∃r.{a}</c> forces no edge
    /// into the real individual a, so the role range never types a — which is only L — and the module
    /// is consistent. The fresh proxy for A's existential is not live, so the range it carries is never
    /// promoted onto a; the tableau, nominal-blind, agrees on the verdict.
    /// </summary>
    [TestMethod]
    public void SuperclassNominalUninhabitedCarrierIsConsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasValue("r", "a")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The superclass-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "No individual is an A, so the proxy carrying r's range K is never live; a is only L, so nothing clashes.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau also returns consistent.");
    }

    /// <summary>The inhabited variant: asserting an A makes the carrier live, so x's r-edge to a is forced, r's range types a as K, and a's disjoint L makes a unsatisfiable — an inconsistency the nominal-blind tableau misses.</summary>
    [TestMethod]
    public void SuperclassNominalInhabitedCarrierForcesInconsistency()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasValue("r", "a")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The superclass-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A, so it has an r-edge to a; r's range types a as K; a is also L and K, L are disjoint.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the nominal and misses the forced inconsistency.");
    }

    /// <summary>Two carriers over the same individual do not bleed: an inhabited A1 reaches a, while an uninhabited A2 whose proxy carries a clashing range does not — the range never promotes onto a, so the module stays consistent. Pins the per-carrier fresh proxy against cross-carrier contamination.</summary>
    [TestMethod]
    public void SuperclassNominalTwoCarriersNoCrossBleed()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A1"), Some("s", OneOf("a"))),
            ClassAssertion(Class("A1"), Individual("x")),
            SubClassOf(Class("A2"), HasValue("r", "a")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The two-carrier nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A2 is uninhabited, so its proxy never promotes the range K onto a even though the inhabited A1 reaches a; a is only L.");
    }

    /// <summary>
    /// Two inhabited carriers reaching the same individual through NAMED INTERMEDIATES, each writing a
    /// disjoint half of the clash onto a: x:A1 forces a B1 whose r-edge types a as K, y:A2 forces a B2
    /// whose s-edge types a as L, and K, L are disjoint, so a is unsatisfiable. Decided INCONSISTENT —
    /// the regression guard that liveness propagates forward across the existential edge a class
    /// intermediate fired before it was inhabited, so each proxy becomes live and promotes its range
    /// half onto a where the two halves clash.
    /// </summary>
    [TestMethod]
    public void SuperclassNominalTwoCarrierSplitClashIsInconsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A1"), Some("p", Class("B1"))),
            SubClassOf(Class("B1"), HasValue("r", "a")),
            Range("r", Class("K")),
            SubClassOf(Class("A2"), Some("q", Class("B2"))),
            SubClassOf(Class("B2"), HasValue("s", "a")),
            Range("s", Class("L")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("A1"), Individual("x")),
            ClassAssertion(Class("A2"), Individual("y")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The split-clash nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x forces a B1 whose r-edge types a as K; y forces a B2 whose s-edge types a as L; K and L are disjoint, so a is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops both nominal edges, so neither range reaches a.");
    }

    /// <summary>Completeness through the non-empty-domain root: <c>⊤ ⊑ B</c> and <c>B ⊑ ∃p.A</c> force an A to exist in every model, making A live without any asserted individual; A's superclass nominal then types a, whose disjoint type makes the module inconsistent. Exercises ⊤ as a liveness root propagated along the forced existential chain.</summary>
    [TestMethod]
    public void SuperclassNominalReachedFromNonEmptyDomainIsInconsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(ThingReference, Class("B")),
            SubClassOf(Class("B"), Some("p", Class("A"))),
            SubClassOf(Class("A"), HasValue("r", "a")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The non-empty-domain nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Every model has an A (forced from ⊤ through B and ∃p.A), whose r-edge types a as K; a is also L, so a is unsatisfiable — no individual need be asserted A.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the nominal and misses it.");
    }

    /// <summary>A bare superclass nominal <c>A ⊑ {a}</c> over an inhabited carrier merges the carrier's instance with a (both directions, both live), so a type on a and a disjoint type on the instance clash — decided where the nominal-blind tableau keeps them apart.</summary>
    [TestMethod]
    public void SuperclassBareNominalMergesInhabitedCarrierWithIndividual()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("C"), Individual("a")),
            ClassAssertion(Class("D"), Individual("x")),
            Disjoint(Class("C"), Class("D")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The bare-superclass-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A and A ⊑ {a} forces x = a, so x is both C (from a) and D; C and D are disjoint.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops A ⊑ {a}, so x and a stay separate.");
    }

    /// <summary>The subsumption-vs-consistency split: a bare superclass nominal can make the carrier A unsatisfiable (A ⊑ {a} ⊓ C ⊓ D with C, D disjoint) without condemning the module, because A is uninhabited — a never gains A's D, so a is only C and the module is consistent.</summary>
    [TestMethod]
    public void SuperclassBareNominalUnsatisfiableCarrierStaysConsistent()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            SubClassOf(Class("A"), Class("D")),
            ClassAssertion(Class("C"), Individual("a")),
            Disjoint(Class("C"), Class("D")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The bare-superclass-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A ⊑ {a} ⊓ C ⊓ D is unsatisfiable, but no individual is an A, so a (only C) is never typed D and the module is consistent.");
    }

    /// <summary>Two distinct nominals forced onto one live carrier are unified: with an inhabited A ⊑ {a} ⊓ {b}, a and b denote the same element, so a's type and b's disjoint type clash — the discovered-equality the merge derives from the live node carrying both nominals.</summary>
    [TestMethod]
    public void SuperclassTwoNominalsOnLiveCarrierUnifyAndCanClash()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            SubClassOf(Class("A"), OneOf("b")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("C"), Individual("a")),
            ClassAssertion(Class("D"), Individual("b")),
            Disjoint(Class("C"), Class("D")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The two-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A ⊑ {a} ⊓ {b}, so x = a = b; a is C and b is D, and C, D are disjoint.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops both nominals, so a and b stay distinct.");
    }

    /// <summary>A live carrier's superclass nominal identifies the carrier with an individual that bears asserted ground edges, and that identity is established at saturation — after the functional pre-merge and the distinctness scan resolved their keys. The ground-identity restart decides it: the first pass's live-node sweep finds x holding both its own nominal and a, folds x = a into the told identities, and the rebuild's inverse-functional union then forces the two r-sources equal, which the asserted distinctness contradicts. One rebuild, decided INCONSISTENT — the nominal-blind fallback has no <c>ObjectOneOf</c> reading, drops the identity-forcing inclusion whole, and answers consistent, so the fast path decides strictly more.</summary>
    [TestMethod]
    public void SuperclassNominalIdentityOverInverseFunctionalGroundEdgesIsDecidedByTheGroundIdentityRestart()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            ClassAssertion(Class("A"), Individual("x")),
            InverseFunctional("r"),
            Edge("z", "r", "x"),
            Edge("z2", "r", "a"),
            Different("z", "z2"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The restart replays the discovered identity into the pre-intern regime, so the EL fast-path decides the module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A ⊑ {a}, so x = a; the two r-edges then share a target, inverse-functionality forces z = z2, and DifferentIndividuals(z, z2) forbids it.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops A ⊑ {a}, never derives x = a, and misses the clash.");
    }

    /// <summary>The saturation-discovered identity contradicts an asserted distinctness with no role feature involved: x = a told through the live carrier's superclass nominal, against DifferentIndividuals(x, a), whose collision scan compares union-find representatives at normalize time. The ground-identity restart hands that scan the discovered identity as a told one, so the rebuild's collision fires and the module is decided INCONSISTENT — the nominal-blind fallback drops A ⊑ {a} whole and answers consistent.</summary>
    [TestMethod]
    public void SuperclassNominalIdentityContradictingAssertedDistinctnessIsDecidedByTheGroundIdentityRestart()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            ClassAssertion(Class("A"), Individual("x")),
            Different("x", "a"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The restart replays x = a as a told identity, so the distinctness scan reads an identity-complete union-find and the fast-path decides the module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x inhabits A ⊑ {a}, so x = a, which DifferentIndividuals(x, a) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops A ⊑ {a}, so x and a stay distinct.");
    }

    /// <summary>Two superclass nominals on one live carrier identify a and b with each other through the carrier — a discovered identity the distinctness scan, which compares union-find representatives before interning, cannot reach on its own. The restart's sweep reads the carrier's inhabited instance holding both nominals, folds the pair, and the rebuild's collision decides the module INCONSISTENT where the nominal-blind fallback drops both inclusions and answers consistent.</summary>
    [TestMethod]
    public void TwoSuperclassNominalsAgainstAssertedDistinctnessAreDecidedByTheGroundIdentityRestart()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            SubClassOf(Class("A"), OneOf("b")),
            ClassAssertion(Class("A"), Individual("x")),
            Different("a", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The restart folds the pair the carrier's instance holds, so the fast-path decides the module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A ⊑ {a} ⊓ {b}, so a = x = b, which DifferentIndividuals(a, b) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops both inclusions, so a and b stay distinct.");
    }

    /// <summary>Gate narrowness: a told nominal identity with NO pre-intern identity consumer in the module never enters the restart loop and decides on the single pass. The live carrier A ⊑ {a} pools its D onto a, whose asserted E is disjoint from D, so the fast-path decides the module inconsistent — the loop's entry condition is the pairing, never the nominal alone.</summary>
    [TestMethod]
    public void SuperclassNominalPooledClashWithoutAnIdentityConsumerIsDecided()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("a")),
            SubClassOf(Class("A"), Class("D")),
            ClassAssertion(Class("A"), Individual("x")),
            ClassAssertion(Class("E"), Individual("a")),
            Disjoint(Class("D"), Class("E")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "With no distinctness or ground characteristic in the module the completion gate is false and the fast-path decides it on one pass.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x inhabits A, so A's constraints pool onto a: a is D from the carrier and E from its assertion, and D, E are disjoint.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops A ⊑ {a} and keeps the carrier's D away from a.");
    }

    /// <summary>Two nominals sharing one asserted filler spine are told identities of each other, and the pre-intern ground-spine fold writes that identity before any interning — so the distinctness scan sees one representative for a and b and the module is decided INCONSISTENT with no rebuild at all. The mechanism is the fold, not the restart: nothing in the module tells a class it is an individual, so the completion gate never opens. The nominal-blind fallback drops the assertion whole and answers consistent.</summary>
    [TestMethod]
    public void NestedFillerNominalsAgainstAssertedDistinctnessAreDecidedByThePreInternFold()
    {
        ReasoningModule module = Module(
            ClassAssertion(Some("r", new OwlObjectIntersectionOf([OneOf("a"), OneOf("b")])), Individual("x")),
            Different("a", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The sibling nominals fold pre-intern, so the fast-path decides the module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x's r-successor is told to be both a and b, so a = b, which DifferentIndividuals(a, b) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the enumerated filler, so a and b stay distinct.");
    }

    /// <summary>A multi-individual <c>ObjectOneOf</c> filler is a disjunction outside the Horn EL fragment, so the module is delegated to the tableau unchanged.</summary>
    [TestMethod]
    public void MultiIndividualObjectOneOfIsDelegated()
    {
        ReasoningModule module = Module(
            ClassAssertion(Some("r", OneOf("a", "b")), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A bare singleton nominal assertion <c>x : {a}</c> is the told identity x = a, folded into the SameIndividual union-find; with nothing forcing a clash the fast-path decides it consistent.</summary>
    [TestMethod]
    public void BareNominalAssertedIsDecidedConsistent()
    {
        ReasoningModule module = Module(
            ClassAssertion(OneOf("a"), Individual("x")),
            ClassAssertion(Class("Person"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The bare-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x = a with a a Person is satisfiable; nothing forces a clash.");
    }

    /// <summary>A bare nominal <c>x : {a}</c> collapses x and a to one node, so disjoint types asserted across them clash — an inconsistency the nominal-blind tableau, which drops the bare nominal, misses.</summary>
    [TestMethod]
    public void BareNominalCollapsesTypesAndCanClash()
    {
        ReasoningModule module = Module(
            ClassAssertion(OneOf("a"), Individual("x")),
            Disjoint(Class("C"), Class("D")),
            ClassAssertion(Class("C"), Individual("a")),
            ClassAssertion(Class("D"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The bare-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x = a collapses C on a and D on x onto one node; C and D are disjoint, so the node is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops x : {a}, so x and a stay separate and the clash is missed.");
    }

    /// <summary>A bare nominal reactivates DifferentIndividuals as a clash source: x : {a} and y : {a} force x = a = y, contradicting DifferentIndividuals(x, y) — decided inconsistent by the fast-path where the nominal-blind tableau misses it.</summary>
    [TestMethod]
    public void BareNominalDifferentIndividualsCollisionIsInconsistent()
    {
        ReasoningModule module = Module(
            ClassAssertion(OneOf("a"), Individual("x")),
            ClassAssertion(OneOf("a"), Individual("y")),
            Different("x", "y"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The collision module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x = a and y = a force x = y, which DifferentIndividuals(x, y) forbids.");
    }

    /// <summary>SameIndividual(x, y) with DifferentIndividuals(x, y) is inconsistent — the representative collision is seen after the union-find closure, where the distinctness constraint follows the merged representative.</summary>
    [TestMethod]
    public void SameIndividualDifferentIndividualsCollisionIsInconsistent()
    {
        ReasoningModule module = Module(
            SameIndividual(Individual("x"), Individual("y")),
            Different("x", "y"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The collision module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x = y forced by SameIndividual contradicts DifferentIndividuals(x, y).");
    }

    /// <summary>The collision is computed over the full union-find closure: x : {a}, SameIndividual(a, b), y : {b} chains x and y onto one representative, so DifferentIndividuals(x, y) clashes.</summary>
    [TestMethod]
    public void DifferentIndividualsCollisionThroughMergeChainIsInconsistent()
    {
        ReasoningModule module = Module(
            ClassAssertion(OneOf("a"), Individual("x")),
            SameIndividual(Individual("a"), Individual("b")),
            ClassAssertion(OneOf("b"), Individual("y")),
            Different("x", "y"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The chained-collision module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x = a = b = y collapses to one node, which DifferentIndividuals(x, y) forbids.");
    }

    /// <summary>Distinct nominals do not collide: x : {a}, y : {b} with DifferentIndividuals(x, y) keeps a and b on separate representatives, so the module is consistent.</summary>
    [TestMethod]
    public void DistinctBareNominalsWithDifferentIndividualsAreConsistent()
    {
        ReasoningModule module = Module(
            ClassAssertion(OneOf("a"), Individual("x")),
            ClassAssertion(OneOf("b"), Individual("y")),
            Different("x", "y"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The distinct-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x = a and y = b with a, b distinct nodes is satisfiable.");
    }

    /// <summary>An asserted nominal nested two existentials deep is the one-axiom spelling of an admitted two-axiom module: the filler is named as a proxy the nominal is told of, and the proxy is live because its owner is a genuine individual. With <c>x : ∃s.(∃r.{a})</c> the inner r-edge's range types the proxy's successor, which IS a once the merge pools it, and a's disjoint type condemns the module — decided by the fast-path where the nominal-blind tableau drops the assertion.</summary>
    [TestMethod]
    public void NestedAssertedNominalUnderExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(Some("s", Some("r", OneOf("a"))), Individual("x")),
            Range("r", Class("C")),
            ClassAssertion(Class("D"), Individual("a")),
            Disjoint(Class("C"), Class("D")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The nominal below the top level of an asserted filler is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x's s-successor has an r-successor told to be a, so range(r) = C types a; a is also D, and C, D are disjoint.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the nested nominal, never types a with C, and misses the clash.");
    }

    /// <summary>A subclass-side ∃r.{a} is a left existential keyed on the individual node: an asserted r-edge into a fires the conclusion onto its source. With ∃r.{a} ⊑ ⊥ the source individual is condemned — decided by the fast-path where the nominal-blind tableau drops the restriction.</summary>
    [TestMethod]
    public void SubclassNominalLeftExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(HasValue("r", "a"), NothingReference),
            Edge("x", "r", "a"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The subclass-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x has an asserted r-edge to a, so x is in ∃r.{a} ⊑ ⊥ and is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops ∃r.{a}, so x is never condemned.");
    }

    /// <summary>A bare nominal on the subclass side, <c>{a} ⊑ C</c>, is told typing of a: with C disjoint from a's asserted type the individual a clashes — decided by the fast-path.</summary>
    [TestMethod]
    public void SubclassBareNominalTypesIndividualAndCanClash()
    {
        ReasoningModule module = Module(
            SubClassOf(OneOf("a"), Class("C")),
            Disjoint(Class("C"), Class("D")),
            ClassAssertion(Class("D"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The bare-subclass-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "{a} ⊑ C types a as C; a is also D and C, D are disjoint, so a is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops {a} ⊑ C, so a is only D and no clash arises.");
    }

    /// <summary>An equivalence carrying a nominal is now decided in both directions: <c>A ≡ ∃r.{a}</c> makes an A's r-edge to a forced (superclass) and an r-edge to a sufficient for A (subclass). With an inhabited A, r's range types a, and a's disjoint type makes the module inconsistent — a verdict the nominal-blind tableau misses.</summary>
    [TestMethod]
    public void EquivalenceWithNominalRangeClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Equivalent(Class("A"), HasValue("r", "a")),
            ClassAssertion(Class("A"), Individual("x")),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The equivalence-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A ≡ ∃r.{a}, so it has an r-edge to a; r's range types a as K; a is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the nominal in the equivalence.");
    }

    /// <summary>A disjointness with a singleton-nominal operand, Disjoint(A, ∃r.{a}), reduces to A ⊓ ∃r.{a} ⊑ ⊥: an A with an asserted r-edge to a clashes — decided by the fast-path where the nominal-blind tableau drops the operand.</summary>
    [TestMethod]
    public void DisjointWithSingletonNominalClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Disjoint(Class("A"), HasValue("r", "a")),
            ClassAssertion(Class("A"), Individual("x")),
            Edge("x", "r", "a"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The disjoint-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x is an A with an r-edge to a, so x is A ⊓ ∃r.{a} ⊑ ⊥ and unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops ∃r.{a}, so the disjointness never fires.");
    }

    /// <summary>The same disjointness without the triggering edge is consistent: x is an A but has no r-edge to a, so A ⊓ ∃r.{a} never fires — decided consistent by the fast-path.</summary>
    [TestMethod]
    public void DisjointWithSingletonNominalNoEdgeIsConsistent()
    {
        ReasoningModule module = Module(
            Disjoint(Class("A"), HasValue("r", "a")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The disjoint-nominal module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x is an A but has no r-edge to a, so the disjointness does not fire.");
    }

    /// <summary>A multi-individual ObjectOneOf on the subclass side, ∃r.{a,b} ⊑ Y, is a disjunctive filler the singleton increment does not decompose, so it is delegated.</summary>
    [TestMethod]
    public void MultiIndividualSubclassNominalIsDelegated()
    {
        ReasoningModule module = Module(
            SubClassOf(Some("r", OneOf("a", "b")), Class("Y")),
            ClassAssertion(Class("Y"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A bare multi-individual nominal on the subclass side, {a,b} ⊑ Y, is likewise delegated until the negative-position decomposition ships.</summary>
    [TestMethod]
    public void BareMultiIndividualSubclassNominalIsDelegated()
    {
        ReasoningModule module = Module(
            SubClassOf(OneOf("a", "b"), Class("Y")),
            ClassAssertion(Class("Y"), Individual("a")));

        AssertDelegatesLike(module);
    }

    /// <summary>An asserted <c>ObjectHasValue(r⁻, a)</c> on <c>x</c> is the ground fact <c>(a, x) ∈ r</c> — the forward spelling's edge with its endpoints EXCHANGED, since <c>x</c> having <c>a</c> as an r-predecessor is <c>a</c> having <c>x</c> as an r-successor. The edge feeds the left-existential elimination <c>∃r.B ⊑ ⊥</c> from <c>a</c>, whose r-successor <c>x</c> is a <c>B</c>, so <c>a</c> is unsatisfiable; written unexchanged the edge would leave <c>a</c>, and the module, untouched.</summary>
    [TestMethod]
    public void InverseAssertedNominalIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(HasValueInverse("r", "a"), Individual("x")),
            ClassAssertion(Class("B"), Individual("x")),
            SubClassOf(Some("r", Class("B")), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse asserted-nominal module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x asserts a as its r-predecessor, so a has the r-successor x, which is a B, and ∃r.B ⊑ ⊥ sends a to the bottom concept.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops the asserted restriction, so a has no known r-successor and the elimination never fires.");
    }

    /// <summary>A symmetric role mirrors an asserted edge: with <c>Symmetric(r)</c> and an asserted <c>(a, r, b)</c>, the reverse <c>(b, r, a)</c> is forced, so <c>b</c> has an r-successor <c>a</c> typed <c>B</c>, and <c>∃r.B ⊑ ⊥</c> condemns <c>b</c> — an inconsistency the symmetry-blind tableau, which keeps only the asserted direction, misses.</summary>
    [TestMethod]
    public void SymmetricEdgeFeedsLeftExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            Edge("a", "r", "b"),
            ClassAssertion(Class("B"), Individual("a")),
            SubClassOf(Some("r", Class("B")), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The symmetric module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Symmetry mirrors (a, r, b) to (b, r, a); a is a B, so b has an r-successor in B, which ∃r.B ⊑ ⊥ sends to the bottom concept.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The symmetry-blind tableau keeps only (a, r, b), so b has no known r-successor and the elimination never fires.");
    }

    /// <summary>A symmetric role mirrors an asserted edge backwards into a range clash: <c>Symmetric(r)</c> makes <c>a</c> an r-target of <c>b</c>, so <c>range(r) = K</c> types <c>a</c>, which is also the disjoint <c>L</c> — decided where the symmetry-blind tableau types only the forward target <c>b</c>.</summary>
    [TestMethod]
    public void SymmetricEdgeFeedsRangeBackwardIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            Edge("a", "r", "b"),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The symmetric-range module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The mirror (b, r, a) makes a an r-target, so r's range types a as K; a is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The symmetry-blind tableau types only the forward target b, so a stays only L and nothing clashes.");
    }

    /// <summary>A consistent symmetric module is decided consistent by the fast-path (known-answer: EL keeps the symmetry the tableau drops, so the mirror is harmless when nothing clashes).</summary>
    [TestMethod]
    public void SymmetricEdgeWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            Edge("a", "r", "b"),
            ClassAssertion(Class("Person"), Individual("a")),
            ClassAssertion(Class("Person"), Individual("b")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The symmetric module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The reverse edge (b, r, a) forces no clash, so the module is consistent.");
    }

    /// <summary>A symmetric role with only a subclass-side left existential is decided, not delegated: <c>∃r.Z ⊑ B</c> consumes incoming edges and generates none, so the role stays ground-only and the harmless module is decided consistent.</summary>
    [TestMethod]
    public void SymmetricWithLeftExistentialIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            Edge("a", "r", "b"),
            SubClassOf(Some("r", Class("Z")), Class("HasZNeighbour")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "A left existential does not generate edges, so the symmetric role stays ground-only and the module is decided.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Neither a nor b is a Z, so ∃r.Z ⊑ HasZNeighbour never fires and the module is consistent.");
    }

    /// <summary>A symmetric role bearing a superclass existential is decided by the EL fast-path through per-owner witness minting: <c>A ⊑ ∃r.B</c> gives each owner a distinct interned witness, so the symmetric mirror over it stays owner-local — the shared-filler edge the asserted-edge mirror could not reproduce. Consistent: <c>x</c> has an <c>r</c>-successor in <c>B</c> and the mirror forces no clash.</summary>
    [TestMethod]
    public void SymmetricRoleWithSuperclassExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A symmetric SUPER-role of an existential-bearing sub-role is decided by the EL fast-path: <c>q ⊑ r</c> and <c>A ⊑ ∃q.B</c> promote the minted <c>q</c>-edge up to <c>r</c>, and because minting follows the role hierarchy downward every mirrored role gives a per-owner witness, so the promoted edge the shared filler could not reproduce is now owner-local. Consistent: the mirror forces no clash.</summary>
    [TestMethod]
    public void SymmetricSuperRoleOverExistentialSubRoleIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubProperty("q", "r"),
            SubClassOf(Class("A"), Some("q", Class("B"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A symmetric role that is also reflexive is delegated: the reflexive self-demand gives the role non-asserted self-edges beyond the asserted ground graph, so the ground-only gate rejects it.</summary>
    [TestMethod]
    public void SymmetricReflexiveRoleIsDelegated()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            Reflexive("r"),
            Edge("a", "r", "b"),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDelegatesLike(module);
    }

    /// <summary>A symmetric characteristic over an inverse role expression is admitted after the inverse-spelling tier: <c>Symmetric(r⁻) ≡ Symmetric(r)</c> self-pairs the forward role, and with no edges and no asymmetric constraint nothing is unsafe and no forced-empty rewrite fires, so the EL fast-path decides the module consistent — the peer of the forward-symmetric decided path.</summary>
    [TestMethod]
    public void SymmetricInverseRoleSpellingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SymmetricInverse("r"),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A symmetric role mirrors an asserted edge that reaches it through a SUB-role: with <c>q ⊑ r</c> and an asserted <c>(a, q, b)</c>, the edge promotes to <c>(a, r, b)</c>, symmetry forces <c>(b, r, a)</c>, so <c>range(r) = K</c> types <c>a</c>, clashing with the disjoint <c>L</c> on <c>a</c>. The mirror must follow the role hierarchy downward to every sub-role, matching the gate's upward closure — decided where the symmetry-blind tableau, which promotes only the forward edge, stays consistent.</summary>
    [TestMethod]
    public void SymmetricSuperRoleMirrorsSubRoleAssertedEdgeIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubProperty("q", "r"),
            Edge("a", "q", "b"),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The symmetric sub-role module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "(a, q, b) promotes to (a, r, b); symmetry forces (b, r, a); r's range types a as K; a is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The symmetry-blind tableau promotes only the forward (a, r, b), so the range types b not a and nothing clashes.");
    }

    /// <summary>A symmetric role mirrors an asserted edge that reaches it through an EQUIVALENT role: <c>EquivalentObjectProperties(q, r)</c> is bidirectional subsumption, so an asserted <c>(a, q, b)</c> is an <c>r</c>-edge whose symmetric reverse <c>(b, r, a)</c> the mirror must seed — the range then types <c>a</c> and clashes with the disjoint <c>L</c>.</summary>
    [TestMethod]
    public void SymmetricEquivalentRoleMirrorsAssertedEdgeIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            EquivalentProperties("q", "r"),
            Edge("a", "q", "b"),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The symmetric equivalent-role module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "q ≡ r makes (a, q, b) an r-edge; symmetry forces (b, r, a); r's range types a as K; a is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The symmetry-blind tableau keeps a as the source only, so the range never types it.");
    }

    /// <summary>A symmetric sub-role mirror feeds a left-existential clash: <c>q ⊑ r</c>, asserted <c>(a, q, b)</c>, <c>a : B</c>, and <c>∃r.B ⊑ ⊥</c> — the mirror seeds <c>(b, r, a)</c>, so <c>b</c> has an r-successor in <c>B</c> and is condemned, where the symmetry-blind tableau finds no such successor.</summary>
    [TestMethod]
    public void SymmetricSuperRoleMirrorFeedsLeftExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubProperty("q", "r"),
            Edge("a", "q", "b"),
            ClassAssertion(Class("B"), Individual("a")),
            SubClassOf(Some("r", Class("B")), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The symmetric sub-role module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "(a, q, b) promotes to (a, r, b); symmetry forces (b, r, a); a is a B, so b has an r-successor in B, which ∃r.B ⊑ ⊥ condemns.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The symmetry-blind tableau keeps only (a, r, b), so b has no known r-successor.");
    }

    /// <summary>An inverse pairing mirrors an asserted edge backwards into a range clash: <c>InverseObjectProperties(r, s)</c> makes the asserted <c>(x, r, a)</c> an <c>s</c>-edge <c>(a, s, x)</c>, so <c>range(s) = K</c> types <c>x</c>, which is also the disjoint <c>L</c> — decided where the inverse-blind tableau never seeds the s-edge.</summary>
    [TestMethod]
    public void InverseEdgeFeedsRangeBackwardIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Edge("x", "r", "a"),
            Range("s", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "(x, r, a) is the s-edge (a, s, x); s's range types x as K; x is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the inverse pairing, so no s-edge into x exists and nothing clashes.");
    }

    /// <summary>An inverse pairing mirrors an asserted edge into a left-existential clash: <c>(x, r, a)</c> becomes the s-edge <c>(a, s, x)</c>, so <c>a</c> has an s-successor <c>x</c> and <c>∃s.{x} ⊑ ⊥</c> condemns it — decided where the inverse-blind tableau finds no such successor.</summary>
    [TestMethod]
    public void InverseEdgeFeedsLeftExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Edge("x", "r", "a"),
            SubClassOf(HasValue("s", "x"), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "(x, r, a) is the s-edge (a, s, x), so a is in ∃s.{x} ⊑ ⊥ and unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the pairing, so a has no known s-successor.");
    }

    /// <summary>A consistent inverse module is decided consistent by the fast-path (known-answer: EL keeps the inverse the tableau drops, so the mirror is harmless when nothing clashes).</summary>
    [TestMethod]
    public void InverseEdgeWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Edge("x", "r", "a"),
            ClassAssertion(Class("Person"), Individual("x")),
            ClassAssertion(Class("Place"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The reverse s-edge forces no clash, so the module is consistent.");
    }

    /// <summary>A chained inverse pairing closes completely: <c>InverseObjectProperties(r, s)</c> and <c>InverseObjectProperties(s, t)</c> make <c>t</c>'s extension equal <c>r</c>'s, so the asserted <c>(x, r, a)</c> is a <c>t</c>-edge whose range types <c>a</c> and clashes with the disjoint <c>L</c>. The saturation mirror fires on the derived s-edge too, reaching <c>t</c> — the completeness a one-pass seed of asserted edges alone would miss.</summary>
    [TestMethod]
    public void InverseChainedPairingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Inverse("s", "t"),
            Edge("x", "r", "a"),
            Range("t", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The chained-inverse module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "t's extension equals r's, so (x, r, a) is the t-edge (x, t, a); t's range types a as K; a is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops both pairings, so t never gains the edge.");
    }

    /// <summary>A mixed symmetric and inverse pairing closes completely: <c>Symmetric(s)</c> and <c>InverseObjectProperties(r, s)</c> make <c>r</c> symmetric too, so the asserted <c>(x, r, a)</c> forces <c>(a, r, x)</c>, whose range types <c>x</c> and clashes with the disjoint <c>L</c>. The unified saturation mirror fires the symmetric reverse on the inverse-derived s-edge — the case a seed-time symmetric mirror combined with a saturation inverse rule would miss.</summary>
    [TestMethod]
    public void MixedSymmetricAndInversePairingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Symmetric("s"),
            Edge("x", "r", "a"),
            Range("r", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The mixed symmetric/inverse module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "s symmetric with r's inverse makes r symmetric, so (x, r, a) forces (a, r, x); r's range types x as K; x is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse/symmetry-blind tableau drops both characteristics, so r never gains the reverse edge.");
    }

    /// <summary>An inverse-paired role bearing a superclass existential is decided by the EL fast-path through per-owner witness minting: <c>A ⊑ ∃r.B</c> gives each owner a distinct interned witness, so the inverse mirror seeds <c>s</c> from a per-owner node the shared filler could not reproduce. Consistent: <c>x</c> has an <c>r</c>-successor in <c>B</c> and the mirror forces no clash.</summary>
    [TestMethod]
    public void InverseRoleBearingExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// A superclass-position inverse existential (<c>A ⊑ ∃r⁻.C</c>) is decided by the EL fast-path
    /// through the eager generator reduction: it reduces at normalization to <c>A ⊑ ∃g.C</c> over the
    /// synthetic generator role <c>g ⊑ r⁻</c>, so <c>x</c>'s <c>r</c>-predecessor is minted as a
    /// per-owner forward <c>g</c>-successor and the mirror writes the real <c>r</c>-edge back. Consistent:
    /// model Δ = {x, w}, A = {x}, C = {w}, r = {(w, x)} — x has an r-predecessor w in C and nothing
    /// clashes. The inverse-blind tableau simply drops the inverse existential.
    /// </summary>
    [TestMethod]
    public void InverseRoleInSuperclassPositionIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The superclass-position inverse existential is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x gains an r-predecessor in C via the minted g-successor; nothing clashes.");
    }

    /// <summary>An inverse axiom spelled over inverse-role expressions is outside the admitted fragment, so the module is delegated — pinning the <c>IsInverse: false</c> survey guard on both sides.</summary>
    [TestMethod]
    public void InverseAxiomOverInverseRolesIsDelegated()
    {
        ReasoningModule module = Module(
            new OwlInverseObjectPropertiesAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Property("s")) { Origin = Origin("inverse") },
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>An inverse pairing mirrors an asserted edge that reaches the paired role through a SUB-role: with <c>q ⊑ r</c> and an asserted <c>(a, q, b)</c>, the edge promotes to <c>(a, r, b)</c>, the inverse seeds <c>(b, s, a)</c>, so <c>range(s) = K</c> types <c>a</c>, clashing with the disjoint <c>L</c>. The saturation mirror fires on the hierarchy-promoted edge, the inverse analogue of the symmetric sub-role case.</summary>
    [TestMethod]
    public void InverseSubRolePromotionIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubProperty("q", "r"),
            Edge("a", "q", "b"),
            Range("s", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse sub-role module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "(a, q, b) promotes to (a, r, b); the inverse seeds (b, s, a); s's range types a as K; a is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the pairing, so no s-edge into a exists and nothing clashes.");
    }

    /// <summary>A consistent chained inverse pairing is decided consistent by the fast-path: the double mirror terminates and forces no clash (the chained analogue of the no-clash inverse case).</summary>
    [TestMethod]
    public void InverseChainedPairingWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Inverse("s", "t"),
            Edge("x", "r", "a"),
            ClassAssertion(Class("Person"), Individual("x")),
            ClassAssertion(Class("Place"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The chained-inverse module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The chained mirror forces no clash, so the module is consistent.");
    }

    /// <summary>A consistent mixed symmetric and inverse pairing is decided consistent by the fast-path: the unified mirror terminates and forces no clash.</summary>
    [TestMethod]
    public void MixedSymmetricAndInversePairingWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            Symmetric("s"),
            Edge("x", "r", "a"),
            ClassAssertion(Class("Person"), Individual("x")),
            ClassAssertion(Class("Place"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The mixed symmetric/inverse module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The unified mirror forces no clash, so the module is consistent.");
    }

    /// <summary>Two minting pairings where one role also bears a forward range are decided by the EL fast-path: the range on the minting role <c>p</c> types each per-owner witness through the rewritten fresh successor (never the named filler <c>C</c>), and the mirror over both pairings stays owner-local. Consistent: <c>x</c> has an <c>r</c>-successor in <c>B</c> and a <c>p</c>-successor in <c>C</c> typed <c>K</c>, with no clash.</summary>
    [TestMethod]
    public void InversePairingWithRangeOnMintingRoleIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            Inverse("p", "q"),
            SubClassOf(Class("A"), Some("p", Class("C"))),
            Range("p", Class("K")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The minting-pairings-with-range module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The range types each per-owner p-witness as K; nothing clashes.");
    }

    /// <summary>A functional role forces its two asserted successors equal, contradicting their asserted distinctness: <c>Functional(r)</c> with <c>(a, r, b)</c> and <c>(a, r, c)</c> makes <c>b = c</c>, which <c>DifferentIndividuals(b, c)</c> forbids — decided where the functionality-blind tableau keeps <c>b</c> and <c>c</c> distinct.</summary>
    [TestMethod]
    public void FunctionalForcesDistinctSuccessorsClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            Different("b", "c"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "a has at most one r-successor, so b = c, which DifferentIndividuals(b, c) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau keeps b and c distinct and misses the collision.");
    }

    /// <summary>A functional role merges its two successors onto one node, so disjoint types asserted across them clash: <c>b = c</c> makes the node both <c>B</c> and <c>C</c>, and <c>B</c>, <c>C</c> are disjoint — decided where the tableau keeps the types apart.</summary>
    [TestMethod]
    public void FunctionalMergesSuccessorTypesIntoDisjointClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            ClassAssertion(Class("B"), Individual("b")),
            ClassAssertion(Class("C"), Individual("c")),
            Disjoint(Class("B"), Class("C")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "b = c collapses B on b and C on c onto one node; B and C are disjoint, so the node is unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau keeps b and c distinct, so the disjoint types never meet.");
    }

    /// <summary>A functional role with two successors that carry no clashing constraint is decided consistent by the fast-path: the merge forces no inconsistency.</summary>
    [TestMethod]
    public void FunctionalWithoutDistinctnessIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            ClassAssertion(Class("Person"), Individual("b")),
            ClassAssertion(Class("Person"), Individual("c")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "b = c merges two Persons onto one node, which is satisfiable.");
    }

    /// <summary>An inverse-functional role forces its two asserted predecessors equal: <c>InverseFunctional(r)</c> with <c>(b, r, a)</c> and <c>(c, r, a)</c> makes <c>b = c</c> (no reified inverse needed — predecessors are grouped by target), contradicting <c>DifferentIndividuals(b, c)</c>.</summary>
    [TestMethod]
    public void InverseFunctionalForcesDistinctPredecessorsClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseFunctional("r"),
            Edge("b", "r", "a"),
            Edge("c", "r", "a"),
            Different("b", "c"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-functional module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "a has at most one r-predecessor, so b = c, which DifferentIndividuals(b, c) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau keeps b and c distinct.");
    }

    /// <summary>A functional role counts successors over its SUB-roles: with <c>q ⊑ r</c>, an asserted <c>(a, q, b)</c> is an r-successor of <c>a</c>, so together with <c>(a, r, c)</c> functionality forces <c>b = c</c>, which <c>DifferentIndividuals(b, c)</c> forbids. The pre-merge union reads the role's sub-role closure.</summary>
    [TestMethod]
    public void FunctionalMergesViaSubRoleSuccessorIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            SubProperty("q", "r"),
            Edge("a", "q", "b"),
            Edge("a", "r", "c"),
            Different("b", "c"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional sub-role module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "(a, q, b) is an r-successor via q ⊑ r, so a has r-successors b and c; functionality forces b = c, which DifferentIndividuals(b, c) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau misses the collision.");
    }

    /// <summary>A functional role counts an asserted nominal-edge successor: <c>ObjectHasValue(r, b)</c> on <c>a</c> is the r-successor <c>b</c>, so with <c>(a, r, c)</c> functionality forces <c>b = c</c>, which <c>DifferentIndividuals(b, c)</c> forbids.</summary>
    [TestMethod]
    public void FunctionalMergesViaAssertedNominalSuccessorIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            ClassAssertion(HasValue("r", "b"), Individual("a")),
            Edge("a", "r", "c"),
            Different("b", "c"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional nominal-edge module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "ObjectHasValue(r, b) on a is the r-successor b, so a has r-successors b and c; functionality forces b = c, which DifferentIndividuals(b, c) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The nominal/functionality-blind tableau misses the collision.");
    }

    /// <summary>The functional union reaches a fixpoint across more than two successors: <c>(a, r, b)</c>, <c>(a, r, c)</c>, <c>(a, r, d)</c> merge b, c, d onto one node, so <c>DifferentIndividuals(b, d)</c> clashes.</summary>
    [TestMethod]
    public void FunctionalMergesMultipleSuccessorsIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            Edge("a", "r", "d"),
            Different("b", "d"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional multi-successor module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "b, c, d are all r-successors of a, so all three merge; DifferentIndividuals(b, d) forbids b = d.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau keeps them distinct.");
    }

    /// <summary>The functional and inverse-functional unions reach a combined fixpoint: <c>InverseFunctional(s)</c> merges <c>b = c</c> (predecessors of <c>a</c>), which then makes <c>(b, r, d)</c> and <c>(c, r, e)</c> share a source under <c>Functional(r)</c>, forcing <c>d = e</c> — which <c>DifferentIndividuals(d, e)</c> forbids. Pins the fixpoint loop across the two characteristics.</summary>
    [TestMethod]
    public void FunctionalAndInverseFunctionalReachCombinedFixpointIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseFunctional("s"),
            Functional("r"),
            Edge("b", "s", "a"),
            Edge("c", "s", "a"),
            Edge("b", "r", "d"),
            Edge("c", "r", "e"),
            Different("d", "e"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The combined functional module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "b = c (inverse-functional s into a) makes b and c one source, so r's functionality forces d = e, which DifferentIndividuals(d, e) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau makes neither merge.");
    }

    /// <summary>A single asserted successor over a functional role is decided consistent by the fast-path: functionality is vacuous with one successor.</summary>
    [TestMethod]
    public void FunctionalSingleSuccessorIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Edge("a", "r", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "One r-successor forces no merge, so the module is consistent.");
    }

    /// <summary>A functional role bearing a superclass existential is delegated: an existential successor onto a shared filler is a merge the pre-merge ground union cannot perform soundly.</summary>
    [TestMethod]
    public void FunctionalRoleWithExistentialIsDelegated()
    {
        ReasoningModule module = Module(
            Functional("r"),
            SubClassOf(Class("X"), Some("r", Class("B"))),
            ClassAssertion(Class("X"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role that is also symmetric is delegated: the saturation mirror seeds r-successors the pre-merge ground scan cannot see, so the union could be incomplete.</summary>
    [TestMethod]
    public void FunctionalRoleThatIsSymmetricIsDelegated()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Symmetric("r"),
            Edge("a", "r", "b"),
            Edge("c", "r", "a"),
            Different("b", "c"));

        AssertDelegatesLike(module);
    }

    /// <summary>An inverse-functional role that is inverse-paired is delegated: the mirror seeds predecessors the pre-merge ground scan cannot see.</summary>
    [TestMethod]
    public void InverseFunctionalRoleThatIsInversePairedIsDelegated()
    {
        ReasoningModule module = Module(
            InverseFunctional("p"),
            Inverse("p", "q"),
            Edge("b", "p", "a"),
            Edge("c", "p", "a"),
            Different("b", "c"));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional characteristic over an inverse role expression is admitted after the inverse-spelling tier: <c>Functional(r⁻) ≡ InverseFunctional(r)</c> registers the swapped inverse-functional set on the forward role, and with no edges the ground role is safe, so the EL fast-path decides the module consistent.</summary>
    [TestMethod]
    public void FunctionalInverseRoleSpellingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            FunctionalInverse("r"),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>An inverse sub-property <c>r⁻ ⊑ s</c> mirrors an asserted r-edge into an s-edge reverse: with <c>(x, r, a)</c> the classifier seeds <c>(a, s, x)</c>, so <c>range(s) = K</c> types <c>x</c>, clashing with the disjoint <c>L</c> — decided where the inverse-blind tableau never seeds the s-edge.</summary>
    [TestMethod]
    public void InverseSubPropertyMirrorsEdgeIntoRangeClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            Edge("x", "r", "a"),
            Range("s", Class("K")),
            Disjoint(Class("K"), Class("L")),
            ClassAssertion(Class("L"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The r⁻ ⊑ s module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "r⁻ ⊑ s makes (x, r, a) force the s-edge (a, s, x); s's range types x as K; x is also L, disjoint from K.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops r⁻ ⊑ s, so no s-edge into x exists and nothing clashes.");
    }

    /// <summary>A sub-property-of-inverse <c>s ⊑ r⁻</c> mirrors an asserted s-edge into an r-edge reverse: with <c>(x, s, a)</c> the classifier seeds <c>(a, r, x)</c>, so <c>a</c> has an r-successor <c>x</c> and <c>∃r.{x} ⊑ ⊥</c> condemns it.</summary>
    [TestMethod]
    public void SubPropertyInverseMirrorsEdgeIntoLeftExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubPropertyInverse("s", "r"),
            Edge("x", "s", "a"),
            SubClassOf(HasValue("r", "x"), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The s ⊑ r⁻ module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "s ⊑ r⁻ makes (x, s, a) force the r-edge (a, r, x), so a is in ∃r.{x} ⊑ ⊥ and unsatisfiable.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops s ⊑ r⁻, so a has no known r-successor.");
    }

    /// <summary>A consistent inverse sub-property module is decided consistent by the fast-path (known-answer: EL keeps the inverse sub-property the tableau drops).</summary>
    [TestMethod]
    public void InverseSubPropertyWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            Edge("x", "r", "a"),
            ClassAssertion(Class("Person"), Individual("x")),
            ClassAssertion(Class("Place"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The r⁻ ⊑ s module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The reverse s-edge forces no clash, so the module is consistent.");
    }

    /// <summary>An inverse sub-property is ONE-directional: <c>r⁻ ⊑ s</c> mirrors r-edges into s but NOT s-edges into r, so an s-edge that would clash through an r-reverse does not — decided consistent, where a mutual pairing would be inconsistent.</summary>
    [TestMethod]
    public void InverseSubPropertyIsOneDirectionalAndDecidedConsistent()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            Edge("x", "s", "a"),
            SubClassOf(HasValue("r", "x"), NothingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The r⁻ ⊑ s module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "r⁻ ⊑ s does not force s-edges into r, so (x, s, a) yields no r-edge and ∃r.{x} ⊑ ⊥ never fires.");
    }

    /// <summary>An inverse sub-property spelled over inverse roles on BOTH sides (<c>r⁻ ⊑ s⁻</c>, i.e. a plain <c>r ⊑ s</c>) is delegated — pinning that only the exactly-one-inverse spellings are admitted.</summary>
    [TestMethod]
    public void InverseSubPropertyOverBothInverseRolesIsDelegated()
    {
        ReasoningModule module = Module(
            new OwlSubObjectPropertyOfAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "s")))) { Origin = Origin("bothinverse") },
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role that RECEIVES the mirror of an inverse sub-property is delegated: <c>r⁻ ⊑ s</c> seeds s-successors from r-edges that the pre-merge scan cannot see, so <c>Functional(s)</c> could miss a merge — the mirror-target arm of the functional gate for the one-directional case.</summary>
    [TestMethod]
    public void FunctionalRoleReceivingInverseSubPropertyMirrorIsDelegated()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            Functional("s"),
            Edge("x", "r", "a"),
            Edge("y", "r", "a"),
            Different("x", "y"));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role that is the mirror SOURCE of an inverse sub-property stays EL-decided: <c>r⁻ ⊑ s</c> reads r's edges but seeds none onto r, so <c>Functional(r)</c>'s successors are all asserted and the merge is complete. Guards the exact gate line that admits a mirror source while delegating a mirror target — a regression widening it back to every paired role would spuriously delegate this.</summary>
    [TestMethod]
    public void FunctionalRoleThatIsInverseSubPropertySourceIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            Functional("r"),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            Different("b", "c"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional mirror-source module is decided by the EL fast-path: r receives no mirror edges.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "r's successors b and c are all asserted, so functionality forces b = c, which DifferentIndividuals(b, c) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau misses the collision.");
    }

    /// <summary>An inverse-functional role that RECEIVES the mirror of a sub-property-of-inverse is delegated: <c>s ⊑ r⁻</c> seeds r-predecessors the pre-merge scan cannot see, so <c>InverseFunctional(r)</c> could miss a merge.</summary>
    [TestMethod]
    public void InverseFunctionalRoleReceivingSubPropertyInverseMirrorIsDelegated()
    {
        ReasoningModule module = Module(
            SubPropertyInverse("s", "r"),
            InverseFunctional("r"),
            Edge("b", "r", "a"),
            Edge("c", "r", "a"),
            Different("b", "c"));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role reached transitively through the role hierarchy from a directed mirror target is delegated: <c>r⁻ ⊑ s</c>, <c>s ⊑ t</c>, <c>Functional(t)</c> — the mirror seeds s-edges that promote to t, so t's successors are not all asserted (the sub-role-closure arm of the gate for the one-directional spelling).</summary>
    [TestMethod]
    public void FunctionalSuperRoleOverDirectedMirrorSubRoleIsDelegated()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            SubProperty("s", "t"),
            Functional("t"),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>An inverse sub-property whose mirror SOURCE bears a superclass existential is decided by the EL fast-path through per-owner witness minting: <c>r⁻ ⊑ s</c> with <c>A ⊑ ∃r.B</c> gives each owner a distinct interned witness, so the reverse <c>s</c>-edge the mirror seeds is owner-local — sound where the shared filler was not. Consistent: the mirror forces no clash.</summary>
    [TestMethod]
    public void InverseSubPropertySourceBearingExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            ClassAssertion(Class("A"), Individual("x")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>A consistent sub-property-of-inverse module is decided consistent by the fast-path: <c>s ⊑ r⁻</c> alone forces no clash.</summary>
    [TestMethod]
    public void SubPropertyInverseWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            SubPropertyInverse("s", "r"),
            Edge("x", "s", "a"),
            ClassAssertion(Class("Person"), Individual("x")),
            ClassAssertion(Class("Place"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The s ⊑ r⁻ module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The reverse r-edge forces no clash, so the module is consistent.");
    }

    /// <summary>A functional role counts successors reaching it through a SINGLE-LINK property chain, which the normalizer reduces to a plain sub-role: with <c>[r] ⊑ s</c>, the asserted <c>(x, r, a)</c> and <c>(x, r, b)</c> are both s-successors of <c>x</c>, so <c>Functional(s)</c> forces <c>a = b</c>, which <c>DifferentIndividuals(a, b)</c> forbids. The pre-merge union closure must include the single-link chain the gate's closure already does.</summary>
    [TestMethod]
    public void FunctionalMergesViaSingleLinkChainSubRoleIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("s"),
            Chain("s", "r"),
            Edge("x", "r", "a"),
            Edge("x", "r", "b"),
            Different("a", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The single-link-chain functional module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "[r] ⊑ s makes (x, r, a) and (x, r, b) s-successors of x; s's functionality forces a = b, which DifferentIndividuals(a, b) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The chain/functionality-blind tableau misses the collision.");
    }

    /// <summary>An inverse-functional role counts predecessors reaching it through a single-link property chain: with <c>[r] ⊑ s</c>, <c>(a, r, x)</c> and <c>(b, r, x)</c> are both s-predecessors of <c>x</c>, so <c>InverseFunctional(s)</c> forces <c>a = b</c>, which <c>DifferentIndividuals(a, b)</c> forbids.</summary>
    [TestMethod]
    public void InverseFunctionalMergesViaSingleLinkChainSubRoleIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseFunctional("s"),
            Chain("s", "r"),
            Edge("a", "r", "x"),
            Edge("b", "r", "x"),
            Different("a", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The single-link-chain inverse-functional module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "[r] ⊑ s makes (a, r, x) and (b, r, x) s-predecessors of x; s's inverse-functionality forces a = b, which DifferentIndividuals(a, b) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The chain/functionality-blind tableau misses the collision.");
    }

    /// <summary>A functional role with a MULTI-LINK property chain conclusion is delegated: the chain composes edges, making the conclusion edge-generating, so its successors are not confined to asserted ground edges.</summary>
    [TestMethod]
    public void FunctionalMultiLinkChainConclusionIsDelegated()
    {
        ReasoningModule module = Module(
            Functional("t"),
            Chain("t", "r", "s"),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role that is also reflexive is delegated: the reflexive characteristic gives every node a self-successor the pre-merge ground scan cannot see, so functionality could miss a merge.</summary>
    [TestMethod]
    public void FunctionalReflexiveRoleIsDelegated()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Reflexive("r"),
            Edge("a", "r", "b"),
            Different("a", "b"));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role over a transitive SUB-role is delegated: transitivity composes edges over the sub-role that promote up to the functional role, so its successors are not all asserted — the upward-closure arm of the gate.</summary>
    [TestMethod]
    public void FunctionalSuperRoleOverTransitiveSubRoleIsDelegated()
    {
        ReasoningModule module = Module(
            Functional("s"),
            SubProperty("r", "s"),
            Transitive("r"),
            Edge("a", "r", "b"),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDelegatesLike(module);
    }

    /// <summary>A functional role bearing a property range is decided, not delegated: a range writes a subsumer onto the successor, not a new successor, so the role stays ground-only and the merge still fires.</summary>
    [TestMethod]
    public void FunctionalRoleWithRangeIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Range("r", Class("C")),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            Different("b", "c"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "A range does not generate a successor, so the functional role stays ground-only and the module is decided.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "b = c via functionality, which DifferentIndividuals(b, c) forbids; the range types the merged successor as C but does not block the decision.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The functionality-blind tableau misses the collision.");
    }

    /// <summary>An inverse-functional role with two predecessors that carry no clashing constraint is decided consistent by the fast-path.</summary>
    [TestMethod]
    public void InverseFunctionalWithoutClashIsConsistentAndDecided()
    {
        ReasoningModule module = Module(
            InverseFunctional("r"),
            Edge("b", "r", "a"),
            Edge("c", "r", "a"),
            ClassAssertion(Class("Person"), Individual("b")),
            ClassAssertion(Class("Person"), Individual("c")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-functional module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "b = c merges two Persons onto one node, which is satisfiable.");
    }

    /// <summary>
    /// An inverse-role range types the source of the forward role's existential: with
    /// <c>range(r⁻) = D</c> (equivalently <c>domain(r) = D</c>) and <c>A ⊑ ∃r.B</c>, every
    /// <c>A</c> is an <c>r</c>-source and so a <c>D</c>; disjoint from <c>A</c>, that empties
    /// <c>A</c> and condemns the asserted <c>A</c> individual — decided where the inverse-blind
    /// tableau drops the axiom and never types the source.
    /// </summary>
    [TestMethod]
    public void InverseRangeTypesExistentialSourceIntoClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            InverseRange("r", Class("D")),
            Disjoint(Class("A"), Class("D")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-range module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "range(r⁻) = domain(r) = D types A (an r-source); A is disjoint from D, so A is empty and its asserted individual x is condemned.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops range(r⁻), so A is never typed D and nothing clashes.");
    }

    /// <summary>
    /// An inverse-role domain types the target of the forward role's existential: with
    /// <c>domain(r⁻) = E</c> (equivalently <c>range(r) = E</c>) and <c>A ⊑ ∃r.B</c>, the
    /// existential's successor is an <c>E</c> as well as a <c>B</c>; disjoint <c>B</c> and
    /// <c>E</c> empty the successor and so empty <c>A</c> — decided where the inverse-blind
    /// tableau drops the axiom and never types the target.
    /// </summary>
    [TestMethod]
    public void InverseDomainTypesExistentialTargetIntoClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            InverseDomain("r", Class("E")),
            Disjoint(Class("B"), Class("E")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-domain module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "domain(r⁻) = range(r) = E types A's r-successor, which is also a B; B and E are disjoint, so the successor is empty and A cannot have it.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops domain(r⁻), so the successor stays only B and nothing clashes.");
    }

    /// <summary>
    /// An inverse-role range subsumes the existential's owner and composes forward: with
    /// <c>range(r⁻) = C</c> (equivalently <c>domain(r) = C</c>), <c>A ⊑ ∃r.B</c>, and
    /// <c>C ⊑ G</c>, every <c>A</c> has an <c>r</c>-successor and so is a <c>C</c> and hence a
    /// <c>G</c>, giving <c>A ⊑ C</c> and <c>A ⊑ G</c> — subsumptions the inverse-blind tableau,
    /// which drops the range axiom, does not derive. (<c>C ⊑ G</c> is forward, so <c>C</c> and
    /// <c>G</c> enter the module signature the subsumption enumeration ranges over.)
    /// </summary>
    [TestMethod]
    public void InverseRangeSubsumesTheExistentialOwnerIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            InverseRange("r", Class("C")),
            SubClassOf(Class("C"), Class("G")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        List<string> elKeys = SubsumptionKeys(decision.Verdict!);
        List<string> tableauKeys = SubsumptionKeys(AlcModuleReasoner.Decide(module, TestContext.CancellationToken));
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-range module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing forces any class empty, so the module is consistent.");
        Assert.Contains($"{Example}A→{Example}C", elKeys, "range(r⁻) = domain(r) = C types A's r-source, so A ⊑ C.");
        Assert.Contains($"{Example}A→{Example}G", elKeys, "A ⊑ C ⊑ G composes forward.");
        Assert.DoesNotContain($"{Example}A→{Example}C", tableauKeys, "The inverse-blind tableau drops range(r⁻) and never derives A ⊑ C.");
        Assert.DoesNotContain($"{Example}A→{Example}G", tableauKeys, "The inverse-blind tableau never derives A ⊑ G either.");
    }

    /// <summary>
    /// A transitive inverse role composes the forward role: <c>Transitive(r⁻)</c> holds exactly
    /// when <c>Transitive(r)</c> does, so an asserted <c>a→b→c</c> gives <c>a→c</c>; with
    /// <c>a</c> an <c>A</c> disjoint from <c>∃r.C</c> and <c>c</c> a <c>C</c>, the composed edge
    /// condemns <c>a</c> — a clash only the composition creates (the direct <c>b→c</c> edge does
    /// not condemn <c>b</c>, which is not an <c>A</c>).
    /// </summary>
    [TestMethod]
    public void InverseTransitiveComposesIntoClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseTransitive("r"),
            Edge("a", "r", "b"),
            Edge("b", "r", "c"),
            ClassAssertion(Class("A"), Individual("a")),
            ClassAssertion(Class("C"), Individual("c")),
            Disjoint(Class("A"), Some("r", Class("C"))));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-transitive module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Transitive(r⁻) = Transitive(r) composes a→b→c to a→c; a is an A with an r-successor c in C, which A ⊓ ∃r.C ⊑ ⊥ condemns.");
    }

    /// <summary>
    /// An inverse pairing over an existential-bearing role is decided by the EL fast-path — a capability
    /// gain the inverse-blind tableau misses. <c>A ⊑ ∃r.B</c> mints <c>x</c> a per-owner witness <c>v</c>,
    /// the inverse seeds <c>(s, v, x)</c>, and <c>x</c> is an <c>A</c>, so <c>∃s.A ⊑ ⊥</c> empties <c>v</c>;
    /// <c>x</c> then has no satisfiable <c>r</c>-successor in <c>B</c> and is condemned. The per-owner
    /// witness is what makes the backward clash reach only its own owner. The tableau drops the pairing, so
    /// no <c>s</c>-edge into <c>x</c> exists and it stays consistent.
    /// </summary>
    [TestMethod]
    public void InversePairingOverExistentialMirrorsBackwardClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Some("s", Class("A")), NothingReference),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-pairing-over-existential module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "x mints a witness v; the inverse seeds (s, v, x); x is an A, so ∃s.A ⊑ ⊥ empties v, leaving x with no satisfiable r-successor.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the pairing, so no s-edge into x exists and nothing clashes.");
    }

    /// <summary>
    /// An inverse range on a role that is both inverse-paired and existential-bearing is decided by the
    /// EL fast-path: the inverse range <c>range(r⁻) = domain(r)</c> types the edge SOURCE — the genuine,
    /// owner-independent owner — while the per-owner mint makes the pairing over the existential sound, so
    /// EL now decides the module the shared-witness tier once delegated. Consistent: <c>x</c> has an
    /// <c>r</c>-successor in <c>B</c> and <c>domain(r) = D</c> types <c>x</c>, with no clash.
    /// </summary>
    [TestMethod]
    public void InverseRangeOnPairedExistentialRoleIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            InverseRange("r", Class("D")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-range-on-paired-existential module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x has an r-successor in B and domain(r) = D types x; nothing clashes.");
    }

    /// <summary>A range on the mirror-target role of a minting pairing is decided by the EL fast-path: <c>range(s)</c> under <c>Inverse(r, s)</c> is an owner-independent constraint on every <c>r</c>-source — <c>domain(r) = K</c> — so <c>A ⊑ ∃r.B</c> entails <c>A ⊑ K</c> and the module is consistent. The inverse-blind tableau drops the pairing and cannot derive the subsumption, so the entailment is asserted as the known correct answer.</summary>
    [TestMethod]
    public void RangeOnMintingPairingMirrorTargetIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            Range("s", Class("K")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The range-on-mirror-target module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "domain(r) = range(s) = K types every r-source; nothing clashes.");
        Assert.Contains($"{Example}A→{Example}K", SubsumptionKeys(decision.Verdict), "Every A has an r-successor, so every A is an r-source and range(s) = domain(r) = K subsumes it.");
    }

    /// <summary>
    /// CE-0 — per-owner containment. <c>{A1 ⊑ ∃r.B, A2 ⊑ ∃r.B, A1 ⊑ ⊥, Symmetric(r)}</c> with <c>a : A2</c>
    /// is CONSISTENT: minting gives <c>A1</c> and <c>A2</c> distinct witnesses, so <c>A1 ⊑ ⊥</c>
    /// back-propagates through the symmetric mirror only onto <c>A1</c>'s witness and never reaches
    /// <c>A2</c> or <c>a</c>. The shared-filler calculus would fold both onto one <c>B</c> node and wrongly
    /// empty <c>A2</c>, condemning <c>a</c> — the unsoundness the per-owner witness dissolves. Decided by
    /// EL and agreeing with the symmetry-blind tableau, which reaches the same verdict by dropping the mirror.
    /// </summary>
    [TestMethod]
    public void MintedWitnessContainsOwnerBottomIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A1"), Some("r", Class("B"))),
            SubClassOf(Class("A2"), Some("r", Class("B"))),
            SubClassOf(Class("A1"), NothingReference),
            ClassAssertion(Class("A2"), Individual("a")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// CE-1 — one owner's bottom among many predecessors. <c>{A1, A2, A3 ⊑ ∃r.B, A2 ⊑ ⊥, r⁻ ⊑ r}</c> with
    /// <c>a : A1</c> and <c>c : A3</c> is CONSISTENT: the one-directional inverse mirror over each owner's
    /// distinct minted witness keeps <c>A2 ⊑ ⊥</c> off <c>A1</c>'s and <c>A3</c>'s witnesses, so <c>a</c>
    /// and <c>c</c> stay satisfiable. Proves the <c>∃r⁻.owner-core</c> decoration is load-bearing — interning
    /// on the filler alone would re-leak. Decided by EL, agreeing with the inverse-blind tableau.
    /// </summary>
    [TestMethod]
    public void MintedWitnessesIsolateOneOwnerBottomIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseSubProperty("r", "r"),
            SubClassOf(Class("A1"), Some("r", Class("B"))),
            SubClassOf(Class("A2"), Some("r", Class("B"))),
            SubClassOf(Class("A3"), Some("r", Class("B"))),
            SubClassOf(Class("A2"), NothingReference),
            ClassAssertion(Class("A1"), Individual("a")),
            ClassAssertion(Class("A3"), Individual("c")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// CE-4 — cyclic existential termination. <c>{A ⊑ ∃r.A, Symmetric(r)}</c> with <c>a : A</c> is
    /// CONSISTENT and TERMINATES: firing the existential for a minted witness whose core is <c>A</c>
    /// re-derives the identical <c>(A, {∃r⁻.A})</c> description, so the interner folds it and the saturation
    /// reaches fixpoint rather than minting an unbounded chain. Equality-blocking on the grown description is
    /// what bounds the witness forest. Decided by EL (the test also pins that saturation halts).
    /// </summary>
    [TestMethod]
    public void CyclicInverseExistentialFoldsAndIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A"), Some("r", Class("A"))),
            ClassAssertion(Class("A"), Individual("a")));

        AssertElDecidesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// The saturation-time self-fold abstention. <c>CyclicInverseExistentialFoldsAndIsDecidedByEl</c>'s
    /// module with the single self-elimination <c>∃r.Self ⊑ Q</c> added: the cyclic existential still
    /// re-derives its witness's own description, so the witness interns onto its owner, but the fold's
    /// artifact self-edge now reaches a self-elimination that would force <c>Q</c> onto a position the
    /// module's self-loop-free models need not carry. The mint records the abstention and the module
    /// delegates. The pair is the controlled contrast: the self-elimination is the only axiom between the
    /// decided sibling and this delegation, so a saturation that stopped recording the fold would decide
    /// this module too.
    /// </summary>
    [TestMethod]
    public void CyclicSelfFoldUnderSelfEliminationDelegates()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A"), Some("r", Class("A"))),
            SubClassOf(HasSelf("r"), Class("Q")),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ElTotals.ElDecided, "The cyclic witness folds onto its own owner where a self-elimination reaches the witness role, so the module delegates to the fallback decider.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, "The delegated verdict surfaces as a named fragment-relative outcome.");
    }

    /// <summary>
    /// The saturation-time cross-owner abstention. Mutually recursive inverse-coupled existentials
    /// <c>A ⊑ ∃r.B</c> and <c>B ⊑ ∃r.A</c> grow both owners' witnesses along the same two decorations, so
    /// the two chains reach one demand set from opposite ends and a mint returns a witness another owner
    /// already created — the unique-ownership invariant's collision. The self-elimination
    /// <c>∃r.Self ⊑ Q</c> puts the module outside the fold-safety fence (it reaches the witness role
    /// closure) without ever folding a witness onto its own owner, which is what leaves the cross-owner
    /// clause the one that fires. The mint records the abstention and the module delegates.
    /// </summary>
    [TestMethod]
    public void CrossOwnerWitnessFoldOutsideTheFoldFenceDelegates()
    {
        ReasoningModule module = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Some("r", Class("A"))),
            SubClassOf(HasSelf("r"), Class("Q")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ElTotals.ElDecided, "Two distinct owners' demand sets coincide, so the shared per-owner witness delegates the module to the fallback decider.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, "The delegated verdict surfaces as a named fragment-relative outcome.");
    }

    /// <summary>
    /// Adversarial minting-soundness battery: each module's TRUE consistency was established by an explicit
    /// model (consistent) or an unsat derivation (inconsistent), independent of the inverse-blind tableau. The
    /// FINAL coupled verdict (EL fast-path or its delegation) must match that ground truth. A mismatch is a
    /// soundness hole — a false inconsistency (EL over-condemns via a folded successor) or a false consistency
    /// (a real clash missed, e.g. delegated to the inverse-blind oracle).
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent)[] MintingSoundnessCases() =>
        [
            ("SeedRepro_CoreFoldLeak_Inverse", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("CoreFoldLeak_Symmetric", Module(
                Symmetric("r"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("r", Class("A1")), Class("Q")),
                SubClassOf(Some("r", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("CoreFoldLeak_InverseSubProperty", Module(
                InverseSubProperty("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("CoreFoldLeak_ThreeOwners_A3Condemned", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("A3"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A3"), Individual("a3"))), true),

            ("CoreFoldLeak_ThreeLevelDeepFold", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("C"))),
                SubClassOf(Class("C"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("CoreFoldLeak_NominalTypedOwner", Module(
                Inverse("r", "s"),
                SubClassOf(OneOf("a2"), Class("A2")),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference)), true),

            ("CoreFoldLeak_SubRolePromoted", Module(
                Inverse("r", "s"),
                SubProperty("q", "r"),
                SubClassOf(Class("A1"), Some("q", Class("B"))),
                SubClassOf(Class("A2"), Some("q", Class("B"))),
                SubClassOf(Class("B"), Some("q", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("CoreFoldLeak_EquivalentProperties", Module(
                Inverse("r", "s"),
                EquivalentProperties("q", "r"),
                SubClassOf(Class("A1"), Some("q", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("FalseConsistent_RangeOnMirroredRole", Module(
                SubProperty("q", "r"),
                Inverse("r", "s"),
                Range("s", NothingReference),
                SubClassOf(Class("A"), Some("q", Class("B"))),
                ClassAssertion(Class("A"), Individual("a"))), false),

            ("FalseConsistent_InverseDomainRangeTarget", Module(
                Inverse("r", "s"),
                InverseDomain("s", NothingReference),
                SubClassOf(Class("A"), Some("s", Class("B"))),
                ClassAssertion(Class("A"), Individual("a"))), false),

            ("FalseConsistency_ChainedInversePairThroughT", Module(
                Inverse("r", "s"),
                Inverse("s", "t"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("t", Class("K")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), false),

            ("Probe_CyclicCoreFold_BackwardClash", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("B"))),
                SubClassOf(Some("s", Class("B")), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false),

            ("CrossCoreMutualCycleFoldDoesNotLeak", Module(
                Inverse("r", "s"),
                SubClassOf(Class("P"), Some("r", Class("Q"))),
                SubClassOf(Class("Q"), Some("r", Class("P"))),
                SubClassOf(Class("P"), Some("r", Class("G"))),
                SubClassOf(Class("Q"), Some("r", Class("G"))),
                SubClassOf(Some("s", Class("P")), Class("M")),
                SubClassOf(Some("s", Class("Q")), Class("N")),
                SubClassOf(new OwlObjectIntersectionOf([Class("M"), Class("N")]), NothingReference),
                ClassAssertion(Class("P"), Individual("aP")),
                ClassAssertion(Class("Q"), Individual("aQ"))), true),

            ("Control_ChainedInversePairSingleOwner", Module(
                Inverse("r", "s"),
                Inverse("s", "t"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("t", Class("K")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A1"), Individual("a1"))), false),

            ("Control_DistinctFillerCoresNoFold", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B1"))),
                SubClassOf(Class("A2"), Some("r", Class("B2"))),
                SubClassOf(Class("B1"), Some("r", Class("K"))),
                SubClassOf(Class("B2"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("Control_SharedSuccessorGenuinelyEmpty", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Class("K"), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), false),

            ("Control_FoldNoBackwardClash", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                ClassAssertion(Class("A2"), Individual("a2"))), true),

            ("Control_SingleOwnerBackwardClash", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("s", Class("A2")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), false),

            ("Control_PlainTwoLevelMintNoClash", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                ClassAssertion(Class("A"), Individual("a"))), true),

            ("Control_MintedWitnessTwoLevelForwardBottom_Symmetric", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("C"))),
                SubClassOf(Class("C"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false),

            ("Control_AssertedGroundEdgeBackwardClash", Module(
                Inverse("r", "s"),
                Edge("a", "r", "b"),
                SubClassOf(Some("s", Class("P")), NothingReference),
                ClassAssertion(Class("P"), Individual("a"))), false),

            ("Control_SuccessorOverUncoupledRangeRole", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("t", Class("K"))),
                Range("t", Class("Q")),
                SubClassOf(Some("s", Class("A1")), Class("Q")),
                SubClassOf(Some("s", Class("Q")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true),
        ];

    /// <summary>The minting-soundness battery: every <see cref="MintingSoundnessCases"/> case's EL-coupled verdict matches its ground-truth consistency; the report names every offender.</summary>
    [TestMethod]
    public void MintingSoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent)[] cases = MintingSoundnessCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | elDecided | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool elDecided = decision.Statistics.ElTotals.ElDecided;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool ok = finalConsistent == trueConsistent;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + elDecided + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " elDecided=" + elDecided);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>The EL coupled reasoner's decision path for a battery case: the fast-path decided the module, or the module fell outside the fragment and was delegated to the fallback decider.</summary>
    private enum ElPath
    {
        /// <summary>The EL fast-path decided the module.</summary>
        Decided,

        /// <summary>The module was delegated to the fallback decider.</summary>
        Delegated,
    }

    /// <summary>
    /// Adversarial backward-minting-soundness battery for the eager generator reduction of a
    /// superclass-position inverse existential (<c>A ⊑ ∃r⁻.C</c> reduced to <c>A ⊑ ∃g.C</c> over the
    /// synthetic generator role <c>g ⊑ r⁻</c>). Each module's TRUE consistency is established by an
    /// explicit hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent
    /// of the inverse-blind tableau — the oracle cannot witness these gains. The FINAL coupled verdict
    /// (EL fast-path, or its delegation to the fallback) must match that ground truth. A mismatch on an
    /// inconsistent case is the headline soundness/capability hole: delegation would answer it consistent
    /// via the inverse-blind fallback, so the inconsistent cases are decided here or the run fails. Each
    /// case also carries its expected decision path (decided by the fast-path, or delegated), asserted
    /// alongside the verdict: a case that silently changes tier fails even when its verdict happens to
    /// agree.
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] BackwardMintingCases() =>
        [
            //B1 — model Δ = {a, c}, A = {a}, C = {c}, r = {(c, a)}: a's forced r-predecessor c lies in C,
            //nothing clashes.
            ("B1_BasicPredecessorWitness", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //B2 — a needs an r-predecessor w; the mirror r-edge (w, a) puts its target a in range(r) = E ⊑ ⊥,
            //so a is condemned. The inverse-blind tableau drops the inverse existential and misses it.
            ("B2_RangeClashOnOwner", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //B3 — the witness w is in C (its core) and in D (∃r.⊤ ⊑ D over its r-edge to a); C ⊓ D ⊑ ⊥
            //empties w, and ⊥ back-propagates over the g-edge to condemn a.
            ("B3_DomainConjunctionClash", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                Domain("r", Class("D")),
                SubClassOf(new OwlObjectIntersectionOf([Class("C"), Class("D")]), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //B4 — the synthetic (a) mirror over the witness's real r-edge fires the left existential
            //∃r⁻.C ⊑ Y on a, and Y ⊑ ⊥ condemns it.
            ("B4_APlusBComposition", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(SomeInverse("r", Class("C")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //B5 — model Δ = {a2, b}, A2 = {a2}, B = {b}, r = {(b, a2)}: b's only r-successor a2 is not in A1,
            //so ∃r.A1 ⊑ Q never fires on b. A1 ⊑ ⊥ is genuinely entailed but a2 ∈ A2 stays satisfiable — a
            //fold of the A1 and a2 witnesses would falsely condemn a2.
            ("B5_OwnerSeparation", Module(
                SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
                SubClassOf(Some("r", Class("A1")), Class("Q")),
                SubClassOf(Class("Q"), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true, ElPath.Decided),

            //B6 — model Δ = {a2, b, k}, A2 = {a2}, B = {b}, K = {k}, r = {(k, b), (b, a2)}, Q1 = P = ∅. A1's
            //branch derives ⊥ (its depth-2 predecessor gains P and back-propagates), which REQUIRES the
            //depth-2 witnesses to stay distinct per owner (owner-demand inheritance); a2 stays satisfiable.
            ("B6_DepthTwoFoldDiscrimination", Module(
                SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(Some("r", Class("A1")), Class("Q1")),
                SubClassOf(Some("r", Class("Q1")), Class("P")),
                SubClassOf(Class("P"), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true, ElPath.Decided),

            //B7 — model r = {(c, c)}: the witness is c itself. Pins termination and the witness == subject
            //fold, with no spurious ownership abstention.
            ("B7_CyclicSelfModel", Module(
                SubClassOf(Class("C"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("C"), Individual("c"))), true, ElPath.Decided),

            //B8 — model Δ = {a, w, c}, r = {(w, a)}, s = {(c, w)}, C = {c}: two generator roles compose
            //through the complex-filler naming walk.
            ("B8_NestedGenerators", Module(
                SubClassOf(Class("A"), SomeInverse("r", SomeInverse("s", Class("C")))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //B9 — model x ∈ P, y ∈ Q, r = {(y, x), (x, y)}. The module carries no position-distinguishing
            //machinery, so the fold-safety fence clears and the cross-owner fold is accepted: the fast-path
            //decides it consistent rather than abstaining through MintOwnerByNode.
            ("B9_MutualRecursionFoldDecided", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //B10 — model A = {a}, C = {c}, X = {x}, B = {b}, r = {(c, a), (x, b)}: r is uncoupled for X's
            //forward existential (shared filler B) while g mints for A's inverse existential; the shared
            //filler is never mirrored, so no reverse flow exists off it.
            ("B10_SharedFillerCoexists", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(Class("X"), Some("r", Class("B"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("X"), Individual("x"))), true, ElPath.Decided),

            //B11 — the witness edge (w, a) mirrors to (a, w) via the symmetric self-pairing, so a gains an
            //r-successor w in B; ∃r.B ⊑ Y fires on a and Y ⊑ ⊥ condemns it.
            ("B11_SymmetricComposition", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Some("r", Class("B")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //B12a — model r = {(b, a)}: b's forward successor demand is satisfied by a itself. r is not a
            //pairing key, so B's forward existential rides the shared filler and must not abstain spuriously.
            ("B12a_MixedCycleNoPairing", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //B12b — same model plus the symmetric closure; exercises the g ↔ r mutual-minting regime (r
            //coupled via the symmetric key AND its own forward existential). No distinguishing machinery, so
            //the fence clears and the fold is accepted: decided consistent.
            ("B12b_MixedCycleSymmetric", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //B13 — any r-predecessor of a forces a into range(r) = E ⊑ ⊥; the ⊤-cored witness keeps distinct
            //owners distinct through inherited demands.
            ("B13_TopFiller", Module(
                SubClassOf(Class("A"), SomeInverse("r", ThingReference)),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //A6 — witnesses r(w1, a) and r(w2, w1); the r⁻ ⊑ s pairing mirrors them to s(a, w1) and
            //s(w1, w2); Transitive(s) composes s(a, w2); w2 ∈ K, so ∃s.K ⊑ Y puts a ∈ Y ⊑ ⊥. r carries no
            //chain in its own upward closure (r⁻ ⊑ s pairs the inverse, not r), so the fence stays clear and
            //the composition on the mirror-target role s is decided.
            ("A6_MirrorTargetTransitiveClash", Module(
                InverseSubProperty("r", "s"),
                Transitive("s"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(Some("s", Class("K")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //A7 — A6 minus Y ⊑ ⊥: model Δ = {a, w1, w2}, r = {(w1, a), (w2, w1)},
            //s = {(a, w1), (w1, w2), (a, w2)}, Y = {a, w1}. The composed s-path holds but nothing is
            //emptied, so the module is consistent.
            ("A7_MirrorTargetTransitiveControl", Module(
                InverseSubProperty("r", "s"),
                Transitive("s"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(Some("s", Class("K")), Class("Y")),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //A8 — p(x, α ∈ A); α's witness w ∈ B gives the mirror edge s(α, w) over r⁻ ⊑ s; the chain
            //q = p ∘ s composes q(x, w); w ∈ B, so ∃q.B ⊑ Y puts x ∈ Y ⊑ ⊥. The chain sits on p and s, not
            //in r's upward closure, so the fence stays clear.
            ("A8_ChainViaMirrorTargetSharedFillerClash", Module(
                InverseSubProperty("r", "s"),
                Chain("q", "p", "s"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("X"), Some("p", Class("A"))),
                SubClassOf(Some("q", Class("B")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("X"), Individual("x"))), false, ElPath.Decided),

            //A9 — A8 minus Y ⊑ ⊥: model Δ = {x, α, w}, p = {(x, α)}, r = {(w, α)}, s = {(α, w)},
            //q = {(x, w)}, Y = {x}. The chain fires but nothing is emptied.
            ("A9_ChainViaMirrorTargetSharedFillerControl", Module(
                InverseSubProperty("r", "s"),
                Chain("q", "p", "s"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("X"), Some("p", Class("A"))),
                SubClassOf(Some("q", Class("B")), Class("Y")),
                ClassAssertion(Class("X"), Individual("x"))), true, ElPath.Decided),

            //A21 — A1 is genuinely unsatisfiable but uninhabited; a2 gains Y through its OWN composed s-path
            //(a2's witnesses mirror to s-edges that Transitive(s) composes) and a2 ∉ A1, so Y ⊓ A1 ⊑ ⊥ never
            //fires on a2. A cross-owner fold of the A1 and a2 witnesses would falsely condemn a2;
            //owner-demand inheritance keeps them distinct.
            ("A21_MirrorTargetTransitiveOwnerSeparation", Module(
                InverseSubProperty("r", "s"),
                Transitive("s"),
                SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(Some("s", Class("K")), Class("Y")),
                SubClassOf(new OwlObjectIntersectionOf([Class("Y"), Class("A1")]), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true, ElPath.Decided),

            //A22 — A6's composed s(a, w2) with w2 ∈ K puts a ∈ Y; a ∈ A too, so Y ⊓ A ⊑ ⊥ empties a. The
            //clash routes through a conjunction with the owner's own type rather than a bare Y ⊑ ⊥.
            ("A22_MirrorTargetTransitiveConjunctionClash", Module(
                InverseSubProperty("r", "s"),
                Transitive("s"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(Some("s", Class("K")), Class("Y")),
                SubClassOf(new OwlObjectIntersectionOf([Class("Y"), Class("A")]), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //P13b — a's r-predecessor witness w lies in B, and B ⊑ ⊥ empties it, so ⊥ back-propagates over
            //the g-edge to condemn a. No chain or self on r, so the fence stays clear and the bottom filler
            //is decided.
            ("P13b_UnfencedBackwardBottomControl", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),
        ];

    /// <summary>The backward-minting-soundness battery: every <see cref="BackwardMintingCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void BackwardMintingSoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = BackwardMintingCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Adversarial ground-characteristic soundness battery for the asymmetric/irreflexive tier. Each
    /// module's TRUE consistency is established by an explicit hand-built model (consistent) or an explicit
    /// unsat derivation (inconsistent), independent of the characteristic-blind tableau — the oracle names
    /// both characteristics as uninterpreted and cannot witness these gains. The FINAL coupled verdict (EL
    /// fast-path, or its delegation to the fallback) must match that ground truth, and each case carries its
    /// expected decision path (decided by the fast-path, or delegated) so a silent tier drift fails even when
    /// the verdict happens to agree. A mismatch on an inconsistent decided case is the headline
    /// capability/soundness hole; a mismatch on a consistent decided case is a false inconsistency; a delegated
    /// case whose fallback verdict differs from its stated TRUE consistency is a fragment-relative miss the pin
    /// records.
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] GroundCharacteristicCases() =>
        [
            //C1 — Asymmetric(r) requires r(x, y) -> not r(y, x); the asserted r(a, b) and r(b, a) are a
            //direct reverse pair, so no model interprets both. Inconsistent.
            ("C1_DirectPair", Module(
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "a")), false, ElPath.Decided),

            //C2 — Irreflexive(r) requires not r(x, x); the asserted self-edge r(a, a) violates it. Inconsistent.
            ("C2_IrreflexiveSelfEdge", Module(
                Irreflexive("r"),
                Edge("a", "r", "a")), false, ElPath.Decided),

            //C3 — Asymmetric(r) with x = y = a reads r(a, a) -> not r(a, a), so the asserted self-edge forces
            //its own negation (asymmetry implies irreflexivity). Inconsistent.
            ("C3_AsymmetricSelfEdge", Module(
                Asymmetric("r"),
                Edge("a", "r", "a")), false, ElPath.Decided),

            //C4 — s1 ⊑ r and s2 ⊑ r make s1(a, b) an r-edge (a, b) and s2(b, a) an r-edge (b, a); the
            //asymmetric r then bears the reverse pair. Inconsistent.
            ("C4_PairViaTwoSubRoles", Module(
                SubProperty("s1", "r"),
                SubProperty("s2", "r"),
                Asymmetric("r"),
                Edge("a", "s1", "b"),
                Edge("b", "s2", "a")), false, ElPath.Decided),

            //C5 — q ≡ r makes q(b, a) an r-edge (b, a); with the asserted r(a, b) the asymmetric r bears the
            //reverse pair. Inconsistent.
            ("C5_PairViaEquivalentProperty", Module(
                EquivalentProperties("q", "r"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "q", "a")), false, ElPath.Decided),

            //C6 — SameIndividual(a, b) merges a and b to one node, so the asserted r(a, b) is the self-edge
            //r(a, a); the irreflexive r forbids it. Inconsistent.
            ("C6_MergeCreatedSelfEdge", Module(
                SameIndividual(Individual("a"), Individual("b")),
                Edge("a", "r", "b"),
                Irreflexive("r")), false, ElPath.Decided),

            //C7 — Functional(f) with f(x, a) and f(x, b) forces a = b; then r(a, c) and r(c, b) = r(c, a) are
            //the reverse pair (a, c), (c, a) over the asymmetric r. Inconsistent.
            ("C7_FunctionalCollapsePair", Module(
                Functional("f"),
                Edge("x", "f", "a"),
                Edge("x", "f", "b"),
                Asymmetric("r"),
                Edge("a", "r", "c"),
                Edge("c", "r", "b")), false, ElPath.Decided),

            //C8 — the class assertion x : ObjectHasValue(r, a) is the asserted edge r(x, a); with the asserted
            //r(a, x) the asymmetric r bears the reverse pair (x, a), (a, x). Inconsistent.
            ("C8_HasValueCreatedPair", Module(
                ClassAssertion(HasValue("r", "a"), Individual("x")),
                Edge("a", "r", "x"),
                Asymmetric("r")), false, ElPath.Decided),

            //C9 — the bare nominal x : {a} folds x = a; the asserted r(x, a) is then the self-edge r(a, a),
            //which the irreflexive r forbids. Inconsistent.
            ("C9_BareNominalFoldSelfEdge", Module(
                ClassAssertion(OneOf("a"), Individual("x")),
                Edge("x", "r", "a"),
                Irreflexive("r")), false, ElPath.Decided),

            //C10 — TBox only, no ABox: the non-empty domain has some x; Reflexive(r) forces r(x, x), which the
            //irreflexive r forbids. Inconsistent (the told reflexive x irreflexive clash).
            ("C10_ToldReflexiveIrreflexive", Module(
                Reflexive("r"),
                Irreflexive("r")), false, ElPath.Decided),

            //C11 — Reflexive(s) forces s(x, x) on the non-empty domain; s ⊑ r promotes it to r(x, x), which the
            //asymmetric r forbids (a self-edge is its own reverse). Inconsistent.
            ("C11_ReflexiveSubRoleOfAsymmetricSuper", Module(
                Reflexive("s"),
                SubProperty("s", "r"),
                Asymmetric("r")), false, ElPath.Decided),

            //C12 — the superclass demand Top ⊑ ∃r.Self forces r(x, x) on every element of the non-empty
            //domain, which the irreflexive r forbids. Inconsistent.
            ("C12_TopSelfIrreflexive", Module(
                TopSubClassOfHasSelf("r"),
                Irreflexive("r")), false, ElPath.Decided),

            //C13 — the asserted r(a, b), r(b, a) reverse pair contradicts the asymmetric r directly; the scan
            //decides it before the existential (an unsafe edge-generating role) reaches the gate — scan beats
            //gate. Inconsistent.
            ("C13_ScanBeatsGate", Module(
                Asymmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                Edge("a", "r", "b"),
                Edge("b", "r", "a")), false, ElPath.Decided),

            //C14 — model D = {a, b, c}, r = {(a, b), (b, c)}: a one-directional chain has no reverse pair and
            //no self-edge, so the asymmetric r is satisfied. Consistent.
            ("C14_OneDirectionalChain", Module(
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c")), true, ElPath.Decided),

            //C15 — model D = {a, b}, r = {(a, b), (b, a)}: no self-edge, so the irreflexive r is satisfied — a
            //reverse pair is irreflexive-legal, only self-edges violate. Consistent.
            ("C15_IrreflexiveNonSelfEdges", Module(
                Irreflexive("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "a")), true, ElPath.Decided),

            //C16 — model D = {a, b}, r = {(a, b)}, q = {(b, a)}: the reverse edge lies on the unconstrained q,
            //so the asymmetric r keeps its single one-directional edge. Consistent.
            ("C16_ReverseEdgeOnUnrelatedRole", Module(
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "q", "a")), true, ElPath.Decided),

            //C17 — model D = {a, b}, s = {}, r = {(a, b), (b, a)}: the asymmetric s bears no edge (vacuously
            //satisfied), and the super-role r edges do not violate a sub-role constraint. Consistent.
            ("C17_HierarchyDirection", Module(
                Asymmetric("s"),
                SubProperty("s", "r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "a")), true, ElPath.Decided),

            //C18 — model D = {x}, r = {(x, x)}, s = {}: Reflexive(r) is satisfied and Irreflexive(s) is
            //vacuous — the constraints sit on unrelated roles. Consistent.
            ("C18_ReflexiveIrreflexiveUnrelated", Module(
                Reflexive("r"),
                Irreflexive("s")), true, ElPath.Decided),

            //C19 — model D = {x}, r = {(x, x)}, s = {}: Reflexive(r) forces self-edges on r only, not on its
            //sub-role s, so the asymmetric s stays vacuously satisfied. Consistent (the reflexive super of an
            //asymmetric sub is legal).
            ("C19_ReflexiveSuperOfAsymmetricSub", Module(
                Reflexive("r"),
                SubProperty("s", "r"),
                Asymmetric("s")), true, ElPath.Decided),

            //C20 — model D = {a, b}, A = {}, s = {(a, b)}, r = {(a, b)}: the existential is on the SUPER-role r
            //(uninhabited A fires nothing), so the asymmetric sub-role s is not edge-generating and its single
            //edge has no reverse. Consistent (gate precision).
            ("C20_GatePrecisionExistentialOnSuper", Module(
                Asymmetric("s"),
                SubProperty("s", "r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                Edge("a", "s", "b")), true, ElPath.Decided),

            //C21 — model D = {a, b}, r = {(a, b)}: the asserted edge and the ObjectHasValue(r, b) on a denote
            //the same ordered edge (a, b), and an ordered-pair set absorbs the duplicate — no reverse.
            //Consistent.
            ("C21_DuplicateEdge", Module(
                Asymmetric("r"),
                Edge("a", "r", "b"),
                ClassAssertion(HasValue("r", "b"), Individual("a"))), true, ElPath.Decided),

            //C22 — model D = {a, b}, r = {(a, b)}, A = {}: the asymmetric r bears a positive-position
            //existential (A ⊑ ∃r.B), so a saturation edge the asserted-edge scan cannot see could arise; the
            //gate delegates. The single asserted edge has no reverse, so the module is consistent.
            ("C22_ExistentialOnConstrainedRole", Module(
                Asymmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                Edge("a", "r", "b")), true, ElPath.Delegated),

            //C23 — Symmetric(r) + Asymmetric(r), no edges: r is symmetric-in-effect and asymmetric, so the
            //forced-empty rewrite decides r empty (its characteristics reduce to ∃r.⊤ ⊑ ⊥). Model Δ = {x},
            //r = {}: nothing populates r, so ∃r.⊤ ⊑ ⊥ never fires. Consistent decided.
            ("C23_SymmetricPlusAsymmetric", Module(
                Symmetric("r"),
                Asymmetric("r")), true, ElPath.Decided),

            //C24 — InverseObjectProperties(r, s) makes r a mirror target, so the mirror could add a reverse
            //edge the scan cannot see; the gate delegates. Model r = s = {} makes it consistent.
            ("C24_InversePairedAsymmetric", Module(
                Inverse("r", "s"),
                Asymmetric("r")), true, ElPath.Delegated),

            //C25 — Transitive(r) lands the chain r ∘ r ⊑ r, so r is edge-generating and could compose an edge
            //the scan cannot see; the gate delegates. Model r = {} makes it consistent.
            ("C25_TransitiveAsymmetric", Module(
                Transitive("r"),
                Asymmetric("r")), true, ElPath.Delegated),

            //C26 — the class-level self demand B ⊑ ∃r.Self makes r edge-generating (a self-edge on inhabited
            //B), which the gate delegates (it needs inhabitation, not the told-reflexivity decision). Model
            //B = {} forces no self-edge, so the module is consistent.
            ("C26_ClassLevelSelfDemand", Module(
                SubClassOf(Class("B"), HasSelf("r")),
                Irreflexive("r")), true, ElPath.Delegated),

            //C27 — the inverse spelling Asymmetric(r⁻) ≡ Asymmetric(r) is admitted; r is
            //asymmetric-constrained with no symmetric pairing and no edges, so it is not forced empty and no
            //asserted edge clashes. Model Δ = {a, b}, r = {(a, b)}: a single one-directional edge has no
            //reverse. Consistent decided.
            ("C27_InverseSpelling", Module(
                AsymmetricInverse("r")), true, ElPath.Decided),
        ];

    /// <summary>The ground-characteristic soundness battery: every <see cref="GroundCharacteristicCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void GroundCharacteristicSoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = GroundCharacteristicCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Role-forced-empty inference soundness battery. A role that is both symmetric-in-effect (self-paired in
    /// the inverse index) and asymmetric-constrained (itself, or under an asymmetric super-role) is EMPTY in
    /// every model, so its characteristics reduce to <c>∃r.⊤ ⊑ ⊥</c> and the module is DECIDED: inconsistent
    /// for anything that populates the role, consistent otherwise. Each module's TRUE consistency is an
    /// explicit hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent of
    /// the characteristic-blind tableau which drops both characteristics. Each case carries its expected
    /// decision path so a lost decision (a forced-empty module silently delegated) or a spurious clash fails
    /// even when the verdict happens to agree.
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] RoleForcedEmptyCases() =>
        [
            //FE1 — Symmetric(r) makes r(a, b) force r(b, a); with Asymmetric(r) the reverse pair has no model.
            //Equivalently r is forced empty, so the seeded ∃r.⊤ ⊑ ⊥ condemns a, the source of the asserted
            //r(a, b). Inconsistent.
            ("FE1_SymAsymAssertedEdge", Module(
                Symmetric("r"),
                Asymmetric("r"),
                Edge("a", "r", "b")), false, ElPath.Decided),

            //FE2 — c : A and A ⊑ ∃r.B force an r-edge out of c over the forced-empty r, so ∃r.⊤ ⊑ ⊥ condemns c.
            //Inconsistent.
            ("FE2_SymAsymExistentialInhabited", Module(
                Symmetric("r"),
                Asymmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                ClassAssertion(Class("A"), Individual("c"))), false, ElPath.Decided),

            //FE3 — Symmetric(s), s ⊑ r, Asymmetric(r): the sub-role s is forced empty (an s-edge promotes to
            //the asymmetric r and its symmetric reverse violates asymmetry), so the asserted s(a, b) fires
            //∃s.⊤ ⊑ ⊥ on a. Inconsistent.
            ("FE3_SubRoleForcedEmptyAssertedEdge", Module(
                Symmetric("s"),
                SubProperty("s", "r"),
                Asymmetric("r"),
                Edge("a", "s", "b")), false, ElPath.Decided),

            //FE4 — Reflexive(r) self-edges every node over the forced-empty r; the self-edge on ⊤ (the sole
            //non-empty-domain witness with no ABox) fires ∃r.⊤ ⊑ ⊥, giving ⊤ ⊑ ⊥. Inconsistent. The
            //told-check handoff: r leaves AsymmetricRoles under the rewrite, so the saturation self-edge — not
            //TryDecideToldReflexivityClash — carries the verdict.
            ("FE4_SymAsymReflexiveToldHandoff", Module(
                Symmetric("r"),
                Asymmetric("r"),
                Reflexive("r")), false, ElPath.Decided),

            //FE5 — Asymmetric(r⁻) ≡ Asymmetric(r); with Symmetric(r) self-pairing r, r is forced empty and the
            //asserted r(a, b) fires ∃r.⊤ ⊑ ⊥ on a. Inconsistent (C27 × C23 composition).
            ("FE5_AsymInverseTimesSymmetric", Module(
                AsymmetricInverse("r"),
                Symmetric("r"),
                Edge("a", "r", "b")), false, ElPath.Decided),

            //FE6 — InverseObjectProperties(r, r) self-pairs r; with Asymmetric(r), r is forced empty and
            //r(a, b) condemns a. Inconsistent (self-pairing via InverseObjectProperties).
            ("FE6_InversePairSelfAsym", Module(
                Inverse("r", "r"),
                Asymmetric("r"),
                Edge("a", "r", "b")), false, ElPath.Decided),

            //FE7 — r⁻ ⊑ r self-pairs r; with Asymmetric(r), r is forced empty and r(a, b) condemns a.
            //Inconsistent (self-pairing via the inverse sub-property).
            ("FE7_InverseSubSelfAsym", Module(
                InverseSubProperty("r", "r"),
                Asymmetric("r"),
                Edge("a", "r", "b")), false, ElPath.Decided),

            //FE8 — c : ObjectHasValue(r, a) is the asserted edge r(c, a); over the forced-empty r it fires
            //∃r.⊤ ⊑ ⊥ on c. Inconsistent (HasValue-created edge).
            ("FE8_SymAsymHasValueEdge", Module(
                Symmetric("r"),
                Asymmetric("r"),
                ClassAssertion(HasValue("r", "a"), Individual("c"))), false, ElPath.Decided),

            //FE9 — model Δ = {x}, r = ∅, every class empty: nothing populates the forced-empty r, so ∃r.⊤ ⊑ ⊥
            //never fires. Consistent.
            ("FE9_SymAsymNoEdges", Module(
                Symmetric("r"),
                Asymmetric("r")), true, ElPath.Decided),

            //FE10 — model Δ = {x}, A = ∅, B = ∅, r = ∅: A ⊑ ∃r.B forces A into the empty role so A ⊑ ⊥, but no
            //individual is A, so ⊤ stays inhabited. Consistent (A merely unsatisfiable; entailments in 5.3).
            ("FE10_SymAsymExistentialNoInhabitant", Module(
                Symmetric("r"),
                Asymmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B")))), true, ElPath.Decided),

            //FE11 — model Δ = {a, b}, s = ∅, r = {(a, b), (b, a)}: symmetry sits on the SUPER-role r (r is
            //self-paired but Up(r) ∩ AsymmetricRoles = ∅, so no rewrite fires), the asymmetric sub-role s
            //bears no edge and is vacuously satisfied, and the super-role's symmetric reverse pair does not
            //violate a sub-role constraint. Consistent. The DIRECTION pin.
            ("FE11_SymmetricSuperAsymmetricSub", Module(
                Symmetric("r"),
                Asymmetric("s"),
                SubProperty("s", "r"),
                Edge("a", "r", "b")), true, ElPath.Decided),

            //FE12 — model Δ = {x}, s = ∅, r = ∅: Symmetric(s), s ⊑ r, Asymmetric(r) force s empty (the super
            //keeps its constraint), and nothing populates s or r. Consistent. The MirrorTargets-rebuild pin:
            //without the rebuild the stale self-pairing on s makes the asymmetric super r a mirror target and
            //the module drifts to delegation.
            ("FE12_SubRoleForcedEmptyNoEdges", Module(
                Symmetric("s"),
                SubProperty("s", "r"),
                Asymmetric("r")), true, ElPath.Decided),

            //FE13 — the super-role r keeps its asymmetric constraint after s is forced empty; the asserted
            //r(c, d), r(d, c) reverse pair over r fires the asymmetric scan → d ⊑ ⊥. Inconsistent. The
            //over-removal pin: removing r (not only s) from AsymmetricRoles would lose this clash.
            ("FE13_SuperRoleKeepsConstraintReversePair", Module(
                Symmetric("s"),
                SubProperty("s", "r"),
                Asymmetric("r"),
                Edge("c", "r", "d"),
                Edge("d", "r", "c")), false, ElPath.Decided),

            //FE14 — model Δ = {a, b}, r = {(a, b), (b, a)}: Irreflexive(r) does NOT force r empty (a reverse
            //pair is irreflexive-legal, only self-edges violate), so no rewrite fires; r is a symmetric mirror
            //target, so the gate delegates. No self-edge, so Irreflexive(r) is satisfied. Consistent.
            ("FE14_SymmetricIrreflexiveDelegated", Module(
                Symmetric("r"),
                Irreflexive("r"),
                Edge("a", "r", "b")), true, ElPath.Delegated),

            //FE15 — model all roles empty: Inverse(s, q), q ⊑ s make s symmetric-in-effect but WITHOUT a
            //self-pairing (InversePairs[s] = [q] ≠ [s]), so the rewrite does not fire and the mirror-target
            //gate delegates. Nothing populates s or q, so Asymmetric(s) is vacuously satisfied — the delegated
            //fragment-relative verdict is genuinely correct. Consistent (residual pin).
            ("FE15_CompoundSymmetricNoSelfPairing", Module(
                Inverse("s", "q"),
                SubProperty("q", "s"),
                Asymmetric("s")), true, ElPath.Delegated),

            //FE16 — s is forced empty (Symmetric(s) + Asymmetric(s)); the Inverse(s, q) pairing survives, so
            //c : A forces a per-owner q-witness whose mirror seeds an s-edge firing ∃s.⊤ ⊑ ⊥ on the witness,
            //emptying it and back-propagating ⊥ over the per-owner q-edge to c. Inconsistent (minting
            //composition; a drift to Delegated fails the row).
            ("FE16_MintingCompositionInhabited", Module(
                Symmetric("s"),
                Asymmetric("s"),
                Inverse("s", "q"),
                SubClassOf(Class("A"), Some("q", Class("B"))),
                ClassAssertion(Class("A"), Individual("c"))), false, ElPath.Decided),

            //FE17 — model Δ = {x}, A = ∅, q = ∅, s = ∅: the same forced-empty s and surviving q-pairing as
            //FE16, but no individual is A; A ⊑ ⊥ leaves A unsatisfiable while ⊤ stays inhabited. Consistent
            //(the A ⊑ ⊥ / not-B ⊑ ⊥ entailments are pinned in the 5.3 headliner; a drift to Delegated fails
            //the row).
            ("FE17_MintingCompositionNoInhabitant", Module(
                Symmetric("s"),
                Asymmetric("s"),
                Inverse("s", "q"),
                SubClassOf(Class("A"), Some("q", Class("B")))), true, ElPath.Decided),
        ];

    /// <summary>The role-forced-empty soundness battery: every <see cref="RoleForcedEmptyCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void RoleForcedEmptySoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = RoleForcedEmptyCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Inverse characteristic spelling soundness battery. Each of the six ground characteristics on an
    /// inverse role expression is exactly a forward characteristic (<c>Asymmetric(r⁻) ≡ Asymmetric(r)</c>,
    /// <c>Irreflexive(r⁻) ≡ Irreflexive(r)</c>, <c>Symmetric(r⁻) ≡ Symmetric(r)</c>,
    /// <c>Reflexive(r⁻) ≡ Reflexive(r)</c>, and the functional pair swapping —
    /// <c>Functional(r⁻) ≡ InverseFunctional(r)</c>, <c>InverseFunctional(r⁻) ≡ Functional(r)</c>), so each
    /// admits and decides identically to its forward equivalent. Each module's TRUE consistency is an
    /// explicit hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent of
    /// the spelling-blind tableau. Each case carries its expected decision path.
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] InverseCharacteristicSpellingCases() =>
        [
            //IS1 — Asymmetric(r⁻) ≡ Asymmetric(r) requires r(x, y) → ¬r(y, x); the asserted r(a, b), r(b, a)
            //reverse pair has no model. Inconsistent.
            ("IS1_AsymmetricInverseReversePair", Module(
                AsymmetricInverse("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "a")), false, ElPath.Decided),

            //IS2 — model Δ = {a, b, c}, r = {(a, b), (b, c)}: a one-directional chain has no reverse pair and
            //no self-edge, so Asymmetric(r⁻) ≡ Asymmetric(r) is satisfied. Consistent decided.
            ("IS2_AsymmetricInverseOneDirectional", Module(
                AsymmetricInverse("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c")), true, ElPath.Decided),

            //IS3 — Irreflexive(r⁻) ≡ Irreflexive(r) forbids r(x, x); the asserted self-edge r(a, a) violates
            //it. Inconsistent.
            ("IS3_IrreflexiveInverseSelfEdge", Module(
                IrreflexiveInverse("r"),
                Edge("a", "r", "a")), false, ElPath.Decided),

            //IS4 — Reflexive(r⁻) ≡ Reflexive(r) forces r(x, x) on the non-empty domain, which Irreflexive(r)
            //forbids; the told reflexive × irreflexive clash decides ⊤ ⊑ ⊥ through the spelling. Inconsistent,
            //TBox-only.
            ("IS4_ReflexiveInverseIrreflexive", Module(
                ReflexiveInverse("r"),
                Irreflexive("r")), false, ElPath.Decided),

            //IS5 — Symmetric(r⁻) ≡ Symmetric(r) self-pairs r; with Asymmetric(r), r is forced empty and the
            //asserted r(a, b) fires ∃r.⊤ ⊑ ⊥ on a. Inconsistent (C27 spelled symmetric feeds C23).
            ("IS5_SymmetricInverseAsymmetric", Module(
                SymmetricInverse("r"),
                Asymmetric("r"),
                Edge("a", "r", "b")), false, ElPath.Decided),

            //IS6 — Functional(r⁻) ≡ InverseFunctional(r): x's two r-predecessors a, b collapse to one element,
            //contradicting DifferentIndividuals(a, b). Inconsistent — the SWAP row (predecessor collapse) and
            //the SeedFunctionalMerges pin. Plain Functional(r) would collapse SUCCESSORS and never merge a, b,
            //so a wrong swap goes false-consistent here.
            ("IS6_FunctionalInversePredecessorCollapse", Module(
                FunctionalInverse("r"),
                Edge("a", "r", "x"),
                Edge("b", "r", "x"),
                Different("a", "b")), false, ElPath.Decided),

            //IS7 — model Δ = {a, x}, a = b, r = {(a, x)}: Functional(r⁻) collapses the two r-predecessors of x
            //to one element, and with no distinctness the merge is consistent. Consistent decided.
            ("IS7_FunctionalInverseMergeNoDistinctness", Module(
                FunctionalInverse("r"),
                Edge("a", "r", "x"),
                Edge("b", "r", "x")), true, ElPath.Decided),

            //IS8 — InverseFunctional(r⁻) ≡ Functional(r): x's two r-successors a, b collapse to one element,
            //contradicting DifferentIndividuals(a, b). Inconsistent (successor collapse).
            ("IS8_InverseFunctionalInverseSuccessorCollapse", Module(
                InverseFunctionalInverse("r"),
                Edge("x", "r", "a"),
                Edge("x", "r", "b"),
                Different("a", "b")), false, ElPath.Decided),

            //IS9 — model Δ = {c, w}, A = {c}, B = {w}, r = {(c, w)}: Functional(r⁻) ≡ InverseFunctional(r) is
            //an unsafe ground role once A ⊑ ∃r.B makes r edge-generating (the existential successor the
            //pre-merge predecessor scan cannot account for), so the gate delegates exactly as it would for
            //InverseFunctional(r). Each element has one r-predecessor, so the model satisfies it. Consistent
            //(unsafe ground role honesty).
            ("IS9_FunctionalInverseExistentialDelegated", Module(
                FunctionalInverse("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                ClassAssertion(Class("A"), Individual("c"))), true, ElPath.Delegated),

            //IS10 — model Δ = {c}, r = {(c, c)}, A = {c}, B = {c}: Reflexive(r⁻) ≡ Reflexive(r) self-edges
            //every node, and ∃r.Self ⊑ B then types every node B, so c gets B. No ⊥, so consistent decided;
            //the reflexive machinery fires through the spelling (without C27 the module would delegate).
            ("IS10_ReflexiveInverseSelfElimination", Module(
                ReflexiveInverse("r"),
                SubClassOf(HasSelf("r"), Class("B")),
                ClassAssertion(Class("A"), Individual("c"))), true, ElPath.Decided),
        ];

    /// <summary>The inverse-characteristic-spelling battery: every <see cref="InverseCharacteristicSpellingCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void InverseCharacteristicSpellingBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = InverseCharacteristicSpellingCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Fold-safety fence soundness battery. Each module's TRUE consistency is established by an explicit
    /// hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent of the
    /// inverse-blind tableau. A module the fold-safety fence CLEARS accepts a cross-owner witness fold and
    /// is decided by the fast-path; a module the fence FAILS keeps the unique-ownership abstention and
    /// delegates. The FINAL coupled verdict must match the ground truth, and each case carries its expected
    /// decision path so a false fold (a spurious inconsistency on a ladder module) or a lost decision (a
    /// fold-safe cycle silently delegated) fails even when the verdict happens to agree.
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] FoldSafeCycleCases() =>
        [
            //FS1 — B9 verbatim. Model Δ = {p, q}, P = {p}, Q = {q}, r = {(q, p), (p, q)}: p's r-predecessor
            //q lies in Q, q's r-predecessor p lies in P. No distinguishing machinery, so the fence clears and
            //the cross-owner fold is accepted.
            ("FS1_B9Verbatim", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS2 — B12b verbatim. Model Δ = {a, b}, A = {a}, B = {b}, r = {(b, a), (a, b)} (symmetric): a's
            //r-predecessor b is in B, b's r-successor a is in A. Fence clears.
            ("FS2_B12bVerbatim", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //FS3 — period-3 cycle. Model Δ = {p, q, s}, P = {p}, Q = {q}, S = {s}, r = {(q, p), (s, q), (p, s)}:
            //each element's r-predecessor lies in the next core round the 3-cycle. Fence clears.
            ("FS3_PeriodThree", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("S"))),
                SubClassOf(Class("S"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS4 — B9 + Q ⊑ ⊥. Derivation: p ∈ P forces an r-predecessor in Q; Q = ∅ leaves none, so the
            //Q-cored witness is ⊥ and ⊥ back-propagates over its generator edge to condemn p. Inconsistent.
            ("FS4_B9EmptyPredecessorCore", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Class("Q"), NothingReference),
                ClassAssertion(Class("P"), Individual("p"))), false, ElPath.Decided),

            //FS5 — symmetric forward 2-cycle. Model Δ = {a, b}, A = {a}, B = {b}, r = {(a, b), (b, a)}: a's
            //r-successor b is in B, b's r-successor a is in A. Fence clears (forward existentials only).
            ("FS5_SymmetricForwardCycle", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //FS6 — B9 + p : T + the class-typed parity ladder over the mirror role r + F3 ⊓ G3 ⊑ ⊥. Model:
            //the infinite chain x0 = p (∈ P, ∈ T), x1 (∈ Q), x2 (∈ P), … with r = {(x_{i+1}, x_i)}. T is
            //root-only (only x0), so ∃r.T ⊑ F1 gives F1 = {x1}, then F2 = {x2}, F3 = {x3}, G2 = {x4},
            //G3 = {x5}: F3 and G3 hold at disjoint depths and the conjunction never fires — consistent. Each
            //ladder deposit travels from a witness to its owner, so it is consumed into the witness's intern
            //key: the positions carrying F3 and G3 stay key-distinct while the ladder deposits, and folding
            //resumes only once the decorations saturate, past the last rung.
            ("FS6_ParityLadderOverMirrorRole", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r", Class("T")), Class("F1")),
                SubClassOf(Some("r", Class("F1")), Class("F2")),
                SubClassOf(Some("r", Class("F2")), Class("F3")),
                SubClassOf(Some("r", Class("F3")), Class("G2")),
                SubClassOf(Some("r", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //FS7 — B9 + a bare left existential ∃r.X ⊑ Y over the mirror role. Model: the B9 chain with
            //X = ∅, so Y is never derived and nothing clashes — consistent. X is never derived, so the
            //backward consumer never fires, no witness is refined, and the module folds exactly as the
            //undecorated cycle does.
            ("FS7_BareMirrorLeftExistential", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Some("r", Class("X")), Class("Y")),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS8 — B12b + Range(r, K). Model Δ = {a, b}, r = {(b, a), (a, b)}, K = {a, b}: every r-target is
            //in K, nothing clashes — consistent. A range writes a fact about an ACTUAL edge, but it writes
            //the same fact at every position of one content class: the mirror-range reduction turns it into
            //an owner-independent left existential on the paired role, and the per-edge rule types the
            //shared witness every owner of the (role, filler) pair reaches. No chain or self feature
            //touches the witness closure, so the module mints on shared content keys and decides. FS22 is
            //the one-directional-pairing sibling of this row.
            ("FS8_RangeOverCoupledRole", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                Range("r", Class("K")),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //FS9 — B9 + K ⊑ {p} (a told class-to-nominal inclusion). Model: the B9 chain with K = ∅, so the
            //nominal inclusion is vacuous — consistent. A class-space nominal is not a regime question: the
            //witness closure carries no chain or self feature, so the module mints on shared content keys,
            //where a witness denotes one canonical element and the nominal merge pools its constraints onto
            //the real individual only where an inhabited chain forces that element to be it.
            ("FS9_ToldClassToNominal", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Class("K"), OneOf("p")),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS10 — period-3 + S ⊑ ⊥ (a mid-core told ⊥). Derivation: p ∈ P forces an r-predecessor in Q,
            //which forces one in S; S = ∅ leaves none, so the S-cored witness is ⊥ and ⊥ back-propagates up
            //the generator chain to condemn p. Inconsistent.
            ("FS10_PeriodThreeMidCoreBottom", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("S"))),
                SubClassOf(Class("S"), SomeInverse("r", Class("P"))),
                SubClassOf(Class("S"), NothingReference),
                ClassAssertion(Class("P"), Individual("p"))), false, ElPath.Decided),

            //FS11 — B9 + X ⊑ ∃t.C over an uncoupled sidecar role t. Model: the B9 chain plus X = ∅ (nothing
            //forces X), so the sidecar existential is vacuous — consistent. t is not in the witness-reachable
            //closure, so no clause fires and the fence clears.
            ("FS11_UncoupledSidecar", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Class("X"), Some("t", Class("C"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS12 — B9 with a second individual root q : P. Model: two disjoint copies of the B9 chain, one
            //rooted at p and one at q — consistent. Both roots are assertion individuals, not class-space
            //nominals, so F3 stays clear and the fence clears.
            ("FS12_SecondIndividualRoot", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("P"), Individual("q"))), true, ElPath.Decided),

            //FS13 — the mutual EquivalentClasses spelling P ≡ ∃r⁻.Q, Q ≡ ∃r⁻.P, p : P. Model: the B9 chain,
            //with P = {even positions} and Q = {odd positions} satisfying both equivalence directions —
            //consistent. The subclass direction ∃r⁻.Q ⊑ P is a left existential over the synthetic mirror
            //role, which the mint edge reaches only by mirroring twice: its conclusions therefore land on the
            //OWNER, whose own key determines them, so no refinement is owed and the cycle folds. The mirror
            //role bears the only backward consumer and is not itself an inverse-pairing key, so the
            //double-mirrored clause does not fire and the fence clears.
            ("FS13_MutualEquivalentClasses", Module(
                Equivalent(Class("P"), SomeInverse("r", Class("Q"))),
                Equivalent(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS14 — B9 + Transitive(r). Model: the B9 chain closed transitively over r, with no class emptied
            //— consistent. The generator fence admits the forward role's own self-transitivity r ∘ r ⊑ r, the
            //module reaches the mint, and the mutually recursive cross-owner witnesses fold there;
            //Transitive(r) is a chain over the witness closure, so F4 keeps the fold-safety fence closed and
            //the mint-site ownership abstention delegates.
            ("FS14_TransitiveMirrorRole", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                Transitive("r"),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Delegated),

            //FS15 — symmetric forward 2-cycle, no individuals (TBox only). Model Δ = {x, y}, A = {x}, B = {y},
            //r = {(x, y), (y, x)}: A and B are both satisfiable — consistent. Fence clears; the classification
            //side alone exercises the fold.
            ("FS15_SymmetricCycleNoIndividuals", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A")))), true, ElPath.Decided),

            //FS16a — FS5 + A ⊑ ⊥ + a : A. Derivation: a ∈ A and A = ∅ condemn a directly. Inconsistent.
            ("FS16a_ForcedRootInEmptyClass", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                SubClassOf(Class("A"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //FS16b — FS5 + A ⊑ ⊥, no assertions. Model Δ = {z}, z in neither A nor B, r = ∅: nothing is
            //forced non-empty — consistent. A = ∅ (told), and B ⊑ ∃r.A mints a witness in the empty A that
            //back-propagates ⊥ over (r, B, witness) to give B ⊑ ⊥ as a subsumption; with no individual carried
            //into A or B the module stays consistent. Fence clears.
            ("FS16b_BothCoresEmptyNoIndividuals", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                SubClassOf(Class("A"), NothingReference)), true, ElPath.Decided),

            //FS17 — B9 + SubProperty(r, r2) + p : T + the parity ladder over the SUPER-role r2 + F3 ⊓ G3 ⊑ ⊥.
            //Model: the same infinite chain as FS6, with every r-edge promoted to r2 so the ladder reads r2 at
            //the same disjoint depths — consistent. r2 is not itself a mirror target; only the UPWARD closure
            //of the mirror role r reaches it, so the backward-consumer set finds it only through Up() —
            //dropping Up() would leave the ladder deposits unconsumed on the folded witness and flip the
            //verdict to false-inconsistent.
            ("FS17_ParityLadderOverSuperRole", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubProperty("r", "r2"),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r2", Class("T")), Class("F1")),
                SubClassOf(Some("r2", Class("F1")), Class("F2")),
                SubClassOf(Some("r2", Class("F2")), Class("F3")),
                SubClassOf(Some("r2", Class("F3")), Class("G2")),
                SubClassOf(Some("r2", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //FS18 — two independent 2-cycles: a symmetric-forward s-cycle over {a, b} and an inverse-generator
            //r-cycle over {p, q}. Model: the disjoint union of the FS5 and FS1 models — consistent. Neither
            //cycle carries distinguishing machinery, so the fence clears for both roots.
            ("FS18_TwoDisjointCycles", Module(
                Symmetric("s"),
                SubClassOf(Class("A"), Some("s", Class("B"))),
                SubClassOf(Class("B"), Some("s", Class("A"))),
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS19 — symmetric forward 2-cycle + b : B + B ⊑ ⊥. Derivation: b ∈ B and B = ∅ condemn b directly
            //through the genuinely-empty shared core B (the true-positive complement to FS6). Inconsistent.
            ("FS19_ForcedRootInEmptySharedCore", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("B"), Individual("b"))), false, ElPath.Decided),

            //FS20 — symmetric-forward 2-cycle over r, with r ⊑ t, Transitive(t), and ∃t.Self ⊑ D. Model
            //Δ = {x, y}, A = {x}, B = {y}, r = {(x, y), (y, x)}, t ⊇ r closed transitively (adds (x, x), (y, y)),
            //so x, y ∈ D — consistent. t is a transitive super-role of the coupled role r bearing a
            //self-elimination, reached only by the upward closure, so F4 fails and the abstention is kept.
            ("FS20_SelfElimOverTransitiveSuperRole", Module(
                Symmetric("r"),
                SubProperty("r", "t"),
                Transitive("t"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                SubClassOf(HasSelf("t"), Class("D"))), true, ElPath.Delegated),

            //FS21 — told-cycle spelling: EquivalentClasses(P, Q) + Symmetric(r) + P ⊑ ∃r.Q; Q ⊑ ∃r.P; p : P.
            //Model Δ = {p, w}, P = Q = {p, w}, r = {(p, w), (w, p)}: the told equivalence and the forward cycle
            //both hold — consistent. P and Q pack distinct raw intern keys, so witnesses never spuriously fold
            //(raw-key discipline only ever REFUSES a fold), and with no distinguishing machinery the fence
            //clears.
            ("FS21_ToldEquivalentCycle", Module(
                Equivalent(Class("P"), Class("Q")),
                Symmetric("r"),
                SubClassOf(Class("P"), Some("r", Class("Q"))),
                SubClassOf(Class("Q"), Some("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //FS22 — one-directional pairing (r⁻ ⊑ s) forward 2-cycle with a range on the coupled role r
            //itself. Model Δ = {a, b}, A = {a}, B = {b}, K = {a, b}, r = {(a, b), (b, a)}, s ⊇ r⁻ —
            //consistent. The isolator of FS8's geometry: here r is a pairing key and NOT a mirror target
            //(only s is), so the mirror-range reduction adds no left existential and the range reaches the
            //witnesses through the per-edge rule alone. It types every position of the content class
            //alike, so shared content keys carry it without loss and the module decides.
            ("FS22_RangeOnCoupledRoleOneDirectionalPairing", Module(
                InverseSubProperty("r", "s"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                Range("r", Class("K")),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),
        ];

    /// <summary>The fold-safe-cycle soundness battery: every <see cref="FoldSafeCycleCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void FoldSafeCycleSoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = FoldSafeCycleCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Shared-witness soundness battery. A module whose witness-reachable closure carries no chain or self
    /// feature mints every existential over a coupled role on a SHARED CONTENT KEY: one node per
    /// (role, filler) pair, denoting the canonical element of that content class and serving every owner —
    /// class atom or individual — at once. These rows are the ground truth of that sharing, each with an
    /// explicit hand-built model (consistent) or an explicit unsat derivation (inconsistent) independent of
    /// the inverse-blind tableau. They cover the writers that could fabricate a fact at a position that
    /// does not warrant it: the nominal merge in both directions and its liveness gate, a range write
    /// reaching an individual, two owners' backward refinements converging or separating, the identity a
    /// live shared witness genuinely entails, and the bottom arms, where a <c>⊥</c> travelling from a
    /// witness to its own owner is vacuous and every other <c>⊥</c> must still land.
    /// </summary>
    /// <returns>Every case as (name, module, true consistency, expected decision path).</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] SharedWitnessCases() =>
        [
            //SH1 — a live owner and a dead co-owner of one shared witness, the dead branch carrying a fact
            //of its own. Model Δ = {a}, A1 = K = B = {a}, r = s = {(a, a)}, A2 = M = N = ∅: a's r-successor
            //is itself and lies in B, B ⊑ {a} holds, nothing has an s-successor in the empty M so N is
            //empty, and N ⊓ K is empty — consistent. A2 is entailed empty (an A2-element would give a an
            //s-successor in M, hence N, clashing with K), and the row pins that this leaks no further: A2's
            //own ⊥ travels to the shared witness over a mirror edge running witness-to-owner, where it is
            //vacuous, so the live branch and the individual keep their satisfiability. The deposit of N is
            //separated onto A2's own refinement, which no live chain reaches, so the merge onto a never
            //pools it.
            ("SH1_LiveAndDeadCoOwnersOfOneWitness", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), OneOf("a")),
                SubClassOf(Class("A2"), Class("M")),
                SubClassOf(Some("s", Class("M")), Class("N")),
                SubClassOf(new OwlObjectIntersectionOf([Class("N"), Class("K")]), NothingReference),
                ClassAssertion(Class("A1"), Individual("a")),
                ClassAssertion(Class("K"), Individual("a"))), true, ElPath.Decided),

            //SH2 — a range reaching an individual through a live shared witness. Derivation: a ∈ A1 forces
            //an r-successor w cored B; the pairing gives the s-edge (w, a), so range(s) = K types a; a is
            //also Z and K ⊓ Z ⊑ ⊥ condemns it — inconsistent. The write onto the individual is gated on the
            //edge's source being live, which it is: a is an ABox individual, so the witness it mints is
            //inhabited. A2 co-owns that same witness and must survive a's condemnation, which is exactly
            //the ⊥ the witness-to-owner arm suppresses.
            ("SH2_RangeOntoIndividualThroughALiveWitness", Module(
                Inverse("r", "s"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                Range("s", Class("K")),
                SubClassOf(new OwlObjectIntersectionOf([Class("K"), Class("Z")]), NothingReference),
                ClassAssertion(Class("A1"), Individual("a")),
                ClassAssertion(Class("Z"), Individual("a"))), false, ElPath.Decided),

            //SH3 — two owners funnelling two DISTINCT individuals through one shared witness. Model
            //Δ = {c1, c2, a, b}, C1 = {c1}, C2 = {c2}, G = {c1, c2}, F = {a, b},
            //r = {(c1, a), (a, c1), (c2, b), (b, c2)}: a has an r-successor in C1 so a ∈ K1 ⊑ {a}, b has
            //one in C2 so b ∈ K2 ⊑ {b}, both have an r-successor in G so both are H, and a ≠ b holds —
            //consistent. The two owners' branches deposit DIFFERENT decorations, so they refine onto
            //different nodes and the two nominals never meet on one position; a common decoration (the G
            //trigger) converges onto one node, which is the sharing the design delivers. If the two
            //nominals landed together the derived identity would collide with the asserted distinctness.
            ("SH3_TwoOwnersFunnelDistinctIndividualsAndSeparate", Module(
                Symmetric("r"),
                SubClassOf(Class("C1"), Class("G")),
                SubClassOf(Class("C2"), Class("G")),
                SubClassOf(Class("C1"), Some("r", Class("F"))),
                SubClassOf(Class("C2"), Some("r", Class("F"))),
                SubClassOf(Some("r", Class("G")), Class("H")),
                SubClassOf(Some("r", Class("C1")), Class("K1")),
                SubClassOf(Class("K1"), OneOf("a")),
                SubClassOf(Some("r", Class("C2")), Class("K2")),
                SubClassOf(Class("K2"), OneOf("b")),
                Different("a", "b"),
                ClassAssertion(Class("C1"), Individual("c1")),
                ClassAssertion(Class("C2"), Individual("c2"))), true, ElPath.Decided),

            //SH4 — the identity-bearing witness that is never live. Model Δ = {a, b} with a ≠ b and every
            //class empty except ⊤: nothing forces C1 or C2 to have an element, so F is empty and both
            //nominal inclusions on it are vacuous — consistent. The shared witness cored F is told to be
            //BOTH individuals, which would equate them, but no inhabited chain ever reaches it, so the
            //merge onto the real individuals never fires and no identity is discovered. Read the identity
            //off a node the model need not populate and the asserted distinctness would condemn a module
            //that has a model.
            ("SH4_UninhabitedWitnessCarriesTwoNominalsWithoutMerging", Module(
                Symmetric("r"),
                SubClassOf(Class("C1"), Some("r", Class("F"))),
                SubClassOf(Class("C2"), Some("r", Class("F"))),
                SubClassOf(Class("F"), OneOf("a")),
                SubClassOf(Class("F"), OneOf("b")),
                Different("a", "b")), true, ElPath.Decided),

            //SH5 — two individuals reaching one shared witness where the identity IS entailed. Derivation:
            //a and b are both A, so each forces an r-successor; the symmetric role makes each of them an
            //r-TARGET of that successor, so range(r) = N types both, and N ⊑ {c} forces a = c and b = c,
            //hence a = b — which the asserted distinctness forbids, so the module is inconsistent. The
            //engine must DERIVE it: the identity is discovered at saturation on the live individuals and
            //folded into the pre-intern identity set, where the distinctness scan then reads it. Sharing
            //the witness must not lose the derivation.
            ("SH5_RangeToldNominalEntailsTheIdentityAcrossOneWitness", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("F"))),
                Range("r", Class("N")),
                SubClassOf(Class("N"), OneOf("c")),
                Different("a", "b"),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("A"), Individual("b"))), false, ElPath.Decided),

            //SH6 — the individual hub, both branches live. Derivation: p1 ∈ A1 forces a B-element, and
            //B ⊑ {a} makes it a, so a ∈ B ⊑ K; q1 ∈ C forces a D-element, and D ⊑ {a} makes it a, so
            //a ∈ D ⊑ M; M ⊓ K ⊑ ⊥ condemns a — inconsistent. The two witnesses are structurally unrelated
            //(different cores, different roles, disjoint signatures) and meet only at the nominal both are
            //told to be, which is exactly the semantic link that makes pooling both their constraints onto
            //the individual sound.
            ("SH6_IndividualHubBothBranchesLive", Module(
                Inverse("r", "s"),
                Inverse("u", "v"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("C"), Some("u", Class("D"))),
                SubClassOf(Class("B"), OneOf("a")),
                SubClassOf(Class("D"), OneOf("a")),
                SubClassOf(Class("B"), Class("K")),
                SubClassOf(Class("D"), Class("M")),
                SubClassOf(new OwlObjectIntersectionOf([Class("M"), Class("K")]), NothingReference),
                ClassAssertion(Class("A1"), Individual("p1")),
                ClassAssertion(Class("C"), Individual("q1"))), false, ElPath.Decided),

            //SH7 — the same hub with one branch dead. Model Δ = {p1, a}, A1 = {p1}, B = K = {a},
            //r = {(p1, a)}, s = {(a, p1)}, C = D = M = ∅, u = v = ∅ — consistent: nothing forces D to have
            //an element, so a never gains M. The false-inconsistency guard of the hub: the D-cored witness
            //is told to be a but is never inhabited, so its constraints must not be pooled onto the real
            //individual. Ungate that pooling and this module is wrongly condemned, while SH6 — where both
            //branches are genuinely forced — stays inconsistent.
            ("SH7_IndividualHubOneBranchDead", Module(
                Inverse("r", "s"),
                Inverse("u", "v"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("C"), Some("u", Class("D"))),
                SubClassOf(Class("B"), OneOf("a")),
                SubClassOf(Class("D"), OneOf("a")),
                SubClassOf(Class("B"), Class("K")),
                SubClassOf(Class("D"), Class("M")),
                SubClassOf(new OwlObjectIntersectionOf([Class("M"), Class("K")]), NothingReference),
                ClassAssertion(Class("A1"), Individual("p1"))), true, ElPath.Decided),

            //SH8 — the bottom broadcast over MIXED incoming sources. Derivation: Y ⊑ ⊥ empties Y, so
            //Z ⊑ ∃r.Y empties Z, so B ⊑ ∃r.Z empties B, so A2 ⊑ ∃r.B empties A2, and a2 : A2 condemns the
            //module — inconsistent. The ⊥ climbs a chain of shared witnesses, and at the B-cored witness
            //the incoming sources are mixed: the class atom A2 and the individual a2 are ordinary
            //predecessors that MUST receive it, while the Z-cored witness minted from that node must not,
            //its position under an empty owner being vacuous. Suppress by mere ledger membership rather
            //than by the owner actually matching and the whole chain is swallowed, leaving the module
            //wrongly consistent.
            ("SH8_BottomBroadcastOverMixedIncomingSources", Module(
                Symmetric("r"),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("Z"))),
                SubClassOf(Class("Z"), Some("r", Class("Y"))),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), false, ElPath.Decided),

            //SH9 — a mutual mint cycle with a third owner sharing one leg. Model Δ = {p, w1, w2, x},
            //P = {p, w2}, Q = {w1}, R = {x}, r the symmetric closure of
            //{(p, w1), (w1, w2), (x, w1)}: F is the set with an r-successor in P, which is {w1}, and G the
            //set with an r-successor in Q, which is {p, w2, x} — disjoint, so F ⊓ G ⊑ ⊥ never fires and the
            //module is consistent. In the saturation the two cycle witnesses each mint the other, so one
            //edge is at once a forward mint edge and a witness-to-owner mirror; the deposits are refined
            //rather than written plainly, which is the conservative reading, and the third owner keeps the
            //shared leg genuinely multi-owner while it happens.
            ("SH9_MutualMintCycleWithAThirdOwner", Module(
                Symmetric("r"),
                SubClassOf(Class("P"), Some("r", Class("Q"))),
                SubClassOf(Class("Q"), Some("r", Class("P"))),
                SubClassOf(Class("R"), Some("r", Class("Q"))),
                SubClassOf(Some("r", Class("P")), Class("F")),
                SubClassOf(Some("r", Class("Q")), Class("G")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F"), Class("G")]), NothingReference),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("R"), Individual("x"))), true, ElPath.Decided),
        ];

    /// <summary>The shared-witness soundness battery: every <see cref="SharedWitnessCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void SharedWitnessSoundnessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = SharedWitnessCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Two owners whose backward refinements consume the IDENTICAL decoration converge on ONE refined
    /// node, and that convergence is the ordinary decided case rather than a collision to report.
    /// <c>C1</c> and <c>C2</c> are both <c>G</c> and both mint over the same (role, filler) pair, so the
    /// single trigger <c>∃r.G ⊑ H</c> refines the witness they share onto one node and re-points BOTH
    /// their minting edges at it; <c>∃r.H ⊑ W</c> then reads that node back from each owner's own edge, so
    /// <c>C1 ⊑ W</c> and <c>C2 ⊑ W</c> are both entailed — in the model
    /// Δ = {c1, c2, f} with r the symmetric closure of {(c1, f), (c2, f)}, H = {f} and W = {c1, c2}.
    /// The module is consistent and the fast path must DECIDE it: a mint that treats the second owner's
    /// arrival at an interned node as an ownership collision abstains here, which is exactly the case the
    /// shared regime exists to collapse.
    /// </summary>
    [TestMethod]
    public void ConvergentCrossOwnerRefinementsShareOneRefinedNode()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("C1"), Class("G")),
            SubClassOf(Class("C2"), Class("G")),
            SubClassOf(Class("C1"), Some("r", Class("F"))),
            SubClassOf(Class("C2"), Some("r", Class("F"))),
            SubClassOf(Some("r", Class("G")), Class("H")),
            SubClassOf(Some("r", Class("H")), Class("W")),
            ClassAssertion(Class("C1"), Individual("c1")),
            ClassAssertion(Class("C2"), Individual("c2")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "Two owners converging on one refined witness is the ordinary shared case, so the EL fast-path decides the module rather than abstaining.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing empties any class: the model has the two owners, their shared successor, H on the successor and W on both owners.");
        Assert.IsTrue(Subsumes(decision.Verdict, "C1", "W"), "C1's minting edge is re-pointed at the refined node carrying H, so ∃r.H ⊑ W reads it back onto C1.");
        Assert.IsTrue(Subsumes(decision.Verdict, "C2", "W"), "C2's minting edge is re-pointed at the SAME refined node, so the conclusion reaches the second owner too.");
    }

    /// <summary>
    /// The witness description's equality contract, read off the interning it drives: two descriptions are
    /// equal exactly when their core atom and their demand elements agree in order, so equal descriptions
    /// fold to one node and unequal ones stay apart. Each row is a module whose verdict turns on one of
    /// those cases. EQUAL descriptions: a cyclic existential re-derives its own description, which must
    /// fold rather than mint forever, and two owners of one (role, filler) pair reach one node. UNEQUAL by
    /// CORE: two owners over distinct filler cores keep distinct successors, so one branch's clash cannot
    /// empty the other. UNEQUAL by a demand ELEMENT: two backward decorations over the same role with
    /// different trigger atoms separate the ladder positions a conjunction distinguishes. UNEQUAL by
    /// demand LENGTH: a refinement's strict superset is a distinct node from its origin, which is what
    /// lets the origin keep serving its other owners while the refined node carries the deposit.
    /// </summary>
    [TestMethod]
    public void WitnessDescriptionEqualityFoldsEqualKeysAndSeparatesUnequalOnes()
    {
        //Equal core, equal demands: the cyclic existential re-derives the identical description and folds,
        //so the saturation reaches a fixpoint instead of minting an unbounded chain.
        ReasoningModule equalDescriptions = Module(
            Symmetric("r"),
            SubClassOf(Class("A"), Some("r", Class("A"))),
            ClassAssertion(Class("A"), Individual("a")));
        AssertElDecidesLike(equalDescriptions, expectConsistent: true);

        //Unequal by CORE: distinct filler cores never intern together, so A1's backward clash empties only
        //its own branch and a2 keeps its witness.
        ReasoningModule unequalCores = Module(
            Inverse("r", "s"),
            SubClassOf(Class("A1"), Some("r", Class("B1"))),
            SubClassOf(Class("A2"), Some("r", Class("B2"))),
            SubClassOf(Class("B1"), Some("r", Class("K"))),
            SubClassOf(Class("B2"), Some("r", Class("K"))),
            SubClassOf(Some("s", Class("A1")), Class("Q")),
            SubClassOf(Some("s", Class("Q")), NothingReference),
            ClassAssertion(Class("A2"), Individual("a2")));

        ModuleDecision unequalCoresDecision = ElCoupledModuleReasoner.DecideModule(unequalCores, TestContext.CancellationToken);
        Assert.IsTrue(unequalCoresDecision.Statistics.ElTotals.ElDecided, "The distinct-core module is decided by the EL fast-path.");
        Assert.IsTrue(unequalCoresDecision.Verdict!.IsConsistent, "B1 and B2 are distinct cores, so the two successors never intern together and A1's clash leaves a2 satisfiable.");

        //Unequal by a demand ELEMENT and by demand LENGTH: the ladder's two rungs consume different
        //trigger atoms over one role, so each refinement is a strict superset of its origin AND differs
        //from its sibling in an element. F1 and F2 therefore hold at distinct positions and their
        //conjunction never fires.
        ReasoningModule unequalDemands = Module(
            Symmetric("r"),
            SubClassOf(Class("P"), Some("r", Class("Q"))),
            SubClassOf(Class("Q"), Some("r", Class("P"))),
            ClassAssertion(Class("P"), Individual("p")),
            SubClassOf(Some("r", Class("P")), Class("F1")),
            SubClassOf(Some("r", Class("F1")), Class("F2")),
            SubClassOf(new OwlObjectIntersectionOf([Class("F1"), Class("F2")]), NothingReference));

        ModuleDecision unequalDemandsDecision = ElCoupledModuleReasoner.DecideModule(unequalDemands, TestContext.CancellationToken);
        Assert.IsTrue(unequalDemandsDecision.Statistics.ElTotals.ElDecided, "The ladder module is decided by the EL fast-path.");
        Assert.IsTrue(unequalDemandsDecision.Verdict!.IsConsistent, "The two rungs' decorations differ, so the refined descriptions differ and F1 ⊓ F2 is never realised at one position.");
    }

    /// <summary>
    /// R-BACK completeness battery. Each module's TRUE consistency is an explicit hand-built model
    /// (consistent) or an explicit unsat derivation (inconsistent), independent of the inverse-blind tableau
    /// that cannot witness these gains. A left-existential conclusion a join would deposit on a minted
    /// witness through an edge running from that witness to one of its owners is consumed into the witness's
    /// intern key — the witness refines, the conclusions land on the refined node, and the owner's minting
    /// edge re-points — so the ladder positions stay key-distinct while the ladder deposits and the fold
    /// resumes only once the decorations saturate. The FINAL coupled verdict must match the ground truth,
    /// and each case carries its expected decision path so a false fold (a spurious inconsistency on a
    /// ladder module), a lost decision (a refined module silently delegated) or a lost deposit (a swallowed
    /// backward conclusion) fails even when the verdict happens to agree.
    /// </summary>
    /// <returns>Every case as (name, module, true consistency, expected decision path).</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] RBackCompletenessCases() =>
        [
            //RB1 — a constant backward trigger reaching every owner: A1 ⊑ ∃r⁻.B, A2 ⊑ ∃r⁻.B, ∃r.⊤ ⊑ D,
            //(D ⊓ B) ⊑ ⊥, a2 : A2. Derivation: a2 ∈ A2 forces an r-predecessor witness w cored B; w has an
            //r-successor (a2 itself), so ∃r.⊤ ⊑ D types w as D; B ⊓ D ⊑ ⊥ leaves no such w, so A2 = ∅ and a2
            //is condemned — inconsistent. The refinement must fire for EVERY owner: a rule that refines only
            //the first owner's witness leaves A2's branch consistent.
            ("RB1_ConstantBackwardTriggerReachesEveryOwner", Module(
                SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
                SubClassOf(Some("r", ThingReference), Class("D")),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("B")]), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), false, ElPath.Decided),

            //RB2 — the FS6 parity ladder over the mirror role. Model: the infinite chain x0 = p (∈ P, ∈ T),
            //x1 (∈ Q), x2 (∈ P), … with r = {(x_{i+1}, x_i)}. T is root-only, so ∃r.T ⊑ F1 gives F1 = {x1},
            //then F2 = {x2}, F3 = {x3}, G2 = {x4}, G3 = {x5}: F3 and G3 hold at disjoint depths and the
            //conjunction never fires — consistent. Each ladder deposit is consumed into its witness's key, so
            //the positions carrying F3 and G3 intern distinctly and the fold resumes only past the ladder.
            ("RB2_ParityLadderOverMirrorRole", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r", Class("T")), Class("F1")),
                SubClassOf(Some("r", Class("F1")), Class("F2")),
                SubClassOf(Some("r", Class("F2")), Class("F3")),
                SubClassOf(Some("r", Class("F3")), Class("G2")),
                SubClassOf(Some("r", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //RB3 — the FS17 parity ladder over a SUPER-role of the mirror role. Model: the RB2 model with
            //every r-edge promoted to r2, so the ladder reads r2 at the same disjoint depths — consistent.
            //r2 is not itself a mirror target; only the UPWARD closure of the mirror role r reaches it, so
            //dropping that closure from the backward-consumer roles leaves the ladder deposits unconsumed.
            ("RB3_ParityLadderOverSuperRole", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubProperty("r", "r2"),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r2", Class("T")), Class("F1")),
                SubClassOf(Some("r2", Class("F1")), Class("F2")),
                SubClassOf(Some("r2", Class("F2")), Class("F3")),
                SubClassOf(Some("r2", Class("F3")), Class("G2")),
                SubClassOf(Some("r2", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //RB4 — chain × inverse: A1 ⊑ ∃r.B, A2 ⊑ ∃r.B, B ⊑ ∃s.E, r ∘ s ⊑ t, ∃t⁻.A1 ⊑ ⊥, Sym(r), a : A2.
            //Model Δ = {a, b, e}, r = {(a, b), (b, a)}, s = {(b, e)}, t = {(a, e)}, A2 = {a}, B = {b},
            //E = {e}, A1 = ∅: only A1's branch is condemned and a keeps its witness — consistent. The
            //explicit NON-scope pin: r is a mirrored role that is also a chain link, which the admission gate
            //delegates, so a relaxation that admits chains here must re-derive this row.
            ("RB4_ChainInverseCompositionDelegates", Module(
                Symmetric("r"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("s", Class("E"))),
                Chain("t", "r", "s"),
                SubClassOf(SomeInverse("t", Class("A1")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a"))), true, ElPath.Delegated),

            //RB5 — the FS7 bare mirror left existential with an uninhabited trigger. Model: the mutual-cycle
            //chain x0 = p (∈ P), x1 (∈ Q), x2 (∈ P), … with r = {(x_{i+1}, x_i)} and X = ∅, so Y is never
            //derived and nothing clashes — consistent. X is never derived, so no backward deposit ever fires
            //and the module folds exactly as the undecorated cycle does.
            ("RB5_BareMirrorLeftExistential", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Some("r", Class("X")), Class("Y")),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //RB6 — the FS13 mutual EquivalentClasses cycle. Model: the same chain with P = the even
            //positions and Q = the odd positions, which satisfies both equivalence directions at every
            //position — consistent. The subclass directions arrive by the double-mirror path and deposit on
            //the OWNER, whose key already determines them, so no refinement is owed; the row pins the path
            //and RB17 certifies the geometry.
            ("RB6_MutualEquivalentClasses", Module(
                Equivalent(Class("P"), SomeInverse("r", Class("Q"))),
                Equivalent(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //RB7 — re-point at depth 2: A ⊑ ∃r⁻.B, B ⊑ ∃r⁻.K, a : A, a : T, ∃r.T ⊑ Q, ∃r.Q ⊑ P, P ⊑ ⊥.
            //Derivation: a's r-predecessor w1 cored B has the r-successor a ∈ T, so w1 ∈ Q; w1's
            //r-predecessor w2 cored K has the r-successor w1 ∈ Q, so w2 ∈ P ⊑ ⊥; ⊥ climbs both generator
            //edges back to a — inconsistent. Both edges must be re-pointed at the refined witnesses, or the
            //refined nodes are orphaned and ⊥ never reaches a.
            ("RB7_RePointAtDepthTwo", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("T"), Individual("a")),
                SubClassOf(Some("r", Class("T")), Class("Q")),
                SubClassOf(Some("r", Class("Q")), Class("P")),
                SubClassOf(Class("P"), NothingReference)), false, ElPath.Decided),

            //RB8 — sub-role promotion under a pairing key: Sym(r), u ⊑ r, A1 ⊑ ∃u.B, A2 ⊑ ∃u.B, A1 ⊑ T,
            //∃r.T ⊑ Y, Y ⊑ ⊥, a2 : A2. Model Δ = {a2, b2}, u = r = {(a2, b2), (b2, a2)}, A2 = {a2},
            //B = {b2}, T = Y = ∅, A1 = ∅: a2's u-successor b2 is in B, symmetry gives b2 its r-successor a2,
            //and nothing is typed T — consistent. Admission closes the mirrored roles DOWNWARD, so the
            //sub-role u mints per owner and B never becomes a shared filler receiving A1's backward deposit.
            ("RB8_SubRolePromotionUnderPairingKey", Module(
                Symmetric("r"),
                SubProperty("u", "r"),
                SubClassOf(Class("A1"), Some("u", Class("B"))),
                SubClassOf(Class("A2"), Some("u", Class("B"))),
                SubClassOf(Class("A1"), Class("T")),
                SubClassOf(Some("r", Class("T")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true, ElPath.Decided),

            //RB9 — the FS8 range over the coupled symmetric role, restated inside the backward battery.
            //Model Δ = {a, b}, r = {(b, a), (a, b)}, A = {a}, B = {b}, K = {a, b}: every r-target is in K
            //and nothing clashes — consistent. The mirror-range registration this module plants is keyed on
            //⊤, so it separates no position and the backward machinery has nothing owner-specific to
            //consume; the range types every position of the content class alike and the module decides on
            //shared content keys.
            ("RB9_RangeOverCoupledRole", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                Range("r", Class("K")),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //RB10 — the FS22 geometry restated here: a one-directional pairing (r⁻ ⊑ s) forward 2-cycle
            //with a range on the coupled role r itself. Model Δ = {a, b}, A = {a}, B = {b}, K = {a, b},
            //r = {(a, b), (b, a)}, s ⊇ r⁻ — consistent. The module carries no left existential over a
            //mirror role at all, so the backward machinery is completely inert on it and the range is the
            //only writer onto the shared witnesses, which it types uniformly.
            ("RB10_RangeOneDirectionalPairingIsolator", Module(
                InverseSubProperty("r", "s"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                Range("r", Class("K")),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //RB11 — a cyclic self-fold with a backward consumer: C ⊑ ∃r⁻.C, ∃r.Self ⊑ B, B ⊑ ⊥, c : C,
            //c : T, ∃r.T ⊑ K. Model Δ = {c, w}, C = {c, w}, K = {w}, r = {(w, c), (c, w)}: each element's
            //r-predecessor is in C, no element bears a self-loop, so ∃r.Self is empty and B ⊑ ⊥ is vacuous —
            //consistent. The refined chain mints its own witnesses through the ordinary existential
            //introduction, which is where the cyclic-self-fold guard lives, so the artifact self-edge is
            //still caught and the module abstains.
            ("RB11_CyclicSelfFoldWithBackwardConsumer", Module(
                SubClassOf(Class("C"), SomeInverse("r", Class("C"))),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("C"), Individual("c")),
                ClassAssertion(Class("T"), Individual("c")),
                SubClassOf(Some("r", Class("T")), Class("K"))), true, ElPath.Delegated),

            //RB12 — a ground-edge backward deposit onto a non-minted source: Sym(r), r(a, b), a : T,
            //∃r.T ⊑ Y, Y ⊑ ⊥. Derivation: the mirror edge (r, b, a) gives b an r-successor in T, so b ∈ Y
            //and Y ⊑ ⊥ condemns the individual b — inconsistent. The deposit lands on an individual, whose
            //subsumer set is position-independent and which is never folded, so it must proceed plainly: a
            //fence applied to every source instead of minted ones swallows it and no refinement can replace
            //it.
            ("RB12_GroundEdgeBackwardDeposit", Module(
                Symmetric("r"),
                Edge("a", "r", "b"),
                ClassAssertion(Class("T"), Individual("a")),
                SubClassOf(Some("r", Class("T")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference)), false, ElPath.Decided),

            //RB13 — a late trigger: the RB2 ladder with T reaching the root indirectly (p : U, U ⊑ T in
            //place of p : T). Model: the RB2 model, with U = {x0} — consistent. The trigger arrives on the
            //root AFTER its witness edge exists, so the deposit fires through the incoming-edge join rather
            //than the outgoing-edge join: intercepting one join site only leaves this deposit unconsumed.
            ("RB13_LateTriggerThroughSubsumerJoin", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("U"), Individual("p")),
                SubClassOf(Class("U"), Class("T")),
                SubClassOf(Some("r", Class("T")), Class("F1")),
                SubClassOf(Some("r", Class("F1")), Class("F2")),
                SubClassOf(Some("r", Class("F2")), Class("F3")),
                SubClassOf(Some("r", Class("F3")), Class("G2")),
                SubClassOf(Some("r", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //RB14 — a saturating self-referential ladder: P ⊑ ∃r⁻.Q, Q ⊑ ∃r⁻.P, p : P, p : T, ∃r.T ⊑ T.
            //Model Δ = {x, y}, r = {(y, x), (x, y)}, P = {x}, Q = {y}, T = {x, y}: each element's
            //r-predecessor carries T, which is exactly what ∃r.T ⊑ T demands — consistent. The trigger is
            //its own conclusion, so once a witness's key records the decoration the deposit must degenerate
            //to a plain one on that witness; otherwise the module either refines forever or loses T.
            ("RB14_SaturatingSelfReferentialLadder", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r", Class("T")), Class("T"))), true, ElPath.Decided),

            //RB15 — a FORWARD left existential over a symmetric role at depth 2: Sym(r), A ⊑ ∃r.B,
            //B ⊑ ∃r.K, ∃r.K ⊑ Y, Y ⊑ ⊥, a : A. Derivation: a's successor b ∈ B has a successor k ∈ K, so
            //∃r.K ⊑ Y types b, Y ⊑ ⊥ empties it, B = ∅ empties A, and a is condemned — inconsistent. The
            //deposit on b travels from an owner to its own successor, so the direction test's proper-subset
            //conjunct must classify it FORWARD: a predicate without it refines b and re-points the wrong
            //edge, and ⊥ never reaches a.
            ("RB15_ForwardLeftExistentialAtDepthTwo", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("r", Class("K")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //RB16 — two triggers on one witness: A ⊑ ∃r⁻.C, a : A, a : T1, a : T2, ∃r.T1 ⊑ Y1,
            //∃r.T2 ⊑ Y2, (Y1 ⊓ Y2) ⊑ ⊥. Derivation: a's single r-predecessor w has the r-successor a, which
            //is both T1 and T2, so w ∈ Y1 ⊓ Y2 ⊑ ⊥, A = ∅, and a is condemned — inconsistent. Both
            //decorations must end on ONE node — by the batch of a single firing — or the two conclusions sit
            //on sibling witnesses that never meet and the conjunction never fires.
            ("RB16_TwoTriggersOnOneWitness", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("T1"), Individual("a")),
                ClassAssertion(Class("T2"), Individual("a")),
                SubClassOf(Some("r", Class("T1")), Class("Y1")),
                SubClassOf(Some("r", Class("T2")), Class("Y2")),
                SubClassOf(new OwlObjectIntersectionOf([Class("Y1"), Class("Y2")]), NothingReference)), false, ElPath.Decided),

            //RB17 — the double-mirror certifier extended past the fold depth: the RB6 cycle plus p : T, the
            //down-chain ∃r.T ⊑ F1 … ∃r.F4 ⊑ F5 and the up-chain ∃r⁻.F5 ⊑ G4, ∃r⁻.G4 ⊑ G3, with
            //(F5 ⊓ G3) ⊑ ⊥. Model: positions x0 = p, x1, x2, … with r = {(x_{i+1}, x_i)}, P = the even and
            //Q = the odd positions (each position has an r-predecessor of the opposite parity, so both
            //equivalence directions hold), T = {x0}. The down-chain axioms are ∃r.· and travel AWAY from the
            //root, giving F1 = {x1} … F5 = {x5}; the up-chain axioms are ∃r⁻.· and travel TOWARD it, giving
            //G4 = {x4} from F5 and G3 = {x3} from G4. F5 ⊓ G3 = {x5} ∩ {x3} = ∅ — consistent. The module
            //carries a DOUBLE-MIRRORED backward consumer: r is both an inverse-pairing key (the subclass
            //direction of the equivalences mirrors it again) and a mirror target (the generator pairs it),
            //so the ladder consumes in BOTH directions. Under shared content keys neither direction can
            //carry a fact onto a position that does not warrant it: a witness key is the role mark alone
            //and grows only by the decorations its own refinements consume, so no witness carries an
            //owner's whole demand set and no deeper position interns onto an earlier one. The row pins that
            //the up-chain reads exactly the positions it genuinely reaches.
            ("RB17_DoubleMirrorAcrossFoldDepth", Module(
                Equivalent(Class("P"), SomeInverse("r", Class("Q"))),
                Equivalent(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r", Class("T")), Class("F1")),
                SubClassOf(Some("r", Class("F1")), Class("F2")),
                SubClassOf(Some("r", Class("F2")), Class("F3")),
                SubClassOf(Some("r", Class("F3")), Class("F4")),
                SubClassOf(Some("r", Class("F4")), Class("F5")),
                SubClassOf(SomeInverse("r", Class("F5")), Class("G4")),
                SubClassOf(SomeInverse("r", Class("G4")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F5"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //RB18 — a constant trigger every position satisfies: P ⊑ ∃r⁻.Q, Q ⊑ ∃r⁻.P, p : P, ∃r.⊤ ⊑ F.
            //Model: the mutual-cycle chain with F on every witness (each has an r-successor, so each
            //satisfies ∃r.⊤) — consistent. Every position records the same single decoration, so the
            //refinements saturate at once and every owner converges on ONE refined node; the row pins that
            //convergence as the ordinary decided case, never a collision to report, and pins that the
            //conclusion still reaches the owners whose minting edges the refinement re-points.
            ("RB18_SharedRefinementMustNotAbstain", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                SubClassOf(Some("r", ThingReference), Class("F"))), true, ElPath.Decided),

            //RB19 — one symmetric role carrying the mint edges and both ladder directions at once: Sym(r),
            //P ⊑ ∃r.Q, Q ⊑ ∃r.P, p : P, ∃r.P ⊑ F1, ∃r.F1 ⊑ F2, (F1 ⊓ F2) ⊑ ⊥. Model Δ = {x, y}, P = {x},
            //Q = {y}, r = {(x, y), (y, x)}: y's r-successor x is in P, so F1 = {y}; x's r-successor y is in
            //F1, so F2 = {x}; F1 ⊓ F2 = ∅ and nothing is emptied — consistent. A symmetric role is its own
            //mirror target AND its own inverse-pairing key, so the ladder consumes in both directions over
            //the very role the witnesses are minted on. The two decoration kinds stay structurally
            //disjoint there: the mint mark records the ROLE alone under its own tag bit, while a backward
            //decoration records a (role, trigger atom) pair under a different one, so no mark can ever
            //equal a decoration and the refinement's strictly growing decoration set is what keeps the two
            //ladder positions apart. Drop either tag and a mark meets a decoration on one numeric value,
            //merging the positions the conjunction separates.
            ("RB19_SymmetricLadderNamespaceCollision", Module(
                Symmetric("r"),
                SubClassOf(Class("P"), Some("r", Class("Q"))),
                SubClassOf(Class("Q"), Some("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                SubClassOf(Some("r", Class("P")), Class("F1")),
                SubClassOf(Some("r", Class("F1")), Class("F2")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F1"), Class("F2")]), NothingReference)), true, ElPath.Decided),

            //RB20 — the promote-then-mirror geometry: r⁻ ⊑ s, u ⊑ r, P ⊑ ∃u.Q, Q ⊑ ∃u.P, p : P, p : T, and
            //the parity ladder over s. Model: the chain x0 = p (∈ P, ∈ T), x1 (∈ Q), x2 (∈ P), … with
            //u = r = {(x_i, x_{i+1})} and s ⊇ r⁻ = {(x_{i+1}, x_i)}, so the s-ladder reads the same disjoint
            //depths the mirror ladder does: F1 = {x1}, F2 = {x2}, F3 = {x3}, G2 = {x4}, G3 = {x5} — the
            //conjunction never fires, consistent. The mint edges run over the sub-role u and are mirrored
            //only after promotion to r, so the pairing of the minting role is empty and the deposit is
            //witness-to-owner only by the key lattice, never by the role relation.
            ("RB20_PromoteThenMirrorLeak", Module(
                InverseSubProperty("r", "s"),
                SubProperty("u", "r"),
                SubClassOf(Class("P"), Some("u", Class("Q"))),
                SubClassOf(Class("Q"), Some("u", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("s", Class("T")), Class("F1")),
                SubClassOf(Some("s", Class("F1")), Class("F2")),
                SubClassOf(Some("s", Class("F2")), Class("F3")),
                SubClassOf(Some("s", Class("F3")), Class("G2")),
                SubClassOf(Some("s", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //RB21 — told-equivalent owners sharing one base mint: EquivalentClasses(C1, C2), Sym(r),
            //C1 ⊑ ∃r.F, C2 ⊑ ∃r.F, ∃r.C1 ⊑ Y, ∃r.C2 ⊑ Y, K ⊑ {p}, p : C1. Model Δ = {p, w},
            //C1 = C2 = {p}, F = Y = {w}, K = ∅, r = {(p, w), (w, p)}: p's r-successor w is in F, w's
            //r-successor p is in C1 and C2 so w ∈ Y, and the nominal inclusion is vacuous — nothing is
            //emptied, consistent. Both existentials name one (role, filler) pair, so every owner — the two
            //told-equivalent class atoms and the individual that carries both cores — reaches ONE shared
            //witness, and the single join firing consumes both trigger atoms in one batch onto one refined
            //node, the degeneration a one-decoration-at-a-time consumption would split into siblings. The
            //row pins that a mint returning the node another owner already reached is the ordinary case:
            //make any non-fresh return abstain and this module loses its decision.
            ("RB21_FirstCollisionAtRefinement", Module(
                Equivalent(Class("C1"), Class("C2")),
                Symmetric("r"),
                SubClassOf(Class("C1"), Some("r", Class("F"))),
                SubClassOf(Class("C2"), Some("r", Class("F"))),
                SubClassOf(Some("r", Class("C1")), Class("Y")),
                SubClassOf(Some("r", Class("C2")), Class("Y")),
                SubClassOf(Class("K"), OneOf("p")),
                ClassAssertion(Class("C1"), Individual("p"))), true, ElPath.Decided),

            //RB22 — a forward conclusion the OWNER must read: Sym(r), A ⊑ ∃r.B, B ⊑ ∃r.K, ∃r.K ⊑ Y,
            //∃r.Y ⊑ W, (W ⊓ V) ⊑ ⊥, a : A, a : V. Derivation: a's r-successor b ∈ B has the r-successor
            //k ∈ K, so ∃r.K ⊑ Y types b; a then has an r-successor in Y, so a ∈ W; a is also V, and
            //W ⊓ V ⊑ ⊥ condemns it — inconsistent. The deposit of Y on b travels from an owner to its OWN
            //witness, and a symmetric coupled role carries the mirror edge back, so the coupled-edge
            //membership alone reads it as backward: only the OWNERSHIP DECORATION separates the two —
            //b's key records its own owner a, never the core of its successor k. Refining b instead moves
            //Y onto a node a's edge does not reach, and the clash on a never fires.
            ("RB22_ForwardConclusionTheOwnerMustRead", Module(
                Symmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("K"))),
                SubClassOf(Some("r", Class("K")), Class("Y")),
                SubClassOf(Some("r", Class("Y")), Class("W")),
                SubClassOf(new OwlObjectIntersectionOf([Class("W"), Class("V")]), NothingReference),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("V"), Individual("a"))), false, ElPath.Decided),

            //RB23 — the doubly mirrored role reached only through its SUPER-role: the RB17 cycle plus
            //SubProperty(r, r2), with the down-chain ladder registered over r2 and the up-chain still over
            //r⁻. Model: positions x0 = p (∈ P, ∈ T), x1, x2, … with r = {(x_{i+1}, x_i)} and r2 ⊇ r, P = the
            //even and Q = the odd positions (each has an r-predecessor of the opposite parity, so both
            //equivalence directions hold). The down-chain axioms are ∃r2.· and travel away from the root
            //over the promoted edges, giving F1 = {x1} … F5 = {x5}; the up-chain axioms are ∃r⁻.· and travel
            //toward it, giving G4 = {x4} and then G3 = {x3}. F5 ⊓ G3 = ∅ — consistent. r is at once an
            //inverse-pairing key and a mirror target, but NO left existential is registered on r itself:
            //only its super-role r2 carries one, so the down-chain reaches the witnesses only over the
            //promoted edges and the backward-consumer set finds the consuming role only by closing the role
            //UPWARD. Drop that closure and the down-chain deposits stay unconsumed on the shared witnesses,
            //merging the ladder positions the conjunction separates.
            ("RB23_DoubleMirrorLadderOverSuperRole", Module(
                Equivalent(Class("P"), SomeInverse("r", Class("Q"))),
                Equivalent(Class("Q"), SomeInverse("r", Class("P"))),
                SubProperty("r", "r2"),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Class("T"), Individual("p")),
                SubClassOf(Some("r2", Class("T")), Class("F1")),
                SubClassOf(Some("r2", Class("F1")), Class("F2")),
                SubClassOf(Some("r2", Class("F2")), Class("F3")),
                SubClassOf(Some("r2", Class("F3")), Class("F4")),
                SubClassOf(Some("r2", Class("F4")), Class("F5")),
                SubClassOf(SomeInverse("r", Class("F5")), Class("G4")),
                SubClassOf(SomeInverse("r", Class("G4")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F5"), Class("G3")]), NothingReference)), true, ElPath.Decided),
        ];

    /// <summary>The R-BACK completeness battery: every <see cref="RBackCompletenessCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void RBackCompletenessBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = RBackCompletenessCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            bool verdictOk = finalConsistent == trueConsistent;
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// E-RB1 capability — <c>A ⊑ ∃r⁻.B</c> with <c>∃r.A ⊑ Y</c> and <c>Y ⊑ ⊥</c> entails <c>A ⊑ ⊥</c>: the
    /// generator mints A's <c>r</c>-predecessor, the mirror writes the real <c>r</c>-edge from that witness
    /// back onto the A-member, and the left existential over that edge types the witness as <c>Y</c>, which
    /// <c>Y ⊑ ⊥</c> empties — so <c>⊥</c> climbs the generator edge and empties <c>A</c>. The conclusion is
    /// deposited on the witness through a witness-to-owner edge, so it is consumed into the witness's key and
    /// the owner's edge is re-pointed at the refined node; without that re-point the emptied node is orphaned
    /// and <c>A</c> survives. Asserted through the ⊥-proxy idiom (an unsatisfiable class is subsumed by every
    /// signature class, and the projection never enumerates <c>owl:Nothing</c>); the inverse-blind tableau
    /// drops the inverse existential and derives nothing.
    /// </summary>
    [TestMethod]
    public void BackwardConsumedExistentialEmptiesOwnerIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
            SubClassOf(Some("r", Class("A")), Class("Y")),
            SubClassOf(Class("Y"), NothingReference),
            SubClassOf(Class("Z"), ThingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The backward-consuming module is decided by the EL fast-path.");
        Assert.IsTrue(Subsumes(decision.Verdict!, "A", "Z"), "A ⊑ ⊥ collapses A into every signature class, including the unrelated Z — the A ⊑ Nothing witness.");
        Assert.IsFalse(Subsumes(AlcModuleReasoner.Decide(module, TestContext.CancellationToken), "A", "Z"), "The inverse-blind tableau drops the inverse existential, never types the predecessor, and finds A satisfiable.");
    }

    /// <summary>
    /// E-RB2 non-contamination — <c>A1 ⊑ ∃r⁻.B</c>, <c>A2 ⊑ ∃r⁻.B</c> with the constant trigger
    /// <c>∃r.⊤ ⊑ D</c> types each owner's per-owner witness, never the shared named filler <c>B</c>: the
    /// refinement writes <c>D</c> onto the refined witnesses, whose keys separate the owners, so <c>B ⊑ D</c>
    /// is NOT entailed. <c>B ⊑ M</c> is the enumeration control — it confirms <c>B</c> is enumerated by the
    /// same projection the absent pair is tested against.
    /// </summary>
    [TestMethod]
    public void BackwardConsumedConstantTriggerDoesNotContaminateSharedFillerIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
            SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
            SubClassOf(Some("r", ThingReference), Class("D")),
            SubClassOf(Class("B"), Class("M")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The constant-trigger module is decided by the EL fast-path.");
        Assert.IsTrue(Subsumes(decision.Verdict!, "B", "M"), "B ⊑ M: the told inclusion confirms B is enumerated by the projection the absent B ⊑ D is tested against.");
        Assert.IsFalse(Subsumes(decision.Verdict!, "B", "D"), "B ⊑ D is NOT derived: the consumed trigger types the per-owner witnesses, never the shared named class B.");
    }

    /// <summary>
    /// E-RB3 consumer parity — a module the backward consumption flips from delegated to decided reports a
    /// decisive EL decision, stays consistent, and its enumerated subsumption set is a SUPERSET of the
    /// inverse-blind fallback's on the same module. The flip may only move in that direction: the EL closure
    /// is complete for the admitted fragment, so it may gain pairs the fallback misses and may never lose one.
    /// </summary>
    [TestMethod]
    public void FlippedBackwardModuleGainsSubsumptionsAndStaysDecisive()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
            SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
            SubClassOf(Some("r", Class("X")), Class("Y")),
            ClassAssertion(Class("P"), Individual("p")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The flipped module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The mutual cycle has a model and X is never derived, so the module is consistent.");
        Assert.IsTrue(decision.Verdict.IsDecisive, "A whole-module fast-path decision names no excluded construct.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The decision is whole-module Decided, not fragment-relative.");
        Assert.IsEmpty(decision.Verdict.UnsupportedConstructs, "A decided module carries an empty remainder.");

        List<string> elKeys = SubsumptionKeys(decision.Verdict);
        foreach(string key in SubsumptionKeys(AlcModuleReasoner.Decide(module, TestContext.CancellationToken)))
        {
            Assert.Contains(key, elKeys, "The EL pair set is a superset of the fallback's: " + key + " must survive the flip.");
        }
    }

    /// <summary>
    /// Adversarial battery for the generator-fence relaxation: the slice of self and chain features over a
    /// generator's forward role the per-owner mint reproduces is admitted, and the cyclic self-fold under a
    /// reachable self-elimination abstains at the mint. Each module's TRUE consistency is a hand-built model
    /// (consistent) or an explicit unsat derivation (inconsistent), independent of the inverse-blind tableau
    /// the oracle cannot witness these gains on. The FINAL coupled verdict (EL fast-path, or its delegation
    /// to the fallback) must match that ground truth, and each case carries its expected decision path so a
    /// silent tier drift fails even when the verdict happens to agree. M0 and GF15 are the failing-first
    /// rows: without the mint-site guard the cyclic self-fold mints a self-edge whose mirror artifact fires
    /// the self-elimination and back-propagates ⊥, condemning a module whose true models need no self-loop —
    /// a false inconsistency (M0 pre-existing, independent of the relaxation).
    /// </summary>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] GeneratorFenceRelaxationCases() =>
        [
            //GF1 — the transitive forward role's own self-transitivity r ∘ r ⊑ r is the admitted slice. Model
            //Δ = {a, w}, A = {a}, C = {w}, r = {(w, a)}: a's r-predecessor w is in C and nothing clashes —
            //consistent. The mint writes the real r-edge onto a via the generator; no ⊥ anywhere.
            ("GF1_TransitiveForwardSelfTransitivity", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //GF2 — Transitive(r), A ⊑ ∃r⁻.B, B ⊑ ⊥, a : A. Derivation: a ∈ A forces an r-predecessor witness
            //cored B; B = ∅ empties it, and ⊥ back-propagates over the g-edge to condemn a — inconsistent.
            //The relaxation admits the self-transitive forward role, flipping the pinned false-consistent.
            ("GF2_TransitiveEmptyPredecessorCore", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //GF3 — Transitive(r), A ⊑ ∃r⁻.C, ∃r⁻.C ⊑ Y, Y ⊑ ⊥, a : A. TRUE inconsistent: a's forced
            //r-predecessor in C fires ∃r⁻.C ⊑ Y on a, and Y ⊑ ⊥ condemns it. The subclass ∃r⁻.C makes r a
            //pairing KEY via the synthetic mirror, so the untouched second check delegates the chain on r —
            //fragment-relative honest miss: the inverse-blind fallback drops the inverse existential and
            //answers consistent, so the delegated verdict is not held to ground truth here.
            ("GF3_SubclassInverseMakesPairingKey", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(SomeInverse("r", Class("C")), Class("Y")),
                SubClassOf(Class("Y"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Delegated),

            //GF4 — Transitive(r), A1 ⊑ ∃r⁻.B, A2 ⊑ ∃r⁻.B, B ⊑ ∃r⁻.K, (K ⊓ ∃r.A1) ⊑ ⊥, a2 : A2. The A1-side
            //depth-2 chain forces a K-cored witness with an r-edge to an A1-witness, so the conjunction
            //empties class A1 only; a2's chain is owner-key-separate (owner-demand inheritance keeps the
            //witnesses distinct), so a2 stays satisfiable — consistent.
            ("GF4_DepthTwoOwnerSeparation", Module(
                Transitive("r"),
                SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(new OwlObjectIntersectionOf([Class("K"), Some("r", Class("A1"))]), NothingReference),
                ClassAssertion(Class("A2"), Individual("a2"))), true, ElPath.Decided),

            //GF5 — Transitive(r), A ⊑ ∃r⁻.B, B ⊑ ∃r⁻.K, (K ⊓ ∃r.A) ⊑ ⊥, a : A. Inconsistent, and the clash
            //reaches a ONLY through the depth-2 composed edge r(w2, a): the K-cored witness w2 has an r-edge
            //to a's witness and, transitively, to a, so K ⊓ ∃r.A empties w2 and ⊥ back-propagates to a. The
            //composition on the self-transitive forward role is load-bearing here.
            ("GF5_CompositionLoadBearingClash", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                SubClassOf(Class("B"), SomeInverse("r", Class("K"))),
                SubClassOf(new OwlObjectIntersectionOf([Class("K"), Some("r", Class("A"))]), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //GF6 — A2 ⊑ ∃r.Self, A ⊑ ∃r⁻.C, a : A. The self-demand is on the forward role r itself, the
            //admitted (R-b) slice. Model Δ = {a, w}, A = {a}, C = {w}, r = {(w, a)}, A2 = ∅: nothing forces a
            //self-edge, a's r-predecessor w is in C — consistent. The self-demand is decorated on the forward
            //role, inert here.
            ("GF6_SelfDemandOnForwardRole", Module(
                SubClassOf(Class("A2"), HasSelf("r")),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //GF7 — GF6 plus a2 : A2, ∃r.Self ⊑ B, B ⊑ ⊥. Inconsistent: a2 ∈ A2 gains a GENUINE told self-edge
            //(r, a2, a2), ∃r.Self ⊑ B fires on it (source == target holds for real), and B ⊑ ⊥ condemns a2.
            //This is not the fold artifact — the mint-site guard must NOT delegate it, and does not: the
            //guard fires only on a cyclic self-FOLD (witness == subject), never a told self-demand edge.
            ("GF7_ToldSelfEdgeFiresEliminationGenuine", Module(
                SubClassOf(Class("A2"), HasSelf("r")),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("A2"), Individual("a2")),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference)), false, ElPath.Decided),

            //GF8 — SubProperty(r, t), Transitive(t), A ⊑ ∃r⁻.C, a : A. The chain t ∘ t ⊑ t is on the strict
            //super-role t in the forward role's upward closure, outside the (R-a) slice, so the module
            //delegates. Consistent (the fallback drops the inverse existential and agrees).
            ("GF8_SuperRoleChainDelegates", Module(
                SubProperty("r", "t"),
                Transitive("t"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Delegated),

            //GF9 — Chain r ∘ r ⊑ q (q ≠ r), A ⊑ ∃r⁻.C, a : A. The chain entry over the forward role r has
            //conclusion q ≠ r, a mixed chain outside the (R-a) slice, so the module delegates. Consistent.
            ("GF9_MixedChainDelegates", Module(
                Chain("q", "r", "r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Delegated),

            //GF10 — Transitive(r), Symmetric(r), A ⊑ ∃r⁻.C, a : A. Symmetric(r) makes r a pairing KEY, so the
            //untouched second check delegates the chain on r (the relaxed generator loop is not a catcher
            //here — the second check is invariant under it). Consistent.
            ("GF10_SymmetricPairingKeyChainDelegates", Module(
                Transitive("r"),
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Delegated),

            //GF11 — the FS14 module: P ⊑ ∃r⁻.Q, Q ⊑ ∃r⁻.P, Transitive(r), p : P. The generator fence admits
            //the self-transitive forward role, the module reaches the mint, and the mutually recursive
            //cross-owner witnesses fold there; Transitive(r) is a chain over the witness closure, so F4 keeps
            //the fold-safety fence closed and the mint-site ownership abstention delegates. Consistent.
            ("GF11_Fs14FoldAbstentionDelegates", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                Transitive("r"),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Delegated),

            //GF12 — Transitive(r), A ⊑ ∃r⁻.C, X ⊑ ∃u.F over an unrelated role u. The sidecar existential over
            //u touches neither the forward role's closure nor the mirror, so it does not fence; the module is
            //admitted and decided. Consistent (no class emptied).
            ("GF12_UnrelatedSidecarExistential", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(Class("X"), Some("u", Class("F")))), true, ElPath.Decided),

            //GF13 — Transitive(r), A ⊑ ∃r⁻.C, SubProperty(u, r), B ⊑ ∃u.F, a : A, b : B. Model {a, w, b, f}:
            //r = {(w, a), (b, f)}, u = {(b, f)}. The sub-role u's existential promotes a plain r-edge (b, f)
            //but the forward role is a mirror TARGET never a pairing key, so no g-edge is minted back and the
            //shared filler f receives only universal facts — consistent, decided.
            ("GF13_SubRolePromotionSharedFiller", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubProperty("u", "r"),
                SubClassOf(Class("B"), Some("u", Class("F"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("B"), Individual("b"))), true, ElPath.Decided),

            //GF14 — SubProperty(r, t), A2 ⊑ ∃t.Self, A ⊑ ∃r⁻.C, a : A. The self-demand is on the strict
            //super-role t in the forward role's upward closure, outside the (R-b) slice, so the module
            //delegates (the super-role self-demand catcher). Consistent.
            ("GF14_SuperRoleSelfDemandDelegates", Module(
                SubProperty("r", "t"),
                SubClassOf(Class("A2"), HasSelf("t")),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Delegated),

            //GF15 — Transitive(r), C ⊑ ∃r⁻.C, ∃r.Self ⊑ B, B ⊑ ⊥, c : C. Model Δ = the negative integers,
            //C = Δ, r = the strict order y < x: every C-element x has an r-predecessor (any y < x) in C, r is
            //transitive, and no element bears a self-loop, so ∃r.Self is empty and B ⊑ ⊥ is vacuous —
            //consistent. The relaxed loop admits the self-transitive forward role, so the module reaches the
            //mint, where the cyclic self-fold under the reachable self-elimination on r abstains (the 1.2
            //guard). Failing-first RED without the guard: the fold artifact self-edge fires ∃r.Self ⊑ B and
            //falsely condemns c.
            ("GF15_CyclicSelfFoldSelfEliminationTransitive", Module(
                Transitive("r"),
                SubClassOf(Class("C"), SomeInverse("r", Class("C"))),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("C"), Individual("c"))), true, ElPath.Delegated),

            //M0 — GF15 without Transitive(r): the pre-existing production false-inconsistent, admitted by both
            //the old and the relaxed loop (no chain or self-demand for the loop to catch). Model Δ = {c, w},
            //C = {c, w}, r = {(w, c), (c, w)}: c's and w's r-predecessors lie in C and no element bears a
            //self-loop, so ∃r.Self is empty and B ⊑ ⊥ is vacuous — consistent. Without the guard the cyclic
            //C ⊑ ∃r⁻.C folds a witness onto c, mints a g self-edge whose mirror is an artifact r self-edge,
            //∃r.Self ⊑ B fires on it, and ⊥ back-propagates to condemn c. Failing-first RED without the guard.
            ("M0_CyclicSelfFoldSelfEliminationNoTransitive", Module(
                SubClassOf(Class("C"), SomeInverse("r", Class("C"))),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("C"), Individual("c"))), true, ElPath.Delegated),

            //GF16 — Transitive(r), A ⊑ ∃r⁻.C, Range(r, E), E ⊑ ⊥, a : A. Inconsistent: a is the target of its
            //witness's r-edge, so range(r) = E types a, and E ⊑ ⊥ condemns a. A capability flip — the
            //inverse-blind fallback drops the inverse existential, never types a, and answers consistent.
            ("GF16_RangeOverBackwardTargetClash", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference),
                ClassAssertion(Class("A"), Individual("a"))), false, ElPath.Decided),

            //GF17 — Transitive(r), A ⊑ ∃r⁻.C, A ⊑ ∃s.{n}, a : A. A class-space nominal {n} on the uncoupled
            //role s coexists with the composed witness edges. Model {a, w, n}: r = {(w, a)}, s = {(a, n)} —
            //a has its r-predecessor w in C and its s-value the individual n; nothing clashes — consistent,
            //decided. The coexistence pin: the nominal machinery and the generator's witness edges both run.
            ("GF17_NominalCoexistsWithWitnessEdges", Module(
                Transitive("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(Class("A"), Some("s", OneOf("n"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Decided),

            //GF18 — Chain q ∘ s ⊑ r (q, s ∉ Up(r)), A ⊑ ∃r⁻.C, a : A. The chain's CONCLUSION r lies in the
            //forward role's upward closure while its links q, s do not, so only ENTRY enumeration catches it
            //(a first/second-role lookup on r finds nothing); the mixed chain is outside the (R-a) slice, so
            //the module delegates. Consistent.
            ("GF18_ConclusionInClosureChainDelegates", Module(
                Chain("r", "q", "s"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a"))), true, ElPath.Delegated),

            //GF19 — two generators where the self-transitive forward role r1 is a strict sub-role of the
            //second generator's forward role r2. The chain (r1, r1, r1) satisfies the purity test on r1's own
            //iteration and does not touch Up(r2) = {r2}, so both generators are admitted; r1's composed edges
            //promote into the mirror-target r2 as genuine relationships (hierarchy semantics over real
            //paths). Model Δ = {a, w1, b, w2}: A = {a}, C = {w1}, B = {b}, D = {w2}, r1 = {(w1, a)},
            //r2 = {(w1, a), (w2, b)} ⊇ r1 — transitivity of r1 vacuous, every axiom satisfied. Consistent.
            ("GF19_SelfTransitiveSubRoleOfSecondGenerator", Module(
                SubProperty("r1", "r2"),
                Transitive("r1"),
                SubClassOf(Class("A"), SomeInverse("r1", Class("C"))),
                SubClassOf(Class("B"), SomeInverse("r2", Class("D"))),
                ClassAssertion(Class("A"), Individual("a")),
                ClassAssertion(Class("B"), Individual("b"))), true, ElPath.Decided),
        ];

    /// <summary>The generator-fence-relaxation battery: every <see cref="GeneratorFenceRelaxationCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void GeneratorFenceRelaxationBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = GeneratorFenceRelaxationCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;

            //A fast-path decision carries the ground-truth verdict; a delegated decision is fragment-relative,
            //so its verdict is held only to the fallback it returns (GF3 is the pinned honest miss where the
            //inverse-blind fallback answers consistent against a true inconsistency).
            bool verdictOk = expectedPath switch
            {
                (ElPath.Decided) => finalConsistent == trueConsistent,
                (ElPath.Delegated) => finalConsistent == tableauConsistent,
                _ => false,
            };
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Survey-widening battery for the inverse existential in the three positions the module survey admits
    /// beside the subclass, superclass, equivalence and disjointness ones: a property domain class, a
    /// property range class, and a class-assertion class. Each module's TRUE consistency is established by
    /// an explicit hand-built model (consistent) or an explicit unsat derivation (inconsistent),
    /// independent of the inverse-blind tableau, which cannot witness these gains. Each case carries its
    /// expected decision path beside its verdict, so a case that silently changes tier fails even when its
    /// verdict happens to agree.
    /// </summary>
    /// <returns>Every case as its name, module, TRUE consistency, and expected decision path.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] SurveyWideningCases() =>
        [
            //SW1 — model Δ = {x, w}, C = {w}, r = {(w, x)}: the asserted demand for an r-predecessor in C is
            //met by the minted witness w and nothing clashes. The assertion arm reduces ∃r⁻.C on the
            //individual atom to the forward generator existential ∃g.C, so the module is decided; the survey
            //flag alone leaves the shape unsupported and the remainder check delegates it.
            ("SW1_AssertedInverseExistential", Module(
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x"))), true, ElPath.Decided),

            //SW2 — INCONSISTENT: x is the target of its witness's r-edge, so range(r) = E types x, and
            //E ⊑ ⊥ condemns it. The mirror writes the real r-edge back onto the individual; a mint without
            //that pairing would leave x untyped. The inverse-blind tableau answers consistent.
            ("SW2_AssertedInverseExistentialRangeClash", Module(
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x")),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference)), false, ElPath.Decided),

            //SW3 — INCONSISTENT: the forced predecessor is cored C, C ⊑ ⊥ empties it, and ⊥ back-propagates
            //across the assertion-rooted generator edge to condemn x.
            ("SW3_AssertedInverseExistentialEmptyFiller", Module(
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x")),
                SubClassOf(Class("C"), NothingReference)), false, ElPath.Decided),

            //SW4 — CONSISTENT: A = ∅ (its witness has an r-successor in X, so it is in Q = ∅); model
            //Δ = {y, w}, B = {w}, r = {(w, y)}, Y = {y}. The two witnesses share the core B and differ only
            //in their mint decoration, which carries the owner's identity — the individual atom for y, the
            //class atom for A — so the condemned class branch never reaches y.
            ("SW4_AssertedAndClassOwnerShareAFillerCore", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
                ClassAssertion(SomeInverse("r", Class("B")), Individual("y")),
                SubClassOf(Class("A"), Class("X")),
                SubClassOf(Some("r", Class("X")), Class("Q")),
                SubClassOf(Class("Q"), NothingReference),
                ClassAssertion(Class("Y"), Individual("y"))), true, ElPath.Decided),

            //SW5 — INCONSISTENT: x's forced r-predecessor lies in C ⊑ {a}, so it IS a, and it lies in D;
            //b's asserted s-edge to a then puts b in ∃s.D ⊑ K = ∅. The conclusion reaches the real
            //individual through the nominal merge's Direction 2, which fires only on a LIVE carrier — and
            //an asserted root is live by assertion, so its witness is live by the forward cascade.
            ("SW5_AssertedRootWitnessIsLive", Module(
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x")),
                SubClassOf(Class("C"), OneOf("a")),
                SubClassOf(Class("C"), Class("D")),
                SubClassOf(Some("s", Class("D")), Class("K")),
                SubClassOf(Class("K"), NothingReference),
                ClassAssertion(Some("s", OneOf("a")), Individual("b"))), false, ElPath.Decided),

            //SW26 — INCONSISTENT: a ∈ A is inhabited, so the witness minted from a's carriage of A is live
            //by the forward cascade; it lies in C ⊑ {b}, so it IS b, and it lies in D; c's asserted s-edge
            //to b puts c in ∃s.D ⊑ K = ∅. The live witness here is minted through the superclass route, so
            //the row holds the liveness gate rather than the assertion arm — the middle of the bracket.
            ("SW26_InhabitedClassOwnerWitnessIsLive", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a")),
                SubClassOf(Class("C"), OneOf("b")),
                SubClassOf(Class("C"), Class("D")),
                SubClassOf(Some("s", Class("D")), Class("K")),
                SubClassOf(Class("K"), NothingReference),
                ClassAssertion(Some("s", OneOf("b")), Individual("c"))), false, ElPath.Decided),

            //SW6 — CONSISTENT: A = ∅, so no element is forced to have a predecessor and a is never typed D;
            //model Δ = {b, a}, s = {(b, a)}, every class other than the empty A, C, D, K unconstrained. The
            //complement of SW5 and SW26: a hypothetical witness must not constrain the real individual.
            ("SW6_UninhabitedClassOwnerWitnessIsNotLive", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                SubClassOf(Class("C"), OneOf("a")),
                SubClassOf(Class("C"), Class("D")),
                SubClassOf(Some("s", Class("D")), Class("K")),
                SubClassOf(Class("K"), NothingReference),
                ClassAssertion(Some("s", OneOf("a")), Individual("b"))), true, ElPath.Decided),

            //SW7 — INCONSISTENT: the pre-merge collapses x and y onto one representative m; m carries the
            //assertion, so it is the target of its witness's r-edge and gains range(r) = E, and it carries
            //y's D; D ⊓ E ⊑ ⊥ condemns it. The clash COMBINES facts that would split across the raw and the
            //representative atom, so it fires only when the merge is resolved before the assertion interns.
            ("SW7_AssertionInternsOnTheMergeRepresentative", Module(
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x")),
                SameIndividual(Individual("x"), Individual("y")),
                Range("r", Class("E")),
                ClassAssertion(Class("D"), Individual("y")),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("E")]), NothingReference)), false, ElPath.Decided),

            //SW8 — INCONSISTENT: x is D, the conjunction's named operand, and E, asserted directly, and
            //D ⊓ E ⊑ ⊥ condemns it. The conjunction walk pushes the inverse operand into the inverse arm; a
            //top-level-only arm sends it to the default and the module delegates.
            ("SW8_NestedInverseOperandInAConjunction", Module(
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), SomeInverse("r", Class("C"))]), Individual("x")),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("E")]), NothingReference),
                ClassAssertion(Class("E"), Individual("x"))), false, ElPath.Decided),

            //SW9 — INCONSISTENT: x demands an r-predecessor w with w ∈ D and w ∈ E; D ⊓ E ⊑ ⊥ leaves no such
            //element and x is asserted. The complex filler is named as a fresh proxy F carrying F ⊑ D and
            //F ⊑ E through the superclass-intersection split, the conjunction rule fires ⊥ on the witness,
            //and ⊥ back-propagates over the generator edge. The outermost inverse existential matches
            //neither the named-filler inverse arm nor the forward complex-filler catch-all.
            ("SW9_AssertedInverseExistentialComplexFiller", Module(
                ClassAssertion(SomeInverse("r", new OwlObjectIntersectionOf([Class("D"), Class("E")])), Individual("x")),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("E")]), NothingReference)), false, ElPath.Decided),

            //SW25 — CONSISTENT: model Δ = {x, a}, r = {(a, x)}. The asserted inverse singleton nominal is
            //the ground edge (a, x) — the shared shape recognizer's enumeration branch, whose arm precedes
            //the forward complex-filler arm. The complex-filler inverse arm keeps its top-level
            //ObjectOneOf exclusion, whose remaining job is to hold a MULTI-individual enumeration at the
            //default rather than route it through the existential machinery; the survey declines that
            //shape first, so the exclusion is defence in depth for a caller that runs no survey.
            ("SW25_AssertedInverseSingletonNominalFiller", Module(
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x"))), true, ElPath.Decided),

            //SW10 — CONSISTENT: model Δ = {x, a}, r = {(a, x)}. The ObjectHasValue spelling is a different
            //AST type and the same claim: the shared recognizer's HasValue branch writes the identical
            //edge, so the two spellings decide alike.
            ("SW10_AssertedInverseHasValueNominal", Module(
                ClassAssertion(HasValueInverse("r", "a"), Individual("x"))), true, ElPath.Decided),

            //SW11 — CONSISTENT: model Δ = {x, w}, X = {w}, r = {(w, x)}. A union filler is the genuine
            //disjunction the EL fragment does not express, so the filler decomposition records the
            //unsupported marker and the module delegates.
            ("SW11_AssertedInverseExistentialUnionFiller", Module(
                ClassAssertion(SomeInverse("r", new OwlObjectUnionOf([Class("X"), Class("Y")])), Individual("x"))), true, ElPath.Delegated),

            //SW12 — CONSISTENT: owl:topObjectProperty relates every pair, so x has an r-predecessor as soon
            //as C is inhabited; model Δ = {x, w}, C = {w}. The shape is not pointwise constant, so the
            //front-door reserved fold keeps it and the reserved guard on the survey arm and both new
            //normalizer arms declines it.
            ("SW12_AssertedInverseExistentialOverAReservedRole", Module(
                ClassAssertion(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(OwlVocabulary.TopObjectProperty)), Class("C")), Individual("x"))), true, ElPath.Delegated),

            //SW13 — CONSISTENT: model Δ = {s, t, w}, p = {(s, t)}, r = {(w, s)}, C = {w}. Domain(p, ∃r⁻.C)
            //reduces to ∃p.⊤ ⊑ F with F ⊑ ∃g.C, so only p-sources gain F and mint their own predecessor.
            ("SW13_DomainInverseExistential", Module(
                Domain("p", SomeInverse("r", Class("C"))),
                Edge("s", "p", "t")), true, ElPath.Decided),

            //SW14 — INCONSISTENT: s is a p-source, so it needs an r-predecessor; that edge makes s an
            //r-target, and range(r) = E = ∅ condemns it. The domain's fresh atom must reach the generator
            //through the left-naming step.
            ("SW14_DomainInverseExistentialRangeClash", Module(
                Domain("p", SomeInverse("r", Class("C"))),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference),
                Edge("s", "p", "t")), false, ElPath.Decided),

            //SW15 — INCONSISTENT: s gains D from the domain and carries the asserted E, and D ⊓ E ⊑ ⊥
            //condemns it. The superclass-intersection split must carry the widened admission through the
            //nested operands.
            ("SW15_DomainConjunctionWithAnInverseOperand", Module(
                Domain("p", new OwlObjectIntersectionOf([Class("D"), SomeInverse("r", Class("C"))])),
                Edge("s", "p", "t"),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("E")]), NothingReference),
                ClassAssertion(Class("E"), Individual("s"))), false, ElPath.Decided),

            //SW16 — CONSISTENT: nothing is a p-target, so the range never fires; model Δ = {x}, A = B = {x},
            //p = r = ∅. The range's fresh proxy atom is inert without a p-edge.
            ("SW16_RangeInverseExistentialWithoutAnEdge", Module(
                Range("p", SomeInverse("r", Class("C"))),
                SubClassOf(Class("A"), Class("B")),
                ClassAssertion(Class("A"), Individual("x"))), true, ElPath.Decided),

            //SW17 — INCONSISTENT: b is a p-target, so it carries the range proxy and must have an
            //r-predecessor cored C = ∅; ⊥ back-propagates over the generator edge to condemn b. The proxy
            //carries the generator existential only because the naming step normalizes the complex range.
            ("SW17_RangeInverseExistentialEmptyFiller", Module(
                Range("p", SomeInverse("r", Class("C"))),
                SubClassOf(Class("C"), NothingReference),
                Edge("a", "p", "b")), false, ElPath.Decided),

            //SW18 — CONSISTENT: vacuous, since nothing is a p-target; model Δ = {e} with every class and
            //role empty. A range atom that is ⊥ empties only actual p-targets.
            ("SW18_EmptyRangeInverseExistentialWithoutAnEdge", Module(
                Range("p", SomeInverse("r", Class("C"))),
                SubClassOf(Class("C"), NothingReference)), true, ElPath.Decided),

            //SW19 — CONSISTENT: model Δ = the non-positive integers, r = {(i - 1, i)}, C = Δ, P the even
            //positions, Q the odd ones, p = 0. Every element has its r-predecessor, every r-target has an
            //r-predecessor in C, and nothing clashes. The self-referential range names a proxy that itself
            //mints over the generator role, so the range re-enters the mint machinery it types; every
            //position of one content class receives it alike, no chain or self feature reaches the witness
            //closure, and the module decides on shared content keys.
            ("SW19_SelfReferentialRangeOverAFoldShape", Module(
                Range("r", SomeInverse("r", Class("C"))),
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p"))), true, ElPath.Decided),

            //SW20 — CONSISTENT: model Δ = {a} with p = ∅, so the range never fires. An inverse NOMINAL
            //filler stays out of the range position too, the survey's singleton-nominal flag being false
            //there.
            ("SW20_RangeInverseSingletonNominalFiller", Module(
                Range("p", SomeInverse("r", OneOf("a")))), true, ElPath.Delegated),

            //SW21 — CONSISTENT: the parity-ladder model rooted at x — positions x0 = x ∈ T, x1 ∈ Q, x2 ∈ P,
            //alternating, with r = {(x_(i+1), x_i)}. F3 holds at depth 3 and G3 at depth 5, so F3 ⊓ G3 is
            //empty at every position. The witness's key records PackDemand(g, individualAtom) and the
            //coupled edge runs x → witness, so the direction test must recognise an individual owner or the
            //ladder deposits stay unrefined and the mutual recursion folds.
            ("SW21_AssertionRootedParityLadder", Module(
                ClassAssertion(SomeInverse("r", Class("Q")), Individual("x")),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                ClassAssertion(Class("T"), Individual("x")),
                SubClassOf(Some("r", Class("T")), Class("F1")),
                SubClassOf(Some("r", Class("F1")), Class("F2")),
                SubClassOf(Some("r", Class("F2")), Class("F3")),
                SubClassOf(Some("r", Class("F3")), Class("G2")),
                SubClassOf(Some("r", Class("G2")), Class("G3")),
                SubClassOf(new OwlObjectIntersectionOf([Class("F3"), Class("G3")]), NothingReference)), true, ElPath.Decided),

            //SW22 — CONSISTENT: A = ∅ (its witness has an r-successor in X, so it is in Z = ∅); the
            //assertion-rooted chain Δ = {x, w1, w2, …}, Q = {w1, w2, …}, r = {(w_(i+1), w_i)} ∪ {(w1, x)},
            //Y = {x}, is untouched by it. The second-level witness has a DescrByNode core, so its mint
            //decoration is the core label; drop that wrapper and the two chains fold, carrying the
            //condemned class branch onto x.
            ("SW22_AssertionRootedSubtreeStaysDisjoint", Module(
                ClassAssertion(SomeInverse("r", Class("Q")), Individual("x")),
                SubClassOf(Class("A"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("A"), Class("X")),
                SubClassOf(Some("r", Class("X")), Class("Z")),
                SubClassOf(Class("Z"), NothingReference),
                ClassAssertion(Class("Y"), Individual("x"))), true, ElPath.Decided),

            //SW23 — CONSISTENT: model Δ = {x, w1, w2}, C = {w1, w2}, r = {(w1, x), (w2, w1), (w1, w2)} — a
            //2-cycle with no self-loop, so ∃r.Self is empty and B ⊑ ⊥ is vacuous. The cyclic self-fold
            //guard fires at the class-cored depth, where the fold occurs; it cannot fire at the assertion
            //root itself, whose witness is minted and whose owner is an individual atom.
            ("SW23_AssertionRootedCyclicSelfFoldAbstains", Module(
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x")),
                SubClassOf(Class("C"), SomeInverse("r", Class("C"))),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference)), true, ElPath.Delegated),

            //SW24 — CONSISTENT: model Δ = the non-positive integers with r = {(i - 1, i)}, Q the even
            //positions, P the odd ones, x = 0, and a a separate element with no r-edge into it, so
            //∃r.{a} ⊑ B never fires. The left existential keyed on an individual atom is consumed like any
            //other trigger: nothing ever types a position as that individual, so the key never fires on a
            //witness at all, and with no chain or self feature over the witness closure the module decides
            //on shared content keys.
            ("SW24_AssertionRootedFoldWithAnIndividualKeyedLeftExistential", Module(
                ClassAssertion(SomeInverse("r", Class("Q")), Individual("x")),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Some("r", OneOf("a")), Class("B"))), true, ElPath.Decided),
        ];

    /// <summary>The survey-widening battery: every <see cref="SurveyWideningCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void SurveyWideningBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = SurveyWideningCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;

            //A fast-path decision carries the ground-truth verdict; a delegated decision is fragment-relative,
            //so its verdict is held only to the fallback it returns, whose inverse-blind reading may honestly
            //differ from the truth the row states.
            bool verdictOk = expectedPath switch
            {
                (ElPath.Decided) => finalConsistent == trueConsistent,
                (ElPath.Delegated) => finalConsistent == tableauConsistent,
                _ => false,
            };
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + expectedPath + " | " + actualPath + " | " + tableauConsistent + " | " + (ok ? "OK" : "MISMATCH"));
            if(!ok)
            {
                mismatches.Add(name + ": true=" + trueConsistent + " final=" + finalConsistent + " expectedPath=" + expectedPath + " actualPath=" + actualPath);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Inverse nominal and self battery: the assertion-position inverse individual-valued restriction in
    /// both spellings (<c>x : ∃r⁻.{a}</c> and <c>x : ObjectHasValue(r⁻, a)</c>, each the ground edge
    /// <c>(a, x)</c> over <c>r</c>), the superclass and subclass parity spellings, and
    /// <c>ObjectHasSelf</c> over an inverse role. Each module's TRUE consistency is established by an
    /// explicit hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent
    /// of the tableau, which is blind to inverse roles, nominals and self-restrictions alike. Each case
    /// carries its expected decision path beside its verdict, so a case that silently changes tier fails
    /// even when its verdict happens to agree.
    /// </summary>
    /// <returns>Every case as its name, module, TRUE consistency, and expected decision path.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] InverseNominalAndSelfCases() =>
        [
            //IN1 — CONSISTENT: model Δ = {x, a}, r = {(a, x)}. The assertion says x has an r-predecessor
            //which IS a, so it is the single ground fact (a, x) ∈ r — the forward arm's edge with its
            //endpoints exchanged, seeded from two concrete individuals with no witness minted.
            ("IN1_AssertedInverseSingletonNominal", Module(
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x"))), true, ElPath.Decided),

            //IN2 — INCONSISTENT: the edge (a, x) makes x an r-target, so range(r) = E types x, and E ⊑ ⊥
            //condemns it. The seeded edge is a real saturation edge, not an inert record. It does NOT
            //discriminate endpoint order: both endpoints are individual atoms and the range types
            //whichever one the edge targets.
            ("IN2_AssertedInverseNominalRangeClash", Module(
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x")),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference)), false, ElPath.Decided),

            //IN3 — INCONSISTENT: the assertion contributes (a, x) and the role assertion (x, a);
            //asymmetry forbids an edge and its reverse. The endpoint-order pin on the interned index —
            //written unexchanged both facts collapse to the duplicate (x, a), no reverse pair forms, and
            //the module answers a false CONSISTENT.
            ("IN3_AssertedInverseNominalAsymmetryReversePair", Module(
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x")),
                Asymmetric("r"),
                Edge("x", "r", "a")), false, ElPath.Decided),

            //IN4 — INCONSISTENT: a's two r-successors x and y are forced equal by functionality, which
            //DifferentIndividuals(x, y) forbids. The raw re-scan write path: the functional pre-merge runs
            //before interning and re-reads the raw axioms, so an assertion-position spelling that reaches
            //the interned index alone leaves the pre-merge with no second successor to union — a false
            //CONSISTENT while the ground-role gate still admits r.
            ("IN4_FunctionalSuccessorUnionOverTheRawScan", Module(
                Functional("r"),
                Edge("a", "r", "x"),
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("y")),
                Different("x", "y")), false, ElPath.Decided),

            //IN5 — INCONSISTENT: x's two r-predecessors a and b are forced equal by inverse
            //functionality, which DifferentIndividuals(a, b) forbids. The predecessor half of the raw-scan
            //write path; the same omission gives a false CONSISTENT.
            ("IN5_InverseFunctionalPredecessorUnionOverTheRawScan", Module(
                InverseFunctional("r"),
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x")),
                ClassAssertion(SomeInverse("r", OneOf("b")), Individual("x")),
                Different("a", "b")), false, ElPath.Decided),

            //IN6 — INCONSISTENT: as IN3, in the ObjectHasValue spelling. The two spellings are one claim,
            //so this row asserts IN3's verdict over a different AST type, and is the second catcher of an
            //unexchanged write.
            ("IN6_AssertedInverseHasValueAsymmetryReversePair", Module(
                ClassAssertion(HasValueInverse("r", "a"), Individual("x")),
                Asymmetric("r"),
                Edge("x", "r", "a")), false, ElPath.Decided),

            //IN7 — INCONSISTENT: a ≡ b, so the seeded edge is (b, x) and the asserted (x, b) is its
            //reverse, which asymmetry forbids. The caller must resolve its individual through the
            //SameIndividual merge; interning the raw key puts the two edges on different nodes and no
            //reverse pair forms.
            ("IN7_AssertedInverseNominalInternsOnTheMergeRepresentative", Module(
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x")),
                SameIndividual(Individual("a"), Individual("b")),
                Asymmetric("r"),
                Edge("x", "r", "b")), false, ElPath.Decided),

            //IN8 — CONSISTENT: model Δ = {x, a, b}, r = {(a, x)}. A multi-individual enumeration is a
            //genuine disjunction — x has an r-predecessor which is a OR b — and the row pins the SURVEY
            //declining it, which is what keeps the classifier from ever seeing the shape: the shape
            //recognizer's own single-individual restriction is defence in depth for a caller that runs
            //no survey, and the tree has none, so widening the recognizer alone moves no verdict.
            ("IN8_AssertedInverseMultiIndividualNominal", Module(
                ClassAssertion(SomeInverse("r", OneOf("a", "b")), Individual("x"))), true, ElPath.Delegated),

            //IN9 — CONSISTENT: owl:topObjectProperty relates every pair, so the demanded predecessor holds
            //outright; model Δ = {x, a}. The row pins the SURVEY's reserved-role guard, which declines the
            //spelling before the classifier sees it; the guard at the classifier's own call site is
            //defence in depth for a caller that runs no survey, the shape recognizer applying no reserved
            //filtering of its own.
            ("IN9_AssertedInverseNominalOverAReservedRole", Module(
                ClassAssertion(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(OwlVocabulary.TopObjectProperty)), OneOf("a")), Individual("x"))), true, ElPath.Delegated),

            //IN10 — INCONSISTENT: x ≡ a collapses the seeded edge onto one node, making it the self-edge
            //(a, a), which irreflexivity forbids. The new edge participates in the merge-then-scan order,
            //not before it.
            ("IN10_AssertedInverseNominalMergedIntoASelfEdge", Module(
                ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x")),
                Irreflexive("r"),
                SameIndividual(Individual("x"), Individual("a"))), false, ElPath.Decided),

            //IN11 — INCONSISTENT: every A-member has a as its r-predecessor, so x is an r-target and
            //range(r) = E = ∅ condemns it. The control that already decides: the superclass enumeration
            //spelling rides the shipped complex-filler naming, the generator reduction and the superclass
            //singleton-nominal arm with no edit of its own.
            ("IN11_SuperclassInverseNominalRangeClash", Module(
                SubClassOf(Class("A"), SomeInverse("r", OneOf("a"))),
                ClassAssertion(Class("A"), Individual("x")),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference)), false, ElPath.Decided),

            //IN12 — INCONSISTENT: as IN11. The superclass ObjectHasValue rewrite carries its property
            //expression unchanged, so the inverse spelling re-enqueues as ∃r⁻.{a} and asserts IN11's
            //verdict — the superclass parity claim.
            ("IN12_SuperclassInverseHasValueRangeClash", Module(
                SubClassOf(Class("A"), HasValueInverse("r", "a")),
                ClassAssertion(Class("A"), Individual("x")),
                Range("r", Class("E")),
                SubClassOf(Class("E"), NothingReference)), false, ElPath.Decided),

            //IN13 — INCONSISTENT: x has a as an r-predecessor, so x ∈ B = ∅. The subclass inverse
            //individual-valued restriction is a left existential over the synthetic MIRROR role keyed on
            //the individual node — every r-edge forces its reverse mirror edge, so the asserted (a, x)
            //gives x a mirror-successor a. Keyed on the forward role instead, nothing fires.
            ("IN13_SubclassInverseHasValueLeftExistential", Module(
                SubClassOf(HasValueInverse("r", "a"), Class("B")),
                Edge("a", "r", "x"),
                SubClassOf(Class("B"), NothingReference)), false, ElPath.Decided),

            //IN14 — CONSISTENT: A = ∅, so no element forces an r-edge out of a and a is never typed D;
            //model Δ = {a}, A = C = D = ∅, E = {a}, r = ∅. The hypothetical-owner control: the proxy
            //witness the superclass nominal names is not live, so the merge must not pool the domain
            //typing onto the real individual.
            ("IN14_UninhabitedSuperclassInverseNominalOwner", Module(
                SubClassOf(Class("A"), SomeInverse("r", OneOf("a"))),
                Domain("r", Class("D")),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("E")]), NothingReference),
                ClassAssertion(Class("E"), Individual("a"))), true, ElPath.Decided),

            //IN15 — INCONSISTENT: A is inhabited by x, so the module is condemned through the witness.
            //The witness is told it IS a, which is the UNGATED direction of the nominal merge — the
            //carrier inherits the individual's subsumers, so the witness carries a's E — and it is the
            //source of the r-edge onto x, so domain(r) = D types it; D ⊓ E ⊑ ⊥ empties the witness and ⊥
            //back-propagates over the generator edge to condemn x. The inhabited half of the bracket: the
            //gate IN14 holds open is the other direction, the one that would carry the witness's D back
            //onto the real individual.
            ("IN15_InhabitedSuperclassInverseNominalOwner", Module(
                SubClassOf(Class("A"), SomeInverse("r", OneOf("a"))),
                Domain("r", Class("D")),
                SubClassOf(new OwlObjectIntersectionOf([Class("D"), Class("E")]), NothingReference),
                ClassAssertion(Class("E"), Individual("a")),
                ClassAssertion(Class("A"), Individual("x"))), false, ElPath.Decided),

            //IN16 — CONSISTENT: the B9 2-cycle for the r-chain plus the single s-edge (a, p); model
            //Δ = {p, w, a}, P = {p}, Q = {w}, r = {(w, p), (p, w)}, s = {(a, p)}. The assertion-position
            //nominal writes its individual atom to an EDGE ENDPOINT, which the class-space nominal clause
            //excludes, so the fold fence stays open and the module keeps deciding.
            ("IN16_AssertionNominalAcrossAFoldStaysDecided", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(SomeInverse("s", OneOf("a")), Individual("p"))), true, ElPath.Decided),

            //IN17 — CONSISTENT: the same 2-cycle with p's s-predecessor being a; model Δ = {p, w, a},
            //P = {p}, Q = {w}, r = {(w, p), (p, w)}, s = {(a, p)}. The SUPERCLASS nominal tells the filler
            //proxy it is the individual, so the proxy's shared witness denotes the one canonical element
            //that nominal names and pools its constraints onto the individual exactly where an inhabited
            //chain forces the coincidence; no chain or self feature reaches the witness closure, so the
            //module decides on shared content keys. IN16 is the assertion-position sibling.
            ("IN17_SuperclassNominalAcrossAFoldIsDecided", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                SubClassOf(Class("P"), SomeInverse("s", OneOf("a")))), true, ElPath.Decided),

            //IN18 — CONSISTENT: model Δ = {x}, A = {x}, r = {(x, x)}. A self-edge is its own reverse, so
            //∃r⁻.Self and ∃r.Self hold of exactly the same elements and the inverse spelling registers a
            //self-demand on the forward role.
            ("IN18_SuperclassInverseSelfRestriction", Module(
                SubClassOf(Class("A"), HasSelfInverse("r")),
                ClassAssertion(Class("A"), Individual("x"))), true, ElPath.Decided),

            //IN19 — INCONSISTENT: the inverse-spelled demand forces (x, x) ∈ r, exactly what the
            //forward-spelled elimination consumes, so x ∈ B = ∅. The identity's discriminator: register
            //the inverse spelling on anything but the forward role and the elimination never fires.
            ("IN19_InverseSelfDemandFeedsTheForwardElimination", Module(
                SubClassOf(Class("A"), HasSelfInverse("r")),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("A"), Individual("x"))), false, ElPath.Decided),

            //IN20 — INCONSISTENT: the mirror image of IN19 — the forward demand's self-edge is consumed
            //by the inverse-spelled elimination, so x ∈ B = ∅. The identity must hold in the ELIMINATION
            //register too; relax only the demand side and this row stays delegated.
            ("IN20_ForwardSelfDemandFeedsTheInverseElimination", Module(
                SubClassOf(Class("A"), HasSelf("r")),
                SubClassOf(HasSelfInverse("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference),
                ClassAssertion(Class("A"), Individual("x"))), false, ElPath.Decided),

            //IN21 — TRUE inconsistent: A's members each need the self-edge (x, x) ∈ r that irreflexivity
            //forbids, so A = ∅ and x : A condemns the module. The self-demand makes r edge-generating, so
            //the constrained-ground-role gate delegates it exactly as it delegates the forward spelling —
            //a fragment-relative honest miss, the self-blind fallback dropping both constructs and
            //answering consistent, so the delegated verdict is not held to ground truth here.
            ("IN21_IrreflexiveOverAnInverseSelfDemand", Module(
                Irreflexive("r"),
                SubClassOf(Class("A"), HasSelfInverse("r")),
                ClassAssertion(Class("A"), Individual("x"))), false, ElPath.Delegated),

            //IN22 — CONSISTENT: model Δ = {x, a}, A = B = {x}, p = r = ∅, so the range never fires. An
            //inverse nominal stays out of the range position: that seam carries only the
            //singleton-nominal flag, which cannot separate the inverse half from the forward one.
            ("IN22_RangeInverseSingletonNominalFiller", Module(
                Range("p", SomeInverse("r", OneOf("a"))),
                SubClassOf(Class("A"), Class("B")),
                ClassAssertion(Class("A"), Individual("x"))), true, ElPath.Delegated),

            //IN23 — INCONSISTENT: the asserted inverse-spelled demand forces (x, x) ∈ r, which the
            //forward-spelled elimination consumes, so x ∈ B = ∅. The assertion position carries the same
            //identity as the superclass one: the class-assertion arm registers the self-demand on the
            //forward role, where the elimination and every fence already look.
            ("IN23_AssertedInverseSelfDemandFeedsTheForwardElimination", Module(
                ClassAssertion(HasSelfInverse("r"), Individual("x")),
                SubClassOf(HasSelf("r"), Class("B")),
                SubClassOf(Class("B"), NothingReference)), false, ElPath.Decided)
        ];

    /// <summary>The inverse nominal and self battery: every <see cref="InverseNominalAndSelfCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void InverseNominalAndSelfBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = InverseNominalAndSelfCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;

            //A fast-path decision carries the ground-truth verdict; a delegated decision is fragment-relative,
            //so its verdict is held only to the fallback it returns (IN21 is the pinned honest miss where the
            //self-blind fallback answers consistent against a true inconsistency).
            bool verdictOk = expectedPath switch
            {
                (ElPath.Decided) => finalConsistent == trueConsistent,
                (ElPath.Delegated) => finalConsistent == tableauConsistent,
                _ => false,
            };
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.Append(name).Append(" | ").Append(trueConsistent).Append(" | ").Append(finalConsistent).Append(" | ").Append(expectedPath).Append(" | ").Append(actualPath).Append(" | ").Append(tableauConsistent).Append(" | ").AppendLine(ok ? "OK" : "MISMATCH");
            if(!ok)
            {
                mismatches.Add(name);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// Nested-filler and conjunct-spine nominal battery for the class-assertion position: a
    /// single-individual nominal below the top level of an asserted filler, and the nominal shapes on the
    /// asserted class's conjunct spine. Each module's TRUE consistency is established by an explicit
    /// hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent of the
    /// nominal-blind tableau, which cannot witness these gains. Each case carries its expected decision
    /// path beside its verdict, so a case that silently changes tier fails even when its verdict happens to
    /// agree.
    /// </summary>
    /// <returns>Every case as its name, module, TRUE consistency, and expected decision path.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] NestedFillerNominalCases() =>
        [
            //NF1 — CONSISTENT: model Δ = {x, a}, r = {(x, a)}, D = {a}. The filler is named as a proxy told
            //to be a, the proxy is live from the asserted owner, and the merge pools an empty constraint
            //set onto a. Mutation: without the survey's assertion-position nominal flag the shape delegates.
            ("NF1_NominalBelowAForwardFiller", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x"))), true, ElPath.Decided),

            //NF2 — INCONSISTENT: x's r-successor is cored D and told to be a; the successor is live (its
            //owner is an asserted individual), so the merge pools D onto a. a carries E and D ⊓ E ⊑ ⊥, so a
            //is unsatisfiable. Mutation: drop the pooling direction and the module answers consistent.
            ("NF2_NominalBelowAForwardFillerPoolsOntoTheIndividual", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("D"), Class("E"))), false, ElPath.Decided),

            //NF3 — INCONSISTENT: x : ∃r⁻.(D ⊓ {a}) mints x's r-predecessor as a forward generator successor
            //and the mirror writes the real r-edge from that witness to x, so domain(r) = K types the
            //witness. The witness is told to be a and is live, so K pools onto a, which carries E with
            //K ⊓ E ⊑ ⊥. Mutation: without the mirror the witness never bears the domain.
            ("NF3_NominalBelowAnInverseFillerCarriesTheDomain", Module(
                ClassAssertion(SomeInverse("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x")),
                Domain("r", Class("K")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("K"), Class("E"))), false, ElPath.Decided),

            //NF4 — INCONSISTENT: two existentials deep, x's s-successor has an r-successor told to be a.
            //range(r) = C types that successor, liveness cascades along both forward edges, and the merge
            //pools C onto a, which carries D with C ⊓ D ⊑ ⊥. Mutation: a depth-limited admission delegates.
            ("NF4_NominalTwoExistentialsDeepCarriesTheRange", Module(
                ClassAssertion(Some("s", Some("r", OneOf("a"))), Individual("x")),
                Range("r", Class("C")),
                ClassAssertion(Class("D"), Individual("a")),
                Disjoint(Class("C"), Class("D"))), false, ElPath.Decided),

            //NF5 — INCONSISTENT: the ObjectHasValue leaf below a filler rewrites to ∃r.{b} and rides the
            //same proxy path; range(r) = C types the r-successor told to be b, and b carries E with
            //C ⊓ E ⊑ ⊥. Mutation: keep the rewrite top-level only and the leaf records a marker instead.
            ("NF5_HasValueLeafBelowAFillerCarriesTheRange", Module(
                ClassAssertion(Some("s", new OwlObjectIntersectionOf([Class("D"), HasValue("r", "b")])), Individual("x")),
                Range("r", Class("C")),
                ClassAssertion(Class("E"), Individual("b")),
                Disjoint(Class("C"), Class("E"))), false, ElPath.Decided),

            //NF6 — INCONSISTENT: the inverse HasValue leaf below a filler is ∃r⁻.{b}; the generator mints
            //the predecessor and the mirror's real r-edge runs from it, so domain(r) = K types the node
            //told to be b, and b carries E with K ⊓ E ⊑ ⊥. Mutation: exchange the endpoints and the domain
            //lands on the wrong node, leaving the module consistent.
            ("NF6_InverseHasValueLeafBelowAFillerCarriesTheDomain", Module(
                ClassAssertion(Some("s", new OwlObjectIntersectionOf([Class("D"), HasValueInverse("r", "b")])), Individual("x")),
                Domain("r", Class("K")),
                ClassAssertion(Class("E"), Individual("b")),
                Disjoint(Class("K"), Class("E"))), false, ElPath.Decided),

            //NF7 — CONSISTENT: model Δ = {x, a}, r = {(x, a)}, D = E = {a}, G = ∅. The consistent variant of
            //NF2, and the one-axiom half of the spelling differential
            //NestedFillerNominalSpellingMatchesTheTwoAxiomForm pins against its two-axiom twin.
            ("NF7_SpellingIdentityConsistentVariant", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("E"), Class("G"))), true, ElPath.Decided),

            //NF8 — INCONSISTENT: the spine edge spelling ∃r.{a} is the asserted edge (r, x, a), so range(r)
            //types a directly with C; a carries E and C ⊓ E ⊑ ⊥. Mutation: route the spine edge through the
            //existential machinery instead and the range types a fresh successor, not a.
            ("NF8_SpineEdgeSpellingCarriesTheRange", Module(
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), Some("r", OneOf("a"))]), Individual("x")),
                Range("r", Class("C")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("C"), Class("E"))), false, ElPath.Decided),

            //NF9 — INCONSISTENT: Functional(r) bounds x to one r-successor, and x has two asserted ones —
            //a through the spine edge spelling and b through the role assertion — so a = b, which
            //DifferentIndividuals(a, b) forbids. The collapse runs in the pre-merge, which re-reads the
            //axioms, so the spine edge must reach the raw scan as well as the interned index: withhold the
            //spine walk there and the module answers false-CONSISTENT on a decided path. The fence stays
            //clear because an edge endpoint is not a told subsumer.
            ("NF9_SpineEdgeReachesTheFunctionalPreMerge", Module(
                Functional("r"),
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), Some("r", OneOf("a"))]), Individual("x")),
                Edge("x", "r", "b"),
                Different("a", "b")), false, ElPath.Decided),

            //NF10 — INCONSISTENT: the spine's inverse HasValue is the same edge with its endpoints
            //exchanged, (r, a, x), so a is an r-source and domain(r) = K types it; a carries E and
            //K ⊓ E ⊑ ⊥. Mutation: write the forward endpoints and the domain misses a.
            ("NF10_SpineInverseHasValueCarriesTheDomain", Module(
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), HasValueInverse("r", "a")]), Individual("x")),
                Domain("r", Class("K")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("K"), Class("E"))), false, ElPath.Decided),

            //NF11 — INCONSISTENT: the spine's bare nominal is the told identity x = a, folded into the
            //union-find before interning, so the distinctness scan finds one representative for x and a.
            //Mutation (two distinct failures): drop the spine fold and the collision vanishes; drop the
            //enumeration arm of the type walk and a spurious marker delegates the module instead.
            ("NF11_SpineBareNominalFoldsAgainstDistinctness", Module(
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), OneOf("a")]), Individual("x")),
                Different("x", "a")), false, ElPath.Decided),

            //NF12 — INCONSISTENT: the spine fold x = a precedes the functional collapse, so x's u-edge and
            //a's v-edge share a source representative, Functional(r) unions u and v, and
            //DifferentIndividuals(u, v) clashes. Mutation: fold after the collapse and the two edges keep
            //separate sources, leaving the module consistent.
            ("NF12_SpineBareNominalFoldPrecedesTheFunctionalCollapse", Module(
                Functional("r"),
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), OneOf("a")]), Individual("x")),
                Edge("x", "r", "u"),
                Edge("a", "r", "v"),
                Different("u", "v")), false, ElPath.Decided),

            //NF13 — CONSISTENT: model Δ = {x, a}, r = {(x, a)}, D = {a}, with the enumeration satisfied by
            //its first individual. A multi-individual enumeration is a genuine disjunction the fragment
            //does not express, so it stays delegated below a filler.
            ("NF13_MultiIndividualEnumerationBelowAFiller", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a", "b")])), Individual("x"))), true, ElPath.Delegated),

            //NF14 — CONSISTENT: model Δ = {a, b}, x = a, D = {a}. The same disjunction on the conjunct
            //spine stays delegated.
            ("NF14_MultiIndividualEnumerationOnTheSpine", Module(
                ClassAssertion(new OwlObjectIntersectionOf([Class("D"), OneOf("a", "b")]), Individual("x"))), true, ElPath.Delegated),

            //NF15 — CONSISTENT: owl:topObjectProperty relates every pair, so the restriction holds of
            //everything; model Δ = {x, w, a}, r = {(x, w)}, D = {w}. The reserved guard declines the
            //spelling at depth exactly as it does at the top level. The enumeration spelling carries the
            //row: a top existential with a non-Thing filler is a global non-emptiness assertion the
            //front-door reserved fold keeps verbatim, so it reaches the survey guard, whereas the
            //ObjectHasValue spelling of the same claim is pointwise constant and folds to owl:Thing before
            //any survey runs.
            ("NF15_ReservedRoleSpellingBelowAFiller", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty)), OneOf("a"))])), Individual("x"))), true, ElPath.Delegated),

            //NF16 — INCONSISTENT: one spine carries two edge spellings, (r, x, a) and (s, x, b); range(r)
            //types a with C against its E, and range(s) types b with K against its L, so either clash
            //condemns the module. Mutation: collect only the first spine match and one of the two edges is
            //lost at both write sites.
            ("NF16_TwoEdgeSpellingsOnOneSpine", Module(
                ClassAssertion(new OwlObjectIntersectionOf([Some("r", OneOf("a")), Some("s", OneOf("b"))]), Individual("x")),
                Range("r", Class("C")),
                Range("s", Class("K")),
                ClassAssertion(Class("E"), Individual("a")),
                ClassAssertion(Class("L"), Individual("b")),
                Disjoint(Class("C"), Class("E")),
                Disjoint(Class("K"), Class("L"))), false, ElPath.Decided),

            //NF17 — INCONSISTENT: the spine is a nested intersection tree, (D ⊓ E) ⊓ {a}; the enumerator
            //flattens it, so the bare nominal is found and the fold x = a fires against the asserted
            //distinctness. Mutation: match the top-level operands only and the nested tree hides nothing
            //here, but a nominal one level deeper is missed.
            ("NF17_NestedSpineTreeFlattensForTheFold", Module(
                ClassAssertion(new OwlObjectIntersectionOf([new OwlObjectIntersectionOf([Class("D"), Class("E")]), OneOf("a")]), Individual("x")),
                Different("x", "a")), false, ElPath.Decided),

            //NF18 — INCONSISTENT through the union-find chain alone: both spine nominals fold pre-intern,
            //x = a and then x = b onto the same representative, so the distinctness scan sees one node for
            //a and b. The fold compounds only because the subject is re-resolved through the union-find per
            //spine nominal rather than cached per axiom. No told subsumer is an individual atom here, so
            //the saturation-identity fence's discovery surface stays empty and the module decides — the
            //boundary between the two identity regimes.
            ("NF18_TwoSpineNominalsChainThroughTheUnionFind", Module(
                ClassAssertion(new OwlObjectIntersectionOf([OneOf("a"), OneOf("b")]), Individual("x")),
                Different("a", "b")), false, ElPath.Decided)
        ];

    /// <summary>The nested-filler and spine nominal battery: every <see cref="NestedFillerNominalCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void NestedFillerNominalBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = NestedFillerNominalCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;

            //A fast-path decision carries the ground-truth verdict; a delegated decision is fragment-relative,
            //so its verdict is held only to the fallback it returns.
            bool verdictOk = expectedPath switch
            {
                (ElPath.Decided) => finalConsistent == trueConsistent,
                (ElPath.Delegated) => finalConsistent == tableauConsistent,
                _ => false,
            };
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.Append(name).Append(" | ").Append(trueConsistent).Append(" | ").Append(finalConsistent).Append(" | ").Append(expectedPath).Append(" | ").Append(actualPath).Append(" | ").Append(tableauConsistent).Append(" | ").AppendLine(ok ? "OK" : "MISMATCH");
            if(!ok)
            {
                mismatches.Add(name);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>The spelling identity: a nominal below the top level of an asserted filler decides exactly as the two-axiom module that names the filler's carrier — same consistency verdict and same subsumption set once both are projected onto the classes the two modules share. The two-axiom spelling's carrier class is outside that shared signature by construction, so the projection compares the fragment the spellings genuinely have in common.</summary>
    [TestMethod]
    public void NestedFillerNominalSpellingMatchesTheTwoAxiomForm()
    {
        ReasoningModule oneAxiom = Module(
            ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x")),
            ClassAssertion(Class("E"), Individual("a")),
            Disjoint(Class("E"), Class("G")));
        ReasoningModule twoAxiom = Module(
            ClassAssertion(Class("Carrier"), Individual("x")),
            SubClassOf(Class("Carrier"), Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")]))),
            ClassAssertion(Class("E"), Individual("a")),
            Disjoint(Class("E"), Class("G")));

        ModuleDecision oneDecision = ElCoupledModuleReasoner.DecideModule(oneAxiom, TestContext.CancellationToken);
        ModuleDecision twoDecision = ElCoupledModuleReasoner.DecideModule(twoAxiom, TestContext.CancellationToken);
        Assert.IsTrue(oneDecision.Statistics.ElTotals.ElDecided, "The one-axiom spelling is decided by the EL fast-path.");
        Assert.IsTrue(twoDecision.Statistics.ElTotals.ElDecided, "The two-axiom spelling is decided by the EL fast-path.");
        Assert.AreEqual(twoDecision.Verdict!.IsConsistent, oneDecision.Verdict!.IsConsistent, "The two spellings agree on consistency.");

        HashSet<Utf8String> shared = [.. AlcModuleReasoner.Translate(oneAxiom).SignatureClasses];
        shared.IntersectWith(AlcModuleReasoner.Translate(twoAxiom).SignatureClasses);
        Assert.AreSequenceEqual(ProjectOntoSignature(twoDecision.Verdict, shared), ProjectOntoSignature(oneDecision.Verdict, shared), "The two spellings agree on the subsumption set over the classes they share.");
    }

    /// <summary>
    /// Ground-identity completion battery: the restart loop that replays a saturation-discovered identity
    /// into the pre-intern regime, and the ground-spine rewrite that turns a nominal-bearing asserted filler
    /// into the ground edge it states. Each module's TRUE consistency is established by an explicit
    /// hand-built model (consistent) or an explicit unsat derivation (inconsistent), independent of the
    /// nominal-blind tableau, which cannot witness these gains. Each case carries its expected decision path
    /// beside its verdict, so a case that silently changes tier fails even when its verdict happens to agree.
    /// </summary>
    /// <returns>Every case as its name, module, TRUE consistency, and expected decision path.</returns>
    private static (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] GroundIdentityCompletionCases() =>
        [
            //INCONSISTENT: x inhabits A ⊑ {a}, so pass one discovers x = a; the rebuild's functional collapse
            //then unions x's u-successor with a's v-successor, and the merged node carries P from u and Q
            //from v, so P ⊓ Q ⊑ {c} discovers u = c on the SECOND pass. The third pass's distinctness scan
            //reads that identity and clashes. Mutation: cap the loop at one rebuild and the module answers
            //consistent, because the conjunction-carried nominal is unreachable until the collapse has run.
            ("TwoRebuildChainThroughAFunctionalCollapse", Module(
                Functional("r"),
                ClassAssertion(Class("A"), Individual("x")),
                SubClassOf(Class("A"), OneOf("a")),
                Edge("x", "r", "u"),
                Edge("a", "r", "v"),
                ClassAssertion(Class("P"), Individual("u")),
                ClassAssertion(Class("Q"), Individual("v")),
                SubClassOf(new OwlObjectIntersectionOf([Class("P"), Class("Q")]), OneOf("c")),
                Different("u", "c")), false, ElPath.Decided),

            //INCONSISTENT on the first pass, with the completion gate open: y carries two disjoint types, so
            //⊥ is derived before any identity is replayed and the verdict stands with no rebuild — every
            //subsumption the pass derived is entailed without the identities a rebuild would add. Mutation:
            //a loop that delegated a gate-open module, or that ran to its structural bound before reading the
            //verdict, fails this row.
            ("GateOpenModuleAlreadyInconsistentDecidesOnTheFirstPass", Module(
                SubClassOf(Class("A"), OneOf("a")),
                ClassAssertion(Class("A"), Individual("x")),
                Different("x", "a"),
                Disjoint(Class("C"), Class("D")),
                ClassAssertion(Class("C"), Individual("y")),
                ClassAssertion(Class("D"), Individual("y"))), false, ElPath.Decided),

            //INCONSISTENT: the outer filler spine carries no nominal, so nothing grounds and the anonymous
            //proxy below it keeps both nominals. The sweep reads that inhabited carrier, discovers a = b, and
            //the rebuild's distinctness scan clashes. Mutation: drop the restart loop and the module answers
            //false-CONSISTENT on a decided path.
            ("NominalPairBelowANominalFreeLayerIsCaughtByTheSweep", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("C"), Some("s", new OwlObjectIntersectionOf([OneOf("a"), OneOf("b")]))])), Individual("x")),
                Different("a", "b")), false, ElPath.Decided),

            //INCONSISTENT: the filler spine grounds onto a, so the spine's own existential is the further
            //ground edge (a, b) ∈ s and domain(s) = K types a — the recursion's anchor. a carries E with
            //K ⊓ E ⊑ ⊥. Mutation: anchor the descent on the subject instead of the nominal and K lands on x,
            //leaving the module consistent.
            ("ChainedGroundEdgesCarryTheSecondEdgeDomain", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a"), Some("s", OneOf("b"))])), Individual("x")),
                Domain("s", Class("K")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("K"), Class("E"))), false, ElPath.Decided),

            //INCONSISTENT: with the filler grounded, r bears no right existential at all, so it stops being
            //edge-generating and the ground-only gate admits the functional characteristic it used to refuse.
            //x's two asserted r-successors a and b are then unioned, which DifferentIndividuals(a, b)
            //forbids. Mutation: keep the filler on the proxy path and r is edge-generating again, so the
            //functional fence delegates the whole module.
            ("GroundRewriteAdmitsTheFunctionalRoleAndMergesTheEndpoints", Module(
                Functional("r"),
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x")),
                Edge("x", "r", "b"),
                Different("a", "b")), false, ElPath.Decided),

            //INCONSISTENT: the consumer sits on the SECOND, recursive edge — Functional(s) unions the spine's
            //(a, b) with the asserted (a, c) — so the raw pre-merge re-scan must descend through the nominal
            //exactly as the interned index does. Mutation: make the re-scan single-level and the module
            //answers false-CONSISTENT on a decided path, the merge silently skipped while the gate still
            //admits s.
            ("RecursiveGroundEdgeReachesTheFunctionalPreMerge", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a"), Some("s", OneOf("b"))])), Individual("x")),
                Functional("s"),
                Edge("a", "s", "c"),
                Different("b", "c")), false, ElPath.Decided),

            //INCONSISTENT: two nominals on one grounded filler spine are told identities of each other, so
            //the pre-intern fold collapses a and b onto one node carrying C from a and E from b, and
            //C ⊓ E ⊑ ⊥. Mutation: fold only the first nominal against the subject and the two individuals
            //stay apart, leaving the module consistent.
            ("SiblingNominalsOnAFillerSpineFoldOntoOneNode", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([OneOf("a"), OneOf("b")])), Individual("x")),
                ClassAssertion(Class("C"), Individual("a")),
                ClassAssertion(Class("E"), Individual("b")),
                Disjoint(Class("C"), Class("E"))), false, ElPath.Decided),

            //CONSISTENT: model Δ = {a}, A = ∅, E = {a}, D = ∅, r = ∅. The ground rewrite fires for asserted
            //subjects only, so a superclass occurrence writes no edge and asserts nothing on a; A is
            //uninhabited, the existential-filler proxy never becomes live, and D never reaches a. Mutation:
            //run the walk on the subclass side too and a is told D against its disjoint E, wrongly condemning
            //a module whose carrier has no member.
            ("UninhabitedSuperclassCarrierWritesNoGroundEdge", Module(
                SubClassOf(Class("A"), Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")]))),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("D"), Class("E"))), true, ElPath.Decided),

            //INCONSISTENT: the same superclass occurrence with an inhabited carrier decides through the
            //pooling path — x makes the proxy live, so the proxy's D pools onto the individual it is told to
            //be, and a already carries the disjoint E. The control for the row above: the difference between
            //them is inhabitation and nothing else.
            ("InhabitedSuperclassCarrierPoolsOntoTheIndividual", Module(
                SubClassOf(Class("A"), Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")]))),
                ClassAssertion(Class("A"), Individual("x")),
                ClassAssertion(Class("E"), Individual("a")),
                Disjoint(Class("D"), Class("E"))), false, ElPath.Decided),

            //CONSISTENT: model Δ = {x, a, b}, r = {(x, a)}, D = {a}; the universal role relates every pair, so
            //the reserved restriction holds of a. A reserved-role spelling BELOW a grounded spine is declined
            //exactly as one at the top level is, so the module delegates and the reserved role never enters
            //the ground edge family. Mutation: inherit the enclosing spine's admission and a role whose fixed
            //extension the calculus does not interpret is written as a ground edge.
            ("ReservedRoleSpellingBelowAGroundedSpineIsDelegated", Module(
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a"), new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty)), OneOf("b"))])), Individual("x"))), true, ElPath.Delegated),

            //CONSISTENT: model Δ = {x} with x = a, r = {(x, x)}, D = {x}; one element has one universal-role
            //successor, so functionality on the universal role holds. The companion invariant of the row
            //above: a reserved role is never registered functional, because the module carrying the
            //characteristic is declined whole. Mutation: admit the characteristic and the pre-merge unions
            //endpoints over a role whose extension the calculus does not interpret.
            ("ReservedRoleIsNeverRegisteredFunctional", Module(
                new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty))) { Origin = Origin("functionaltop") },
                ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x"))), true, ElPath.Delegated),

            //INCONSISTENT: the class-carried discovery x = a lands on a pair that each carry an inverse
            //existential, so the rebuild mints both witnesses from ONE owner. Their demand sets differ by
            //their cores, so the ownership ledger keeps them apart and no cross-owner abstention fires; the
            //rebuilt distinctness scan then clashes on x = a. Mutation: reconcile the mint on the forward
            //edge alone and the rebuild abstains instead of deciding.
            ("CoupledMintReconcilesAcrossTheGroundIdentityRebuild", Module(
                SubClassOf(Class("A"), OneOf("a")),
                ClassAssertion(Class("A"), Individual("x")),
                ClassAssertion(SomeInverse("r", Class("C")), Individual("x")),
                ClassAssertion(SomeInverse("r", Class("K")), Individual("a")),
                Different("x", "a")), false, ElPath.Decided),

            //CONSISTENT: the mutually recursive inverse existentials fold two owners onto one witness, which
            //the fold-safety fence accepts only when no machinery can distinguish the folded positions. The
            //asserted filler nominal is the module's only class-space nominal, and the ground rewrite leaves
            //it as an edge endpoint instead, so the fence clears and the fold is accepted. Model Δ = {p, w, a},
            //P = {p}, Q = {w}, r = {(w, p), (p, w)}, s = {(p, a)}, D = {a}. Mutation: keep the nominal in
            //class space and the fence refuses the fold, delegating a module the calculus decides.
            ("GroundRewriteClearsTheFoldSafetyNominalClause", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                ClassAssertion(Class("P"), Individual("p")),
                ClassAssertion(Some("s", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("p"))), true, ElPath.Decided)
        ];

    /// <summary>The ground-identity completion battery: every <see cref="GroundIdentityCompletionCases"/> case's EL-coupled verdict and decision path match its ground truth; the report names every offender.</summary>
    [TestMethod]
    public void GroundIdentityCompletionBattery()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent, ElPath ExpectedPath)[] cases = GroundIdentityCompletionCases();

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | true | final | expectedPath | actualPath | tableau | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool trueConsistent, ElPath expectedPath) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            ElPath actualPath = decision.Statistics.ElTotals.ElDecided ? ElPath.Decided : ElPath.Delegated;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;

            //A fast-path decision carries the ground-truth verdict; a delegated decision is fragment-relative,
            //so its verdict is held only to the fallback it returns.
            bool verdictOk = expectedPath switch
            {
                (ElPath.Decided) => finalConsistent == trueConsistent,
                (ElPath.Delegated) => finalConsistent == tableauConsistent,
                _ => false,
            };
            bool pathOk = actualPath == expectedPath;
            bool ok = verdictOk && pathOk;
            report.Append(name).Append(" | ").Append(trueConsistent).Append(" | ").Append(finalConsistent).Append(" | ").Append(expectedPath).Append(" | ").Append(actualPath).Append(" | ").Append(tableauConsistent).Append(" | ").AppendLine(ok ? "OK" : "MISMATCH");
            if(!ok)
            {
                mismatches.Add(name);
            }
        }

        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// The functional collapse runs across the ground-identity rebuild. The base module states
    /// <c>Functional(r)</c>, an inhabited carrier <c>A ⊑ {a}</c>, one r-edge from each of the two individuals
    /// the carrier identifies, and a second nominal-bearing class on the far successor: the identity the
    /// first pass discovers is what makes the two r-sources one, so the collapse the second pass runs is
    /// unreachable on a single pass. It is consistent — model Δ = {x, y}, x = a, y = y1 = y2 = c — and the
    /// clash variant adds the distinctness the post-collapse identity contradicts, so a decider that never
    /// rebuilds answers it consistent.
    /// </summary>
    [TestMethod]
    public void FunctionalCollapseRunsAcrossTheGroundIdentityRebuild()
    {
        ReasoningModule settling = Module(
            Functional("r"),
            ClassAssertion(Class("A"), Individual("x")),
            SubClassOf(Class("A"), OneOf("a")),
            Edge("x", "r", "y1"),
            Edge("a", "r", "y2"),
            ClassAssertion(Class("B"), Individual("y2")),
            SubClassOf(Class("B"), OneOf("c")));

        ModuleDecision settlingDecision = ElCoupledModuleReasoner.DecideModule(settling, TestContext.CancellationToken);
        Assert.IsTrue(settlingDecision.Statistics.ElTotals.ElDecided, "The rebuild sequence settles inside the structural bound, so the EL fast-path decides the module.");
        Assert.IsTrue(settlingDecision.Verdict!.IsConsistent, "x = a and y1 = y2 = c is a model; nothing forces a clash.");

        ReasoningModule clashing = Module(
            Functional("r"),
            ClassAssertion(Class("A"), Individual("x")),
            SubClassOf(Class("A"), OneOf("a")),
            Edge("x", "r", "y1"),
            Edge("a", "r", "y2"),
            ClassAssertion(Class("B"), Individual("y2")),
            SubClassOf(Class("B"), OneOf("c")),
            Different("y1", "c"));

        ModuleDecision clashingDecision = ElCoupledModuleReasoner.DecideModule(clashing, TestContext.CancellationToken);
        Assert.IsTrue(clashingDecision.Statistics.ElTotals.ElDecided, "The clash variant is decided by the same rebuild sequence.");
        Assert.IsFalse(clashingDecision.Verdict!.IsConsistent, "The rebuild's functional collapse unions y1 with y2, which the discovered y2 = c already merged into c, so DifferentIndividuals(y1, c) is contradicted.");
        Assert.IsTrue(AlcModuleReasoner.Decide(clashing, TestContext.CancellationToken).IsConsistent, "The nominal-blind tableau drops both nominal inclusions and answers consistent.");
    }

    /// <summary>The ground spelling of a nominal-bearing filler decides exactly as the two-axiom module that states the edge and the typing separately — same consistency verdict and same subsumption set over the classes the two share. The asymmetric role makes the two paths separable: the ground edge is what the asserted-edge clash scan reads, and its ENDPOINTS are what the scan matches against the reverse edge, so a spelling that routed the filler through the existential machinery, or that exchanged the edge's endpoints, would diverge from its two-axiom twin.</summary>
    [TestMethod]
    public void GroundFillerSpellingMatchesTheTwoAxiomFormUnderAnAsymmetricRole()
    {
        ReasoningModule oneAxiom = Module(
            Asymmetric("r"),
            ClassAssertion(Some("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a")])), Individual("x")),
            Edge("a", "r", "x"));
        ReasoningModule twoAxiom = Module(
            Asymmetric("r"),
            ClassAssertion(Some("r", OneOf("a")), Individual("x")),
            ClassAssertion(Class("D"), Individual("a")),
            Edge("a", "r", "x"));

        ModuleDecision oneDecision = ElCoupledModuleReasoner.DecideModule(oneAxiom, TestContext.CancellationToken);
        ModuleDecision twoDecision = ElCoupledModuleReasoner.DecideModule(twoAxiom, TestContext.CancellationToken);
        Assert.IsTrue(oneDecision.Statistics.ElTotals.ElDecided, "The one-axiom spelling is decided by the EL fast-path.");
        Assert.IsTrue(twoDecision.Statistics.ElTotals.ElDecided, "The two-axiom spelling is decided by the EL fast-path.");
        Assert.IsFalse(oneDecision.Verdict!.IsConsistent, "The asserted (x, a) and (a, x) edges are a reverse pair over an asymmetric role.");
        Assert.AreEqual(twoDecision.Verdict!.IsConsistent, oneDecision.Verdict.IsConsistent, "The two spellings agree on consistency.");

        HashSet<Utf8String> shared = [.. AlcModuleReasoner.Translate(oneAxiom).SignatureClasses];
        shared.IntersectWith(AlcModuleReasoner.Translate(twoAxiom).SignatureClasses);
        Assert.AreSequenceEqual(ProjectOntoSignature(twoDecision.Verdict, shared), ProjectOntoSignature(oneDecision.Verdict, shared), "The two spellings agree on the subsumption set over the classes they share.");
    }

    /// <summary>Three layers of inverse ground composition decide exactly as the four-axiom module that spells each layer out. Every layer exchanges its OWN endpoints — the outer <c>∃r⁻</c> gives (a, x) and the inner <c>∃s⁻</c> gives (b, a) — while the anchors descend x, a, b, so the asserted (a, b) edge is the reverse of the composition's inner edge over the asymmetric role. A composition that carried one layer's direction into the next, or that anchored the inner layer on x, would not clash.</summary>
    [TestMethod]
    public void ThreeLayerInverseGroundCompositionMatchesTheSpelledForm()
    {
        ReasoningModule oneAxiom = Module(
            Asymmetric("s"),
            ClassAssertion(SomeInverse("r", new OwlObjectIntersectionOf([Class("D"), OneOf("a"), SomeInverse("s", new OwlObjectIntersectionOf([Class("E"), OneOf("b")]))])), Individual("x")),
            Edge("a", "s", "b"));
        ReasoningModule fourAxiom = Module(
            Asymmetric("s"),
            ClassAssertion(SomeInverse("r", OneOf("a")), Individual("x")),
            ClassAssertion(Class("D"), Individual("a")),
            ClassAssertion(SomeInverse("s", OneOf("b")), Individual("a")),
            ClassAssertion(Class("E"), Individual("b")),
            Edge("a", "s", "b"));

        ModuleDecision oneDecision = ElCoupledModuleReasoner.DecideModule(oneAxiom, TestContext.CancellationToken);
        ModuleDecision fourDecision = ElCoupledModuleReasoner.DecideModule(fourAxiom, TestContext.CancellationToken);
        Assert.IsTrue(oneDecision.Statistics.ElTotals.ElDecided, "The one-axiom composition is decided by the EL fast-path.");
        Assert.IsTrue(fourDecision.Statistics.ElTotals.ElDecided, "The four-axiom spelling is decided by the EL fast-path.");
        Assert.IsFalse(oneDecision.Verdict!.IsConsistent, "The composition's inner edge (b, a) and the asserted (a, b) are a reverse pair over an asymmetric role.");
        Assert.AreEqual(fourDecision.Verdict!.IsConsistent, oneDecision.Verdict.IsConsistent, "The two spellings agree on consistency.");

        HashSet<Utf8String> shared = [.. AlcModuleReasoner.Translate(oneAxiom).SignatureClasses];
        shared.IntersectWith(AlcModuleReasoner.Translate(fourAxiom).SignatureClasses);
        Assert.AreSequenceEqual(ProjectOntoSignature(fourDecision.Verdict, shared), ProjectOntoSignature(oneDecision.Verdict, shared), "The two spellings agree on the subsumption set over the classes they share.");
    }

    /// <summary>B9 capability — the mutually recursive superclass inverse existentials <c>P ⊑ ∃r⁻.Q</c>, <c>Q ⊑ ∃r⁻.P</c> fold two distinct owners onto one witness; the module carries no machinery to distinguish the folded positions, so the fold-safety fence accepts the fold and the fast-path decides it consistent — a decision the inverse-blind tableau cannot witness.</summary>
    [TestMethod]
    public void MutualRecursionBackwardMintingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
            SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
            ClassAssertion(Class("P"), Individual("p")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The fold-safe mutual recursion is decided by the EL fast-path, not delegated.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The mutual cycle has a model (the 2-cycle {p, q}), so it is consistent.");
        Assert.IsTrue(decision.Verdict.IsDecisive, "A whole-module fast-path decision names no excluded construct.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The decision is whole-module Decided, not fragment-relative.");
    }

    /// <summary>B12b capability — the symmetric mixed cycle <c>Symmetric(r)</c>, <c>A ⊑ ∃r⁻.B</c>, <c>B ⊑ ∃r.A</c> drives the <c>g ↔ r</c> mutual-minting regime; with no position-distinguishing machinery the fence clears, the cross-owner fold is accepted, and the fast-path decides it consistent.</summary>
    [TestMethod]
    public void SymmetricMixedCycleBackwardMintingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
            SubClassOf(Class("B"), Some("r", Class("A"))),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The fold-safe symmetric mixed cycle is decided by the EL fast-path, not delegated.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The cycle has a 2-element model, so it is consistent.");
        Assert.IsTrue(decision.Verdict.IsDecisive, "A whole-module fast-path decision names no excluded construct.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The decision is whole-module Decided.");
    }

    /// <summary>D1 — a functional forward role whose inverse appears in a superclass existential delegates: the generator pairing makes <c>r</c> a mirror target, and a functional mirror target gains a non-asserted successor the pre-merge scan cannot see, so the functional fence delegates the whole module. TRUE consistent; the pin proves the fence sees the generator pairing.</summary>
    [TestMethod]
    public void FunctionalForwardRoleWithBackwardMintingDelegates()
    {
        ReasoningModule module = Module(
            Functional("r"),
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDelegatesLike(module, expectConsistent: true);
    }

    /// <summary>D2 — an inverse-functional forward role whose inverse appears in a superclass existential delegates via the same mirror-target functional fence. TRUE consistent.</summary>
    [TestMethod]
    public void InverseFunctionalForwardRoleWithBackwardMintingDelegates()
    {
        ReasoningModule module = Module(
            InverseFunctional("r"),
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDelegatesLike(module, expectConsistent: true);
    }

    /// <summary>GF1 capability — a transitive forward role whose inverse appears in a superclass existential is DECIDED by the EL fast-path: the forward role's own self-transitivity <c>r ∘ r ⊑ r</c> sits inside the generator fence's admitted slice, so the per-owner mint writes a's real r-predecessor edge and the fast-path decides the module. Consistent: model Δ = {a, w}, A = {a}, C = {w}, r = {(w, a)}.</summary>
    [TestMethod]
    public void TransitiveForwardRoleWithBackwardMintingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Transitive("r"),
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The self-transitive forward role sits inside the generator fence's admitted slice and the EL fast-path decides the module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "a gains its r-predecessor w in C via the minted witness; nothing clashes.");
    }

    /// <summary>D4 — a forward role whose SUPER-role is transitive delegates: the witness r-edge promotes to the super-role <c>t</c>, which chains, so the fence's UPWARD role closure over the generator's forward role must catch it. TRUE consistent — the key fence test.</summary>
    [TestMethod]
    public void ChainViaSuperRoleWithBackwardMintingDelegates()
    {
        ReasoningModule module = Module(
            SubProperty("r", "t"),
            Transitive("t"),
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDelegatesLike(module, expectConsistent: true);
    }

    /// <summary>GF6 capability — a self-demand on the generator's forward role itself is DECIDED by the EL fast-path: <c>A2 ⊑ ∃r.Self</c> puts the self-demand on the forward role <c>r</c>, inside the generator fence's (R-b) admitted slice. Consistent: model Δ = {a, w}, A = {a}, C = {w}, r = {(w, a)}, A2 = ∅ — nothing forces a self-edge and a's r-predecessor w is in C.</summary>
    [TestMethod]
    public void SelfDemandOnForwardRoleWithBackwardMintingIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A2"), HasSelf("r")),
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The self-demand on the forward role itself is admitted by the relaxed generator fence and decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A2 is empty so no self-edge is forced; a gains its r-predecessor w in C and nothing clashes.");
    }

    /// <summary>D6 capability — an inverse existential in the class-assertion position is DECIDED by the EL fast-path: the assertion arm reduces <c>∃r⁻.C</c> on the individual's atom to a forward existential over the synthetic per-<c>r</c> generator role (<c>g ⊑ r⁻</c>), and a class assertion names a concrete individual, so the minted <c>r</c>-predecessor is forced from an inhabited node rather than hypothetical. Consistent: model Δ = {x, w}, C = {w}, r = {(w, x)}.</summary>
    [TestMethod]
    public void AssertionPositionInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(SomeInverse("r", Class("C")), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The assertion arm reduces the inverse existential on the individual atom, so the EL fast-path decides the module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x gains its forced r-predecessor w in C; nothing clashes.");
    }

    /// <summary>D7 capability — an inverse existential in a property-domain class is DECIDED by the EL fast-path: the domain axiom normalizes to <c>∃p.⊤ ⊑ F</c> with <c>F ⊑ ∃r⁻.C</c>, whose superclass occurrence rides the generator reduction, so exactly the <c>p</c>-sources gain <c>F</c> and mint their own predecessor. Consistent: model Δ = {s, t, w}, p = {(s, t)}, r = {(w, s)}, C = {w}.</summary>
    [TestMethod]
    public void DomainPositionInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Domain("p", SomeInverse("r", Class("C"))),
            Edge("s", "p", "t"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The domain class reaches the generator reduction through the inclusion the domain axiom normalizes to, so the EL fast-path decides the module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The p-source s gains its forced r-predecessor w in C; nothing clashes.");
    }

    /// <summary>D8 — an inverse existential with a UNION filler delegates: the filler decomposition records an unsupported marker for the disjunction on the superclass side, so the module abstains. TRUE consistent.</summary>
    [TestMethod]
    public void UnionFillerBackwardMintingDelegates()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), SomeInverse("r", new OwlObjectUnionOf([Class("X"), Class("Y")]))),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDelegatesLike(module, expectConsistent: true);
    }

    /// <summary>
    /// GF2 capability — the module <c>Transitive(r)</c>, <c>A ⊑ ∃r⁻.B</c>, <c>B ⊑ ⊥</c>, <c>a : A</c> has a
    /// hand-derived TRUE verdict of INCONSISTENT: <c>a ∈ A</c> forces an <c>r</c>-predecessor witness cored
    /// <c>B</c>, and <c>B = ∅</c> empties it, so <c>⊥</c> back-propagates over the generator edge to condemn
    /// <c>a</c>. The forward role's own self-transitivity <c>r ∘ r ⊑ r</c> sits inside the generator
    /// fence's admitted slice, so the EL fast-path decides this module inconsistent — a decision the
    /// inverse-blind tableau cannot witness: dropping the inverse existential, it never forces <c>a</c>'s
    /// missing predecessor and answers consistent.
    /// </summary>
    [TestMethod]
    public void TransitiveBackwardExistentialEmptyPredecessorIsDecidedInconsistentByEl()
    {
        ReasoningModule module = Module(
            Transitive("r"),
            SubClassOf(Class("A"), SomeInverse("r", Class("B"))),
            SubClassOf(Class("B"), NothingReference),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The self-transitive forward role sits inside the generator fence's admitted slice and the EL fast-path decides the module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "a's r-predecessor witness cored B is emptied by B ⊑ ⊥, and ⊥ back-propagates over the generator edge to condemn a.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the inverse existential, never forces a's predecessor, and answers consistent — the capability the fast-path adds.");
    }

    /// <summary>
    /// The delegation-honesty contract over a battery of delegated modules bearing beyond-ALC(H)
    /// constructs: each is not EL-decided, and its consistent delegated verdict names a remainder — so
    /// it is not decisive and the decision records
    /// <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/>, never a blind consistent. The
    /// control module's union superclass the tableau interprets whole, so its full delegated decision
    /// stays <see cref="ReasoningDecisionOutcome.Decided"/> and decisive — a whole-module delegated
    /// decision is not marked.
    /// </summary>
    [TestMethod]
    public void DelegatedBeyondFragmentModulesAreNeverBlindConsistent()
    {
        (string Name, ReasoningModule Module)[] battery =
        [
            ("GF10 symmetric pairing-key chain over a backward existential", Module(
                Transitive("r"),
                Symmetric("r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a")))),
            ("FS20 self-elimination over a transitive super-role", Module(
                Symmetric("r"),
                SubProperty("r", "t"),
                Transitive("t"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("r", Class("A"))),
                SubClassOf(HasSelf("t"), Class("D")))),
            ("FS14 transitive mirror role", Module(
                SubClassOf(Class("P"), SomeInverse("r", Class("Q"))),
                SubClassOf(Class("Q"), SomeInverse("r", Class("P"))),
                Transitive("r"),
                ClassAssertion(Class("P"), Individual("p")))),
            ("chain over mirrored role", Module(
                SubProperty("r", "t"),
                Transitive("t"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a")))),
            //A row of this sweep must be drawn from a family the EL fast path does not decide, or the sweep pays a
            //fixture every time the EL lane reaches further: the chain and self families over a
            //witness-reachable role, and the constrained-ground-role families, are the durable sources.
            //This one is a mirrored role that is ALSO a chain link, which the ADMISSION gate delegates —
            //a different mechanism from the rows above, which reach the mint and stop at the witness
            //regime.
            ("RB4 chain over a mirrored role composed into a backward existential", Module(
                Symmetric("r"),
                SubClassOf(Class("A1"), Some("r", Class("B"))),
                SubClassOf(Class("A2"), Some("r", Class("B"))),
                SubClassOf(Class("B"), Some("s", Class("E"))),
                Chain("t", "r", "s"),
                SubClassOf(SomeInverse("t", Class("A1")), NothingReference),
                ClassAssertion(Class("A2"), Individual("a")))),
            ("GF9 mixed chain over the generator's forward role", Module(
                Chain("q", "r", "r"),
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("a")))),
            ("D8 union-filler inverse existential", Module(
                SubClassOf(Class("A"), SomeInverse("r", new OwlObjectUnionOf([Class("X"), Class("Y")]))),
                ClassAssertion(Class("A"), Individual("a")))),
            ("IS9 inverse-functional-spelled ground role over an existential", Module(
                FunctionalInverse("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                ClassAssertion(Class("A"), Individual("c")))),
        ];

        foreach((string name, ReasoningModule module) in battery)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            Assert.IsFalse(decision.Statistics.ElTotals.ElDecided, name + ": delegated, not EL-decided.");
            Assert.IsFalse(decision.Verdict!.IsDecisive, name + ": the consistent delegated verdict names a remainder, so it is fragment-relative.");
            Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, name + ": the decision records the fragment-relative outcome.");
        }

        //Control: the tableau interprets the union superclass whole, so the
        //delegated decision is decisive and unmarked.
        ReasoningModule control = Module(
            SubClassOf(Class("A"), new OwlObjectUnionOf([Class("X"), Class("Y")])),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision controlDecision = ElCoupledModuleReasoner.DecideModule(control, TestContext.CancellationToken);
        Assert.IsFalse(controlDecision.Statistics.ElTotals.ElDecided, "The control delegates the disjunction the EL fragment cannot express.");
        Assert.IsTrue(controlDecision.Verdict!.IsDecisive, "The tableau decides the union superclass whole, with no named remainder.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, controlDecision.Outcome, "A whole-module delegated decision stays Decided.");
        Assert.IsEmpty(controlDecision.Verdict.UnsupportedConstructs, "No construct was excluded.");
    }

    /// <summary>
    /// E1 — <c>A ⊑ ∃r⁻.C</c> with <c>range(r) = E</c> entails <c>A ⊑ E</c>: every A-member has an
    /// r-predecessor whose r-edge target (the A-member itself) lies in range(r) = E, so
    /// <c>range(r⁻) = domain(r)</c> types A. Asserted as a subsumption in the EL projection; the
    /// inverse-blind tableau cannot derive it.
    /// </summary>
    [TestMethod]
    public void RangeOverBackwardMintingEntailsOwnerTypeIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            Range("r", Class("E")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The backward-minting range module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing empties any class, so the module is consistent.");
        Assert.IsTrue(Subsumes(decision.Verdict, "A", "E"), "A ⊑ E: every A has an r-predecessor whose r-edge target (the A-member) is in range(r) = E.");
        Assert.IsFalse(Subsumes(AlcModuleReasoner.Decide(module, TestContext.CancellationToken), "A", "E"), "The inverse-blind tableau drops the inverse existential and never derives A ⊑ E.");
    }

    /// <summary>
    /// E2 — <c>A ⊑ ∃r⁻.C</c> with <c>∃r⁻.C ⊑ Y</c> entails <c>A ⊑ Y</c>: the generator mints A's
    /// r-predecessor and the (a) mirror over its real r-edge fires the left existential on A. Asserted as
    /// a subsumption in the EL projection; the inverse-blind tableau cannot derive it. The tautology
    /// <c>Y ⊑ ⊤</c> surfaces <c>Y</c> in the tableau signature the projection enumerates over (a class
    /// occurring only behind an untranslatable inverse existential is otherwise absent from it), with no
    /// effect on the entailment.
    /// </summary>
    [TestMethod]
    public void BackwardMintingComposesWithSubclassInverseIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
            SubClassOf(SomeInverse("r", Class("C")), Class("Y")),
            SubClassOf(Class("Y"), ThingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The (a)+(b) composition module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing empties any class, so the module is consistent.");
        Assert.IsTrue(Subsumes(decision.Verdict, "A", "Y"), "A ⊑ Y: the mirror over the minted witness's r-edge fires ∃r⁻.C ⊑ Y on A.");
        Assert.IsFalse(Subsumes(AlcModuleReasoner.Decide(module, TestContext.CancellationToken), "A", "Y"), "The inverse-blind tableau drops both inverse existentials and never derives A ⊑ Y.");
    }

    /// <summary>
    /// E3 (sharing-not-contaminating, CE-2b) — <c>A1, A2 ⊑ ∃r⁻.B</c> with <c>domain(r) = D</c> does NOT
    /// entail <c>B ⊑ D</c>: the domain types each per-owner witness (which has an r-successor), never the
    /// shared named class B, so B stays untyped while both A-branches stay consistent. The positive control
    /// <c>B ⊑ M</c> over a fresh <c>M</c> proves <c>B</c> is enumerated by the same projection — the derived
    /// <c>B ⊑ M</c> is present, so the absent <c>B ⊑ D</c> is genuinely tested, not vacuously missing — and,
    /// <c>M</c> being fresh (named by no other axiom), it leaves the model reasoning untouched.
    /// </summary>
    [TestMethod]
    public void BackwardMintingDomainDoesNotContaminateSharedFillerIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A1"), SomeInverse("r", Class("B"))),
            SubClassOf(Class("A2"), SomeInverse("r", Class("B"))),
            Domain("r", Class("D")),
            SubClassOf(Class("B"), Class("M")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The shared-filler backward-minting module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Nothing empties any class, so the module is consistent.");
        Assert.IsTrue(Subsumes(decision.Verdict, "B", "M"), "B ⊑ M: the fresh-M control confirms B is enumerated by the same projection the absent B ⊑ D is tested against.");
        Assert.IsFalse(Subsumes(decision.Verdict, "B", "D"), "B ⊑ D is NOT derived: the domain types the per-owner witnesses, never the shared named class B.");
    }

    /// <summary>
    /// CE-3 — a subclass-side inverse existential with a non-bottom conclusion is decided by the EL
    /// fast-path via the synthetic-mirror reduction: <c>∃r⁻.A1 ⊑ C</c> with <c>C ⊑ ⊥</c> forbids any
    /// element an <c>r</c>-predecessor in <c>A1</c>, so <c>A1 ⊑ ∃r.B</c> empties <c>A1</c> — but only
    /// <c>A1</c>: <c>A2</c>'s witness has no <c>A1</c> predecessor and <c>a : A2</c> keeps the module
    /// CONSISTENT. The inverse-blind tableau reaches consistent by dropping everything, so the
    /// owner-locality is asserted as the known correct answer.
    /// </summary>
    [TestMethod]
    public void LeftInverseExistentialClashStaysOwnerLocalIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A1"), Some("r", Class("B"))),
            SubClassOf(Class("A2"), Some("r", Class("B"))),
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("A1")), Class("C")),
            SubClassOf(Class("C"), NothingReference),
            ClassAssertion(Class("A2"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The subclass-side inverse existential is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Only A1 is emptied — A2's witness has no A1 predecessor, so a stays satisfiable.");
    }

    /// <summary>A subclass-side inverse existential into ⊥ is decided by the EL fast-path: <c>∃r⁻.A ⊑ ⊥</c> forbids any element an <c>r</c>-predecessor in <c>A</c>, and <c>A ⊑ ∃r.B</c> forces every <c>A</c>-element to create exactly such an element — so <c>A</c> is empty and <c>a : A</c> condemns the module. The synthetic mirror alone carries the reduction (no user inverse axiom is present); the inverse-blind tableau misses the clash.</summary>
    [TestMethod]
    public void LeftInverseExistentialIntoBottomIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("A")), NothingReference),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The subclass-side inverse existential is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Every A-element's forced r-successor has an r-predecessor in A, which ∃r⁻.A ⊑ ⊥ forbids — A is empty and a is condemned.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the inverse existential and misses the clash.");
    }

    /// <summary>A subclass-side inverse existential with a plain named conclusion is decided consistent: <c>∃r⁻.A ⊑ Y</c> types the witness <c>Y</c> without any clash.</summary>
    [TestMethod]
    public void LeftInverseExistentialGeneralConclusionIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("A")), Class("Y")),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The subclass-side inverse existential is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The witness gains Y and nothing clashes.");
    }

    /// <summary>
    /// A NESTED subclass-side inverse existential composes through the naming walk: <c>∃r⁻.(∃q.D) ⊑ Y</c>
    /// names the inner forward existential first, then reduces the outer inverse over the synthetic
    /// mirror. Ground edges make <c>a ∈ ∃q.D</c> and give <c>b</c> the <c>r</c>-predecessor <c>a</c>,
    /// so <c>b ∈ Y</c>, which the disjointness with <c>Z</c> and <c>b : Z</c> condemns — a clash the
    /// inverse-blind tableau misses.
    /// </summary>
    [TestMethod]
    public void NestedLeftInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Some("q", Class("D"))), Class("Y")),
            Edge("a", "q", "d"),
            Edge("a", "r", "b"),
            ClassAssertion(Class("D"), Individual("d")),
            ClassAssertion(Class("Z"), Individual("b")),
            Disjoint(Class("Y"), Class("Z")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The nested subclass-side inverse existential is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "a is in ∃q.D and is b's r-predecessor, so b gains Y, disjoint with its asserted Z.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the inverse existential and misses the clash.");
    }

    /// <summary>A user inverse pairing and a subclass-side inverse existential over the SAME role compose: the user mirror and the synthetic mirror both fire, and the reduction still condemns the module that forbids <c>A</c> an <c>r</c>-successor creation.</summary>
    [TestMethod]
    public void UserInverseAndLeftInverseExistentialOnSameRoleIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Inverse("r", "u"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("A")), NothingReference),
            ClassAssertion(Class("A"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The doubly-paired module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The reduction fires over the synthetic mirror regardless of the user pairing; A is empty and a is condemned.");
    }

    /// <summary>A functional role whose inverse appears in a subclass-side existential keeps its ground-graph decision: the synthetic pairing marks only the mirror role a mirror target, so <c>Functional(r)</c> over asserted edges is still admitted and the forced merge still clashes with the asserted distinctness.</summary>
    [TestMethod]
    public void FunctionalRoleWithLeftInverseExistentialIsStillDecidedByEl()
    {
        ReasoningModule module = Module(
            Functional("r"),
            Edge("a", "r", "b"),
            Edge("a", "r", "c"),
            Different("b", "c"),
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("X")), Class("Y")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The functional module with a subclass-side inverse existential is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Functional(r) forces b = c against DifferentIndividuals(b, c); the synthetic pairing does not fence the functional role.");
    }

    /// <summary>Two ⊤-filler existential owners stay owner-local under a subclass-side inverse clash: the witnesses are ⊤-cored but carry distinct inherited demand sets, so only <c>A1</c> is emptied and <c>a : A2</c> keeps the module consistent.</summary>
    [TestMethod]
    public void TopFillerWitnessesStayOwnerLocalIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            SubClassOf(Class("A1"), Some("r", ThingReference)),
            SubClassOf(Class("A2"), Some("r", ThingReference)),
            SubClassOf(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("A1")), NothingReference),
            ClassAssertion(Class("A2"), Individual("a")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The ⊤-filler module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Only A1's ⊤-cored witness is condemned; A2's carries a distinct demand set and a stays satisfiable.");
    }

    /// <summary>
    /// An inverse existential across an EQUIVALENCE is decided by the EL fast-path: the
    /// subclass-polarity occurrence (<c>∃r⁻.C ⊑ B</c>) rides the synthetic mirror and the
    /// superclass-polarity occurrence (<c>B ⊑ ∃r⁻.C</c>) rides the generator reduction, the two
    /// machineries the equivalence checker relies on. Consistent: model Δ = {x, w}, B = {x}, C = {w},
    /// r = {(w, x)} — x ∈ B gains its forced r-predecessor w ∈ C, the mirror re-derives x ∈ B, and
    /// nothing clashes.
    /// </summary>
    [TestMethod]
    public void EquivalenceWithInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Equivalent(SomeInverse("r", Class("C")), Class("B")),
            ClassAssertion(Class("B"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The equivalence with an inverse existential is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x ∈ B gains its forced r-predecessor w ∈ C; nothing clashes.");
    }

    /// <summary>An inverse existential as a property RANGE is decided by the EL fast-path: the range axiom names the complex range as a fresh proxy atom told <c>F ⊑ ∃r⁻.C</c>, whose superclass occurrence rides the generator reduction, and registers the proxy as the role's range. Consistent: nothing is a <c>p</c>-target, so the proxy is inert — model Δ = {x}, A = B = {x}, p = r = ∅.</summary>
    [TestMethod]
    public void RangeWithInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Range("p", new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("C"))),
            SubClassOf(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The range's naming step carries the inverse existential to the generator reduction, so the EL fast-path decides the module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "No element is a p-target, so the range proxy never fires and nothing clashes.");
    }

    /// <summary>An inverse existential as a property DOMAIN is decided by the EL fast-path: the domain axiom normalizes to <c>∃p.⊤ ⊑ F</c> with <c>F ⊑ ∃r⁻.C</c>, whose superclass occurrence rides the generator reduction. Consistent: nothing is a <c>p</c>-source, so <c>F</c> is inert — model Δ = {x}, A = B = {x}, p = r = ∅.</summary>
    [TestMethod]
    public void DomainWithInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            new OwlObjectPropertyDomainAxiom(Property("p"), new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("C"))) { Origin = Origin("inversedomainexpr") },
            SubClassOf(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The domain class reaches the generator reduction through the inclusion the domain axiom normalizes to, so the EL fast-path decides the module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "No element is a p-source, so nothing is forced to have an r-predecessor and nothing clashes.");
    }

    /// <summary>An inverse existential as a DISJOINTNESS operand is decided by the EL fast-path: the pairwise reduction keeps each operand in subclass polarity, so <c>∃r⁻.C</c> rides the same synthetic-mirror reduction as the subclass side. Consistent: nothing gives <c>x</c> an <c>r</c>-predecessor in <c>C</c>, so the disjointness never fires.</summary>
    [TestMethod]
    public void DisjointnessWithInverseExistentialIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Disjoint(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("C")), Class("X")),
            ClassAssertion(Class("X"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The disjointness-operand inverse existential is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "x has no r-predecessor in C, so ∃r⁻.C ⊓ X ⊑ ⊥ never fires.");
    }

    /// <summary>A disjointness whose inverse-existential operand is genuinely inhabited clashes: the asserted edge gives <c>x</c> an <c>r</c>-predecessor in <c>C</c>, so <c>x ∈ ∃r⁻.C ⊓ X</c> and the pairwise disjointness condemns it — a clash the inverse-blind tableau misses.</summary>
    [TestMethod]
    public void DisjointnessWithInverseExistentialClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Disjoint(new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + "r"))), Class("C")), Class("X")),
            Edge("c0", "r", "x"),
            ClassAssertion(Class("C"), Individual("c0")),
            ClassAssertion(Class("X"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inhabited disjointness-operand module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "c0 ∈ C is x's r-predecessor, so x ∈ ∃r⁻.C ⊓ X, which the disjointness forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the inverse existential and misses the clash.");
    }

    /// <summary>An inverse range over a reserved role is delegated: the reserved built-in's fixed extension is not interpreted, so admitting inverse range/domain axioms must still reject it.</summary>
    [TestMethod]
    public void InverseRangeOnReservedRoleDelegates()
    {
        ReasoningModule module = Module(
            new OwlObjectPropertyRangeAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#topObjectProperty"))), Class("D")) { Origin = Origin("reservedinverserange") },
            SubClassOf(Class("A"), Class("B")),
            ClassAssertion(Class("A"), Individual("x")));

        AssertDelegatesLike(module);
    }

    /// <summary>
    /// RVFEL-BOT-SOME: a class assertion of an existential over the empty
    /// <c>owl:bottomObjectProperty</c> folds at the tier's front door to
    /// <c>ClassAssertion(owl:Nothing, i)</c> — no bottom-successor exists — so the fast-path
    /// decides the module INCONSISTENT itself rather than handing a reserved-role module on.
    /// </summary>
    [TestMethod]
    public void ReservedBottomExistentialIsDecidedByEl()
    {
        OwlObjectPropertyReference bottom = new(new NamedNode(OwlVocabulary.BottomObjectProperty));
        ReasoningModule module = Module(
            ClassAssertion(new OwlObjectSomeValuesFrom(bottom, ThingReference), Individual("i")));

        AssertElDecidesLike(module, expectConsistent: false);

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The folded bottom existential is a plain owl:Nothing assertion, inside EL⊥, so the fast-path decides it.");
    }

    /// <summary>
    /// RVFEL-BOT-ALL: a universal over the empty <c>owl:bottomObjectProperty</c>
    /// is vacuously true, so it folds to <c>owl:Thing</c> and the module — that vacuous assertion
    /// beside an unrelated consistent fact — is decided CONSISTENT by the fast-path. The ⊤-fold
    /// witness that folding does not over-clash.
    /// </summary>
    [TestMethod]
    public void ReservedBottomUniversalIsDecidedByEl()
    {
        OwlObjectPropertyReference bottom = new(new NamedNode(OwlVocabulary.BottomObjectProperty));
        ReasoningModule module = Module(
            ClassAssertion(new OwlObjectAllValuesFrom(bottom, Class("C")), Individual("i")),
            ClassAssertion(Class("D"), Individual("j")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The folded vacuous universal is a plain owl:Thing assertion, inside EL⊥, so the fast-path decides it.");
    }

    /// <summary>
    /// RVFEL-SUBSUMPTION-PARITY: a subclass inclusion into an existential over
    /// the empty <c>owl:bottomObjectProperty</c> folds to <c>A ⊑ ⊥</c>, so the module is CONSISTENT
    /// — no individual is an <c>A</c> — and the fast-path enumerates the same subsumption set as the
    /// snapshot over the shared folded signature <c>{A, B}</c>, the discarded filler's name absent
    /// from both. An unsatisfiable class is subsumed by every signature class, so <c>A ⊑ B</c> is a
    /// member.
    /// </summary>
    [TestMethod]
    public void ReservedBottomExistentialSubsumptionMatchesSnapshot()
    {
        OwlObjectPropertyReference bottom = new(new NamedNode(OwlVocabulary.BottomObjectProperty));
        ReasoningModule module = Module(
            SubClassOf(Class("A"), new OwlObjectSomeValuesFrom(bottom, Class("C"))),
            ClassAssertion(Class("B"), Individual("j")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The folded inclusion A ⊑ owl:Nothing is inside EL⊥, so the fast-path decides it.");
        Assert.Contains($"{Example}A→{Example}B", SubsumptionKeys(decision.Verdict!), "The fold empties A, and an unsatisfiable class is subsumed by every signature class, including the unrelated B.");
    }

    /// <summary>
    /// RVFEL-NEARMISS (the near-miss hazard): an ordinary object property whose local name
    /// resembles the reserved one but lives in a non-owl namespace is not the reserved built-in, so
    /// nothing folds and the module stays an ordinary EL existential the fast-path decides CONSISTENT.
    /// </summary>
    [TestMethod]
    public void ReservedNearMissPropertyIsDecidedByEl()
    {
        ReasoningModule module = Module(
            ClassAssertion(Some("bottomObjectProperty", ThingReference), Individual("i")));

        AssertElDecidesLike(module, expectConsistent: true);

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The example-namespace property is not the reserved bottom object property, so the ordinary existential is decided by the fast-path unfolded.");
    }

    /// <summary>
    /// RVFEL-KEPT-DELEGATES: a reserved role in an axiom's PROPERTY position is
    /// no pointwise-constant class expression, so the fold keeps it and the survey's reserved guard
    /// still declines the module — it is delegated whole and the verdict is the fallback's, the kept
    /// contrast to the folded rows.
    /// </summary>
    [TestMethod]
    public void ReservedTopDomainDelegates()
    {
        OwlObjectPropertyReference top = new(new NamedNode(OwlVocabulary.TopObjectProperty));
        ReasoningModule module = Module(
            new OwlObjectPropertyDomainAxiom(top, Class("C")) { Origin = Origin("reservedtopdomain") },
            ClassAssertion(Class("D"), Individual("i")));

        AssertDelegatesLike(module);
    }

    /// <summary>
    /// A complex (non-atomic) inverse range decomposes through the fresh-atom path: with
    /// <c>range(r⁻) = D ⊓ E</c> and <c>A ⊑ ∃r.B</c>, every <c>A</c> is an <c>r</c>-source and so
    /// both a <c>D</c> and an <c>E</c>; disjoint, they empty <c>A</c> and condemn its asserted
    /// individual — exercising the conjunction decomposition of the reduced <c>∃r.⊤ ⊑ (D ⊓ E)</c>.
    /// </summary>
    [TestMethod]
    public void InverseRangeWithConjunctionTypesSourceIntoClashIsDecidedByEl()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            InverseRange("r", new OwlObjectIntersectionOf([Class("D"), Class("E")])),
            Disjoint(Class("D"), Class("E")),
            ClassAssertion(Class("A"), Individual("x")));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The complex inverse-range module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "range(r⁻) = domain(r) = D ⊓ E types A (an r-source) as both D and E, which are disjoint, so A is empty and x is condemned.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The inverse-blind tableau drops the range and never types A.");
    }

    /// <summary>
    /// A transitive inverse role composes under the role hierarchy: <c>Transitive(r⁻)</c> composes
    /// asserted <c>a→b→c</c> to <c>a→c</c>, which <c>r ⊑ s</c> promotes to an <c>s</c>-edge; with
    /// <c>a</c> an <c>A</c> disjoint from <c>∃s.C</c> and <c>c</c> a <c>C</c>, the promoted composed
    /// edge condemns <c>a</c> — the composition-plus-promotion corner, which the direct edges alone
    /// (<c>a→b</c> only reaches a non-<c>C</c> node) do not reach.
    /// </summary>
    [TestMethod]
    public void InverseTransitiveComposesUnderRoleHierarchyIsDecidedByEl()
    {
        ReasoningModule module = Module(
            InverseTransitive("r"),
            SubProperty("r", "s"),
            Edge("a", "r", "b"),
            Edge("b", "r", "c"),
            ClassAssertion(Class("A"), Individual("a")),
            ClassAssertion(Class("C"), Individual("c")),
            Disjoint(Class("A"), Some("s", Class("C"))));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-transitive-with-hierarchy module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Transitive(r⁻) composes a→c; r ⊑ s promotes it to an s-edge, so a is an A with an s-successor c in C, which A ⊓ ∃s.C ⊑ ⊥ condemns.");
    }

    /// <summary>
    /// The forced-empty inference entails <c>A ⊑ ⊥</c> on the classification path: <c>Symmetric(r) +
    /// Asymmetric(r)</c> decides <c>r</c> empty, so <c>A ⊑ ∃r.B</c> makes <c>A</c> unsatisfiable while
    /// <c>B</c> stays satisfiable and the module stays consistent (no individual is <c>A</c>). An
    /// unsatisfiable class is subsumed by every signature class, so subsumption by the unrelated class
    /// <c>Z</c> (surfaced by the tautology <c>Z ⊑ ⊤</c>) witnesses <c>A ⊑ ⊥</c> — <c>owl:Nothing</c> itself
    /// is never enumerated into the module verdict's named-class signature, so an unrelated class stands for
    /// ⊥. <c>B</c> is not subsumed by <c>Z</c>, witnessing <c>B</c> is NOT emptied. The characteristic-blind
    /// tableau drops both characteristics, finds <c>A</c> satisfiable, and never derives <c>A ⊑ Z</c> — the
    /// capability the fast-path adds.
    /// </summary>
    [TestMethod]
    public void SymmetricAsymmetricEmptyRoleEntailsBottomIsDecidedByEl()
    {
        ReasoningModule module = Module(
            Symmetric("r"),
            Asymmetric("r"),
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("Z"), ThingReference));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The forced-empty-role module is decided by the EL fast-path.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "No individual is A, so A ⊑ ⊥ leaves ⊤ inhabited and the module is consistent.");
        Assert.IsTrue(Subsumes(decision.Verdict, "A", "Z"), "A ⊑ ⊥ collapses A into every class, including the unrelated Z — the A ⊑ Nothing witness.");
        Assert.IsFalse(Subsumes(decision.Verdict, "B", "Z"), "B is satisfiable, so it is not subsumed by the unrelated Z — the NOT B ⊑ Nothing witness.");
        Assert.IsFalse(Subsumes(AlcModuleReasoner.Decide(module, TestContext.CancellationToken), "A", "Z"), "The characteristic-blind tableau drops Symmetric and Asymmetric, finds A satisfiable, and never derives A ⊑ Z.");
    }

    /// <summary>
    /// The inverse-spelled functional collapse fires the predecessor union: <c>Functional(r⁻) ≡
    /// InverseFunctional(r)</c> unions <c>x</c>'s two asserted r-predecessors <c>a</c> and <c>b</c> into one
    /// element, which <c>DifferentIndividuals(a, b)</c> contradicts, so the EL fast-path decides the module
    /// inconsistent. The characteristic-blind tableau drops the functional characteristic, keeps <c>a</c> and
    /// <c>b</c> distinct, and answers consistent — the capability the fast-path adds. The SeedFunctionalMerges
    /// two-site pin: the pre-merge scan must feed the inverse spelling to the predecessor collapse.
    /// </summary>
    [TestMethod]
    public void InverseSpelledFunctionalMergesPredecessorsIsDecidedByEl()
    {
        ReasoningModule module = Module(
            FunctionalInverse("r"),
            Edge("a", "r", "x"),
            Edge("b", "r", "x"),
            Different("a", "b"));

        ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ElTotals.ElDecided, "The inverse-functional-spelled module is decided by the EL fast-path.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Functional(r⁻) unions x's two r-predecessors a, b, which DifferentIndividuals(a, b) forbids.");
        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The characteristic-blind tableau drops the functional characteristic, so a and b stay distinct and nothing clashes.");
    }

    //Assertion helpers.

    /// <summary>Asserts the EL fast-path decided the module to the expected consistency and that its full verdict matches the snapshot tableau.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="expectConsistent">The expected consistency verdict.</param>
    private void AssertElDecidesLike(ReasoningModule module, bool expectConsistent)
    {
        ModuleDecision elDecision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(elDecision.Statistics.ElTotals.ElDecided, "The EL fast-path was expected to decide the module.");

        ModuleVerdict reference = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
        Assert.AreEqual(expectConsistent, elDecision.Verdict!.IsConsistent, "The EL consistency verdict.");
        Assert.AreEqual(reference.IsConsistent, elDecision.Verdict.IsConsistent, "The EL engine agrees with the snapshot on consistency.");
        Assert.AreSequenceEqual(SubsumptionKeys(reference), SubsumptionKeys(elDecision.Verdict), "The EL engine agrees with the snapshot on the subsumption set.");
    }

    /// <summary>Asserts the module fell outside the EL fragment, was delegated to the tableau, and the delegated verdict matches the snapshot. When <paramref name="expectConsistent"/> is supplied, also asserts the final delegated verdict equals that stated TRUE consistency, not merely that the EL path and the snapshot agree.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="expectConsistent">The stated TRUE consistency the final delegated verdict must equal, or <see langword="null"/> to assert only agreement with the snapshot.</param>
    private void AssertDelegatesLike(ReasoningModule module, bool? expectConsistent = null)
    {
        ModuleDecision elDecision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(elDecision.Statistics.ElTotals.ElDecided, "The module was expected to be delegated to the tableau.");

        ModuleVerdict reference = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
        Assert.AreEqual(reference.IsConsistent, elDecision.Verdict!.IsConsistent, "The delegated verdict matches the snapshot.");
        Assert.AreSequenceEqual(SubsumptionKeys(reference), SubsumptionKeys(elDecision.Verdict), "The delegated subsumptions match the snapshot.");

        if(expectConsistent is bool expected)
        {
            Assert.AreEqual(expected, elDecision.Verdict.IsConsistent, "The final delegated verdict equals the stated TRUE consistency.");
        }
    }

    /// <summary>Whether the verdict records the named-class subsumption <c>sub ⊑ super</c> over the example-namespace classes.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="sub">The subclass's local name.</param>
    /// <param name="super">The superclass's local name.</param>
    /// <returns><see langword="true"/> when the subsumption is present.</returns>
    private static bool Subsumes(ModuleVerdict verdict, string sub, string super)
    {
        Utf8String subIri = Utf8Strings.From(Example + sub);
        Utf8String superIri = Utf8Strings.From(Example + super);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            if(subClass.Iri.Equals(subIri) && superClass.Iri.Equals(superIri))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The verdict's subsumption pairs as sorted comparison keys, one <c>sub→super</c> string per pair.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The keys, sorted ordinally.</returns>
    private static List<string> SubsumptionKeys(ModuleVerdict verdict)
    {
        List<string> keys = new(verdict.Subsumptions.Count);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add($"{subClass.Iri}→{superClass.Iri}");
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    //Construction helpers.

    /// <summary>The IRI prefix the test classes, roles, and individuals live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The fixed-⊤ class reference, <c>owl:Thing</c>.</summary>
    private static OwlClassReference ThingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The fixed-⊥ class reference, <c>owl:Nothing</c>.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

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

    /// <summary>An inverse existential restriction <c>∃r⁻.C</c> — an existential over the inverse of a forward role, spelled as an <c>ObjectInverseOf</c> property expression.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The inverse existential restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), filler);
    }

    /// <summary>An individual-value restriction over a forward role — <c>∃r.{a}</c> in its <c>ObjectHasValue</c> spelling.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An individual-value restriction over an inverse role — <c>∃r⁻.{a}</c> in its <c>ObjectHasValue</c> spelling, which in a class assertion on <c>x</c> is the ground edge <c>(a, x)</c>.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValueInverse(string property, string individual)
    {
        return new OwlObjectHasValue(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), Individual(individual));
    }

    /// <summary>A self-restriction over an inverse role — <c>∃r⁻.Self</c>, which holds of exactly the elements <c>∃r.Self</c> holds of, a self-edge being its own reverse.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelfInverse(string property)
    {
        return new OwlObjectHasSelf(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))));
    }

    /// <summary>An enumeration of individuals in the example namespace (<c>ObjectOneOf</c>); a single individual is the nominal <c>{a}</c>.</summary>
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

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A pairwise disjointness axiom.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = Origin("disjoint") };
    }

    /// <summary>A transitive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = Origin("transitive") };
    }

    /// <summary>A self-restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>A reflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, Property(property)) { Origin = Origin("reflexive") };
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

    /// <summary>An inverse-functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, Property(property)) { Origin = Origin("inversefunctional") };
    }

    /// <summary>An asymmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, Property(property)) { Origin = Origin("asymmetric") };
    }

    /// <summary>An irreflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, Property(property)) { Origin = Origin("irreflexive") };
    }

    /// <summary>An asymmetric characteristic on an inverse role — <c>Asymmetric(r⁻)</c>, spelled over an <c>ObjectInverseOf</c> property expression.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom AsymmetricInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("asymmetricinverse") };
    }

    /// <summary>A symmetric characteristic on an inverse role — <c>Symmetric(r⁻)</c>, which holds exactly when <c>Symmetric(r)</c> does and self-pairs the forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom SymmetricInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("symmetricinverse") };
    }

    /// <summary>A reflexive characteristic on an inverse role — <c>Reflexive(r⁻)</c>, which holds exactly when <c>Reflexive(r)</c> does and demands the same self-edge.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom ReflexiveInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("reflexiveinverse") };
    }

    /// <summary>An irreflexive characteristic on an inverse role — <c>Irreflexive(r⁻)</c>, which holds exactly when <c>Irreflexive(r)</c> does and forbids the same self-edge.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom IrreflexiveInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("irreflexiveinverse") };
    }

    /// <summary>A functional characteristic on an inverse role — <c>Functional(r⁻)</c>, which bounds each element to one r-predecessor and so IS inverse-functionality on <c>r</c>.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom FunctionalInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("functionalinverse") };
    }

    /// <summary>An inverse-functional characteristic on an inverse role — <c>InverseFunctional(r⁻)</c>, which bounds each element to one r-successor and so IS functionality on <c>r</c>.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctionalInverse(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("inversefunctionalinverse") };
    }

    /// <summary>A superclass-position self-restriction demand <c>⊤ ⊑ ∃r.Self</c> — global reflexivity spelled through <c>ObjectHasSelf</c>.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom TopSubClassOfHasSelf(string property)
    {
        return new OwlSubClassOfAxiom(ThingReference, new OwlObjectHasSelf(Property(property))) { Origin = Origin("topself") };
    }

    /// <summary>A subrole inclusion <c>sub ⊑ super</c>.</summary>
    /// <param name="sub">The subrole's local name.</param>
    /// <param name="super">The superrole's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = Origin("subrole") };
    }

    /// <summary>An equivalence of two object properties — bidirectional sub-role inclusion.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentObjectPropertiesAxiom EquivalentProperties(string first, string second)
    {
        return new OwlEquivalentObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("equivalentrole") };
    }

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>An inverse sub-property inclusion <c>ObjectInverseOf(sub) ⊑ super</c> — that is, <c>sub⁻ ⊑ super</c>.</summary>
    /// <param name="sub">The inverted subproperty's local name.</param>
    /// <param name="super">The superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom InverseSubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + sub))), Property(super)) { Origin = Origin("inversesubrole") };
    }

    /// <summary>A sub-property-of-inverse inclusion <c>sub ⊑ ObjectInverseOf(super)</c> — that is, <c>sub ⊑ super⁻</c>.</summary>
    /// <param name="sub">The subproperty's local name.</param>
    /// <param name="super">The inverted superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubPropertyInverse(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + super)))) { Origin = Origin("subroleinverse") };
    }

    /// <summary>A property-chain sub-role inclusion — a single link is a plain sub-role, several compose.</summary>
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

    /// <summary>A range axiom on an inverse role — <c>range(r⁻)</c>, which equals <c>domain(r)</c> and so types every source of the forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="range">The range class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom InverseRange(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), range) { Origin = Origin("inverserange") };
    }

    /// <summary>A domain axiom on an inverse role — <c>domain(r⁻)</c>, which equals <c>range(r)</c> and so types every target of the forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="domain">The domain class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom InverseDomain(string property, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), domain) { Origin = Origin("inversedomain") };
    }

    /// <summary>A transitive characteristic on an inverse role — <c>Transitive(r⁻)</c>, which holds exactly when <c>Transitive(r)</c> does.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseTransitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property)))) { Origin = Origin("inversetransitive") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An asserted role edge between two individuals.</summary>
    /// <param name="from">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="to">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string from, string role, string to)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(from), Individual(role), Individual(to)) { Origin = Origin($"edge-{from}-{to}") };
    }

    /// <summary>A same-individual axiom.</summary>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom SameIndividual(NamedNode first, NamedNode second)
    {
        return new OwlSameIndividualAxiom(first, second) { Origin = Origin("same") };
    }

    /// <summary>The seven ABox-carrying EL soundness battery families, projected as (family, name, module) for the ELH-degeneracy differential population; the differential double-filters on both surveys admitting and both arms deciding before comparing.</summary>
    /// <returns>Every soundness-battery case tagged with its family and case name.</returns>
    internal static IReadOnlyList<(string Family, string Name, ReasoningModule Module)> AboxSoundnessBatteryModules()
    {
        List<(string Family, string Name, ReasoningModule Module)> modules = [];
        foreach((string name, ReasoningModule module, bool _) in MintingSoundnessCases())
        {
            modules.Add(("MintingSoundness", name, module));
        }

        foreach((string name, ReasoningModule module, bool _, ElPath _) in BackwardMintingCases())
        {
            modules.Add(("BackwardMinting", name, module));
        }

        foreach((string name, ReasoningModule module, bool _, ElPath _) in GroundCharacteristicCases())
        {
            modules.Add(("GroundCharacteristic", name, module));
        }

        foreach((string name, ReasoningModule module, bool _, ElPath _) in RoleForcedEmptyCases())
        {
            modules.Add(("RoleForcedEmpty", name, module));
        }

        foreach((string name, ReasoningModule module, bool _, ElPath _) in InverseCharacteristicSpellingCases())
        {
            modules.Add(("InverseCharacteristicSpelling", name, module));
        }

        foreach((string name, ReasoningModule module, bool _, ElPath _) in FoldSafeCycleCases())
        {
            modules.Add(("FoldSafeCycle", name, module));
        }

        foreach((string name, ReasoningModule module, bool _, ElPath _) in GeneratorFenceRelaxationCases())
        {
            modules.Add(("GeneratorFenceRelaxation", name, module));
        }

        return modules;
    }

    /// <summary>A different-individuals axiom over the named individuals.</summary>
    /// <param name="individuals">The mutually distinct individuals' local names.</param>
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

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A single-property data existential over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Utf8Strings.From(Example + property))], range);
    }

    /// <summary>A data existential over two data properties in the example namespace — the n-ary spelling, a value tuple with no single-property reading.</summary>
    /// <param name="firstProperty">The first data property's local name.</param>
    /// <param name="secondProperty">The second data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The n-ary data existential.</returns>
    private static OwlDataSomeValuesFrom DataSomeAcross(string firstProperty, string secondProperty, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Utf8Strings.From(Example + firstProperty)), new NamedNode(Utf8Strings.From(Example + secondProperty))], range);
    }

    /// <summary>A single-property data existential over the reserved <c>owl:topDataProperty</c>, whose fixed extension the calculus does not interpret.</summary>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential over the reserved property.</returns>
    private static OwlDataSomeValuesFrom ReservedDataSome(OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(OwlVocabulary.TopDataProperty)], range);
    }

    /// <summary>A <c>FunctionalDataProperty</c> axiom over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlFunctionalDataPropertyAxiom FunctionalData(string property)
    {
        return new OwlFunctionalDataPropertyAxiom(new NamedNode(Utf8Strings.From(Example + property))) { Origin = Origin("functionaldata") };
    }

    /// <summary>A <c>SubDataPropertyOf</c> inclusion between two data properties in the example namespace.</summary>
    /// <param name="sub">The sub-property's local name.</param>
    /// <param name="super">The super-property's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubDataPropertyOfAxiom SubDataProperty(string sub, string super)
    {
        return new OwlSubDataPropertyOfAxiom(new NamedNode(Utf8Strings.From(Example + sub)), new NamedNode(Utf8Strings.From(Example + super))) { Origin = Origin("subdata") };
    }

    /// <summary>An <c>EquivalentDataProperties</c> axiom over two data properties in the example namespace.</summary>
    /// <param name="first">The first data property's local name.</param>
    /// <param name="second">The second data property's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentDataPropertiesAxiom EquivalentDataProperties(string first, string second)
    {
        return new OwlEquivalentDataPropertiesAxiom(new NamedNode(Utf8Strings.From(Example + first)), new NamedNode(Utf8Strings.From(Example + second))) { Origin = Origin("equivalentdata") };
    }

    /// <summary>A data value restriction over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The required literal value.</param>
    /// <returns>The value restriction.</returns>
    private static OwlDataHasValue DataValue(string property, Literal value)
    {
        return new OwlDataHasValue(new NamedNode(Utf8Strings.From(Example + property)), value);
    }

    /// <summary>A single-property data universal over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data universal.</returns>
    private static OwlDataAllValuesFrom DataAll(string property, OwlDataRange range)
    {
        return new OwlDataAllValuesFrom([new NamedNode(Utf8Strings.From(Example + property))], range);
    }

    /// <summary>An equivalence of two class expressions.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>The <c>xsd:integer</c> datatype as a data range.</summary>
    private static OwlDatatypeReference Integer { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));

    /// <summary>An inclusively-bounded <c>xsd:integer</c> range; a lower bound above the upper bound is the empty value space.</summary>
    /// <param name="minInclusive">The inclusive lower bound's lexical form.</param>
    /// <param name="maxInclusive">The inclusive upper bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction IntegerBetween(string minInclusive, string maxInclusive)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer),
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), Lit(minInclusive, Vocabulary.Xsd.Integer)),
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), Lit(maxInclusive, Vocabulary.Xsd.Integer)),
        ]);
    }

    /// <summary>The <c>xsd:decimal</c> datatype as a data range — the value space every integer lies in.</summary>
    private static OwlDatatypeReference DecimalRange { get; } = new(new NamedNode(Vocabulary.Xsd.Decimal));

    /// <summary>The <c>owl:rational</c> datatype as a data range — the value space every decimal, and so every integer, lies in.</summary>
    private static OwlDatatypeReference RationalRange { get; } = new(new NamedNode(OwlVocabulary.Rational));

    /// <summary>An <c>xsd:integer</c> range constrained by one facet bound.</summary>
    /// <param name="facet">The facet IRI.</param>
    /// <param name="bound">The bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction IntegerFacet(Utf8String facet, string bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), [new OwlFacetRestriction(new NamedNode(facet), Lit(bound, Vocabulary.Xsd.Integer))]);
    }

    /// <summary>An <c>xsd:integer</c> range bounded below inclusively, unbounded above.</summary>
    /// <param name="minInclusive">The inclusive lower bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction IntegerAtLeast(string minInclusive)
    {
        return IntegerFacet(Vocabulary.XsdFacets.MinInclusive, minInclusive);
    }

    /// <summary>An <c>xsd:integer</c> range bounded below exclusively, unbounded above.</summary>
    /// <param name="minExclusive">The exclusive lower bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction IntegerAbove(string minExclusive)
    {
        return IntegerFacet(Vocabulary.XsdFacets.MinExclusive, minExclusive);
    }

    /// <summary>An <c>xsd:integer</c> range bounded above inclusively, unbounded below.</summary>
    /// <param name="maxInclusive">The inclusive upper bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction IntegerAtMost(string maxInclusive)
    {
        return IntegerFacet(Vocabulary.XsdFacets.MaxInclusive, maxInclusive);
    }

    /// <summary>An <c>xsd:integer</c> range bounded above exclusively, unbounded below.</summary>
    /// <param name="maxExclusive">The exclusive upper bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction IntegerBelow(string maxExclusive)
    {
        return IntegerFacet(Vocabulary.XsdFacets.MaxExclusive, maxExclusive);
    }

    /// <summary>An inclusively-bounded <c>xsd:decimal</c> range, whose value space carries the fractional values no integer range does.</summary>
    /// <param name="minInclusive">The inclusive lower bound's lexical form.</param>
    /// <param name="maxInclusive">The inclusive upper bound's lexical form.</param>
    /// <returns>The bounded range.</returns>
    private static OwlDatatypeRestriction DecimalBetween(string minInclusive, string maxInclusive)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Decimal),
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), Lit(minInclusive, Vocabulary.Xsd.Decimal)),
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), Lit(maxInclusive, Vocabulary.Xsd.Decimal)),
        ]);
    }

    /// <summary>An <c>xsd:normalizedString</c> range constrained by a regex pattern. The built-in automaton route models pattern facets over <c>xsd:string</c> only; a text sibling needs a base-automaton intersection the route defers, so the value-space checker reports it undecided.</summary>
    /// <param name="pattern">The pattern's lexical form.</param>
    /// <returns>The pattern-restricted range.</returns>
    private static OwlDatatypeRestriction StringPattern(string pattern)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.NormalizedString),
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.Pattern), Lit(pattern, Vocabulary.Xsd.String)),
        ]);
    }

    /// <summary>An <c>xsd:string</c> range constrained by a regex pattern, which the built-in automaton route decides.</summary>
    /// <param name="pattern">The pattern's lexical form.</param>
    /// <returns>The pattern-restricted range.</returns>
    private static OwlDatatypeRestriction PlainStringPattern(string pattern)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.String),
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.Pattern), Lit(pattern, Vocabulary.Xsd.String)),
        ]);
    }

    /// <summary>A typed literal in the given datatype.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatype">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal Lit(string lexical, Utf8String datatype)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(datatype));
    }
}
