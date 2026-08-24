using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// Well-known IRI constants from the OWL 2 vocabulary
/// (<c>http://www.w3.org/2002/07/owl#</c>) used by the structural
/// mapping and the profile checkers.
/// </summary>
/// <remarks>
/// <para>
/// These are allocated once as static byte arrays, mirroring
/// <see cref="Vocabulary"/> and <c>Lumoin.Veritas.Rdf.RdfVocabulary</c>.
/// The RDF and RDFS terms the OWL mapping also consumes
/// (<c>rdf:type</c>, list vocabulary, <c>rdfs:subClassOf</c>, …) stay in
/// their existing homes; only the <c>owl:</c> namespace lives here.
/// </para>
/// <para>
/// Term inventory follows
/// <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/">OWL 2 Mapping to RDF Graphs</see>:
/// the entity-typing classes, the class-expression and data-range
/// constructors, the axiom predicates, and the reification vocabulary.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "OwlVocabulary.Terms.Class is the intended usage pattern.")]
public static class OwlVocabulary
{
    /// <summary>The OWL namespace IRI.</summary>
    public const string Namespace = "http://www.w3.org/2002/07/owl#";

    //Entity-typing classes.
    private static byte[] OntologyBytes { get; } = "http://www.w3.org/2002/07/owl#Ontology"u8.ToArray();
    private static byte[] ClassTermBytes { get; } = "http://www.w3.org/2002/07/owl#Class"u8.ToArray();
    private static byte[] RestrictionBytes { get; } = "http://www.w3.org/2002/07/owl#Restriction"u8.ToArray();
    private static byte[] DataRangeBytes { get; } = "http://www.w3.org/2002/07/owl#DataRange"u8.ToArray();
    private static byte[] ThingBytes { get; } = "http://www.w3.org/2002/07/owl#Thing"u8.ToArray();
    private static byte[] NothingBytes { get; } = "http://www.w3.org/2002/07/owl#Nothing"u8.ToArray();
    private static byte[] NamedIndividualBytes { get; } = "http://www.w3.org/2002/07/owl#NamedIndividual"u8.ToArray();
    private static byte[] ObjectPropertyTermBytes { get; } = "http://www.w3.org/2002/07/owl#ObjectProperty"u8.ToArray();
    private static byte[] DatatypePropertyBytes { get; } = "http://www.w3.org/2002/07/owl#DatatypeProperty"u8.ToArray();
    private static byte[] AnnotationPropertyTermBytes { get; } = "http://www.w3.org/2002/07/owl#AnnotationProperty"u8.ToArray();
    private static byte[] FunctionalPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#FunctionalProperty"u8.ToArray();
    private static byte[] InverseFunctionalPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#InverseFunctionalProperty"u8.ToArray();
    private static byte[] TransitivePropertyBytes { get; } = "http://www.w3.org/2002/07/owl#TransitiveProperty"u8.ToArray();
    private static byte[] SymmetricPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#SymmetricProperty"u8.ToArray();
    private static byte[] AsymmetricPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#AsymmetricProperty"u8.ToArray();
    private static byte[] ReflexivePropertyBytes { get; } = "http://www.w3.org/2002/07/owl#ReflexiveProperty"u8.ToArray();
    private static byte[] IrreflexivePropertyBytes { get; } = "http://www.w3.org/2002/07/owl#IrreflexiveProperty"u8.ToArray();
    private static byte[] AllDisjointClassesBytes { get; } = "http://www.w3.org/2002/07/owl#AllDisjointClasses"u8.ToArray();
    private static byte[] AllDisjointPropertiesBytes { get; } = "http://www.w3.org/2002/07/owl#AllDisjointProperties"u8.ToArray();
    private static byte[] AllDifferentBytes { get; } = "http://www.w3.org/2002/07/owl#AllDifferent"u8.ToArray();
    private static byte[] NegativePropertyAssertionBytes { get; } = "http://www.w3.org/2002/07/owl#NegativePropertyAssertion"u8.ToArray();
    private static byte[] AxiomTermBytes { get; } = "http://www.w3.org/2002/07/owl#Axiom"u8.ToArray();
    private static byte[] AnnotationTermBytes { get; } = "http://www.w3.org/2002/07/owl#Annotation"u8.ToArray();
    private static byte[] DeprecatedClassBytes { get; } = "http://www.w3.org/2002/07/owl#DeprecatedClass"u8.ToArray();
    private static byte[] DeprecatedPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#DeprecatedProperty"u8.ToArray();

