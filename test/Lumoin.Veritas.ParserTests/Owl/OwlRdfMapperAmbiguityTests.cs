using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The duplicate-functional-field countergraphs of the structural mapper:
/// a semantically single-valued position carrying several distinct values
/// is not a value the reverse mapping determines, so the mapper must read
/// the same graph identically in every quad order and refuse the ambiguous
/// construct with a diagnostic instead of silently picking a value. The
/// RL closure's readings of the same shapes ride along: scalar duplicates
/// fire per asserted value, and duplicate list-cell values collapse to
/// equality under the axiomatic list functionality.
/// </summary>
[TestClass]
internal sealed class OwlRdfMapperAmbiguityTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The <c>rdf:type</c> predicate IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdf:first</c> predicate IRI.</summary>
    private const string RdfFirst = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";

    /// <summary>The <c>rdf:rest</c> predicate IRI.</summary>
    private const string RdfRest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";

    /// <summary>The <c>rdf:nil</c> node IRI.</summary>
    private const string RdfNil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>The <c>rdfs:subClassOf</c> predicate IRI.</summary>
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The <c>owl:Restriction</c> class IRI.</summary>
    private const string OwlRestriction = "http://www.w3.org/2002/07/owl#Restriction";

    /// <summary>The <c>owl:onProperty</c> predicate IRI.</summary>
    private const string OwlOnProperty = "http://www.w3.org/2002/07/owl#onProperty";

    /// <summary>The <c>owl:someValuesFrom</c> predicate IRI.</summary>
    private const string OwlSomeValuesFrom = "http://www.w3.org/2002/07/owl#someValuesFrom";

    /// <summary>The <c>owl:intersectionOf</c> predicate IRI.</summary>
    private const string OwlIntersectionOf = "http://www.w3.org/2002/07/owl#intersectionOf";

    /// <summary>The <c>owl:ObjectProperty</c> class IRI.</summary>
    private const string OwlObjectProperty = "http://www.w3.org/2002/07/owl#ObjectProperty";

    /// <summary>The <c>owl:Class</c> class IRI.</summary>
    private const string OwlClassIri = "http://www.w3.org/2002/07/owl#Class";

    /// <summary>The subclass the restriction and intersection documents subsume.</summary>
    private static NamedNode SubjectClass { get; } = new(Utf8Strings.From("http://example.org/A"));

    /// <summary>The filler class of the restriction documents.</summary>
    private static NamedNode FillerClass { get; } = new(Utf8Strings.From("http://example.org/B"));

    /// <summary>The second filler class of the duplicate-filler document.</summary>
    private static NamedNode SecondFillerClass { get; } = new(Utf8Strings.From("http://example.org/B2"));

    /// <summary>The first restricted property.</summary>
    private static NamedNode PropertyOne { get; } = new(Utf8Strings.From("http://example.org/p1"));

    /// <summary>The second restricted property of the duplicate-property document.</summary>
    private static NamedNode PropertyTwo { get; } = new(Utf8Strings.From("http://example.org/p2"));

    /// <summary>A restriction node carrying two distinct owl:onProperty values reads identically in every quad order and refuses with a diagnostic: the mapper is a function of the graph, and the reverse mapping determines no property for the construct.</summary>
    [TestMethod]
    public void DuplicateOnPropertyReadsIdenticallyAcrossQuadOrders()
    {
        OwlOntologyDocument first = OwlRdfMapper.Map(RestrictionQuads(PropertyOne, PropertyTwo));
        OwlOntologyDocument second = OwlRdfMapper.Map(RestrictionQuads(PropertyTwo, PropertyOne));

        Assert.AreEqual(
            FindSubClassOf(first)?.SuperClass,
            FindSubClassOf(second)?.SuperClass,
            "The same triple set read in two quad orders is one graph; the mapper's reading must not depend on the order.");
        Assert.IsTrue(first.Diagnostics.HasErrors, "Two distinct owl:onProperty values on one restriction are an ambiguity the mapper reports.");
        Assert.IsTrue(second.Diagnostics.HasErrors, "The report is order-independent.");
    }

    /// <summary>The cross-model row: on a restriction with two someValuesFrom fillers the structural mapper refuses with a diagnostic while the RL closure fires the existential per asserted filler — the two models stop silently disagreeing about one document.</summary>
    [TestMethod]
    public void DuplicateFillerDivergesBetweenTheRlAndStructuralReadings()
    {
        List<Quad> quads =
        [
            Row(PropertyOne, RdfType, Node(OwlObjectProperty)),
            Row(SubjectClass, RdfType, Node(OwlClassIri)),
            Row(FillerClass, RdfType, Node(OwlClassIri)),
            Row(SecondFillerClass, RdfType, Node(OwlClassIri)),
            Row(Restriction(), RdfType, Node(OwlRestriction)),
            Row(Restriction(), OwlOnProperty, PropertyOne),
            Row(Restriction(), OwlSomeValuesFrom, FillerClass),
            Row(Restriction(), OwlSomeValuesFrom, SecondFillerClass),
            Row(SubjectClass, RdfsSubClassOf, Restriction()),
        ];

        OwlOntologyDocument document = OwlRdfMapper.Map(quads);

        Assert.IsTrue(document.Diagnostics.HasErrors, "Two distinct someValuesFrom fillers on one restriction are an ambiguity the mapper reports.");

        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Blank(dictionary, "r");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p1");
        TermId earlyFiller = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId lateFiller = OwlRlBatteryHelpers.Mint(dictionary, "B2");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(x, terms.OnProperty, p),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, earlyFiller),
                OwlRlBatteryHelpers.Triple(x, terms.SomeValuesFrom, lateFiller),
                OwlRlBatteryHelpers.Triple(u, p, v),
                OwlRlBatteryHelpers.Triple(v, terms.Type, lateFiller),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        Assert.Contains(OwlRlBatteryHelpers.Triple(u, terms.Type, x), [.. result.Derived]);
    }

    /// <summary>A list cell carrying two distinct rdf:first values is an ambiguity the mapper reports, refusing the list; the well-formed sibling maps clean — the pair pins that the report is about the duplicate, not the construct.</summary>
    [TestMethod]
    public void DuplicateListCellRefusesTheStructuralList()
    {
        OwlOntologyDocument clean = OwlRdfMapper.Map(IntersectionQuads(duplicateFirstCell: false));
        Assert.IsFalse(clean.Diagnostics.HasErrors, "The well-formed intersection list maps without diagnostics.");

        OwlOntologyDocument ambiguous = OwlRdfMapper.Map(IntersectionQuads(duplicateFirstCell: true));
        Assert.IsTrue(ambiguous.Diagnostics.HasErrors, "Two distinct rdf:first values on one cell are an ambiguity the mapper reports.");
    }

    /// <summary>The RL control of the list-cell row: duplicate rdf:first values on one cell collapse to equality under the axiomatic list functionality, so the closure's reading of an ambiguous cell is equality-sound rather than a silent pick.</summary>
    [TestMethod]
    public void DuplicateListCellValuesCollapseToEqualityInTheClosure()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId cell = OwlRlBatteryHelpers.Blank(dictionary, "cell");
        TermId firstMember = OwlRlBatteryHelpers.Mint(dictionary, "m1");
        TermId secondMember = OwlRlBatteryHelpers.Mint(dictionary, "m2");

        OwlRlResult result = OwlRlClosure.Compute(
            [
                OwlRlBatteryHelpers.Triple(cell, terms.First, firstMember),
                OwlRlBatteryHelpers.Triple(cell, terms.First, secondMember),
                OwlRlBatteryHelpers.Triple(cell, terms.Rest, terms.Nil),
            ],
            terms,
            OwlRlDatatypeOracles.FromDictionary(dictionary),
            cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        HashSet<EncodedTriple> derived = [.. result.Derived];
        Assert.IsTrue(
            derived.Contains(OwlRlBatteryHelpers.Triple(firstMember, terms.SameAs, secondMember))
            || derived.Contains(OwlRlBatteryHelpers.Triple(secondMember, terms.SameAs, firstMember)),
            "The axiomatic rdf:first functionality entails the cell's values equal.");
    }

    /// <summary>The identical-repeat control: the same quad row appearing twice is one fact of the graph — the document maps clean, with the restriction read and no diagnostic.</summary>
    [TestMethod]
    public void RepeatedIdenticalQuadRowsMapWithoutDiagnostics()
    {
        List<Quad> quads =
        [
            Row(PropertyOne, RdfType, Node(OwlObjectProperty)),
            Row(SubjectClass, RdfType, Node(OwlClassIri)),
            Row(FillerClass, RdfType, Node(OwlClassIri)),
            Row(Restriction(), RdfType, Node(OwlRestriction)),
            Row(Restriction(), OwlOnProperty, PropertyOne),
            Row(Restriction(), OwlOnProperty, PropertyOne),
            Row(Restriction(), OwlSomeValuesFrom, FillerClass),
            Row(SubjectClass, RdfsSubClassOf, Restriction()),
        ];

        OwlOntologyDocument document = OwlRdfMapper.Map(quads);

        Assert.IsFalse(document.Diagnostics.HasErrors, "An identical repeated row carries no ambiguity.");
        OwlSubClassOfAxiom? subClass = FindSubClassOf(document);
        Assert.IsNotNull(subClass);
        Assert.IsInstanceOfType<OwlObjectSomeValuesFrom>(subClass.SuperClass);
    }

    /// <summary>Builds the duplicate-onProperty restriction document with the two property rows in the given order; the triple SET is the same for both orders.</summary>
    /// <param name="firstListed">The property row listed first.</param>
    /// <param name="secondListed">The property row listed second.</param>
    /// <returns>The quads.</returns>
    private static List<Quad> RestrictionQuads(NamedNode firstListed, NamedNode secondListed)
    {
        return
        [
            Row(PropertyOne, RdfType, Node(OwlObjectProperty)),
            Row(PropertyTwo, RdfType, Node(OwlObjectProperty)),
            Row(SubjectClass, RdfType, Node(OwlClassIri)),
            Row(FillerClass, RdfType, Node(OwlClassIri)),
            Row(Restriction(), RdfType, Node(OwlRestriction)),
            Row(Restriction(), OwlOnProperty, firstListed),
            Row(Restriction(), OwlOnProperty, secondListed),
            Row(Restriction(), OwlSomeValuesFrom, FillerClass),
            Row(SubjectClass, RdfsSubClassOf, Restriction()),
        ];
    }

    /// <summary>Builds an intersection-superclass document whose member list is well formed or carries a duplicate rdf:first on its head cell.</summary>
    /// <param name="duplicateFirstCell">Whether the head cell carries a second, distinct rdf:first value.</param>
    /// <returns>The quads.</returns>
    private static List<Quad> IntersectionQuads(bool duplicateFirstCell)
    {
        BlankNode intersection = new(Utf8Strings.From("i"));
        BlankNode headCell = new(Utf8Strings.From("l1"));
        BlankNode tailCell = new(Utf8Strings.From("l2"));

        List<Quad> quads =
        [
            Row(SubjectClass, RdfType, Node(OwlClassIri)),
            Row(FillerClass, RdfType, Node(OwlClassIri)),
            Row(SecondFillerClass, RdfType, Node(OwlClassIri)),
            Row(intersection, RdfType, Node(OwlClassIri)),
            Row(intersection, OwlIntersectionOf, headCell),
            Row(headCell, RdfFirst, FillerClass),
            Row(headCell, RdfRest, tailCell),
            Row(tailCell, RdfFirst, SecondFillerClass),
            Row(tailCell, RdfRest, Node(RdfNil)),
            Row(SubjectClass, RdfsSubClassOf, intersection),
        ];

        if(duplicateFirstCell)
        {
            quads.Insert(6, Row(headCell, RdfFirst, SecondFillerClass));
        }

        return quads;
    }

    /// <summary>The restriction blank node of the restriction documents.</summary>
    /// <returns>The blank node.</returns>
    private static BlankNode Restriction()
    {
        return new BlankNode(Utf8Strings.From("r"));
    }

    /// <summary>Builds a named node for the given IRI.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Node(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>Builds one default-graph quad.</summary>
    /// <param name="subject">The subject term.</param>
    /// <param name="predicate">The predicate IRI.</param>
    /// <param name="object">The object term.</param>
    /// <returns>The quad.</returns>
    private static Quad Row(RdfTerm subject, string predicate, RdfTerm @object)
    {
        return new Quad(subject, new NamedNode(Utf8Strings.From(predicate)), @object, Graph: null);
    }

    /// <summary>The document's first SubClassOf axiom, if any.</summary>
    /// <param name="document">The mapped document.</param>
    /// <returns>The axiom, or <see langword="null"/> when none mapped.</returns>
    private static OwlSubClassOfAxiom? FindSubClassOf(OwlOntologyDocument document)
    {
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is OwlSubClassOfAxiom subClass)
            {
                return subClass;
            }
        }

        return null;
    }
}
