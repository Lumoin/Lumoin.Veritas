using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Owl.Functional;

/// <summary>
/// The reserved constructor and directive keywords of the OWL 2 functional-style
/// syntax, each as its canonical UTF-8 byte sequence.
/// </summary>
/// <remarks>
/// <para>
/// The byte-native reader and converter match a node's head against these
/// sequences with <see cref="System.MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>,
/// so the <c>u8</c> literal is the single source of truth for each keyword — there
/// is no UTF-16 form and no per-keyword allocation (the literals are embedded in
/// the assembly). The functional syntax names its constructors as bare words
/// followed by <c>(</c>, so — unlike the Manchester syntax — none of these carry a
/// trailing colon.
/// </para>
/// </remarks>
internal static class OwlFunctionalKeywords
{
    /// <summary>The top-level <c>Ontology(…)</c> group introducing the document body.</summary>
    public static ReadOnlySpan<byte> Ontology => "Ontology"u8;

    /// <summary>The top-level <c>Prefix(…)</c> declaration binding a namespace prefix.</summary>
    public static ReadOnlySpan<byte> Prefix => "Prefix"u8;

    /// <summary>The <c>Import(…)</c> directive naming an imported ontology IRI.</summary>
    public static ReadOnlySpan<byte> Import => "Import"u8;

    /// <summary>The <c>Annotation(…)</c> frame, both as an ontology annotation and a nested annotation.</summary>
    public static ReadOnlySpan<byte> Annotation => "Annotation"u8;

    /// <summary>The <c>Declaration(…)</c> axiom head wrapping an entity declaration.</summary>
    public static ReadOnlySpan<byte> Declaration => "Declaration"u8;

    /// <summary>The <c>Class(…)</c> entity, declared and referenced.</summary>
    public static ReadOnlySpan<byte> Class => "Class"u8;

    /// <summary>The <c>Datatype(…)</c> entity, declared and referenced.</summary>
    public static ReadOnlySpan<byte> Datatype => "Datatype"u8;

    /// <summary>The <c>ObjectProperty(…)</c> entity, declared and referenced.</summary>
    public static ReadOnlySpan<byte> ObjectProperty => "ObjectProperty"u8;

    /// <summary>The <c>DataProperty(…)</c> entity, declared and referenced.</summary>
    public static ReadOnlySpan<byte> DataProperty => "DataProperty"u8;

    /// <summary>The <c>AnnotationProperty(…)</c> entity, declared and referenced.</summary>
    public static ReadOnlySpan<byte> AnnotationProperty => "AnnotationProperty"u8;

    /// <summary>The <c>NamedIndividual(…)</c> entity declaration.</summary>
    public static ReadOnlySpan<byte> NamedIndividual => "NamedIndividual"u8;

    /// <summary>The <c>ObjectInverseOf(…)</c> object-property expression.</summary>
    public static ReadOnlySpan<byte> ObjectInverseOf => "ObjectInverseOf"u8;

    /// <summary>The <c>ObjectIntersectionOf(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectIntersectionOf => "ObjectIntersectionOf"u8;

    /// <summary>The <c>ObjectUnionOf(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectUnionOf => "ObjectUnionOf"u8;

    /// <summary>The <c>ObjectComplementOf(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectComplementOf => "ObjectComplementOf"u8;

    /// <summary>The <c>ObjectOneOf(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectOneOf => "ObjectOneOf"u8;

    /// <summary>The <c>ObjectSomeValuesFrom(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectSomeValuesFrom => "ObjectSomeValuesFrom"u8;

    /// <summary>The <c>ObjectAllValuesFrom(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectAllValuesFrom => "ObjectAllValuesFrom"u8;

    /// <summary>The <c>ObjectHasValue(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectHasValue => "ObjectHasValue"u8;

    /// <summary>The <c>ObjectHasSelf(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectHasSelf => "ObjectHasSelf"u8;