    //Top and bottom properties.
    private static byte[] TopObjectPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#topObjectProperty"u8.ToArray();
    private static byte[] BottomObjectPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#bottomObjectProperty"u8.ToArray();
    private static byte[] TopDataPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#topDataProperty"u8.ToArray();
    private static byte[] BottomDataPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#bottomDataProperty"u8.ToArray();

    //Class-expression and data-range constructors.
    private static byte[] IntersectionOfBytes { get; } = "http://www.w3.org/2002/07/owl#intersectionOf"u8.ToArray();
    private static byte[] UnionOfBytes { get; } = "http://www.w3.org/2002/07/owl#unionOf"u8.ToArray();
    private static byte[] ComplementOfBytes { get; } = "http://www.w3.org/2002/07/owl#complementOf"u8.ToArray();
    private static byte[] OneOfBytes { get; } = "http://www.w3.org/2002/07/owl#oneOf"u8.ToArray();
    private static byte[] OnPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#onProperty"u8.ToArray();
    private static byte[] OnPropertiesBytes { get; } = "http://www.w3.org/2002/07/owl#onProperties"u8.ToArray();
    private static byte[] OnClassBytes { get; } = "http://www.w3.org/2002/07/owl#onClass"u8.ToArray();
    private static byte[] OnDataRangeBytes { get; } = "http://www.w3.org/2002/07/owl#onDataRange"u8.ToArray();
    private static byte[] OnDatatypeBytes { get; } = "http://www.w3.org/2002/07/owl#onDatatype"u8.ToArray();
    private static byte[] SomeValuesFromBytes { get; } = "http://www.w3.org/2002/07/owl#someValuesFrom"u8.ToArray();
    private static byte[] AllValuesFromBytes { get; } = "http://www.w3.org/2002/07/owl#allValuesFrom"u8.ToArray();
    private static byte[] HasValueBytes { get; } = "http://www.w3.org/2002/07/owl#hasValue"u8.ToArray();
    private static byte[] HasSelfBytes { get; } = "http://www.w3.org/2002/07/owl#hasSelf"u8.ToArray();
    private static byte[] MinCardinalityBytes { get; } = "http://www.w3.org/2002/07/owl#minCardinality"u8.ToArray();
    private static byte[] MaxCardinalityBytes { get; } = "http://www.w3.org/2002/07/owl#maxCardinality"u8.ToArray();
    private static byte[] CardinalityBytes { get; } = "http://www.w3.org/2002/07/owl#cardinality"u8.ToArray();
    private static byte[] MinQualifiedCardinalityBytes { get; } = "http://www.w3.org/2002/07/owl#minQualifiedCardinality"u8.ToArray();
    private static byte[] MaxQualifiedCardinalityBytes { get; } = "http://www.w3.org/2002/07/owl#maxQualifiedCardinality"u8.ToArray();
    private static byte[] QualifiedCardinalityBytes { get; } = "http://www.w3.org/2002/07/owl#qualifiedCardinality"u8.ToArray();
    private static byte[] DatatypeComplementOfBytes { get; } = "http://www.w3.org/2002/07/owl#datatypeComplementOf"u8.ToArray();
    private static byte[] WithRestrictionsBytes { get; } = "http://www.w3.org/2002/07/owl#withRestrictions"u8.ToArray();

