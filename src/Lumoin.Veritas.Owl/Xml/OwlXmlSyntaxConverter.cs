using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Xml;

/// <summary>
/// Converts the OWL/XML element tree into the structural model. Each element
/// folds directly into its axiom or expression — the serialization mirrors the
/// structural specification one-to-one, so no RDF graph stands between the XML
/// and the axioms. The <c>Prefix</c> declarations resolve abbreviated IRIs, and
/// the declaration census disambiguates property kinds.
/// </summary>
/// <remarks>
/// Expression and data-range subtrees convert in post-order over an explicit
/// stack, each node's value memoized, so arbitrarily nested class expressions
/// never touch the call stack. Conversion is value-based: a malformed element is
/// recorded as a diagnostic and replaced with a sentinel where structure
/// requires an operand.
/// </remarks>
internal sealed class OwlXmlSyntaxConverter
{
    /// <summary>Gets the bag every conversion diagnostic of the parse accumulates into.</summary>
    public DiagnosticBag Diagnostics { get; } = new();

    /// <summary>Gets the axioms converted so far, in document order.</summary>
    public ImmutableArray<OwlAxiom>.Builder Axioms { get; } = ImmutableArray.CreateBuilder<OwlAxiom>();

    /// <summary>Gets the ontology IRI, once the ontology header has supplied one.</summary>
    public NamedNode? OntologyIri { get; private set; }

    /// <summary>Gets the IRIs declared as classes.</summary>
    public HashSet<Utf8String> DeclaredClasses { get; } = [];

    /// <summary>Gets the IRIs declared as object properties.</summary>
    public HashSet<Utf8String> DeclaredObjectProperties { get; } = [];

    /// <summary>Gets the IRIs declared as data properties.</summary>
    public HashSet<Utf8String> DeclaredDataProperties { get; } = [];

    /// <summary>Gets the IRIs declared as annotation properties.</summary>
    public HashSet<Utf8String> DeclaredAnnotationProperties { get; } = [];

    /// <summary>Gets the IRIs declared as datatypes.</summary>
    public HashSet<Utf8String> DeclaredDatatypes { get; } = [];

    /// <summary>The prefix table the document's abbreviated names resolve through; keys carry an eager hash for lookup.</summary>
    private Dictionary<Utf8String, Utf8String> Prefixes { get; } = [];

    /// <summary>The structural value of each evaluated expression or term node, filled in post-order.</summary>
    private Dictionary<OwlXmlNode, object?> Converted { get; } = [];

    /// <summary>The source extent of the element currently converting — the span every conversion diagnostic carries.</summary>
    private SourceSpan CurrentSpan { get; set; }

    /// <summary>The synthetic origin every OWL/XML axiom carries — the serialization has no triples to anchor to.</summary>
    private Quad Origin { get; } = new(
        new NamedNode(Utf8Strings.From("urn:veritas:owl-xml")),
        new NamedNode(Utf8Strings.From("urn:veritas:owl-xml")),
        new NamedNode(Utf8Strings.From("urn:veritas:owl-xml")),
        Graph: null);

    /// <summary>The <c>xsd:string</c> datatype IRI, the default datatype of a plain literal.</summary>
    private static ReadOnlySpan<byte> XsdString => "http://www.w3.org/2001/XMLSchema#string"u8;

    /// <summary>The <c>rdf:langString</c> datatype IRI, the datatype of a language-tagged literal.</summary>
    private static ReadOnlySpan<byte> RdfLangString => "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8;

    /// <summary>The placeholder node a malformed reference resolves to, shared so an error path allocates nothing.</summary>
    private static NamedNode InvalidNamed { get; } = new(new Utf8String("urn:veritas:invalid"u8.ToArray()));

    /// <summary>The placeholder literal a malformed literal operand resolves to.</summary>
    private static Literal InvalidLiteral { get; } = new(new Utf8String("\0"u8.ToArray()), InvalidNamed);

    /// <summary>Converts the ontology element and its children into structural axioms.</summary>
    /// <param name="ontology">The document's <c>Ontology</c> element, or <see langword="null"/> when the document has none.</param>
    public void Convert(OwlXmlNode? ontology)
    {
        if(ontology is null)
        {
            return;
        }

        if(ontology.Attribute(OwlXmlNames.OntologyIriAttribute) is Utf8String iri)
        {
            OntologyIri = new NamedNode(Intern(iri));
        }

        foreach(OwlXmlNode child in ontology.Children)
        {
            ConvertChild(child);
        }
    }

    /// <summary>Converts one direct child of the ontology element: a prefix, an import, an ontology annotation, or an axiom.</summary>
    /// <param name="node">The child element.</param>
    private void ConvertChild(OwlXmlNode node)
    {
        CurrentSpan = node.Span;

        switch(node.Element)
        {
            case(OwlXmlElement.Prefix):
            {
                RegisterPrefix(node);
                break;
            }
            case(OwlXmlElement.Import):
            {
                Axioms.Add(new OwlImportAxiom(new NamedNode(Intern(Trim(node.Text)))) { Origin = Origin });
                break;
            }
            case(OwlXmlElement.Annotation):
            {
                ConvertOntologyAnnotation(node);
                break;
            }
            case(OwlXmlElement.Unknown):
            {
                break;
            }
            default:
            {
                ConvertAxiom(node);
                break;
            }
        }
    }

    /// <summary>Registers a <c>Prefix</c> declaration into the prefix table.</summary>
    /// <param name="node">The prefix element.</param>
    private void RegisterPrefix(OwlXmlNode node)
    {
        if(node.Attribute(OwlXmlNames.NameAttribute) is Utf8String name
            && node.Attribute(OwlXmlNames.IriAttribute) is Utf8String iri)
        {
            Prefixes[Intern(name)] = Intern(iri);
        }
    }

