using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        //An anonymous ontology maps to a blank-node header (Mapping to RDF
        //Graphs §3.1) — every ontology document carries the header triple.
        RdfTerm ontologyNode = document.OntologyIri is NamedNode ontology ? ontology : context.Fresh();
        context.Emit(ontologyNode, context.RdfType, context.OwlOntology);

        foreach(OwlAxiom axiom in document.Axioms)
        {
            context.EmitAxiom(axiom);
        }

        return ResolveMintCollisions(context);
    }

    /// <summary>
    /// Replaces any writer-minted blank node whose counter label a document individual already occupies with a
    /// continuation-label copy, so minted expression structure never conflates with an input anonymous
    /// individual inside the mapped document. The check runs over the finished quads, so every input label the
    /// output carries is seen, whichever axiom position carried it; a document with no colliding label returns
    /// the quads unchanged.
    /// </summary>
    /// <param name="context">The finished writer run.</param>
    /// <returns>The mapped quads, with colliding minted nodes relabelled.</returns>
    private static List<Quad> ResolveMintCollisions(WriterContext context)
    {
        HashSet<BlankNode> minted = new(ReferenceEqualityComparer.Instance);
        foreach(BlankNode node in context.MintedNodes)
        {
            minted.Add(node);
        }

        HashSet<Utf8String> inputLabels = [];
        foreach(Quad quad in context.Quads)
        {
            if(quad.Subject is BlankNode subject && !minted.Contains(subject))
            {
                inputLabels.Add(subject.Label);
            }

            if(quad.Object is BlankNode @object && !minted.Contains(@object))
            {
                inputLabels.Add(@object.Label);
            }
        }

        Dictionary<BlankNode, BlankNode> replacements = new(ReferenceEqualityComparer.Instance);
        int mintedCount = context.MintedNodes.Count;
        for(int i = 0; i < mintedCount; i++)
        {
            BlankNode node = context.MintedNodes[i];
            if(!inputLabels.Contains(node.Label))
            {
                continue;
            }

            BlankNode fresh = context.Fresh();
            while(inputLabels.Contains(fresh.Label))
            {
                fresh = context.Fresh();
            }

            replacements[node] = fresh;
        }

        if(replacements.Count == 0)
        {
            return context.Quads;
        }

        List<Quad> resolved = new(context.Quads.Count);
        foreach(Quad quad in context.Quads)
        {
            RdfTerm subject = quad.Subject is BlankNode subjectBlank && replacements.TryGetValue(subjectBlank, out BlankNode? subjectFresh) ? subjectFresh : quad.Subject;
            RdfTerm @object = quad.Object is BlankNode objectBlank && replacements.TryGetValue(objectBlank, out BlankNode? objectFresh) ? objectFresh : quad.Object;
            resolved.Add(new Quad(subject, quad.Predicate, @object, Graph: null));
        }

        return resolved;
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

        public Quad Emit(RdfTerm subject, NamedNode predicate, RdfTerm @object)
        {
            Quad quad = new(subject, predicate, @object, Graph: null);
            Quads.Add(quad);

            return quad;
        }

        private Quad Emit(RdfTerm subject, Utf8String predicate, RdfTerm @object)
        {
            return Emit(subject, new NamedNode(predicate), @object);
        }

        /// <summary>Every blank node this writer minted, in mint order — the set the collision resolution checks against the document's own labels.</summary>
        public List<BlankNode> MintedNodes { get; } = [];

        /// <summary>Mints a fresh blank node and records it as writer-minted.</summary>
        /// <returns>The blank node.</returns>
        public BlankNode Fresh()
        {
            BlankNode node = new(Utf8Strings.From($"owlmap{NextBlank++}"));
            MintedNodes.Add(node);

            return node;
        }

        //The annotation anchor one axiom's serialisation hands back: the
        //root triple an owl:Axiom block reifies, or the reification node
        //(AllDisjoint*, NegativePropertyAssertion, AllDifferent) that
        //carries annotations directly.
        private readonly record struct EmitResult(Quad? RootTriple, RdfTerm? AnnotationNode);

        /// <summary>Serialises one axiom, its annotations included: a triple-form axiom's annotations reify through an <c>owl:Axiom</c> block, a node-form axiom carries them on its node.</summary>
        /// <param name="axiom">The axiom to serialise.</param>
        public void EmitAxiom(OwlAxiom axiom)
        {
            EmitResult anchor = EmitAxiomCore(axiom);
            if(axiom.Annotations.Length == 0)
            {
                return;
            }

            if(anchor.AnnotationNode is RdfTerm node)
            {
                EmitAnnotations(node, axiom.Annotations);
            }
            else if(anchor.RootTriple is Quad root)
            {
                BlankNode reification = Fresh();
                Emit(reification, RdfType, new NamedNode(OwlVocabulary.AxiomTerm));
                Emit(reification, OwlVocabulary.AnnotatedSource, root.Subject);
                Emit(reification, OwlVocabulary.AnnotatedProperty, root.Predicate);
                Emit(reification, OwlVocabulary.AnnotatedTarget, root.Object);
                EmitAnnotations(reification, axiom.Annotations);
            }
        }

        //Annotations on annotations reify through owl:Annotation blocks;
        //the walk is an explicit stack, no recursion.
        private void EmitAnnotations(RdfTerm node, ImmutableArray<OwlAnnotation> annotations)
        {
            Stack<(RdfTerm Node, OwlAnnotation Annotation)> work = new();
            for(int i = annotations.Length - 1; i >= 0; i--)
            {
                work.Push((node, annotations[i]));
            }

            while(work.Count > 0)
            {
                (RdfTerm parent, OwlAnnotation annotation) = work.Pop();
                Emit(parent, annotation.Property, annotation.Value);

                if(annotation.Annotations.Length == 0)
                {
                    continue;
                }

                BlankNode reification = Fresh();
                Emit(reification, RdfType, new NamedNode(OwlVocabulary.AnnotationTerm));
                Emit(reification, OwlVocabulary.AnnotatedSource, parent);
                Emit(reification, OwlVocabulary.AnnotatedProperty, annotation.Property);
                Emit(reification, OwlVocabulary.AnnotatedTarget, annotation.Value);
                for(int i = annotation.Annotations.Length - 1; i >= 0; i--)
                {
                    work.Push((reification, annotation.Annotations[i]));
                }
            }
        }

        private EmitResult EmitAxiomCore(OwlAxiom axiom)
        {
            return axiom switch
            {
                OwlDeclarationAxiom declaration => Root(Emit(declaration.Entity, RdfType, new NamedNode(DeclarationKindClass(declaration.Kind)))),
                OwlSubClassOfAxiom subClass => Root(Emit(ClassNode(subClass.SubClass), RdfsSubClassOf, ClassNode(subClass.SuperClass))),
                OwlEquivalentClassesAxiom equivalent => Root(Emit(ClassNode(equivalent.First), OwlVocabulary.EquivalentClass, ClassNode(equivalent.Second))),
                OwlDisjointClassesAxiom { Operands.Count: 2 } disjointPair => Root(Emit(ClassNode(disjointPair.Operands[0]), OwlVocabulary.DisjointWith, ClassNode(disjointPair.Operands[1]))),
                OwlDisjointClassesAxiom disjoint => Node(EmitMemberedNode(OwlVocabulary.AllDisjointClasses, ClassNodeList(disjoint.Operands))),
                OwlDisjointUnionAxiom disjointUnion => Root(Emit(disjointUnion.Class, OwlVocabulary.DisjointUnionOf, EmitList(ClassNodeList(disjointUnion.Operands)))),
                OwlSubObjectPropertyOfAxiom subProperty => Root(Emit(PropertyNode(subProperty.SubProperty), RdfsSubPropertyOf, PropertyNode(subProperty.SuperProperty))),
                OwlPropertyChainAxiom chain => Root(Emit(PropertyNode(chain.SuperProperty), OwlVocabulary.PropertyChainAxiom, EmitList(PropertyNodeList(chain.Chain)))),
                OwlEquivalentObjectPropertiesAxiom equivalentProperties => Root(Emit(PropertyNode(equivalentProperties.First), OwlVocabulary.EquivalentProperty, PropertyNode(equivalentProperties.Second))),
                OwlDisjointObjectPropertiesAxiom { Operands.Count: 2 } disjointPropertyPair => Root(Emit(PropertyNode(disjointPropertyPair.Operands[0]), OwlVocabulary.PropertyDisjointWith, PropertyNode(disjointPropertyPair.Operands[1]))),
                OwlDisjointObjectPropertiesAxiom disjointProperties => Node(EmitMemberedNode(OwlVocabulary.AllDisjointProperties, PropertyNodeList(disjointProperties.Operands))),
                OwlInverseObjectPropertiesAxiom inverse => Root(Emit(PropertyNode(inverse.First), OwlVocabulary.InverseOf, PropertyNode(inverse.Second))),
                OwlObjectPropertyDomainAxiom domain => Root(Emit(PropertyNode(domain.Property), RdfsDomain, ClassNode(domain.Domain))),
                OwlObjectPropertyRangeAxiom range => Root(Emit(PropertyNode(range.Property), RdfsRange, ClassNode(range.Range))),
                OwlObjectPropertyCharacteristicAxiom characteristic => Root(Emit(PropertyNode(characteristic.Property), RdfType, new NamedNode(CharacteristicClass(characteristic.Characteristic)))),
                OwlSubDataPropertyOfAxiom subDataProperty => Root(Emit(subDataProperty.SubProperty, RdfsSubPropertyOf, subDataProperty.SuperProperty)),
                OwlEquivalentDataPropertiesAxiom equivalentDataProperties => Root(Emit(equivalentDataProperties.First, OwlVocabulary.EquivalentProperty, equivalentDataProperties.Second)),
                OwlDisjointDataPropertiesAxiom { Operands.Count: 2 } disjointDataPair => Root(Emit(disjointDataPair.Operands[0], OwlVocabulary.PropertyDisjointWith, disjointDataPair.Operands[1])),
                OwlDisjointDataPropertiesAxiom disjointDataProperties => Node(EmitMemberedNode(OwlVocabulary.AllDisjointProperties, [.. disjointDataProperties.Operands])),
                OwlDataPropertyDomainAxiom dataDomain => Root(Emit(dataDomain.Property, RdfsDomain, ClassNode(dataDomain.Domain))),
                OwlDataPropertyRangeAxiom dataRange => Root(Emit(dataRange.Property, RdfsRange, RangeNode(dataRange.Range))),
                OwlFunctionalDataPropertyAxiom functionalData => Root(Emit(functionalData.Property, RdfType, new NamedNode(OwlVocabulary.FunctionalProperty))),
                OwlDatatypeDefinitionAxiom definition => Root(Emit(definition.Datatype, OwlVocabulary.EquivalentClass, RangeNode(definition.Range))),
                OwlHasKeyAxiom hasKey => Root(Emit(ClassNode(hasKey.Class), OwlVocabulary.HasKey, EmitList(KeyNodeList(hasKey)))),
                OwlClassAssertionAxiom assertion => Root(Emit(assertion.Individual, RdfType, ClassNode(assertion.Class))),
                OwlObjectPropertyAssertionAxiom objectAssertion => Root(Emit(objectAssertion.Source, objectAssertion.Property, objectAssertion.Target)),
                OwlNegativeObjectPropertyAssertionAxiom negativeObject => Node(EmitNegativeAssertion(negativeObject.Source, PropertyNode(negativeObject.Property), OwlVocabulary.TargetIndividual, negativeObject.Target)),
                OwlDataPropertyAssertionAxiom dataAssertion => Root(Emit(dataAssertion.Source, dataAssertion.Property, dataAssertion.Target)),
                OwlNegativeDataPropertyAssertionAxiom negativeData => Node(EmitNegativeAssertion(negativeData.Source, negativeData.Property, OwlVocabulary.TargetValue, negativeData.Target)),
                OwlSameIndividualAxiom same => Root(Emit(same.First, OwlVocabulary.SameAs, same.Second)),
                OwlDifferentIndividualsAxiom { Individuals.Count: 2 } differentPair => Root(Emit(differentPair.Individuals[0], OwlVocabulary.DifferentFrom, differentPair.Individuals[1])),
                OwlDifferentIndividualsAxiom different => EmitNaryDifferent(different),
                OwlAnnotationAssertionAxiom annotation => Root(Emit(annotation.Subject, annotation.Property, annotation.Value)),
                OwlSubAnnotationPropertyOfAxiom subAnnotation => Root(Emit(subAnnotation.SubProperty, RdfsSubPropertyOf, subAnnotation.SuperProperty)),
                OwlAnnotationPropertyDomainAxiom annotationDomain => Root(Emit(annotationDomain.Property, RdfsDomain, annotationDomain.Domain)),
                OwlAnnotationPropertyRangeAxiom annotationRange => Root(Emit(annotationRange.Property, RdfsRange, annotationRange.Range)),
                OwlImportAxiom import => Root(Emit(Fresh(), new NamedNode(OwlVocabulary.Imports), import.Imported)),
                _ => default
            };
        }

        private static EmitResult Root(Quad triple)
        {
            return new EmitResult(triple, null);
        }

        private static EmitResult Node(RdfTerm node)
        {
            return new EmitResult(null, node);
        }

        private static Utf8String DeclarationKindClass(OwlEntityKind kind)
        {
            return kind switch
            {
                OwlEntityKind.Class => OwlVocabulary.ClassTerm,
                OwlEntityKind.Datatype => RdfVocabulary.Rdfs.Datatype,
                OwlEntityKind.ObjectProperty => OwlVocabulary.ObjectPropertyTerm,
                OwlEntityKind.DataProperty => OwlVocabulary.DatatypeProperty,
                OwlEntityKind.AnnotationProperty => OwlVocabulary.AnnotationPropertyTerm,
                _ => OwlVocabulary.NamedIndividual
            };
        }

        private static Utf8String CharacteristicClass(OwlPropertyCharacteristic characteristic)
        {
            return characteristic switch
            {
                OwlPropertyCharacteristic.Functional => OwlVocabulary.FunctionalProperty,
                OwlPropertyCharacteristic.InverseFunctional => OwlVocabulary.InverseFunctionalProperty,
                OwlPropertyCharacteristic.Transitive => OwlVocabulary.TransitiveProperty,
                OwlPropertyCharacteristic.Symmetric => OwlVocabulary.SymmetricProperty,
                OwlPropertyCharacteristic.Asymmetric => OwlVocabulary.AsymmetricProperty,
                OwlPropertyCharacteristic.Reflexive => OwlVocabulary.ReflexiveProperty,
                _ => OwlVocabulary.IrreflexiveProperty
            };
        }

        //A reified members node: the AllDisjointClasses/AllDisjointProperties form.
        private BlankNode EmitMemberedNode(Utf8String typeClass, List<RdfTerm> members)
        {
            BlankNode node = Fresh();
            Emit(node, RdfType, new NamedNode(typeClass));
            Emit(node, OwlVocabulary.Members, EmitList(members));

            return node;
        }

        private BlankNode EmitNegativeAssertion(RdfTerm source, RdfTerm property, Utf8String targetPredicate, RdfTerm target)
        {
            BlankNode node = Fresh();
            Emit(node, RdfType, new NamedNode(OwlVocabulary.NegativePropertyAssertion));
            Emit(node, OwlVocabulary.SourceIndividual, source);
            Emit(node, OwlVocabulary.AssertionProperty, property);
            Emit(node, targetPredicate, target);

            return node;
        }

        private EmitResult EmitNaryDifferent(OwlDifferentIndividualsAxiom different)
        {
            for(int i = 0; i < different.Individuals.Count; i++)
            {
                for(int j = i + 1; j < different.Individuals.Count; j++)
                {
                    Emit(different.Individuals[i], OwlVocabulary.DifferentFrom, different.Individuals[j]);
                }
            }

            //An annotated n-ary difference additionally serialises the
            //owl:AllDifferent node the annotations live on.
            return different.Annotations.Length == 0
                ? default
                : Node(EmitMemberedNode(OwlVocabulary.AllDifferent, [.. different.Individuals]));
        }

        private List<RdfTerm> PropertyNodeList(IReadOnlyList<OwlObjectPropertyExpression> properties)
        {
            List<RdfTerm> nodes = new(properties.Count);
            foreach(OwlObjectPropertyExpression property in properties)
            {
                nodes.Add(PropertyNode(property));
            }

            return nodes;
        }

        private List<RdfTerm> KeyNodeList(OwlHasKeyAxiom hasKey)
        {
            List<RdfTerm> keys = PropertyNodeList(hasKey.ObjectProperties);
            foreach(NamedNode dataKey in hasKey.DataProperties)
            {
                keys.Add(dataKey);
            }

            return keys;
        }

        //Expression serialisation.

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
                    Emit(subject, new NamedNode(OwlVocabulary.HasSelf), new Literal(Utf8Strings.From("true"), XsdBoolean));
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

                    Emit(subject, predicate, new Literal(Utf8Strings.From(cardinality.Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture)), XsdNonNegativeInteger));
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

                    Emit(subject, predicate, new Literal(Utf8Strings.From(dataCardinality.Cardinality.ToString(System.Globalization.CultureInfo.InvariantCulture)), XsdNonNegativeInteger));
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
                    if(oneOf.Literals.Count == 0)
                    {
                        //The canonical bottom (an empty enumeration) is a reasoning-internal
                        //normal form the canonicalization layer rewrites empty value spaces to;
                        //it denotes no OWL construct and has no serialisation, so reaching the
                        //writer with one is an invariant violation, never valid input.
                        throw new InvalidOperationException("An empty DataOneOf (the canonical bottom data range) is reasoning-internal and has no RDF serialisation.");
                    }

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
