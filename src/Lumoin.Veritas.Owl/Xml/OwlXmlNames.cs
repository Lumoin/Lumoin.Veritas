using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>An OWL 2 XML serialization element, as a closed dispatch discriminant.</summary>
internal enum OwlXmlElement
{
    /// <summary>The element names no known OWL/XML construct.</summary>
    Unknown = 0,

    /// <summary>The <c>Ontology</c> document root.</summary>
    Ontology,

    /// <summary>The <c>Prefix</c> declaration.</summary>
    Prefix,

    /// <summary>The <c>Import</c> directive.</summary>
    Import,

    /// <summary>The <c>Class</c> entity.</summary>
    Class,

    /// <summary>The <c>Datatype</c> entity.</summary>
    Datatype,

    /// <summary>The <c>ObjectProperty</c> entity.</summary>
    ObjectProperty,

    /// <summary>The <c>DataProperty</c> entity.</summary>
    DataProperty,

    /// <summary>The <c>AnnotationProperty</c> entity.</summary>
    AnnotationProperty,

    /// <summary>The <c>NamedIndividual</c> entity.</summary>
    NamedIndividual,

    /// <summary>The <c>AnonymousIndividual</c> term.</summary>
    AnonymousIndividual,

    /// <summary>The <c>Declaration</c> axiom.</summary>
    Declaration,

    /// <summary>The <c>IRI</c> child-element term form.</summary>
    Iri,

    /// <summary>The <c>AbbreviatedIRI</c> child-element term form.</summary>
    AbbreviatedIri,

    /// <summary>The <c>Literal</c> term.</summary>
    Literal,

    /// <summary>The <c>ObjectInverseOf</c> object-property expression.</summary>
    ObjectInverseOf,

    /// <summary>The <c>ObjectPropertyChain</c> sub-expression of a sub-property axiom.</summary>
    ObjectPropertyChain,

    /// <summary>The <c>ObjectIntersectionOf</c> class expression.</summary>
    ObjectIntersectionOf,

    /// <summary>The <c>ObjectUnionOf</c> class expression.</summary>
    ObjectUnionOf,

    /// <summary>The <c>ObjectComplementOf</c> class expression.</summary>
    ObjectComplementOf,

    /// <summary>The <c>ObjectOneOf</c> class expression.</summary>
    ObjectOneOf,

    /// <summary>The <c>ObjectSomeValuesFrom</c> class expression.</summary>
    ObjectSomeValuesFrom,

    /// <summary>The <c>ObjectAllValuesFrom</c> class expression.</summary>
    ObjectAllValuesFrom,

    /// <summary>The <c>ObjectHasValue</c> class expression.</summary>
    ObjectHasValue,

    /// <summary>The <c>ObjectHasSelf</c> class expression.</summary>
    ObjectHasSelf,

    /// <summary>The <c>ObjectMinCardinality</c> class expression.</summary>
    ObjectMinCardinality,

    /// <summary>The <c>ObjectMaxCardinality</c> class expression.</summary>
    ObjectMaxCardinality,

    /// <summary>The <c>ObjectExactCardinality</c> class expression.</summary>
    ObjectExactCardinality,

    /// <summary>The <c>DataSomeValuesFrom</c> class expression.</summary>
    DataSomeValuesFrom,

    /// <summary>The <c>DataAllValuesFrom</c> class expression.</summary>
    DataAllValuesFrom,

    /// <summary>The <c>DataHasValue</c> class expression.</summary>
    DataHasValue,

    /// <summary>The <c>DataMinCardinality</c> class expression.</summary>
    DataMinCardinality,

    /// <summary>The <c>DataMaxCardinality</c> class expression.</summary>
    DataMaxCardinality,

    /// <summary>The <c>DataExactCardinality</c> class expression.</summary>
    DataExactCardinality,

    /// <summary>The <c>DataIntersectionOf</c> data range.</summary>
    DataIntersectionOf,

    /// <summary>The <c>DataUnionOf</c> data range.</summary>
    DataUnionOf,

    /// <summary>The <c>DataComplementOf</c> data range.</summary>
    DataComplementOf,

    /// <summary>The <c>DataOneOf</c> data range.</summary>
    DataOneOf,

    /// <summary>The <c>DatatypeRestriction</c> data range.</summary>
    DatatypeRestriction,

    /// <summary>The <c>FacetRestriction</c> facet-value pair of a datatype restriction.</summary>
    FacetRestriction,

    /// <summary>The <c>SubClassOf</c> axiom.</summary>
    SubClassOf,

    /// <summary>The <c>EquivalentClasses</c> axiom.</summary>
    EquivalentClasses,

    /// <summary>The <c>DisjointClasses</c> axiom.</summary>
    DisjointClasses,

    /// <summary>The <c>DisjointUnion</c> axiom.</summary>
    DisjointUnion,

