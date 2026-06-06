using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Structural;

/// <summary>
/// The forward direction of
/// <see href="https://www.w3.org/TR/owl2-mapping-to-rdf/">OWL 2 Mapping to RDF Graphs</see>:
/// serialises a structural document into triples, so a document read from a
/// non-RDF syntax feeds the triple-level consumers — the RL rules closure
/// foremost.
/// </summary>
/// <remarks>
/// Class expressions, data ranges, reified axiom structures, and RDF
/// collections materialise as fresh blank nodes; nested expressions walk
/// with an explicit stack. Pairwise axioms emit their pairwise triple form;
/// n-ary disjointness emits the reified <c>owl:AllDisjointClasses</c> /
/// <c>owl:AllDisjointProperties</c> form the rules read either way.
/// </remarks>
public static class OwlStructuralToRdf
{
    /// <summary>
    /// Serialises the document's axioms into triples.
    /// </summary>
    /// <param name="document">The structural document.</param>
    /// <returns>The triples, in axiom order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static List<Quad> ToQuads(OwlOntologyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        WriterContext context = new();

        if(document.OntologyIri is NamedNode ontology)
        {
            context.Emit(ontology, context.RdfType, context.OwlOntology);
        }

        foreach(OwlAxiom axiom in document.Axioms)
        {
            context.EmitAxiom(axiom);
        }