    /// <summary>The <c>ObjectMinCardinality(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectMinCardinality => "ObjectMinCardinality"u8;

    /// <summary>The <c>ObjectMaxCardinality(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectMaxCardinality => "ObjectMaxCardinality"u8;

    /// <summary>The <c>ObjectExactCardinality(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> ObjectExactCardinality => "ObjectExactCardinality"u8;

    /// <summary>The <c>DataSomeValuesFrom(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> DataSomeValuesFrom => "DataSomeValuesFrom"u8;

    /// <summary>The <c>DataAllValuesFrom(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> DataAllValuesFrom => "DataAllValuesFrom"u8;

    /// <summary>The <c>DataHasValue(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> DataHasValue => "DataHasValue"u8;

    /// <summary>The <c>DataMinCardinality(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> DataMinCardinality => "DataMinCardinality"u8;

    /// <summary>The <c>DataMaxCardinality(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> DataMaxCardinality => "DataMaxCardinality"u8;

    /// <summary>The <c>DataExactCardinality(…)</c> class expression.</summary>
    public static ReadOnlySpan<byte> DataExactCardinality => "DataExactCardinality"u8;

    /// <summary>The <c>DataIntersectionOf(…)</c> data range.</summary>
    public static ReadOnlySpan<byte> DataIntersectionOf => "DataIntersectionOf"u8;

    /// <summary>The <c>DataUnionOf(…)</c> data range.</summary>
    public static ReadOnlySpan<byte> DataUnionOf => "DataUnionOf"u8;

    /// <summary>The <c>DataComplementOf(…)</c> data range.</summary>
    public static ReadOnlySpan<byte> DataComplementOf => "DataComplementOf"u8;

    /// <summary>The <c>DataOneOf(…)</c> data range.</summary>
    public static ReadOnlySpan<byte> DataOneOf => "DataOneOf"u8;

    /// <summary>The <c>DatatypeRestriction(…)</c> data range.</summary>
    public static ReadOnlySpan<byte> DatatypeRestriction => "DatatypeRestriction"u8;

    /// <summary>The <c>ObjectPropertyChain(…)</c> sub-expression of a property-chain axiom.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyChain => "ObjectPropertyChain"u8;

    /// <summary>The <c>SubClassOf(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> SubClassOf => "SubClassOf"u8;

    /// <summary>The <c>EquivalentClasses(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> EquivalentClasses => "EquivalentClasses"u8;

    /// <summary>The <c>DisjointClasses(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DisjointClasses => "DisjointClasses"u8;

    /// <summary>The <c>DisjointUnion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DisjointUnion => "DisjointUnion"u8;

    /// <summary>The <c>SubObjectPropertyOf(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> SubObjectPropertyOf => "SubObjectPropertyOf"u8;

    /// <summary>The <c>EquivalentObjectProperties(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> EquivalentObjectProperties => "EquivalentObjectProperties"u8;

    /// <summary>The <c>DisjointObjectProperties(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DisjointObjectProperties => "DisjointObjectProperties"u8;

    /// <summary>The <c>InverseObjectProperties(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> InverseObjectProperties => "InverseObjectProperties"u8;

    /// <summary>The <c>ObjectPropertyDomain(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyDomain => "ObjectPropertyDomain"u8;

    /// <summary>The <c>ObjectPropertyRange(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyRange => "ObjectPropertyRange"u8;

    /// <summary>The <c>FunctionalObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> FunctionalObjectProperty => "FunctionalObjectProperty"u8;

    /// <summary>The <c>InverseFunctionalObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> InverseFunctionalObjectProperty => "InverseFunctionalObjectProperty"u8;

    /// <summary>The <c>TransitiveObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> TransitiveObjectProperty => "TransitiveObjectProperty"u8;

    /// <summary>The <c>SymmetricObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> SymmetricObjectProperty => "SymmetricObjectProperty"u8;