    /// <summary>The <c>SubObjectPropertyOf</c> axiom.</summary>
    SubObjectPropertyOf,

    /// <summary>The <c>EquivalentObjectProperties</c> axiom.</summary>
    EquivalentObjectProperties,

    /// <summary>The <c>DisjointObjectProperties</c> axiom.</summary>
    DisjointObjectProperties,

    /// <summary>The <c>InverseObjectProperties</c> axiom.</summary>
    InverseObjectProperties,

    /// <summary>The <c>ObjectPropertyDomain</c> axiom.</summary>
    ObjectPropertyDomain,

    /// <summary>The <c>ObjectPropertyRange</c> axiom.</summary>
    ObjectPropertyRange,

    /// <summary>The <c>FunctionalObjectProperty</c> axiom.</summary>
    FunctionalObjectProperty,

    /// <summary>The <c>InverseFunctionalObjectProperty</c> axiom.</summary>
    InverseFunctionalObjectProperty,

    /// <summary>The <c>ReflexiveObjectProperty</c> axiom.</summary>
    ReflexiveObjectProperty,

    /// <summary>The <c>IrreflexiveObjectProperty</c> axiom.</summary>
    IrreflexiveObjectProperty,

    /// <summary>The <c>SymmetricObjectProperty</c> axiom.</summary>
    SymmetricObjectProperty,

    /// <summary>The <c>AsymmetricObjectProperty</c> axiom.</summary>
    AsymmetricObjectProperty,

    /// <summary>The <c>TransitiveObjectProperty</c> axiom.</summary>
    TransitiveObjectProperty,

    /// <summary>The <c>SubDataPropertyOf</c> axiom.</summary>
    SubDataPropertyOf,

    /// <summary>The <c>EquivalentDataProperties</c> axiom.</summary>
    EquivalentDataProperties,

    /// <summary>The <c>DisjointDataProperties</c> axiom.</summary>
    DisjointDataProperties,

    /// <summary>The <c>DataPropertyDomain</c> axiom.</summary>
    DataPropertyDomain,

    /// <summary>The <c>DataPropertyRange</c> axiom.</summary>
    DataPropertyRange,

    /// <summary>The <c>FunctionalDataProperty</c> axiom.</summary>
    FunctionalDataProperty,

    /// <summary>The <c>DatatypeDefinition</c> axiom.</summary>
    DatatypeDefinition,

    /// <summary>The <c>HasKey</c> axiom.</summary>
    HasKey,

    /// <summary>The <c>SameIndividual</c> axiom.</summary>
    SameIndividual,

    /// <summary>The <c>DifferentIndividuals</c> axiom.</summary>
    DifferentIndividuals,

    /// <summary>The <c>ClassAssertion</c> axiom.</summary>
    ClassAssertion,

    /// <summary>The <c>ObjectPropertyAssertion</c> axiom.</summary>
    ObjectPropertyAssertion,

    /// <summary>The <c>NegativeObjectPropertyAssertion</c> axiom.</summary>
    NegativeObjectPropertyAssertion,

    /// <summary>The <c>DataPropertyAssertion</c> axiom.</summary>
    DataPropertyAssertion,

    /// <summary>The <c>NegativeDataPropertyAssertion</c> axiom.</summary>
    NegativeDataPropertyAssertion,

    /// <summary>The <c>AnnotationAssertion</c> axiom.</summary>
    AnnotationAssertion,

    /// <summary>The <c>SubAnnotationPropertyOf</c> axiom.</summary>
    SubAnnotationPropertyOf,

    /// <summary>The <c>AnnotationPropertyDomain</c> axiom.</summary>
    AnnotationPropertyDomain,

    /// <summary>The <c>AnnotationPropertyRange</c> axiom.</summary>
    AnnotationPropertyRange,

    /// <summary>The <c>Annotation</c> frame, an ontology annotation or a nested annotation.</summary>
    Annotation
}

/// <summary>
/// The element and attribute names of the OWL 2 XML serialization
/// (<see href="https://www.w3.org/TR/owl2-xml-serialization/">OWL 2 XML Serialization</see>),
/// each as its canonical UTF-8 byte sequence, in one home for the reader, the
/// converter, and the writer.
/// </summary>
/// <remarks>
/// The names are <c>u8</c> literals (the single source of truth the writer emits
/// and the reader matches with
/// <see cref="Utf8String.SequenceEqual(System.ReadOnlySpan{byte})"/>); the
/// element-name to discriminant table is a byte-keyed frozen dictionary. Element
/// names are case-sensitive and carry no namespace prefix in the canonical
/// serialization (the OWL namespace is the default namespace of the document).
/// </remarks>
internal static class OwlXmlNames
{
    /// <summary>The OWL namespace, the default namespace of an OWL/XML document.</summary>
    public static ReadOnlySpan<byte> OwlNamespace => "http://www.w3.org/2002/07/owl#"u8;

