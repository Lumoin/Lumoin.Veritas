using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The datatype-registry arc stage-D END-TO-END battery at MODULE level: operator-registered datatypes
/// consulted through the tableau arms via the registry-carrying decision entries. The registered
/// <c>:Percent</c> is a <see cref="BoundedDatatype"/> over <c>xsd:integer[0,100]</c>; the rows drive a
/// module carrying a data obligation over it (or its complement) through the snapshot and SAT-backed arms
/// and read the verdict off the public decision surface. The delegate row registers a delegate-backed
/// (self-certified) datatype and pins the self-certified provenance marker; the empty-registry row pins
/// byte-identity with zero consult. Each row carries its certified battery id.
/// </summary>
[TestClass]
internal sealed class DatatypeRegistryModuleReasonerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, data properties, individuals, and datatypes are drawn from.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The tableau arms the parametrized rows decide through.</summary>
    internal enum Arm
    {
        /// <summary>The snapshot tableau, <see cref="AlcModuleReasoner"/>.</summary>
        Snapshot,

        /// <summary>The SAT-backed sibling, <see cref="SatTableauModuleReasoner"/>.</summary>
        SatBacked,
    }

    /// <summary>REG-E2E-IN: a data value 50 asserted on a property whose range is the registered :Percent[0,100] is a member, so the module is consistent.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGE2EINValueInRegisteredRangeConsistent(Arm arm)
    {
        ModuleVerdict verdict = DecideConsistency(arm, PercentRegistry(), Module(
            DataPropertyRange("d", PercentReference),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(50))),
            ClassAssertion("A", "x")));

        Assert.IsTrue(verdict.IsConsistent, "50 lies in the registered :Percent[0,100] value space, so the obligation is satisfiable.");
    }

    /// <summary>REG-E2E-OUT: a data value 150 asserted on a property whose range is the registered :Percent[0,100] is outside the value space, so the module is inconsistent.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGE2EOUTValueOutOfRegisteredRangeInconsistent(Arm arm)
    {
        ModuleVerdict verdict = DecideConsistency(arm, PercentRegistry(), Module(
            DataPropertyRange("d", PercentReference),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(150))),
            ClassAssertion("A", "x")));

        Assert.IsFalse(verdict.IsConsistent, "150 lies outside the registered :Percent[0,100] value space, so the obligation clashes.");
    }

    /// <summary>REG-E2E-EMPTY: a demand over the registered :Percent conjoined with its own complement is an empty value space, so the module is inconsistent.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGE2EEMPTYPercentAndComplementInconsistent(Arm arm)
    {
        ModuleVerdict verdict = DecideConsistency(arm, PercentRegistry(), Module(
            SubClassOf(Reference("A"), DataSome("p", new OwlDataIntersectionOf([PercentReference, new OwlDataComplementOf(PercentReference)]))),
            ClassAssertion("A", "x")));

        Assert.IsFalse(verdict.IsConsistent, "Percent conjoined with its complement is empty, so the demand clashes.");
    }

    /// <summary>REG-E2E-IN through the EL-coupled and context arms: the registry reaches every seam, so the value-in-range module stays consistent whichever engine decides it.</summary>
    [TestMethod]
    public void REGE2EINThroughElAndContextArms()
    {
        DatatypeRegistry registry = PercentRegistry();
        ReasoningModule module = Module(
            DataPropertyRange("d", PercentReference),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(50))),
            ClassAssertion("A", "x"));

        Assert.IsTrue(ElCoupledModuleReasoner.DecideConsistency(module, registry, TestContext.CancellationToken).IsConsistent, "The EL-coupled arm consults the registry through its fallback.");
        Assert.IsTrue(ContextSaturationModuleReasoner.DecideConsistency(module, registry, TestContext.CancellationToken).IsConsistent, "The context arm consults the registry through its sidecar or fallback.");
    }

    /// <summary>REG-E2E-OUT through the EL-coupled and context arms: the value-out-of-range module is inconsistent whichever engine decides it.</summary>
    [TestMethod]
    public void REGE2EOUTThroughElAndContextArms()
    {
        DatatypeRegistry registry = PercentRegistry();
        ReasoningModule module = Module(
            DataPropertyRange("d", PercentReference),
            SubClassOf(Reference("A"), DataHasValue("d", IntegerLiteral(150))),
            ClassAssertion("A", "x"));

        Assert.IsFalse(ElCoupledModuleReasoner.DecideConsistency(module, registry, TestContext.CancellationToken).IsConsistent, "The EL-coupled arm consults the registry through its fallback.");
        Assert.IsFalse(ContextSaturationModuleReasoner.DecideConsistency(module, registry, TestContext.CancellationToken).IsConsistent, "The context arm consults the registry through its sidecar or fallback.");
    }

    /// <summary>REG-DELEGATE: a delegate-backed (self-certified) datatype whose oracle empties every conjunction decides a demand inconsistent, and the module verdict names the self-certified provenance marker on its remainder.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGDELEGATESelfCertifiedDecidesAndMarksProvenance(Arm arm)
    {
        DatatypeRegistry registry = DelegateRegistry();
        ModuleDecision decision = DecideModule(arm, registry, Module(
            SubClassOf(Reference("A"), DataSome("d", new OwlDatatypeReference(new NamedNode(Iri("Oracle"))))),
            ClassAssertion("A", "x")));

        Assert.IsNotNull(decision.Verdict);
        Assert.IsFalse(decision.Verdict.IsConsistent, "The delegate empties the demand's value space, so the module clashes.");
        Assert.Contains(DataRestrictionConsistency.SelfCertifiedMarker, decision.Verdict.UnsupportedConstructs, "A delegate-backed decision names its self-certified provenance on the remainder.");
    }

    /// <summary>
    /// REG-DELEGATE-XPAIR: two disjoint data properties each carry a point demand over the same
    /// delegate-backed (self-certified) value; the delegate reports the two forced values the SAME, so the
    /// disjoint cross-pair clashes and the module verdict names the self-certified provenance marker. The
    /// individual point enumerations are each undecided (the delegate abstains on membership), so the marker
    /// can only reach the remainder through the cross-pair value-identity decision.
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGDELEGATEDisjointCrossPairSelfCertifiedMarks(Arm arm)
    {
        DatatypeRegistry registry = ComparingDelegateRegistry();
        ModuleDecision decision = DecideModule(arm, registry, Module(
            Disjoint("e", "f"),
            SubClassOf(Reference("A"), DataHasValue("e", OracleLiteral("v"))),
            SubClassOf(Reference("A"), DataHasValue("f", OracleLiteral("v"))),
            ClassAssertion("A", "x")));

        Assert.IsNotNull(decision.Verdict);
        Assert.IsFalse(decision.Verdict.IsConsistent, "The two disjoint point demands force the same self-certified value, so the cross-pair clashes.");
        Assert.Contains(DataRestrictionConsistency.SelfCertifiedMarker, decision.Verdict.UnsupportedConstructs, "The self-certified cross-pair value-identity decision names its provenance on the remainder.");
    }

    /// <summary>
    /// REG-DELEGATE-POOL: a functional data property pools two point demands over distinct delegate-backed
    /// (self-certified) values into one value; the delegate reports the two values DISTINCT, so the pooled
    /// conjunction is empty and the module clashes through the functional pool, and the verdict names the
    /// self-certified provenance marker. Each point demand alone is undecided (the delegate abstains on
    /// membership), so the marker can only reach the remainder through the pooled decision.
    /// </summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGDELEGATEFunctionalPoolSelfCertifiedMarks(Arm arm)
    {
        DatatypeRegistry registry = ComparingDelegateRegistry();
        ModuleDecision decision = DecideModule(arm, registry, Module(
            Functional("d"),
            SubClassOf(Reference("A"), DataHasValue("d", OracleLiteral("a"))),
            SubClassOf(Reference("A"), DataHasValue("d", OracleLiteral("b"))),
            ClassAssertion("A", "x")));

        Assert.IsNotNull(decision.Verdict);
        Assert.IsFalse(decision.Verdict.IsConsistent, "The functional property pools two distinct self-certified point values into one, an empty pool, so the module clashes.");
        Assert.Contains(DataRestrictionConsistency.SelfCertifiedMarker, decision.Verdict.UnsupportedConstructs, "The self-certified pooled decision names its provenance on the remainder.");
    }

    /// <summary>REG-EMPTY-ID: a decisive built-in-datatype module decided under the empty registry is byte-identical to its pre-arc verdict — inconsistent — and observably fires no self-certified consult.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGEMPTYIDEmptyRegistryDecidesWithoutConsult(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, DatatypeRegistry.Empty, Module(
            SubClassOf(Reference("A"), DataSome("d", IntegerAtMost(3))),
            SubClassOf(Reference("A"), DataAll("d", IntegerAtLeast(5))),
            ClassAssertion("A", "x")));

        Assert.IsNotNull(decision.Verdict);
        Assert.IsFalse(decision.Verdict.IsConsistent, "integer <=3 conjoined with integer >=5 is empty, decided without any registry consult.");
        Assert.DoesNotContain(DataRestrictionConsistency.SelfCertifiedMarker, decision.Verdict.UnsupportedConstructs, "The empty registry fires no self-certified consult.");
    }

    /// <summary>REG-EMPTY-ID control: a consistent built-in-datatype module decided under the empty registry stays consistent with no self-certified marker.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void REGEMPTYIDConsistentControl(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, DatatypeRegistry.Empty, Module(
            SubClassOf(Reference("A"), DataSome("d", IntegerAtLeast(5))),
            ClassAssertion("A", "x")));

        Assert.IsNotNull(decision.Verdict);
        Assert.IsTrue(decision.Verdict.IsConsistent, "integer >=5 is satisfiable, decided without any registry consult.");
        Assert.DoesNotContain(DataRestrictionConsistency.SelfCertifiedMarker, decision.Verdict.UnsupportedConstructs, "The empty registry fires no self-certified consult.");
    }

    /// <summary>REG-EMPTY-ID (IsEmpty flag): the empty registry reports empty and a registry carrying a registration reports non-empty — the fast-path flag every self-certified consult site checks first to skip the range walk.</summary>
    [TestMethod]
    public void REGEMPTYIDIsEmptyReflectsRegistrationCount()
    {
        Assert.IsTrue(DatatypeRegistry.Empty.IsEmpty, "The empty registry carries no registration.");
        Assert.IsFalse(PercentRegistry().IsEmpty, "A registry carrying the :Percent registration is not empty.");
    }

    /// <summary>Builds a registry carrying the :Percent bounded datatype over xsd:integer[0,100].</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry PercentRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new BoundedDatatype(Iri("Percent"), Vocabulary.Xsd.Integer,
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), IntegerLiteral(0)),
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), IntegerLiteral(100)),
        ]));

        return builder.Build();
    }

    /// <summary>Builds a registry carrying the :Oracle delegate-backed datatype whose oracle empties every conjunction.</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry DelegateRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new DelegateBackedDatatype(Iri("Oracle"), new EmptyingOracle().Answer));

        return builder.Build();
    }

    /// <summary>Builds a registry carrying the :Oracle delegate-backed datatype whose oracle abstains on membership and decides value identity by lexical equality — the value-comparison route the cross-pair and functional-pool rows exercise.</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry ComparingDelegateRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new DelegateBackedDatatype(Iri("Oracle"), new ValueEqualityOracle().Answer));

        return builder.Build();
    }

    /// <summary>The registered :Percent datatype reference.</summary>
    private static OwlDatatypeReference PercentReference { get; } = new(new NamedNode(Iri("Percent")));

    /// <summary>Decides the module's consistency through the arm consulting the registry.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <param name="module">The module.</param>
    /// <returns>The verdict.</returns>
    private ModuleVerdict DecideConsistency(Arm arm, DatatypeRegistry registry, ReasoningModule module)
    {
        return arm switch
        {
            Arm.SatBacked => SatTableauModuleReasoner.DecideConsistency(module, registry, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.DecideConsistency(module, registry, TestContext.CancellationToken),
        };
    }

    /// <summary>Decides the module through the arm as a full decision consulting the registry, so its remainder is observable.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="registry">The registered-datatype set.</param>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideModule(Arm arm, DatatypeRegistry registry, ReasoningModule module)
    {
        return arm switch
        {
            Arm.SatBacked => SatTableauModuleReasoner.DecideModule(module, registry, ReasoningBudget.Unbounded, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.DecideModule(module, registry, TestContext.CancellationToken),
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

    /// <summary>A <c>DataPropertyRange</c> axiom.</summary>
    /// <param name="property">The data property local name.</param>
    /// <param name="range">The asserted range.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyRangeAxiom DataPropertyRange(string property, OwlDataRange range)
    {
        return new OwlDataPropertyRangeAxiom(new NamedNode(Iri(property)), range) { Origin = Origin("range") };
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

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>A literal typed with the registered :Oracle datatype.</summary>
    /// <param name="lexical">The literal lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal OracleLiteral(string lexical)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Iri("Oracle")));
    }

    /// <summary>An integer range bounded below inclusively.</summary>
    /// <param name="bound">The inclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtLeast(int bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), IntegerLiteral(bound))]);
    }

    /// <summary>An integer range bounded above inclusively.</summary>
    /// <param name="bound">The inclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAtMost(int bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), IntegerLiteral(bound))]);
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

    /// <summary>A frame binding a datatype oracle that empties every conjunction and admits no value, exposing a method group as the oracle without a lexical closure.</summary>
    private sealed class EmptyingOracle
    {
        /// <summary>The bound membership verdict the oracle returns for a value.</summary>
        private DatatypeMembership Membership { get; } = DatatypeMembership.Out;

        /// <summary>The bound satisfiability verdict the oracle returns for a conjunction.</summary>
        private DatatypeSatisfiability Satisfiability { get; } = DatatypeSatisfiability.Unsatisfiable;

        /// <summary>Answers the folded question from the bound frame state: every conjunction empty, every membership out, carrying no witness.</summary>
        /// <param name="question">The folded question.</param>
        /// <returns>The folded answer.</returns>
        public DatatypeAnswer Answer(in DatatypeQuestion question)
        {
            return question.Operation switch
            {
                DatatypeOperation.Contains => DatatypeAnswer.ForContains(Membership, null),
                DatatypeOperation.DecideConjunction => DatatypeAnswer.ForConjunction(Satisfiability),
                _ => default
            };
        }
    }

    /// <summary>
    /// A frame binding a datatype oracle that abstains on membership and every conjunction but decides value
    /// identity by lexical equality — Same on matching lexical forms, Distinct otherwise. It leaves each point
    /// demand individually undecided (membership abstained), so a decision reaches a verdict only through the
    /// cross-pair or functional-pool value-identity route, exposing a method group as the oracle without a
    /// lexical closure.
    /// </summary>
    private sealed class ValueEqualityOracle
    {
        /// <summary>The bound membership verdict the oracle abstains with for a value, so no point demand is individually decided.</summary>
        private DatatypeMembership Membership { get; } = DatatypeMembership.Indeterminate;

        /// <summary>The bound satisfiability verdict the oracle abstains with for a conjunction.</summary>
        private DatatypeSatisfiability Satisfiability { get; } = DatatypeSatisfiability.Unknown;

        /// <summary>Answers the folded question from the bound frame state: identity by lexical equality, membership and conjunction abstained.</summary>
        /// <param name="question">The folded question.</param>
        /// <returns>The folded answer.</returns>
        public DatatypeAnswer Answer(in DatatypeQuestion question)
        {
            return question.Operation switch
            {
                DatatypeOperation.Contains => DatatypeAnswer.ForContains(Membership, null),
                DatatypeOperation.DecideConjunction => DatatypeAnswer.ForConjunction(Satisfiability),
                DatatypeOperation.SameValue => DatatypeAnswer.ForSameValue(Identity(question.First, question.Second)),
                _ => default
            };
        }

        /// <summary>The identity of two literals by lexical equality — Same on equal lexical forms, Distinct otherwise.</summary>
        /// <param name="first">The first literal.</param>
        /// <param name="second">The second literal.</param>
        /// <returns>The identity verdict.</returns>
        private static DatatypeValueIdentity Identity(Literal? first, Literal? second)
        {
            return first is not null && second is not null && first.Value.Equals(second.Value)
                ? DatatypeValueIdentity.Same
                : DatatypeValueIdentity.Distinct;
        }
    }
}
