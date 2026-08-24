using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The SROIQ datatype-sidecar battery at MODULE level, driven
/// through both tableau arms — the snapshot <see cref="AlcModuleReasoner"/> and
/// the SAT-backed <see cref="SatTableauModuleReasoner"/>. F1 domain typing
/// (R01–R08) reads the entailment surface (module-local subsumptions) or the
/// refutation surface (a class assertion that condemns the module when the
/// class is unsatisfiable); F2 functional value-uniqueness and F3
/// disjoint/sub/equivalent (R09–R29) run the same modules through BOTH arms and
/// assert identical verdicts and identical undecided-marker presence, the parity
/// obligation; the honesty rows (R38–R41) pin that a construct the arms do not
/// decide stays a named remainder or a delegating undecided marker, never a silent
/// decisive verdict, and that a data maximum cardinality decides on the sidecar's
/// max slot instead; R45 runs a two-carrier body union through BOTH arms
/// and pins that the clash condemns only the shared carrier, never a carrier that
/// supplies just part of the clash (the MU9 observable). The certified ground
/// truths are the independently derived battery table.
/// </summary>
[TestClass]
internal sealed class DatatypeSidecarModuleReasonerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, data properties, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The bystander class name whose entailment <c>A ⊑ Bystander</c> would witness that <c>A</c> is unsatisfiable.</summary>
    private const string Bystander = "Bystander";

    /// <summary>The tableau arms the parametrized rows decide through.</summary>
    internal enum Arm
    {
        /// <summary>The snapshot tableau, <see cref="AlcModuleReasoner"/>.</summary>
        Snapshot,

        /// <summary>The SAT-backed sibling, <see cref="SatTableauModuleReasoner"/>.</summary>
        SatBacked,
    }

    //F1 — domain typing.

    /// <summary>R01: a data demand fires the property domain, so the carrier is entailed to be the domain class.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R01DomainFiresOnDemand(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataSome("d", Integer))));

        AssertEntailed(verdict, "A", "C");
    }

    /// <summary>R02: a data demand types the carrier into a domain class emptied by owl:Nothing, so the carrier is unsatisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R02DomainIntoNothingIsUnsatisfiable(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("C"), Nothing),
            SubClassOf(Reference("A"), DataSome("d", Integer)),
            ClassAssertion("A", "x")));

        Assert.IsFalse(verdict.IsConsistent, "A is domain-typed C, and C ⊑ ⊥, so an A instance is unsatisfiable.");
    }

    /// <summary>R03: a sub-property demand fires a super-property domain through the box closure.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R03DomainFiresThroughSubPropertyClosure(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("e", Reference("C")),
            SubDataProperty("d", "e"),
            SubClassOf(Reference("A"), DataSome("d", Integer))));

        AssertEntailed(verdict, "A", "C");
    }

    /// <summary>R04: a demand on an unrelated property does not fire the domain, so the carrier is not entailed to be the domain class.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R04UnrelatedPropertyDoesNotFireDomain(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataSome("e", Integer))));

        AssertNotEntailed(verdict, "A", "C");
    }

    /// <summary>R05: a has-value demand fires the property domain.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R05DomainFiresOnHasValue(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5)))));

        AssertEntailed(verdict, "A", "C");
    }

    /// <summary>R06: a positive counting demand fires the property domain.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R06DomainFiresOnMinCardinality(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("A"), DataMinCard(1, "d", Integer))));

        AssertEntailed(verdict, "A", "C");
    }

    /// <summary>R07: a demand over an empty integer interval makes the carrier unsatisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R07EmptyIntervalDemandIsUnsatisfiable(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            SubClassOf(Reference("A"), DataSome("d", IntegerRestriction((Vocabulary.XsdFacets.MinExclusive, 5), (Vocabulary.XsdFacets.MaxExclusive, 3)))),
            ClassAssertion("A", "x")));

        Assert.IsFalse(verdict.IsConsistent, "The demand's integer interval (>5 and <3) is empty, so an A instance clashes.");
    }

    /// <summary>R08: domain typing to C plus B ⊑ C does not entail A ⊑ B — the domain-typed carrier is a C, not a B.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R08DomainDoesNotEntailAnUnrelatedSubclass(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            DataDomain("d", Reference("C")),
            SubClassOf(Reference("B"), Reference("C")),
            SubClassOf(Reference("A"), DataSome("d", StringType))));

        AssertNotEntailed(verdict, "A", "B");
    }

    //F2 — functional data properties.

    /// <summary>R09: overlapping ranges on a functional property share a value, so the carrier is satisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R09FunctionalOverlappingRangesSatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerAtMost(10))));
    }

    /// <summary>R10: disjoint ranges on a functional property cannot share a value, so the carrier is unsatisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R10FunctionalDisjointRangesUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(3))));
    }

    /// <summary>R11: without functionality the two demands take different values, so the carrier is satisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R11NoFunctionalityTwoDemandsSatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(3))));
    }

    /// <summary>R12: a functional property cannot carry two distinct values, so a minimum cardinality of two is unsatisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R12FunctionalMinCardinalityTwoUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("d"),
            SubClassOf(Reference("A"), DataMinCard(2, "d", Integer)));
    }

    /// <summary>R13: functionality pools a demand on a property and one on its functional super-property across disjoint ranges.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R13FunctionalPoolingViaSubPropertyUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("f"),
            SubDataProperty("d", "f"),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("A"), DataSome("f", IntegerBelow(3))));
    }

    /// <summary>R14: two has-value demands whose literals denote the same integer (5 and 05) agree on a functional property.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R14FunctionalSameValuedHasValuesSatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("d", new Literal(Utf8Strings.From("05"), new NamedNode(Vocabulary.Xsd.Integer)))));
    }

    /// <summary>R15: two distinct has-value demands cannot both hold of a functional property's single value.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R15FunctionalDistinctHasValuesUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(7))));
    }

    /// <summary>R16: a functional existential constrained by a same-property universal still admits a value above the bound.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R16FunctionalExistentialUnderUniversalSatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", Integer)),
            SubClassOf(Reference("A"), DataAll("d", IntegerAbove(10))));
    }

    /// <summary>R17: an existential whose range is disjoint from the same-property universal clashes without any functionality.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R17ExistentialDisjointFromUniversalUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))),
            SubClassOf(Reference("A"), DataAll("d", IntegerAbove(10))));
    }

    /// <summary>R18: a functional property pooling a string demand and an integer demand crosses disjoint families.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R18FunctionalAcrossDisjointFamiliesUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", StringType)),
            SubClassOf(Reference("A"), DataSome("d", Integer)));
    }

    /// <summary>R19: a vacuous minimum cardinality of zero never joins a functional pool, so the carrier stays satisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R19FunctionalPoolExcludesVacuousMinCardinalityZero(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", Integer)),
            SubClassOf(Reference("A"), DataMinCard(0, "d", StringType)));
    }

    //F3 — disjoint / sub / equivalent data properties.

    /// <summary>R20: two has-value demands forcing the same value into a disjoint property pair clash.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R20DisjointPairSamePointValueUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataHasValue("a", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("b", IntegerLiteral(5))));
    }

    /// <summary>R21: two distinct has-value demands across a disjoint pair co-exist.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R21DisjointPairDistinctPointValuesSatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataHasValue("a", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("b", IntegerLiteral(7))));
    }

    /// <summary>R22: a single property below both members of a disjoint pair forces one value into both (the common-subproperty rule).</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R22CommonSubPropertyOfDisjointPairUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Disjoint("a", "b"),
            SubDataProperty("d", "a"),
            SubDataProperty("d", "b"),
            SubClassOf(Reference("A"), DataSome("d", Integer)));
    }

    /// <summary>R23: equivalent properties that are also disjoint reduce to a self-disjoint property.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R23EquivalentAndDisjointUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Disjoint("a", "b"),
            EquivalentDataProperties("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", Integer)));
    }

    /// <summary>R24: two unconstrained existentials across a disjoint pair take different integer values.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R24DisjointPairFreeValueChoiceSatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", Integer)),
            SubClassOf(Reference("A"), DataSome("b", Integer)));
    }

    /// <summary>R25: a functional super-property pools demands from both members of a disjoint pair into one shared value.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R25FunctionalForcedSharedValueAcrossDisjointPairUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("f"),
            SubDataProperty("a", "f"),
            SubDataProperty("b", "f"),
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", Integer)),
            SubClassOf(Reference("A"), DataSome("b", Integer)));
    }

    /// <summary>R26: a super-property's asserted range constrains a sub-property's demand into an empty conjunction.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R26SubPropertyDemandUnderSuperRangeUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            SubDataProperty("d", "e"),
            DataPropertyRange("e", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>R27: an asserted range on an unrelated property does not constrain the demand.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R27RangeOnUnrelatedPropertyDoesNotConstrain(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: true,
            DataPropertyRange("e", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>R28: an equivalent property's asserted range flows across the equivalence and empties the demand.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R28EquivalentPropertyRangeConstrainsUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            EquivalentDataProperties("d", "e"),
            DataPropertyRange("e", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    /// <summary>
    /// R29: a disjoint pair whose two demands are each a degenerate single-point
    /// interval canonicalizes both to the same point enumeration, so BOTH arms
    /// prove the disjoint clash decisively — the module is inconsistent, and no
    /// undecided marker remains on the remainder.
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R29DisjointPairForcedToDegeneratePointInconsistent(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            Disjoint("a", "b"),
            SubClassOf(Reference("A"), DataSome("a", IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)))),
            SubClassOf(Reference("A"), DataSome("b", IntegerRestriction((Vocabulary.XsdFacets.MinInclusive, 5), (Vocabulary.XsdFacets.MaxInclusive, 5)))),
            ClassAssertion("A", "x")));

        Assert.IsFalse(verdict.IsConsistent, "The degenerate points canonicalize to one point, so the disjoint pair clashes decisively.");
        Assert.DoesNotContain(DataRestrictionConsistency.UndecidedMarker, verdict.UnsupportedConstructs, "R29 now decides, so no undecided marker remains on the remainder.");
    }

    //R35 — xsd:string value identity is decisive on every arm.

    /// <summary>
    /// R35: an enumeration existential constrained by a universal excluding one
    /// member. The shared value-space checker models <c>xsd:string</c> value
    /// identity decisively — the string lexical-to-value mapping is the
    /// identity function, so "b" is provably not the excluded "a" — and the
    /// surviving "b" witness satisfies both restrictions, so both arms decide
    /// the module whole and consistent with no undecided marker on the
    /// remainder (the ground-truth SAT verdict).
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R35StringEnumerationExclusionDecidesConsistent(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            SubClassOf(Reference("A"), DataSome("d", OneOf(StringLiteral("a"), StringLiteral("b")))),
            SubClassOf(Reference("A"), DataAll("d", ComplementOf(OneOf(StringLiteral("a"))))),
            ClassAssertion("A", "x")));

        Assert.IsTrue(verdict.IsConsistent, "The d = \"b\" witness satisfies the enumeration and the exclusion, so the module is consistent.");
        Assert.DoesNotContain(DataRestrictionConsistency.UndecidedMarker, verdict.UnsupportedConstructs, "R35 now decides, so no undecided marker remains on the remainder.");
    }

    /// <summary>
    /// R45: two named classes C ⊑ A and C ⊑ B share a carrier where A demands an
    /// existential above five and B a universal below three on the same property.
    /// The shared carrier's clash condemns only the intersection C, not A alone —
    /// the MU9 observable pins that a weakened single-body clash must not derive a
    /// spurious A ⊑ ⊥, which would wrongly entail A ⊑ Bystander.
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R45TwoCarrierDataClashCondemnsOnlyTheCarrierIntersection(Arm arm)
    {
        OwlAxiom[] tbox =
        [
            SubClassOf(Reference("C"), Reference("A")),
            SubClassOf(Reference("C"), Reference("B")),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Reference("B"), DataAll("d", IntegerBelow(3))),
        ];

        List<OwlAxiom> carrierAxioms = [.. tbox, ClassAssertion("C", "x")];
        ModuleVerdict carrierVerdict = Decide(arm, Module([.. carrierAxioms]));
        Assert.IsFalse(carrierVerdict.IsConsistent, "The shared carrier C inherits both the existential and the disjoint universal, so a C instance clashes.");

        List<OwlAxiom> aloneAxioms = [.. tbox, ClassAssertion("A", "y")];
        ModuleVerdict aloneVerdict = Decide(arm, Module([.. aloneAxioms]));
        Assert.IsTrue(aloneVerdict.IsConsistent, "A alone carries only the existential above five, so an A instance is satisfiable.");

        List<OwlAxiom> subsumptionAxioms = [.. tbox, SubClassOf(Reference(Bystander), Thing)];
        AssertNotEntailed(Decide(arm, Module([.. subsumptionAxioms])), "A", Bystander);
    }

    //R46/R47 — addendum-certified UNSAT rows (the F1 exact-core fix; the MU1 two-hop sub-closure catcher).

    /// <summary>
    /// R46: a functional property pools two existentials that each individually
    /// survive the node universal, but the pooled conjunction — both existentials
    /// AND the universal together — is empty, so the carrier is unsatisfiable (the
    /// functional-pool clash driven by the universal, the exact-core fix's
    /// module-level pin).
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R46FunctionalPoolClashDrivenByUniversalUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            Functional("d"),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))),
            SubClassOf(Reference("A"), DataSome("d", IntegerAbove(2))),
            SubClassOf(Reference("A"), DataAll("d", OneOf(IntegerLiteral(1), IntegerLiteral(6)))));
    }

    /// <summary>
    /// R47: a two-hop sub-property closure (d ⊑ e ⊑ f) carries a range asserted only
    /// on the top of the chain, f, down to a demand on d — the MU1 catcher every
    /// prior sub-closure row reached in a single hop.
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R47TwoHopSubPropertyRangeClosureUnsatisfiable(Arm arm)
    {
        AssertClassSatisfiability(arm, satisfiable: false,
            SubDataProperty("d", "e"),
            SubDataProperty("e", "f"),
            DataPropertyRange("f", IntegerAbove(10)),
            SubClassOf(Reference("A"), DataSome("d", IntegerBelow(5))));
    }

    //R48/R49/R50 — the mutation-survivor killer rows (object-successor clash routing).

    /// <summary>
    /// R48: Q's r-successor is forced to be both the existential witness B and the
    /// universal filler A1, so a Q instance is unsatisfiable; the clash must
    /// invert through the Pred rule into Q, not condemn the shared filler B
    /// unconditionally — a B instance stays satisfiable, and B ⊑ Bystander is not
    /// entailed (the MU9 discriminator: a clash-body union truncated to its first
    /// contributor would wrongly derive that entailment).
    /// </summary>
    /// <remarks>
    /// The row runs the SAT-backed arm only: proving the UNSAT cell on the
    /// snapshot arm exhausts its internalized-GCI choice space with the
    /// concrete-domain clash visible only at full expansion — measured
    /// infeasible (killed after minutes; the SAT-backed arm decides the same
    /// cells in milliseconds). The context-engine sibling in
    /// <see cref="ContextDatatypeSidecarTests"/> carries the mutation kill.
    /// </remarks>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.SatBacked)]
    public void R48ClashBodyInversionIntoThePredecessorLeavesTheFillerSatisfiable(Arm arm)
    {
        OwlAxiom[] tbox =
        [
            SubClassOf(Reference("Q"), ObjectSome("r", Reference("B"))),
            SubClassOf(Reference("Q"), ObjectAll("r", Reference("A1"))),
            SubClassOf(Reference("B"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("A1"), DataAll("d", IntegerBelow(3))),
        ];

        List<OwlAxiom> qAxioms = [.. tbox, ClassAssertion("Q", "x")];
        ModuleVerdict qVerdict = Decide(arm, Module([.. qAxioms]));
        Assert.IsFalse(qVerdict.IsConsistent, "Q's r-successor inherits both B's existential and A1's universal, so a Q instance clashes.");

        List<OwlAxiom> bAxioms = [.. tbox, ClassAssertion("B", "y")];
        ModuleVerdict bVerdict = Decide(arm, Module([.. bAxioms]));
        Assert.IsTrue(bVerdict.IsConsistent, "B alone carries only the existential, so a B instance is satisfiable.");

        List<OwlAxiom> subsumptionAxioms = [.. tbox, SubClassOf(Reference(Bystander), Thing)];
        AssertNotEntailed(Decide(arm, Module([.. subsumptionAxioms])), "B", Bystander);
    }

    /// <summary>
    /// R49: P's own existential lands first and decides the module consistent,
    /// then a three-hop subclass chain (P ⊑ X ⊑ Y) delivers Y's disjoint
    /// universal in a later round; the demand-set memo must recognise the changed
    /// signature and re-decide the clash, or a P instance is wrongly left
    /// satisfiable (the MU14 discriminator).
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R49StaggeredThreeHopUniversalForcesRedecision(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            SubClassOf(Reference("P"), DataSome("d", IntegerAtLeast(5))),
            SubClassOf(Reference("P"), Reference("X")),
            SubClassOf(Reference("X"), Reference("Y")),
            SubClassOf(Reference("Y"), DataAll("d", IntegerBelow(3))),
            ClassAssertion("P", "x")));

        Assert.IsFalse(verdict.IsConsistent, "The staggered universal arriving through the three-hop chain must still clash with P's own existential.");
    }

    /// <summary>
    /// R50: both predecessors reach the same r-successor shape through the single
    /// existential occurrence E ⊑ ∃r.C — P_X directly, P_Y through a three-hop
    /// subclass chain — and both fillers funnel into the single demand occurrence
    /// W ⊑ ∃d.integer[≥5], so the successor carries C's universal beside W's
    /// existential either way and a P_X instance and a P_Y instance are both
    /// unsatisfiable (the tableau-arm parity face of the MU15 discriminator,
    /// whose staggered-landing kill lives in the context-engine sibling).
    /// </summary>
    /// <remarks>
    /// The row runs the SAT-backed arm only, for the same measured reason as
    /// the R48 row: the object-role UNSAT cells are infeasible on the snapshot
    /// arm's internalized-GCI search. The context-engine sibling in
    /// <see cref="ContextDatatypeSidecarTests"/> carries the mutation kill.
    /// </remarks>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.SatBacked)]
    public void R50SignatureUnchangedSecondContributorRoutesThroughReemission(Arm arm)
    {
        OwlAxiom[] tbox =
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
        ];

        List<OwlAxiom> pxAxioms = [.. tbox, ClassAssertion("P_X", "x")];
        ModuleVerdict pxVerdict = Decide(arm, Module([.. pxAxioms]));
        Assert.IsFalse(pxVerdict.IsConsistent, "P_X's r-successor carries C's universal and X's existential, so a P_X instance clashes.");

        List<OwlAxiom> pyAxioms = [.. tbox, ClassAssertion("P_Y", "y")];
        ModuleVerdict pyVerdict = Decide(arm, Module([.. pyAxioms]));
        Assert.IsFalse(pyVerdict.IsConsistent, "P_Y reaches the same successor shape through the three-hop chain, so a P_Y instance clashes too.");

        List<OwlAxiom> eAxioms = [.. tbox, ClassAssertion("E", "z")];
        ModuleVerdict eVerdict = Decide(arm, Module([.. eAxioms]));
        Assert.IsTrue(eVerdict.IsConsistent, "E alone gives the successor only C's universal — no existential demand — so an E instance is satisfiable.");
    }

    //Honesty rows — named remainder or delegating undecided marker, never a silent decisive verdict.

    /// <summary>R38: an unqualified DataMaxCardinality translates to the sidecar's range-less max slot, so the module decides WHOLE — no named remainder — and the slot closes a carrier that also forces two distinct values on the property.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R38DataMaxCardinalityDecidesOnTheRangeLessSlot(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            SubClassOf(Reference("A"), new OwlDataCardinality(OwlCardinalityKind.Max, 1, new NamedNode(Iri("d")), null))));

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The range-less maximum is inside the fragment, so the verdict covers the module whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "A bound alone forces no value, so the module is consistent.");
        Assert.IsEmpty(decision.Verdict!.UnsupportedConstructs, "The translated maximum leaves nothing on the remainder.");

        ModuleVerdict closed = Decide(arm, Module(
            SubClassOf(Reference("A"), new OwlDataCardinality(OwlCardinalityKind.Max, 1, new NamedNode(Iri("d")), null)),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(5))),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(7))),
            ClassAssertion("A", "x")));

        Assert.IsFalse(closed.IsConsistent, "One range-less slot cannot hold two distinct integers, so the populated carrier closes.");
    }

    /// <summary>
    /// M8: the mixed-pool flagship across both tableau arms. A range-less exact
    /// cardinality of two beside two provably-distinct told values pools a counting
    /// demand with the points in the max slot; the points fit the bound and each
    /// inhabits the counting demand's literal-top range, so the pool is the model,
    /// the module decides WHOLE — no undecided marker — and the populated carrier
    /// is consistent.
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void M8RangeLessExactTwoTwoDistinctValuesDecidesConsistent(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            SubClassOf(Reference("A"), new OwlDataCardinality(OwlCardinalityKind.Exact, 2, new NamedNode(Iri("d")), null)),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("alpha"))),
            SubClassOf(Reference("A"), DataHasValue("d", StringLiteral("beta"))),
            ClassAssertion("A", "x")));

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The mixed pool certifies its own model, so the verdict covers the module whole.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Two distinct points fit the bound of two and both witness the counting demand, so the carrier is consistent.");
        Assert.DoesNotContain(DataRestrictionConsistency.UndecidedMarker, decision.Verdict!.UnsupportedConstructs, "The certified slot leaves no undecided-obligation marker.");
    }

    /// <summary>R39: a DatatypeDefinition is a named remainder — neither arm interprets it.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R39DatatypeDefinitionIsNamedRemainder(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            new OwlDatatypeDefinitionAxiom(new NamedNode(Iri("D")), IntegerAbove(5)) { Origin = Origin("def") },
            SubClassOf(Reference("A"), DataSome("d", new OwlDatatypeReference(new NamedNode(Iri("D")))))));

        AssertNamedRemainder(decision, nameof(OwlDatatypeDefinitionAxiom));
    }

    /// <summary>R40: a pattern-facet range over <c>xsd:string</c> is decided by the built-in automaton route — the language of <c>[0-9]+</c> is non-empty — so the demand is satisfiable and the module is consistent and fully decided, carrying no undecided marker.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R40PatternFacetRangeDecidedByAutomatonRoute(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            SubClassOf(Reference("A"), DataSome("d", StringPattern("[0-9]+"))),
            ClassAssertion("A", "x")));

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The pattern-facet range is decided by the automaton route, so the verdict is not fragment-relative.");
        Assert.IsNotNull(decision.Verdict);
        Assert.IsTrue(decision.Verdict.IsConsistent, "A string matching [0-9]+ exists, so the demand is satisfiable.");
        Assert.DoesNotContain(DataRestrictionConsistency.UndecidedMarker, decision.Verdict.UnsupportedConstructs, "The pattern range is decided, so no undecided marker remains.");
    }

    /// <summary>R41: a HasKey axiom is a named remainder — neither arm interprets it — and the keyed class stays satisfiable.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void R41HasKeyIsNamedRemainder(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            new OwlHasKeyAxiom(Reference("C"), [], [new NamedNode(Iri("d"))]) { Origin = Origin("key") },
            ClassAssertion("C", "x")));

        AssertNamedRemainder(decision, nameof(OwlHasKeyAxiom));
    }

    /// <summary>Asserts a class is satisfiable (a class assertion keeps the module consistent) or unsatisfiable (it condemns the module), through the given arm.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="satisfiable">Whether the class A is satisfiable.</param>
    /// <param name="tboxAxioms">The TBox and RBox axioms; a <c>ClassAssertion(A, x)</c> is added to force an A instance.</param>
    private void AssertClassSatisfiability(Arm arm, bool satisfiable, params OwlAxiom[] tboxAxioms)
    {
        List<OwlAxiom> axioms = [.. tboxAxioms, ClassAssertion("A", "x")];
        ModuleVerdict verdict = Decide(arm, Module([.. axioms]));

        Assert.AreEqual(satisfiable, verdict.IsConsistent);
    }

    /// <summary>Asserts the entailment <c>sub ⊑ super</c> surfaces on the consistent module's subsumptions.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    private static void AssertEntailed(ModuleVerdict verdict, string sub, string super)
    {
        Assert.IsTrue(verdict.IsConsistent, "The entailment surface is the subsumption sweep, which runs only on a consistent module.");
        Assert.IsTrue(HasSubsumption(verdict, sub, super), $"{sub} ⊑ {super} must be entailed.");
    }

    /// <summary>Asserts the entailment <c>sub ⊑ super</c> does NOT surface on the consistent module's subsumptions.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    private static void AssertNotEntailed(ModuleVerdict verdict, string sub, string super)
    {
        Assert.IsTrue(verdict.IsConsistent, "The control module is consistent so the subsumption sweep runs.");
        Assert.IsFalse(HasSubsumption(verdict, sub, super), $"{sub} ⊑ {super} must NOT be entailed.");
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

    /// <summary>Asserts the decision is consistent but fragment-relative, naming the given construct on its remainder.</summary>
    /// <param name="decision">The module decision.</param>
    /// <param name="construct">The named remainder entry expected.</param>
    private static void AssertNamedRemainder(ModuleDecision decision, string construct)
    {
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, "A named remainder scopes the consistent verdict to the modelled fragment.");
        Assert.IsNotNull(decision.Verdict);
        Assert.IsTrue(decision.Verdict.IsConsistent);
        Assert.Contains(construct, decision.Verdict.UnsupportedConstructs, $"The remainder must name {construct}, not silently decide it.");
    }

    /// <summary>Decides the module through the arm's full entry — consistency plus the subsumption sweep.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="module">The module.</param>
    /// <returns>The verdict.</returns>
    private ModuleVerdict Decide(Arm arm, ReasoningModule module)
    {
        return arm switch
        {
            Arm.SatBacked => SatTableauModuleReasoner.Decide(module, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.Decide(module, TestContext.CancellationToken),
        };
    }

    /// <summary>Decides the module through the arm as a full decision, so its fragment-relative outcome is observable.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideModule(Arm arm, ReasoningModule module)
    {
        return arm switch
        {
            Arm.SatBacked => SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.DecideModule(module, TestContext.CancellationToken),
        };
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

    /// <summary>The local name of an example-namespace node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The local name.</returns>
    private static string Local(NamedNode node)
    {
        return node.Iri.ToString()[Example.Length..];
    }

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
    /// <param name="bounds">The facet–bound pairs.</param>
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

    /// <summary>An <c>xsd:string</c> range restricted by a pattern facet, decided by the built-in automaton route.</summary>
    /// <param name="pattern">The pattern lexical form.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction StringPattern(string pattern)
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.String),
            [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.Pattern), new Literal(Utf8Strings.From(pattern), new NamedNode(Vocabulary.Xsd.String)))]);
    }
}
