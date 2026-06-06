using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Tests.Owl;

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

    private static Utf8String Thing { get; } = Utf8String.FromString("http://www.w3.org/2002/07/owl#Thing");

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

    /// <summary>Equivalence yields mutual subsumption.</summary>
    [TestMethod]
    public void EquivalenceIsMutual()
    {
        ElClassification result = Classify(
            new OwlEquivalentClassesAxiom(Named("Human"), Named("Person")) { Origin = Origin });

        Assert.IsTrue(result.IsSubsumedBy(Iri("Human"), Iri("Person")));
        Assert.IsTrue(result.IsSubsumedBy(Iri("Person"), Iri("Human")));
    }

    /// <summary>A domain axiom types every edge source; a range axiom types every edge target.</summary>
    [TestMethod]
    public void DomainAndRangeType()
    {
        ElClassification result = Classify(
            new OwlObjectPropertyDomainAxiom(Property("hasChild"), Named("Parent")) { Origin = Origin },
            new OwlObjectPropertyRangeAxiom(Property("hasChild"), Named("Child")) { Origin = Origin },
            SubClassOf(Named("Mother"), Some("hasChild", Named("Person"))));

        Assert.IsTrue(result.IsSubsumedBy(Iri("Mother"), Iri("Parent")), "The edge source gains the domain.");
        Assert.IsTrue(result.IsSubsumedBy(Iri("Person"), Iri("Child")), "The edge target gains the range.");
    }

    /// <summary>An uninterpreted construct is recorded, never silently dropped.</summary>
    [TestMethod]
    public void UnsupportedConstructsAreReported()
    {
        ElClassification result = Classify(
            SubClassOf(Named("A"), new OwlObjectComplementOf(Named("B"))));

        Assert.IsNotEmpty(result.UnsupportedConstructs);
    }

    //----- Construction helpers -----

    private static Quad Origin { get; } = new(
        new NamedNode(Utf8String.FromString(Example + "s")),
        new NamedNode(Utf8String.FromString(Example + "p")),
        new NamedNode(Utf8String.FromString(Example + "o")),
        Graph: null);

    private static Utf8String Iri(string local)
    {
        return Utf8String.FromString(Example + local);
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