    //Axiom predicates.
    private static byte[] EquivalentClassBytes { get; } = "http://www.w3.org/2002/07/owl#equivalentClass"u8.ToArray();
    private static byte[] DisjointWithBytes { get; } = "http://www.w3.org/2002/07/owl#disjointWith"u8.ToArray();
    private static byte[] DisjointUnionOfBytes { get; } = "http://www.w3.org/2002/07/owl#disjointUnionOf"u8.ToArray();
    private static byte[] EquivalentPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#equivalentProperty"u8.ToArray();
    private static byte[] PropertyDisjointWithBytes { get; } = "http://www.w3.org/2002/07/owl#propertyDisjointWith"u8.ToArray();
    private static byte[] InverseOfBytes { get; } = "http://www.w3.org/2002/07/owl#inverseOf"u8.ToArray();
    private static byte[] PropertyChainAxiomBytes { get; } = "http://www.w3.org/2002/07/owl#propertyChainAxiom"u8.ToArray();
    private static byte[] SameAsBytes { get; } = "http://www.w3.org/2002/07/owl#sameAs"u8.ToArray();
    private static byte[] DifferentFromBytes { get; } = "http://www.w3.org/2002/07/owl#differentFrom"u8.ToArray();
    private static byte[] DistinctMembersBytes { get; } = "http://www.w3.org/2002/07/owl#distinctMembers"u8.ToArray();
    private static byte[] MembersBytes { get; } = "http://www.w3.org/2002/07/owl#members"u8.ToArray();
    private static byte[] HasKeyBytes { get; } = "http://www.w3.org/2002/07/owl#hasKey"u8.ToArray();
    private static byte[] SourceIndividualBytes { get; } = "http://www.w3.org/2002/07/owl#sourceIndividual"u8.ToArray();
    private static byte[] AssertionPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#assertionProperty"u8.ToArray();
    private static byte[] TargetIndividualBytes { get; } = "http://www.w3.org/2002/07/owl#targetIndividual"u8.ToArray();
    private static byte[] TargetValueBytes { get; } = "http://www.w3.org/2002/07/owl#targetValue"u8.ToArray();
    private static byte[] ImportsBytes { get; } = "http://www.w3.org/2002/07/owl#imports"u8.ToArray();
    private static byte[] VersionIriBytes { get; } = "http://www.w3.org/2002/07/owl#versionIRI"u8.ToArray();
    private static byte[] AnnotatedSourceBytes { get; } = "http://www.w3.org/2002/07/owl#annotatedSource"u8.ToArray();
    private static byte[] AnnotatedPropertyBytes { get; } = "http://www.w3.org/2002/07/owl#annotatedProperty"u8.ToArray();
    private static byte[] AnnotatedTargetBytes { get; } = "http://www.w3.org/2002/07/owl#annotatedTarget"u8.ToArray();

    //Annotation properties and version-compatibility markers.
    private static byte[] VersionInfoBytes { get; } = "http://www.w3.org/2002/07/owl#versionInfo"u8.ToArray();
    private static byte[] DeprecatedBytes { get; } = "http://www.w3.org/2002/07/owl#deprecated"u8.ToArray();
    private static byte[] PriorVersionBytes { get; } = "http://www.w3.org/2002/07/owl#priorVersion"u8.ToArray();
    private static byte[] BackwardCompatibleWithBytes { get; } = "http://www.w3.org/2002/07/owl#backwardCompatibleWith"u8.ToArray();
    private static byte[] IncompatibleWithBytes { get; } = "http://www.w3.org/2002/07/owl#incompatibleWith"u8.ToArray();

    //Datatypes the OWL 2 datatype map adds over XSD.
    private static byte[] RealBytes { get; } = "http://www.w3.org/2002/07/owl#real"u8.ToArray();
    private static byte[] RationalBytes { get; } = "http://www.w3.org/2002/07/owl#rational"u8.ToArray();