    /// <summary>Surfaces an ontology-level annotation as an annotation assertion on the ontology IRI.</summary>
    /// <param name="node">The annotation element.</param>
    private void ConvertOntologyAnnotation(OwlXmlNode node)
    {
        if(OntologyIri is NamedNode subject && BuildAnnotation(node) is OwlAnnotation annotation)
        {
            Axioms.Add(new OwlAnnotationAssertionAxiom(subject, annotation.Property, annotation.Value)
            {
                Origin = Origin,
                Annotations = annotation.Annotations,
            });
        }
    }

    /// <summary>Converts an axiom element: its leading annotations, then its operands.</summary>
    /// <param name="node">The axiom element.</param>
    private void ConvertAxiom(OwlXmlNode node)
    {
        CurrentSpan = node.Span;
        (ImmutableArray<OwlAnnotation> annotations, List<OwlXmlNode> operands) = SplitAnnotations(node);
        BuildAxiom(node, operands, annotations);
    }

    /// <summary>Partitions an element's children into its leading annotations and its remaining operand elements.</summary>
    /// <param name="node">The element to partition.</param>
    /// <returns>The annotations and the operand elements.</returns>
    private (ImmutableArray<OwlAnnotation> Annotations, List<OwlXmlNode> Operands) SplitAnnotations(OwlXmlNode node)
    {
        ImmutableArray<OwlAnnotation>.Builder annotations = ImmutableArray.CreateBuilder<OwlAnnotation>();
        List<OwlXmlNode> operands = [];
        foreach(OwlXmlNode child in node.Children)
        {
            if(child.Element == OwlXmlElement.Annotation)
            {
                if(BuildAnnotation(child) is OwlAnnotation annotation)
                {
                    annotations.Add(annotation);
                }
            }
            else
            {
                operands.Add(child);
            }
        }

        return (annotations.ToImmutable(), operands);
    }

    /// <summary>Builds one annotation: its own nested annotations, its property, and its value.</summary>
    /// <param name="node">The annotation element.</param>
    /// <returns>The annotation, or <see langword="null"/> when it has no property and value.</returns>
    private OwlAnnotation? BuildAnnotation(OwlXmlNode node)
    {
        (ImmutableArray<OwlAnnotation> nested, List<OwlXmlNode> operands) = SplitAnnotations(node);
        if(operands.Count < 2)
        {
            return null;
        }

        NamedNode property = AsNamedNode(Evaluate(operands[0]));
        RdfTerm value = AsTerm(Evaluate(operands[1]));

        return new OwlAnnotation(property, value) { Annotations = nested };
    }

