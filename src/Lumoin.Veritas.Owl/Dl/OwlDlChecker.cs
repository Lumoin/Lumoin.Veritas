using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Dl;

/// <summary>
/// Checks a document against the OWL 2 DL species restrictions
/// (<see href="https://www.w3.org/TR/owl2-syntax/">Syntax</see> §3, §5, §11):
/// the canonical-parsing preconditions (an ontology header and typing triples
/// that disambiguate every entity), the punning limits, the reserved-vocabulary
/// limits, the datatype map, and the global restrictions on the property
/// hierarchy (simple roles in restricted positions, regular chains).
/// </summary>
/// <remarks>
/// <para>
/// Every RDF graph is an OWL 2 Full document, so the species question is
/// one-sided: a document is OWL 2 DL when the reverse mapping reads it
/// completely and the result satisfies the restrictions. The checker
/// consumes both the raw graph (the typing census operates on triples the
/// lenient mapper resolves by guessing) and the mapped document (positions
/// and the property hierarchy live there).
/// </para>
/// <para>
/// Expression trees are walked with explicit stacks; the no-recursion
/// discipline holds.
/// </para>
/// </remarks>
public static class OwlDlChecker
{
    private static byte[] RdfNamespace { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"u8.ToArray();
    private static byte[] RdfsNamespace { get; } = "http://www.w3.org/2000/01/rdf-schema#"u8.ToArray();
    private static byte[] OwlNamespace { get; } = "http://www.w3.org/2002/07/owl#"u8.ToArray();
    private static byte[] XsdNamespace { get; } = "http://www.w3.org/2001/XMLSchema#"u8.ToArray();

    /// <summary>The reserved IRIs usable as classes: the top and bottom class.</summary>
    private static HashSet<Utf8String> ReservedClasses { get; } =
    [
        OwlVocabulary.Thing,
        OwlVocabulary.Nothing,
    ];

    /// <summary>The reserved IRIs usable as object properties: the top and bottom object property.</summary>
    private static HashSet<Utf8String> ReservedObjectProperties { get; } =
    [
        OwlVocabulary.TopObjectProperty,
        OwlVocabulary.BottomObjectProperty,
    ];

    /// <summary>The reserved IRIs usable as data properties: the top and bottom data property.</summary>
    private static HashSet<Utf8String> ReservedDataProperties { get; } =
    [
        OwlVocabulary.TopDataProperty,
        OwlVocabulary.BottomDataProperty,
    ];

    /// <summary>The built-in annotation properties (Syntax §5.5, Table 3).</summary>
    private static HashSet<Utf8String> BuiltinAnnotationProperties { get; } =
    [
        RdfVocabulary.Rdfs.Label,
        RdfVocabulary.Rdfs.Comment,
        RdfVocabulary.Rdfs.SeeAlso,
        RdfVocabulary.Rdfs.IsDefinedBy,
        OwlVocabulary.Deprecated,
        OwlVocabulary.VersionInfo,
        OwlVocabulary.PriorVersion,
        OwlVocabulary.BackwardCompatibleWith,
        OwlVocabulary.IncompatibleWith,
    ];

    /// <summary>The property-characteristic classes whose subjects must be declared object properties.</summary>
    private static HashSet<Utf8String> ObjectCharacteristicClasses { get; } =
    [
        OwlVocabulary.InverseFunctionalProperty,
        OwlVocabulary.TransitiveProperty,
        OwlVocabulary.SymmetricProperty,
        OwlVocabulary.AsymmetricProperty,
        OwlVocabulary.ReflexiveProperty,
        OwlVocabulary.IrreflexiveProperty,
    ];

    /// <summary>
    /// Checks the document against the OWL 2 DL restrictions.
    /// </summary>
    /// <param name="quads">The raw graph the document was mapped from.</param>
    /// <param name="document">The mapped ontology document.</param>
    /// <returns>The species report with every violation found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> or <paramref name="document"/> is <see langword="null"/>.</exception>
    public static OwlDlReport Check(IReadOnlyList<Quad> quads, OwlOntologyDocument document)
    {
        ArgumentNullException.ThrowIfNull(quads);
        ArgumentNullException.ThrowIfNull(document);

        List<OwlDlViolation> violations = [];

        //A graph the mapping could not read as structural OWL 2 cannot be
        //canonically parsed, so it is OWL 2 Full only.
        if(document.Diagnostics.HasErrors)
        {
            violations.Add(new OwlDlViolation(Origin: null, $"Not structurally a well-formed OWL 2 ontology: {DescribeFirstError(document)}"));

            return new OwlDlReport(IsInDl: false, violations);
        }

        CheckOntologyHeader(quads, violations);
        CheckCharacteristicTyping(quads, document, violations);
        CheckPunning(document, violations);

        //Datatype definitions are collected up front: a datatype reference
        //anywhere in the document may resolve against a definition that
        //appears later.
        HashSet<Utf8String> definedDatatypes = [];
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is OwlDatatypeDefinitionAxiom definition)
            {
                definedDatatypes.Add(definition.Datatype.Iri);
            }
        }