    /// <summary>The <c>AsymmetricObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> AsymmetricObjectProperty => "AsymmetricObjectProperty"u8;

    /// <summary>The <c>ReflexiveObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> ReflexiveObjectProperty => "ReflexiveObjectProperty"u8;

    /// <summary>The <c>IrreflexiveObjectProperty(…)</c> characteristic axiom.</summary>
    public static ReadOnlySpan<byte> IrreflexiveObjectProperty => "IrreflexiveObjectProperty"u8;

    /// <summary>The <c>SubDataPropertyOf(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> SubDataPropertyOf => "SubDataPropertyOf"u8;

    /// <summary>The <c>EquivalentDataProperties(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> EquivalentDataProperties => "EquivalentDataProperties"u8;

    /// <summary>The <c>DisjointDataProperties(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DisjointDataProperties => "DisjointDataProperties"u8;

    /// <summary>The <c>DataPropertyDomain(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DataPropertyDomain => "DataPropertyDomain"u8;

    /// <summary>The <c>DataPropertyRange(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DataPropertyRange => "DataPropertyRange"u8;

    /// <summary>The <c>FunctionalDataProperty(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> FunctionalDataProperty => "FunctionalDataProperty"u8;

    /// <summary>The <c>DatatypeDefinition(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DatatypeDefinition => "DatatypeDefinition"u8;

    /// <summary>The <c>HasKey(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> HasKey => "HasKey"u8;

    /// <summary>The <c>SameIndividual(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> SameIndividual => "SameIndividual"u8;

    /// <summary>The <c>DifferentIndividuals(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DifferentIndividuals => "DifferentIndividuals"u8;

    /// <summary>The <c>ClassAssertion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> ClassAssertion => "ClassAssertion"u8;

    /// <summary>The <c>ObjectPropertyAssertion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> ObjectPropertyAssertion => "ObjectPropertyAssertion"u8;

    /// <summary>The <c>NegativeObjectPropertyAssertion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> NegativeObjectPropertyAssertion => "NegativeObjectPropertyAssertion"u8;

    /// <summary>The <c>DataPropertyAssertion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> DataPropertyAssertion => "DataPropertyAssertion"u8;

    /// <summary>The <c>NegativeDataPropertyAssertion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> NegativeDataPropertyAssertion => "NegativeDataPropertyAssertion"u8;

    /// <summary>The <c>AnnotationAssertion(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> AnnotationAssertion => "AnnotationAssertion"u8;

    /// <summary>The <c>SubAnnotationPropertyOf(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> SubAnnotationPropertyOf => "SubAnnotationPropertyOf"u8;

    /// <summary>The <c>AnnotationPropertyDomain(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> AnnotationPropertyDomain => "AnnotationPropertyDomain"u8;

    /// <summary>The <c>AnnotationPropertyRange(…)</c> axiom.</summary>
    public static ReadOnlySpan<byte> AnnotationPropertyRange => "AnnotationPropertyRange"u8;