    /// <summary>The <c>owl:Ontology</c> class.</summary>
    public static Utf8String Ontology { get; } = new(OntologyBytes);

    /// <summary>The <c>owl:Class</c> class.</summary>
    public static Utf8String ClassTerm { get; } = new(ClassTermBytes);

    /// <summary>The <c>owl:Restriction</c> class.</summary>
    public static Utf8String Restriction { get; } = new(RestrictionBytes);

    /// <summary>The <c>owl:DataRange</c> class — the OWL 1 spelling of data-range structure, read like <c>rdfs:Datatype</c>.</summary>
    public static Utf8String DataRange { get; } = new(DataRangeBytes);

    /// <summary>The <c>owl:Thing</c> class — the universal class.</summary>
    public static Utf8String Thing { get; } = new(ThingBytes);

    /// <summary>The <c>owl:Nothing</c> class — the empty class.</summary>
    public static Utf8String Nothing { get; } = new(NothingBytes);

    /// <summary>The <c>owl:NamedIndividual</c> class.</summary>
    public static Utf8String NamedIndividual { get; } = new(NamedIndividualBytes);

    /// <summary>The <c>owl:ObjectProperty</c> class.</summary>
    public static Utf8String ObjectPropertyTerm { get; } = new(ObjectPropertyTermBytes);

    /// <summary>The <c>owl:DatatypeProperty</c> class.</summary>
    public static Utf8String DatatypeProperty { get; } = new(DatatypePropertyBytes);

    /// <summary>The <c>owl:AnnotationProperty</c> class.</summary>
    public static Utf8String AnnotationPropertyTerm { get; } = new(AnnotationPropertyTermBytes);

    /// <summary>The <c>owl:FunctionalProperty</c> characteristic class.</summary>
    public static Utf8String FunctionalProperty { get; } = new(FunctionalPropertyBytes);

    /// <summary>The <c>owl:InverseFunctionalProperty</c> characteristic class.</summary>
    public static Utf8String InverseFunctionalProperty { get; } = new(InverseFunctionalPropertyBytes);

    /// <summary>The <c>owl:TransitiveProperty</c> characteristic class.</summary>
    public static Utf8String TransitiveProperty { get; } = new(TransitivePropertyBytes);

    /// <summary>The <c>owl:SymmetricProperty</c> characteristic class.</summary>
    public static Utf8String SymmetricProperty { get; } = new(SymmetricPropertyBytes);

    /// <summary>The <c>owl:AsymmetricProperty</c> characteristic class.</summary>
    public static Utf8String AsymmetricProperty { get; } = new(AsymmetricPropertyBytes);

    /// <summary>The <c>owl:ReflexiveProperty</c> characteristic class.</summary>
    public static Utf8String ReflexiveProperty { get; } = new(ReflexivePropertyBytes);

    /// <summary>The <c>owl:IrreflexiveProperty</c> characteristic class.</summary>
    public static Utf8String IrreflexiveProperty { get; } = new(IrreflexivePropertyBytes);

    /// <summary>The <c>owl:AllDisjointClasses</c> reification class.</summary>
    public static Utf8String AllDisjointClasses { get; } = new(AllDisjointClassesBytes);

    /// <summary>The <c>owl:AllDisjointProperties</c> reification class.</summary>
    public static Utf8String AllDisjointProperties { get; } = new(AllDisjointPropertiesBytes);

    /// <summary>The <c>owl:AllDifferent</c> reification class.</summary>
    public static Utf8String AllDifferent { get; } = new(AllDifferentBytes);

    /// <summary>The <c>owl:NegativePropertyAssertion</c> reification class.</summary>
    public static Utf8String NegativePropertyAssertion { get; } = new(NegativePropertyAssertionBytes);

    /// <summary>The <c>owl:Axiom</c> annotated-axiom reification class.</summary>
    public static Utf8String AxiomTerm { get; } = new(AxiomTermBytes);