        WalkState state = new(document, definedDatatypes, violations);
        foreach(OwlAxiom axiom in document.Axioms)
        {
            CheckAxiom(axiom, state);
        }

        //Global restriction: the properties in cardinality, Self, key,
        //disjointness, (inverse-)functionality, irreflexivity, and asymmetry
        //positions must be simple (Syntax §11.2). Non-simplicity seeds at
        //composite properties (transitive, or a chain superproperty) and
        //propagates upward through the property hierarchy.
        HashSet<Utf8String> nonSimple = PropagateNonSimple(state);
        foreach((Utf8String property, Quad origin, string construct) in state.SimpleRequired)
        {
            if(nonSimple.Contains(property))
            {
                violations.Add(new OwlDlViolation(origin, $"Non-simple property in a {construct} position."));
            }
        }

        CheckChainRegularity(state, violations);

        return new OwlDlReport(violations.Count == 0, violations);
    }

    //Graph-level preconditions.

    private static void CheckOntologyHeader(IReadOnlyList<Quad> quads, List<OwlDlViolation> violations)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && quad.Object is NamedNode type
                && type.Iri.Equals(OwlVocabulary.Ontology))
            {
                return;
            }
        }

        violations.Add(new OwlDlViolation(Origin: null, "No owl:Ontology header."));
    }

    private static void CheckCharacteristicTyping(IReadOnlyList<Quad> quads, OwlOntologyDocument document, List<OwlDlViolation> violations)
    {
        //The reverse mapping needs the property kind to read a
        //characteristic triple; a subject typed only with the
        //characteristic is ambiguous and therefore not canonically
        //parseable. owl:FunctionalProperty admits either kind; the others
        //are object-property characteristics.
        foreach(Quad quad in quads)
        {
            if(!quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type) || quad.Subject is not NamedNode subject || quad.Object is not NamedNode type)
            {
                continue;
            }

            if(type.Iri.Equals(OwlVocabulary.FunctionalProperty))
            {
                if(!document.DeclaredObjectProperties.Contains(subject.Iri) && !document.DeclaredDataProperties.Contains(subject.Iri))
                {
                    violations.Add(new OwlDlViolation(quad, "A property typed owl:FunctionalProperty is declared neither an object nor a data property."));
                }
            }
            else if(ObjectCharacteristicClasses.Contains(type.Iri) && !document.DeclaredObjectProperties.Contains(subject.Iri))
            {
                violations.Add(new OwlDlViolation(quad, "A property carrying an object-property characteristic is not declared an object property."));
            }
        }
    }

    private static void CheckPunning(OwlOntologyDocument document, List<OwlDlViolation> violations)
    {
        //Syntax §5.8.1: class/datatype names are disjoint, and the three
        //property kinds are pairwise disjoint. Class/individual and
        //property/individual punning is legal.
        AddIntersection(document.DeclaredClasses, document.DeclaredDatatypes, "an IRI is declared both a class and a datatype", violations);
        AddIntersection(document.DeclaredObjectProperties, document.DeclaredDataProperties, "an IRI is declared both an object and a data property", violations);
        AddIntersection(document.DeclaredObjectProperties, document.DeclaredAnnotationProperties, "an IRI is declared both an object and an annotation property", violations);
        AddIntersection(document.DeclaredDataProperties, document.DeclaredAnnotationProperties, "an IRI is declared both a data and an annotation property", violations);
    }

    private static void AddIntersection(IReadOnlySet<Utf8String> first, IReadOnlySet<Utf8String> second, string description, List<OwlDlViolation> violations)
    {
        foreach(Utf8String iri in first)
        {
            if(second.Contains(iri) && !IsReserved(iri))
            {
                violations.Add(new OwlDlViolation(Origin: null, $"Illegal punning: {description} ({iri})."));
            }
        }
    }

    //The axiom walk.

    /// <summary>The mutable state one document walk accumulates.</summary>
    private sealed class WalkState
    {
        /// <summary>The document under check.</summary>
        public OwlOntologyDocument Document { get; }

        /// <summary>The datatypes a <c>DatatypeDefinition</c> axiom defines.</summary>
        public HashSet<Utf8String> DefinedDatatypes { get; }

        /// <summary>The violations accumulated so far.</summary>
        public List<OwlDlViolation> Violations { get; }

        /// <summary>The property positions that require a simple property.</summary>
        public List<(Utf8String Property, Quad Origin, string Construct)> SimpleRequired { get; } = [];

        /// <summary>The composite (transitive or chain-super) property IRIs — the non-simplicity seeds.</summary>
        public HashSet<Utf8String> Composite { get; } = [];

        /// <summary>The property-hierarchy edges non-simplicity propagates along (sub → super).</summary>
        public Dictionary<Utf8String, List<Utf8String>> SuperEdges { get; } = [];

        /// <summary>The chain dependency edges (element → superproperty) the regularity order must satisfy.</summary>
        public List<(Utf8String From, Utf8String To)> ChainEdges { get; } = [];

        /// <summary>A representative chain-axiom origin per superproperty, for violation reporting.</summary>
        public Dictionary<Utf8String, Quad> ChainOrigins { get; } = [];

        /// <summary>Initialises the state for one document walk.</summary>
        /// <param name="document">The document under check.</param>
        /// <param name="definedDatatypes">The datatypes defined in the document.</param>
        /// <param name="violations">The shared violation list.</param>
        public WalkState(OwlOntologyDocument document, HashSet<Utf8String> definedDatatypes, List<OwlDlViolation> violations)
        {
            Document = document;
            DefinedDatatypes = definedDatatypes;
            Violations = violations;
        }

        /// <summary>Adds a sub → super hierarchy edge.</summary>
        /// <param name="sub">The subproperty IRI.</param>
        /// <param name="super">The superproperty IRI.</param>
        public void AddEdge(Utf8String sub, Utf8String super)
        {
            if(!SuperEdges.TryGetValue(sub, out List<Utf8String>? targets))
            {
                targets = [];
                SuperEdges[sub] = targets;
            }

            targets.Add(super);
        }
    }

    private static void CheckAxiom(OwlAxiom axiom, WalkState state)
    {
        switch(axiom)
        {
            case OwlDeclarationAxiom declaration:
            {
                CheckDeclaration(declaration, state);
                break;
            }
            case OwlSubClassOfAxiom subClass:
            {
                WalkClass(subClass.SubClass, axiom.Origin, state);
                WalkClass(subClass.SuperClass, axiom.Origin, state);
                break;
            }
            case OwlEquivalentClassesAxiom equivalent:
            {
                WalkClass(equivalent.First, axiom.Origin, state);
                WalkClass(equivalent.Second, axiom.Origin, state);
                break;
            }
            case OwlDisjointClassesAxiom disjoint:
            {
                foreach(OwlClassExpression operand in disjoint.Operands)
                {
                    WalkClass(operand, axiom.Origin, state);
                }

                break;
            }
            case OwlDisjointUnionAxiom disjointUnion:
            {
                CheckClassReference(disjointUnion.Class, axiom.Origin, state);
                foreach(OwlClassExpression operand in disjointUnion.Operands)
                {
                    WalkClass(operand, axiom.Origin, state);
                }

                break;
            }
            case OwlSubObjectPropertyOfAxiom subProperty:
            {
                NamedNode sub = CheckObjectProperty(subProperty.SubProperty, axiom.Origin, state);
                NamedNode super = CheckObjectProperty(subProperty.SuperProperty, axiom.Origin, state);
                state.AddEdge(sub.Iri, super.Iri);
                break;
            }
            case OwlPropertyChainAxiom chain:
            {
                CheckChainAxiom(chain, state);
                break;
            }
            case OwlEquivalentObjectPropertiesAxiom equivalentProperties:
            {
                NamedNode first = CheckObjectProperty(equivalentProperties.First, axiom.Origin, state);
                NamedNode second = CheckObjectProperty(equivalentProperties.Second, axiom.Origin, state);
                state.AddEdge(first.Iri, second.Iri);
                state.AddEdge(second.Iri, first.Iri);
                break;
            }
            case OwlDisjointObjectPropertiesAxiom disjointProperties:
            {
                foreach(OwlObjectPropertyExpression operand in disjointProperties.Operands)
                {
                    NamedNode node = CheckObjectProperty(operand, axiom.Origin, state);
                    state.SimpleRequired.Add((node.Iri, axiom.Origin, "DisjointObjectProperties"));
                }

                break;
            }
            case OwlInverseObjectPropertiesAxiom inverse:
            {
                NamedNode first = CheckObjectProperty(inverse.First, axiom.Origin, state);
                NamedNode second = CheckObjectProperty(inverse.Second, axiom.Origin, state);
                state.AddEdge(first.Iri, second.Iri);
                state.AddEdge(second.Iri, first.Iri);
                break;
            }
            case OwlObjectPropertyDomainAxiom domain:
            {
                CheckObjectProperty(domain.Property, axiom.Origin, state);
                WalkClass(domain.Domain, axiom.Origin, state);
                break;
            }
            case OwlObjectPropertyRangeAxiom range:
            {
                CheckObjectProperty(range.Property, axiom.Origin, state);
                WalkClass(range.Range, axiom.Origin, state);
                break;
            }
            case OwlObjectPropertyCharacteristicAxiom characteristic:
            {
                NamedNode node = CheckObjectProperty(characteristic.Property, axiom.Origin, state);
                switch(characteristic.Characteristic)
                {
                    case OwlPropertyCharacteristic.Transitive:
                    {
                        state.Composite.Add(node.Iri);
                        break;
                    }
                    case OwlPropertyCharacteristic.Functional:
                    case OwlPropertyCharacteristic.InverseFunctional:
                    case OwlPropertyCharacteristic.Irreflexive:
                    case OwlPropertyCharacteristic.Asymmetric:
                    {
                        state.SimpleRequired.Add((node.Iri, axiom.Origin, characteristic.Characteristic.ToString()));
                        break;
                    }
                    default:
                    {
                        break;
                    }
                }

                break;
            }
            case OwlSubDataPropertyOfAxiom subData:
            {
                CheckDataProperty(subData.SubProperty, axiom.Origin, state);
                CheckDataProperty(subData.SuperProperty, axiom.Origin, state);
                break;
            }
            case OwlEquivalentDataPropertiesAxiom equivalentData:
            {
                CheckDataProperty(equivalentData.First, axiom.Origin, state);
                CheckDataProperty(equivalentData.Second, axiom.Origin, state);
                break;
            }
            case OwlDisjointDataPropertiesAxiom disjointData:
            {
                foreach(NamedNode operand in disjointData.Operands)
                {
                    CheckDataProperty(operand, axiom.Origin, state);
                }

                break;
            }
            case OwlDataPropertyDomainAxiom dataDomain:
            {
                CheckDataProperty(dataDomain.Property, axiom.Origin, state);
                WalkClass(dataDomain.Domain, axiom.Origin, state);
                break;
            }
            case OwlDataPropertyRangeAxiom dataRange:
            {
                CheckDataProperty(dataRange.Property, axiom.Origin, state);
                WalkRange(dataRange.Range, axiom.Origin, state);
                break;
            }
            case OwlFunctionalDataPropertyAxiom functionalData:
            {
                CheckDataProperty(functionalData.Property, axiom.Origin, state);
                break;
            }
            case OwlDatatypeDefinitionAxiom definition:
            {
                if(IsReserved(definition.Datatype.Iri))
                {
                    state.Violations.Add(new OwlDlViolation(axiom.Origin, "A datatype definition redefines reserved vocabulary."));
                }

                WalkRange(definition.Range, axiom.Origin, state);
                break;
            }
            case OwlHasKeyAxiom hasKey:
            {
                WalkClass(hasKey.Class, axiom.Origin, state);
                foreach(OwlObjectPropertyExpression property in hasKey.ObjectProperties)
                {
                    NamedNode node = CheckObjectProperty(property, axiom.Origin, state);
                    state.SimpleRequired.Add((node.Iri, axiom.Origin, "HasKey"));
                }

                foreach(NamedNode property in hasKey.DataProperties)
                {
                    CheckDataProperty(property, axiom.Origin, state);
                }

                break;
            }
            case OwlClassAssertionAxiom classAssertion:
            {
                //An anonymous individual typed owl:NamedIndividual surfaces
                //as a class assertion on the typing class; the corpus reads
                //it as individual typing, not as a use of reserved
                //vocabulary in class position.
                if(classAssertion.Class is not OwlClassReference { Class.Iri: var classIri } || !classIri.Equals(OwlVocabulary.NamedIndividual))
                {
                    WalkClass(classAssertion.Class, axiom.Origin, state);
                }

                CheckIndividual(classAssertion.Individual, axiom.Origin, state);
                break;
            }
            case OwlObjectPropertyAssertionAxiom objectAssertion:
            {
                CheckObjectPropertyIri(objectAssertion.Property, axiom.Origin, state);
                CheckIndividual(objectAssertion.Source, axiom.Origin, state);
                CheckIndividual(objectAssertion.Target, axiom.Origin, state);
                break;
            }
            case OwlNegativeObjectPropertyAssertionAxiom negativeObject:
            {
                CheckObjectProperty(negativeObject.Property, axiom.Origin, state);
                CheckIndividual(negativeObject.Source, axiom.Origin, state);
                CheckIndividual(negativeObject.Target, axiom.Origin, state);
                break;
            }
            case OwlDataPropertyAssertionAxiom dataAssertion:
            {
                CheckDataProperty(dataAssertion.Property, axiom.Origin, state);
                CheckIndividual(dataAssertion.Source, axiom.Origin, state);
                CheckLiteral(dataAssertion.Target, axiom.Origin, state);
                break;
            }
            case OwlNegativeDataPropertyAssertionAxiom negativeData:
            {
                CheckDataProperty(negativeData.Property, axiom.Origin, state);
                CheckIndividual(negativeData.Source, axiom.Origin, state);
                CheckLiteral(negativeData.Target, axiom.Origin, state);
                break;
            }
            case OwlSameIndividualAxiom same:
            {
                CheckIndividual(same.First, axiom.Origin, state);
                CheckIndividual(same.Second, axiom.Origin, state);
                break;
            }
            case OwlDifferentIndividualsAxiom different:
            {
                foreach(RdfTerm individual in different.Individuals)
                {
                    CheckIndividual(individual, axiom.Origin, state);
                }

                break;
            }
            case OwlAnnotationAssertionAxiom annotation:
            {
                CheckAnnotationProperty(annotation.Property, axiom.Origin, state);
                break;
            }
            case OwlSubAnnotationPropertyOfAxiom subAnnotation:
            {
                CheckAnnotationProperty(subAnnotation.SubProperty, axiom.Origin, state);
                CheckAnnotationProperty(subAnnotation.SuperProperty, axiom.Origin, state);
                break;
            }
            case OwlAnnotationPropertyDomainAxiom annotationDomain:
            {
                CheckAnnotationProperty(annotationDomain.Property, axiom.Origin, state);
                break;
            }
            case OwlAnnotationPropertyRangeAxiom annotationRange:
            {
                CheckAnnotationProperty(annotationRange.Property, axiom.Origin, state);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    private static void CheckChainAxiom(OwlPropertyChainAxiom chain, WalkState state)
    {
        NamedNode super = CheckObjectProperty(chain.SuperProperty, chain.Origin, state);

        //A length-one chain is an ordinary subproperty axiom; a longer one
        //makes the superproperty composite, and its elements form the
        //dependency edges the regularity order must satisfy (Syntax §11.1):
        //the superproperty may appear only first or last in its own chain.
        if(chain.Chain.Count == 1)
        {
            NamedNode sub = CheckObjectProperty(chain.Chain[0], chain.Origin, state);
            state.AddEdge(sub.Iri, super.Iri);

            return;
        }

        state.Composite.Add(super.Iri);
        state.ChainOrigins.TryAdd(super.Iri, chain.Origin);

        for(int i = 0; i < chain.Chain.Count; i++)
        {
            NamedNode element = CheckObjectProperty(chain.Chain[i], chain.Origin, state);
            if(element.Iri.Equals(super.Iri))
            {
                if(i != 0 && i != chain.Chain.Count - 1)
                {
                    state.Violations.Add(new OwlDlViolation(chain.Origin, "A property chain uses its superproperty in a middle position; the hierarchy is not regular."));
                }
            }
            else
            {
                state.ChainEdges.Add((element.Iri, super.Iri));
            }
        }
    }

    private static void CheckDeclaration(OwlDeclarationAxiom declaration, WalkState state)
    {
        //Reserved vocabulary may be declared only as the built-in it is.
        Utf8String iri = declaration.Entity.Iri;
        if(!IsReserved(iri))
        {
            return;
        }

        bool allowed = declaration.Kind switch
        {
            OwlEntityKind.Class => ReservedClasses.Contains(iri),
            OwlEntityKind.Datatype => OwlDatatypeMap.Dl.Contains(iri),
            OwlEntityKind.ObjectProperty => ReservedObjectProperties.Contains(iri),
            OwlEntityKind.DataProperty => ReservedDataProperties.Contains(iri),
            OwlEntityKind.AnnotationProperty => BuiltinAnnotationProperties.Contains(iri),
            OwlEntityKind.NamedIndividual => false,
            _ => false
        };

        if(!allowed)
        {
            state.Violations.Add(new OwlDlViolation(declaration.Origin, $"Reserved vocabulary declared as {declaration.Kind} ({iri})."));
        }
    }

    //Expression walks (explicit stacks).

    private static void WalkClass(OwlClassExpression expression, Quad origin, WalkState state)
    {
        Stack<OwlClassExpression> stack = new();
        stack.Push(expression);

        while(stack.Count > 0)
        {
            switch(stack.Pop())
            {
                case OwlClassReference reference:
                {
                    CheckClassReference(reference.Class, origin, state);
                    break;
                }
                case OwlObjectIntersectionOf intersection:
                {
                    foreach(OwlClassExpression operand in intersection.Operands)
                    {
                        stack.Push(operand);
                    }

                    break;
                }
                case OwlObjectUnionOf union:
                {
                    foreach(OwlClassExpression operand in union.Operands)
                    {
                        stack.Push(operand);
                    }

                    break;
                }
                case OwlObjectComplementOf complement:
                {
                    stack.Push(complement.Operand);
                    break;
                }
                case OwlObjectOneOf oneOf:
                {
                    foreach(RdfTerm individual in oneOf.Individuals)
                    {
                        CheckIndividual(individual, origin, state);
                    }

                    break;
                }
                case OwlObjectSomeValuesFrom someValues:
                {
                    CheckObjectProperty(someValues.Property, origin, state);
                    stack.Push(someValues.Filler);
                    break;
                }
                case OwlObjectAllValuesFrom allValues:
                {
                    CheckObjectProperty(allValues.Property, origin, state);
                    stack.Push(allValues.Filler);
                    break;
                }
                case OwlObjectHasValue hasValue:
                {
                    CheckObjectProperty(hasValue.Property, origin, state);
                    CheckIndividual(hasValue.Individual, origin, state);
                    break;
                }
                case OwlObjectHasSelf hasSelf:
                {
                    NamedNode selfNode = CheckObjectProperty(hasSelf.Property, origin, state);
                    state.SimpleRequired.Add((selfNode.Iri, origin, "ObjectHasSelf"));
                    break;
                }
                case OwlObjectCardinality cardinality:
                {
                    NamedNode cardinalityNode = CheckObjectProperty(cardinality.Property, origin, state);
                    state.SimpleRequired.Add((cardinalityNode.Iri, origin, "ObjectCardinality"));
                    if(cardinality.Filler is OwlClassExpression filler)
                    {
                        stack.Push(filler);
                    }

                    break;
                }
                case OwlDataSomeValuesFrom dataSome:
                {
                    foreach(NamedNode property in dataSome.Properties)
                    {
                        CheckDataProperty(property, origin, state);
                    }

                    WalkRange(dataSome.Range, origin, state);
                    break;
                }
                case OwlDataAllValuesFrom dataAll:
                {
                    foreach(NamedNode property in dataAll.Properties)
                    {
                        CheckDataProperty(property, origin, state);
                    }

                    WalkRange(dataAll.Range, origin, state);
                    break;
                }
                case OwlDataHasValue dataHasValue:
                {
                    CheckDataProperty(dataHasValue.Property, origin, state);
                    CheckLiteral(dataHasValue.Value, origin, state);
                    break;
                }
                case OwlDataCardinality dataCardinality:
                {
                    CheckDataProperty(dataCardinality.Property, origin, state);
                    if(dataCardinality.Range is OwlDataRange range)
                    {
                        WalkRange(range, origin, state);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    private static void WalkRange(OwlDataRange range, Quad origin, WalkState state)
    {
        Stack<OwlDataRange> stack = new();
        stack.Push(range);

        while(stack.Count > 0)
        {
            switch(stack.Pop())
            {
                case OwlDatatypeReference reference:
                {
                    CheckDatatype(reference.Datatype, origin, state);
                    break;
                }
                case OwlDataIntersectionOf intersection:
                {
                    foreach(OwlDataRange operand in intersection.Ranges)
                    {
                        stack.Push(operand);
                    }

                    break;
                }
                case OwlDataUnionOf union:
                {
                    foreach(OwlDataRange operand in union.Ranges)
                    {
                        stack.Push(operand);
                    }

                    break;
                }
                case OwlDataComplementOf complement:
                {
                    stack.Push(complement.Range);
                    break;
                }
                case OwlDataOneOf oneOf:
                {
                    foreach(Literal literal in oneOf.Literals)
                    {
                        CheckLiteral(literal, origin, state);
                    }

                    break;
                }
                case OwlDatatypeRestriction restriction:
                {
                    //Only datatype-map members carry the facet spaces a
                    //restriction constrains.
                    if(!OwlDatatypeMap.Dl.Contains(restriction.Datatype.Iri))
                    {
                        state.Violations.Add(new OwlDlViolation(origin, $"A datatype restriction constrains a datatype outside the OWL 2 map ({restriction.Datatype.Iri})."));
                    }

                    foreach(OwlFacetRestriction facet in restriction.Restrictions)
                    {
                        CheckLiteral(facet.Value, origin, state);
                    }

                    break;
                }
                default:
                {
                    break;
                }
            }
        }
    }

    //Entity checks.

    private static void CheckClassReference(NamedNode node, Quad origin, WalkState state)
    {
        if(ReservedClasses.Contains(node.Iri))
        {
            return;
        }

        if(IsReserved(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Reserved vocabulary used as a class ({node.Iri})."));
        }
        else if(!state.Document.DeclaredClasses.Contains(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Undeclared class ({node.Iri})."));
        }
    }

    private static NamedNode CheckObjectProperty(OwlObjectPropertyExpression expression, Quad origin, WalkState state)
    {
        NamedNode node = expression switch
        {
            OwlObjectPropertyReference reference => reference.Named,
            OwlInverseObjectProperty inverse => inverse.Inverted,
            _ => throw new InvalidOperationException("Unknown object-property expression.")
        };

        CheckObjectPropertyIri(node, origin, state);

        return node;
    }

    private static void CheckObjectPropertyIri(NamedNode node, Quad origin, WalkState state)
    {
        if(ReservedObjectProperties.Contains(node.Iri))
        {
            return;
        }

        if(IsReserved(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Reserved vocabulary used as an object property ({node.Iri})."));
        }
        else if(!state.Document.DeclaredObjectProperties.Contains(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Undeclared object property ({node.Iri})."));
        }
    }

    private static void CheckDataProperty(NamedNode node, Quad origin, WalkState state)
    {
        if(ReservedDataProperties.Contains(node.Iri))
        {
            return;
        }

        if(IsReserved(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Reserved vocabulary used as a data property ({node.Iri})."));
        }
        else if(!state.Document.DeclaredDataProperties.Contains(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Undeclared data property ({node.Iri})."));
        }
    }

    private static void CheckAnnotationProperty(NamedNode node, Quad origin, WalkState state)
    {
        if(state.Document.DeclaredAnnotationProperties.Contains(node.Iri))
        {
            return;
        }

        if(IsReserved(node.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Reserved vocabulary used as an annotation property ({node.Iri})."));
        }
        else
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Undeclared annotation property ({node.Iri})."));
        }
    }

    private static void CheckDatatype(NamedNode node, Quad origin, WalkState state)
    {
        if(OwlDatatypeMap.Dl.Contains(node.Iri) || state.DefinedDatatypes.Contains(node.Iri))
        {
            return;
        }

        string detail = IsReserved(node.Iri) ? "not in the OWL 2 datatype map" : "neither in the OWL 2 datatype map nor defined";

        state.Violations.Add(new OwlDlViolation(origin, $"A datatype is {detail} ({node.Iri})."));
    }

    private static void CheckIndividual(RdfTerm term, Quad origin, WalkState state)
    {
        if(term is NamedNode named && IsReserved(named.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"Reserved vocabulary used as an individual ({named.Iri})."));
        }
    }

    private static void CheckLiteral(Literal literal, Quad origin, WalkState state)
    {
        //A language-tagged literal is an rdf:PlainLiteral/rdf:langString
        //value; otherwise the datatype must come from the map.
        if(literal.Language is not null)
        {
            return;
        }

        if(!OwlDatatypeMap.Dl.Contains(literal.Datatype.Iri))
        {
            state.Violations.Add(new OwlDlViolation(origin, $"A literal's datatype is not in the OWL 2 datatype map ({literal.Datatype.Iri})."));
        }
    }

    //Global restrictions.

    private static HashSet<Utf8String> PropagateNonSimple(WalkState state)
    {
        HashSet<Utf8String> nonSimple = [.. state.Composite];
        Queue<Utf8String> pending = new(nonSimple);

        while(pending.Count > 0)
        {
            Utf8String current = pending.Dequeue();
            if(!state.SuperEdges.TryGetValue(current, out List<Utf8String>? supers))
            {
                continue;
            }

            foreach(Utf8String super in supers)
            {
                if(nonSimple.Add(super))
                {
                    pending.Enqueue(super);
                }
            }
        }

        return nonSimple;
    }

    private static void CheckChainRegularity(WalkState state, List<OwlDlViolation> violations)
    {
        if(state.ChainEdges.Count == 0)
        {
            return;
        }

        //The regularity order is violated exactly when the chain dependency
        //graph has a cycle: some property's chain depends, transitively, on
        //the property itself. Iterative three-colour depth-first search.
        Dictionary<Utf8String, List<Utf8String>> edges = [];
        foreach((Utf8String from, Utf8String to) in state.ChainEdges)
        {
            if(!edges.TryGetValue(from, out List<Utf8String>? targets))
            {
                targets = [];
                edges[from] = targets;
            }

            targets.Add(to);
        }

        Dictionary<Utf8String, int> colour = [];
        foreach(Utf8String root in edges.Keys)
        {
            if(colour.ContainsKey(root))
            {
                continue;
            }

            //Each frame holds the node and its next out-edge index.
            Stack<(Utf8String Node, int Next)> stack = new();
            stack.Push((root, 0));
            colour[root] = 1;

            while(stack.Count > 0)
            {
                (Utf8String node, int next) = stack.Pop();
                List<Utf8String>? targets = edges.TryGetValue(node, out List<Utf8String>? found) ? found : null;

                if(targets is null || next >= targets.Count)
                {
                    colour[node] = 2;

                    continue;
                }

                stack.Push((node, next + 1));
                Utf8String target = targets[next];
                if(!colour.TryGetValue(target, out int targetColour))
                {
                    colour[target] = 1;
                    stack.Push((target, 0));
                }
                else if(targetColour == 1)
                {
                    Quad? origin = state.ChainOrigins.TryGetValue(target, out Quad? chainOrigin) ? chainOrigin : null;
                    violations.Add(new OwlDlViolation(origin, "The property chains form a cycle; the hierarchy is not regular."));

                    return;
                }
            }
        }
    }

    private static string DescribeFirstError(OwlOntologyDocument document)
    {
        foreach(Diagnostic diagnostic in document.Diagnostics.Diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return diagnostic.Message.ToString();
            }
        }

        return "(unspecified mapping error)";
    }

    private static bool IsReserved(Utf8String iri)
    {
        ReadOnlySpan<byte> span = iri.Span;

        return span.StartsWith(OwlNamespace)
            || span.StartsWith(RdfNamespace)
            || span.StartsWith(RdfsNamespace)
            || span.StartsWith(XsdNamespace);
    }
}
