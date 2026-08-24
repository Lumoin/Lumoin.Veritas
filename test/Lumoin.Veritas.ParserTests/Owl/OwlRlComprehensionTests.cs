using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Pins the conclusion-guided comprehension substrate and its completion
/// family: the contentful-scaffold minting inside the entailment path, the
/// completion conclusions over the minted structure, the normative-reading
/// and leak gates, the direction and scope pins, the default and
/// maintained closures staying dark, the semi-naive/naive agreement, the
/// fresh-label discipline, the late-round premise arrival the conservative
/// trigger must cover, the bounded existential witness — its corpus
/// shape, its freshness and independence leak gates, its cycle bound, and
/// its sound functional merge — the schema completions: the
/// type-domain universal subsumption, the shared has-value property
/// collapse, and the disjoint-range emptiness rules with their
/// value-space-family oracle member — and the value-identity pair: the
/// datatype-alias retype with its admission and validity fences, the
/// entailment surface's dt-eq bridge with its same-value-space guard and
/// its pre-bridge degrade — and the fibre-cardinality certificate family:
/// the corpus gadget with its exact traced premises, the refusal fences of
/// the count reads and the read-back mint, both orientation dimensions,
/// both late-arrival dimensions, and the one-directional, first-certificate,
/// and clash-composition pins.
/// </summary>
[TestClass]
internal sealed class OwlRlComprehensionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";
    private const string OwlNs = "http://www.w3.org/2002/07/owl#";
    private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string RdfsNs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string XsdNs = "http://www.w3.org/2001/XMLSchema#";

    /// <summary>The rendered Skolem IRI of the first comprehension-scaffold copy — the spelling an adversarial premise blank takes to try to be captured by a minted copy.</summary>
    private static string ScaffoldCopySpelling { get; } = $"urn:veritas:genid:{EngineNodeFamily.ComprehensionScaffold.Code}:0:0:0:0";

    /// <summary>A union holding a class and its complement covers everything, so a Thing-typed individual belongs to it.</summary>
    [TestMethod]
    public void UnionWithComplementEntailsTheExcludedMiddleMembership()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(OwlNs + "Thing")),
            Q(Named("c"), RdfNs + "type", Iri(OwlNs + "Class")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("x"), RdfNs + "type", Blank("u")),
            Q(Blank("u"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Blank("comp"), RdfNs + "type", Iri(RdfsNs + "Class")),
            Q(Blank("comp"), OwlNs + "complementOf", Named("c")),
        ];
        RdfTerm head = AddList(conclusion, "u-members", [Named("c"), Blank("comp")]);
        conclusion.Add(Q(Blank("u"), OwlNs + "unionOf", head));

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A union of a some-values-from-Thing restriction and a max-0 restriction on one property covers everything; the max-0 disjunct carries only an owl:Class typing.</summary>
    [TestMethod]
    public void UnionOfSomeValuesAndMaxZeroEntailsTheDichotomyMembership()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(OwlNs + "Thing")),
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "ObjectProperty")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("x"), RdfNs + "type", Blank("u")),
            Q(Blank("u"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Blank("r1"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r1"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r1"), OwlNs + "someValuesFrom", Iri(OwlNs + "Thing")),
            Q(Blank("r2"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Blank("r2"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r2"), OwlNs + "maxCardinality", Bound("0", XsdNs + "int")),
        ];
        RdfTerm head = AddList(conclusion, "u-members", [Blank("r1"), Blank("r2")]);
        conclusion.Add(Q(Blank("u"), OwlNs + "unionOf", head));

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A functional property confines every individual to at most one value, so a Thing-typed individual belongs to the max-1 restriction on it.</summary>
    [TestMethod]
    public void FunctionalPropertyEntailsTheUniversalMaxOneRestriction()
    {
        List<Quad> premise =
        [
            Q(Named("prop"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("object"), RdfNs + "type", Iri(OwlNs + "Thing")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("object"), RdfNs + "type", Iri(OwlNs + "Thing")),
            Q(Named("object"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r"), OwlNs + "onProperty", Named("prop")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
            Q(Named("prop"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>The empty enumeration denotes the empty class, so its equivalence with owl:Nothing follows from the empty premise.</summary>
    [TestMethod]
    public void EmptyEnumerationEntailsTheNothingEquivalence()
    {
        List<Quad> conclusion =
        [
            Q(Blank("c"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Blank("c"), OwlNs + "oneOf", Iri(RdfNs + "nil")),
            Q(Blank("c"), OwlNs + "equivalentClass", Iri(OwlNs + "Nothing")),
        ];

        AssertEntailed([], conclusion);
    }

    /// <summary>Two ranges of one property conclude the range over the conclusion-named intersection of the two classes.</summary>
    [TestMethod]
    public void TwoRangesEntailTheConclusionNamedIntersectionRange()
    {
        List<Quad> premise =
        [
            Q(Named("prop"), RdfNs + "type", Iri(RdfNs + "Property")),
            Q(Named("prop"), RdfsNs + "range", Named("A")),
            Q(Named("prop"), RdfsNs + "range", Named("B")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("prop"), RdfsNs + "range", Blank("i")),
            Q(Blank("i"), RdfNs + "type", Iri(OwlNs + "Class")),
        ];
        RdfTerm head = AddList(conclusion, "i-members", [Named("A"), Named("B")]);
        conclusion.Add(Q(Blank("i"), OwlNs + "intersectionOf", head));

        AssertEntailed(premise, conclusion);
    }

    /// <summary>The intersection of two complements is equivalent to the complement of the union — De Morgan duality over the minted expressions.</summary>
    [TestMethod]
    public void ComplementIntersectionEntailsTheComplementOfTheUnion()
    {
        List<Quad> premise =
        [
            Q(Named("A"), RdfNs + "type", Iri(RdfsNs + "Class")),
            Q(Named("B"), RdfNs + "type", Iri(RdfsNs + "Class")),
        ];
        List<Quad> conclusion =
        [
            Q(Blank("ca"), OwlNs + "complementOf", Named("A")),
            Q(Blank("cb"), OwlNs + "complementOf", Named("B")),
            Q(Blank("i"), OwlNs + "equivalentClass", Blank("cu")),
            Q(Blank("cu"), OwlNs + "complementOf", Blank("u2")),
        ];
        RdfTerm intersectionHead = AddList(conclusion, "i-members", [Blank("ca"), Blank("cb")]);
        conclusion.Add(Q(Blank("i"), OwlNs + "intersectionOf", intersectionHead));
        RdfTerm unionHead = AddList(conclusion, "u2-members", [Named("A"), Named("B")]);
        conclusion.Add(Q(Blank("u2"), OwlNs + "unionOf", unionHead));

        AssertEntailed(premise, conclusion);
    }

    /// <summary>An intersection of a same-bound min- and max-cardinality pair also carries the intersection over the singleton exact-cardinality list.</summary>
    [TestMethod]
    public void CardinalityShorthandEntailsTheSingletonIntersection()
    {
        List<Quad> premise = CardinalityShorthandPremise(minFirst: false, withExtraMember: false);
        List<Quad> conclusion = CardinalityShorthandConclusion();

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A declared domain of rdf:type subsumes every class — ICEXT is the rdf:type slice, so every class member lands in the domain.</summary>
    [TestMethod]
    public void TypeDomainDeclarationEntailsTheUniversalClassSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(RdfsNs + "Class")),
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("x"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A general property's domain never concludes a subsumption — the rule's subject is the fixed rdf:type term.</summary>
    [TestMethod]
    public void AGeneralPropertyDomainRefusesTheUniversalSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(RdfsNs + "Class")),
            Q(Named("p"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("x"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A class typing that arrives derived on a later round still fires the type-domain subsumption — the conservative trigger covers the class roster's growth.</summary>
    [TestMethod]
    public void ALateArrivingClassTypingStillFiresTheTypeDomainSubsumption()
    {
        List<Quad> premise =
        [
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y")),
            Q(Named("x"), RdfNs + "type", Named("C")),
            Q(Named("C"), RdfsNs + "subClassOf", Iri(RdfsNs + "Class"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("x"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>An owl:Class typing alone reaches the type-domain subsumption — owl:Class and rdfs:Class share the class extension in every RDF-Based interpretation, independent of the metaclass seeding mode.</summary>
    [TestMethod]
    public void AnOwlClassTypedClassSubsumesUnderTheTypeDomain()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("x"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A term evidenced as a class only by appearing as an rdf:type object subsumes under the domain — rdf:type's axiomatic range types every type object a class.</summary>
    [TestMethod]
    public void ATypeObjectOnlyClassSubsumesUnderTheTypeDomain()
    {
        List<Quad> premise =
        [
            Q(Named("i"), RdfNs + "type", Named("c")),
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("c"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>Both endpoints of a subClassOf statement subsume under the domain — the subClassOf condition's only-if direction places both in the class part.</summary>
    [TestMethod]
    public void ASubClassOfPositionClassSubsumesUnderTheTypeDomain()
    {
        List<Quad> premise =
        [
            Q(Named("c"), RdfsNs + "subClassOf", Named("d")),
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("c"), RdfsNs + "subClassOf", Named("y")),
            Q(Named("d"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A domain or range statement's object subsumes under the domain — the domain and range conditions' only-if directions place the object in the class part.</summary>
    [TestMethod]
    public void ADomainOrRangeObjectClassSubsumesUnderTheTypeDomain()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfsNs + "domain", Named("c")),
            Q(Named("q"), RdfsNs + "range", Named("d")),
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("c"), RdfsNs + "subClassOf", Named("y")),
            Q(Named("d"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>The domain of rdf:type is the whole universe: the owl:Thing bracket holds in both directions from the domain statement alone and scm-eqc2 composes the equivalence.</summary>
    [TestMethod]
    public void TheTypeDomainForcesTheThingEquivalence()
    {
        List<Quad> premise =
        [
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        List<Quad> conclusion =
        [
            Q(Iri(OwlNs + "Thing"), RdfsNs + "subClassOf", Named("y")),
            Q(Named("y"), RdfsNs + "subClassOf", Iri(OwlNs + "Thing")),
            Q(Named("y"), OwlNs + "equivalentClass", Iri(OwlNs + "Thing"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A term whose only occurrences are subject and predicate positions gains no subsumption — statement-hood evidences neither term as a class, and the leak gate holds.</summary>
    [TestMethod]
    public void ASubjectPositionOnlyTermGainsNoTypeDomainSubsumption()
    {
        List<Quad> premise =
        [
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y")),
            Q(Named("s"), Example + "q", Named("o"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("s"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A class whose only evidence is a typing derived on a later round still subsumes — the subproperty propagation into rdf:type mints the type object mid-closure, and only the roster trigger re-fires the family for it.</summary>
    [TestMethod]
    public void ADerivedTypeObjectStillReachesTheTypeDomainRule()
    {
        List<Quad> premise =
        [
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y")),
            Q(Named("r"), RdfsNs + "subPropertyOf", Iri(RdfNs + "type")),
            Q(Named("s"), Example + "r", Named("k"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("k"), RdfsNs + "subClassOf", Named("y"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>Each type-domain emission cites the domain statement and its class evidence exactly — the witness discipline over the widened roster.</summary>
    [TestMethod]
    public void TheTypeDomainEmissionCitesItsWitness()
    {
        TermDictionary dictionary = new();
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))
        ];

        OwlRlResult result = ComputeTraced(premise, dictionary, out List<InferenceTraceEvent> events);
        Assert.IsTrue(result.IsConsistent);

        OwlRlTerms terms = new(dictionary);
        TermId x = dictionary.GetOrAdd((RdfTerm)Named("x"));
        TermId y = dictionary.GetOrAdd((RdfTerm)Named("y"));
        EncodedTriple expected = OwlRlBatteryHelpers.Triple(x, terms.SubClassOf, y);
        EncodedTriple domain = OwlRlBatteryHelpers.Triple(terms.Type, terms.Domain, y);
        EncodedTriple typing = OwlRlBatteryHelpers.Triple(x, terms.Type, terms.ClassTerm);
        bool found = false;
        foreach(InferenceTraceEvent evt in events)
        {
            if(evt.Rule == EntailmentRules.TypeDomainUniversalSubsumption && evt.Conclusion == expected)
            {
                found = true;
                Assert.HasCount(2, evt.Premises);
                Assert.Contains(domain, evt.Premises);
                Assert.Contains(typing, evt.Premises);
            }
        }

        Assert.IsTrue(found, "The widened rule emits the owl:Class-witnessed subsumption.");
    }

    /// <summary>Two functional properties sharing one has-value node as domain and onProperty target have the same extension, so their equivalence follows.</summary>
    [TestMethod]
    public void SharedHasValueDomainCollapseEntailsTheEquivalence()
    {
        List<Quad> premise = SharedHasValuePremise(OwlNs + "hasValue", Named("v"), withSecondDomain: true, withSecondFunctional: true);
        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A some-values-from detail in place of has-value refuses the collapse — the members' values need not coincide inside the filler.</summary>
    [TestMethod]
    public void SomeValuesFromInPlaceOfHasValueRefusesTheCollapse()
    {
        List<Quad> premise = SharedHasValuePremise(OwlNs + "someValuesFrom", Named("V"), withSecondDomain: true, withSecondFunctional: true);
        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>An all-values-from detail in place of has-value refuses the collapse — the universal reading grants no existence and no shared value.</summary>
    [TestMethod]
    public void AllValuesFromInPlaceOfHasValueRefusesTheCollapse()
    {
        List<Quad> premise = SharedHasValuePremise(OwlNs + "allValuesFrom", Named("V"), withSecondDomain: true, withSecondFunctional: true);
        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A cardinality detail in place of has-value refuses the collapse — equal counts do not make equal pairs.</summary>
    [TestMethod]
    public void CardinalityInPlaceOfHasValueRefusesTheCollapse()
    {
        List<Quad> premise = SharedHasValuePremise(OwlNs + "cardinality", Bound("1", XsdNs + "nonNegativeInteger"), withSecondDomain: true, withSecondFunctional: true);
        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A missing domain edge refuses the collapse — the property may relate members outside the node's extension.</summary>
    [TestMethod]
    public void AMissingDomainEdgeRefusesTheCollapse()
    {
        List<Quad> premise = SharedHasValuePremise(OwlNs + "hasValue", Named("v"), withSecondDomain: false, withSecondFunctional: true);
        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A non-functional property refuses the collapse — a member may carry a second value beside the asserted one.</summary>
    [TestMethod]
    public void ANonFunctionalPropertyRefusesTheCollapse()
    {
        List<Quad> premise = SharedHasValuePremise(OwlNs + "hasValue", Named("v"), withSecondDomain: true, withSecondFunctional: false);
        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>Two separate restriction nodes sharing a has-value refuse the collapse — the rule reads one node, and two nodes' extensions are unrelated.</summary>
    [TestMethod]
    public void TwoSeparateRestrictionNodesRefuseTheCollapse()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("q"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("p"), RdfsNs + "domain", Blank("d1")),
            Q(Named("q"), RdfsNs + "domain", Blank("d2")),
            Q(Blank("d1"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("d1"), OwlNs + "onProperty", Named("p")),
            Q(Blank("d1"), OwlNs + "hasValue", Named("v")),
            Q(Blank("d2"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("d2"), OwlNs + "onProperty", Named("q")),
            Q(Blank("d2"), OwlNs + "hasValue", Named("v"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A property ranged by two datatypes with disjoint value spaces has the empty extension, so it is a subproperty of every typed property.</summary>
    [TestMethod]
    public void DisjointRangesEntailTheVacuousSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(RdfNs + "Property")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "string")),
            Q(Named("q"), RdfNs + "type", Iri(RdfNs + "Property"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("p"), RdfsNs + "subPropertyOf", Named("q"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>Two ranges of one value-space family refuse the vacuous subsumption — within-family refinement belongs to the numeric interval map.</summary>
    [TestMethod]
    public void SameFamilyRangesRefuseTheVacuousSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(RdfNs + "Property")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "decimal")),
            Q(Named("q"), RdfNs + "type", Iri(RdfNs + "Property"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("p"), RdfsNs + "subPropertyOf", Named("q"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A candidate appearing only in predicate position receives the vacuous subsumption — a satisfied statement's predicate denotes a property, so predicate-position membership forces IP membership and the empty extension lies under it.</summary>
    [TestMethod]
    public void APredicatePositionOnlyCandidateReceivesTheVacuousSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(RdfNs + "Property")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "string")),
            Q(Named("s"), Example + "q", Named("o"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("p"), RdfsNs + "subPropertyOf", Named("q"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A term the closure never mentions in predicate position and never types a property stays outside the vacuous emission — the leak gate over the widened roster.</summary>
    [TestMethod]
    public void AnUnmentionedTermGainsNoVacuousSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(RdfNs + "Property")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "string")),
            Q(Named("s"), RdfNs + "type", Named("o"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("p"), RdfsNs + "subPropertyOf", Named("s"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A predicate whose first statement derives mid-closure through the inverse transfer still receives the vacuous subsumption — no other rule bridges a subsumption onto an inverse, so only the new-predicate roster trigger reaches it.</summary>
    [TestMethod]
    public void ALateArrivingPredicateReachesTheVacuousSubsumption()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(RdfNs + "Property")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "string")),
            Q(Named("s"), Example + "r", Named("o")),
            Q(Named("r"), OwlNs + "inverseOf", Named("r2"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("p"), RdfsNs + "subPropertyOf", Named("r2"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>Each predicate-position emission cites the disjoint range pair and one statement of the candidate — the witness discipline over the widened roster.</summary>
    [TestMethod]
    public void TheVacuousEmissionCitesItsWitnessStatement()
    {
        TermDictionary dictionary = new();
        List<Quad> premise =
        [
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
            Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "string")),
            Q(Named("s"), Example + "q", Named("o"))
        ];

        OwlRlResult result = ComputeTraced(premise, dictionary, out List<InferenceTraceEvent> events);
        Assert.IsTrue(result.IsConsistent);

        OwlRlTerms terms = new(dictionary);
        TermId p = dictionary.GetOrAdd((RdfTerm)Named("p"));
        TermId q = dictionary.GetOrAdd((RdfTerm)Named("q"));
        TermId s = dictionary.GetOrAdd((RdfTerm)Named("s"));
        TermId o = dictionary.GetOrAdd((RdfTerm)Named("o"));
        TermId integer = dictionary.GetOrAdd((RdfTerm)Iri(XsdNs + "integer"));
        TermId text = dictionary.GetOrAdd((RdfTerm)Iri(XsdNs + "string"));
        EncodedTriple expected = OwlRlBatteryHelpers.Triple(p, terms.SubPropertyOf, q);
        EncodedTriple firstRange = OwlRlBatteryHelpers.Triple(p, terms.Range, integer);
        EncodedTriple secondRange = OwlRlBatteryHelpers.Triple(p, terms.Range, text);
        EncodedTriple witness = OwlRlBatteryHelpers.Triple(s, q, o);
        bool found = false;
        foreach(InferenceTraceEvent evt in events)
        {
            if(evt.Rule == EntailmentRules.DisjointRangeVacuousSubproperty && evt.Conclusion == expected)
            {
                found = true;
                Assert.HasCount(3, evt.Premises);
                Assert.Contains(firstRange, evt.Premises);
                Assert.Contains(secondRange, evt.Premises);
                Assert.Contains(witness, evt.Premises);
            }
        }

        Assert.IsTrue(found, "The widened rule emits the statement-witnessed subsumption.");
    }

    /// <summary>A statement of a doubly-ranged property clashes on the entailment path and stays quiet on the normative closure — the object's denotation would lie in both disjoint value spaces.</summary>
    [TestMethod]
    public void DisjointRangesWithAnAssertedStatementClashOnTheEntailmentPath()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "integer");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "string");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(p, terms.Range, integer),
            OwlRlBatteryHelpers.Triple(p, terms.Range, text),
            OwlRlBatteryHelpers.Triple(s, p, o)
        ];

        OwlRlResult lit = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(lit.IsConsistent, "The disjoint-range clash refutes the statement under the informative conditions.");
        Assert.AreEqual(EntailmentRules.DisjointRangeClash, lit.InconsistencyRule);

        OwlRlResult dark = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(dark.IsConsistent, "The normative closure never fires the comprehension family's falsity.");
    }

    /// <summary>A statement that derives into the doubly-ranged property on a later round still clashes — the conservative trigger covers a ranged property's statement growth.</summary>
    [TestMethod]
    public void ALateDerivedStatementStillClashesTheDisjointRanges()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId r = OwlRlBatteryHelpers.Mint(dictionary, "r");
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "integer");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "string");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");
        TermId o = OwlRlBatteryHelpers.Mint(dictionary, "o");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(p, terms.Range, integer),
            OwlRlBatteryHelpers.Triple(p, terms.Range, text),
            OwlRlBatteryHelpers.Triple(r, terms.SubPropertyOf, p),
            OwlRlBatteryHelpers.Triple(s, r, o)
        ];

        OwlRlResult lit = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(lit.IsConsistent, "The statement propagated into the ranged property refutes the premise under the informative conditions.");
        Assert.AreEqual(EntailmentRules.DisjointRangeClash, lit.InconsistencyRule);
    }

    /// <summary>Under the normative reading the type-domain, has-value collapse, and disjoint-range conclusions stay unsettled — the completions ride only the informative conditions.</summary>
    [TestMethod]
    public void TheNormativeReadingLeavesTheSchemaCompletionsUnsettled()
    {
        TermDictionary first = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            [Q(Named("x"), RdfNs + "type", Iri(RdfsNs + "Class")), Q(Iri(RdfNs + "type"), RdfsNs + "domain", Named("y"))],
            [Q(Named("x"), RdfsNs + "subClassOf", Named("y"))],
            first, new OwlRlTerms(first),
            OwlRlDatatypeOracles.FromDictionary(first), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));

        TermDictionary second = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            SharedHasValuePremise(OwlNs + "hasValue", Named("v"), withSecondDomain: true, withSecondFunctional: true),
            [Q(Named("p"), OwlNs + "equivalentProperty", Named("q"))],
            second, new OwlRlTerms(second),
            OwlRlDatatypeOracles.FromDictionary(second), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));

        TermDictionary third = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            [
                Q(Named("p"), RdfNs + "type", Iri(RdfNs + "Property")),
                Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "integer")),
                Q(Named("p"), RdfsNs + "range", Iri(XsdNs + "string")),
                Q(Named("q"), RdfNs + "type", Iri(RdfNs + "Property"))
            ],
            [Q(Named("p"), RdfsNs + "subPropertyOf", Named("q"))],
            third, new OwlRlTerms(third),
            OwlRlDatatypeOracles.FromDictionary(third), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>The datatype oracle answers disjointness across value-space families only: unknown datatypes, same-family pairs, and non-datatype terms all answer unknown.</summary>
    [TestMethod]
    public void TheDatatypeOracleAnswersDisjointnessAcrossFamiliesOnly()
    {
        TermDictionary dictionary = new();
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "integer");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "string");
        TermId dec = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "decimal");
        TermId boolean = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "boolean");
        TermId dateTime = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "dateTime");
        TermId unrecognized = OwlRlBatteryHelpers.Mint(dictionary, "HomeGrownDatatype");
        TermId literal = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdNs + "integer");

        Assert.IsTrue(oracle.DatatypesKnownDisjoint(integer, text), "Numeric and text value spaces are disjoint.");
        Assert.IsTrue(oracle.DatatypesKnownDisjoint(boolean, dateTime), "Boolean and temporal value spaces are disjoint.");
        Assert.IsFalse(oracle.DatatypesKnownDisjoint(integer, dec), "Same-family pairs answer unknown.");
        Assert.IsFalse(oracle.DatatypesKnownDisjoint(integer, unrecognized), "An unrecognized datatype answers unknown.");
        Assert.IsFalse(oracle.DatatypesKnownDisjoint(text, text), "A datatype is never disjoint from itself.");
        Assert.IsFalse(oracle.DatatypesKnownDisjoint(literal, text), "A non-datatype term answers unknown.");
    }

    /// <summary>The semi-naive and naive closures agree on the type-domain, has-value collapse, and disjoint-range shapes — the conservative trigger loses no derivation.</summary>
    [TestMethod]
    public void TheSemiNaiveAndNaiveClosuresAgreeOnTheSchemaCompletionShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");
        TermId y = OwlRlBatteryHelpers.Mint(dictionary, "y");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId v = OwlRlBatteryHelpers.Mint(dictionary, "v");
        TermId d = OwlRlBatteryHelpers.Blank(dictionary, "d");
        TermId ranged = OwlRlBatteryHelpers.Mint(dictionary, "ranged");
        TermId candidate = OwlRlBatteryHelpers.Mint(dictionary, "candidate");
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "integer");
        TermId text = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "string");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(x, terms.Type, terms.RdfsClass),
            OwlRlBatteryHelpers.Triple(terms.Type, terms.Domain, y),
            OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(q, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(p, terms.Domain, d),
            OwlRlBatteryHelpers.Triple(q, terms.Domain, d),
            OwlRlBatteryHelpers.Triple(d, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(d, terms.OnProperty, q),
            OwlRlBatteryHelpers.Triple(d, terms.HasValue, v),
            OwlRlBatteryHelpers.Triple(ranged, terms.Range, integer),
            OwlRlBatteryHelpers.Triple(ranged, terms.Range, text),
            OwlRlBatteryHelpers.Triple(ranged, terms.Type, terms.RdfProperty),
            OwlRlBatteryHelpers.Triple(candidate, terms.Type, terms.RdfProperty)
        ];

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
        Assert.IsTrue(semiNaiveDerived.SetEquals(naive.Derived), "The semi-naive and naive derived sets are equal.");
    }

    /// <summary>An alias held owl:sameAs a datatype-map member retypes its literals onto the member — the corpus shape: literal denotation runs through the datatype the type IRI denotes, and the value-identity bridge carries the respelled conclusion.</summary>
    [TestMethod]
    public void DatatypeAliasRetypeSettlesTheCorpusShape()
    {
        List<Quad> premise =
        [
            Q(Iri(XsdNs + "decimal"), OwlNs + "sameAs", Named("bar")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>The mirrored sameAs orientation retypes identically — the identity is symmetric and the rule tries both readings of every edge.</summary>
    [TestMethod]
    public void TheMirroredAliasOrientationAlsoSettles()
    {
        List<Quad> premise =
        [
            Q(Named("bar"), OwlNs + "sameAs", Iri(XsdNs + "decimal")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A conclusion spelled with the premise's own lexical settles through the retype alone — no value respelling is involved.</summary>
    [TestMethod]
    public void TheSameLexicalConclusionSettlesThroughTheRetypeAlone()
    {
        List<Quad> premise =
        [
            Q(Iri(XsdNs + "decimal"), OwlNs + "sameAs", Named("bar")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("01", XsdNs + "decimal"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A value-respelled conclusion with no alias in sight settles through the bridge alone — the dt-eq direction at the entailment surface.</summary>
    [TestMethod]
    public void AValueRespelledConclusionSettlesThroughTheBridgeAlone()
    {
        List<Quad> premise =
        [
            Q(Named("xx"), Example + "yy", Bound("01", XsdNs + "decimal"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>owl:equivalentClass between the datatype IRIs refuses the retype — equal value spaces never force equal lexical maps; only identity engages the denotation clause.</summary>
    [TestMethod]
    public void EquivalentClassBetweenDatatypesRefusesTheRetype()
    {
        List<Quad> premise =
        [
            Q(Iri(XsdNs + "decimal"), OwlNs + "equivalentClass", Named("bar")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("01", XsdNs + "decimal"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>rdfs:subClassOf between the datatype IRIs refuses the retype for the same reason.</summary>
    [TestMethod]
    public void SubClassOfBetweenDatatypesRefusesTheRetype()
    {
        List<Quad> premise =
        [
            Q(Named("bar"), RdfsNs + "subClassOf", Iri(XsdNs + "decimal")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("01", XsdNs + "decimal"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A lexical form invalid for the target refuses the retype — both denotations are arbitrary non-values no model forces equal.</summary>
    [TestMethod]
    public void AnInvalidLexicalForTheTargetRefusesTheRetype()
    {
        List<Quad> premise =
        [
            Q(Iri(XsdNs + "integer"), OwlNs + "sameAs", Named("bar")),
            Q(Named("xx"), Example + "yy", Bound("1.5", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1.5", XsdNs + "integer"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A facade-validated target such as xsd:token refuses the retype — its lexical space is unmodelled, so an acceptance would be the default, not a check.</summary>
    [TestMethod]
    public void ATokenTargetRefusesTheRetype()
    {
        List<Quad> premise =
        [
            Q(Iri(XsdNs + "token"), OwlNs + "sameAs", Named("bar")),
            Q(Named("xx"), Example + "yy", Bound("a  b", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("a  b", XsdNs + "token"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>An alias held sameAs two recognized datatypes emits both retypes — each derivation is carried by its own edge alone, so neither is a choice over the other.</summary>
    [TestMethod]
    public void AnAliasCliqueWithTwoRecognizedTargetsEmitsBothRetypes()
    {
        List<Quad> premise =
        [
            Q(Named("bar"), OwlNs + "sameAs", Iri(XsdNs + "decimal")),
            Q(Named("bar"), OwlNs + "sameAs", Iri(XsdNs + "integer")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        AssertEntailed(premise, [Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))]);
        AssertEntailed(premise, [Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "integer"))]);
    }

    /// <summary>The float value space keeps positive and negative zero distinct, so the signed pair never bridges.</summary>
    [TestMethod]
    public void SignedFloatingZerosNeverBridge()
    {
        List<Quad> premise =
        [
            Q(Named("xx"), Example + "yy", Bound("0.0", XsdNs + "float"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("-0.0", XsdNs + "float"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>An unknown-family respelling never bridges — the oracle answers unknown outside the modelled families.</summary>
    [TestMethod]
    public void AnUnknownFamilyRespellingNeverBridges()
    {
        List<Quad> premise =
        [
            Q(Named("xx"), Example + "yy", Bound("01", Example + "mystery"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", Example + "mystery"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A numeric coincidence across the exact tower and the float space never bridges — the datatype map keeps those value spaces disjoint, so the promoted comparison's agreement is not value identity.</summary>
    [TestMethod]
    public void ACrossSpaceNumericCoincidenceNeverBridges()
    {
        List<Quad> premise =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "integer"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "float"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A numeric coincidence between the float and double spaces never bridges — they are disjoint sibling primitives, not one space.</summary>
    [TestMethod]
    public void FloatAndDoubleCoincidenceNeverBridges()
    {
        List<Quad> premise =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "float"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "double"))
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>An owl:rational respelling bridges within the exact tower — the rational, decimal, and integer value spaces genuinely nest, so one value carries across the spellings.</summary>
    [TestMethod]
    public void ARationalRespellingBridgesWithinTheExactTower()
    {
        List<Quad> premise =
        [
            Q(Named("xx"), Example + "yy", Bound("1/1", OwlNs + "rational"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "integer"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A sameAs edge that derives on a later round still retypes — the conservative trigger covers sameAs growth.</summary>
    [TestMethod]
    public void ALateDerivedAliasEdgeStillRetypes()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfsNs + "subPropertyOf", Iri(OwlNs + "sameAs")),
            Q(Iri(XsdNs + "decimal"), Example + "p", Named("bar")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>An alias of an alias still retypes — eq-trans closes the chain onto the datatype-map member on a later round and the family re-fires behind the latch.</summary>
    [TestMethod]
    public void AChainedAliasOfAnAliasStillRetypes()
    {
        List<Quad> premise =
        [
            Q(Named("baz"), OwlNs + "sameAs", Named("bar")),
            Q(Named("bar"), OwlNs + "sameAs", Iri(XsdNs + "decimal")),
            Q(Named("xx"), Example + "yy", Bound("01", Example + "baz"))
        ];

        List<Quad> conclusion =
        [
            Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>Under the normative reading the alias case stays unsettled — the retype rides only the informative conditions and the bridge only the completion-granting mode.</summary>
    [TestMethod]
    public void TheNormativeReadingLeavesTheAliasCaseUnsettled()
    {
        TermDictionary dictionary = new();

        Assert.IsFalse(OwlRlEntailment.Entails(
            [
                Q(Iri(XsdNs + "decimal"), OwlNs + "sameAs", Named("bar")),
                Q(Named("xx"), Example + "yy", Bound("01", Example + "bar"))
            ],
            [Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "decimal"))],
            dictionary, new OwlRlTerms(dictionary),
            OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>The semi-naive and naive closures agree on the alias shapes — the sameAs trigger and the alias-pair latch lose no derivation.</summary>
    [TestMethod]
    public void TheSemiNaiveAndNaiveClosuresAgreeOnTheAliasShape()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId dec = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "decimal");
        TermId bar = OwlRlBatteryHelpers.Named(dictionary, Example + "bar");
        TermId baz = OwlRlBatteryHelpers.Named(dictionary, Example + "baz");
        TermId yy = OwlRlBatteryHelpers.Mint(dictionary, "yy");
        TermId xx = OwlRlBatteryHelpers.Mint(dictionary, "xx");
        TermId xx2 = OwlRlBatteryHelpers.Mint(dictionary, "xx2");
        TermId aliasLiteral = OwlRlBatteryHelpers.Literal(dictionary, "01", Example + "bar");
        TermId chainedLiteral = OwlRlBatteryHelpers.Literal(dictionary, "02", Example + "baz");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(dec, terms.SameAs, bar),
            OwlRlBatteryHelpers.Triple(baz, terms.SameAs, bar),
            OwlRlBatteryHelpers.Triple(xx, yy, aliasLiteral),
            OwlRlBatteryHelpers.Triple(xx2, yy, chainedLiteral)
        ];

        OwlRlResult semiNaive = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveDerived = [.. semiNaive.Derived];
        Assert.IsTrue(semiNaiveDerived.SetEquals(naive.Derived), "The semi-naive and naive derived sets are equal over the alias shapes.");
    }

    /// <summary>A clash unique to the bridged run refuses the bridges and keeps the pre-bridge state — minted scaffolding included: the scaffold-dependent part stays proven and only the false literal claim stays unsettled.</summary>
    [TestMethod]
    public void ABridgeOnlyClashDegradesToThePreBridgeState()
    {
        TermDictionary dictionary = new();
        OwlRlDatatypeOracle rigged = OwlRlDatatypeOracles.FromDictionary(dictionary) with { LiteralsKnownEqual = static (_, _) => true };
        List<Quad> premise = CardinalityShorthandPremise(minFirst: true, withExtraMember: false);
        premise.Add(Q(Named("xx"), Example + "yy", Bound("1", XsdNs + "integer")));
        Quad falseClaim = Q(Named("xx"), Example + "yy", Bound("2", XsdNs + "integer"));
        List<Quad> conclusion = CardinalityShorthandConclusion();
        conclusion.Add(falseClaim);

        bool entailed = OwlRlEntailment.TryEntail(
            premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled,
            rigged, OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(entailed, "A bridged-only clash never proves everything.");
        Assert.HasCount(1, unsettled, "The scaffold-dependent part stays proven through the retained minting.");
        Assert.AreEqual(falseClaim, unsettled[0]);
    }

    /// <summary>The datatype oracle's alias admissions and value equality: the pair gate refuses recognized aliases and unmodelled targets, the retype mints the exact target-typed literal, and equal and distinct never both affirm.</summary>
    [TestMethod]
    public void TheDatatypeOracleAnswersAliasAdmissionsAndValueEquality()
    {
        TermDictionary dictionary = new();
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        TermId dec = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "decimal");
        TermId integer = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "integer");
        TermId intType = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "int");
        TermId token = OwlRlBatteryHelpers.Named(dictionary, XsdNs + "token");
        TermId bar = OwlRlBatteryHelpers.Named(dictionary, Example + "bar");
        TermId baz = OwlRlBatteryHelpers.Named(dictionary, Example + "baz");
        TermId aliasLiteral = OwlRlBatteryHelpers.Literal(dictionary, "01", Example + "bar");
        TermId fractionalAlias = OwlRlBatteryHelpers.Literal(dictionary, "1.5", Example + "bar");

        Assert.IsTrue(oracle.DatatypeAliasRecognized(bar, dec), "An unknown alias against a modelled member admits.");
        Assert.IsFalse(oracle.DatatypeAliasRecognized(dec, bar), "A recognized alias side refuses.");
        Assert.IsFalse(oracle.DatatypeAliasRecognized(intType, integer), "A recognized-recognized pair refuses.");
        Assert.IsFalse(oracle.DatatypeAliasRecognized(bar, token), "An unmodelled target's validity would be the default acceptance, so it refuses.");
        Assert.IsFalse(oracle.DatatypeAliasRecognized(bar, baz), "An unknown target refuses.");
        Assert.IsFalse(oracle.DatatypeAliasRecognized(aliasLiteral, dec), "A non-IRI alias side refuses.");

        TermId retyped = oracle.DatatypeAliasRetype(aliasLiteral, bar, dec);
        Assert.AreEqual(OwlRlBatteryHelpers.Literal(dictionary, "01", XsdNs + "decimal"), retyped, "The retype mints exactly the target-typed literal.");
        Assert.AreEqual(TermId.None, oracle.DatatypeAliasRetype(fractionalAlias, bar, integer), "A lexical invalid for the target refuses.");
        Assert.AreEqual(TermId.None, oracle.DatatypeAliasRetype(aliasLiteral, bar, token), "An unmodelled target refuses the mint.");
        Assert.AreEqual(TermId.None, oracle.DatatypeAliasRetype(bar, bar, dec), "A non-literal candidate refuses.");

        TermId paddedInteger = OwlRlBatteryHelpers.Literal(dictionary, "01", XsdNs + "integer");
        TermId plainInteger = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdNs + "integer");
        TermId pointDecimal = OwlRlBatteryHelpers.Literal(dictionary, "1.0", XsdNs + "decimal");
        TermId unitRational = OwlRlBatteryHelpers.Literal(dictionary, "1/1", OwlNs + "rational");
        TermId oneFloat = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdNs + "float");
        TermId oneDouble = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdNs + "double");
        TermId positiveZeroFloat = OwlRlBatteryHelpers.Literal(dictionary, "0.0", XsdNs + "float");
        TermId negativeZeroFloat = OwlRlBatteryHelpers.Literal(dictionary, "-0.0", XsdNs + "float");
        TermId plainZeroFloat = OwlRlBatteryHelpers.Literal(dictionary, "0", XsdNs + "float");
        TermId trueBoolean = OwlRlBatteryHelpers.Literal(dictionary, "true", XsdNs + "boolean");
        TermId oneBoolean = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdNs + "boolean");
        TermId twoInteger = OwlRlBatteryHelpers.Literal(dictionary, "2", XsdNs + "integer");

        Assert.IsTrue(oracle.LiteralsKnownEqual(paddedInteger, plainInteger), "A padded integer respelling is one value.");
        Assert.IsTrue(oracle.LiteralsKnownEqual(plainInteger, pointDecimal), "The integer and decimal spellings of one number are one value.");
        Assert.IsTrue(oracle.LiteralsKnownEqual(unitRational, plainInteger), "The rational spelling of one number is one value.");
        Assert.IsTrue(oracle.LiteralsKnownEqual(positiveZeroFloat, plainZeroFloat), "Two positive-zero float spellings are one value.");
        Assert.IsTrue(oracle.LiteralsKnownEqual(trueBoolean, oneBoolean), "The boolean spellings of one truth value are one value.");
        Assert.IsFalse(oracle.LiteralsKnownEqual(plainInteger, oneFloat), "The exact tower and the float space never share a value.");
        Assert.IsFalse(oracle.LiteralsKnownEqual(oneFloat, oneDouble), "The float and double spaces never share a value.");
        Assert.IsFalse(oracle.LiteralsKnownEqual(positiveZeroFloat, negativeZeroFloat), "The float space keeps the signed zeros distinct.");
        Assert.IsFalse(oracle.LiteralsKnownEqual(oneBoolean, plainInteger), "A boolean and a number are never one value.");

        Span<(TermId First, TermId Second)> pairs =
        [
            (paddedInteger, plainInteger),
            (plainInteger, pointDecimal),
            (positiveZeroFloat, negativeZeroFloat),
            (plainInteger, oneFloat),
            (plainInteger, twoInteger),
            (trueBoolean, oneBoolean)
        ];
        foreach((TermId first, TermId second) in pairs)
        {
            Assert.IsFalse(oracle.LiteralsKnownEqual(first, second) && oracle.LiteralsKnownDistinct(first, second), "Equal and distinct never both affirm on one pair.");
        }
    }

    /// <summary>Under the normative reading the same comprehension conclusions stay unsettled — the completions ride only the informative conditions.</summary>
    [TestMethod]
    public void NormativeReadingLeavesTheComprehensionCasesUnsettled()
    {
        List<Quad> premise =
        [
            Q(Named("prop"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("object"), RdfNs + "type", Iri(OwlNs + "Thing")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("object"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r"), OwlNs + "onProperty", Named("prop")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            premise, conclusion, dictionary, new OwlRlTerms(dictionary),
            OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));

        List<Quad> enumeration =
        [
            Q(Blank("c"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Blank("c"), OwlNs + "oneOf", Iri(RdfNs + "nil")),
            Q(Blank("c"), OwlNs + "equivalentClass", Iri(OwlNs + "Nothing")),
        ];

        TermDictionary second = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            [], enumeration, second, new OwlRlTerms(second),
            OwlRlDatatypeOracles.FromDictionary(second), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>Without the functional typing the max-1 restriction does not cover everything — the minted structure alone settles nothing.</summary>
    [TestMethod]
    public void NonFunctionalPropertyDoesNotEntailTheMaxOneRestriction()
    {
        List<Quad> premise =
        [
            Q(Named("prop"), RdfNs + "type", Iri(OwlNs + "ObjectProperty")),
            Q(Named("object"), RdfNs + "type", Iri(OwlNs + "Thing")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("object"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r"), OwlNs + "onProperty", Named("prop")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A union of a class and a complement of a different class covers nothing special — the excluded middle demands the same class on both sides.</summary>
    [TestMethod]
    public void MismatchedComplementDoesNotEntailTheExcludedMiddle()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Iri(OwlNs + "Thing")),
            Q(Named("c"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Named("d"), RdfNs + "type", Iri(OwlNs + "Class")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("x"), RdfNs + "type", Blank("u")),
            Q(Blank("u"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Blank("comp"), OwlNs + "complementOf", Named("d")),
        ];
        RdfTerm head = AddList(conclusion, "u-members", [Named("c"), Blank("comp")]);
        conclusion.Add(Q(Blank("u"), OwlNs + "unionOf", head));

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A strict disjunct subset concludes the one subsumption direction and not the equivalence.</summary>
    [TestMethod]
    public void DeMorganSubsetConcludesOneDirectionOnly()
    {
        List<Quad> premise =
        [
            Q(Named("A"), RdfNs + "type", Iri(RdfsNs + "Class")),
            Q(Named("B"), RdfNs + "type", Iri(RdfsNs + "Class")),
            Q(Named("C"), RdfNs + "type", Iri(RdfsNs + "Class")),
        ];

        AssertEntailed(premise, DeMorganStrictSubsetConclusion(RdfsNs + "subClassOf"));
        AssertNotEntailed(premise, DeMorganStrictSubsetConclusion(OwlNs + "equivalentClass"));
    }

    /// <summary>The shorthand reads the premise list as a set — a swapped member order still settles — and an extra third member disarms it.</summary>
    [TestMethod]
    public void CardinalityShorthandIgnoresListOrderAndExtraMembers()
    {
        AssertEntailed(CardinalityShorthandPremise(minFirst: true, withExtraMember: false), CardinalityShorthandConclusion());
        AssertNotEntailed(CardinalityShorthandPremise(minFirst: false, withExtraMember: true), CardinalityShorthandConclusion());
    }

    /// <summary>The default closure never fires the comprehension family: the same shapes derive none of its conclusions without the flag.</summary>
    [TestMethod]
    public void DefaultClosureStaysDarkOnComprehensionShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = ComprehensionShapedBase(dictionary, terms, out TermId restriction, out TermId enumeration);

        OwlRlResult result = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.Thing, terms.SubClassOf, restriction), result.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(enumeration, terms.SubClassOf, terms.Nothing), result.Derived);
    }

    /// <summary>The maintained closure never fires the comprehension family — through the initial build and an incremental Apply alike.</summary>
    [TestMethod]
    public void MaintainedClosureStaysDarkOnComprehensionShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = ComprehensionShapedBase(dictionary, terms, out TermId restriction, out TermId enumeration);

        OwlRlMaintainedClosure maintained = new(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), TestContext.CancellationToken);

        Assert.IsTrue(maintained.Current.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.Thing, terms.SubClassOf, restriction), maintained.Current.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(enumeration, terms.SubClassOf, terms.Nothing), maintained.Current.Derived);

        TermId extra = OwlRlBatteryHelpers.Mint(dictionary, "extra");
        OwlRlResult applied = maintained.Apply([OwlRlBatteryHelpers.Triple(extra, terms.Type, terms.ClassTerm)], [], TestContext.CancellationToken);

        Assert.IsTrue(applied.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(terms.Thing, terms.SubClassOf, restriction), applied.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(enumeration, terms.SubClassOf, terms.Nothing), applied.Derived);
    }

    /// <summary>The semi-naive and naive evaluations agree on a graph exercising the whole comprehension family with the flag on.</summary>
    [TestMethod]
    public void SemiNaiveAndNaiveAgreeOnComprehensionShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = ComprehensionShapedBase(dictionary, terms, out TermId restriction, out _);

        OwlRlResult semiNaive = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveSet = [.. semiNaive.Derived];
        HashSet<EncodedTriple> naiveSet = [.. naive.Derived];
        Assert.IsTrue(semiNaiveSet.SetEquals(naiveSet));
        Assert.Contains(OwlRlBatteryHelpers.Triple(terms.Thing, terms.SubClassOf, restriction), semiNaiveSet);
    }

    /// <summary>
    /// A scaffold copy stands on an engine-minted node, never on a blank label: the copies carry no blank node
    /// of the conclusion's scaffold at all, and through one shared dictionary no minted node's identifier
    /// coincides with any blank the premise or the conclusion spells — including a premise blank whose label
    /// spells a copy's own rendered Skolem IRI, the spelling an avoidance scan would have had to dodge.
    /// </summary>
    [TestMethod]
    public void MintedScaffoldCopiesAreEngineNodesApartFromEveryInputBlank()
    {
        List<Quad> premise =
        [
            Q(Named("prop"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("object"), RdfNs + "type", Iri(OwlNs + "Thing")),
            Q(Blank(ScaffoldCopySpelling), RdfNs + "type", Iri(OwlNs + "Class")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("object"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r"), OwlNs + "onProperty", Named("prop")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        List<Quad> copies = OwlComprehensionScaffolds.MintContentful(conclusion, premise);

        Assert.IsNotEmpty(copies, "The contentful restriction scaffold mints its grammar copies.");

        TermDictionary dictionary = new();
        HashSet<TermId> inputBlanks = [];
        AppendBlankIds(premise, dictionary, inputBlanks);
        AppendBlankIds(conclusion, dictionary, inputBlanks);

        int mintedPositions = 0;
        foreach(Quad copy in copies)
        {
            mintedPositions += CountMintedPosition(copy.Subject, dictionary, inputBlanks);
            mintedPositions += CountMintedPosition(copy.Object, dictionary, inputBlanks);
        }

        Assert.IsGreaterThan(0, mintedPositions, "Every blank position of the original scaffold is replaced by an engine-minted node.");

        AssertEntailed(premise, conclusion);
    }

    /// <summary>
    /// The existential witness is an engine mint, so a premise blank spelling the witness's former label is a
    /// different term: the closure keeps the minted witness's own identifier, the poison typing the spoof node
    /// carries reaches the spoof node alone, and none of it lands on the witness.
    /// </summary>
    [TestMethod]
    public void PoisonedWitnessSpellingNeverReachesTheMintedWitness()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = WitnessShapedBase(dictionary, terms, out TermId restriction, out TermId instance, out TermId property, out TermId filler);
        TermId poison = OwlRlBatteryHelpers.Blank(dictionary, $"rl-svf-witness-{instance.Encoded}-{restriction.Encoded}-{property.Encoded}-{filler.Encoded}");
        TermId poisonClass = OwlRlBatteryHelpers.Mint(dictionary, "Poison");
        TermId poisonSuper = OwlRlBatteryHelpers.Mint(dictionary, "PoisonSuper");
        triples.Add(OwlRlBatteryHelpers.Triple(poisonClass, terms.SubClassOf, poisonSuper));
        triples.Add(OwlRlBatteryHelpers.Triple(poison, terms.Type, poisonClass));

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        TermId witness = terms.SomeValuesFromWitnessNode(instance, restriction, property, filler);
        Assert.IsTrue(result.IsConsistent);
        Assert.AreNotEqual(poison, witness, "The engine-minted witness is a distinct term from a parsed blank spelling its former label.");
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, property, witness), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(witness, terms.Type, filler), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(poison, terms.Type, poisonSuper), result.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(witness, terms.Type, poisonClass), result.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(witness, terms.Type, poisonSuper), result.Derived);
    }

    /// <summary>
    /// The same spoof gate over the transitivity-chain structure: the chain nodes are content-keyed — re-minting
    /// returns the same node, the two positions differ, and a second property carries its own structure — and a
    /// premise blank spelling a chain node's former label stays a separate term whose poison typing never
    /// reaches the chain.
    /// </summary>
    [TestMethod]
    public void PoisonedChainSpellingNeverReachesTheMintedChainNodes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId property = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId sibling = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId start = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId middle = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId end = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId poison = OwlRlBatteryHelpers.Blank(dictionary, $"rl-trans-chain-{property.Encoded}-0");
        TermId poisonClass = OwlRlBatteryHelpers.Mint(dictionary, "Poison");
        TermId poisonSuper = OwlRlBatteryHelpers.Mint(dictionary, "PoisonSuper");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(property, terms.Type, terms.TransitiveProperty),
            OwlRlBatteryHelpers.Triple(start, property, middle),
            OwlRlBatteryHelpers.Triple(middle, property, end),
            OwlRlBatteryHelpers.Triple(poisonClass, terms.SubClassOf, poisonSuper),
            OwlRlBatteryHelpers.Triple(poison, terms.Type, poisonClass),
        ];

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        int internedAfterClosure = dictionary.Count;
        TermId firstLink = terms.TransitivityChainNode(property, 0);
        Assert.AreEqual(internedAfterClosure, dictionary.Count, "The closure already interned the chain node, so the row observes the live extension structure rather than a term it minted itself.");

        TermId secondLink = terms.TransitivityChainNode(property, 1);
        Assert.IsTrue(result.IsConsistent);
        Assert.AreEqual(firstLink, terms.TransitivityChainNode(property, 0), "The chain node is content-keyed, so re-minting the same position returns the same term.");
        Assert.AreNotEqual(firstLink, secondLink, "The two chain positions are distinct nodes.");
        Assert.AreNotEqual(firstLink, terms.TransitivityChainNode(sibling, 0), "A second transitive property carries its own chain structure.");
        Assert.AreNotEqual(poison, firstLink, "The engine-minted chain node is a distinct term from a parsed blank spelling its former label.");
        Assert.Contains(OwlRlBatteryHelpers.Triple(start, property, end), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(poison, terms.Type, poisonSuper), result.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(firstLink, terms.Type, poisonSuper), result.Derived);
    }

    /// <summary>A named member the premise never forces into class standing refuses the whole scaffold — the comprehension conditions quantify over classes only.</summary>
    [TestMethod]
    public void UnforcedNamedMembersRefuseTheScaffold()
    {
        List<Quad> premise = [Q(Named("A"), RdfNs + "type", Iri(OwlNs + "Class"))];
        List<Quad> conclusion =
        [
            Q(Blank("i"), RdfsNs + "subClassOf", Named("B")),
        ];
        RdfTerm head = AddList(conclusion, "i-members", [Named("A"), Named("B")]);
        conclusion.Add(Q(Blank("i"), OwlNs + "intersectionOf", head));

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A node stated under two constraint forms at once is granted by neither, so nothing mints and the unsatisfiable conclusion never launders into vacuous entailment.</summary>
    [TestMethod]
    public void OverConstrainedScaffoldsAreNeverMinted()
    {
        List<Quad> premise =
        [
            Q(Named("a"), Example + "p", Named("b")),
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "ObjectProperty")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("a"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Iri(OwlNs + "Thing")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("0", XsdNs + "nonNegativeInteger")),
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>A pure scaffold stating two boolean constructors on one node is granted by neither side: the stripper refuses it and the unproven structure blocks the entailment.</summary>
    [TestMethod]
    public void PureTwoConstructorScaffoldIsNeverStripped()
    {
        List<Quad> conclusion =
        [
            Q(Blank("x"), OwlNs + "unionOf", Blank("l1")),
            Q(Blank("l1"), RdfNs + "first", Named("M")),
            Q(Blank("l1"), RdfNs + "rest", Iri(RdfNs + "nil")),
            Q(Blank("x"), OwlNs + "complementOf", Named("N")),
        ];

        AssertNotEntailed([], conclusion);
    }

    /// <summary>A pure restriction scaffold stating two primary constraints is granted by neither side and blocks the entailment.</summary>
    [TestMethod]
    public void PureTwoPrimaryConstraintScaffoldIsNeverStripped()
    {
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("C")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        AssertNotEntailed([], conclusion);
    }

    /// <summary>A pure restriction scaffold carrying two onProperty triples is granted by neither side and blocks the entailment.</summary>
    [TestMethod]
    public void PureDoubleOnPropertyScaffoldIsNeverStripped()
    {
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "onProperty", Named("q")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("C")),
        ];

        AssertNotEntailed([], conclusion);
    }

    /// <summary>A pure scaffold pairing onProperty with a qualifier class but no qualified cardinality is not a restriction shape at all: refused, never stripped.</summary>
    [TestMethod]
    public void PureQualifierWithoutQualifiedCardinalityIsNeverStripped()
    {
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "onClass", Named("C")),
        ];

        AssertNotEntailed([], conclusion);
    }

    /// <summary>The control: a clean single-constructor pure scaffold still strips whole and the entailment holds vacuously.</summary>
    [TestMethod]
    public void SingleConstructorPureScaffoldStillStrips()
    {
        List<Quad> premise = [Q(Named("p"), RdfNs + "type", Iri(OwlNs + "ObjectProperty"))];
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "minCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>The comprehension conditions grant restrictions only over standing-bearing arguments: a pure scaffold naming a property and a filler the premise never forces is not entailed — a model exists where neither has the standing.</summary>
    [TestMethod]
    public void UnforcedStandingScaffoldIsNeverStripped()
    {
        List<Quad> premise = [Q(Named("a"), RdfNs + "type", Named("Foo"))];
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("C")),
        ];

        AssertNotEntailed(premise, conclusion);
    }

    /// <summary>The same scaffold strips once the premise forces both standings, and the entailment holds vacuously.</summary>
    [TestMethod]
    public void ForcedStandingScaffoldStillStrips()
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "ObjectProperty")),
            Q(Named("C"), RdfNs + "type", Iri(OwlNs + "Class")),
        ];
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("C")),
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>Standing the closure derives counts: a property typed only through a subclass of owl:ObjectProperty is property-forced by the derived typing, and the scaffold strips.</summary>
    [TestMethod]
    public void ClosureDerivedStandingStripsTheScaffold()
    {
        List<Quad> premise =
        [
            Q(Named("PropertyKind"), RdfsNs + "subClassOf", Iri(OwlNs + "ObjectProperty")),
            Q(Named("p"), RdfNs + "type", Named("PropertyKind")),
        ];
        List<Quad> conclusion =
        [
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "minCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A union's members are class expressions: a pure union scaffold over an unforced named member is granted by neither side and blocks the entailment.</summary>
    [TestMethod]
    public void UnforcedUnionMemberScaffoldIsNeverStripped()
    {
        List<Quad> conclusion =
        [
            Q(Blank("x"), OwlNs + "unionOf", Blank("l1")),
            Q(Blank("l1"), RdfNs + "first", Named("M")),
            Q(Blank("l1"), RdfNs + "rest", Iri(RdfNs + "nil")),
        ];

        AssertNotEntailed([], conclusion);
    }

    /// <summary>An enumeration's members are individuals and need no standing: the pure singleton enumeration scaffold strips from the empty premise.</summary>
    [TestMethod]
    public void EnumerationMemberScaffoldNeedsNoStanding()
    {
        List<Quad> conclusion =
        [
            Q(Blank("x"), OwlNs + "oneOf", Blank("l1")),
            Q(Blank("l1"), RdfNs + "first", Named("a")),
            Q(Blank("l1"), RdfNs + "rest", Iri(RdfNs + "nil")),
        ];

        AssertEntailed([], conclusion);
    }

    /// <summary>A cyclic enumeration list is no sequence: the empty-enumeration rule derives nothing from it and the closure stays consistent.</summary>
    [TestMethod]
    public void CyclicEnumerationListDerivesNothing()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId enumeration = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId cell = OwlRlBatteryHelpers.Blank(dictionary, "cyclic-cell");
        TermId member = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "x");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(enumeration, terms.OneOf, cell),
            OwlRlBatteryHelpers.Triple(cell, terms.First, member),
            OwlRlBatteryHelpers.Triple(cell, terms.Rest, cell),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, enumeration),
        ];

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(enumeration, terms.SubClassOf, terms.Nothing), result.Derived);
    }

    /// <summary>A functional typing arriving only through the inverse-characteristic transfer still fires the universal max-1 — the conservative trigger covers late premise arrival.</summary>
    [TestMethod]
    public void LateArrivingFunctionalTypingStillFiresTheUniversalMaxOne()
    {
        List<Quad> premise =
        [
            Q(Named("q"), OwlNs + "inverseOf", Named("prop")),
            Q(Named("q"), RdfNs + "type", Iri(OwlNs + "InverseFunctionalProperty")),
            Q(Named("object"), RdfNs + "type", Iri(OwlNs + "Thing")),
        ];
        List<Quad> conclusion =
        [
            Q(Named("object"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r"), OwlNs + "onProperty", Named("prop")),
            Q(Blank("r"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        AssertEntailed(premise, conclusion);
    }

    /// <summary>A member of a subclass of a some-values-from restriction has a witnessed value in the filler — the corpus shape, settled by the minted witness.</summary>
    [TestMethod]
    public void SomeValuesFromMemberEntailsTheExistentialWitness()
    {
        AssertEntailed(WitnessCorpusPremise(withAssertedFiller: false), WitnessCorpusConclusion());
    }

    /// <summary>An asserted filler of the property is never typed by the witness: some-values-from differs from all-values-from, and the fresh witness stays apart from the real filler.</summary>
    [TestMethod]
    public void ExistingFillerIsNeverTypedByTheWitness()
    {
        AssertNotEntailed(WitnessCorpusPremise(withAssertedFiller: true), [Q(Named("o"), RdfNs + "type", Named("c"))]);
    }

    /// <summary>Under the normative reading the witness conclusion stays unsettled — the existential rides only the informative conditions.</summary>
    [TestMethod]
    public void NormativeReadingLeavesTheWitnessCaseUnsettled()
    {
        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            WitnessCorpusPremise(withAssertedFiller: false), WitnessCorpusConclusion(), dictionary, new OwlRlTerms(dictionary),
            OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.None, cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>The default closure never mints a witness: the same shape derives neither the edge nor the typing without the flag.</summary>
    [TestMethod]
    public void DefaultClosureStaysDarkOnWitnessShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = WitnessShapedBase(dictionary, terms, out TermId restriction, out TermId instance, out TermId property, out TermId filler);

        OwlRlResult result = OwlRlClosure.Compute(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        TermId witness = terms.SomeValuesFromWitnessNode(instance, restriction, property, filler);
        Assert.IsTrue(result.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(instance, property, witness), result.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(witness, terms.Type, filler), result.Derived);
    }

    /// <summary>The maintained closure never mints a witness — through the initial build and an incremental Apply alike.</summary>
    [TestMethod]
    public void MaintainedClosureStaysDarkOnWitnessShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = WitnessShapedBase(dictionary, terms, out TermId restriction, out TermId instance, out TermId property, out TermId filler);

        OwlRlMaintainedClosure maintained = new(triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), TestContext.CancellationToken);

        TermId witness = terms.SomeValuesFromWitnessNode(instance, restriction, property, filler);
        Assert.IsTrue(maintained.Current.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(instance, property, witness), maintained.Current.Derived);

        TermId another = OwlRlBatteryHelpers.Mint(dictionary, "another");
        OwlRlResult applied = maintained.Apply([OwlRlBatteryHelpers.Triple(another, terms.Type, restriction)], [], TestContext.CancellationToken);

        Assert.IsTrue(applied.IsConsistent);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(instance, property, witness), applied.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(another, property, terms.SomeValuesFromWitnessNode(another, restriction, property, filler)), applied.Derived);
    }

    /// <summary>Two closures over the same input derive the identical witness triples — the content-derived labels make the minting a function of the graph.</summary>
    [TestMethod]
    public void WitnessDerivationIsDeterministicAcrossRuns()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = WitnessShapedBase(dictionary, terms, out TermId restriction, out TermId instance, out TermId property, out TermId filler);

        OwlRlResult first = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        OwlRlResult second = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        TermId witness = terms.SomeValuesFromWitnessNode(instance, restriction, property, filler);
        HashSet<EncodedTriple> firstSet = [.. first.Derived];
        Assert.IsTrue(firstSet.SetEquals([.. second.Derived]));
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, property, witness), firstSet);
        Assert.Contains(OwlRlBatteryHelpers.Triple(witness, terms.Type, filler), firstSet);
    }

    /// <summary>A filler subsumed under its own restriction unfolds exactly one witness level: the repeated restriction on the chain refuses the second mint and the fixpoint terminates.</summary>
    [TestMethod]
    public void WitnessUnfoldingRefusesARepeatedRestriction()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = OwlRlBatteryHelpers.Blank(dictionary, "self-restriction");
        TermId property = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "x");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(restriction, terms.OnProperty, property),
            OwlRlBatteryHelpers.Triple(restriction, terms.SomeValuesFrom, filler),
            OwlRlBatteryHelpers.Triple(filler, terms.SubClassOf, restriction),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, filler),
        ];

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        TermId level1 = terms.SomeValuesFromWitnessNode(instance, restriction, property, filler);
        TermId level2 = terms.SomeValuesFromWitnessNode(level1, restriction, property, filler);
        Assert.IsTrue(result.IsConsistent);
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, property, level1), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(level1, terms.Type, filler), result.Derived);
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(level1, property, level2), result.Derived);
    }

    /// <summary>A non-repeating two-restriction chain mints both witness levels — the cycle bound refuses only repeats, never depth.</summary>
    [TestMethod]
    public void WitnessChainMintsThroughDistinctRestrictions()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = WitnessChainBase(
            dictionary, terms, out TermId firstRestriction, out TermId secondRestriction,
            out TermId firstProperty, out TermId secondProperty, out TermId firstFiller, out TermId secondFiller, out TermId instance);

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        TermId level1 = terms.SomeValuesFromWitnessNode(instance, firstRestriction, firstProperty, firstFiller);
        TermId level2 = terms.SomeValuesFromWitnessNode(level1, secondRestriction, secondProperty, secondFiller);
        Assert.IsTrue(result.IsConsistent);
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, firstProperty, level1), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(level1, terms.Type, firstFiller), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(level1, secondProperty, level2), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(level2, terms.Type, secondFiller), result.Derived);
    }

    /// <summary>A member typing that reaches the restriction only through a two-hop subsumption chain still mints the witness — the conservative trigger covers late instance arrival.</summary>
    [TestMethod]
    public void LateArrivingMemberTypingStillMintsTheWitness()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = OwlRlBatteryHelpers.Blank(dictionary, "late-restriction");
        TermId property = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId lower = OwlRlBatteryHelpers.Mint(dictionary, "lower");
        TermId middle = OwlRlBatteryHelpers.Mint(dictionary, "middle");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "x");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(restriction, terms.OnProperty, property),
            OwlRlBatteryHelpers.Triple(restriction, terms.SomeValuesFrom, filler),
            OwlRlBatteryHelpers.Triple(lower, terms.SubClassOf, middle),
            OwlRlBatteryHelpers.Triple(middle, terms.SubClassOf, restriction),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, lower),
        ];

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        TermId witness = terms.SomeValuesFromWitnessNode(instance, restriction, property, filler);
        Assert.IsTrue(result.IsConsistent);
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, property, witness), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(witness, terms.Type, filler), result.Derived);
    }

    /// <summary>The semi-naive and naive evaluations agree on a graph exercising the witness chain and the refused self-unfolding together.</summary>
    [TestMethod]
    public void SemiNaiveAndNaiveAgreeOnWitnessShapes()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> triples = WitnessChainBase(
            dictionary, terms, out TermId firstRestriction, out _, out TermId firstProperty, out _, out TermId firstFiller, out TermId secondFiller, out TermId instance);
        triples.Add(OwlRlBatteryHelpers.Triple(secondFiller, terms.SubClassOf, firstRestriction));

        OwlRlResult semiNaive = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveSet = [.. semiNaive.Derived];
        Assert.IsTrue(semiNaiveSet.SetEquals([.. naive.Derived]));
        Assert.Contains(
            OwlRlBatteryHelpers.Triple(instance, firstProperty, terms.SomeValuesFromWitnessNode(instance, firstRestriction, firstProperty, firstFiller)),
            semiNaiveSet);
    }

    /// <summary>Under a functional property the witness merges onto the asserted filler, so the filler's typing genuinely follows — the sound merge direction, apart from the non-functional leak gate.</summary>
    [TestMethod]
    public void FunctionalPropertyMergesTheWitnessOntoTheFiller()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("c")),
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("x"), Example + "p", Named("o")),
        ];

        AssertEntailed(premise, [Q(Named("o"), RdfNs + "type", Named("c"))]);
    }

    /// <summary>Two fillers on one restriction node state independent existentials: each mints its own witness, and a single shared witness typed by both fillers is never granted.</summary>
    [TestMethod]
    public void MultiFillerNodeMintsIndependentWitnesses()
    {
        List<Quad> premise =
        [
            Q(Named("x"), RdfNs + "type", Blank("r")),
            Q(Blank("r"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("c1")),
            Q(Blank("r"), OwlNs + "someValuesFrom", Named("c2")),
        ];
        List<Quad> independent =
        [
            Q(Named("x"), Example + "p", Blank("w1")),
            Q(Blank("w1"), RdfNs + "type", Named("c1")),
            Q(Named("x"), Example + "p", Blank("w2")),
            Q(Blank("w2"), RdfNs + "type", Named("c2")),
        ];
        List<Quad> shared =
        [
            Q(Named("x"), Example + "p", Blank("w")),
            Q(Blank("w"), RdfNs + "type", Named("c1")),
            Q(Blank("w"), RdfNs + "type", Named("c2")),
        ];

        AssertEntailed(premise, independent);
        AssertNotEntailed(premise, shared);
    }

    /// <summary>Disjoint fillers on one multi-filler restriction stay consistent under the witnesses — the independent witnesses never collapse into a clash on a satisfiable premise.</summary>
    [TestMethod]
    public void DisjointFillersStayConsistentUnderTheWitnesses()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = OwlRlBatteryHelpers.Blank(dictionary, "two-filler-restriction");
        TermId property = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId firstFiller = OwlRlBatteryHelpers.Mint(dictionary, "C1");
        TermId secondFiller = OwlRlBatteryHelpers.Mint(dictionary, "C2");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "x");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(restriction, terms.OnProperty, property),
            OwlRlBatteryHelpers.Triple(restriction, terms.SomeValuesFrom, firstFiller),
            OwlRlBatteryHelpers.Triple(restriction, terms.SomeValuesFrom, secondFiller),
            OwlRlBatteryHelpers.Triple(firstFiller, terms.DisjointWith, secondFiller),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, restriction),
        ];

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        Assert.Contains(
            OwlRlBatteryHelpers.Triple(terms.SomeValuesFromWitnessNode(instance, restriction, property, firstFiller), terms.Type, firstFiller),
            result.Derived);
        Assert.Contains(
            OwlRlBatteryHelpers.Triple(terms.SomeValuesFromWitnessNode(instance, restriction, property, secondFiller), terms.Type, secondFiller),
            result.Derived);
    }

    /// <summary>A restriction on <c>rdf:type</c> itself mints its witness as a typing without deriving any falsity — the semantic conditions quantify over every property position.</summary>
    [TestMethod]
    public void TypePunnedRestrictionMintsWithoutFalsity()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId restriction = OwlRlBatteryHelpers.Blank(dictionary, "type-punned-restriction");
        TermId filler = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId instance = OwlRlBatteryHelpers.Mint(dictionary, "x");
        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(restriction, terms.OnProperty, terms.Type),
            OwlRlBatteryHelpers.Triple(restriction, terms.SomeValuesFrom, filler),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, restriction),
        ];

        OwlRlResult result = OwlRlClosure.Compute(
            triples, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        TermId witness = terms.SomeValuesFromWitnessNode(instance, restriction, terms.Type, filler);
        Assert.IsTrue(result.IsConsistent);
        Assert.Contains(OwlRlBatteryHelpers.Triple(instance, terms.Type, witness), result.Derived);
        Assert.Contains(OwlRlBatteryHelpers.Triple(witness, terms.Type, filler), result.Derived);
    }

    /// <summary>The corpus premise of the witness shape: a named class below an anonymous some-values-from restriction, declared vocabulary, and a member — with the asserted filler edge of the negative case parameterised.</summary>
    /// <param name="withAssertedFiller">Whether the member also reaches an asserted filler over the property.</param>
    /// <returns>The premise graph.</returns>
    private static List<Quad> WitnessCorpusPremise(bool withAssertedFiller)
    {
        List<Quad> premise =
        [
            Q(Named("r"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Named("r"), RdfsNs + "subClassOf", Blank("restriction")),
            Q(Blank("restriction"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("restriction"), OwlNs + "onProperty", Named("p")),
            Q(Blank("restriction"), OwlNs + "someValuesFrom", Named("c")),
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "ObjectProperty")),
            Q(Named("c"), RdfNs + "type", Iri(OwlNs + "Class")),
            Q(Named("i"), RdfNs + "type", Named("r")),
        ];

        if(withAssertedFiller)
        {
            premise.Add(Q(Named("i"), Example + "p", Named("o")));
        }

        return premise;
    }

    /// <summary>The corpus conclusion of the witness shape: the member has some value in the filler.</summary>
    /// <returns>The conclusion graph.</returns>
    private static List<Quad> WitnessCorpusConclusion()
    {
        return
        [
            Q(Named("i"), Example + "p", Blank("b")),
            Q(Blank("b"), RdfNs + "type", Named("c")),
        ];
    }

    /// <summary>An encoded base carrying the plain witness shape: a restriction with one property and one filler, and one direct member.</summary>
    /// <param name="dictionary">The dictionary the terms mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="restriction">The restriction node.</param>
    /// <param name="instance">The member.</param>
    /// <param name="property">The restriction's property.</param>
    /// <param name="filler">The restriction's filler class.</param>
    /// <returns>The encoded base triples.</returns>
    private static List<EncodedTriple> WitnessShapedBase(
        TermDictionary dictionary, OwlRlTerms terms, out TermId restriction, out TermId instance, out TermId property, out TermId filler)
    {
        restriction = OwlRlBatteryHelpers.Blank(dictionary, "witness-restriction");
        property = OwlRlBatteryHelpers.Mint(dictionary, "p");
        filler = OwlRlBatteryHelpers.Mint(dictionary, "c");
        instance = OwlRlBatteryHelpers.Mint(dictionary, "x");

        return
        [
            OwlRlBatteryHelpers.Triple(restriction, terms.OnProperty, property),
            OwlRlBatteryHelpers.Triple(restriction, terms.SomeValuesFrom, filler),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, restriction),
        ];
    }

    /// <summary>An encoded base chaining two distinct restrictions: the first restriction's filler subsumes under the second, so the first witness becomes the second's member.</summary>
    /// <param name="dictionary">The dictionary the terms mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="firstRestriction">The chain's first restriction node.</param>
    /// <param name="secondRestriction">The chain's second restriction node.</param>
    /// <param name="firstProperty">The first restriction's property.</param>
    /// <param name="secondProperty">The second restriction's property.</param>
    /// <param name="firstFiller">The first restriction's filler, subsumed under the second restriction.</param>
    /// <param name="secondFiller">The second restriction's filler.</param>
    /// <param name="instance">The member of the first restriction.</param>
    /// <returns>The encoded base triples.</returns>
    private static List<EncodedTriple> WitnessChainBase(
        TermDictionary dictionary, OwlRlTerms terms, out TermId firstRestriction, out TermId secondRestriction,
        out TermId firstProperty, out TermId secondProperty, out TermId firstFiller, out TermId secondFiller, out TermId instance)
    {
        firstRestriction = OwlRlBatteryHelpers.Blank(dictionary, "chain-first-restriction");
        secondRestriction = OwlRlBatteryHelpers.Blank(dictionary, "chain-second-restriction");
        firstProperty = OwlRlBatteryHelpers.Mint(dictionary, "p");
        secondProperty = OwlRlBatteryHelpers.Mint(dictionary, "q");
        firstFiller = OwlRlBatteryHelpers.Mint(dictionary, "C1");
        secondFiller = OwlRlBatteryHelpers.Mint(dictionary, "C2");
        instance = OwlRlBatteryHelpers.Mint(dictionary, "x");

        return
        [
            OwlRlBatteryHelpers.Triple(firstRestriction, terms.OnProperty, firstProperty),
            OwlRlBatteryHelpers.Triple(firstRestriction, terms.SomeValuesFrom, firstFiller),
            OwlRlBatteryHelpers.Triple(firstFiller, terms.SubClassOf, secondRestriction),
            OwlRlBatteryHelpers.Triple(secondRestriction, terms.OnProperty, secondProperty),
            OwlRlBatteryHelpers.Triple(secondRestriction, terms.SomeValuesFrom, secondFiller),
            OwlRlBatteryHelpers.Triple(instance, terms.Type, firstRestriction),
        ];
    }

    /// <summary>Appends the identifier of every blank node occurring in a graph, in either term position, to a set.</summary>
    /// <param name="graph">The graph to scan.</param>
    /// <param name="dictionary">The shared dictionary the identifiers are minted through.</param>
    /// <param name="idsToAppendTo">The identifier sink.</param>
    private static void AppendBlankIds(List<Quad> graph, TermDictionary dictionary, HashSet<TermId> idsToAppendTo)
    {
        foreach(Quad quad in graph)
        {
            if(quad.Subject is BlankNode subject)
            {
                idsToAppendTo.Add(dictionary.GetOrAdd((RdfTerm)subject));
            }

            if(quad.Object is BlankNode @object)
            {
                idsToAppendTo.Add(dictionary.GetOrAdd((RdfTerm)@object));
            }
        }
    }

    /// <summary>Checks one copied term position: a surviving blank node fails outright, and an engine-minted node must take an identifier no input blank holds.</summary>
    /// <param name="term">The copied term at a subject or object position.</param>
    /// <param name="dictionary">The shared dictionary the identifiers are minted through.</param>
    /// <param name="inputBlanks">The identifiers of the premise's and the conclusion's blank nodes.</param>
    /// <returns><c>1</c> when the position carries an engine-minted node, <c>0</c> otherwise.</returns>
    private static int CountMintedPosition(RdfTerm term, TermDictionary dictionary, HashSet<TermId> inputBlanks)
    {
        Assert.IsFalse(term is BlankNode, "No blank node of the conclusion's scaffold survives into the minted copies.");

        if(term is not EngineNode minted)
        {
            return 0;
        }

        Assert.DoesNotContain(dictionary.GetOrAdd((RdfTerm)minted), inputBlanks, "A minted scaffold node's identifier never coincides with a premise or conclusion blank's.");

        return 1;
    }

    /// <summary>Asserts the conclusion follows under the informative comprehension conditions with an empty remainder.</summary>
    /// <param name="premise">The premise graph.</param>
    /// <param name="conclusion">The conclusion graph.</param>
    private void AssertEntailed(List<Quad> premise, List<Quad> conclusion)
    {
        TermDictionary dictionary = new();
        bool entailed = OwlRlEntailment.TryEntail(
            premise, conclusion, dictionary, new OwlRlTerms(dictionary), out IReadOnlyList<Quad> unsettled,
            OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(entailed, $"The conclusion should follow; unsettled: {W3cOwl2RdfBasedTests.DescribeUnsettled(unsettled)}.");
        Assert.IsEmpty(unsettled);
    }

    /// <summary>Asserts the conclusion does not follow under the informative comprehension conditions — the leak gate.</summary>
    /// <param name="premise">The premise graph.</param>
    /// <param name="conclusion">The conclusion graph.</param>
    private void AssertNotEntailed(List<Quad> premise, List<Quad> conclusion)
    {
        TermDictionary dictionary = new();
        Assert.IsFalse(OwlRlEntailment.Entails(
            premise, conclusion, dictionary, new OwlRlTerms(dictionary),
            OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>The shared has-value collapse premise: one restriction node carrying a detail edge and two onProperty edges, with the second property's domain edge and functional typing parameterised — the corpus gadget and its near-misses.</summary>
    /// <param name="detailPredicate">The detail predicate on the node — <c>owl:hasValue</c> for the entailed shape, a substitute for a refusal row.</param>
    /// <param name="detailValue">The detail edge's object term.</param>
    /// <param name="withSecondDomain">Whether the second property carries its domain edge onto the node.</param>
    /// <param name="withSecondFunctional">Whether the second property is typed functional; otherwise it is a plain object property.</param>
    /// <returns>The premise graph.</returns>
    private static List<Quad> SharedHasValuePremise(string detailPredicate, RdfTerm detailValue, bool withSecondDomain, bool withSecondFunctional)
    {
        List<Quad> premise =
        [
            Q(Named("p"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")),
            Q(Named("p"), RdfsNs + "domain", Blank("d")),
            Q(Blank("d"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("d"), OwlNs + "onProperty", Named("p")),
            Q(Blank("d"), OwlNs + "onProperty", Named("q")),
            Q(Blank("d"), detailPredicate, detailValue),
            Q(Named("q"), RdfNs + "type", Iri(withSecondFunctional ? OwlNs + "FunctionalProperty" : OwlNs + "ObjectProperty"))
        ];

        if(withSecondDomain)
        {
            premise.Add(Q(Named("q"), RdfsNs + "domain", Blank("d")));
        }

        return premise;
    }

    /// <summary>The exact-cardinality shorthand premise: a named class stated as the intersection of a max-1 and a min-1 restriction on one property, with the member order and an optional disarming third member parameterised.</summary>
    /// <param name="minFirst">Whether the min-cardinality member leads the list.</param>
    /// <param name="withExtraMember">Whether a third member disarms the exact-pair shape.</param>
    /// <returns>The premise graph.</returns>
    private static List<Quad> CardinalityShorthandPremise(bool minFirst, bool withExtraMember)
    {
        List<Quad> premise =
        [
            Q(Blank("rmax"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("rmax"), OwlNs + "onProperty", Named("p")),
            Q(Blank("rmax"), OwlNs + "maxCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
            Q(Blank("rmin"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("rmin"), OwlNs + "onProperty", Named("p")),
            Q(Blank("rmin"), OwlNs + "minCardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];

        List<RdfTerm> members = minFirst ? [Blank("rmin"), Blank("rmax")] : [Blank("rmax"), Blank("rmin")];
        if(withExtraMember)
        {
            premise.Add(Q(Named("extra"), RdfNs + "type", Iri(OwlNs + "Class")));
            members.Add(Named("extra"));
        }

        RdfTerm head = AddList(premise, "c-members", [.. members]);
        premise.Add(Q(Named("c"), OwlNs + "intersectionOf", head));

        return premise;
    }

    /// <summary>The exact-cardinality shorthand conclusion: the same named class carrying the intersection over the singleton exact-cardinality list.</summary>
    /// <returns>The conclusion graph.</returns>
    private static List<Quad> CardinalityShorthandConclusion()
    {
        return
        [
            Q(Named("c"), OwlNs + "intersectionOf", Blank("l1")),
            Q(Blank("l1"), RdfNs + "first", Blank("r3")),
            Q(Blank("l1"), RdfNs + "rest", Iri(RdfNs + "nil")),
            Q(Blank("r3"), RdfNs + "type", Iri(OwlNs + "Restriction")),
            Q(Blank("r3"), OwlNs + "onProperty", Named("p")),
            Q(Blank("r3"), OwlNs + "cardinality", Bound("1", XsdNs + "nonNegativeInteger")),
        ];
    }

    /// <summary>The strict-subset De Morgan conclusion: an intersection of the complements of A, B, and C against a complement of the union of A and B, related by the given predicate.</summary>
    /// <param name="relation">The relating predicate — the subsumption that holds or the equivalence that must not.</param>
    /// <returns>The conclusion graph.</returns>
    private static List<Quad> DeMorganStrictSubsetConclusion(string relation)
    {
        List<Quad> conclusion =
        [
            Q(Blank("ca"), OwlNs + "complementOf", Named("A")),
            Q(Blank("cb"), OwlNs + "complementOf", Named("B")),
            Q(Blank("cc"), OwlNs + "complementOf", Named("C")),
            Q(Blank("i"), relation, Blank("cu")),
            Q(Blank("cu"), OwlNs + "complementOf", Blank("u2")),
        ];
        RdfTerm intersectionHead = AddList(conclusion, "i-members", [Blank("ca"), Blank("cb"), Blank("cc")]);
        conclusion.Add(Q(Blank("i"), OwlNs + "intersectionOf", intersectionHead));
        RdfTerm unionHead = AddList(conclusion, "u2-members", [Named("A"), Named("B")]);
        conclusion.Add(Q(Blank("u2"), OwlNs + "unionOf", unionHead));

        return conclusion;
    }

    /// <summary>An encoded base carrying the family's trigger shapes — a max-1 restriction on a functional property and an empty enumeration — plus the excluded-middle, dichotomy, range-intersection, De Morgan, and shorthand shapes for the agreement row.</summary>
    /// <param name="dictionary">The dictionary the terms mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="restriction">The max-1 restriction node the family would place owl:Thing under.</param>
    /// <param name="enumeration">The empty-enumeration node the family would place under owl:Nothing.</param>
    /// <returns>The encoded base triples.</returns>
    private static List<EncodedTriple> ComprehensionShapedBase(TermDictionary dictionary, OwlRlTerms terms, out TermId restriction, out TermId enumeration)
    {
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");
        TermId zero = OwlRlBatteryHelpers.Literal(dictionary, "0", "http://www.w3.org/2001/XMLSchema#nonNegativeInteger");
        restriction = OwlRlBatteryHelpers.Blank(dictionary, "max-one");
        enumeration = OwlRlBatteryHelpers.Blank(dictionary, "empty-enumeration");

        List<EncodedTriple> triples =
        [
            OwlRlBatteryHelpers.Triple(p, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(restriction, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(restriction, terms.MaxCardinality, one),
            OwlRlBatteryHelpers.Triple(enumeration, terms.OneOf, terms.Nil),
        ];

        //The excluded-middle union of a class and its complement.
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");
        TermId complement = OwlRlBatteryHelpers.Blank(dictionary, "complement-of-c");
        triples.Add(OwlRlBatteryHelpers.Triple(complement, terms.ComplementOf, c));
        TermId unionClass = OwlRlBatteryHelpers.Blank(dictionary, "excluded-middle-union");
        TermId unionHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [c, complement], "excluded-middle");
        triples.Add(OwlRlBatteryHelpers.Triple(unionClass, terms.UnionOf, unionHead));

        //The value-dichotomy union on one property.
        TermId some = OwlRlBatteryHelpers.Blank(dictionary, "some-values");
        TermId capped = OwlRlBatteryHelpers.Blank(dictionary, "max-zero");
        triples.Add(OwlRlBatteryHelpers.Triple(some, terms.OnProperty, p));
        triples.Add(OwlRlBatteryHelpers.Triple(some, terms.SomeValuesFrom, terms.Thing));
        triples.Add(OwlRlBatteryHelpers.Triple(capped, terms.OnProperty, p));
        triples.Add(OwlRlBatteryHelpers.Triple(capped, terms.MaxCardinality, zero));
        TermId dichotomy = OwlRlBatteryHelpers.Blank(dictionary, "dichotomy-union");
        TermId dichotomyHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [some, capped], "dichotomy");
        triples.Add(OwlRlBatteryHelpers.Triple(dichotomy, terms.UnionOf, dichotomyHead));

        //The range-completion intersection under two ranges.
        TermId ranged = OwlRlBatteryHelpers.Mint(dictionary, "ranged");
        TermId first = OwlRlBatteryHelpers.Mint(dictionary, "FirstRange");
        TermId second = OwlRlBatteryHelpers.Mint(dictionary, "SecondRange");
        triples.Add(OwlRlBatteryHelpers.Triple(ranged, terms.Range, first));
        triples.Add(OwlRlBatteryHelpers.Triple(ranged, terms.Range, second));
        TermId rangeIntersection = OwlRlBatteryHelpers.Blank(dictionary, "range-intersection");
        TermId rangeHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [first, second], "range-intersection");
        triples.Add(OwlRlBatteryHelpers.Triple(rangeIntersection, terms.IntersectionOf, rangeHead));

        //The De Morgan pair over the two ranged classes.
        TermId firstComplement = OwlRlBatteryHelpers.Blank(dictionary, "complement-of-first");
        TermId secondComplement = OwlRlBatteryHelpers.Blank(dictionary, "complement-of-second");
        triples.Add(OwlRlBatteryHelpers.Triple(firstComplement, terms.ComplementOf, first));
        triples.Add(OwlRlBatteryHelpers.Triple(secondComplement, terms.ComplementOf, second));
        TermId deMorganIntersection = OwlRlBatteryHelpers.Blank(dictionary, "de-morgan-intersection");
        TermId deMorganHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [firstComplement, secondComplement], "de-morgan-intersection");
        triples.Add(OwlRlBatteryHelpers.Triple(deMorganIntersection, terms.IntersectionOf, deMorganHead));
        TermId complementOfUnion = OwlRlBatteryHelpers.Blank(dictionary, "complement-of-union");
        TermId dualUnion = OwlRlBatteryHelpers.Blank(dictionary, "de-morgan-union");
        TermId dualHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [first, second], "de-morgan-union");
        triples.Add(OwlRlBatteryHelpers.Triple(dualUnion, terms.UnionOf, dualHead));
        triples.Add(OwlRlBatteryHelpers.Triple(complementOfUnion, terms.ComplementOf, dualUnion));

        //The exact-cardinality shorthand pair and its singleton list.
        TermId shorthand = OwlRlBatteryHelpers.Mint(dictionary, "shorthand");
        TermId cappedOne = OwlRlBatteryHelpers.Blank(dictionary, "shorthand-max");
        TermId flooredOne = OwlRlBatteryHelpers.Blank(dictionary, "shorthand-min");
        triples.Add(OwlRlBatteryHelpers.Triple(cappedOne, terms.OnProperty, p));
        triples.Add(OwlRlBatteryHelpers.Triple(cappedOne, terms.MaxCardinality, one));
        triples.Add(OwlRlBatteryHelpers.Triple(flooredOne, terms.OnProperty, p));
        triples.Add(OwlRlBatteryHelpers.Triple(flooredOne, terms.MinCardinality, one));
        TermId shorthandHead = OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [cappedOne, flooredOne], "shorthand-pair");
        triples.Add(OwlRlBatteryHelpers.Triple(shorthand, terms.IntersectionOf, shorthandHead));
        TermId exact = OwlRlBatteryHelpers.Blank(dictionary, "exact-one");
        triples.Add(OwlRlBatteryHelpers.Triple(exact, terms.OnProperty, p));
        triples.Add(OwlRlBatteryHelpers.Triple(exact, terms.Cardinality, one));
        OwlRlBatteryHelpers.AddList(triples, dictionary, terms, [exact], "exact-singleton");

        return triples;
    }

    /// <summary>The corpus fibre gadget settles end to end: the read-back emits the exact product pin, and the traced emission's premise set is exactly the matched antecedents — the enumeration, the six equivalences with their details, the two input pins, the two consumed inverse edges, and the middle property's functionality alone; the class typings and the outer properties' functionality never enter.</summary>
    [TestMethod]
    public void FibreGadgetSettlesTheCorpusShapeWithExactTracedPremises()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions());
        AssertEntailed(premise, FibreGadgetConclusion("345", XsdNs + "int"));

        TermDictionary dictionary = new();
        OwlRlResult result = ComputeTraced(premise, dictionary, out List<InferenceTraceEvent> events);

        Assert.IsTrue(result.IsConsistent);
        List<InferenceTraceEvent> emissions = FibreEmissions(events);
        Assert.HasCount(1, emissions, "The gadget carries exactly one read-back emission; the input-bound read-backs rederive their own pins, which deduplicate silently.");
        TermId product = OwlRlBatteryHelpers.Mint(dictionary, "boundNM");
        TermId minted = OwlRlBatteryHelpers.Literal(dictionary, "345", XsdNs + "int");
        OwlRlTerms terms = new(dictionary);
        Assert.AreEqual(OwlRlBatteryHelpers.Triple(product, terms.SameAs, minted), emissions[0].Conclusion);
        HashSet<EncodedTriple> expected = ExpectedFibreGadgetPremises(dictionary, terms);
        HashSet<EncodedTriple> traced = [.. emissions[0].Premises];
        Assert.IsTrue(traced.SetEquals(expected), "The traced premise set is exactly the matched antecedents.");
    }

    /// <summary>The middle property's functionality removed refuses the fibre product — without disjoint fibres the product over-counts, so the fence is load-bearing.</summary>
    [TestMethod]
    public void ANonFunctionalMiddlePropertyRefusesTheFibreProduct()
    {
        AssertNotEntailed(FibreGadgetPremise(new FibreGadgetOptions { SecondFunctional = false }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>A some-values-from filler other than the certified class refuses the count — the filler read is exact-term, never up-to-equivalence.</summary>
    [TestMethod]
    public void AForeignSomeValuesFillerRefusesTheFibreCount()
    {
        AssertNotEntailed(FibreGadgetPremise(new FibreGadgetOptions { FirstStageFiller = "other-class" }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>A two-member enumeration seeds no anchor certificate, so the whole chain stays dark.</summary>
    [TestMethod]
    public void ATwoMemberEnumerationSeedsNoAnchor()
    {
        AssertNotEntailed(FibreGadgetPremise(new FibreGadgetOptions { AnchorArity = 2 }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>The first stage's inverseOf edge removed refuses the count — without the inverse the cardinality bound counts the wrong direction.</summary>
    [TestMethod]
    public void AMissingInverseEdgeRefusesTheFibreCount()
    {
        AssertNotEntailed(FibreGadgetPremise(new FibreGadgetOptions { WithFirstInverse = false }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>Float-pinned bounds refuse the value read — the float, double, and exact value spaces are pairwise disjoint, so a float pin never denotes a nonnegative integer.</summary>
    [TestMethod]
    public void FloatPinnedBoundsRefuseTheFibreRead()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions { FirstPinDatatype = XsdNs + "float", SecondPinDatatype = XsdNs + "float" });

        AssertNotEntailed(premise, FibreGadgetConclusion("345", XsdNs + "float"));
        AssertNotEntailed(premise, FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>Pins mixing xsd:int and xsd:integer read their values but refuse the read-back mint — the emission datatype is the single datatype every contributing pin carried.</summary>
    [TestMethod]
    public void MixedPinDatatypesRefuseTheReadBackMint()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions { SecondPinDatatype = XsdNs + "integer" });

        AssertNotEntailed(premise, FibreGadgetConclusion("345", XsdNs + "int"));
        AssertNotEntailed(premise, FibreGadgetConclusion("345", XsdNs + "integer"));
    }

    /// <summary>A product overflowing the checked long arithmetic refuses the certificate, so the analyzer emits nothing.</summary>
    [TestMethod]
    public void AnOverflowingFibreProductRefuses()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions
        {
            FirstPin = "9223372036854775807",
            FirstPinDatatype = XsdNs + "long",
            SecondPin = "3",
            SecondPinDatatype = XsdNs + "long"
        });

        OwlRlResult result = ComputeTraced(premise, new TermDictionary(), out List<InferenceTraceEvent> events);

        Assert.IsTrue(result.IsConsistent);
        Assert.IsEmpty(FibreEmissions(events));
    }

    /// <summary>Byte pins whose product escapes the byte value space refuse the mint — a literal outside its datatype's space is never minted.</summary>
    [TestMethod]
    public void BytePinsWithAnOutOfRangeProductRefuseTheMint()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions { FirstPinDatatype = XsdNs + "byte", SecondPinDatatype = XsdNs + "byte" });

        OwlRlResult result = ComputeTraced(premise, new TermDictionary(), out List<InferenceTraceEvent> events);

        Assert.IsTrue(result.IsConsistent);
        Assert.IsEmpty(FibreEmissions(events));
    }

    /// <summary>A bound pinned to two distinct values refuses the value read whole, and the contradiction stays the equality machinery's: the analyzer emits nothing while the composed sameAs closure finds the dt-diff clash.</summary>
    [TestMethod]
    public void ADoublyPinnedBoundRefusesTheReadAndTheClashStaysWithTheEqualityMachinery()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions { ExtraFirstPin = "16" });

        OwlRlResult result = ComputeTraced(premise, new TermDictionary(), out List<InferenceTraceEvent> events);

        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDiff, result.InconsistencyRule);
        Assert.IsEmpty(FibreEmissions(events));
    }

    /// <summary>rdfs:subClassOf in place of the equivalence refuses the count — containment does not transfer extension identity.</summary>
    [TestMethod]
    public void SubClassOfInPlaceOfTheEquivalenceRefusesTheCount()
    {
        AssertNotEntailed(FibreGadgetPremise(new FibreGadgetOptions { AnchorDetailEquivalencePredicate = RdfsNs + "subClassOf" }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>Under the default reading the fibre gadget stays dark — the certificates ride only the informative comprehension conditions.</summary>
    [TestMethod]
    public void TheDefaultReadingLeavesTheFibreGadgetDark()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> encoded = Encode(FibreGadgetPremise(new FibreGadgetOptions()), dictionary);

        OwlRlResult result = OwlRlClosure.Compute(encoded, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(result.IsConsistent);
        TermId product = OwlRlBatteryHelpers.Mint(dictionary, "boundNM");
        TermId minted = OwlRlBatteryHelpers.Literal(dictionary, "345", XsdNs + "int");
        Assert.DoesNotContain(OwlRlBatteryHelpers.Triple(product, terms.SameAs, minted), result.Derived);
    }

    /// <summary>An extra detail beside the read pair on a restriction node still emits — the semantic conditions hold per (onProperty, detail) pair, so extra details never cross-join.</summary>
    [TestMethod]
    public void AMultiDetailRestrictionNodeStillEmits()
    {
        AssertEntailed(FibreGadgetPremise(new FibreGadgetOptions { WithExtraDetail = true }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>Every equivalence asserted with the restriction node as subject still emits — the equivalence condition is symmetric, so neither orientation waits on materialised symmetry.</summary>
    [TestMethod]
    public void ReversedEquivalenceOrientationsStillEmit()
    {
        AssertEntailed(FibreGadgetPremise(new FibreGadgetOptions { ReversedEquivalences = true }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>A three-stage chain composes the product through a second functional hop.</summary>
    [TestMethod]
    public void AThreeStageChainComposesTheProduct()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions());
        AddFibreProperty(premise, "s", "invS", functional: true, withInverse: true, reversedInverse: false, chainedInverse: false);
        premise.Add(Q(Named("boundO"), OwlNs + "sameAs", Bound("2", XsdNs + "int")));
        AddCardinalityEquivalence(premise, "cardinality-nm", "z7", "invS", Named("boundO"), reversed: false, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "cardinality-nmo", "z8", "s", "cardinality-nm", reversed: false);
        AddFibreProperty(premise, "t", "invT", functional: true, withInverse: true, reversedInverse: false, chainedInverse: false);
        AddCardinalityEquivalence(premise, "only-d", "z9", "invT", Named("boundNMO"), reversed: false, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "cardinality-nmo", "z10", "t", "only-d", reversed: false);

        AssertEntailed(premise, [Q(Named("boundNMO"), OwlNs + "sameAs", Bound("690", XsdNs + "int"))]);
    }

    /// <summary>Direct-literal cardinality bounds with no IRI indirection still emit — a literal bound is the same lexical-to-value read as a pinned one.</summary>
    [TestMethod]
    public void DirectLiteralBoundsStillEmit()
    {
        AssertEntailed(FibreGadgetPremise(new FibreGadgetOptions { DirectLiteralBounds = true }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>The semi-naive and naive evaluations agree on the fibre gadget, and both derive the product pin.</summary>
    [TestMethod]
    public void TheSemiNaiveAndNaiveClosuresAgreeOnTheFibreGadget()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        List<EncodedTriple> encoded = Encode(FibreGadgetPremise(new FibreGadgetOptions()), dictionary);

        OwlRlResult semiNaive = OwlRlClosure.Compute(
            encoded, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        OwlRlResult naive = OwlRlClosure.ComputeNaive(
            encoded, terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(naive.IsConsistent, semiNaive.IsConsistent);
        HashSet<EncodedTriple> semiNaiveSet = [.. semiNaive.Derived];
        HashSet<EncodedTriple> naiveSet = [.. naive.Derived];
        Assert.IsTrue(semiNaiveSet.SetEquals(naiveSet));
        TermId product = OwlRlBatteryHelpers.Mint(dictionary, "boundNM");
        TermId minted = OwlRlBatteryHelpers.Literal(dictionary, "345", XsdNs + "int");
        Assert.Contains(OwlRlBatteryHelpers.Triple(product, terms.SameAs, minted), semiNaiveSet);
    }

    /// <summary>The anchor's cardinality equivalence closing only through a subclass-transitivity hop and scm-eqc2 still emits — the equivalence lands on a round whose delta holds no other family trigger, so the equivalent-class delta key alone re-fires the certificates.</summary>
    [TestMethod]
    public void ALateDerivedEquivalenceStillFiresTheCertificates()
    {
        AssertEntailed(FibreGadgetPremise(new FibreGadgetOptions { DerivedAnchorDetailEquivalence = true }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>The first stage's inverseOf edge reachable only through a subproperty chain onto owl:inverseOf still emits — the edge lands on a round whose delta holds no other family trigger, so the inverse-of delta key alone re-fires the certificates.</summary>
    [TestMethod]
    public void ALateDerivedInverseEdgeStillFiresTheCertificates()
    {
        AssertEntailed(FibreGadgetPremise(new FibreGadgetOptions { ChainedFirstInverse = true }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>The backward shape with only the product pinned derives nothing new: the read-back's sole candidate is the premise's own pin, which deduplicates to silence — the mechanism composes known factors forward and never runs from a pinned product back to the factors.</summary>
    [TestMethod]
    public void TheBackwardPinnedProductShapeStaysSilent()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions { WithPins = false, ProductPin = "77" });

        OwlRlResult result = ComputeTraced(premise, new TermDictionary(), out List<InferenceTraceEvent> events);

        Assert.IsTrue(result.IsConsistent);
        Assert.IsEmpty(FibreEmissions(events));
        AssertNotEntailed(premise, [Q(Named("boundN"), OwlNs + "sameAs", Bound("7", XsdNs + "int"))]);
    }

    /// <summary>Every inverseOf edge asserted with the inverse property as subject still emits — the inverse condition is symmetric in its pair.</summary>
    [TestMethod]
    public void ReversedInverseOrientationsStillEmit()
    {
        AssertEntailed(FibreGadgetPremise(new FibreGadgetOptions { ReversedInverses = true }), FibreGadgetConclusion("345", XsdNs + "int"));
    }

    /// <summary>Two independent chains proving one class at different counts keep the first count: the family completes without fault, and the read-back emits the first chain's value alone — determinism rides the indexes' insertion order.</summary>
    [TestMethod]
    public void TwoChainsCertifyingOneClassKeepTheFirstCount()
    {
        List<Quad> premise = [];
        RdfTerm head = AddList(premise, "a-members", [Named("d")]);
        premise.Add(Q(Named("a"), OwlNs + "oneOf", head));
        premise.Add(Q(Named("p1"), OwlNs + "inverseOf", Named("invP1")));
        AddCardinalityEquivalence(premise, "a", "z1a", "invP1", Named("k1"), reversed: false, OwlNs + "equivalentClass", derived: false);
        premise.Add(Q(Named("k1"), OwlNs + "sameAs", Bound("15", XsdNs + "int")));
        AddSomeValuesEquivalence(premise, "b", "z3a", "p1", "a", reversed: false);
        premise.Add(Q(Named("p2"), OwlNs + "inverseOf", Named("invP2")));
        AddCardinalityEquivalence(premise, "a", "z1b", "invP2", Bound("16", XsdNs + "int"), reversed: false, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "b", "z3b", "p2", "a", reversed: false);
        premise.Add(Q(Named("r"), OwlNs + "inverseOf", Named("invR")));
        AddCardinalityEquivalence(premise, "a", "z2", "invR", Named("k3"), reversed: false, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "b", "z6", "r", "a", reversed: false);

        TermDictionary dictionary = new();
        OwlRlResult result = ComputeTraced(premise, dictionary, out List<InferenceTraceEvent> events);

        Assert.IsTrue(result.IsConsistent);
        List<InferenceTraceEvent> emissions = FibreEmissions(events);
        Assert.HasCount(1, emissions, "The second chain's direct-literal bound needs no read-back, so the sole emission is the read-back bound's pin.");
        OwlRlTerms terms = new(dictionary);
        TermId bound = OwlRlBatteryHelpers.Mint(dictionary, "k3");
        TermId minted = OwlRlBatteryHelpers.Literal(dictionary, "15", XsdNs + "int");
        Assert.AreEqual(OwlRlBatteryHelpers.Triple(bound, terms.SameAs, minted), emissions[0].Conclusion);
    }

    /// <summary>A read-back onto a bound already pinned to a conflicting value emits regardless, and the shared calculus composes the clash: the emitted pin and the asserted pin meet through equality composition in the dt-diff falsity — conflicting pins stay the datatype machinery's jurisdiction.</summary>
    [TestMethod]
    public void AConflictingProductPinComposesTheDatatypeClash()
    {
        List<Quad> premise = FibreGadgetPremise(new FibreGadgetOptions { ProductPin = "-345" });

        TermDictionary dictionary = new();
        OwlRlResult result = ComputeTraced(premise, dictionary, out List<InferenceTraceEvent> events);

        OwlRlTerms terms = new(dictionary);
        TermId product = OwlRlBatteryHelpers.Mint(dictionary, "boundNM");
        TermId minted = OwlRlBatteryHelpers.Literal(dictionary, "345", XsdNs + "int");
        EncodedTriple pin = OwlRlBatteryHelpers.Triple(product, terms.SameAs, minted);
        bool pinEmitted = false;
        foreach(InferenceTraceEvent emission in FibreEmissions(events))
        {
            if(emission.Conclusion == pin)
            {
                pinEmitted = true;
            }
        }

        Assert.IsTrue(pinEmitted, "The negative pin refuses the value read, so the forward chain emits its own product pin; equality rewriting may re-emit onto respelled bounds before the clash lands, and every such emission is vacuously sound on the inconsistent premise.");
        Assert.IsFalse(result.IsConsistent);
        Assert.AreEqual(EntailmentRules.DtDiff, result.InconsistencyRule);
    }

    /// <summary>A mutually dependent certificate premise — each class's count reachable only through the other, with no enumeration-grounded chain — stays silent and terminates: certificates attach to existing terms only and the first certificate wins, so the worklist drains over the finite roster.</summary>
    [TestMethod]
    public void AMutuallyDependentCertificatePremiseStaysSilentAndTerminates()
    {
        List<Quad> premise = [];
        premise.Add(Q(Named("p"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")));
        premise.Add(Q(Named("q"), RdfNs + "type", Iri(OwlNs + "FunctionalProperty")));
        premise.Add(Q(Named("p"), OwlNs + "inverseOf", Named("invP")));
        premise.Add(Q(Named("q"), OwlNs + "inverseOf", Named("invQ")));
        AddCardinalityEquivalence(premise, "b", "z1", "invQ", Named("k1"), reversed: false, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "c", "z2", "q", "b", reversed: false);
        premise.Add(Q(Named("k1"), OwlNs + "sameAs", Bound("15", XsdNs + "int")));
        AddCardinalityEquivalence(premise, "c", "z3", "invP", Named("k2"), reversed: false, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "b", "z4", "p", "c", reversed: false);
        premise.Add(Q(Named("k2"), OwlNs + "sameAs", Bound("23", XsdNs + "int")));

        OwlRlResult result = ComputeTraced(premise, new TermDictionary(), out List<InferenceTraceEvent> events);

        Assert.IsTrue(result.IsConsistent);
        Assert.IsEmpty(FibreEmissions(events));
    }

    /// <summary>The deviation knobs of the fibre gadget's premise builder — the corpus shape with every default, one named deviation per refusal or orientation row.</summary>
    private sealed class FibreGadgetOptions
    {
        /// <summary>The anchor enumeration's member count.</summary>
        public int AnchorArity { get; init; } = 1;

        /// <summary>The first input pin's lexical form.</summary>
        public string FirstPin { get; init; } = "15";

        /// <summary>The first input pin's datatype IRI.</summary>
        public string FirstPinDatatype { get; init; } = XsdNs + "int";

        /// <summary>The second input pin's lexical form.</summary>
        public string SecondPin { get; init; } = "23";

        /// <summary>The second input pin's datatype IRI.</summary>
        public string SecondPinDatatype { get; init; } = XsdNs + "int";

        /// <summary>Whether the input bounds are literals in place, with no IRI indirection and no pins.</summary>
        public bool DirectLiteralBounds { get; init; }

        /// <summary>Whether the input pins are asserted at all.</summary>
        public bool WithPins { get; init; } = true;

        /// <summary>An extra conflicting pin lexical on the first bound, or <c>null</c> for none.</summary>
        public string? ExtraFirstPin { get; init; }

        /// <summary>A pin lexical on the product bound, or <c>null</c> for none.</summary>
        public string? ProductPin { get; init; }

        /// <summary>Whether the middle property carries its functional typing.</summary>
        public bool SecondFunctional { get; init; } = true;

        /// <summary>Whether the first property's inverseOf edge is asserted.</summary>
        public bool WithFirstInverse { get; init; } = true;

        /// <summary>The first stage's some-values-from filler, the anchor by default.</summary>
        public string FirstStageFiller { get; init; } = "only-d";

        /// <summary>Whether every equivalence is asserted with the restriction node as subject.</summary>
        public bool ReversedEquivalences { get; init; }

        /// <summary>Whether every inverseOf edge is asserted with the inverse property as subject.</summary>
        public bool ReversedInverses { get; init; }

        /// <summary>The predicate of the anchor's first cardinality equivalence, <c>owl:equivalentClass</c> by default.</summary>
        public string AnchorDetailEquivalencePredicate { get; init; } = OwlNs + "equivalentClass";

        /// <summary>Whether the anchor's first cardinality equivalence closes only through a subclass-transitivity hop and scm-eqc2, landing the edge on a late, otherwise-triggerless round.</summary>
        public bool DerivedAnchorDetailEquivalence { get; init; }

        /// <summary>Whether the first property's inverseOf edge arrives only through a subproperty chain onto <c>owl:inverseOf</c>, landing the edge on a late, otherwise-triggerless round.</summary>
        public bool ChainedFirstInverse { get; init; }

        /// <summary>Whether an extra harmless detail rides the anchor's first cardinality restriction node.</summary>
        public bool WithExtraDetail { get; init; }
    }

    /// <summary>The corpus fibre gadget's premise: three properties with inverses, the singleton anchor enumeration, the anchor's two inverse-cardinality equivalences, the two chained fibre classes, and the two input pins — shaped by the options' deviation knobs.</summary>
    /// <param name="options">The deviation knobs.</param>
    /// <returns>The premise graph.</returns>
    private static List<Quad> FibreGadgetPremise(FibreGadgetOptions options)
    {
        List<Quad> premise = [];
        AddFibreProperty(premise, "p", "invP", functional: true, options.WithFirstInverse, options.ReversedInverses, options.ChainedFirstInverse);
        AddFibreProperty(premise, "q", "invQ", options.SecondFunctional, withInverse: true, options.ReversedInverses, chainedInverse: false);
        AddFibreProperty(premise, "r", "invR", functional: true, withInverse: true, options.ReversedInverses, chainedInverse: false);
        premise.Add(Q(Named("only-d"), RdfNs + "type", Iri(OwlNs + "Class")));
        premise.Add(Q(Named("cardinality-n"), RdfNs + "type", Iri(OwlNs + "Class")));
        premise.Add(Q(Named("cardinality-nm"), RdfNs + "type", Iri(OwlNs + "Class")));

        List<RdfTerm> members = [Named("d")];
        for(int i = 1; i < options.AnchorArity; i++)
        {
            members.Add(Named($"d{i}"));
        }

        RdfTerm head = AddList(premise, "only-d-members", [.. members]);
        premise.Add(Q(Named("only-d"), OwlNs + "oneOf", head));

        RdfTerm firstBound = options.DirectLiteralBounds ? Bound(options.FirstPin, options.FirstPinDatatype) : Named("boundN");
        RdfTerm secondBound = options.DirectLiteralBounds ? Bound(options.SecondPin, options.SecondPinDatatype) : Named("boundM");
        if(!options.DirectLiteralBounds && options.WithPins)
        {
            premise.Add(Q(Named("boundN"), OwlNs + "sameAs", Bound(options.FirstPin, options.FirstPinDatatype)));
            premise.Add(Q(Named("boundM"), OwlNs + "sameAs", Bound(options.SecondPin, options.SecondPinDatatype)));
            if(options.ExtraFirstPin is string extraPin)
            {
                premise.Add(Q(Named("boundN"), OwlNs + "sameAs", Bound(extraPin, options.FirstPinDatatype)));
            }
        }

        if(options.ProductPin is string productPin)
        {
            premise.Add(Q(Named("boundNM"), OwlNs + "sameAs", Bound(productPin, XsdNs + "int")));
        }

        AddCardinalityEquivalence(premise, "only-d", "z1", "invP", firstBound, options.ReversedEquivalences, options.AnchorDetailEquivalencePredicate, options.DerivedAnchorDetailEquivalence);
        AddCardinalityEquivalence(premise, "only-d", "z2", "invR", Named("boundNM"), options.ReversedEquivalences, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "cardinality-n", "z3", "p", options.FirstStageFiller, options.ReversedEquivalences);
        AddCardinalityEquivalence(premise, "cardinality-n", "z4", "invQ", secondBound, options.ReversedEquivalences, OwlNs + "equivalentClass", derived: false);
        AddSomeValuesEquivalence(premise, "cardinality-nm", "z5", "q", "cardinality-n", options.ReversedEquivalences);
        AddSomeValuesEquivalence(premise, "cardinality-nm", "z6", "r", "only-d", options.ReversedEquivalences);

        if(options.WithExtraDetail)
        {
            premise.Add(Q(Blank("z1"), OwlNs + "hasValue", Named("d")));
        }

        return premise;
    }

    /// <summary>The corpus conclusion: the product bound pinned to the given literal.</summary>
    /// <param name="lexical">The pinned lexical form.</param>
    /// <param name="datatype">The pinned datatype IRI.</param>
    /// <returns>The conclusion graph.</returns>
    private static List<Quad> FibreGadgetConclusion(string lexical, string datatype)
    {
        return [Q(Named("boundNM"), OwlNs + "sameAs", Bound(lexical, datatype))];
    }

    /// <summary>The exact antecedent set the corpus gadget's read-back matches, minted through the shared dictionary.</summary>
    /// <param name="dictionary">The dictionary the encoded premise interned through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <returns>The expected premise set.</returns>
    private static HashSet<EncodedTriple> ExpectedFibreGadgetPremises(TermDictionary dictionary, OwlRlTerms terms)
    {
        TermId onlyD = OwlRlBatteryHelpers.Mint(dictionary, "only-d");
        TermId cardinalityN = OwlRlBatteryHelpers.Mint(dictionary, "cardinality-n");
        TermId cardinalityNm = OwlRlBatteryHelpers.Mint(dictionary, "cardinality-nm");
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId q = OwlRlBatteryHelpers.Mint(dictionary, "q");
        TermId r = OwlRlBatteryHelpers.Mint(dictionary, "r");
        TermId invP = OwlRlBatteryHelpers.Mint(dictionary, "invP");
        TermId invQ = OwlRlBatteryHelpers.Mint(dictionary, "invQ");
        TermId invR = OwlRlBatteryHelpers.Mint(dictionary, "invR");
        TermId boundN = OwlRlBatteryHelpers.Mint(dictionary, "boundN");
        TermId boundM = OwlRlBatteryHelpers.Mint(dictionary, "boundM");
        TermId boundNm = OwlRlBatteryHelpers.Mint(dictionary, "boundNM");
        TermId cell = OwlRlBatteryHelpers.Blank(dictionary, "only-d-members-0");
        TermId z1 = OwlRlBatteryHelpers.Blank(dictionary, "z1");
        TermId z2 = OwlRlBatteryHelpers.Blank(dictionary, "z2");
        TermId z3 = OwlRlBatteryHelpers.Blank(dictionary, "z3");
        TermId z4 = OwlRlBatteryHelpers.Blank(dictionary, "z4");
        TermId z5 = OwlRlBatteryHelpers.Blank(dictionary, "z5");
        TermId z6 = OwlRlBatteryHelpers.Blank(dictionary, "z6");
        TermId first = OwlRlBatteryHelpers.Literal(dictionary, "15", XsdNs + "int");
        TermId second = OwlRlBatteryHelpers.Literal(dictionary, "23", XsdNs + "int");

        return
        [
            OwlRlBatteryHelpers.Triple(onlyD, terms.OneOf, cell),
            OwlRlBatteryHelpers.Triple(onlyD, terms.EquivalentClass, z1),
            OwlRlBatteryHelpers.Triple(z1, terms.OnProperty, invP),
            OwlRlBatteryHelpers.Triple(z1, terms.Cardinality, boundN),
            OwlRlBatteryHelpers.Triple(boundN, terms.SameAs, first),
            OwlRlBatteryHelpers.Triple(z3, terms.SomeValuesFrom, onlyD),
            OwlRlBatteryHelpers.Triple(z3, terms.OnProperty, p),
            OwlRlBatteryHelpers.Triple(p, terms.InverseOf, invP),
            OwlRlBatteryHelpers.Triple(cardinalityN, terms.EquivalentClass, z3),
            OwlRlBatteryHelpers.Triple(cardinalityN, terms.EquivalentClass, z4),
            OwlRlBatteryHelpers.Triple(z4, terms.OnProperty, invQ),
            OwlRlBatteryHelpers.Triple(z4, terms.Cardinality, boundM),
            OwlRlBatteryHelpers.Triple(boundM, terms.SameAs, second),
            OwlRlBatteryHelpers.Triple(z5, terms.SomeValuesFrom, cardinalityN),
            OwlRlBatteryHelpers.Triple(z5, terms.OnProperty, q),
            OwlRlBatteryHelpers.Triple(q, terms.InverseOf, invQ),
            OwlRlBatteryHelpers.Triple(q, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(cardinalityNm, terms.EquivalentClass, z5),
            OwlRlBatteryHelpers.Triple(onlyD, terms.EquivalentClass, z2),
            OwlRlBatteryHelpers.Triple(z2, terms.OnProperty, invR),
            OwlRlBatteryHelpers.Triple(z2, terms.Cardinality, boundNm),
            OwlRlBatteryHelpers.Triple(z6, terms.SomeValuesFrom, onlyD),
            OwlRlBatteryHelpers.Triple(z6, terms.OnProperty, r),
            OwlRlBatteryHelpers.Triple(r, terms.InverseOf, invR),
            OwlRlBatteryHelpers.Triple(cardinalityNm, terms.EquivalentClass, z6)
        ];
    }

    /// <summary>Adds one fibre property: its functional or plain typing, its object-property-typed inverse, and the inverseOf edge in the asked orientation — direct, reversed, or through a subproperty chain the schema rules close on a later round.</summary>
    /// <param name="premiseToAppendTo">The premise graph.</param>
    /// <param name="property">The property's local name.</param>
    /// <param name="inverse">The inverse property's local name.</param>
    /// <param name="functional">Whether the property is typed functional; a plain object property otherwise.</param>
    /// <param name="withInverse">Whether any inverse edge is asserted.</param>
    /// <param name="reversedInverse">Whether the inverse edge is asserted with the inverse property as subject.</param>
    /// <param name="chainedInverse">Whether the inverse edge arrives only through a two-step subproperty chain onto <c>owl:inverseOf</c>.</param>
    private static void AddFibreProperty(List<Quad> premiseToAppendTo, string property, string inverse, bool functional, bool withInverse, bool reversedInverse, bool chainedInverse)
    {
        premiseToAppendTo.Add(Q(Named(property), RdfNs + "type", functional ? Iri(OwlNs + "FunctionalProperty") : Iri(OwlNs + "ObjectProperty")));
        premiseToAppendTo.Add(Q(Named(inverse), RdfNs + "type", Iri(OwlNs + "ObjectProperty")));
        if(!withInverse)
        {
            return;
        }

        if(chainedInverse)
        {
            premiseToAppendTo.Add(Q(Named(property + "-via"), RdfsNs + "subPropertyOf", Named(property + "-carrier")));
            premiseToAppendTo.Add(Q(Named(property + "-carrier"), RdfsNs + "subPropertyOf", Iri(OwlNs + "inverseOf")));
            premiseToAppendTo.Add(Q(Named(property), Example + property + "-via", Named(inverse)));

            return;
        }

        if(reversedInverse)
        {
            premiseToAppendTo.Add(Q(Named(inverse), OwlNs + "inverseOf", Named(property)));

            return;
        }

        premiseToAppendTo.Add(Q(Named(property), OwlNs + "inverseOf", Named(inverse)));
    }

    /// <summary>Adds one restriction node carrying onProperty and owl:cardinality details, related to a named class by the asked equivalence shape.</summary>
    /// <param name="premiseToAppendTo">The premise graph.</param>
    /// <param name="className">The related class's local name.</param>
    /// <param name="node">The restriction node's blank label.</param>
    /// <param name="onProperty">The onProperty target's local name.</param>
    /// <param name="bound">The cardinality bound term.</param>
    /// <param name="reversed">Whether the equivalence is asserted with the restriction node as subject.</param>
    /// <param name="relation">The relating predicate, <c>owl:equivalentClass</c> for the entailed shapes.</param>
    /// <param name="derived">Whether the equivalence closes only through a subclass-transitivity hop and scm-eqc2.</param>
    private static void AddCardinalityEquivalence(List<Quad> premiseToAppendTo, string className, string node, string onProperty, RdfTerm bound, bool reversed, string relation, bool derived)
    {
        premiseToAppendTo.Add(Q(Blank(node), RdfNs + "type", Iri(OwlNs + "Restriction")));
        premiseToAppendTo.Add(Q(Blank(node), OwlNs + "onProperty", Named(onProperty)));
        premiseToAppendTo.Add(Q(Blank(node), OwlNs + "cardinality", bound));
        AddEquivalence(premiseToAppendTo, className, node, reversed, relation, derived);
    }

    /// <summary>Adds one restriction node carrying onProperty and someValuesFrom details, related to a named class by owl:equivalentClass.</summary>
    /// <param name="premiseToAppendTo">The premise graph.</param>
    /// <param name="className">The related class's local name.</param>
    /// <param name="node">The restriction node's blank label.</param>
    /// <param name="onProperty">The onProperty target's local name.</param>
    /// <param name="filler">The someValuesFrom filler's local name.</param>
    /// <param name="reversed">Whether the equivalence is asserted with the restriction node as subject.</param>
    private static void AddSomeValuesEquivalence(List<Quad> premiseToAppendTo, string className, string node, string onProperty, string filler, bool reversed)
    {
        premiseToAppendTo.Add(Q(Blank(node), RdfNs + "type", Iri(OwlNs + "Restriction")));
        premiseToAppendTo.Add(Q(Blank(node), OwlNs + "onProperty", Named(onProperty)));
        premiseToAppendTo.Add(Q(Blank(node), OwlNs + "someValuesFrom", Named(filler)));
        AddEquivalence(premiseToAppendTo, className, node, reversed, OwlNs + "equivalentClass", derived: false);
    }

    /// <summary>Adds the class-to-restriction relation in the asked shape: asserted in one orientation, or closed on a later round by scm-eqc2 over a subclass-transitivity hop.</summary>
    /// <param name="premiseToAppendTo">The premise graph.</param>
    /// <param name="className">The related class's local name.</param>
    /// <param name="node">The restriction node's blank label.</param>
    /// <param name="reversed">Whether the relation is asserted with the restriction node as subject.</param>
    /// <param name="relation">The relating predicate.</param>
    /// <param name="derived">Whether the equivalence closes only through the derived route.</param>
    private static void AddEquivalence(List<Quad> premiseToAppendTo, string className, string node, bool reversed, string relation, bool derived)
    {
        if(derived)
        {
            premiseToAppendTo.Add(Q(Named(className), RdfsNs + "subClassOf", Named(className + "-mid")));
            premiseToAppendTo.Add(Q(Named(className + "-mid"), RdfsNs + "subClassOf", Blank(node)));
            premiseToAppendTo.Add(Q(Blank(node), RdfsNs + "subClassOf", Named(className)));

            return;
        }

        if(reversed)
        {
            premiseToAppendTo.Add(Q(Blank(node), relation, Named(className)));

            return;
        }

        premiseToAppendTo.Add(Q(Named(className), relation, Blank(node)));
    }

    /// <summary>Encodes a quad graph's triples through the dictionary — the trace rows' bridge from the quad builders to the closure's encoded surface.</summary>
    /// <param name="quads">The quad graph.</param>
    /// <param name="dictionary">The dictionary the terms intern through.</param>
    /// <returns>The encoded triples, in graph order.</returns>
    private static List<EncodedTriple> Encode(List<Quad> quads, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = [];
        foreach(Quad quad in quads)
        {
            TermId subject = dictionary.GetOrAdd(quad.Subject);
            TermId predicate = dictionary.GetOrAdd((RdfTerm)quad.Predicate);
            TermId @object = dictionary.GetOrAdd(quad.Object);
            encoded.Add(OwlRlBatteryHelpers.Triple(subject, predicate, @object));
        }

        return encoded;
    }

    /// <summary>Computes the informative-conditions closure over the premise with a trace collector attached.</summary>
    /// <param name="premise">The premise graph.</param>
    /// <param name="dictionary">The dictionary the terms intern through.</param>
    /// <param name="events">The collected trace events.</param>
    /// <returns>The closure result.</returns>
    private OwlRlResult ComputeTraced(List<Quad> premise, TermDictionary dictionary, out List<InferenceTraceEvent> events)
    {
        OwlRlTerms terms = new(dictionary);
        TraceCollector collector = new();
        OwlRlResult result = OwlRlClosure.Compute(
            Encode(premise, dictionary), terms, OwlRlDatatypeOracles.FromDictionary(dictionary),
            collector.Handle, VeritasClock.System,
            comprehension: OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);
        events = collector.Events;

        return result;
    }

    /// <summary>The traced events carrying the fibre-cardinality-certificate rule.</summary>
    /// <param name="events">The collected trace events.</param>
    /// <returns>The rule's emissions, in emission order.</returns>
    private static List<InferenceTraceEvent> FibreEmissions(List<InferenceTraceEvent> events)
    {
        List<InferenceTraceEvent> emissions = [];
        foreach(InferenceTraceEvent evt in events)
        {
            if(evt.Rule == EntailmentRules.FibreCardinalityCertificate)
            {
                emissions.Add(evt);
            }
        }

        return emissions;
    }

    /// <summary>Collects inference trace events through a bound method group.</summary>
    private sealed class TraceCollector
    {
        /// <summary>The collected events, in emission order.</summary>
        public List<InferenceTraceEvent> Events { get; } = [];

        /// <summary>Appends one event.</summary>
        /// <param name="evt">The event.</param>
        public void Handle(in InferenceTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>Builds a nil-terminated RDF list over the members and returns its head node, appending the cell triples to the graph.</summary>
    /// <param name="quadsToAppendTo">The graph the list structure appends to.</param>
    /// <param name="labelPrefix">A unique prefix distinguishing this list's cell labels.</param>
    /// <param name="members">The list members, in order.</param>
    /// <returns>The head term — <c>rdf:nil</c> for an empty member array.</returns>
    private static RdfTerm AddList(List<Quad> quadsToAppendTo, string labelPrefix, RdfTerm[] members)
    {
        RdfTerm head = Iri(RdfNs + "nil");
        for(int i = members.Length - 1; i >= 0; i--)
        {
            BlankNode cell = Blank($"{labelPrefix}-{i}");
            quadsToAppendTo.Add(Q(cell, RdfNs + "first", members[i]));
            quadsToAppendTo.Add(Q(cell, RdfNs + "rest", head));
            head = cell;
        }

        return head;
    }

    /// <summary>A quad from a subject term, an absolute predicate IRI, and an object term.</summary>
    /// <param name="subject">The subject term.</param>
    /// <param name="predicate">The predicate IRI, absolute.</param>
    /// <param name="object">The object term.</param>
    /// <returns>The quad.</returns>
    private static Quad Q(RdfTerm subject, string predicate, RdfTerm @object)
    {
        return new Quad(subject, new NamedNode(Utf8Strings.From(predicate)), @object, Graph: null);
    }

    /// <summary>A named node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A named node by absolute IRI.</summary>
    /// <param name="iri">The absolute IRI.</param>
    /// <returns>The node.</returns>
    private static NamedNode Iri(string iri)
    {
        return new NamedNode(Utf8Strings.From(iri));
    }

    /// <summary>A blank node with the label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The node.</returns>
    private static BlankNode Blank(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>A typed cardinality-bound literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatype">The datatype IRI, absolute.</param>
    /// <returns>The literal term.</returns>
    private static Literal Bound(string lexical, string datatype)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From(datatype)));
    }
}