    /// <summary>The <c>xml:lang</c> attribute carrying a language tag.</summary>
    public static ReadOnlySpan<byte> XmlLangAttribute => "xml:lang"u8;

    /// <summary>The <c>xml:base</c> attribute setting the base IRI.</summary>
    public static ReadOnlySpan<byte> XmlBaseAttribute => "xml:base"u8;

    /// <summary>The <c>xmlns</c> default-namespace declaration attribute.</summary>
    public static ReadOnlySpan<byte> XmlnsAttribute => "xmlns"u8;

    /// <summary>The <c>IRI</c> attribute carrying a full IRI on an entity element.</summary>
    public static ReadOnlySpan<byte> IriAttribute => "IRI"u8;

    /// <summary>The <c>abbreviatedIRI</c> attribute carrying a prefixed name on an entity element.</summary>
    public static ReadOnlySpan<byte> AbbreviatedIriAttribute => "abbreviatedIRI"u8;

    /// <summary>The <c>cardinality</c> attribute of a cardinality restriction.</summary>
    public static ReadOnlySpan<byte> CardinalityAttribute => "cardinality"u8;

    /// <summary>The <c>facet</c> attribute of a facet restriction.</summary>
    public static ReadOnlySpan<byte> FacetAttribute => "facet"u8;

    /// <summary>The <c>name</c> attribute of a prefix declaration.</summary>
    public static ReadOnlySpan<byte> NameAttribute => "name"u8;

    /// <summary>The <c>nodeID</c> attribute of an anonymous individual.</summary>
    public static ReadOnlySpan<byte> NodeIdAttribute => "nodeID"u8;

    /// <summary>The <c>datatypeIRI</c> attribute of a literal.</summary>
    public static ReadOnlySpan<byte> DatatypeIriAttribute => "datatypeIRI"u8;

    /// <summary>The <c>ontologyIRI</c> attribute of the ontology element.</summary>
    public static ReadOnlySpan<byte> OntologyIriAttribute => "ontologyIRI"u8;

    /// <summary>The <c>versionIRI</c> attribute of the ontology element.</summary>
    public static ReadOnlySpan<byte> VersionIriAttribute => "versionIRI"u8;

    /// <summary>The <c>Ontology</c> document root element.</summary>
    public static ReadOnlySpan<byte> Ontology => "Ontology"u8;

    /// <summary>The <c>Prefix</c> declaration element.</summary>
    public static ReadOnlySpan<byte> Prefix => "Prefix"u8;

    /// <summary>The <c>Import</c> directive element.</summary>
    public static ReadOnlySpan<byte> Import => "Import"u8;

    /// <summary>The <c>Class</c> entity element.</summary>
    public static ReadOnlySpan<byte> Class => "Class"u8;

    /// <summary>The <c>Datatype</c> entity element.</summary>
    public static ReadOnlySpan<byte> Datatype => "Datatype"u8;

    /// <summary>The <c>ObjectProperty</c> entity element.</summary>
    public static ReadOnlySpan<byte> ObjectProperty => "ObjectProperty"u8;

    /// <summary>The <c>DataProperty</c> entity element.</summary>
    public static ReadOnlySpan<byte> DataProperty => "DataProperty"u8;

    /// <summary>The <c>AnnotationProperty</c> entity element.</summary>
    public static ReadOnlySpan<byte> AnnotationProperty => "AnnotationProperty"u8;

    /// <summary>The <c>NamedIndividual</c> entity element.</summary>
    public static ReadOnlySpan<byte> NamedIndividual => "NamedIndividual"u8;

    /// <summary>The <c>AnonymousIndividual</c> term element.</summary>
    public static ReadOnlySpan<byte> AnonymousIndividual => "AnonymousIndividual"u8;

    /// <summary>The <c>Declaration</c> axiom element.</summary>
    public static ReadOnlySpan<byte> Declaration => "Declaration"u8;

    /// <summary>The <c>IRI</c> child-element term form.</summary>
    public static ReadOnlySpan<byte> Iri => "IRI"u8;

    /// <summary>The <c>AbbreviatedIRI</c> child-element term form.</summary>
    public static ReadOnlySpan<byte> AbbreviatedIri => "AbbreviatedIRI"u8;

    /// <summary>The <c>Literal</c> term element.</summary>
    public static ReadOnlySpan<byte> Literal => "Literal"u8;

    /// <summary>The <c>ObjectInverseOf</c> object-property expression element.</summary>
    public static ReadOnlySpan<byte> ObjectInverseOf => "ObjectInverseOf"u8;

    /// <summary>The <c>ObjectPropertyChain</c> sub-expression element.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyChain => "ObjectPropertyChain"u8;

    /// <summary>The <c>ObjectIntersectionOf</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectIntersectionOf => "ObjectIntersectionOf"u8;

    /// <summary>The <c>ObjectUnionOf</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectUnionOf => "ObjectUnionOf"u8;