    /// <summary>Maps each keyword's UTF-8 bytes to its <see cref="OwlFunctionalKeyword"/>, so a node head dispatches by a single lookup rather than a chain of comparisons.</summary>
    private static readonly FrozenDictionary<Utf8String, OwlFunctionalKeyword> ByName = new Dictionary<Utf8String, OwlFunctionalKeyword>
    {
        [new Utf8String(Ontology.ToArray())] = OwlFunctionalKeyword.Ontology,
        [new Utf8String(Prefix.ToArray())] = OwlFunctionalKeyword.Prefix,
        [new Utf8String(Import.ToArray())] = OwlFunctionalKeyword.Import,
        [new Utf8String(Annotation.ToArray())] = OwlFunctionalKeyword.Annotation,
        [new Utf8String(Declaration.ToArray())] = OwlFunctionalKeyword.Declaration,
        [new Utf8String(Class.ToArray())] = OwlFunctionalKeyword.Class,
        [new Utf8String(Datatype.ToArray())] = OwlFunctionalKeyword.Datatype,
        [new Utf8String(ObjectProperty.ToArray())] = OwlFunctionalKeyword.ObjectProperty,
        [new Utf8String(DataProperty.ToArray())] = OwlFunctionalKeyword.DataProperty,
        [new Utf8String(AnnotationProperty.ToArray())] = OwlFunctionalKeyword.AnnotationProperty,
        [new Utf8String(NamedIndividual.ToArray())] = OwlFunctionalKeyword.NamedIndividual,
        [new Utf8String(ObjectInverseOf.ToArray())] = OwlFunctionalKeyword.ObjectInverseOf,
        [new Utf8String(ObjectIntersectionOf.ToArray())] = OwlFunctionalKeyword.ObjectIntersectionOf,
        [new Utf8String(ObjectUnionOf.ToArray())] = OwlFunctionalKeyword.ObjectUnionOf,
        [new Utf8String(ObjectComplementOf.ToArray())] = OwlFunctionalKeyword.ObjectComplementOf,
        [new Utf8String(ObjectOneOf.ToArray())] = OwlFunctionalKeyword.ObjectOneOf,
        [new Utf8String(ObjectSomeValuesFrom.ToArray())] = OwlFunctionalKeyword.ObjectSomeValuesFrom,
        [new Utf8String(ObjectAllValuesFrom.ToArray())] = OwlFunctionalKeyword.ObjectAllValuesFrom,
        [new Utf8String(ObjectHasValue.ToArray())] = OwlFunctionalKeyword.ObjectHasValue,
        [new Utf8String(ObjectHasSelf.ToArray())] = OwlFunctionalKeyword.ObjectHasSelf,
        [new Utf8String(ObjectMinCardinality.ToArray())] = OwlFunctionalKeyword.ObjectMinCardinality,
        [new Utf8String(ObjectMaxCardinality.ToArray())] = OwlFunctionalKeyword.ObjectMaxCardinality,
        [new Utf8String(ObjectExactCardinality.ToArray())] = OwlFunctionalKeyword.ObjectExactCardinality,
        [new Utf8String(DataSomeValuesFrom.ToArray())] = OwlFunctionalKeyword.DataSomeValuesFrom,
        [new Utf8String(DataAllValuesFrom.ToArray())] = OwlFunctionalKeyword.DataAllValuesFrom,
        [new Utf8String(DataHasValue.ToArray())] = OwlFunctionalKeyword.DataHasValue,
        [new Utf8String(DataMinCardinality.ToArray())] = OwlFunctionalKeyword.DataMinCardinality,
        [new Utf8String(DataMaxCardinality.ToArray())] = OwlFunctionalKeyword.DataMaxCardinality,
        [new Utf8String(DataExactCardinality.ToArray())] = OwlFunctionalKeyword.DataExactCardinality,
        [new Utf8String(DataIntersectionOf.ToArray())] = OwlFunctionalKeyword.DataIntersectionOf,
        [new Utf8String(DataUnionOf.ToArray())] = OwlFunctionalKeyword.DataUnionOf,
        [new Utf8String(DataComplementOf.ToArray())] = OwlFunctionalKeyword.DataComplementOf,
        [new Utf8String(DataOneOf.ToArray())] = OwlFunctionalKeyword.DataOneOf,
        [new Utf8String(DatatypeRestriction.ToArray())] = OwlFunctionalKeyword.DatatypeRestriction,
        [new Utf8String(ObjectPropertyChain.ToArray())] = OwlFunctionalKeyword.ObjectPropertyChain,
        [new Utf8String(SubClassOf.ToArray())] = OwlFunctionalKeyword.SubClassOf,
        [new Utf8String(EquivalentClasses.ToArray())] = OwlFunctionalKeyword.EquivalentClasses,
        [new Utf8String(DisjointClasses.ToArray())] = OwlFunctionalKeyword.DisjointClasses,
        [new Utf8String(DisjointUnion.ToArray())] = OwlFunctionalKeyword.DisjointUnion,
        [new Utf8String(SubObjectPropertyOf.ToArray())] = OwlFunctionalKeyword.SubObjectPropertyOf,
        [new Utf8String(EquivalentObjectProperties.ToArray())] = OwlFunctionalKeyword.EquivalentObjectProperties,
        [new Utf8String(DisjointObjectProperties.ToArray())] = OwlFunctionalKeyword.DisjointObjectProperties,
        [new Utf8String(InverseObjectProperties.ToArray())] = OwlFunctionalKeyword.InverseObjectProperties,
        [new Utf8String(ObjectPropertyDomain.ToArray())] = OwlFunctionalKeyword.ObjectPropertyDomain,
        [new Utf8String(ObjectPropertyRange.ToArray())] = OwlFunctionalKeyword.ObjectPropertyRange,
        [new Utf8String(FunctionalObjectProperty.ToArray())] = OwlFunctionalKeyword.FunctionalObjectProperty,
        [new Utf8String(InverseFunctionalObjectProperty.ToArray())] = OwlFunctionalKeyword.InverseFunctionalObjectProperty,
        [new Utf8String(TransitiveObjectProperty.ToArray())] = OwlFunctionalKeyword.TransitiveObjectProperty,
        [new Utf8String(SymmetricObjectProperty.ToArray())] = OwlFunctionalKeyword.SymmetricObjectProperty,
        [new Utf8String(AsymmetricObjectProperty.ToArray())] = OwlFunctionalKeyword.AsymmetricObjectProperty,
        [new Utf8String(ReflexiveObjectProperty.ToArray())] = OwlFunctionalKeyword.ReflexiveObjectProperty,
        [new Utf8String(IrreflexiveObjectProperty.ToArray())] = OwlFunctionalKeyword.IrreflexiveObjectProperty,
        [new Utf8String(SubDataPropertyOf.ToArray())] = OwlFunctionalKeyword.SubDataPropertyOf,
        [new Utf8String(EquivalentDataProperties.ToArray())] = OwlFunctionalKeyword.EquivalentDataProperties,
        [new Utf8String(DisjointDataProperties.ToArray())] = OwlFunctionalKeyword.DisjointDataProperties,
        [new Utf8String(DataPropertyDomain.ToArray())] = OwlFunctionalKeyword.DataPropertyDomain,
        [new Utf8String(DataPropertyRange.ToArray())] = OwlFunctionalKeyword.DataPropertyRange,
        [new Utf8String(FunctionalDataProperty.ToArray())] = OwlFunctionalKeyword.FunctionalDataProperty,
        [new Utf8String(DatatypeDefinition.ToArray())] = OwlFunctionalKeyword.DatatypeDefinition,
        [new Utf8String(HasKey.ToArray())] = OwlFunctionalKeyword.HasKey,
        [new Utf8String(SameIndividual.ToArray())] = OwlFunctionalKeyword.SameIndividual,
        [new Utf8String(DifferentIndividuals.ToArray())] = OwlFunctionalKeyword.DifferentIndividuals,
        [new Utf8String(ClassAssertion.ToArray())] = OwlFunctionalKeyword.ClassAssertion,
        [new Utf8String(ObjectPropertyAssertion.ToArray())] = OwlFunctionalKeyword.ObjectPropertyAssertion,
        [new Utf8String(NegativeObjectPropertyAssertion.ToArray())] = OwlFunctionalKeyword.NegativeObjectPropertyAssertion,
        [new Utf8String(DataPropertyAssertion.ToArray())] = OwlFunctionalKeyword.DataPropertyAssertion,
        [new Utf8String(NegativeDataPropertyAssertion.ToArray())] = OwlFunctionalKeyword.NegativeDataPropertyAssertion,
        [new Utf8String(AnnotationAssertion.ToArray())] = OwlFunctionalKeyword.AnnotationAssertion,
        [new Utf8String(SubAnnotationPropertyOf.ToArray())] = OwlFunctionalKeyword.SubAnnotationPropertyOf,
        [new Utf8String(AnnotationPropertyDomain.ToArray())] = OwlFunctionalKeyword.AnnotationPropertyDomain,
        [new Utf8String(AnnotationPropertyRange.ToArray())] = OwlFunctionalKeyword.AnnotationPropertyRange,
    }.ToFrozenDictionary();