    /// <summary>Builds the axiom an element names and adds it to the axiom list.</summary>
    /// <param name="node">The axiom element.</param>
    /// <param name="operands">The element's operand elements, annotations removed.</param>
    /// <param name="annotations">The element's annotations.</param>
    private void BuildAxiom(OwlXmlNode node, List<OwlXmlNode> operands, ImmutableArray<OwlAnnotation> annotations)
    {
        switch(node.Element)
        {
            case(OwlXmlElement.Declaration):
            {
                BuildDeclaration(operands, annotations);
                return;
            }
            case(OwlXmlElement.SubObjectPropertyOf):
            {
                BuildSubObjectProperty(operands, annotations);
                return;
            }
            case(OwlXmlElement.HasKey):
            {
                BuildHasKey(operands, annotations);
                return;
            }
            case(OwlXmlElement.EquivalentClasses):
            {
                foreach((int first, int second) in Pairs(operands.Count))
                {
                    Add(new OwlEquivalentClassesAxiom(Class(operands, first), Class(operands, second)) { Origin = Origin }, annotations);
                }

                return;
            }
            case(OwlXmlElement.EquivalentObjectProperties):
            {
                foreach((int first, int second) in Pairs(operands.Count))
                {
                    Add(new OwlEquivalentObjectPropertiesAxiom(ObjectProperty(operands, first), ObjectProperty(operands, second)) { Origin = Origin }, annotations);
                }

                return;
            }
            case(OwlXmlElement.EquivalentDataProperties):
            {
                foreach((int first, int second) in Pairs(operands.Count))
                {
                    Add(new OwlEquivalentDataPropertiesAxiom(Named(operands, first), Named(operands, second)) { Origin = Origin }, annotations);
                }

                return;
            }
            case(OwlXmlElement.SameIndividual):
            {
                foreach((int first, int second) in Pairs(operands.Count))
                {
                    Add(new OwlSameIndividualAxiom(Term(operands, first), Term(operands, second)) { Origin = Origin }, annotations);
                }

                return;
            }
            default:
            {
                break;
            }
        }

        OwlAxiom? axiom = node.Element switch
        {
            OwlXmlElement.SubClassOf => new OwlSubClassOfAxiom(Class(operands, 0), Class(operands, 1)) { Origin = Origin },
            OwlXmlElement.DisjointClasses => new OwlDisjointClassesAxiom(ClassList(operands)) { Origin = Origin },
            OwlXmlElement.DisjointUnion => new OwlDisjointUnionAxiom(Named(operands, 0), ClassList(Rest(operands))) { Origin = Origin },
            OwlXmlElement.DisjointObjectProperties => new OwlDisjointObjectPropertiesAxiom(ObjectPropertyList(operands)) { Origin = Origin },
            OwlXmlElement.InverseObjectProperties => new OwlInverseObjectPropertiesAxiom(ObjectProperty(operands, 0), ObjectProperty(operands, 1)) { Origin = Origin },
            OwlXmlElement.ObjectPropertyDomain => new OwlObjectPropertyDomainAxiom(ObjectProperty(operands, 0), Class(operands, 1)) { Origin = Origin },
            OwlXmlElement.ObjectPropertyRange => new OwlObjectPropertyRangeAxiom(ObjectProperty(operands, 0), Class(operands, 1)) { Origin = Origin },
            OwlXmlElement.FunctionalObjectProperty
                or OwlXmlElement.InverseFunctionalObjectProperty
                or OwlXmlElement.ReflexiveObjectProperty
                or OwlXmlElement.IrreflexiveObjectProperty
                or OwlXmlElement.SymmetricObjectProperty
                or OwlXmlElement.AsymmetricObjectProperty
                or OwlXmlElement.TransitiveObjectProperty
                => new OwlObjectPropertyCharacteristicAxiom(Characteristic(node.Element), ObjectProperty(operands, 0)) { Origin = Origin },
            OwlXmlElement.SubDataPropertyOf => new OwlSubDataPropertyOfAxiom(Named(operands, 0), Named(operands, 1)) { Origin = Origin },
            OwlXmlElement.DisjointDataProperties => new OwlDisjointDataPropertiesAxiom(NamedList(operands)) { Origin = Origin },
            OwlXmlElement.DataPropertyDomain => new OwlDataPropertyDomainAxiom(Named(operands, 0), Class(operands, 1)) { Origin = Origin },
            OwlXmlElement.DataPropertyRange => new OwlDataPropertyRangeAxiom(Named(operands, 0), DataRange(operands, 1)) { Origin = Origin },
            OwlXmlElement.FunctionalDataProperty => new OwlFunctionalDataPropertyAxiom(Named(operands, 0)) { Origin = Origin },
            OwlXmlElement.DatatypeDefinition => new OwlDatatypeDefinitionAxiom(Named(operands, 0), DataRange(operands, 1)) { Origin = Origin },
            OwlXmlElement.DifferentIndividuals => new OwlDifferentIndividualsAxiom(TermList(operands)) { Origin = Origin },
            OwlXmlElement.ClassAssertion => new OwlClassAssertionAxiom(Class(operands, 0), Term(operands, 1)) { Origin = Origin },
            OwlXmlElement.ObjectPropertyAssertion => new OwlObjectPropertyAssertionAxiom(Term(operands, 1), ObjectProperty(operands, 0).Property, Term(operands, 2)) { Origin = Origin },
            OwlXmlElement.NegativeObjectPropertyAssertion => new OwlNegativeObjectPropertyAssertionAxiom(Term(operands, 1), ObjectProperty(operands, 0), Term(operands, 2)) { Origin = Origin },
            OwlXmlElement.DataPropertyAssertion => new OwlDataPropertyAssertionAxiom(Term(operands, 1), Named(operands, 0), LiteralAt(operands, 2)) { Origin = Origin },
            OwlXmlElement.NegativeDataPropertyAssertion => new OwlNegativeDataPropertyAssertionAxiom(Term(operands, 1), Named(operands, 0), LiteralAt(operands, 2)) { Origin = Origin },
            OwlXmlElement.AnnotationAssertion => new OwlAnnotationAssertionAxiom(Term(operands, 1), Named(operands, 0), Term(operands, 2)) { Origin = Origin },
            OwlXmlElement.SubAnnotationPropertyOf => new OwlSubAnnotationPropertyOfAxiom(Named(operands, 0), Named(operands, 1)) { Origin = Origin },
            OwlXmlElement.AnnotationPropertyDomain => new OwlAnnotationPropertyDomainAxiom(Named(operands, 0), Named(operands, 1)) { Origin = Origin },
            OwlXmlElement.AnnotationPropertyRange => new OwlAnnotationPropertyRangeAxiom(Named(operands, 0), Named(operands, 1)) { Origin = Origin },
            _ => null,
        };

        if(axiom is not null)
        {
            Add(axiom, annotations);
        }
        else
        {
            Report($"Unsupported OWL/XML axiom element '{Decode(node.LocalName)}'.");
        }
    }

    /// <summary>Builds a declaration axiom from its single entity operand and records the entity in the declaration census.</summary>
    /// <param name="operands">The declaration's operands.</param>
    /// <param name="annotations">The declaration's annotations.</param>
    private void BuildDeclaration(List<OwlXmlNode> operands, ImmutableArray<OwlAnnotation> annotations)
    {
        if(operands.Count < 1)
        {
            Report("'Declaration' requires an entity.");

            return;
        }

        OwlXmlNode entity = operands[0];
        NamedNode named = NamedFrom(entity);
        OwlEntityKind kind = entity.Element switch
        {
            OwlXmlElement.Class => OwlEntityKind.Class,
            OwlXmlElement.Datatype => OwlEntityKind.Datatype,
            OwlXmlElement.ObjectProperty => OwlEntityKind.ObjectProperty,
            OwlXmlElement.DataProperty => OwlEntityKind.DataProperty,
            OwlXmlElement.AnnotationProperty => OwlEntityKind.AnnotationProperty,
            OwlXmlElement.NamedIndividual => OwlEntityKind.NamedIndividual,
            _ => OwlEntityKind.Class,
        };

        Census(kind, named.Iri);
        Add(new OwlDeclarationAxiom(kind, named) { Origin = Origin }, annotations);
    }