    /// <summary>The <c>ObjectComplementOf</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectComplementOf => "ObjectComplementOf"u8;

    /// <summary>The <c>ObjectOneOf</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectOneOf => "ObjectOneOf"u8;

    /// <summary>The <c>ObjectSomeValuesFrom</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectSomeValuesFrom => "ObjectSomeValuesFrom"u8;

    /// <summary>The <c>ObjectAllValuesFrom</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectAllValuesFrom => "ObjectAllValuesFrom"u8;

    /// <summary>The <c>ObjectHasValue</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectHasValue => "ObjectHasValue"u8;

    /// <summary>The <c>ObjectHasSelf</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectHasSelf => "ObjectHasSelf"u8;

    /// <summary>The <c>ObjectMinCardinality</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectMinCardinality => "ObjectMinCardinality"u8;

    /// <summary>The <c>ObjectMaxCardinality</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectMaxCardinality => "ObjectMaxCardinality"u8;

    /// <summary>The <c>ObjectExactCardinality</c> class expression element.</summary>
    public static ReadOnlySpan<byte> ObjectExactCardinality => "ObjectExactCardinality"u8;

    /// <summary>The <c>DataSomeValuesFrom</c> class expression element.</summary>
    public static ReadOnlySpan<byte> DataSomeValuesFrom => "DataSomeValuesFrom"u8;

    /// <summary>The <c>DataAllValuesFrom</c> class expression element.</summary>
    public static ReadOnlySpan<byte> DataAllValuesFrom => "DataAllValuesFrom"u8;

    /// <summary>The <c>DataHasValue</c> class expression element.</summary>
    public static ReadOnlySpan<byte> DataHasValue => "DataHasValue"u8;

    /// <summary>The <c>DataMinCardinality</c> class expression element.</summary>
    public static ReadOnlySpan<byte> DataMinCardinality => "DataMinCardinality"u8;

    /// <summary>The <c>DataMaxCardinality</c> class expression element.</summary>
    public static ReadOnlySpan<byte> DataMaxCardinality => "DataMaxCardinality"u8;

    /// <summary>The <c>DataExactCardinality</c> class expression element.</summary>
    public static ReadOnlySpan<byte> DataExactCardinality => "DataExactCardinality"u8;

    /// <summary>The <c>DataIntersectionOf</c> data range element.</summary>
    public static ReadOnlySpan<byte> DataIntersectionOf => "DataIntersectionOf"u8;

    /// <summary>The <c>DataUnionOf</c> data range element.</summary>
    public static ReadOnlySpan<byte> DataUnionOf => "DataUnionOf"u8;

    /// <summary>The <c>DataComplementOf</c> data range element.</summary>
    public static ReadOnlySpan<byte> DataComplementOf => "DataComplementOf"u8;

    /// <summary>The <c>DataOneOf</c> data range element.</summary>
    public static ReadOnlySpan<byte> DataOneOf => "DataOneOf"u8;

    /// <summary>The <c>DatatypeRestriction</c> data range element.</summary>
    public static ReadOnlySpan<byte> DatatypeRestriction => "DatatypeRestriction"u8;

    /// <summary>The <c>FacetRestriction</c> element.</summary>
    public static ReadOnlySpan<byte> FacetRestriction => "FacetRestriction"u8;

    /// <summary>The <c>SubClassOf</c> axiom element.</summary>
    public static ReadOnlySpan<byte> SubClassOf => "SubClassOf"u8;

    /// <summary>The <c>EquivalentClasses</c> axiom element.</summary>
    public static ReadOnlySpan<byte> EquivalentClasses => "EquivalentClasses"u8;

    /// <summary>The <c>DisjointClasses</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DisjointClasses => "DisjointClasses"u8;

    /// <summary>The <c>DisjointUnion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DisjointUnion => "DisjointUnion"u8;

    /// <summary>The <c>SubObjectPropertyOf</c> axiom element.</summary>
    public static ReadOnlySpan<byte> SubObjectPropertyOf => "SubObjectPropertyOf"u8;

    /// <summary>The <c>EquivalentObjectProperties</c> axiom element.</summary>
    public static ReadOnlySpan<byte> EquivalentObjectProperties => "EquivalentObjectProperties"u8;

    /// <summary>The <c>DisjointObjectProperties</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DisjointObjectProperties => "DisjointObjectProperties"u8;

    /// <summary>The <c>InverseObjectProperties</c> axiom element.</summary>
    public static ReadOnlySpan<byte> InverseObjectProperties => "InverseObjectProperties"u8;

    /// <summary>The <c>ObjectPropertyDomain</c> axiom element.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyDomain => "ObjectPropertyDomain"u8;

    /// <summary>The <c>ObjectPropertyRange</c> axiom element.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyRange => "ObjectPropertyRange"u8;

    /// <summary>The <c>FunctionalObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> FunctionalObjectProperty => "FunctionalObjectProperty"u8;