    /// <summary>Resolves a node head's bytes to its <see cref="OwlFunctionalKeyword"/>.</summary>
    /// <param name="head">The constructor-name bytes to classify.</param>
    /// <returns>The matching keyword, or <see cref="OwlFunctionalKeyword.Unrecognised"/> when the bytes name no constructor.</returns>
    public static OwlFunctionalKeyword Resolve(Utf8String head)
    {
        return ByName.GetValueOrDefault(head, OwlFunctionalKeyword.Unrecognised);
    }

    /// <summary>Whether a node head names the <c>Ontology</c> document group.</summary>
    /// <param name="head">The constructor-name bytes to classify.</param>
    /// <returns><see langword="true"/> for the <c>Ontology</c> head.</returns>
    public static bool IsOntology(Utf8String head)
    {
        return head.SequenceEqual(Ontology);
    }

    /// <summary>Whether a node head names a <c>Prefix</c> declaration.</summary>
    /// <param name="head">The constructor-name bytes to classify.</param>
    /// <returns><see langword="true"/> for the <c>Prefix</c> head.</returns>
    public static bool IsPrefix(Utf8String head)
    {
        return head.SequenceEqual(Prefix);
    }

    /// <summary>Whether a node head names an <c>Import</c> directive.</summary>
    /// <param name="head">The constructor-name bytes to classify.</param>
    /// <returns><see langword="true"/> for the <c>Import</c> head.</returns>
    public static bool IsImport(Utf8String head)
    {
        return head.SequenceEqual(Import);
    }