    /// <summary>The <c>owl:Annotation</c> annotated-annotation reification class.</summary>
    public static Utf8String AnnotationTerm { get; } = new(AnnotationTermBytes);

    /// <summary>The <c>owl:DeprecatedClass</c> class.</summary>
    public static Utf8String DeprecatedClass { get; } = new(DeprecatedClassBytes);

    /// <summary>The <c>owl:DeprecatedProperty</c> class.</summary>
    public static Utf8String DeprecatedProperty { get; } = new(DeprecatedPropertyBytes);

    /// <summary>The <c>owl:topObjectProperty</c> property.</summary>
    public static Utf8String TopObjectProperty { get; } = new(TopObjectPropertyBytes);

    /// <summary>The <c>owl:bottomObjectProperty</c> property.</summary>
    public static Utf8String BottomObjectProperty { get; } = new(BottomObjectPropertyBytes);

    /// <summary>The <c>owl:topDataProperty</c> property.</summary>
    public static Utf8String TopDataProperty { get; } = new(TopDataPropertyBytes);

    /// <summary>The <c>owl:bottomDataProperty</c> property.</summary>
    public static Utf8String BottomDataProperty { get; } = new(BottomDataPropertyBytes);

    /// <summary>The <c>owl:intersectionOf</c> constructor predicate.</summary>
    public static Utf8String IntersectionOf { get; } = new(IntersectionOfBytes);

    /// <summary>The <c>owl:unionOf</c> constructor predicate.</summary>
    public static Utf8String UnionOf { get; } = new(UnionOfBytes);

    /// <summary>The <c>owl:complementOf</c> constructor predicate.</summary>
    public static Utf8String ComplementOf { get; } = new(ComplementOfBytes);

    /// <summary>The <c>owl:oneOf</c> constructor predicate.</summary>
    public static Utf8String OneOf { get; } = new(OneOfBytes);

    /// <summary>The <c>owl:onProperty</c> restriction predicate.</summary>
    public static Utf8String OnProperty { get; } = new(OnPropertyBytes);

    /// <summary>The <c>owl:onProperties</c> n-ary restriction predicate.</summary>
    public static Utf8String OnProperties { get; } = new(OnPropertiesBytes);

    /// <summary>The <c>owl:onClass</c> qualified-cardinality predicate.</summary>
    public static Utf8String OnClass { get; } = new(OnClassBytes);

    /// <summary>The <c>owl:onDataRange</c> qualified-cardinality predicate.</summary>
    public static Utf8String OnDataRange { get; } = new(OnDataRangeBytes);

    /// <summary>The <c>owl:onDatatype</c> datatype-restriction predicate.</summary>
    public static Utf8String OnDatatype { get; } = new(OnDatatypeBytes);

    /// <summary>The <c>owl:someValuesFrom</c> restriction predicate.</summary>
    public static Utf8String SomeValuesFrom { get; } = new(SomeValuesFromBytes);

    /// <summary>The <c>owl:allValuesFrom</c> restriction predicate.</summary>
    public static Utf8String AllValuesFrom { get; } = new(AllValuesFromBytes);

    /// <summary>The <c>owl:hasValue</c> restriction predicate.</summary>
    public static Utf8String HasValue { get; } = new(HasValueBytes);

    /// <summary>The <c>owl:hasSelf</c> restriction predicate.</summary>
    public static Utf8String HasSelf { get; } = new(HasSelfBytes);

    /// <summary>The <c>owl:minCardinality</c> restriction predicate.</summary>
    public static Utf8String MinCardinality { get; } = new(MinCardinalityBytes);

    /// <summary>The <c>owl:maxCardinality</c> restriction predicate.</summary>
    public static Utf8String MaxCardinality { get; } = new(MaxCardinalityBytes);

    /// <summary>The <c>owl:cardinality</c> restriction predicate.</summary>
    public static Utf8String Cardinality { get; } = new(CardinalityBytes);