    /// <summary>The <c>InverseFunctionalObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> InverseFunctionalObjectProperty => "InverseFunctionalObjectProperty"u8;

    /// <summary>The <c>ReflexiveObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> ReflexiveObjectProperty => "ReflexiveObjectProperty"u8;

    /// <summary>The <c>IrreflexiveObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> IrreflexiveObjectProperty => "IrreflexiveObjectProperty"u8;

    /// <summary>The <c>SymmetricObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> SymmetricObjectProperty => "SymmetricObjectProperty"u8;

    /// <summary>The <c>AsymmetricObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> AsymmetricObjectProperty => "AsymmetricObjectProperty"u8;

    /// <summary>The <c>TransitiveObjectProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> TransitiveObjectProperty => "TransitiveObjectProperty"u8;

    /// <summary>The <c>SubDataPropertyOf</c> axiom element.</summary>
    public static ReadOnlySpan<byte> SubDataPropertyOf => "SubDataPropertyOf"u8;

    /// <summary>The <c>EquivalentDataProperties</c> axiom element.</summary>
    public static ReadOnlySpan<byte> EquivalentDataProperties => "EquivalentDataProperties"u8;

    /// <summary>The <c>DisjointDataProperties</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DisjointDataProperties => "DisjointDataProperties"u8;

    /// <summary>The <c>DataPropertyDomain</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DataPropertyDomain => "DataPropertyDomain"u8;

    /// <summary>The <c>DataPropertyRange</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DataPropertyRange => "DataPropertyRange"u8;

    /// <summary>The <c>FunctionalDataProperty</c> axiom element.</summary>
    public static ReadOnlySpan<byte> FunctionalDataProperty => "FunctionalDataProperty"u8;

    /// <summary>The <c>DatatypeDefinition</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DatatypeDefinition => "DatatypeDefinition"u8;

    /// <summary>The <c>HasKey</c> axiom element.</summary>
    public static ReadOnlySpan<byte> HasKey => "HasKey"u8;

    /// <summary>The <c>SameIndividual</c> axiom element.</summary>
    public static ReadOnlySpan<byte> SameIndividual => "SameIndividual"u8;

    /// <summary>The <c>DifferentIndividuals</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DifferentIndividuals => "DifferentIndividuals"u8;

    /// <summary>The <c>ClassAssertion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> ClassAssertion => "ClassAssertion"u8;

    /// <summary>The <c>ObjectPropertyAssertion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyAssertion => "ObjectPropertyAssertion"u8;

    /// <summary>The <c>NegativeObjectPropertyAssertion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> NegativeObjectPropertyAssertion => "NegativeObjectPropertyAssertion"u8;

    /// <summary>The <c>DataPropertyAssertion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> DataPropertyAssertion => "DataPropertyAssertion"u8;

    /// <summary>The <c>NegativeDataPropertyAssertion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> NegativeDataPropertyAssertion => "NegativeDataPropertyAssertion"u8;

    /// <summary>The <c>AnnotationAssertion</c> axiom element.</summary>
    public static ReadOnlySpan<byte> AnnotationAssertion => "AnnotationAssertion"u8;

    /// <summary>The <c>SubAnnotationPropertyOf</c> axiom element.</summary>
    public static ReadOnlySpan<byte> SubAnnotationPropertyOf => "SubAnnotationPropertyOf"u8;

    /// <summary>The <c>AnnotationPropertyDomain</c> axiom element.</summary>
    public static ReadOnlySpan<byte> AnnotationPropertyDomain => "AnnotationPropertyDomain"u8;

    /// <summary>The <c>AnnotationPropertyRange</c> axiom element.</summary>
    public static ReadOnlySpan<byte> AnnotationPropertyRange => "AnnotationPropertyRange"u8;

    /// <summary>The <c>Annotation</c> frame element.</summary>
    public static ReadOnlySpan<byte> Annotation => "Annotation"u8;