    /// <summary>Whether a node head names an <c>Annotation</c> frame.</summary>
    /// <param name="head">The constructor-name bytes to classify.</param>
    /// <returns><see langword="true"/> for the <c>Annotation</c> head.</returns>
    public static bool IsAnnotation(Utf8String head)
    {
        return head.SequenceEqual(Annotation);
    }
}

/// <summary>The constructor and directive keywords of the OWL 2 functional-style syntax as a closed dispatch discriminant.</summary>
internal enum OwlFunctionalKeyword
{
    /// <summary>The head names no known constructor.</summary>
    Unrecognised = 0,

    /// <summary>The <c>Ontology</c> document group.</summary>
    Ontology,

    /// <summary>The <c>Prefix</c> declaration.</summary>
    Prefix,

    /// <summary>The <c>Import</c> directive.</summary>
    Import,

    /// <summary>The <c>Annotation</c> frame.</summary>
    Annotation,

    /// <summary>The <c>Declaration</c> axiom.</summary>
    Declaration,

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

    /// <summary>The <c>ObjectInverseOf</c> object-property expression.</summary>
    ObjectInverseOf,

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

    /// <summary>The <c>ObjectPropertyChain</c> sub-expression.</summary>
    ObjectPropertyChain,

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

    /// <summary>The <c>TransitiveObjectProperty</c> axiom.</summary>
    TransitiveObjectProperty,

    /// <summary>The <c>SymmetricObjectProperty</c> axiom.</summary>
    SymmetricObjectProperty,

    /// <summary>The <c>AsymmetricObjectProperty</c> axiom.</summary>
    AsymmetricObjectProperty,

    /// <summary>The <c>ReflexiveObjectProperty</c> axiom.</summary>
    ReflexiveObjectProperty,

    /// <summary>The <c>IrreflexiveObjectProperty</c> axiom.</summary>
    IrreflexiveObjectProperty,

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
    AnnotationPropertyRange
}