        return context.Quads;
    }

    //The per-run state: the output triples, the blank-node counter, and
    //the vocabulary nodes minted once.
    private sealed class WriterContext
    {
        public List<Quad> Quads { get; } = [];

        /// <summary>The fresh blank-node counter for expression structure.</summary>
        private int NextBlank { get; set; }

        /// <summary>Class expressions already serialised, so a shared subtree keeps one node.</summary>
        private Dictionary<OwlClassExpression, RdfTerm> ClassNodes { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Data ranges already serialised.</summary>
        private Dictionary<OwlDataRange, RdfTerm> RangeNodes { get; } = new(ReferenceEqualityComparer.Instance);

        public NamedNode RdfType { get; } = new(Vocabulary.Rdf.Type);

        public NamedNode OwlOntology { get; } = new(OwlVocabulary.Ontology);

        private NamedNode RdfFirst { get; } = new(RdfVocabulary.Rdf.First);

        private NamedNode RdfRest { get; } = new(RdfVocabulary.Rdf.Rest);

        private NamedNode RdfNil { get; } = new(RdfVocabulary.Rdf.Nil);

        private NamedNode RdfsSubClassOf { get; } = new(RdfVocabulary.Rdfs.SubClassOf);

        private NamedNode RdfsSubPropertyOf { get; } = new(RdfVocabulary.Rdfs.SubPropertyOf);

        private NamedNode RdfsDomain { get; } = new(RdfVocabulary.Rdfs.Domain);

        private NamedNode RdfsRange { get; } = new(RdfVocabulary.Rdfs.Range);

        private NamedNode XsdBoolean { get; } = new(Vocabulary.Xsd.Boolean);

        private NamedNode XsdNonNegativeInteger { get; } = new(Vocabulary.Xsd.NonNegativeInteger);

        public void Emit(RdfTerm subject, NamedNode predicate, RdfTerm @object)
        {
            Quads.Add(new Quad(subject, predicate, @object, Graph: null));
        }

        private void Emit(RdfTerm subject, Utf8String predicate, RdfTerm @object)
        {
            Emit(subject, new NamedNode(predicate), @object);
        }

        private BlankNode Fresh()
        {
            return new BlankNode(Utf8String.FromString($"owlmap{NextBlank++}"));
        }

        /// <summary>Serialises one axiom.</summary>
        public void EmitAxiom(OwlAxiom axiom)
        {
            switch(axiom)
            {
                case OwlDeclarationAxiom declaration:
                {
                    Utf8String kind = declaration.Kind switch
                    {
                        OwlEntityKind.Class => OwlVocabulary.ClassTerm,
                        OwlEntityKind.Datatype => RdfVocabulary.Rdfs.Datatype,
                        OwlEntityKind.ObjectProperty => OwlVocabulary.ObjectPropertyTerm,
                        OwlEntityKind.DataProperty => OwlVocabulary.DatatypeProperty,
                        OwlEntityKind.AnnotationProperty => OwlVocabulary.AnnotationPropertyTerm,
                        _ => OwlVocabulary.NamedIndividual
                    };

                    Emit(declaration.Entity, RdfType, new NamedNode(kind));
                    break;
                }
                case OwlSubClassOfAxiom subClass:
                {
                    Emit(ClassNode(subClass.SubClass), RdfsSubClassOf, ClassNode(subClass.SuperClass));
                    break;
                }
                case OwlEquivalentClassesAxiom equivalent:
                {
                    Emit(ClassNode(equivalent.First), OwlVocabulary.EquivalentClass, ClassNode(equivalent.Second));
                    break;
                }
                case OwlDisjointClassesAxiom { Operands.Count: 2 } disjointPair:
                {
                    Emit(ClassNode(disjointPair.Operands[0]), OwlVocabulary.DisjointWith, ClassNode(disjointPair.Operands[1]));
                    break;
                }
                case OwlDisjointClassesAxiom disjoint:
                {
                    BlankNode node = Fresh();
                    Emit(node, RdfType, new NamedNode(OwlVocabulary.AllDisjointClasses));
                    Emit(node, OwlVocabulary.Members, EmitList(ClassNodeList(disjoint.Operands)));
                    break;
                }
                case OwlDisjointUnionAxiom disjointUnion:
                {
                    Emit(disjointUnion.Class, OwlVocabulary.DisjointUnionOf, EmitList(ClassNodeList(disjointUnion.Operands)));
                    break;
                }
                case OwlSubObjectPropertyOfAxiom subProperty:
                {
                    Emit(PropertyNode(subProperty.SubProperty), RdfsSubPropertyOf, PropertyNode(subProperty.SuperProperty));
                    break;
                }
                case OwlPropertyChainAxiom chain:
                {
                    List<RdfTerm> links = [];
                    foreach(OwlObjectPropertyExpression link in chain.Chain)
                    {
                        links.Add(PropertyNode(link));
                    }

                    Emit(PropertyNode(chain.SuperProperty), OwlVocabulary.PropertyChainAxiom, EmitList(links));
                    break;
                }
                case OwlEquivalentObjectPropertiesAxiom equivalentProperties:
                {
                    Emit(PropertyNode(equivalentProperties.First), OwlVocabulary.EquivalentProperty, PropertyNode(equivalentProperties.Second));
                    break;
                }
                case OwlDisjointObjectPropertiesAxiom { Operands.Count: 2 } disjointPropertyPair:
                {
                    Emit(PropertyNode(disjointPropertyPair.Operands[0]), OwlVocabulary.PropertyDisjointWith, PropertyNode(disjointPropertyPair.Operands[1]));
                    break;
                }
                case OwlDisjointObjectPropertiesAxiom disjointProperties:
                {
                    BlankNode node = Fresh();
                    Emit(node, RdfType, new NamedNode(OwlVocabulary.AllDisjointProperties));
                    List<RdfTerm> members = [];
                    foreach(OwlObjectPropertyExpression property in disjointProperties.Operands)
                    {
                        members.Add(PropertyNode(property));
                    }

                    Emit(node, OwlVocabulary.Members, EmitList(members));
                    break;
                }
                case OwlInverseObjectPropertiesAxiom inverse:
                {
                    Emit(PropertyNode(inverse.First), OwlVocabulary.InverseOf, PropertyNode(inverse.Second));
                    break;
                }
                case OwlObjectPropertyDomainAxiom domain:
                {
                    Emit(PropertyNode(domain.Property), RdfsDomain, ClassNode(domain.Domain));
                    break;
                }
                case OwlObjectPropertyRangeAxiom range:
                {
                    Emit(PropertyNode(range.Property), RdfsRange, ClassNode(range.Range));
                    break;
                }
                case OwlObjectPropertyCharacteristicAxiom characteristic:
                {
                    Utf8String kind = characteristic.Characteristic switch
                    {
                        OwlPropertyCharacteristic.Functional => OwlVocabulary.FunctionalProperty,
                        OwlPropertyCharacteristic.InverseFunctional => OwlVocabulary.InverseFunctionalProperty,
                        OwlPropertyCharacteristic.Transitive => OwlVocabulary.TransitiveProperty,
                        OwlPropertyCharacteristic.Symmetric => OwlVocabulary.SymmetricProperty,
                        OwlPropertyCharacteristic.Asymmetric => OwlVocabulary.AsymmetricProperty,
                        OwlPropertyCharacteristic.Reflexive => OwlVocabulary.ReflexiveProperty,
                        _ => OwlVocabulary.IrreflexiveProperty
                    };

                    Emit(PropertyNode(characteristic.Property), RdfType, new NamedNode(kind));
                    break;
                }
                case OwlSubDataPropertyOfAxiom subDataProperty:
                {
                    Emit(subDataProperty.SubProperty, RdfsSubPropertyOf, subDataProperty.SuperProperty);
                    break;
                }
                case OwlEquivalentDataPropertiesAxiom equivalentDataProperties:
                {
                    Emit(equivalentDataProperties.First, OwlVocabulary.EquivalentProperty, equivalentDataProperties.Second);
                    break;
                }
                case OwlDisjointDataPropertiesAxiom { Operands.Count: 2 } disjointDataPair:
                {
                    Emit(disjointDataPair.Operands[0], OwlVocabulary.PropertyDisjointWith, disjointDataPair.Operands[1]);
                    break;
                }
                case OwlDisjointDataPropertiesAxiom disjointDataProperties:
                {
                    BlankNode node = Fresh();
                    Emit(node, RdfType, new NamedNode(OwlVocabulary.AllDisjointProperties));
                    List<RdfTerm> members = [.. disjointDataProperties.Operands];
                    Emit(node, OwlVocabulary.Members, EmitList(members));
                    break;
                }
                case OwlDataPropertyDomainAxiom dataDomain:
                {
                    Emit(dataDomain.Property, RdfsDomain, ClassNode(dataDomain.Domain));
                    break;
                }
                case OwlDataPropertyRangeAxiom dataRange:
                {
                    Emit(dataRange.Property, RdfsRange, RangeNode(dataRange.Range));
                    break;
                }
                case OwlFunctionalDataPropertyAxiom functionalData:
                {
                    Emit(functionalData.Property, RdfType, new NamedNode(OwlVocabulary.FunctionalProperty));
                    break;
                }
                case OwlDatatypeDefinitionAxiom definition:
                {
                    Emit(definition.Datatype, OwlVocabulary.EquivalentClass, RangeNode(definition.Range));
                    break;
                }
                case OwlHasKeyAxiom hasKey:
                {
                    List<RdfTerm> keys = [];
                    foreach(OwlObjectPropertyExpression objectKey in hasKey.ObjectProperties)
                    {
                        keys.Add(PropertyNode(objectKey));
                    }

                    foreach(NamedNode dataKey in hasKey.DataProperties)
                    {
                        keys.Add(dataKey);
                    }

                    Emit(ClassNode(hasKey.Class), OwlVocabulary.HasKey, EmitList(keys));
                    break;
                }
                case OwlClassAssertionAxiom assertion:
                {
                    Emit(assertion.Individual, RdfType, ClassNode(assertion.Class));
                    break;
                }
                case OwlObjectPropertyAssertionAxiom objectAssertion:
                {
                    Emit(objectAssertion.Source, objectAssertion.Property, objectAssertion.Target);
                    break;
                }
                case OwlNegativeObjectPropertyAssertionAxiom negativeObject:
                {
                    BlankNode node = Fresh();
                    Emit(node, RdfType, new NamedNode(OwlVocabulary.NegativePropertyAssertion));
                    Emit(node, OwlVocabulary.SourceIndividual, negativeObject.Source);
                    Emit(node, OwlVocabulary.AssertionProperty, PropertyNode(negativeObject.Property));
                    Emit(node, OwlVocabulary.TargetIndividual, negativeObject.Target);
                    break;
                }
                case OwlDataPropertyAssertionAxiom dataAssertion:
                {
                    Emit(dataAssertion.Source, dataAssertion.Property, dataAssertion.Target);
                    break;
                }
                case OwlNegativeDataPropertyAssertionAxiom negativeData:
                {
                    BlankNode node = Fresh();
                    Emit(node, RdfType, new NamedNode(OwlVocabulary.NegativePropertyAssertion));
                    Emit(node, OwlVocabulary.SourceIndividual, negativeData.Source);
                    Emit(node, OwlVocabulary.AssertionProperty, negativeData.Property);
                    Emit(node, OwlVocabulary.TargetValue, negativeData.Target);
                    break;
                }
                case OwlSameIndividualAxiom same:
                {
                    Emit(same.First, OwlVocabulary.SameAs, same.Second);
                    break;
                }
                case OwlDifferentIndividualsAxiom different:
                {
                    for(int i = 0; i < different.Individuals.Count; i++)
                    {
                        for(int j = i + 1; j < different.Individuals.Count; j++)
                        {
                            Emit(different.Individuals[i], OwlVocabulary.DifferentFrom, different.Individuals[j]);
                        }
                    }

                    break;
                }
                case OwlAnnotationAssertionAxiom annotation:
                {
                    Emit(annotation.Subject, annotation.Property, annotation.Value);
                    break;
                }
                case OwlSubAnnotationPropertyOfAxiom subAnnotation:
                {
                    Emit(subAnnotation.SubProperty, RdfsSubPropertyOf, subAnnotation.SuperProperty);
                    break;
                }
                case OwlAnnotationPropertyDomainAxiom annotationDomain:
                {
                    Emit(annotationDomain.Property, RdfsDomain, annotationDomain.Domain);
                    break;
                }
                case OwlAnnotationPropertyRangeAxiom annotationRange:
                {
                    Emit(annotationRange.Property, RdfsRange, annotationRange.Range);
                    break;
                }
                case OwlImportAxiom import:
                {
                    Emit(Fresh(), new NamedNode(OwlVocabulary.Imports), import.Imported);
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        //----- Expression serialisation -----

        /// <summary>The node a class expression denotes — the IRI for a reference, a structured blank node otherwise. Nested expressions walk post-order with an explicit stack.</summary>
        private RdfTerm ClassNode(OwlClassExpression root)
        {
            if(root is OwlClassReference rootReference)
            {
                return rootReference.Class;
            }

            if(ClassNodes.TryGetValue(root, out RdfTerm? cached))
            {
                return cached;
            }

            Stack<OwlClassExpression> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlClassExpression node = work.Peek();
                if(ClassNodes.ContainsKey(node) || node is OwlClassReference)
                {
                    work.Pop();

                    continue;
                }

                List<OwlClassExpression> pendingChildren = [];
                foreach(OwlClassExpression child in ClassChildren(node))
                {
                    if(child is not OwlClassReference && !ClassNodes.ContainsKey(child))
                    {
                        pendingChildren.Add(child);
                    }
                }

                if(pendingChildren.Count > 0)
                {
                    foreach(OwlClassExpression child in pendingChildren)
                    {
                        work.Push(child);
                    }

                    continue;
                }

                ClassNodes[node] = ConstructClassNode(node);
                work.Pop();
            }

            return ClassNodes[root];
        }

        private static IEnumerable<OwlClassExpression> ClassChildren(OwlClassExpression node)
        {
            switch(node)
            {
                case OwlObjectIntersectionOf intersection:
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        yield return operand;
                    }

                    break;
                }
                case OwlObjectUnionOf union:
                {
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        yield return operand;
                    }

                    break;
                }
                case OwlObjectComplementOf complement:
                {
                    yield return complement.Operand;
                    break;
                }
                case OwlObjectSomeValuesFrom someValues:
                {
                    yield return someValues.Filler;
                    break;
                }
                case OwlObjectAllValuesFrom allValues:
                {
                    yield return allValues.Filler;
                    break;
                }
                case OwlObjectCardinality { Filler: OwlClassExpression filler }:
                {
                    yield return filler;
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        //Constructs the blank node and structure triples for an expression
        //whose children already have nodes.
        private BlankNode ConstructClassNode(OwlClassExpression node)
        {
            BlankNode subject = Fresh();

            switch(node)
            {
                case OwlObjectIntersectionOf intersection:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.ClassTerm));
                    Emit(subject, OwlVocabulary.IntersectionOf, EmitList(ClassNodeList(intersection.Operands)));
                    break;
                }
                case OwlObjectUnionOf union:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.ClassTerm));
                    Emit(subject, OwlVocabulary.UnionOf, EmitList(ClassNodeList(union.Operands)));
                    break;
                }
                case OwlObjectComplementOf complement:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.ClassTerm));
                    Emit(subject, OwlVocabulary.ComplementOf, ClassNode(complement.Operand));
                    break;
                }
                case OwlObjectOneOf oneOf:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.ClassTerm));
                    Emit(subject, OwlVocabulary.OneOf, EmitList([.. oneOf.Individuals]));
                    break;
                }
                case OwlObjectSomeValuesFrom someValues:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, PropertyNode(someValues.Property));
                    Emit(subject, OwlVocabulary.SomeValuesFrom, ClassNode(someValues.Filler));
                    break;
                }
                case OwlObjectAllValuesFrom allValues:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, PropertyNode(allValues.Property));
                    Emit(subject, OwlVocabulary.AllValuesFrom, ClassNode(allValues.Filler));
                    break;
                }
                case OwlObjectHasValue hasValue:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, PropertyNode(hasValue.Property));
                    Emit(subject, OwlVocabulary.HasValue, hasValue.Individual);
                    break;
                }
                case OwlObjectHasSelf hasSelf:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, PropertyNode(hasSelf.Property));
                    Emit(subject, new NamedNode(OwlVocabulary.HasSelf), new Literal(Utf8String.FromString("true"), XsdBoolean));
                    break;
                }
                case OwlObjectCardinality cardinality:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, PropertyNode(cardinality.Property));

                    Utf8String predicate = (cardinality.Kind, cardinality.Filler is not null) switch
                    {
                        (OwlCardinalityKind.Min, false) => OwlVocabulary.MinCardinality,
                        (OwlCardinalityKind.Max, false) => OwlVocabulary.MaxCardinality,
                        (OwlCardinalityKind.Exact, false) => OwlVocabulary.Cardinality,
                        (OwlCardinalityKind.Min, true) => OwlVocabulary.MinQualifiedCardinality,
                        (OwlCardinalityKind.Max, true) => OwlVocabulary.MaxQualifiedCardinality,
                        _ => OwlVocabulary.QualifiedCardinality
                    };

                    Emit(subject, predicate, new Literal(Utf8String.FromString(cardinality.Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture)), XsdNonNegativeInteger));
                    if(cardinality.Filler is OwlClassExpression qualifier)
                    {
                        Emit(subject, OwlVocabulary.OnClass, ClassNode(qualifier));
                    }

                    break;
                }
                case OwlDataSomeValuesFrom dataSome:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, dataSome.Properties[0]);
                    Emit(subject, OwlVocabulary.SomeValuesFrom, RangeNode(dataSome.Range));
                    break;
                }
                case OwlDataAllValuesFrom dataAll:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, dataAll.Properties[0]);
                    Emit(subject, OwlVocabulary.AllValuesFrom, RangeNode(dataAll.Range));
                    break;
                }
                case OwlDataHasValue dataValue:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, dataValue.Property);
                    Emit(subject, OwlVocabulary.HasValue, dataValue.Value);
                    break;
                }
                case OwlDataCardinality dataCardinality:
                {
                    Emit(subject, RdfType, new NamedNode(OwlVocabulary.Restriction));
                    Emit(subject, OwlVocabulary.OnProperty, dataCardinality.Property);

                    Utf8String predicate = (dataCardinality.Kind, dataCardinality.Range is not null) switch
                    {
                        (OwlCardinalityKind.Min, false) => OwlVocabulary.MinCardinality,
                        (OwlCardinalityKind.Max, false) => OwlVocabulary.MaxCardinality,
                        (OwlCardinalityKind.Exact, false) => OwlVocabulary.Cardinality,
                        (OwlCardinalityKind.Min, true) => OwlVocabulary.MinQualifiedCardinality,
                        (OwlCardinalityKind.Max, true) => OwlVocabulary.MaxQualifiedCardinality,
                        _ => OwlVocabulary.QualifiedCardinality
                    };

                    Emit(subject, predicate, new Literal(Utf8String.FromString(dataCardinality.Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture)), XsdNonNegativeInteger));
                    if(dataCardinality.Range is OwlDataRange rangeQualifier)
                    {
                        Emit(subject, OwlVocabulary.OnDataRange, RangeNode(rangeQualifier));
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }

            return subject;
        }

        /// <summary>The node a data range denotes. Data ranges nest shallowly; the walk still avoids the call stack via the same cache-and-revisit pattern.</summary>
        private RdfTerm RangeNode(OwlDataRange root)
        {
            if(root is OwlDatatypeReference rootReference)
            {
                return rootReference.Datatype;
            }

            if(RangeNodes.TryGetValue(root, out RdfTerm? cached))
            {
                return cached;
            }

            Stack<OwlDataRange> work = new();
            work.Push(root);

            while(work.Count > 0)
            {
                OwlDataRange node = work.Peek();
                if(RangeNodes.ContainsKey(node) || node is OwlDatatypeReference)
                {
                    work.Pop();

                    continue;
                }

                List<OwlDataRange> pending = [];
                foreach(OwlDataRange child in RangeChildren(node))
                {
                    if(child is not OwlDatatypeReference && !RangeNodes.ContainsKey(child))
                    {
                        pending.Add(child);
                    }
                }

                if(pending.Count > 0)
                {
                    foreach(OwlDataRange child in pending)
                    {
                        work.Push(child);
                    }

                    continue;
                }

                RangeNodes[node] = ConstructRangeNode(node);
                work.Pop();
            }

            return RangeNodes[root];
        }

        private static IEnumerable<OwlDataRange> RangeChildren(OwlDataRange node)
        {
            switch(node)
            {
                case OwlDataIntersectionOf intersection:
                {
                    foreach(OwlDataRange operand in intersection.Ranges)
                    {
                        yield return operand;
                    }

                    break;
                }
                case OwlDataUnionOf union:
                {
                    foreach(OwlDataRange operand in union.Ranges)
                    {
                        yield return operand;
                    }

                    break;
                }
                case OwlDataComplementOf complement:
                {
                    yield return complement.Range;
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        private BlankNode ConstructRangeNode(OwlDataRange node)
        {
            BlankNode subject = Fresh();
            Emit(subject, RdfType, new NamedNode(RdfVocabulary.Rdfs.Datatype));

            switch(node)
            {
                case OwlDataIntersectionOf intersection:
                {
                    List<RdfTerm> members = [];
                    foreach(OwlDataRange operand in intersection.Ranges)
                    {
                        members.Add(RangeNode(operand));
                    }

                    Emit(subject, OwlVocabulary.IntersectionOf, EmitList(members));
                    break;
                }
                case OwlDataUnionOf union:
                {
                    List<RdfTerm> members = [];
                    foreach(OwlDataRange operand in union.Ranges)
                    {
                        members.Add(RangeNode(operand));
                    }

                    Emit(subject, OwlVocabulary.UnionOf, EmitList(members));
                    break;
                }
                case OwlDataComplementOf complement:
                {
                    Emit(subject, OwlVocabulary.DatatypeComplementOf, RangeNode(complement.Range));
                    break;
                }
                case OwlDataOneOf oneOf:
                {
                    Emit(subject, OwlVocabulary.OneOf, EmitList([.. oneOf.Literals]));
                    break;
                }
                case OwlDatatypeRestriction restriction:
                {
                    Emit(subject, OwlVocabulary.OnDatatype, restriction.Datatype);
                    List<RdfTerm> facets = [];
                    foreach(OwlFacetRestriction facet in restriction.Restrictions)
                    {
                        BlankNode facetNode = Fresh();
                        Emit(facetNode, facet.Facet, facet.Value);
                        facets.Add(facetNode);
                    }

                    Emit(subject, OwlVocabulary.WithRestrictions, EmitList(facets));
                    break;
                }
                default:
                {
                    break;
                }
            }

            return subject;
        }

        private RdfTerm PropertyNode(OwlObjectPropertyExpression property)
        {
            if(!property.IsInverse)
            {
                return property.Property;
            }

            BlankNode node = Fresh();
            Emit(node, OwlVocabulary.InverseOf, property.Property);

            return node;
        }

        private List<RdfTerm> ClassNodeList(IReadOnlyList<OwlClassExpression> expressions)
        {
            List<RdfTerm> nodes = new(expressions.Count);
            foreach(OwlClassExpression expression in expressions)
            {
                nodes.Add(ClassNode(expression));
            }

            return nodes;
        }

        /// <summary>Serialises an RDF collection over the members and returns its head (or <c>rdf:nil</c> for an empty list).</summary>
        private RdfTerm EmitList(List<RdfTerm> members)
        {
            if(members.Count == 0)
            {
                return RdfNil;
            }

            BlankNode head = Fresh();
            BlankNode current = head;
            for(int i = 0; i < members.Count; i++)
            {
                Emit(current, RdfFirst, members[i]);
                if(i == members.Count - 1)
                {
                    Emit(current, RdfRest, RdfNil);
                }
                else
                {
                    BlankNode next = Fresh();
                    Emit(current, RdfRest, next);
                    current = next;
                }
            }

            return head;
        }
    }
}