    /// <summary>The <c>owl:minQualifiedCardinality</c> restriction predicate.</summary>
    public static Utf8String MinQualifiedCardinality { get; } = new(MinQualifiedCardinalityBytes);

    /// <summary>The <c>owl:maxQualifiedCardinality</c> restriction predicate.</summary>
    public static Utf8String MaxQualifiedCardinality { get; } = new(MaxQualifiedCardinalityBytes);

    /// <summary>The <c>owl:qualifiedCardinality</c> restriction predicate.</summary>
    public static Utf8String QualifiedCardinality { get; } = new(QualifiedCardinalityBytes);

    /// <summary>The <c>owl:datatypeComplementOf</c> data-range predicate.</summary>
    public static Utf8String DatatypeComplementOf { get; } = new(DatatypeComplementOfBytes);

    /// <summary>The <c>owl:withRestrictions</c> datatype-restriction predicate.</summary>
    public static Utf8String WithRestrictions { get; } = new(WithRestrictionsBytes);

    /// <summary>The <c>owl:equivalentClass</c> axiom predicate.</summary>
    public static Utf8String EquivalentClass { get; } = new(EquivalentClassBytes);

    /// <summary>The <c>owl:disjointWith</c> axiom predicate.</summary>
    public static Utf8String DisjointWith { get; } = new(DisjointWithBytes);

    /// <summary>The <c>owl:disjointUnionOf</c> axiom predicate.</summary>
    public static Utf8String DisjointUnionOf { get; } = new(DisjointUnionOfBytes);

    /// <summary>The <c>owl:equivalentProperty</c> axiom predicate.</summary>
    public static Utf8String EquivalentProperty { get; } = new(EquivalentPropertyBytes);

    /// <summary>The <c>owl:propertyDisjointWith</c> axiom predicate.</summary>
    public static Utf8String PropertyDisjointWith { get; } = new(PropertyDisjointWithBytes);

    /// <summary>The <c>owl:inverseOf</c> predicate — an axiom between named properties, an inverse property expression on a blank subject.</summary>
    public static Utf8String InverseOf { get; } = new(InverseOfBytes);

    /// <summary>The <c>owl:propertyChainAxiom</c> predicate.</summary>
    public static Utf8String PropertyChainAxiom { get; } = new(PropertyChainAxiomBytes);

    /// <summary>The <c>owl:sameAs</c> axiom predicate.</summary>
    public static Utf8String SameAs { get; } = new(SameAsBytes);

    /// <summary>The <c>owl:differentFrom</c> axiom predicate.</summary>
    public static Utf8String DifferentFrom { get; } = new(DifferentFromBytes);

    /// <summary>The <c>owl:distinctMembers</c> predicate of <c>owl:AllDifferent</c>.</summary>
    public static Utf8String DistinctMembers { get; } = new(DistinctMembersBytes);

    /// <summary>The <c>owl:members</c> predicate of the reification classes.</summary>
    public static Utf8String Members { get; } = new(MembersBytes);

    /// <summary>The <c>owl:hasKey</c> axiom predicate.</summary>
    public static Utf8String HasKey { get; } = new(HasKeyBytes);

    /// <summary>The <c>owl:sourceIndividual</c> predicate of a negative property assertion.</summary>
    public static Utf8String SourceIndividual { get; } = new(SourceIndividualBytes);

    /// <summary>The <c>owl:assertionProperty</c> predicate of a negative property assertion.</summary>
    public static Utf8String AssertionProperty { get; } = new(AssertionPropertyBytes);

    /// <summary>The <c>owl:targetIndividual</c> predicate of a negative object property assertion.</summary>
    public static Utf8String TargetIndividual { get; } = new(TargetIndividualBytes);

    /// <summary>The <c>owl:targetValue</c> predicate of a negative data property assertion.</summary>
    public static Utf8String TargetValue { get; } = new(TargetValueBytes);