    /// <summary>The element names, keyed to their discriminant, so an element dispatches by a single lookup.</summary>
    private static readonly FrozenDictionary<Utf8String, OwlXmlElement> ByName = new Dictionary<Utf8String, OwlXmlElement>
    {
        [new Utf8String(Ontology.ToArray())] = OwlXmlElement.Ontology,
        [new Utf8String(Prefix.ToArray())] = OwlXmlElement.Prefix,
        [new Utf8String(Import.ToArray())] = OwlXmlElement.Import,
        [new Utf8String(Class.ToArray())] = OwlXmlElement.Class,
        [new Utf8String(Datatype.ToArray())] = OwlXmlElement.Datatype,
        [new Utf8String(ObjectProperty.ToArray())] = OwlXmlElement.ObjectProperty,
        [new Utf8String(DataProperty.ToArray())] = OwlXmlElement.DataProperty,
        [new Utf8String(AnnotationProperty.ToArray())] = OwlXmlElement.AnnotationProperty,
        [new Utf8String(NamedIndividual.ToArray())] = OwlXmlElement.NamedIndividual,
        [new Utf8String(AnonymousIndividual.ToArray())] = OwlXmlElement.AnonymousIndividual,
        [new Utf8String(Declaration.ToArray())] = OwlXmlElement.Declaration,
        [new Utf8String(Iri.ToArray())] = OwlXmlElement.Iri,
        [new Utf8String(AbbreviatedIri.ToArray())] = OwlXmlElement.AbbreviatedIri,
        [new Utf8String(Literal.ToArray())] = OwlXmlElement.Literal,
        [new Utf8String(ObjectInverseOf.ToArray())] = OwlXmlElement.ObjectInverseOf,
        [new Utf8String(ObjectPropertyChain.ToArray())] = OwlXmlElement.ObjectPropertyChain,
        [new Utf8String(ObjectIntersectionOf.ToArray())] = OwlXmlElement.ObjectIntersectionOf,
        [new Utf8String(ObjectUnionOf.ToArray())] = OwlXmlElement.ObjectUnionOf,
        [new Utf8String(ObjectComplementOf.ToArray())] = OwlXmlElement.ObjectComplementOf,
        [new Utf8String(ObjectOneOf.ToArray())] = OwlXmlElement.ObjectOneOf,
        [new Utf8String(ObjectSomeValuesFrom.ToArray())] = OwlXmlElement.ObjectSomeValuesFrom,
        [new Utf8String(ObjectAllValuesFrom.ToArray())] = OwlXmlElement.ObjectAllValuesFrom,
        [new Utf8String(ObjectHasValue.ToArray())] = OwlXmlElement.ObjectHasValue,
        [new Utf8String(ObjectHasSelf.ToArray())] = OwlXmlElement.ObjectHasSelf,
        [new Utf8String(ObjectMinCardinality.ToArray())] = OwlXmlElement.ObjectMinCardinality,
        [new Utf8String(ObjectMaxCardinality.ToArray())] = OwlXmlElement.ObjectMaxCardinality,
        [new Utf8String(ObjectExactCardinality.ToArray())] = OwlXmlElement.ObjectExactCardinality,
        [new Utf8String(DataSomeValuesFrom.ToArray())] = OwlXmlElement.DataSomeValuesFrom,
        [new Utf8String(DataAllValuesFrom.ToArray())] = OwlXmlElement.DataAllValuesFrom,
        [new Utf8String(DataHasValue.ToArray())] = OwlXmlElement.DataHasValue,
        [new Utf8String(DataMinCardinality.ToArray())] = OwlXmlElement.DataMinCardinality,
        [new Utf8String(DataMaxCardinality.ToArray())] = OwlXmlElement.DataMaxCardinality,
        [new Utf8String(DataExactCardinality.ToArray())] = OwlXmlElement.DataExactCardinality,
        [new Utf8String(DataIntersectionOf.ToArray())] = OwlXmlElement.DataIntersectionOf,
        [new Utf8String(DataUnionOf.ToArray())] = OwlXmlElement.DataUnionOf,
        [new Utf8String(DataComplementOf.ToArray())] = OwlXmlElement.DataComplementOf,
        [new Utf8String(DataOneOf.ToArray())] = OwlXmlElement.DataOneOf,
        [new Utf8String(DatatypeRestriction.ToArray())] = OwlXmlElement.DatatypeRestriction,
        [new Utf8String(FacetRestriction.ToArray())] = OwlXmlElement.FacetRestriction,
        [new Utf8String(SubClassOf.ToArray())] = OwlXmlElement.SubClassOf,
        [new Utf8String(EquivalentClasses.ToArray())] = OwlXmlElement.EquivalentClasses,
        [new Utf8String(DisjointClasses.ToArray())] = OwlXmlElement.DisjointClasses,
        [new Utf8String(DisjointUnion.ToArray())] = OwlXmlElement.DisjointUnion,
        [new Utf8String(SubObjectPropertyOf.ToArray())] = OwlXmlElement.SubObjectPropertyOf,
        [new Utf8String(EquivalentObjectProperties.ToArray())] = OwlXmlElement.EquivalentObjectProperties,
        [new Utf8String(DisjointObjectProperties.ToArray())] = OwlXmlElement.DisjointObjectProperties,
        [new Utf8String(InverseObjectProperties.ToArray())] = OwlXmlElement.InverseObjectProperties,
        [new Utf8String(ObjectPropertyDomain.ToArray())] = OwlXmlElement.ObjectPropertyDomain,
        [new Utf8String(ObjectPropertyRange.ToArray())] = OwlXmlElement.ObjectPropertyRange,
        [new Utf8String(FunctionalObjectProperty.ToArray())] = OwlXmlElement.FunctionalObjectProperty,
        [new Utf8String(InverseFunctionalObjectProperty.ToArray())] = OwlXmlElement.InverseFunctionalObjectProperty,
        [new Utf8String(ReflexiveObjectProperty.ToArray())] = OwlXmlElement.ReflexiveObjectProperty,
        [new Utf8String(IrreflexiveObjectProperty.ToArray())] = OwlXmlElement.IrreflexiveObjectProperty,
        [new Utf8String(SymmetricObjectProperty.ToArray())] = OwlXmlElement.SymmetricObjectProperty,
        [new Utf8String(AsymmetricObjectProperty.ToArray())] = OwlXmlElement.AsymmetricObjectProperty,
        [new Utf8String(TransitiveObjectProperty.ToArray())] = OwlXmlElement.TransitiveObjectProperty,
        [new Utf8String(SubDataPropertyOf.ToArray())] = OwlXmlElement.SubDataPropertyOf,
        [new Utf8String(EquivalentDataProperties.ToArray())] = OwlXmlElement.EquivalentDataProperties,
        [new Utf8String(DisjointDataProperties.ToArray())] = OwlXmlElement.DisjointDataProperties,
        [new Utf8String(DataPropertyDomain.ToArray())] = OwlXmlElement.DataPropertyDomain,
        [new Utf8String(DataPropertyRange.ToArray())] = OwlXmlElement.DataPropertyRange,
        [new Utf8String(FunctionalDataProperty.ToArray())] = OwlXmlElement.FunctionalDataProperty,
        [new Utf8String(DatatypeDefinition.ToArray())] = OwlXmlElement.DatatypeDefinition,
        [new Utf8String(HasKey.ToArray())] = OwlXmlElement.HasKey,
        [new Utf8String(SameIndividual.ToArray())] = OwlXmlElement.SameIndividual,
        [new Utf8String(DifferentIndividuals.ToArray())] = OwlXmlElement.DifferentIndividuals,
        [new Utf8String(ClassAssertion.ToArray())] = OwlXmlElement.ClassAssertion,
        [new Utf8String(ObjectPropertyAssertion.ToArray())] = OwlXmlElement.ObjectPropertyAssertion,
        [new Utf8String(NegativeObjectPropertyAssertion.ToArray())] = OwlXmlElement.NegativeObjectPropertyAssertion,
        [new Utf8String(DataPropertyAssertion.ToArray())] = OwlXmlElement.DataPropertyAssertion,
        [new Utf8String(NegativeDataPropertyAssertion.ToArray())] = OwlXmlElement.NegativeDataPropertyAssertion,
        [new Utf8String(AnnotationAssertion.ToArray())] = OwlXmlElement.AnnotationAssertion,
        [new Utf8String(SubAnnotationPropertyOf.ToArray())] = OwlXmlElement.SubAnnotationPropertyOf,
        [new Utf8String(AnnotationPropertyDomain.ToArray())] = OwlXmlElement.AnnotationPropertyDomain,
        [new Utf8String(AnnotationPropertyRange.ToArray())] = OwlXmlElement.AnnotationPropertyRange,
        [new Utf8String(Annotation.ToArray())] = OwlXmlElement.Annotation,
    }.ToFrozenDictionary();

