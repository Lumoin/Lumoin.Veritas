using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="ElClassifier"/>: the completion rules against
/// hand-built TBoxes with known closures — told transitivity, conjunction,
/// existential introduction and elimination, role composition, bottom
/// propagation, domains and ranges, and the honest coverage report.
/// </summary>
[TestClass]
internal sealed class ElClassifierTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    private static Utf8String Thing { get; } = Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing");

    private static Utf8String Nothing { get; } = Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing");

    /// <summary>Told subsumptions close transitively.</summary>
    [TestMethod]
    public void ToldHierarchyCloses()
    {
        ElClassification result = Classify(
            SubClassOf(Named("Car"), Named("Vehicle")),
            SubClassOf(Named("Vehicle"), Named("Artifact")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("Car"), Iri("Artifact")));
        Assert.IsTrue(result.IsSubsumedBy(Iri("Car"), Thing));
        Assert.IsFalse(result.IsSubsumedBy(Iri("Artifact"), Iri("Car")));
        Assert.IsTrue(result.IsCoherent);
    }

    /// <summary>The conjunction rule: membership in both conjuncts yields the defined class.</summary>
    [TestMethod]
    public void ConjunctionFires()
    {
        ElClassification result = Classify(
            SubClassOf(new OwlObjectIntersectionOf([Named("Male"), Named("Parent")]), Named("Father")),
            SubClassOf(Named("X"), Named("Male")),
            SubClassOf(Named("X"), Named("Parent")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("X"), Iri("Father")));
        Assert.IsFalse(result.IsSubsumedBy(Iri("Male"), Iri("Father")));
    }

    /// <summary>Existential introduction and elimination compose: A ⊑ ∃r.B and ∃r.B ⊑ C give A ⊑ C.</summary>
    [TestMethod]
    public void ExistentialComposes()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A"), Some("r", Named("B"))),
            SubClassOf(Some("r", Named("B")), Named("C")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Iri("C")));
    }

    /// <summary>A transitive role composes its edges before existential elimination.</summary>
    [TestMethod]
    public void TransitiveRoleComposes()
    {
        ElClassification result = Classify(
            Transitive("partOf"),
            SubClassOf(Named("Wheel"), Some("partOf", Named("Car"))),
            SubClassOf(Named("Car"), Some("partOf", Named("Fleet"))),
            SubClassOf(Some("partOf", Named("Fleet")), Named("FleetComponent")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("Wheel"), Iri("FleetComponent")), "Wheel —partOf→ Car —partOf→ Fleet composes.");
        Assert.IsTrue(result.IsSubsumedBy(Iri("Car"), Iri("FleetComponent")));
    }

    /// <summary>A general property chain composes two distinct roles: <c>r∘s ⊑ t</c> links an r-edge and an s-edge into a t-edge, which an existential elimination over t then reads.</summary>
    [TestMethod]
    public void GeneralChainComposes()
    {
        ElClassification result = Classify(
            Chain("t", "r", "s"),
            SubClassOf(Named("A"), Some("r", Named("B"))),
            SubClassOf(Named("B"), Some("s", Named("D"))),
            SubClassOf(Some("t", Named("D")), Named("G")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Iri("G")), "A —r→ B —s→ D composes to a t-edge A —t→ D, so ∃t.D ⊑ G subsumes A.");
    }

    /// <summary>A three-link chain decomposes left-associatively through fresh roles and still composes end to end.</summary>
    [TestMethod]
    public void ThreeLinkChainComposes()
    {
        ElClassification result = Classify(
            Chain("t", "r", "s", "u"),
            SubClassOf(Named("A"), Some("r", Named("B"))),
            SubClassOf(Named("B"), Some("s", Named("C"))),
            SubClassOf(Named("C"), Some("u", Named("D"))),
            SubClassOf(Some("t", Named("D")), Named("G")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Iri("G")), "A —r→ B —s→ C —u→ D composes to A —t→ D.");
    }

    /// <summary>Disjointness makes a doubly-typed class unsatisfiable, and unsatisfiability propagates backwards over edges.</summary>
    [TestMethod]
    public void BottomPropagates()
    {
        ElClassification result = Classify(
            Disjoint(Named("Male"), Named("Female")),
            SubClassOf(Named("X"), Named("Male")),
            SubClassOf(Named("X"), Named("Female")),
            SubClassOf(Named("Y"), Some("r", Named("X"))));

        Assert.IsFalse(result.IsSatisfiable(Iri("X")));
        Assert.IsFalse(result.IsSatisfiable(Iri("Y")), "An edge into an unsatisfiable filler is unsatisfiable.");
        Assert.IsTrue(result.IsSatisfiable(Iri("Male")));
        Assert.IsFalse(result.IsCoherent);
        Assert.IsTrue(result.IsSubsumedBy(Iri("X"), Iri("Female")), "An unsatisfiable class is subsumed by everything.");
    }

    /// <summary>
    /// A told global reflexivity under an irreflexive role makes the TBox inconsistent — the non-empty
    /// domain forces a reflexive self-edge the irreflexive characteristic forbids — and the correct
    /// classification of an inconsistent TBox subsumes every named class by <c>owl:Nothing</c>. On the
    /// document classification path there is no ABox, so <see cref="Top"/> is the sole non-empty-domain
    /// witness and the seeded <c>⊤ ⊑ ⊥</c> propagates to every atom.
    /// </summary>
    [TestMethod]
    public void ToldReflexiveIrreflexiveClashClassifiesEveryClassAsNothing()
    {
        ElClassification result = Classify(
            Reflexive("r"),
            Irreflexive("r"),
            SubClassOf(Named("A"), Named("B")));

        Assert.IsFalse(result.IsCoherent, "The reflexive x irreflexive clash empties the non-empty domain, so the TBox is incoherent.");
        Assert.IsFalse(result.IsSatisfiable(Iri("A")), "Every named class is unsatisfiable in an inconsistent TBox.");
        Assert.IsFalse(result.IsSatisfiable(Iri("B")), "Every named class is unsatisfiable in an inconsistent TBox.");
        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Nothing), "An unsatisfiable class is subsumed by owl:Nothing.");
        Assert.IsTrue(result.IsSubsumedBy(Iri("B"), Nothing), "An unsatisfiable class is subsumed by owl:Nothing.");
    }

    /// <summary>
    /// The forced-empty inference decides <c>A ⊑ owl:Nothing</c> on the document classification path:
    /// <c>Symmetric(r) + Asymmetric(r)</c> empties <c>r</c>, so <c>A ⊑ ∃r.B</c> makes <c>A</c> unsatisfiable
    /// while <c>B</c> stays satisfiable and the TBox stays consistent (a model with empty <c>r</c>, empty
    /// <c>A</c>, and non-empty <c>B</c>). The classifier's direct document path runs the same
    /// ground-role-feature gate as the module path, so the reduction fires with no ABox.
    /// </summary>
    [TestMethod]
    public void SymmetricAsymmetricEmptyRoleClassifiesOwnerAsNothing()
    {
        ElClassification result = Classify(
            Symmetric("r"),
            Asymmetric("r"),
            SubClassOf(Named("A"), Some("r", Named("B"))));

        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Nothing), "A forces an r-edge over the empty role, so A ⊑ owl:Nothing.");
        Assert.IsFalse(result.IsSubsumedBy(Iri("B"), Nothing), "B is the filler of the empty existential, never emptied, so B is not owl:Nothing.");
        Assert.IsTrue(result.IsSatisfiable(Iri("B")), "B is satisfiable — the TBox has a model with empty r, empty A, and non-empty B — so the document is consistent though A is not.");
    }

    /// <summary>Equivalence yields mutual subsumption.</summary>
    [TestMethod]
    public void EquivalenceIsMutual()
    {
        ElClassification result = Classify(
            new OwlEquivalentClassesAxiom(Named("Human"), Named("Person")) { Origin = Origin });

        Assert.IsTrue(result.IsSubsumedBy(Iri("Human"), Iri("Person")));
        Assert.IsTrue(result.IsSubsumedBy(Iri("Person"), Iri("Human")));
    }

    /// <summary>
    /// A domain axiom types every edge source — the source genuinely bears the
    /// role, so it gains the domain. A range axiom types the existential's
    /// anonymous successor, not the named filler class: the filler is used as a
    /// successor here but is not globally constrained to the range, so the range
    /// must not contaminate it.
    /// </summary>
    [TestMethod]
    public void DomainTypesSourceAndRangeDoesNotContaminateTheNamedFiller()
    {
        ElClassification result = Classify(
            new OwlObjectPropertyDomainAxiom(Property("hasChild"), Named("Parent")) { Origin = Origin },
            new OwlObjectPropertyRangeAxiom(Property("hasChild"), Named("Child")) { Origin = Origin },
            SubClassOf(Named("Mother"), Some("hasChild", Named("Person"))));

        Assert.IsTrue(result.IsSubsumedBy(Iri("Mother"), Iri("Parent")), "The edge source gains the domain.");
        Assert.IsFalse(result.IsSubsumedBy(Iri("Person"), Iri("Child")), "The range types the anonymous successor, not the named filler class Person.");
    }

    /// <summary>An uninterpreted construct is recorded, never silently dropped.</summary>
    [TestMethod]
    public void UnsupportedConstructsAreReported()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A"), new OwlObjectComplementOf(Named("B"))));

        Assert.IsNotEmpty(result.UnsupportedConstructs);
    }

    /// <summary>
    /// A subclass-side inverse existential stays owner-local on the TBox-classification path: the
    /// document entry runs the same ground-role-feature gate as the module path, so the reduction's
    /// mirror fires over per-owner minted witnesses and <c>∃r⁻.A1 ⊑ ⊥</c> empties exactly the class
    /// whose successor creation it forbids. Without the gate the mirror would ride a shared filler
    /// node and one owner's clash would condemn every owner of the same existential.
    /// </summary>
    [TestMethod]
    public void InverseExistentialClashStaysOwnerLocalOnTheClassificationPath()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A1"), Some("r", Named("B"))),
            SubClassOf(Named("A2"), Some("r", Named("B"))),
            SubClassOf(
                new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Iri("r"))), Named("A1")),
                new OwlClassReference(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")))));

        Assert.IsFalse(result.IsSatisfiable(Iri("A1")), "A1's forced r-successor has an A1 predecessor, which ∃r⁻.A1 ⊑ ⊥ forbids.");
        Assert.IsTrue(result.IsSatisfiable(Iri("A2")), "A2's witness has an A2 predecessor, not an A1 one — the clash must not cross owners.");
        Assert.IsEmpty(result.UnsupportedConstructs);
    }

    /// <summary>
    /// A superclass-position inverse existential inside a property DOMAIN is decided on the direct
    /// <c>Classify(document)</c> path with no unsupported marker: a domain axiom normalizes to
    /// <c>∃p.⊤ ⊑ ∃r⁻.C</c>, whose superclass inverse existential the eager generator reduction reduces to a
    /// forward existential over a synthetic per-<c>r</c> generator role (<c>g ⊑ r⁻</c>). The module survey
    /// admits the same occurrence, so both tiers decide it and the document path pins the normal form the
    /// module path rides.
    /// </summary>
    [TestMethod]
    public void InverseExistentialInDomainIsDecidedOnTheClassificationPath()
    {
        ElClassification result = Classify(
            new OwlObjectPropertyDomainAxiom(
                Property("p"),
                new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Iri("r"))), Named("C"))) { Origin = Origin });

        Assert.IsEmpty(result.UnsupportedConstructs, "The generator reduction reaches the domain class on the direct classification path, so nothing is recorded unsupported.");
    }

    /// <summary>
    /// A superclass-position inverse existential inside a property RANGE is decided on the direct
    /// <c>Classify(document)</c> path with no unsupported marker: a range axiom names a complex range as a
    /// fresh atom <c>F</c> with <c>F ⊑ ∃r⁻.C</c> and registers <c>F</c> as the role's range, and that
    /// inclusion's superclass inverse existential rides the eager generator reduction to a forward
    /// existential over a synthetic per-<c>r</c> generator role (<c>g ⊑ r⁻</c>). Every <c>p</c>-target gains
    /// the range proxy and mints its own <c>r</c>-predecessor from it.
    /// </summary>
    [TestMethod]
    public void InverseExistentialInRangeIsDecidedOnTheClassificationPath()
    {
        ElClassification result = Classify(
            new OwlObjectPropertyRangeAxiom(
                Property("p"),
                new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Iri("r"))), Named("C"))) { Origin = Origin });

        Assert.IsEmpty(result.UnsupportedConstructs, "The range's naming step reaches the generator reduction on the direct classification path, so nothing is recorded unsupported.");
    }

    /// <summary>
    /// A superclass-position <c>ObjectHasValue</c> over an INVERSE role is decided on the direct
    /// <c>Classify(document)</c> path, the survey-less caller: the superclass rewrite carries the property
    /// expression unchanged, so <c>A ⊑ ObjectHasValue(r⁻, a)</c> becomes <c>A ⊑ ∃r⁻.{a}</c> and rides the
    /// complex-filler naming and the eager generator reduction. Every A-member is then the target of its
    /// witness's r-edge, so <c>range(r) = E</c> types it and <c>E ⊑ ⊥</c> empties <c>A</c>. Without the
    /// rewrite the superclass side records an unsupported marker and <c>A</c> stays satisfiable.
    /// </summary>
    [TestMethod]
    public void SuperclassInverseHasValueNominalIsDecidedOnTheClassificationPath()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A"), new OwlObjectHasValue(new OwlInverseObjectProperty(new NamedNode(Iri("r"))), new NamedNode(Iri("a")))),
            new OwlObjectPropertyRangeAxiom(Property("r"), Named("E")) { Origin = Origin },
            SubClassOf(Named("E"), new OwlClassReference(new NamedNode(Nothing))));

        Assert.IsFalse(result.IsSatisfiable(Iri("A")), "Every A-member is the target of its minted r-predecessor's edge, so range(r) = E types it and E ⊑ ⊥ empties A.");
        Assert.IsEmpty(result.UnsupportedConstructs, "The inverse HasValue rewrite reaches the generator reduction on the direct classification path, so nothing is recorded unsupported.");
    }

    /// <summary>A self-restriction round-trips demand to elimination: A ⊑ ∃r.Self and ∃r.Self ⊑ B give A ⊑ B.</summary>
    [TestMethod]
    public void SelfRestrictionComposes()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A"), HasSelf("r")),
            SubClassOf(HasSelf("r"), Named("B")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Iri("B")));
    }

    /// <summary>A reflexive role's range types every node through its self-edge, so every named class is subsumed by the range.</summary>
    [TestMethod]
    public void ReflexiveRoleRangeTypesEveryNode()
    {
        ElClassification result = Classify(
            Reflexive("r"),
            new OwlObjectPropertyRangeAxiom(Property("r"), Named("C")) { Origin = Origin },
            SubClassOf(Named("N"), Named("D")));

        Assert.IsTrue(result.IsSubsumedBy(Iri("N"), Iri("C")), "Every named node has a self r-edge, so r's range types it.");
    }

    /// <summary>Self elimination needs a GENUINE self-edge: an ordinary r-successor (∃r.F, not ∃r.Self) must not license the self-restriction's conclusion.</summary>
    [TestMethod]
    public void SelfEliminationNeedsGenuineSelfEdge()
    {
        ElClassification result = Classify(
            SubClassOf(HasSelf("r"), Named("B")),
            SubClassOf(Named("A"), Some("r", Named("F"))));

        Assert.IsFalse(result.IsSubsumedBy(Iri("A"), Iri("B")), "A has an ordinary r-successor but no self r-edge, so the self elimination must not fire.");
    }

    /// <summary>
    /// R10 on the EL document path: a functional data property forces the two
    /// data existentials a class demands into one value, and the two integer
    /// ranges are disjoint, so the carrier is incoherent. This is the §1.3
    /// functional-pooling check reaching the EL lane through the module data
    /// property box — before it, the classifier read each range's emptiness in
    /// isolation, so two individually-satisfiable ranges left the class silently
    /// coherent with no undecided marker.
    /// </summary>
    [TestMethod]
    public void FunctionalDataPropertyWithDisjointRangesMakesTheCarrierIncoherent()
    {
        ElClassification result = Classify(
            FunctionalDataProperty("d"),
            SubClassOf(Named("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Named("A"), DataSome("d", IntegerBelow(3))));

        Assert.IsFalse(result.IsSatisfiable(Iri("A")), "A functional d forces one value into two disjoint integer ranges, so A is incoherent.");
        Assert.IsFalse(result.IsCoherent);
    }

    /// <summary>
    /// The control for the functional-incoherence row: without the functionality
    /// the two data existentials take different values under the open-world
    /// assumption, so the carrier stays coherent — the pooling fires on
    /// functionality alone, not on the mere presence of two data demands.
    /// </summary>
    [TestMethod]
    public void TwoDataExistentialsWithoutFunctionalityStayCoherent()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A"), DataSome("d", IntegerAbove(5))),
            SubClassOf(Named("A"), DataSome("d", IntegerBelow(3))));

        Assert.IsTrue(result.IsSatisfiable(Iri("A")), "Without functionality the two data demands take different values, so A is coherent.");
        Assert.IsTrue(result.IsCoherent);
    }

    /// <summary>
    /// A named <c>DataPropertyDomain</c> fires on the EL document path: a class
    /// carrying a data demand on the domain property — here through a
    /// sub-property, via the box closure — is told it is the domain class.
    /// </summary>
    [TestMethod]
    public void DataPropertyDomainTypesTheDemandCarrier()
    {
        ElClassification result = Classify(
            new OwlDataPropertyDomainAxiom(new NamedNode(Iri("e")), Named("C")) { Origin = Origin },
            new OwlSubDataPropertyOfAxiom(new NamedNode(Iri("d")), new NamedNode(Iri("e"))) { Origin = Origin },
            SubClassOf(Named("A"), DataSome("d", IntegerAbove(5))));

        Assert.IsTrue(result.IsSubsumedBy(Iri("A"), Iri("C")), "A demands a d-value and d ⊑ e with Domain(e, C), so A ⊑ C.");
    }

    /// <summary>
    /// Shared-witness soundness on the DOCUMENT classification path, which is not the lighter stress the
    /// absence of an ABox suggests: a class-space nominal interns an individual atom at normalization and
    /// the saturation seeds every individual atom inhabited, with no lane test, so a nominal-bearing
    /// document drives the liveness-gated merge exactly as a module does — and a nominal typed into a
    /// class (<c>{a} ⊑ A</c>) makes the individual itself a mint owner sharing its witness with the class
    /// owners. Each row states the entailment its expectations read: a class listed unsatisfiable is empty
    /// in every model of the document, and one listed satisfiable has a model, so a leak across the shared
    /// witness shows up as either a lost emptiness or a fabricated one.
    /// </summary>
    /// <returns>Every case as (name, axioms, entailed-empty classes, satisfiable classes).</returns>
    private static (string Name, OwlAxiom[] Axioms, string[] Unsatisfiable, string[] Satisfiable)[] SharedWitnessDocumentLaneCases() =>
        [
            //SH1 — a live owner and a dead co-owner of one shared witness. Δ = {a}, A1 = K = B = {a},
            //r = s = {(a, a)}, A2 = M = N = ∅. A2 is entailed empty: an A2-element would have an r-successor
            //in B, which B ⊑ {a} makes a, so a would have an s-successor in M, hence be N as well as K,
            //which N ⊓ K ⊑ ⊥ forbids. A1 and B keep models, which is the leak guard — A2's own emptiness
            //travels to the shared witness over a mirror edge running witness-to-owner, where it is vacuous
            //and must not empty the branch the individual inhabits.
            ("SH1_LiveAndDeadCoOwnersOfOneWitness",
                [
                    Inverse("r", "s"),
                    SubClassOf(OneOf("a"), Named("A1")),
                    SubClassOf(OneOf("a"), Named("K")),
                    SubClassOf(Named("A1"), Some("r", Named("B"))),
                    SubClassOf(Named("A2"), Some("r", Named("B"))),
                    SubClassOf(Named("B"), OneOf("a")),
                    SubClassOf(Named("A2"), Named("M")),
                    SubClassOf(Some("s", Named("M")), Named("N")),
                    SubClassOf(new OwlObjectIntersectionOf([Named("N"), Named("K")]), NothingReference),
                ],
                ["A2"],
                ["A1", "B"]),

            //SH5 — a range-told nominal closing over one shared witness. Δ = {c}, A = ∅, A2 = N = Z = {c},
            //r = {(c, c)}. A is entailed empty: an A-element has an r-successor, and the symmetric role
            //makes it an r-TARGET too, so range(r) = N types it and N ⊑ {c} makes it c, which {c} ⊑ Z types
            //Z — and A ⊓ Z ⊑ ⊥ forbids an element in both. A2 shares the very same witness and stays
            //satisfiable, which is the containment claim: A's emptiness reaches the shared witness over a
            //witness-to-owner mirror edge and stops there.
            ("SH5_RangeToldNominalOverOneSharedWitness",
                [
                    Symmetric("r"),
                    SubClassOf(Named("A"), Some("r", Named("F"))),
                    SubClassOf(Named("A2"), Some("r", Named("F"))),
                    Range("r", Named("N")),
                    SubClassOf(Named("N"), OneOf("c")),
                    SubClassOf(OneOf("c"), Named("Z")),
                    SubClassOf(new OwlObjectIntersectionOf([Named("A"), Named("Z")]), NothingReference),
                ],
                ["A"],
                ["A2", "F"]),

            //SH6 — the individual hub with both branches live. The document has no model at all: {p} ⊑ A1
            //and {p} ⊑ C force the individual p into both owners, so a B-element and a D-element are both
            //forced, both are told to be a, and a would be K and M together, which M ⊓ K ⊑ ⊥ forbids. Every
            //class is therefore unsatisfiable, and the row reads that off the two owners and the two
            //witness cores. The liveness the entailment rests on comes from the class-space nominal alone —
            //there is no ABox on this path.
            ("SH6_IndividualHubBothBranchesLive",
                [
                    Inverse("r", "s"),
                    Inverse("u", "v"),
                    SubClassOf(OneOf("p"), Named("A1")),
                    SubClassOf(OneOf("p"), Named("C")),
                    SubClassOf(Named("A1"), Some("r", Named("B"))),
                    SubClassOf(Named("C"), Some("u", Named("D"))),
                    SubClassOf(Named("B"), OneOf("a")),
                    SubClassOf(Named("D"), OneOf("a")),
                    SubClassOf(Named("B"), Named("K")),
                    SubClassOf(Named("D"), Named("M")),
                    SubClassOf(new OwlObjectIntersectionOf([Named("M"), Named("K")]), NothingReference),
                ],
                ["A1", "C", "B", "D"],
                []),
        ];

    /// <summary>The shared-witness document-lane battery: every <see cref="SharedWitnessDocumentLaneCases"/> row's entailed emptiness and satisfiability hold on the direct <c>Classify(document)</c> path, with nothing recorded unsupported; the report names every offender.</summary>
    [TestMethod]
    public void SharedWitnessDocumentLaneBattery()
    {
        (string Name, OwlAxiom[] Axioms, string[] Unsatisfiable, string[] Satisfiable)[] cases = SharedWitnessDocumentLaneCases();

        List<string> mismatches = [];
        foreach((string name, OwlAxiom[] axioms, string[] unsatisfiable, string[] satisfiable) in cases)
        {
            ElClassification result = Classify(axioms);
            if(result.UnsupportedConstructs.Count > 0)
            {
                mismatches.Add(name + ": the document lane recorded an unsupported construct.");
            }

            foreach(string local in unsatisfiable)
            {
                if(result.IsSatisfiable(Iri(local)))
                {
                    mismatches.Add(name + ": " + local + " is entailed empty but was reported satisfiable.");
                }
            }

            foreach(string local in satisfiable)
            {
                if(!result.IsSatisfiable(Iri(local)))
                {
                    mismatches.Add(name + ": " + local + " has a model but was reported unsatisfiable.");
                }
            }
        }

        Assert.IsEmpty(mismatches, string.Join("\n", mismatches));
    }

    //Construction helpers.

    private static Quad Origin { get; } = new(
        new NamedNode(Utf8Strings.From(Example + "s")),
        new NamedNode(Utf8Strings.From(Example + "p")),
        new NamedNode(Utf8Strings.From(Example + "o")),
        Graph: null);

    private static Utf8String Iri(string local)
    {
        return Utf8Strings.From(Example + local);
    }

    private static OwlClassReference Named(string local)
    {
        return new OwlClassReference(new NamedNode(Iri(local)));
    }

    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Iri(local)));
    }

    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin };
    }

    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = Origin };
    }

    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = Origin };
    }

    private static OwlPropertyChainAxiom Chain(string super, params string[] links)
    {
        OwlObjectPropertyExpression[] chain = new OwlObjectPropertyExpression[links.Length];
        for(int index = 0; index < links.Length; index++)
        {
            chain[index] = Property(links[index]);
        }

        return new OwlPropertyChainAxiom(chain, Property(super)) { Origin = Origin };
    }

    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>A <c>FunctionalDataProperty</c> axiom.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlFunctionalDataPropertyAxiom FunctionalDataProperty(string property)
    {
        return new OwlFunctionalDataPropertyAxiom(new NamedNode(Iri(property))) { Origin = Origin };
    }

    /// <summary>A single-property data existential (<c>DataSomeValuesFrom</c>).</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Iri(property))], range);
    }

    /// <summary>An integer range bounded below exclusively.</summary>
    /// <param name="bound">The exclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAbove(int bound)
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.Integer),
            [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinExclusive), new Literal(Utf8Strings.From(bound.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer)))]);
    }

    /// <summary>An integer range bounded above exclusively.</summary>
    /// <param name="bound">The exclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBelow(int bound)
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.Integer),
            [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxExclusive), new Literal(Utf8Strings.From(bound.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer)))]);
    }

    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, Property(property)) { Origin = Origin };
    }

    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, Property(property)) { Origin = Origin };
    }

    /// <summary>A symmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(property)) { Origin = Origin };
    }

    /// <summary>An asymmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, Property(property)) { Origin = Origin };
    }

    /// <summary>The fixed-⊥ class reference, <c>owl:Nothing</c>.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Nothing));

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin };
    }

    /// <summary>A range axiom typing every target of the role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="range">The range class.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(property), range) { Origin = Origin };
    }

    /// <summary>An enumeration of individuals in the example namespace (<c>ObjectOneOf</c>); a single individual is the nominal <c>{a}</c>.</summary>
    /// <param name="individuals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = new NamedNode(Iri(individuals[index]));
        }

        return new OwlObjectOneOf(terms);
    }

    private ElClassification Classify(params OwlAxiom[] axioms)
    {
        OwlOntologyDocument document = new(
            [.. axioms],
            ontologyIri: null,
            new DiagnosticBag(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>());

        return ElClassifier.Classify(document, TestContext.CancellationToken);
    }
}