    /// <summary>The <c>owl:imports</c> ontology predicate.</summary>
    public static Utf8String Imports { get; } = new(ImportsBytes);

    /// <summary>The <c>owl:versionIRI</c> ontology predicate.</summary>
    public static Utf8String VersionIri { get; } = new(VersionIriBytes);

    /// <summary>The <c>owl:annotatedSource</c> axiom-reification predicate.</summary>
    public static Utf8String AnnotatedSource { get; } = new(AnnotatedSourceBytes);

    /// <summary>The <c>owl:annotatedProperty</c> axiom-reification predicate.</summary>
    public static Utf8String AnnotatedProperty { get; } = new(AnnotatedPropertyBytes);

    /// <summary>The <c>owl:annotatedTarget</c> axiom-reification predicate.</summary>
    public static Utf8String AnnotatedTarget { get; } = new(AnnotatedTargetBytes);

    /// <summary>The <c>owl:versionInfo</c> annotation property.</summary>
    public static Utf8String VersionInfo { get; } = new(VersionInfoBytes);

    /// <summary>The <c>owl:deprecated</c> annotation property.</summary>
    public static Utf8String Deprecated { get; } = new(DeprecatedBytes);

    /// <summary>The <c>owl:priorVersion</c> ontology annotation property.</summary>
    public static Utf8String PriorVersion { get; } = new(PriorVersionBytes);

    /// <summary>The <c>owl:backwardCompatibleWith</c> ontology annotation property.</summary>
    public static Utf8String BackwardCompatibleWith { get; } = new(BackwardCompatibleWithBytes);

    /// <summary>The <c>owl:incompatibleWith</c> ontology annotation property.</summary>
    public static Utf8String IncompatibleWith { get; } = new(IncompatibleWithBytes);

    /// <summary>The <c>owl:real</c> datatype.</summary>
    public static Utf8String Real { get; } = new(RealBytes);

    /// <summary>The <c>owl:rational</c> datatype.</summary>
    public static Utf8String Rational { get; } = new(RationalBytes);

    /// <summary>Every IRI constant in this vocabulary, in declaration order — the OWL 2 term set, for callers that enumerate it (e.g. an editor's completion proposal corpus).</summary>
    public static IReadOnlyList<Utf8String> All { get; } =
    [
        Ontology, ClassTerm, Restriction, DataRange, Thing, Nothing, NamedIndividual,
        ObjectPropertyTerm, DatatypeProperty, AnnotationPropertyTerm, FunctionalProperty,
        InverseFunctionalProperty, TransitiveProperty, SymmetricProperty, AsymmetricProperty,
        ReflexiveProperty, IrreflexiveProperty, AllDisjointClasses, AllDisjointProperties,
        AllDifferent, NegativePropertyAssertion, AxiomTerm, AnnotationTerm, DeprecatedClass,
        DeprecatedProperty, TopObjectProperty, BottomObjectProperty, TopDataProperty, BottomDataProperty,
        IntersectionOf, UnionOf, ComplementOf, OneOf, OnProperty, OnProperties, OnClass, OnDataRange,
        OnDatatype, SomeValuesFrom, AllValuesFrom, HasValue, HasSelf, MinCardinality, MaxCardinality,
        Cardinality, MinQualifiedCardinality, MaxQualifiedCardinality, QualifiedCardinality,
        DatatypeComplementOf, WithRestrictions, EquivalentClass, DisjointWith, DisjointUnionOf,
        EquivalentProperty, PropertyDisjointWith, InverseOf, PropertyChainAxiom, SameAs, DifferentFrom,
        DistinctMembers, Members, HasKey, SourceIndividual, AssertionProperty, TargetIndividual,
        TargetValue, Imports, VersionIri, AnnotatedSource, AnnotatedProperty, AnnotatedTarget,
        VersionInfo, Deprecated, PriorVersion, BackwardCompatibleWith, IncompatibleWith, Real, Rational,
    ];
}