    /// <summary>Resolves an element's local name to its <see cref="OwlXmlElement"/> discriminant.</summary>
    /// <param name="localName">The element local name to classify.</param>
    /// <returns>The matching element, or <see cref="OwlXmlElement.Unknown"/> when the name is not an OWL/XML element.</returns>
    public static OwlXmlElement Resolve(Utf8String localName)
    {
        return ByName.GetValueOrDefault(localName, OwlXmlElement.Unknown);
    }

    /// <summary>Returns the element-name bytes of a discriminant, the inverse of <see cref="Resolve(Utf8String)"/>.</summary>
    /// <param name="element">The element discriminant.</param>
    /// <returns>The element-name bytes.</returns>
    public static ReadOnlySpan<byte> Name(OwlXmlElement element)
    {
        return element switch
        {
            OwlXmlElement.Ontology => Ontology,
            OwlXmlElement.Prefix => Prefix,
            OwlXmlElement.Import => Import,
            OwlXmlElement.Class => Class,
            OwlXmlElement.Datatype => Datatype,
            OwlXmlElement.ObjectProperty => ObjectProperty,
            OwlXmlElement.DataProperty => DataProperty,
            OwlXmlElement.AnnotationProperty => AnnotationProperty,
            OwlXmlElement.NamedIndividual => NamedIndividual,
            OwlXmlElement.AnonymousIndividual => AnonymousIndividual,
            OwlXmlElement.Declaration => Declaration,
            OwlXmlElement.Iri => Iri,
            OwlXmlElement.AbbreviatedIri => AbbreviatedIri,
            OwlXmlElement.Literal => Literal,
            OwlXmlElement.ObjectInverseOf => ObjectInverseOf,
            OwlXmlElement.ObjectPropertyChain => ObjectPropertyChain,
            OwlXmlElement.ObjectIntersectionOf => ObjectIntersectionOf,
            OwlXmlElement.ObjectUnionOf => ObjectUnionOf,
            OwlXmlElement.ObjectComplementOf => ObjectComplementOf,
            OwlXmlElement.ObjectOneOf => ObjectOneOf,
            OwlXmlElement.ObjectSomeValuesFrom => ObjectSomeValuesFrom,
            OwlXmlElement.ObjectAllValuesFrom => ObjectAllValuesFrom,
            OwlXmlElement.ObjectHasValue => ObjectHasValue,
            OwlXmlElement.ObjectHasSelf => ObjectHasSelf,
            OwlXmlElement.ObjectMinCardinality => ObjectMinCardinality,
            OwlXmlElement.ObjectMaxCardinality => ObjectMaxCardinality,
            OwlXmlElement.ObjectExactCardinality => ObjectExactCardinality,
            OwlXmlElement.DataSomeValuesFrom => DataSomeValuesFrom,
            OwlXmlElement.DataAllValuesFrom => DataAllValuesFrom,
            OwlXmlElement.DataHasValue => DataHasValue,
            OwlXmlElement.DataMinCardinality => DataMinCardinality,
            OwlXmlElement.DataMaxCardinality => DataMaxCardinality,
            OwlXmlElement.DataExactCardinality => DataExactCardinality,
            OwlXmlElement.DataIntersectionOf => DataIntersectionOf,
            OwlXmlElement.DataUnionOf => DataUnionOf,
            OwlXmlElement.DataComplementOf => DataComplementOf,
            OwlXmlElement.DataOneOf => DataOneOf,
            OwlXmlElement.DatatypeRestriction => DatatypeRestriction,
            OwlXmlElement.FacetRestriction => FacetRestriction,
            OwlXmlElement.SubClassOf => SubClassOf,
            OwlXmlElement.EquivalentClasses => EquivalentClasses,
            OwlXmlElement.DisjointClasses => DisjointClasses,
            OwlXmlElement.DisjointUnion => DisjointUnion,
            OwlXmlElement.SubObjectPropertyOf => SubObjectPropertyOf,
            OwlXmlElement.EquivalentObjectProperties => EquivalentObjectProperties,
            OwlXmlElement.DisjointObjectProperties => DisjointObjectProperties,
            OwlXmlElement.InverseObjectProperties => InverseObjectProperties,
            OwlXmlElement.ObjectPropertyDomain => ObjectPropertyDomain,
            OwlXmlElement.ObjectPropertyRange => ObjectPropertyRange,
            OwlXmlElement.FunctionalObjectProperty => FunctionalObjectProperty,
            OwlXmlElement.InverseFunctionalObjectProperty => InverseFunctionalObjectProperty,
            OwlXmlElement.ReflexiveObjectProperty => ReflexiveObjectProperty,
            OwlXmlElement.IrreflexiveObjectProperty => IrreflexiveObjectProperty,
            OwlXmlElement.SymmetricObjectProperty => SymmetricObjectProperty,
            OwlXmlElement.AsymmetricObjectProperty => AsymmetricObjectProperty,
            OwlXmlElement.TransitiveObjectProperty => TransitiveObjectProperty,
            OwlXmlElement.SubDataPropertyOf => SubDataPropertyOf,
            OwlXmlElement.EquivalentDataProperties => EquivalentDataProperties,
            OwlXmlElement.DisjointDataProperties => DisjointDataProperties,
            OwlXmlElement.DataPropertyDomain => DataPropertyDomain,
            OwlXmlElement.DataPropertyRange => DataPropertyRange,
            OwlXmlElement.FunctionalDataProperty => FunctionalDataProperty,
            OwlXmlElement.DatatypeDefinition => DatatypeDefinition,
            OwlXmlElement.HasKey => HasKey,
            OwlXmlElement.SameIndividual => SameIndividual,
            OwlXmlElement.DifferentIndividuals => DifferentIndividuals,
            OwlXmlElement.ClassAssertion => ClassAssertion,
            OwlXmlElement.ObjectPropertyAssertion => ObjectPropertyAssertion,
            OwlXmlElement.NegativeObjectPropertyAssertion => NegativeObjectPropertyAssertion,
            OwlXmlElement.DataPropertyAssertion => DataPropertyAssertion,
            OwlXmlElement.NegativeDataPropertyAssertion => NegativeDataPropertyAssertion,
            OwlXmlElement.AnnotationAssertion => AnnotationAssertion,
            OwlXmlElement.SubAnnotationPropertyOf => SubAnnotationPropertyOf,
            OwlXmlElement.AnnotationPropertyDomain => AnnotationPropertyDomain,
            OwlXmlElement.AnnotationPropertyRange => AnnotationPropertyRange,
            OwlXmlElement.Annotation => Annotation,
            _ => default,
        };
    }
}