    /// <summary>Records a declared entity IRI in the per-kind declaration census.</summary>
    /// <param name="kind">The declared entity kind.</param>
    /// <param name="iri">The declared entity IRI.</param>
    private void Census(OwlEntityKind kind, Utf8String iri)
    {
        switch(kind)
        {
            case(OwlEntityKind.Class):
            {
                DeclaredClasses.Add(iri);
                break;
            }
            case(OwlEntityKind.Datatype):
            {
                DeclaredDatatypes.Add(iri);
                break;
            }
            case(OwlEntityKind.ObjectProperty):
            {
                DeclaredObjectProperties.Add(iri);
                break;
            }
            case(OwlEntityKind.DataProperty):
            {
                DeclaredDataProperties.Add(iri);
                break;
            }
            case(OwlEntityKind.AnnotationProperty):
            {
                DeclaredAnnotationProperties.Add(iri);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Builds a <c>SubObjectPropertyOf</c> axiom, distinguishing a property chain subproperty from a plain one.</summary>
    /// <param name="operands">The axiom operands.</param>
    /// <param name="annotations">The axiom annotations.</param>
    private void BuildSubObjectProperty(List<OwlXmlNode> operands, ImmutableArray<OwlAnnotation> annotations)
    {
        if(operands.Count < 2)
        {
            Report("'SubObjectPropertyOf' requires a subproperty and a superproperty.");

            return;
        }

        OwlObjectPropertyExpression super = ObjectProperty(operands, 1);
        if(operands[0].Element == OwlXmlElement.ObjectPropertyChain)
        {
            List<OwlObjectPropertyExpression> chain = [];
            foreach(OwlXmlNode link in operands[0].Children)
            {
                chain.Add(AsObjectProperty(Evaluate(link)));
            }

            Add(new OwlPropertyChainAxiom(chain, super) { Origin = Origin }, annotations);

            return;
        }

        Add(new OwlSubObjectPropertyOfAxiom(ObjectProperty(operands, 0), super) { Origin = Origin }, annotations);
    }

    /// <summary>Builds a <c>HasKey</c> axiom, splitting its key components into object and data properties.</summary>
    /// <param name="operands">The axiom operands.</param>
    /// <param name="annotations">The axiom annotations.</param>
    private void BuildHasKey(List<OwlXmlNode> operands, ImmutableArray<OwlAnnotation> annotations)
    {
        if(operands.Count < 1)
        {
            Report("'HasKey' requires a class expression.");

            return;
        }

        OwlClassExpression keyed = AsClass(Evaluate(operands[0]));
        List<OwlObjectPropertyExpression> objectKeys = [];
        List<NamedNode> dataKeys = [];
        for(int i = 1; i < operands.Count; i++)
        {
            OwlXmlNode key = operands[i];
            if(key.Element is OwlXmlElement.ObjectProperty or OwlXmlElement.ObjectInverseOf)
            {
                objectKeys.Add(AsObjectProperty(Evaluate(key)));
            }
            else
            {
                dataKeys.Add(NamedFrom(key));
            }
        }

        Add(new OwlHasKeyAxiom(keyed, objectKeys, dataKeys) { Origin = Origin }, annotations);
    }

    /// <summary>Evaluates an expression, data-range, property-expression, or term subtree to its structural value, post-order over an explicit stack.</summary>
    /// <param name="root">The subtree root.</param>
    /// <returns>The structural value of the root.</returns>
    private object? Evaluate(OwlXmlNode root)
    {
        Stack<OwlXmlNode> work = new();
        work.Push(root);
        while(work.Count > 0)
        {
            OwlXmlNode node = work.Peek();
            if(Converted.ContainsKey(node))
            {
                work.Pop();

                continue;
            }

            bool ready = true;
            foreach(OwlXmlNode child in node.Children)
            {
                if(child.Element != OwlXmlElement.Annotation && !Converted.ContainsKey(child))
                {
                    work.Push(child);
                    ready = false;
                }
            }

            if(ready)
            {
                work.Pop();
                Converted[node] = Build(node);
            }
        }

        return Converted[root];
    }

    /// <summary>Builds the structural value of a node from its already-evaluated children.</summary>
    /// <param name="node">The node to build.</param>
    /// <returns>The class expression, data range, property expression, term, or facet restriction.</returns>
    private object? Build(OwlXmlNode node)
    {
        switch(node.Element)
        {
            case(OwlXmlElement.Class):
            {
                return new OwlClassReference(NamedFrom(node));
            }
            case(OwlXmlElement.ObjectIntersectionOf):
            {
                return new OwlObjectIntersectionOf(ClassList(node.Children));
            }
            case(OwlXmlElement.ObjectUnionOf):
            {
                return new OwlObjectUnionOf(ClassList(node.Children));
            }
            case(OwlXmlElement.ObjectComplementOf):
            {
                return new OwlObjectComplementOf(FirstClass(node));
            }
            case(OwlXmlElement.ObjectOneOf):
            {
                return new OwlObjectOneOf(TermList(node.Children));
            }
            case(OwlXmlElement.ObjectSomeValuesFrom):
            {
                return new OwlObjectSomeValuesFrom(FirstObjectProperty(node), SecondClass(node));
            }
            case(OwlXmlElement.ObjectAllValuesFrom):
            {
                return new OwlObjectAllValuesFrom(FirstObjectProperty(node), SecondClass(node));
            }
            case(OwlXmlElement.ObjectHasValue):
            {
                return new OwlObjectHasValue(FirstObjectProperty(node), SecondTerm(node));
            }
            case(OwlXmlElement.ObjectHasSelf):
            {
                return new OwlObjectHasSelf(FirstObjectProperty(node));
            }
            case(OwlXmlElement.ObjectMinCardinality):
            case(OwlXmlElement.ObjectMaxCardinality):
            case(OwlXmlElement.ObjectExactCardinality):
            {
                return BuildObjectCardinality(node);
            }
            case(OwlXmlElement.DataSomeValuesFrom):
            {
                return new OwlDataSomeValuesFrom(DataPropertyList(node), LastDataRange(node));
            }
            case(OwlXmlElement.DataAllValuesFrom):
            {
                return new OwlDataAllValuesFrom(DataPropertyList(node), LastDataRange(node));
            }
            case(OwlXmlElement.DataHasValue):
            {
                return new OwlDataHasValue(
                    node.Children.Count > 0 ? NamedFrom(node.Children[0]) : InvalidNamed,
                    node.Children.Count > 1 ? LiteralFrom(node.Children[1]) : InvalidLiteral);
            }
            case(OwlXmlElement.DataMinCardinality):
            case(OwlXmlElement.DataMaxCardinality):
            case(OwlXmlElement.DataExactCardinality):
            {
                return BuildDataCardinality(node);
            }
            case(OwlXmlElement.Datatype):
            {
                return new OwlDatatypeReference(NamedFrom(node));
            }
            case(OwlXmlElement.DataIntersectionOf):
            {
                return new OwlDataIntersectionOf(DataRangeList(node.Children));
            }
            case(OwlXmlElement.DataUnionOf):
            {
                return new OwlDataUnionOf(DataRangeList(node.Children));
            }
            case(OwlXmlElement.DataComplementOf):
            {
                return new OwlDataComplementOf(FirstDataRange(node));
            }
            case(OwlXmlElement.DataOneOf):
            {
                return new OwlDataOneOf(LiteralList(node.Children));
            }
            case(OwlXmlElement.DatatypeRestriction):
            {
                return BuildDatatypeRestriction(node);
            }
            case(OwlXmlElement.FacetRestriction):
            {
                return new OwlFacetRestriction(NamedFromIri(node.Attribute(OwlXmlNames.FacetAttribute)), node.Children.Count > 0 ? LiteralFrom(node.Children[0]) : InvalidLiteral);
            }
            case(OwlXmlElement.ObjectProperty):
            {
                return new OwlObjectPropertyReference(NamedFrom(node));
            }
            case(OwlXmlElement.ObjectInverseOf):
            {
                return new OwlInverseObjectProperty(node.Children.Count > 0 ? NamedFrom(node.Children[0]) : InvalidNamed);
            }
            case(OwlXmlElement.DataProperty):
            case(OwlXmlElement.AnnotationProperty):
            {
                return NamedFrom(node);
            }
            case(OwlXmlElement.NamedIndividual):
            case(OwlXmlElement.Iri):
            case(OwlXmlElement.AbbreviatedIri):
            {
                return NamedFrom(node);
            }
            case(OwlXmlElement.AnonymousIndividual):
            {
                return new BlankNode(Intern(node.Attribute(OwlXmlNames.NodeIdAttribute) ?? Trim(node.Text)));
            }
            case(OwlXmlElement.Literal):
            {
                return LiteralFrom(node);
            }
            default:
            {
                Report($"Unsupported OWL/XML expression element '{Decode(node.LocalName)}'.");

                return new OwlClassReference(InvalidNamed);
            }
        }
    }

    /// <summary>Builds an object cardinality restriction, qualified when a filler follows the property.</summary>
    /// <param name="node">The cardinality element.</param>
    /// <returns>The cardinality restriction.</returns>
    private OwlObjectCardinality BuildObjectCardinality(OwlXmlNode node)
    {
        OwlCardinalityKind kind = CardinalityKind(node.Element);
        int bound = Cardinality(node);
        OwlObjectPropertyExpression property = FirstObjectProperty(node);
        OwlClassExpression? filler = node.Children.Count > 1 ? AsClass(Converted[node.Children[1]]) : null;

        return new OwlObjectCardinality(kind, bound, property, filler);
    }

    /// <summary>Builds a data cardinality restriction, qualified when a range follows the property.</summary>
    /// <param name="node">The cardinality element.</param>
    /// <returns>The cardinality restriction.</returns>
    private OwlDataCardinality BuildDataCardinality(OwlXmlNode node)
    {
        OwlCardinalityKind kind = CardinalityKind(node.Element);
        int bound = Cardinality(node);
        NamedNode property = node.Children.Count > 0 ? NamedFrom(node.Children[0]) : InvalidNamed;
        OwlDataRange? range = node.Children.Count > 1 ? AsDataRange(Converted[node.Children[1]]) : null;

        return new OwlDataCardinality(kind, bound, property, range);
    }

    /// <summary>Builds a datatype restriction from its datatype and facet–value pairs.</summary>
    /// <param name="node">The datatype-restriction element.</param>
    /// <returns>The datatype restriction.</returns>
    private OwlDatatypeRestriction BuildDatatypeRestriction(OwlXmlNode node)
    {
        NamedNode datatype = node.Children.Count > 0 ? NamedFrom(node.Children[0]) : InvalidNamed;
        List<OwlFacetRestriction> restrictions = [];
        for(int i = 1; i < node.Children.Count; i++)
        {
            if(Converted[node.Children[i]] is OwlFacetRestriction facet)
            {
                restrictions.Add(facet);
            }
        }

        return new OwlDatatypeRestriction(datatype, restrictions);
    }

    /// <summary>Resolves a node to a named node from its IRI, abbreviated IRI, or child IRI form.</summary>
    /// <param name="node">The node to resolve.</param>
    /// <returns>The named node.</returns>
    private NamedNode NamedFrom(OwlXmlNode node)
    {
        if(node.Attribute(OwlXmlNames.IriAttribute) is Utf8String iri)
        {
            return new NamedNode(Intern(iri));
        }

        if(node.Attribute(OwlXmlNames.AbbreviatedIriAttribute) is Utf8String abbreviated)
        {
            return new NamedNode(Expand(abbreviated));
        }

        foreach(OwlXmlNode child in node.Children)
        {
            if(child.Element == OwlXmlElement.Iri)
            {
                return new NamedNode(Intern(Trim(child.Text)));
            }

            if(child.Element == OwlXmlElement.AbbreviatedIri)
            {
                return new NamedNode(Expand(Trim(child.Text)));
            }
        }

        if(!node.Text.IsEmpty)
        {
            return new NamedNode(Intern(Trim(node.Text)));
        }

        return InvalidNamed;
    }

    /// <summary>Resolves an IRI attribute value, full or abbreviated, to a named node.</summary>
    /// <param name="iri">The attribute value, or <see langword="null"/>.</param>
    /// <returns>The named node.</returns>
    private NamedNode NamedFromIri(Utf8String? iri)
    {
        if(iri is Utf8String value)
        {
            return value.Span.IndexOf((byte)':') >= 0 && !LooksAbsolute(value)
                ? new NamedNode(Expand(value))
                : new NamedNode(Intern(value));
        }

        return InvalidNamed;
    }

    /// <summary>Builds a literal from a <c>Literal</c> element: its datatype, optional language tag, and lexical value.</summary>
    /// <param name="node">The literal element.</param>
    /// <returns>The literal.</returns>
    private static Literal LiteralFrom(OwlXmlNode node)
    {
        Utf8String value = node.Text;
        Utf8String? language = node.Attribute(OwlXmlNames.XmlLangAttribute);
        if(node.Attribute(OwlXmlNames.DatatypeIriAttribute) is Utf8String datatype)
        {
            return new Literal(Intern(value), new NamedNode(Intern(datatype)), language);
        }

        if(language is Utf8String)
        {
            return new Literal(Intern(value), new NamedNode(new Utf8String(RdfLangString.ToArray())), language);
        }

        return new Literal(Intern(value), new NamedNode(new Utf8String(XsdString.ToArray())));
    }

    /// <summary>Expands an abbreviated IRI (<c>prefix:local</c>) through the prefix table.</summary>
    /// <param name="abbreviated">The abbreviated IRI.</param>
    /// <returns>The expanded IRI, or the abbreviated form interned when its prefix is undeclared.</returns>
    private Utf8String Expand(Utf8String abbreviated)
    {
        int colon = abbreviated.Span.IndexOf((byte)':');
        if(colon < 0)
        {
            return Intern(abbreviated);
        }

        Utf8String prefix = new(abbreviated.Memory.Slice(0, colon));
        if(Prefixes.TryGetValue(prefix, out Utf8String namespaceIri))
        {
            return Concat(namespaceIri, new Utf8String(abbreviated.Memory.Slice(colon + 1)));
        }

        Report($"Undeclared prefix '{Decode(prefix)}:'.");

        return Intern(abbreviated);
    }

    /// <summary>Reads a cardinality restriction's non-negative bound from its <c>cardinality</c> attribute.</summary>
    /// <param name="node">The cardinality element.</param>
    /// <returns>The bound, or zero when the attribute is missing or malformed.</returns>
    private int Cardinality(OwlXmlNode node)
    {
        if(node.Attribute(OwlXmlNames.CardinalityAttribute) is Utf8String text
            && Utf8Parser.TryParse(text.Span, out int value, out int consumed)
            && consumed == text.Length
            && value >= 0)
        {
            return value;
        }

        Report("A cardinality restriction requires a non-negative integer 'cardinality'.");

        return 0;
    }

    /// <summary>Adds an axiom to the axiom list with the synthetic origin and the given annotations.</summary>
    /// <param name="axiom">The axiom to add.</param>
    /// <param name="annotations">The axiom's annotations.</param>
    private void Add(OwlAxiom axiom, ImmutableArray<OwlAnnotation> annotations)
    {
        Axioms.Add(axiom with { Annotations = annotations });
    }

    /// <summary>The named node of an operand, or a sentinel when the operand is absent.</summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="index">The operand index.</param>
    /// <returns>The named node.</returns>
    private NamedNode Named(List<OwlXmlNode> operands, int index)
    {
        return index < operands.Count ? NamedFrom(operands[index]) : InvalidNamed;
    }

    /// <summary>The literal of an operand, or a sentinel when the operand is absent.</summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="index">The operand index.</param>
    /// <returns>The literal.</returns>
    private static Literal LiteralAt(List<OwlXmlNode> operands, int index)
    {
        return index < operands.Count ? LiteralFrom(operands[index]) : InvalidLiteral;
    }

    /// <summary>The operands after the first, the disjoint-union members that follow the defined class.</summary>
    /// <param name="operands">The operand list.</param>
    /// <returns>The operands from the second onward.</returns>
    private static List<OwlXmlNode> Rest(List<OwlXmlNode> operands)
    {
        return operands.Count > 1 ? operands.GetRange(1, operands.Count - 1) : [];
    }

    /// <summary>The class expression of an operand, or a sentinel when the operand is absent or not a class expression.</summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="index">The operand index.</param>
    /// <returns>The class expression.</returns>
    private OwlClassExpression Class(List<OwlXmlNode> operands, int index)
    {
        return index < operands.Count ? AsClass(Evaluate(operands[index])) : new OwlClassReference(InvalidNamed);
    }

    /// <summary>The object property expression of an operand, or a sentinel when the operand is absent.</summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="index">The operand index.</param>
    /// <returns>The object property expression.</returns>
    private OwlObjectPropertyExpression ObjectProperty(List<OwlXmlNode> operands, int index)
    {
        return index < operands.Count ? AsObjectProperty(Evaluate(operands[index])) : new OwlObjectPropertyReference(InvalidNamed);
    }

    /// <summary>The data range of an operand, or a sentinel when the operand is absent.</summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="index">The operand index.</param>
    /// <returns>The data range.</returns>
    private OwlDataRange DataRange(List<OwlXmlNode> operands, int index)
    {
        return index < operands.Count ? AsDataRange(Evaluate(operands[index])) : new OwlDatatypeReference(InvalidNamed);
    }

    /// <summary>The term of an operand, or a sentinel when the operand is absent.</summary>
    /// <param name="operands">The operand list.</param>
    /// <param name="index">The operand index.</param>
    /// <returns>The term.</returns>
    private RdfTerm Term(List<OwlXmlNode> operands, int index)
    {
        return index < operands.Count ? AsTerm(Evaluate(operands[index])) : InvalidNamed;
    }

    /// <summary>The class expression of an element's first child.</summary>
    /// <param name="node">The parent element.</param>
    /// <returns>The first child's class expression.</returns>
    private OwlClassExpression FirstClass(OwlXmlNode node)
    {
        return node.Children.Count > 0 ? AsClass(Converted[node.Children[0]]) : new OwlClassReference(InvalidNamed);
    }

    /// <summary>The class expression of an element's second child.</summary>
    /// <param name="node">The parent element.</param>
    /// <returns>The second child's class expression.</returns>
    private OwlClassExpression SecondClass(OwlXmlNode node)
    {
        return node.Children.Count > 1 ? AsClass(Converted[node.Children[1]]) : new OwlClassReference(InvalidNamed);
    }

    /// <summary>The object property expression of an element's first child.</summary>
    /// <param name="node">The parent element.</param>
    /// <returns>The first child's object property expression.</returns>
    private OwlObjectPropertyExpression FirstObjectProperty(OwlXmlNode node)
    {
        return node.Children.Count > 0 ? AsObjectProperty(Converted[node.Children[0]]) : new OwlObjectPropertyReference(InvalidNamed);
    }

    /// <summary>The term of an element's second child.</summary>
    /// <param name="node">The parent element.</param>
    /// <returns>The second child's term.</returns>
    private RdfTerm SecondTerm(OwlXmlNode node)
    {
        return node.Children.Count > 1 ? AsTerm(Converted[node.Children[1]]) : InvalidNamed;
    }

    /// <summary>The data range of an element's first child.</summary>
    /// <param name="node">The parent element.</param>
    /// <returns>The first child's data range.</returns>
    private OwlDataRange FirstDataRange(OwlXmlNode node)
    {
        return node.Children.Count > 0 ? AsDataRange(Converted[node.Children[0]]) : new OwlDatatypeReference(InvalidNamed);
    }

    /// <summary>The data range of an element's last child (the filler of an n-ary data restriction).</summary>
    /// <param name="node">The parent element.</param>
    /// <returns>The last child's data range.</returns>
    private OwlDataRange LastDataRange(OwlXmlNode node)
    {
        return node.Children.Count > 0 ? AsDataRange(Converted[node.Children[^1]]) : new OwlDatatypeReference(InvalidNamed);
    }

    /// <summary>The data properties of an n-ary data restriction: every child but the trailing range.</summary>
    /// <param name="node">The data-restriction element.</param>
    /// <returns>The restricted data properties.</returns>
    private List<NamedNode> DataPropertyList(OwlXmlNode node)
    {
        List<NamedNode> properties = [];
        for(int i = 0; i < node.Children.Count - 1; i++)
        {
            properties.Add(NamedFrom(node.Children[i]));
        }

        return properties;
    }

    /// <summary>The class expressions of a list of operand elements.</summary>
    /// <param name="nodes">The operand elements.</param>
    /// <returns>The class expressions.</returns>
    private List<OwlClassExpression> ClassList(List<OwlXmlNode> nodes)
    {
        List<OwlClassExpression> result = [];
        foreach(OwlXmlNode node in nodes)
        {
            result.Add(AsClass(Evaluate(node)));
        }

        return result;
    }

    /// <summary>The object property expressions of a list of operand elements.</summary>
    /// <param name="nodes">The operand elements.</param>
    /// <returns>The object property expressions.</returns>
    private List<OwlObjectPropertyExpression> ObjectPropertyList(List<OwlXmlNode> nodes)
    {
        List<OwlObjectPropertyExpression> result = [];
        foreach(OwlXmlNode node in nodes)
        {
            result.Add(AsObjectProperty(Evaluate(node)));
        }

        return result;
    }

    /// <summary>The data ranges of a list of operand elements.</summary>
    /// <param name="nodes">The operand elements.</param>
    /// <returns>The data ranges.</returns>
    private List<OwlDataRange> DataRangeList(List<OwlXmlNode> nodes)
    {
        List<OwlDataRange> result = [];
        foreach(OwlXmlNode node in nodes)
        {
            result.Add(AsDataRange(Evaluate(node)));
        }

        return result;
    }

    /// <summary>The named nodes of a list of operand elements.</summary>
    /// <param name="nodes">The operand elements.</param>
    /// <returns>The named nodes.</returns>
    private List<NamedNode> NamedList(List<OwlXmlNode> nodes)
    {
        List<NamedNode> result = [];
        foreach(OwlXmlNode node in nodes)
        {
            result.Add(NamedFrom(node));
        }

        return result;
    }

    /// <summary>The terms of a list of operand elements.</summary>
    /// <param name="nodes">The operand elements.</param>
    /// <returns>The terms.</returns>
    private List<RdfTerm> TermList(List<OwlXmlNode> nodes)
    {
        List<RdfTerm> result = [];
        foreach(OwlXmlNode node in nodes)
        {
            result.Add(AsTerm(Evaluate(node)));
        }

        return result;
    }

    /// <summary>The literals of a list of operand elements.</summary>
    /// <param name="nodes">The operand elements.</param>
    /// <returns>The literals.</returns>
    private static List<Literal> LiteralList(List<OwlXmlNode> nodes)
    {
        List<Literal> result = [];
        foreach(OwlXmlNode node in nodes)
        {
            result.Add(LiteralFrom(node));
        }

        return result;
    }

    /// <summary>Casts an evaluated value to a class expression, reporting and substituting a sentinel on a mismatch.</summary>
    /// <param name="value">The evaluated value.</param>
    /// <returns>The class expression.</returns>
    private OwlClassExpression AsClass(object? value)
    {
        if(value is OwlClassExpression expression)
        {
            return expression;
        }

        Report("Expected a class expression.");

        return new OwlClassReference(InvalidNamed);
    }

    /// <summary>Casts an evaluated value to a data range, reporting and substituting a sentinel on a mismatch.</summary>
    /// <param name="value">The evaluated value.</param>
    /// <returns>The data range.</returns>
    private OwlDataRange AsDataRange(object? value)
    {
        if(value is OwlDataRange range)
        {
            return range;
        }

        Report("Expected a data range.");

        return new OwlDatatypeReference(InvalidNamed);
    }

    /// <summary>Casts an evaluated value to an object property expression, reporting and substituting a sentinel on a mismatch.</summary>
    /// <param name="value">The evaluated value.</param>
    /// <returns>The object property expression.</returns>
    private OwlObjectPropertyExpression AsObjectProperty(object? value)
    {
        if(value is OwlObjectPropertyExpression expression)
        {
            return expression;
        }

        Report("Expected an object property expression.");

        return new OwlObjectPropertyReference(InvalidNamed);
    }

    /// <summary>Casts an evaluated value to a term, reporting and substituting a sentinel on a mismatch.</summary>
    /// <param name="value">The evaluated value.</param>
    /// <returns>The term.</returns>
    private RdfTerm AsTerm(object? value)
    {
        if(value is RdfTerm term)
        {
            return term;
        }

        Report("Expected an individual or value.");

        return InvalidNamed;
    }

    /// <summary>Casts an evaluated value to a named node, drawing the named property out of a property expression where needed.</summary>
    /// <param name="value">The evaluated value.</param>
    /// <returns>The named node.</returns>
    private NamedNode AsNamedNode(object? value)
    {
        if(value is NamedNode named)
        {
            return named;
        }

        if(value is OwlObjectPropertyExpression property)
        {
            return property.Property;
        }

        Report("Expected a named entity.");

        return InvalidNamed;
    }

    /// <summary>Maps a characteristic axiom element to the characteristic it asserts.</summary>
    /// <param name="element">The characteristic element.</param>
    /// <returns>The asserted characteristic.</returns>
    private static OwlPropertyCharacteristic Characteristic(OwlXmlElement element)
    {
        return element switch
        {
            OwlXmlElement.FunctionalObjectProperty => OwlPropertyCharacteristic.Functional,
            OwlXmlElement.InverseFunctionalObjectProperty => OwlPropertyCharacteristic.InverseFunctional,
            OwlXmlElement.TransitiveObjectProperty => OwlPropertyCharacteristic.Transitive,
            OwlXmlElement.SymmetricObjectProperty => OwlPropertyCharacteristic.Symmetric,
            OwlXmlElement.AsymmetricObjectProperty => OwlPropertyCharacteristic.Asymmetric,
            OwlXmlElement.ReflexiveObjectProperty => OwlPropertyCharacteristic.Reflexive,
            _ => OwlPropertyCharacteristic.Irreflexive,
        };
    }

    /// <summary>Maps a cardinality element to its cardinality flavour.</summary>
    /// <param name="element">The cardinality element.</param>
    /// <returns>The cardinality flavour.</returns>
    private static OwlCardinalityKind CardinalityKind(OwlXmlElement element)
    {
        return element switch
        {
            OwlXmlElement.ObjectMinCardinality or OwlXmlElement.DataMinCardinality => OwlCardinalityKind.Min,
            OwlXmlElement.ObjectMaxCardinality or OwlXmlElement.DataMaxCardinality => OwlCardinalityKind.Max,
            _ => OwlCardinalityKind.Exact,
        };
    }

    /// <summary>The consecutive index pairs of an operand list, the pairwise expansion of an n-ary equivalence or identity axiom.</summary>
    /// <param name="count">The operand count.</param>
    /// <returns>The consecutive pairs.</returns>
    private static IEnumerable<(int First, int Second)> Pairs(int count)
    {
        for(int i = 0; i + 1 < count; i++)
        {
            yield return (i, i + 1);
        }
    }

    /// <summary>Interns a term value, giving it an eager hash for use as a record field or census key while sharing the source bytes.</summary>
    /// <param name="value">The value to intern.</param>
    /// <returns>The eager-hash value.</returns>
    private static Utf8String Intern(Utf8String value)
    {
        return new Utf8String(value.Memory);
    }

    /// <summary>Concatenates a namespace IRI and a local name into one expanded IRI.</summary>
    /// <param name="namespaceIri">The namespace IRI.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The expanded IRI.</returns>
    private static Utf8String Concat(Utf8String namespaceIri, Utf8String local)
    {
        byte[] joined = new byte[namespaceIri.Length + local.Length];
        namespaceIri.Span.CopyTo(joined);
        local.Span.CopyTo(joined.AsSpan(namespaceIri.Length));

        return new Utf8String(joined);
    }

    /// <summary>Whether an IRI value is absolute (carries a scheme followed by <c>://</c> or names a URN-like scheme).</summary>
    /// <param name="iri">The IRI value.</param>
    /// <returns><see langword="true"/> when the value looks absolute rather than a prefixed name.</returns>
    private static bool LooksAbsolute(Utf8String iri)
    {
        return iri.Span.IndexOf("://"u8) >= 0;
    }

    /// <summary>Trims leading and trailing XML whitespace from a text value.</summary>
    /// <param name="text">The text value.</param>
    /// <returns>The trimmed value, sharing the source bytes.</returns>
    private static Utf8String Trim(Utf8String text)
    {
        ReadOnlySpan<byte> span = text.Span;
        int start = 0;
        int end = span.Length;
        while(start < end && IsWhitespace(span[start]))
        {
            start++;
        }

        while(end > start && IsWhitespace(span[end - 1]))
        {
            end--;
        }

        return new Utf8String(text.Memory.Slice(start, end - start));
    }

    /// <summary>Whether a byte is XML whitespace.</summary>
    /// <param name="b">The byte to test.</param>
    /// <returns><see langword="true"/> for XML whitespace.</returns>
    private static bool IsWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Decodes a value to a string for a diagnostic message.</summary>
    /// <param name="value">The value to decode.</param>
    /// <returns>The decoded string.</returns>
    private static string Decode(Utf8String value)
    {
        return System.Text.Encoding.UTF8.GetString(value.Span);
    }

    /// <summary>Records a conversion diagnostic at the converting element's span.</summary>
    /// <param name="message">The diagnostic message.</param>
    private void Report(string message)
    {
        Diagnostics.Add(new Diagnostic(
            WellKnownDiagnostics.Owl.MalformedAxiomStructure,
            DiagnosticSeverity.Error,
            CurrentSpan,
            Utf8Strings.From(message)));
    }
}
